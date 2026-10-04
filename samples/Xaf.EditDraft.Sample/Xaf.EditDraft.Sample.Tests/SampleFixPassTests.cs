using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.DC.Xpo;
using DevExpress.ExpressApp.Layout;
using DevExpress.ExpressApp.Updating;
using DevExpress.ExpressApp.Xpo;
using DevExpress.ExpressApp.Xpo.Updating;
using DevExpress.Xpo;
using DevExpress.Xpo.DB;
using DevExpress.Xpo.Metadata;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Sample.Tests;

/// <summary>A store mapped to a non-dbo schema (F1 / Codex T4).</summary>
[Persistent("drafts.FixPassDraft")]
public class FixPassSchemaStore : EditDraftStoreBase
{
    public FixPassSchemaStore(Session session) : base(session) { }
}

/// <summary>A table of the same name in dbo, with the same columns, that the library must never touch (T4 sentinel).</summary>
[Persistent("dbo.FixPassDraft")]
public class FixPassDboDecoy : EditDraftStoreBase
{
    public FixPassDboDecoy(Session session) : base(session) { }
}

/// <summary>An unqualified store for the ObjectsOwner checks (T7/T8); its table is never needed.</summary>
public class FixPassOwnerStore : EditDraftStoreBase
{
    public FixPassOwnerStore(Session session) : base(session) { }
}

/// <summary>The store of the per-connection cache test (T9/T10); its table exists.</summary>
public class FixPassCacheStore : EditDraftStoreBase
{
    public FixPassCacheStore(Session session) : base(session) { }
}

/// <summary>A store whose table is created only in the middle of its test (T14).</summary>
public class FixPassLateStore : EditDraftStoreBase
{
    public FixPassLateStore(Session session) : base(session) { }
}

/// <summary>A store whose table XPO creates during the XAF database update of a fresh database (T25).</summary>
public class FixPassFreshStore : EditDraftStoreBase
{
    public FixPassFreshStore(Session session) : base(session) { }
}

/// <summary>A store whose table is never created (T27).</summary>
public class FixPassNeverStore : EditDraftStoreBase
{
    public FixPassNeverStore(Session session) : base(session) { }
}

