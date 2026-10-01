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
