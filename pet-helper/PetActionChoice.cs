using System.Collections.Immutable;

namespace PetHelper;

/// <summary>
/// One previewable action of a single state: its local display name, whether it is a one-shot extra,
/// and the resolved clip.  Display names never leave the Helper.
/// </summary>
internal sealed record PetActionChoice(string Label, bool IsExtra, ResolvedClip Clip);

/// <summary>
/// Builds the previewable action list of a resolved state.  Both sources share this order and shape;
/// they differ only in the fallback display name of a clip that declares none: a built-in manifest
/// clip keeps its stable clip id, an imported character gets the two generic names.
/// </summary>
internal static class PetActionCatalog
{
    internal static ImmutableArray<PetActionChoice> Build(ResolvedStateProgram program, string? primaryLabel,
        string? extraLabel) => program.Loop
        .Select(clip => new PetActionChoice(clip.Label ?? primaryLabel ?? clip.Id, false, clip))
        .Concat(program.Extras.Select(clip => new PetActionChoice(clip.Label ?? extraLabel ?? clip.Id, true, clip)))
        .ToImmutableArray();
}
