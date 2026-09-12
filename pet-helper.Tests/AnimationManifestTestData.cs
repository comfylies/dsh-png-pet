using PetHelper;

namespace PetHelper.Tests;

internal static class AnimationManifestTestData
{
    internal static string Root(int version) => $$"""
        {
          "formatVersion": {{version}},
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
        """;

    /// <summary>Parses the ten-action root with custom idle and working state manifests.</summary>
    internal static PetAnimationManifest Parse(int version, string idleStateJson, string workingStateJson) =>
        PetAnimationManifest.Parse(Root(version), path => path switch
        {
            "Animations/idle/animation.json" => idleStateJson,
            "Animations/working/animation.json" => workingStateJson,
            _ => "{ \"clips\": {} }",
        });

    /// <summary>Parses the ten-action root with a custom idle state manifest; every other state is empty.</summary>
    internal static PetAnimationManifest ParseIdle(int version, string idleStateJson) =>
        Parse(version, idleStateJson, "{ \"clips\": {} }");
}
