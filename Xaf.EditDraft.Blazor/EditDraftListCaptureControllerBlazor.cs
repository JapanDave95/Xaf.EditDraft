using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor.Editors;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Utils;
using DevExpress.Persistent.BaseImpl;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// 入力控 CAPTURE on ListViews (generic edit-draft engine, wave 1b; design
/// docs/generic-edit-draft-wave1b-design-2026-10-01.md §3; owner B1, B3, B7). Beside the DetailView capture,
/// which is unchanged. A row inline-edited in an admitted list becomes a draft of the SAME shape as a
/// DetailView draft (same payload, writer and dbo.EditDraft table; ViewId = the list id), restored through the
/// record's root DetailView (B2 — nothing is ever applied into a grid row).
///
/// GATE (re-checked per event and before every write): a generic policy for the list's EXACT row type, the
/// list id in the policy's ListViewIds (B7: the allowlist), a ROOT list whose DxGrid edits rows in place (not
/// split, not light access), an EXISTING record (never a new row), EditDraftCapture:Enabled AND the type key
/// AND EditDraftCapture:ListViews:Enabled, and an owner (the XAF login; a GeneralUser login never, D6).
///
/// Per ROW: one context per record Oid on the list's OWN ObjectSpace (EditDraftRowContext); the change is read
/// from ObjectChangedEventArgs.Object, never the focused/selected row (fix-032). Baseline at the grid's
/// EditingStarted (fix-529 initializing getters first), ObjectChanged.OldValue as fallback.
/// SAVE: the row ✓ commits the WHOLE list ObjectSpace (AutoCommitChanges), so Committed retires EVERY row
/// draft this screen holds (scoped to this screen's EditorInstanceId; an in-flight create ends as
/// create-then-delete). A failed commit raises no Committed and retires nothing.
/// CANCEL (B3 "Keep the draft"): row ✕, an object reload or a whole-space rollback reverts the row; the
/// reversal notifications are taken back out of the payload, the draft stays live (its pending write is
/// flushed) and the context detaches, so later typing on that row starts a fresh context and draft.
/// CLOSE: a posted write is started, never dropped.
///
/// Library milestone M2: in Xaf.EditDraft.Blazor (it uses DxGridListEditorBase and IGridEditingLifeCycle); the owner
/// through the Core owner seam, log lines through EditDraftLog (same text); it writes through the Core writer, slot,
/// mark and worker loop (internal to Core, visible to this assembly).
/// </summary>
public class EditDraftListCaptureControllerBlazor : ObjectViewController<ListView, object>
{
    private IObjectSpace _objectSpace;
    private EditDraftWriter _writer;
    private EditDraftTypePolicy _policy;
    private Guid _editorInstanceId = Guid.NewGuid();
    private readonly Dictionary<Guid, EditDraftRowContext> _contexts = new();
    private IGridEditingLifeCycle _grid;
    private SynchronizationContext _circuit;
    private EditDraftBadgeNotifier _badges;
    private bool _suppressCapture;
    private bool _stoppedAfterError;
    private bool _refusalLogged;

    /// <summary>This list screen's editing context (shared by its row contexts).</summary>
    public Guid CurrentEditorInstanceId => _editorInstanceId;

    /// <summary>True while this list is admitted for capture (switches and owner are checked per event).</summary>
    public bool IsAdmitted => _policy != null;

