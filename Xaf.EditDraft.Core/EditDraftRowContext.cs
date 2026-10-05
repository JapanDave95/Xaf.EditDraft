using System;
using System.Collections.Generic;
using System.Linq;

namespace Xaf.EditDraft.Core;

/// <summary>
/// One persistent row's 入力控 capture context on a ListView (wave 1b, design §3): its own baseline,
/// payload, write slot and sequence marks, keyed by the record's Oid (sorting, paging and filtering never
/// change it). The list screen's contexts share the screen's EditorInstanceId. XAF-free so the capture,
/// fallback and cancel rules are unit-tested against the compiled library;
/// EditDraftListCaptureControllerBlazor (in Xaf.EditDraft.Blazor since M2) owns the events, the gate and the worker — hence public.
///
/// Baseline: prepared at the grid's EditingStarted (policy InitializingGetters first, fix-529), else by the
/// FALLBACK at the first change (the changed member from ObjectChanged.OldValue; the others read at that
/// moment — a cascade could already have moved them, recorded as <see cref="FallbackPrepared"/>).
///
/// Cancel (owner B3 "Keep the draft"): the grid reverts a cancelled row through setters (an already-modified
/// row, GridIntermediateStoreProxy) or a reload. Those changes are reversals, not typing: <see cref="RevertReversals"/>
/// puts back what the draft held before them, so the live draft keeps the typed values.
/// </summary>
public sealed class EditDraftRowContext
{
    private Dictionary<string, (string Raw, string Text)> _baseline = new(StringComparer.Ordinal);
    private Dictionary<string, string> _sessionStart = new(StringComparer.Ordinal);

    // The OPEN WINDOW: members captured since the last snapshot was built (i.e. since the last write left the
    // circuit). Value = a copy of the member's entry as it was before its LATEST capture (null = no entry then).
    private readonly Dictionary<string, EditDraftEntry> _priorInWindow = new(StringComparer.Ordinal);
    // The member's CapturedAt before that same capture (null = none). A reverted entry gets it back, so a value
    // already stored is never relabelled as never-stored (Codex diffreview pass 2 C9: fresh start after 破棄/expiry).
    private readonly Dictionary<string, long?> _priorSeqInWindow = new(StringComparer.Ordinal);
    private long _captureSeq;

    public EditDraftRowContext(EditDraftTypePolicy policy, object record, Guid targetOid)
    {
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        Record = record ?? throw new ArgumentNullException(nameof(record));
        TargetOid = targetOid;
        Mark = new EditDraftCaptureController.StoredMark(0);
    }

    public EditDraftTypePolicy Policy { get; }
    public object Record { get; }
    public Guid TargetOid { get; }

    public bool Prepared { get; private set; }

    /// <summary>True when the baseline came from the fallback (no EditingStarted before the first change).</summary>
    public bool FallbackPrepared { get; private set; }

    public EditDraftPayload Payload { get; private set; }
    public EditDraftOwnerInfo PayloadOwner { get; private set; } = EditDraftOwnerInfo.None;
    public DraftWriteSlot<EditDraftCaptureController.DraftSnapshot> Slot { get; } = new();
    public EditDraftCaptureController.StoredMark Mark { get; private set; }
    public Dictionary<string, long> CapturedAt { get; private set; } = new(StringComparer.Ordinal);
    public long CaptureSeq => _captureSeq;

    /// <summary>
    /// The row was already modified in the ObjectSpace when its current edit session started. Only then does
    /// a row ✕ restore values THROUGH SETTERS (GridIntermediateStoreProxy); a clean row is reloaded instead
    /// (ObjectReloaded, no per-member change). Reversals are taken out of the payload only in that case
    /// (Codex diffreview C1).
    /// </summary>
    public bool SessionStartedModified { get; set; }

    /// <summary>Circuit-side write bookkeeping (the controller's PostWrite/RunWrite).</summary>
    public bool WritePosted { get; set; }
    public int Gesture { get; set; }

