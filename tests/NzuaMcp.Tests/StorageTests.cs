using NzuaMcp.Nzua;

namespace NzuaMcp.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"nzua-tests-{Guid.NewGuid():N}");

    [Fact]
    public void SessionStore_RoundTripsAndRemovesExpiredSession()
    {
        var path = Path.Combine(_directory, "session.json");
        var store = new NzuaSessionStore(path);
        var active = new NzuaSession(
            "session",
            "identity",
            "csrf",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds());

        store.Save(active);
        Assert.Equal(active, store.Load());

        store.Save(active with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds() });
        Assert.Null(store.Load());
        Assert.False(File.Exists(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
