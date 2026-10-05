using System;
using System.Collections.Generic;
using System.Linq;
using DevExpress.ExpressApp;

namespace Xaf.EditDraft.Core;

/// <summary>
/// The current owner ON THE CIRCUIT. The answer crosses to worker threads only as a plain value. Guid.Empty (<see cref="None"/>)
/// means "no owner" and every caller treats it as refusal.
/// </summary>
public readonly record struct EditDraftOwnerInfo(Guid Oid)
{
    public static readonly EditDraftOwnerInfo None = new(Guid.Empty);
    public bool IsNone => Oid == Guid.Empty;
}

/// <summary>
/// WHO OWNS A DRAFT — design §4.11 SEC-1. SINGLE-MODEL (owner review). Called on the UI thread/circuit. Returns the owner,
/// or <see cref="EditDraftOwnerInfo.None"/>: None refuses capture, offer, list and apply. An exception inside a resolver is
/// None (<see cref="EditDraftServices.CurrentOwner(IServiceProvider, IObjectSpace, EditDraftTypePolicy)"/>).
///
/// This seam answers "who owns a draft", never "who may restore it" (that is XAF security plus
/// <see cref="IEditDraftAccessCheck"/>). The policy argument (0.4.0-preview.1) is the registered policy of the type the draft
/// is for, as <see cref="EditDraftRegistry.Find(string)"/> returns it, so a host can name a different owner per type; it is
/// null when the draft's type is not registered (the drafts list still shows such a row; 破棄 is its only action). Screens
/// that show drafts of several types (the drafts list) ask once per type and list a row only when its stored owner is the
/// owner named for its type.
/// </summary>
public interface IEditDraftOwnerResolver
{
    /// <summary>The owner of drafts of <paramref name="policy"/>'s type as seen from <paramref name="objectSpace"/> (the screen's own space).</summary>
    EditDraftOwnerInfo Current(IObjectSpace objectSpace, EditDraftTypePolicy policy);

    /// <summary>For callers whose own object space cannot load the login (popups over non-persistent objects).</summary>
    EditDraftOwnerInfo Current(XafApplication application, EditDraftTypePolicy policy);
}

public static partial class EditDraftServices
{
    /// <summary>The host's owner seam; the library default (the XAF login's Guid) otherwise. SINGLE-MODEL (design §4.11 SEC-1).</summary>
    public static IEditDraftOwnerResolver Owner(IServiceProvider services) =>
        (services?.GetService(typeof(IEditDraftOwnerResolver)) as IEditDraftOwnerResolver) ?? XafLoginEditDraftOwnerResolver.Instance;

    /// <summary>The owner of drafts of <paramref name="policy"/>'s type ON THE CIRCUIT through the owner seam. An exception inside a resolver is no owner.</summary>
    public static EditDraftOwnerInfo CurrentOwner(IServiceProvider services, IObjectSpace objectSpace, EditDraftTypePolicy policy)
    {
        try { return Owner(services).Current(objectSpace, policy); }
        catch (Exception ex)
        {
            EditDraftLog.Warning($"[EditDraft] could not resolve the login owner, treating as none: {ex.GetType().Name}");
            return EditDraftOwnerInfo.None;
        }
    }

    /// <summary>As <see cref="CurrentOwner(IServiceProvider, IObjectSpace, EditDraftTypePolicy)"/>, for callers without an object space that can load the login.</summary>
    public static EditDraftOwnerInfo CurrentOwner(IServiceProvider services, XafApplication application, EditDraftTypePolicy policy)
    {
        try { return Owner(services).Current(application, policy); }
        catch (Exception ex)
        {
            EditDraftLog.Warning($"[EditDraft] owner lookup failed, treating as none: {ex.GetType().Name}");
            return EditDraftOwnerInfo.None;
        }
    }

    /// <summary>The owner of drafts of the type named <paramref name="objectType"/> (its registered policy, or null when it is not registered).</summary>
    internal static EditDraftOwnerInfo CurrentOwnerOfType(IServiceProvider services, XafApplication application, string objectType) =>
        CurrentOwner(services, application, string.IsNullOrEmpty(objectType) ? null : Registry(services).Find(objectType));
}

