using System;
using System.Collections.Generic;
using System.Linq;
using DevExpress.ExpressApp;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.PermissionPolicy;

namespace Xaf.EditDraft.Core;

/// <summary>A role that can read the store through ordinary XAF security, and why.</summary>
public sealed record EditDraftRoleExposure(string RoleName, string Reason);

/// <summary>
/// The store's security obligation (gaps G2/G3, 2026-10-04). SINGLE-MODEL (binding guardrails Part 4, owner review).
///
/// The library's writer never goes through XAF security for the store: it works on a NON-SECURED object space and every
/// read, update and delete names the owner (<see cref="EditDraftStoreBase.OwnerUserOid"/>) in the statement. That owner
/// condition is what keeps one login's drafts from another's. A role, however, can still reach the store class through
/// ordinary XAF security (a generic ListView, a lookup, the Web API), where only its permissions apply; the payload is the
/// typed text in readable JSON. The consumer therefore denies the store class to every role:
/// <see cref="DenyStoreToAllRoles"/> in its ModuleUpdater, on every database update.
///
/// What a type deny does NOT cover (stated here, in the consumer guide, and logged at startup by
/// <see cref="WarnRolesThatCanReadStore"/>):
/// - a role with IsAdministrative = true: "You cannot deny any rights for a role with the Administrative Permission"
///   (XAF 26.1, Type, Object and Member Permissions). The rows are still written, so they apply if the flag is cleared;
/// - object or member permissions that ALLOW access to the store: XAF applies them over a type deny (XAF 26.1, Access the
///   Currently Logged User ...: a type Read deny with an object Read allow). The helper leaves them in place;
/// - a role created after the last database update: it has no deny until the next update runs the helper again.
///
/// <see cref="FindRolesThatCanReadStore"/> reads the role ROWS (IsAdministrative, PermissionPolicy, type permissions on the
/// store and on its base types, object and member permissions); it does not run XAF's permission evaluation for a user,
/// and it is conservative: a base-type ALLOW without a deny on the store itself is reported. It is best-effort (Codex C5/C6,
/// documented 2026-10-04): roles of a class that does not derive from the scanned role type (the startup warning scans
/// <see cref="PermissionPolicyRoleBase"/>) are not scanned, and the criteria of object and member grants are not evaluated,
/// so a grant whose criterion never matches is still reported.
/// XPO roles only (contract v1): the default role type is <see cref="PermissionPolicyRoleBase"/> (covers PermissionPolicyRole
/// and subclasses); pass another role type that implements <see cref="IPermissionPolicyRole"/> if the application has one.
/// </summary>
public static class EditDraftSecurity
{
    /// <summary>
    /// Gives every role of <paramref name="roleType"/> (default <see cref="PermissionPolicyRoleBase"/>) in
    /// <paramref name="objectSpace"/> an explicit DENY of Read, Write, Create, Delete and Navigate on
    /// <paramref name="storeType"/>, reusing the role's existing type permission row for that type (idempotent; the same
    /// row PermissionSettingHelper.AddTypePermission would write). Does not commit: the caller (the ModuleUpdater) commits.
    /// Logs a warning for each role the deny cannot bind (see the class summary). Returns the number of roles.
    /// </summary>
    public static int DenyStoreToAllRoles(IObjectSpace objectSpace, Type storeType, Type roleType = null)
    {
        if (objectSpace == null) throw new ArgumentNullException(nameof(objectSpace));
        CheckStore(storeType);
        var roles = Roles(objectSpace, roleType);
        foreach (var role in roles)
        {
            var permission = role.TypePermissions.FirstOrDefault(p => TargetOf(p, storeType) == storeType) ?? role.CreateTypePermissionObject(storeType);
            permission.ReadState = SecurityPermissionState.Deny;
            permission.WriteState = SecurityPermissionState.Deny;
            permission.CreateState = SecurityPermissionState.Deny;
            permission.DeleteState = SecurityPermissionState.Deny;
            permission.NavigateState = SecurityPermissionState.Deny;
        }
        foreach (var e in Exposures(roles, storeType))
            EditDraftLog.Warning($"[EditDraft] store {storeType.FullName}: role '{e.RoleName}' may still be able to read it after the deny: {e.Reason} (best-effort scan; see the consumer guide, section 5)");
        return roles.Count;
    }

    /// <summary>The roles that can read <paramref name="storeType"/> through ordinary XAF security, from their rows (see the class summary).</summary>
    public static IReadOnlyList<EditDraftRoleExposure> FindRolesThatCanReadStore(IObjectSpace objectSpace, Type storeType, Type roleType = null)
    {
        if (objectSpace == null) throw new ArgumentNullException(nameof(objectSpace));
        CheckStore(storeType);
        return Exposures(Roles(objectSpace, roleType), storeType).ToList();
    }

