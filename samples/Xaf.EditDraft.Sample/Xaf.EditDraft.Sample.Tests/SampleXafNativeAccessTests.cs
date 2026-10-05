using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.DC.Xpo;
using DevExpress.ExpressApp.Layout;
using DevExpress.ExpressApp.Security;
using DevExpress.ExpressApp.Security.ClientServer;
using DevExpress.ExpressApp.Xpo;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.PermissionPolicy;
using DevExpress.Xpo;
using DevExpress.Xpo.DB;
using DevExpress.Xpo.Metadata;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Xaf.EditDraft.Blazor;
using Xaf.EditDraft.Core;
using Xaf.EditDraft.Sample.Module;
using Xaf.EditDraft.Sample.Module.BusinessObjects;
using Xaf.EditDraft.Sample.Module.DatabaseUpdate;
using Xaf.EditDraft.Sample.Module.EditDrafts;

namespace Xaf.EditDraft.Sample.Tests;

/// <summary>
/// Xaf.EditDraft 0.4.0-preview.1 (owner ruling 2026-10-05): who may restore and recreate a Note is decided by XAF security —
/// the sample's RestrictedNotes role (Updater.CreateRestrictedNotesRole: Note Read and Create, Write only where Priority is
/// not High) — plus an optional IEditDraftAccessCheck that can only narrow. SINGLE-MODEL (Claude only; owner review): written
/// by the same model as the code. Executed against a real SecurityStrategyComplex, a SecuredObjectSpaceProvider and a logged-on
/// user, over an in-memory XPO data store (one per fixture).
/// </summary>
[TestFixture]
[NonParallelizable]
public class SampleXafNativeAccessTests
{
    private const string AdminName = "Admin";
    private MemoryDataStoreProvider _dataStore;
    private Guid _normalNote, _highNote;

    private sealed class HeadlessApplication : XafApplication
    {
        protected override LayoutManager CreateLayoutManagerCore(bool simple) => null;
    }

    /// <summary>A host check that allows everything (to show it cannot widen XAF) or refuses everything (to show it narrows).</summary>
    private sealed class FixedCheck : IEditDraftAccessCheck
    {
        private readonly bool _allow;
        public FixedCheck(bool allow) => _allow = allow;
        public bool MayRestore(XafApplication application, EditDraftTypePolicy policy, object record) => _allow;
        public bool MayRecreate(XafApplication application, EditDraftTypePolicy policy, object record) => _allow;
    }

    [OneTimeSetUp]
    public void Seed()
    {
        _dataStore = new MemoryDataStoreProvider();
        using var app = Application(out var provider);
        using var os = provider.CreateNonsecuredObjectSpace();
        var restricted = Updater.CreateRestrictedNotesRole(os);
        var adminRole = os.CreateObject<PermissionPolicyRole>();
        adminRole.Name = "Administrators";
        adminRole.IsAdministrative = true;
        User(os, Updater.RestrictedUserName, restricted);
        User(os, AdminName, adminRole);
        var normal = os.CreateObject<Note>();
        normal.Title = "normal";
        normal.Priority = NotePriority.Normal;
        var high = os.CreateObject<Note>();
        high.Title = "high";
        high.Priority = NotePriority.High;
        os.CommitChanges();
        (_normalNote, _highNote) = (normal.Oid, high.Oid);
    }

    [TearDown]
    public void ResetSecuritySystem() => SecuritySystem.SetInstance(null);

    private static void User(IObjectSpace os, string name, PermissionPolicyRole role)
    {
        var user = os.CreateObject<ApplicationUser>();
        user.UserName = name;
        user.SetPassword("");   // the template's test users have an empty password
        user.Roles.Add(role);
        // What the template's UserManager.CreateUser adds: the password login (AuthenticationStandard checks it).
        ((ISecurityUserWithLoginInfo)user).CreateUserLoginInfo(SecurityDefaults.PasswordAuthentication, os.GetKeyValueAsString(user));
    }

