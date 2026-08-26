using PetHelper;
using System.Reflection;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetAnimationManifestTests
{
    [Fact]
    public void Resolves_a_configured_multiframe_action_in_order()
    {
        var manifest = PetAnimationManifest.Parse("""
            {
              "idle": { "frames": ["placeholder-a.png"], "intervalMs": 1000 },
              "thinking": {
                "frames": ["Animations/thinking/001.png", "Animations/thinking/002.png"],
                "intervalMs": 120,
                "fallback": "idle"
              }
            }
            """);

        var resolved = manifest.Resolve(PetAnimationKey.Thinking, _ => true);

        Assert.Equal(PetAnimationKey.Thinking, resolved.Key);
        Assert.Equal(new[] { "Animations/thinking/001.png", "Animations/thinking/002.png" }, resolved.Frames);
        Assert.Equal(120, resolved.IntervalMs);
    }

    [Fact]
    public void Resolves_missing_composite_action_through_working_before_idle()
    {
        var manifest = PetAnimationManifest.Parse("""
            {
              "idle": { "frames": ["placeholder-a.png"], "intervalMs": 1000 },
              "working": { "frames": ["Animations/working/001.png"], "intervalMs": 100, "fallback": "idle" },
              "thinking-working": { "frames": [], "intervalMs": 100, "fallback": "working" }
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
            { "idle": { "frames": ["{{frame}}"], "intervalMs": 1000 } }
            """));
    }

    [Theory]
    [InlineData("{ \"working\": { \"frames\": [\"placeholder-a.png\"], \"intervalMs\": 100 } }")]
    [InlineData("{ \"idle\": { \"frames\": [], \"intervalMs\": 1000 } }")]
    [InlineData("{ \"idle\": { \"frames\": [\"placeholder-a.png\"], \"intervalMs\": 1000 }, \"dance\": { \"frames\": [], \"intervalMs\": 100 } }")]
    [InlineData("{ \"idle\": { \"frames\": [\"placeholder-a.png\"], \"intervalMs\": 1000, \"unexpected\": true } }")]
    [InlineData("{ \"idle\": { \"frames\": [\"placeholder-a.png\"], \"intervalMs\": 1000 }, \"thinking\": { \"frames\": [], \"intervalMs\": 100, \"fallback\": \"dance\" } }")]
    [InlineData("{ \"idle\": { \"frames\": [\"placeholder-a.png\"], \"intervalMs\": 1000 }, \"thinking\": { \"frames\": [], \"intervalMs\": 100, \"fallback\": \"working\" }, \"working\": { \"frames\": [], \"intervalMs\": 100, \"fallback\": \"thinking\" } }")]
    [InlineData("{ \"idle\": { \"frames\": [\"placeholder-a.png\"], \"intervalMs\": 1000 }, \"thinking\": { \"frames\": [], \"intervalMs\": 100, \"fallback\": \"thinking\" } }")]
    [InlineData("{ \"idle\": { \"frames\": [\"placeholder-a.png\", \"placeholder-a.png\"], \"intervalMs\": 1000 } }")]
    [InlineData("{ \"idle\": { \"frames\": [\"placeholder-a.png\"], \"intervalMs\": 15 } }")]
    [InlineData("{ \"idle\": { \"frames\": [\"placeholder-a.png\"], \"intervalMs\": 10001 } }")]
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
        var resolved = manifest.Resolve(PetAnimationKey.Idle, frame => frame == "placeholder-a.png");

        Assert.Equal(PetAnimationKey.Idle, resolved.Key);
        Assert.Equal(new[] { "placeholder-a.png" }, resolved.Frames);
        Assert.Equal(1000, resolved.IntervalMs);
    }

    [Fact]
    public void Missing_embedded_manifest_is_reported_without_resource_details()
    {
        var method = typeof(PetAnimationPlayer).GetMethod(
            "LoadManifest",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(Assembly)],
            modifiers: null);

        Assert.NotNull(method);
        var exception = Assert.Throws<TargetInvocationException>(() => method!.Invoke(null, [typeof(PetAnimationManifestTests).Assembly]));
        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);

        Assert.Equal("The pet animation manifest is unavailable.", inner.Message);
        Assert.DoesNotContain("Assets", inner.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pack:", inner.Message, StringComparison.OrdinalIgnoreCase);
    }
}