    protected override void OnActivated()
    {
        base.OnActivated();
        _objectSpace = ObjectSpace;
        _writer = new EditDraftWriter(Application.ServiceProvider);
        _editorInstanceId = Guid.NewGuid();
        _contexts.Clear();
        _suppressCapture = false;
        _stoppedAfterError = false;
        _refusalLogged = false;
        _sessionRow = Guid.Empty;
        _circuit = SynchronizationContext.Current;
        _badges = Application.ServiceProvider?.GetService(typeof(EditDraftBadgeNotifier)) as EditDraftBadgeNotifier;
        _policy = null;

        var policy = EditDraftServices.Registry(Application?.ServiceProvider).Find(View?.ObjectTypeInfo?.Type);
        if (!EditDraftTypePolicy.IsGeneric(policy) || View?.Id == null || !policy.ListViewIds.Contains(View.Id)) return;
        var editsInPlace = EditsInPlace();
        if (!EditDraftListAdmission.IsAdmittedList(policy, View.Id, View.IsRoot, editsInPlace, isNew: false))
        {
            EditDraftLog.Info($"[EditDraft] list capture not admitted: type={policy.TypeName} view={View.Id} isRoot={View.IsRoot} editsInPlace={editsInPlace} (row badge and 開く only)");
            return;
        }
        _policy = policy;
        _objectSpace.ObjectChanged += ObjectSpace_ObjectChanged;
        _objectSpace.Committing += ObjectSpace_Committing;
        _objectSpace.Committed += ObjectSpace_Committed;
        _objectSpace.ObjectReloaded += ObjectSpace_ObjectReloaded;
        _objectSpace.Reloaded += ObjectSpace_Reloaded;
        _grid = View.Editor as IGridEditingLifeCycle;
        if (_grid != null)
        {
            _grid.EditingStarted += Grid_EditingStarted;
            _grid.EditingCompleted += Grid_EditingCompleted;
        }
        EditDraftLog.Info($"[EditDraft] list capture ready view={View.Id} type={_policy.TypeName} batch={IsBatch()} " +
                          $"enabled={EditDraftSwitch.IsListEnabled(Application.ServiceProvider, _policy.PolicyId)} editor={Short(_editorInstanceId)}");
    }

    protected override void OnDeactivated()
    {
        if (_objectSpace != null)
        {
            _objectSpace.ObjectChanged -= ObjectSpace_ObjectChanged;
            _objectSpace.Committing -= ObjectSpace_Committing;
            _objectSpace.Committed -= ObjectSpace_Committed;
            _objectSpace.ObjectReloaded -= ObjectSpace_ObjectReloaded;
            _objectSpace.Reloaded -= ObjectSpace_Reloaded;
        }
        if (_grid != null)
        {
            _grid.EditingStarted -= Grid_EditingStarted;
            _grid.EditingCompleted -= Grid_EditingCompleted;
            _grid = null;
        }
        // Close keeps every draft: a write posted but not yet run is started now (never lost by the close).
        foreach (var ctx in _contexts.Values.ToList()) FlushPosted(ctx, "screen closed");
        _contexts.Clear();
        _objectSpace = null;
        base.OnDeactivated();
    }

    private bool EditsInPlace()
    {
        try
        {
            var editor = View?.Editor;
            var split = View?.Model?.MasterDetailMode == MasterDetailMode.ListViewAndDetailView;
            var mode = View?.CollectionSource?.DataAccessMode ?? CollectionSourceDataAccessMode.Client;
            var allowEdit = View != null && View.AllowEdit && (editor?.AllowEdit ?? false);
            return EditDraftListAdmission.EditsInPlace(editor is DxGridListEditorBase && editor is IGridEditingLifeCycle, allowEdit, split, mode);
        }
        catch { return false; }
    }

    private bool IsBatch() => View?.Editor is DxGridListEditorBase grid && grid.IsBatchEditMode;

    private bool ListEnabled() => _policy != null && EditDraftSwitch.IsListEnabled(Application?.ServiceProvider, _policy.PolicyId);

    /// <summary>The row type exactly (a subclass needs its own policy) and an existing record.</summary>
    private bool IsCapturableRow(object row) =>
        _policy != null && row != null && row.GetType() == _policy.Type && _objectSpace != null && !_objectSpace.IsNewObject(row);

    private static Guid OidOf(object row) => row is BaseObject bo ? bo.Oid : Guid.Empty;

    private void Suppressed(Action action)
    {
        var was = _suppressCapture;
        _suppressCapture = true;
        try { action(); }
        finally { _suppressCapture = was; }
    }

    // ---------------------------------------------------------------------------------------
    // Grid lifecycle: baseline at EditingStarted, cancel at EditingCompleted without a save
    // ---------------------------------------------------------------------------------------

