using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
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
    public static EditDraftDatabaseKind Classify(Session session, out string providerName)
    {
        providerName = null;
        var layer = session?.DataLayer;
        if (layer == null) { providerName = "none"; return EditDraftDatabaseKind.Unknown; }
        var kind = ClassifyStore((layer as BaseDataLayer)?.ConnectionProvider, 0, out providerName);
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

    private static EditDraftDatabaseKind ClassifyStore(IDataStore store, int depth, out string name)
    {
        name = store?.GetType().Name;
        switch (store)
        {
            case null:
                return EditDraftDatabaseKind.Unknown;
            case MSSqlConnectionProvider:
                return EditDraftDatabaseKind.SqlServer;
            case DataStoreBase:
                return EditDraftDatabaseKind.NotSqlServer;
            case DataStoreForkBase fork when depth < 3:
                IDataStore inner = null;
                try
                {
                    inner = fork.AcquireReadProvider();
                    var kind = ClassifyStore(inner, depth + 1, out var innerName);
                    name = $"{name}({innerName})";
                    return kind;
                }
                catch { return EditDraftDatabaseKind.Unknown; }
                finally { if (inner != null) fork.ReleaseReadProvider(inner); }
            default:
                return EditDraftDatabaseKind.Unknown;
        }
    }

    private static bool IsSqlClient(IDbConnection connection) =>
        connection.GetType().FullName is "Microsoft.Data.SqlClient.SqlConnection" or "System.Data.SqlClient.SqlConnection";
}

/// <summary>
/// Startup checks (gaps G1, G2, G6, 2026-10-04). <see cref="EditDraftCoreModule"/> runs <see cref="Run"/> when the XAF
/// application's setup completes; the Blazor module (Xaf.EditDraft.Blazor, EditDraftBlazorModule) runs
/// <see cref="RequireNonPersistentProvider"/>. A problem stops the application with an
/// <see cref="EditDraftConfigurationException"/> whose message names the fix. Checks:
/// 1. a store class is registered (AddEditDraftStore&lt;TStore&gt;);
/// 2. the schema option does not contradict the store's XPO mapping;
/// 3. while EditDraftCapture:Enabled is true, at least one policy is registered (AddEditDraftRegistry);
/// 4. the store's database is SQL Server (by the XPO data store's type; no statement is run);
/// 5. optional, when EditDraftCapture:StartupChecks:Table is true: the store table exists;
/// 6. Blazor: a NonPersistentObjectSpaceProvider is registered (DevExpress XAF 26.1 adds one itself unless the application
///    overrides XafApplication.EnsureNonPersistentObjectSpaceProvider).
/// Checks 4-5 and the role warning (EditDraftSecurity.WarnRolesThatCanReadStore, which only logs) run once per process
/// per store class after they pass. An application without a service provider (a headless or design-time application)
/// is not checked.
/// </summary>
public static class EditDraftStartup
{
    /// <summary>Optional table check (named in the default section; a custom section moves it like the other switches).</summary>
    public const string TableCheckKey = "EditDraftCapture:StartupChecks:Table";

    private static readonly ConcurrentDictionary<Type, bool> DatabaseChecked = new();

