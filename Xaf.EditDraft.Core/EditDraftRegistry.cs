using System;
using System.Collections.Generic;
using System.Linq;

namespace Xaf.EditDraft.Core;

/// <summary>
/// The opt-in list of the generic edit-draft engine (design §1.3, owner D3): a type that is not
/// registered here is never captured and never restored (fail closed). Lookups are by the EXACT type
/// or its CLR class name; nothing is inherited.
///
/// Library (owner decision D4, milestone M1): an INSTANCE the host composes and registers as a DI singleton
/// (<see cref="EditDraftServiceCollectionExtensions.AddEditDraftRegistry"/>), frozen before use; there is no
/// static default registry. Registered types must have distinct CLR short names (the payload keys on Type.Name).
/// </summary>
public sealed class EditDraftRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, EditDraftTypePolicy> _byType = new();
    private readonly Dictionary<string, EditDraftTypePolicy> _byName = new(StringComparer.Ordinal);
    private bool _frozen;

    /// <summary>A frozen registry with no policy: what a host that registered none gets — nothing is admitted.</summary>
    public static EditDraftRegistry Empty { get; } = Create(_ => { });

    /// <summary>A new registry with <paramref name="register"/> applied, then frozen (the DI singleton's content).</summary>
    public static EditDraftRegistry Create(Action<EditDraftRegistry> register)
    {
        if (register == null) throw new ArgumentNullException(nameof(register));
        var registry = new EditDraftRegistry();
        register(registry);
        registry.Freeze();
        return registry;
    }

    /// <summary>True once <see cref="Freeze"/> ran: no policy can be added any more.</summary>
    public bool IsFrozen { get { lock (_gate) return _frozen; } }

    /// <summary>Ends registration (the host composes at startup; lookups never see a changing list).</summary>
    public void Freeze() { lock (_gate) _frozen = true; }

    public void Register(EditDraftTypePolicy policy)
    {
        if (policy == null) throw new ArgumentNullException(nameof(policy));
        lock (_gate)
        {
            if (_frozen)
                throw new InvalidOperationException($"The edit-draft registry is frozen; {policy.TypeName} cannot be registered after startup.");
            if (_byType.ContainsKey(policy.Type) || _byName.ContainsKey(policy.TypeName))
                throw new InvalidOperationException($"An edit-draft policy for {policy.TypeName} is already registered.");
            _byType[policy.Type] = policy;
            _byName[policy.TypeName] = policy;
        }
    }

    public EditDraftTypePolicy Find(Type type)
    {
        if (type == null) return null;
        lock (_gate) return _byType.TryGetValue(type, out var p) ? p : null;
    }

    public EditDraftTypePolicy Find(string typeName)
    {
        if (typeName == null) return null;
        lock (_gate) return _byName.TryGetValue(typeName, out var p) ? p : null;
    }

    public IReadOnlyList<EditDraftTypePolicy> All
    {
        get { lock (_gate) return _byType.Values.OrderBy(p => p.TypeName, StringComparer.Ordinal).ToList(); }
    }
}
