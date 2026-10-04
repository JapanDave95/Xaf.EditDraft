using System;
using System.Collections.Generic;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model.Core;

namespace Xaf.EditDraft.Core;

/// <summary>
/// The XAF module of the platform-agnostic edit-draft (入力控) engine. Register it in the application's
/// module list; its controllers are collected from this assembly.
///
/// It exports NO business class: the store is the consumer's own one-line subclass of
/// <see cref="EditDraftStoreBase"/> (only one class may map to a table, XPO SameTableNameException), registered
/// with <c>services.AddEditDraftStore&lt;TStore&gt;()</c>. The store's controls stay the consumer's
/// obligation: an explicit DENY for every role (<see cref="EditDraftSecurity.DenyStoreToAllRoles"/> in the ModuleUpdater),
/// exclusion from the audit trail, and a retention sweep (<see cref="EditDraftRetention"/>). See docs/consumer-guide.md.
///
/// Gaps G1/G2/G6 (2026-10-04): when the application's setup completes, the module runs the startup checks
/// (<see cref="EditDraftStartup.Run"/>): a registered store, a policy while capture is on, a SQL Server database, the
/// optional table check; a problem stops the application with a message naming the fix. It also logs, once, each role
/// that can read the store through XAF security.
/// </summary>
public sealed class EditDraftCoreModule : ModuleBase
{
    public EditDraftCoreModule()
    {
        RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.SystemModule.SystemModule));
    }

    /// <summary>Nothing is exported: the non-persistent base reaches XAF through the consumer's store subclass.</summary>
    protected override IEnumerable<Type> GetDeclaredExportedTypes() => Type.EmptyTypes;

    public override void Setup(XafApplication application)
    {
        base.Setup(application);
        // Logging default: the application's ILogger, unless the host chose a sink (EditDraftLog.Sink).
        EditDraftLog.UseLoggerIfNoHostSink(application?.ServiceProvider);
        if (application != null) application.SetupComplete += OnSetupComplete;
    }

    private static void OnSetupComplete(object sender, EventArgs e)
    {
        var application = (XafApplication)sender;
        application.SetupComplete -= OnSetupComplete;
        EditDraftStartup.Run(application);
    }

    /// <summary>Milestone M3: the store base's member captions come from the text set in use (<see cref="EditDraftModelCaptions"/>).</summary>
    public override void AddGeneratorUpdaters(ModelNodesGeneratorUpdaters updaters)
    {
        base.AddGeneratorUpdaters(updaters);
        updaters.Add(new EditDraftStoreCaptionUpdater());
    }
}
