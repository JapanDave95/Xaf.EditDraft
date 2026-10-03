using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Xaf.EditDraft.Core;

/// <summary>
/// NEW records (design docs/edit-draft-new-records-design-2026-10-02.md §4.4 step 9; Codex review C6): one draft the 「入力控」
/// list's recreate has CLAIMED for the record it is about to show — the draft row, the revision the claim produced, the owner,
/// the EXACT editor id the claim wrote and the payload it stored. The new screen's capture takes it and attaches with that editor
/// id (EditDraftCaptureControllerBlazor.TryAttachClaimed), so its writes supersede the claimed row and the first save deletes it
/// (DeleteOwn is scoped to the editor). The capture acknowledges the attachment; the recreate reports success only after that.
/// Plain data plus a one-way acknowledgement flag (thread-safe).
/// </summary>
public sealed class EditDraftPendingAdoption
{
    private int _acknowledged;

    public EditDraftPendingAdoption(Guid draftOid, int claimedRevision, Guid ownerOid, Guid editorInstanceId, string payloadJson)
    {
        DraftOid = draftOid;
        ClaimedRevision = claimedRevision;
        OwnerOid = ownerOid;
        EditorInstanceId = editorInstanceId;
        PayloadJson = payloadJson;
    }

    public Guid DraftOid { get; }
    public int ClaimedRevision { get; }
    public Guid OwnerOid { get; }
    public Guid EditorInstanceId { get; }
    public string PayloadJson { get; }

    /// <summary>True once the record's screen has attached the claimed draft.</summary>
    public bool IsAcknowledged => Volatile.Read(ref _acknowledged) == 1;

    /// <summary>Called by the screen that attached the draft. Once set, never cleared.</summary>
    public void Acknowledge() => Interlocked.Exchange(ref _acknowledged, 1);
}

/// <summary>
/// NEW records: the per-circuit (scoped) hand-over of claimed drafts from the recreate to the screen of the record it built —
/// a Core contract (the capture lives in Core; Codex review C6: it cannot be Blazor-only). Keyed weakly on the business object,
/// so nothing outlives it; a record gets at most one pending draft and the screen takes it exactly once. Registered per circuit
/// by the host (Xaf.EditDraft.Blazor: AddEditDraftBlazor). Never process-wide (design rule 2, shared state).
/// </summary>
public sealed class EditDraftPendingAdoptions
{
    private readonly ConditionalWeakTable<object, EditDraftPendingAdoption> _pending = new();

    /// <summary>Hands <paramref name="adoption"/> to the screen that will show <paramref name="record"/> (replaces an earlier one for it).</summary>
    public void Offer(object record, EditDraftPendingAdoption adoption)
    {
        if (record == null || adoption == null) return;
        lock (_pending) _pending.AddOrUpdate(record, adoption);
    }

    /// <summary>The pending draft of <paramref name="record"/>, removed; null when there is none.</summary>
    public EditDraftPendingAdoption Take(object record)
    {
        if (record == null) return null;
        lock (_pending)
        {
            if (!_pending.TryGetValue(record, out var adoption)) return null;
            _pending.Remove(record);
            return adoption;
        }
    }

    /// <summary>Removes a pending draft nobody took (the recreate failed after offering it).</summary>
    public void Withdraw(object record)
    {
        if (record == null) return;
        lock (_pending) _pending.Remove(record);
    }
}
