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
        return new PetStateAnimationCoordinator(manifest, (string _) => true, nextExtraIndex);
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

    private const string WorkingState = """
        {
          "clips": {
            "haul": {
              "frames": ["haul/001.png", "haul/002.png"], "frameDurationMs": 100, "playback": "loop",
              "statusAnchor": { "x": 0.5, "y": 0.11 }
            }
          }
        }
        """;

    private static PetStateAnimationCoordinator CreateWithWorking(Func<int, int>? nextExtraIndex = null)
    {
        var manifest = AnimationManifestTestData.Parse(5, IdleState, WorkingState);
        return new PetStateAnimationCoordinator(manifest, _ => true, nextExtraIndex);
    }

    [Fact]
    public void Repeated_messages_for_the_same_state_do_not_interrupt_an_extra_or_reset_the_cooldown()
    {
        var coordinator = Create(_ => 0);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        for (var tick = 0; tick < 50; tick++) coordinator.Advance();
        Assert.Equal("Animations/idle/stretch/001.png", coordinator.Frame);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        Assert.Equal("Animations/idle/stretch/001.png", coordinator.Frame);

        coordinator.Advance();
        coordinator.Advance();
        Assert.Equal("Animations/idle/breathe/001.png", coordinator.Frame);

        // The cooldown restarted when the extra returned, so the next extra needs another 50 ticks.
        for (var tick = 0; tick < 49; tick++) coordinator.Advance();
        Assert.StartsWith("Animations/idle/breathe/", coordinator.Frame, StringComparison.Ordinal);
        coordinator.Advance();
        Assert.Equal("Animations/idle/stretch/001.png", coordinator.Frame);
    }

    [Fact]
    public void A_real_state_change_abandons_the_extra_and_restarts_the_cooldown()
    {
        var coordinator = CreateWithWorking(_ => 0);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        for (var tick = 0; tick < 50; tick++) coordinator.Advance();
        Assert.Equal("Animations/idle/stretch/001.png", coordinator.Frame);

        coordinator.Apply(PetAnimationKey.Working, reducedMotion: false);
        Assert.Equal("Animations/working/haul/001.png", coordinator.Frame);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        Assert.Equal("Animations/idle/breathe/001.png", coordinator.Frame);
        for (var tick = 0; tick < 49; tick++) coordinator.Advance();
        Assert.StartsWith("Animations/idle/breathe/", coordinator.Frame, StringComparison.Ordinal);
        coordinator.Advance();
        Assert.Equal("Animations/idle/stretch/001.png", coordinator.Frame);
    }

    [Fact]
    public void Reduced_motion_never_plays_an_extra()
    {
        var coordinator = Create(_ => 0);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: true);
        Assert.False(coordinator.IsAnimating);
        Assert.Equal("Animations/idle/breathe/001.png", coordinator.Frame);

        for (var tick = 0; tick < 500; tick++) coordinator.Advance();

        Assert.Equal("Animations/idle/breathe/001.png", coordinator.Frame);
        Assert.False(coordinator.IsAnimating);
    }

    [Fact]
    public void A_one_shot_state_never_reaches_an_extra()
    {
        var manifest = AnimationManifestTestData.ParseIdle(5, """
            {
              "clips": {
                "complete": {
                  "frames": ["complete/001.png", "complete/002.png"], "frameDurationMs": 100, "playback": "once",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                },
                "stretch": {
                  "frames": ["stretch/001.png"], "frameDurationMs": 1000, "playback": "once",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                }
              },
              "extras": { "clips": ["stretch"], "cooldownMs": 5000 }
            }
            """);
        var coordinator = new PetStateAnimationCoordinator(manifest, _ => true, (int _) => 0);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        for (var tick = 0; tick < 100; tick++) coordinator.Advance();

        Assert.Equal("Animations/idle/complete/002.png", coordinator.Frame);
        Assert.False(coordinator.IsAnimating);
    }

    [Fact]
    public void A_static_primary_keeps_a_one_second_heartbeat_so_extras_still_fire()
    {
        // The primary is a single static frame, so it never asks for a frame advance on its own; the
        // 1000 ms heartbeat is the only thing that can measure the cooldown.  The extra runs two
        // frames at 50 ms, so its reported interval is distinguishable from that heartbeat.
        var staticManifest = AnimationManifestTestData.ParseIdle(5, """
            {
              "clips": {
                "pose": {
                  "frames": ["pose/001.png"], "frameDurationMs": 100, "playback": "loop",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                },
                "stretch": {
                  "frames": ["stretch/001.png", "stretch/002.png"], "frameDurationMs": 50, "playback": "once",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                }
              },
              "extras": { "clips": ["stretch"], "cooldownMs": 5000 }
            }
            """);
        var coordinator = new PetStateAnimationCoordinator(staticManifest, _ => true, (int _) => 0);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        Assert.True(coordinator.IsAnimating);
        Assert.Equal(1000, coordinator.IntervalMs);

        // Five heartbeats: 5 * 1000 ms reaches the 5000 ms cooldown and starts the extra.
        for (var tick = 0; tick < 5; tick++) coordinator.Advance();

        Assert.Equal("Animations/idle/stretch/001.png", coordinator.Frame);
        Assert.Equal(50, coordinator.IntervalMs);

        // The extra holds two 50 ms frames, so its second frame is shown on the next heartbeat.
        coordinator.Advance();
        Assert.Equal("Animations/idle/stretch/002.png", coordinator.Frame);
        Assert.Equal(50, coordinator.IntervalMs);

        coordinator.Advance();
        Assert.Equal("Animations/idle/pose/001.png", coordinator.Frame);
        Assert.Equal(1000, coordinator.IntervalMs);
    }
}
