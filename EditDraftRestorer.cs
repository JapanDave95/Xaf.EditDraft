using System;
using System.Collections.Generic;
using System.Linq;
using DevExpress.ExpressApp;

namespace Xaf.EditDraft.Core;

/// <summary>
/// Builds the restore plan for a draft and puts chosen values back on a record, UNSAVED (fill in,
/// the user saves). Every decision comes from the record type's <see cref="EditDraftTypePolicy"/>;
/// a null policy (a type that is not registered) offers nothing and applies nothing.
/// The plan items are the neutral <see cref="EditDraftRestoreRow"/>; a host maps them to its popup row type.
/// </summary>
public static class EditDraftRestorer
{
    /// <summary>Plan items for <paramref name="payload"/> against <paramref name="live"/> (null = NEW, nothing to compare with).</summary>
    public static List<EditDraftRestoreRow> BuildItems(EditDraftTypePolicy policy, EditDraftPayload payload, object live, bool isNew)
    {
        var items = new List<EditDraftRestoreRow>();
        if (payload == null) return items;

        foreach (var e in payload.Entries)
        {
            var spec = policy?.Find(e.Path);
            EditDraftItemStatus status;
            string currentText = string.Empty;
            string currentRawShown = null;   // the value the person reviewed (apply re-checks against it, C7)
            if (spec == null) status = EditDraftItemStatus.Unavailable;
            else if (!isNew && policy.IsNotRestorableOnExisting(e.Path))
                status = EditDraftItemStatus.Unavailable;   // its setter rewrites other members (C6)
            else if (isNew) status = EditDraftItemStatus.New;
            else
            {
                object current = null;
                var exists = live != null;
                try { current = EditDraftMembers.GetValue(live, e.Path); }
                catch { exists = false; }
                var currentRaw = EditDraftCodec.RawOf(current);
                currentRawShown = currentRaw;
                currentText = EditDraftDisplay.TextOf(current);
                status = EditDraftComparison.Classify(exists, e.BaseKnown, e.BaseRaw, e.ValueRaw, currentRaw);
            }

            var side = policy != null && policy.HasSideEffect(e.Path);
            items.Add(new EditDraftRestoreRow
            {
                Path = e.Path,
                Group = policy?.GroupOf(e.Path),
                Label = e.Caption ?? e.Path,
                ChangeText = EditDraftDisplay.Short(
                    isNew || e.Seeded ? (string.IsNullOrEmpty(e.ValueText) ? EditDraftTexts.Of(t => t.Empty) : e.ValueText)
                                      : EditDraftDisplay.ChangeText(e.BaseText, e.ValueText, e.BaseKnown)),
                CurrentText = status == EditDraftItemStatus.Conflict || status == EditDraftItemStatus.Unverifiable
                    ? EditDraftDisplay.Short(string.IsNullOrEmpty(currentText) ? EditDraftTexts.Of(t => t.Empty) : currentText) : string.Empty,
                CurrentRaw = currentRawShown,
                StatusText = EditDraftComparison.StatusText(status, side),
                StatusCode = (int)status,
                Selectable = EditDraftComparison.IsSelectable(status),
                SideEffect = side,
                Selected = EditDraftComparison.IsPreSelected(status, side)
            });
        }

        // Group rule: one tick per group; a conflict/unverifiable/side-effect member unticks it.
        foreach (var g in items.Where(i => i.Group != null).GroupBy(i => i.Group))
        {
            var on = EditDraftComparison.GroupPreSelected(
                g.Select(i => ((EditDraftItemStatus)i.StatusCode, i.SideEffect)));
            foreach (var i in g) i.Selected = on && i.Selectable;
        }
        return items;
    }

    /// <summary>
    /// The order values go back: reconstruction context (in the policy's fixed order), then the group
    /// drivers in group order, then everything else, then companions, then — group by group — the
    /// other group members again as explicit final values (a driver's setter may have overwritten them).
    /// </summary>
    public static List<string> ApplyOrder(EditDraftTypePolicy policy, IEnumerable<string> paths)
    {
        var set = paths.Distinct(StringComparer.Ordinal).ToList();
        var ordered = new List<string>();
        if (policy != null)
            foreach (var p in policy.ReconstructionOrder) if (set.Contains(p)) ordered.Add(p);
        var groups = policy?.Groups ?? Array.Empty<EditDraftGroup>();
        foreach (var g in groups)
            if (set.Contains(g.Driver) && !ordered.Contains(g.Driver)) ordered.Add(g.Driver);
        foreach (var p in set.OrderBy(x => x, StringComparer.Ordinal))
            if (!ordered.Contains(p) && !p.Contains('.')) ordered.Add(p);
        foreach (var p in set.Where(x => x.Contains('.'))) if (!ordered.Contains(p)) ordered.Add(p);
        foreach (var g in groups)
            foreach (var p in g.Members.Skip(1)) if (set.Contains(p)) ordered.Add(p);   // final values, again
        return ordered;
    }

