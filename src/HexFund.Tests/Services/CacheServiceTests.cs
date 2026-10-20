using HexFund.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace HexFund.Tests.Services;

public class CacheServiceTests
{
    private static CacheService Create() => new(new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }));


    [Fact]
    public async Task SecondCall_IsServedFromCache()
    {
        var cache = Create();
        var calls = 0;

        await cache.GetOrCreateAsync("k", () => { calls++; return Task.FromResult<string?>("v"); }, TimeSpan.FromMinutes(1));
        var value = await cache.GetOrCreateAsync("k", () => { calls++; return Task.FromResult<string?>("other"); }, TimeSpan.FromMinutes(1));

        Assert.Equal("v", value);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task NullResults_AreNotCached()
    {
        var cache = Create();
        var calls = 0;

        await cache.GetOrCreateAsync<string>("k", () => { calls++; return Task.FromResult<string?>(null); }, TimeSpan.FromMinutes(1));
        await cache.GetOrCreateAsync<string>("k", () => { calls++; return Task.FromResult<string?>(null); }, TimeSpan.FromMinutes(1));

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task InvalidateAccount_EvictsOnlyThatAccountsEntries()
    {
        var cache = Create();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var calls = 0;

        await cache.GetOrCreateAsync("a", () => { calls++; return Task.FromResult<string?>("1"); }, TimeSpan.FromMinutes(1), a);
        await cache.GetOrCreateAsync("b", () => { calls++; return Task.FromResult<string?>("1"); }, TimeSpan.FromMinutes(1), b);

        cache.InvalidateAccount(a);

        await cache.GetOrCreateAsync("a", () => { calls++; return Task.FromResult<string?>("2"); }, TimeSpan.FromMinutes(1), a);
        await cache.GetOrCreateAsync("b", () => { calls++; return Task.FromResult<string?>("2"); }, TimeSpan.FromMinutes(1), b);

        Assert.Equal(3, calls); // "a" rebuilt, "b" still cached
    }

    [Fact]
    public async Task InvalidationDuringFactory_DoesNotLeaveAStaleEntry()
    {
        var cache = Create();
        var account = Guid.NewGuid();
        var calls = 0;

        await cache.GetOrCreateAsync("k", async () =>
        {
            calls++;
            cache.InvalidateAccount(account); // a write lands while the value is being built
            await Task.Yield();
            return "stale";
        }, TimeSpan.FromMinutes(1), account);

        await cache.GetOrCreateAsync("k", () => { calls++; return Task.FromResult<string?>("fresh"); }, TimeSpan.FromMinutes(1), account);

        Assert.Equal(2, calls);
    }
}
