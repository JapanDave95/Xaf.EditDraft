using System;
using DevExpress.ExpressApp;
using DevExpress.Xpo.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Xaf.EditDraft.Core;

/// <summary>
/// Options of the store registration (<see cref="EditDraftServiceCollectionExtensions.AddEditDraftStore{TStore}"/>).
/// The library supports SQL Server only.
/// </summary>
public sealed class EditDraftStoreOptions
{
    /// <summary>The schema used when neither the store class's XPO table name nor <see cref="Schema"/> names one.</summary>
    public const string DefaultSchema = "dbo";

    /// <summary>
    /// The SQL Server schema of the store table, for a store class whose XPO table name has no schema prefix. It must be the
    /// schema XPO creates the table in: the SQL Server data store's ObjectsOwner (XPO default "dbo"). Null or blank =
    /// <see cref="DefaultSchema"/>. A store class mapped as <c>[Persistent("Schema.Table")]</c> takes the schema from that
    /// name; a different value here is reported at startup.
    /// </summary>
    public string Schema { get; set; }
}

/// <summary>
/// The store class a host registered (<see cref="EditDraftServiceCollectionExtensions.AddEditDraftStore{TStore}"/>):
/// the host's own subclass of <see cref="EditDraftStoreBase"/>. Its schema-qualified, quoted table name
/// (<see cref="QualifiedName"/>) is what the writer's statements address.
/// </summary>
public sealed class EditDraftStoreRegistration
{
    private readonly Lazy<string> _tableName;
    private readonly Lazy<(string Schema, string Table)> _parts;

    public EditDraftStoreRegistration(Type storeType) : this(storeType, null) { }

    public EditDraftStoreRegistration(Type storeType, EditDraftStoreOptions options)
    {
        if (storeType == null) throw new ArgumentNullException(nameof(storeType));
        if (!typeof(EditDraftStoreBase).IsAssignableFrom(storeType) || storeType.IsAbstract)
            throw new ArgumentException($"{storeType.FullName} is not a concrete subclass of {nameof(EditDraftStoreBase)}.", nameof(storeType));
        StoreType = storeType;
        ConfiguredSchema = string.IsNullOrWhiteSpace(options?.Schema) ? null : options.Schema.Trim();
        _tableName = new Lazy<string>(() => TableNameOf(storeType));
        _parts = new Lazy<(string, string)>(() => Split(TableName, ConfiguredSchema));
    }

    public Type StoreType { get; }

    /// <summary>The XPO table of the store class ([Persistent("...")] or the class name), as XPO names it (may be "Schema.Table").</summary>
    public string TableName => _tableName.Value;

    /// <summary>The schema given in <see cref="EditDraftStoreOptions.Schema"/>, or null.</summary>
    public string ConfiguredSchema { get; }

    /// <summary>The schema of the store table: the prefix of the XPO table name, else the configured schema, else "dbo".</summary>
    public string Schema => _parts.Value.Schema;

    /// <summary>The table name without its schema.</summary>
    public string Table => _parts.Value.Table;

    /// <summary>The name the library's T-SQL addresses: <c>[schema].[table]</c>, each part quoted (EditDraftSql.QuoteIdentifier).</summary>
    public string QualifiedName => EditDraftSql.QuoteIdentifier(Schema) + "." + EditDraftSql.QuoteIdentifier(Table);

    /// <summary>True when the XPO table name carries a schema and the configured schema names a different one.</summary>
    public bool SchemaConflicts =>
        ConfiguredSchema != null && TableName.IndexOf('.') > 0 && !string.Equals(Schema, ConfiguredSchema, StringComparison.Ordinal);

    /// <summary>The XPO table name of a store class, read from its XPO class info.</summary>
    public static string TableNameOf(Type storeType) => new ReflectionDictionary().GetClassInfo(storeType).TableName;

    /// <summary>XPO's rule (ConnectionProviderSql.ComposeSafeSchemaName): the part before the first '.' is the schema.</summary>
    private static (string Schema, string Table) Split(string xpoTableName, string configuredSchema)
    {
        var dot = xpoTableName.IndexOf('.');
        if (dot > 0) return (xpoTableName.Substring(0, dot), xpoTableName.Substring(dot + 1));
        return (configuredSchema ?? EditDraftStoreOptions.DefaultSchema, xpoTableName);
    }
}

/// <summary>T-SQL helpers. The library supports SQL Server only.</summary>
public static class EditDraftSql
{
    /// <summary>A SQL Server delimited identifier: <c>[name]</c>, with every <c>]</c> doubled. A blank name is refused.</summary>
    public static string QuoteIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("An identifier cannot be blank.", nameof(name));
        return "[" + name.Replace("]", "]]") + "]";
    }
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
    /// <summary>
    /// Registers the host's store class (its own subclass of <see cref="EditDraftStoreBase"/>). <paramref name="configure"/>
    /// sets <see cref="EditDraftStoreOptions"/> (the schema of the store table; default "dbo"). SQL Server only.
    /// </summary>
    public static IServiceCollection AddEditDraftStore<TStore>(this IServiceCollection services, Action<EditDraftStoreOptions> configure = null) where TStore : EditDraftStoreBase
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        var options = new EditDraftStoreOptions();
        configure?.Invoke(options);
        services.AddSingleton(new EditDraftStoreRegistration(typeof(TStore), options));
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
