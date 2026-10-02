using System.Linq;
using DevExpress.ExpressApp.Blazor.Components;
using DevExpress.ExpressApp.Utils;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// The per-row rule for the inline 「入力控を開く」 icon in a grid row (library milestone M3, M4 finding b). XAF renders a
/// RecordEdit SimpleAction both as a toolbar button and as an icon in every data row; the row icon asks
/// <c>ListEditorInlineActionControl.CustomizeInlineActionButton</c> once per row (DevExpress.ExpressApp.Blazor 26.1:
/// InlineActionButton.OnParametersSetAsync passes the row as DataItem and takes Visible/Enabled back).
/// The icon shows only on a row in the badge set. On such a row the action's "the selected row has a draft" reason
/// does not apply — clicking the icon selects that row first — so the icon stays usable when another row is selected,
/// unless any other reason disables the action. The toolbar button is a different control and keeps the action's own
/// state (enabled when a selected row has a draft).
/// </summary>
public static class EditDraftRowOpenRule
{
    /// <summary>Applies the rule to one row's button. <paramref name="badged"/>: the row's key is in the screen's badge set.</summary>
    public static void Apply(CustomizeInlineActionButtonEventArgs e, bool badged, BoolList actionEnabled, string selectedRowReason)
    {
        if (e == null) return;
        if (!badged) { e.Visible = false; return; }
        if (!e.Enabled && OnlySelectedRowReasonDisables(actionEnabled, selectedRowReason)) e.Enabled = true;
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
