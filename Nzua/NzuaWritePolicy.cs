namespace NzuaMcp.Nzua;

public static class NzuaWritePolicy
{
    public static bool WritesAllowed => string.Equals(
        Environment.GetEnvironmentVariable("NZUA_ALLOW_WRITES"),
        "true",
        StringComparison.OrdinalIgnoreCase);

    public static void EnsureAllowed(string path) => EnsureAllowed(path, WritesAllowed);

    public static void EnsureAllowed(string path, bool writesAllowed)
    {
        if (writesAllowed || !IsJournalMutation(path))
            return;

        throw new NzuaException(
            "Сервер працює в режимі лише читання. Для змін у журналі явно встановіть NZUA_ALLOW_WRITES=true і перезапустіть MCP-сервер.");
    }

    private static bool IsJournalMutation(string path) =>
        path.StartsWith("/journal/", StringComparison.OrdinalIgnoreCase);
}
