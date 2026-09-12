using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class BoundedLruCacheTests
{
    [Fact]
    public void Enforces_the_byte_budget_on_replacement_and_releases_everything_on_clear()
    {
        var cache = new BoundedLruCache<string, int>(10, 8, value => value);
        cache.AddOrUpdate("first", 3);
        cache.AddOrUpdate("second", 4);
        cache.AddOrUpdate("second", 6);
        Assert.False(cache.TryGetValue("first", out _));
        Assert.True(cache.TryGetValue("second", out var value));
        Assert.Equal(6, value);
        cache.AddOrUpdate("too-large", 9);
        Assert.Equal(0, cache.Count);
        cache.AddOrUpdate("small", 1);
        cache.Clear();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Evicts_the_least_recently_used_value_at_capacity()
    {
        var cache = new BoundedLruCache<string, int>(2);
        cache.AddOrUpdate("first", 1);
        cache.AddOrUpdate("second", 2);

        Assert.True(cache.TryGetValue("first", out var first));
        Assert.Equal(1, first);

        cache.AddOrUpdate("third", 3);

        Assert.False(cache.TryGetValue("second", out _));
        Assert.True(cache.TryGetValue("first", out _));
        Assert.True(cache.TryGetValue("third", out _));
        Assert.Equal(2, cache.Count);
    }
}
