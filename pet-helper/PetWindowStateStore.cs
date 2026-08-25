using System.IO;
using System.Text.Json;

namespace PetHelper;

public sealed class PetWindowStateStore
{
    private readonly string statePath;

    public PetWindowStateStore(string? statePath = null)
    {
        this.statePath = statePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DshPngPet",
            "window-state.json");
    }

    public PetWindowState Load()
    {
        try
        {
            var saved = JsonSerializer.Deserialize<StoredWindowState>(File.ReadAllText(statePath));
            return saved is null
                ? PetWindowState.Default
                : PetWindowState.Normalize(saved.Left, saved.Top, saved.Scale);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return PetWindowState.Default;
        }
    }

    public void Save(PetWindowState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var saved = new StoredWindowState(state.Left, state.Top, state.Scale);
            File.WriteAllText(statePath, JsonSerializer.Serialize(saved));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record StoredWindowState(double? Left, double? Top, double? Scale);
}
