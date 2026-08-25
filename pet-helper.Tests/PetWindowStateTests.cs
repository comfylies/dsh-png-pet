using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetWindowStateTests
{
    [Fact]
    public void Normalize_keeps_supported_scale_and_position()
    {
        var state = PetWindowState.Normalize(100d, 200d, 1.25d);

        Assert.Equal(100d, state.Left);
        Assert.Equal(200d, state.Top);
        Assert.Equal(1.25d, state.Scale);
        Assert.Equal(275d, state.Width);
        Assert.Equal(275d, state.Height);
    }

    [Theory]
    [InlineData(0.5d)]
    [InlineData(1.1d)]
    [InlineData(2d)]
    public void Normalize_replaces_unsupported_scale_with_default(double scale)
    {
        Assert.Equal(1d, PetWindowState.Normalize(100d, 200d, scale).Scale);
    }

    [Fact]
    public void Reset_discards_position_and_restores_100_percent_scale()
    {
        var state = PetWindowState.Normalize(100d, 200d, 1.5d).Reset();

        Assert.Equal(PetWindowState.Default, state);
    }

    [Fact]
    public void Load_returns_default_for_malformed_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pet-state-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "not json");
        try
        {
            Assert.Equal(PetWindowState.Default, new PetWindowStateStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_and_load_round_trip_valid_state()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pet-state-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "window-state.json");
        try
        {
            var store = new PetWindowStateStore(path);
            var expected = PetWindowState.Normalize(320d, 180d, 1.5d);

            store.Save(expected);

            Assert.Equal(expected, store.Load());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Tray_icon_can_be_disposed_before_being_shown()
    {
        using var trayIcon = new PetTrayIcon(() => { }, () => { });
    }
}
