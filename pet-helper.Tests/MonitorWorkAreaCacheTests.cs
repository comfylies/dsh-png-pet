using System.Windows;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class MonitorWorkAreaCacheTests
{
    private static readonly Rect Primary = new(0, 0, 1920, 1040);
    private static readonly Rect Secondary = new(-1920, 0, 1920, 1040);

    [Fact]
    public void TryGet_returns_false_before_the_monitor_was_added()
    {
        var cache = new MonitorWorkAreaCache();

        Assert.False(cache.TryGet(new IntPtr(1), out _));
    }

    [Fact]
    public void Add_then_TryGet_returns_the_same_monitor_work_area()
    {
        var cache = new MonitorWorkAreaCache();
        cache.Add(new IntPtr(7), Primary);

        Assert.True(cache.TryGet(new IntPtr(7), out var workArea));
        Assert.Equal(Primary, workArea);
    }

    [Fact]
    public void Add_replaces_an_existing_entry_for_the_same_monitor()
    {
        var cache = new MonitorWorkAreaCache();
        cache.Add(new IntPtr(7), Primary);

        cache.Add(new IntPtr(7), Secondary);

        Assert.True(cache.TryGet(new IntPtr(7), out var workArea));
        Assert.Equal(Secondary, workArea);
    }

    [Fact]
    public void Add_distinguishes_different_monitor_handles()
    {
        var cache = new MonitorWorkAreaCache();
        cache.Add(new IntPtr(3), Primary);

        Assert.False(cache.TryGet(new IntPtr(4), out _));
    }

    [Fact]
    public void Add_beyond_capacity_drops_stale_entries_but_keeps_the_newest()
    {
        var cache = new MonitorWorkAreaCache(capacity: 2);
        cache.Add(new IntPtr(1), Primary);
        cache.Add(new IntPtr(2), Secondary);

        cache.Add(new IntPtr(3), Primary);

        // The oldest entry was evicted with the rest once the cache filled up.
        Assert.False(cache.TryGet(new IntPtr(1), out _));
        Assert.True(cache.TryGet(new IntPtr(3), out var newest));
        Assert.Equal(Primary, newest);
    }
}
