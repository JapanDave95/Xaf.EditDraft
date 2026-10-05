using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using DevExpress.ExpressApp.SystemModule;
using DevExpress.ExpressApp.Updating;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Persistent.BaseImpl.PermissionPolicy;
using Microsoft.Extensions.DependencyInjection;
using Xaf.EditDraft.Core;
using Xaf.EditDraft.Sample.Module.BusinessObjects;

namespace Xaf.EditDraft.Sample.Module.DatabaseUpdate;

/// <summary>
/// The DevExpress template's updater, with two additions for the sample:
/// 1. No business data is seeded: the Note list starts empty.
/// 2. The draft store's security obligation: every role gets an explicit DENY of every operation on SampleEditDraft
///    (<see cref="DenyDraftStoreToEveryRole"/>, the library's EditDraftSecurity.DenyStoreToAllRoles), on every update, in
///    every build configuration.
/// The template's test users and roles (Admin and User, empty passwords) are created as the template creates them,
/// only in non-RELEASE builds; the Default role additionally gets full access to Note so "User" can try the feature
/// as a non-administrator. A third test user, "Restricted" (empty password, non-RELEASE builds only), is in the
/// RestrictedNotes role (<see cref="CreateRestrictedNotesRole"/>): its Note permission has an object criterion, which shows
/// that XAF security alone decides who may restore and recreate a Note (Xaf.EditDraft 0.4.0-preview.1).
/// </summary>
public class Updater : ModuleUpdater
{
    /// <summary>The role whose Note Write permission holds only where Priority is not High.</summary>
    public const string RestrictedRoleName = "RestrictedNotes";

    /// <summary>The test user in <see cref="RestrictedRoleName"/> (empty password, non-RELEASE builds only).</summary>
    public const string RestrictedUserName = "Restricted";

    public Updater(IObjectSpace objectSpace, Version currentDBVersion) : base(objectSpace, currentDBVersion) { }

    public override void UpdateDatabaseAfterUpdateSchema()
    {
        base.UpdateDatabaseAfterUpdateSchema();
        // No Note is seeded.

#if !RELEASE
        // The template's test users and roles. In production, create users as described in
        // https://docs.devexpress.com/eXpressAppFramework/119064/data-security-and-safety/security-system/authentication
        var defaultRole = CreateDefaultRole();
        var adminRole = CreateAdminRole();
        var restrictedRole = CreateRestrictedNotesRole(ObjectSpace);
        ObjectSpace.CommitChanges();

        var userManager = ObjectSpace.ServiceProvider.GetRequiredService<UserManager>();
        if (userManager.FindUserByName<ApplicationUser>(ObjectSpace, "User") == null)
        {
            const string emptyPassword = "";
            _ = userManager.CreateUser<ApplicationUser>(ObjectSpace, "User", emptyPassword, user => user.Roles.Add(defaultRole));
        }
        if (userManager.FindUserByName<ApplicationUser>(ObjectSpace, "Admin") == null)
        {
            const string emptyPassword = "";
            _ = userManager.CreateUser<ApplicationUser>(ObjectSpace, "Admin", emptyPassword, user => user.Roles.Add(adminRole));
        }
        if (userManager.FindUserByName<ApplicationUser>(ObjectSpace, RestrictedUserName) == null)
        {
            const string emptyPassword = "";
            _ = userManager.CreateUser<ApplicationUser>(ObjectSpace, RestrictedUserName, emptyPassword, user => user.Roles.Add(restrictedRole));
        }
        ObjectSpace.CommitChanges();
#endif

        DenyDraftStoreToEveryRole(ObjectSpace);
        ObjectSpace.CommitChanges();
    }

    /// <summary>
    /// The store's security obligation (docs/consumer-guide.md, "Security"), through the library helper
    /// <see cref="EditDraftSecurity.DenyStoreToAllRoles"/>: every role in <paramref name="objectSpace"/> gets an explicit
    /// DENY of Read, Write, Create, Delete and Navigate on <see cref="SampleEditDraft"/>; returns the number of roles.
    /// Idempotent (the role's existing permission row for the type is reused). The helper logs each role the deny cannot
    /// bind: an administrative role, and a role with an object or member ALLOW grant on the store. A role created after
    /// this update has no deny until the next update; the library logs it at startup.
    /// The library's writer does not go through XAF security for this table (non-secured object space, owner-filtered
    /// statements), so the deny does not stop capture, restore or the drafts list.
    /// </summary>
    public static int DenyDraftStoreToEveryRole(IObjectSpace objectSpace) =>
        EditDraftSecurity.DenyStoreToAllRoles(objectSpace, typeof(SampleEditDraft));

