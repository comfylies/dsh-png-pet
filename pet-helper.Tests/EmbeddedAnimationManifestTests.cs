using System.IO;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class EmbeddedAnimationManifestTests
{
    [Fact]
    public void The_shipped_manifest_is_version_five_and_labels_its_primary_actions()
    {
        var assembly = typeof(PetAnimationPlayer).Assembly;
        string Read(string name)
        {
            using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException(name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        var manifest = PetAnimationManifest.Parse(
            Read("PetHelper.Assets.pet-animations.json"),
            path => Read("PetHelper.Assets." + path.Replace('/', '.').Replace('-', '_')));

        Assert.Equal(new[] { "呼吸" }, manifest.ResolveActions(PetAnimationKey.Idle, _ => true).Select(choice => choice.Label));
        Assert.Equal(new[] { "思考" }, manifest.ResolveActions(PetAnimationKey.Thinking, _ => true).Select(choice => choice.Label));
        Assert.Equal(new[] { "搬运" }, manifest.ResolveActions(PetAnimationKey.Working, _ => true).Select(choice => choice.Label));
        Assert.Equal(new[] { "打字" }, manifest.ResolveActions(PetAnimationKey.Responding, _ => true).Select(choice => choice.Label));
        Assert.Equal(new[] { "等待" }, manifest.ResolveActions(PetAnimationKey.Question, _ => true).Select(choice => choice.Label));
        Assert.Empty(manifest.ResolveActions(PetAnimationKey.Idle, _ => true).Where(choice => choice.IsExtra));
    }
}