/// <summary>
/// Bounded fix pass on 0.2.0-preview.1 (run 2026-10-04-editdraft-fix-pass-3b8147): the SQL Server parts of F1, F2 and F4,
/// against LocalDB. Expectation ids Tn are from the Codex requirement-only list of this run (tests a1). Each run creates its
/// own throwaway databases (XafEditDraftFixPass_&lt;random&gt;, and XafEditDraftFresh_&lt;random&gt; for the fresh-database
/// test) and drops them at the end; nothing else is touched. Skipped where LocalDB is not installed.
///
/// The writer is internal to Xaf.EditDraft.Core (its internals are granted only to the library's own test project, G13), and
/// the SQL Server client is referenced only by this sample's test project, so <see cref="Writer"/> reaches the real writer
/// by reflection to execute its own UPDATE and DELETE statements.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SampleFixPassTests
{
    private const string Server = @"(localdb)\MSSQLLocalDB";
    private string _database;
    private string _connectionString;
    private XPObjectSpaceProvider _provider;
    private readonly List<string> _freshDatabases = new();

    private sealed class Factory : INonSecuredObjectSpaceFactory
    {
        private readonly XPObjectSpaceProvider _provider;
        public Factory(XPObjectSpaceProvider provider) => _provider = provider;
        public IObjectSpace CreateNonSecuredObjectSpace(Type objectType) => _provider.CreateObjectSpace();
    }

    /// <summary>XPO's SQL Server provider with a chosen ObjectsOwner (null = none).</summary>
    private sealed class OwnerDataStoreProvider : IXpoDataStoreProvider
    {
        private readonly string _owner;
        public OwnerDataStoreProvider(string connectionString, string owner) { ConnectionString = connectionString; _owner = owner; }
        public string ConnectionString { get; }

        private IDataStore Create(AutoCreateOption option, out IDisposable[] disposables)
        {
            var store = (MSSqlConnectionProvider)XpoDefault.GetConnectionProvider(ConnectionString, option, out disposables);
            store.ObjectsOwner = _owner;
            return store;
        }

        public IDataStore CreateWorkingStore(out IDisposable[] disposableObjects) => Create(AutoCreateOption.SchemaAlreadyExists, out disposableObjects);
        public IDataStore CreateUpdatingStore(bool allowUpdateSchema, out IDisposable[] disposableObjects) =>
            Create(allowUpdateSchema ? AutoCreateOption.DatabaseAndSchema : AutoCreateOption.SchemaAlreadyExists, out disposableObjects);
        public IDataStore CreateSchemaCheckingStore(out IDisposable[] disposableObjects) => Create(AutoCreateOption.None, out disposableObjects);
    }

    private sealed class HeadlessApplication : XafApplication
    {
        protected override LayoutManager CreateLayoutManagerCore(bool simple) => null;
    }

    private sealed class CapturingLog : IEditDraftLog
    {
        public List<string> Lines { get; } = new();
        public void Info(string message) { lock (Lines) Lines.Add(message); }
        public void Warning(string message) { lock (Lines) Lines.Add(message); }
        public void Error(string message) { lock (Lines) Lines.Add(message); }
    }

    /// <summary>The library's internal writer for <typeparamref name="TStore"/>, called by name.</summary>
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
        _database = "XafEditDraftFixPass_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Run("master", $"CREATE DATABASE [{_database}]");
        Run(_database, "CREATE SCHEMA [drafts]");
        _connectionString = $"Data Source={Server};Integrated Security=true;Initial Catalog={_database}";
        CreateTables(typeof(FixPassSchemaStore), typeof(FixPassDboDecoy), typeof(FixPassCacheStore));
        _provider = Provider(_connectionString, typeof(FixPassSchemaStore), typeof(FixPassDboDecoy), typeof(FixPassCacheStore), typeof(FixPassLateStore), typeof(FixPassNeverStore));
    }

    [OneTimeTearDown]
    public void DropDatabases()
    {
        _provider?.Dispose();
        SqlConnection.ClearAllPools();
        foreach (var name in _freshDatabases.Append(_database).Where(n => n != null))
            Run("master", $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}] END");
    }

    private static void Run(string database, string sql)
    {
        using var connection = new SqlConnection($"Data Source={Server};Integrated Security=true;Initial Catalog={database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private void CreateTables(params Type[] types)
    {
        var dictionary = new ReflectionDictionary();
        dictionary.CollectClassInfos(types);
        using var layer = new SimpleDataLayer(dictionary, XpoDefault.GetConnectionProvider(_connectionString, AutoCreateOption.DatabaseAndSchema));
        using var uow = new UnitOfWork(layer);
        uow.UpdateSchema(types);
    }

    private static XPObjectSpaceProvider Provider(string connectionString, params Type[] types) => Provider(new ConnectionStringDataStoreProvider(connectionString), types);

    private static XPObjectSpaceProvider Provider(IXpoDataStoreProvider dataStore, params Type[] types)
    {
        var typesInfo = new TypesInfo();
        var source = new XpoTypeInfoSource(typesInfo);
        typesInfo.AddEntityStore(source);
        foreach (var type in types) typesInfo.RegisterEntity(type);
        return new XPObjectSpaceProvider(dataStore, typesInfo, source, true, false);
    }

    private static ServiceProvider Services<TStore>(XPObjectSpaceProvider provider, bool tableCheck = false) where TStore : EditDraftStoreBase
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { [EditDraftStartup.TableCheckKey] = tableCheck ? "true" : "false" }).Build());
        services.AddSingleton<INonSecuredObjectSpaceFactory>(new Factory(provider));
        services.AddEditDraftStore<TStore>();
        return services.BuildServiceProvider();
    }

    private static List<string> WithLog(Action body)
    {
        var log = new CapturingLog();
        var before = EditDraftLog.Sink;
        EditDraftLog.Sink = log;
        try { body(); }
        finally { EditDraftLog.Sink = before; }
        return log.Lines;
    }

    private static void RunStartup(IServiceProvider services)
    {
        using var app = new HeadlessApplication { ServiceProvider = services };
        EditDraftStartup.Run(app);
    }

    private TStore Find<TStore>(Guid oid) where TStore : EditDraftStoreBase
    {
        using var os = _provider.CreateObjectSpace();
        var row = os.GetObjectsQuery<TStore>().FirstOrDefault(d => d.Oid == oid);
        return row;
    }

    private Guid AddRow<TStore>(Guid owner, DateTime expiresOn, string payload) where TStore : EditDraftStoreBase
    {
        using var os = _provider.CreateObjectSpace();
        var d = os.CreateObject<TStore>();
        d.DraftKey = Guid.NewGuid();
        d.OwnerUserOid = owner;
        d.ObjectType = "Note";
        d.TargetOid = Guid.NewGuid();
        d.Payload = payload;
        d.FirstCapturedOn = expiresOn.AddDays(-EditDraftStoreBase.RetentionDays);
        d.LastCapturedOn = d.FirstCapturedOn;
        d.ExpiresOn = expiresOn;
        d.Revision = 1;
        os.CommitChanges();
        return d.Oid;
    }

    private List<string> Columns(string schema, string table)
    {
        var names = new List<string>();
        using var connection = new SqlConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @s AND TABLE_NAME = @t";
        command.Parameters.AddWithValue("@s", schema);
        command.Parameters.AddWithValue("@t", table);
        using var reader = command.ExecuteReader();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    // ---- F1 -------------------------------------------------------------------------------------------------------------

    [Test]
    public void L1_F1_T4_T5_a_store_mapped_to_a_non_dbo_schema_is_one_table_for_XPO_and_for_the_writers_T_SQL()
    {
        using var services = Services<FixPassSchemaStore>(_provider);
        Assert.That(new EditDraftStoreRegistration(typeof(FixPassSchemaStore)).QualifiedName, Is.EqualTo("[drafts].[FixPassDraft]"));
        Assert.That(EditDraftStartup.DatabaseProblems(services, checkTable: true), Is.Empty, "SQL Server, XPO's name equals the library's, the table exists");
        Assert.That(Columns("drafts", "FixPassDraft"), Does.Contain("LoginIsStaffMember").And.Contain("SubSectionOid"), "T5: columns kept (O-1)");

        var now = DateTime.Now;
        var sentinel = AddRow<FixPassDboDecoy>(Guid.NewGuid(), now.AddDays(3), "{\"decoy\":1}");
        var owner = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var writer = new Writer(services, typeof(FixPassSchemaStore));

        // Create: XPO's insert.
        var oid = writer.Call<Guid>("Create", new EditDraftSeed { OwnerUserOid = owner, EditorInstanceId = editor, ObjectType = "Note", TargetOid = Guid.NewGuid(), ViewId = "Note_DetailView" }, "{\"v\":1}", 1, now);
        Assert.That(oid, Is.Not.EqualTo(Guid.Empty));
        Assert.That(Find<FixPassSchemaStore>(oid)?.Revision, Is.EqualTo(1), "XPO reads the row it created");

        // Supersede: the writer's UPDATE. XPO must see the change in the same table.
        var scope = Guid.NewGuid();
        Assert.That(writer.Call<bool>("TrySupersede", oid, 1, owner, "{\"v\":2}", 2, scope, "context", now), Is.True);
        var row = Find<FixPassSchemaStore>(oid);
        Assert.That((row.Revision, row.Payload, row.ScopeOid, row.EntryCount), Is.EqualTo((2, "{\"v\":2}", scope, 2)), "the UPDATE reached XPO's table (ScopeOid through column SubSectionOid)");

        // Discard and claim: two more UPDATEs.
        Assert.That(writer.Call<bool>("TrySoftDiscard", oid, owner, now), Is.True);
        Assert.That(Find<FixPassSchemaStore>(oid).DeletedOn, Is.Not.Null);
        var claimer = Guid.NewGuid();
        Assert.That(writer.Call<int>("TryClaim", oid, 2, owner, claimer, now), Is.EqualTo(3));
        row = Find<FixPassSchemaStore>(oid);
        Assert.That((row.Revision, row.DeletedOn, row.EditorInstanceId), Is.EqualTo((3, (DateTime?)null, claimer)));

        // Delete on save: the writer's DELETE.
        Assert.That(writer.Call<int>("DeleteOwn", oid, owner, claimer), Is.EqualTo(1));
        Assert.That(Find<FixPassSchemaStore>(oid), Is.Null, "the DELETE removed XPO's row");

        // Retention: the T-SQL sweep on the registration's name deletes the expired row in drafts, never the dbo one.
        var expiredDraft = AddRow<FixPassSchemaStore>(Guid.NewGuid(), now.AddDays(-1), "{\"old\":1}");
        var expiredDecoy = AddRow<FixPassDboDecoy>(Guid.NewGuid(), now.AddDays(-1), "{\"decoy\":2}");
        using (var os = _provider.CreateObjectSpace())
            Assert.That(EditDraftRetention.Sweep(os, new EditDraftStoreRegistration(typeof(FixPassSchemaStore)), now), Is.EqualTo(1));
        Assert.That(Find<FixPassSchemaStore>(expiredDraft), Is.Null);

        // The dbo table of the same name: untouched throughout.
        var decoy = Find<FixPassDboDecoy>(sentinel);
        Assert.That((decoy.Revision, decoy.Payload, decoy.DeletedOn), Is.EqualTo((1, "{\"decoy\":1}", (DateTime?)null)), "T4: the dbo sentinel row is unchanged");
        Assert.That(Find<FixPassDboDecoy>(expiredDecoy), Is.Not.Null, "the sweep did not delete from dbo");
    }

    [TestCase("drafts", "[drafts].[FixPassOwnerStore]", "[Persistent(\"drafts.FixPassOwnerStore\")]", TestName = "L2_F1_T8_a_non_default_ObjectsOwner_is_rejected_at_startup")]
    [TestCase(null, "[FixPassOwnerStore]", "[Persistent(\"schema.table\")]", TestName = "L2_F1_T8_no_ObjectsOwner_is_rejected_at_startup")]
    public void L2_F1_T8_when_XPO_would_address_another_table_the_startup_check_stops_before_any_statement(string owner, string xpoName, string fix)
    {
        using var provider = Provider(new OwnerDataStoreProvider(_connectionString, owner), typeof(FixPassOwnerStore));
        using var services = Services<FixPassOwnerStore>(provider);
        var problems = EditDraftStartup.DatabaseProblems(services, checkTable: false);
        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.Contain(xpoName).And.Contain("[dbo].[FixPassOwnerStore]").And.Contain(fix));
        Assert.Throws<EditDraftConfigurationException>(() => RunStartup(services));
    }

    [Test]
    public void L2_F1_the_default_ObjectsOwner_dbo_matches_the_library()
    {
        using var provider = Provider(new OwnerDataStoreProvider(_connectionString, "dbo"), typeof(FixPassOwnerStore));
        using var services = Services<FixPassOwnerStore>(provider);
        Assert.That(EditDraftStartup.DatabaseProblems(services, checkTable: false), Is.Empty);
    }

    // ---- F2 -------------------------------------------------------------------------------------------------------------

    [Test]
    public void L3_F2_T9_T10_a_passed_check_is_remembered_per_store_class_and_connection()
    {
        const string Passed = "startup checks passed: store FixPassCacheStore";
        var lines = WithLog(() =>
        {
            using (var a = Services<FixPassCacheStore>(_provider)) RunStartup(a);
            using (var providerA2 = Provider(_connectionString, typeof(FixPassCacheStore)))
            using (var a2 = Services<FixPassCacheStore>(providerA2)) RunStartup(a2);   // another circuit: new services, same connection
            using (var providerB = Provider(_connectionString + ";Application Name=EditDraftFixPassB", typeof(FixPassCacheStore)))
            using (var b = Services<FixPassCacheStore>(providerB)) RunStartup(b);      // the same store class on another connection
        });
        Assert.That(lines.Count(l => l.Contains(Passed)), Is.EqualTo(2), "connection A once (the second circuit reuses it), connection B checked on its own:\n" + string.Join("\n", lines));
    }

    [Test]
    public void L4_F2_T14_a_missing_table_at_setup_is_a_warning_that_is_not_remembered_as_passed()
    {
        using var services = Services<FixPassLateStore>(_provider, tableCheck: true);
        var first = WithLog(() => Assert.DoesNotThrow(() => RunStartup(services), "A1: at setup a missing table is only a warning"));
        Assert.That(first, Has.Some.Contains("[dbo].[FixPassLateStore] was not found"));
        Assert.That(first, Has.None.Contains("startup checks passed: store FixPassLateStore"));

        CreateTables(typeof(FixPassLateStore));
        var second = WithLog(() => RunStartup(services));
        Assert.That(second, Has.Some.Contains("startup checks passed: store FixPassLateStore at [dbo].[FixPassLateStore]"), "checked again, and now it passes");
    }

    // ---- F4 -------------------------------------------------------------------------------------------------------------

    [Test]
    public void L5_F4_T25_a_fresh_database_with_the_table_check_on_completes_its_first_update_and_the_check_then_finds_the_table()
    {
        var fresh = "XafEditDraftFresh_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        _freshDatabases.Add(fresh);
        var connectionString = $"Data Source={Server};Integrated Security=true;Initial Catalog={fresh}";
        using var provider = Provider(connectionString, typeof(FixPassFreshStore), typeof(ModuleInfo));
        using var services = Services<FixPassFreshStore>(provider, tableCheck: true);
        using var app = new HeadlessApplication { ServiceProvider = services };
        // Harness: XAF's own XafApplication.Setup needs XAF's service registrations, which this minimal provider does not have,
        // and the process-wide XafTypesInfo.Instance (already used by other fixtures) does not make types registered later known
        // to new object spaces. So the provider has a private types info, the module gets its Setup(application) call directly
        // (what XafApplication.Setup does for each module), and the test runs EditDraftStartup.Run itself: the call the module's
        // SetupComplete handler makes (pinned by G1_the_modules_run_the_checks_at_setup).
        var core = new EditDraftCoreModule();
        core.Setup(app);

        var lines = WithLog(() =>
        {
            Assert.DoesNotThrow(() => EditDraftStartup.Run(app), "A1: the setup check must not stop the first update");
            // XAF's DatabaseUpdater: the update both --updateDatabase and the Debug template's DatabaseVersionMismatch handler run.
            using var updater = new DatabaseUpdater(provider, new List<ModuleBase> { core }, "EditDraftFixPassFresh", provider.ModuleInfoType);
            updater.Update();
        });
        Assert.That(lines, Has.Some.Contains("table check after the database update: store table [dbo].[FixPassFreshStore] found"), string.Join("\n", lines));
        Assert.That(EditDraftStartup.DatabaseProblems(services, checkTable: true), Is.Empty, "the table now exists");
    }

    [Test]
    public void L6_F4_T27_a_table_still_missing_after_the_schema_update_stops_the_update_and_with_the_check_off_nothing_is_checked()
    {
        foreach (var tableCheck in new[] { true, false })
        {
            using var provider = Provider(_connectionString, typeof(FixPassNeverStore));
            using var services = Services<FixPassNeverStore>(provider, tableCheck);
            using var app = new HeadlessApplication { ServiceProvider = services };
            var core = new EditDraftCoreModule();
            core.Setup(app);   // harness: see L5
            Assert.DoesNotThrow(() => EditDraftStartup.Run(app), $"tableCheck={tableCheck}: setup only warns");

            using var os = provider.CreateUpdatingObjectSpace(false);
            var check = core.GetModuleUpdaters(os, new Version(0, 0, 0, 0)).Single(u => u.GetType().Name == "EditDraftTableCheckUpdater");
            if (tableCheck)
            {
                var ex = Assert.Throws<EditDraftConfigurationException>(() => check.UpdateDatabaseAfterUpdateSchema());
                Assert.That(ex.Message, Does.Contain("[dbo].[FixPassNeverStore]").And.Contain("after the XAF database update"));
            }
            else
                Assert.DoesNotThrow(() => check.UpdateDatabaseAfterUpdateSchema());
        }
    }
}
