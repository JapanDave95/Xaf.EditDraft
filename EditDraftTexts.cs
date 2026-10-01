using System;

namespace Xaf.EditDraft.Core;

/// <summary>The built-in text sets. Chosen explicitly by the host (<see cref="EditDraftTexts.Use(EditDraftLanguage)"/>), never from the thread culture.</summary>
public enum EditDraftLanguage
{
    English,
    Japanese
}

/// <summary>
/// Every user-facing word the platform-agnostic engine produces (status texts, empty/unknown markers, provenance,
/// the duplicate note, the context separator). Log lines are not texts: they stay as they are.
/// A host may supply its own set (all properties are init-only); a null property falls back to English.
/// </summary>
public sealed class EditDraftTextSet
{
    public string StatusAlreadyApplied { get; init; }
    public string StatusClean { get; init; }
    public string StatusConflict { get; init; }
    public string StatusUnverifiable { get; init; }
    public string StatusUnavailable { get; init; }
    public string StatusNew { get; init; }
    /// <summary>Appended to a selectable status of a member whose setter changes another record.</summary>
    public string SideEffectSuffix { get; init; }
    public string Empty { get; init; }
    public string Unknown { get; init; }
    public string Yes { get; init; }
    public string No { get; init; }
    /// <summary>Appended to a member drafted again in an OLDER draft of the same record.</summary>
    public string DuplicateNote { get; init; }
    public string ProvenanceUnknown { get; init; }
    public string FromList { get; init; }
    public string FromDetail { get; init; }
    /// <summary>{0} = FromList/FromDetail, {1} = the view caption.</summary>
    public string ProvenanceFormat { get; init; }
    /// <summary>Between the type caption and the date in a draft's context text.</summary>
    public string ContextSeparator { get; init; }

    /// <summary>Today's CareCrew strings, byte for byte (the golden snapshot and the wave tests pin them).</summary>
    public static EditDraftTextSet Japanese { get; } = new()
    {
        StatusAlreadyApplied = "反映済み",
        StatusClean = "戻せます",
        StatusConflict = "他で変更されています",
        StatusUnverifiable = "変更前の値が不明です",
        StatusUnavailable = "戻せません",
        StatusNew = "新規",
        SideEffectSuffix = "（他の記録も変わります）",
        Empty = "（空）",
        Unknown = "（不明）",
        Yes = "はい",
        No = "いいえ",
        DuplicateNote = "（新しい入力控に同じ項目があります）",
        ProvenanceUnknown = "由来不明",
        FromList = "一覧から",
        FromDetail = "詳細から",
        ProvenanceFormat = "{0}（{1}）",
        ContextSeparator = "／"
    };

    public static EditDraftTextSet English { get; } = new()
    {
        StatusAlreadyApplied = "Already applied",
        StatusClean = "Can be restored",
        StatusConflict = "Changed elsewhere",
        StatusUnverifiable = "Value before the edit is unknown",
        StatusUnavailable = "Cannot be restored",
        StatusNew = "New",
        SideEffectSuffix = " (other records change too)",
        Empty = "(empty)",
        Unknown = "(unknown)",
        Yes = "Yes",
        No = "No",
        DuplicateNote = " (a newer draft has the same field)",
        ProvenanceUnknown = "Unknown origin",
        FromList = "From the list",
        FromDetail = "From the detail screen",
        ProvenanceFormat = "{0} ({1})",
        ContextSeparator = " / "
    };
}

/// <summary>
/// The text set in use, chosen once by the host at startup. Default: <see cref="EditDraftTextSet.English"/>.
/// The thread culture is never consulted.
/// </summary>
public static class EditDraftTexts
{
    private static volatile EditDraftTextSet _current = EditDraftTextSet.English;

    public static EditDraftTextSet Current => _current;

    public static void Use(EditDraftLanguage language) =>
        _current = language == EditDraftLanguage.Japanese ? EditDraftTextSet.Japanese : EditDraftTextSet.English;

    /// <summary>A host-supplied set; null returns to English.</summary>
    public static void Use(EditDraftTextSet set) => _current = set ?? EditDraftTextSet.English;

    /// <summary>The text, or the English one when the set in use leaves it null.</summary>
    internal static string Of(Func<EditDraftTextSet, string> key)
    {
        var text = key(_current);
        return text ?? key(EditDraftTextSet.English);
    }
}
