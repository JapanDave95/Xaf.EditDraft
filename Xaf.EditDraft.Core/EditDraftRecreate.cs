using System;

namespace Xaf.EditDraft.Core;

/// <summary>A NEW-record draft as the recreate read it (owner-scoped). Plain data.</summary>
public sealed class EditDraftRecreateDraft
{
    public Guid DraftOid { get; init; }
    public int Revision { get; init; }
    public string ObjectType { get; init; }
    public Guid TargetOid { get; init; }
    /// <summary>The access scope stored at capture (EditDraftStoreBase.ScopeOid); Guid.Empty = none.</summary>
    public Guid ScopeOid { get; init; }
    public string ContextText { get; init; }
    public string ViewId { get; init; }
    public DateTime LastCapturedOn { get; init; }
    public int EntryCount { get; init; }

    /// <summary>Not expired (ExpiresOn &gt; now). A 破棄'd row is live: the list's search opens it and the claim un-discards it.</summary>
    public bool Live { get; init; }

    /// <summary>The payload version is one this build reads.</summary>
    public bool PayloadReadable { get; init; }

    public string PayloadJson { get; init; }
}

/// <summary>How a recreate ended (design §4.4). Everything before <see cref="ClaimLost"/> changed nothing; after a won claim the row stays live at its new revision.</summary>
public enum EditDraftRecreateOutcome
{
    /// <summary>No owner (not logged in, or a login the host's owner seam refuses).</summary>
    NoOwner,
    /// <summary>Not this login's live draft (gone, expired, or another owner's — the read is owner-scoped).</summary>
    NotLive,
    Unreadable,
    /// <summary>Not a new-record draft (TargetOid is set): the existing-record path opens it.</summary>
    NotNewRecord,
    /// <summary>The type is not a registered generic type, or its policy does not allow new records.</summary>
    TypeNotAllowed,
    /// <summary>No typed entry can go onto a fresh record (all 戻せません / unknown): the D9 read-only display; nothing created.</summary>
    NothingRestorable,
    /// <summary>An Oid of the draft's history names a saved record: nothing created; that record can be opened.</summary>
    AlreadySaved,
    /// <summary>The "already saved?" read failed: the person is asked; nothing created.</summary>
    SavedCheckFailed,
    /// <summary>The login may not create the type, or the UI does not offer creating it (S1/S2).</summary>
    NotPermitted,
    /// <summary>The draft's access scope (ScopeOid) is not visible to the login (S5 i, IEditDraftRecordAccess.IsScopeVisible).</summary>
    SubSectionNotVisible,
    /// <summary>The candidate object could not be built.</summary>
    CandidateFailed,
    /// <summary>The claim statement lost (another screen claimed or changed the row, or it expired): nothing applied or shown.</summary>
    ClaimLost,
    /// <summary>Filling the candidate failed after the claim.</summary>
    FillFailed,
    /// <summary>The filled record is not visible to the login (S5 ii, IEditDraftRecordAccess.IsRecordVisible): nothing shown.</summary>
    FilledNotVisible,
    /// <summary>Showing the record failed.</summary>
    ShowFailed,
    /// <summary>The record's screen did not attach the claimed draft: the record was closed unsaved.</summary>
    NotAcknowledged,
    /// <summary>The record was recreated, unsaved, in a modal window, and its screen holds the draft.</summary>
    Created
}

/// <summary>The result of <see cref="EditDraftRecreate.Run"/>.</summary>
public sealed class EditDraftRecreateResult
{
    public EditDraftRecreateOutcome Outcome { get; init; }
    public Guid OwnerOid { get; init; }
    public EditDraftRecreateDraft Draft { get; init; }
    public EditDraftTypePolicy Policy { get; init; }

    /// <summary>The payload as read (before the claim added the candidate's Oid).</summary>
    public EditDraftPayload Payload { get; init; }

    /// <summary>The saved record found by the "already saved?" check (<see cref="EditDraftRecreateOutcome.AlreadySaved"/>).</summary>
    public Guid SavedOid { get; init; }

    /// <summary>The revision the claim produced; 0 = no claim was made or it lost.</summary>
    public int ClaimedRevision { get; init; }