    private void Grid_EditingStarted(object sender, GridEditingLifeCycleEventArgs e)
    {
        if (_policy == null || e == null || e.IsNew || !IsCapturableRow(e.EditedObject)) return;
        _saveAttemptThisTurn = false;
        try
        {
            var oid = OidOf(e.EditedObject);
            if (oid == Guid.Empty) return;
            var modified = _objectSpace.ModifiedObjects.Contains(e.EditedObject);   // the proxy's own test (GridIntermediateStoreProxy :111)
            // Codex diffreview pass 2 C8: the session's cancel branch is kept even when no context starts now (capture
            // off at EditingStarted), so a FALLBACK context created later in this session takes it instead of "clean".
            _sessionRow = oid;
            _sessionStartedModified = modified;
            if (_contexts.TryGetValue(oid, out var kept))
            {
                kept.StartSession();   // a failed save left the row in edit, or the same row again: baseline stays
                kept.SessionStartedModified = modified;
                return;
            }
            // Codex diffreview C6: switched off or no owner -> no context and no initializing getters (T1 "nothing").
            if (!ListEnabled() || EditDraftServices.CurrentOwner(Application?.ServiceProvider, _objectSpace).IsNone) return;
            var ctx = new EditDraftRowContext(_policy, e.EditedObject, oid) { SessionStartedModified = modified };
            Suppressed(() =>
            {
                try { ctx.PrepareAtEditingStarted(r => EditDraftMembers.RunInitializingGetters(_policy, r)); }   // BEFORE the baseline (fix-529)
                catch (Exception ex)
                {
                    EditDraftLog.Warning($"[EditDraft] list row baseline incomplete on {_policy.TypeName} ({ex.GetType().Name})");
                }
            });
            // Claude pass-2 P2-A1: this handler runs before the grid proxy's (controller activation precedes control
            // creation), so a writing getter above can dirty the row before the proxy samples it: sample again, as it will.
            ctx.SessionStartedModified = _sessionStartedModified = _objectSpace.ModifiedObjects.Contains(e.EditedObject);
            _contexts[oid] = ctx;
        }
        catch (Exception ex)
        {
            EditDraftLog.Error($"[EditDraft] list row preparation failed: {ex.GetType().Name}");
        }
    }

    private void Grid_EditingCompleted(object sender, GridEditingLifeCycleEventArgs e)
    {
        _sessionRow = Guid.Empty;   // the row's grid edit session ends here (C8)
        if (_policy == null || e == null || e.IsNew || e.EditedObject == null) return;
        // A Batch list's row completion is neither a save nor a cancel: its SaveAction commits (Committed).
        if (IsBatch()) return;
        var oid = OidOf(e.EditedObject);
        // A successful ✓ raised Committed first, which retired the context: nothing is found here then.
        if (oid == Guid.Empty || !_contexts.TryGetValue(oid, out var ctx)) return;
        if (_saveAttemptThisTurn)
        {
            // Codex diffreview C2: a Committing handler vetoed the ✓ (no Committed, no exception) and the grid then
            // completed the edit. The edits are still pending in the ObjectSpace: not a cancel; the context stays.
            _saveAttemptThisTurn = false;
            EditDraftLog.Info($"[EditDraft] list row save did not complete (commit vetoed): record={Short(oid)} context kept, draft live");
            return;
        }
        // Setter-restores (reversals) happen only on the already-modified branch (Codex diffreview C1).
        Detach(ctx, "row cancelled (✕)", revertReversals: ctx.SessionStartedModified);
    }

    // Codex diffreview C2: a commit attempt in THIS circuit turn (Committing seen, Committed not). A vetoed commit
    // completes the grid edit in the same turn; a later ✕ comes in another turn, after the posted reset.
    private bool _saveAttemptThisTurn;

    // Codex diffreview pass 2 C8: the row in its grid edit session, and whether it was modified when the session started.
    private Guid _sessionRow;
    private bool _sessionStartedModified;

