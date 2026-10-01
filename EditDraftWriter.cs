using System;
using System.Collections.Generic;
using System.Linq;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Xpo;
using DevExpress.Xpo;
using Microsoft.Extensions.DependencyInjection;

namespace Xaf.EditDraft.Core;

/// <summary>Header of a new 入力控 draft, as PLAIN DATA (it crosses the circuit/worker boundary).</summary>
public sealed class EditDraftSeed
{
    public Guid DraftKey { get; set; }
    public Guid EditorInstanceId { get; set; }
    public Guid OwnerUserOid { get; set; }
    public bool LoginIsStaffMember { get; set; }
    public string ObjectType { get; set; }
    public Guid TargetOid { get; set; }
    public Guid SubSectionOid { get; set; }
    public string ContextText { get; set; }
    public string ViewId { get; set; }
}

/// <summary>What a failed supersede found when it looked (for the capture's "row gone" recovery).</summary>
public enum EditDraftRowState { ReadFailed, Present, Gone, Moved, Discarded }

/// <summary>
/// The writer's operations, typed on the store base (the engine does not know the host's store class).
/// Every read, update and delete takes the owner and filters on it in the statement (library design §4.11).
/// </summary>
public interface IEditDraftWriter
{
    IObjectSpace CreateReadSpace(out IServiceScope scope);
    Guid Create(EditDraftSeed seed, string payloadJson, int entryCount, DateTime now);
    bool TrySupersede(Guid oid, int expectedRevision, Guid ownerOid, string payloadJson, int entryCount, Guid subSectionOid, string contextText, DateTime now);
    EditDraftRowState ReadRowState(Guid oid, int expectedRevision, Guid ownerOid, DateTime now);
    int TryClaim(Guid oid, int expectedRevision, Guid ownerOid, Guid editorInstanceId, DateTime now);
    int DeleteOwn(Guid oid, Guid ownerOid, Guid editorInstanceId);
    bool TableExists();
    bool TrySoftDiscard(Guid oid, Guid ownerOid, DateTime now);
    EditDraftStoreBase ReadOwn(IObjectSpace os, Guid oid, Guid ownerOid);
    EditDraftStoreBase FindOfferable(IObjectSpace os, Guid ownerOid, string objectType, Guid targetOid, Guid excludeEditorInstanceId, DateTime now);
    IReadOnlyList<EditDraftStoreBase> ListOwn(IObjectSpace os, Guid ownerOid, string objectType, bool includeDiscarded, DateTime now, out bool readFailed);
    List<Guid> ListOwnTargets(IObjectSpace os, Guid ownerOid, string objectType, DateTime now, out bool readFailed);
}

/// <summary>
/// The writer of the host's registered store (<see cref="EditDraftStoreRegistration"/> in the service provider):
/// every call goes to <see cref="EditDraftWriter{TStore}"/> for that store class. With no store registered every
/// operation fails closed — nothing is written or read, a read is "failed", the table is "absent".
/// </summary>
public sealed class EditDraftWriter : IEditDraftWriter
{
    private readonly IEditDraftWriter _store;

    public EditDraftWriter(IServiceProvider serviceProvider)
        : this(serviceProvider, (serviceProvider?.GetService(typeof(EditDraftStoreRegistration)) as EditDraftStoreRegistration)?.StoreType) { }

    /// <summary>The writer of an explicit store class (null = none: fail closed).</summary>
    public EditDraftWriter(IServiceProvider serviceProvider, Type storeType)
    {
        if (storeType == null) { _store = NoStore.Instance; return; }
        if (!typeof(EditDraftStoreBase).IsAssignableFrom(storeType) || storeType.IsAbstract)
            throw new ArgumentException($"{storeType.FullName} is not a concrete subclass of {nameof(EditDraftStoreBase)}.", nameof(storeType));
        _store = (IEditDraftWriter)Activator.CreateInstance(typeof(EditDraftWriter<>).MakeGenericType(storeType), serviceProvider);
    }

