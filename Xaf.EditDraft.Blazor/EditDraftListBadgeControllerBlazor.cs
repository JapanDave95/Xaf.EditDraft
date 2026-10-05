using System;
using System.Linq;
using System.Threading;
using DevExpress.Blazor;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Blazor.Components;
using DevExpress.ExpressApp.Blazor.Editors;
using DevExpress.ExpressApp.Blazor.Editors.ActionControls;
using DevExpress.ExpressApp.Blazor.Templates;
using DevExpress.ExpressApp.Editors;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// 入力控 row badge and 「入力控を開く」 on the ListViews of the generic types (wave 1b; owner D17, B2, B4, B6;
/// design §4, §7 S2). Active on a ROOT ListView whose id is in a generic policy's ListViewIds (a list that edits in
/// place and a non-editable list alike), for an owner (the owner seam's answer for the
/// policy), while dbo.EditDraft exists (switches do not hide drafts).
///
/// BADGE: a CSS class on the data row (GridModel.CustomizeElement) when the row's Oid is in a per-screen set
/// built from ONE owner-scoped metadata query (EditDraftWriter.ListOwnTargets): presence only — no value, count
/// or provenance reaches the DOM. Refreshed on activation, on tab activation and on this circuit's own capture,
/// save and 破棄 (EditDraftBadgeNotifier, B6); another circuit's changes and expiry show at the next refresh.
///
/// 開く (B2): opens the record's approved ROOT DetailView by key in a modal window, where the wave-1 offer
/// appears with every re-check. Nothing is ever written into a grid row; refused while a row is in edit.
///
/// ROW ICON (library milestone M3): XAF shows the action as a toolbar button and as an icon in every grid row; the
/// row icon now shows only on a row in the badge set and stays usable there when another row is selected
/// (EditDraftRowOpenRule, per row through ListEditorInlineActionControl.CustomizeInlineActionButton). The toolbar button
/// is unchanged: shown while the screen has badged rows, enabled for a selected row that has one.
///
/// Library milestone M2: owner and access check through the Core seams, "now" from the host clock, texts from
/// EditDraftTexts, log lines through EditDraftLog (same text). The row class is styled by the library's static web
/// asset _content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css, which the host page links.
/// </summary>
public class EditDraftListBadgeControllerBlazor : ObjectViewController<ListView, object>
{
    public const string OpenActionId = "EditDraftRowOpen";
    private const string AdmittedKey = "EditDraftBadgeList";
    private const string HasDraftsKey = "EditDraftHasDrafts";
    private const string RowKey = "EditDraftRowHasDraft";

    private readonly EditDraftBadgeSet _set = new();
    private EditDraftTypePolicy _policy;
    private EditDraftBadgeNotifier _badges;
    private DxGridListEditor _editor;
    private IGridEditingLifeCycle _lifeCycle;
    private ITabbedMdiMainFormTemplate _mdi;
    private SynchronizationContext _circuit;
    private bool _renderPending;

    public SimpleAction OpenAction { get; }

    public EditDraftListBadgeControllerBlazor()
    {
        OpenAction = new SimpleAction(this, OpenActionId, PredefinedCategory.RecordEdit)
        {
            Caption = EditDraftTexts.Of(t => t.RowOpenAction),
            ToolTip = EditDraftTexts.Of(t => t.RowOpenToolTip),
            ImageName = "Action_Open",
            SelectionDependencyType = SelectionDependencyType.RequireSingleObject
        };
        OpenAction.Active[AdmittedKey] = false;
        OpenAction.Execute += OpenAction_Execute;
        OpenAction.CustomizeControl += OpenAction_CustomizeControl;
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        _circuit = SynchronizationContext.Current;
        _policy = null;
        _set.Replace(null);
        OpenAction.Active[AdmittedKey] = false;

        var policy = EditDraftListControllerBlazor.PolicyForListView(EditDraftServices.Registry(Application?.ServiceProvider), View?.Id);
        if (!EditDraftListAdmission.IsBadgeList(policy, View?.Id, View?.ObjectTypeInfo?.Type, View?.IsRoot ?? false)) return;
        if (!EditDraftSwitch.IsRestoreAvailable(Application?.ServiceProvider))
        {
            EditDraftLog.Info($"[EditDraft] row badges off view={View.Id}: restore not available (table absent)");
            return;
        }
        if (EditDraftServices.CurrentOwner(Application?.ServiceProvider, Application, policy).IsNone)
        {
            EditDraftLog.Info($"[EditDraft] row badges off view={View.Id}: no owner (not logged in, or the owner seam named none)");
            return;
        }
        _policy = policy;
        OpenAction.Active[AdmittedKey] = true;
        _badges = Application.ServiceProvider?.GetService(typeof(EditDraftBadgeNotifier)) as EditDraftBadgeNotifier;
        if (_badges != null)
        {
            _badges.DraftWritten += OnDraftWritten;
            _badges.DraftsChanged += OnDraftsChanged;
        }
        View.SelectionChanged += View_SelectionChanged;
        WatchMdi();
        ReloadSet("activated", rerender: false);   // before the first render: no re-render needed
        HookGrid();
        PostNewRecordNotice();
    }

