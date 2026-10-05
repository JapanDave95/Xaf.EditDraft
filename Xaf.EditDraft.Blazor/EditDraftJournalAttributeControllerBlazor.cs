using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor.Components.Models;
using DevExpress.ExpressApp.Blazor.Editors;
using DevExpress.Persistent.BaseImpl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// Client-side input journal, phase 2 milestone M1 (docs/edit-draft-client-journal-design-2026-10-03.md Q1;
/// docs/edit-draft-client-journal-m0-2026-10-03.md section 5 and section 9). Puts a server-built descriptor on the root
/// element of each journaled editor of an admitted DetailView through XAF's documented hook
/// (View.CustomizeViewItemControl + ComponentModelBase.SetAttribute("data-editdraft", ...)) and imports the RCL module
/// _content/Xaf.EditDraft.Blazor/edit-draft-journal.js through IJSRuntime, which journals only those editors.
///
/// ADMISSION (all re-checked at every apply): the screen's capture controller admits the record (generic policy, root,
/// approved DetailView, a never-saved record only when the policy allows new records); EditDraftCapture:Journal:Enabled
/// AND the policy's type key are true (and EditDraftCapture:NewRecords:Enabled for a never-saved record); an owner
/// (a login without one gets no attribute, design Q5 rule 1). DetailViews only (owner U2/U4).
/// MEMBERS: the policy's members of journaled kinds (string, TimeSpan, DateTime only when the policy lists it in
/// JournalTimeOfDayMembers) whose component model the library knows (text box, memo, editable string combo, masked input,
/// time edit). Other component models get no attribute (no guess from a descendant input; rich text is excluded).
/// GENERATION: per editor, starting at 1; +1 when its control is created again, and for every editor after a record change
/// or a save. The module keys entries by (owner token, page load, context, member, generation).
/// No capture, no write, no intake here: the browser holds the entries (M3 adds the intake).
/// </summary>
public sealed class EditDraftJournalAttributeControllerBlazor : ViewController<DetailView>
{
    public const string AttributeName = "data-editdraft";
    public const string ModulePath = "./_content/Xaf.EditDraft.Blazor/edit-draft-journal.js";

    private readonly Dictionary<BlazorPropertyEditorBase, int> _generation = new();
    private readonly Dictionary<BlazorPropertyEditorBase, (string Raw, bool Known)> _baseline = new();
    private readonly HashSet<BlazorPropertyEditorBase> _attributed = new();
    private readonly Dictionary<string, string> _admitted = new(StringComparer.Ordinal);   // member -> kind, for the coverage log
    private readonly Dictionary<string, string> _unsupported = new(StringComparer.Ordinal);   // member -> component model type, not journaled
    private SynchronizationContext _circuit;
    private CancellationTokenSource _lifetime;
    private string _lastContext;
    private bool _moduleRequested;
    private bool _reportPending;          // a coverage report is scheduled and has not run yet (M1b D9)
    private IJSObjectReference _module;

    protected override void OnActivated()
    {
        base.OnActivated();
        _generation.Clear();
        _baseline.Clear();
        _attributed.Clear();
        _admitted.Clear();
        _unsupported.Clear();
        _moduleRequested = false;
        _reportPending = false;
        _module = null;
        _lastContext = null;
        _circuit = SynchronizationContext.Current;
        _lifetime = new CancellationTokenSource();
        View.CustomizeViewItemControl<BlazorPropertyEditorBase>(this, Customize);
        View.CurrentObjectChanged += View_CurrentObjectChanged;
        ObjectSpace.Committed += ObjectSpace_Committed;
        ObjectSpace.ObjectChanged += ObjectSpace_ObjectChanged;
    }

    protected override void OnDeactivated()
    {
        View.CurrentObjectChanged -= View_CurrentObjectChanged;
        if (ObjectSpace != null)
        {
            ObjectSpace.Committed -= ObjectSpace_Committed;
            ObjectSpace.ObjectChanged -= ObjectSpace_ObjectChanged;
        }
        try { _lifetime?.Cancel(); } catch { }
        _lifetime = null;
        base.OnDeactivated();
    }

    // ---------------------------------------------------------------------------------------------------- attribute

    /// <summary>XAF calls this for each property editor control, now and whenever a control is created again.</summary>
    private void Customize(BlazorPropertyEditorBase editor)
    {
        _generation[editor] = _generation.TryGetValue(editor, out var g) ? g + 1 : 1;
        _baseline.Remove(editor);
        Apply(editor);
        // A control created after the first coverage line is reported without waiting for a save or record change (M1b D9).
        var lifetime = _lifetime;
        if (lifetime != null && !lifetime.IsCancellationRequested && EditDraftJournalCoverageRules.ReportForLaterControl(_module != null, _reportPending))
        {
            _reportPending = true;
            _ = ReportCoverageAsync(_module, lifetime.Token);
        }
    }

