using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xaf.EditDraft.Core;

// Client-side input journal, phase 2 milestone M1 (docs/edit-draft-client-journal-design-2026-10-03.md Q1-Q4;
// docs/edit-draft-client-journal-m0-2026-10-03.md section 5 and section 9). The browser module
// (Xaf.EditDraft.Blazor/wwwroot/edit-draft-journal.js) applies the same rules; the constants here and there must agree
// (pinned by the tests on both sides). Nothing in this file reads a database, a request or a configuration key.

/// <summary>The editor kinds the client journal knows (a closed list; anything else is not journaled).</summary>
public static class EditDraftJournalKinds
{
    /// <summary>DxTextBox: the exact text.</summary>
    public const string Text = "text";
    /// <summary>DxMemo: the exact text.</summary>
    public const string Memo = "memo";
    /// <summary>Editable string combo (a string member with predefined values): the exact text.</summary>
    public const string Combo = "combo";
    /// <summary>DxMaskedInput of a string member with an edit mask: the displayed text; copy-only in v1 (conversion not proven).</summary>
    public const string Masked = "masked";
    /// <summary>DxTimeEdit: the displayed text, converted only with the editor's effective format.</summary>
    public const string Time = "time";
    /// <summary>Reserved for a component model the library does not know. Never put on an editor in M1 (no guess from a descendant input).</summary>
    public const string Custom = "custom";

    public static readonly IReadOnlyList<string> All = new[] { Text, Memo, Combo, Masked, Time, Custom };

    public static bool IsKnown(string kind) => kind != null && All.Contains(kind, StringComparer.Ordinal);
}

/// <summary>The journal's limits and pure rules.</summary>
public static class EditDraftJournalRules
{
    public const int FormatVersion = 1;
    /// <summary>Every entry key starts with this. The format version is part of it, so another format never shares keys (F6 D6).</summary>
    public const string KeyPrefix = "XafEditDraft.j1|";
    /// <summary>Appended to an entry key for the incomplete text of an open composition.</summary>
    public const string ComposingSuffix = "|c";
    /// <summary>
    /// Owner decision 9: at most 60 journal keys per page load (all its namespaces; incomplete copies included). Per page
    /// load since the owner ruling of 2026-10-04 (per-tab clears): a page load evicts only its own keys.
    /// </summary>
    public const int MaxEntries = 60;
    /// <summary>Owner decision 9: values up to 12,000 characters (UTF-16 units); a longer value is stored truncated and flagged.</summary>
    public const int MaxValueChars = 12000;
    /// <summary>Owner decision 11: an entry 60 minutes old or older is expired.</summary>
    public const int RetentionMinutes = 60;
    /// <summary>
    /// Serialized budget: key length + serialized entry length, summed over the journal keys of one page load (localStorage
    /// counts characters of keys and values). 1,048,576 = 20% of the 5,242,880 measured in M0 (G4); per page load by the
    /// owner ruling of 2026-10-04, so several page loads together can hold more.
    /// </summary>
    public const int MaxSerializedChars = 1048576;
    /// <summary>The post-blur window of one masked edit attempt (the JS interop default timeout, M0 section 5 item 1).</summary>
    public const int PostBlurWindowMs = 60000;

    /// <summary>Member kinds (EditDraftMembers.KindOf) whose typed text is journaled; "datetime" only when the policy opts the member in.</summary>
    public static bool IsJournaledMemberKind(string memberKind, bool timeOfDayOptIn) =>
        memberKind == "string" || memberKind == "timespan" || (memberKind == "datetime" && timeOfDayOptIn);

