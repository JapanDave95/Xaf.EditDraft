using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Xaf.EditDraft.Core;

/// <summary>One captured member: the value at first capture (baseline) and the latest value.</summary>
public sealed class EditDraftEntry
{
    [JsonProperty("p")] public string Path { get; set; }
    [JsonProperty("k")] public string Kind { get; set; }
    [JsonProperty("cap")] public string Caption { get; set; }

    /// <summary>False when the value before the edit is not known (a NEW record, or a draft seeded without one).</summary>
    [JsonProperty("bk")] public bool BaseKnown { get; set; }
    [JsonProperty("br")] public string BaseRaw { get; set; }
    [JsonProperty("bt")] public string BaseText { get; set; }
    [JsonProperty("vr")] public string ValueRaw { get; set; }
    [JsonProperty("vt")] public string ValueText { get; set; }

    /// <summary>True for a reconstruction-context value seeded at first capture rather than typed.</summary>
    [JsonProperty("ctx")] public bool Seeded { get; set; }
}

/// <summary>
/// The payload of an edit draft (入力控). Pure. The baseline of an entry is fixed at the FIRST capture
/// of that member; later edits move only the value (the same rule as the attendance store — a
/// drifting baseline would make every conflict test answer "clean").
/// Every serialised property is declared HERE, in this order, so a derived payload writes the same text.
/// </summary>
public class EditDraftPayload
{
    [JsonProperty("schema")] public int Schema { get; set; } = 1;

    /// <summary>CLR class name of the drafted record's type; resolved through <see cref="EditDraftRegistry"/>.</summary>
    [JsonProperty("type")] public string TypeName { get; set; }
    [JsonProperty("entries")] public List<EditDraftEntry> Entries { get; set; } = new();

    /// <summary>
    /// NEW records (design 2026-10-02 §4.1; owner D2: the payload stays version 1 with this OPTIONAL header): the Oids of every
    /// screen object the draft was typed on or recreated into, NEWEST FIRST — the original object and each recreated one —
    /// so that 開く can tell a record that was in fact saved (D11). Written only for a never-saved record's draft; null
    /// everywhere else, and a null header is not serialised, so every other payload's text is unchanged and a reader
    /// without it reads the payload as before. Wire format: "prov":["&lt;guid&gt;", …].
    /// </summary>
    [JsonProperty("prov", NullValueHandling = NullValueHandling.Ignore)] public List<Guid> Provisional { get; set; }

    [JsonIgnore] public int Count => Entries?.Count ?? 0;

    /// <summary>The Oid history of <see cref="Provisional"/>, newest first; empty when the payload has no header.</summary>
    [JsonIgnore] public IReadOnlyList<Guid> ProvisionalOids => Provisional ?? (IReadOnlyList<Guid>)Array.Empty<Guid>();

    /// <summary>Puts <paramref name="oid"/> at the head of the Oid history, once; an Oid already at the head is left as it is. Guid.Empty is ignored.</summary>
    public void AddProvisional(Guid oid)
    {
        if (oid == Guid.Empty) return;
        Provisional ??= new List<Guid>();
        if (Provisional.Count > 0 && Provisional[0] == oid) return;
        Provisional.Remove(oid);
        Provisional.Insert(0, oid);
    }

    public EditDraftEntry Get(string path) => Entries.FirstOrDefault(e => e.Path == path);

    /// <summary>Records a member. First call fixes the baseline; later calls move only the value.</summary>
    public void Upsert(string path, string kind, string caption, bool baseKnown, string baseRaw, string baseText,
                       string valueRaw, string valueText, bool seeded = false)
    {
        if (string.IsNullOrEmpty(path)) return;
        var e = Get(path);
        if (e == null)
        {
            e = new EditDraftEntry
            {
                Path = path, Kind = kind, Caption = caption,
                BaseKnown = baseKnown, BaseRaw = baseRaw, BaseText = baseText, Seeded = seeded
            };
            Entries.Add(e);
        }
        else if (e.Seeded && !seeded)
        {
            e.Seeded = false;   // typed after being seeded: now a real edit
        }
        e.ValueRaw = valueRaw;
        e.ValueText = valueText;
    }