    /// <summary>A headless XAF application with the sample module, the library's Core module and XAF integrated security.</summary>
    private HeadlessApplication Application(out SecuredObjectSpaceProvider provider)
    {
        var security = new SecurityStrategyComplex(typeof(ApplicationUser), typeof(PermissionPolicyRole), new AuthenticationStandard());
        var app = new HeadlessApplication { Security = security };
        app.Modules.Add(new SampleModule());
        app.Modules.Add(new EditDraftCoreModule());
        app.CustomCheckCompatibility += (s, e) => e.Handled = true;   // the in-memory store has no module versions to compare
        provider = new SecuredObjectSpaceProvider(security, _dataStore, XafTypesInfo.Instance, XpoTypesInfoHelper.GetXpoTypeInfoSource(), true);
        app.Setup("Xaf.EditDraft.Sample.Tests", provider);
        return app;
    }

    /// <summary>The application with <paramref name="userName"/> logged on through XAF's password authentication.</summary>
    private HeadlessApplication LoggedOn(string userName, IEditDraftAccessCheck hostCheck = null)
    {
        var app = Application(out var provider);
        var security = (SecurityStrategyComplex)app.Security;
        security.Authentication.SetLogonParameters(new AuthenticationStandardLogonParameters(userName, ""));
        security.Logon(provider.CreateNonsecuredObjectSpace());
        SecuritySystem.SetInstance(security);   // the library's member-write check asks SecuritySystem (EditDraftMemberAccess)
        // The registrations of the sample's Startup that the access check uses: AddEditDraftBlazor (it registers the evaluation
        // of a new object's permissions) and, for X4 only, a host check.
        var services = new ServiceCollection();
        services.AddEditDraftBlazor();
        if (hostCheck != null) services.AddSingleton(hostCheck);
        app.ServiceProvider = services.BuildServiceProvider();
        return app;
    }

    /// <summary>A never-saved Note rebuilt from a new-record draft the way the recreate fills it (EditDraftRestorer.ApplyNew).</summary>
    private static Note Rebuild(XafApplication app, IObjectSpace os, NotePriority priority)
    {
        var policy = NoteEditDraftPolicy.Create();
        var payload = new EditDraftPayload { TypeName = nameof(Note) };
        payload.Upsert(nameof(Note.Title), "string", "Title", true, null, null, "typed title", "typed title");
        payload.Upsert(nameof(Note.Priority), "enum", "Priority", true, EditDraftCodec.RawOf(NotePriority.Low), "Low", EditDraftCodec.RawOf(priority), priority.ToString());
        var note = os.CreateObject<Note>();
        var result = EditDraftRestorer.ApplyNew(policy, os, note, payload, p => EditDraftMemberAccess.CanWrite(os, note, p));
        Assert.That(result.Failed, Is.EqualTo(0), "every typed value went onto the rebuilt Note");
        Assert.That((note.Title, note.Priority), Is.EqualTo(("typed title", priority)));
        Assert.That(os.IsNewObject(note), Is.True, "rebuilt, uncommitted");
        return note;
    }

    [Test]
    public void X1_the_restricted_role_reads_every_Note_but_may_restore_only_onto_one_outside_its_criterion()
    {
        using var app = LoggedOn(Updater.RestrictedUserName);
        var policy = NoteEditDraftPolicy.Create();
        using var os = app.CreateObjectSpace(typeof(Note));
        Assert.That(os, Is.InstanceOf<ISecuredObjectSpace>(), "the application's object space is the secured one (integrated mode)");
        var normal = os.GetObjectByKey<Note>(_normalNote);
        var high = os.GetObjectByKey<Note>(_highNote);
        Assert.That(normal, Is.Not.Null, "Read is granted on every Note");
        Assert.That(high, Is.Not.Null, "Read is granted on every Note, High too");
        Assert.That(EditDraftServices.MayRestore(app, policy, normal), Is.True, "Write is granted where Priority is not High");
        Assert.That(EditDraftServices.MayRestore(app, policy, high), Is.False, "no Write on a High Note: a draft is not restored onto it");
        Assert.That(XafSecurityEditDraftAccessCheck.Instance.MayRestore(app, policy, high), Is.False, "the refusal is XAF security's (no host check is registered)");
    }

