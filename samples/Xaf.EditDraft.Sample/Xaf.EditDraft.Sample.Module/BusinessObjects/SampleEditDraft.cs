using DevExpress.Xpo;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Sample.Module.BusinessObjects;

/// <summary>
/// The sample's draft store. The library ships the store's members on a NON-persistent abstract base
/// (EditDraftStoreBase); this one-line class is the consumer-owned persistent class that maps them. XPO stores the
/// 19 inherited members plus Oid and OptimisticLockField in this class's table, dbo.SampleEditDraft (the class name;
/// [Persistent("...")] would choose another name). Registered in Startup with services.AddEditDraftStore&lt;SampleEditDraft&gt;().
///
/// Security: the library's writer works on this table through a NON-SECURED object space (create with CommitChanges,
/// reads by owner-filtered queries, updates and deletes by owner-filtered T-SQL; Xaf.EditDraft.Core/EditDraftWriter.cs),
/// so XAF role permissions do not apply to the engine. The owner condition (the XAF login's Guid) does. The type DENY the
/// Updater gives every role (DatabaseUpdate/Updater.cs) covers the other way in: ordinary XAF access to this class. A role
/// with IsAdministrative = true cannot be denied anything (XAF), so an administrator can open SampleEditDraft_ListView
/// and read every user's drafts. See the sample README, "Security".
/// </summary>
public class SampleEditDraft : EditDraftStoreBase
{
    public SampleEditDraft(Session session) : base(session) { }
}
