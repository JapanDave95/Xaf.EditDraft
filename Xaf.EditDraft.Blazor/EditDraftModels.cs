using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.ConditionalAppearance;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;
using DevExpress.Xpo;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// The 入力控 restore popup for the generic types. A SEPARATE class from any plan class a host keeps for its own
/// drafts on purpose: a host's popup controller targets its own class and routes 破棄 to its own owner and writer;
/// this plan never activates it. The rows are the library's <see cref="EditDraftRestoreItem"/>
/// (milestone M2; before M2 they reused the chart row type), ticked by EditDraftRestoreItemListControllerBlazor.
/// Non-persistent; the controller re-checks owner, revision, permissions and the record's access rule before
/// anything is applied.
///
/// Library milestone M2 (owner decision D9, plain XAF): the base class is DevExpress's
/// <see cref="NonPersistentBaseObject"/> (no third-party base class; docs/xaf-editdraft-library-m2-2026-10-02.md §3).
/// Milestone M3: the text lines use the library's own read-only caption-style editor (<see cref="EditDraftLabelEditor"/>,
/// owner ruling O-7), and the captions declared in this file are the English defaults; the captions shown come from
/// the text set in use (<see cref="EditDraftPopupCaptions"/>).
///
/// Owner D16: ONE plan may hold the rows of SEVERAL live drafts of the same record, newest first; each
/// row knows its source draft (<see cref="Sources"/>) and the provenance lines are listed in
/// <see cref="Provenance"/> with the ①②③ marker that prefixes the row's 項目.
/// </summary>
[DomainComponent]
[XafDisplayName("Unsaved input")]
[ImageName("BO_Note")]
public class EditDraftRestorePlan : NonPersistentBaseObject
{
    public EditDraftRestorePlan() { Items = new BindingList<EditDraftRestoreItem>(); }

    [XafDisplayName("")] [EditorAlias(EditDraftLabelEditor.Alias)] [ModelDefault("AllowEdit", "False")] [ModelDefault("RowCount", "3")]
    public string Lead { get; set; }

    [XafDisplayName("")] [EditorAlias(EditDraftLabelEditor.Alias)] [ModelDefault("AllowEdit", "False")] [ModelDefault("RowCount", "3")]
    public string Provenance { get; set; }

    [XafDisplayName("")] [EditorAlias(EditDraftLabelEditor.Alias)] [ModelDefault("AllowEdit", "False")] [ModelDefault("RowCount", "2")]
    [Appearance("EditDraftRestorePlan_HideEmptyConflictBanner",
        AppearanceItemType = "ViewItem", TargetItems = nameof(ConflictBanner),
        Criteria = "IsNullOrEmpty(ConflictBanner)", Context = "DetailView",
        Visibility = DevExpress.ExpressApp.Editors.ViewItemVisibility.Hide)]
    public string ConflictBanner { get; set; }

    [XafDisplayName("Fields")]
    public BindingList<EditDraftRestoreItem> Items { get; }

    /// <summary>One live draft of the record, newest first (D16).</summary>
    public sealed record Source(Guid DraftOid, int Revision, DateTime LastCapturedOn, string ViewId, string Marker);

    [Browsable(false)] public List<Source> Drafts { get; } = new();

    /// <summary>Which draft each row came from.</summary>
    [Browsable(false)] public Dictionary<EditDraftRestoreItem, Guid> Sources { get; } = new();

    [Browsable(false)] public Guid OwnerOid { get; set; }
    [Browsable(false)] public Guid TargetOid { get; set; }
    [Browsable(false)] public string ObjectType { get; set; }
    [Browsable(false)] public string PolicyId { get; set; }
    [Browsable(false)] public bool Answered { get; set; }

    /// <summary>Opened from the list for ONE draft (possibly a discarded one found by search).</summary>
    [Browsable(false)] public bool FromSearch { get; set; }

    [Browsable(false)] public int SelectedCount => Items.Count(i => i.Selected);
    [Browsable(false)] public bool HasNothingToOffer => Items.Count == 0 || Items.All(i => !i.Selectable);
}

/// <summary>
/// Owner D9: a draft whose entries are all 戻せません (shown, not restorable) is displayed read-only with
/// the FULL typed text, so the person can retype it. Nothing on this view applies anything.
/// </summary>
[DomainComponent]
[XafDisplayName("Input that cannot be restored")]
[ImageName("BO_Note")]
public class EditDraftReadOnlyView : NonPersistentBaseObject
{
    [XafDisplayName("")] [EditorAlias(EditDraftLabelEditor.Alias)] [ModelDefault("AllowEdit", "False")] [ModelDefault("RowCount", "2")]
    public string Lead { get; set; }

