using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using NzuaMcp.Nzua;
using NzuaMcp.Nzua.Api;

namespace NzuaMcp;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        var sessionStore = new NzuaSessionStore();

        // При втраті сесії одразу відкриваємо ручний вхід і повторюємо запит.
        var client = new NzuaClient(
            sessionStore.Load(),
            DoManualAuthenticate,
            sessionStore.Save);

        // Реєструємо сервіси
        builder.Services.AddSingleton(sessionStore);
        builder.Services.AddSingleton(client);
        builder.Services.AddSingleton<JournalApi>();
        builder.Services.AddSingleton<MarksApi>();
        builder.Services.AddSingleton<LessonsApi>();
        builder.Services.AddSingleton<HomeTasksApi>();

        // MCP сервер
        builder.Services
            .AddMcpServer(options =>
            {
                var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
                options.ServerInfo = new() { Name = "nzua-mcp", Version = version };
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        Console.Error.WriteLine("[nzua-mcp] Запуск MCP сервера...");
        try
        {
            await builder.Build().RunAsync();
        }
        finally
        {
            await NzuaAuth.CloseBrowser();
        }
    }

    private static async Task<NzuaSession> DoManualAuthenticate()
    {
        Console.Error.WriteLine("[nzua-mcp] Сесія недійсна. Відкриваємо браузер для ручного входу...");
        var session = await NzuaAuth.ManualLogin();
        Console.Error.WriteLine("[nzua-mcp] Ручний вхід завершено. Сесію збережено.");
        return session;
    }
}
