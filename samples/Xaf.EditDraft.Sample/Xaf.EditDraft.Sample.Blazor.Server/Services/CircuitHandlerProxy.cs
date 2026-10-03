using DevExpress.ExpressApp.Blazor.Services;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Xaf.EditDraft.Sample.Blazor.Server.Services;

/// <summary>The DevExpress template's circuit handler proxy (unchanged apart from the namespace).</summary>
internal class CircuitHandlerProxy : CircuitHandler
{
    private readonly IScopedCircuitHandler _scopedCircuitHandler;

    public CircuitHandlerProxy(IScopedCircuitHandler scopedCircuitHandler) => _scopedCircuitHandler = scopedCircuitHandler;

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken) =>
        _scopedCircuitHandler.OnCircuitOpenedAsync(cancellationToken);

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken) =>
        _scopedCircuitHandler.OnConnectionUpAsync(cancellationToken);

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken) =>
        _scopedCircuitHandler.OnCircuitClosedAsync(cancellationToken);

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken) =>
        _scopedCircuitHandler.OnConnectionDownAsync(cancellationToken);
}
