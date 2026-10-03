using System;
using Microsoft.Extensions.DependencyInjection;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>Host registration of the Blazor part's per-circuit services.</summary>
public static class EditDraftBlazorServiceCollectionExtensions
{
    /// <summary>
    /// Registers, per circuit (scoped): the 「入力控」 list bridge (the host's settings panel entry), the exact-draft
    /// hand-over from the list to the record's screen, and the row-badge notifier. Never process-wide: each circuit
    /// has its own (design rule 2, shared state).
    /// </summary>
    public static IServiceCollection AddEditDraftBlazor(this IServiceCollection services)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        services.AddScoped<EditDraftListBridge>();
        services.AddScoped<EditDraftOfferRequests>();
        services.AddScoped<EditDraftBadgeNotifier>();   // wave 1b row badges, per circuit (B6)
        services.AddScoped<EditDraftPendingAdoptions>();   // NEW records: the recreate hands its claimed draft to the record's screen (Core contract)
        return services;
    }
}
