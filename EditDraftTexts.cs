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
/// Milestone M2: also every word the Blazor part (Xaf.EditDraft.Blazor) shows — action captions, popup captions and
/// lead lines, the 入力控 list and the messages — so one choice of set covers the whole library. Model captions
/// (class and member display names) are not texts here; they are localised in M3.
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

    // ---- Xaf.EditDraft.Blazor (milestone M2): the restore popup, the 入力控 list, the row badges. ----
    // Actions and popup captions
    public string ListCaption { get; init; }
    /// <summary>{0} = the type caption the list is filtered to.</summary>
    public string ListCaptionFiltered { get; init; }
    public string HeaderActionToolTip { get; init; }
    public string ActionOpen { get; init; }
    public string ActionDiscard { get; init; }
    public string ActionSelectAll { get; init; }
    public string ActionShowDiscarded { get; init; }
    public string ActionHideDiscarded { get; init; }
    public string RowOpenAction { get; init; }
    public string RowOpenToolTip { get; init; }
    public string Close { get; init; }
    public string OfferViewCaption { get; init; }
    public string OfferOk { get; init; }
    public string OfferLater { get; init; }
    public string ReadOnlyViewCaption { get; init; }
    // Confirmations
    public string ConfirmDiscardShown { get; init; }
    public string ConfirmDiscardOne { get; init; }
    public string ConfirmDiscardListRow { get; init; }
    // Offer and read-only display (popup data)
    /// <summary>{0} = the number of restorable entries.</summary>
    public string OfferLeadCount { get; init; }
    public string OfferLeadInstruction { get; init; }
    /// <summary>{0} = the number of entries changed elsewhere.</summary>
    public string OfferConflictBanner { get; init; }
    /// <summary>{0} marker, {1} type caption, {2} last capture (DateTime), {3} entry count, {4} origin.</summary>
    public string OfferProvenance { get; init; }
    /// <summary>Appended to a 戻せません entry's 項目 on the read-only display.</summary>
    public string ReadOnlyEntrySuffix { get; init; }
    /// <summary>{0} = the number of entries shown.</summary>
    public string ReadOnlyLead { get; init; }
    // The 入力控 list (popup data)
    public string ListLead { get; init; }
    public string ListEmpty { get; init; }
    public string ListReadFailed { get; init; }
    public string StateExisting { get; init; }
    public string DiscardedSuffix { get; init; }
    public string TargetNotShown { get; init; }
    // Messages
    public string Discarded { get; init; }
    public string DiscardFailedShownAgain { get; init; }
    public string DiscardFailed { get; init; }
    public string SelectDraftToOpen { get; init; }
    public string PersonalLoginOnly { get; init; }
    public string OpenListTabFirst { get; init; }
    public string ListOpenFailedReload { get; init; }
    public string ListOpenFailedContact { get; init; }
    public string DraftCannotOpen { get; init; }
    public string DraftUnreadable { get; init; }
    public string DraftTypeUnknown { get; init; }
    public string RecordNotFound { get; init; }
    public string RecordNotVisible { get; init; }
    public string NothingSelected { get; init; }
    public string ApplyScreenChanged { get; init; }
    public string ApplyViewNotEditable { get; init; }
    public string ApplyNotOwnLiveDraft { get; init; }
    public string ApplyClaimLost { get; init; }
    public string ApplyScreenHoldsDraft { get; init; }
    public string ApplyGuardStopped { get; init; }
    /// <summary>{0} = applied.</summary>
    public string Applied { get; init; }
    /// <summary>{0} = applied, {1} = not applied.</summary>
    public string AppliedPartly { get; init; }
    public string BadgeUnreadable { get; init; }
    public string FinishRowEditFirst { get; init; }
    public string SelectRowToOpen { get; init; }
    public string RowHasNoDraft { get; init; }
    public string OpenFailed { get; init; }

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
        ContextSeparator = "／",
        ListCaption = "入力控",
        ListCaptionFiltered = "入力控（{0}）",
        HeaderActionToolTip = "この画面の保存されていない入力控の一覧を開きます",
        ActionOpen = "開く",
        ActionDiscard = "破棄",
        ActionSelectAll = "すべて選択",
        ActionShowDiscarded = "破棄済みも表示",
        ActionHideDiscarded = "破棄済みを隠す",
        RowOpenAction = "入力控を開く",
        RowOpenToolTip = "この行の保存されていない入力控を、詳細画面で開いて戻します",
        Close = "閉じる",
        OfferViewCaption = "保存されていない入力が見つかりました",
        OfferOk = "はい（選択した項目を戻す）",
        OfferLater = "あとで",
        ReadOnlyViewCaption = "戻せない入力があります",
        ConfirmDiscardShown = "表示中の入力控をすべて破棄します。一覧の検索からは期限まで確認できます。よろしいですか？",
        ConfirmDiscardOne = "この入力控を破棄します。一覧の検索からは期限まで確認できます。よろしいですか？",
        ConfirmDiscardListRow = "この入力控を破棄します。検索からは期限まで確認できます。よろしいですか？",
        OfferLeadCount = "前回この記録に入力され、保存されていない内容が {0} 件あります。",
        OfferLeadInstruction = "戻す項目を選んでください。戻した内容はまだ保存されません — 確認してから保存してください。",
        OfferConflictBanner = "※ {0} 件は、この入力のあとに他で変更されています。チェックを外してあります。現在の値を確認してから戻してください。",
        OfferProvenance = "{0} {1}／入力 {2:yyyy/MM/dd HH:mm}（{3} 項目）／{4}",
        ReadOnlyEntrySuffix = "（戻せません）",
        ReadOnlyLead = "戻せない入力が {0} 件あります（表示のみ）。この項目は自動では戻せません。必要なら内容を見て入力し直してください。",
        ListLead = "保存されていない入力です。選んで「開く」を押すと記録に戻せます（戻した内容はまだ保存されません）。保存期限を過ぎると削除されます。",
        ListEmpty = "保存されていない入力控はありません。",
        ListReadFailed = "入力控を読み込めませんでした。入力控が消えたわけではありません。しばらくしてからもう一度開いてください。",
        StateExisting = "既存",
        DiscardedSuffix = "・破棄済み",
        TargetNotShown = "（表示できません）",
        Discarded = "入力控を破棄しました。",
        DiscardFailedShownAgain = "破棄できませんでした。次回もう一度表示されます。",
        DiscardFailed = "破棄できませんでした。",
        SelectDraftToOpen = "開く入力控を選んでください。",
        PersonalLoginOnly = "この画面の入力控は、職員個人のログインで使えます。",
        OpenListTabFirst = "対象の一覧タブを開いてから、もう一度押してください。",
        ListOpenFailedReload = "入力控を開けませんでした。画面を再読み込みしてからもう一度お試しください。",
        ListOpenFailedContact = "入力控を開けませんでした。もう一度お試しください。続く場合は管理者に連絡してください。",
        DraftCannotOpen = "この入力控は開けません（期限切れか、ほかの利用者のものです）。",
        DraftUnreadable = "この入力控は読み取れません。",
        DraftTypeUnknown = "この入力控の記録種別が分かりません。",
        RecordNotFound = "元の記録が見つかりません（削除されたか、表示できません）。",
        RecordNotVisible = "この記録は表示できません。",
        NothingSelected = "戻す項目が選ばれていません。",
        ApplyScreenChanged = "この入力控は戻せません（画面が変わりました）。",
        ApplyViewNotEditable = "この画面は編集できないため、入力控は戻せません。",
        ApplyNotOwnLiveDraft = "この入力控は戻せません（ログインが切り替わったか、すでに処理されています）。",
        ApplyClaimLost = "この入力控は戻せません（ほかの画面で戻されたか変更されました）。",
        ApplyScreenHoldsDraft = "この入力控は戻せません（この画面に保存されていない入力があります）。",
        ApplyGuardStopped = "入力控を戻している間に保存または取り消しが行われようとしたため、止めました。内容を確認してください。",
        Applied = "{0} 件を戻しました。内容を確認して保存してください。",
        AppliedPartly = "{0} 件を戻しました。{1} 件は戻せませんでした（その後に変更されたか、参照先がありません）。内容を確認してください。",
        BadgeUnreadable = "入力控を確認できませんでした。もう一度お試しください。",
        FinishRowEditFirst = "行の編集を確定（✓）または取り消し（✕）してから、もう一度押してください。",
        SelectRowToOpen = "開く行を選んでください。",
        RowHasNoDraft = "この行には保存されていない入力控がありません。",
        OpenFailed = "入力控を開けませんでした。もう一度お試しください。"
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
        ContextSeparator = " / ",
        ListCaption = "Drafts",
        ListCaptionFiltered = "Drafts ({0})",
        HeaderActionToolTip = "Opens the list of unsaved drafts of this screen",
        ActionOpen = "Open",
        ActionDiscard = "Discard",
        ActionSelectAll = "Select all",
        ActionShowDiscarded = "Show discarded",
        ActionHideDiscarded = "Hide discarded",
        RowOpenAction = "Open draft",
        RowOpenToolTip = "Opens this row's unsaved draft in the detail screen to put it back",
        Close = "Close",
        OfferViewCaption = "Unsaved input was found",
        OfferOk = "Yes (put the selected fields back)",
        OfferLater = "Later",
        ReadOnlyViewCaption = "Some input cannot be restored",
        ConfirmDiscardShown = "Every draft shown will be discarded. You can still find them by searching the list until they expire. Continue?",
        ConfirmDiscardOne = "This draft will be discarded. You can still find it by searching the list until it expires. Continue?",
        ConfirmDiscardListRow = "This draft will be discarded. You can still find it by searching until it expires. Continue?",
        OfferLeadCount = "This record has {0} field(s) typed earlier and not saved.",
        OfferLeadInstruction = "Select the fields to put back. They are not saved yet: check them, then save.",
        OfferConflictBanner = "* {0} field(s) were changed elsewhere after this input. They are not ticked. Check the current value before putting them back.",
        OfferProvenance = "{0} {1} / typed {2:yyyy/MM/dd HH:mm} ({3} field(s)) / {4}",
        ReadOnlyEntrySuffix = " (cannot be restored)",
        ReadOnlyLead = "{0} field(s) cannot be put back (display only). Read the text and type it again if needed.",
        ListLead = "Unsaved input. Select a draft and press Open to put it back into its record (it is not saved until you save). Drafts are deleted when they expire.",
        ListEmpty = "There are no unsaved drafts.",
        ListReadFailed = "The drafts could not be read. They have not been lost. Open the list again later.",
        StateExisting = "Existing",
        DiscardedSuffix = " (discarded)",
        TargetNotShown = "(cannot be shown)",
        Discarded = "The draft was discarded.",
        DiscardFailedShownAgain = "The draft could not be discarded. It will be shown again next time.",
        DiscardFailed = "The draft could not be discarded.",
        SelectDraftToOpen = "Select a draft to open.",
        PersonalLoginOnly = "Drafts are available to a personal login only.",
        OpenListTabFirst = "Open the list tab first, then press it again.",
        ListOpenFailedReload = "The drafts could not be opened. Reload the page and try again.",
        ListOpenFailedContact = "The drafts could not be opened. Try again; if this continues, contact your administrator.",
        DraftCannotOpen = "This draft cannot be opened (it has expired or belongs to another user).",
        DraftUnreadable = "This draft cannot be read.",
        DraftTypeUnknown = "The record type of this draft is not known.",
        RecordNotFound = "The original record was not found (it was deleted or cannot be shown).",
        RecordNotVisible = "This record cannot be shown.",
        NothingSelected = "No field is selected.",
        ApplyScreenChanged = "This draft cannot be put back (the screen changed).",
        ApplyViewNotEditable = "This draft cannot be put back because the screen cannot be edited.",
        ApplyNotOwnLiveDraft = "This draft cannot be put back (the login changed, or it was already handled).",
        ApplyClaimLost = "This draft cannot be put back (it was restored or changed on another screen).",
        ApplyScreenHoldsDraft = "This draft cannot be put back (this screen holds unsaved input of its own).",
        ApplyGuardStopped = "A save or undo was attempted while the draft was being put back, so it was stopped. Check the values.",
        Applied = "{0} field(s) put back. Check them, then save.",
        AppliedPartly = "{0} field(s) put back. {1} could not be put back (changed since, or the referenced object is missing). Check the values.",
        BadgeUnreadable = "The drafts could not be checked. Try again.",
        FinishRowEditFirst = "Confirm (✓) or cancel (✕) the row edit first, then press it again.",
        SelectRowToOpen = "Select a row to open.",
        RowHasNoDraft = "This row has no unsaved draft.",
        OpenFailed = "The draft could not be opened. Try again."
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
