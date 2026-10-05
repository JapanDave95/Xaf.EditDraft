using Xaf.EditDraft.Core;
using Xaf.EditDraft.Sample.Module.BusinessObjects;

namespace Xaf.EditDraft.Sample.Module.EditDrafts;

/// <summary>
/// The sample's one edit-draft policy. A type takes part only when a policy for it is registered; nothing else is
/// captured. Registered in Startup with <c>services.AddEditDraftRegistry(NoteEditDraftPolicy.Register)</c>.
///
/// What each line decides:
/// - PolicyId "Note": the per-type switch key is EditDraftCapture:Types:Note:Enabled (appsettings.json).
/// - Owner and access: nothing to declare here. The XAF login owns the draft (the library's default owner seam; no custom
///   resolver), and who may restore or recreate a Note is decided by XAF security (roles and their Note permissions; see
///   the RestrictedNotes role in DatabaseUpdate/Updater.cs). No IEditDraftAccessCheck is registered.
/// - MemberDeclaringBase Note: only members declared on Note are candidates, not BaseObject's Oid and lock field.
/// - ApprovedViewIds / ListViewIds: the XAF-generated view ids of Note (ClassName_DetailView / ClassName_ListView).
///   Capture and the offer run only in the approved root DetailView; the header action and the row badges use the ListView.
/// - AllowNewRecords: a never-saved Note is captured too (while EditDraftCapture:NewRecords:Enabled is on) and can be
///   recreated from the drafts list.
/// - Decisions: one line per member reflection proposes, written with the library helpers (EditDraftDecisions.Restorable
///   etc.). Every Note setter is SetPropertyValue only, so all four are Restorable (label "A"). A member added to Note later
///   is NOT captured until it gets a line here.
/// - ContextDateOf, Groups, NewRecordReconstructionOrder: not needed for Note (left at their defaults).
/// </summary>
public static class NoteEditDraftPolicy
{
    public const string Id = "Note";
    public const string DetailViewId = "Note_DetailView";
    public const string ListViewId = "Note_ListView";

    private const string Evidence = "samples/Xaf.EditDraft.Sample/Xaf.EditDraft.Sample.Module/BusinessObjects/Note.cs";

    public static EditDraftTypePolicy Create() => new(typeof(Note))
    {
        PolicyId = Id,
        MemberDeclaringBase = typeof(Note),
        ApprovedViewIds = new HashSet<string>(StringComparer.Ordinal) { DetailViewId },
        ListViewIds = new HashSet<string>(StringComparer.Ordinal) { ListViewId },
        AllowNewRecords = true,
        // The library helper writes label "A" (Restorable); the reason and the evidence are required by the gate.
        Decisions = EditDraftDecisions.Table(
            EditDraftDecisions.Restorable(nameof(Note.Title), SetterReason, Evidence),
            EditDraftDecisions.Restorable(nameof(Note.Body), SetterReason, Evidence),
            EditDraftDecisions.Restorable(nameof(Note.Priority), SetterReason, Evidence),
            EditDraftDecisions.Restorable(nameof(Note.DueOn), SetterReason, Evidence))
    };

    /// <summary>The registration callback for services.AddEditDraftRegistry(...).</summary>
    public static void Register(EditDraftRegistry registry)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        registry.Register(Create());
    }

    // Every Note setter is SetPropertyValue only, so the typed value can be put back as it was typed.
    private const string SetterReason = "SetPropertyValue only";
}
