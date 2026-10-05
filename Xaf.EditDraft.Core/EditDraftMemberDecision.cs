using System;
using System.Collections.Generic;
using System.Linq;

namespace Xaf.EditDraft.Core;

/// <summary>The audited disposition of one member of a registered generic type (design §1.3, owner Q1; test T3).</summary>
public enum EditDraftDisposition
{
    /// <summary>Census A: SetPropertyValue only (or a setter that rewrites its own value). Captured and restorable.</summary>
    Restorable,
    /// <summary>Census B: moves with a driver inside a named group.</summary>
    Group,
    /// <summary>Census C, selectable: 「（他の記録も変わります）」, never pre-ticked.</summary>
    SideEffect,
    /// <summary>Census C/D: captured and shown, never applied to an existing record (戻せません).</summary>
    NotRestorable,
    /// <summary>Not captured at all (design §3 S4: the value must not be stored).</summary>
    Excluded
}

/// <summary>
/// One line of a type's decision table: what the engine does with the member and why.
/// - <see cref="CensusCategory"/>: a label recorded with the decision; the gate does not read it. The helpers below write
///   the labels of a setter census (design §1.3): A restorable, B group, C side effect, D not restorable,
///   X excluded. Any label works.
/// - <see cref="Reason"/>: why (for example "SetPropertyValue only"). Required, non-empty (the gate checks it).
/// - <see cref="Evidence"/>: where the decision can be checked (for example the class file and line). Required, non-empty.
/// </summary>
public sealed record EditDraftMemberDecision(string Path, EditDraftDisposition Disposition, string CensusCategory, string Reason, string Evidence);

/// <summary>
/// Builds the per-type decision table and checks it against the policy's lists (the same fact stated twice must agree).
/// Gap G10 (2026-10-04): one helper per disposition, so a consumer does not write its own.
/// </summary>
public static class EditDraftDecisions
{
    /// <summary>Captured and put back as typed (the setter is SetPropertyValue only, or rewrites only its own value). Label "A".</summary>
    public static EditDraftMemberDecision Restorable(string path, string reason, string evidence) =>
        new(path, EditDraftDisposition.Restorable, "A", reason, evidence);

    /// <summary>Moves with a driver inside a named group (EditDraftTypePolicy.Groups). Label "B".</summary>
    public static EditDraftMemberDecision Group(string path, string reason, string evidence) =>
        new(path, EditDraftDisposition.Group, "B", reason, evidence);

    /// <summary>Its setter changes another record (EditDraftTypePolicy.SideEffectMembers): selectable, never pre-ticked. Label "C".</summary>
    public static EditDraftMemberDecision SideEffect(string path, string reason, string evidence) =>
        new(path, EditDraftDisposition.SideEffect, "C", reason, evidence);

    /// <summary>Captured and shown, never put back on an existing record (EditDraftTypePolicy.NotRestorableOnExisting). Label "D".</summary>
    public static EditDraftMemberDecision NotRestorable(string path, string reason, string evidence) =>
        new(path, EditDraftDisposition.NotRestorable, "D", reason, evidence);

    /// <summary>Never captured (EditDraftTypePolicy.Excluded); the value must not be stored. Label "X".</summary>
    public static EditDraftMemberDecision Excluded(string path, string reason, string evidence) =>
        new(path, EditDraftDisposition.Excluded, "X", reason, evidence);

    public static IReadOnlyDictionary<string, EditDraftMemberDecision> Table(params EditDraftMemberDecision[] decisions)
    {
        var d = new Dictionary<string, EditDraftMemberDecision>(StringComparer.Ordinal);
        foreach (var x in decisions)
        {
            if (x == null || string.IsNullOrWhiteSpace(x.Path)) throw new ArgumentException("A member decision needs a path.");
            if (!d.TryAdd(x.Path, x)) throw new ArgumentException($"Member '{x.Path}' has two decisions.");
        }
        return d;
    }

    /// <summary>
    /// The T3 gate as a function: every discovered member has a decision, every decision names a
    /// member that exists or is excluded, and each disposition agrees with the policy's lists.
    /// Returns the problems (empty = the policy passes).
    /// </summary>
    public static List<string> Check(EditDraftTypePolicy policy)
    {
        var problems = new List<string>();
        if (policy == null) { problems.Add("no policy"); return problems; }
        var table = policy.Decisions;
        if (table == null || table.Count == 0) { problems.Add($"{policy.TypeName}: no decision table"); return problems; }

        // Compared against what reflection PROPOSES (Candidates), not against the admitted Members:
        // Members is already filtered by this table, so testing it would be circular (Codex diag C3).
        foreach (var m in EditDraftMembers.Candidates(policy))
        {
            if (!table.TryGetValue(m.Path, out var d)) { problems.Add($"{policy.TypeName}.{m.Path}: capturable member without a recorded decision"); continue; }
            if (d.Disposition == EditDraftDisposition.Excluded)
            {
                if (policy.Find(m.Path) != null) problems.Add($"{policy.TypeName}.{m.Path}: decided Excluded but still admitted");
                if (string.IsNullOrWhiteSpace(d.Reason) || string.IsNullOrWhiteSpace(d.Evidence)) problems.Add($"{policy.TypeName}.{m.Path}: decision without reason or evidence");
                continue;
            }
            if (policy.Find(m.Path) == null) problems.Add($"{policy.TypeName}.{m.Path}: decided {d.Disposition} but not admitted");
            var group = policy.GroupOf(m.Path) != null;
            var side = policy.HasSideEffect(m.Path);
            var notRestorable = policy.IsNotRestorableOnExisting(m.Path);
            var expected = notRestorable ? EditDraftDisposition.NotRestorable
                         : side ? EditDraftDisposition.SideEffect
                         : group ? EditDraftDisposition.Group
                         : EditDraftDisposition.Restorable;
            if (d.Disposition != expected) problems.Add($"{policy.TypeName}.{m.Path}: decision {d.Disposition} but the policy lists say {expected}");
            // Apply re-expands groups after AssignableOnExisting has dropped 戻せません members, so a
            // 戻せません member inside a group would come back: not supported, refused at the gate.
            if (group && notRestorable) problems.Add($"{policy.TypeName}.{m.Path}: a 戻せません member may not be in a group (ApplyExisting cannot keep it out)");
            if (string.IsNullOrWhiteSpace(d.Reason) || string.IsNullOrWhiteSpace(d.Evidence)) problems.Add($"{policy.TypeName}.{m.Path}: decision without reason or evidence");
        }
        var candidates = new HashSet<string>(EditDraftMembers.Candidates(policy).Select(m => m.Path), StringComparer.Ordinal);
        foreach (var d in table.Values)
        {
            if (candidates.Contains(d.Path)) continue;
            // A decision for something reflection no longer proposes: a renamed/removed member, or a
            // member the policy's own Excluded list already hides. Either way the table is stale or redundant.
            if (policy.Type.GetProperty(d.Path) == null)
                problems.Add($"{policy.TypeName}.{d.Path}: decision for a member that does not exist on the type (stale table)");
            else if (d.Disposition != EditDraftDisposition.Excluded)
                problems.Add($"{policy.TypeName}.{d.Path}: decided {d.Disposition} but reflection does not propose it (not capturable)");
        }
        return problems;
    }
}
