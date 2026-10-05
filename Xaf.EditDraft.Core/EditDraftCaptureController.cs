using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Utils;

namespace Xaf.EditDraft.Core;

/// <summary>
/// 入力控 CAPTURE for the DetailViews of the registered generic types (generic edit-draft engine,
/// wave 1; design §1.2, §6; owner D3/D5/D6/D15).
///
/// GATE (all must hold, re-checked at every capture and before every write):
///   registered generic policy (one with a decision table) for the record's EXACT type AND View.Id in the policy's
///   approved DetailView ids AND View.IsRoot AND an EXISTING record AND EditDraftCapture:Enabled AND
///   EditDraftCapture:Types:&lt;PolicyId&gt;:Enabled AND an owner (the owner seam's answer for the policy; the library
///   default is the XAF login). Anything else: nothing is captured, not even in memory.
///   NEW records (design docs/edit-draft-new-records-design-2026-10-02.md, owner rulings 2026-10-03): a never-saved record
///   is admitted too when its policy has AllowNewRecords, and captured only while EditDraftCapture:NewRecords:Enabled is on
///   as well. Its rows are keyed by TargetOid = Guid.Empty with the screen object's Oid in the payload's prov header; its
///   first genuine edit seeds the policy's NewRecordReconstructionOrder members; the first save deletes its row.
///
/// Never commits the screen's ObjectSpace: each change is reduced to plain values on the circuit and
/// written to a separate dbo.EditDraft row OFF the circuit, one write at a time (DraftWriteSlot).
/// Values are READ from the object after the setter cascade. Baselines come from a snapshot taken when
/// the record was bound. Saving retires the whole draft; closing without saving keeps it.
///
/// INITIAL-LOAD RULE (owner ruling 2026-10-01, aligned with KB fix-529): the policy's
/// InitializingGetters (getters that write) run ONCE, under capture suppression, BEFORE the baseline is
/// taken in BindTo, so their fill is baseline and not an edit; the restore re-check runs them on its fresh
/// read. Nothing else is deferred: capture is live from activation.
///
/// Library (milestone M1): platform-agnostic, in Xaf.EditDraft.Core (owner decision D3). The registry, owner,
/// switch section, clock and log sink come from the host (EditDraftServices). Gap G15 (2026-10-04): renamed from
/// EditDraftCaptureControllerBlazor, since nothing in it is Blazor-specific.
/// </summary>
public class EditDraftCaptureController : ObjectViewController<DetailView, object>
{
    private IObjectSpace _objectSpace;
    private EditDraftWriter _writer;
    private TimeProvider _clock = TimeProvider.System;
    private EditDraftTypePolicy _policy;
    private object _record;
    private Guid _editorInstanceId = Guid.NewGuid();
    private EditDraftPayload _payload;
    private EditDraftOwnerInfo _payloadOwner;
    private DraftWriteSlot<DraftSnapshot> _slot = new();
    private Dictionary<string, (string Raw, string Text)> _baseline = new();
    private bool _writePosted;
    private int _gesture;
    private bool _stoppedAfterError;
    private bool _refusalLogged;
    private bool _suppressCapture;
    private SynchronizationContext _circuit;

    /// <summary>This screen's editing context; the restore offer skips this context's own draft.</summary>
    public Guid CurrentEditorInstanceId => _editorInstanceId;

    /// <summary>The registered policy of the bound record, or null when this screen is not admitted.</summary>
    public EditDraftTypePolicy Policy => _policy;

    /// <summary>True while a capture of this screen is admitted (policy, view, root, existing record). Switches and owner are checked per event.</summary>
    public bool IsAdmitted => _policy != null && _record != null;

    /// <summary>
    /// Client journal (design S11): the canonical raw of <paramref name="path"/> in this screen's baseline snapshot (taken when
    /// the record was bound and again after each save). False when the screen is not admitted or the snapshot lacks the member.
    /// </summary>
    public bool TryGetBaselineRaw(string path, out string raw)
    {
        raw = null;
        if (!IsAdmitted || path == null || !_baseline.TryGetValue(path, out var entry)) return false;
        raw = entry.Raw;
        return true;
    }

    /// <summary>The pure admission rule (view part), tested without XAF.</summary>
    public static bool IsAdmittedView(EditDraftTypePolicy policy, string viewId, bool isRoot, bool isNew) =>
        EditDraftTypePolicy.IsGeneric(policy) && isRoot && !isNew
        && policy.ApprovedViewIds != null && viewId != null && policy.ApprovedViewIds.Contains(viewId);

