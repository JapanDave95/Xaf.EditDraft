using System;
using System.Linq;
using DevExpress.Blazor;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor.Editors;
using DevExpress.ExpressApp.Blazor.SystemModule;
using DevExpress.ExpressApp.SystemModule;
using Microsoft.AspNetCore.Components;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// The item grid of the generic 入力控 restore popup: one checkbox column that toggles on one click, members of one
/// setter group ticking TOGETHER, no list chrome (milestone M2). A host popup with its own row type keeps its own
/// controller.
/// </summary>
public class EditDraftRestoreItemListControllerBlazor : ObjectViewController<ListView, EditDraftRestoreItem>
{
    protected override void OnActivated()
    {
        base.OnActivated();
        const string reason = "EditDraftRestoreList";
        foreach (var c in new Controller[]
                 {
                     Frame?.GetController<ListViewProcessCurrentObjectController>(), Frame?.GetController<NewObjectViewController>(),
                     Frame?.GetController<DeleteObjectsViewController>(), Frame?.GetController<ExportController>(),
                     Frame?.GetController<FilterController>(), Frame?.GetController<DiagnosticInfoController>()
                 })
            if (c != null) c.Active[reason] = false;
        if (View?.Model is IModelListViewBlazor m) { m.ShowSelectionColumn = false; m.ShowAllRows = true; }
    }

    protected override void OnViewControlsCreated()
    {
        base.OnViewControlsCreated();
        if (View?.Editor is not DxGridListEditor editor || editor.GridModel == null) return;
        editor.GridModel.PagerVisible = false;
        editor.GridModel.ShowGroupPanel = false;
        editor.GridModel.FooterDisplayMode = GridFooterDisplayMode.Never;
        editor.BeginUpdate();
        try
        {
            foreach (var column in editor.GridDataColumnModels)
                if (column.FieldName == nameof(EditDraftRestoreItem.Selected))
                    column.CellDisplayTemplate = Template();
        }
        finally { editor.EndUpdate(); }
    }

    private RenderFragment<GridDataColumnCellDisplayTemplateContext> Template() => context =>
    {
        var row = context.DataItem as EditDraftRestoreItem;
        var canEdit = row != null && row.Selectable;
        return builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "style", "display:flex;align-items:center;justify-content:center;margin:-4px;padding:4px;");
            builder.OpenComponent<DxCheckBox<bool>>(2);
            builder.AddAttribute(3, "Checked", row?.Selected ?? false);
            builder.AddAttribute(4, "CheckedChanged", EventCallback.Factory.Create<bool>(this, value =>
            {
                if (!canEdit) return;
                // One setter group = one decision: the whole group follows this tick.
                var all = (View?.CollectionSource?.List ?? Array.Empty<object>()).OfType<EditDraftRestoreItem>();
                foreach (var i in all.Where(i => i == row || (row.Group != null && i.Group == row.Group)))
                    if (i.Selectable) i.Selected = value;
                EditDraftLog.Info($"[EditDraft] restore tick {row.Path} -> {value}{(row.Group != null ? " (group)" : "")}");
                if (View?.Editor is DxGridListEditorBase g) g.ComponentModel.ForceRaiseChanged();
            }));
            builder.AddAttribute(5, "ValidationEnabled", false);
            builder.AddAttribute(6, "Enabled", canEdit);
            builder.CloseComponent();
            builder.CloseElement();
        };
    };
}