    /// <summary>Checks 1-3: registrations and configuration only, no database.</summary>
    public static IReadOnlyList<string> ConfigurationProblems(IServiceProvider services)
    {
        var problems = new List<string>();
        var store = services?.GetService(typeof(EditDraftStoreRegistration)) as EditDraftStoreRegistration;
        if (store == null)
            problems.Add("No store class is registered. Fix: call services.AddEditDraftStore<TStore>() with your own persistent subclass of EditDraftStoreBase.");
        else if (store.SchemaConflicts)
            problems.Add($"The store class {store.StoreType.FullName} is mapped to schema \"{store.Schema}\" (XPO table name \"{store.TableName}\") " +
                         $"but EditDraftStoreOptions.Schema is \"{store.ConfiguredSchema}\". Fix: remove the option or set it to \"{store.Schema}\".");
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
    /// Checks 4-5 on the store's database through a non-secured object space. A database that cannot be reached is a
    /// problem only when <paramref name="checkTable"/> is on; otherwise it is logged and left to the writer, which fails closed.
    /// </summary>
    public static IReadOnlyList<string> DatabaseProblems(IServiceProvider services, bool checkTable)
    {
        var problems = new List<string>();
        var store = services?.GetService(typeof(EditDraftStoreRegistration)) as EditDraftStoreRegistration;
        if (store == null) return problems;   // check 1 reports it
        IServiceScope scope = null;
        IObjectSpace space = null;
        try
        {
            scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope();
            space = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>().CreateNonSecuredObjectSpace(store.StoreType);
            var session = (space as XPObjectSpace)?.Session;
            if (session == null)
            {
                problems.Add($"The store class {store.StoreType.FullName} is not served by an XPO object space ({space?.GetType().Name ?? "none"}). Xaf.EditDraft supports XPO on SQL Server only.");
                return problems;
            }
            var kind = EditDraftSqlServer.Classify(session, out var provider);
            if (kind == EditDraftDatabaseKind.NotSqlServer)
            {
                problems.Add($"The store class {store.StoreType.FullName} is in a database whose XPO data store is {provider}. Xaf.EditDraft supports SQL Server only " +
                             "(its writer and retention sweep use T-SQL). Fix: keep the store in a SQL Server database (XPO MSSqlConnectionProvider).");
                return problems;
            }
            if (kind == EditDraftDatabaseKind.Unknown)
                EditDraftLog.Warning($"[EditDraft] startup: the data store of {store.StoreType.Name} could not be identified ({provider ?? "unknown"}); Xaf.EditDraft supports SQL Server only");
            if (checkTable && !TableExists(session, store))
                problems.Add($"The store table {store.QualifiedName} was not found. Fix: run the XAF database update so that XPO creates it, " +
                             "or set EditDraftStoreOptions.Schema to the schema the table is in.");
        }
        catch (Exception ex)
        {
            if (checkTable)
                problems.Add($"The store table {store.QualifiedName} could not be checked ({ex.GetType().Name}). Fix: check the connection string and that the database exists, then run the XAF database update.");
            else
                EditDraftLog.Warning($"[EditDraft] startup: the database check of {store.StoreType.Name} could not run ({ex.GetType().Name})");
        }
        finally
        {
            space?.Dispose();
            scope?.Dispose();
        }
        return problems;
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

    /// <summary>Checks 1-5 for <paramref name="application"/>, then the role warning; throws <see cref="EditDraftConfigurationException"/> on a problem.</summary>
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
        if (problems.Count == 0 && store != null && !DatabaseChecked.ContainsKey(store.StoreType))
        {
            problems.AddRange(DatabaseProblems(services, IsTableCheckOn(services)));
            if (problems.Count == 0 && DatabaseChecked.TryAdd(store.StoreType, true))
            {
                EditDraftLog.Info($"[EditDraft] startup checks passed: store {store.StoreType.Name} at {store.QualifiedName}");
                WarnRoles(services, store);
            }
        }
        if (problems.Count > 0) throw new EditDraftConfigurationException(Message(problems));
    }

    /// <summary>SINGLE-MODEL (owner review): logs the roles that can read the store; never throws.</summary>
    private static void WarnRoles(IServiceProvider services, EditDraftStoreRegistration store)
    {
        try
        {
            using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope();
            using var space = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>().CreateNonSecuredObjectSpace(typeof(PermissionPolicyRoleBase));
            if (!space.CanInstantiate(typeof(PermissionPolicyRoleBase)))
            {
                EditDraftLog.Info("[EditDraft] startup: no XPO PermissionPolicyRole in this application; role check skipped");
                return;
            }
            EditDraftSecurity.WarnRolesThatCanReadStore(space, store.StoreType);
        }
        catch (Exception ex)
        {
            EditDraftLog.Info($"[EditDraft] startup: role check skipped ({ex.GetType().Name})");
        }
    }

    private static string Message(IEnumerable<string> problems) =>
        "Xaf.EditDraft is not configured correctly:" + string.Concat(problems.Select(p => Environment.NewLine + "- " + p));
}
