using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetStateAnimationCoordinatorTests
{
    [Fact]
    public void Waiting_transition_completes_once_and_enters_the_latest_matching_target()
    {
        var coordinator = new PetStateAnimationCoordinator(CreateManifest(), _ => true);

        coordinator.Apply(PetAnimationKey.Waiting, reducedMotion: false);
        Assert.Equal("Animations/waiting/raise/001.png", coordinator.Frame);

        coordinator.Advance();
        Assert.Equal("Animations/waiting/raise/002.png", coordinator.Frame);
        coordinator.Advance();
        Assert.Equal("Animations/waiting/hold/001.png", coordinator.Frame);

        coordinator.Apply(PetAnimationKey.Thinking, reducedMotion: false);
        Assert.Equal("Animations/waiting/withdraw/001.png", coordinator.Frame);
        coordinator.Apply(PetAnimationKey.Working, reducedMotion: false);
        Assert.Equal("Animations/waiting/withdraw/001.png", coordinator.Frame);

        coordinator.Advance();
        Assert.Equal("Animations/waiting/withdraw/002.png", coordinator.Frame);
        coordinator.Advance();
        Assert.Equal("Animations/working/working/001.png", coordinator.Frame);
    }

    [Fact]
    public void Any_source_action_can_declare_its_own_transition()
    {
        var coordinator = new PetStateAnimationCoordinator(CreateManifest(), _ => true);

        coordinator.Apply(PetAnimationKey.Thinking, reducedMotion: false);
        Assert.Equal("Animations/thinking/think/001.png", coordinator.Frame);

        coordinator.Apply(PetAnimationKey.Success, reducedMotion: false);
        Assert.Equal("Animations/thinking/finish/001.png", coordinator.Frame);

        coordinator.Advance();
        Assert.Equal("Animations/thinking/finish/002.png", coordinator.Frame);
        coordinator.Advance();
        Assert.Equal("Animations/success/success/001.png", coordinator.Frame);
    }

    [Fact]
    public void A_target_outside_the_current_route_interrupts_the_transition()
    {
        var coordinator = new PetStateAnimationCoordinator(CreateManifest(), _ => true);

        coordinator.Apply(PetAnimationKey.Waiting, reducedMotion: false);
        coordinator.Advance();
        coordinator.Advance();
        coordinator.Apply(PetAnimationKey.Thinking, reducedMotion: false);
        Assert.Equal("Animations/waiting/withdraw/001.png", coordinator.Frame);

        coordinator.Apply(PetAnimationKey.Error, reducedMotion: false);

        Assert.Equal("Animations/error/error/001.png", coordinator.Frame);
    }

    [Fact]
    public void Reduced_motion_skips_enter_and_transition_and_uses_the_target_loop_first_frame()
    {
        var coordinator = new PetStateAnimationCoordinator(CreateManifest(), _ => true);

        coordinator.Apply(PetAnimationKey.Waiting, reducedMotion: true);
        Assert.Equal("Animations/waiting/hold/001.png", coordinator.Frame);
        Assert.False(coordinator.IsAnimating);

        coordinator.Apply(PetAnimationKey.Thinking, reducedMotion: true);
        Assert.Equal("Animations/thinking/think/001.png", coordinator.Frame);
        Assert.False(coordinator.IsAnimating);
    }

    private static PetAnimationManifest CreateManifest() => PetAnimationManifest.Parse("""
        {
          "formatVersion": 4,
          "actions": {
            "idle": { "manifest": "Animations/idle/animation.json" },
            "thinking": { "manifest": "Animations/thinking/animation.json", "fallback": "idle" },
            "working": { "manifest": "Animations/working/animation.json", "fallback": "idle" },
            "thinking-working": { "manifest": "Animations/thinking-working/animation.json", "fallback": "working" },
            "responding": { "manifest": "Animations/responding/animation.json", "fallback": "idle" },
            "waiting": { "manifest": "Animations/waiting/animation.json", "fallback": "idle" },
            "question": { "manifest": "Animations/question/animation.json", "fallback": "waiting" },
            "success": { "manifest": "Animations/success/animation.json", "fallback": "idle" },
            "error": { "manifest": "Animations/error/animation.json", "fallback": "idle" },
            "disconnected": { "manifest": "Animations/disconnected/animation.json", "fallback": "idle" }
          }
        }
        """, ActionManifest);

    private static string ActionManifest(string path) => path switch
    {
        "Animations/waiting/animation.json" => """
            {
              "clips": {
                "raise": { "frames": ["raise/001.png", "raise/002.png"], "frameDurationMs": 50, "playback": "once", "statusAnchor": { "x": 0.5, "y": 0.11 } },
                "hold": { "frames": ["hold/001.png", "hold/002.png"], "frameDurationMs": 50, "playback": "once", "statusAnchor": { "x": 0.5, "y": 0.11 } },
                "withdraw": { "frames": ["withdraw/001.png", "withdraw/002.png"], "frameDurationMs": 50, "playback": "once", "statusAnchor": { "x": 0.5, "y": 0.11 } }
              },
              "program": { "enter": ["raise"], "loop": ["hold"] },
              "transitions": [ { "to": ["thinking", "working"], "clips": ["withdraw"] } ]
            }
            """,
        "Animations/thinking/animation.json" => """
            {
              "clips": {
                "think": { "frames": ["think/001.png", "think/002.png"], "frameDurationMs": 50, "playback": "once", "statusAnchor": { "x": 0.5, "y": 0.11 } },
                "finish": { "frames": ["finish/001.png", "finish/002.png"], "frameDurationMs": 50, "playback": "once", "statusAnchor": { "x": 0.5, "y": 0.11 } }
              },
              "program": { "enter": [], "loop": ["think"] },
              "transitions": [ { "to": ["success"], "clips": ["finish"] } ]
            }
            """,
        _ => StateManifest(path),
    };

    private static string StateManifest(string path)
    {
        var action = path.Split('/')[1];
        return $$"""
            {
              "clips": {
                "{{action}}": { "frames": ["{{action}}/001.png", "{{action}}/002.png"], "frameDurationMs": 50, "playback": "once", "statusAnchor": { "x": 0.5, "y": 0.11 } }
              },
              "program": { "enter": [], "loop": ["{{action}}"] }
            }
            """;
    }
}
