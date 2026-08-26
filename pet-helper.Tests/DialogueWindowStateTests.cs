using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class DialogueWindowStateTests
{
    [Fact]
    public void Load_returns_default_for_missing_or_malformed_json()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"dialogue-{Guid.NewGuid():N}.json");
        Assert.Equal(DialogueWindowState.Default, new DialogueWindowStateStore(missing).Load());

        var malformed = Path.Combine(Path.GetTempPath(), $"dialogue-{Guid.NewGuid():N}.json");
        File.WriteAllText(malformed, "not json");
        try
        {
            Assert.Equal(DialogueWindowState.Default, new DialogueWindowStateStore(malformed).Load());
        }
        finally
        {
            File.Delete(malformed);
        }
    }

    [Fact]
    public void Save_and_load_round_trip_valid_state()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dialogue-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "dialogue-window-state.json");
        try
        {
            var store = new DialogueWindowStateStore(path);
            var expected = new DialogueWindowState(320d, 180d, 280d, 360d);

            store.Save(expected);

            Assert.Equal(expected, store.Load());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Normalize_replaces_out_of_range_values_with_defaults()
    {
        Assert.Equal(DialogueWindowState.Default, DialogueWindowState.Normalize(50000d, 200d, 280d, 360d));
        Assert.Equal(DialogueWindowState.Default, DialogueWindowState.Normalize(100d, 200d, 10d, 360d));
        Assert.Equal(DialogueWindowState.Default, DialogueWindowState.Normalize(100d, 200d, 280d, 5000d));
    }
}
