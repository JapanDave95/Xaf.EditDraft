using System;
using System.Globalization;
using DevExpress.ExpressApp.Utils;
using DevExpress.Persistent.BaseImpl;

namespace Xaf.EditDraft.Core;

/// <summary>How a member value reads in the popup and the list — taken ON THE CIRCUIT at capture time.</summary>
public static class EditDraftDisplay
{
    public static string TextOf(object value)
    {
        try
        {
            switch (value)
            {
                case null: return string.Empty;
                case string s: return s;
                case bool b: return b ? EditDraftTexts.Of(t => t.Yes) : EditDraftTexts.Of(t => t.No);
                case Enum e: return new EnumDescriptor(e.GetType()).GetCaption(e) ?? e.ToString();
                case DateTime d: return d == DateTime.MinValue ? string.Empty : d.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
                case TimeSpan ts: return ts.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
                case BaseObject bo: return CaptionHelper.GetDisplayText(bo) ?? string.Empty;
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }
        catch { return value?.ToString() ?? string.Empty; }
    }

    /// <summary>「60 → 61.5」 or 「(空) → 61.5」.</summary>
    public static string ChangeText(string before, string after, bool beforeKnown) =>
        (beforeKnown ? (string.IsNullOrEmpty(before) ? EditDraftTexts.Of(t => t.Empty) : before) : EditDraftTexts.Of(t => t.Unknown)) + " → " +
        (string.IsNullOrEmpty(after) ? EditDraftTexts.Of(t => t.Empty) : after);

    /// <summary>Long text is cut for a grid cell; the full value is still what is restored.</summary>
    public static string Short(string text, int max = 60) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text ?? string.Empty : text.Substring(0, max) + "…";
}
