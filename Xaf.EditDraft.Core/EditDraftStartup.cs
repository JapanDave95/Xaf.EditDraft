using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Xpo;
using DevExpress.Persistent.BaseImpl.PermissionPolicy;
using DevExpress.Xpo;
using DevExpress.Xpo.DB;
using DevExpress.Xpo.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Xaf.EditDraft.Core;

/// <summary>Thrown at application setup when Xaf.EditDraft is not configured correctly. The message names each problem and its fix.</summary>
public sealed class EditDraftConfigurationException : InvalidOperationException
{
    public EditDraftConfigurationException(string message) : base(message) { }
}

/// <summary>What the XPO data store of a session is, for the SQL-Server-only contract.</summary>
public enum EditDraftDatabaseKind
{
    SqlServer,
    NotSqlServer,
    Unknown
}

/// <summary>
/// Gap G6 (2026-10-04): the library's writer and retention sweep use T-SQL, so the store must be in SQL Server. The check
/// looks at the XPO data store's TYPE (never runs a statement): XPO's MSSqlConnectionProvider is SQL Server; any other
/// XPO provider (DataStoreBase: SQLite, PostgreSQL, Oracle, MySQL, the in-memory and DataSet stores ...) is not. A fork
/// store (DataStoreForkBase, for example the DataStorePool XAF uses with XpoDataStorePool=True) is looked through: one
/// read provider is borrowed, classified and given back (borrowing can open a connection; no statement is run). Any
/// other wrapper is identified by its ADO.NET connection when it exposes one, else it is Unknown.
/// </summary>
public static class EditDraftSqlServer
{
    public static EditDraftDatabaseKind Classify(Session session, out string providerName) => Classify(session, null, out providerName, out _);

    /// <summary>
    /// <see cref="Classify(Session, out string)"/>, and, when the data store is XPO's SQL Server provider and
    /// <paramref name="storeType"/> is given, the schema and table that provider composes for the store class's table name in
    /// this session's XPO dictionary (ComposeSafeSchemaName / ComposeSafeTableName; the schema is the provider's ObjectsOwner
    /// for a name without one). Null otherwise. No statement is run (Codex C1: one source of truth for the store table).
    /// </summary>
    internal static EditDraftDatabaseKind Classify(Session session, Type storeType, out string providerName, out (string Schema, string Table)? xpoName)
    {
        providerName = null;
        xpoName = null;
        var layer = session?.DataLayer;
        if (layer == null) { providerName = "none"; return EditDraftDatabaseKind.Unknown; }
        var xpoTableName = storeType == null ? null : session.GetClassInfo(storeType).TableName;
        var kind = ClassifyStore((layer as BaseDataLayer)?.ConnectionProvider, 0, xpoTableName, out providerName, out xpoName);
        if (kind != EditDraftDatabaseKind.Unknown) return kind;
        try
        {
            var connection = layer.Connection;
            if (connection != null)
            {
                providerName ??= connection.GetType().Name;
                if (IsSqlClient(connection)) return EditDraftDatabaseKind.SqlServer;
            }
        }
        catch { /* no ADO.NET connection to look at */ }
        return EditDraftDatabaseKind.Unknown;
    }

    private static EditDraftDatabaseKind ClassifyStore(IDataStore store, int depth, string xpoTableName, out string name, out (string Schema, string Table)? xpoName)
    {
        name = store?.GetType().Name;
        xpoName = null;
        switch (store)
        {
            case null:
                return EditDraftDatabaseKind.Unknown;
            case MSSqlConnectionProvider sql:
                if (xpoTableName != null) xpoName = (sql.ComposeSafeSchemaName(xpoTableName), sql.ComposeSafeTableName(xpoTableName));
                return EditDraftDatabaseKind.SqlServer;
            case DataStoreBase:
                return EditDraftDatabaseKind.NotSqlServer;
            case DataStoreForkBase fork when depth < 3:
                IDataStore inner = null;
                try
                {
                    inner = fork.AcquireReadProvider();
                    var kind = ClassifyStore(inner, depth + 1, xpoTableName, out var innerName, out xpoName);
                    name = $"{name}({innerName})";
                    return kind;
                }
                catch { xpoName = null; return EditDraftDatabaseKind.Unknown; }
                finally { if (inner != null) fork.ReleaseReadProvider(inner); }
            default:
                return EditDraftDatabaseKind.Unknown;
        }
    }

    private static bool IsSqlClient(IDbConnection connection) =>
        connection.GetType().FullName is "Microsoft.Data.SqlClient.SqlConnection" or "System.Data.SqlClient.SqlConnection";
}

