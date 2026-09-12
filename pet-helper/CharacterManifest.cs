using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PetHelper;

internal sealed record CharacterAction(string Type, string[] Files, int FrameDurationMs);

internal sealed record CharacterManifest(string Name, PetStatusAnchor StatusAnchor, double Baseline,
    Dictionary<string, CharacterAction> Actions)
{
    internal static readonly Dictionary<string, PetAnimationKey> Keys = new(StringComparer.Ordinal)
    {
        ["idle"] = PetAnimationKey.Idle, ["thinking"] = PetAnimationKey.Thinking,
        ["working"] = PetAnimationKey.Working, ["thinking-working"] = PetAnimationKey.ThinkingWorking,
        ["responding"] = PetAnimationKey.Responding, ["waiting"] = PetAnimationKey.Waiting,
        ["question"] = PetAnimationKey.Question, ["success"] = PetAnimationKey.Success,
        ["error"] = PetAnimationKey.Error, ["disconnected"] = PetAnimationKey.Disconnected,
    };

    internal static FormatException Invalid() => new("人物素材格式不正确或超过限制。");

    internal static JsonDocument ReadJson(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 65536) throw Invalid();
        try
        {
            var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            try { RejectDuplicates(document.RootElement); return document; }
            catch { document.Dispose(); throw; }
        }
        catch (JsonException) { throw Invalid(); }
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in element.EnumerateObject())
            {
                if (!seen.Add(field.Name)) throw Invalid();
                RejectDuplicates(field.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }

    internal static void Fields(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var actual = element.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(names)) throw Invalid();
    }

    internal static int Integer(JsonElement element, int min, int max)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value) || value < min || value > max)
            throw Invalid();
        return value;
    }

    internal static double Unit(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value) ||
            !double.IsFinite(value) || value is < 0 or > 1) throw Invalid();
        return value;
    }

    internal static string Text(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String) throw Invalid();
        return element.GetString()!;
    }

    internal static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 40 || name.Any(char.IsControl)) throw Invalid();
        return name;
    }

    internal static PetStatusAnchor Anchor(JsonElement element)
    {
        Fields(element, "x", "y");
        return new(Unit(element.GetProperty("x")), Unit(element.GetProperty("y")));
    }

    internal static string AssetReference(string value, string extension)
    {
        if (value.Length is < 1 or > 180 || !value.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) throw Invalid();
        var parts = value.Split('/');
        if (parts.Length > 4) throw Invalid();
        foreach (var part in parts)
        {
            if (!Regex.IsMatch(part, @"\A[a-zA-Z0-9_-][a-zA-Z0-9_.-]{0,63}\z") ||
                part.Contains("..", StringComparison.Ordinal) || part.EndsWith('.') ||
                Regex.IsMatch(part.Split('.')[0], @"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\z", RegexOptions.IgnoreCase))
                throw Invalid();
        }
        return value;
    }

    internal static CharacterManifest Parse(string json)
    {
        using var document = ReadJson(json);
        var root = document.RootElement;
        Fields(root, "characterFormatVersion", "name", "statusAnchor", "baseline", "actions");
        Integer(root.GetProperty("characterFormatVersion"), 1, 1);
        var actions = new Dictionary<string, CharacterAction>(StringComparer.Ordinal);
        var value = root.GetProperty("actions");
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        foreach (var entry in value.EnumerateObject())
        {
            if (!Keys.ContainsKey(entry.Name) || entry.Value.ValueKind != JsonValueKind.Object ||
                !entry.Value.TryGetProperty("type", out var typeElement)) throw Invalid();
            var type = Text(typeElement);
            string[] files;
            var interval = 100;
            if (type is "gif" or "png")
            {
                Fields(entry.Value, "type", "file");
                files = [AssetReference(Text(entry.Value.GetProperty("file")), "." + type)];
            }
            else if (type == "png-sequence")
            {
                Fields(entry.Value, "type", "frames", "frameDurationMs");
                var frames = entry.Value.GetProperty("frames");
                if (frames.ValueKind != JsonValueKind.Array || frames.GetArrayLength() is < 1 or > 240) throw Invalid();
                files = frames.EnumerateArray().Select(f => AssetReference(Text(f), ".png")).ToArray();
                interval = Integer(entry.Value.GetProperty("frameDurationMs"), 16, 1000);
            }
            else throw Invalid();
            actions.Add(entry.Name, new(type, files, interval));
        }
        if (!actions.ContainsKey("idle")) throw Invalid();
        return new(ValidateName(Text(root.GetProperty("name"))), Anchor(root.GetProperty("statusAnchor")),
            Unit(root.GetProperty("baseline")), actions);
    }
}