    public int OpenWindowCount => _priorInWindow.Count;

    public bool HasBaseline(string path) => _baseline.ContainsKey(path);

    // ---- baseline ---------------------------------------------------------------------------

    /// <summary>
    /// At the grid's EditingStarted for this row (design §3): the policy's writing getters run FIRST (the
    /// caller suppresses capture around this call), then every member is snapshotted — the DetailView BindTo
    /// sequence for one row, without loading every row.
    /// </summary>
    public void PrepareAtEditingStarted(Func<object, int> runInitializingGetters)
    {
        runInitializingGetters?.Invoke(Record);
        _baseline = Snapshot();
        FallbackPrepared = false;
        Prepared = true;
        StartSession();
    }

    /// <summary>
    /// No EditingStarted came first (a control that bypasses the grid lifecycle, or a row whose context a save
    /// retired while it stayed in edit). The changed member's baseline is ObjectChanged.OldValue when the
    /// notification carries values, else unknown; the other members are read now.
    /// </summary>
    public void PrepareFallback(string path, object oldValue, object newValue)
    {
        _baseline = Snapshot();
        if (path != null && Policy.Find(path) != null)
        {
            var b = EditDraftFallbackBaseline.ForChanged(oldValue, newValue);
            if (b.Known) _baseline[path] = (b.Raw, b.Text);
            else _baseline.Remove(path);
        }
        FallbackPrepared = true;
        Prepared = true;
        _sessionStart = _baseline.ToDictionary(kv => kv.Key, kv => kv.Value.Raw, StringComparer.Ordinal);
    }

