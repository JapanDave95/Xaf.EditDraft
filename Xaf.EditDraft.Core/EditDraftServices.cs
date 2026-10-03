using System;
using DevExpress.ExpressApp;
using DevExpress.Xpo.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Xaf.EditDraft.Core;

/// <summary>
/// The store class a host registered (<see cref="EditDraftServiceCollectionExtensions.AddEditDraftStore{TStore}"/>):
/// the host's own subclass of <see cref="EditDraftStoreBase"/>. Its XPO table name is what the writer's
/// statements address.
/// </summary>
public sealed class EditDraftStoreRegistration
{
    private readonly Lazy<string> _tableName;

    public EditDraftStoreRegistration(Type storeType)
    {
        if (storeType == null) throw new ArgumentNullException(nameof(storeType));
        if (!typeof(EditDraftStoreBase).IsAssignableFrom(storeType) || storeType.IsAbstract)
            throw new ArgumentException($"{storeType.FullName} is not a concrete subclass of {nameof(EditDraftStoreBase)}.", nameof(storeType));
        StoreType = storeType;
        _tableName = new Lazy<string>(() => TableNameOf(storeType));
    }

    public Type StoreType { get; }

    /// <summary>The XPO table of the store class ([Persistent("...")] or the class name).</summary>
    public string TableName => _tableName.Value;

    /// <summary>The XPO table name of a store class, read from its XPO class info.</summary>
    public static string TableNameOf(Type storeType) => new ReflectionDictionary().GetClassInfo(storeType).TableName;
}

/// <summary>Switch configuration: the section that holds Enabled, Types:&lt;PolicyId&gt;:Enabled and ListViews:Enabled.</summary>
public sealed class EditDraftSwitchOptions
{
    public string Section { get; init; } = EditDraftSwitch.DefaultSection;
}

/// <summary>Local "now" from a <see cref="TimeProvider"/>, with the same local-time semantics as DateTime.Now.</summary>
public static class EditDraftClock
{
    /// <summary>The provider's local wall-clock time, Kind = Local (as DateTime.Now). Null = the system clock.</summary>
    public static DateTime Now(TimeProvider clock) =>
        DateTime.SpecifyKind((clock ?? TimeProvider.System).GetLocalNow().DateTime, DateTimeKind.Local);
}

/// <summary>
/// How the engine finds its host-supplied parts in the application's service provider. Each lookup has a
/// library default that fails closed or keeps today's behaviour; nothing is held in static state.
/// The owner and record-access lookups (single-model security) are in EditDraftOwnerSeam.cs and EditDraftAccessSeam.cs.
/// </summary>
public static partial class EditDraftServices
{
    /// <summary>The host's registry (DI singleton); an empty frozen registry when none is registered (nothing is admitted).</summary>
    public static EditDraftRegistry Registry(IServiceProvider services) =>
        (services?.GetService(typeof(EditDraftRegistry)) as EditDraftRegistry) ?? EditDraftRegistry.Empty;

    /// <summary>The host's clock (TimeProvider in DI); the system clock otherwise.</summary>
    public static TimeProvider Clock(IServiceProvider services) =>
        (services?.GetService(typeof(TimeProvider)) as TimeProvider) ?? TimeProvider.System;
}

/// <summary>Host registration of the engine's parts.</summary>
public static class EditDraftServiceCollectionExtensions
{
    /// <summary>Registers the host's store class (its own subclass of <see cref="EditDraftStoreBase"/>).</summary>
    public static IServiceCollection AddEditDraftStore<TStore>(this IServiceCollection services) where TStore : EditDraftStoreBase
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        services.AddSingleton(new EditDraftStoreRegistration(typeof(TStore)));
        return services;
    }

    /// <summary>
    /// Registers the host's policy registry as a DI singleton: <paramref name="register"/> runs once, then the
    /// registry is frozen (no registration after the first use).
    /// </summary>
    public static IServiceCollection AddEditDraftRegistry(this IServiceCollection services, Action<EditDraftRegistry> register)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (register == null) throw new ArgumentNullException(nameof(register));
        services.AddSingleton(_ => EditDraftRegistry.Create(register));
        return services;
    }
}
