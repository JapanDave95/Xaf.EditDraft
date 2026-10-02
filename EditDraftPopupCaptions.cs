using System.Collections.Generic;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using DevExpress.ExpressApp.Model.NodeGenerators;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// The model captions of the five popup/list classes, from the text set in use (library milestone M3). The classes
/// declare the English captions as attributes; <see cref="EditDraftPopupClassCaptionUpdater"/> and
/// <see cref="EditDraftPopupMemberCaptionUpdater"/> (registered by <see cref="EditDraftBlazorModule"/>) write the set's
/// captions into the generated model layer, so a host that chose the Japanese set shows the Japanese captions and a host
/// model difference still wins (see <see cref="EditDraftModelCaptions"/>). The empty captions of the text lines
/// (Lead, Provenance, ConflictBanner) are not texts and are not listed.
/// </summary>
public static class EditDraftPopupCaptions
{
    public static IReadOnlyList<EditDraftModelCaption> All { get; } = new[]
    {
        new EditDraftModelCaption(typeof(EditDraftRestorePlan), null, t => t.CaptionRestorePlan),
        new EditDraftModelCaption(typeof(EditDraftRestorePlan), nameof(EditDraftRestorePlan.Items), t => t.CaptionRestorePlanItems),
        new EditDraftModelCaption(typeof(EditDraftReadOnlyView), null, t => t.CaptionReadOnlyView),
        new EditDraftModelCaption(typeof(EditDraftReadOnlyView), nameof(EditDraftReadOnlyView.Text), t => t.CaptionReadOnlyText),
        new EditDraftModelCaption(typeof(EditDraftRestoreItem), null, t => t.CaptionRestoreItem),
        new EditDraftModelCaption(typeof(EditDraftRestoreItem), nameof(EditDraftRestoreItem.Selected), t => t.CaptionItemSelected),
        new EditDraftModelCaption(typeof(EditDraftRestoreItem), nameof(EditDraftRestoreItem.Label), t => t.CaptionItemLabel),
        new EditDraftModelCaption(typeof(EditDraftRestoreItem), nameof(EditDraftRestoreItem.ChangeText), t => t.CaptionItemChange),
        new EditDraftModelCaption(typeof(EditDraftRestoreItem), nameof(EditDraftRestoreItem.CurrentText), t => t.CaptionItemCurrent),
        new EditDraftModelCaption(typeof(EditDraftRestoreItem), nameof(EditDraftRestoreItem.StatusText), t => t.CaptionItemStatus),
        new EditDraftModelCaption(typeof(EditDraftList), null, t => t.CaptionList),
        new EditDraftModelCaption(typeof(EditDraftList), nameof(EditDraftList.Items), t => t.CaptionListItems),
        new EditDraftModelCaption(typeof(EditDraftListItem), null, t => t.CaptionListItem),
        new EditDraftModelCaption(typeof(EditDraftListItem), nameof(EditDraftListItem.TypeCaption), t => t.CaptionListItemType),
        new EditDraftModelCaption(typeof(EditDraftListItem), nameof(EditDraftListItem.Target), t => t.CaptionListItemTarget),
        new EditDraftModelCaption(typeof(EditDraftListItem), nameof(EditDraftListItem.Origin), t => t.CaptionListItemOrigin),
        new EditDraftModelCaption(typeof(EditDraftListItem), nameof(EditDraftListItem.LastCapturedOn), t => t.CaptionListItemCapturedOn),
        new EditDraftModelCaption(typeof(EditDraftListItem), nameof(EditDraftListItem.EntryCount), t => t.CaptionListItemEntryCount),
        new EditDraftModelCaption(typeof(EditDraftListItem), nameof(EditDraftListItem.StateText), t => t.CaptionListItemState),
        new EditDraftModelCaption(typeof(EditDraftListItem), nameof(EditDraftListItem.ExpiresOn), t => t.CaptionListItemExpiresOn),
    };
}

/// <summary>Writes the popup/list class captions from the text set in use.</summary>
public sealed class EditDraftPopupClassCaptionUpdater : ModelNodesGeneratorUpdater<ModelBOModelClassNodesGenerator>
{
    public override void UpdateNode(ModelNode node)
    {
        if (node is IModelBOModel classes) EditDraftModelCaptions.ApplyToClasses(classes, EditDraftPopupCaptions.All);
    }
}

/// <summary>Writes the popup/list member captions from the text set in use.</summary>
public sealed class EditDraftPopupMemberCaptionUpdater : ModelNodesGeneratorUpdater<ModelBOModelMemberNodesGenerator>
{
    public override void UpdateNode(ModelNode node)
    {
        if (node is IModelBOModelClassMembers members && node.Parent is IModelClass owner)
            EditDraftModelCaptions.ApplyToMembers(owner.TypeInfo?.Type, members, EditDraftPopupCaptions.All);
    }
}
