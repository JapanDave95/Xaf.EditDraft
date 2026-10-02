using System;
using System.Linq;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Blazor.SystemModule;
using DevExpress.ExpressApp.SystemModule;
using DevExpress.Persistent.Base;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// 開く / 破棄 on the rows of the 「入力控」 list; the grid shows no generic list chrome.
/// Library milestone M2: owner through the Core owner seam, "now" from the host clock, texts from EditDraftTexts.
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
            ClosePopup();
            list.OpenDraftDeferred(draftOid);   // runs on the main window after the popup has closed
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
            var owner = EditDraftServices.CurrentOwner(Application?.ServiceProvider, Application);
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
    }

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
            var owner = EditDraftServices.CurrentOwner(Application?.ServiceProvider, Application);
            EditDraftLog.Info($"[EditDraft] list toggle discarded -> {!list.IncludeDiscarded} owner={(owner.IsNone ? "none" : "set")} filter={list.ObjectTypeFilter ?? "all"}");
            if (owner.IsNone) return;
            list.IncludeDiscarded = !list.IncludeDiscarded;
            ToggleDiscardedAction.Caption = list.IncludeDiscarded ? EditDraftTexts.Of(t => t.ActionHideDiscarded) : EditDraftTexts.Of(t => t.ActionShowDiscarded);
            Application?.MainWindow?.GetController<EditDraftListControllerBlazor>()?.Fill(list, owner.Oid);
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
