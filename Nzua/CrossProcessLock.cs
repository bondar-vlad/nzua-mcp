namespace NzuaMcp.Nzua;

/// <summary>
/// Міжпроцесний лок на основі ексклюзивного відкриття lock-файлу (FileShare.None).
/// Працює і на Windows, і на Unix (.NET емулює FileShare через flock).
/// DeleteOnClose гарантує прибирання файлу навіть при аварійному завершенні процесу.
/// </summary>
public sealed class CrossProcessLock : IDisposable
{
    private readonly FileStream _stream;

    private CrossProcessLock(FileStream stream) => _stream = stream;

    public static CrossProcessLock? TryAcquire(string lockFilePath, TimeSpan timeout, TimeSpan? pollInterval = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        var poll = pollInterval ?? TimeSpan.FromMilliseconds(200);
        while (true)
        {
            if (TryOpen(lockFilePath) is { } stream)
                return new CrossProcessLock(stream);
            if (DateTime.UtcNow >= deadline)
                return null;
            Thread.Sleep(poll);
        }
    }

    public static async Task<CrossProcessLock?> TryAcquireAsync(string lockFilePath, TimeSpan timeout, TimeSpan? pollInterval = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        var poll = pollInterval ?? TimeSpan.FromMilliseconds(200);
        while (true)
        {
            if (TryOpen(lockFilePath) is { } stream)
                return new CrossProcessLock(stream);
            if (DateTime.UtcNow >= deadline)
                return null;
            await Task.Delay(poll);
        }
    }

    private static FileStream? TryOpen(string lockFilePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(lockFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            return new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
