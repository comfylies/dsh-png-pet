using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetExtrasPlaybackTests
{
    // Primary: two-frame loop at 100 ms. Extras: two-frame one-shot at 50 ms. Cooldown: 5000 ms.
    internal const string IdleState = """
        {
          "clips": {
            "breathe": {
              "frames": ["breathe/001.png", "breathe/002.png"], "frameDurationMs": 100, "playback": "loop",
              "statusAnchor": { "x": 0.5, "y": 0.11 }, "label": "呼吸"
            },
            "stretch": {
              "frames": ["stretch/001.png", "stretch/002.png"], "frameDurationMs": 50, "playback": "once",
              "statusAnchor": { "x": 0.5, "y": 0.11 }, "label": "伸懒腰"
            },
            "yawn": {
              "frames": ["yawn/001.png", "yawn/002.png"], "frameDurationMs": 50, "playback": "once",
              "statusAnchor": { "x": 0.5, "y": 0.11 }, "label": "打哈欠"
            }
          },
          "extras": { "clips": ["stretch", "yawn"], "cooldownMs": 5000 }
        }
        """;

    internal static PetStateAnimationCoordinator Create(Func<int, int>? nextExtraIndex = null)
    {
        var manifest = AnimationManifestTestData.ParseIdle(5, IdleState);
        // Bound through an explicitly typed delegate: a bare method group here makes the compiler
        // pick the manifest-only overload and report the three-argument call as unbindable.
        Func<PetAnimationKey, Func<string, bool>, ResolvedStateProgram> resolver =
            (key, isFrameAvailable) => manifest.ResolveProgram(key, isFrameAvailable);
        return new PetStateAnimationCoordinator(resolver, _ => true, nextExtraIndex);
    }

    [Fact]
    public void An_extra_starts_only_after_the_cooldown_and_returns_to_the_primary()
    {
        var coordinator = Create(_ => 0);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        Assert.Equal("Animations/idle/breathe/001.png", coordinator.Frame);

        for (var tick = 0; tick < 49; tick++) coordinator.Advance();
        Assert.StartsWith("Animations/idle/breathe/", coordinator.Frame, StringComparison.Ordinal);

        coordinator.Advance();  // 50th tick: 50 * 100 ms = the 5000 ms cooldown.
        Assert.Equal("Animations/idle/stretch/001.png", coordinator.Frame);

        coordinator.Advance();
        Assert.Equal("Animations/idle/stretch/002.png", coordinator.Frame);

        coordinator.Advance();  // The extra finishes and the primary restarts from its first frame.
        Assert.Equal("Animations/idle/breathe/001.png", coordinator.Frame);
    }

    [Fact]
    public void The_cooldown_restarts_after_every_extra()
    {
        var coordinator = Create(_ => 1);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        for (var tick = 0; tick < 50; tick++) coordinator.Advance();
        Assert.Equal("Animations/idle/yawn/001.png", coordinator.Frame);

        coordinator.Advance();
        coordinator.Advance();
        Assert.Equal("Animations/idle/breathe/001.png", coordinator.Frame);

        for (var tick = 0; tick < 49; tick++) coordinator.Advance();
        Assert.StartsWith("Animations/idle/breathe/", coordinator.Frame, StringComparison.Ordinal);

        coordinator.Advance();
        Assert.Equal("Animations/idle/yawn/001.png", coordinator.Frame);
    }

    [Fact]
    public void States_without_extras_never_interrupt_their_loop()
    {
        var coordinator = new PetStateAnimationCoordinator(AnimationManifestTestData.ParseIdle(5, """
            {
              "clips": {
                "breathe": {
                  "frames": ["breathe/001.png", "breathe/002.png"], "frameDurationMs": 100, "playback": "loop",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                }
              }
            }
            """), _ => true);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        for (var tick = 0; tick < 200; tick++)
        {
            coordinator.Advance();
            Assert.StartsWith("Animations/idle/breathe/", coordinator.Frame, StringComparison.Ordinal);
        }
    }
}