    public IObjectSpace CreateReadSpace(out IServiceScope scope) => _store.CreateReadSpace(out scope);
    public Guid Create(EditDraftSeed seed, string payloadJson, int entryCount, DateTime now) => _store.Create(seed, payloadJson, entryCount, now);
    public bool TrySupersede(Guid oid, int expectedRevision, Guid ownerOid, string payloadJson, int entryCount, Guid subSectionOid, string contextText, DateTime now) =>
        _store.TrySupersede(oid, expectedRevision, ownerOid, payloadJson, entryCount, subSectionOid, contextText, now);
    public EditDraftRowState ReadRowState(Guid oid, int expectedRevision, Guid ownerOid, DateTime now) => _store.ReadRowState(oid, expectedRevision, ownerOid, now);
    public int TryClaim(Guid oid, int expectedRevision, Guid ownerOid, Guid editorInstanceId, DateTime now) => _store.TryClaim(oid, expectedRevision, ownerOid, editorInstanceId, now);
    public int DeleteOwn(Guid oid, Guid ownerOid, Guid editorInstanceId) => _store.DeleteOwn(oid, ownerOid, editorInstanceId);
    public bool TableExists() => _store.TableExists();
    public bool TrySoftDiscard(Guid oid, Guid ownerOid, DateTime now) => _store.TrySoftDiscard(oid, ownerOid, now);
    public EditDraftStoreBase ReadOwn(IObjectSpace os, Guid oid, Guid ownerOid) => _store.ReadOwn(os, oid, ownerOid);
    public EditDraftStoreBase FindOfferable(IObjectSpace os, Guid ownerOid, string objectType, Guid targetOid, Guid excludeEditorInstanceId, DateTime now) =>
        _store.FindOfferable(os, ownerOid, objectType, targetOid, excludeEditorInstanceId, now);
    public IReadOnlyList<EditDraftStoreBase> ListOwn(IObjectSpace os, Guid ownerOid, string objectType, bool includeDiscarded, DateTime now, out bool readFailed) =>
        _store.ListOwn(os, ownerOid, objectType, includeDiscarded, now, out readFailed);
    public List<Guid> ListOwnTargets(IObjectSpace os, Guid ownerOid, string objectType, DateTime now, out bool readFailed) =>
        _store.ListOwnTargets(os, ownerOid, objectType, now, out readFailed);

    /// <summary>No store registered: every operation fails closed (logged once per process).</summary>
    private sealed class NoStore : IEditDraftWriter
    {
        public static readonly NoStore Instance = new();
        private static int _logged;

        private static void Refused()
        {
            if (System.Threading.Interlocked.Exchange(ref _logged, 1) == 0)
                EditDraftLog.Warning("[EditDraft] no store class is registered (services.AddEditDraftStore<T>()); nothing is written or read");
        }

        public IObjectSpace CreateReadSpace(out IServiceScope scope) { Refused(); scope = null; return null; }
        public Guid Create(EditDraftSeed seed, string payloadJson, int entryCount, DateTime now) { Refused(); return Guid.Empty; }
        public bool TrySupersede(Guid oid, int expectedRevision, Guid ownerOid, string payloadJson, int entryCount, Guid subSectionOid, string contextText, DateTime now) { Refused(); return false; }
        public EditDraftRowState ReadRowState(Guid oid, int expectedRevision, Guid ownerOid, DateTime now) { Refused(); return EditDraftRowState.ReadFailed; }
        public int TryClaim(Guid oid, int expectedRevision, Guid ownerOid, Guid editorInstanceId, DateTime now) { Refused(); return 0; }
        public int DeleteOwn(Guid oid, Guid ownerOid, Guid editorInstanceId) { Refused(); return -1; }
        public bool TableExists() { Refused(); return false; }
        public bool TrySoftDiscard(Guid oid, Guid ownerOid, DateTime now) { Refused(); return false; }
        public EditDraftStoreBase ReadOwn(IObjectSpace os, Guid oid, Guid ownerOid) => null;
        public EditDraftStoreBase FindOfferable(IObjectSpace os, Guid ownerOid, string objectType, Guid targetOid, Guid excludeEditorInstanceId, DateTime now) => null;
        public IReadOnlyList<EditDraftStoreBase> ListOwn(IObjectSpace os, Guid ownerOid, string objectType, bool includeDiscarded, DateTime now, out bool readFailed)
        { readFailed = true; return new List<EditDraftStoreBase>(); }
        public List<Guid> ListOwnTargets(IObjectSpace os, Guid ownerOid, string objectType, DateTime now, out bool readFailed)
        { readFailed = true; return new List<Guid>(); }
    }
}

