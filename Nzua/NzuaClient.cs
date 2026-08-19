using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NzuaMcp.Nzua;

public class NzuaClient
{
    private const string BaseUrl = "https://nz.ua";
    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    private NzuaSession? _session;
    private readonly Func<Task<NzuaSession>>? _onSessionExpired;
    private readonly Action<NzuaSession>? _onSessionUpdated;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _sessionRenewalLock = new(1, 1);

    /// <summary>Зростає після кожного успішного запису. Використовується для інвалідації кешу прочитаних журналів.</summary>
    public int WriteGeneration { get; private set; }

    public NzuaClient(
        NzuaSession? session = null,
        Func<Task<NzuaSession>>? onSessionExpired = null,
        Action<NzuaSession>? onSessionUpdated = null)
    {
        _session = session;
        _onSessionExpired = onSessionExpired;
        _onSessionUpdated = onSessionUpdated;

        var handler = new HttpClientHandler
        {
            UseCookies = false, // Ми керуємо cookies вручну
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl),
        };
    }

    public void SetSession(NzuaSession? session)
    {
        _session = session;
        if (session is not null)
            _onSessionUpdated?.Invoke(session);
    }
    public NzuaSession? Session => _session;

    private async Task RenewSession(Exception cause)
    {
        var failedSession = _session;
        await _sessionRenewalLock.WaitAsync();
        try
        {
            if (!Equals(_session, failedSession))
                return;

            if (_onSessionExpired is null)
                throw cause;

            Console.Error.WriteLine($"[nzua] Сесія недійсна ({cause.GetType().Name}), потрібен ручний вхід...");
            SetSession(await _onSessionExpired());
        }
        finally
        {
            _sessionRenewalLock.Release();
        }
    }

    private async Task EnsureSession()
    {
        if (_session is null)
            await RenewSession(new AuthException("Немає активної сесії."));
    }

    private NzuaSession RequireSession() =>
        _session ?? throw new AuthException("Ручний вхід не створив активну сесію.");

    private string BuildCookieHeader()
    {
        if (_session is null) throw new AuthException("Не автентифіковано. Потрібен ручний вхід.");

        var cookies = new StringBuilder();
        var identityCookieName = string.IsNullOrWhiteSpace(_session.IdentityCookieName)
            ? "_identity"
            : _session.IdentityCookieName;

        cookies.Append($"PHPSESSID={_session.Phpsessid}");
        if (!string.IsNullOrWhiteSpace(_session.Identity))
            cookies.Append($"; {identityCookieName}={_session.Identity}");
        if (!string.IsNullOrWhiteSpace(_session.CsrfCookie))
            cookies.Append($"; _csrf={_session.CsrfCookie}");
        if (!string.IsNullOrEmpty(_session.CfClearance))
            cookies.Append($"; cf_clearance={_session.CfClearance}");
        return cookies.ToString();
    }

    private void ApplyHeaders(HttpRequestMessage request, bool isAjax = false, bool includeCsrfHeader = true)
    {
        if (_session is null) throw new AuthException("Не автентифіковано. Потрібен ручний вхід.");

        request.Headers.Add("Cookie", BuildCookieHeader());
        request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
        request.Headers.TryAddWithoutValidation("Accept-Language", "uk-UA,uk;q=0.9,en-US;q=0.8,en;q=0.7");
        request.Headers.Referrer = new Uri("https://nz.ua/");
        if (includeCsrfHeader)
            request.Headers.Add("X-Csrf-Token", _session.CsrfToken);
        if (isAjax)
        {
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            request.Headers.TryAddWithoutValidation("Origin", "https://nz.ua");
        }
    }

    private async Task HandleResponse(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new CloudflareException();
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new CsrfException();
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new AuthException("Сесія протухла. Потрібна повторна автентифікація.");
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new NzuaException($"HTTP {(int)response.StatusCode}: {body}");
        }
    }

    /// <summary>
    /// Розпізнає помилки Playwright, спричинені закриттям вікна браузера користувачем вручну
    /// (сервер навмисно тримає його відкритим — див. AcquireBrowser), щоб дати зрозумілу пораду
    /// замість сирого повідомлення Playwright.
    /// </summary>
    private static bool IsBrowserUnavailable(Exception ex) =>
        ex.Message.Contains("has been closed", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Target closed", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Execution context was destroyed", StringComparison.OrdinalIgnoreCase);

    private static void HandleBodyLevelErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return;

        if (body.Contains("Trying to get property 'school' of non-object", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("Trying to get property \"school\" of non-object", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthException("Сесія не прив'язана до школи/кабінету. Потрібна повторна авторизація в кабінеті вчителя.");
        }

        if (body.Contains("cf-challenge-running", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("challenge-running", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("Just a moment", StringComparison.OrdinalIgnoreCase))
        {
            throw new CloudflareException();
        }

        var hasLoginForm = Regex.IsMatch(body, "id=[\"']login-form[\"']", RegexOptions.IgnoreCase);
        if (hasLoginForm || body.Contains("LoginForm[login]", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthException("Сесія протухла або неавторизована. Потрібен повторний логін.");
        }
    }

    public async Task<string> RequestWithRetry(Func<Task<string>> action, int retries = 1, bool renewOnCloudflare = true)
    {
        for (int attempt = 0; attempt <= retries; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < retries && (ex is AuthException || ex is CsrfException || ex is CloudflareException))
            {
                if (ex is CloudflareException && !renewOnCloudflare)
                    throw;

                await RenewSession(ex);
            }
        }
        throw new NzuaException("Не вдалося виконати запит після повторних спроб.");
    }

    public async Task<string> Get(string path, Dictionary<string, string>? queryParams = null)
    {
        try
        {
            return await RequestWithRetry(async () =>
            {
                var url = path;
                if (queryParams is { Count: > 0 })
                {
                    var query = string.Join("&", queryParams
                        .Where(kv => !string.IsNullOrEmpty(kv.Value))
                        .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
                    url = path.Contains('?') ? $"{path}&{query}" : $"{path}?{query}";
                }

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                ApplyHeaders(request, isAjax: false, includeCsrfHeader: false);

                var response = await _httpClient.SendAsync(request);
                await HandleResponse(response);
                var responseBody = await response.Content.ReadAsStringAsync();
                HandleBodyLevelErrors(responseBody);
                return responseBody;
            }, retries: 1, renewOnCloudflare: false);
        }
        catch (CloudflareException) when (_session is not null)
        {
            Console.Error.WriteLine("[nzua] GET fallback через браузерний контекст (Playwright)...");
            var url = path;
            if (queryParams is { Count: > 0 })
            {
                var query = string.Join("&", queryParams
                    .Where(kv => !string.IsNullOrEmpty(kv.Value))
                    .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
                url = path.Contains('?') ? $"{path}&{query}" : $"{path}?{query}";
            }

            var headless = Environment.GetEnvironmentVariable("NZUA_HEADLESS") != "false";
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var result = await NzuaAuth.FetchPageWithBrowser(_session, url, headless);
                    SetSession(result.Session);
                    HandleBodyLevelErrors(result.Html);
                    return result.Html;
                }
                catch (Exception ex) when (attempt == 0 && (ex is AuthException or CsrfException or CloudflareException || IsBrowserUnavailable(ex)))
                {
                    await RenewSession(ex is AuthException or CsrfException or CloudflareException ? ex : new AuthException("Вікно браузера було закрито. Потрібен новий ручний вхід."));
                }
            }

            throw new NzuaException("Не вдалося завантажити сторінку після ручного входу.");
        }
    }

    public async Task<string> Post(string path, Dictionary<string, string> body)
    {
        NzuaWritePolicy.EnsureAllowed(path);

        return await RequestWithRetry(async () =>
        {
            if (_session is null)
                throw new AuthException("Немає активної сесії.");

            Console.Error.WriteLine($"[nzua] POST через браузер: {path}");
            var headless = Environment.GetEnvironmentVariable("NZUA_HEADLESS") != "false";
            (string ResponseBody, int Status, NzuaSession Session) result;
            try
            {
                result = await NzuaAuth.PostWithBrowser(_session, path, body, headless);
            }
            catch (Exception ex) when (IsBrowserUnavailable(ex))
            {
                // Не повторюємо автоматично: невідомо, чи запис уже застосувався на сервері до закриття вікна.
                throw new NzuaException(
                    "Вікно браузера було закрито під час запису. Стан цього запису невідомий — " +
                    "перевірте результат вручну через nzua_get_journal, " +
                    "потім за потреби повторіть. Викличте nzua_session(action:\"login\") для нового входу.");
            }
            SetSession(result.Session);

            if (RequiresManualLogin(result.ResponseBody, result.Status))
                throw result.Status == 401
                    ? new AuthException("Сесія протухла. Потрібен ручний вхід.")
                    : new CloudflareException();

            if (result.Status >= 400)
            {
                var snippet = result.ResponseBody.Length > 500 ? result.ResponseBody[..500] : result.ResponseBody;
                throw new NzuaException($"HTTP {result.Status}: {snippet}");
            }

            HandleBodyLevelErrors(result.ResponseBody);
            WriteGeneration++;
            return result.ResponseBody;
        });
    }

    /// <summary>
    /// Виконує кілька POST-запитів в одному браузерному вікні. Повертає тіло + HTTP-статус кожної відповіді.
    /// При виявленні неавторизованої відповіді запускає ручний вхід і повторює лише невдалі запити,
    /// зберігаючи вже успішні відповіді.
    /// </summary>
    public async Task<List<(string Body, int Status)>> BatchPost(List<(string Path, Dictionary<string, string> Body)> requests)
    {
        foreach (var request in requests)
            NzuaWritePolicy.EnsureAllowed(request.Path);

        await EnsureSession();

        Console.Error.WriteLine($"[nzua] Batch POST через браузер: {requests.Count} запитів");
        var headless = Environment.GetEnvironmentVariable("NZUA_HEADLESS") != "false";
        (List<(string Body, int Status)> Responses, NzuaSession Session) result;
        try
        {
            result = await NzuaAuth.BatchPostWithBrowser(RequireSession(), requests, headless);
        }
        catch (Exception ex) when (IsBrowserUnavailable(ex))
        {
            // Не повторюємо автоматично: частина запитів у пакеті могла вже застосуватися на сервері.
            throw new NzuaException(
                "Вікно браузера було закрито під час пакетного запису. Стан запитів у цьому пакеті невідомий — " +
                "перевірте результат вручну, потім за потреби повторіть лише невдалі записи. " +
                "Викличте nzua_session(action:\"login\") для нового входу.");
        }
        SetSession(result.Session);

        var failedIndices = result.Responses
            .Select((r, i) => (r, i))
            .Where(x => RequiresManualLogin(x.r.Body, x.r.Status))
            .Select(x => x.i)
            .ToList();

        if (failedIndices.Count > 0)
        {
            Console.Error.WriteLine($"[nzua] Batch POST: {failedIndices.Count}/{requests.Count} запитів заблоковано, потрібен ручний вхід...");
            await RenewSession(new CloudflareException());

            var retryRequests = failedIndices.Select(i => requests[i]).ToList();
            var retryResult = await NzuaAuth.BatchPostWithBrowser(RequireSession(), retryRequests, headless);
            SetSession(retryResult.Session);

            var merged = result.Responses.ToList();
            for (int j = 0; j < failedIndices.Count; j++)
                merged[failedIndices[j]] = retryResult.Responses[j];
            WriteGeneration++;
            return merged;
        }

        WriteGeneration++;
        return result.Responses;
    }

    /// <summary>
    /// Повертає true лише для явної втрати авторизації або Cloudflare challenge.
    /// HTTP 400 не є достатньою ознакою: nz.ua так само повертає його для помилок форми.
    /// </summary>
    private static bool RequiresManualLogin(string body, int status)
    {
        if (status is 401 or 403)
            return true;

        if (body.Contains("cf-challenge-running", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("challenge-running", StringComparison.OrdinalIgnoreCase))
            return true;

        return Regex.IsMatch(body, "id=[\"']login-form[\"']", RegexOptions.IgnoreCase) ||
               body.Contains("LoginForm[login]", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("Trying to get property 'school' of non-object", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("Trying to get property \"school\" of non-object", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Отримує кілька GET-сторінок. Спочатку через HttpClient; ті що заблоковані CF — через один браузерний сеанс.
    /// </summary>
    public async Task<List<string>> BatchGet(List<string> paths)
    {
        await EnsureSession();

        var results = new string?[paths.Count];
        var needBrowser = new List<int>();

        for (int i = 0; i < paths.Count; i++)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, paths[i]);
                request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                ApplyHeaders(request, isAjax: false, includeCsrfHeader: false);
                var response = await _httpClient.SendAsync(request);
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    needBrowser.Add(i);
                    continue;
                }
                await HandleResponse(response);
                var body = await response.Content.ReadAsStringAsync();
                if (body.Contains("cf-challenge-running", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("Just a moment", StringComparison.OrdinalIgnoreCase))
                {
                    needBrowser.Add(i);
                    continue;
                }
                results[i] = body;
            }
            catch (CloudflareException)
            {
                needBrowser.Add(i);
            }
        }

        if (needBrowser.Count > 0)
        {
            Console.Error.WriteLine($"[nzua] BatchGet: {needBrowser.Count}/{paths.Count} сторінок через браузер");
            var headless = Environment.GetEnvironmentVariable("NZUA_HEADLESS") != "false";
            var browserPaths = needBrowser.Select(idx => paths[idx]).ToList();
            (List<string> HtmlPages, NzuaSession Session) browserResult;
            try
            {
                browserResult = await NzuaAuth.BatchFetchWithBrowser(RequireSession(), browserPaths, headless);
            }
            catch (Exception ex) when (IsBrowserUnavailable(ex))
            {
                // Читання без побічних ефектів — безпечно відновити сесію й повторити один раз.
                await RenewSession(new AuthException("Вікно браузера було закрито. Потрібен новий ручний вхід."));
                browserResult = await NzuaAuth.BatchFetchWithBrowser(RequireSession(), browserPaths, headless);
            }
            SetSession(browserResult.Session);
            for (int j = 0; j < needBrowser.Count; j++)
                results[needBrowser[j]] = browserResult.HtmlPages[j];
        }

        return results.Select(r => r ?? "").ToList();
    }
}
