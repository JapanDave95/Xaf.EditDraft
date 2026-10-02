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
/// The 入力控 restore popup for the generic (non-chart) types. A SEPARATE class from the application's chart
/// plan on purpose: the chart popup controller targets that class and routes 破棄 to the F2 author and the
/// chart writer; this plan never activates it. The rows are the library's <see cref="EditDraftRestoreItem"/>
/// (milestone M2; before M2 they reused the chart row type), ticked by EditDraftRestoreItemListControllerBlazor.
/// Non-persistent; the controller re-checks owner, revision, permissions and the record's access rule before
/// anything is applied.
///
/// Library milestone M2 (owner decision D9, plain XAF): the base class is DevExpress's
/// <see cref="NonPersistentBaseObject"/> and the text lines use XAF's own string property editor, read-only
/// (no third-party base class or editor alias; before and after: docs/xaf-editdraft-library-m2-2026-10-02.md §3).
///
/// Owner D16: ONE plan may hold the rows of SEVERAL live drafts of the same record, newest first; each
/// row knows its source draft (<see cref="Sources"/>) and the provenance lines are listed in
/// <see cref="Provenance"/> with the ①②③ marker that prefixes the row's 項目.
/// </summary>
[DomainComponent]
[XafDisplayName("保存されていない入力")]
[ImageName("BO_Note")]
public class EditDraftRestorePlan : NonPersistentBaseObject
{
    public EditDraftRestorePlan() { Items = new BindingList<EditDraftRestoreItem>(); }

    [XafDisplayName("")] [ModelDefault("AllowEdit", "False")] [ModelDefault("RowCount", "3")]
    public string Lead { get; set; }

    [XafDisplayName("")] [ModelDefault("AllowEdit", "False")] [ModelDefault("RowCount", "3")]
    public string Provenance { get; set; }

    [XafDisplayName("")] [ModelDefault("AllowEdit", "False")] [ModelDefault("RowCount", "2")]
    [Appearance("EditDraftRestorePlan_HideEmptyConflictBanner",
        AppearanceItemType = "ViewItem", TargetItems = nameof(ConflictBanner),
        Criteria = "IsNullOrEmpty(ConflictBanner)", Context = "DetailView",
        Visibility = DevExpress.ExpressApp.Editors.ViewItemVisibility.Hide)]
    public string ConflictBanner { get; set; }

    [XafDisplayName("内容")]
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
[XafDisplayName("戻せない入力")]
[ImageName("BO_Note")]
public class EditDraftReadOnlyView : NonPersistentBaseObject
{
    [XafDisplayName("")] [ModelDefault("AllowEdit", "False")] [ModelDefault("RowCount", "2")]
    public string Lead { get; set; }

    [XafDisplayName("")] [ModelDefault("AllowEdit", "False")] [ModelDefault("RowCount", "3")]
    public string Provenance { get; set; }

    /// <summary>Every 戻せません entry: 項目, then the full typed text on its own lines. Never shortened.</summary>
    [XafDisplayName("入力した内容（表示のみ）")]
    [Size(SizeAttribute.Unlimited)]
    [ModelDefault("AllowEdit", "False")]
    [ModelDefault("RowCount", "14")]
    public string Text { get; set; }

    [Browsable(false)] public List<Guid> DraftOids { get; } = new();
    [Browsable(false)] public Guid OwnerOid { get; set; }
    [Browsable(false)] public bool Answered { get; set; }
}

/// <summary>One row of the 「入力控」 list (the login's own drafts only).</summary>
[DomainComponent]
[XafDisplayName("入力控")]
public class EditDraftListItem : NonPersistentBaseObject
{
    [XafDisplayName("画面（種類）")] [ModelDefault("AllowEdit", "False")] public string TypeCaption { get; set; }
    [XafDisplayName("対象")] [ModelDefault("AllowEdit", "False")] public string Target { get; set; }
    /// <summary>Owner B8 (wave 1b): 一覧から／詳細から + the caption of the view the draft was typed in.</summary>
    [XafDisplayName("由来")] [ModelDefault("AllowEdit", "False")] public string Origin { get; set; }
    [XafDisplayName("入力日時")] [ModelDefault("AllowEdit", "False")] [ModelDefault("DisplayFormat", "{0:yyyy/MM/dd HH:mm}")] public DateTime LastCapturedOn { get; set; }
    [XafDisplayName("項目数")] [ModelDefault("AllowEdit", "False")] public int EntryCount { get; set; }
    [XafDisplayName("状態")] [ModelDefault("AllowEdit", "False")] public string StateText { get; set; }
    [XafDisplayName("保存期限")] [ModelDefault("AllowEdit", "False")] [ModelDefault("DisplayFormat", "{0:yyyy/MM/dd HH:mm}")] public DateTime ExpiresOn { get; set; }

    [Browsable(false)] public Guid DraftOid { get; set; }
    [Browsable(false)] public bool IsDiscarded { get; set; }
}

/// <summary>The 「入力控」 popup: the list, the optional type filter (D14) and whether discarded drafts are included.</summary>
[DomainComponent]
[XafDisplayName("入力控")]
[ImageName("BO_Note")]
public class EditDraftList : NonPersistentBaseObject
{
    public EditDraftList() { Items = new BindingList<EditDraftListItem>(); }

    [XafDisplayName("")] [ModelDefault("AllowEdit", "False")] [ModelDefault("RowCount", "2")]
    public string Lead { get; set; }

    [XafDisplayName("入力控")]
    public BindingList<EditDraftListItem> Items { get; }

    [Browsable(false)] public bool IncludeDiscarded { get; set; }

    /// <summary>D14: the ObjectType (CLR class name) the list is filtered to; null = every registered type (the gear entry).</summary>
    [Browsable(false)] public string ObjectTypeFilter { get; set; }
}
