using System;
using System.Globalization;
using System.Numerics;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Xpo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Xaf.EditDraft.Core;

/// <summary>
/// Retention sweep (gaps G4/G5, 2026-10-04): physically deletes the drafts that have expired. A draft expires
/// <see cref="EditDraftStoreBase.RetentionDays"/> days after its first capture and is hidden from that instant
/// (<see cref="EditDraftStoreBase.HasExpired"/>); without a sweep its row stays in the table.
///
/// OFF unless the host turns it on, in one of two ways:
/// - the hosted service of the Blazor package (services.AddEditDraftRetention()), which sweeps every
///   <see cref="IntervalMinutesKey"/> minutes (at most <see cref="MaxIntervalMinutes"/>; 0 or below turns it off) while
///   <see cref="EnabledKey"/> reads as the boolean true; or
/// - a call to <see cref="Sweep(IServiceProvider)"/> from the host's own scheduler.
///
/// Owner-agnostic by design: the sweep deletes by expiry alone, across every owner, because no login asks for it. It is not
/// part of the owner-fenced writer (EditDraftWriter keeps exactly its five owner-named statements).
///
/// Clock (G5): ExpiresOn is written in the APPLICATION SERVER's local time (EditDraftClock.Now over the host's
/// TimeProvider), so the cutoff is that clock's "now", passed as a parameter, never the database server's own clock.
/// The comparison is ExpiresOn &lt;= cutoff, the same rule that hides a draft. Rows are deleted in batches of
/// <see cref="BatchSize"/>; each batch is one statement. SQL Server only.
/// </summary>
public static class EditDraftRetention
{
    /// <summary>Hosted-service switch (named in the default section; a custom section moves it like the other switches).</summary>
    public const string EnabledKey = "EditDraftCapture:Retention:Enabled";

    /// <summary>
    /// Minutes between hosted-service sweeps (<see cref="IntervalMinutes"/>): missing or not a whole number =
    /// <see cref="DefaultIntervalMinutes"/>; above <see cref="MaxIntervalMinutes"/> = <see cref="MaxIntervalMinutes"/>; 0 or
    /// below turns the hosted sweep off (the service logs a warning and stops).
    /// </summary>
    public const string IntervalMinutesKey = "EditDraftCapture:Retention:IntervalMinutes";

    public const int DefaultIntervalMinutes = 60;

    /// <summary>
    /// The longest interval, one day (Codex C3, 2026-10-04). A larger configured value is clamped to it, so the hosted
    /// service's delay stays far below Task.Delay's limit (about 49.7 days) and cannot stop the host.
    /// </summary>
    public const int MaxIntervalMinutes = 1440;

    public const int BatchSize = 1000;

    /// <summary>The hidden rule and the delete rule: expired at and after the exact expiry instant.</summary>
    public static bool IsExpired(DateTime expiresOn, DateTime cutoff) => expiresOn <= cutoff;

    /// <summary>True only when <see cref="EnabledKey"/> reads as the boolean true (EditDraftSwitch.IsOn). Capture being on does not turn it on.</summary>
    public static bool IsEnabled(IServiceProvider services)
    {
        try
        {
            var configuration = services?.GetService<IConfiguration>();
            return configuration != null && EditDraftSwitch.IsOn(configuration[EditDraftSwitch.In(services, EnabledKey)]);
        }
        catch { return false; }
    }

    /// <summary>
    /// The hosted-service interval in minutes, from 1 to <see cref="MaxIntervalMinutes"/>, or 0 when the configured value is 0
    /// or below (the hosted sweep is off). Missing or not a whole number = <see cref="DefaultIntervalMinutes"/>; a whole number
    /// above the maximum, however large, = <see cref="MaxIntervalMinutes"/>.
    /// </summary>
    public static int IntervalMinutes(IServiceProvider services)
    {
        try
        {
            var raw = services?.GetService<IConfiguration>()?[EditDraftSwitch.In(services, IntervalMinutesKey)];
            if (!BigInteger.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)) return DefaultIntervalMinutes;
            if (minutes <= 0) return 0;
            return minutes >= MaxIntervalMinutes ? MaxIntervalMinutes : (int)minutes;
        }
        catch { return DefaultIntervalMinutes; }
    }

    /// <summary>
    /// Sweeps the registered store (AddEditDraftStore) with the host clock's "now" as the cutoff, through a non-secured object
    /// space. Returns the number of rows deleted, or -1 when nothing could be swept (no store, not SQL Server, a failed
    /// statement); a failure is logged, with the rows already deleted by earlier batches.
    /// </summary>
    public static int Sweep(IServiceProvider services)
    {
        var store = services?.GetService(typeof(EditDraftStoreRegistration)) as EditDraftStoreRegistration;
        if (store == null)
        {
            EditDraftLog.Warning("[EditDraft] retention sweep: no store class is registered (services.AddEditDraftStore<T>()); nothing deleted");
            return -1;
        }
        try
        {
            using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope();
            using var space = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>().CreateNonSecuredObjectSpace(store.StoreType);
            return Sweep(space, store, EditDraftClock.Now(EditDraftServices.Clock(services)));
        }
        catch (Exception ex)
        {
            EditDraftLog.Error($"[EditDraft] retention sweep failed: {ex.GetType().Name}");
            return -1;
        }
    }

    /// <summary>
    /// Deletes the rows of <paramref name="store"/> whose ExpiresOn is at or before <paramref name="cutoff"/> (a local time of
    /// the application server's clock), through <paramref name="objectSpace"/> (an XPO object space; non-secured, since the
    /// store is denied to every role). Returns the number deleted, or -1 on failure (logged).
    /// </summary>
    public static int Sweep(IObjectSpace objectSpace, EditDraftStoreRegistration store, DateTime cutoff)
    {
        if (objectSpace == null) throw new ArgumentNullException(nameof(objectSpace));
        if (store == null) throw new ArgumentNullException(nameof(store));
        var session = (objectSpace as XPObjectSpace)?.Session;
        if (session == null)
        {
            EditDraftLog.Error("[EditDraft] retention sweep: the object space is not an XPO object space; nothing deleted");
            return -1;
        }
        if (EditDraftSqlServer.Classify(session, out var provider) == EditDraftDatabaseKind.NotSqlServer)
        {
            EditDraftLog.Error($"[EditDraft] retention sweep: the store's data store is {provider}, not SQL Server; nothing deleted");
            return -1;
        }
        var deleted = 0;
        try
        {
            var sql = DeleteStatement(store.QualifiedName);
            int n;
            do
            {
                n = session.ExecuteNonQuery(sql, new[] { "@p0", "@p1" }, new object[] { cutoff, BatchSize });
                if (n > 0) deleted += n;
            } while (n == BatchSize);
            EditDraftLog.Info($"[EditDraft] retention sweep: {deleted} expired draft row(s) deleted from {store.QualifiedName} (cutoff {cutoff.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}, application clock)");
            return deleted;
        }
        catch (Exception ex)
        {
            // Type only: an SQL message can quote data. The rows of earlier batches are gone; the count says how many.
            EditDraftLog.Error($"[EditDraft] retention sweep failed after {deleted} row(s): {ex.GetType().Name}");
            return -1;
        }
    }

    /// <summary>One batch: no owner predicate (owner-agnostic by design), the cutoff and the batch size as parameters.</summary>
    internal static string DeleteStatement(string qualifiedTable) =>
        $"DELETE TOP (@p1) FROM {qualifiedTable} WHERE [ExpiresOn] <= @p0";
}
