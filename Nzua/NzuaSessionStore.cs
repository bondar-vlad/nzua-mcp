using System.Text.Json;

namespace NzuaMcp.Nzua;

public sealed class NzuaSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public NzuaSessionStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".nzua-session.json");
    }

    public string FilePath { get; }

    public NzuaSession? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            var session = JsonSerializer.Deserialize<NzuaSession>(File.ReadAllText(FilePath));
            if (session?.ExpiresAt is not null &&
                session.ExpiresAt < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            {
                Clear();
                return null;
            }

            return session;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[nzua-mcp] Не вдалося прочитати сесію: {ex.Message}");
            return null;
        }
    }

    public void Save(NzuaSession session)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temporaryFile = FilePath + ".tmp";
            File.WriteAllText(temporaryFile, JsonSerializer.Serialize(session, JsonOptions));
            File.Move(temporaryFile, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[nzua-mcp] Не вдалося зберегти сесію: {ex.Message}");
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[nzua-mcp] Не вдалося видалити сесію: {ex.Message}");
        }
    }
}
