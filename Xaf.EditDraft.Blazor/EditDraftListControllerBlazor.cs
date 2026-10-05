using System;
using System.Collections.Generic;
using System.Linq;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Utils;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using Microsoft.Extensions.DependencyInjection;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// 「入力控」 (owner D4): the logged-in user's own drafts of the registered generic types, opened through
/// EditDraftListBridge from wherever the host places it (every type), or from the
/// main-header action 「入力控」 FILTERED to the type whose ListView is the active tab (owner D14).
/// Opening a draft opens the EXISTING record in
/// its approved DetailView and its screen offers exactly that draft (D16: alone, even when siblings are
/// live). NEW records (owner D4 (a), 2026-10-03): a draft of a never-saved record is listed as 「新規」 and its 開く recreates
/// the record from the draft (EditDraftRecreate, Core; design docs/edit-draft-new-records-design-2026-10-02.md §4.4).
/// Every read is owner-scoped (single-model predicates in EditDraftWriter). 0.4.0-preview.1: the owner seam is asked per
/// type, the list reads each distinct owner it names and shows a row only when its stored owner is the owner named for
/// its type; the access check is EditDraftServices.MayRestore / MayRecreate (XAF security plus the host's
/// IEditDraftAccessCheck).
/// Library milestone M2: owner and access check through the Core seams, "now" from the host clock, texts from
/// EditDraftTexts, log lines through EditDraftLog (same text).
/// </summary>
public class EditDraftListControllerBlazor : WindowController
{
    private EditDraftListBridge _bridge;
    private System.Threading.SynchronizationContext _circuit;

    /// <summary>The main-header 「入力控」 action id (Model.xafml ActionDesign node of the same id).</summary>
    public const string HeaderActionId = "EditDraftListBlazor";

    private const string TabKey = "EditDraftListTabOnly";
    private const string AvailableKey = "EditDraftRestoreAvailable";

    public SimpleAction HeaderListAction { get; }

    private DevExpress.ExpressApp.Blazor.Templates.ITabbedMdiMainFormTemplate _mdi;
    private DevExpress.ExpressApp.Templates.ISupportViewChanged _watchedTab;
    private DevExpress.ExpressApp.Blazor.Templates.Toolbar.ActionControls.DxToolbarAdapter _header;

    public EditDraftListControllerBlazor()
    {
        TargetWindowType = WindowType.Main;
        HeaderListAction = new SimpleAction(this, HeaderActionId, PredefinedCategory.QuickAccess)
        {
            Caption = EditDraftListBridge.Caption,
            ToolTip = EditDraftTexts.Of(t => t.HeaderActionToolTip),
            ImageName = "Action_Open",
            // Gap G12 (2026-10-04): caption and image without a host model node (a model node can still change it).
            PaintStyle = DevExpress.ExpressApp.Templates.ActionItemPaintStyle.CaptionAndImage,
            SelectionDependencyType = SelectionDependencyType.Independent
        };
        HeaderListAction.Active[TabKey] = false;   // hidden until the first evaluation
        HeaderListAction.Execute += HeaderListAction_Execute;
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        _circuit = System.Threading.SynchronizationContext.Current;
        _bridge = Application.ServiceProvider.GetService<EditDraftListBridge>();
        _bridge?.Register(this, IsAvailable, OpenList);
        EditDraftLog.Info($"[EditDraft] list entry registered on the main window (bridge={(_bridge != null ? "yes" : "missing")})");
        Window.TemplateChanged += OnTemplateChanged;
        Window.ViewChanged += OnWindowViewChanged;
        AttachTemplate();
        UpdateHeaderAction();
    }

    protected override void OnDeactivated()
    {
        Window.TemplateChanged -= OnTemplateChanged;
        Window.ViewChanged -= OnWindowViewChanged;
        DetachTemplate();
        DetachHeader();
        _bridge?.Unregister(this);
        _bridge = null;
        base.OnDeactivated();
    }

    // ---------------------------------------------------------------------------------------
    // Header action (D14): which tab is on show, and which registered type it lists
    // ---------------------------------------------------------------------------------------

