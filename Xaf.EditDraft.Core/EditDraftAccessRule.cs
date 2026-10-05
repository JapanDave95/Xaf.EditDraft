using System;

namespace Xaf.EditDraft.Core;

/// <summary>
/// SINGLE-MODEL (owner review). Pure part of the generic restore's apply-time re-check (design §3 S3
/// check 1 + View.AllowEdit; library design §4.11 SEC-3), tested without XAF. The XAF-facing checks are
/// EditDraftMemberAccess (member write permission) and EditDraftServices.MayRestore (XAF security on the record plus the
/// host's optional IEditDraftAccessCheck).
/// </summary>
public static class EditDraftAccessRule
{
    /// <summary>
    /// The draft may be applied when the current login is its owner (and the owner the plan was built
    /// for), it is live (not expired; discarded only when opened from the list's search), at the revision
    /// the popup showed, for THIS record, and the view allows editing.
    /// </summary>
    public static bool MayApply(Guid currentOwner, Guid draftOwner, Guid planOwner, Guid recordOid, Guid planTarget, Guid draftTarget,
                                bool expired, bool discarded, bool fromSearch, int draftRevision, int planRevision, bool viewAllowEdit)
    {
        if (currentOwner == Guid.Empty || currentOwner != draftOwner || currentOwner != planOwner) return false;
        if (recordOid == Guid.Empty || recordOid != planTarget || draftTarget != planTarget) return false;
        if (expired) return false;
        if (discarded && !fromSearch) return false;
        if (draftRevision != planRevision) return false;
        return viewAllowEdit;
    }
}
