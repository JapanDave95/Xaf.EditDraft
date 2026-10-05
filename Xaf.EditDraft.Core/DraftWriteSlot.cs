using System;

namespace Xaf.EditDraft.Core;

/// <summary>
/// The write slot of ONE editing context (owner decision D5, milestone M1): the draft-row state of one
/// capture controller activation, kept XAF-free so the ordering rules can be unit-tested without XAF.
/// <typeparamref name="TSnapshot"/> is the prepared, immutable data of one write.
///
/// Why it exists (KB fix-507): a capture controller that kept the
/// draft Oid and revision in fields that the off-circuit worker read and wrote had no ordering
/// against a save. A save that landed while the FIRST write (the create) was still running found
/// no Oid, deleted nothing, and the create then published its Oid — leaving a draft of edits that
/// were already saved. A second write could also start while the first was running (two creates,
/// one untracked row), and an old worker could read a newer draft's Oid and overwrite it.
///
/// The rules this class enforces:
///  - ONE write at a time. The in-flight flag is held until the worker COMPLETES, not until it is
///    dispatched.
///  - A write asked for while one runs keeps its prepared snapshot here (the newest replaces an
///    older one). The finishing worker receives it as <see cref="Completion.Next"/>, already
///    started, and writes it itself — nothing has to go back to the circuit, so a screen that
///    closes meanwhile does not lose it.
///  - Every write carries a ticket with the Oid, revision and save EPOCH it was started under; the
///    worker never reads live state.
///  - A successful save (<see cref="OnSaved"/>) starts a new epoch and drops a pending snapshot
///    (its edits are the ones just saved). A create that completes under an older epoch wrote
///    saved edits, so its row is handed back to be deleted. An update that completes under an
///    older epoch changes nothing here.
///  - A refused or failed save does not call <see cref="OnSaved"/>, so nothing is deleted.
/// Thread-safe: the circuit calls TryBeginWrite / OnSaved, the worker calls Complete*.
/// </summary>
public sealed class DraftWriteSlot<TSnapshot> where TSnapshot : class
{
    private readonly object _gate = new();
    private int _epoch;
    private Guid _oid;
    private int _revision;
    private bool _inFlight;
    private TSnapshot _pending;

    public readonly struct WriteTicket
    {
        public WriteTicket(int epoch, Guid oid, int revision, TSnapshot snapshot)
        {
            Epoch = epoch;
            Oid = oid;
            Revision = revision;
            Snapshot = snapshot;
        }

        public int Epoch { get; }
        public Guid Oid { get; }
        public int Revision { get; }
        public TSnapshot Snapshot { get; }
        public bool IsCreate => Oid == Guid.Empty;
    }

    public sealed class Completion
    {
        internal Completion(Guid rowToDelete, WriteTicket? next)
        {
            RowToDelete = rowToDelete;
            Next = next;
        }

        /// <summary>A row created for edits a save has since committed; the caller deletes it.</summary>
        public Guid RowToDelete { get; }

        /// <summary>The next write, ALREADY STARTED (in flight); the caller must run and complete it.</summary>
        public WriteTicket? Next { get; }
    }

    public Guid Oid { get { lock (_gate) return _oid; } }
    public int Revision { get { lock (_gate) return _revision; } }
    public bool IsWriteInFlight { get { lock (_gate) return _inFlight; } }
    public bool HasPendingSnapshot { get { lock (_gate) return _pending != null; } }