    [Test]
    public void X2_a_new_Note_rebuilt_from_a_draft_is_refused_when_its_values_fall_outside_the_criterion_and_allowed_inside_it()
    {
        using var app = LoggedOn(Updater.RestrictedUserName);
        var policy = NoteEditDraftPolicy.Create();
        Assert.That(EditDraftCreateAccess.MayCreate(app, policy), Is.True, "type-level Create is granted and the UI offers creating a Note (step 4 passes)");
        using (var os = app.CreateObjectSpace(typeof(Note)))
        {
            var high = Rebuild(app, os, NotePriority.High);
            Assert.That(EditDraftServices.MayRecreate(app, policy, high), Is.False, "the rebuilt values fall outside Write's criterion");
            // Why the library does not use the public PermissionRequest path here: for a NEW object it answers at type level, so
            // it grants Create and even Write on this High Note (PermissionRequestProcessorWrapper drops a new target object).
            var security = (IRequestSecurity)app.Security;
            Assert.That(security.IsGranted(new PermissionRequest(os, typeof(Note), SecurityOperations.Create, high)), Is.True, "type level: Create is granted");
            Assert.That(security.IsGranted(new PermissionRequest(os, typeof(Note), SecurityOperations.Write, high)), Is.True,
                "type level: the Write criterion is not evaluated on a new object's values through PermissionRequest");
        }
        using (var os = app.CreateObjectSpace(typeof(Note)))
        {
            var normal = Rebuild(app, os, NotePriority.Normal);
            Assert.That(EditDraftServices.MayRecreate(app, policy, normal), Is.True, "inside the criterion: may be recreated");
        }
    }

    [Test]
    public void X3_an_administrator_is_unaffected()
    {
        using var app = LoggedOn(AdminName);
        var policy = NoteEditDraftPolicy.Create();
        using var os = app.CreateObjectSpace(typeof(Note));
        Assert.That(EditDraftServices.MayRestore(app, policy, os.GetObjectByKey<Note>(_highNote)), Is.True);
        Assert.That(EditDraftServices.MayRestore(app, policy, os.GetObjectByKey<Note>(_normalNote)), Is.True);
        Assert.That(EditDraftServices.MayRecreate(app, policy, Rebuild(app, os, NotePriority.High)), Is.True);
    }

    [Test]
    public void X4_a_host_check_narrows_but_cannot_widen_what_XAF_security_refuses()
    {
        var policy = NoteEditDraftPolicy.Create();
        using (var app = LoggedOn(Updater.RestrictedUserName, new FixedCheck(allow: true)))
        using (var os = app.CreateObjectSpace(typeof(Note)))
        {
            Assert.That(EditDraftServices.AccessCheck(app.ServiceProvider), Is.InstanceOf<FixedCheck>());
            Assert.That(EditDraftServices.MayRestore(app, policy, os.GetObjectByKey<Note>(_highNote)), Is.False, "XAF refuses: a host that allows everything cannot widen it");
            Assert.That(EditDraftServices.MayRecreate(app, policy, Rebuild(app, os, NotePriority.High)), Is.False);
            Assert.That(EditDraftServices.MayRestore(app, policy, os.GetObjectByKey<Note>(_normalNote)), Is.True, "both allow");
        }
        using (var app = LoggedOn(AdminName, new FixedCheck(allow: false)))
        using (var os = app.CreateObjectSpace(typeof(Note)))
        {
            Assert.That(EditDraftServices.MayRestore(app, policy, os.GetObjectByKey<Note>(_normalNote)), Is.False, "the host narrows even an administrator");
            Assert.That(EditDraftServices.MayRecreate(app, policy, Rebuild(app, os, NotePriority.Normal)), Is.False);
        }
    }

