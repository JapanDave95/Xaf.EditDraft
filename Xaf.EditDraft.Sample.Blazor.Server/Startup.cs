using DevExpress.ExpressApp.ApplicationBuilder;
using DevExpress.ExpressApp.Blazor.ApplicationBuilder;
using DevExpress.ExpressApp.Blazor.Services;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.BaseImpl.PermissionPolicy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Xaf.EditDraft.Blazor;
using Xaf.EditDraft.Core;
using Xaf.EditDraft.Sample.Blazor.Server.Services;
using Xaf.EditDraft.Sample.Module.BusinessObjects;
using Xaf.EditDraft.Sample.Module.EditDrafts;

namespace Xaf.EditDraft.Sample.Blazor.Server;

/// <summary>
/// The DevExpress template's Startup with the Xaf.EditDraft additions marked "Xaf.EditDraft". Everything a consumer must
/// supply is here or in the files it names; everything else is a library default.
/// </summary>
public class Startup
{
    public Startup(IConfiguration configuration) => Configuration = configuration;

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(Microsoft.AspNetCore.SignalR.HubConnectionHandler<>), typeof(ProxyHubConnectionHandler<>));

        services.AddRazorPages();
        services.AddServerSideBlazor();
        services.AddHttpContextAccessor();
        services.AddScoped<CircuitHandler, CircuitHandlerProxy>();

        // Xaf.EditDraft: the store, the policy registry and the Blazor per-circuit services.
        AddEditDrafts(services);

        services.AddXaf(Configuration, builder =>
        {
            builder.UseApplication<SampleBlazorApplication>();
            builder.Modules
                .Add<Module.SampleModule>()
                // Xaf.EditDraft: the engine (DetailView capture) and the Blazor part (restore offer, drafts list,
                // ListView capture, row badges). The Blazor module requires the Core module; both are listed explicitly.
                .Add<EditDraftCoreModule>()
                .Add<EditDraftBlazorModule>()
                .Add<SampleBlazorModule>();
            builder.ObjectSpaceProviders
                .AddSecuredXpo((serviceProvider, options) =>
                {
                    string connectionString = Configuration.GetConnectionString("ConnectionString");
                    ArgumentNullException.ThrowIfNull(connectionString);
                    options.ConnectionString = connectionString;
                    options.ThreadSafe = true;
                    options.UseSharedDataStoreProvider = true;
                })
                // Template line, and a Xaf.EditDraft prerequisite: the library's restore popup and drafts list are
                // non-persistent objects and need the non-persistent object space provider.
                .AddNonPersistent();
            builder.Security
                .UseIntegratedMode(options =>
                {
                    options.Lockout.Enabled = true;
                    options.RoleType = typeof(PermissionPolicyRole);
                    options.UserType = typeof(ApplicationUser);
                    options.UserLoginInfoType = typeof(ApplicationUserLoginInfo);
                    options.UseXpoPermissionsCaching();
                    options.Events.OnSecurityStrategyCreated += securityStrategy =>
                    {
                        ((SecurityStrategy)securityStrategy).PermissionsReloadMode = PermissionsReloadMode.NoCache;
                    };
                })
                .AddPasswordAuthentication(options =>
                {
                    options.IsSupportChangePassword = true;
                });
        });
        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        });
        authentication.AddCookie(options =>
        {
            options.LoginPath = "/LoginPage";
        });
    }

    /// <summary>
    /// Xaf.EditDraft: the three registrations a consumer must make. Nothing else is registered, so every other seam is the
    /// library default:
    /// - owner: the XAF login's Guid (XafLoginEditDraftOwnerResolver), no owner without a Guid login;
    /// - record access: XAF security only (XafSecurityEditDraftRecordAccess); records are always loaded through a secured
    ///   object space first;
    /// - switches: the configuration section "EditDraftCapture" (appsettings.json);
    /// - clock: TimeProvider.System; log sink: the application's ILogger, category "Xaf.EditDraft"; texts: English.
    /// Public and static so Xaf.EditDraft.Sample.Tests checks this exact composition.
    /// </summary>
    public static IServiceCollection AddEditDrafts(IServiceCollection services)
    {
        services.AddEditDraftStore<SampleEditDraft>();
        services.AddEditDraftRegistry(NoteEditDraftPolicy.Register);
        services.AddEditDraftBlazor();
        return services;
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }
        app.UseHttpsRedirection();
        app.UseRequestLocalization();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.UseXaf();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapXafEndpoints();
            endpoints.MapBlazorHub();
            endpoints.MapFallbackToPage("/_Host");
            endpoints.MapControllers();
        });
    }
}