    [XafDisplayName("")] [EditorAlias(EditDraftLabelEditor.Alias)] [ModelDefault("AllowEdit", "False")] [ModelDefault("RowCount", "3")]
    public string Provenance { get; set; }

    /// <summary>Every 戻せません entry: 項目, then the full typed text on its own lines. Never shortened.</summary>
    [XafDisplayName("Typed text (display only)")]
    [Size(SizeAttribute.Unlimited)]
    [ModelDefault("AllowEdit", "False")]
    [ModelDefault("RowCount", "14")]
    public string Text { get; set; }

    [Browsable(false)] public List<Guid> DraftOids { get; } = new();
    [Browsable(false)] public Guid OwnerOid { get; set; }
    /// <summary>The drafts' type (CLR class name): 破棄 asks the owner seam for this type's owner.</summary>
    [Browsable(false)] public string ObjectType { get; set; }
    [Browsable(false)] public bool Answered { get; set; }

    /// <summary>NEW records: the display of entries a recreate could not put back — the draft now belongs to the recreated screen, so 破棄 is not offered.</summary>
    [Browsable(false)] public bool HideDiscard { get; set; }
}

/// <summary>One row of the 「入力控」 list (the login's own drafts only).</summary>
[DomainComponent]
[XafDisplayName("Draft")]
public class EditDraftListItem : NonPersistentBaseObject
{
    [XafDisplayName("Screen (type)")] [ModelDefault("AllowEdit", "False")] public string TypeCaption { get; set; }
    [XafDisplayName("Record")] [ModelDefault("AllowEdit", "False")] public string Target { get; set; }
    /// <summary>Owner B8 (wave 1b): 一覧から／詳細から + the caption of the view the draft was typed in.</summary>
    [XafDisplayName("Origin")] [ModelDefault("AllowEdit", "False")] public string Origin { get; set; }
    [XafDisplayName("Typed on")] [ModelDefault("AllowEdit", "False")] [ModelDefault("DisplayFormat", "{0:yyyy/MM/dd HH:mm}")] public DateTime LastCapturedOn { get; set; }
    [XafDisplayName("Fields")] [ModelDefault("AllowEdit", "False")] public int EntryCount { get; set; }
    [XafDisplayName("State")] [ModelDefault("AllowEdit", "False")] public string StateText { get; set; }
    [XafDisplayName("Kept until")] [ModelDefault("AllowEdit", "False")] [ModelDefault("DisplayFormat", "{0:yyyy/MM/dd HH:mm}")] public DateTime ExpiresOn { get; set; }

    [Browsable(false)] public Guid DraftOid { get; set; }
    [Browsable(false)] public bool IsDiscarded { get; set; }
}

/// <summary>The 「入力控」 popup: the list, the optional type filter (D14) and whether discarded drafts are included.</summary>
[DomainComponent]
[XafDisplayName("Drafts")]
[ImageName("BO_Note")]
public class EditDraftList : NonPersistentBaseObject
{
    public EditDraftList() { Items = new BindingList<EditDraftListItem>(); }

    [XafDisplayName("")] [EditorAlias(EditDraftLabelEditor.Alias)] [ModelDefault("AllowEdit", "False")] [ModelDefault("RowCount", "2")]
    public string Lead { get; set; }

    [XafDisplayName("Drafts")]
    public BindingList<EditDraftListItem> Items { get; }

    [Browsable(false)] public bool IncludeDiscarded { get; set; }

    /// <summary>D14: the ObjectType (CLR class name) the list is filtered to; null = every registered type (the gear entry).</summary>
    [Browsable(false)] public string ObjectTypeFilter { get; set; }

    /// <summary>Each listed draft's type (CLR class name) by draft Oid (0.4.0-preview.1): 開く and 破棄 ask the owner seam for that type's owner.</summary>
    [Browsable(false)] public Dictionary<Guid, string> ObjectTypes { get; } = new();

    /// <summary>
    /// The 「新規」 rows whose type this login may not create, or the UI does not offer creating (EditDraftCreateAccess.MayCreate;
    /// 0.4.0-preview.1): shown, with 開く disabled (EditDraftListItemControllerBlazor turns this set into the action's
    /// TargetObjectsCriteria). 開く re-checks everything anyway.
    /// </summary>
    [Browsable(false)] public HashSet<Guid> NotOpenable { get; } = new();
}