    protected override void OnViewControlsCreated()
    {
        base.OnViewControlsCreated();
        if (_policy != null) HookGrid();
    }

    protected override void OnDeactivated()
    {
        if (_badges != null)
        {
            _badges.DraftWritten -= OnDraftWritten;
            _badges.DraftsChanged -= OnDraftsChanged;
            _badges = null;
        }
        if (View != null) View.SelectionChanged -= View_SelectionChanged;
        UnhookGrid();
        UnwatchMdi();
        _policy = null;
        base.OnDeactivated();
    }

    // ---- grid -----------------------------------------------------------------------------------

    private void HookGrid()
    {
        if (View?.Editor is not DxGridListEditor editor || editor.GridModel == null || ReferenceEquals(editor, _editor)) return;
        UnhookGrid();
        _editor = editor;
        _editor.GridModel.CustomizeElement += Grid_CustomizeElement;
        _lifeCycle = editor as IGridEditingLifeCycle;
        if (_lifeCycle != null) _lifeCycle.EditingCompleted += Grid_EditingCompleted;
    }

    private void UnhookGrid()
    {
        if (_editor?.GridModel != null) _editor.GridModel.CustomizeElement -= Grid_CustomizeElement;
        if (_lifeCycle != null) _lifeCycle.EditingCompleted -= Grid_EditingCompleted;
        _editor = null;
        _lifeCycle = null;
    }

    private void Grid_CustomizeElement(GridCustomizeElementEventArgs e)
    {
        if (_policy == null || _set.Count == 0 || e.ElementType != GridElementType.DataRow) return;
        try
        {
            var oid = KeyOf(e.Grid.GetDataItem(e.VisibleIndex));
            if (_set.Contains(oid)) e.CssClass = EditDraftBadgeSet.AppendClass(e.CssClass);   // one hash lookup, no query
        }
        catch { /* a row that cannot be keyed carries no badge */ }
    }

    private void Grid_EditingCompleted(object sender, GridEditingLifeCycleEventArgs e)
    {
        if (!_renderPending) return;
        Post(Rerender);   // after the grid has left edit mode
    }

    private bool IsEditing()
    {
        try { return _editor?.ComponentInstance?.IsEditing() ?? false; } catch { return false; }
    }

    /// <summary>Re-renders the rows so CustomizeElement runs again; deferred while a row is in edit (never disturbs it).</summary>
    private void Rerender()
    {
        var editor = _editor;
        if (editor == null) return;
        try
        {
            if (IsEditing()) { _renderPending = true; return; }
            _renderPending = false;
            editor.ComponentInstance?.Reload();
        }
        catch (Exception ex)
        {
            EditDraftLog.Warning($"[EditDraft] row badge re-render failed: {ex.GetType().Name}");
        }
    }

    private Guid KeyOf(object row)
    {
        if (row == null) return Guid.Empty;
        if (row is BaseObject bo) return bo.Oid;
        try { return ObjectSpace?.GetKeyValue(row) is Guid g ? g : Guid.Empty; } catch { return Guid.Empty; }
    }

    // ---- the row icon (library M3: only on badged rows) -------------------------------------------

    /// <summary>
    /// XAF raises CustomizeControl for the action's inline (per-row) control as well as for its toolbar item. Only the inline
    /// control is customised, once per control: each data row then decides its own icon (EditDraftRowOpenRule). The toolbar
    /// item keeps the action's state (UpdateActionState).
    /// </summary>
    private void OpenAction_CustomizeControl(object sender, CustomizeControlEventArgs e)
    {
        if (e.Control is not ListEditorInlineActionControl inline) return;
        inline.CustomizeInlineActionButton -= Inline_CustomizeButton;
        inline.CustomizeInlineActionButton += Inline_CustomizeButton;
    }

    /// <summary>Per row, at render: the icon only on a row whose key is in the badge set (the row's own key, never the selected row).</summary>
    private void Inline_CustomizeButton(object sender, CustomizeInlineActionButtonEventArgs e)
    {
        if (e == null || e.ActionId != OpenActionId) return;
        var badged = _policy != null && _set.Count > 0 && _set.Contains(KeyOf(e.DataItem));
        EditDraftRowOpenRule.Apply(e, badged, OpenAction.Enabled, RowKey);
    }