    public EditDraftNewApplyResult Fill { get; init; }

    /// <summary>The restore guard cancelled a commit or rollback while the record was filled or its screen activated: not a full success (Codex diffreview D5).</summary>
    public bool GuardViolated { get; init; }
}

/// <summary>
/// The XAF side of a recreate, behind an interface so the order of the steps is tested with fakes (design T12). The
/// implementation (Xaf.EditDraft.Blazor) reads through the owner-scoped writer and the application's SECURED object spaces.
/// SINGLE-MODEL parts (owner review, D14): <see cref="MayCreate"/>, <see cref="IsScopeVisible"/>, the candidate's
/// <see cref="IEditDraftRecreateCandidate.IsVisible"/>, and the owner predicate of <see cref="ReadDraft"/> and <see cref="Claim"/>.
/// </summary>
public interface IEditDraftRecreateHost
{
    EditDraftOwnerInfo CurrentOwner();
    DateTime Now();

    /// <summary>The draft, read with the owner in the query; null = none for this owner.</summary>
    EditDraftRecreateDraft ReadDraft(Guid draftOid, Guid ownerOid, DateTime now);

    EditDraftTypePolicy Policy(string objectType);

    /// <summary>Secured read: true = a record of the type with this Oid exists and the login can read it; false = none; null = the read failed.</summary>
    bool? IsSaved(EditDraftTypePolicy policy, Guid oid);

    /// <summary>S1/S2: the login may create the type and the UI offers creating it.</summary>
    bool MayCreate(EditDraftTypePolicy policy);

    /// <summary>S5 (i): records of this access scope are visible to the login.</summary>
    bool IsScopeVisible(EditDraftTypePolicy policy, Guid scopeOid);

    /// <summary>Builds the fresh, unsaved object in its own object space (its AfterConstruction runs). Null = failed.</summary>
    IEditDraftRecreateCandidate CreateCandidate(EditDraftTypePolicy policy);

    /// <summary>The one fenced claim statement (EditDraftWriter.TryClaimNew). The new revision, or 0 when it lost.</summary>
    int Claim(EditDraftRecreateDraft draft, Guid ownerOid, Guid editorInstanceId, string payloadJson, int entryCount, DateTime now);
}

/// <summary>The fresh object of one recreate. Disposing it discards it unsaved — unless <see cref="Show"/> succeeded (its screen owns it then).</summary>
public interface IEditDraftRecreateCandidate : IDisposable
{
    /// <summary>The object's Oid (BaseObject assigns it at construction); Guid.Empty = none (refused).</summary>
    Guid Oid { get; }

    /// <summary>InitializingGetters, then EditDraftRestorer.ApplyNew under EditDraftRestoreGuard.</summary>
    EditDraftNewApplyResult Fill(EditDraftTypePolicy policy, EditDraftPayload payload);

    /// <summary>S5 (ii): the FILLED object is visible to the login.</summary>
    bool IsVisible(EditDraftTypePolicy policy);

    /// <summary>Offers <paramref name="adoption"/> to the object's screen and shows it (modal, owner D6). True only when the screen acknowledged the adoption.</summary>
    bool Show(EditDraftTypePolicy policy, EditDraftPendingAdoption adoption);

    /// <summary>True when the restore guard cancelled a commit or rollback during the fill or the screen's activation (EditDraftRestoreGuard.Violated).</summary>
    bool GuardViolated { get; }
}

