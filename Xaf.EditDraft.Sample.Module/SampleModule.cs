using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Updating;
using DevExpress.ExpressApp.Xpo;
using DevExpress.Persistent.BaseImpl;

namespace Xaf.EditDraft.Sample.Module;

/// <summary>
/// The sample's platform-agnostic module (DevExpress template shape). XAF collects its business classes from this
/// assembly: Note, the draft store SampleEditDraft and the template's ApplicationUser / ApplicationUserLoginInfo.
/// The two library modules (EditDraftCoreModule, EditDraftBlazorModule) are added in the Blazor application's
/// Startup, as the library asks; this module does not require them.
/// </summary>
public sealed class SampleModule : ModuleBase
{
    public SampleModule()
    {
        AdditionalExportedTypes.Add(typeof(ModelDifference));
        AdditionalExportedTypes.Add(typeof(ModelDifferenceAspect));
        RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.SystemModule.SystemModule));
        RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Security.SecurityModule));
    }

    public override IEnumerable<ModuleUpdater> GetModuleUpdaters(IObjectSpace objectSpace, Version versionFromDB) =>
        new ModuleUpdater[] { new DatabaseUpdate.Updater(objectSpace, versionFromDB) };

    public override void CustomizeTypesInfo(ITypesInfo typesInfo)
    {
        base.CustomizeTypesInfo(typesInfo);
        CalculatedPersistentAliasHelper.CustomizeTypesInfo(typesInfo);
    }
}