/// <summary>
/// Persists 入力控 drafts in the host's store table (CareCrew: dbo.EditDraft, class NursingHome_Chart.Module.BusinessObjects.EditDraft).
/// SINGLE-MODEL (owner review): the ownership predicates live here. Same shape as TenantChartDraftWriter; the owner column is different.
///
/// OWNER-SCOPED IN EVERY STATEMENT: OwnerUserOid = the XAF login (design §3 S1) is part of the WHERE
/// of every read, update and delete, so another login's draft is never loaded, changed or deleted,
/// whatever Oid a caller passes. The rows are denied to every role, so the space is non-secured and
/// this predicate IS the protection.
///
/// Every mutation is a single conditional statement whose affected-row count is the fence (no
/// OptimisticLockField, no load-then-save, @pN names, no ExplicitBeginTransaction — KB fix-505).
/// Expiry is set once at creation and never written again.
///
/// Library (milestone M1): generic over the host's store class; the statements address its XPO table
/// (unchanged text for CareCrew: the table is EditDraft). Supported contract v1: XPO, SQL Server.
/// </summary>
public sealed class EditDraftWriter<TStore> : IEditDraftWriter where TStore : EditDraftStoreBase
{
    private static readonly string Table = EditDraftStoreRegistration.TableNameOf(typeof(TStore));
    private static readonly EditDraftTableCache TableCache = new();
    private readonly IServiceProvider _serviceProvider;

