using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetAnimationManifestTests
{
    [Fact]
    public void Resolves_a_version_three_action_manifest_from_its_own_state_directory()
    {
        var manifest = PetAnimationManifest.Parse("""
            {
              "formatVersion": 3,
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
            """, path => path == "Animations/idle/animation.json" ? """
            {
              "clips": {
                "breathe": {
                  "frames": ["breathe/001.png", "breathe/002.png"],
                  "frameDurationMs": 160,
                  "playback": "loop",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                }
              }
            }
            """ : "{ \"clips\": {} }");

        var resolved = manifest.Resolve(PetAnimationKey.Idle, _ => true);

        Assert.Equal("idle-breathe", resolved.Id);
        Assert.Equal(
            new[] { "Animations/idle/breathe/001.png", "Animations/idle/breathe/002.png" },
            resolved.Frames);
        Assert.Equal(new PetStatusAnchor(0.5d, 0.11d), resolved.StatusAnchor);
    }

    [Fact]
    public void Rejects_a_version_three_action_manifest_outside_its_own_directory()
    {
        Assert.Throws<FormatException>(() => PetAnimationManifest.Parse("""
            {
              "formatVersion": 3,
              "actions": {
                "idle": { "manifest": "Animations/working/animation.json" }
              }
            }
            """, _ => "{ \"clips\": {} }"));
    }

    [Fact]
    public void Resolves_a_version_two_clip_with_its_declared_duration_and_playback_mode()
    {
        var manifest = PetAnimationManifest.Parse("""
            {
              "formatVersion": 2,
              "clips": {
                "idle-look-around": {
                  "frames": ["Animations/idle/001.png", "Animations/idle/002.png"],
                  "frameDurationMs": 160,
                  "playback": "once",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                }
              },
              "actions": {
                "idle": { "clips": ["idle-look-around"] }
              }
            }
            """);

        var resolved = manifest.Resolve(PetAnimationKey.Idle, _ => true);

        Assert.Equal("idle-look-around", resolved.Id);
        Assert.Equal(160, resolved.FrameDurationMs);
        Assert.Equal(PetClipPlaybackMode.Once, resolved.Playback);
    }

    [Fact]
    public void Converts_a_legacy_action_to_a_looping_default_clip()
    {
        var manifest = PetAnimationManifest.Parse("""
            { "idle": { "frames": ["Animations/idle/001.png", "Animations/idle/002.png"] } }
            """);

        var resolved = manifest.Resolve(PetAnimationKey.Idle, _ => true);

        Assert.Equal("idle-default", resolved.Id);
        Assert.Equal(500, resolved.FrameDurationMs);
        Assert.Equal(PetClipPlaybackMode.Loop, resolved.Playback);
    }

    [Theory]
    [InlineData("Idle-looks")]
    [InlineData("idle_looks")]
    [InlineData("idle looks")]
    public void Rejects_an_invalid_version_two_clip_identifier(string clipId)
    {
        Assert.Throws<FormatException>(() => PetAnimationManifest.Parse($$"""
            {
              "formatVersion": 2,
              "clips": {
                "{{clipId}}": {
                  "frames": ["Animations/idle/001.png"],
                  "frameDurationMs": 160,
                  "playback": "loop",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                }
              },
              "actions": { "idle": { "clips": ["{{clipId}}"] } }
            }
            """));
    }

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
        Assert.Equal(PetStatusAnchor.Default, resolved.StatusAnchor);
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
    [InlineData(PetAnimationKey.Responding)]
    [InlineData(PetAnimationKey.Waiting)]
    [InlineData(PetAnimationKey.Question)]
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

        var manifest = PetAnimationManifest.Parse(reader.ReadToEnd(), path =>
        {
            using var actionStream = assembly.GetManifestResourceStream(
                $"PetHelper.Assets.{path.Replace('/', '.').Replace('-', '_')}");
            Assert.NotNull(actionStream);
            using var actionReader = new StreamReader(actionStream!);
            return actionReader.ReadToEnd();
        });
        var idleFrames = Enumerable.Range(1, 32)
            .Select(i => $"Animations/idle/breathe/{i:D3}.png")
            .ToArray();
        var questionRaiseFrames = Enumerable.Range(1, 30)
            .Select(i => $"Animations/question/raise-card/{i:D3}.png")
            .ToArray();
        var questionHoldFrames = Enumerable.Range(1, 45)
            .Select(i => $"Animations/question/hold-card/{i:D3}.png")
            .ToArray();
        var questionWithdrawFrames = Enumerable.Range(1, 22)
            .Select(i => $"Animations/question/withdraw-card/{i:D3}.png")
            .ToArray();
        var successFrames = Enumerable.Range(1, 60)
            .Select(i => $"Animations/success/complete/{i:D3}.png")
            .ToArray();
        var resolved = manifest.Resolve(
            PetAnimationKey.Idle,
            frame => idleFrames.Contains(frame) || questionRaiseFrames.Contains(frame) ||
                questionHoldFrames.Contains(frame) || questionWithdrawFrames.Contains(frame) || successFrames.Contains(frame));

        Assert.Equal(PetAnimationKey.Idle, resolved.Key);
        Assert.Equal(idleFrames, resolved.Frames);
        Assert.Equal(125, resolved.IntervalMs);

        var question = manifest.ResolveProgram(
            PetAnimationKey.Question,
            frame => idleFrames.Contains(frame) || questionRaiseFrames.Contains(frame) ||
                questionHoldFrames.Contains(frame) || questionWithdrawFrames.Contains(frame) || successFrames.Contains(frame));

        Assert.Equal(PetAnimationKey.Question, question.EffectiveKey);
        Assert.Equal(questionRaiseFrames, question.Enter.Single().Frames);
        Assert.Equal(questionHoldFrames, question.Loop.Single().Frames);
        Assert.Equal(83, question.Loop.Single().IntervalMs);
        var withdraw = Assert.Single(question.Transitions);
        Assert.Equal(questionWithdrawFrames, Assert.Single(withdraw.Clips).Frames);
        Assert.Contains(PetAnimationKey.Thinking, withdraw.Targets);
        Assert.Contains(PetAnimationKey.Working, withdraw.Targets);
        Assert.Contains(PetAnimationKey.ThinkingWorking, withdraw.Targets);
        Assert.Contains(PetAnimationKey.Responding, withdraw.Targets);

        var success = manifest.Resolve(
            PetAnimationKey.Success,
            frame => idleFrames.Contains(frame) || questionRaiseFrames.Contains(frame) ||
                questionHoldFrames.Contains(frame) || questionWithdrawFrames.Contains(frame) || successFrames.Contains(frame));

        Assert.Equal(PetAnimationKey.Success, success.Key);
        Assert.Equal(successFrames, success.Frames);
        Assert.Equal(83, success.IntervalMs);
        Assert.Equal(PetClipPlaybackMode.Once, success.Playback);
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

    [Fact]
    public void Resolves_the_status_anchor_from_the_effective_version_three_fallback_action()
    {
        var manifest = PetAnimationManifest.Parse("""
            {
              "formatVersion": 3,
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
            """, path => path == "Animations/idle/animation.json" ? """
            {
              "clips": {
                "breathe": {
                  "frames": ["breathe/001.png"],
                  "frameDurationMs": 1000,
                  "playback": "loop",
                  "statusAnchor": { "x": 0.5, "y": 0.08 }
                }
              }
            }
            """ : "{ \"clips\": {} }");

        var resolved = manifest.Resolve(PetAnimationKey.Working, _ => true);

        Assert.Equal(PetAnimationKey.Idle, resolved.Key);
        Assert.Equal(new PetStatusAnchor(0.5d, 0.08d), resolved.StatusAnchor);
    }

    [Theory]
    [InlineData("{ \"frames\": [\"Animations/idle/001.png\"], \"frameDurationMs\": 1000, \"playback\": \"loop\" }")]
    [InlineData("{ \"frames\": [\"Animations/idle/001.png\"], \"frameDurationMs\": 1000, \"playback\": \"loop\", \"statusAnchor\": { \"x\": -0.1, \"y\": 0.1 } }")]
    [InlineData("{ \"frames\": [\"Animations/idle/001.png\"], \"frameDurationMs\": 1000, \"playback\": \"loop\", \"statusAnchor\": { \"x\": 0.5 } }")]
    public void Rejects_missing_or_invalid_status_anchors_for_version_two_clips(string clip)
    {
        Assert.Throws<FormatException>(() => PetAnimationManifest.Parse($$"""
            {
              "formatVersion": 2,
              "clips": { "idle-default": {{clip}} },
              "actions": { "idle": { "clips": ["idle-default"] } }
            }
            """));
    }
}
