using System;
using System.Collections.Generic;
using System.Threading;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model;

namespace Xaf.EditDraft.Core;

/// <summary>
/// 入力控 wave 1b (ListView capture, design docs/generic-edit-draft-wave1b-design-2026-10-01.md §3, owner
/// B1/B4/B7): which ListViews capture and which carry the row badge and 開く. Pure, tested without XAF.
/// </summary>
public static class EditDraftListAdmission
{
    /// <summary>
    /// List CAPTURE admission (view part): a generic policy for the exact row type, the ListView id in the
    /// policy's ListViewIds (B7: the allowlist), a ROOT list, a grid that edits rows in place, an EXISTING
    /// record. A list of ListViewIds that does not edit in place (the wave-1 main lists, B4) is refused here
    /// and gets the badge and 開く only.
    /// </summary>
    public static bool IsAdmittedList(EditDraftTypePolicy policy, string viewId, bool isRoot, bool editsInPlace, bool isNew) =>
        EditDraftTypePolicy.IsGeneric(policy) && isRoot && editsInPlace && !isNew
        && viewId != null && policy.ListViewIds != null && policy.ListViewIds.Contains(viewId);

    /// <summary>
    /// The grid edits rows in place: a DxGrid list editor with the grid editing lifecycle, ListView and editor
    /// AllowEdit, not a split (ListViewAndDetailView) list, not a light data-access mode (those refuse edit,
    /// DX ListView.cs:691). Scheduler and other editors are not DxGrid editors and are refused.
    /// </summary>
    public static bool EditsInPlace(bool isDxGridEditor, bool allowEdit, bool isSplitView, CollectionSourceDataAccessMode mode) =>
        isDxGridEditor && allowEdit && !isSplitView && !IsLightMode(mode);

    public static bool IsLightMode(CollectionSourceDataAccessMode mode) =>
        mode is CollectionSourceDataAccessMode.ServerView or CollectionSourceDataAccessMode.DataView or CollectionSourceDataAccessMode.InstantFeedbackView;

    /// <summary>Badge + 開く (B4): a ROOT ListView whose id is in a generic policy's ListViewIds and whose rows are of that exact type.</summary>
    public static bool IsBadgeList(EditDraftTypePolicy policy, string viewId, Type rowType, bool isRoot) =>
        EditDraftTypePolicy.IsGeneric(policy) && isRoot && viewId != null && rowType != null
        && rowType == policy.Type && policy.ListViewIds != null && policy.ListViewIds.Contains(viewId);
}

/// <summary>
/// The baseline of the CHANGED member when a list row's first change arrives with no context prepared at the
/// grid's EditingStarted (design §3 fallback, Codex review C3; T15). ObjectChanged's OldValue is the value
/// before THIS change. A notification that carries neither an old nor a new value (OnChanged(name), a
/// synthetic notification) does not tell the value before the edit: unknown, never a guessed "known".
/// </summary>
public static class EditDraftFallbackBaseline
{
    public static (bool Known, string Raw, string Text) ForChanged(object oldValue, object newValue)
    {
        if (oldValue == null && newValue == null) return (false, null, null);
        return (true, EditDraftCodec.RawOf(oldValue), EditDraftDisplay.TextOf(oldValue));
    }
}

/// <summary>
/// Owner B8: the origin of a draft, shown in the offer popup and the 「入力控」 list — 「一覧から」 or
/// 「詳細から」 and the caption of the view in the stored ViewId. EditorInstanceId is operational and never shown.
/// </summary>
public static class EditDraftProvenance
{
    /// <summary>「由来不明」 in the Japanese set (EditDraftTexts).</summary>
    public static string Unknown => EditDraftTexts.Of(t => t.ProvenanceUnknown);

    public static string Origin(bool? isListView, string caption)
    {
        if (isListView == null) return Unknown;
        var where = isListView.Value ? EditDraftTexts.Of(t => t.FromList) : EditDraftTexts.Of(t => t.FromDetail);
        return string.IsNullOrWhiteSpace(caption) ? where : string.Format(EditDraftTexts.Of(t => t.ProvenanceFormat), where, caption);
    }

    /// <summary>The origin text for a stored ViewId; a view id the model no longer has is <see cref="Unknown"/>.</summary>
    public static string Resolve(IModelApplication model, string viewId)
    {
        if (model == null || string.IsNullOrEmpty(viewId)) return Unknown;
        try
        {
            return model.Views?[viewId] switch
            {
                IModelListView l => Origin(true, l.Caption),
                IModelDetailView d => Origin(false, d.Caption),
                _ => Unknown
            };
        }
        catch { return Unknown; }
    }
}

/// <summary>
/// The records of one list screen that have a live draft of the logged-in user (owner D17 badge, B6).
/// Built from ONE owner-scoped metadata query (EditDraftWriter.ListOwnTargets); one hash lookup per
/// rendered row. Reveals presence only (design §7 S2): a CSS class, no value, count or provenance.
/// </summary>
public sealed class EditDraftBadgeSet
{
    public const string RowCssClass = "edit-draft-row";
    private HashSet<Guid> _oids = new();

    public int Count => _oids.Count;
    public bool Contains(Guid oid) => oid != Guid.Empty && _oids.Contains(oid);
    public bool Add(Guid oid) => oid != Guid.Empty && _oids.Add(oid);

    public void Replace(IEnumerable<Guid> oids)
    {
        var next = new HashSet<Guid>();
        if (oids != null) foreach (var o in oids) if (o != Guid.Empty) next.Add(o);
        _oids = next;
    }

    /// <summary>The row's CSS classes with the badge class added once.</summary>
    public static string AppendClass(string existing)
    {
        if (string.IsNullOrWhiteSpace(existing)) return RowCssClass;
        foreach (var c in existing.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (c == RowCssClass) return existing;
        return existing + " " + RowCssClass;
    }
}

/// <summary>
/// Per-circuit (scoped) local invalidation of the row badges (owner B6): raised ON THE CIRCUIT by this
/// circuit's own capture writes, saves and 破棄. Drafts written or discarded in another circuit, and
/// expiry, are seen at the next activation or tab activation (the documented stale window).
/// </summary>
public sealed class EditDraftBadgeNotifier
{
    /// <summary>A draft of (type, record) is being written on this circuit.</summary>
    public event Action<string, Guid> DraftWritten;

    /// <summary>Drafts of this type changed (saved, discarded); null = any type.</summary>
    public event Action<string> DraftsChanged;

    public void Written(string objectType, Guid targetOid)
    {
        try { DraftWritten?.Invoke(objectType, targetOid); } catch { }
    }

    public void Changed(string objectType)
    {
        try { DraftsChanged?.Invoke(objectType); } catch { }
    }

    public static void NotifyChanged(IServiceProvider services, string objectType)
    {
        try { (services?.GetService(typeof(EditDraftBadgeNotifier)) as EditDraftBadgeNotifier)?.Changed(objectType); } catch { }
    }

    /// <summary>From a worker thread: back to the circuit first. Nothing happens without a circuit.</summary>
    public void PostChanged(SynchronizationContext circuit, string objectType)
    {
        if (circuit == null) return;
        try { circuit.Post(_ => Changed(objectType), null); } catch { }
    }
}
