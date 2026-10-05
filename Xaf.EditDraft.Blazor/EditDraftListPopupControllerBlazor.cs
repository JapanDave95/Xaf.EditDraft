using System;
using System.Linq;
using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Blazor.SystemModule;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.SystemModule;
using DevExpress.Persistent.Base;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// 開く / 破棄 on the rows of the 「入力控」 list; the grid shows no generic list chrome.
/// Library milestone M2: owner through the Core owner seam, "now" from the host clock, texts from EditDraftTexts.
/// 0.4.0-preview.1: the owner seam is asked for the row's type (the list's <see cref="EditDraftList.ObjectTypes"/>), and 開く
/// is disabled on the rows in <see cref="EditDraftList.NotOpenable"/> through the action's TargetObjectsCriteria (XAF
/// evaluates it for the selected row and for each row's inline button).
/// </summary>
public class EditDraftListItemControllerBlazor : ObjectViewController<ListView, EditDraftListItem>
{
    public SimpleAction OpenAction { get; }
    public SimpleAction DiscardAction { get; }

    public EditDraftListItemControllerBlazor()
    {
        OpenAction = new SimpleAction(this, "EditDraftListOpen", PredefinedCategory.RecordEdit)
        {
            Caption = EditDraftTexts.Of(t => t.ActionOpen), ImageName = "Action_Open", SelectionDependencyType = SelectionDependencyType.RequireSingleObject
        };
        OpenAction.Execute += (s, e) =>
        {
            var item = (e.CurrentObject ?? e.SelectedObjects?.OfType<object>().FirstOrDefault()) as EditDraftListItem;
            EditDraftLog.Info($"[EditDraft] list 開く clicked: row={(item == null ? "none" : item.DraftOid.ToString())}");
            if (item == null)
            {
                try { Application?.ShowViewStrategy?.ShowMessage(EditDraftTexts.Of(t => t.SelectDraftToOpen), InformationType.Warning, 8000); } catch { }
                return;
            }
            var list = ListController();
            if (list == null)
            {
                EditDraftLog.Warning($"[EditDraft] list 開く {item.DraftOid}: list controller not found on the main window; nothing opened");
                return;
            }
            var draftOid = item.DraftOid;
            var objectType = ObjectTypeOf(item);
            ClosePopup();
            list.OpenDraftDeferred(draftOid, objectType);   // runs on the main window after the popup has closed
        };

        DiscardAction = new SimpleAction(this, "EditDraftListDiscard", PredefinedCategory.RecordEdit)
        {
            Caption = EditDraftTexts.Of(t => t.ActionDiscard), ImageName = "Action_Delete", SelectionDependencyType = SelectionDependencyType.RequireSingleObject,
            ConfirmationMessage = EditDraftTexts.Of(t => t.ConfirmDiscardListRow)
        };
        DiscardAction.Execute += (s, e) =>
        {
            if (e.CurrentObject is not EditDraftListItem item || item.IsDiscarded) return;
            EditDraftLog.Info($"[EditDraft] list 破棄 clicked: row={item.DraftOid}");
            var owner = EditDraftServices.CurrentOwnerOfType(Application?.ServiceProvider, Application, ObjectTypeOf(item));
            if (owner.IsNone)
            {
                try { Application?.ShowViewStrategy?.ShowMessage(EditDraftTexts.Of(t => t.PersonalLoginOnly), InformationType.Warning, 8000); } catch { }
                return;
            }
            var ok = new EditDraftWriter(Application.ServiceProvider).TrySoftDiscard(item.DraftOid, owner.Oid, EditDraftClock.Now(EditDraftServices.Clock(Application.ServiceProvider)));
            EditDraftLog.Info($"[EditDraft] list 破棄 {item.DraftOid} ok={ok}");
            item.IsDiscarded = ok || item.IsDiscarded;
            if (ok) item.StateText += EditDraftTexts.Of(t => t.DiscardedSuffix);
            if (ok) EditDraftBadgeNotifier.NotifyChanged(Application?.ServiceProvider, null);   // wave 1b row badges (B6)
            View?.Refresh();
        };
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        const string reason = "EditDraftList";
        foreach (var c in new Controller[]
                 {
                     Frame?.GetController<ListViewProcessCurrentObjectController>(), Frame?.GetController<NewObjectViewController>(),
                     Frame?.GetController<DeleteObjectsViewController>(), Frame?.GetController<ExportController>(),
                     Frame?.GetController<DiagnosticInfoController>()
                 })
            if (c != null) c.Active[reason] = false;
        if (View?.Model is IModelListViewBlazor m) m.ShowAllRows = true;
        UpdateOpenCriteria();
    }

