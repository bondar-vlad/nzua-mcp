using NzuaMcp.Nzua;

namespace NzuaMcp.Tests;

public class RetryTests
{
    private static readonly NzuaSession Session = new(
        "session",
        "identity",
        "csrf",
        ExpiresAt: DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds());

    [Fact]
    public async Task RequestWithRetry_UsesManualRenewalForExpiredSession()
    {
        var attempts = 0;
        var renewals = 0;
        var client = new NzuaClient(null, () =>
        {
            renewals++;
            return Task.FromResult(Session);
        });

        var result = await client.RequestWithRetry(() =>
        {
            attempts++;
            return attempts == 1
                ? Task.FromException<string>(new AuthException("expired"))
                : Task.FromResult("ok");
        });

        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
        Assert.Equal(1, renewals);
        Assert.Equal(Session, client.Session);
    }

    [Fact]
    public async Task RequestWithRetry_DoesNotRenewOrdinaryValidationFailure()
    {
        var renewals = 0;
        var client = new NzuaClient(Session, () =>
        {
            renewals++;
            return Task.FromResult(Session);
        });

        await Assert.ThrowsAsync<NzuaException>(() =>
            client.RequestWithRetry(() => Task.FromException<string>(new NzuaException("HTTP 400"))));

        Assert.Equal(0, renewals);
    }

    [Fact]
    public async Task RequestWithRetry_AllowsGetToUseBrowserFallbackForCloudflare()
    {
        var renewals = 0;
        var client = new NzuaClient(Session, () =>
        {
            renewals++;
            return Task.FromResult(Session);
        });

        await Assert.ThrowsAsync<CloudflareException>(() =>
            client.RequestWithRetry(
                () => Task.FromException<string>(new CloudflareException()),
                renewOnCloudflare: false));

        Assert.Equal(0, renewals);
    }
}
