using System;
using System.Globalization;
using DevExpress.Persistent.BaseImpl;

namespace Xaf.EditDraft.Core;

/// <summary>Invariant text for a member value; the same text both ways, so comparison is exact.</summary>
public static class EditDraftCodec
{
    public static string RawOf(object value)
    {
        switch (value)
        {
            case null: return null;
            case string s: return s;
            case bool b: return b ? "true" : "false";
            case Enum e: return Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            case DateTime d: return d.ToString("o", CultureInfo.InvariantCulture);
            case TimeSpan ts: return ts.ToString("c", CultureInfo.InvariantCulture);
            case Guid g: return g.ToString("D");
            case BaseObject bo: return bo.Oid.ToString("D");
            case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
            default: return value.ToString();
        }
    }

    /// <summary>Raw text back to a value of <paramref name="type"/>. References are resolved by the caller (they are Oids here).</summary>
    public static object Parse(string raw, Type type)
    {
        var u = Nullable.GetUnderlyingType(type) ?? type;
        var nullable = Nullable.GetUnderlyingType(type) != null || !type.IsValueType;
        if (raw == null) return nullable ? null : Activator.CreateInstance(u);
        if (u == typeof(string)) return raw;
        if (u == typeof(bool)) return raw == "true";
        if (u.IsEnum) return Enum.ToObject(u, long.Parse(raw, CultureInfo.InvariantCulture));
        if (u == typeof(DateTime)) return DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (u == typeof(TimeSpan)) return TimeSpan.ParseExact(raw, "c", CultureInfo.InvariantCulture);
        if (u == typeof(Guid)) return Guid.Parse(raw);
        return Convert.ChangeType(raw, u, CultureInfo.InvariantCulture);
    }
}
