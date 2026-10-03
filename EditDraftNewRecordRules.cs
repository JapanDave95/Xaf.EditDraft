using System;
using System.Collections.Generic;
using System.Linq;

namespace Xaf.EditDraft.Core;

/// <summary>What the "already saved?" check found over a NEW-record draft's Oid history (design 2026-10-02 §4.4 step 3).</summary>
public enum EditDraftSavedState
{
    /// <summary>No Oid of the history names a readable record: the draft's record was not saved (as far as this login can read).</summary>
    NotSaved,
    /// <summary>An Oid of the history names a record that exists: nothing is created.</summary>
    Saved,
    /// <summary>A read failed and nothing was found: never taken as "not saved" — the person is asked.</summary>
    CheckFailed
}

/// <summary>
/// NEW (never saved) records — the pure rules of the generic engine (design docs/edit-draft-new-records-design-2026-10-02.md
/// §4.1–§4.4; owner rulings 2026-10-03). No XAF: tested without a host. A new-record draft is the same dbo.EditDraft row as
/// any other, with TargetOid = Guid.Empty (an existing-record row can never have it) and the optional prov header in its payload.
/// </summary>
public static class EditDraftNewRecordRules
{
    /// <summary>A draft of a never-saved record: TargetOid = Guid.Empty (design §4.1).</summary>
    public static bool IsNewRecordDraft(Guid targetOid) => targetOid == Guid.Empty;

    /// <summary>
    /// The keying of ONE write (design §4.2.6), decided at every snapshot: while the screen's record is NEW the row is keyed by
    /// Guid.Empty and marked IsNew; once it is saved, by the record's Oid. <paramref name="isNew"/> null = it could not be
    /// decided: Guid.Empty WITHOUT IsNew, which <see cref="IsWritable"/> refuses — no row is written under a guess.
    /// </summary>
    public static (Guid TargetOid, bool IsNew) Key(bool? isNew, Guid recordOid) => isNew switch
    {
        true => (Guid.Empty, true),
        false => (recordOid, false),
        _ => (Guid.Empty, false)
    };

    /// <summary>
    /// The write guard of the capture (StartWrite) and of EditDraftWriter.Create: an owner, and TargetOid = Guid.Empty exactly
    /// when the seed is IsNew. An ownerless row, a targetless existing-record row and a NEW row that names a target are refused.
    /// </summary>
    public static bool IsWritable(EditDraftSeed seed) =>
        seed != null && seed.OwnerUserOid != Guid.Empty && (seed.TargetOid == Guid.Empty) == seed.IsNew;

    /// <summary>The 状態 of a 「入力控」 list row (design §4.3 (a)): 「新規」 for a new-record draft, 「既存」 otherwise; 「・破棄済み」 when discarded.</summary>
    public static string StateText(Guid targetOid, bool discarded) =>
        (IsNewRecordDraft(targetOid) ? EditDraftTexts.Of(t => t.StateNew) : EditDraftTexts.Of(t => t.StateExisting))
        + (discarded ? EditDraftTexts.Of(t => t.DiscardedSuffix) : string.Empty);

    /// <summary>
    /// How one entry of a NEW-record draft can go onto a fresh record (design §4.5): no member spec, or a 戻せません member (the
    /// NotRestorableOnExisting set applies to new records too — Codex SEC2) → Unavailable; otherwise New. A reference that does
    /// not resolve, or a member the login may not write, is found only when it is applied (it is then not applied).
    /// </summary>
    public static EditDraftItemStatus Classify(EditDraftTypePolicy policy, EditDraftEntry entry)
    {
        if (policy == null || entry == null || policy.Find(entry.Path) == null) return EditDraftItemStatus.Unavailable;
        return policy.IsNotRestorableOnExisting(entry.Path) ? EditDraftItemStatus.Unavailable : EditDraftItemStatus.New;
    }