    // ---- the set ----------------------------------------------------------------------------------

    // Codex diffreview pass 2 C4: the last ReloadSet could not read the store (the set is the previous one).
    private bool _lastReadFailed;
    private static string Unreadable => EditDraftTexts.Of(t => t.BadgeUnreadable);

    /// <summary>"Now" from the host clock (TimeProvider in DI; the system clock otherwise), local wall time.</summary>
    private DateTime Now() => EditDraftClock.Now(EditDraftServices.Clock(Application?.ServiceProvider));

    private void ReloadSet(string why, bool rerender = true)
    {
        if (_policy == null) return;
        _lastReadFailed = true;   // until a read completes below
        var owner = EditDraftServices.CurrentOwner(Application?.ServiceProvider, Application, _policy);
        if (owner.IsNone) { _lastReadFailed = false; _set.Replace(null); UpdateActionState(); if (rerender) Rerender(); return; }
        try
        {
            var writer = new EditDraftWriter(Application.ServiceProvider);
            using var readSpace = writer.CreateReadSpace(out var scope);
            using (scope)
            {
                var targets = writer.ListOwnTargets(readSpace, owner.Oid, _policy.TypeName, Now(), out var readFailed);
                if (readFailed)
                {
                    EditDraftLog.Warning($"[EditDraft] row badges not refreshed view={View?.Id} at '{why}': read failed; previous badges kept");
                    return;
                }
                _set.Replace(targets);
                _lastReadFailed = false;
            }
        }
        catch (Exception ex)
        {
            EditDraftLog.Warning($"[EditDraft] row badges not refreshed at '{why}': {ex.GetType().Name}");
            return;
        }
        EditDraftLog.Info($"[EditDraft] row badges view={View?.Id} type={_policy.TypeName} records={_set.Count} at '{why}'");   // counts only (S7)
        UpdateActionState();
        if (rerender) Rerender();
    }

    /// <summary>
    /// NEW records (owner D4 (b), D5; design docs/edit-draft-new-records-design-2026-10-02.md §4.3): once per activation of
    /// the type's list, a notice when the login has live, readable new-record draft ROWS of the type — 「新規の入力控が n 件
    /// あります。」 with the pointer to 「入力控」. Read through ListOwn (owner and type in the query): the badge projection
    /// (ListOwnTargets) cannot tell readability. Only for a type whose policy allows new records. Posted, not raised inside the
    /// activation. Counts only in the log (S7).
    /// </summary>
    private void PostNewRecordNotice()
    {
        if (_policy == null || !_policy.AllowNewRecords) return;
        Post(() =>
        {
            try
            {
                var owner = EditDraftServices.CurrentOwner(Application?.ServiceProvider, Application, _policy);
                if (owner.IsNone) return;
                var writer = new EditDraftWriter(Application.ServiceProvider);
                using var readSpace = writer.CreateReadSpace(out var scope);
                using (scope)
                {
                    var now = Now();
                    var rows = writer.ListOwn(readSpace, owner.Oid, _policy.TypeName, false, now, out var readFailed);
                    if (readFailed) { EditDraftLog.Warning($"[EditDraft] new-record notice view={View?.Id}: read failed; no notice"); return; }
                    var count = EditDraftNewRecordRules.NoticeCount(rows.Select(d => (d.TargetOid, d.PayloadSchemaVersion, d.DeletedOn != null, d.ExpiresOn)), now);
                    EditDraftLog.Info($"[EditDraft] new-record notice view={View?.Id} type={_policy.TypeName} rows={count}");
                    if (count > 0) Message(EditDraftNewRecordRules.NoticeText(count), InformationType.Info);
                }
            }
            catch (Exception ex)
            {
                EditDraftLog.Warning($"[EditDraft] new-record notice failed view={View?.Id}: {ex.GetType().Name}");
            }
        });
    }

    private void OnDraftWritten(string objectType, Guid targetOid)
    {
        if (_policy == null || objectType != _policy.TypeName) return;
        if (!_set.Add(targetOid)) return;
        UpdateActionState();
        Rerender();
    }

    private void OnDraftsChanged(string objectType)
    {
        if (_policy == null || (objectType != null && objectType != _policy.TypeName)) return;
        ReloadSet("drafts changed");
    }

    private void View_SelectionChanged(object sender, EventArgs e) => UpdateActionState();

