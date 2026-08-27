using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetAnimationManifestTests
{
    [Fact]
    public void Resolves_a_configured_multiframe_action_in_order()
    {
        var manifest = PetAnimationManifest.Parse("""
            {
              "idle": { "frames": ["placeholder-a.png"] },
              "thinking": {
                "frames": ["Animations/thinking/001.png", "Animations/thinking/002.png"],
                "fallback": "idle"
              }
            }
            """);

        var resolved = manifest.Resolve(PetAnimationKey.Thinking, _ => true);

        Assert.Equal(PetAnimationKey.Thinking, resolved.Key);
        Assert.Equal(new[] { "Animations/thinking/001.png", "Animations/thinking/002.png" }, resolved.Frames);
        Assert.Equal(500, resolved.IntervalMs);
    }

    [Fact]
    public void Resolves_missing_composite_action_through_working_before_idle()
    {
        var manifest = PetAnimationManifest.Parse("""
            {
              "idle": { "frames": ["placeholder-a.png"] },
              "working": { "frames": ["Animations/working/001.png"], "fallback": "idle" },
              "thinking-working": { "frames": [], "fallback": "working" }
            }
            """);

        Assert.Equal(
            PetAnimationKey.Working,
            manifest.Resolve(PetAnimationKey.ThinkingWorking, frame => frame == "Animations/working/001.png").Key);
        Assert.Equal(
            PetAnimationKey.Idle,
            manifest.Resolve(PetAnimationKey.ThinkingWorking, frame => frame == "placeholder-a.png").Key);
    }

    [Theory]
    [InlineData(PetAnimationKey.Thinking)]
    [InlineData(PetAnimationKey.Working)]
    [InlineData(PetAnimationKey.ThinkingWorking)]
    [InlineData(PetAnimationKey.Waiting)]
    [InlineData(PetAnimationKey.Success)]
    [InlineData(PetAnimationKey.Error)]
    [InlineData(PetAnimationKey.Disconnected)]
    public void Resolves_each_missing_non_idle_action_to_the_available_idle_action(PetAnimationKey requested)
    {
        var manifest = PetAnimationManifest.Parse("""
            { "idle": { "frames": ["placeholder-a.png"] } }
            """);

        var resolved = manifest.Resolve(requested, frame => frame == "placeholder-a.png");

        Assert.Equal(PetAnimationKey.Idle, resolved.Key);
        Assert.Equal(new[] { "placeholder-a.png" }, resolved.Frames);
    }

    [Theory]
    [InlineData("../secret.png")]
    [InlineData("Animations\\working\\001.png")]
    [InlineData("C:/secret.png")]
    [InlineData("/secret.png")]
    [InlineData("Animations//working.png")]
    [InlineData("Animations/ /working.png")]
    [InlineData("Animations/working/001.jpg")]
    [InlineData("Animations/%2e%2e/secret.png")]
    [InlineData("Animations/%2E%2E/secret.png")]
    [InlineData("Animations%2f%2e%2e%2fsecret.png")]
    public void Rejects_unsafe_frame_identifiers(string frame)
    {
        Assert.Throws<FormatException>(() => PetAnimationManifest.Parse($$"""
            { "idle": { "frames": ["{{frame}}"] } }
            """));
    }

    [Theory]
    [InlineData("{ \"working\": { \"frames\": [\"placeholder-a.png\"] } }")]
    [InlineData("{ \"idle\": { \"frames\": [] } }")]
    [InlineData("{ \"idle\": { \"frames\": [\"placeholder-a.png\"] }, \"dance\": { \"frames\": [] } }")]
    [InlineData("{ \"idle\": { \"frames\": [\"placeholder-a.png\"], \"unexpected\": true } }")]
    [InlineData("{ \"idle\": { \"frames\": [\"placeholder-a.png\"], \"intervalMs\": 1000 } }")]
    [InlineData("{ \"idle\": { \"frames\": [\"placeholder-a.png\"] }, \"thinking\": { \"frames\": [], \"fallback\": \"dance\" } }")]
    [InlineData("{ \"idle\": { \"frames\": [\"placeholder-a.png\"] }, \"thinking\": { \"frames\": [], \"fallback\": \"working\" }, \"working\": { \"frames\": [], \"fallback\": \"thinking\" } }")]
    [InlineData("{ \"idle\": { \"frames\": [\"placeholder-a.png\"] }, \"thinking\": { \"frames\": [], \"fallback\": \"thinking\" } }")]
    [InlineData("{ \"idle\": { \"frames\": [\"placeholder-a.png\", \"placeholder-a.png\"] } }")]
    public void Rejects_invalid_manifest_configuration(string json)
    {
        Assert.Throws<FormatException>(() => PetAnimationManifest.Parse(json));
    }

    [Fact]
    public void Embeds_and_resolves_the_default_idle_manifest()
    {
        var assembly = typeof(PetAnimationManifest).Assembly;
        using var stream = assembly.GetManifestResourceStream("PetHelper.Assets.pet-animations.json");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);

        var manifest = PetAnimationManifest.Parse(reader.ReadToEnd());
        var idleFrames = Enumerable.Range(1, 4)
            .Select(i => $"Animations/idle/{i:D3}.png")
            .ToArray();
        var resolved = manifest.Resolve(PetAnimationKey.Idle, idleFrames.Contains);

        Assert.Equal(PetAnimationKey.Idle, resolved.Key);
        Assert.Equal(idleFrames, resolved.Frames);
        Assert.Equal(250, resolved.IntervalMs);
    }

    [Fact]
    public void Calculates_each_actions_interval_from_its_own_frame_count()
    {
        var manifest = PetAnimationManifest.Parse("""
            {
              "idle": { "frames": ["placeholder-a.png"] },
              "thinking": {
                "frames": ["Animations/thinking/001.png", "Animations/thinking/002.png", "Animations/thinking/003.png"],
                "fallback": "idle"
              },
              "working": {
                "frames": ["Animations/working/001.png", "Animations/working/002.png", "Animations/working/003.png", "Animations/working/004.png", "Animations/working/005.png", "Animations/working/006.png"],
                "fallback": "idle"
              }
            }
            """);

        Assert.Equal(1000, manifest.Resolve(PetAnimationKey.Idle, _ => true).IntervalMs);
        Assert.Equal(333, manifest.Resolve(PetAnimationKey.Thinking, _ => true).IntervalMs);
        Assert.Equal(167, manifest.Resolve(PetAnimationKey.Working, _ => true).IntervalMs);
    }

    [Fact]
    public void Uses_the_resolved_fallback_actions_own_frame_count()
    {
        var manifest = PetAnimationManifest.Parse("""
            {
              "idle": { "frames": ["placeholder-a.png"] },
              "working": {
                "frames": [],
                "fallback": "idle"
              }
            }
            """);

        var resolved = manifest.Resolve(PetAnimationKey.Working, _ => true);

        Assert.Equal(PetAnimationKey.Idle, resolved.Key);
        Assert.Equal(1000, resolved.IntervalMs);
    }
}