    /// <summary>The entries a person typed (not the seeded reconstruction context), in payload order.</summary>
    public static List<EditDraftEntry> TypedEntries(EditDraftPayload payload) =>
        payload?.Entries.Where(e => !e.Seeded).ToList() ?? new List<EditDraftEntry>();

    /// <summary>Typed entries a fresh record can take (classified New).</summary>
    public static List<EditDraftEntry> RestorableTyped(EditDraftTypePolicy policy, EditDraftPayload payload) =>
        TypedEntries(payload).Where(e => Classify(policy, e) == EditDraftItemStatus.New).ToList();

    /// <summary>Typed entries a fresh record cannot take (classified Unavailable): the D9 read-only display shows their full text.</summary>
    public static List<EditDraftEntry> UnavailableTyped(EditDraftTypePolicy policy, EditDraftPayload payload) =>
        TypedEntries(payload).Where(e => Classify(policy, e) == EditDraftItemStatus.Unavailable).ToList();

    /// <summary>
    /// A copy of <paramref name="payload"/> with <paramref name="oid"/> at the head of its Oid history — the payload the
    /// recreate's claim statement stores (design §4.4 step 6). The original is not changed.
    /// </summary>
    public static EditDraftPayload WithProvisional(EditDraftPayload payload, Guid oid)
    {
        if (payload == null) return null;
        var copy = EditDraftPayload.FromJson<EditDraftPayload>(payload.ToJson());
        copy?.AddProvisional(oid);
        return copy;
    }

    /// <summary>
    /// "Already saved?" over the WHOLE Oid history (design §4.4 step 3; owner D11): the original screen object and every
    /// recreated one. <paramref name="exists"/> answers per Oid: true = a record with this Oid exists and this login can read
    /// it, false = none, null = the read failed. A found record decides Saved (with its Oid) even when another read failed; a
    /// failed read with nothing found is CheckFailed, never NotSaved. An empty history is NotSaved.
    /// </summary>
    public static (EditDraftSavedState State, Guid SavedOid) SavedState(IEnumerable<Guid> history, Func<Guid, bool?> exists)
    {
        if (exists == null) throw new ArgumentNullException(nameof(exists));
        var failed = false;
        foreach (var oid in history ?? Enumerable.Empty<Guid>())
        {
            if (oid == Guid.Empty) continue;
            bool? found;
            try { found = exists(oid); }
            catch { found = null; }
            if (found == true) return (EditDraftSavedState.Saved, oid);
            if (found == null) failed = true;
        }
        return failed ? (EditDraftSavedState.CheckFailed, Guid.Empty) : (EditDraftSavedState.NotSaved, Guid.Empty);
    }

    /// <summary>
    /// The ListView notice (owner D4 (b), D5; design §4.3; Codex review C8/C19): how many live, READABLE new-record draft ROWS
    /// the login has for the list's type — every row counts (two unsaved records are two rows). Discarded, expired
    /// (ExpiresOn &lt;= now), unreadable (unsupported payload version) and existing-record rows do not. Whether a row's record
    /// was in fact saved is decided at 開く, so the count claims rows only. The caller passes the owner's rows of the type
    /// (EditDraftWriter.ListOwn: owner and type in the query).
    /// </summary>
    public static int NoticeCount(IEnumerable<(Guid TargetOid, int PayloadSchemaVersion, bool Discarded, DateTime ExpiresOn)> rows, DateTime now) =>
        rows?.Count(r => IsNewRecordDraft(r.TargetOid) && r.PayloadSchemaVersion == EditDraftStoreBase.CurrentPayloadSchemaVersion
                         && !r.Discarded && r.ExpiresOn > now) ?? 0;

    /// <summary>The notice text for <paramref name="count"/> rows (「新規の入力控が n 件あります。…」); null when there is none.</summary>
    public static string NoticeText(int count) =>
        count <= 0 ? null : string.Format(EditDraftTexts.Of(t => t.NewRecordNotice), count);
}
