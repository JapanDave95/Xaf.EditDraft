namespace Xaf.EditDraft.Core;

/// <summary>
/// One drafted member as a restore plan shows it — the platform-neutral row the engine builds
/// (<see cref="EditDraftRestorer.BuildItems"/>) and merges (<see cref="EditDraftOfferMerge"/>). A popup maps it
/// to its own row type (the library's: Xaf.EditDraft.Blazor.EditDraftRestoreItem) and keeps identity on its side: each
/// mapped row is a new object, created once per plan.
/// </summary>
public sealed class EditDraftRestoreRow
{
    /// <summary>The member path ("Member" or "Companion.Member").</summary>
    public string Path { get; set; }

    /// <summary>The group the member moves with (one tick per group), or null.</summary>
    public string Group { get; set; }

    /// <summary>項目: the member caption (prefixed with the draft marker when several drafts are shown).</summary>
    public string Label { get; set; }

    /// <summary>入力した内容: "before → after" (or the value of a NEW/seeded entry), shortened for a grid cell.</summary>
    public string ChangeText { get; set; }

    /// <summary>現在の値: shown for a conflict or an unverifiable entry, else empty.</summary>
    public string CurrentText { get; set; }

    /// <summary>The record's value when the plan was built; apply requires it to be unchanged.</summary>
    public string CurrentRaw { get; set; }

    /// <summary>状態: the status text (<see cref="EditDraftComparison.StatusText"/>).</summary>
    public string StatusText { get; set; }

    /// <summary>(int)<see cref="EditDraftItemStatus"/>.</summary>
    public int StatusCode { get; set; }

    public bool Selectable { get; set; }

    /// <summary>The member's setter changes another record: never pre-ticked.</summary>
    public bool SideEffect { get; set; }

    /// <summary>戻す: ticked.</summary>
    public bool Selected { get; set; }
}
