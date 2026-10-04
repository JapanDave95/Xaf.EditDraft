using System;
using DevExpress.ExpressApp;

namespace Xaf.EditDraft.Core;

/// <summary>
/// WHO OWNS A 入力控 DRAFT — design §4.11 SEC-1. SINGLE-MODEL (owner review).
///
/// The pure decision a host's owner resolver can reuse: a login Guid owns its drafts unless the host
/// knows it as a staff login that cannot be read (unknown owner = no owner) or one it refuses (CareCrew,
/// owner decision D6: a GeneralUser login never owns a draft). Guid.Empty means "no owner" and every
/// caller treats it as refusal. Pure, so it is tested without XAF.
/// </summary>
public static class EditDraftOwnerRule
{
    public static Guid Decide(Guid? loginOid, bool loginIsStaffMember, bool staffFound, bool staffIsGeneralUser)
    {
        if (loginOid is not Guid oid || oid == Guid.Empty) return Guid.Empty;
        if (!loginIsStaffMember) return oid;
        if (!staffFound) return Guid.Empty;
        if (staffIsGeneralUser) return Guid.Empty;
        return oid;
    }
}

/// <summary>
/// The current owner ON THE CIRCUIT. The answer crosses to worker threads only as plain values. <see cref="OwnerFlag"/> is a
/// host-defined flag stored with each draft (EditDraftStoreBase.OwnerFlag) — record only, never used for access; the
/// library default resolver sets it to false.
/// </summary>
public readonly record struct EditDraftOwnerInfo(Guid Oid, bool OwnerFlag)
{
    public static readonly EditDraftOwnerInfo None = new(Guid.Empty, false);
    public bool IsNone => Oid == Guid.Empty;
}

/// <summary>
/// Owner seam (SEC-1). Called on the UI thread/circuit. Returns the owner, or <see cref="EditDraftOwnerInfo.None"/>:
/// None refuses capture, offer, list and apply. An exception inside a resolver is None
/// (<see cref="EditDraftServices.CurrentOwner"/>).
/// </summary>
public interface IEditDraftOwnerResolver
{
    /// <summary>The owner as seen from <paramref name="objectSpace"/> (the screen's own space).</summary>
    EditDraftOwnerInfo Current(IObjectSpace objectSpace);

    /// <summary>For callers whose own object space cannot load the login (popups over non-persistent objects).</summary>
    EditDraftOwnerInfo Current(XafApplication application);
}

public static partial class EditDraftServices
{
    /// <summary>The host's owner seam; the library default (the XAF login's Guid) otherwise. SINGLE-MODEL (design §4.11 SEC-1).</summary>
    public static IEditDraftOwnerResolver Owner(IServiceProvider services) =>
        (services?.GetService(typeof(IEditDraftOwnerResolver)) as IEditDraftOwnerResolver) ?? XafLoginEditDraftOwnerResolver.Instance;

    /// <summary>The current owner ON THE CIRCUIT through the owner seam. An exception inside a resolver is no owner.</summary>
    public static EditDraftOwnerInfo CurrentOwner(IServiceProvider services, IObjectSpace objectSpace)
    {
        try { return Owner(services).Current(objectSpace); }
        catch (Exception ex)
        {
            EditDraftLog.Warning($"[EditDraft] could not resolve the login owner, treating as none: {ex.GetType().Name}");
            return EditDraftOwnerInfo.None;
        }
    }

    /// <summary>As <see cref="CurrentOwner(IServiceProvider, IObjectSpace)"/>, for callers without an object space that can load the login.</summary>
    public static EditDraftOwnerInfo CurrentOwner(IServiceProvider services, XafApplication application)
    {
        try { return Owner(services).Current(application); }
        catch (Exception ex)
        {
            EditDraftLog.Warning($"[EditDraft] owner lookup failed, treating as none: {ex.GetType().Name}");
            return EditDraftOwnerInfo.None;
        }
    }
}

/// <summary>
/// Library default (SEC-1): the XAF login's key when it is a non-empty Guid, else no owner. A login whose key
/// is not a Guid is not supported (the store column is a Guid) and resolves to no owner — fail closed, never a
/// guessed owner. The recorded flag (OwnerFlag) is false.
/// </summary>
public sealed class XafLoginEditDraftOwnerResolver : IEditDraftOwnerResolver
{
    public static readonly XafLoginEditDraftOwnerResolver Instance = new();

    public EditDraftOwnerInfo Current(IObjectSpace objectSpace) => Login();

    public EditDraftOwnerInfo Current(XafApplication application) => application == null ? EditDraftOwnerInfo.None : Login();

    private static EditDraftOwnerInfo Login()
    {
        try
        {
            var oid = SecuritySystem.CurrentUserId is Guid g ? g : Guid.Empty;
            return oid == Guid.Empty ? EditDraftOwnerInfo.None : new EditDraftOwnerInfo(oid, false);
        }
        catch (Exception ex)
        {
            // Unknown owner = no owner. A draft is never written or shown under a guess.
            EditDraftLog.Warning($"[EditDraft] could not resolve the login owner, treating as none: {ex.GetType().Name}");
            return EditDraftOwnerInfo.None;
        }
    }
}
