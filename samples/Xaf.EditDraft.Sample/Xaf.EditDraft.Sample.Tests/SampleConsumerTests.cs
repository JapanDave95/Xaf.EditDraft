using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.DC.Xpo;
using DevExpress.ExpressApp.Layout;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Xpo;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.PermissionPolicy;
using DevExpress.Xpo;
using DevExpress.Xpo.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Xaf.EditDraft.Blazor;
using Xaf.EditDraft.Core;
using Xaf.EditDraft.Sample.Blazor.Server;
using Xaf.EditDraft.Sample.Module;
using Xaf.EditDraft.Sample.Module.BusinessObjects;
using Xaf.EditDraft.Sample.Module.DatabaseUpdate;
using Xaf.EditDraft.Sample.Module.EditDrafts;

namespace Xaf.EditDraft.Sample.Tests;

/// <summary>
/// The sample consumer of the Xaf.EditDraft library (run 2026-10-03-editdraft-sample-e817ca). Expectation ids C1-C32 are
/// from the Codex requirement-only list of that run (tests a1); each test names the ids it covers. What only a running
/// host or a browser can show (served stylesheet, capture, offer, restore) is not claimed here.
/// </summary>
[TestFixture]
public class SampleConsumerTests
{
    private static readonly Assembly ModuleAssembly = typeof(Note).Assembly;
    private static readonly Assembly BlazorAssembly = typeof(Startup).Assembly;
    private static readonly string[] ForbiddenPrefixes = { "CareCrew", "NursingHome_Chart", "Progress.", "Llamachant", "CareTree" };

    // ---- (b) reference isolation: C3, C4 -----------------------------------------------------------------------

    [Test]
    public void B1_the_sample_assemblies_reference_both_libraries_and_no_application_assembly_directly_or_reachably()
    {
        var blazorRefs = BlazorAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        var moduleRefs = ModuleAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.That(blazorRefs, Does.Contain("Xaf.EditDraft.Core"), "Startup registers the store and the registry (Core)");
        Assert.That(blazorRefs, Does.Contain("Xaf.EditDraft.Blazor"), "Startup registers EditDraftBlazorModule and AddEditDraftBlazor (Blazor)");
        Assert.That(blazorRefs, Does.Contain("Xaf.EditDraft.Sample.Module"));
        Assert.That(moduleRefs, Does.Contain("Xaf.EditDraft.Core"), "SampleEditDraft derives from EditDraftStoreBase");
        Assert.That(moduleRefs, Does.Not.Contain("Xaf.EditDraft.Blazor"), "the platform-agnostic module needs only Core");

        // C3 boundary: walk every reachable non-framework assembly, so an intermediary cannot hide an application reference.
        var reachable = new List<string>();
        var unloadable = new List<string>();
        foreach (var root in new[] { BlazorAssembly, ModuleAssembly })
            Walk(root, reachable, unloadable);
        Assert.That(reachable, Does.Contain("Xaf.EditDraft.Core").And.Contain("Xaf.EditDraft.Blazor"));
        foreach (var name in reachable)
            foreach (var prefix in ForbiddenPrefixes)
                Assert.That(name, Does.Not.StartWith(prefix), $"reachable assembly {name} is application code");
        Assert.That(unloadable, Is.Empty, "every reachable non-framework assembly must load, or the walk proves nothing about it");
    }

