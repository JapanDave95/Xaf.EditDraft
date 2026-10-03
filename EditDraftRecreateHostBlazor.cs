using System;
using System.Linq;
using System.Threading;
using DevExpress.ExpressApp;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// NEW records — the XAF side of a recreate (EditDraftRecreate.Run in Core decides the order; design
/// docs/edit-draft-new-records-design-2026-10-02.md §4.4): reads the draft through the owner-scoped writer, answers "already
/// saved?" through a SECURED object space, asks the security questions, builds the candidate in its own object space and
/// shows it in a MODAL window (owner D6, like today's 開く). Owner, record access and member write permission through the
/// Core seams; "now" from the host clock; texts from EditDraftTexts.
/// SINGLE-MODEL parts (owner review, D14): <see cref="MayCreate"/> (S1/S2, EditDraftCreateAccess), <see cref="IsSubSectionVisible"/>
/// (S5 i, the record-access seam), the candidate's IsVisible (S5 ii), and the owner predicate of the read and of the claim
/// (EditDraftWriter.ReadOwn / TryClaimNew).
/// </summary>
internal sealed class EditDraftRecreateHostBlazor : IEditDraftRecreateHost
{
    private readonly XafApplication _application;
    private readonly Frame _frame;
    private readonly SynchronizationContext _circuit;
    private readonly Action<string, InformationType> _message;
    private readonly EditDraftWriter _writer;