    /// <summary>
    /// 0.4.0-preview.1: 開く is disabled on the rows of <see cref="EditDraftList.NotOpenable"/> (「新規」 rows whose type this login
    /// may not create): the action's TargetObjectsCriteria excludes their draft Oids. Called on activation and after the list
    /// is filled again (破棄済みも表示).
    /// </summary>
    internal void UpdateOpenCriteria()
    {
        var blocked = ParentList()?.NotOpenable;
        OpenAction.TargetObjectsCriteria = blocked == null || blocked.Count == 0
            ? null
            : new NotOperator(new InOperator(nameof(EditDraftListItem.DraftOid), blocked.Select(o => (object)o).ToArray())).ToString();
    }

    /// <summary>The list this ListView shows (the popup's DetailView object).</summary>
    private EditDraftList ParentList() => (Frame as NestedFrame)?.ViewItem?.View?.CurrentObject as EditDraftList;

    /// <summary>The row's draft type, as the list recorded it when it was filled; null when unknown (the owner seam then answers for no type).</summary>
    private string ObjectTypeOf(EditDraftListItem item) =>
        item != null && ParentList()?.ObjectTypes.TryGetValue(item.DraftOid, out var type) == true ? type : null;

    private EditDraftListControllerBlazor ListController() => Application?.MainWindow?.GetController<EditDraftListControllerBlazor>();

    private void ClosePopup()
    {
        try { (Frame as NestedFrame)?.ViewItem?.View?.Close(); } catch { }
    }
}

/// <summary>破棄済みも表示 (discarded drafts stay searchable until they expire); the D14 type filter is kept across the toggle.</summary>
public class EditDraftListViewControllerBlazor : ObjectViewController<DetailView, EditDraftList>
{
    public SimpleAction ToggleDiscardedAction { get; }

    public EditDraftListViewControllerBlazor()
    {
        ToggleDiscardedAction = new SimpleAction(this, "EditDraftListToggleDiscarded", PredefinedCategory.PopupActions)
        {
            Caption = EditDraftTexts.Of(t => t.ActionShowDiscarded), ImageName = "Action_Search"
        };
        ToggleDiscardedAction.Execute += (s, e) =>
        {
            var list = ViewCurrentObject;
            if (list == null) return;
            list.IncludeDiscarded = !list.IncludeDiscarded;
            // Fill asks the owner seam per type and changes nothing when it names no owner (0.4.0-preview.1).
            var filled = Application?.MainWindow?.GetController<EditDraftListControllerBlazor>()?.Fill(list) ?? false;
            EditDraftLog.Info($"[EditDraft] list toggle discarded -> {list.IncludeDiscarded} owner={(filled ? "set" : "none")} filter={list.ObjectTypeFilter ?? "all"}");
            if (!filled) { list.IncludeDiscarded = !list.IncludeDiscarded; return; }
            ToggleDiscardedAction.Caption = list.IncludeDiscarded ? EditDraftTexts.Of(t => t.ActionHideDiscarded) : EditDraftTexts.Of(t => t.ActionShowDiscarded);
            (View?.FindItem(nameof(EditDraftList.Items)) as ListPropertyEditor)?.Frame?.GetController<EditDraftListItemControllerBlazor>()?.UpdateOpenCriteria();
            View?.Refresh();
        };
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        var diagnostics = Frame?.GetController<DiagnosticInfoController>();
        if (diagnostics != null) diagnostics.Active["EditDraftList"] = false;
        var modifications = Frame?.GetController<ModificationsController>();
        if (modifications != null) modifications.Active["EditDraftList"] = false;
    }
}