    /// <summary>The browser key of one entry; null when a part is missing or contains the separator.</summary>
    public static string Key(string ns, string loadId, string context, string member, int generation)
    {
        foreach (var part in new[] { ns, loadId, context, member })
            if (string.IsNullOrEmpty(part) || part.IndexOf('|') >= 0) return null;
        if (generation < 1) return null;
        return KeyPrefix + string.Join("|", ns, loadId, context, member, generation.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// True for a 12-hour pattern without an AM/PM designator: "h" outside quoted or escaped text and no "t". Such text
    /// cannot be converted unambiguously (M0 C8, dxdocs Blazor 26.1 402515: in a date-time mask "hh" is 01-12).
    /// </summary>
    public static bool IsTwelveHour(string format)
    {
        if (string.IsNullOrEmpty(format)) return false;
        var hasH = false;
        var hasT = false;
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c == '\\') { i++; continue; }
            if (c == '\'' || c == '"')
            {
                var close = format.IndexOf(c, i + 1);
                if (close < 0) break;
                i = close;
                continue;
            }
            if (c == 'h') hasH = true;
            if (c == 't') hasT = true;
        }
        return hasH && !hasT;
    }

    /// <summary>
    /// Whether an editor's text may only be offered as a copy, never converted (M0 section 5 item 8): text, memo and
    /// combo never; a time editor when its effective format is unknown or 12-hour without a designator; masked and
    /// custom always in v1.
    /// </summary>
    public static bool IsCopyOnly(string kind, string format) => IsCopyOnly(kind, format, null);

    /// <summary>As <see cref="IsCopyOnly(string, string)"/>; a one-letter standard format is expanded with <paramref name="culture"/> first (Codex a1 C11).</summary>
    public static bool IsCopyOnly(string kind, string format, string culture) => kind switch
    {
        EditDraftJournalKinds.Text or EditDraftJournalKinds.Memo or EditDraftJournalKinds.Combo => false,
        EditDraftJournalKinds.Time => string.IsNullOrWhiteSpace(format) || IsTwelveHour(ExpandFormat(format, culture)),
        _ => true
    };

    /// <summary>
    /// The culture named by a descriptor; the invariant culture when none or an unknown one is named. Predefined cultures
    /// only: with ICU, GetCultureInfo(name) accepts any well-formed name.
    /// </summary>
    public static CultureInfo CultureOf(string name)
    {
        if (string.IsNullOrEmpty(name)) return CultureInfo.InvariantCulture;
        try { return CultureInfo.GetCultureInfo(name, predefinedOnly: true); } catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    /// <summary>A one-letter standard date-time format ("t", "T") as the culture's custom pattern; any other format unchanged.</summary>
    public static string ExpandFormat(string format, string culture)
    {
        if (format == null || format.Length != 1) return format;
        try { return CultureOf(culture).DateTimeFormat.GetAllDateTimePatterns(format[0]).FirstOrDefault() ?? format; }
        catch (FormatException) { return format; }
    }

    /// <summary>
    /// The smallest time unit a custom pattern shows, in ticks: fractions ("f"/"F", by count), seconds, minutes, hours.
    /// Quoted and escaped text is skipped.
    /// </summary>
    public static long PrecisionTicks(string pattern)
    {
        var fractions = 0; var seconds = false; var minutes = false;
        if (pattern != null)
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                if (c == '\\') { i++; continue; }
                if (c == '\'' || c == '"') { var close = pattern.IndexOf(c, i + 1); if (close < 0) break; i = close; continue; }
                if (c == 'f' || c == 'F')
                {
                    var run = 1;
                    while (i + 1 < pattern.Length && pattern[i + 1] == c) { run++; i++; }
                    fractions = Math.Max(fractions, Math.Min(run, 7));
                }
                else if (c == 's') seconds = true;
                else if (c == 'm') minutes = true;
            }
        if (fractions > 0) return TimeSpan.TicksPerSecond / (long)Math.Pow(10, fractions);
        if (seconds) return TimeSpan.TicksPerSecond;
        return minutes ? TimeSpan.TicksPerMinute : TimeSpan.TicksPerHour;
    }

