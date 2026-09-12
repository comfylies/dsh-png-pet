using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PetHelper;

internal sealed record CharacterAction(string Type, string[] Files, int FrameDurationMs, bool DurationDeclared);
internal sealed record CharacterExtra(string Name, CharacterAction Action);
internal sealed record CharacterStateActions(CharacterAction Primary, ImmutableArray<CharacterExtra> Extras);

internal sealed record CharacterManifest(string Name, PetStatusAnchor StatusAnchor, double Baseline,
    int ExtrasCooldownMs, Dictionary<string, CharacterStateActions> Actions)
{
    internal const int DefaultExtrasCooldownMs = 30000;
    internal const int MaximumExtrasPerState = 4;
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
        if (root.ValueKind != JsonValueKind.Object) throw Invalid();
        if (!root.TryGetProperty("characterFormatVersion", out var versionElement)) throw Invalid();
        var version = Integer(versionElement, 1, 2);
        var required = new[] { "characterFormatVersion", "name", "statusAnchor", "baseline", "actions" };
        var allowed = version == 2
            ? required.Append("extrasCooldownMs").ToHashSet(StringComparer.Ordinal)
            : required.ToHashSet(StringComparer.Ordinal);
        var seen = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        if (!seen.IsSubsetOf(allowed) || required.Any(name => !seen.Contains(name))) throw Invalid();
        var cooldownMs = version == 2 && root.TryGetProperty("extrasCooldownMs", out var cooldownElement)
            ? Integer(cooldownElement, 5000, 600000)
            : DefaultExtrasCooldownMs;

        var actions = new Dictionary<string, CharacterStateActions>(StringComparer.Ordinal);
        var value = root.GetProperty("actions");
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        foreach (var entry in value.EnumerateObject())
        {
            if (!Keys.ContainsKey(entry.Name) || entry.Value.ValueKind != JsonValueKind.Object) throw Invalid();
            var hasType = entry.Value.TryGetProperty("type", out _);
            var hasPrimary = entry.Value.TryGetProperty("primary", out _);
            if (hasType == hasPrimary) throw Invalid();
            if (!hasPrimary)
            {
                actions.Add(entry.Name, new(ParseAction(entry.Value, isExtra: false), []));
                continue;
            }

            var fields = entry.Value.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
            if (!fields.SetEquals(new[] { "primary", "extras" })) throw Invalid();
            var primary = ParseAction(entry.Value.GetProperty("primary"), isExtra: false);
            var extrasElement = entry.Value.GetProperty("extras");
            if (extrasElement.ValueKind != JsonValueKind.Array ||
                extrasElement.GetArrayLength() is < 1 or > MaximumExtrasPerState) throw Invalid();
            var extras = ImmutableArray.CreateBuilder<CharacterExtra>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var extraElement in extrasElement.EnumerateArray())
            {
                if (extraElement.ValueKind != JsonValueKind.Object) throw Invalid();
                var extraFields = extraElement.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
                if (!extraFields.SetEquals(new[] { "name", "type", "file", "frameDurationMs" }) &&
                    !extraFields.SetEquals(new[] { "name", "type", "file" }) &&
                    !extraFields.SetEquals(new[] { "name", "type", "frames", "frameDurationMs" })) throw Invalid();
                var name = ValidateName(Text(extraElement.GetProperty("name")));
                if (!names.Add(name)) throw Invalid();
                extras.Add(new(name, ParseAction(extraElement, isExtra: true)));
            }
            actions.Add(entry.Name, new(primary, extras.ToImmutable()));
        }
        if (!actions.ContainsKey("idle")) throw Invalid();
        return new(ValidateName(Text(root.GetProperty("name"))), Anchor(root.GetProperty("statusAnchor")),
            Unit(root.GetProperty("baseline")), cooldownMs, actions);
    }

    private static CharacterAction ParseAction(JsonElement element, bool isExtra)
    {
        var type = Text(element.GetProperty("type"));
        if (type is "gif" or "png")
        {
            // An extra is declared inline with its own display name, so its action object carries
            // exactly one more member than the same action declared as a primary.
            string[] expected = isExtra
                ? element.TryGetProperty("frameDurationMs", out _)
                    ? ["name", "type", "file", "frameDurationMs"]
                    : ["name", "type", "file"]
                : ["type", "file"];
            Fields(element, expected);
            var files = new[] { AssetReference(Text(element.GetProperty("file")), "." + type) };
            if (!isExtra || !element.TryGetProperty("frameDurationMs", out var durationElement))
                return new(type, files, 100, DurationDeclared: false);
            var declared = Integer(durationElement, 16, 10000);
            return new(type, files, declared, DurationDeclared: true);
        }
        if (type == "png-sequence")
        {
            string[] expected = isExtra ? ["name", "type", "frames", "frameDurationMs"] : ["type", "frames", "frameDurationMs"];
            Fields(element, expected);
            var frames = element.GetProperty("frames");
            if (frames.ValueKind != JsonValueKind.Array || frames.GetArrayLength() is < 1 or > 240) throw Invalid();
            var files = frames.EnumerateArray().Select(frame => AssetReference(Text(frame), ".png")).ToArray();
            return new(type, files, Integer(element.GetProperty("frameDurationMs"), 16, 1000), DurationDeclared: true);
        }
        throw Invalid();
    }
}
