using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Model;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// One drafted member as the 入力控 restore popup shows it (milestone M2). Before M2 the generic popup reused the
/// chart popup's row type (the application's TenantChartDraftRestoreItem); this is the library's own row with the
/// same members, captions, order and visibility, so the popup grid shows the same columns. Plain XAF non-persistent
/// object (D9): <see cref="NonPersistentBaseObject"/>, the base the replaced Llamachant NPOBase derived from.
/// </summary>
[DomainComponent]
[XafDisplayName("入力控の項目")]
public class EditDraftRestoreItem : NonPersistentBaseObject
{
    private bool _selected;

    /// <summary>戻す: ticked. Raises PropertyChanged on every assignment, as the replaced base's helper did.</summary>
    [XafDisplayName("戻す")]
    public bool Selected { get => _selected; set { _selected = value; OnPropertyChanged(nameof(Selected)); } }

    [XafDisplayName("項目")] [ModelDefault("AllowEdit", "False")] public string Label { get; set; }
    [XafDisplayName("入力した内容")] [ModelDefault("AllowEdit", "False")] public string ChangeText { get; set; }
    [XafDisplayName("現在の値")] [ModelDefault("AllowEdit", "False")] public string CurrentText { get; set; }
    [XafDisplayName("状態")] [ModelDefault("AllowEdit", "False")] public string StatusText { get; set; }

    // --- identity, not shown ---
    [Browsable(false)] public string Path { get; set; }
    [Browsable(false)] public string Group { get; set; }
    [Browsable(false)] public int StatusCode { get; set; }
    [Browsable(false)] public bool Selectable { get; set; }
    [Browsable(false)] public bool SideEffect { get; set; }
    /// <summary>The record's value when the popup was built; apply requires it to be unchanged.</summary>
    [Browsable(false)] public string CurrentRaw { get; set; }
}

/// <summary>
/// The engine's neutral row (<see cref="EditDraftRestoreRow"/>) to and from the popup row. Every field is copied;
/// each mapped item is a NEW object created once per plan, so a plan that keys a dictionary by item
/// (<see cref="EditDraftRestorePlan.Sources"/>) keeps one key per row.
/// </summary>
public static class EditDraftRestoreItems
{
    public static EditDraftRestoreItem ToItem(EditDraftRestoreRow row) => row == null ? null : new EditDraftRestoreItem
    {
        Path = row.Path,
        Group = row.Group,
        Label = row.Label,
        ChangeText = row.ChangeText,
        CurrentText = row.CurrentText,
        CurrentRaw = row.CurrentRaw,
        StatusText = row.StatusText,
        StatusCode = row.StatusCode,
        Selectable = row.Selectable,
        SideEffect = row.SideEffect,
        Selected = row.Selected
    };

    public static List<EditDraftRestoreItem> ToItems(IEnumerable<EditDraftRestoreRow> rows) =>
        (rows ?? Enumerable.Empty<EditDraftRestoreRow>()).Select(ToItem).ToList();

    public static EditDraftRestoreRow ToRow(EditDraftRestoreItem item) => item == null ? null : new EditDraftRestoreRow
    {
        Path = item.Path,
        Group = item.Group,
        Label = item.Label,
        ChangeText = item.ChangeText,
        CurrentText = item.CurrentText,
        CurrentRaw = item.CurrentRaw,
        StatusText = item.StatusText,
        StatusCode = item.StatusCode,
        Selectable = item.Selectable,
        SideEffect = item.SideEffect,
        Selected = item.Selected
    };
}