    /// <summary>
    /// Fingerprint of a canonical baseline raw (EditDraftCodec text): the first 16 hex digits of SHA-256 over a marker
    /// and the text. Null and "" differ (EditDraftCodec keeps them apart).
    /// </summary>
    public static string BaselineHash(string raw)
    {
        var input = raw == null ? "\u0000null" : "\u0001" + raw;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>Expired when 60 minutes old or older (times in milliseconds since 1970, as the browser stores them).</summary>
    public static bool IsExpired(long atMs, long nowMs) => nowMs - atMs >= RetentionMinutes * 60_000L;

    /// <summary>Oldest first; equal times by key (ordinal), so every tab evicts the same entry (F6 D1).</summary>
    public static IEnumerable<EditDraftJournalStoredItem> EvictionOrder(IEnumerable<EditDraftJournalStoredItem> items) =>
        items.OrderBy(x => x.At).ThenBy(x => x.Key, StringComparer.Ordinal);

    /// <summary>
    /// The keys to remove after <paramref name="writtenKey"/> was written with <paramref name="writtenSize"/> characters
    /// (key + serialized entry): the oldest items first (At, then Key) until the count and the serialized total fit. The
    /// written entry competes like any other, as in the browser module's planEviction (M1b-D C2, M1b-E G4): when it is
    /// among <paramref name="existing"/> it takes its At from its item and <paramref name="writtenSize"/> as its size, and
    /// it is chosen when it is the oldest. A written key that is not among them has no known time: it is counted and never
    /// chosen.
    /// </summary>
    public static IReadOnlyList<string> PlanEviction(IEnumerable<EditDraftJournalStoredItem> existing, string writtenKey, int writtenSize,
                                                     int maxEntries = MaxEntries, int maxSerializedChars = MaxSerializedChars)
    {
        var all = (existing ?? Enumerable.Empty<EditDraftJournalStoredItem>()).ToList();
        var listed = all.FirstOrDefault(x => x.Key == writtenKey);
        var candidates = all.Where(x => x.Key != writtenKey).ToList();
        if (listed != null) candidates.Add(listed with { Size = writtenSize });
        var others = EvictionOrder(candidates).ToList();
        var count = others.Count + (listed != null ? 0 : 1);
        var total = others.Sum(x => (long)x.Size) + (listed != null ? 0 : writtenSize);
        var evict = new List<string>();
        foreach (var x in others)
        {
            if (count <= maxEntries && total <= maxSerializedChars) break;
            evict.Add(x.Key);
            count--;
            total -= x.Size;
        }
        return evict;
    }
}

/// <summary>One journal key in browser storage: its key, its time (ms since 1970) and its size (key + value characters).</summary>
public sealed record EditDraftJournalStoredItem(string Key, long At, int Size);

/// <summary>
/// The descriptor the server puts on a journaled editor's root element as data-editdraft (design Q1). Built on the
/// circuit; persisted with every browser entry, so a recovery never needs the circuit that built it. New versus existing
/// is decided at intake by a fresh read, never by a flag here (design S12).
/// </summary>
public sealed record EditDraftJournalDescriptor
{
    public int Version { get; init; } = EditDraftJournalRules.FormatVersion;
    /// <summary>The owner token: a filter, not a secret (built by EditDraftJournalBoundary.OwnerToken).</summary>
    public string Namespace { get; init; }
    public string PolicyId { get; init; }
    public string TypeName { get; init; }
    /// <summary>The record's Oid; for a never-saved record the screen object's Oid (the payload's provisional Oid).</summary>
    public string RecordOid { get; init; }
    /// <summary>The capture's CurrentEditorInstanceId ("N" format).</summary>
    public string Context { get; init; }
    public string ViewId { get; init; }
    public string Member { get; init; }
    public string Kind { get; init; }
    /// <summary>The editor's effective format (null = none or unknown).</summary>
    public string Format { get; init; }
    /// <summary>The circuit's culture when the descriptor was built (the editor formats and parses with it); null = invariant.</summary>
    public string Culture { get; init; }
    public bool CopyOnly { get; init; }
    /// <summary>Fingerprint of the capture's canonical baseline raw of this member (null = baseline not known).</summary>
    public string BaselineHash { get; init; }
    public int Generation { get; init; }
    /// <summary>Never-saved records only: the canonical raws of the policy's NewRecordReconstructionOrder members (M0 section 5 item 10).</summary>
    public IReadOnlyDictionary<string, string> Reconstruction { get; init; }