    [Test]
    public void X5_the_restricted_role_is_the_one_the_sample_Updater_creates()
    {
        using var app = Application(out var provider);
        using var os = provider.CreateNonsecuredObjectSpace();
        var role = os.FirstOrDefault<PermissionPolicyRole>(r => r.Name == Updater.RestrictedRoleName);
        Assert.That(role, Is.Not.Null);
        Assert.That(role.IsAdministrative, Is.False);
        Assert.That(Updater.CreateRestrictedNotesRole(os), Is.SameAs(role), "found by name, not created twice");
        var note = role.TypePermissions.Single(p => p.TargetType == typeof(Note));
        Assert.That((note.ReadState, note.CreateState, note.WriteState, note.DeleteState),
            Is.EqualTo(((SecurityPermissionState?)SecurityPermissionState.Allow, (SecurityPermissionState?)SecurityPermissionState.Allow, (SecurityPermissionState?)null, (SecurityPermissionState?)null)));
        var write = note.ObjectPermissions.Single();
        Assert.That((write.WriteState, write.ReadState), Is.EqualTo(((SecurityPermissionState?)SecurityPermissionState.Allow, (SecurityPermissionState?)null)));
        Assert.That(write.Criteria, Is.EqualTo("[Priority] <> " + (int)NotePriority.High), "Write only where Priority is not High (XPO stores the enum value)");
    }
}

/// <summary>
/// A host's store class that keeps the two columns 0.3.0-preview.1's store base declared (the upgrade path in
/// docs/consumer-guide.md, "Upgrading from 0.3.0-preview.1"): same column names and types, declared by the host.
/// </summary>
[Persistent("drafts.LegacyColumnsDraft")]
public class LegacyColumnsStore : EditDraftStoreBase
{
    public LegacyColumnsStore(Session session) : base(session) { }

    private bool _loginIsStaffMember;
    [Persistent("LoginIsStaffMember")]
    public bool LoginIsStaffMember { get => _loginIsStaffMember; set => SetPropertyValue(nameof(LoginIsStaffMember), ref _loginIsStaffMember, value); }

    private Guid _subSectionOid;
    [Persistent("SubSectionOid")]
    public Guid SubSectionOid { get => _subSectionOid; set => SetPropertyValue(nameof(SubSectionOid), ref _subSectionOid, value); }
}

