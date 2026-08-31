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

    [Fact]
    public async Task SessionStore_SurvivesConcurrentSavesFromTwoStores()
    {
        // Симуляція двох MCP-процесів, що пишуть той самий файл сесії.
        var path = Path.Combine(_directory, "session.json");
        var storeA = new NzuaSessionStore(path);
        var storeB = new NzuaSessionStore(path);

        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            var store = i % 2 == 0 ? storeA : storeB;
            store.Save(new NzuaSession(
                $"session-{i}",
                "identity",
                "csrf",
                ExpiresAt: DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()));
            return store.Load();
        }));

        var results = await Task.WhenAll(tasks);

        Assert.All(results, session => Assert.NotNull(session));
        var final = storeA.Load();
        Assert.NotNull(final);
        Assert.StartsWith("session-", final.Phpsessid);
    }

    [Fact]
    public void SessionStore_PicksUpSessionRenewedByAnotherProcess()
    {
        // Сценарій single-flight логіну: процес A має протухлу сесію в пам'яті,
        // процес B уже перелогінився і зберіг нову на диск — A мусить її побачити.
        var path = Path.Combine(_directory, "session.json");
        var processA = new NzuaSessionStore(path);
        var processB = new NzuaSessionStore(path);

        var stale = new NzuaSession("stale", "identity", "csrf",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds());
        var renewed = stale with { Phpsessid = "renewed-by-b" };
        processB.Save(renewed);

        var fromDisk = processA.Load();
        Assert.NotNull(fromDisk);
        Assert.NotEqual(stale, fromDisk);
        Assert.Equal(renewed, fromDisk);
    }

    [Fact]
    public void CrossProcessLock_IsExclusiveAndReleasedOnDispose()
    {
        var lockPath = Path.Combine(_directory, "test.lock");

        using (var first = CrossProcessLock.TryAcquire(lockPath, TimeSpan.Zero))
        {
            Assert.NotNull(first);
            Assert.Null(CrossProcessLock.TryAcquire(lockPath, TimeSpan.FromMilliseconds(50)));
        }

        using var second = CrossProcessLock.TryAcquire(lockPath, TimeSpan.Zero);
        Assert.NotNull(second);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
