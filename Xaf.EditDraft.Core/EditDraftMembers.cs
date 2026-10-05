using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;

namespace Xaf.EditDraft.Core;

/// <summary>One capturable member: a path on the record (or "Companion.Member"), its kind and caption.</summary>
public sealed record EditDraftMemberSpec(string Path, string Kind, Type ValueType, bool IsReconstruction, string Caption);

/// <summary>
/// Member discovery by policy, and reading a member on a live object. Reflection proposes the
/// candidates; the policy's lists decide (design §1.3).
/// </summary>
public static class EditDraftMembers
{
    /// <summary>
    /// Every capturable member under <paramref name="policy"/>, reconstruction context first. For a
    /// policy with a decision table (a generic type) only members with a recorded, non-Excluded
    /// decision are admitted: reflection proposes, the table decides (design §1.3; Codex diag C3).
    /// A policy without a table gets every candidate.
    /// </summary>
    internal static IReadOnlyList<EditDraftMemberSpec> Discover(EditDraftTypePolicy policy)
    {
        var candidates = Candidates(policy);
        var table = policy.Decisions;
        if (table == null) return candidates;
        return candidates
            .Where(m => table.TryGetValue(m.Path, out var d) && d.Disposition != EditDraftDisposition.Excluded)
            .ToList();
    }

    /// <summary>What reflection proposes under the policy's lists, BEFORE the decision table (the T3 gate compares against this).</summary>
    public static IReadOnlyList<EditDraftMemberSpec> Candidates(EditDraftTypePolicy policy)
    {
        var result = new List<EditDraftMemberSpec>();
        var type = policy.Type;
        var declaringBase = policy.MemberDeclaringBase ?? type;
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var p in props)
        {
            if (!declaringBase.IsAssignableFrom(p.DeclaringType)) continue;   // not BaseObject/CustomBaseObject stamps
            if (policy.Excluded.Contains(p.Name)) continue;
            if (policy.ExcludedPrefixes.Any(x => p.Name.StartsWith(x, StringComparison.Ordinal))) continue;
            if (!p.CanRead || p.SetMethod == null || !p.SetMethod.IsPublic || p.GetIndexParameters().Length > 0) continue;
            if (p.IsDefined(typeof(NonPersistentAttribute), true) || p.IsDefined(typeof(PersistentAliasAttribute), true)) continue;
            var kind = KindOf(p.PropertyType);
            if (kind == null) continue;

            var isContext = policy.ReconstructionOrder.Contains(p.Name);
            if (!isContext)
            {
                var browsable = p.GetCustomAttribute<BrowsableAttribute>(true);
                if (browsable != null && !browsable.Browsable) continue;
                var visible = p.GetCustomAttribute<VisibleInDetailViewAttribute>(true);
                if (visible != null && Equals(visible.Value, false)) continue;
            }
            result.Add(new EditDraftMemberSpec(p.Name, kind, p.PropertyType, isContext, CaptionOf(p)));
        }

        if (policy.Companion is { } c)
        {
            var cp = type.GetProperty(c.Companion);
            var vp = cp?.PropertyType.GetProperty(c.Member);
            var kind = vp == null ? null : KindOf(vp.PropertyType);
            if (kind != null)
                result.Add(new EditDraftMemberSpec(c.Companion + "." + c.Member, kind, vp.PropertyType, false, CaptionOf(vp)));
        }

        // Reconstruction context first, in the fixed order; then the rest by name (stable).
        var order = policy.ReconstructionOrder.ToList();
        return result
            .OrderBy(m => m.IsReconstruction ? order.IndexOf(m.Path) : int.MaxValue)
            .ThenBy(m => m.Path, StringComparer.Ordinal)
            .ToList();
    }

    private static string CaptionOf(PropertyInfo p)
    {
        foreach (var a in p.GetCustomAttributes<ModelDefaultAttribute>(true))
            if (a.PropertyName == "Caption" && !string.IsNullOrWhiteSpace(a.PropertyValue)) return a.PropertyValue;
        var dn = p.GetCustomAttribute<DevExpress.ExpressApp.DC.XafDisplayNameAttribute>(true);
        return string.IsNullOrWhiteSpace(dn?.DisplayName) ? p.Name : dn.DisplayName;
    }

    /// <summary>The codec kind of a member type, or null when it is not captured (collections, blobs, unknown objects).</summary>
    public static string KindOf(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        if (u == typeof(string)) return "string";
        if (u == typeof(bool)) return "bool";
        if (u.IsEnum) return "enum";
        if (u == typeof(int) || u == typeof(long) || u == typeof(short) || u == typeof(byte)) return "int";
        if (u == typeof(double) || u == typeof(float)) return "double";
        if (u == typeof(decimal)) return "decimal";
        if (u == typeof(DateTime)) return "datetime";
        if (u == typeof(TimeSpan)) return "timespan";
        if (u == typeof(Guid)) return "guid";
        if (typeof(BaseObject).IsAssignableFrom(u)) return "ref";
        return null;
    }

    /// <summary>
    /// Evaluates every <see cref="EditDraftTypePolicy.InitializingGetters"/> getter of <paramref name="record"/>
    /// (KB fix-529). Returns how many ran (0 for a null record/policy or an empty list). The caller
    /// suppresses capture around it.
    /// </summary>
    public static int RunInitializingGetters(EditDraftTypePolicy policy, object record)
    {
        if (policy == null || record == null) return 0;
        var ran = 0;
        foreach (var name in policy.InitializingGetters ?? Array.Empty<string>())
        {
            var p = record.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanRead || p.GetIndexParameters().Length > 0) continue;
            _ = p.GetValue(record);
            ran++;
        }
        return ran;
    }

    // ---- reading a member on a live object ----------------------------------------------------

    /// <summary>The object that owns <paramref name="path"/>: the record itself, or its companion.</summary>
    public static object OwnerOf(object record, string path, out string member)
    {
        member = path;
        if (record == null || path == null) return null;
        var dot = path.IndexOf('.');
        if (dot < 0) return record;
        member = path.Substring(dot + 1);
        return record.GetType().GetProperty(path.Substring(0, dot))?.GetValue(record);
    }

    public static object GetValue(object record, string path)
    {
        var owner = OwnerOf(record, path, out var member);
        return owner?.GetType().GetProperty(member)?.GetValue(owner);
    }

    /// <summary>The companion object of a record, so capture can recognise its ObjectChanged.</summary>
    public static object CompanionOf(EditDraftTypePolicy policy, object record)
    {
        if (record == null || policy?.Companion is not { } c) return null;
        return record.GetType().GetProperty(c.Companion)?.GetValue(record);
    }

    /// <summary>The path a change on <paramref name="changed"/>.<paramref name="property"/> maps to, or null.</summary>
    public static string PathFor(EditDraftTypePolicy policy, object record, object changed, string property)
    {
        if (policy == null || record == null || changed == null || string.IsNullOrEmpty(property)) return null;
        if (ReferenceEquals(changed, record)) return policy.Find(property) != null ? property : null;
        if (policy.Companion is { } c && ReferenceEquals(changed, CompanionOf(policy, record)) && property == c.Member)
            return c.Companion + "." + c.Member;
        return null;
    }
}