    private PermissionPolicyRole CreateAdminRole()
    {
        var adminRole = ObjectSpace.FirstOrDefault<PermissionPolicyRole>(r => r.Name == "Administrators");
        if (adminRole == null)
        {
            adminRole = ObjectSpace.CreateObject<PermissionPolicyRole>();
            adminRole.Name = "Administrators";
            adminRole.IsAdministrative = true;
        }
        return adminRole;
    }

    private PermissionPolicyRole CreateDefaultRole()
    {
        var defaultRole = ObjectSpace.FirstOrDefault<PermissionPolicyRole>(role => role.Name == "Default");
        if (defaultRole == null)
        {
            defaultRole = ObjectSpace.CreateObject<PermissionPolicyRole>();
            defaultRole.Name = "Default";

            // The template's Default role permissions.
            AddTemplateUserPermissions(defaultRole);

            // Sample: a non-administrator can read, create, edit and delete Notes and see them in the navigation.
            defaultRole.AddTypePermissionsRecursively<Note>(SecurityOperations.CRUDAccess, SecurityPermissionState.Allow);
            defaultRole.AddNavigationPermission(@"Application/NavigationItems/Items/Default/Items/Note_ListView", SecurityPermissionState.Allow);
        }
        return defaultRole;
    }

    /// <summary>
    /// The RestrictedNotes role (created once, found by name afterwards): the template's user permissions, and on Note
    /// Read and Create for every Note but Write only where Priority is not High (an object permission with a criterion);
    /// no Delete. XAF then lets a member open every Note but edit only those that are not High, and save a new Note only
    /// when it is not High (XAF checks Create, Write and Read on a new object before it saves it). Xaf.EditDraft asks the
    /// same permissions: a draft restores onto a Note only where Write is granted, and a new Note is recreated from a draft
    /// only when Create, Write and Read are granted on the rebuilt Note. Public so Xaf.EditDraft.Sample.Tests checks this
    /// exact role. Does not commit.
    /// </summary>
    public static PermissionPolicyRole CreateRestrictedNotesRole(IObjectSpace objectSpace)
    {
        if (objectSpace == null) throw new ArgumentNullException(nameof(objectSpace));
        var role = objectSpace.FirstOrDefault<PermissionPolicyRole>(r => r.Name == RestrictedRoleName);
        if (role != null) return role;
        role = objectSpace.CreateObject<PermissionPolicyRole>();
        role.Name = RestrictedRoleName;
        AddTemplateUserPermissions(role);
        role.AddTypePermissionsRecursively<Note>(SecurityOperations.Read + SecurityOperations.Delimiter + SecurityOperations.Create, SecurityPermissionState.Allow);
        role.AddObjectPermissionFromLambda<Note>(SecurityOperations.Write, n => n.Priority != NotePriority.High, SecurityPermissionState.Allow);
        role.AddNavigationPermission(@"Application/NavigationItems/Items/Default/Items/Note_ListView", SecurityPermissionState.Allow);
        return role;
    }

    /// <summary>The template's Default role permissions (own user record, My Details, password change, model differences).</summary>
    private static void AddTemplateUserPermissions(PermissionPolicyRole role)
    {
        role.AddObjectPermissionFromLambda<ApplicationUser>(SecurityOperations.Read, cm => cm.Oid == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
        role.AddNavigationPermission(@"Application/NavigationItems/Items/Default/Items/MyDetails", SecurityPermissionState.Allow);
        role.AddMemberPermissionFromLambda<ApplicationUser>(SecurityOperations.Write, "ChangePasswordOnFirstLogon", cm => cm.Oid == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
        role.AddMemberPermissionFromLambda<ApplicationUser>(SecurityOperations.Write, "StoredPassword", cm => cm.Oid == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
        role.AddTypePermissionsRecursively<PermissionPolicyRole>(SecurityOperations.Read, SecurityPermissionState.Deny);
        role.AddObjectPermission<ModelDifference>(SecurityOperations.ReadWriteAccess, "UserId = ToStr(CurrentUserId())", SecurityPermissionState.Allow);
        role.AddObjectPermission<ModelDifferenceAspect>(SecurityOperations.ReadWriteAccess, "Owner.UserId = ToStr(CurrentUserId())", SecurityPermissionState.Allow);
        role.AddTypePermissionsRecursively<ModelDifference>(SecurityOperations.Create, SecurityPermissionState.Allow);
        role.AddTypePermissionsRecursively<ModelDifferenceAspect>(SecurityOperations.Create, SecurityPermissionState.Allow);
    }
}