    private void View_CurrentObjectChanged(object sender, EventArgs e)
    {
        // A different record: the attributes go NOW, in the same render as the new record's values, so the browser never
        // reads a value the server set under the previous record's descriptor. The new descriptors follow after the
        // current handlers (the capture controller renews its editing context on this same event).
        foreach (var editor in _attributed.ToList())
            (editor.ComponentModel as ComponentModelBase)?.RemoveAttribute(AttributeName);
        _attributed.Clear();
        if (_circuit == null) NextGeneration();
        else _circuit.Post(_ => { if (View != null) NextGeneration(); }, null);
    }

    private void ObjectSpace_Committed(object sender, EventArgs e)
    {
        // After a save the capture takes a new baseline in its own Committed handler: apply after the current handlers.
        if (_circuit == null) NextGeneration();
        else _circuit.Post(_ => { if (View != null) NextGeneration(); }, null);
    }

    private void ObjectSpace_ObjectChanged(object sender, ObjectChangedEventArgs e)
    {
        // A never-saved record's reconstruction members travel with every descriptor (M0 section 5 item 10): refresh the
        // attributes, same generation, same baseline, when one of them changes.
        var record = View?.CurrentObject;
        if (record == null || !ReferenceEquals(e.Object, record) || string.IsNullOrEmpty(e.PropertyName)) return;
        var policy = Frame?.GetController<EditDraftCaptureController>()?.Policy;
        if (policy == null || !policy.NewRecordReconstructionOrder.Contains(e.PropertyName)) return;
        if (ObjectSpace == null || !ObjectSpace.IsNewObject(record)) return;
        foreach (var editor in _attributed.ToList()) Apply(editor);
    }

    private void NextGeneration()
    {
        // A callback posted before deactivation does nothing afterwards (OnDeactivated drops the lifetime).
        var lifetime = _lifetime;
        if (lifetime == null || lifetime.IsCancellationRequested || View == null) return;
        foreach (var editor in View.GetItems<BlazorPropertyEditorBase>())
        {
            if (editor.Control == null) continue;
            _generation[editor] = _generation.TryGetValue(editor, out var g) ? g + 1 : 1;
            _baseline.Remove(editor);
            Apply(editor);
        }
        if (_module != null) _ = ReportCoverageAsync(_module, lifetime.Token);   // the editors of the new record or generation (Codex a1 C12)
    }

    private void Apply(BlazorPropertyEditorBase editor)
    {
        if (editor?.ComponentModel is not ComponentModelBase model) return;
        string json = null;
        try { json = Describe(editor)?.ToJson(); }
        catch (Exception ex) { EditDraftLog.Warning($"[EditDraft] journal descriptor not built for {editor.PropertyName}: {ex.GetType().Name}"); }
        if (json == null)
        {
            if (_attributed.Remove(editor)) model.RemoveAttribute(AttributeName);
        }
        else
        {
            model.SetAttribute(AttributeName, json);
            _attributed.Add(editor);
        }
        // Started for the first admitted editor, attributed OR unsupported: an unsupported-only view still logs its
        // coverage line with unsupported=[...] (M1b D9).
        if (EditDraftJournalCoverageRules.StartModule(_moduleRequested, _attributed.Count, _unsupported.Count))
        {
            _moduleRequested = true;
            _ = StartModuleAsync(_lifetime?.Token ?? CancellationToken.None);
        }
    }

