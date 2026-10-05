using System;
using System.Collections.Generic;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// 0.4.0-preview.1 — SINGLE-MODEL (owner review). XAF permissions on a NEW (uncommitted) object, evaluated on the object's own
/// values, the way XAF evaluates a new object when it saves it (DevExpress 26.1.4, XPO integrated security:
/// SecurityRule2.IsGrantedCore sends a ServerPermissionRequest on the object with an expression evaluator, uncached). The
/// public PermissionRequest path cannot do this: PermissionRequestProcessorWrapper.IsGranted drops a new target object and
/// answers at type level. Used by XafSecurityEditDraftAccessCheck.MayRecreate (Core) through
/// EditDraftServices.MayRecreate; registered by AddEditDraftBlazor. Only XAF's integrated <see cref="SecurityStrategy"/>
/// evaluates such requests; any other security is "cannot be decided" (refused).
/// </summary>
internal sealed class EditDraftNewObjectPermissions : IEditDraftNewObjectPermissions
{
    public bool IsGranted(ISecurityStrategyBase security, IObjectSpace objectSpace, Type type, object record, IReadOnlyList<string> operations)
    {
        if (security is not SecurityStrategy strategy || objectSpace == null || type == null || record == null || operations == null || operations.Count == 0)
            return false;
        var evaluator = new SecurityExpressionEvaluator(objectSpace, strategy);
        foreach (var operation in operations)
        {
            var request = new NoCacheablePermissionRequest(new ServerPermissionRequest(type, record, null, operation, evaluator));
            if (!strategy.IsGranted(request)) return false;
        }
        return true;
    }
}