    public EditDraftRecreateHostBlazor(XafApplication application, Frame frame, SynchronizationContext circuit, Action<string, InformationType> message)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _frame = frame;
        _circuit = circuit;
        _message = message;
        _writer = new EditDraftWriter(application.ServiceProvider);
    }

    public EditDraftOwnerInfo CurrentOwner() => EditDraftServices.CurrentOwner(_application.ServiceProvider, _application);

    public DateTime Now() => EditDraftClock.Now(EditDraftServices.Clock(_application.ServiceProvider));

    public EditDraftRecreateDraft ReadDraft(Guid draftOid, Guid ownerOid, DateTime now)
    {
        using var readSpace = _writer.CreateReadSpace(out var scope);
        using (scope)
        {
            var d = _writer.ReadOwn(readSpace, draftOid, ownerOid);   // owner-scoped (single-model)
            if (d == null) return null;
            return new EditDraftRecreateDraft
            {
                DraftOid = d.Oid, Revision = d.Revision, ObjectType = d.ObjectType, TargetOid = d.TargetOid, SubSectionOid = d.SubSectionOid,
                ContextText = d.ContextText, ViewId = d.ViewId, LastCapturedOn = d.LastCapturedOn, EntryCount = d.EntryCount,
                Live = !d.HasExpired(now), PayloadReadable = d.IsPayloadReadable, PayloadJson = d.Payload
            };
        }
    }

    public EditDraftTypePolicy Policy(string objectType) => EditDraftServices.Registry(_application.ServiceProvider).Find(objectType);

    /// <summary>A SECURED read: a record this login may not read answers "not saved" (accepted residual, design §5 S10 / D11).</summary>
    public bool? IsSaved(EditDraftTypePolicy policy, Guid oid)
    {
        try
        {
            using var os = _application.CreateObjectSpace(policy.Type);
            return os.GetObjectByKey(policy.Type, oid) != null;
        }
        catch (Exception ex)
        {
            EditDraftLog.Warning($"[EditDraft] recreate: the already-saved read failed ({ex.GetType().Name}); the person is asked");
            return null;
        }
    }

    public bool MayCreate(EditDraftTypePolicy policy) => EditDraftCreateAccess.MayCreate(_application, policy);

    public bool IsSubSectionVisible(EditDraftTypePolicy policy, Guid subSectionOid) =>
        EditDraftServices.RecordAccess(_application.ServiceProvider).IsSubSectionVisible(_application, policy, subSectionOid);

    public IEditDraftRecreateCandidate CreateCandidate(EditDraftTypePolicy policy)
    {
        var os = _application.CreateObjectSpace(policy.Type);
        try
        {
            var record = os.CreateObject(policy.Type);   // AfterConstruction runs; nothing is saved until the person saves
            return new Candidate(this, os, record);
        }
        catch
        {
            os.Dispose();
            throw;
        }
    }

    public int Claim(EditDraftRecreateDraft draft, Guid ownerOid, Guid editorInstanceId, string payloadJson, int entryCount, DateTime now) =>
        _writer.TryClaimNew(draft.DraftOid, draft.Revision, ownerOid, editorInstanceId, payloadJson, entryCount, now);

    /// <summary>The fresh object of one recreate, in its own object space. Disposed unsaved unless its screen took it over.</summary>
    private sealed class Candidate : IEditDraftRecreateCandidate
    {
        private readonly EditDraftRecreateHostBlazor _host;
        private readonly IObjectSpace _os;
        private readonly object _record;
        private EditDraftRestoreGuard _guard;
        private DetailView _view;
        private bool _handedOver;

        public Candidate(EditDraftRecreateHostBlazor host, IObjectSpace os, object record)
        {
            _host = host;
            _os = os;
            _record = record;
        }

        public Guid Oid => (_record as BaseObject)?.Oid ?? Guid.Empty;

        /// <summary>Read right after Show, while the guard is still open (it closes behind the posted work).</summary>
        public bool GuardViolated => _guard?.Violated ?? false;

        public EditDraftNewApplyResult Fill(EditDraftTypePolicy policy, EditDraftPayload payload)
        {
            // D12: open from before the first setter until the work posted by the apply and the screen's activation has run.
            _guard = new EditDraftRestoreGuard(_os, policy.LogTag,
                what => _host._message?.Invoke(EditDraftTexts.Of(t => t.ApplyGuardStopped), InformationType.Warning));
            return EditDraftRestorer.ApplyNew(policy, _os, _record, payload, p => EditDraftMemberAccess.CanWrite(_os, _record, p));
        }

        public bool IsVisible(EditDraftTypePolicy policy) =>
            EditDraftServices.RecordAccess(_host._application.ServiceProvider).IsRecordVisible(_host._application, policy, _record);

        public bool Show(EditDraftTypePolicy policy, EditDraftPendingAdoption adoption)
        {
            var adoptions = _host._application.ServiceProvider?.GetService(typeof(EditDraftPendingAdoptions)) as EditDraftPendingAdoptions;
            var viewId = policy.ApprovedViewIds?.FirstOrDefault();
            if (adoptions == null || string.IsNullOrEmpty(viewId))
            {
                EditDraftLog.Warning($"[EditDraft] recreate: cannot show {policy.TypeName} (pending adoptions registered={adoptions != null}, view={viewId ?? "none"})");
                return false;
            }
            adoptions.Offer(_record, adoption);
            _view = _host._application.CreateDetailView(_os, viewId, true, _record);
            // The modal window's controllers activate inside ShowView (BlazorShowViewStrategy.ShowDialog → window.SetView),
            // so the capture has taken (or refused) the adoption when ShowView returns.
            _host._application.ShowViewStrategy.ShowView(new ShowViewParameters(_view) { TargetWindow = TargetWindow.NewModalWindow },
                new ShowViewSource(_host._frame, null));
            if (!adoption.IsAcknowledged)
            {
                adoptions.Withdraw(_record);
                return false;   // Dispose closes the record unsaved
            }
            _handedOver = true;
            _guard?.CloseAfterPostedWork(_host._circuit);
            return true;
        }

        public void Dispose()
        {
            if (_handedOver) return;   // the shown screen owns its object space now
            try { _guard?.Dispose(); } catch { }   // closed first: the rollback below must not be cancelled by it
            if (_view != null)
            {
                try { if (!_os.IsDisposed) _os.Rollback(); } catch { }
                try { _view.Close(); } catch { }
            }
            try { if (!_os.IsDisposed) _os.Dispose(); } catch { }
        }
    }
}