    /// <summary>The descriptor of one editor, or null when it gets no attribute (see the class summary).</summary>
    private EditDraftJournalDescriptor Describe(BlazorPropertyEditorBase editor)
    {
        var services = Application?.ServiceProvider;
        var capture = Frame?.GetController<EditDraftCaptureController>();
        var record = View?.CurrentObject;
        var policy = capture?.Policy;
        if (record == null || capture == null || !capture.IsAdmitted || policy == null || ObjectSpace == null) return null;
        var isNew = ObjectSpace.IsNewObject(record);
        if (!EditDraftCaptureController.IsAdmittedViewIncludingNew(policy, View.Id, View.IsRoot, isNew)) return null;
        if (!EditDraftSwitch.IsJournalEnabled(services, policy.PolicyId, isNew)) return null;
        var owner = EditDraftServices.CurrentOwner(services, ObjectSpace);
        if (owner.IsNone) return null;
        var spec = policy.Find(editor.PropertyName);
        if (spec == null || !EditDraftJournalRules.IsJournaledMemberKind(spec.Kind, policy.JournalTimeOfDayMembers.Contains(spec.Path))) return null;
        // The editing context of the coverage line, also when no editor of the view is supported (M1b D9).
        var context = capture.CurrentEditorInstanceId;
        _lastContext = context.ToString("N");
        var (kind, format) = KindOf(editor.ComponentModel, spec.Kind);
        if (kind == null)
        {
            // Named in the coverage line (M0 section 5 item 5c; Codex a1 C12): a journaled member whose component the library does not know.
            _admitted.Remove(spec.Path);
            _unsupported[spec.Path] = editor.ComponentModel?.GetType().Name ?? "none";
            return null;
        }
        _unsupported.Remove(spec.Path);
        _admitted[spec.Path] = kind;

        // The baseline is read once per generation, so a refresh of the reconstruction raws keeps it (design S11).
        if (!_baseline.TryGetValue(editor, out var baseline))
        {
            var known = capture.TryGetBaselineRaw(spec.Path, out var raw);
            baseline = (raw, known);
            _baseline[editor] = baseline;
        }
        IReadOnlyDictionary<string, string> reconstruction = isNew ? ReconstructionRaws(policy, record) : null;
        // The circuit's culture: the editor formats and parses its text with it (Codex a1 C11).
        return EditDraftJournalDescriptor.Build(EditDraftJournalBoundary.OwnerToken(owner.Oid), policy, RecordOid(record), context,
            View.Id, spec.Path, kind, format, baseline.Raw, baseline.Known, _generation.TryGetValue(editor, out var g) ? g : 1, reconstruction,
            System.Globalization.CultureInfo.CurrentCulture.Name);
    }

    private string RecordOid(object record)
    {
        if (record is BaseObject bo) return bo.Oid.ToString("D");
        try { return ObjectSpace.GetKeyValueAsString(record); } catch { return null; }
    }