    private void ObjectSpace_Committing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_policy == null || _contexts.Count == 0) return;
        _saveAttemptThisTurn = true;
        var context = SynchronizationContext.Current ?? _circuit;
        try { context?.Post(_ => _saveAttemptThisTurn = false, null); } catch { }
    }

    private void ObjectSpace_ObjectReloaded(object sender, ObjectManipulatingEventArgs e)
    {
        if (_policy == null || e?.Object == null) return;
        if (_objectSpace == null || _objectSpace.IsCommitting) return;   // a reload inside a commit is not a cancel; Committed settles it
        var oid = OidOf(e.Object);
        // A reload sets fields without per-member ObjectChanged (XPObjectSpace.cs:91-93 raises ObjectReloaded on Reset): no reversals to take out.
        if (oid != Guid.Empty && _contexts.TryGetValue(oid, out var ctx)) Detach(ctx, "row reloaded", revertReversals: false);
    }

    private void ObjectSpace_Reloaded(object sender, EventArgs e)
    {
        if (_policy == null || _contexts.Count == 0) return;
        foreach (var ctx in _contexts.Values.ToList()) Detach(ctx, "list reloaded/rolled back", revertReversals: false);
    }

    /// <summary>
    /// B3: the row's in-memory edit was thrown away without a save. On the setter-restore branch the reversal
    /// notifications of this turn are taken back out of the payload; a pending write is started now (the draft
    /// stays live and is offered next time), and the context leaves this screen: later typing on the row starts
    /// a fresh context.
    /// </summary>
    private void Detach(EditDraftRowContext ctx, string why, bool revertReversals)
    {
        if (!_contexts.Remove(ctx.TargetOid)) return;
        var reverted = 0;
        try { if (revertReversals) reverted = ctx.RevertReversals(); }
        catch (Exception ex) { EditDraftLog.Error($"[EditDraft] list row revert failed: {ex.GetType().Name}"); }
        FlushPosted(ctx, why);
        EditDraftLog.Info($"[EditDraft] list {why}: record={Short(ctx.TargetOid)} draft kept live (B3), context detached; " +
                          $"reversals taken out={reverted} entries={ctx.Payload?.Count ?? 0} fallback={ctx.FallbackPrepared}");
    }

    /// <summary>Starts a write that was posted and not yet run (detach or close). The posted callback then finds nothing to do.</summary>
    private void FlushPosted(EditDraftRowContext ctx, string why)
    {
        var posted = ctx.WritePosted;
        ctx.WritePosted = false;
        ctx.Gesture++;
        if (!posted || ctx.Payload == null || ctx.Payload.Count == 0) return;
        try
        {
            if (ListEnabled()) StartWrite(ctx, BuildSnapshot(ctx));
        }
        catch (Exception ex)
        {
            EditDraftLog.Error($"[EditDraft] list flush failed ({why}): {ex.GetType().Name}");
        }
    }

    // ---------------------------------------------------------------------------------------
    // Capture
    // ---------------------------------------------------------------------------------------

    private void ObjectSpace_ObjectChanged(object sender, ObjectChangedEventArgs e)
    {
        if (_stoppedAfterError || _suppressCapture || _policy == null) return;
        if (e == null || string.IsNullOrEmpty(e.PropertyName) || e.Object == null) return;   // an invalidation, not an edit
        var objectSpace = _objectSpace;
        if (objectSpace == null || objectSpace.IsDisposed || objectSpace.IsCommitting) return;
        if (!IsCapturableRow(e.Object)) return;
        if (!ListEnabled()) return;

        var record = e.Object;                                                          // the changed row, never the focused one
        var path = EditDraftMembers.PathFor(_policy, record, record, e.PropertyName);   // the row's own members only (B5: no paths)
        if (path == null) return;
        var oid = OidOf(record);
        if (oid == Guid.Empty) return;

        var owner = EditDraftServices.CurrentOwner(Application?.ServiceProvider, objectSpace);
        if (owner.IsNone)
        {
            if (!_refusalLogged)
            {
                _refusalLogged = true;
                EditDraftLog.Info("[EditDraft] list capture refused: no owner (not logged in, or a GeneralUser login — owner D6); nothing is stored");
            }
            return;
        }

        EditDraftRowContext ctx;
        try
        {
            if (!_contexts.TryGetValue(oid, out ctx))
            {
                // KB fix-511: XAF raises ObjectChanged with old = new = null when a cell is merely clicked (SetModified).
                // Without a prepared context such a notification carries no evidence of an edit: no context, no draft.
                if (e.OldValue == null && e.NewValue == null) return;
                ctx = new EditDraftRowContext(_policy, record, oid);
                ctx.PrepareFallback(path, e.OldValue, e.NewValue);
                ctx.SessionStartedModified = _sessionRow == oid && _sessionStartedModified;   // C8: the proxy's branch for this session
                _contexts[oid] = ctx;
                EditDraftLog.Info($"[EditDraft] list row context prepared by FALLBACK (no EditingStarted): record={Short(oid)} " +
                                  $"changed member baseline known={ctx.HasBaseline(path)}; other members read now (a cascade may already have moved them)");
            }
            if (ctx.Payload != null && owner.Oid != ctx.PayloadOwner.Oid) return;   // the login changed under an open screen
            if (ctx.Slot.NeedsFreshStart) ctx.RebuildAfterFreshStart();
            if (!ctx.Capture(path, owner)) return;
        }
        catch (Exception ex)
        {
            _stoppedAfterError = true;
            EditDraftLog.Error($"[EditDraft] list capture failed, stopped for this screen: {ex.GetType().Name}");
            return;
        }
        _badges?.Written(_policy.TypeName, oid);
        PostWrite(ctx);
    }

    // ---------------------------------------------------------------------------------------
    // Write — off the circuit, one at a time per row (the DetailView capture's slot, mark and worker loop)
    // ---------------------------------------------------------------------------------------

    private void PostWrite(EditDraftRowContext ctx)
    {
        if (ctx.WritePosted) return;
        var context = SynchronizationContext.Current ?? _circuit;
        if (context == null) return;
        ctx.WritePosted = true;
        var gesture = ++ctx.Gesture;
        var objectSpace = _objectSpace;
        context.Post(_ => RunWrite(ctx, objectSpace, gesture), null);
    }

    private void RunWrite(EditDraftRowContext ctx, IObjectSpace objectSpace, int gesture)
    {
        if (gesture != ctx.Gesture) return;                                 // detached, retired or closed: settled there
        ctx.WritePosted = false;
        if (objectSpace == null || !ReferenceEquals(objectSpace, _objectSpace) || objectSpace.IsDisposed) return;
        if (!_contexts.TryGetValue(ctx.TargetOid, out var live) || !ReferenceEquals(live, ctx)) return;
        if (ctx.Payload == null || !ListEnabled()) return;
        try { StartWrite(ctx, BuildSnapshot(ctx)); }
        catch (Exception ex) { EditDraftLog.Error($"[EditDraft] list write start failed: {ex.GetType().Name}"); }
    }

    private EditDraftCaptureController.DraftSnapshot BuildSnapshot(EditDraftRowContext ctx)
    {
        var seed = new EditDraftSeed
        {
            EditorInstanceId = _editorInstanceId,
            OwnerUserOid = ctx.PayloadOwner.Oid,
            OwnerFlag = ctx.PayloadOwner.OwnerFlag,
            ObjectType = _policy.TypeName,
            TargetOid = ctx.TargetOid,
            ScopeOid = SafeScope(ctx.Record),
            ContextText = ContextText(ctx.Record),
            ViewId = View?.Id
        };
        return ctx.BuildSnapshot(seed, EditDraftServices.Clock(Application?.ServiceProvider));
    }

    private Guid SafeScope(object record)
    {
        try { return _policy.ScopeOf?.Invoke(record) ?? Guid.Empty; } catch { return Guid.Empty; }
    }

    /// <summary>Type caption + the policy's context date, never a name (same text as a DetailView draft).</summary>
    private string ContextText(object record)
    {
        try
        {
            var caption = CaptionHelper.GetClassCaption(_policy.Type.FullName);
            if (string.IsNullOrWhiteSpace(caption)) caption = _policy.TypeName;
            return EditDraftCaptureController.ContextTextFor(caption, _policy.ContextDateOf?.Invoke(record));
        }
        catch { return _policy.TypeName; }
    }

    private void StartWrite(EditDraftRowContext ctx, EditDraftCaptureController.DraftSnapshot snapshot)
    {
        if (snapshot.Seed.OwnerUserOid == Guid.Empty || snapshot.Seed.TargetOid == Guid.Empty) return;   // never an ownerless or targetless row
        var slot = ctx.Slot;
        var mark = ctx.Mark;
        var writer = _writer;
        if (!slot.TryBeginWrite(snapshot, out var ticket)) return;
        var circuit = SynchronizationContext.Current ?? _circuit;
        var services = Application?.ServiceProvider;
        // Bound to THIS policy and the LIST switch: queued and coalesced writes re-read all three keys (T11, E3).
        var stillEnabled = EditDraftWriteGate.Bind(_policy?.PolicyId, id => EditDraftSwitch.IsListEnabled(services, id));
        Action<EditDraftCaptureController.DraftSnapshot, EditDraftCaptureController.DraftSnapshot> onFreshStart = (dropped, s) =>
        {
            var newest = dropped ?? s;
            if (circuit == null) { EditDraftCaptureController.RetiredFreshStart(slot, mark, newest, writer, stillEnabled); return; }
            try { circuit.Post(_ => ResumeAfterFreshStart(ctx, newest, writer, stillEnabled), null); } catch { }
        };
        var badges = _badges;
        var type = _policy?.TypeName;
        System.Threading.Tasks.Task.Run(() =>
        {
            DraftWriteSlot<EditDraftCaptureController.DraftSnapshot>.WriteTicket? current = ticket;
            var created = false;
            while (current is { } running)
            {
                created |= running.IsCreate;
                current = EditDraftCaptureController.RunOneWrite(slot, writer, running, mark.Raise, onFreshStart, stillEnabled);
            }
            // Codex diffreview pass 2 C5/C10/C4: a create's outcome (stored, create-then-delete after a save, failed or
            // skipped) is known only now; this circuit's badge lists of the type re-read what is stored.
            if (created) badges?.PostChanged(circuit, type);
        });
    }

    /// <summary>The row's draft was 破棄'd or expired while this context lived: never-stored members are written as a fresh draft.</summary>
    private void ResumeAfterFreshStart(EditDraftRowContext ctx, EditDraftCaptureController.DraftSnapshot newest, EditDraftWriter writer, Func<bool> stillEnabled)
    {
        try
        {
            if (!ctx.Slot.NeedsFreshStart) return;
            if (!_contexts.TryGetValue(ctx.TargetOid, out var live) || !ReferenceEquals(live, ctx))
            {
                EditDraftCaptureController.RetiredFreshStart(ctx.Slot, ctx.Mark, newest, writer, stillEnabled);
                return;
            }
            var kept = ctx.RebuildAfterFreshStart();
            EditDraftLog.Info($"[EditDraft] list row fresh draft after expiry/破棄: kept {kept} never-stored member(s)");
            if (ctx.Payload != null && ListEnabled()) StartWrite(ctx, BuildSnapshot(ctx));
        }
        catch (Exception ex)
        {
            EditDraftLog.Error($"[EditDraft] list fresh-start write failed: {ex.GetType().Name}");
        }
    }

    // ---------------------------------------------------------------------------------------
    // Save retires every row draft of this screen
    // ---------------------------------------------------------------------------------------

    private void ObjectSpace_Committed(object sender, EventArgs e)
    {
        // NOT Committing: a commit can still fail after Committing (KB fix-497); a failed ✓ retires nothing.
        _saveAttemptThisTurn = false;
        if (_contexts.Count == 0) return;
        var editor = _editorInstanceId;
        var retire = new List<(Guid Row, Guid Owner)>();
        var contexts = _contexts.Count;
        foreach (var ctx in _contexts.Values)
        {
            ctx.Gesture++;                       // a posted write of saved edits is dropped
            ctx.WritePosted = false;
            var own = ctx.Slot.OnSaved();        // new epoch: an in-flight create of saved edits deletes its own row
            if (own != Guid.Empty && ctx.PayloadOwner.Oid != Guid.Empty) retire.Add((own, ctx.PayloadOwner.Oid));
        }
        _contexts.Clear();
        EditDraftLog.Info($"[EditDraft] list saved: {contexts} row context(s) retired, {retire.Count} stored draft(s) to delete (the whole ObjectSpace was committed)");

        var writer = _writer;
        var badges = _badges;
        var circuit = SynchronizationContext.Current ?? _circuit;
        var type = _policy?.TypeName;
        System.Threading.Tasks.Task.Run(() =>
        {
            var rows = 0;
            foreach (var (row, owner) in retire)
            {
                try { if (writer.DeleteOwn(row, owner, editor) > 0) rows++; }
                catch (Exception ex) { EditDraftLog.Error($"[EditDraft] list delete after save failed: {ex.GetType().Name}"); }
            }
            EditDraftLog.Info($"[EditDraft] list delete after save: rows={rows} (whole drafts, unticked fields included) (off-turn)");
            badges?.PostChanged(circuit, type);
        });
    }

    private static string Short(Guid g) => EditDraftCaptureController.Short(g);
}
