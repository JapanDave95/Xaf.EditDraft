using System;
using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;

namespace Xaf.EditDraft.Core;

/// <summary>
/// 入力控 store — the unsaved input of ONE detail or list screen of a registered type, as plain columns plus
/// a JSON payload (<see cref="EditDraftPayload"/>). NON-PERSISTENT and abstract: XPO maps every member
/// below into the table of the consumer's concrete subclass (XPO class inheritance, docs 2064), so a
/// consumer declares one class, e.g. <c>public class EditDraft : EditDraftStoreBase</c>, and registers it
/// with <c>services.AddEditDraftStore&lt;EditDraft&gt;()</c>. The library never maps a class of its own to
/// a table. No library type is named <c>EditDraft</c> (a type of that name under the Xaf namespace tree
/// collides with the namespace Xaf.EditDraft, CS0118).
///
/// The 19 members, their sizes and the four named indexes are the ones the library's first host declared before the
/// extraction (milestone M1), so the table, columns and indexes of an existing database stay valid (no migration).
/// Two members were renamed on 2026-10-04 (gap G8) and keep their column names through [Persistent]:
/// <see cref="OwnerFlag"/> (column LoginIsStaffMember) and <see cref="ScopeOid"/> (column SubSectionOid).
///
/// SECURITY: the payload is the typed text in readable JSON. Deny the store class to every role
/// (EditDraftSecurity.DenyStoreToAllRoles in the ModuleUpdater), exclude it from the audit trail if the application
/// uses one, and turn on a retention sweep (EditDraftRetention) — see docs/consumer-guide.md.
///
/// OWNER = <see cref="OwnerUserOid"/>, a Guid (supported contract v1: Guid user keys). A row is never written
/// without an owner, and every read, update and delete filters on it IN THE QUERY (EditDraftWriter).
///
/// Retention: <see cref="ExpiresOn"/> = <see cref="FirstCapturedOn"/> + 7 days, set once, never moved.
/// <see cref="DeletedOn"/> = soft discard (破棄). Saving the record deletes the draft.
///
/// BaseObject: its key is the Guid Oid the writer's statements name (KB fix-497: not CustomBaseObject).
///
/// Captions (milestone M3): the three member captions below are the English defaults; the captions shown come from the
/// text set in use, written into the model by <see cref="EditDraftStoreCaptionUpdater"/> (<see cref="EditDraftModelCaptions.Store"/>).
/// </summary>
[NonPersistent]
[DeferredDeletion(false)]
public abstract class EditDraftStoreBase : BaseObject
{
    protected EditDraftStoreBase(Session session) : base(session) { }

    /// <summary>Kept 7 days from the first capture, never extended (design §2).</summary>
    public const int RetentionDays = 7;

    /// <summary>The payload shape this build writes and reads (EditDraftPayload.Schema).</summary>
    public const int CurrentPayloadSchemaVersion = 1;

    public static DateTime CalculateExpiry(DateTime firstCapturedOn) => firstCapturedOn.AddDays(RetentionDays);

    // ---- identity -------------------------------------------------------------------------

    private Guid _DraftKey;
    /// <summary>Generated at the first capture of an editing context. Unique per owner.</summary>
    [Indexed(nameof(OwnerUserOid), Unique = true, Name = "uxEditDraft_Key")]
    public Guid DraftKey { get => _DraftKey; set => SetPropertyValue(nameof(DraftKey), ref _DraftKey, value); }

    private Guid _EditorInstanceId;
    /// <summary>One per open editing context (one detail screen activation).</summary>
    public Guid EditorInstanceId { get => _EditorInstanceId; set => SetPropertyValue(nameof(EditorInstanceId), ref _EditorInstanceId, value); }

    // ---- ownership --------------------------------------------------------------------------

    private Guid _OwnerUserOid;
    /// <summary>The XAF login that typed it. Every read path filters on this in the query.</summary>
    [Indexed(nameof(ExpiresOn), Name = "iEditDraft_Owner_Expiry")]
    [ModelDefault("Caption", "Owner")]
    public Guid OwnerUserOid { get => _OwnerUserOid; set => SetPropertyValue(nameof(OwnerUserOid), ref _OwnerUserOid, value); }

    private bool _OwnerFlag;
    /// <summary>
    /// A host-defined flag recorded with the owner by the host's owner resolver (EditDraftOwnerInfo.OwnerFlag; the library
    /// default resolver records false). Record only; never used for access. Column "LoginIsStaffMember" (gap G8: the member
    /// was renamed, the column was not, so an existing store table needs no migration).
    /// </summary>
    [Persistent("LoginIsStaffMember")]
    public bool OwnerFlag { get => _OwnerFlag; set => SetPropertyValue(nameof(OwnerFlag), ref _OwnerFlag, value); }