/// <summary>
/// Startup checks (gaps G1, G2, G6, 2026-10-04; Codex C1, C2 and A1 fixed the same day). <see cref="EditDraftCoreModule"/>
/// runs <see cref="Run"/> when the XAF application's setup completes, and the optional table check again after the XAF
/// database update's schema update (EditDraftCoreModule's module updater); the Blazor module (Xaf.EditDraft.Blazor,
/// EditDraftBlazorModule) runs <see cref="RequireNonPersistentProvider"/>. A problem stops the application (or the database
/// update) with an <see cref="EditDraftConfigurationException"/> whose message names the fix. Checks:
/// 1. a store class is registered (AddEditDraftStore&lt;TStore&gt;);
/// 2. while EditDraftCapture:Enabled is true, at least one policy is registered (AddEditDraftRegistry);
/// 3. the store's database is SQL Server (by the XPO data store's type; no statement is run);
/// 4. XPO's SQL Server provider names the store table as the library does: the schema and table it composes for the store
///    class (ComposeSafeSchemaName / ComposeSafeTableName, ObjectsOwner included) equal
///    <see cref="EditDraftStoreRegistration.QualifiedName"/> (no statement is run);
/// 5. optional, when EditDraftCapture:StartupChecks:Table is true: the store table exists. At setup a missing table is only
///    a warning, because the database update that creates it runs after setup (Codex A1). After the update's schema update
///    a table that is still missing stops the update.
/// 6. Blazor: a NonPersistentObjectSpaceProvider is registered (DevExpress XAF 26.1 adds one itself unless the application
///    overrides XafApplication.EnsureNonPersistentObjectSpaceProvider).
/// Caching (Codex C2): checks 3-5 at setup, and the role warning (EditDraftSecurity.WarnRolesThatCanReadStore, which only
/// logs), are remembered per store class and database (its connection string, else its XPO data store object) and only after they
/// PASS. A check that failed or could not run (an exception, an unidentified data store, a missing table) is logged and runs
/// again at the next application setup; in Blazor every circuit sets up its own application. An application without a
/// service provider (a headless or design-time application) is not checked.
/// </summary>
public static class EditDraftStartup
{
    /// <summary>Optional table check (named in the default section; a custom section moves it like the other switches).</summary>
    public const string TableCheckKey = "EditDraftCapture:StartupChecks:Table";

    /// <summary>How the database check treats the optional table check.</summary>
    internal enum TableCheck { Off, Warn, Require }

    /// <summary>What has passed for one store class on one database (Codex C2).</summary>
    internal sealed class CheckState
    {
        public volatile bool DatabasePassed;
        public volatile bool RolesChecked;
    }

    /// <summary>The outcome of checks 3-5 on the store's database.</summary>
    internal sealed class DatabaseCheck
    {
        public List<string> Problems { get; } = new();

        /// <summary>True only when every check ran and passed: positively SQL Server, the XPO name matches, the table found when checked.</summary>
        public bool Passed { get; set; }

        /// <summary>The remembered state of this store class on this database; null when the check did not get that far.</summary>
        public CheckState State { get; set; }
    }

    private static readonly ConcurrentDictionary<(Type Store, string Connection), CheckState> StateByConnection = new();
    private static readonly ConditionalWeakTable<object, ConcurrentDictionary<Type, CheckState>> StateByLayer = new();

    /// <summary>
    /// The database identity of the startup cache (Codex C2): the connection string of the data layer, or of XPO's SQL provider
    /// under it (XPO's ThreadSafeDataLayer does not expose its connection), else the data store object (a DataStorePool or
    /// another wrapper, which the application's object space providers share), else the data layer, else null. Held in memory
    /// only, never logged.
    /// </summary>
    internal static object CheckKeyOf(Session session)
    {
        var layer = session?.DataLayer;
        if (layer == null) return null;
        try
        {
            var connectionString = layer.Connection?.ConnectionString;
            if (!string.IsNullOrEmpty(connectionString)) return connectionString;
        }
        catch { /* no ADO.NET connection on the layer */ }
        var store = (layer as BaseDataLayer)?.ConnectionProvider;
        try
        {
            var connectionString = (store as ConnectionProviderSql)?.Connection?.ConnectionString;
            if (!string.IsNullOrEmpty(connectionString)) return connectionString;
        }
        catch { /* no ADO.NET connection on the provider */ }
        return (object)store ?? layer;
    }

