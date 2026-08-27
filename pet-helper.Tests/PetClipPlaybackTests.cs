using System.Collections.Immutable;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetClipPlaybackTests
{
    [Fact]
    public void A_long_once_clip_reaches_its_last_frame_and_completes_once()
    {
        var playback = new PetClipPlayback();
        var completed = 0;
        playback.Completed += (_, _) => completed++;

        playback.Start(OnceClip, reducedMotion: false);
        Assert.Equal("Animations/idle/001.png", playback.Frame);
        Assert.True(playback.IsAnimating);

        playback.Advance();
        Assert.Equal("Animations/idle/002.png", playback.Frame);
        Assert.True(playback.IsAnimating);

        playback.Advance();
        Assert.Equal("Animations/idle/003.png", playback.Frame);
        Assert.False(playback.IsAnimating);
        Assert.Equal(1, completed);

        playback.Advance();
        Assert.Equal("Animations/idle/003.png", playback.Frame);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void A_loop_clip_wraps_only_after_its_complete_frame_sequence()
    {
        var playback = new PetClipPlayback();

        playback.Start(LoopClip, reducedMotion: false);
        playback.Advance();
        Assert.Equal("Animations/idle/002.png", playback.Frame);
        playback.Advance();
        Assert.Equal("Animations/idle/003.png", playback.Frame);
        playback.Advance();
        Assert.Equal("Animations/idle/001.png", playback.Frame);
        Assert.True(playback.IsAnimating);
    }

    [Fact]
    public void Starting_the_same_clip_preserves_its_current_frame()
    {
        var playback = new PetClipPlayback();

        playback.Start(LoopClip, reducedMotion: false);
        playback.Advance();
        playback.Start(LoopClip, reducedMotion: false);

        Assert.Equal("Animations/idle/002.png", playback.Frame);
    }

    [Fact]
    public void Reduced_motion_holds_the_first_frame_without_ticks_then_resumes()
    {
        var playback = new PetClipPlayback();

        playback.Start(LoopClip, reducedMotion: false);
        playback.Advance();
        playback.Start(LoopClip, reducedMotion: true);
        playback.Advance();

        Assert.Equal("Animations/idle/001.png", playback.Frame);
        Assert.False(playback.IsAnimating);

        playback.Start(LoopClip, reducedMotion: false);
        Assert.True(playback.IsAnimating);
        playback.Advance();
        Assert.Equal("Animations/idle/002.png", playback.Frame);
    }

    [Fact]
    public void A_single_frame_once_clip_completes_without_starting_a_timer()
    {
        var playback = new PetClipPlayback();
        var completed = 0;
        playback.Completed += (_, _) => completed++;

        playback.Start(SingleFrameOnceClip, reducedMotion: false);

        Assert.Equal("Animations/idle/001.png", playback.Frame);
        Assert.False(playback.IsAnimating);
        Assert.Equal(1, completed);
    }

    private static readonly ResolvedClip OnceClip = new(
        PetAnimationKey.Idle,
        "idle-look-around",
        ImmutableArray.Create(
            "Animations/idle/001.png",
            "Animations/idle/002.png",
            "Animations/idle/003.png"),
        160,
        PetClipPlaybackMode.Once);

    private static readonly ResolvedClip LoopClip = OnceClip with
    {
        Id = "idle-breathe",
        Playback = PetClipPlaybackMode.Loop,
    };

    private static readonly ResolvedClip SingleFrameOnceClip = new(
        PetAnimationKey.Idle,
        "idle-blink",
        ImmutableArray.Create("Animations/idle/001.png"),
        160,
        PetClipPlaybackMode.Once);
}