    /// <summary>
    /// Logs one warning per role that may read <paramref name="storeType"/>; returns their number. Never throws for a role's
    /// content. Best-effort (Codex C5/C6, documented 2026-10-04): it scans only roles of <paramref name="roleType"/> (default
    /// <see cref="PermissionPolicyRoleBase"/> and its subclasses; other role classes are not scanned) and does not evaluate
    /// the criteria of object or member grants (a grant whose criterion never matches is still reported).
    /// </summary>
    public static int WarnRolesThatCanReadStore(IObjectSpace objectSpace, Type storeType, Type roleType = null)
    {
        var exposures = FindRolesThatCanReadStore(objectSpace, storeType, roleType);
        foreach (var e in exposures)
            EditDraftLog.Warning($"[EditDraft] store {storeType.FullName}: role '{e.RoleName}' may be able to read it through XAF security: {e.Reason}. " +
                                 "Fix: run EditDraftSecurity.DenyStoreToAllRoles in the ModuleUpdater; see the consumer guide for the cases a deny cannot cover. " +
                                 $"This scan is best-effort: it reads the rows of {(roleType ?? typeof(PermissionPolicyRoleBase)).Name} roles and their subclasses only " +
                                 "(other role classes are not scanned) and does not evaluate the criteria of object or member grants.");
        return exposures.Count;
    }

    private static void CheckStore(Type storeType)
    {
        if (storeType == null) throw new ArgumentNullException(nameof(storeType));
        if (!typeof(EditDraftStoreBase).IsAssignableFrom(storeType) || storeType.IsAbstract)
            throw new ArgumentException($"{storeType.FullName} is not a concrete subclass of {nameof(EditDraftStoreBase)}.", nameof(storeType));
    }

    private static List<IPermissionPolicyRole> Roles(IObjectSpace objectSpace, Type roleType)
    {
        roleType ??= typeof(PermissionPolicyRoleBase);
        if (!typeof(IPermissionPolicyRole).IsAssignableFrom(roleType))
            throw new ArgumentException($"{roleType.FullName} does not implement {nameof(IPermissionPolicyRole)}.", nameof(roleType));
        return objectSpace.GetObjects(roleType).Cast<object>().OfType<IPermissionPolicyRole>().ToList();
    }

    /// <summary>At most one exposure per role, the first that applies: administrative, an object/member ALLOW, a type ALLOW, the policy default.</summary>
    private static IEnumerable<EditDraftRoleExposure> Exposures(IEnumerable<IPermissionPolicyRole> roles, Type storeType)
    {
        foreach (var role in roles)
        {
            var name = role.Name ?? "(no name)";
            if (role.IsAdministrative)
            {
                yield return new EditDraftRoleExposure(name, "IsAdministrative = true; XAF does not apply deny rows to an administrative role");
                continue;
            }
            var relevant = role.TypePermissions
                .Select(p => (Row: p, Target: TargetOf(p, storeType)))
                .Where(x => x.Target != null && x.Target.IsAssignableFrom(storeType))
                .OrderBy(x => Distance(storeType, x.Target))
                .ToList();
            var objectGrant = relevant.FirstOrDefault(x => x.Row.ObjectPermissions.Any(o => o.ReadState == SecurityPermissionState.Allow));
            if (objectGrant.Row != null)
            {
                yield return new EditDraftRoleExposure(name, $"an object permission on {objectGrant.Target.FullName} allows Read; XAF applies it over a type deny");
                continue;
            }
            var memberGrant = relevant.FirstOrDefault(x => x.Row.MemberPermissions.Any(m => m.ReadState == SecurityPermissionState.Allow));
            if (memberGrant.Row != null)
            {
                yield return new EditDraftRoleExposure(name, $"a member permission on {memberGrant.Target.FullName} allows Read; XAF applies it over a type deny");
                continue;
            }
            var nearest = relevant.FirstOrDefault(x => x.Row.ReadState != null);
            if (nearest.Row?.ReadState == SecurityPermissionState.Allow)
            {
                yield return new EditDraftRoleExposure(name, nearest.Target == storeType
                    ? "a type permission on the store allows Read"
                    : $"a type permission on {nearest.Target.FullName} allows Read and the store has no Read deny of its own");
                continue;
            }
            if (nearest.Row == null && role.PermissionPolicy != SecurityPermissionPolicy.DenyAllByDefault)
                yield return new EditDraftRoleExposure(name, $"PermissionPolicy = {role.PermissionPolicy} and no type permission denies Read on the store");
        }
    }

    /// <summary>
    /// The row's target type. XPO's PermissionPolicyTypePermissionObject stores the type's FULL NAME and resolves it through
    /// the object space's types info, or XafTypesInfo.Instance for a row that has no object space yet (a row just created by
    /// CreateTypePermissionObject); where that types info does not know the type, TargetType is null although the row names
    /// it. Such a row is matched by its stored full name against <paramref name="storeType"/> and its base types.
    /// </summary>
    private static Type TargetOf(IPermissionPolicyTypePermissionObject row, Type storeType)
    {
        if (row.TargetType != null) return row.TargetType;
        var name = (row as PermissionPolicyTypePermissionObject)?.TargetTypeFullName;
        if (string.IsNullOrEmpty(name)) return null;
        for (var t = storeType; t != null; t = t.BaseType)
            if (string.Equals(t.FullName, name, StringComparison.Ordinal)) return t;
        return null;
    }

    /// <summary>Inheritance steps from <paramref name="type"/> up to <paramref name="ancestor"/> (0 = the same type; interfaces last).</summary>
    private static int Distance(Type type, Type ancestor)
    {
        var steps = 0;
        for (var t = type; t != null; t = t.BaseType, steps++)
            if (t == ancestor) return steps;
        return int.MaxValue;
    }
}
