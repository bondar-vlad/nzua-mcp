using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using NzuaMcp.Nzua;
using NzuaMcp.Nzua.Api;

namespace NzuaMcp;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Ізольований режим встановлення Chromium: запускається як дочірній процес із
        // перенаправленими потоками, щоб вивід Playwright не потрапив у MCP stdout.
        if (args is ["--install-chromium"])
        {
            Console.SetOut(Console.Error);
            return Microsoft.Playwright.Program.Main(["install", "chromium"]);
        }

        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        NzuaAuth.CleanupStaleProfiles();

        var sessionStore = new NzuaSessionStore();

        // При втраті сесії одразу відкриваємо ручний вхід і повторюємо запит.
        // client захоплюється лямбдою після ініціалізації — на момент виклику вже присвоєний.
        NzuaClient client = null!;
        client = new NzuaClient(
            sessionStore.Load(),
            () => DoManualAuthenticate(sessionStore, () => client.Session),
            sessionStore.Save);

        var journalApi = new JournalApi(client);

        // Реєструємо сервіси
        builder.Services.AddSingleton(sessionStore);
        builder.Services.AddSingleton(client);
        builder.Services.AddSingleton(journalApi);
        builder.Services.AddSingleton<MarksApi>();
        builder.Services.AddSingleton<LessonsApi>();
        builder.Services.AddSingleton<HomeTasksApi>();
        builder.Services.AddSingleton<Mcp.Tools.JournalTools>();

        // MCP сервер
        builder.Services
            .AddMcpServer(options =>
            {
                var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
                options.ServerInfo = new() { Name = "nzua-mcp", Version = version };
                options.ServerInstructions =
                    "Неофіційний MCP-сервер для журналів NZ.UA (кабінет вчителя). Правила роботи: " +
                    "(1) Починайте з nzua_list_journals, щоб отримати journal_id. " +
                    "(2) Перед будь-яким записом читайте актуальний стан через nzua_get_journal, після запису — перевіряйте ним же. " +
                    "(3) Масові зміни робіть ОДНИМ викликом із entriesJson, а не серією одиночних викликів. " +
                    "(4) ID типів уроків/часу/кабінетів беріть лише з nzua_get_form — не вгадуйте. " +
                    "(5) Семестрові й річні оцінки не виставляйте автоматично: підготуйте дані, рішення ухвалює вчитель. " +
                    "(6) ПІБ учнів за замовчуванням замінені стабільними псевдонімами — це навмисне налаштування приватності. " +
                    "Запис у журнал вимкнено, доки не задано NZUA_ALLOW_WRITES=true.";
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly()
            .WithPromptsFromAssembly()
            .WithResourcesFromAssembly()
            // Автодоповнення аргументів промптів і шаблону ресурсу: journalId береться лише
            // з кешу останнього nzua_list_journals — без мережі й вікон логіну.
            .WithCompleteHandler((ctx, _) =>
                ValueTask.FromResult(Mcp.NzuaCompletions.Resolve(ctx.Params, journalApi.CachedJournals)));

        Console.Error.WriteLine("[nzua-mcp] Запуск MCP сервера...");
        try
        {
            await builder.Build().RunAsync();
        }
        finally
        {
            await NzuaAuth.CloseBrowser();
        }

        return 0;
    }

    private static async Task<NzuaSession> DoManualAuthenticate(NzuaSessionStore store, Func<NzuaSession?> currentSession)
    {
        // Single-flight між процесами: якщо інший інстанс уже відкрив вікно входу — чекаємо на нього,
        // а після звільнення лока спершу пробуємо його свіжу сесію з диска.
        using var loginLock = await CrossProcessLock.TryAcquireAsync(
            store.LoginLockFilePath, timeout: TimeSpan.FromMinutes(6), pollInterval: TimeSpan.FromMilliseconds(500));
        if (loginLock is null)
            Console.Error.WriteLine("[nzua-mcp] Не дочекалися завершення входу в іншому процесі — відкриваємо власне вікно.");

        var fromDisk = store.Load();
        if (fromDisk is not null && !Equals(fromDisk, currentSession()))
        {
            Console.Error.WriteLine("[nzua-mcp] Інший процес уже оновив сесію — використовуємо її без нового входу.");
            return fromDisk;
        }

        Console.Error.WriteLine("[nzua-mcp] Сесія недійсна. Відкриваємо браузер для ручного входу...");
        var session = await NzuaAuth.ManualLogin();
        store.Save(session);
        Console.Error.WriteLine("[nzua-mcp] Ручний вхід завершено. Сесію збережено.");
        return session;
    }
}