    private void OnTemplateChanged(object sender, EventArgs e) { AttachTemplate(); UpdateHeaderAction(); }
    private void OnWindowViewChanged(object sender, ViewChangedEventArgs e) => UpdateHeaderAction();

    private bool IsTabbedMdi =>
        Application?.Model?.Options is DevExpress.ExpressApp.Blazor.SystemModule.IModelOptionsBlazor o
        && o.UIType == UIType.TabbedMDI;

    private void AttachTemplate()
    {
        AttachHeader();
        var mdi = IsTabbedMdi ? Window?.Template as DevExpress.ExpressApp.Blazor.Templates.ITabbedMdiMainFormTemplate : null;
        if (ReferenceEquals(mdi, _mdi)) { WatchActiveTab(); return; }
        DetachTemplate();
        _mdi = mdi;
        if (_mdi != null)
        {
            _mdi.ActiveTemplateChanged += OnActiveTabChanged;
            _mdi.ChildTemplatesChanged += OnTabsChanged;   // a new tab can become active without ActiveTemplateChanged (KB fix-506)
        }
        WatchActiveTab();
    }

    private void DetachTemplate()
    {
        if (_mdi != null)
        {
            _mdi.ActiveTemplateChanged -= OnActiveTabChanged;
            _mdi.ChildTemplatesChanged -= OnTabsChanged;
        }
        _mdi = null;
        UnwatchTab();
    }

    private void AttachHeader()
    {
        var header = (Window?.Template as DevExpress.ExpressApp.Blazor.Templates.ApplicationWindowTemplateBase)?.HeaderToolbar;
        if (ReferenceEquals(header, _header)) return;
        DetachHeader();
        _header = header;
        if (_header != null) _header.ComponentFirstRender += OnHeaderFirstRender;
    }

    private void DetachHeader()
    {
        if (_header != null) _header.ComponentFirstRender -= OnHeaderFirstRender;
        _header = null;
    }

    // Retained fix-506 workaround: one explicit header render after the first render.
    private void OnHeaderFirstRender(object sender, EventArgs e)
    {
        try
        {
            WatchActiveTab();
            UpdateHeaderAction();
            if (!HeaderListAction.Active.ResultValue || _header == null) return;
            var update = (ISupportUpdate)_header;
            update.BeginUpdate();
            update.EndUpdate();
        }
        catch (Exception ex)
        {
            EditDraftLog.Error($"[EditDraft] header list action: header re-render failed: {ex.GetType().Name}");
        }
    }

    private void OnActiveTabChanged(object sender, DevExpress.ExpressApp.Blazor.Templates.DetailFormTemplateChangedEventArgs e) { WatchActiveTab(); UpdateHeaderAction(); }
    private void OnTabsChanged(object sender, EventArgs e) { WatchActiveTab(); UpdateHeaderAction(); }

    private void WatchActiveTab()
    {
        var tab = _mdi?.ActiveTemplate as DevExpress.ExpressApp.Templates.ISupportViewChanged;
        if (ReferenceEquals(tab, _watchedTab)) return;
        UnwatchTab();
        _watchedTab = tab;
        if (_watchedTab != null) _watchedTab.ViewChanged += OnActiveTabViewChanged;
    }

    private void UnwatchTab()
    {
        if (_watchedTab != null) _watchedTab.ViewChanged -= OnActiveTabViewChanged;
        _watchedTab = null;
    }

    private void OnActiveTabViewChanged(object sender, EventArgs e) => UpdateHeaderAction();

    /// <summary>The view on show: the active tab's in TabbedMDI (none while no tab is active), the window's own otherwise.</summary>
    private View ShownView() => IsTabbedMdi ? _mdi?.ActiveTemplate?.View : Window?.View;

    /// <summary>D14, pure: the registered policy whose ListViewIds hold <paramref name="viewId"/> exactly, or null.</summary>
    public static EditDraftTypePolicy PolicyForListView(EditDraftRegistry registry, string viewId)
    {
        if (registry == null || string.IsNullOrEmpty(viewId)) return null;
        return registry.All.FirstOrDefault(p => EditDraftTypePolicy.IsGeneric(p) && p.ListViewIds.Contains(viewId));
    }