    /// <summary>
    /// The admission rule (view part) including a NEW record (owner D9): an EXISTING record exactly as
    /// <see cref="IsAdmittedView"/>; a never-saved one only when its policy has AllowNewRecords and the view part is the same
    /// (generic policy, root, approved view). The runtime key EditDraftCapture:NewRecords:Enabled is read per event, like the
    /// per-type key. The restore offer keeps using <see cref="IsAdmittedView"/> (existing records only).
    /// </summary>
    public static bool IsAdmittedViewIncludingNew(EditDraftTypePolicy policy, string viewId, bool isRoot, bool isNew) =>
        isNew ? policy != null && policy.AllowNewRecords && IsAdmittedView(policy, viewId, isRoot, false)
              : IsAdmittedView(policy, viewId, isRoot, false);

    // ---------------------------------------------------------------------------------------
    // Adopt (restore claims the draft into THIS editing context before anything is applied)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Claims a draft for THIS editing context BEFORE anything is applied, so the restored draft becomes
    /// this screen's own row — later typing updates it, and the save retires it whole. The claim is
    /// owner-scoped and revision-fenced in one statement. Refused when this screen already holds
    /// captured edits of its own. Returns the claimed revision, or 0.
    /// </summary>
    public int TryAdopt(Guid draftOid, int revision, Guid ownerOid, string payloadJson)
    {
        if (draftOid == Guid.Empty || ownerOid == Guid.Empty || _objectSpace == null || _writer == null) return 0;
        var current = EditDraftServices.CurrentOwner(Application?.ServiceProvider, _objectSpace, _policy);
        if (current.Oid != ownerOid) return 0;
        if (_payload != null || _slot.Oid != Guid.Empty || _slot.IsWriteInFlight)
        {
            EditDraftLog.Info($"[EditDraft] adopt refused for {draftOid}: this screen already holds its own unsaved draft");
            return 0;
        }
        var payload = EditDraftPayload.FromJson<EditDraftPayload>(payloadJson);
        if (payload == null) return 0;

        var newRevision = _writer.TryClaim(draftOid, revision, ownerOid, _editorInstanceId, EditDraftClock.Now(_clock));
        if (newRevision <= 0)
        {
            EditDraftLog.Info($"[EditDraft] adopt lost for {draftOid} at rev {revision}: claimed or changed elsewhere");
            return 0;
        }
        if (!_slot.Attach(draftOid, newRevision)) return 0;
        _payload = payload;
        _payloadOwner = current;
        EditDraftLog.Info($"[EditDraft] draft {draftOid} adopted by this screen at rev {newRevision}");
        return newRevision;
    }

    /// <summary>
    /// Owner D16: the restore claims EVERY contributing draft itself (one fenced statement each, under
    /// this screen's editor id) before any setter runs, then attaches the NEWEST contributing one here
    /// without claiming again. Refused when this screen already holds captured edits of its own.
    /// </summary>
    public bool TryAttachClaimed(Guid draftOid, int claimedRevision, Guid ownerOid, string payloadJson)
    {
        if (draftOid == Guid.Empty || ownerOid == Guid.Empty || claimedRevision <= 0 || _objectSpace == null) return false;
        var current = EditDraftServices.CurrentOwner(Application?.ServiceProvider, _objectSpace, _policy);
        if (current.Oid != ownerOid || _payload != null || _slot.Oid != Guid.Empty || _slot.IsWriteInFlight)
        {
            EditDraftLog.Warning($"[EditDraft] claimed draft {draftOid} not attached (owner changed or screen already holds a draft)");
            return false;
        }
        var payload = EditDraftPayload.FromJson<EditDraftPayload>(payloadJson);
        if (payload == null || !_slot.Attach(draftOid, claimedRevision)) return false;
        _payload = payload;
        _payloadOwner = current;
        EditDraftLog.Info($"[EditDraft] claimed draft {draftOid} attached to this screen at rev {claimedRevision}");
        return true;
    }

    /// <summary>
    /// NEW records (design §4.4 step 9): attaches a draft that the 「入力控」 list's recreate claimed for THIS new screen under
    /// <paramref name="claimedEditorInstanceId"/>. The screen takes that editor id as its own, so its writes supersede the
    /// claimed row and its first save deletes it (DeleteOwn is scoped to the editor). Same refusals as the overload without it.
    /// </summary>
    public bool TryAttachClaimed(Guid draftOid, int claimedRevision, Guid ownerOid, string payloadJson, Guid claimedEditorInstanceId)
    {
        if (claimedEditorInstanceId == Guid.Empty || draftOid == Guid.Empty || ownerOid == Guid.Empty || claimedRevision <= 0 || _objectSpace == null) return false;
        var current = EditDraftServices.CurrentOwner(Application?.ServiceProvider, _objectSpace, _policy);
        if (current.Oid != ownerOid || _payload != null || _slot.Oid != Guid.Empty || _slot.IsWriteInFlight)
        {
            EditDraftLog.Warning($"[EditDraft] claimed draft {draftOid} not attached to the recreated record (owner changed or screen already holds a draft)");
            return false;
        }
        var payload = EditDraftPayload.FromJson<EditDraftPayload>(payloadJson);
        if (payload == null || !_slot.Attach(draftOid, claimedRevision)) return false;
        _editorInstanceId = claimedEditorInstanceId;
        _payload = payload;
        _payloadOwner = current;
        EditDraftLog.Info($"[EditDraft] claimed draft {draftOid} attached to the recreated record at rev {claimedRevision} editor={Short(claimedEditorInstanceId)}");
        return true;
    }