    /// <summary>A new edit session of a row whose context is kept: the reversal reference moves, the baseline does not.</summary>
    public void StartSession()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in Policy.Members)
        {
            try { d[m.Path] = EditDraftCodec.RawOf(EditDraftMembers.GetValue(Record, m.Path)); } catch { }
        }
        _sessionStart = d;
    }

    private Dictionary<string, (string Raw, string Text)> Snapshot()
    {
        var d = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var m in Policy.Members)
        {
            try
            {
                var v = EditDraftMembers.GetValue(Record, m.Path);
                d[m.Path] = (EditDraftCodec.RawOf(v), EditDraftDisplay.TextOf(v));
            }
            catch { /* a member that cannot be read has no known baseline */ }
        }
        return d;
    }

    // ---- capture ----------------------------------------------------------------------------

    /// <summary>
    /// Records one member's CURRENT value (read after the setter cascade) under <paramref name="owner"/>.
    /// False when nothing changed (a bare notification); an empty payload is never left behind.
    /// </summary>
    public bool Capture(string path, EditDraftOwnerInfo owner)
    {
        var spec = Policy.Find(path);
        if (spec == null) return false;
        var v = EditDraftMembers.GetValue(Record, path);
        var raw = EditDraftCodec.RawOf(v);

        var started = Payload == null;
        if (started)
        {
            Payload = new EditDraftPayload { TypeName = Policy.TypeName };
            PayloadOwner = owner;
        }
        var existing = Payload.Get(path);
        var baseKnown = _baseline.TryGetValue(path, out var b);
        if ((existing == null && baseKnown && string.Equals(raw, b.Raw, StringComparison.Ordinal))
            || (existing != null && string.Equals(existing.ValueRaw, raw, StringComparison.Ordinal)))
        {
            if (started) { Payload = null; PayloadOwner = EditDraftOwnerInfo.None; }
            return false;
        }

        _priorInWindow[path] = existing == null ? null : Copy(existing);
        _priorSeqInWindow[path] = CapturedAt.TryGetValue(path, out var prevSeq) ? prevSeq : null;
        Payload.Upsert(path, spec.Kind, spec.Caption, baseKnown, baseKnown ? b.Raw : null, baseKnown ? b.Text : null,
                       raw, EditDraftDisplay.TextOf(v));
        CapturedAt[path] = ++_captureSeq;
        return true;
    }

    private static EditDraftEntry Copy(EditDraftEntry e) => new()
    {
        Path = e.Path, Kind = e.Kind, Caption = e.Caption, BaseKnown = e.BaseKnown, BaseRaw = e.BaseRaw,
        BaseText = e.BaseText, ValueRaw = e.ValueRaw, ValueText = e.ValueText, Seeded = e.Seeded
    };

    // ---- cancel / reload (B3) ---------------------------------------------------------------

    /// <summary>
    /// Row cancel, object reload or whole-space rollback: the setter-restores and reload notifications run on
    /// the same circuit turn, after the typing and before the controller's posted write. For every member
    /// captured in the OPEN window whose captured value now equals its value at the start of the row's edit
    /// session (or its baseline), the entry goes back to what it held before that capture; a member that had
    /// no entry before it is removed. A typed value whose write already left the circuit is not in the window
    /// and is never touched. Returns how many entries were reverted. Closes the window.
    /// </summary>
    public int RevertReversals()
    {
        var reverted = 0;
        if (Payload != null)
        {
            foreach (var (path, before) in _priorInWindow.ToList())
            {
                var e = Payload.Get(path);
                if (e == null) continue;
                var atStart = _sessionStart.TryGetValue(path, out var s) && string.Equals(s, e.ValueRaw, StringComparison.Ordinal);
                var atBase = _baseline.TryGetValue(path, out var b) && string.Equals(b.Raw, e.ValueRaw, StringComparison.Ordinal);
                if (!atStart && !atBase) continue;
                if (before == null)
                {
                    Payload.Entries.Remove(e);
                    CapturedAt.Remove(path);
                }
                else
                {
                    e.ValueRaw = before.ValueRaw;
                    e.ValueText = before.ValueText;
                    e.Seeded = before.Seeded;
                    if (_priorSeqInWindow.TryGetValue(path, out var seq) && seq is long restored) CapturedAt[path] = restored;
                }
                reverted++;
            }
            if (Payload.Count == 0) { Payload = null; PayloadOwner = EditDraftOwnerInfo.None; }
        }
        _priorInWindow.Clear();
        _priorSeqInWindow.Clear();
        return reverted;
    }

    // ---- write ------------------------------------------------------------------------------

    /// <summary>Built ON THE CIRCUIT; only plain values cross to the worker. Closes the open window. Null clock = the system clock.</summary>
    public EditDraftCaptureController.DraftSnapshot BuildSnapshot(EditDraftSeed seed, TimeProvider clock = null)
    {
        _priorInWindow.Clear();
        _priorSeqInWindow.Clear();
        return new EditDraftCaptureController.DraftSnapshot(seed, Payload.ToJson(), Payload.Count, EditDraftClock.Now(clock), _captureSeq,
            new Dictionary<string, long>(CapturedAt, StringComparer.Ordinal)) { Clock = clock };
    }

    /// <summary>After the slot reported that its row expired or was 破棄'd: a new payload with only the never-stored members.</summary>
    public int RebuildAfterFreshStart()
    {
        var old = Payload;
        var owner = PayloadOwner;
        var stored = Mark.Value;
        Payload = null;
        PayloadOwner = EditDraftOwnerInfo.None;
        Slot.AcknowledgeFreshStart();
        if (old == null || owner.IsNone) return 0;

        var fresh = new EditDraftPayload { TypeName = old.TypeName };
        foreach (var e in old.Entries)
        {
            if (!CapturedAt.TryGetValue(e.Path, out var at) || at <= stored) continue;
            fresh.Upsert(e.Path, e.Kind, e.Caption, e.BaseKnown, e.BaseRaw, e.BaseText, e.ValueRaw, e.ValueText);
        }
        if (fresh.Count > 0) { Payload = fresh; PayloadOwner = owner; }
        return fresh.Count;
    }
}
