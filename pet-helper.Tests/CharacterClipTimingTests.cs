using System.Collections.Immutable;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class CharacterClipTimingTests
{
    [Fact]
    public void Variable_delays_include_the_entire_last_frame_before_completion()
    {
        var clip = new ResolvedClip(PetAnimationKey.Idle, "test", ["a", "b"], 100,
            PetClipPlaybackMode.Once, PetStatusAnchor.Default) { FrameDurationsMs = [40, 250] };
        var playback = new PetClipPlayback();
        var completed = 0;
        playback.Completed += (_, _) => completed++;
        playback.Start(clip, false);
        Assert.Equal(40, playback.FrameDurationMs);
        playback.Advance();
        Assert.Equal("b", playback.Frame);
        Assert.Equal(250, playback.FrameDurationMs);
        Assert.Equal(0, completed);
        playback.Advance();
        Assert.Equal(1, completed);
        Assert.False(playback.IsAnimating);
        playback.Advance();
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Single_frame_once_waits_one_interval_and_reduced_motion_never_ticks()
    {
        var clip = new ResolvedClip(PetAnimationKey.Idle, "test", ["a"], 80,
            PetClipPlaybackMode.Once, PetStatusAnchor.Default);
        var playback = new PetClipPlayback();
        playback.Start(clip, false);
        Assert.True(playback.IsAnimating);
        playback.Advance();
        Assert.False(playback.IsAnimating);
        playback.Start(clip, true, restart: true);
        Assert.False(playback.IsAnimating);
    }
}