    /// <summary>
    /// Assigns the chosen entries through the SAME public setters a person's edit uses. References
    /// are resolved in <paramref name="objectSpace"/> (the destination, secured); a reference that no
    /// longer resolves is NOT replaced by null — it is reported as failed.
    /// </summary>
    public static (int Applied, int Failed, List<string> AppliedPaths) Apply(
        EditDraftTypePolicy policy, IObjectSpace objectSpace, object record, EditDraftPayload payload, ICollection<string> chosenPaths)
    {
        var applied = new List<string>();
        var failed = 0;
        if (objectSpace == null || record == null || payload == null) return (0, 0, applied);
        var tag = policy?.LogTag ?? "EditDraft";

        // Codex diff review C6: a group moves whole. Every drafted member of a touched group is
        // applied (an AlreadyApplied member the driver would overwrite comes back as its draft value),
        // and a group member that is NOT drafted is put back to its value from before the driver ran,
        // so "not ticked" really means "unchanged".
        var expanded = ExpandGroups(policy, chosenPaths, payload);
        var preserved = new List<(string Path, object Value)>();
        // Every touched group's undrafted members are read BEFORE any setter runs (not group by group).
        foreach (var g in policy?.Groups ?? Array.Empty<EditDraftGroup>())
        {
            if (!expanded.Any(p => g.Members.Contains(p))) continue;
            foreach (var m in g.Members)
            {
                if (expanded.Contains(m)) continue;
                try { preserved.Add((m, EditDraftMembers.GetValue(record, m))); }
                catch { }
            }
        }

        foreach (var path in ApplyOrder(policy, expanded))
        {
            var e = payload.Get(path);
            var spec = policy?.Find(path);
            if (e == null || spec == null) { failed++; continue; }
            try
            {
                var owner = EditDraftMembers.OwnerOf(record, path, out var member);
                var prop = owner?.GetType().GetProperty(member);
                if (prop == null || prop.SetMethod == null) { failed++; continue; }

                object value;
                if (spec.Kind == "ref")
                {
                    if (e.ValueRaw == null) value = null;                         // deliberately cleared
                    else
                    {
                        value = objectSpace.GetObjectByKey(prop.PropertyType, Guid.Parse(e.ValueRaw));
                        if (value == null) { failed++; continue; }               // gone or not readable: not nulled
                    }
                }
                else value = EditDraftCodec.Parse(e.ValueRaw, prop.PropertyType);

                prop.SetValue(owner, value);
                if (!applied.Contains(path)) applied.Add(path);
            }
            catch (Exception ex)
            {
                failed++;
                EditDraftLog.Warning($"[{tag}] could not restore {path}: {ex.GetType().Name}");
            }
        }

        foreach (var (path, value) in preserved)
        {
            try
            {
                var owner = EditDraftMembers.OwnerOf(record, path, out var member);
                var prop = owner?.GetType().GetProperty(member);
                if (prop?.SetMethod == null) continue;
                if (Equals(prop.GetValue(owner), value)) continue;
                prop.SetValue(owner, value);
            }
            catch (Exception ex)
            {
                failed++;
                EditDraftLog.Warning($"[{tag}] could not keep undrafted {path} unchanged: {ex.GetType().Name}");
            }
        }
        return (applied.Count, failed, applied);
    }

    /// <summary>
    /// The chosen paths plus every OTHER drafted member of any group a chosen path belongs to
    /// (Codex diff review C6). Paths outside a group are returned as chosen.
    /// </summary>
    public static List<string> ExpandGroups(EditDraftTypePolicy policy, IEnumerable<string> chosenPaths, EditDraftPayload payload)
    {
        var result = (chosenPaths ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).ToList();
        if (payload == null || policy == null) return result;
        foreach (var g in policy.Groups)
        {
            if (!result.Any(p => g.Members.Contains(p))) continue;   // only a group a chosen path belongs to
            foreach (var m in g.Members)
                if (!result.Contains(m) && payload.Get(m) != null) result.Add(m);
        }
        return result;
    }

    /// <summary>
    /// Wave 1 (existing records only): the paths of <paramref name="chosenPaths"/> that may be assigned
    /// to an EXISTING record. Group expansion is applied first, then every 戻せません member
    /// (<see cref="EditDraftTypePolicy.NotRestorableOnExisting"/>) and every path without a member spec
    /// is dropped. <see cref="Apply"/> itself assigns whatever it is given (the chart NEW-record path
    /// needs that), so the generic controllers call <see cref="ApplyExisting"/>, which enforces the
    /// disposition at the engine, not only in the popup's checkbox state (Codex diag C2).
    /// </summary>
    public static List<string> AssignableOnExisting(EditDraftTypePolicy policy, IEnumerable<string> chosenPaths, EditDraftPayload payload)
    {
        if (policy == null || payload == null) return new List<string>();
        var expanded = ExpandGroups(policy, chosenPaths, payload);
        // Apply re-expands groups from whatever it is given (Codex review C2): a group that holds a
        // drafted 戻せません member is therefore dropped WHOLE here, so no path of it is left for the
        // re-expansion to grow from. (The T3 gate also refuses such a policy; this is the engine's own guard.)
        var droppedGroups = policy.Groups
            .Where(g => g.Members.Any(m => policy.IsNotRestorableOnExisting(m) && payload.Get(m) != null))
            .ToList();
        return expanded
            .Where(p => policy.Find(p) != null && !policy.IsNotRestorableOnExisting(p) && payload.Get(p) != null)
            .Where(p => !droppedGroups.Any(g => g.Members.Contains(p)))
            .ToList();
    }

    /// <summary>Apply to an EXISTING record with the 戻せません members dropped (see <see cref="AssignableOnExisting"/>).</summary>
    public static (int Applied, int Failed, List<string> AppliedPaths) ApplyExisting(
        EditDraftTypePolicy policy, IObjectSpace objectSpace, object record, EditDraftPayload payload, ICollection<string> chosenPaths)
    {
        var assignable = AssignableOnExisting(policy, chosenPaths, payload);
        if (assignable.Count == 0) return (0, 0, new List<string>());
        return Apply(policy, objectSpace, record, payload, assignable);
    }
}