/// <summary>
/// The owners a screen that shows drafts of several types reads (the drafts list), resolved once per type for one call.
/// A row belongs to the list only when its stored owner is the owner the seam names for the row's type. With one owner for
/// every type (the library default) that is one query, as before 0.4.0-preview.1. SINGLE-MODEL (owner review).
/// </summary>
internal sealed class EditDraftOwnersByType
{
    private readonly EditDraftRegistry _registry;
    private readonly Func<EditDraftTypePolicy, EditDraftOwnerInfo> _resolve;
    private readonly Dictionary<EditDraftTypePolicy, EditDraftOwnerInfo> _byPolicy = new();
    private EditDraftOwnerInfo? _unregistered;

    public EditDraftOwnersByType(EditDraftRegistry registry, Func<EditDraftTypePolicy, EditDraftOwnerInfo> resolve)
    {
        _registry = registry ?? EditDraftRegistry.Empty;
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
    }

    /// <summary>The owner for drafts of <paramref name="objectType"/> (its registered policy, or none registered).</summary>
    public EditDraftOwnerInfo Of(string objectType)
    {
        var policy = string.IsNullOrEmpty(objectType) ? null : _registry.Find(objectType);
        if (policy == null) return _unregistered ??= Resolve(null);
        if (!_byPolicy.TryGetValue(policy, out var owner)) _byPolicy[policy] = owner = Resolve(policy);
        return owner;
    }

    /// <summary>
    /// The distinct owners to read: for <paramref name="objectTypeFilter"/> only, or (null) for every generic policy and for
    /// drafts of a type that is not registered. Empty when the seam names no owner at all.
    /// </summary>
    public IReadOnlyList<Guid> ToRead(string objectTypeFilter)
    {
        var types = objectTypeFilter != null
            ? new[] { objectTypeFilter }
            : _registry.All.Where(EditDraftTypePolicy.IsGeneric).Select(p => p.TypeName).Append(null);
        return types.Select(Of).Where(o => !o.IsNone).Select(o => o.Oid).Distinct().ToList();
    }

    /// <summary>A row is listed when its stored owner is the owner named for its type.</summary>
    public bool Lists(Guid rowOwner, string rowType) => rowOwner != Guid.Empty && Of(rowType).Oid == rowOwner;

    private EditDraftOwnerInfo Resolve(EditDraftTypePolicy policy)
    {
        try { return _resolve(policy); }
        catch (Exception ex)
        {
            EditDraftLog.Warning($"[EditDraft] owner lookup failed, treating as none: {ex.GetType().Name}");
            return EditDraftOwnerInfo.None;
        }
    }
}

/// <summary>
/// Library default (SEC-1): the XAF login's key when it is a non-empty Guid, else no owner, for every type (the policy is
/// not consulted). A login whose key is not a Guid is not supported (the store column is a Guid) and resolves to no owner —
/// fail closed, never a guessed owner.
/// </summary>
public sealed class XafLoginEditDraftOwnerResolver : IEditDraftOwnerResolver
{
    public static readonly XafLoginEditDraftOwnerResolver Instance = new();

    public EditDraftOwnerInfo Current(IObjectSpace objectSpace, EditDraftTypePolicy policy) => Login();

    public EditDraftOwnerInfo Current(XafApplication application, EditDraftTypePolicy policy) => application == null ? EditDraftOwnerInfo.None : Login();

    private static EditDraftOwnerInfo Login()
    {
        try
        {
            var oid = SecuritySystem.CurrentUserId is Guid g ? g : Guid.Empty;
            return oid == Guid.Empty ? EditDraftOwnerInfo.None : new EditDraftOwnerInfo(oid);
        }
        catch (Exception ex)
        {
            // Unknown owner = no owner. A draft is never written or shown under a guess.
            EditDraftLog.Warning($"[EditDraft] could not resolve the login owner, treating as none: {ex.GetType().Name}");
            return EditDraftOwnerInfo.None;
        }
    }
}