    [Test]
    public void B2_the_sample_project_files_reference_only_the_libraries_and_the_sample_module()
    {
        var root = SampleRoot();
        var allowed = new[] { "Xaf.EditDraft.Core.csproj", "Xaf.EditDraft.Blazor.csproj", "Xaf.EditDraft.Sample.Module.csproj" };
        foreach (var project in new[] { "Xaf.EditDraft.Sample.Module", "Xaf.EditDraft.Sample.Blazor.Server" })
        {
            var text = File.ReadAllText(Path.Combine(root, project, project + ".csproj"));
            var projectRefs = Regex.Matches(text, "<ProjectReference Include=\"([^\"]+)\"").Select(m => Path.GetFileName(m.Groups[1].Value)).ToList();
            Assert.That(projectRefs, Is.Not.Empty);
            Assert.That(projectRefs, Is.SubsetOf(allowed), project);
            var packages = Regex.Matches(text, "<PackageReference Include=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
            Assert.That(packages, Has.All.Matches<string>(p => p.StartsWith("DevExpress.", StringComparison.Ordinal) || p == "Microsoft.Data.SqlClient"), project);
            Assert.That(text, Does.Not.Contain("Version=\""), $"{project}: versions come from Directory.Packages.props only");
            Assert.That(text, Does.Not.Contain("<Compile Include").And.Not.Contain("Link="), $"{project}: no linked application source");
        }
    }

    // ---- the consumer's own class and store: C5, C23 ---------------------------------------------------------------

    [Test]
    public void C1_Note_has_the_four_members_the_requirement_names()
    {
        Assert.That(typeof(DevExpress.Persistent.BaseImpl.BaseObject).IsAssignableFrom(typeof(Note)), "contract v1: a Guid-keyed BaseObject");
        Assert.That(typeof(Note).GetProperty(nameof(Note.Title)).PropertyType, Is.EqualTo(typeof(string)));
        Assert.That(typeof(Note).GetProperty(nameof(Note.Title)).GetCustomAttribute<SizeAttribute>()?.Size, Is.EqualTo(100));
        Assert.That(typeof(Note).GetProperty(nameof(Note.Body)).PropertyType, Is.EqualTo(typeof(string)));
        Assert.That(typeof(Note).GetProperty(nameof(Note.Body)).GetCustomAttribute<SizeAttribute>()?.Size, Is.EqualTo(SizeAttribute.Unlimited), "memo");
        Assert.That(typeof(Note).GetProperty(nameof(Note.Priority)).PropertyType.IsEnum, Is.True);
        Assert.That(typeof(Note).GetProperty(nameof(Note.DueOn)).PropertyType, Is.EqualTo(typeof(DateTime)));
        Assert.That(typeof(Note).GetCustomAttribute<DefaultClassOptionsAttribute>(), Is.Not.Null, "Note is in the navigation, so its ListView is reachable");
    }

    [Test]
    public void C2_SampleEditDraft_is_the_consumer_owned_store_and_XPO_maps_the_library_members_into_its_table()
    {
        Assert.That(typeof(EditDraftStoreBase).IsAssignableFrom(typeof(SampleEditDraft)));
        Assert.That(typeof(SampleEditDraft).IsAbstract, Is.False);
        Assert.That(new EditDraftStoreRegistration(typeof(SampleEditDraft)).TableName, Is.EqualTo("SampleEditDraft"));

        var dictionary = new ReflectionDictionary();
        var store = dictionary.GetClassInfo(typeof(SampleEditDraft));
        Assert.That(store.IsPersistent, Is.True);
        Assert.That(dictionary.GetClassInfo(typeof(EditDraftStoreBase)).IsPersistent, Is.False, "the library maps no table of its own");
        var libraryMembers = typeof(EditDraftStoreBase).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.CanWrite).Select(p => p.Name).ToList();
        Assert.That(libraryMembers, Has.Count.EqualTo(19), "the store base declares 19 persistent members");
        foreach (var name in libraryMembers)
            Assert.That(store.FindMember(name)?.IsPersistent, Is.True, $"{name} is a column of dbo.SampleEditDraft");
        Assert.That(store.FindMember("GCRecord"), Is.Null, "DeferredDeletion(false) is inherited: no GCRecord column");
    }

    // ---- (c) policy, registry and model admission: C6, C7, C8 -----------------------------------------------------

