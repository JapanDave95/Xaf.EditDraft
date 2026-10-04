using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>Options of the Blazor part (<see cref="EditDraftBlazorServiceCollectionExtensions.AddEditDraftBlazor"/>).</summary>
public sealed class EditDraftBlazorOptions
{
    /// <summary>
    /// Gap G11 (2026-10-04): when true, the main-header action 「入力控」 ("Drafts" in the English set) is shown on every view,
    /// not only while a registered ListView is on show; where no registered ListView is shown it opens the list of every
    /// registered type. Default false (the action shows only on a registered ListView, filtered to its type). A host can also
    /// open the all-types list from its own UI through the per-circuit <see cref="EditDraftListBridge"/> (Open()).
    /// </summary>
    public bool HeaderActionOnEveryView { get; set; }

    /// <summary>The option as registered in <paramref name="services"/>; false when none is registered.</summary>
    public static bool HeaderActionOnEveryViewIn(IServiceProvider services) =>
        (services?.GetService(typeof(EditDraftBlazorOptions)) as EditDraftBlazorOptions)?.HeaderActionOnEveryView == true;
}

/// <summary>Host registration of the Blazor part's per-circuit services.</summary>
public static class EditDraftBlazorServiceCollectionExtensions
{
    /// <summary>
    /// Registers, per circuit (scoped): the 「入力控」 list bridge (a host UI entry point for the all-types list), the exact-draft
    /// hand-over from the list to the record's screen, and the row-badge notifier. Never process-wide: each circuit
    /// has its own (design rule 2, shared state). <paramref name="configure"/> sets <see cref="EditDraftBlazorOptions"/>.
    /// </summary>
    public static IServiceCollection AddEditDraftBlazor(this IServiceCollection services, Action<EditDraftBlazorOptions> configure = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        var options = new EditDraftBlazorOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddScoped<EditDraftListBridge>();
        services.AddScoped<EditDraftOfferRequests>();
        services.AddScoped<EditDraftBadgeNotifier>();   // wave 1b row badges, per circuit (B6)
        services.AddScoped<EditDraftPendingAdoptions>();   // NEW records: the recreate hands its claimed draft to the record's screen (Core contract)
        return services;
    }

    /// <summary>
    /// Gap G4 (2026-10-04): registers the retention sweep as a hosted service (<see cref="EditDraftRetentionService"/>). It
    /// deletes expired drafts only while EditDraftCapture:Retention:Enabled reads as the boolean true, every
    /// EditDraftCapture:Retention:IntervalMinutes minutes (default 60). Without this call, or with the key off, no draft row
    /// is deleted by the library.
    /// </summary>
    public static IServiceCollection AddEditDraftRetention(this IServiceCollection services)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        services.AddHostedService<EditDraftRetentionService>();
        return services;
    }
}

/// <summary>
/// The retention sweep as a hosted service (gap G4). One minute after the host starts, and then every
/// EditDraftCapture:Retention:IntervalMinutes minutes, it re-reads EditDraftCapture:Retention:Enabled and, when it is the
/// boolean true, runs <see cref="EditDraftRetention.Sweep(IServiceProvider)"/> (owner-agnostic delete of expired rows, the
/// application clock as the cutoff, logged count). A failed sweep is logged and retried at the next interval; it never
/// stops the host.
/// </summary>
public sealed class EditDraftRetentionService : BackgroundService
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(1);
    private readonly IServiceProvider _services;

    public EditDraftRetentionService(IServiceProvider services) => _services = services ?? throw new ArgumentNullException(nameof(services));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstDelay, stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                if (EditDraftRetention.IsEnabled(_services))
                {
                    try { EditDraftRetention.Sweep(_services); }
                    catch (Exception ex) { EditDraftLog.Error($"[EditDraft] retention sweep failed: {ex.GetType().Name}"); }
                }
                await Task.Delay(TimeSpan.FromMinutes(EditDraftRetention.IntervalMinutes(_services)), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
