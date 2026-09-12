namespace PetHelper;

/// <summary>
/// One previewable action of a single state: its local display name, whether it is a one-shot extra,
/// and the resolved clip.  Display names never leave the Helper.
/// </summary>
public sealed record PetActionChoice(string Label, bool IsExtra, ResolvedClip Clip);
