using System.ComponentModel;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.PermissionPolicy;
using DevExpress.Xpo;

namespace Xaf.EditDraft.Sample.Module.BusinessObjects;

/// <summary>
/// The DevExpress template's user class (unchanged apart from the namespace): a PermissionPolicyUser with login info
/// and lockout. Its key is a Guid (Oid), which is what the library's default owner seam needs: the draft's owner is
/// SecuritySystem.CurrentUserId when it is a non-empty Guid.
/// </summary>
[MapInheritance(MapInheritanceType.ParentTable)]
[DefaultProperty(nameof(UserName))]
public class ApplicationUser : PermissionPolicyUser, ISecurityUserWithLoginInfo, ISecurityUserLockout
{
    private int _accessFailedCount;
    private DateTime _lockoutEnd;

    public ApplicationUser(Session session) : base(session) { }

    [Browsable(false)]
    public int AccessFailedCount
    {
        get => _accessFailedCount;
        set => SetPropertyValue(nameof(AccessFailedCount), ref _accessFailedCount, value);
    }

    [Browsable(false)]
    public DateTime LockoutEnd
    {
        get => _lockoutEnd;
        set => SetPropertyValue(nameof(LockoutEnd), ref _lockoutEnd, value);
    }

    [Browsable(false)]
    [NonCloneable]
    [Aggregated, Association("User-LoginInfo")]
    public XPCollection<ApplicationUserLoginInfo> LoginInfo => GetCollection<ApplicationUserLoginInfo>(nameof(LoginInfo));

    IEnumerable<ISecurityUserLoginInfo> IOAuthSecurityUser.UserLogins => LoginInfo.OfType<ISecurityUserLoginInfo>();

    ISecurityUserLoginInfo ISecurityUserWithLoginInfo.CreateUserLoginInfo(string loginProviderName, string providerUserKey)
    {
        var result = new ApplicationUserLoginInfo(Session)
        {
            LoginProviderName = loginProviderName,
            ProviderUserKey = providerUserKey,
            User = this
        };
        return result;
    }
}