    /// <summary>「入力控を開く」 shows while the screen has badged rows; enabled for a selected row that has one.</summary>
    private void UpdateActionState()
    {
        OpenAction.Active[HasDraftsKey] = _set.Count > 0;
        var selected = View?.SelectedObjects?.Count == 1 ? View.SelectedObjects[0] : null;
        OpenAction.Enabled[RowKey] = selected == null || _set.Contains(KeyOf(selected));
    }

    // ---- tab activation (B6) --------------------------------------------------------------------

    private bool IsTabbedMdi =>
        Application?.Model?.Options is DevExpress.ExpressApp.Blazor.SystemModule.IModelOptionsBlazor o && o.UIType == UIType.TabbedMDI;

    private void WatchMdi()
    {
        if (!IsTabbedMdi) return;
        var mdi = Application?.MainWindow?.Template as ITabbedMdiMainFormTemplate;
        if (mdi == null || ReferenceEquals(mdi, _mdi)) return;
        UnwatchMdi();
        _mdi = mdi;
        _mdi.ActiveTemplateChanged += OnActiveTabChanged;
    }

    private void UnwatchMdi()
    {
        if (_mdi != null) _mdi.ActiveTemplateChanged -= OnActiveTabChanged;
        _mdi = null;
    }

    private void OnActiveTabChanged(object sender, DetailFormTemplateChangedEventArgs e)
    {
        if (_policy == null || !ReferenceEquals(_mdi?.ActiveTemplate?.View, View)) return;
        Post(() => ReloadSet("tab activated"));
    }

    private void Post(Action action)
    {
        var ctx = SynchronizationContext.Current ?? _circuit;
        if (ctx != null) ctx.Post(_ => { if (View != null && _policy != null) action(); }, null); else action();
    }

    // ---- 開く (B2: the record's root DetailView, never the grid row) -----------------------------

    private void OpenAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        if (_policy == null) return;
        if (IsEditing())
        {
            // Never resolves an open row edit silently (design rule "apply boundary"; Codex E17).
            Message(EditDraftTexts.Of(t => t.FinishRowEditFirst), InformationType.Warning);
            return;
        }
        var row = e.CurrentObject ?? e.SelectedObjects?.OfType<object>().FirstOrDefault();
        var oid = KeyOf(row);
        if (oid == Guid.Empty) { Message(EditDraftTexts.Of(t => t.SelectRowToOpen), InformationType.Warning); return; }
        // Codex diffreview C4: the set can hold a badge whose write failed or was skipped; decide on a fresh read.
        ReloadSet("開く");
        if (!_set.Contains(oid)) { Message(_lastReadFailed ? Unreadable : EditDraftTexts.Of(t => t.RowHasNoDraft), _lastReadFailed ? InformationType.Warning : InformationType.Info); return; }
        // Codex diffreview pass 2 C4: an unreadable store is reported as such, never decided from the previous set.
        if (_lastReadFailed) { Message(Unreadable, InformationType.Warning); return; }
        try { OpenRecord(oid); }
        catch (Exception ex)
        {
            EditDraftLog.Error($"[EditDraft] row 開く failed: {ex.GetType().Name}");
            Message(EditDraftTexts.Of(t => t.OpenFailed), InformationType.Error);
        }
    }

    private void OpenRecord(Guid oid)
    {
        var owner = EditDraftServices.CurrentOwner(Application?.ServiceProvider, Application, _policy);
        if (owner.IsNone) { Message(EditDraftTexts.Of(t => t.PersonalLoginOnly), InformationType.Warning); return; }
        var detailViewId = _policy.ApprovedViewIds?.FirstOrDefault();
        if (string.IsNullOrEmpty(detailViewId)) return;
        var os = Application.CreateObjectSpace(_policy.Type);
        var target = os.GetObjectByKey(_policy.Type, oid);
        if (target == null) { os.Dispose(); Message(EditDraftTexts.Of(t => t.RecordNotFound), InformationType.Warning); return; }
        if (!EditDraftServices.MayRestore(Application, _policy, target)) { os.Dispose(); Message(EditDraftTexts.Of(t => t.RecordNotVisible), InformationType.Warning); return; }
        EditDraftLog.Info($"[EditDraft] row 開く: opening the existing {_policy.TypeName} in {detailViewId} (root, own ObjectSpace); its screen offers every live draft of the record");
        var view = Application.CreateDetailView(os, detailViewId, true, target);
        Application.ShowViewStrategy.ShowView(new ShowViewParameters(view) { TargetWindow = TargetWindow.NewModalWindow }, new ShowViewSource(Frame, null));
    }

    private void Message(string text, InformationType type)
    {
        try { Application?.ShowViewStrategy?.ShowMessage(text, type, 8000); } catch { }
    }
}