    [Test]
    public void C3_the_registry_built_by_the_sample_Startup_holds_exactly_the_Note_policy_and_resolves_it()
    {
        using var services = Startup.AddEditDrafts(new ServiceCollection()).BuildServiceProvider();
        var registry = EditDraftServices.Registry(services);
        Assert.That(registry, Is.Not.SameAs(EditDraftRegistry.Empty), "Startup registered a registry");
        Assert.That(registry.IsFrozen, Is.True);
        Assert.That(registry.All, Has.Count.EqualTo(1));
        var policy = registry.Find(typeof(Note));
        Assert.That(policy, Is.Not.Null);
        Assert.That(registry.Find(nameof(Note)), Is.SameAs(policy), "the payload keys on Type.Name");
        Assert.That(policy.PolicyId, Is.EqualTo("Note"));
        Assert.That(EditDraftTypePolicy.IsGeneric(policy), Is.True, "login-owned with a decision table");
        Assert.That(policy.OwnerKind, Is.EqualTo(EditDraftOwnerKind.Login));
        Assert.That(policy.AllowNewRecords, Is.True);
        Assert.That(policy.SwitchKey, Is.EqualTo("EditDraftCapture:Types:Note:Enabled"));

        // C7 boundary: no other type and no subclass of Note takes part (matching is by the exact type).
        Assert.That(registry.Find(typeof(SampleEditDraft)), Is.Null);
        Assert.That(registry.Find(typeof(ApplicationUser)), Is.Null);
        Assert.That(registry.Find(typeof(NoteSubclassProbe)), Is.Null, "a subclass of Note needs its own policy");
        Assert.That(registry.Find(nameof(NoteSubclassProbe)), Is.Null);
    }

    [Test]
    public void C3b_the_sample_Startup_ConfigureServices_itself_registers_the_store_the_registry_and_the_Blazor_services()
    {
        // Codex diffreview DR5: C3/D1 call Startup.AddEditDrafts directly; this test goes through ConfigureServices, so
        // removing the AddEditDrafts(services) call from it fails here. Nothing is resolved from XAF (no database).
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["ConnectionStrings:ConnectionString"] = "not used at registration time",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        new Startup(configuration).ConfigureServices(services);

        Assert.That(services.Count(d => d.ServiceType == typeof(EditDraftStoreRegistration)), Is.EqualTo(1));
        Assert.That(services.Count(d => d.ServiceType == typeof(EditDraftRegistry)), Is.EqualTo(1));
        Assert.That(services.Count(d => d.ServiceType == typeof(EditDraftListBridge) && d.Lifetime == ServiceLifetime.Scoped), Is.EqualTo(1));
        Assert.That(services.Any(d => d.ServiceType == typeof(IEditDraftOwnerResolver)), Is.False, "no custom owner seam");
        Assert.That(services.Any(d => d.ServiceType == typeof(IEditDraftRecordAccess)), Is.False, "no custom record-access seam");
        Assert.That(services.Any(d => d.ServiceType == typeof(EditDraftSwitchOptions)), Is.False, "no custom switch section");

        var registryDescriptor = services.Single(d => d.ServiceType == typeof(EditDraftRegistry));
        var registry = (EditDraftRegistry)registryDescriptor.ImplementationFactory(null);
        Assert.That(registry.Find(typeof(Note))?.PolicyId, Is.EqualTo(NoteEditDraftPolicy.Id));
    }

