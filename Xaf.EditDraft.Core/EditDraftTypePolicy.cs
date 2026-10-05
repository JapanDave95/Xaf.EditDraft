using System;
using System.Collections.Generic;
using System.Linq;

namespace Xaf.EditDraft.Core;

/// <summary>
/// One setter cascade: members that one setter rewrites move TOGETHER — ticked together, unticked
/// together, a conflict in one unticks the group. <see cref="Members"/>[0] is the driver; it is
/// applied first, then the others as explicit final values.
/// </summary>
public sealed record EditDraftGroup(string Id, IReadOnlyList<string> Members)
{
    public string Driver => Members[0];
}

/// <summary>
/// The audited, per-type decisions of the generic edit-draft engine (design §1.3). A type takes part
/// only when a policy for it is in the <see cref="EditDraftRegistry"/>; nothing here is inherited from
/// another type's policy (owner Q1: per-type lists).
/// </summary>
public sealed class EditDraftTypePolicy
{
    private static readonly IReadOnlySet<string> NoNames = new HashSet<string>(StringComparer.Ordinal);
    private readonly Lazy<IReadOnlyList<EditDraftMemberSpec>> _members;

    public EditDraftTypePolicy(Type type)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        _members = new Lazy<IReadOnlyList<EditDraftMemberSpec>>(() => EditDraftMembers.Discover(this));
    }

    /// <summary>The concrete record type. Matching is exact: a subclass needs its own policy.</summary>
    public Type Type { get; }
    public string TypeName => Type.Name;

    public string PolicyId { get; init; }
    public int Version { get; init; } = 1;

    /// <summary>Log prefix, without brackets.</summary>
    public string LogTag { get; init; } = "EditDraft";

    /// <summary>Only members declared on this type or below it are candidates (not framework base-class stamps). Null = <see cref="Type"/>.</summary>
    public Type MemberDeclaringBase { get; init; }

    /// <summary>Never captured and never replayed.</summary>
    public IReadOnlySet<string> Excluded { get; init; } = NoNames;
    public IReadOnlyList<string> ExcludedPrefixes { get; init; } = Array.Empty<string>();

    /// <summary>Seeded for a NEW record at first capture; applied first, in this order (chart types only).</summary>
    public IReadOnlyList<string> ReconstructionOrder { get; init; } = Array.Empty<string>();

    /// <summary>(companion property, value member): a named path only, no traversal.</summary>
    public (string Companion, string Member)? Companion { get; init; }

    private readonly IReadOnlyList<EditDraftGroup> _groups = Array.Empty<EditDraftGroup>();

    /// <summary>
    /// The named dependency groups of this type, in apply order. A member belongs to at most ONE group:
    /// overlapping write dependencies are one group (design §4.3), so an overlap is refused here.
    /// </summary>
    public IReadOnlyList<EditDraftGroup> Groups
    {
        get => _groups;
        init => _groups = Validated(value);
    }

    private static IReadOnlyList<EditDraftGroup> Validated(IReadOnlyList<EditDraftGroup> groups)
    {
        if (groups == null) return Array.Empty<EditDraftGroup>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in groups)
        {
            if (g == null || string.IsNullOrWhiteSpace(g.Id)) throw new ArgumentException("An edit-draft group needs an id.");
            if (!ids.Add(g.Id)) throw new ArgumentException($"Edit-draft group id '{g.Id}' is declared twice.");
            if (g.Members == null || g.Members.Count < 2) throw new ArgumentException($"Edit-draft group '{g.Id}' needs a driver and at least one other member.");
            foreach (var m in g.Members)
            {
                if (string.IsNullOrWhiteSpace(m)) throw new ArgumentException($"Edit-draft group '{g.Id}' has an empty member.");
                if (!members.Add(m)) throw new ArgumentException($"Member '{m}' is in more than one edit-draft group (or twice in '{g.Id}'); overlapping dependencies are ONE group.");
            }
        }
        return groups.ToList();
    }

    /// <summary>Members whose setter changes ANOTHER record: selectable, never pre-ticked, 「他の記録も変わります」.</summary>
    public IReadOnlySet<string> SideEffectMembers { get; init; } = NoNames;

    /// <summary>Members shown but not restorable (戻せません) on an EXISTING record.</summary>
    public IReadOnlySet<string> NotRestorableOnExisting { get; init; } = NoNames;

    /// <summary>DetailView ids in which capture and restore may run. Null = no allowlist (the chart types, as today).</summary>
    public IReadOnlySet<string> ApprovedViewIds { get; init; }

    // ---- wave 1 (non-chart types); all null/empty for the chart policies -----------------------

    /// <summary>The per-member decision table (T3 gate). Null for the chart types (owner Q1: their name list stays).</summary>
    public IReadOnlyDictionary<string, EditDraftMemberDecision> Decisions { get; init; }

    /// <summary>The date shown beside the type caption in the list's 対象 column (design §3 S4c: caption + date, never a name). Null = none.</summary>
    public Func<object, DateTime?> ContextDateOf { get; init; }

    /// <summary>The approved DetailView ids as data (owner D17; the same set as <see cref="ApprovedViewIds"/>, empty when there is no allowlist).</summary>
    public IReadOnlySet<string> DetailViewIds => ApprovedViewIds ?? NoNames;

    /// <summary>
    /// The type's own ListView ids, as data (owner D14: the active tab shows the 「入力控」 header action
    /// filtered to this type; owner D17: wave 1b attaches ListView capture here without changing the
    /// policy shape). Wave 1 captures on DetailViews only. Nested ListViews inside a DetailView are
    /// never listed here.
    /// </summary>
    public IReadOnlySet<string> ListViewIds { get; init; } = NoNames;

    /// <summary>The configuration key of the per-type switch (design §6).</summary>
    public string SwitchKey => EditDraftSwitch.TypeKey(PolicyId);

    /// <summary>
    /// Getters that WRITE (KB fix-529; owner ruling 2026-10-01 "align with fix-529"): read-only properties
    /// whose getter fills another member when it is empty (chart example: TenantChartAccident.AccidentDateTime
    /// fills AccidentTime). The capture runs them ONCE, under capture suppression, BEFORE it takes the baseline,
    /// so the fill is baseline, not an edit; the restore re-check runs them on its fresh database read. Same
    /// concept as TenantChartDraftPolicy.InitializingGetters on the chart branch. Empty for the wave-1 types
    /// (no writing getter found in their sources). A name that does not resolve is ignored.
    /// </summary>
    public IReadOnlyList<string> InitializingGetters { get; init; } = Array.Empty<string>();

    // ---- NEW (never saved) records: design docs/edit-draft-new-records-design-2026-10-02.md, owner rulings 2026-10-03 ----

    /// <summary>
    /// NEW records (owner D9): when true, a never-saved record of this type is captured in its approved root DetailView too
    /// (while EditDraftCapture:NewRecords:Enabled is on) and can be recreated from the 「入力控」 list. Default false: no
    /// type takes part unless its policy opts in. Read for generic policies (IsGeneric) only.
    /// </summary>
    public bool AllowNewRecords { get; init; }

    /// <summary>
    /// NEW records (design §4.2.3): the members seeded (Seeded = true) at a new record's FIRST genuine edit and applied first,
    /// in this order, when the draft is recreated (EditDraftRestorer.ApplyNew) — the context the record's construction
    /// defaults depend on (残業・有給: 職員, 日付, 開始時刻, 終了時刻; 日付 before the times). Kept apart from
    /// <see cref="ReconstructionOrder"/> (the chart types' list), which also orders the EXISTING-record apply and admits
    /// non-browsable members: this list changes neither. Only members the policy admits are seeded. Empty = no seed.
    /// </summary>
    public IReadOnlyList<string> NewRecordReconstructionOrder { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Client-side journal (design Q2): DateTime members whose editor edits a time of day, journaled ONLY when listed here
    /// (their setters differ: one re-dates the value, another keeps the date). Empty = no DateTime member is journaled.
    /// </summary>
    public IReadOnlySet<string> JournalTimeOfDayMembers { get; init; } = NoNames;

    /// <summary>Every capturable member, reconstruction context first. Discovered once.</summary>
    public IReadOnlyList<EditDraftMemberSpec> Members => _members.Value;

    public EditDraftMemberSpec Find(string path) => Members.FirstOrDefault(m => m.Path == path);

    /// <summary>The id of the group <paramref name="path"/> belongs to, or null.</summary>
    public string GroupOf(string path) => Groups.FirstOrDefault(g => g.Members.Contains(path))?.Id;

    public bool HasSideEffect(string path) => SideEffectMembers.Contains(path);

    public bool IsNotRestorableOnExisting(string path) => NotRestorableOnExisting.Contains(path);

    /// <summary>
    /// True for a policy with a decision table (a generic type), false for a policy without one or null: the admission rule
    /// of the generic capture, restore and list. A host may keep policies without a decision table in the same registry for
    /// its own code; the library never admits them. (0.4.0-preview.1: the owner kind is gone; who owns a draft is the owner
    /// seam's answer, IEditDraftOwnerResolver, asked with the policy.)
    /// </summary>
    public static bool IsGeneric(EditDraftTypePolicy policy) =>
        policy != null && policy.Decisions != null;
}