    /// <summary>
    /// The remembered state of <paramref name="storeType"/> on the database <paramref name="databaseKey"/>
    /// (<see cref="CheckKeyOf"/>): per connection string, else per data store or data layer object. A database without an
    /// identity gets a new state each time, so nothing is remembered for it.
    /// </summary>
    internal static CheckState StateOf(Type storeType, object databaseKey) => databaseKey switch
    {
        null => new CheckState(),
        string connection => StateByConnection.GetOrAdd((storeType, connection), _ => new CheckState()),
        _ => StateByLayer.GetValue(databaseKey, _ => new ConcurrentDictionary<Type, CheckState>()).GetOrAdd(storeType, _ => new CheckState())
    };

    /// <summary>Checks 1-2: registrations and configuration only, no database.</summary>
    public static IReadOnlyList<string> ConfigurationProblems(IServiceProvider services)
    {
        var problems = new List<string>();
        var store = services?.GetService(typeof(EditDraftStoreRegistration)) as EditDraftStoreRegistration;
        if (store == null)
            problems.Add("No store class is registered. Fix: call services.AddEditDraftStore<TStore>() with your own persistent subclass of EditDraftStoreBase.");
        if (EditDraftSwitch.IsGlobalEnabled(services))
        {
            var registry = services.GetService(typeof(EditDraftRegistry)) as EditDraftRegistry;
            if (registry == null || registry.All.Count == 0)
                problems.Add($"{EditDraftSwitch.In(services, EditDraftSwitch.EnabledKey)} is true but no edit-draft policy is registered. " +
                             "Fix: call services.AddEditDraftRegistry(registry => registry.Register(...)) with at least one EditDraftTypePolicy, or switch capture off.");
        }
        return problems;
    }

    /// <summary>Check 6, pure: null when a NonPersistentObjectSpaceProvider is among <paramref name="providers"/>, else the problem.</summary>
    public static string NonPersistentProviderProblem(IEnumerable<IObjectSpaceProvider> providers) =>
        providers != null && providers.Any(p => p is NonPersistentObjectSpaceProvider)
            ? null
            : "No NonPersistentObjectSpaceProvider is registered, so the restore popup and the drafts list (non-persistent objects) cannot be shown. " +
              "Fix: add .AddNonPersistent() to builder.ObjectSpaceProviders in Startup (DevExpress XAF 26.1 adds this provider itself unless the " +
              "application overrides XafApplication.EnsureNonPersistentObjectSpaceProvider).";

    /// <summary>Check 6 for an application whose setup has completed; throws <see cref="EditDraftConfigurationException"/>.</summary>
    public static void RequireNonPersistentProvider(XafApplication application)
    {
        if (application == null) throw new ArgumentNullException(nameof(application));
        var problem = NonPersistentProviderProblem(application.ObjectSpaceProviders);
        if (problem != null) throw new EditDraftConfigurationException(Message(new[] { problem }));
    }

    /// <summary>
    /// Checks 3-5 on the store's database through a non-secured object space; nothing is remembered. With
    /// <paramref name="checkTable"/> a missing table, or a database that cannot be read, is a problem; without it a failure
    /// to run the check is logged and left to the writer, which fails closed.
    /// </summary>
    public static IReadOnlyList<string> DatabaseProblems(IServiceProvider services, bool checkTable) =>
        CheckDatabase(services, checkTable ? TableCheck.Require : TableCheck.Off, useCache: false).Problems;

    /// <summary>
    /// Checks 3-5 through a non-secured object space. With <paramref name="useCache"/> the database's remembered state is
    /// read first, and a store class that has passed on this database is not checked again (<see cref="DatabaseCheck.State"/>).
    /// </summary>
    internal static DatabaseCheck CheckDatabase(IServiceProvider services, TableCheck table, bool useCache)
    {
        var result = new DatabaseCheck();
        var store = services?.GetService(typeof(EditDraftStoreRegistration)) as EditDraftStoreRegistration;
        if (store == null) return result;   // check 1 reports it
        IServiceScope scope = null;
        IObjectSpace space = null;
        try
        {
            scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope();
            space = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>().CreateNonSecuredObjectSpace(store.StoreType);
            var session = (space as XPObjectSpace)?.Session;
            if (session == null)
            {
                result.Problems.Add($"The store class {store.StoreType.FullName} is not served by an XPO object space ({space?.GetType().Name ?? "none"}). Xaf.EditDraft supports XPO on SQL Server only.");
                return result;
            }
            if (useCache)
            {
                result.State = StateOf(store.StoreType, CheckKeyOf(session));
                if (result.State.DatabasePassed)
                {
                    result.Passed = true;
                    return result;
                }
            }
            CheckSession(session, store, table, result, useCache);
        }
        catch (Exception ex)
        {
            if (table == TableCheck.Require)
                result.Problems.Add($"The store table {store.QualifiedName} could not be checked ({ex.GetType().Name}). Fix: check the connection string and that the database exists, then run the XAF database update.");
            else
                EditDraftLog.Warning($"[EditDraft] startup: the database check of {store.StoreType.Name} could not run ({ex.GetType().Name})" + Again(useCache));
        }
        finally
        {
            space?.Dispose();
            scope?.Dispose();
        }
        return result;
    }

