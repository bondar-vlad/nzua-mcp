using System.Diagnostics;
using Microsoft.Playwright;

namespace NzuaMcp.Nzua;

public static class NzuaAuth
{
    private static readonly string ProfilesRootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "nzua-mcp",
        "playwright-profiles");

    // Окремий профіль на процес: кілька MCP-серверів (Claude Desktop + Code + Cowork)
    // не б'ються за Chromium SingletonLock одного профілю.
    private static readonly string BrowserProfileDir = Path.Combine(ProfilesRootDir, Environment.ProcessId.ToString());

    /// <summary>Прибирає профілі процесів, яких уже немає. Викликається один раз на старті.</summary>
    public static void CleanupStaleProfiles()
    {
        // Легасі-профіль версій до мульти-інстансу.
        TryDeleteDirectory(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "nzua-mcp",
            "playwright-profile"));

        if (!Directory.Exists(ProfilesRootDir))
            return;

        foreach (var dir in Directory.GetDirectories(ProfilesRootDir))
        {
            var name = Path.GetFileName(dir);
            if (name == Environment.ProcessId.ToString())
                continue;
            if (int.TryParse(name, out var pid) && IsProcessRunning(pid))
                continue;
            TryDeleteDirectory(dir);
        }
    }

    private static bool IsProcessRunning(int pid)
    {
        try { return !Process.GetProcessById(pid).HasExited; }
        catch { return false; }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* профіль може бути зайнятий живим Chromium — пропускаємо */ }
    }

    // ===== Автовстановлення Chromium =====
    private static bool _chromiumEnsured;

    /// <summary>
    /// Ставить Chromium при першому використанні, якщо його немає. Інсталяція йде в дочірньому
    /// процесі з перенаправленими потоками: stdout цього процесу зайнятий MCP-транспортом.
    /// </summary>
    private static async Task EnsureChromiumInstalled(IPlaywright playwright)
    {
        if (_chromiumEnsured)
            return;
        _chromiumEnsured = true;

        var executablePath = playwright.Chromium.ExecutablePath;
        if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
            return;

        if (Environment.GetEnvironmentVariable("NZUA_AUTO_INSTALL_BROWSER") == "false")
            throw new NzuaException(
                "Chromium для Playwright не знайдено, а автовстановлення вимкнено (NZUA_AUTO_INSTALL_BROWSER=false). " +
                "Встановіть вручну: pwsh playwright.ps1 install chromium");

        Console.Error.WriteLine("[nzua-auth] Chromium не знайдено — встановлюємо (одноразово, кілька хвилин)...");
        var selfPath = Environment.ProcessPath
            ?? throw new NzuaException("Не вдалося визначити шлях до процесу для встановлення Chromium.");

        var psi = new ProcessStartInfo(selfPath, "--install-chromium")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var installer = Process.Start(psi)
            ?? throw new NzuaException("Не вдалося запустити процес встановлення Chromium.");
        installer.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine($"[playwright] {e.Data}"); };
        installer.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine($"[playwright] {e.Data}"); };
        installer.BeginOutputReadLine();
        installer.BeginErrorReadLine();
        await installer.WaitForExitAsync();

        if (installer.ExitCode != 0)
            throw new NzuaException(
                $"Встановлення Chromium завершилося з кодом {installer.ExitCode}. " +
                "Встановіть вручну: pwsh playwright.ps1 install chromium");
        Console.Error.WriteLine("[nzua-auth] Chromium встановлено.");
    }

    // ===== Багаторазовий кеш браузерного контексту =====
    private static IPlaywright? _cachedPlaywright;
    private static IBrowserContext? _cachedContext;
    private static IPage? _cachedPage;
    private static bool _cachedHeadless;
    private static readonly SemaphoreSlim _browserLock = new(1, 1);

    private static void CleanProfileLock()
    {
        // Видаляємо lock-файли Chromium, щоб уникнути exitCode=21 при повторному запуску.
        foreach (var lockFile in new[] { "SingletonLock", "lockfile", "SingletonCookie", "SingletonSocket" })
        {
            var path = Path.Combine(BrowserProfileDir, lockFile);
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    /// <summary>Отримує кешований або створює новий браузер для POST/GET операцій.</summary>
    /// <remarks>
    /// Кешований браузер (зокрема вже пройдений вручну Cloudflare-виклик) завжди має пріоритет над
    /// параметром <paramref name="headless"/>: перестворення headless-контексту замість повторного
    /// використання вже верифікованого вікна саме й провокує повторні Cloudflare-виклики.
    /// </remarks>
    private static async Task<IPage> AcquireBrowser(NzuaSession session, bool headless)
    {
        await _browserLock.WaitAsync();

        if (_cachedPage != null)
        {
            try
            {
                await _cachedPage.EvaluateAsync<int>("1");
                await InjectSessionCookies(_cachedContext!, session);
                Console.Error.WriteLine("[nzua-auth] Використовуємо кешований браузер.");
                return _cachedPage;
            }
            catch
            {
                Console.Error.WriteLine("[nzua-auth] Кешований браузер недоступний, створюємо новий.");
                await DisposeCachedBrowserUnsafe();
            }
        }

        _cachedPlaywright = await Playwright.CreateAsync();
        await EnsureChromiumInstalled(_cachedPlaywright);
        Directory.CreateDirectory(BrowserProfileDir);
        CleanProfileLock();
        _cachedContext = await _cachedPlaywright.Chromium.LaunchPersistentContextAsync(BrowserProfileDir, new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = headless,
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            Args = ["--disable-blink-features=AutomationControlled"],
        });
        _cachedPage = _cachedContext.Pages.FirstOrDefault() ?? await _cachedContext.NewPageAsync();
        _cachedHeadless = headless;

        await InjectSessionCookies(_cachedContext, session);
        await _cachedPage.GotoAsync("https://nz.ua/journal/list", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000,
        });
        await HandleCloudflareChallenge(_cachedPage, headless);
        Console.Error.WriteLine("[nzua-auth] Новий браузерний контекст створено.");
        return _cachedPage;
    }

    private static void ReleaseBrowser() => _browserLock.Release();

    private static async Task DisposeCachedBrowserUnsafe()
    {
        if (_cachedContext != null) try { await _cachedContext.CloseAsync(); } catch { }
        if (_cachedPlaywright != null) try { _cachedPlaywright.Dispose(); } catch { }
        _cachedContext = null;
        _cachedPage = null;
        _cachedPlaywright = null;
    }

    /// <summary>Закриває кешований браузер. Безпечно навіть якщо браузер не відкритий.</summary>
    public static async Task CloseBrowser()
    {
        await _browserLock.WaitAsync();
        try { await DisposeCachedBrowserUnsafe(); }
        finally { _browserLock.Release(); }
    }

    private static async Task InjectSessionCookies(IBrowserContext context, NzuaSession session)
    {
        var cookies = new List<Cookie>
        {
            new() { Name = "PHPSESSID", Value = session.Phpsessid, Domain = "nz.ua", Path = "/" }
        };
        if (!string.IsNullOrWhiteSpace(session.Identity))
            cookies.Add(new Cookie
            {
                Name = string.IsNullOrWhiteSpace(session.IdentityCookieName) ? "_identity" : session.IdentityCookieName,
                Value = session.Identity,
                Domain = "nz.ua",
                Path = "/"
            });
        if (!string.IsNullOrWhiteSpace(session.CsrfCookie))
            cookies.Add(new Cookie { Name = "_csrf", Value = session.CsrfCookie, Domain = "nz.ua", Path = "/" });
        if (!string.IsNullOrWhiteSpace(session.CfClearance))
            cookies.Add(new Cookie { Name = "cf_clearance", Value = session.CfClearance, Domain = "nz.ua", Path = "/" });
        await context.AddCookiesAsync(cookies);
    }

    private static async Task<NzuaSession> ExtractSessionUpdate(NzuaSession current)
    {
        if (_cachedContext == null || _cachedPage == null) return current;

        var cookies = await _cachedContext.CookiesAsync();
        var phpsessid = cookies.FirstOrDefault(c => c.Name == "PHPSESSID")?.Value ?? current.Phpsessid;
        var identityCookie = cookies.FirstOrDefault(c =>
            c.Name.Equals("_identity", StringComparison.OrdinalIgnoreCase) ||
            c.Name.StartsWith("_identity", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("identity", StringComparison.OrdinalIgnoreCase));
        var identity = identityCookie?.Value ?? current.Identity;
        var cfClearance = cookies.FirstOrDefault(c => c.Name == "cf_clearance")?.Value ?? current.CfClearance;
        var csrfCookie = cookies.FirstOrDefault(c => c.Name == "_csrf")?.Value ?? current.CsrfCookie;
        var csrfToken = await _cachedPage.EvaluateAsync<string>("document.querySelector('meta[name=csrf-token]')?.content ?? ''");
        if (string.IsNullOrEmpty(csrfToken)) csrfToken = csrfCookie ?? current.CsrfToken;

        return current with
        {
            Phpsessid = phpsessid,
            Identity = identity,
            IdentityCookieName = identityCookie?.Name ?? current.IdentityCookieName,
            CsrfToken = csrfToken,
            CsrfCookie = csrfCookie,
            CfClearance = cfClearance,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeMilliseconds(),
        };
    }

    public static async Task<NzuaSession> ManualLogin()
    {
        await CloseBrowser();

        // Завжди починаємо ручний вхід із чистого профілю: старий cf_clearance/localStorage не
        // прискорює повторний вхід, а лише підвищує шанс, що Cloudflare розцінить його як
        // недійсний/підозрілий "replay" і покаже перевірку знову — тож немає сенсу його берегти.
        if (Directory.Exists(BrowserProfileDir))
        {
            try { Directory.Delete(BrowserProfileDir, recursive: true); }
            catch (Exception ex) { Console.Error.WriteLine($"[nzua-auth] Не вдалося видалити старий профіль: {ex.Message}"); }
        }

        var playwright = await Playwright.CreateAsync();
        await EnsureChromiumInstalled(playwright);
        var ownsPlaywright = true;
        IBrowserContext? context = null;
        try
        {
            Directory.CreateDirectory(BrowserProfileDir);
            CleanProfileLock();
            context = await playwright.Chromium.LaunchPersistentContextAsync(BrowserProfileDir, new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = false,
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                Args =
                [
                    "--disable-blink-features=AutomationControlled",
                ],
            });

            var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            await page.GotoAsync("https://nz.ua/", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000,
            });

            Console.Error.WriteLine("[nzua-auth] Ручний вхід: пройдіть Cloudflare, увійдіть у кабінет вчителя та оберіть школу (до 300с)...");
            await WaitForAuthorizedUi(page, timeoutMs: 300_000, keepReloading: false);

            if (!await IsAuthorizedUi(page))
                throw new AuthException("Ручний вхід не завершено: журнали недоступні.");

            var html = await page.ContentAsync();
            if (html.Contains("Trying to get property 'school' of non-object", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("Trying to get property \"school\" of non-object", StringComparison.OrdinalIgnoreCase))
                throw new AuthException("У кабінеті не обрано школу. Оберіть школу і повторіть ручний вхід.");

            var session = await ExtractSessionFromContext(context, page);

            // Залишаємо це вже верифіковане (пройдений Cloudflare) вікно відкритим і кешуємо його для
            // подальших GET/POST — перестворення headless-контексту після цього знову провокує Cloudflare.
            await _browserLock.WaitAsync();
            try
            {
                await DisposeCachedBrowserUnsafe();
                _cachedPlaywright = playwright;
                _cachedContext = context;
                _cachedPage = page;
                _cachedHeadless = false;
                ownsPlaywright = false;
            }
            finally
            {
                _browserLock.Release();
            }

            return session;
        }
        finally
        {
            if (ownsPlaywright)
            {
                if (context != null) try { await context.CloseAsync(); } catch { }
                playwright.Dispose();
            }
        }
    }


    public static async Task<(string Html, NzuaSession Session)> FetchPageWithBrowser(
        NzuaSession currentSession,
        string url,
        bool headless = true)
    {
        var page = await AcquireBrowser(currentSession, headless);
        try
        {
            var targetUrl = url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? url
                : $"https://nz.ua{url}";

            await page.GotoAsync(targetUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000,
            });
            await HandleCloudflareChallenge(page, headless);

            var html = await page.ContentAsync();
            var session = await ExtractSessionUpdate(currentSession);
            return (html, session);
        }
        catch
        {
            await DisposeCachedBrowserUnsafe();
            throw;
        }
        finally
        {
            ReleaseBrowser();
        }
    }

    public static async Task<(string ResponseBody, int Status, NzuaSession Session)> PostWithBrowser(
        NzuaSession currentSession,
        string path,
        Dictionary<string, string> formBody,
        bool headless = true)
    {
        var page = await AcquireBrowser(currentSession, headless);
        try
        {
            if (!formBody.ContainsKey("_csrf"))
                formBody = new Dictionary<string, string>(formBody) { ["_csrf"] = currentSession.CsrfToken };

            var targetUrl = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? path
                : $"https://nz.ua{path}";

            var bodyJson = System.Text.Json.JsonSerializer.Serialize(formBody);
            var responseJson = await page.EvaluateAsync<string>($@"
                async () => {{
                    const fields = {bodyJson};
                    const body = new URLSearchParams(fields).toString();
                    const resp = await fetch({System.Text.Json.JsonSerializer.Serialize(targetUrl)}, {{
                        method: 'POST',
                        headers: {{
                            'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                            'X-Requested-With': 'XMLHttpRequest',
                            'X-Csrf-Token': fields['_csrf'] || '',
                        }},
                        credentials: 'include',
                        body: body,
                    }});
                    const text = await resp.text();
                    return JSON.stringify({{ status: resp.status, body: text }});
                }}
            ");

            var parsed = System.Text.Json.JsonDocument.Parse(responseJson ?? "{}");
            var status = parsed.RootElement.TryGetProperty("status", out var s) ? s.GetInt32() : 0;
            var respBody = parsed.RootElement.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

            var session = await ExtractSessionUpdate(currentSession);
            return (respBody, status, session);
        }
        catch
        {
            await DisposeCachedBrowserUnsafe();
            throw;
        }
        finally
        {
            ReleaseBrowser();
        }
    }

    /// <summary>
    /// Виконує кілька POST-запитів через кешований браузерний контекст.
    /// </summary>
    public static async Task<(List<(string Body, int Status)> Responses, NzuaSession Session)> BatchPostWithBrowser(
        NzuaSession currentSession,
        List<(string Path, Dictionary<string, string> FormBody)> requests,
        bool headless = true,
        Action<int, int>? onProgress = null)
    {
        var page = await AcquireBrowser(currentSession, headless);
        try
        {
            var responses = new List<(string Body, int Status)>();
            for (int i = 0; i < requests.Count; i++)
            {
                var (path, formBody) = requests[i];
                var body = new Dictionary<string, string>(formBody);
                if (!body.ContainsKey("_csrf"))
                    body["_csrf"] = currentSession.CsrfToken;

                var targetUrl = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? path
                    : $"https://nz.ua{path}";

                var bodyJson = System.Text.Json.JsonSerializer.Serialize(body);
                Console.Error.WriteLine($"[nzua-batch] [{i + 1}/{requests.Count}] POST {path}");
                var responseJson = await page.EvaluateAsync<string>($@"
                    async () => {{
                        const fields = {bodyJson};
                        const body = new URLSearchParams(fields).toString();
                        const resp = await fetch({System.Text.Json.JsonSerializer.Serialize(targetUrl)}, {{
                            method: 'POST',
                            headers: {{
                                'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                                'X-Requested-With': 'XMLHttpRequest',
                                'X-Csrf-Token': fields['_csrf'] || '',
                            }},
                            credentials: 'include',
                            body: body,
                        }});
                        const text = await resp.text();
                        return JSON.stringify({{ status: resp.status, body: text }});
                    }}
                ");
                var parsed = System.Text.Json.JsonDocument.Parse(responseJson ?? "{}");
                var status = parsed.RootElement.TryGetProperty("status", out var s) ? s.GetInt32() : 0;
                var respBody = parsed.RootElement.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                Console.Error.WriteLine($"[nzua-batch] [{i + 1}/{requests.Count}] HTTP {status}");
                responses.Add((respBody, status));
                onProgress?.Invoke(i + 1, requests.Count);
            }

            var session = await ExtractSessionUpdate(currentSession);
            return (responses, session);
        }
        catch
        {
            await DisposeCachedBrowserUnsafe();
            throw;
        }
        finally
        {
            ReleaseBrowser();
        }
    }

    /// <summary>
    /// Завантажує кілька GET-сторінок через кешований браузерний контекст.
    /// </summary>
    public static async Task<(List<string> HtmlPages, NzuaSession Session)> BatchFetchWithBrowser(
        NzuaSession currentSession,
        List<string> paths,
        bool headless = true,
        Action<int, int>? onProgress = null)
    {
        var page = await AcquireBrowser(currentSession, headless);
        try
        {
            var htmlPages = new List<string>();
            for (int i = 0; i < paths.Count; i++)
            {
                var targetUrl = paths[i].StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? paths[i]
                    : $"https://nz.ua{paths[i]}";

                Console.Error.WriteLine($"[nzua-batch-get] [{i + 1}/{paths.Count}] GET {paths[i]}");
                await page.GotoAsync(targetUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60_000,
                });
                await HandleCloudflareChallenge(page, headless);
                htmlPages.Add(await page.ContentAsync());
                onProgress?.Invoke(i + 1, paths.Count);
            }

            var session = await ExtractSessionUpdate(currentSession);
            return (htmlPages, session);
        }
        catch
        {
            await DisposeCachedBrowserUnsafe();
            throw;
        }
        finally
        {
            ReleaseBrowser();
        }
    }

    private static async Task HandleCloudflareChallenge(IPage page, bool headless = true)
    {
        // Якщо CF challenge — чекаємо до 30с
        var cfCheck = await page.QuerySelectorAsync("#challenge-running, #cf-challenge-running");
        if (cfCheck is not null)
        {
            Console.Error.WriteLine("[nzua-auth] Cloudflare challenge виявлено, чекаємо...");
            if (!headless)
            {
                await page.WaitForSelectorAsync("#challenge-running, #cf-challenge-running",
                    new PageWaitForSelectorOptions { State = WaitForSelectorState.Hidden, Timeout = 180_000 });
                return;
            }

            var started = DateTime.UtcNow;
            while ((DateTime.UtcNow - started).TotalSeconds < 45)
            {
                var stillRunning = await page.QuerySelectorAsync("#challenge-running, #cf-challenge-running");
                if (stillRunning is null)
                    break;

                await Task.Delay(700);
            }

            await page.WaitForSelectorAsync("#challenge-running, #cf-challenge-running",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Hidden, Timeout = 45_000 });
        }
    }

    private static async Task<bool> IsAuthorizedUi(IPage page)
    {
        var hasLoginForm = await page.Locator("#login-form, #loginform-login").CountAsync() > 0;
        var hasJournalsUi = await page.Locator("table.journal-choose, #personalselectform-semester_id, a[href*='journal/index']").CountAsync() > 0;
        return !hasLoginForm && hasJournalsUi;
    }

    private static async Task WaitForAuthorizedUi(IPage page, int timeoutMs, bool keepReloading)
    {
        var started = DateTime.UtcNow;
        if (keepReloading)
        {
            await page.GotoAsync("https://nz.ua/journal/list", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30_000,
            });
        }

        while ((DateTime.UtcNow - started).TotalMilliseconds < timeoutMs)
        {
            try
            {
                if (keepReloading)
                {
                    await page.GotoAsync("https://nz.ua/journal/list", new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 30_000,
                    });
                }

                await HandleCloudflareChallenge(page, headless: false);

                if (await IsAuthorizedUi(page))
                    return;
            }
            catch (Exception ex) when (
                ex.Message.Contains("Execution context was destroyed", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("context or browser has been closed", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("navigation", StringComparison.OrdinalIgnoreCase))
            {
                // Сторінка навігується — просто чекаємо і пробуємо знову
            }

            await Task.Delay(2000);
        }
    }

    private static async Task<NzuaSession> ExtractSessionFromContext(IBrowserContext context, IPage page)
    {
        var cookies = await context.CookiesAsync();
        var phpsessid = cookies.FirstOrDefault(c => c.Name == "PHPSESSID")?.Value
            ?? throw new AuthException("Не знайдено PHPSESSID cookie.");
        var identityCookie = cookies.FirstOrDefault(c =>
            c.Name.Equals("_identity", StringComparison.OrdinalIgnoreCase) ||
            c.Name.StartsWith("_identity", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("identity", StringComparison.OrdinalIgnoreCase));
        var identity = identityCookie?.Value ?? string.Empty;
        var cfClearance = cookies.FirstOrDefault(c => c.Name == "cf_clearance")?.Value;
        var csrfCookie = cookies.FirstOrDefault(c => c.Name == "_csrf")?.Value;

        var csrfToken = await page.EvaluateAsync<string>(
            "document.querySelector('meta[name=csrf-token]')?.content ?? ''"
        );
        if (string.IsNullOrEmpty(csrfToken))
            csrfToken = csrfCookie ?? "";

        return new NzuaSession(
            Phpsessid: phpsessid,
            Identity: identity,
            CsrfToken: csrfToken,
            CsrfCookie: csrfCookie,
            CfClearance: cfClearance,
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeMilliseconds(),
            IdentityCookieName: identityCookie?.Name ?? "_identity"
        );
    }

}
