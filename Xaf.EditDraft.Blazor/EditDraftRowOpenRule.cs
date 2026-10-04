using System.Linq;
using DevExpress.ExpressApp.Blazor.Components;
using DevExpress.ExpressApp.Utils;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// The per-row rule for the inline 「入力控を開く」 icon in a grid row (library milestone M3, M4 finding b). XAF renders a
/// RecordEdit SimpleAction both as a toolbar button and as an icon in every data row; the row icon asks
/// <c>ListEditorInlineActionControl.CustomizeInlineActionButton</c> once per row (DevExpress.ExpressApp.Blazor 26.1:
/// InlineActionButton.OnParametersSetAsync passes the row as DataItem and takes Visible/Enabled back).
/// The icon shows only on a row in the badge set; the rule decides VISIBILITY only.
/// Owner ruling O-11 (M3 §12, "fail closed before NuGet"; applied 2026-10-04): the rule never turns an icon ON. XAF's
/// per-row pass also applies row-specific inputs the rule cannot see (the action's TargetObjectsCriteria, a
/// BoundItemCreating handler), so the icon keeps XAF's enabled state: with a row WITHOUT a draft selected, the icons on
/// badged rows are disabled like the toolbar button (as before M3). The toolbar button is a different control and keeps
/// the action's own state (enabled when a selected row has a draft).
/// </summary>
public static class EditDraftRowOpenRule
{
    /// <summary>
    /// Applies the rule to one row's button. <paramref name="badged"/>: the row's key is in the screen's badge set. An
    /// unbadged row's icon is hidden; a badged row's icon keeps the state XAF gave it. <paramref name="actionEnabled"/> and
    /// <paramref name="selectedRowReason"/> are no longer read (kept so the caller's signature is unchanged).
    /// </summary>
    public static void Apply(CustomizeInlineActionButtonEventArgs e, bool badged, BoolList actionEnabled, string selectedRowReason)
    {
        if (e == null) return;
        if (!badged) e.Visible = false;
    }

    /// <summary>True when <paramref name="selectedRowReason"/> is false and every other reason of the action is true.</summary>
    public static bool OnlySelectedRowReasonDisables(BoolList actionEnabled, string selectedRowReason)
    {
        if (actionEnabled == null || string.IsNullOrEmpty(selectedRowReason)) return false;
        var keys = actionEnabled.GetKeys().ToList();
        if (!keys.Contains(selectedRowReason) || actionEnabled[selectedRowReason]) return false;
        return keys.Where(k => k != selectedRowReason).All(k => actionEnabled[k]);
    }
}