/// <summary>
/// 0.4.0-preview.1 against SQL Server (LocalDB): a host store class that keeps the two removed columns works with the library's
/// writer, which neither reads nor writes them. Own throwaway database (XafEditDraftNative_&lt;random&gt;), dropped at the end;
/// skipped where LocalDB is not installed.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SampleLegacyColumnsTests
{
    private const string Server = @"(localdb)\MSSQLLocalDB";
    private string _database;
    private string _connectionString;
    private XPObjectSpaceProvider _provider;

    private sealed class Factory : INonSecuredObjectSpaceFactory
    {
        private readonly XPObjectSpaceProvider _provider;
        public Factory(XPObjectSpaceProvider provider) => _provider = provider;
        public IObjectSpace CreateNonSecuredObjectSpace(Type objectType) => _provider.CreateObjectSpace();
    }

    /// <summary>The library's internal writer for the store, called by name (its internals are granted only to the library's tests).</summary>
    private sealed class Writer
    {
        private readonly object _writer;
        public Writer(IServiceProvider services, Type storeType)
        {
            var open = typeof(EditDraftStoreBase).Assembly.GetType("Xaf.EditDraft.Core.EditDraftWriter`1", throwOnError: true);
            _writer = Activator.CreateInstance(open.MakeGenericType(storeType), services, new EditDraftStoreRegistration(storeType));
        }
        public T Call<T>(string method, params object[] args) => (T)_writer.GetType().GetMethod(method).Invoke(_writer, args);
    }

    [OneTimeSetUp]
    public void CreateDatabase()
    {
        try
        {
            using var probe = new SqlConnection($"Data Source={Server};Integrated Security=true;Connect Timeout=10");
            probe.Open();
        }
        catch (Exception ex)
        {
            Assert.Ignore($"LocalDB is not available here ({ex.GetType().Name}); the SQL Server checks need it.");
        }
        _database = "XafEditDraftNative_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Run("master", $"CREATE DATABASE [{_database}]");
        Run(_database, "CREATE SCHEMA [drafts]");
        _connectionString = $"Data Source={Server};Integrated Security=true;Initial Catalog={_database}";
        var dictionary = new ReflectionDictionary();
        dictionary.CollectClassInfos(typeof(LegacyColumnsStore));
        using (var layer = new SimpleDataLayer(dictionary, XpoDefault.GetConnectionProvider(_connectionString, AutoCreateOption.DatabaseAndSchema)))
        using (var uow = new UnitOfWork(layer))
            uow.UpdateSchema(typeof(LegacyColumnsStore));
        var typesInfo = new TypesInfo();
        var source = new XpoTypeInfoSource(typesInfo);
        typesInfo.AddEntityStore(source);
        typesInfo.RegisterEntity(typeof(LegacyColumnsStore));
        _provider = new XPObjectSpaceProvider(new ConnectionStringDataStoreProvider(_connectionString), typesInfo, source, true, false);
    }

    [OneTimeTearDown]
    public void DropDatabase()
    {
        if (_database == null) return;
        _provider?.Dispose();
        SqlConnection.ClearAllPools();
        Run("master", $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]");
    }

    private static void Run(string database, string sql)
    {
        using var connection = new SqlConnection($"Data Source={Server};Integrated Security=true;Initial Catalog={database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private LegacyColumnsStore Find(Guid oid)
    {
        using var os = _provider.CreateObjectSpace();
        return os.GetObjectsQuery<LegacyColumnsStore>().FirstOrDefault(d => d.Oid == oid);
    }

    [Test]
    public void L2_a_host_store_that_keeps_the_two_removed_columns_works_and_the_library_leaves_their_values_alone()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INonSecuredObjectSpaceFactory>(new Factory(_provider));
        services.AddEditDraftStore<LegacyColumnsStore>();
        using var provider = services.BuildServiceProvider();
        Assert.That(EditDraftStartup.DatabaseProblems(provider, checkTable: true), Is.Empty, "SQL Server, one table name, the table exists");

        var writer = new Writer(provider, typeof(LegacyColumnsStore));
        var now = DateTime.Now;
        var owner = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var oid = writer.Call<Guid>("Create", new EditDraftSeed { OwnerUserOid = owner, EditorInstanceId = editor, ObjectType = "Note", TargetOid = Guid.NewGuid(), ViewId = "Note_DetailView" }, "{\"v\":1}", 1, now);
        Assert.That(oid, Is.Not.EqualTo(Guid.Empty), "XPO's insert into a table with the two extra columns");
        var row = Find(oid);
        Assert.That((row.LoginIsStaffMember, row.SubSectionOid), Is.EqualTo((false, Guid.Empty)), "the library does not set them: XPO writes the defaults");

        // A host value in the columns survives every library statement.
        var hostScope = Guid.NewGuid();
        using (var os = _provider.CreateObjectSpace())
        {
            var mine = os.GetObjectsQuery<LegacyColumnsStore>().Single(d => d.Oid == oid);
            mine.SubSectionOid = hostScope;
            mine.LoginIsStaffMember = true;
            os.CommitChanges();
        }
        Assert.That(writer.Call<bool>("TrySupersede", oid, row.Revision, owner, "{\"v\":2}", 2, "context", now), Is.True);
        Assert.That(writer.Call<bool>("TrySoftDiscard", oid, owner, now), Is.True);
        var claimed = writer.Call<int>("TryClaim", oid, row.Revision + 1, owner, editor, now);
        Assert.That(claimed, Is.EqualTo(row.Revision + 2));
        row = Find(oid);
        Assert.That((row.Payload, row.ContextText, row.DeletedOn), Is.EqualTo(("{\"v\":2}", "context", (DateTime?)null)));
        Assert.That((row.LoginIsStaffMember, row.SubSectionOid), Is.EqualTo((true, hostScope)), "the library's UPDATEs never name the two columns");
        Assert.That(writer.Call<int>("DeleteOwn", oid, owner, editor), Is.EqualTo(1));
        Assert.That(Find(oid), Is.Null);
    }
}