    /// <summary>
    /// NEW records: the draft the recreate handed to this record (EditDraftPendingAdoptions, a Core contract registered per
    /// circuit by the host), taken once; null when there is none.
    /// </summary>
    private EditDraftPendingAdoption TakePendingAdoption(object record) =>
        (Application?.ServiceProvider?.GetService(typeof(EditDraftPendingAdoptions)) as EditDraftPendingAdoptions)?.Take(record);

    /// <summary>
    /// NEW records: attaches the taken draft with the claimed editor id and acknowledges it, so the recreate can report success.
    /// A refused attachment is not acknowledged (the recreate then closes the record unsaved).
    /// </summary>
    private void AttachPendingAdoption(EditDraftPendingAdoption adoption)
    {
        if (TryAttachClaimed(adoption.DraftOid, adoption.ClaimedRevision, adoption.OwnerOid, adoption.PayloadJson, adoption.EditorInstanceId))
            adoption.Acknowledge();
        else
            EditDraftLog.Warning($"[EditDraft] recreated record: the claimed draft {adoption.DraftOid} was not attached; the recreate closes the record");
    }

    /// <summary>Whether the bound record is still never saved; null when the object space cannot tell (then no row is written, see EditDraftNewRecordRules.Key).</summary>
    private bool? IsNewRecord()
    {
        try
        {
            var objectSpace = _objectSpace;
            if (objectSpace == null || objectSpace.IsDisposed || _record == null) return null;
            return objectSpace.IsNewObject(_record);
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------

    protected override void OnActivated()
    {
        base.OnActivated();
        _objectSpace = ObjectSpace;
        _writer = new EditDraftWriter(Application.ServiceProvider);
        _clock = EditDraftServices.Clock(Application.ServiceProvider);
        _editorInstanceId = Guid.NewGuid();
        _payload = null;
        _payloadOwner = EditDraftOwnerInfo.None;
        _slot = new DraftWriteSlot<DraftSnapshot>();
        ResetCaptureSequence();
        _writePosted = false;
        _stoppedAfterError = false;
        _refusalLogged = false;
        _suppressCapture = false;
        _circuit = SynchronizationContext.Current;

        BindTo(View.CurrentObject);
        _objectSpace.ObjectChanged += ObjectSpace_ObjectChanged;
        _objectSpace.Committed += ObjectSpace_Committed;
        View.CurrentObjectChanged += View_CurrentObjectChanged;

        if (_policy != null)
            EditDraftLog.Info($"[EditDraft] capture ready view={View?.Id} type={_policy.TypeName} isRoot={View?.IsRoot} " +
                              $"enabled={EditDraftSwitch.IsEnabled(Application.ServiceProvider, _policy.PolicyId)} " +
                              $"record={Short(RecordOid())} editor={Short(_editorInstanceId)}");
    }

    protected override void OnDeactivated()
    {
        if (_objectSpace != null)
        {
            _objectSpace.ObjectChanged -= ObjectSpace_ObjectChanged;
            _objectSpace.Committed -= ObjectSpace_Committed;
        }
        if (View != null) View.CurrentObjectChanged -= View_CurrentObjectChanged;
        FlushPostedWrite();
        _objectSpace = null;
        _writePosted = false;
        _gesture++;
        base.OnDeactivated();
    }

    /// <summary>A write posted but not yet run would be lost by the gesture bump of a close or a record change. Start it now.</summary>
    private void FlushPostedWrite()
    {
        if (!_writePosted || _payload == null || _record == null || _policy == null) return;
        try
        {
            if (EditDraftSwitch.IsEnabled(Application?.ServiceProvider, _policy.PolicyId)) StartWrite(BuildSnapshot());
        }
        catch (Exception ex)
        {
            EditDraftLog.Error($"[EditDraft] flush on close failed: {ex.GetType().Name}");
        }
    }

    private void View_CurrentObjectChanged(object sender, EventArgs e)
    {
        // A different record in the same view is a different editing context.
        FlushPostedWrite();
        _gesture++;
        _writePosted = false;
        _payload = null;
        _payloadOwner = EditDraftOwnerInfo.None;
        _slot = new DraftWriteSlot<DraftSnapshot>();
        ResetCaptureSequence();
        _editorInstanceId = Guid.NewGuid();
        BindTo(View.CurrentObject);
    }

    private void BindTo(object record)
    {
        _record = null;
        _policy = null;
        _baseline = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        if (record == null || _objectSpace == null) return;

        var policy = EditDraftServices.Registry(Application?.ServiceProvider).Find(record.GetType());
        var isNew = _objectSpace.IsNewObject(record);
        if (!IsAdmittedViewIncludingNew(policy, View?.Id, View?.IsRoot ?? false, isNew))
        {
            if (EditDraftTypePolicy.IsGeneric(policy) && !_refusalLogged)
            {
                _refusalLogged = true;
                EditDraftLog.Info($"[EditDraft] capture not admitted: type={policy.TypeName} view={View?.Id} isRoot={View?.IsRoot} isNew={isNew} (approved views: {string.Join(",", policy.ApprovedViewIds ?? new HashSet<string>())})");
            }
            return;
        }
        _policy = policy;
        _record = record;
        // NEW records (design §4.4 step 9): a recreated record attaches the draft its 開く claimed. Its getters already ran before
        // the replay (ApplyNew); running them again here could overwrite a restored value (Codex diffreview D4).
        var adoption = isNew ? TakePendingAdoption(record) : null;
        if (adoption == null) RunInitializingGetters(record);   // BEFORE the baseline (fix-529)
        _baseline = SnapshotBaseline(record);
        if (adoption != null) AttachPendingAdoption(adoption);
    }

    /// <summary>
    /// KB fix-529 (owner ruling 2026-10-01): the policy's getters that write run here, once, BEFORE the baseline and
    /// with capture suppressed (BindTo is also called from View_CurrentObjectChanged, where the handler is already
    /// subscribed), so the fill is baseline, not an edit. Nothing runs when the policy lists none.
    /// </summary>
    private void RunInitializingGetters(object record)
    {
        if (record == null || _policy == null || _policy.InitializingGetters.Count == 0) return;
        var was = _suppressCapture;
        _suppressCapture = true;
        try
        {
            var ran = EditDraftMembers.RunInitializingGetters(_policy, record);
            if (ran > 0) EditDraftLog.Info($"[EditDraft] {ran} initializing getter(s) run on {_policy.TypeName} before the baseline");
        }
        catch (Exception ex)
        {
            EditDraftLog.Warning($"[EditDraft] initializing getter failed on {_policy.TypeName} ({ex.GetType().Name}); a phantom capture of its member is possible on this screen");
        }
        finally { _suppressCapture = was; }
    }

    /// <summary>Every capturable member of the EXISTING record as it is now (the conflict baseline).</summary>
    private Dictionary<string, (string Raw, string Text)> SnapshotBaseline(object record)
    {
        var d = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        if (record == null || _policy == null) return d;
        try
        {
            foreach (var m in _policy.Members)
            {
                var v = EditDraftMembers.GetValue(record, m.Path);
                d[m.Path] = (EditDraftCodec.RawOf(v), EditDraftDisplay.TextOf(v));
            }
        }
        catch (Exception ex)
        {
            EditDraftLog.Warning($"[EditDraft] baseline snapshot incomplete: {ex.GetType().Name}");
        }
        return d;
    }

    private Guid RecordOid() => _record is DevExpress.Persistent.BaseImpl.BaseObject bo ? bo.Oid : Guid.Empty;

    // ---------------------------------------------------------------------------------------
    // Capture
    // ---------------------------------------------------------------------------------------

    private void ObjectSpace_ObjectChanged(object sender, ObjectChangedEventArgs e)
    {
        if (_stoppedAfterError || _suppressCapture || _policy == null || _record == null) return;
        if (string.IsNullOrEmpty(e.PropertyName)) return;                 // an invalidation, not an edit
        var objectSpace = _objectSpace;
        if (objectSpace == null || objectSpace.IsDisposed || objectSpace.IsCommitting) return;
        if (!EditDraftSwitch.IsEnabled(Application?.ServiceProvider, _policy.PolicyId)) return;
        if (IsNewRecord() != false && !EditDraftSwitch.IsNewRecordsEnabled(Application?.ServiceProvider, _policy.PolicyId)) return;   // NEW records: the runtime key too (D15)

        var path = EditDraftMembers.PathFor(_policy, _record, e.Object, e.PropertyName);
        if (path == null) return;

        var owner = EditDraftServices.CurrentOwner(Application?.ServiceProvider, objectSpace, _policy);
        if (owner.IsNone)
        {
            if (!_refusalLogged)
            {
                _refusalLogged = true;
                EditDraftLog.Info($"[EditDraft] capture refused: no owner (not logged in, or the owner seam named none); nothing is stored");
            }
            return;
        }
        if (_payload != null && owner.Oid != _payloadOwner.Oid) return;    // the login changed under an open screen: this context stays the previous owner's

        try
        {
            if (_slot.NeedsFreshStart) RebuildAfterFreshStart();
            var started = _payload == null;
            if (started) StartPayload(owner);
            if (!CaptureMember(path))
            {
                // A bare notification with an unchanged value must not leave an EMPTY payload behind: it
                // would block a later adoption as "this screen already holds a draft" (Codex diffreview C4).
                if (started && _payload != null && _payload.Count == 0) { _payload = null; _payloadOwner = EditDraftOwnerInfo.None; }
                return;
            }
            _capturedAt[path] = ++_captureSeq;
        }
        catch (Exception ex)
        {
            _stoppedAfterError = true;
            EditDraftLog.Error($"[EditDraft] capture failed, stopped for this screen: {ex.GetType().Name}");
            return;
        }
        // Wave 1b (B6, Codex diffreview C3): a list of this type on this circuit badges the record now.
        (Application?.ServiceProvider?.GetService(typeof(EditDraftBadgeNotifier)) as EditDraftBadgeNotifier)?.Written(_policy.TypeName, RecordOid());
        PostWrite(objectSpace);
    }

    private void StartPayload(EditDraftOwnerInfo owner)
    {
        _payloadOwner = owner;
        _payload = new EditDraftPayload { TypeName = _policy.TypeName };
    }

    /// <summary>
    /// Records one member's CURRENT value. False when nothing changed (a bare notification). The decision is the pure
    /// EditDraftCaptureRules.Capture: the genuine-edit rule and, on a NEW record, the seeding of the reconstruction members.
    /// </summary>
    private bool CaptureMember(string path) =>
        EditDraftCaptureRules.Capture(_policy, _payload, _baseline, _record, path, IsNewRecord() == true);

    // ---------------------------------------------------------------------------------------
    // Write — off the circuit, one at a time
    // ---------------------------------------------------------------------------------------

    // Public (library M1; internal since wave 1b): the ListView capture's row contexts (Xaf.EditDraft.Blazor since M2)
    // write through the same snapshot, mark and worker loop. The two methods that take the writer are internal with it
    // (owner decision O-3, milestone M2); the Blazor assembly sees them (InternalsVisibleTo).
    public sealed record DraftSnapshot(EditDraftSeed Seed, string Json, int Count, DateTime Now, long MaxSeq,
                                        IReadOnlyDictionary<string, long> CapturedAt)
    {
        /// <summary>The clock of the screen that built the snapshot (null = the system clock); a retired slot's rewrite takes its "now" from it (Codex diffreview D1).</summary>
        public TimeProvider Clock { get; init; }

        /// <summary>
        /// NEW records (D15): the predicate for EditDraftCapture:NewRecords:Enabled, bound to the slot's policy, set only on a
        /// snapshot of a never-saved record. Checked per TICKET in RunOneWrite, so a write coalesced after the save (an
        /// existing-record snapshot, no gate) never depends on the new-record key (Codex diffreview 2026-10-03 D2).
        /// </summary>
        public Func<bool> NewRecordsGate { get; init; }

        /// <summary>
        /// NEW records: the policy's NewRecordReconstructionOrder, set only on a snapshot of a never-saved record, so a retired
        /// slot's fresh start keeps the reconstruction members even when they were typed and already stored (Codex diffreview D1).
        /// </summary>
        public IReadOnlyList<string> Context { get; init; }
    }

    public sealed class StoredMark
    {
        private long _value;
        public StoredMark(long value) { _value = value; }
        public long Value => Interlocked.Read(ref _value);
        public void Raise(long seq)
        {
            long seen;
            while ((seen = Interlocked.Read(ref _value)) < seq)
                if (Interlocked.CompareExchange(ref _value, seq, seen) == seen) break;
        }
    }

    private long _captureSeq;
    private Dictionary<string, long> _capturedAt = new(StringComparer.Ordinal);
    private StoredMark _mark = new(0);

    private void ResetCaptureSequence()
    {
        _capturedAt = new Dictionary<string, long>(StringComparer.Ordinal);
        _mark = new StoredMark(_captureSeq);
    }

    /// <summary>After the slot reported that its row expired or was 破棄'd: a new payload with only the never-stored members.</summary>
    private void RebuildAfterFreshStart()
    {
        var old = _payload;
        var owner = _payloadOwner;
        var stored = _mark.Value;
        _payload = null;
        _payloadOwner = EditDraftOwnerInfo.None;
        _slot.AcknowledgeFreshStart();
        if (old == null || owner.IsNone || _record == null) return;

        // NEW records (design §4.2.5; Codex diffreview D1): the reconstruction members (seeded, or typed even when already
        // stored), any context member still missing (seeded now from the screen) and the Oid history stay with the
        // never-stored typed members.
        var isNew = IsNewRecord() == true;
        var fresh = EditDraftCaptureRules.FreshAfterGone(old, e => _capturedAt.TryGetValue(e.Path, out var at) && at > stored, out var kept,
                                                         isNew ? _policy?.NewRecordReconstructionOrder : null);
        if (fresh != null && isNew) EditDraftCaptureRules.Seed(_policy, fresh, _baseline, _record);
        if (fresh != null) { _payload = fresh; _payloadOwner = owner; }
        EditDraftLog.Info($"[EditDraft] fresh draft after expiry/破棄: kept {kept} never-stored member(s), dropped the old row's content");
    }

    /// <param name="stillEnabled">The slot's OWN switch predicate, bound to its policy at StartWrite (Codex diffreview a2 C1): a retired slot never checks the current screen's policy.</param>
    private void ResumeAfterFreshStart(DraftWriteSlot<DraftSnapshot> slot, StoredMark mark, DraftSnapshot newest, EditDraftWriter writer, Func<bool> stillEnabled)
    {
        try
        {
            if (!slot.NeedsFreshStart) return;
            if (!ReferenceEquals(slot, _slot)) { RetiredFreshStart(slot, mark, newest, writer, stillEnabled); return; }
            RebuildAfterFreshStart();
            if (_payload != null && _policy != null && EditDraftSwitch.IsEnabled(Application?.ServiceProvider, _policy.PolicyId))
                StartWrite(BuildSnapshot());
        }
        catch (Exception ex)
        {
            EditDraftLog.Error($"[EditDraft] fresh-start write failed: {ex.GetType().Name}");
        }
    }

    /// <summary>A replaced slot's fresh start: its never-stored members only, written as a new row.</summary>
    internal static void RetiredFreshStart(DraftWriteSlot<DraftSnapshot> slot, StoredMark mark, DraftSnapshot newest, EditDraftWriter writer, Func<bool> stillEnabled)
    {
        slot.AcknowledgeFreshStart();
        var old = newest == null ? null : EditDraftPayload.FromJson<EditDraftPayload>(newest.Json);
        if (old == null || newest.Seed.OwnerUserOid == Guid.Empty) return;
        var stored = mark.Value;
        // NEW records (design §4.2.5; Codex diffreview D1): the reconstruction members (seeded, or typed even when already
        // stored) and the Oid history stay with the never-stored typed members.
        var fresh = EditDraftCaptureRules.FreshAfterGone(old, e => newest.CapturedAt.TryGetValue(e.Path, out var at) && at > stored, out var kept, newest.Context);
        EditDraftLog.Info($"[EditDraft] fresh draft for a replaced screen context: kept {kept} never-stored member(s)");
        if (fresh == null) return;
        var snap = newest with { Json = fresh.ToJson(), Count = fresh.Count, Now = EditDraftClock.Now(newest.Clock) };   // the clock of the screen that built it
        if (!slot.TryBeginWrite(snap, out var ticket)) return;
        System.Threading.Tasks.Task.Run(() =>
        {
            DraftWriteSlot<DraftSnapshot>.WriteTicket? current = ticket;
            while (current is { } running)
                current = RunOneWrite(slot, writer, running, mark.Raise, (dropped, s) => RetiredFreshStart(slot, mark, dropped ?? s, writer, stillEnabled), stillEnabled);
        });
    }

    private void PostWrite(IObjectSpace objectSpace)
    {
        if (_writePosted) return;
        var context = SynchronizationContext.Current ?? _circuit;
        if (context == null) return;
        _writePosted = true;
        var gesture = ++_gesture;
        context.Post(_ => RunWrite(objectSpace, gesture), null);
    }

    private void RunWrite(IObjectSpace objectSpace, int gesture)
    {
        if (gesture != _gesture) return;
        _writePosted = false;
        if (!ReferenceEquals(objectSpace, _objectSpace) || objectSpace.IsDisposed) return;
        if (_payload == null || _policy == null || !EditDraftSwitch.IsEnabled(Application?.ServiceProvider, _policy.PolicyId)) return;
        StartWrite(BuildSnapshot());
    }

    /// <summary>Built ON THE CIRCUIT: reads the record; only plain values cross to the worker. No names (design §3 S4c).</summary>
    private DraftSnapshot BuildSnapshot()
    {
        // NEW records (design §4.2.6), decided at EVERY write: while never saved, TargetOid = Guid.Empty, IsNew, and the
        // screen object's Oid at the head of the payload's prov header; after the save, the record's Oid.
        var key = EditDraftNewRecordRules.Key(IsNewRecord(), RecordOid());
        if (key.IsNew) _payload.AddProvisional(RecordOid());
        var seed = new EditDraftSeed
        {
            EditorInstanceId = _editorInstanceId,
            OwnerUserOid = _payloadOwner.Oid,
            ObjectType = _policy.TypeName,
            TargetOid = key.TargetOid,
            IsNew = key.IsNew,
            ContextText = ContextText(),
            ViewId = View?.Id
        };
        var snapshot = new DraftSnapshot(seed, _payload.ToJson(), _payload.Count, EditDraftClock.Now(_clock), _captureSeq,
                                         new Dictionary<string, long>(_capturedAt, StringComparer.Ordinal)) { Clock = _clock };
        if (!key.IsNew) return snapshot;
        // NEW records: the new-record key's gate (checked per ticket, Codex diffreview D2) and the reconstruction members a
        // retired fresh start must keep (D1).
        var services = Application?.ServiceProvider;
        return snapshot with
        {
            NewRecordsGate = EditDraftWriteGate.Bind(_policy?.PolicyId, id => EditDraftSwitch.IsNewRecordsEnabled(services, id)),
            Context = _policy?.NewRecordReconstructionOrder
        };
    }

    /// <summary>「メモ／2026/09/30」: the type caption and the policy's context date (separator from EditDraftTexts). Never a person's name.</summary>
    public static string ContextTextFor(string typeCaption, DateTime? date) =>
        date is { } d && d != DateTime.MinValue ? $"{typeCaption}{EditDraftTexts.Of(t => t.ContextSeparator)}{d:yyyy/MM/dd}" : typeCaption;

    private string ContextText()
    {
        try
        {
            var caption = CaptionHelper.GetClassCaption(_policy.Type.FullName);
            if (string.IsNullOrWhiteSpace(caption)) caption = _policy.TypeName;
            return ContextTextFor(caption, _policy.ContextDateOf?.Invoke(_record));
        }
        catch { return _policy.TypeName; }
    }

    private void StartWrite(DraftSnapshot snapshot)
    {
        if (!EditDraftNewRecordRules.IsWritable(snapshot.Seed)) return;   // never an ownerless row; a targetless row only for a NEW record
        var slot = _slot;
        var mark = _mark;
        var writer = _writer;
        if (!slot.TryBeginWrite(snapshot, out var ticket)) return;
        var circuit = SynchronizationContext.Current ?? _circuit;
        var services = Application?.ServiceProvider;
        // Design §6: the keys are re-read before EVERY queued write, the coalesced ones in this loop included
        // (Codex diffreview C5), and ALWAYS for the policy that created THIS slot (Codex diffreview a2 C1): the
        // predicate is bound here and travels with the slot's callbacks. IConfiguration is read off the circuit.
        var stillEnabled = EditDraftWriteGate.Bind(_policy?.PolicyId, id => EditDraftSwitch.IsEnabled(services, id));
        Action<DraftSnapshot, DraftSnapshot> onFreshStart = (dropped, s) =>
        {
            var newest = dropped ?? s;
            if (circuit == null) { RetiredFreshStart(slot, mark, newest, writer, stillEnabled); return; }
            try { circuit.Post(_ => ResumeAfterFreshStart(slot, mark, newest, writer, stillEnabled), null); } catch { }
        };
        // Wave 1b (Codex diffreview pass 2 C5/C10/C4): after a create's outcome the list badges of this circuit re-read.
        var badges = services?.GetService(typeof(EditDraftBadgeNotifier)) as EditDraftBadgeNotifier;
        var type = _policy?.TypeName;
        System.Threading.Tasks.Task.Run(() =>
        {
            DraftWriteSlot<DraftSnapshot>.WriteTicket? current = ticket;
            var created = false;
            while (current is { } running)
            {
                created |= running.IsCreate;
                current = RunOneWrite(slot, writer, running, mark.Raise, onFreshStart, stillEnabled);
            }
            if (created) badges?.PostChanged(circuit, type);
        });
    }

    internal static DraftWriteSlot<DraftSnapshot>.WriteTicket? RunOneWrite(
        DraftWriteSlot<DraftSnapshot> slot, EditDraftWriter writer,
        DraftWriteSlot<DraftSnapshot>.WriteTicket ticket, Action<long> onStored,
        Action<DraftSnapshot, DraftSnapshot> onFreshStart, Func<bool> stillEnabled = null)
    {
        var s = ticket.Snapshot;
        // NEW records (D15): a never-saved record's snapshot also carries the new-record key's gate, checked per ticket.
        if ((stillEnabled != null && !stillEnabled()) || (s.NewRecordsGate != null && !s.NewRecordsGate()))
        {
            // Capture was switched off while this write was queued: nothing is written; the ticket is
            // completed as not done so the slot is not left in flight, and the queue drains the same way.
            DraftWriteSlot<DraftSnapshot>.Completion dropped = null;
            try { dropped = ticket.IsCreate ? slot.CompleteCreate(ticket, Guid.Empty) : slot.CompleteSupersede(ticket, false); } catch { }
            try { EditDraftLog.Info($"[EditDraft] write skipped: capture switched off while queued type={s.Seed.ObjectType} (off-turn)"); } catch { }
            return dropped?.Next;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var what = "none";
        var ok = false;
        var forgot = false;
        DraftWriteSlot<DraftSnapshot>.Completion completion = null;
        try
        {
            if (ticket.IsCreate)
            {
                what = "create";
                var oid = writer.Create(s.Seed, s.Json, s.Count, s.Now);
                ok = oid != Guid.Empty;
                if (ok) onStored?.Invoke(s.MaxSeq);
                completion = slot.CompleteCreate(ticket, oid);
                if (completion.RowToDelete != Guid.Empty)
                {
                    what = "create, then delete (saved meanwhile)";
                    ok = writer.DeleteOwn(completion.RowToDelete, s.Seed.OwnerUserOid, s.Seed.EditorInstanceId) >= 0;
                }
            }
            else
            {
                what = "supersede";
                ok = writer.TrySupersede(ticket.Oid, ticket.Revision, s.Seed.OwnerUserOid, s.Json, s.Count,
                    s.Seed.ContextText, s.Now);
                if (ok) onStored?.Invoke(s.MaxSeq);
                if (!ok)
                {
                    var state = writer.ReadRowState(ticket.Oid, ticket.Revision, s.Seed.OwnerUserOid, s.Now);
                    if (state == EditDraftRowState.Moved && slot.ForgetGoneRow(ticket))
                    {
                        forgot = true;
                        what = "supersede (row taken over elsewhere; recreating this screen's draft)";
                    }
                    else if (state is EditDraftRowState.Gone or EditDraftRowState.Discarded
                             && slot.ForgetGoneRow(ticket, true, out var droppedPending))
                    {
                        what = $"supersede (row {state}; old content dropped, never-stored edits rewritten fresh)";
                        onFreshStart?.Invoke(droppedPending, s);
                    }
                }
                completion = slot.CompleteSupersede(ticket, ok);
                if (forgot && completion.Next == null && slot.TryRestart(ticket, s, out var recreate))
                    return recreate;
            }
        }
        catch (Exception ex)
        {
            EditDraftLog.Error($"[EditDraft] write failed: {ex.GetType().Name}");
        }
        finally
        {
            if (completion == null)
            {
                try { completion = ticket.IsCreate ? slot.CompleteCreate(ticket, Guid.Empty) : slot.CompleteSupersede(ticket, false); }
                catch { }
            }
            watch.Stop();
            try
            {
                EditDraftLog.Info($"[EditDraft] write {what} ok={ok} {watch.ElapsedMilliseconds} ms rev={slot.Revision} " +
                                  $"entries={s.Count} bytes={s.Json?.Length ?? 0} type={s.Seed.ObjectType} (off-turn)");
            }
            catch { }
        }
        return completion?.Next;
    }

    // ---------------------------------------------------------------------------------------
    // Save retires the whole draft
    // ---------------------------------------------------------------------------------------

    private void ObjectSpace_Committed(object sender, EventArgs e)
    {
        // NOT Committing: a commit can still fail after Committing (KB fix-497).
        var owner = _payloadOwner.Oid;
        var editor = _editorInstanceId;
        var own = _slot.OnSaved();
        _payload = null;
        _payloadOwner = EditDraftOwnerInfo.None;
        ResetCaptureSequence();

        // The record is saved now: the next edit compares against what was saved.
        _baseline = SnapshotBaseline(_record);

        if (own == Guid.Empty || owner == Guid.Empty) return;
        var writer = _writer;
        // Wave 1b (B6): the row badges of this circuit's lists are refreshed once the row is gone.
        var badges = Application?.ServiceProvider?.GetService(typeof(EditDraftBadgeNotifier)) as EditDraftBadgeNotifier;
        var circuit = SynchronizationContext.Current ?? _circuit;
        var type = _policy?.TypeName;
        System.Threading.Tasks.Task.Run(() =>
        {
            var n = -1;
            try { n = writer.DeleteOwn(own, owner, editor); }
            catch (Exception ex) { EditDraftLog.Error($"[EditDraft] delete after save failed: {ex.GetType().Name}"); }
            EditDraftLog.Info($"[EditDraft] delete after save: rows={n} (the whole draft, unticked fields included) (off-turn)");
            badges?.PostChanged(circuit, type);
        });
    }

    public static string Short(Guid g) => g == Guid.Empty ? "none" : g.ToString("N").Substring(0, 8).ToUpperInvariant();
}