    /// <summary>
    /// Builds a descriptor, or null when it would not be a valid journal identity: no namespace, policy, context or
    /// member, a member the policy does not capture, an unknown kind, a key part with the separator, generation below 1.
    /// CopyOnly and the baseline fingerprint are derived here, never passed in.
    /// </summary>
    public static EditDraftJournalDescriptor Build(string ns, EditDraftTypePolicy policy, string recordOid, Guid context, string viewId,
                                                   string member, string kind, string format, string baselineRaw, bool baselineKnown,
                                                   int generation, IReadOnlyDictionary<string, string> reconstruction = null, string culture = null)
    {
        if (policy == null || string.IsNullOrEmpty(policy.PolicyId) || context == Guid.Empty || string.IsNullOrEmpty(viewId)) return null;
        if (string.IsNullOrEmpty(member) || policy.Find(member) == null || !EditDraftJournalKinds.IsKnown(kind)) return null;
        var ctx = context.ToString("N");
        if (EditDraftJournalRules.Key(ns, "x", ctx, member, generation) == null) return null;
        return new EditDraftJournalDescriptor
        {
            Namespace = ns,
            PolicyId = policy.PolicyId,
            TypeName = policy.TypeName,
            RecordOid = recordOid,
            Context = ctx,
            ViewId = viewId,
            Member = member,
            Kind = kind,
            Format = string.IsNullOrEmpty(format) ? null : format,
            Culture = string.IsNullOrEmpty(culture) ? null : culture,
            CopyOnly = EditDraftJournalRules.IsCopyOnly(kind, format, culture),
            BaselineHash = baselineKnown ? EditDraftJournalRules.BaselineHash(baselineRaw) : null,
            Generation = generation,
            Reconstruction = reconstruction is { Count: > 0 } ? reconstruction : null
        };
    }

    /// <summary>The attribute text (short property names, as the browser module reads them).</summary>
    public string ToJson() => JsonSerializer.Serialize(EditDraftJournalWire.From(this), EditDraftJournalWire.Options);
}

/// <summary>The wire shape of a descriptor (data-editdraft) and of the descriptor part of a stored entry.</summary>
internal sealed class EditDraftJournalWire
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [JsonPropertyName("v")] public int V { get; set; }
    [JsonPropertyName("ns")] public string Ns { get; set; }
    [JsonPropertyName("p")] public string P { get; set; }
    [JsonPropertyName("t")] public string T { get; set; }
    [JsonPropertyName("o")] public string O { get; set; }
    [JsonPropertyName("ctx")] public string Ctx { get; set; }
    [JsonPropertyName("w")] public string W { get; set; }
    [JsonPropertyName("m")] public string M { get; set; }
    [JsonPropertyName("k")] public string K { get; set; }
    [JsonPropertyName("f")] public string F { get; set; }
    [JsonPropertyName("cu")] public string Cu { get; set; }
    [JsonPropertyName("co")] public bool Co { get; set; }
    [JsonPropertyName("bh")] public string Bh { get; set; }
    [JsonPropertyName("g")] public int G { get; set; }
    [JsonPropertyName("rc")] public Dictionary<string, string> Rc { get; set; }

    internal static EditDraftJournalWire From(EditDraftJournalDescriptor d) => new()
    {
        V = d.Version, Ns = d.Namespace, P = d.PolicyId, T = d.TypeName, O = d.RecordOid, Ctx = d.Context, W = d.ViewId,
        M = d.Member, K = d.Kind, F = d.Format, Cu = d.Culture, Co = d.CopyOnly, Bh = d.BaselineHash, G = d.Generation,
        Rc = d.Reconstruction == null ? null : new Dictionary<string, string>(d.Reconstruction, StringComparer.Ordinal)
    };
}

