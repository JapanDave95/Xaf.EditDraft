using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DevExpress.Xpo;

namespace Xaf.EditDraft.Core;

/// <summary>
/// "Does the store table exist?" answers, cached for <see cref="Lifetime"/> PER DATABASE (library design §4.12
/// rule 2: table availability belongs to the configured database, never to the process — two databases in one
/// process never share an answer). A probe that throws counts as absent (and is cached like any answer).
/// A call without a database identity is probed every time and never cached.
/// </summary>
public sealed class EditDraftTableCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private sealed class Entry
    {
        public DateTime CheckedAtUtc;
        public bool Exists;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _byConnection = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<object, Entry> _byLayer = new();

    /// <param name="databaseKey">A connection string, or the data-layer object bound to one database; null = unknown.</param>
    public bool Get(object databaseKey, DateTime utcNow, Func<bool> probe)
    {
        if (probe == null) throw new ArgumentNullException(nameof(probe));
        lock (_gate)
        {
            var entry = Find(databaseKey);
            if (entry != null && utcNow - entry.CheckedAtUtc < Lifetime) return entry.Exists;
            bool exists;
            try { exists = probe(); }
            catch { exists = false; }
            if (databaseKey != null)
            {
                entry ??= Add(databaseKey);
                entry.CheckedAtUtc = utcNow;
                entry.Exists = exists;
            }
            return exists;
        }
    }

    private Entry Find(object key) => key switch
    {
        null => null,
        string s => _byConnection.TryGetValue(s, out var e) ? e : null,
        _ => _byLayer.TryGetValue(key, out var e) ? e : null
    };

    private Entry Add(object key)
    {
        var e = new Entry();
        if (key is string s) _byConnection[s] = e;
        else _byLayer.AddOrUpdate(key, e);
        return e;
    }

    /// <summary>
    /// The database a session talks to: its connection string when the data layer exposes one, else the data
    /// layer itself (one data layer is bound to one database), else null. The key is held in memory only, never logged.
    /// </summary>
    public static object DatabaseKeyOf(Session session)
    {
        var layer = session?.DataLayer;
        if (layer == null) return null;
        try
        {
            var connectionString = layer.Connection?.ConnectionString;
            if (!string.IsNullOrEmpty(connectionString)) return connectionString;
        }
        catch { /* a layer without an ADO.NET connection: keyed by the layer */ }
        return layer;
    }
}
