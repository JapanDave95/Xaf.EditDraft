using System;
using System.Collections.Generic;
using System.Linq;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.Base;

namespace Xaf.EditDraft.Core;

/// <summary>
/// Record-access seam — design §4.11 SEC-2. SINGLE-MODEL (owner review). May the login see this record's draft?
/// Asked when an offer or a list row is built and again immediately before anything is applied.
/// </summary>
public interface IEditDraftRecordAccess
{
    bool IsRecordVisible(XafApplication application, EditDraftTypePolicy policy, object record);

    /// <summary>
    /// NEW records (design docs/edit-draft-new-records-design-2026-10-02.md §5 S5 i): may the login see records of this 事業所
    /// (a SubSection Oid) — asked before a record is recreated from a draft whose stored SubSectionOid is not empty, when no
    /// record exists yet to ask <see cref="IsRecordVisible"/> about. A host that does not implement it refuses (fail closed).
    /// </summary>
    bool IsSubSectionVisible(XafApplication application, EditDraftTypePolicy policy, Guid subSectionOid) => false;
}

public static partial class EditDraftServices
{
    /// <summary>The host's record-access seam; the library default (XAF security only) otherwise. SINGLE-MODEL (design §4.11 SEC-2).</summary>
    public static IEditDraftRecordAccess RecordAccess(IServiceProvider services) =>
        (services?.GetService(typeof(IEditDraftRecordAccess)) as IEditDraftRecordAccess) ?? XafSecurityEditDraftRecordAccess.Instance;
}

/// <summary>
/// Library default (SEC-2): no rule beyond XAF security. Safe only because every caller first loads the
/// record through the application's SECURED object space (Application.CreateObjectSpace, then GetObjectByKey;
/// null = not readable) or runs inside the record's own DetailView — a record XAF security hides never
/// reaches this check. A missing argument is "not visible".
/// </summary>
public sealed class XafSecurityEditDraftRecordAccess : IEditDraftRecordAccess
{
    public static readonly XafSecurityEditDraftRecordAccess Instance = new();

    public bool IsRecordVisible(XafApplication application, EditDraftTypePolicy policy, object record) =>
        application != null && policy != null && record != null;

    /// <summary>NEW records: no 事業所 rule in the library default; the filled record is still asked <see cref="IsRecordVisible"/> before it is shown.</summary>
    public bool IsSubSectionVisible(XafApplication application, EditDraftTypePolicy policy, Guid subSectionOid) =>
        application != null && policy != null;
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
