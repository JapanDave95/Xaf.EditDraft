using DevExpress.ExpressApp.ConditionalAppearance;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;

namespace Xaf.EditDraft.Sample.Module.BusinessObjects;

/// <summary>The DevExpress template's login-info class (unchanged apart from the namespace).</summary>
[DeferredDeletion(false)]
[Persistent("PermissionPolicyUserLoginInfo")]
public class ApplicationUserLoginInfo : BaseObject, ISecurityUserLoginInfo
{
    private string _loginProviderName;
    private ApplicationUser _user;
    private string _providerUserKey;

    public ApplicationUserLoginInfo(Session session) : base(session) { }

    [Indexed("ProviderUserKey", Unique = true)]
    [Appearance("PasswordProvider", Enabled = false, Criteria = "!(IsNewObject(this)) and LoginProviderName == '" + SecurityDefaults.PasswordAuthentication + "'", Context = "DetailView")]
    public string LoginProviderName
    {
        get => _loginProviderName;
        set => SetPropertyValue(nameof(LoginProviderName), ref _loginProviderName, value);
    }

    [Appearance("PasswordProviderUserKey", Enabled = false, Criteria = "!(IsNewObject(this)) and LoginProviderName == '" + SecurityDefaults.PasswordAuthentication + "'", Context = "DetailView")]
    public string ProviderUserKey
    {
        get => _providerUserKey;
        set => SetPropertyValue(nameof(ProviderUserKey), ref _providerUserKey, value);
    }

    [Association("User-LoginInfo")]
    public ApplicationUser User
    {
        get => _user;
        set => SetPropertyValue(nameof(User), ref _user, value);
    }

    object ISecurityUserLoginInfo.User => User;
}
