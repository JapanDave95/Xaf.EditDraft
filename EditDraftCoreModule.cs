using System;
using System.Collections.Generic;
using DevExpress.ExpressApp;

namespace Xaf.EditDraft.Core;

/// <summary>
/// The XAF module of the platform-agnostic edit-draft (入力控) engine. Register it in the application's
/// module list; its controllers are collected from this assembly.
///
/// It exports NO business class: the store is the consumer's own one-line subclass of
/// <see cref="EditDraftStoreBase"/> (only one class may map to a table, XPO SameTableNameException), registered
/// with <c>services.AddEditDraftStore&lt;TStore&gt;()</c>. The store's controls stay the consumer's
/// obligation: an explicit DENY for every role, exclusion from the audit trail, and a retention sweep.
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
    }
}
