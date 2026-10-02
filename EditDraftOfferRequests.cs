using System;
using System.Runtime.CompilerServices;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// Per-circuit (scoped) note: the 「入力控」 list opened an EXISTING record for one particular draft;
/// the record's screen offers exactly that draft (even a discarded one found by search). Keyed weakly
/// on the business object, so nothing outlives the object.
/// (Milestone M1: moved out of EditDraftSwitch.cs; milestone M2: moved with its two users, the restore and
/// list controllers, into Xaf.EditDraft.Blazor. Registered by EditDraftBlazorServiceCollectionExtensions.AddEditDraftBlazor.)
/// </summary>
public sealed class EditDraftOfferRequests
{
    private readonly ConditionalWeakTable<object, object> _offers = new();

    public void RequestOffer(object record, Guid draftOid)
    {
        if (record == null || draftOid == Guid.Empty) return;
        lock (_offers) { _offers.AddOrUpdate(record, draftOid); }
    }

    public Guid TakeOfferRequest(object record)
    {
        if (record == null) return Guid.Empty;
        lock (_offers)
        {
            if (!_offers.TryGetValue(record, out var v)) return Guid.Empty;
            _offers.Remove(record);
            return v is Guid g ? g : Guid.Empty;
        }
    }
}
