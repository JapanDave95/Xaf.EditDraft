using System;
using System.Collections.Generic;
using System.Globalization;
using DevExpress.ExpressApp.Blazor.Components.Models;
using DevExpress.ExpressApp.Blazor.Editors;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// A read-only, caption-style text editor for the popups' text lines (Lead, Provenance, ConflictBanner) — library
/// milestone M3, owner ruling O-7 "Build a small library label editor". It shows the value as plain text in a div with
/// the form-layout caption classes: no input, no box or border, no resize grip, not focusable; the text wraps and a line
/// break in the value starts a new line. Nothing is ever written back: the value is display-only.
/// Registered for any member type under its own alias (<see cref="Alias"/>); a member opts in with
/// <c>[EditorAlias(EditDraftLabelEditor.Alias)]</c>. Written for this library (DevExpress BlazorPropertyEditorBase).
/// </summary>
[PropertyEditor(typeof(object), Alias, false)]
public sealed class EditDraftLabelEditor : BlazorPropertyEditorBase
{
    /// <summary>The editor alias. Distinct from every alias a host may already register.</summary>
    public const string Alias = "Xaf.EditDraft.Label";

    public EditDraftLabelEditor(Type objectType, IModelMemberViewItem model) : base(objectType, model) { }

    public override EditDraftLabelModel ComponentModel => (EditDraftLabelModel)base.ComponentModel;

    protected override IComponentModel CreateComponentModel() => new EditDraftLabelModel();

    protected override void ReadValueCore()
    {
        base.ReadValueCore();
        if (ComponentModel != null) ComponentModel.Text = EditDraftLabel.TextOf(PropertyValue, Model?.DisplayFormat);
    }

    /// <summary>Display only: the control never holds a value of its own, so the member keeps what it has.</summary>
    protected override object GetControlValueCore() => PropertyValue;
}

/// <summary>The component model of <see cref="EditDraftLabelEditor"/>: the text to show.</summary>
public sealed class EditDraftLabelModel : ComponentModelBase
{
    public string Text
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    public override Type ComponentType => typeof(EditDraftLabel);
}

/// <summary>
/// Renders one text line: <c>&lt;div class="xaf-editdraft-label dxbl-fl-cpt dxbl-text" style="white-space: pre-line; …"&gt;text&lt;/div&gt;</c>.
/// The text is a text node (Blazor encodes it), never markup.
/// </summary>
public sealed class EditDraftLabel : ComponentBase
{
    /// <summary>The library class first, then the DevExpress form-layout caption and text classes (the caption look).</summary>
    public const string CssClass = "xaf-editdraft-label dxbl-fl-cpt dxbl-text";

    /// <summary>pre-line: line breaks in the value start a new line, long lines wrap, runs of spaces collapse.</summary>
    public const string Style = "white-space: pre-line; overflow-wrap: anywhere; padding-left: 4px; padding-right: 4px;";

    [Parameter] public string Text { get; set; }

    /// <summary>Attributes a host or XAF puts on the component model (title, aria-*); rendered on the div before the library's class and style.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object> AdditionalAttributes { get; set; }

    /// <summary>The text shown for a value: empty for null, the string itself for a string (CR LF and CR become LF), otherwise the value formatted with the member's display format.</summary>
    public static string TextOf(object value, string displayFormat)
    {
        if (value == null) return string.Empty;
        var text = value as string
                   ?? (string.IsNullOrEmpty(displayFormat)
                       ? Convert.ToString(value, CultureInfo.CurrentCulture)
                       : string.Format(CultureInfo.CurrentCulture, displayFormat, value));
        return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        if (AdditionalAttributes != null) builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "class", CssClass);
        builder.AddAttribute(3, "style", Style);
        builder.AddContent(4, TextOf(Text, null));   // CR LF / CR -> LF here too, whoever sets Text
        builder.CloseElement();
    }
}
