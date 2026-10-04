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
/// as a non-administrator.
/// </summary>
public class Updater : ModuleUpdater
{
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
            defaultRole.AddObjectPermissionFromLambda<ApplicationUser>(SecurityOperations.Read, cm => cm.Oid == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
            defaultRole.AddNavigationPermission(@"Application/NavigationItems/Items/Default/Items/MyDetails", SecurityPermissionState.Allow);
            defaultRole.AddMemberPermissionFromLambda<ApplicationUser>(SecurityOperations.Write, "ChangePasswordOnFirstLogon", cm => cm.Oid == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
            defaultRole.AddMemberPermissionFromLambda<ApplicationUser>(SecurityOperations.Write, "StoredPassword", cm => cm.Oid == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
            defaultRole.AddTypePermissionsRecursively<PermissionPolicyRole>(SecurityOperations.Read, SecurityPermissionState.Deny);
            defaultRole.AddObjectPermission<ModelDifference>(SecurityOperations.ReadWriteAccess, "UserId = ToStr(CurrentUserId())", SecurityPermissionState.Allow);
            defaultRole.AddObjectPermission<ModelDifferenceAspect>(SecurityOperations.ReadWriteAccess, "Owner.UserId = ToStr(CurrentUserId())", SecurityPermissionState.Allow);
            defaultRole.AddTypePermissionsRecursively<ModelDifference>(SecurityOperations.Create, SecurityPermissionState.Allow);
            defaultRole.AddTypePermissionsRecursively<ModelDifferenceAspect>(SecurityOperations.Create, SecurityPermissionState.Allow);

            // Sample: a non-administrator can read, create, edit and delete Notes and see them in the navigation.
            defaultRole.AddTypePermissionsRecursively<Note>(SecurityOperations.CRUDAccess, SecurityPermissionState.Allow);
            defaultRole.AddNavigationPermission(@"Application/NavigationItems/Items/Default/Items/Note_ListView", SecurityPermissionState.Allow);
        }
        return defaultRole;
    }
}