/// <summary>
/// One browser journal entry as the module stores it (key = KeyPrefix + ns | load | ctx | member | generation). Read at
/// intake (M3) through EditDraftJournalBoundary.TryParseEntry; everything in it came from the browser and is untrusted.
/// </summary>
public sealed record EditDraftJournalEntry
{
    public string Key { get; init; }
    public EditDraftJournalDescriptor Descriptor { get; init; }
    /// <summary>The page load that wrote it.</summary>
    public string LoadId { get; init; }
    /// <summary>The text shown in the editor (at most MaxValueChars).</summary>
    public string Value { get; init; }
    public bool Truncated { get; init; }
    /// <summary>The incomplete text of an open composition: never a complete typed value.</summary>
    public bool Composing { get; init; }
    /// <summary>
    /// Capture time: when the user action produced the value (not when it was written; a retry keeps it), milliseconds
    /// since 1970 (browser clock). Expiry and ordering count from it.
    /// </summary>
    public long At { get; init; }
    /// <summary>The writer's sequence number; a retirement echo must name it.</summary>
    public long Sequence { get; init; }
}

/// <summary>What browser text becomes for a member (design F13/F21; M0 section 5 item 8).</summary>
public enum EditDraftJournalOutcome
{
    /// <summary>A canonical raw (EditDraftCodec text) of the member type.</summary>
    Converted,
    /// <summary>The field was emptied; the canonical value of a clear (null or "") is decided at reconciliation (M2).</summary>
    Clear,
    /// <summary>A time of day typed into a DateTime member; the date comes from the member's own setter rule (M2).</summary>
    TimeOfDay,
    /// <summary>Kept as text only, never converted (copy-only editor, truncated, incomplete, ambiguous).</summary>
    CopyOnly,
    /// <summary>The text does not match the editor's effective format or the member type.</summary>
    Rejected
}

public sealed record EditDraftJournalConversion(EditDraftJournalOutcome Outcome, string Raw, string Reason);

/// <summary>Conversion of browser text to canonical values, ONLY with the editor's effective format (never a host default).</summary>
public static class EditDraftJournalConvert
{
    public static EditDraftJournalConversion ToCanonical(EditDraftJournalDescriptor descriptor, string text, Type valueType,
                                                         bool truncated = false, bool composing = false,
                                                         string baselineRaw = null, bool baselineKnown = false)
    {
        if (descriptor == null) return Rejected("no descriptor");
        if (composing) return CopyOnly("incomplete composition");
        if (truncated) return CopyOnly("truncated");
        if (descriptor.CopyOnly || EditDraftJournalRules.IsCopyOnly(descriptor.Kind, descriptor.Format, descriptor.Culture)) return CopyOnly("copy-only editor");
        if (text == null || valueType == null) return Rejected("no text or member type");
        var u = Nullable.GetUnderlyingType(valueType) ?? valueType;
        switch (descriptor.Kind)
        {
            case EditDraftJournalKinds.Text:
            case EditDraftJournalKinds.Memo:
            case EditDraftJournalKinds.Combo:
                if (u != typeof(string)) return Rejected("not a string member");
                return text.Length == 0 ? new EditDraftJournalConversion(EditDraftJournalOutcome.Clear, null, null)
                                        : new EditDraftJournalConversion(EditDraftJournalOutcome.Converted, text, null);
            case EditDraftJournalKinds.Time:
                return Time(descriptor.Format, descriptor.Culture, text, u, baselineRaw, baselineKnown);
            default:
                return CopyOnly("kind not converted in v1");
        }
    }

