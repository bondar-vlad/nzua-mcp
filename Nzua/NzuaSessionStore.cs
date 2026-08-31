using System.Text.Json;

namespace NzuaMcp.Nzua;

public sealed class NzuaSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly TimeSpan StoreLockTimeout = TimeSpan.FromSeconds(10);

    public NzuaSessionStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".nzua-session.json");
    }

    public string FilePath { get; }

    /// <summary>Lock-файл single-flight ручного входу: лише один процес відкриває вікно логіну.</summary>
    public string LoginLockFilePath => FilePath + ".login.lock";

    private CrossProcessLock? AcquireStoreLock()
    {
        var storeLock = CrossProcessLock.TryAcquire(FilePath + ".lock", StoreLockTimeout);
        if (storeLock is null)
            Console.Error.WriteLine("[nzua-mcp] Не вдалося отримати лок файлу сесії за 10с — продовжуємо без нього.");
        return storeLock;
    }

    public NzuaSession? Load()
    {
        using var storeLock = AcquireStoreLock();
        return LoadUnsafe();
    }

    private NzuaSession? LoadUnsafe()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            var session = JsonSerializer.Deserialize<NzuaSession>(File.ReadAllText(FilePath));
            if (session?.ExpiresAt is not null &&
                session.ExpiresAt < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            {
                ClearUnsafe();
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
        using var storeLock = AcquireStoreLock();
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // Унікальний tmp на процес: два процеси не перетирають один одному проміжний файл.
            var temporaryFile = $"{FilePath}.{Environment.ProcessId}.tmp";
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
        using var storeLock = AcquireStoreLock();
        ClearUnsafe();
    }

    private void ClearUnsafe()
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