    public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None);

    /// <summary>Null for anything unreadable: a malformed draft is not offered, never half-restored.</summary>
    public static T FromJson<T>(string json) where T : EditDraftPayload
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var p = JsonConvert.DeserializeObject<T>(json);
            if (p == null) return null;
            p.Entries ??= new List<EditDraftEntry>();
            p.Entries.RemoveAll(e => e == null || string.IsNullOrEmpty(e.Path));
            return p;
        }
        catch (JsonException) { return null; }
    }
}

public enum EditDraftItemStatus
{
    /// <summary>Current value already equals the drafted value: nothing to do.</summary>
    AlreadyApplied,
    /// <summary>Current value still equals the baseline: safe to restore (pre-ticked).</summary>
    Clean,
    /// <summary>Current value differs from both: 「他で変更されています」, unticked (owner Q6).</summary>
    Conflict,
    /// <summary>No known baseline: unticked.</summary>
    Unverifiable,
    /// <summary>The record or the referenced object is gone or not readable: not applicable.</summary>
    Unavailable,
    /// <summary>A NEW record: nothing to compare with.</summary>
    New
}

/// <summary>The three-way comparison (design §4.4). Exact text comparison of codec raws; null is not "".</summary>
public static class EditDraftComparison
{
    public static EditDraftItemStatus Classify(bool targetExists, bool baseKnown, string baseRaw, string valueRaw, string currentRaw)
    {
        if (!targetExists) return EditDraftItemStatus.Unavailable;
        if (string.Equals(currentRaw, valueRaw, StringComparison.Ordinal)) return EditDraftItemStatus.AlreadyApplied;
        if (!baseKnown) return EditDraftItemStatus.Unverifiable;
        return string.Equals(currentRaw, baseRaw, StringComparison.Ordinal)
            ? EditDraftItemStatus.Clean
            : EditDraftItemStatus.Conflict;
    }

    public static bool IsSelectable(EditDraftItemStatus s) =>
        s == EditDraftItemStatus.Clean || s == EditDraftItemStatus.Conflict
        || s == EditDraftItemStatus.Unverifiable || s == EditDraftItemStatus.New;

    /// <summary>Pre-ticked only when safe: clean (or NEW), and never a member that changes another record.</summary>
    public static bool IsPreSelected(EditDraftItemStatus s, bool hasSideEffect) =>
        !hasSideEffect && (s == EditDraftItemStatus.Clean || s == EditDraftItemStatus.New);

    public static string StatusText(EditDraftItemStatus s, bool hasSideEffect)
    {
        var t = s switch
        {
            EditDraftItemStatus.AlreadyApplied => EditDraftTexts.Of(x => x.StatusAlreadyApplied),
            EditDraftItemStatus.Clean => EditDraftTexts.Of(x => x.StatusClean),
            EditDraftItemStatus.Conflict => EditDraftTexts.Of(x => x.StatusConflict),
            EditDraftItemStatus.Unverifiable => EditDraftTexts.Of(x => x.StatusUnverifiable),
            EditDraftItemStatus.Unavailable => EditDraftTexts.Of(x => x.StatusUnavailable),
            EditDraftItemStatus.New => EditDraftTexts.Of(x => x.StatusNew),
            _ => ""
        };
        return hasSideEffect && IsSelectable(s) ? t + EditDraftTexts.Of(x => x.SideEffectSuffix) : t;
    }

    /// <summary>
    /// Group rule (design §4.4): members moved by one setter share one tick. A conflict or an
    /// unverifiable member anywhere in the group unticks the whole group, and a group whose driver
    /// changes another record is never pre-ticked.
    /// </summary>
    public static bool GroupPreSelected(IEnumerable<(EditDraftItemStatus Status, bool SideEffect)> members)
    {
        var list = members.ToList();
        if (list.Count == 0) return false;
        if (list.Any(m => m.SideEffect)) return false;
        if (list.Any(m => m.Status == EditDraftItemStatus.Conflict || m.Status == EditDraftItemStatus.Unverifiable)) return false;
        return list.Any(m => m.Status == EditDraftItemStatus.Clean || m.Status == EditDraftItemStatus.New);
    }
}
