using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetAnimationExtrasManifestTests
{
    private const string IdleWithExtras = """
        {
          "clips": {
            "breathe": {
              "frames": ["breathe/001.png", "breathe/002.png"],
              "frameDurationMs": 125, "playback": "loop",
              "statusAnchor": { "x": 0.5, "y": 0.11 }, "label": "呼吸"
            },
            "stretch": {
              "frames": ["stretch/001.png", "stretch/002.png"],
              "frameDurationMs": 100, "playback": "once",
              "statusAnchor": { "x": 0.5, "y": 0.11 }, "label": "伸懒腰"
            }
          },
          "extras": { "clips": ["stretch"], "cooldownMs": 30000 }
        }
        """;

    [Fact]
    public void Extras_are_resolved_separately_from_the_primary_loop()
    {
        var manifest = AnimationManifestTestData.ParseIdle(5, IdleWithExtras);

        var program = manifest.ResolveProgram(PetAnimationKey.Idle, _ => true);

        Assert.Equal(new[] { "idle-breathe" }, program.Loop.Select(clip => clip.Id));
        Assert.Equal(new[] { "idle-stretch" }, program.Extras.Select(clip => clip.Id));
        Assert.Equal(30000, program.ExtrasCooldownMs);
        Assert.Equal("伸懒腰", program.Extras[0].Label);
    }

    [Fact]
    public void Extras_default_to_thirty_seconds_and_are_absent_by_default()
    {
        var manifest = AnimationManifestTestData.ParseIdle(5, """
            {
              "clips": {
                "breathe": {
                  "frames": ["breathe/001.png"], "frameDurationMs": 125, "playback": "loop",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                }
              }
            }
            """);

        var program = manifest.ResolveProgram(PetAnimationKey.Idle, _ => true);

        Assert.Empty(program.Extras);
        Assert.Equal(30000, program.ExtrasCooldownMs);
        Assert.Equal(PetAnimationKey.Idle, program.EffectiveKey);
    }

    [Fact]
    public void Extras_without_available_frames_are_dropped()
    {
        var manifest = AnimationManifestTestData.ParseIdle(5, IdleWithExtras);

        // Structured state manifests store frames with their "Animations/<action>/" prefix, so the
        // availability predicate has to match the resolved identifier.
        var program = manifest.ResolveProgram(PetAnimationKey.Idle, frame => frame.StartsWith("Animations/idle/breathe/", StringComparison.Ordinal));

        Assert.Empty(program.Extras);
        Assert.Equal(new[] { "idle-breathe" }, program.Loop.Select(clip => clip.Id));
    }

    [Theory]
    // A clip cannot be both the primary loop and an extra.
    [InlineData("\"program\": { \"enter\": [], \"loop\": [\"stretch\"] }, \"extras\": { \"clips\": [\"stretch\"] }")]
    // Extras must be declared as one-shot clips.
    [InlineData("\"extras\": { \"clips\": [\"breathe\"] }")]
    // Duplicate extras are rejected.
    [InlineData("\"extras\": { \"clips\": [\"stretch\", \"stretch\"] }")]
    // Unknown extra clip id.
    [InlineData("\"extras\": { \"clips\": [\"missing\"] }")]
    // Cooldown below the lower bound.
    [InlineData("\"extras\": { \"clips\": [\"stretch\"], \"cooldownMs\": 4000 }")]
    // Cooldown above the upper bound.
    [InlineData("\"extras\": { \"clips\": [\"stretch\"], \"cooldownMs\": 600001 }")]
    // Unknown field inside extras.
    [InlineData("\"extras\": { \"clips\": [\"stretch\"], \"weight\": 2 }")]
    // Empty extras list.
    [InlineData("\"extras\": { \"clips\": [] }")]
    public void Rejects_an_invalid_extras_block(string extrasOrProgram)
    {
        var state = $$"""
            {
              "clips": {
                "breathe": {
                  "frames": ["breathe/001.png", "breathe/002.png"], "frameDurationMs": 125, "playback": "loop",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                },
                "stretch": {
                  "frames": ["stretch/001.png", "stretch/002.png"], "frameDurationMs": 100, "playback": "once",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                }
              },
              {{extrasOrProgram}}
            }
            """;

        Assert.Throws<FormatException>(() => AnimationManifestTestData.ParseIdle(5, state));
    }

    [Fact]
    public void Rejects_more_than_four_extras()
    {
        var clips = string.Join(",", Enumerable.Range(0, 5).Select(index =>
            $"\"extra{index}\": {{ \"frames\": [\"extra{index}/001.png\"], \"frameDurationMs\": 100, \"playback\": \"once\", \"statusAnchor\": {{ \"x\": 0.5, \"y\": 0.11 }} }}"));
        var ids = string.Join(",", Enumerable.Range(0, 5).Select(index => $"\"extra{index}\""));
        var state = $$"""
            {
              "clips": {
                "breathe": { "frames": ["breathe/001.png", "breathe/002.png"], "frameDurationMs": 125, "playback": "loop", "statusAnchor": { "x": 0.5, "y": 0.11 } },
                {{clips}}
              },
              "extras": { "clips": [{{ids}}] }
            }
            """;

        Assert.Throws<FormatException>(() => AnimationManifestTestData.ParseIdle(5, state));
    }

    [Fact]
    public void Rejects_extras_that_leave_no_primary_action()
    {
        Assert.Throws<FormatException>(() => AnimationManifestTestData.ParseIdle(5, """
            {
              "clips": {
                "stretch": {
                  "frames": ["stretch/001.png"], "frameDurationMs": 100, "playback": "once",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                }
              },
              "extras": { "clips": ["stretch"] }
            }
            """));
    }

    [Fact]
    public void Version_four_rejects_extras()
    {
        Assert.Throws<FormatException>(() => AnimationManifestTestData.ParseIdle(4, IdleWithExtras));
    }
}
