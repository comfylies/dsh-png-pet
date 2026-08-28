using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetAnimationPlaybackTests
{
    [Fact]
    public void Advances_a_two_frame_action_and_wraps_to_its_first_frame()
    {
        var playback = new PetAnimationPlayback(CreateManifest(), _ => true);

        playback.Apply(PetAnimationKey.Thinking, reducedMotion: false);
        Assert.Equal(PetAnimationKey.Thinking, playback.Key);
        Assert.Equal("Animations/thinking/001.png", playback.Frame);
        Assert.Equal(500, playback.IntervalMs);
        Assert.True(playback.IsAnimating);

        playback.Advance();
        Assert.Equal("Animations/thinking/002.png", playback.Frame);
        Assert.Equal(PetStatusAnchor.Default, playback.StatusAnchor);

        playback.Advance();
        Assert.Equal("Animations/thinking/001.png", playback.Frame);
        Assert.Equal(PetStatusAnchor.Default, playback.StatusAnchor);
    }

    [Fact]
    public void Reapplying_the_same_effective_action_preserves_the_current_frame()
    {
        var playback = new PetAnimationPlayback(CreateManifest(), _ => true);

        playback.Apply(PetAnimationKey.Thinking, reducedMotion: false);
        playback.Advance();
        playback.Apply(PetAnimationKey.Thinking, reducedMotion: false);

        Assert.Equal("Animations/thinking/002.png", playback.Frame);
    }

    [Fact]
    public void Switching_actions_resets_to_the_new_actions_first_frame()
    {
        var playback = new PetAnimationPlayback(CreateManifest(), _ => true);

        playback.Apply(PetAnimationKey.Thinking, reducedMotion: false);
        playback.Advance();
        playback.Apply(PetAnimationKey.Working, reducedMotion: false);

        Assert.Equal(PetAnimationKey.Working, playback.Key);
        Assert.Equal("Animations/working/001.png", playback.Frame);
        Assert.False(playback.IsAnimating);
    }

    [Fact]
    public void A_fallback_and_its_effective_action_do_not_reset_the_current_frame()
    {
        var playback = new PetAnimationPlayback(CreateManifestWithTwoWorkingFrames(), _ => true);

        playback.Apply(PetAnimationKey.ThinkingWorking, reducedMotion: false);
        playback.Advance();
        playback.Apply(PetAnimationKey.Working, reducedMotion: false);

        Assert.Equal(PetAnimationKey.Working, playback.Key);
        Assert.Equal("Animations/working/002.png", playback.Frame);
    }

    [Fact]
    public void Reduced_motion_holds_the_first_frame_and_animation_resumes_when_disabled()
    {
        var playback = new PetAnimationPlayback(CreateManifest(), _ => true);

        playback.Apply(PetAnimationKey.Thinking, reducedMotion: false);
        playback.Advance();
        playback.Apply(PetAnimationKey.Thinking, reducedMotion: true);
        playback.Advance();

        Assert.Equal("Animations/thinking/001.png", playback.Frame);
        Assert.False(playback.IsAnimating);

        playback.Apply(PetAnimationKey.Thinking, reducedMotion: false);

        Assert.Equal("Animations/thinking/001.png", playback.Frame);
        Assert.True(playback.IsAnimating);
        playback.Advance();
        Assert.Equal("Animations/thinking/002.png", playback.Frame);
    }

    [Fact]
    public void A_single_frame_action_never_animates()
    {
        var playback = new PetAnimationPlayback(CreateManifest(), _ => true);

        playback.Apply(PetAnimationKey.Idle, reducedMotion: false);
        playback.Advance();

        Assert.Equal("placeholder-a.png", playback.Frame);
        Assert.False(playback.IsAnimating);
    }

    private static PetAnimationManifest CreateManifest() => PetAnimationManifest.Parse("""
        {
          "idle": { "frames": ["placeholder-a.png"] },
          "thinking": {
            "frames": ["Animations/thinking/001.png", "Animations/thinking/002.png"],
            "fallback": "idle"
          },
          "working": {
            "frames": ["Animations/working/001.png"],
            "fallback": "idle"
          },
          "thinking-working": { "frames": [], "fallback": "working" }
        }
        """);

    private static PetAnimationManifest CreateManifestWithTwoWorkingFrames() => PetAnimationManifest.Parse("""
        {
          "idle": { "frames": ["placeholder-a.png"] },
          "working": {
            "frames": ["Animations/working/001.png", "Animations/working/002.png"],
            "fallback": "idle"
          },
          "thinking-working": { "frames": [], "fallback": "working" }
        }
        """);
}