/// <summary>
/// NEW records — 開く on a 「新規」 row of the 「入力控」 list (design docs/edit-draft-new-records-design-2026-10-02.md §4.4;
/// owner rulings 2026-10-03: D6 modal, D7 apply directly, D11 safeguards). The order is the safeguard:
///  1. owner (re-resolved now); the draft read owner-scoped, live, readable; a generic policy with AllowNewRecords;
///  2. a draft with no typed entry a fresh record can take → the D9 read-only display, nothing created;
///  3. "already saved?" over the whole prov history (a failed read asks, never creates silently);
///  4. security before anything is created: Create permission + the UI offers it (S1/S2), the draft's access scope (S5 i);
///  5. the candidate object, in memory (BEFORE the claim: its Oid goes into the claim's payload);
///  6. ONE fenced claim statement: new editor id, revision + 1, the payload with the candidate's Oid at the head of prov;
///  7. InitializingGetters, then ApplyNew under the restore guard;
///  8. the filled object's visibility (S5 ii);
///  9.–10. the pending adoption, then the modal screen; success only once the screen acknowledged the adoption.
/// Every refusal up to step 5 changes nothing; every failure after the claim disposes the candidate, shows and saves nothing,
/// logs the step and leaves the row live at its new revision (the next 開く re-reads it).
/// </summary>
public static class EditDraftRecreate
{
    public static EditDraftRecreateResult Run(IEditDraftRecreateHost host, Guid draftOid, bool proceedWhenSavedCheckFails = false)
    {
        if (host == null) throw new ArgumentNullException(nameof(host));
        var id = EditDraftCaptureController.Short(draftOid);

        // 1. Owner, the draft (owner in the query), liveness, readability, a generic policy that allows new records.
        var owner = host.CurrentOwner();
        if (owner.IsNone) return End(EditDraftRecreateOutcome.NoOwner, id, 1);
        var now = host.Now();
        var draft = host.ReadDraft(draftOid, owner.Oid, now);
        if (draft == null || !draft.Live) return End(EditDraftRecreateOutcome.NotLive, id, 1, owner.Oid);
        if (!EditDraftNewRecordRules.IsNewRecordDraft(draft.TargetOid)) return End(EditDraftRecreateOutcome.NotNewRecord, id, 1, owner.Oid, draft);
        var payload = draft.PayloadReadable ? EditDraftPayload.FromJson<EditDraftPayload>(draft.PayloadJson) : null;
        if (payload == null) return End(EditDraftRecreateOutcome.Unreadable, id, 1, owner.Oid, draft);
        var policy = host.Policy(draft.ObjectType);
        if (!EditDraftTypePolicy.IsGeneric(policy) || !policy.AllowNewRecords || policy.TypeName != draft.ObjectType)
            return End(EditDraftRecreateOutcome.TypeNotAllowed, id, 1, owner.Oid, draft);

        // 2. Determinable refusal: nothing a fresh record can take.
        if (EditDraftNewRecordRules.RestorableTyped(policy, payload).Count == 0)
            return End(EditDraftRecreateOutcome.NothingRestorable, id, 2, owner.Oid, draft, policy, payload);

        // 3. Already saved? Every Oid of the history; a failed read is never "not saved".
        var (saved, savedOid) = EditDraftNewRecordRules.SavedState(payload.ProvisionalOids, oid => host.IsSaved(policy, oid));
        if (saved == EditDraftSavedState.Saved)
            return End(EditDraftRecreateOutcome.AlreadySaved, id, 3, owner.Oid, draft, policy, payload, savedOid: savedOid);
        if (saved == EditDraftSavedState.CheckFailed && !proceedWhenSavedCheckFails)
            return End(EditDraftRecreateOutcome.SavedCheckFailed, id, 3, owner.Oid, draft, policy, payload);

        // 4. Security before anything is created (single-model, design §5 S1/S2/S5 i).
        if (!host.MayCreate(policy)) return End(EditDraftRecreateOutcome.NotPermitted, id, 4, owner.Oid, draft, policy, payload);
        if (draft.ScopeOid != Guid.Empty && !host.IsScopeVisible(policy, draft.ScopeOid))
            return End(EditDraftRecreateOutcome.SubSectionNotVisible, id, 4, owner.Oid, draft, policy, payload);

        // 5. The candidate, in memory; nothing is saved.
        IEditDraftRecreateCandidate candidate = null;
        try { candidate = host.CreateCandidate(policy); }
        catch (Exception ex) { EditDraftLog.Error($"[EditDraft] recreate {id}: candidate failed: {ex.GetType().Name}"); }
        if (candidate == null || candidate.Oid == Guid.Empty)
        {
            try { candidate?.Dispose(); } catch { }
            return End(EditDraftRecreateOutcome.CandidateFailed, id, 5, owner.Oid, draft, policy, payload);
        }

        var shown = false;
        var claimed = 0;
        EditDraftNewApplyResult fill = null;
        try
        {
            // 6. ONE fenced statement: the claim and the payload whose prov carries the candidate's Oid (D11).
            var claimedPayload = EditDraftNewRecordRules.WithProvisional(payload, candidate.Oid);
            var json = claimedPayload.ToJson();
            var editor = Guid.NewGuid();
            // "now" read again at the claim: its [ExpiresOn] > now fence tests liveness when the statement runs (Codex diffreview D3).
            claimed = host.Claim(draft, owner.Oid, editor, json, claimedPayload.Count, host.Now());
            if (claimed <= 0) return End(EditDraftRecreateOutcome.ClaimLost, id, 6, owner.Oid, draft, policy, payload);

            // 7. Getters, then ApplyNew under the restore guard.
            try { fill = candidate.Fill(policy, claimedPayload); }
            catch (Exception ex)
            {
                EditDraftLog.Error($"[EditDraft] recreate {id}: fill failed: {ex.GetType().Name}");
                return End(EditDraftRecreateOutcome.FillFailed, id, 7, owner.Oid, draft, policy, payload, claimed);
            }

            // 8. The filled object's visibility.
            if (!candidate.IsVisible(policy))
                return End(EditDraftRecreateOutcome.FilledNotVisible, id, 8, owner.Oid, draft, policy, payload, claimed, fill);

            // 9.-10. The pending adoption (Core contract), then the modal screen; success only once it attached the draft.
            var adoption = new EditDraftPendingAdoption(draft.DraftOid, claimed, owner.Oid, editor, json);
            bool acknowledged;
            try { acknowledged = candidate.Show(policy, adoption); }
            catch (Exception ex)
            {
                EditDraftLog.Error($"[EditDraft] recreate {id}: show failed: {ex.GetType().Name}");
                return End(EditDraftRecreateOutcome.ShowFailed, id, 10, owner.Oid, draft, policy, payload, claimed, fill);
            }
            if (!acknowledged)
                return End(EditDraftRecreateOutcome.NotAcknowledged, id, 10, owner.Oid, draft, policy, payload, claimed, fill);
            shown = true;
            return End(EditDraftRecreateOutcome.Created, id, 10, owner.Oid, draft, policy, payload, claimed, fill, guardViolated: candidate.GuardViolated);
        }
        finally
        {
            if (!shown)
            {
                try { candidate.Dispose(); }
                catch (Exception ex) { EditDraftLog.Warning($"[EditDraft] recreate {id}: discarding the candidate failed: {ex.GetType().Name}"); }
            }
        }
    }