    [Test]
    public void C4_the_Note_policy_admits_Note_DetailView_and_Note_ListView_and_nothing_else()
    {
        var registry = EditDraftRegistry.Create(NoteEditDraftPolicy.Register);
        var policy = registry.Find(typeof(Note));

        // The allowlists are exactly these ids (Codex diffreview DR7).
        Assert.That(policy.ApprovedViewIds, Is.EquivalentTo(new[] { "Note_DetailView" }));
        Assert.That(policy.ListViewIds, Is.EquivalentTo(new[] { "Note_ListView" }));

        Assert.That(EditDraftCaptureController.IsAdmittedView(policy, "Note_DetailView", isRoot: true, isNew: false), Is.True, "existing Note, root DetailView");
        Assert.That(EditDraftCaptureController.IsAdmittedViewIncludingNew(policy, "Note_DetailView", isRoot: true, isNew: true), Is.True, "new Note (AllowNewRecords)");
        Assert.That(EditDraftListControllerBlazor.PolicyForListView(registry, "Note_ListView"), Is.SameAs(policy), "header action and badges on the Note list");

        // C7 boundary: unapproved views are not admitted.
        Assert.That(EditDraftCaptureController.IsAdmittedView(policy, "Note_DetailView", isRoot: false, isNew: false), Is.False, "a nested (non-root) DetailView");
        Assert.That(EditDraftCaptureController.IsAdmittedView(policy, "Note_ListView", isRoot: true, isNew: false), Is.False, "a ListView id is not a DetailView id");
        Assert.That(EditDraftCaptureController.IsAdmittedView(policy, "Note_DetailView_Copy", isRoot: true, isNew: false), Is.False);
        Assert.That(EditDraftListControllerBlazor.PolicyForListView(registry, "Note_DetailView"), Is.Null);
        Assert.That(EditDraftListControllerBlazor.PolicyForListView(registry, "SampleEditDraft_ListView"), Is.Null);
    }

    [Test]
    public void C5_every_Note_member_is_decided_A_and_the_decision_table_passes_the_library_gate()
    {
        var policy = NoteEditDraftPolicy.Create();
        Assert.That(EditDraftDecisions.Check(policy), Is.Empty, "every member reflection proposes has a decision, and each agrees with the policy lists");
        Assert.That(policy.Decisions.Keys, Is.EquivalentTo(new[] { "Title", "Body", "Priority", "DueOn" }));
        Assert.That(policy.Decisions.Values.Select(d => d.Disposition), Has.All.EqualTo(EditDraftDisposition.Restorable));
        Assert.That(policy.Decisions.Values.Select(d => d.CensusCategory), Has.All.EqualTo("A"));
        Assert.That(policy.Members.Select(m => m.Path), Is.EqualTo(new[] { "Body", "DueOn", "Priority", "Title" }), "admitted members, by name");
        Assert.That(policy.Members.Select(m => m.Kind), Is.EqualTo(new[] { "string", "datetime", "enum", "string" }));
    }

    [Test]
    public void C6_XAF_generates_Note_DetailView_and_Note_ListView_in_the_application_model_of_the_sample_modules()
    {
        // C6: the view ids the policy names must exist in a real XAF application model, not only by naming convention.
        // A headless XafApplication with the sample module and the library's Core module (the Blazor module needs a Blazor host).
        using var application = new HeadlessApplication();
        application.Modules.Add(new SampleModule());
        application.Modules.Add(new EditDraftCoreModule());
        var provider = new XPObjectSpaceProvider((IXpoDataStoreProvider)new MemoryDataStoreProvider(), XafTypesInfo.Instance, XpoTypesInfoHelper.GetXpoTypeInfoSource(), true, false);
        application.Setup("Xaf.EditDraft.Sample.Tests", provider);

        var detail = application.Model.Views[NoteEditDraftPolicy.DetailViewId] as IModelDetailView;
        var list = application.Model.Views[NoteEditDraftPolicy.ListViewId] as IModelListView;
        Assert.That(detail, Is.Not.Null, "Note_DetailView is in the model");
        Assert.That(list, Is.Not.Null, "Note_ListView is in the model");
        Assert.That(detail.ModelClass.TypeInfo.Type, Is.EqualTo(typeof(Note)));
        Assert.That(list.ModelClass.TypeInfo.Type, Is.EqualTo(typeof(Note)));
        // What the NEW-record recreate asks of the model before it creates a Note (EditDraftCreateAccess.MayCreate, S2).
        Assert.That(list.AllowNew, Is.True);
        Assert.That(detail.AllowEdit, Is.True);
        Assert.That(application.Model.BOModel.GetClass(typeof(SampleEditDraft)), Is.Not.Null, "the store class reaches the model through the sample module");
        Assert.That(application.Modules.FindModule<EditDraftCoreModule>(), Is.Not.Null);
    }

