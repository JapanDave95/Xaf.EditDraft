using System;
using System.Collections.Generic;
using System.Linq;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.Base;

namespace Xaf.EditDraft.Core;

/// <summary>
/// The ONE optional extra access check (0.4.0-preview.1, owner ruling 2026-10-05). SINGLE-MODEL (owner review). "May this
/// login restore a draft onto this record / recreate this record from a draft?" A host registers it in DI
/// (<c>services.AddSingleton&lt;IEditDraftAccessCheck&gt;(...)</c>) only when XAF security does not express its rule. It is
/// asked IN ADDITION to the library's XAF check (<see cref="XafSecurityEditDraftAccessCheck"/>): both must allow
/// (<see cref="EditDraftServices.MayRestore"/>, <see cref="EditDraftServices.MayRecreate"/>), so a host can only narrow
/// access, never widen it. An exception is a refusal. Called on the UI thread/circuit, when an offer, a list row or an open
/// is built and again immediately before anything is applied or shown.
/// </summary>
public interface IEditDraftAccessCheck
{
    /// <summary>A SAVED record: may the login restore a draft onto <paramref name="record"/> (of <paramref name="policy"/>'s exact type)?</summary>
    bool MayRestore(XafApplication application, EditDraftTypePolicy policy, object record);

    /// <summary>
    /// A NEVER-SAVED record: may the login create <paramref name="record"/> — the object rebuilt from the draft, filled with
    /// its values, uncommitted in its own object space? Refused = the object is discarded unsaved.
    /// </summary>
    bool MayRecreate(XafApplication application, EditDraftTypePolicy policy, object record);
}

public static partial class EditDraftServices
{
    /// <summary>The host's extra access check, or null when none is registered (XAF security alone decides).</summary>
    public static IEditDraftAccessCheck AccessCheck(IServiceProvider services) =>
        services?.GetService(typeof(IEditDraftAccessCheck)) as IEditDraftAccessCheck;

    /// <summary>
    /// May the login restore a draft onto the saved <paramref name="record"/>: the XAF check
    /// (<see cref="XafSecurityEditDraftAccessCheck.MayRestore"/>) AND the host's <see cref="IEditDraftAccessCheck"/> when one
    /// is registered. A missing argument or an exception is a refusal. SINGLE-MODEL (owner review).
    /// </summary>
    public static bool MayRestore(XafApplication application, EditDraftTypePolicy policy, object record) =>
        Decide("restore", application, policy, record, (c, a, p, r) => c.MayRestore(a, p, r));

    /// <summary>
    /// May the login create the rebuilt, uncommitted <paramref name="record"/>: the XAF check
    /// (<see cref="XafSecurityEditDraftAccessCheck.MayRecreate"/>) AND the host's <see cref="IEditDraftAccessCheck"/> when one
    /// is registered. A missing argument or an exception is a refusal. SINGLE-MODEL (owner review).
    /// </summary>
    public static bool MayRecreate(XafApplication application, EditDraftTypePolicy policy, object record) =>
        Decide("recreate", application, policy, record, (c, a, p, r) => c.MayRecreate(a, p, r));