    private static EditDraftRecreateResult End(EditDraftRecreateOutcome outcome, string id, int step, Guid ownerOid = default,
        EditDraftRecreateDraft draft = null, EditDraftTypePolicy policy = null, EditDraftPayload payload = null, int claimed = 0,
        EditDraftNewApplyResult fill = null, Guid savedOid = default, bool guardViolated = false)
    {
        var detail = fill == null ? string.Empty : $" applied={fill.Applied} failed={fill.Failed} notAppliedTyped={fill.NotAppliedTyped.Count} notAppliedSeeded={fill.NotAppliedSeeded.Count}";
        if (guardViolated) detail += " guardViolated=True";
        var line = $"[EditDraft] recreate {id}: {outcome} at step {step} type={policy?.TypeName ?? draft?.ObjectType ?? "none"} claimedRev={claimed}{detail}";
        if (outcome == EditDraftRecreateOutcome.Created || outcome == EditDraftRecreateOutcome.NothingRestorable || outcome == EditDraftRecreateOutcome.AlreadySaved)
            EditDraftLog.Info(line);
        else
            EditDraftLog.Warning(line);
        return new EditDraftRecreateResult
        {
            Outcome = outcome, OwnerOid = ownerOid, Draft = draft, Policy = policy, Payload = payload,
            ClaimedRevision = claimed, Fill = fill, SavedOid = savedOid, GuardViolated = guardViolated
        };
    }
}