    [Test]
    public void C7_the_sample_Model_xafml_styles_the_library_header_action_and_adds_no_action_of_its_own()
    {
        var path = Path.Combine(SampleRoot(), "Xaf.EditDraft.Sample.Blazor.Server", "Model.xafml");
        var actions = XDocument.Load(path).Descendants("Action").ToList();
        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That((string)actions[0].Attribute("Id"), Is.EqualTo(EditDraftListControllerBlazor.HeaderActionId), "the library's header action id");
        Assert.That((string)actions[0].Attribute("PaintStyle"), Is.EqualTo("CaptionAndImage"), "the only thing the node changes");
        Assert.That(actions[0].Attribute("IsNewNode"), Is.Null, "a difference on the generated action node, not a second action (C8 boundary)");
        // Whether the effective Blazor model shows exactly one such action is a browser-pass item: the headless model in C6
        // has no Blazor module.
    }

    // ---- default seams through the real registrations: C16, C17, C19, C20, C22 -------------------------------------

    [Test]
    public void D1_with_the_sample_registrations_every_other_seam_is_the_library_default()
    {
        using var services = Startup.AddEditDrafts(new ServiceCollection()).BuildServiceProvider();
        Assert.That(EditDraftServices.Owner(services), Is.SameAs(XafLoginEditDraftOwnerResolver.Instance), "owner: the XAF login's Guid");
        Assert.That(EditDraftServices.RecordAccess(services), Is.SameAs(XafSecurityEditDraftRecordAccess.Instance), "record access: XAF security only");
        Assert.That(EditDraftServices.Clock(services), Is.SameAs(TimeProvider.System), "clock: the system clock");
        Assert.That(services.GetService<EditDraftSwitchOptions>(), Is.Null, "switch section: the default, EditDraftCapture");
        Assert.That(EditDraftSwitch.In(services, EditDraftSwitch.EnabledKey), Is.EqualTo("EditDraftCapture:Enabled"));
        Assert.That(services.GetService<EditDraftStoreRegistration>()?.StoreType, Is.EqualTo(typeof(SampleEditDraft)));
        Assert.That(EditDraftTexts.Current, Is.SameAs(EditDraftTextSet.English), "texts: the English set, chosen by no one");

        // Log sink: the sample sets none, so EditDraftCoreModule.Setup takes the application's ILogger
        // (EditDraftLog.UseLoggerIfNoHostSink). The same call, with a provider that has logging, as the host has.
        using (var withLogging = new ServiceCollection().AddLogging().BuildServiceProvider())
        {
            EditDraftLog.UseLoggerIfNoHostSink(withLogging);
            Assert.That(EditDraftLog.Sink, Is.InstanceOf<LoggerEditDraftLog>(), "log: the application's ILogger");
        }

        using var scope = services.CreateScope();   // the Blazor part's per-circuit services
        Assert.That(scope.ServiceProvider.GetService<EditDraftListBridge>(), Is.Not.Null);
        Assert.That(scope.ServiceProvider.GetService<EditDraftOfferRequests>(), Is.Not.Null);
        Assert.That(scope.ServiceProvider.GetService<EditDraftBadgeNotifier>(), Is.Not.Null);
        Assert.That(scope.ServiceProvider.GetService<EditDraftPendingAdoptions>(), Is.Not.Null);
    }

    [Test]
    public void D2_the_default_owner_seam_gives_no_owner_without_a_login()
    {
        // C17 boundary: an unauthenticated context never captures under a guessed or shared owner.
        using var services = Startup.AddEditDrafts(new ServiceCollection()).BuildServiceProvider();
        Assert.That(EditDraftServices.CurrentOwner(services, (IObjectSpace)null).IsNone, Is.True);
        Assert.That(XafLoginEditDraftOwnerResolver.Instance.Current((XafApplication)null).IsNone, Is.True);
    }

