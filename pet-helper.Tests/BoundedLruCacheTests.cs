using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class BoundedLruCacheTests
{
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
