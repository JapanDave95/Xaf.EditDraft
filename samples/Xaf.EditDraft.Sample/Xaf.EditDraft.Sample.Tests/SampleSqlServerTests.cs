using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.DC.Xpo;
using DevExpress.ExpressApp.Xpo;
using DevExpress.Xpo;
using DevExpress.Xpo.DB;
using DevExpress.Xpo.Metadata;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Xaf.EditDraft.Core;
using Xaf.EditDraft.Sample.Module.BusinessObjects;

namespace Xaf.EditDraft.Sample.Tests;

/// <summary>A store in a schema whose name needs quoting, under a table name that is a T-SQL keyword (Codex T23).</summary>
[Persistent("edit drafts.Order")]
public class QuotedSchemaStore : EditDraftStoreBase
{
    public QuotedSchemaStore(Session session) : base(session) { }
}

/// <summary>A store whose table is never created (Codex T26/T27).</summary>
public class MissingTableStore : EditDraftStoreBase
{
    public MissingTableStore(Session session) : base(session) { }
}

/// <summary>
/// Close the library gaps (run 2026-10-04-editdraft-close-gaps-08c338): the SQL Server parts executed against SQL Server.
/// Each run creates its own throwaway LocalDB database (XafEditDraftTests_&lt;random&gt;) and drops it at the end; nothing
/// else is touched. Skipped (Assert.Ignore) where LocalDB is not installed, e.g. on the GitHub build agent. Expectation ids
/// Tn are from the Codex requirement-only list of that run (tests a1).
/// </summary>
[TestFixture]
[NonParallelizable]
public class SampleSqlServerTests
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

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _utc;
        public FixedClock(DateTime local) => _utc = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local)).ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _utc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
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
        _database = "XafEditDraftTests_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Run("master", $"CREATE DATABASE [{_database}]");
        Run(_database, "CREATE SCHEMA [edit drafts]");
        _connectionString = $"Data Source={Server};Integrated Security=true;Initial Catalog={_database}";

        var dictionary = new ReflectionDictionary();
        dictionary.CollectClassInfos(typeof(SampleEditDraft), typeof(QuotedSchemaStore));
        using (var layer = new SimpleDataLayer(dictionary, XpoDefault.GetConnectionProvider(_connectionString, AutoCreateOption.DatabaseAndSchema)))
        using (var uow = new UnitOfWork(layer))
            uow.UpdateSchema(typeof(SampleEditDraft), typeof(QuotedSchemaStore));

        var typesInfo = new TypesInfo();
        var source = new XpoTypeInfoSource(typesInfo);
        typesInfo.AddEntityStore(source);
        foreach (var type in new[] { typeof(SampleEditDraft), typeof(QuotedSchemaStore), typeof(MissingTableStore) }) typesInfo.RegisterEntity(type);
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

    private ServiceProvider Services<TStore>(TimeProvider clock = null) where TStore : EditDraftStoreBase
    {
        var services = new ServiceCollection();
        services.AddSingleton<INonSecuredObjectSpaceFactory>(new Factory(_provider));
        services.AddEditDraftStore<TStore>();
        if (clock != null) services.AddSingleton(clock);
        return services.BuildServiceProvider();
    }

    private static readonly Guid[] Owners = { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

    private int Count()
    {
        using var os = _provider.CreateObjectSpace();
        return os.GetObjectsQuery<QuotedSchemaStore>().Count();
    }

    private void Clear()
    {
        using var os = _provider.CreateObjectSpace();
        os.Delete(os.GetObjects<QuotedSchemaStore>());
        os.CommitChanges();
    }

    private void Add(Guid owner, DateTime expiresOn)
    {
        using var os = _provider.CreateObjectSpace();
        var d = os.CreateObject<QuotedSchemaStore>();
        d.DraftKey = Guid.NewGuid();
        d.OwnerUserOid = owner;
        d.ObjectType = "Note";
        d.TargetOid = Guid.NewGuid();
        d.FirstCapturedOn = expiresOn.AddDays(-EditDraftStoreBase.RetentionDays);
        d.LastCapturedOn = d.FirstCapturedOn;
        d.ExpiresOn = expiresOn;
        d.Revision = 1;
        os.CommitChanges();
    }

    [Test]
    public void Q1_T24_T26_a_SQL_Server_store_passes_the_provider_check_and_the_table_check()
    {
        using var services = Services<SampleEditDraft>();
        Assert.That(EditDraftStartup.DatabaseProblems(services, checkTable: true), Is.Empty);
        using var os = (XPObjectSpace)_provider.CreateObjectSpace();
        Assert.That(EditDraftSqlServer.Classify(os.Session, out var provider), Is.EqualTo(EditDraftDatabaseKind.SqlServer), provider);
    }

    [Test]
    public void Q2_T26_T27_a_missing_table_is_a_problem_only_when_the_table_check_is_on()
    {
        using var services = Services<MissingTableStore>();
        Assert.That(EditDraftStartup.DatabaseProblems(services, checkTable: false), Is.Empty, "the optional check is off");
        var problems = EditDraftStartup.DatabaseProblems(services, checkTable: true);
        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.Contain("[dbo].[MissingTableStore]").And.Contain("database update"));
    }

    [Test]
    public void Q3_T22_T23_the_probe_finds_a_store_in_a_quoted_schema_under_a_keyword_name()
    {
        using var services = Services<QuotedSchemaStore>();
        Assert.That(new EditDraftStoreRegistration(typeof(QuotedSchemaStore)).QualifiedName, Is.EqualTo("[edit drafts].[Order]"));
        Assert.That(EditDraftSwitch.IsRestoreAvailable(services), Is.True, "not probed as absent (G6)");
        Assert.That(EditDraftStartup.DatabaseProblems(services, checkTable: true), Is.Empty);
    }

    [Test]
    public void Q4_T12_T13_T14_the_sweep_deletes_rows_of_every_owner_at_and_before_the_cutoff_and_keeps_later_ones()
    {
        Clear();
        var cutoff = new DateTime(2026, 10, 11, 9, 0, 0);
        foreach (var owner in Owners)
        {
            Add(owner, cutoff.AddSeconds(-1));
            Add(owner, cutoff);              // the exact instant: hidden, so deleted
            Add(owner, cutoff.AddSeconds(1));
        }
        var store = new EditDraftStoreRegistration(typeof(QuotedSchemaStore));
        using (var os = _provider.CreateObjectSpace())
            Assert.That(EditDraftRetention.Sweep(os, store, cutoff), Is.EqualTo(6), "two per owner, across owners");
        Assert.That(Count(), Is.EqualTo(3));
        using (var os = _provider.CreateObjectSpace())
        {
            Assert.That(os.GetObjectsQuery<QuotedSchemaStore>().All(d => d.ExpiresOn > cutoff), Is.True);
            Assert.That(os.GetObjectsQuery<QuotedSchemaStore>().Select(d => d.OwnerUserOid).Distinct().Count(), Is.EqualTo(3));
            Assert.That(EditDraftRetention.Sweep(os, store, cutoff), Is.EqualTo(0), "T14: a repeated sweep deletes nothing");
        }
        Clear();
        using (var os = _provider.CreateObjectSpace())
            Assert.That(EditDraftRetention.Sweep(os, store, cutoff), Is.EqualTo(0), "T14: an empty store");
    }

    [Test]
    public void Q5_T18_T19_the_scheduled_sweep_uses_the_application_clock_not_the_database_clock()
    {
        Clear();
        var appNow = new DateTime(2030, 1, 1, 12, 0, 0);   // far from the database server's GETDATE()
        Add(Owners[0], appNow.AddMinutes(-1));
        Add(Owners[1], appNow.AddMinutes(1));
        using var services = Services<QuotedSchemaStore>(new FixedClock(appNow));
        Assert.That(EditDraftRetention.Sweep(services), Is.EqualTo(1), "only the row expired by the application clock");
        Assert.That(Count(), Is.EqualTo(1));
        Clear();
    }

    [Test]
    public void Q6_T15_a_sweep_that_fails_reports_minus_one()
    {
        using var os = _provider.CreateObjectSpace();
        Assert.That(EditDraftRetention.Sweep(os, new EditDraftStoreRegistration(typeof(MissingTableStore)), DateTime.Now), Is.EqualTo(-1), "the table does not exist");
    }
}