    [Test]
    public void D3_the_sample_appsettings_switch_on_existing_new_and_list_capture_for_Note_only()
    {
        var path = Path.Combine(SampleRoot(), "Xaf.EditDraft.Sample.Blazor.Server", "appsettings.json");
        using var services = WithConfiguration(new ConfigurationBuilder().AddJsonFile(path, optional: false).Build());
        Assert.That(EditDraftSwitch.IsGlobalEnabled(services), Is.True);
        Assert.That(EditDraftSwitch.IsEnabled(services, NoteEditDraftPolicy.Id), Is.True, "existing-record capture");
        Assert.That(EditDraftSwitch.IsNewRecordsEnabled(services, NoteEditDraftPolicy.Id), Is.True, "new-record capture");
        Assert.That(EditDraftSwitch.IsListEnabled(services, NoteEditDraftPolicy.Id), Is.True, "ListView capture");
        Assert.That(EditDraftSwitch.IsEnabled(services, "Other"), Is.False, "the type switch is keyed by the registered PolicyId");
    }

    private const string GlobalKey = "EditDraftCapture:Enabled";
    private const string TypeKey = "EditDraftCapture:Types:Note:Enabled";
    private const string ListKey = "EditDraftCapture:ListViews:Enabled";
    private const string NewKey = "EditDraftCapture:NewRecords:Enabled";
    private const string Missing = "<missing>";

    /// <summary>
    /// C19 (and Codex diffreview DR6): each of the four keys on its own through true, True, false, missing, empty and two
    /// malformed values, the other three on. Expected (library contract, EditDraftSwitch.IsOn): only a value that parses
    /// as the boolean true is on. Global and type keys gate all three captures; the list key gates ListView capture only;
    /// the new-records key gates new-record capture only.
    /// </summary>
    private static IEnumerable<TestCaseData> SwitchMatrix()
    {
        var values = new[] { ("true", true), ("True", true), ("false", false), (Missing, false), ("", false), ("yes", false), ("1", false) };
        foreach (var key in new[] { GlobalKey, TypeKey, ListKey, NewKey })
            foreach (var (value, on) in values)
            {
                var existing = key is GlobalKey or TypeKey ? on : true;
                var list = key is NewKey ? true : on;
                var created = key is ListKey ? true : on;
                var shortKey = key.Substring("EditDraftCapture:".Length).Replace(":", "_");
                var shortValue = value == Missing ? "missing" : value == "" ? "empty" : value;
                yield return new TestCaseData(key, value, existing, created, list).SetName($"D4_switch_{shortKey}_{shortValue}");
            }
    }

    [TestCaseSource(nameof(SwitchMatrix))]
    public void D4_switch_matrix_fails_closed(string key, string value, bool existingOn, bool newOn, bool listOn)
    {
        var values = new Dictionary<string, string> { [GlobalKey] = "true", [TypeKey] = "true", [ListKey] = "true", [NewKey] = "true" };
        if (value == Missing) values.Remove(key); else values[key] = value;
        using var services = WithConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        Assert.That(EditDraftSwitch.IsEnabled(services, NoteEditDraftPolicy.Id), Is.EqualTo(existingOn), "existing-record capture");
        Assert.That(EditDraftSwitch.IsNewRecordsEnabled(services, NoteEditDraftPolicy.Id), Is.EqualTo(newOn), "new-record capture");
        Assert.That(EditDraftSwitch.IsListEnabled(services, NoteEditDraftPolicy.Id), Is.EqualTo(listOn), "ListView capture");
    }

    // ---- the store's security obligation: C24, C26 -----------------------------------------------------------------

