using System;
using System.Collections.Generic;
using System.Linq;

namespace Xaf.EditDraft.Core;

/// <summary>
/// The capture decision for ONE member notification of a DetailView capture, and the payload rebuilt after a fresh start.
/// Pure (no XAF): EditDraftCaptureControllerBlazor calls these and the tests exercise the same code.
///
/// GENUINE-EDIT RULE (design docs/edit-draft-new-records-design-2026-10-02.md §4.2.4 and design rule 9; Codex combined C18):
/// for a member with no entry, or only a SEEDED entry, a notification is a change only when the member's value differs from
/// the BASELINE (the screen's snapshot after construction and the initializing getters). A seeded entry is never evidence
/// of an edit, so a bare notification on an untouched seeded member writes nothing. A member with a TYPED entry changes
/// when its value differs from the entry's value; a member typed back to its baseline keeps its entry (owner ruling D8
/// "keep the entry"). For an EXISTING record no entry is ever seeded, so the rule is the one the capture applied before.
///
/// SEEDING (design §4.2.3): on a NEW record the first genuine edit also records the policy's
/// <see cref="EditDraftTypePolicy.NewRecordReconstructionOrder"/> members with Seeded = true (the values the record's
/// construction defaults produced), so a recreate on another day restores 日付 and the times of the day it was typed. The
/// triggering member is recorded as a typed entry first, so seeding can never hide it (the seeding trap, Codex diag C1).
/// </summary>
public static class EditDraftCaptureRules
{
    /// <summary>
    /// Records <paramref name="path"/>'s CURRENT value of <paramref name="record"/> into <paramref name="payload"/> when the
    /// notification is a genuine change; on a NEW record (<paramref name="isNew"/>) the reconstruction members are then seeded.
    /// True when a write must be posted; false for a bare notification (the payload is not changed).
    /// </summary>
    public static bool Capture(EditDraftTypePolicy policy, EditDraftPayload payload, IReadOnlyDictionary<string, (string Raw, string Text)> baseline,
                               object record, string path, bool isNew)
    {
        var spec = policy?.Find(path);
        if (spec == null || payload == null || record == null) return false;
        var v = EditDraftMembers.GetValue(record, path);
        var raw = EditDraftCodec.RawOf(v);

        var existing = payload.Get(path);
        (string Raw, string Text) b = default;
        var baseKnown = baseline != null && baseline.TryGetValue(path, out b);
        var baseRaw = baseKnown ? b.Raw : null;
        var baseText = baseKnown ? b.Text : null;

        if (existing == null || existing.Seeded)
        {
            // No typed entry: genuine only against the baseline (a seeded value is context, not an edit). With no known
            // baseline a seeded entry's own value is the reference; with neither, the notification is taken as a change.
            var known = baseKnown || existing != null;
            var reference = baseKnown ? baseRaw : existing?.ValueRaw;
            if (known && string.Equals(raw, reference, StringComparison.Ordinal)) return false;
        }
        else if (string.Equals(existing.ValueRaw, raw, StringComparison.Ordinal))
        {
            return false;
        }

        payload.Upsert(path, spec.Kind, spec.Caption, baseKnown, baseRaw, baseText, raw, EditDraftDisplay.TextOf(v));
        if (isNew) Seed(policy, payload, baseline, record);
        return true;
    }

    /// <summary>
    /// NEW records: adds every <see cref="EditDraftTypePolicy.NewRecordReconstructionOrder"/> member that has no entry yet as a
    /// SEEDED entry holding the member's current value (baseline from the snapshot). A member the policy does not admit, or
    /// that cannot be read, is skipped. Returns how many were added.
    /// </summary>
    public static int Seed(EditDraftTypePolicy policy, EditDraftPayload payload, IReadOnlyDictionary<string, (string Raw, string Text)> baseline, object record)
    {
        if (policy == null || payload == null || record == null) return 0;
        var added = 0;
        foreach (var path in policy.NewRecordReconstructionOrder ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(path) || payload.Get(path) != null) continue;
            var spec = policy.Find(path);
            if (spec == null) continue;
            object v;
            try { v = EditDraftMembers.GetValue(record, path); }
            catch { continue; }
            (string Raw, string Text) b = default;
            var baseKnown = baseline != null && baseline.TryGetValue(path, out b);
            payload.Upsert(path, spec.Kind, spec.Caption, baseKnown, baseKnown ? b.Raw : null, baseKnown ? b.Text : null,
                           EditDraftCodec.RawOf(v), EditDraftDisplay.TextOf(v), seeded: true);
            added++;
        }
        return added;
    }

    /// <summary>
    /// The payload a capture writes after its row expired or was 破棄'd (RebuildAfterFreshStart and RetiredFreshStart): the
    /// TYPED entries <paramref name="neverStored"/> selects (edits the old row never stored), plus — NEW records, design
    /// §4.2.5, Codex review C4 — every SEEDED entry and the Oid history (prov) of <paramref name="old"/>, so a new record keeps
    /// the context it depends on. <paramref name="context"/> (a NEW record's NewRecordReconstructionOrder; null otherwise): those
    /// members are kept even when TYPED and already stored — they are the screen's reconstruction context, not a revived edit
    /// (Codex diffreview 2026-10-03 D1). Null when no never-stored typed entry is kept: seeds and context alone are never written.
    /// <paramref name="kept"/> = the never-stored typed entries kept. Entries keep their order, baseline and seeded/typed state.
    /// </summary>
    public static EditDraftPayload FreshAfterGone(EditDraftPayload old, Func<EditDraftEntry, bool> neverStored, out int kept,
                                                  IReadOnlyCollection<string> context = null)
    {
        kept = 0;
        if (old == null || neverStored == null) return null;
        var fresh = new EditDraftPayload { TypeName = old.TypeName };
        foreach (var e in old.Entries)
        {
            var isNeverStoredTyped = !e.Seeded && neverStored(e);
            if (!e.Seeded && !isNeverStoredTyped && (context == null || !context.Contains(e.Path))) continue;
            fresh.Upsert(e.Path, e.Kind, e.Caption, e.BaseKnown, e.BaseRaw, e.BaseText, e.ValueRaw, e.ValueText, seeded: e.Seeded);
            if (isNeverStoredTyped) kept++;
        }
        if (kept == 0) return null;
        if (old.Provisional != null) fresh.Provisional = old.Provisional.ToList();
        return fresh;
    }
}