    /// <summary>
    /// Starts a write of <paramref name="snapshot"/>. False when one is already running: the
    /// snapshot is kept (replacing an older pending one) and handed back by that write's completion.
    /// </summary>
    public bool TryBeginWrite(TSnapshot snapshot, out WriteTicket ticket)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        lock (_gate)
        {
            if (_needsFreshStart)
            {
                // Built from content that must not come back (see ForgetGoneRow(freshStart)); dropped.
                ticket = default;
                return false;
            }
            if (_inFlight)
            {
                _pending = snapshot;
                ticket = default;
                return false;
            }
            _inFlight = true;
            _writesStarted++;
            ticket = new WriteTicket(_epoch, _oid, _revision, snapshot);
            return true;
        }
    }

    // Counts every write started; TryRestart is refused once a newer write started after the forget.
    private long _writesStarted;
    private long _restartAllowedAt = -1;

    /// <summary>A create finished. <paramref name="createdOid"/> is Guid.Empty when it failed.</summary>
    public Completion CompleteCreate(WriteTicket ticket, Guid createdOid)
    {
        lock (_gate)
        {
            var rowToDelete = Guid.Empty;
            if (ticket.Epoch != _epoch)
            {
                // A save committed these edits while the create ran.
                rowToDelete = createdOid;
            }
            else if (createdOid != Guid.Empty)
            {
                _oid = createdOid;
                _revision = 1;
            }
            return new Completion(rowToDelete, StartPendingOrRelease());
        }
    }

    /// <summary>An update finished (false = lost race or failure).</summary>
    public Completion CompleteSupersede(WriteTicket ticket, bool succeeded)
    {
        lock (_gate)
        {
            if (succeeded && ticket.Epoch == _epoch && ticket.Oid == _oid && ticket.Revision == _revision)
                _revision = ticket.Revision + 1;
            return new Completion(Guid.Empty, StartPendingOrRelease());
        }
    }

    /// <summary>
    /// The row a supersede was aimed at no longer exists — 「すべて破棄」 on another screen deleted it
    /// (2026-09-28). The slot forgets it, so the next write CREATES a fresh row for what is on this
    /// screen now instead of superseding a row that is gone for ever. Only when the ticket still
    /// describes the current row (same epoch, same Oid): a save or a newer row in the meantime wins.
    /// Call BEFORE <see cref="CompleteSupersede"/> for that ticket, so a pending snapshot handed on by
    /// the completion is started as a create.
    /// </summary>
    public bool ForgetGoneRow(WriteTicket ticket) => ForgetGoneRow(ticket, freshStart: false);

    /// <summary>
    /// As <see cref="ForgetGoneRow(WriteTicket)"/>; with <paramref name="freshStart"/> the content the
    /// slot holds must NOT come back either (for example the row was 破棄'd or expired — design §4.8,
    /// Codex diff review C5). The pending snapshot is dropped and every write is refused, WITHOUT
    /// being kept, until the owner calls <see cref="AcknowledgeFreshStart"/> after starting new content.
    /// </summary>
    public bool ForgetGoneRow(WriteTicket ticket, bool freshStart) => ForgetGoneRow(ticket, freshStart, out _);

    /// <summary>
    /// As <see cref="ForgetGoneRow(WriteTicket, bool)"/>, handing back the pending snapshot a fresh
    /// start dropped (it may hold the newest content; the caller decides what of it may come back).
    /// </summary>
    public bool ForgetGoneRow(WriteTicket ticket, bool freshStart, out TSnapshot droppedPending)
    {
        droppedPending = null;
        lock (_gate)
        {
            if (ticket.Epoch != _epoch || ticket.Oid == Guid.Empty || ticket.Oid != _oid) return false;
            _oid = Guid.Empty;
            _revision = 0;
            // A restart is valid only for THIS ticket and only until the next write starts
            // (Codex reviews C14 and a3-C3: a second forget must not hand the permission to an older ticket).
            _restartAllowedAt = _writesStarted;
            _forgotOid = ticket.Oid;
            _forgotRevision = ticket.Revision;
            if (freshStart)
            {
                droppedPending = _pending;
                _pending = null;
                _needsFreshStart = true;
            }
            return true;
        }
    }

    private Guid _forgotOid;
    private int _forgotRevision;

    private bool _needsFreshStart;

    /// <summary>True after a fresh-start forget until <see cref="AcknowledgeFreshStart"/>.</summary>
    public bool NeedsFreshStart { get { lock (_gate) return _needsFreshStart; } }

    /// <summary>The owner has replaced its content; writes are accepted again.</summary>
    public void AcknowledgeFreshStart() { lock (_gate) _needsFreshStart = false; }

    /// <summary>
    /// Takes over an EXISTING row that was claimed for this screen (a restore that claimed the row):
    /// later writes supersede it at <paramref name="revision"/>. Only while no write is running and no
    /// row is held — otherwise false and nothing changes.
    /// </summary>
    public bool Attach(Guid oid, int revision)
    {
        lock (_gate)
        {
            if (_inFlight || _oid != Guid.Empty || oid == Guid.Empty || revision <= 0) return false;
            _oid = oid;
            _revision = revision;
            _pending = null;
            return true;
        }
    }

    /// <summary>
    /// Starts <paramref name="snapshot"/> again as a new write — ONLY if no save happened since
    /// <paramref name="previous"/> was issued (same epoch) and no write is running. A save in between
    /// means the snapshot holds saved work and must not come back as a draft (Codex diff review C4,
    /// 2026-09-28: a plain TryBeginWrite would take the NEW epoch and resurrect it). Also refused once
    /// ANY other write has started since the row was forgotten (Codex delta review C14: a newer
    /// snapshot may already have created the replacement row, which the old snapshot must not overwrite).
    /// The restart is always a create.
    /// </summary>
    public bool TryRestart(WriteTicket previous, TSnapshot snapshot, out WriteTicket ticket)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        lock (_gate)
        {
            ticket = default;
            if (previous.Epoch != _epoch || _inFlight || _needsFreshStart) return false;
            if (_restartAllowedAt < 0 || _writesStarted != _restartAllowedAt || _oid != Guid.Empty) return false;
            if (previous.Oid != _forgotOid || previous.Revision != _forgotRevision) return false;
            _restartAllowedAt = -1;
            _inFlight = true;
            _writesStarted++;
            ticket = new WriteTicket(_epoch, Guid.Empty, 0, snapshot);
            return true;
        }
    }

    // Caller holds _gate.
    private WriteTicket? StartPendingOrRelease()
    {
        if (_pending == null)
        {
            _inFlight = false;
            return null;
        }
        var next = new WriteTicket(_epoch, _oid, _revision, _pending);
        _pending = null;
        _writesStarted++;
        return next; // stays in flight
    }

    /// <summary>
    /// A save committed. Returns the known draft row to delete (Guid.Empty if none yet — a create
    /// still running will hand its row back from <see cref="CompleteCreate"/>). A pending snapshot
    /// is dropped: the edits it holds are the ones just saved.
    /// </summary>
    public Guid OnSaved()
    {
        lock (_gate)
        {
            _epoch++;
            var toDelete = _oid;
            _oid = Guid.Empty;
            _revision = 0;
            _pending = null;
            _needsFreshStart = false;   // the owner's content was just saved and cleared
            return toDelete;
        }
    }
}
