using System.ComponentModel;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Updating;
using DevExpress.Persistent.BaseImpl;

namespace Xaf.EditDraft.Sample.Blazor.Server;

/// <summary>The DevExpress template's Blazor module (unchanged apart from names): user model differences are stored in the database.</summary>
[ToolboxItemFilter("Xaf.Platform.Blazor")]
public sealed class SampleBlazorModule : ModuleBase
{
    private void Application_CreateCustomUserModelDifferenceStore(object sender, CreateCustomModelDifferenceStoreEventArgs e)
    {
        e.Store = new ModelDifferenceDbStore((XafApplication)sender, typeof(ModelDifference), false, "Blazor");
        e.Handled = true;
    }

    public override IEnumerable<ModuleUpdater> GetModuleUpdaters(IObjectSpace objectSpace, Version versionFromDB) =>
        ModuleUpdater.EmptyModuleUpdaters;

    public override void Setup(XafApplication application)
    {
        base.Setup(application);
        application.CreateCustomUserModelDifferenceStore += Application_CreateCustomUserModelDifferenceStore;
    }
}