    /// <summary>
    /// Parses with the descriptor's format AND culture (the circuit's, Codex a1 C11); a one-letter standard format is
    /// expanded with that culture. Hidden precision (M0 section 5 item 8): the editor may keep seconds or fractions the text
    /// does not show; converted only when the baseline is known to have none below the shown unit. Without a known
    /// baseline every format stays copy-only (M1b R-A5; Codex M1 diffreview a2 D8).
    /// </summary>
    private static EditDraftJournalConversion Time(string format, string cultureName, string text, Type u, string baselineRaw, bool baselineKnown)
    {
        if (u != typeof(TimeSpan) && u != typeof(DateTime)) return Rejected("not a time member");
        if (text.Trim().Length == 0) return new EditDraftJournalConversion(EditDraftJournalOutcome.Clear, null, null);
        var culture = EditDraftJournalRules.CultureOf(cultureName);
        if (!DateTime.TryParseExact(text, format, culture, DateTimeStyles.NoCurrentDateDefault, out var parsed))
            return Rejected("does not match the editor format");
        var time = parsed.TimeOfDay;
        var unit = EditDraftJournalRules.PrecisionTicks(EditDraftJournalRules.ExpandFormat(format, cultureName));
        if (!baselineKnown) return CopyOnly(unit >= TimeSpan.TicksPerMinute ? "hidden seconds, baseline unknown" : "baseline unknown");
        if (unit > 1)
        {
            var rest = RemainderOf(baselineRaw, u, unit);
            if (rest != 0) return CopyOnly(unit >= TimeSpan.TicksPerMinute ? "hidden seconds" : "hidden fractions");
        }
        var raw = EditDraftCodec.RawOf(time);
        return u == typeof(DateTime)
            ? new EditDraftJournalConversion(EditDraftJournalOutcome.TimeOfDay, raw, null)
            : new EditDraftJournalConversion(EditDraftJournalOutcome.Converted, raw, null);
    }

    /// <summary>The baseline's time below <paramref name="unit"/> in ticks (0 for a null baseline: a new value has none; -1 unreadable).</summary>
    private static long RemainderOf(string raw, Type u, long unit)
    {
        if (raw == null) return 0;
        try
        {
            var ts = u == typeof(DateTime) ? ((DateTime)EditDraftCodec.Parse(raw, typeof(DateTime))).TimeOfDay : (TimeSpan)EditDraftCodec.Parse(raw, typeof(TimeSpan));
            return ts.Ticks % unit;
        }
        catch { return -1; }
    }

    private static EditDraftJournalConversion CopyOnly(string why) => new(EditDraftJournalOutcome.CopyOnly, null, why);
    private static EditDraftJournalConversion Rejected(string why) => new(EditDraftJournalOutcome.Rejected, null, why);
}

/// <summary>The three-way classification of a journal value (STUB for M2).</summary>
public enum EditDraftJournalClass
{
    /// <summary>The record already holds the journal value.</summary>
    AlreadyApplied,
    /// <summary>The record still holds the baseline the journal entry started from; the journal value differs.</summary>
    Clean,
    /// <summary>The record changed since the baseline and holds neither the baseline nor the journal value.</summary>
    Conflict,
    /// <summary>The descriptor carried no baseline fingerprint.</summary>
    BaselineUnknown,
    /// <summary>The journal text has no canonical value (copy-only, rejected, or a time of day still to be dated).</summary>
    NotConvertible
}

/// <summary>
/// STUB for M2 (design Q3 "reconcile before classifying", S6): classifies one converted journal value against the
/// record's current canonical raw and the descriptor's baseline fingerprint. M2 adds the same-context server draft merge
/// BEFORE this classification; M1 has no intake and calls nothing here at run time.
/// </summary>
public static class EditDraftJournalReconcile
{
    public static EditDraftJournalClass Classify(string baselineHash, string currentRaw, EditDraftJournalConversion journal)
    {
        if (journal == null || journal.Outcome is EditDraftJournalOutcome.CopyOnly or EditDraftJournalOutcome.Rejected or EditDraftJournalOutcome.TimeOfDay)
            return EditDraftJournalClass.NotConvertible;
        var applied = journal.Outcome == EditDraftJournalOutcome.Clear ? string.IsNullOrEmpty(currentRaw) : journal.Raw == currentRaw;
        if (applied) return EditDraftJournalClass.AlreadyApplied;
        if (baselineHash == null) return EditDraftJournalClass.BaselineUnknown;
        return baselineHash == EditDraftJournalRules.BaselineHash(currentRaw) ? EditDraftJournalClass.Clean : EditDraftJournalClass.Conflict;
    }
}