    // ---- target -----------------------------------------------------------------------------

    private string _ObjectType;
    /// <summary>The CLR class name of the record (e.g. "ToDo"), mapped back only through the engine's registry.</summary>
    [Size(100)]
    [ModelDefault("Caption", "Record type")]
    public string ObjectType { get => _ObjectType; set => SetPropertyValue(nameof(ObjectType), ref _ObjectType, value); }

    private Guid _TargetOid;
    /// <summary>The existing record the draft edits. Never Guid.Empty for existing-record drafts.</summary>
    [Indexed(Name = "iEditDraft_Target")]
    public Guid TargetOid { get => _TargetOid; set => SetPropertyValue(nameof(TargetOid), ref _TargetOid, value); }

    private Guid _ScopeOid;
    /// <summary>
    /// The record's access scope at capture (EditDraftTypePolicy.ScopeOf), for the visibility re-check of a new record
    /// (IEditDraftRecordAccess.IsScopeVisible). Guid.Empty = the type has none. Column "SubSectionOid" (gap G8: the member
    /// was renamed, the column was not).
    /// </summary>
    [Persistent("SubSectionOid")]
    public Guid ScopeOid { get => _ScopeOid; set => SetPropertyValue(nameof(ScopeOid), ref _ScopeOid, value); }

    private string _ContextText;
    /// <summary>Short display text taken at capture: the type caption and a date. Never a person's name (design §3 S4).</summary>
    [Size(200)]
    [ModelDefault("Caption", "Record")]
    public string ContextText { get => _ContextText; set => SetPropertyValue(nameof(ContextText), ref _ContextText, value); }

    private string _ViewId;
    [Size(100)]
    public string ViewId { get => _ViewId; set => SetPropertyValue(nameof(ViewId), ref _ViewId, value); }

    // ---- payload ----------------------------------------------------------------------------

    private int _PayloadSchemaVersion;
    public int PayloadSchemaVersion { get => _PayloadSchemaVersion; set => SetPropertyValue(nameof(PayloadSchemaVersion), ref _PayloadSchemaVersion, value); }

    private string _Payload;
    /// <summary>JSON: member path -> baseline and latest value. Readable typed text — hence the three controls.</summary>
    [Size(SizeAttribute.Unlimited)]
    public string Payload { get => _Payload; set => SetPropertyValue(nameof(Payload), ref _Payload, value); }

    private int _EntryCount;
    public int EntryCount { get => _EntryCount; set => SetPropertyValue(nameof(EntryCount), ref _EntryCount, value); }

    private int _Revision;
    /// <summary>The write fence: every conditional statement names the expected revision.</summary>
    public int Revision { get => _Revision; set => SetPropertyValue(nameof(Revision), ref _Revision, value); }

    // ---- lifecycle --------------------------------------------------------------------------

    private DateTime _FirstCapturedOn;
    /// <summary>The retention clock. Nothing may move it.</summary>
    public DateTime FirstCapturedOn { get => _FirstCapturedOn; set => SetPropertyValue(nameof(FirstCapturedOn), ref _FirstCapturedOn, value); }

    private DateTime _LastCapturedOn;
    public DateTime LastCapturedOn { get => _LastCapturedOn; set => SetPropertyValue(nameof(LastCapturedOn), ref _LastCapturedOn, value); }

    private DateTime _ExpiresOn;
    /// <summary>
    /// FirstCapturedOn + 7 days, set once, in the APPLICATION SERVER's local time (gap G5). Hidden at expiry; physically
    /// removed only by a retention sweep the host turns on (EditDraftRetention).
    /// </summary>
    [Indexed(Name = "iEditDraft_Expiry")]
    public DateTime ExpiresOn { get => _ExpiresOn; set => SetPropertyValue(nameof(ExpiresOn), ref _ExpiresOn, value); }

    private DateTime? _DeletedOn;
    /// <summary>Soft discard (破棄): hidden from the offer, still found by the list's search until expiry.</summary>
    public DateTime? DeletedOn { get => _DeletedOn; set => SetPropertyValue(nameof(DeletedOn), ref _DeletedOn, value); }

    private string _OriginHost;
    [Size(100)]
    public string OriginHost { get => _OriginHost; set => SetPropertyValue(nameof(OriginHost), ref _OriginHost, value); }

    private string _LastError;
    [Size(2000)]
    public string LastError { get => _LastError; set => SetPropertyValue(nameof(LastError), ref _LastError, value); }

    /// <summary>Hidden at the exact expiry instant and after (never offered or listed).</summary>
    public bool HasExpired(DateTime now) => ExpiresOn <= now;

    public bool IsPayloadReadable => PayloadSchemaVersion == CurrentPayloadSchemaVersion;
}
