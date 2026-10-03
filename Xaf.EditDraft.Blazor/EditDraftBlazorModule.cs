using System;
using System.Collections.Generic;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model.Core;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// The XAF module of the Blazor part of the edit-draft (入力控) library (milestone M2): the restore popup, the
/// 入力控 list, the ListView capture and the row badges. Requires <see cref="EditDraftCoreModule"/> (the engine and
/// the DetailView capture). Its controllers are collected from this assembly; it exports the five non-persistent
/// popup/list classes and nothing persistent.
///
/// A host registers it after the Core module (<c>.Add&lt;EditDraftBlazorModule&gt;()</c>), calls
/// <see cref="EditDraftBlazorServiceCollectionExtensions.AddEditDraftBlazor"/> for the per-circuit services, and
/// links the row-badge stylesheet <c>_content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css</c> in its host page.
///
/// Milestone M3: the module's assembly also holds the read-only label editor of the popups' text lines
/// (<see cref="EditDraftLabelEditor"/>; XAF registers [PropertyEditor] classes from each module's assembly) and the
/// generator updaters that write the popup/list captions of the text set in use.
/// </summary>
public sealed class EditDraftBlazorModule : ModuleBase
{
    /// <summary>The static web asset path of the row-badge stylesheet (a host page links it).</summary>
    public const string RowBadgeStylesheet = "_content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css";

    public EditDraftBlazorModule()
    {
        RequiredModuleTypes.Add(typeof(EditDraftCoreModule));
        RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Blazor.SystemModule.SystemBlazorModule));
        RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.ConditionalAppearance.ConditionalAppearanceModule));
    }

    /// <summary>The popup and list classes (non-persistent). Nothing maps to a table.</summary>
    protected override IEnumerable<Type> GetDeclaredExportedTypes() => new[]
    {
        typeof(EditDraftRestorePlan), typeof(EditDraftRestoreItem), typeof(EditDraftReadOnlyView),
        typeof(EditDraftList), typeof(EditDraftListItem)
    };

    /// <summary>Milestone M3: the popup/list class and member captions come from the text set in use (<see cref="EditDraftPopupCaptions"/>).</summary>
    public override void AddGeneratorUpdaters(ModelNodesGeneratorUpdaters updaters)
    {
        base.AddGeneratorUpdaters(updaters);
        updaters.Add(new EditDraftPopupClassCaptionUpdater());
        updaters.Add(new EditDraftPopupMemberCaptionUpdater());
    }
}