    /// <summary>The canonical raws of the policy's NewRecordReconstructionOrder members the policy admits (never-saved records).</summary>
    public static IReadOnlyDictionary<string, string> ReconstructionRaws(EditDraftTypePolicy policy, object record)
    {
        if (policy == null || record == null || policy.NewRecordReconstructionOrder.Count == 0) return null;
        var raws = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in policy.NewRecordReconstructionOrder)
        {
            if (policy.Find(path) == null) continue;
            try { raws[path] = EditDraftCodec.RawOf(EditDraftMembers.GetValue(record, path)); }
            catch { }
        }
        return raws.Count == 0 ? null : raws;
    }

    /// <summary>
    /// The journal kind and effective format of an editor's component model, or (null, null) when the library does not
    /// journal it: text box, memo, editable string combo, masked input and time edit only. A time edit's format is its Mask
    /// when one is set (the mask governs editing, DevExpress.Blazor MaskedInputModelBase; Codex a1 C11), otherwise its
    /// Format (XAF sets it from the member's EditMask); a masked input's is its Mask.
    /// </summary>
    public static (string Kind, string Format) KindOf(object componentModel, string memberKind)
    {
        switch (memberKind)
        {
            case "string":
                return componentModel switch
                {
                    DxMemoModel => (EditDraftJournalKinds.Memo, null),
                    DxTextBoxModel => (EditDraftJournalKinds.Text, null),
                    DxComboBoxModel => (EditDraftJournalKinds.Combo, null),
                    DxMaskedInputModel masked => (EditDraftJournalKinds.Masked, NullIfEmpty(masked.Mask)),
                    _ => (null, null)
                };
            case "timespan":
            case "datetime":
                return componentModel switch
                {
                    DxTimeEditModel time => (EditDraftJournalKinds.Time, NullIfEmpty(time.Mask) ?? NullIfEmpty(time.Format)),
                    DxMaskedInputModel masked => (EditDraftJournalKinds.Masked, NullIfEmpty(masked.Mask)),
                    _ => (null, null)
                };
            default:
                return (null, null);
        }
    }

    private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    // ---------------------------------------------------------------------------------------------------- module

    private async Task StartModuleAsync(CancellationToken cancel)
    {
        var js = Application?.ServiceProvider?.GetService<IJSRuntime>();
        if (js == null) return;
        var viewId = View?.Id;
        try
        {
            var module = await js.InvokeAsync<IJSObjectReference>("import", cancel, ModulePath);
            var platform = await module.InvokeAsync<JsonElement>("start", cancel);
            EditDraftLog.Info($"[EditDraft] journal module started view={viewId} platform={platform.GetRawText()}");
            _module = module;
            _reportPending = true;        // the first coverage report follows below
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            // Prerendering, a closed circuit or a blocked module: the editors keep working; nothing is journaled.
            EditDraftLog.Warning($"[EditDraft] journal module not started view={viewId}: {ex.GetType().Name}");
            return;
        }
        await ReportCoverageAsync(_module, cancel);
    }

    /// <summary>
    /// Logs the coverage line about 1.5 s after the editors were (re)attributed; after the first one, once per record change
    /// or save, and for a control created later (one pending report at a time for those: M1b D9).
    /// </summary>
    private async Task ReportCoverageAsync(IJSObjectReference module, CancellationToken cancel)
    {
        var viewId = View?.Id;
        try
        {
            await Task.Delay(1500, cancel);
            var found = await module.InvokeAsync<JsonElement>("coverage", cancel);
            var context = _lastContext;
            if (View != null && context != null && !cancel.IsCancellationRequested)
                EditDraftLog.Info("[EditDraft] journal coverage " + CoverageLine(viewId, context, _admitted, found.GetRawText(), _unsupported));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { EditDraftLog.Warning($"[EditDraft] journal coverage not read view={viewId}: {ex.GetType().Name}"); }
        finally { _reportPending = false; }
    }

    /// <summary>
    /// The coverage log line (M0 section 5 item 5): members this view admitted versus attributed roots found in the DOM
    /// for the same view and editing context (inactive MDI tabs keep their editors, so other groups are counted apart),
    /// members with no field, and admitted members not rendered (a component that does not pass the attribute on).
    /// Member names and kinds only; never values.
    /// </summary>
    public static string CoverageLine(string viewId, string context, IReadOnlyDictionary<string, string> admitted, string coverageJson) =>
        CoverageLine(viewId, context, admitted, coverageJson, null);

    /// <summary>As the four-argument overload, plus the journaled members whose component model the library does not know (no attribute).</summary>
    public static string CoverageLine(string viewId, string context, IReadOnlyDictionary<string, string> admitted, string coverageJson,
                                      IReadOnlyDictionary<string, string> unsupported)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var noField = new List<string>();
        var otherGroups = 0;
        var invalid = 0;
        try
        {
            using var doc = JsonDocument.Parse(coverageJson ?? "{}");
            if (doc.RootElement.TryGetProperty("invalid", out var inv) && inv.TryGetInt32(out var n)) invalid = n;
            if (doc.RootElement.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
                foreach (var group in groups.EnumerateArray())
                {
                    var w = group.TryGetProperty("w", out var wp) ? wp.GetString() : null;
                    var ctx = group.TryGetProperty("ctx", out var cp) ? cp.GetString() : null;
                    if (w != viewId || ctx != context) { otherGroups++; continue; }
                    if (!group.TryGetProperty("members", out var members) || members.ValueKind != JsonValueKind.Array) continue;
                    foreach (var m in members.EnumerateArray())
                    {
                        var name = m.TryGetProperty("m", out var mp) ? mp.GetString() : null;
                        if (name == null) continue;
                        found.Add(name);
                        if (m.TryGetProperty("field", out var fp) && fp.ValueKind == JsonValueKind.False) noField.Add(name);
                    }
                }
        }
        catch (JsonException) { return $"view={viewId} unreadable coverage report"; }
        var admittedNames = (admitted ?? new Dictionary<string, string>()).Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var missing = admittedNames.Where(x => !found.Contains(x)).ToList();
        var line = $"view={viewId} context={(context?.Length > 8 ? context.Substring(0, 8) : context)} admitted={admittedNames.Count} found={found.Count} " +
                   $"notRendered=[{string.Join(",", missing)}] noField=[{string.Join(",", noField)}] otherGroups={otherGroups} invalid={invalid}";
        if (unsupported == null) return line;
        return line + $" unsupported=[{string.Join(",", unsupported.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "(" + x.Value + ")"))}]";
    }
}

/// <summary>
/// When the journal attribute controller starts the browser module and logs a coverage line (M1b cluster A; Codex M1
/// diffreview a2 D9). Pure, so the decisions run in NUnit; the XAF lifecycle that calls them is pinned by source tests.
/// </summary>
public static class EditDraftJournalCoverageRules
{
    /// <summary>
    /// The module starts once per view activation, for the first admitted journaled editor, attributed OR unsupported: a view
    /// whose only eligible editors are unsupported still logs its coverage line with unsupported=[...].
    /// </summary>
    public static bool StartModule(bool alreadyRequested, int attributed, int unsupported) =>
        !alreadyRequested && (attributed > 0 || unsupported > 0);

    /// <summary>A control created after the module started is reported when it appears; one pending report at a time.</summary>
    public static bool ReportForLaterControl(bool moduleStarted, bool reportPending) => moduleStarted && !reportPending;
}