    public EditDraftWriter(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    private IServiceScope CreateScope() => _serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();

    private static Session SessionOf(IObjectSpace objectSpace)
    {
        var session = ((XPObjectSpace)objectSpace).Session;
        session.LockingOption = LockingOption.None;
        return session;
    }

    /// <summary>A non-secured space for reading drafts (disposed by the caller with the scope).</summary>
    public IObjectSpace CreateReadSpace(out IServiceScope scope)
    {
        scope = CreateScope();
        var os = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>()
            .CreateNonSecuredObjectSpace(typeof(TStore));
        SessionOf(os);
        return os;
    }

    /// <summary>Creates a draft at revision 1; Guid.Empty on failure. Never called without an owner.</summary>
    public Guid Create(EditDraftSeed seed, string payloadJson, int entryCount, DateTime now)
    {
        if (seed == null || seed.OwnerUserOid == Guid.Empty || seed.TargetOid == Guid.Empty) return Guid.Empty;
        try
        {
            using var scope = CreateScope();
            using var os = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>()
                .CreateNonSecuredObjectSpace(typeof(TStore));
            SessionOf(os);

            var d = os.CreateObject<TStore>();
            d.DraftKey = seed.DraftKey != Guid.Empty ? seed.DraftKey : Guid.NewGuid();   // one key per ROW
            d.EditorInstanceId = seed.EditorInstanceId;
            d.OwnerUserOid = seed.OwnerUserOid;
            d.LoginIsStaffMember = seed.LoginIsStaffMember;
            d.ObjectType = seed.ObjectType;
            d.TargetOid = seed.TargetOid;
            d.SubSectionOid = seed.SubSectionOid;
            d.ContextText = seed.ContextText;
            d.ViewId = seed.ViewId;
            d.PayloadSchemaVersion = EditDraftStoreBase.CurrentPayloadSchemaVersion;
            d.Payload = payloadJson;
            d.EntryCount = entryCount;
            d.Revision = 1;
            d.FirstCapturedOn = now;
            d.LastCapturedOn = now;
            d.ExpiresOn = EditDraftStoreBase.CalculateExpiry(now);   // set ONCE
            try { d.OriginHost = Environment.MachineName; } catch { }

            os.CommitChanges();
            return d.Oid;
        }
        catch (Exception ex)
        {
            EditDraftLog.Error($"[EditDraft] create failed: {ex.GetType().Name}");   // never the message: it can quote values
            return Guid.Empty;
        }
    }

    /// <summary>Replaces the payload of this owner's live draft at the expected revision. ExpiresOn is NOT touched.</summary>
    public bool TrySupersede(Guid oid, int expectedRevision, Guid ownerOid, string payloadJson, int entryCount,
                             Guid subSectionOid, string contextText, DateTime now)
    {
        if (ownerOid == Guid.Empty) return false;
        var sql =
            $"UPDATE [{Table}] SET [Payload] = @p0, [EntryCount] = @p1, [Revision] = [Revision] + 1, [LastCapturedOn] = @p2, " +
            $"[SubSectionOid] = @p3, [ContextText] = @p4, [LastError] = NULL " +
            $"WHERE [Oid] = @p5 AND [Revision] = @p6 AND [OwnerUserOid] = @p7 AND [DeletedOn] IS NULL AND [ExpiresOn] > @p2";
        return Execute(sql, new object[] { payloadJson, entryCount, now, subSectionOid, contextText ?? string.Empty,
                                           oid, expectedRevision, ownerOid }, "supersede") == 1;
    }

    /// <summary>
    /// After a supersede moved nothing: what the row looks like now. Read with the owner predicate,
    /// so another login's row reads as Gone, never as theirs. A failed read is ReadFailed, never Gone (KB fix-507).
    /// </summary>
    public EditDraftRowState ReadRowState(Guid oid, int expectedRevision, Guid ownerOid, DateTime now)
    {
        try
        {
            using var os = CreateReadSpace(out var scope);
            using (scope)
            {
                var d = ReadOwn(os, oid, ownerOid);
                if (d == null || d.HasExpired(now)) return EditDraftRowState.Gone;
                if (d.DeletedOn != null) return EditDraftRowState.Discarded;
                return d.Revision != expectedRevision ? EditDraftRowState.Moved : EditDraftRowState.Present;
            }
        }
        catch (Exception ex)
        {
            EditDraftLog.Error($"[EditDraft] row state read failed: {ex.GetType().Name}");
            return EditDraftRowState.ReadFailed;
        }
    }

    /// <summary>
    /// RESTORE CLAIM: hands this owner's draft to the editing context that is restoring it, fenced on
    /// the revision the restorer saw. One statement, so of two screens restoring the same draft exactly
    /// one wins; the loser must not apply anything. A draft claimed from the list's search is un-discarded.
    /// Returns the new revision, or 0 when the claim lost (or failed).
    /// </summary>
    public int TryClaim(Guid oid, int expectedRevision, Guid ownerOid, Guid editorInstanceId, DateTime now)
    {
        if (oid == Guid.Empty || ownerOid == Guid.Empty) return 0;
        var n = Execute(
            $"UPDATE [{Table}] SET [EditorInstanceId] = @p0, [Revision] = [Revision] + 1, [DeletedOn] = NULL, [LastCapturedOn] = @p1 " +
            $"WHERE [Oid] = @p2 AND [Revision] = @p3 AND [OwnerUserOid] = @p4 AND [ExpiresOn] > @p1",
            new object[] { editorInstanceId, now, oid, expectedRevision, ownerOid }, "claim");
        return n == 1 ? expectedRevision + 1 : 0;
    }

    /// <summary>
    /// Hard delete of this owner's draft — the record was SAVED (the whole draft, unticked fields
    /// included). Scoped to the EDITING CONTEXT that holds the row: once another screen has claimed it,
    /// a save on the former screen no longer deletes it.
    /// </summary>
    public int DeleteOwn(Guid oid, Guid ownerOid, Guid editorInstanceId)
    {
        if (oid == Guid.Empty || ownerOid == Guid.Empty) return -1;
        return Execute($"DELETE FROM [{Table}] WHERE [Oid] = @p0 AND [OwnerUserOid] = @p1 AND [EditorInstanceId] = @p2",
            new object[] { oid, ownerOid, editorInstanceId }, "delete on save");
    }

    /// <summary>
    /// Does the table exist? Restore and the list stay available while capture is switched OFF (design
    /// §6), but only where the table exists — a database without it shows nothing. Cached for five
    /// minutes PER DATABASE (library design §4.12 rule 2; was one process-wide answer); a failed probe
    /// counts as absent. A failure to open the space is "absent" and is not cached.
    /// </summary>
    public bool TableExists()
    {
        try
        {
            using var scope = CreateScope();
            using var os = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>()
                .CreateNonSecuredObjectSpace(typeof(TStore));
            var session = SessionOf(os);
            return TableCache.Get(EditDraftTableCache.DatabaseKeyOf(session), EditDraftServices.Clock(_serviceProvider).GetUtcNow().UtcDateTime, () =>
            {
                try
                {
                    var r = session.ExecuteScalar($"SELECT CASE WHEN OBJECT_ID(N'dbo.{Table}') IS NULL THEN 0 ELSE 1 END");
                    return Convert.ToInt32(r, System.Globalization.CultureInfo.InvariantCulture) == 1;
                }
                catch (Exception ex)
                {
                    EditDraftLog.Warning($"[EditDraft] table probe failed ({ex.GetType().Name}); treated as absent");
                    return false;
                }
            });
        }
        catch (Exception ex)
        {
            EditDraftLog.Warning($"[EditDraft] table probe failed ({ex.GetType().Name}); treated as absent");
            return false;
        }
    }

    /// <summary>破棄: soft discard. Hidden from the offer; the list's search still finds it until expiry.</summary>
    public bool TrySoftDiscard(Guid oid, Guid ownerOid, DateTime now)
    {
        if (oid == Guid.Empty || ownerOid == Guid.Empty) return false;
        return Execute($"UPDATE [{Table}] SET [DeletedOn] = @p2 WHERE [Oid] = @p0 AND [OwnerUserOid] = @p1 AND [DeletedOn] IS NULL",
            new object[] { oid, ownerOid, now }, "discard") == 1;
    }

    /// <summary>This owner's draft by Oid, or null. The owner check is the query, not a filter afterwards.</summary>
    public EditDraftStoreBase ReadOwn(IObjectSpace os, Guid oid, Guid ownerOid)
    {
        if (os == null || oid == Guid.Empty || ownerOid == Guid.Empty) return null;
        return os.GetObjectsQuery<TStore>()
            .FirstOrDefault(d => d.Oid == oid && d.OwnerUserOid == ownerOid);
    }

    /// <summary>
    /// The newest live draft of this owner for an EXISTING record of one registered type, not discarded,
    /// not expired, and not the asking screen's own editing context. Null when there is none (or no owner).
    /// </summary>
    public EditDraftStoreBase FindOfferable(IObjectSpace os, Guid ownerOid, string objectType, Guid targetOid, Guid excludeEditorInstanceId, DateTime now)
    {
        if (os == null || ownerOid == Guid.Empty || targetOid == Guid.Empty || string.IsNullOrEmpty(objectType)) return null;
        try
        {
            return os.GetObjectsQuery<TStore>()
                .Where(d => d.OwnerUserOid == ownerOid)
                .Where(d => d.ObjectType == objectType && d.TargetOid == targetOid && d.DeletedOn == null && d.ExpiresOn > now)
                .Where(d => d.EditorInstanceId != excludeEditorInstanceId)
                .OrderByDescending(d => d.LastCapturedOn)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            EditDraftLog.Error($"[EditDraft] offer query failed: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>
    /// The 「入力控」 list: this owner's unexpired drafts (of one type when <paramref name="objectType"/>
    /// is given, D14); discarded ones only when searching. <paramref name="readFailed"/> tells a failed
    /// read apart from "no drafts". The owner predicate is in the query.
    /// </summary>
    public IReadOnlyList<EditDraftStoreBase> ListOwn(IObjectSpace os, Guid ownerOid, string objectType, bool includeDiscarded, DateTime now, out bool readFailed)
    {
        readFailed = false;
        if (os == null || ownerOid == Guid.Empty) return new List<TStore>();
        try
        {
            var q = os.GetObjectsQuery<TStore>()
                .Where(d => d.OwnerUserOid == ownerOid && d.ExpiresOn > now);
            if (!string.IsNullOrEmpty(objectType)) q = q.Where(d => d.ObjectType == objectType);
            if (!includeDiscarded) q = q.Where(d => d.DeletedOn == null);
            return q.OrderByDescending(d => d.LastCapturedOn).ToList();
        }
        catch (Exception ex)
        {
            EditDraftLog.Error($"[EditDraft] list query failed: {ex.GetType().Name}");
            readFailed = true;
            return new List<TStore>();
        }
    }

    /// <summary>
    /// Wave 1b row badges (owner D17/B6, design §7 S1-S2): the TargetOids of this owner's live (not discarded,
    /// not expired) drafts of ONE type — a projection, so no payload is read. The owner predicate is in the
    /// query; another login's drafts never reach a grid. <paramref name="readFailed"/> tells a failed read apart
    /// from "no drafts".
    /// </summary>
    public List<Guid> ListOwnTargets(IObjectSpace os, Guid ownerOid, string objectType, DateTime now, out bool readFailed)
    {
        readFailed = false;
        if (os == null || ownerOid == Guid.Empty || string.IsNullOrEmpty(objectType)) return new List<Guid>();
        try
        {
            return os.GetObjectsQuery<TStore>()
                .Where(d => d.OwnerUserOid == ownerOid && d.ExpiresOn > now && d.DeletedOn == null && d.ObjectType == objectType)
                .Select(d => d.TargetOid)
                .ToList();
        }
        catch (Exception ex)
        {
            EditDraftLog.Error($"[EditDraft] badge query failed: {ex.GetType().Name}");
            readFailed = true;
            return new List<Guid>();
        }
    }

    private int Execute(string sql, object[] values, string what)
    {
        try
        {
            using var scope = CreateScope();
            using var os = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>()
                .CreateNonSecuredObjectSpace(typeof(TStore));
            var names = Enumerable.Range(0, values.Length).Select(i => "@p" + i).ToArray();
            return SessionOf(os).ExecuteNonQuery(sql, names, values);
        }
        catch (Exception ex)
        {
            // A failed draft write never breaks editing; it is logged (type only — SQL messages can quote data).
            EditDraftLog.Error($"[EditDraft] {what} failed: {ex.GetType().Name}");
            return -1;
        }
    }
}