    /// <summary>Checks 3-5 on one session. A failure to probe the table propagates, except in <see cref="TableCheck.Warn"/> mode.</summary>
    private static void CheckSession(Session session, EditDraftStoreRegistration store, TableCheck table, DatabaseCheck result, bool useCache)
    {
        var kind = EditDraftSqlServer.Classify(session, store.StoreType, out var provider, out var xpoName);
        if (kind == EditDraftDatabaseKind.NotSqlServer)
        {
            result.Problems.Add($"The store class {store.StoreType.FullName} is in a database whose XPO data store is {provider}. Xaf.EditDraft supports SQL Server only " +
                                "(its writer and retention sweep use T-SQL). Fix: keep the store in a SQL Server database (XPO MSSqlConnectionProvider).");
            return;
        }
        if (kind == EditDraftDatabaseKind.Unknown)
            EditDraftLog.Warning($"[EditDraft] startup: the data store of {store.StoreType.Name} could not be identified ({provider ?? "unknown"}); Xaf.EditDraft supports SQL Server only" + Again(useCache));
        else if (xpoName is { } name && XpoQualifiedName(name) is var xpoQualified && !string.Equals(xpoQualified, store.QualifiedName, StringComparison.Ordinal))
        {
            var mapping = string.IsNullOrEmpty(name.Schema) ? "schema.table" : name.Schema + "." + name.Table;
            result.Problems.Add($"XPO addresses the store table of {store.StoreType.FullName} as {xpoQualified}, but the library's T-SQL addresses {store.QualifiedName} " +
                                $"(from the store class's XPO table name \"{store.TableName}\"). Fix: name the schema and table in the store class mapping, " +
                                $"[Persistent(\"{mapping}\")], and do not change XPO's schema or table name for it elsewhere (MSSqlConnectionProvider.ObjectsOwner, table prefixes).");
            return;
        }
        var positive = kind == EditDraftDatabaseKind.SqlServer;
        if (table == TableCheck.Off)
        {
            result.Passed = positive;
            return;
        }
        bool exists;
        try { exists = TableExists(session, store); }
        catch (Exception ex) when (table == TableCheck.Warn)
        {
            EditDraftLog.Warning($"[EditDraft] startup: the store table {store.QualifiedName} could not be checked ({ex.GetType().Name})" + Again(useCache));
            return;
        }
        if (exists)
        {
            result.Passed = positive;
            return;
        }
        if (table == TableCheck.Require)
            result.Problems.Add($"The store table {store.QualifiedName} was not found. Fix: run the XAF database update so that XPO creates it, " +
                                "or map the store class to the schema and table the table is in ([Persistent(\"schema.table\")]).");
        else
            EditDraftLog.Warning($"[EditDraft] startup: the store table {store.QualifiedName} was not found. The XAF database update creates it; " +
                                 "the table check runs again after the update's schema update and at the next application setup");
    }

    private static string Again(bool useCache) => useCache ? "; it runs again at the next application setup" : string.Empty;

    /// <summary>The quoted name XPO's provider uses: <c>[schema].[table]</c>, or <c>[table]</c> when it composes no schema.</summary>
    private static string XpoQualifiedName((string Schema, string Table) name) =>
        string.IsNullOrEmpty(name.Schema)
            ? EditDraftSql.QuoteIdentifier(name.Table)
            : EditDraftSql.QuoteIdentifier(name.Schema) + "." + EditDraftSql.QuoteIdentifier(name.Table);

