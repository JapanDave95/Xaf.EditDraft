using System;
using System.Linq;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.SystemModule;
using DevExpress.Persistent.Base;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// 破棄 and すべて選択 on the generic 入力控 restore popup (the OK/Cancel pair carries はい/あとで).
/// Targets EditDraftRestorePlan ONLY, so the chart popup controller (TenantChartDraftRestorePlan, F2
/// author, chart writer) never acts on a generic plan and this one never acts on a chart plan: exactly
/// one handler per plan (design §1.2). 破棄 soft-discards EVERY draft shown (D16), owner-scoped in the
/// writer's statement, through the generic store and owner.
/// Library milestone M2: owner through the Core owner seam, "now" from the host clock, texts from EditDraftTexts,
/// log lines through EditDraftLog (same text).
/// </summary>
public class EditDraftRestorePopupControllerBlazor : ObjectViewController<DetailView, EditDraftRestorePlan>
{
    public SimpleAction DiscardAction { get; }
    public SimpleAction SelectAllAction { get; }

    public EditDraftRestorePopupControllerBlazor()
    {
        DiscardAction = new SimpleAction(this, "EditDraftDiscard", PredefinedCategory.PopupActions)
        {
            Caption = EditDraftTexts.Of(t => t.ActionDiscard),
            ImageName = "Action_Delete",
            ConfirmationMessage = EditDraftTexts.Of(t => t.ConfirmDiscardShown)
        };
        DiscardAction.Execute += DiscardAction_Execute;

        SelectAllAction = new SimpleAction(this, "EditDraftSelectAll", PredefinedCategory.PopupActions)
        {
            Caption = EditDraftTexts.Of(t => t.ActionSelectAll),
            ImageName = "Action_Grid_SelectAll"
        };
        SelectAllAction.Execute += (s, e) =>
        {
            var plan = ViewCurrentObject;
            if (plan == null) return;
            var selectable = plan.Items.Where(i => i.Selectable).ToList();
            var allOn = selectable.Count > 0 && selectable.All(i => i.Selected);
            foreach (var i in selectable) i.Selected = !allOn;
            View?.Refresh();
        };
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        var diagnostics = Frame?.GetController<DiagnosticInfoController>();
        if (diagnostics != null) diagnostics.Active["EditDraftRestore"] = false;
        var modifications = Frame?.GetController<ModificationsController>();
        if (modifications != null) modifications.Active["EditDraftRestore"] = false;
    }

    private void DiscardAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var plan = ViewCurrentObject;
        if (plan == null) return;
        var ok = 0;
        try
        {
            var owner = EditDraftServices.CurrentOwner(Application?.ServiceProvider, Application);
            if (!owner.IsNone && owner.Oid == plan.OwnerOid)
            {
                var writer = new EditDraftWriter(Application.ServiceProvider);
                var now = EditDraftClock.Now(EditDraftServices.Clock(Application.ServiceProvider));
                foreach (var d in plan.Drafts)
                    if (writer.TrySoftDiscard(d.DraftOid, owner.Oid, now)) ok++;
            }
        }
        catch (Exception ex) { EditDraftLog.Error($"[EditDraft] discard failed: {ex.GetType().Name}"); }

        plan.Answered = true;   // the cancel path that closes the popup must not also report あとで
        if (ok > 0) EditDraftBadgeNotifier.NotifyChanged(Application?.ServiceProvider, plan.ObjectType);   // wave 1b row badges (B6)
        EditDraftLog.Info($"[EditDraft] drafts [{string.Join(",", plan.Drafts.Select(d => EditDraftCaptureController.Short(d.DraftOid)))}] discarded by user ok={ok}/{plan.Drafts.Count}");
        try
        {
            Application?.ShowViewStrategy?.ShowMessage(
                ok == plan.Drafts.Count ? EditDraftTexts.Of(t => t.Discarded) : EditDraftTexts.Of(t => t.DiscardFailedShownAgain),
                ok == plan.Drafts.Count ? InformationType.Info : InformationType.Warning, 8000);
        }
        catch { }
        Frame?.GetController<DialogController>()?.CancelAction?.DoExecute();
    }
}

/// <summary>破棄 on the D9 read-only display; nothing on it applies a value.</summary>
public class EditDraftReadOnlyViewControllerBlazor : ObjectViewController<DetailView, EditDraftReadOnlyView>
{
    public SimpleAction DiscardAction { get; }

    public EditDraftReadOnlyViewControllerBlazor()
    {
        DiscardAction = new SimpleAction(this, "EditDraftReadOnlyDiscard", PredefinedCategory.PopupActions)
        {
            Caption = EditDraftTexts.Of(t => t.ActionDiscard),
            ImageName = "Action_Delete",
            ConfirmationMessage = EditDraftTexts.Of(t => t.ConfirmDiscardOne)
        };
        DiscardAction.Execute += (s, e) =>
        {
            var view = ViewCurrentObject;
            if (view == null) return;
            var ok = 0;
            try
            {
                var owner = EditDraftServices.CurrentOwner(Application?.ServiceProvider, Application);
                if (!owner.IsNone && owner.Oid == view.OwnerOid)
                {
                    var writer = new EditDraftWriter(Application.ServiceProvider);
                    var now = EditDraftClock.Now(EditDraftServices.Clock(Application.ServiceProvider));
                    foreach (var oid in view.DraftOids) if (writer.TrySoftDiscard(oid, owner.Oid, now)) ok++;
                }
            }
            catch (Exception ex) { EditDraftLog.Error($"[EditDraft] read-only discard failed: {ex.GetType().Name}"); }
            view.Answered = true;
            if (ok > 0) EditDraftBadgeNotifier.NotifyChanged(Application?.ServiceProvider, null);   // wave 1b row badges (B6)
            var all = ok == view.DraftOids.Count;
            EditDraftLog.Info($"[EditDraft] read-only drafts [{string.Join(",", view.DraftOids.Select(EditDraftCaptureController.Short))}] discarded by user ok={ok}/{view.DraftOids.Count}");
            try { Application?.ShowViewStrategy?.ShowMessage(all ? EditDraftTexts.Of(t => t.Discarded) : EditDraftTexts.Of(t => t.DiscardFailed), all ? InformationType.Info : InformationType.Warning, 8000); } catch { }
            Frame?.GetController<DialogController>()?.AcceptAction?.DoExecute();
        };
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        var diagnostics = Frame?.GetController<DiagnosticInfoController>();
        if (diagnostics != null) diagnostics.Active["EditDraftReadOnly"] = false;
        var modifications = Frame?.GetController<ModificationsController>();
        if (modifications != null) modifications.Active["EditDraftReadOnly"] = false;
        DiscardAction.Active["EditDraftHideDiscard"] = !(ViewCurrentObject?.HideDiscard ?? false);   // NEW records: the recreated screen holds the draft
    }
}