    private static bool Decide(string what, XafApplication application, EditDraftTypePolicy policy, object record,
        Func<IEditDraftAccessCheck, XafApplication, EditDraftTypePolicy, object, bool> ask)
    {
        if (application == null || policy == null || record == null) return false;
        try
        {
            if (!ask(XafSecurityEditDraftAccessCheck.Instance, application, policy, record))
            {
                EditDraftLog.Info($"[EditDraft] {what} refused for {policy.TypeName}: XAF security");
                return false;
            }
            var host = AccessCheck(application.ServiceProvider);
            if (host != null && !ReferenceEquals(host, XafSecurityEditDraftAccessCheck.Instance) && !ask(host, application, policy, record))
            {
                EditDraftLog.Info($"[EditDraft] {what} refused for {policy.TypeName}: the host's access check");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            EditDraftLog.Warning($"[EditDraft] {what} access check failed for {policy.TypeName} ({ex.GetType().Name}); treated as refused");
            return false;
        }
    }
}

/// <summary>
/// The library's XAF check, always asked first (0.4.0-preview.1). SINGLE-MODEL (owner review). XAF 26.1.4 APIs only:
/// <see cref="IRequestSecurity.IsGranted(IPermissionRequest)"/> with a <see cref="PermissionRequest"/> on the object, from the
/// application's own security (<see cref="XafApplication.Security"/>).
///
/// SAVED record (<see cref="MayRestore"/>): the record is loaded again by its key through
/// <see cref="XafApplication.CreateObjectSpace(Type)"/> — the application's SECURED object space when it uses XAF security,
/// where a record the login may not read is not found — and Write must be granted on the loaded object (role object
/// criteria are evaluated on its stored values).
///
/// NEVER-SAVED record (<see cref="MayRecreate"/>): the rebuilt object must still be new in its own object space, and
/// Create, then Write, then Read must be granted on it with its own values — the check XAF itself makes before it saves a
/// new object (XPO integrated security, SecurityRule2.ValidateObjectOnSave / IsGrantedCore: a server permission request on
/// the object with an expression evaluator). Create alone is not enough: XAF does not evaluate object criteria for Create
/// (PermissionRequestProcessor.IsGrantedInSameRole; role object permissions have no Create state). And the public
/// PermissionRequest path evaluates a NEW object at type level only (PermissionRequestProcessorWrapper.IsGranted drops a new
/// target object), so the evaluation on the values is done by the Blazor package (registered by AddEditDraftBlazor; it
/// references XAF's security assembly, which Core does not). Without it, or with a security that is not XAF's
/// integrated SecurityStrategy, the recreate is refused (fail closed).
///
/// No request security (<see cref="XafApplication.Security"/> is null or not <see cref="IRequestSecurity"/>): allowed, as
/// XAF itself allows creating and editing then (DataManipulationRight.CanCreate / CanEdit ask security only when it is
/// <see cref="IRequestSecurity"/>). A missing argument, a record of another type, or an exception is a refusal.
/// </summary>
public sealed class XafSecurityEditDraftAccessCheck : IEditDraftAccessCheck
{
    public static readonly XafSecurityEditDraftAccessCheck Instance = new();

    /// <summary>The operations XAF requires on a new object before it saves it, in its order.</summary>
    internal static readonly string[] NewObjectOperations = { SecurityOperations.Create, SecurityOperations.Write, SecurityOperations.Read };

    private static int _noEvaluatorLogged;

    public bool MayRestore(XafApplication application, EditDraftTypePolicy policy, object record)
    {
        if (application == null || policy == null || record == null || record.GetType() != policy.Type) return false;
        if (application.Security is not IRequestSecurity security) return true;
        using var objectSpace = application.CreateObjectSpace(policy.Type);
        var key = objectSpace.GetKeyValue(record);
        var loaded = key == null ? null : objectSpace.GetObjectByKey(policy.Type, key);
        if (loaded == null) return false;   // not readable through the application's object space, or never saved
        return security.IsGranted(new PermissionRequest(objectSpace, policy.Type, SecurityOperations.Write, loaded));
    }

