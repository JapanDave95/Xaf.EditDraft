using System;
using System.Collections.Generic;
using System.Linq;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using DevExpress.ExpressApp.Model.NodeGenerators;

namespace Xaf.EditDraft.Core;

/// <summary>
/// One model caption the library owns: a class caption (<see cref="Member"/> null) or a member caption. Its text comes
/// from the text set in use (<see cref="EditDraftTexts"/>), so the host's one choice of set covers the model captions too
/// (library milestone M3; owner decision O-4: English by default, the host chooses). With <see cref="AndDerived"/> the
/// caption also applies where the member appears on a class derived from <see cref="Type"/> (the store base's members on
/// the host's store class).
/// </summary>
public sealed record EditDraftModelCaption(Type Type, string Member, Func<EditDraftTextSet, string> Text, bool AndDerived = false)
{
    /// <summary>The caption in <paramref name="set"/>; the English one where that set leaves it null.</summary>
    public string In(EditDraftTextSet set) => (set == null ? null : Text(set)) ?? Text(EditDraftTextSet.English);

    /// <summary>The caption in the set in use.</summary>
    public string Current => In(EditDraftTexts.Current);

    /// <summary>True for <see cref="Type"/> itself and, with <see cref="AndDerived"/>, for a class derived from it.</summary>
    public bool AppliesTo(Type type) => type != null && Type != null && (type == Type || (AndDerived && Type.IsAssignableFrom(type)));
}

/// <summary>
/// Model captions from the text set (library milestone M3). The library's classes declare the English captions (the
/// default set) as attributes; a generator updater per module writes the caption of the set in use into the generated
/// model layer when the application model is built. Every model difference above that layer still wins: a host's own
/// Model.xafml or a user's stored differences (XAF 26.1, docs 404125: generator updaters work at the Application Model
/// zero layer). Neither the application model language nor the thread culture is consulted.
/// </summary>
public static class EditDraftModelCaptions
{
    /// <summary>The store base's member captions, applied on the host's store class (the base is non-persistent).</summary>
    public static IReadOnlyList<EditDraftModelCaption> Store { get; } = new[]
    {
        new EditDraftModelCaption(typeof(EditDraftStoreBase), nameof(EditDraftStoreBase.OwnerUserOid), t => t.CaptionStoreOwner, AndDerived: true),
        new EditDraftModelCaption(typeof(EditDraftStoreBase), nameof(EditDraftStoreBase.ObjectType), t => t.CaptionStoreObjectType, AndDerived: true),
        new EditDraftModelCaption(typeof(EditDraftStoreBase), nameof(EditDraftStoreBase.ContextText), t => t.CaptionStoreContext, AndDerived: true),
    };

    /// <summary>
    /// The caption <paramref name="captions"/> gives the member <paramref name="member"/> of <paramref name="type"/> (a null
    /// member: the class caption) in <paramref name="set"/>; null when no entry applies.
    /// </summary>
    public static string Find(IEnumerable<EditDraftModelCaption> captions, Type type, string member, EditDraftTextSet set) =>
        captions?.FirstOrDefault(c => c != null && c.Member == member && c.AppliesTo(type))?.In(set);

    /// <summary>Writes the class captions of <paramref name="captions"/> into the matching class nodes (set in use). Returns how many were written.</summary>
    public static int ApplyToClasses(IEnumerable<IModelClass> classes, IEnumerable<EditDraftModelCaption> captions)
    {
        var list = captions?.Where(c => c != null && c.Member == null).ToList();
        if (classes == null || list == null || list.Count == 0) return 0;
        var count = 0;
        foreach (var cls in classes)
        {
            var caption = list.FirstOrDefault(c => c.AppliesTo(cls?.TypeInfo?.Type));
            if (caption == null) continue;
            cls.Caption = caption.Current;
            count++;
        }
        return count;
    }

    /// <summary>Writes the member captions of <paramref name="captions"/> into the members of one class node (set in use). Returns how many were written.</summary>
    public static int ApplyToMembers(Type owner, IEnumerable<IModelMember> members, IEnumerable<EditDraftModelCaption> captions)
    {
        var list = captions?.Where(c => c != null && c.Member != null && c.AppliesTo(owner)).ToList();
        if (members == null || list == null || list.Count == 0) return 0;
        var count = 0;
        foreach (var member in members)
        {
            var caption = list.FirstOrDefault(c => c.Member == member?.Name);
            if (caption == null) continue;
            member.Caption = caption.Current;
            count++;
        }
        return count;
    }
}

/// <summary>Writes the store base's member captions from the text set in use (registered by <see cref="EditDraftCoreModule"/>).</summary>
public sealed class EditDraftStoreCaptionUpdater : ModelNodesGeneratorUpdater<ModelBOModelMemberNodesGenerator>
{
    public override void UpdateNode(ModelNode node)
    {
        if (node is IModelBOModelClassMembers members && node.Parent is IModelClass owner)
            EditDraftModelCaptions.ApplyToMembers(owner.TypeInfo?.Type, members, EditDraftModelCaptions.Store);
    }
}
