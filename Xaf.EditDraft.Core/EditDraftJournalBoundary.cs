using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Xaf.EditDraft.Core;

/// <summary>
/// The journal's trust boundary (design Q5; SECURITY-RELEVANT: single-model by the owner's rule, owner review). Everything
/// a browser returns is untrusted. M1 uses only <see cref="OwnerToken"/> at run time (the descriptor's namespace); the
/// parsers are the shape checks the M3 intake runs before any value is used.
/// </summary>
public static class EditDraftJournalBoundary
{
    private const string TokenSalt = "Xaf.EditDraft.journal.owner.v1";
    private const int MaxIdLength = 256;
    private const int MaxReconstructionMembers = 32;

    /// <summary>
    /// The namespace of an owner's entries: hex SHA-256 of a fixed library string and the owner Oid, 32 digits. A filter,
    /// not a secret and not proof of anything: the intake recomputes it from the circuit's owner and never trusts a token
    /// from the browser. Null for no owner (no attributes and no intake without an owner, design Q5 rule 1).
    /// </summary>
    public static string OwnerToken(Guid ownerOid)
    {
        if (ownerOid == Guid.Empty) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(TokenSalt + "|" + ownerOid.ToString("N")));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    /// <summary>
    /// Parses a data-editdraft descriptor (or the descriptor part of a stored entry). False for anything that is not a
    /// complete version-1 descriptor: unknown version, a missing or over-long identifier, a separator in a key part, a
    /// kind outside the closed list, a generation below 1, a non-boolean copy flag, a non-string optional field, or a
    /// reconstruction map that is not a small string-to-string object. CopyOnly is recomputed, never taken from the input.
    /// </summary>
    public static bool TryParseDescriptor(string json, out EditDraftJournalDescriptor descriptor)
    {
        descriptor = null;
        if (string.IsNullOrEmpty(json) || json.Length > 65536) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return TryRead(doc.RootElement, "f", out descriptor);
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Parses one stored entry (its key and its stored text). False for any shape the module does not write.</summary>
    public static bool TryParseEntry(string key, string json, out EditDraftJournalEntry entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(key) || !key.StartsWith(EditDraftJournalRules.KeyPrefix, StringComparison.Ordinal) || key.Length > 2048) return false;
        if (string.IsNullOrEmpty(json) || json.Length > 200000) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            if (e.ValueKind != JsonValueKind.Object) return false;
            if (!Int(e, "f", out var format) || format != EditDraftJournalRules.FormatVersion) return false;
            if (!Long(e, "at", out var at) || at <= 0) return false;
            if (!Long(e, "seq", out var seq) || seq <= 0) return false;
            if (!Str(e, "load", out var load, true) || load.IndexOf('|') >= 0) return false;
            if (!Str(e, "val", out var value, false, EditDraftJournalRules.MaxValueChars)) return false;
            if (!Bool(e, "tr", out var truncated)) return false;
            var composing = e.TryGetProperty("comp", out var comp) && comp.ValueKind == JsonValueKind.True;
            if (e.TryGetProperty("comp", out comp) && comp.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            if (!TryRead(e, "fmt", out var d, versionFromEntry: true)) return false;
            var expected = EditDraftJournalRules.Key(d.Namespace, load, d.Context, d.Member, d.Generation);
            if (expected == null || key != expected + (composing ? EditDraftJournalRules.ComposingSuffix : string.Empty)) return false;
            entry = new EditDraftJournalEntry
            {
                Key = key, Descriptor = d, LoadId = load, Value = value, Truncated = truncated, Composing = composing, At = at, Sequence = seq
            };
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// Reads the descriptor fields. A data-editdraft attribute carries "v" and the format as "f"; a stored entry carries
    /// the format as "fmt" (its "f" is the entry format version), so the format property name is passed in.
    /// </summary>
    private static bool TryRead(JsonElement e, string formatProperty, out EditDraftJournalDescriptor descriptor, bool versionFromEntry = false)
    {
        descriptor = null;
        if (e.ValueKind != JsonValueKind.Object) return false;
        if (!versionFromEntry && (!Int(e, "v", out var v) || v != EditDraftJournalRules.FormatVersion)) return false;
        if (!Str(e, "ns", out var ns, true) || !Str(e, "p", out var p, true) || !Str(e, "t", out var t, true)
            || !Str(e, "ctx", out var ctx, true) || !Str(e, "w", out var w, true) || !Str(e, "m", out var m, true) || !Str(e, "k", out var k, true))
            return false;
        if (ns.IndexOf('|') >= 0 || ctx.IndexOf('|') >= 0 || m.IndexOf('|') >= 0) return false;
        if (!Guid.TryParseExact(ctx, "N", out _)) return false;
        if (!EditDraftJournalKinds.IsKnown(k)) return false;
        if (!Int(e, "g", out var g) || g < 1) return false;
        if (!Bool(e, "co", out _)) return false;
        if (!OptionalStr(e, "o", out var o) || !OptionalStr(e, formatProperty, out var f) || !OptionalStr(e, "bh", out var bh)
            || !OptionalStr(e, "cu", out var cu) || (cu != null && cu.Length > 64)) return false;
        if (o != null && !Guid.TryParse(o, out _)) return false;
        IReadOnlyDictionary<string, string> rc = null;
        if (e.TryGetProperty("rc", out var rcElement) && rcElement.ValueKind != JsonValueKind.Null)
        {
            if (rcElement.ValueKind != JsonValueKind.Object) return false;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var x in rcElement.EnumerateObject())
            {
                if (map.Count >= MaxReconstructionMembers || x.Name.Length == 0 || x.Name.Length > MaxIdLength) return false;
                if (x.Value.ValueKind == JsonValueKind.Null) map[x.Name] = null;
                else if (x.Value.ValueKind == JsonValueKind.String && x.Value.GetString().Length <= EditDraftJournalRules.MaxValueChars) map[x.Name] = x.Value.GetString();
                else return false;
            }
            rc = map;
        }
        descriptor = new EditDraftJournalDescriptor
        {
            Namespace = ns, PolicyId = p, TypeName = t, RecordOid = o, Context = ctx, ViewId = w, Member = m, Kind = k,
            Format = f, Culture = cu, CopyOnly = EditDraftJournalRules.IsCopyOnly(k, f, cu), BaselineHash = bh, Generation = g,
            Reconstruction = rc is { Count: > 0 } ? rc : null
        };
        return true;
    }

    private static bool Str(JsonElement e, string name, out string value, bool required, int max = MaxIdLength)
    {
        value = null;
        if (!e.TryGetProperty(name, out var x) || x.ValueKind != JsonValueKind.String) return false;
        value = x.GetString();
        if (value.Length > max) return false;
        return !required || value.Length > 0;
    }

    private static bool OptionalStr(JsonElement e, string name, out string value)
    {
        value = null;
        if (!e.TryGetProperty(name, out var x) || x.ValueKind == JsonValueKind.Null) return true;
        if (x.ValueKind != JsonValueKind.String) return false;
        value = x.GetString();
        return value.Length <= MaxIdLength;
    }

    private static bool Int(JsonElement e, string name, out int value)
    {
        value = 0;
        return e.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out value);
    }

    private static bool Long(JsonElement e, string name, out long value)
    {
        value = 0;
        return e.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt64(out value);
    }

    private static bool Bool(JsonElement e, string name, out bool value)
    {
        value = false;
        if (!e.TryGetProperty(name, out var x)) return false;
        if (x.ValueKind == JsonValueKind.True) { value = true; return true; }
        return x.ValueKind == JsonValueKind.False;
    }
}