    /// <summary>
    /// Check 5 after the XAF database update's schema update (Codex A1), run by EditDraftCoreModule's module updater: with
    /// <see cref="TableCheckKey"/> on, a store table that is still missing stops the update with an
    /// <see cref="EditDraftConfigurationException"/>. <paramref name="updatingSpace"/> is the update's object space; an update
    /// of a database that does not hold the store class (another object space provider), or whose data store is not SQL
    /// Server (check 3 stops that application at setup), is not checked here.
    /// </summary>
    internal static void RequireTableAfterSchemaUpdate(IServiceProvider services, IObjectSpace updatingSpace)
    {
        if (services == null || updatingSpace == null || !IsTableCheckOn(services)) return;
        var store = services.GetService(typeof(EditDraftStoreRegistration)) as EditDraftStoreRegistration;
        var session = (updatingSpace as XPObjectSpace)?.Session;
        if (store == null || session == null || !updatingSpace.IsKnownType(store.StoreType)) return;
        if (EditDraftSqlServer.Classify(session, out _) == EditDraftDatabaseKind.NotSqlServer) return;
        if (!TableExists(session, store))
            throw new EditDraftConfigurationException(Message(new[]
            {
                $"The store table {store.QualifiedName} was not found after the XAF database update's schema update. Fix: map the store class to " +
                "the schema and table XPO creates ([Persistent(\"schema.table\")]) and make sure a module exports the store class, then run the update again."
            }));
        EditDraftLog.Info($"[EditDraft] table check after the database update: store table {store.QualifiedName} found");
    }

    /// <summary>OBJECT_ID of the quoted, schema-qualified name, passed as a parameter.</summary>
    internal static bool TableExists(Session session, EditDraftStoreRegistration store) =>
        Convert.ToInt32(session.ExecuteScalar("SELECT CASE WHEN OBJECT_ID(@p0) IS NULL THEN 0 ELSE 1 END", new[] { "@p0" }, new object[] { store.QualifiedName }),
            CultureInfo.InvariantCulture) == 1;

    /// <summary>True when <see cref="TableCheckKey"/> (in the configured section) reads as the boolean true.</summary>
    public static bool IsTableCheckOn(IServiceProvider services)
    {
        try
        {
            var configuration = services?.GetService<IConfiguration>();
            return configuration != null && EditDraftSwitch.IsOn(configuration[EditDraftSwitch.In(services, TableCheckKey)]);
        }
        catch { return false; }
    }

    /// <summary>
    /// Checks 1-5 for <paramref name="application"/> (check 5 only warns here, Codex A1), then the role warning; throws
    /// <see cref="EditDraftConfigurationException"/> on a problem. What passed is remembered per store class and database;
    /// what failed or could not run is checked again at the next call (Codex C2).
    /// </summary>
    public static void Run(XafApplication application)
    {
        var services = application?.ServiceProvider;
        if (services == null)
        {
            EditDraftLog.Info("[EditDraft] startup checks skipped: the application has no service provider");
            return;
        }
        var problems = new List<string>(ConfigurationProblems(services));
        var store = services.GetService(typeof(EditDraftStoreRegistration)) as EditDraftStoreRegistration;
        if (problems.Count == 0 && store != null)
        {
            var check = CheckDatabase(services, IsTableCheckOn(services) ? TableCheck.Warn : TableCheck.Off, useCache: true);
            problems.AddRange(check.Problems);
            var state = check.State ?? new CheckState();   // no database identity: nothing is remembered
            if (problems.Count == 0)
            {
                if (check.Passed && !state.DatabasePassed)
                {
                    state.DatabasePassed = true;
                    EditDraftLog.Info($"[EditDraft] startup checks passed: store {store.StoreType.Name} at {store.QualifiedName}");
                }
                if (!state.RolesChecked && WarnRoles(services, store)) state.RolesChecked = true;
            }
        }
        if (problems.Count > 0) throw new EditDraftConfigurationException(Message(problems));
    }

    /// <summary>
    /// SINGLE-MODEL (owner review): logs the roles that can read the store; never throws. True when the scan completed (or
    /// there is no XPO PermissionPolicyRole to scan); false when it failed, so that it runs again at the next setup.
    /// </summary>
    private static bool WarnRoles(IServiceProvider services, EditDraftStoreRegistration store)
    {
        try
        {
            using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope();
            using var space = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>().CreateNonSecuredObjectSpace(typeof(PermissionPolicyRoleBase));
            if (!space.CanInstantiate(typeof(PermissionPolicyRoleBase)))
            {
                EditDraftLog.Info("[EditDraft] startup: no XPO PermissionPolicyRole in this application; role check skipped");
                return true;
            }
            EditDraftSecurity.WarnRolesThatCanReadStore(space, store.StoreType);
            return true;
        }
        catch (Exception ex)
        {
            EditDraftLog.Info($"[EditDraft] startup: role check failed ({ex.GetType().Name}); it runs again at the next application setup");
            return false;
        }
    }

    private static string Message(IEnumerable<string> problems) =>
        "Xaf.EditDraft is not configured correctly:" + string.Concat(problems.Select(p => Environment.NewLine + "- " + p));
}
