using System.Text;
using OmnisRouter.CacheHygiene;

namespace OmnisRouter.CacheHygiene.Tests;

public class LineageCacheTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Stores_and_returns_the_previous_prefix()
    {
        var cache = new LineageCache(8, TimeSpan.FromHours(1));
        cache.Store("s1", B("prefix"));

        Assert.True(cache.TryGetPrevious("s1", out var got));
        Assert.Equal("prefix", Encoding.UTF8.GetString(got));
        Assert.False(cache.TryGetPrevious("other", out _));
    }

    [Fact]
    public void Evicts_least_recently_used_over_the_bound()
    {
        var cache = new LineageCache(maxEntries: 2, TimeSpan.FromHours(1));
        cache.Store("a", B("1"));
        cache.Store("b", B("2"));
        cache.Store("c", B("3"));   // over the bound of 2 -> "a" (LRU) evicted

        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGetPrevious("a", out _));
        Assert.True(cache.TryGetPrevious("b", out _));
        Assert.True(cache.TryGetPrevious("c", out _));
    }

    [Fact]
    public void A_get_refreshes_recency()
    {
        var cache = new LineageCache(maxEntries: 2, TimeSpan.FromHours(1));
        cache.Store("a", B("1"));
        cache.Store("b", B("2"));
        cache.TryGetPrevious("a", out _);   // "a" now most-recently used
        cache.Store("c", B("3"));           // "b" is LRU -> evicted

        Assert.True(cache.TryGetPrevious("a", out _));
        Assert.False(cache.TryGetPrevious("b", out _));
    }

    [Fact]
    public void Evicts_aged_out_entries()
    {
        var clock = new ManualClock();
        var cache = new LineageCache(8, TimeSpan.FromMinutes(1), clock);
        cache.Store("a", B("1"));

        clock.Now = clock.Now.AddMinutes(2);
        Assert.False(cache.TryGetPrevious("a", out _));   // older than maxAge
    }
}