    public bool MayRecreate(XafApplication application, EditDraftTypePolicy policy, object record)
    {
        if (application == null || policy == null || record == null || record.GetType() != policy.Type) return false;
        if (application.Security is not IRequestSecurity) return true;
        var objectSpace = BaseObjectSpace.FindObjectSpaceByObject(record);
        if (objectSpace == null || objectSpace.IsDisposed || !objectSpace.IsNewObject(record)) return false;
        if (application.ServiceProvider?.GetService(typeof(IEditDraftNewObjectPermissions)) is not IEditDraftNewObjectPermissions evaluator)
        {
            if (System.Threading.Interlocked.Exchange(ref _noEvaluatorLogged, 1) == 0)
                EditDraftLog.Warning("[EditDraft] recreate refused: no evaluator of a new object's permissions is registered (services.AddEditDraftBlazor())");
            return false;
        }
        return evaluator.IsGranted(application.Security, objectSpace, policy.Type, record, NewObjectOperations);
    }
}

/// <summary>
/// Evaluates XAF permissions on a NEW (uncommitted) object with the object's own values, as XAF does when it saves a new
/// object. Implemented by the Blazor package (it references XAF's security assembly) and registered by AddEditDraftBlazor;
/// not a host seam. SINGLE-MODEL (owner review).
/// </summary>
internal interface IEditDraftNewObjectPermissions
{
    /// <summary>True only when every operation is granted on <paramref name="record"/>; false when it is not or cannot be decided.</summary>
    bool IsGranted(ISecurityStrategyBase security, IObjectSpace objectSpace, Type type, object record, IReadOnlyList<string> operations);
}

/// <summary>
/// SINGLE-MODEL (owner review, D14) — NEW records, design docs/edit-draft-new-records-design-2026-10-02.md §5 S1/S2: may a
/// record of this type be CREATED from a draft? S2 — no creation the UI itself does not offer: the policy allows new records,
/// at least one of the policy's ListViews has model AllowNew and the approved DetailView has model AllowEdit (read from the
/// application model at the click). S1 — the login may create the type: DataManipulationRight.HasPermissionTo(type, Create)
/// on a destination object space, asked only when the application's security is IRequestSecurity — the call KB fix-531 uses.
/// Any failure = not permitted.
/// </summary>
public static class EditDraftCreateAccess
{
    /// <summary>The pure decision: every condition must hold.</summary>
    public static bool Decide(bool allowNewRecords, bool anyListAllowsNew, bool detailAllowsEdit, bool createGranted) =>
        allowNewRecords && anyListAllowsNew && detailAllowsEdit && createGranted;

    public static bool MayCreate(XafApplication application, EditDraftTypePolicy policy)
    {
        if (application == null || !EditDraftTypePolicy.IsGeneric(policy)) return false;
        try
        {
            var views = application.Model?.Views;
            var anyListAllowsNew = views != null && policy.ListViewIds.Any(id => views[id] is DevExpress.ExpressApp.Model.IModelListView l && l.AllowNew);
            var detailId = policy.ApprovedViewIds?.FirstOrDefault();
            var detailAllowsEdit = views != null && detailId != null && views[detailId] is DevExpress.ExpressApp.Model.IModelDetailView d && d.AllowEdit;
            var granted = true;
            if (application.Security is IRequestSecurity)
            {
                using var objectSpace = application.CreateObjectSpace(policy.Type);
                granted = DataManipulationRight.HasPermissionTo(policy.Type, null, null, objectSpace, SecurityOperations.Create);
            }
            var ok = Decide(policy.AllowNewRecords, anyListAllowsNew, detailAllowsEdit, granted);
            if (!ok) EditDraftLog.Info($"[EditDraft] create refused for {policy.TypeName}: allowNewRecords={policy.AllowNewRecords} listAllowNew={anyListAllowsNew} detailAllowEdit={detailAllowsEdit} createGranted={granted}");
            return ok;
        }
        catch (Exception ex)
        {
            EditDraftLog.Warning($"[EditDraft] create permission check failed for {policy.TypeName} ({ex.GetType().Name}); treated as not permitted");
            return false;
        }
    }
}

/// <summary>
/// SINGLE-MODEL (owner review) — design §3 S3 check 3: may the login WRITE each drafted member? Plain XAF
/// member-level permission; a path with a companion prefix is checked on the companion object. Any failure
/// = not writable. A path the login may not write is shown as 戻せません, never silently skipped.
/// </summary>
public static class EditDraftMemberAccess
{
    public static bool CanWrite(IObjectSpace objectSpace, object record, string path)
    {
        if (objectSpace == null || record == null || string.IsNullOrEmpty(path)) return false;
        try
        {
            var owner = EditDraftMembers.OwnerOf(record, path, out var member);
            if (owner == null) return false;
            return SecuritySystem.IsGranted(new PermissionRequest(objectSpace, owner.GetType(), SecurityOperations.Write, owner, member));
        }
        catch (Exception ex)
        {
            EditDraftLog.Warning($"[EditDraft] write permission check failed for {path} ({ex.GetType().Name}); treated as not writable");
            return false;
        }
    }

    /// <summary>The paths of <paramref name="paths"/> this user may NOT write (shown as 戻せません, never silently skipped).</summary>
    public static HashSet<string> NotWritable(IObjectSpace objectSpace, object record, IEnumerable<string> paths)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in paths ?? Enumerable.Empty<string>())
            if (!CanWrite(objectSpace, record, p)) result.Add(p);
        return result;
    }
}