    [Test]
    public void E1_the_updater_denies_every_operation_on_the_store_to_every_role_and_is_idempotent()
    {
        using var objectSpace = SecuritySpace();
        var admin = objectSpace.CreateObject<PermissionPolicyRole>();
        admin.Name = "Administrators";
        admin.IsAdministrative = true;
        var standard = objectSpace.CreateObject<PermissionPolicyRole>();
        standard.Name = "Default";
        var permissive = objectSpace.CreateObject<PermissionPolicyRole>();
        permissive.Name = "AllowAll";
        permissive.PermissionPolicy = SecurityPermissionPolicy.AllowAllByDefault;
        objectSpace.CommitChanges();

        Assert.That(Updater.DenyDraftStoreToEveryRole(objectSpace), Is.EqualTo(3));
        objectSpace.CommitChanges();
        Assert.That(Updater.DenyDraftStoreToEveryRole(objectSpace), Is.EqualTo(3), "second run");
        objectSpace.CommitChanges();

        foreach (var role in new[] { admin, standard, permissive })
        {
            var permissions = role.TypePermissions.Where(p => p.TargetType == typeof(SampleEditDraft)).ToList();
            Assert.That(permissions, Has.Count.EqualTo(1), $"{role.Name}: one permission object after two runs");
            var p = permissions[0];
            Assert.That(new[] { p.ReadState, p.WriteState, p.CreateState, p.DeleteState, p.NavigateState },
                Has.All.EqualTo(SecurityPermissionState.Deny), role.Name);
        }
        // The rows exist for the administrative role too, but XAF does not apply them while IsAdministrative is true
        // (README "Security"); this test checks the rows, not their effect.
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private sealed class HeadlessApplication : XafApplication
    {
        protected override LayoutManager CreateLayoutManagerCore(bool simple) => null;
    }

    /// <summary>A subclass of Note, never registered: the registry matches the exact type only.</summary>
    public class NoteSubclassProbe : Note
    {
        public NoteSubclassProbe(Session session) : base(session) { }
    }

    private static ServiceProvider WithConfiguration(IConfiguration configuration)
    {
        var services = Startup.AddEditDrafts(new ServiceCollection());
        services.AddSingleton(configuration);
        return services.BuildServiceProvider();
    }

    private static IObjectSpace SecuritySpace()
    {
        var typesInfo = new TypesInfo();
        var source = new XpoTypeInfoSource(typesInfo);
        typesInfo.AddEntityStore(source);
        foreach (var type in new[]
                 {
                     typeof(PermissionPolicyRole), typeof(PermissionPolicyTypePermissionObject), typeof(PermissionPolicyMemberPermissionsObject),
                     typeof(PermissionPolicyObjectPermissionsObject), typeof(PermissionPolicyNavigationPermissionObject), typeof(PermissionPolicyUser),
                     typeof(SampleEditDraft)
                 })
            typesInfo.RegisterEntity(type);
        var provider = new XPObjectSpaceProvider((IXpoDataStoreProvider)new MemoryDataStoreProvider(), typesInfo, source, true, false);
        return provider.CreateObjectSpace();
    }

    private static void Walk(Assembly root, List<string> reachable, List<string> unloadable)
    {
        var seen = new HashSet<string>(reachable, StringComparer.OrdinalIgnoreCase) { root.GetName().Name };
        var queue = new Queue<Assembly>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            foreach (var reference in queue.Dequeue().GetReferencedAssemblies())
            {
                if (!seen.Add(reference.Name)) continue;
                reachable.Add(reference.Name);
                if (IsFrameworkOrVendor(reference.Name)) continue;
                try { queue.Enqueue(Assembly.Load(reference)); }
                catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException) { unloadable.Add(reference.Name); }
            }
        }
    }

    // Framework and vendor assemblies are recorded but not opened: none of them can reference the application.
    private static bool IsFrameworkOrVendor(string name) =>
        name.StartsWith("System", StringComparison.Ordinal) || name.StartsWith("Microsoft.", StringComparison.Ordinal)
        || name.StartsWith("DevExpress.", StringComparison.Ordinal) || name.StartsWith("Newtonsoft.", StringComparison.Ordinal)
        || name is "netstandard" or "mscorlib" or "WindowsBase";

    /// <summary>samples/Xaf.EditDraft.Sample, found by walking up from the test output (builds go to artifacts/ inside the repo).</summary>
    private static string SampleRoot()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "Xaf.EditDraft.Sample");
            if (Directory.Exists(candidate)) return candidate;
        }
        Assert.Fail("samples/Xaf.EditDraft.Sample not found above " + TestContext.CurrentContext.TestDirectory + " (run the build inside the repository)");
        return null;
    }
}