    private EditDraftTypePolicy ShownPolicy() => PolicyForListView(EditDraftServices.Registry(Application?.ServiceProvider), ShownView()?.Id);

    /// <summary>Gap G11: the host chose to show the header action on every view (EditDraftBlazorOptions).</summary>
    private bool OnEveryView => EditDraftBlazorOptions.HeaderActionOnEveryViewIn(Application?.ServiceProvider);

    private void UpdateHeaderAction()
    {
        var policy = ShownPolicy();
        var onTab = policy != null || OnEveryView;
        bool? available = onTab ? _bridge != null && _bridge.IsAvailable : null;
        HeaderListAction.Active[TabKey] = onTab;
        HeaderListAction.Active[AvailableKey] = available ?? true;
        EditDraftLog.Info($"[EditDraft] header list action view={ShownView()?.Id ?? "none"} policy={policy?.PolicyId ?? "none"} " +
                          $"available={(available.HasValue ? available.Value.ToString() : "notEvaluated")} active={HeaderListAction.Active.ResultValue} tabbed={IsTabbedMdi}/{_mdi != null}");
    }

    private void HeaderListAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var policy = ShownPolicy();   // re-checked at the click, not taken from the last evaluation
        var everyView = OnEveryView;
        EditDraftLog.Info($"[EditDraft] header list action clicked (main header → 入力控) policy={policy?.PolicyId ?? "none"} everyView={everyView}");
        if (policy == null && !everyView)
        {
            UpdateHeaderAction();
            Message(EditDraftTexts.Of(t => t.OpenListTabFirst), InformationType.Warning);
            return;
        }
        if (_bridge == null || !_bridge.IsAvailable)
        {
            Message(EditDraftTexts.Of(t => t.ListOpenFailedReload), InformationType.Warning);
            UpdateHeaderAction();
            return;
        }
        try
        {
            // Gap G11: off a registered ListView (host option HeaderActionOnEveryView) the list shows every registered type.
            if (policy == null) ShowList(includeDiscarded: false, objectTypeFilter: null);
            else ShowList(includeDiscarded: false, objectTypeFilter: policy.TypeName);
        }
        catch (Exception ex) { ReportFailure("header list open", ex); }
    }

    /// <summary>The entry is shown while dbo.EditDraft exists (switches do not hide drafts inside their seven days).</summary>
    private bool IsAvailable() => EditDraftSwitch.IsRestoreAvailable(Application?.ServiceProvider);

    // ---------------------------------------------------------------------------------------
    // The list
    // ---------------------------------------------------------------------------------------

    /// <summary>The gear entry: every registered type, unfiltered.</summary>
    public void OpenList()
    {
        EditDraftLog.Info("[EditDraft] list entry clicked (歯車 → 復元 → 入力控)");
        try { ShowList(includeDiscarded: false, objectTypeFilter: null); }
        catch (Exception ex) { ReportFailure("list open", ex); }
    }

    internal void OpenDraftDeferred(Guid draftOid, string objectType)
    {
        EditDraftLog.Info($"[EditDraft] list 開く queued for {draftOid}");
        void Run()
        {
            try { OpenDraft(draftOid, objectType); }
            catch (Exception ex) { ReportFailure("list 開く", ex); }
        }
        var context = System.Threading.SynchronizationContext.Current ?? _circuit;
        if (context != null) context.Post(_ => Run(), null); else Run();
    }

    private void ReportFailure(string what, Exception ex)
    {
        var site = ex.TargetSite == null ? "?" : $"{ex.TargetSite.DeclaringType?.Name}.{ex.TargetSite.Name}";
        EditDraftLog.Error($"[EditDraft] {what} failed: {ex.GetType().Name} at {site}");
        Message(EditDraftTexts.Of(t => t.ListOpenFailedContact), InformationType.Error);
    }

    /// <summary>"Now" from the host clock (TimeProvider in DI; the system clock otherwise), local wall time.</summary>
    private DateTime Now() => EditDraftClock.Now(EditDraftServices.Clock(Application?.ServiceProvider));

    internal void ShowList(bool includeDiscarded, string objectTypeFilter)
    {
        var list = new EditDraftList { IncludeDiscarded = includeDiscarded, ObjectTypeFilter = objectTypeFilter };
        if (!Fill(list))
        {
            EditDraftLog.Info($"[EditDraft] list refused: no owner (not logged in, or the owner seam named none)");
            Message(EditDraftTexts.Of(t => t.PersonalLoginOnly), InformationType.Warning);
            return;
        }
        var space = Application.CreateObjectSpace(typeof(EditDraftList));
        var view = Application.CreateDetailView(space, list);
        var policy = objectTypeFilter == null ? null : EditDraftServices.Registry(Application?.ServiceProvider).Find(objectTypeFilter);
        view.Caption = policy == null ? EditDraftTexts.Of(t => t.ListCaption) : string.Format(EditDraftTexts.Of(t => t.ListCaptionFiltered), CaptionHelper.GetClassCaption(policy.Type.FullName));
        Application.ShowViewStrategy.ShowViewInPopupWindow(view, okButtonCaption: EditDraftTexts.Of(t => t.Close), cancelButtonCaption: null);
        EditDraftLog.Info($"[EditDraft] list opened: {list.Items.Count} draft(s) shown includeDiscarded={includeDiscarded} filter={objectTypeFilter ?? "all"}");
    }

    /// <summary>
    /// Fills the list with the login's drafts (of <see cref="EditDraftList.ObjectTypeFilter"/>, or of every type). The owner
    /// seam is asked per type (0.4.0-preview.1): each distinct owner it names is read, owner in the query, and a row is shown
    /// only when its stored owner is the owner named for its type. False when the seam names no owner at all (nothing read).
    /// A 「新規」 row whose type this login may not create, or the UI does not offer creating, is shown with 開く disabled
    /// (<see cref="EditDraftList.NotOpenable"/>; type-level EditDraftCreateAccess.MayCreate).
    /// </summary>
    internal bool Fill(EditDraftList list)
    {
        var services = Application?.ServiceProvider;
        var registry = EditDraftServices.Registry(services);
        var owners = new EditDraftOwnersByType(registry, p => EditDraftServices.CurrentOwner(services, Application, p));
        var toRead = owners.ToRead(list.ObjectTypeFilter);
        if (toRead.Count == 0) return false;   // nothing changed
        list.Items.Clear();
        list.ObjectTypes.Clear();
        list.NotOpenable.Clear();
        var writer = new EditDraftWriter(Application.ServiceProvider);
        using var readSpace = writer.CreateReadSpace(out var scope);
        using (scope)
        {
            var now = Now();
            var rows = new List<EditDraftStoreBase>();
            var readFailed = false;
            foreach (var ownerOid in toRead)
            {
                rows.AddRange(writer.ListOwn(readSpace, ownerOid, list.ObjectTypeFilter, list.IncludeDiscarded, now, out var failed));
                readFailed |= failed;
            }
            rows = rows.Where(d => owners.Lists(d.OwnerUserOid, d.ObjectType)).OrderByDescending(d => d.LastCapturedOn).ToList();
            EditDraftLog.Info($"[EditDraft] list: {rows.Count} row(s) read for owner(s) {string.Join(",", toRead.Select(EditDraftCaptureController.Short))} filter={list.ObjectTypeFilter ?? "all"} readFailed={readFailed}");
            if (readFailed)
            {
                list.Lead = EditDraftTexts.Of(t => t.ListReadFailed);
                return true;
            }
            var spaces = new Dictionary<Type, IObjectSpace>();
            var creatable = new Dictionary<EditDraftTypePolicy, bool>();
            try
            {
                foreach (var d in rows)
                {
                    var policy = registry.Find(d.ObjectType);   // mapped only through the registry
                    var typeCaption = policy == null ? d.ObjectType : CaptionHelper.GetClassCaption(policy.Type.FullName);
                    list.ObjectTypes[d.Oid] = d.ObjectType;
                    if (EditDraftNewRecordRules.IsNewRecordDraft(d.TargetOid) && !MayCreateNew(policy, creatable)) list.NotOpenable.Add(d.Oid);
                    list.Items.Add(new EditDraftListItem
                    {
                        DraftOid = d.Oid,
                        TypeCaption = typeCaption,
                        Target = ResolveTarget(policy, d.TargetOid, d.ContextText, spaces),
                        Origin = EditDraftProvenance.Resolve(Application.Model, d.ViewId),   // owner B8 (wave 1b)
                        LastCapturedOn = d.LastCapturedOn,
                        EntryCount = d.EntryCount,
                        StateText = EditDraftNewRecordRules.StateText(d.TargetOid, d.DeletedOn != null),   // 「新規」 / 「既存」 (NEW records, D4)
                        ExpiresOn = d.ExpiresOn,
                        IsDiscarded = d.DeletedOn != null
                    });
                }
            }
            finally { foreach (var s in spaces.Values) s.Dispose(); }
        }
        list.Lead = list.Items.Count == 0
            ? EditDraftTexts.Of(t => t.ListEmpty)
            : EditDraftTexts.Of(t => t.ListLead);
        return true;
    }

    /// <summary>A 「新規」 row can be opened: the login may create the type and the UI offers creating it (cached per policy for one fill).</summary>
    private bool MayCreateNew(EditDraftTypePolicy policy, Dictionary<EditDraftTypePolicy, bool> cache)
    {
        if (policy == null) return false;
        if (!cache.TryGetValue(policy, out var ok)) cache[policy] = ok = EditDraftCreateAccess.MayCreate(Application, policy);
        return ok;
    }

    /// <summary>
    /// 対象: the record's display text resolved NOW through a SECURED object space, shown only when the login may restore
    /// onto the record (EditDraftServices.MayRestore), else 「（表示できません）」; for a 「新規」 row, or a type no longer
    /// registered, the stored context text (type caption + date; never a name).
    /// </summary>
    private string ResolveTarget(EditDraftTypePolicy policy, Guid targetOid, string contextText, Dictionary<Type, IObjectSpace> spaces)
    {
        var notShown = EditDraftTexts.Of(t => t.TargetNotShown);
        if (policy == null || targetOid == Guid.Empty) return string.IsNullOrEmpty(contextText) ? notShown : contextText;
        try
        {
            if (!spaces.TryGetValue(policy.Type, out var space)) spaces[policy.Type] = space = Application.CreateObjectSpace(policy.Type);
            var record = space.GetObjectByKey(policy.Type, targetOid);
            if (record == null || !EditDraftServices.MayRestore(Application, policy, record)) return notShown;
            var text = EditDraftDisplay.TextOf(record);
            return string.IsNullOrEmpty(text) ? (contextText ?? notShown) : text;
        }
        catch { return notShown; }
    }

    // ---------------------------------------------------------------------------------------
    // Open one draft: the EXISTING record, whose screen offers exactly this draft
    // ---------------------------------------------------------------------------------------

    /// <summary>開く on a list row: <paramref name="objectType"/> is the row's type, so the owner seam is asked for that type's owner.</summary>
    internal void OpenDraft(Guid draftOid, string objectType)
    {
        EditDraftLog.Info($"[EditDraft] list 開く: draft {draftOid}");
        var owner = EditDraftServices.CurrentOwnerOfType(Application?.ServiceProvider, Application, objectType);
        if (owner.IsNone) { Message(EditDraftTexts.Of(t => t.PersonalLoginOnly), InformationType.Warning); return; }

        var writer = new EditDraftWriter(Application.ServiceProvider);
        Guid targetOid;
        using (var readSpace = writer.CreateReadSpace(out var scope))
        using (scope)
        {
            var draft = writer.ReadOwn(readSpace, draftOid, owner.Oid);            // owner-scoped (single-model)
            if (draft == null || draft.HasExpired(Now()) || draft.ObjectType != objectType) { Message(EditDraftTexts.Of(t => t.DraftCannotOpen), InformationType.Warning); return; }
            if (!draft.IsPayloadReadable) { Message(EditDraftTexts.Of(t => t.DraftUnreadable), InformationType.Warning); return; }
            targetOid = draft.TargetOid;
        }
        // NEW records (owner D4 (a)): a 「新規」 row has no record to open; its 開く recreates one from the draft.
        if (EditDraftNewRecordRules.IsNewRecordDraft(targetOid)) { Recreate(draftOid, objectType, proceedWhenSavedCheckFails: false); return; }
        var policy = EditDraftServices.Registry(Application?.ServiceProvider).Find(objectType);
        if (!EditDraftTypePolicy.IsGeneric(policy)) { Message(EditDraftTexts.Of(t => t.DraftTypeUnknown), InformationType.Warning); return; }

        var os = Application.CreateObjectSpace(policy.Type);
        var target = os.GetObjectByKey(policy.Type, targetOid);
        if (target == null) { os.Dispose(); Message(EditDraftTexts.Of(t => t.RecordNotFound), InformationType.Warning); return; }
        if (!EditDraftServices.MayRestore(Application, policy, target)) { os.Dispose(); Message(EditDraftTexts.Of(t => t.RecordNotVisible), InformationType.Warning); return; }
        Application.ServiceProvider.GetService<EditDraftOfferRequests>()?.RequestOffer(target, draftOid);
        EditDraftLog.Info($"[EditDraft] list 開く {draftOid}: opening the existing {policy.TypeName} in {policy.ApprovedViewIds.First()}; its screen offers this draft");
        var view = Application.CreateDetailView(os, policy.ApprovedViewIds.First(), true, target);
        var parameters = new ShowViewParameters(view) { TargetWindow = TargetWindow.NewModalWindow };
        Application.ShowViewStrategy.ShowView(parameters, new ShowViewSource(Frame, null));
    }

    // ---------------------------------------------------------------------------------------
    // NEW records: 開く on a 「新規」 row recreates the record (design §4.4; owner D6 modal, D7 apply directly, D11 warning)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Runs the recreate (EditDraftRecreate.Run in Core decides the order of the checks) and shows its outcome: the unsaved
    /// record in a modal window with the D11 warning and, read-only, the typed entries it could not put back; or the D9
    /// read-only display, the already-saved notice, the question when the saved check failed, or a refusal message.
    /// </summary>
    internal void Recreate(Guid draftOid, string objectType, bool proceedWhenSavedCheckFails)
    {
        var circuit = System.Threading.SynchronizationContext.Current ?? _circuit;
        var host = new EditDraftRecreateHostBlazor(Application, Frame, circuit, (text, type) => Message(text, type));
        var r = EditDraftRecreate.Run(host, draftOid, objectType, proceedWhenSavedCheckFails);
        switch (r.Outcome)
        {
            case EditDraftRecreateOutcome.Created:
            {
                var notApplied = r.Fill?.NotAppliedTyped ?? Array.Empty<string>();
                var text = notApplied.Count == 0
                    ? string.Format(EditDraftTexts.Of(t => t.Recreated), r.Draft.LastCapturedOn)
                    : string.Format(EditDraftTexts.Of(t => t.RecreatedPartly), r.Draft.LastCapturedOn, notApplied.Count);
                // A cancelled save/rollback during the fill or the screen's activation is never reported as a full success (Codex diffreview D5).
                if (r.GuardViolated) text += " " + EditDraftTexts.Of(t => t.ApplyGuardStopped);
                var full = notApplied.Count == 0 && !r.GuardViolated;
                Message(text + " " + EditDraftTexts.Of(t => t.RecreateWarning), full ? InformationType.Success : InformationType.Warning, 15000);
                if (notApplied.Count > 0)
                    ShowDraftEntries(r, notApplied, EditDraftTexts.Of(t => t.NotAppliedViewCaption),
                        string.Format(EditDraftTexts.Of(t => t.NotAppliedLead), notApplied.Count), notRestorableSuffix: true, allowDiscard: false);
                return;
            }
            case EditDraftRecreateOutcome.NothingRestorable:
            {
                var unavailable = EditDraftNewRecordRules.UnavailableTyped(r.Policy, r.Payload).Select(e => e.Path).ToList();
                ShowDraftEntries(r, unavailable, EditDraftTexts.Of(t => t.ReadOnlyViewCaption),
                    string.Format(EditDraftTexts.Of(t => t.ReadOnlyLead), unavailable.Count), notRestorableSuffix: true, allowDiscard: true);
                return;
            }
            case EditDraftRecreateOutcome.AlreadySaved:
                ShowDraftEntries(r, TypedPaths(r), EditDraftTexts.Of(t => t.RecreateAlreadySavedCaption), EditDraftTexts.Of(t => t.RecreateAlreadySaved),
                    notRestorableSuffix: false, allowDiscard: true, okCaption: EditDraftTexts.Of(t => t.RecreateOpenSaved),
                    ok: shown => { if (!shown.Answered) Defer(() => OpenSaved(r.Policy, r.SavedOid)); }, cancelCaption: EditDraftTexts.Of(t => t.Close));
                return;
            case EditDraftRecreateOutcome.SavedCheckFailed:
                // Design §4.4 step 3: a failed check asks and never creates silently. Only the explicit OK creates; closing,
                // やめる and 破棄 create nothing.
                ShowDraftEntries(r, TypedPaths(r), EditDraftTexts.Of(t => t.RecreateSavedCheckFailedCaption), EditDraftTexts.Of(t => t.RecreateSavedCheckFailed),
                    notRestorableSuffix: false, allowDiscard: true, okCaption: EditDraftTexts.Of(t => t.RecreateAnyway),
                    ok: shown => { if (!shown.Answered) Defer(() => Recreate(draftOid, objectType, proceedWhenSavedCheckFails: true)); }, cancelCaption: EditDraftTexts.Of(t => t.RecreateCancel));
                return;
            case EditDraftRecreateOutcome.NoOwner: Message(EditDraftTexts.Of(t => t.PersonalLoginOnly), InformationType.Warning); return;
            case EditDraftRecreateOutcome.NotLive: Message(EditDraftTexts.Of(t => t.DraftCannotOpen), InformationType.Warning); return;
            case EditDraftRecreateOutcome.Unreadable: Message(EditDraftTexts.Of(t => t.DraftUnreadable), InformationType.Warning); return;
            case EditDraftRecreateOutcome.NotNewRecord:
            case EditDraftRecreateOutcome.TypeNotAllowed: Message(EditDraftTexts.Of(t => t.RecreateTypeNotAllowed), InformationType.Warning); return;
            case EditDraftRecreateOutcome.NotPermitted:
            case EditDraftRecreateOutcome.FilledNotPermitted: Message(EditDraftTexts.Of(t => t.RecreateNoPermission), InformationType.Warning); return;
            case EditDraftRecreateOutcome.ClaimLost: Message(EditDraftTexts.Of(t => t.RecreateClaimLost), InformationType.Warning); return;
            case EditDraftRecreateOutcome.NotAcknowledged: Message(EditDraftTexts.Of(t => t.RecreateNotAttached), InformationType.Warning); return;
            default: Message(EditDraftTexts.Of(t => t.RecreateFailed), InformationType.Error); return;
        }
    }

    private static List<string> TypedPaths(EditDraftRecreateResult r) =>
        EditDraftNewRecordRules.TypedEntries(r.Payload).Select(e => e.Path).ToList();

    /// <summary>
    /// The full typed text of <paramref name="paths"/> of the draft, read-only (EditDraftReadOnlyView, the D9 shape). 破棄 is
    /// offered only when <paramref name="allowDiscard"/> (never while a recreated screen holds the draft). OK runs
    /// <paramref name="ok"/> unless 破棄 answered the display first.
    /// </summary>
    private void ShowDraftEntries(EditDraftRecreateResult r, IReadOnlyCollection<string> paths, string caption, string lead, bool notRestorableSuffix,
        bool allowDiscard, string okCaption = null, Action<EditDraftReadOnlyView> ok = null, string cancelCaption = null)
    {
        var text = new System.Text.StringBuilder();
        foreach (var path in paths)
        {
            var entry = r.Payload?.Get(path);
            if (entry == null) continue;
            if (text.Length > 0) text.AppendLine().AppendLine();
            text.Append(entry.Caption ?? entry.Path).Append(notRestorableSuffix ? EditDraftTexts.Of(t => t.ReadOnlyEntrySuffix) : string.Empty).AppendLine();
            text.Append(string.IsNullOrEmpty(entry.ValueText) ? EditDraftTexts.Of(t => t.Empty) : entry.ValueText);   // FULL value, never Short()
        }
        var typeCaption = r.Policy == null ? r.Draft?.ObjectType : CaptionHelper.GetClassCaption(r.Policy.Type.FullName);
        var display = new EditDraftReadOnlyView
        {
            OwnerOid = r.OwnerOid,
            ObjectType = r.Draft?.ObjectType,
            Lead = lead,
            Provenance = string.Format(EditDraftTexts.Of(t => t.OfferProvenance), string.Empty, typeCaption, r.Draft?.LastCapturedOn ?? default,
                r.Draft?.EntryCount ?? 0, EditDraftProvenance.Resolve(Application?.Model, r.Draft?.ViewId)).Trim(),
            Text = text.ToString(),
            HideDiscard = !allowDiscard
        };
        if (allowDiscard && r.Draft != null) display.DraftOids.Add(r.Draft.DraftOid);
        var space = Application.CreateObjectSpace(typeof(EditDraftReadOnlyView));
        var detail = Application.CreateDetailView(space, display);
        detail.Caption = caption;
        Application.ShowViewStrategy.ShowViewInPopupWindow(detail,
            okDelegate: ok == null ? null : () => ok(display),
            cancelDelegate: null,
            okButtonCaption: okCaption ?? EditDraftTexts.Of(t => t.Close),
            cancelButtonCaption: cancelCaption);
    }

    /// <summary>Opens the record the "already saved?" check found, in its approved DetailView (modal), through a SECURED object space.</summary>
    private void OpenSaved(EditDraftTypePolicy policy, Guid oid)
    {
        var detailViewId = policy?.ApprovedViewIds?.FirstOrDefault();
        if (policy == null || oid == Guid.Empty || string.IsNullOrEmpty(detailViewId)) { Message(EditDraftTexts.Of(t => t.SavedRecordNotOpened), InformationType.Warning); return; }
        var os = Application.CreateObjectSpace(policy.Type);
        var target = os.GetObjectByKey(policy.Type, oid);
        if (target == null) { os.Dispose(); Message(EditDraftTexts.Of(t => t.SavedRecordNotOpened), InformationType.Warning); return; }
        if (!EditDraftServices.MayRestore(Application, policy, target)) { os.Dispose(); Message(EditDraftTexts.Of(t => t.RecordNotVisible), InformationType.Warning); return; }
        EditDraftLog.Info($"[EditDraft] list 開く: opening the saved {policy.TypeName} the already-saved check found");
        var view = Application.CreateDetailView(os, detailViewId, true, target);
        Application.ShowViewStrategy.ShowView(new ShowViewParameters(view) { TargetWindow = TargetWindow.NewModalWindow }, new ShowViewSource(Frame, null));
    }

    /// <summary>Runs <paramref name="action"/> on the circuit after the current popup has closed (as OpenDraftDeferred does).</summary>
    private void Defer(Action action)
    {
        void Run()
        {
            try { action(); }
            catch (Exception ex) { ReportFailure("recreate follow-up", ex); }
        }
        var context = System.Threading.SynchronizationContext.Current ?? _circuit;
        if (context != null) context.Post(_ => Run(), null); else Run();
    }

    internal void Message(string text, InformationType type) => Message(text, type, 8000);

    private void Message(string text, InformationType type, int duration)
    {
        EditDraftLog.Info($"[EditDraft] list message ({type}): {text}");
        try { Application?.ShowViewStrategy?.ShowMessage(text, type, duration); } catch { }
    }
}
