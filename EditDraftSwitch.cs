using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Xaf.EditDraft.Core;

/// <summary>
/// 入力控 on/off for the generic (non-chart) types — design §6, owner decision D5. A type is captured
/// only when BOTH "EditDraftCapture:Enabled" and "EditDraftCapture:Types:&lt;PolicyId&gt;:Enabled" are
/// the boolean true; missing, empty, unparsable or unreadable = OFF (fail closed; <see cref="IsOn"/> is the
/// parse of CareCrew's CareTreeDraftCaptureSwitch, copied). The chart key TenantChartDraftCapture:Enabled is NOT read here.
/// Re-read at every use.
///
/// Library (milestone M1): the section is the host's choice (<see cref="EditDraftSwitchOptions"/> in the service
/// provider; default <see cref="DefaultSection"/> = "EditDraftCapture"). The key constants below name the keys in
/// the default section; <see cref="In"/> moves a key into the configured section.
/// </summary>
public static class EditDraftSwitch
{
    public const string DefaultSection = "EditDraftCapture";

    public const string EnabledKey = "EditDraftCapture:Enabled";
    public const string TypesSection = "EditDraftCapture:Types";

    public static string TypeKey(string policyId) => TypesSection + ":" + policyId + ":Enabled";

    /// <summary>
    /// Wave 1b (owner B7): ONE global key for ListView capture. The policy's ListViewIds is the per-type
    /// allowlist; there is no per-type list key. A list captures only when this key AND both keys of
    /// <see cref="Decide"/> are true. DetailView capture never reads it.
    /// </summary>
    public const string ListViewsKey = "EditDraftCapture:ListViews:Enabled";

    /// <summary>
    /// FAILS CLOSED: true only for a value that parses as the boolean true (bool.TryParse after Trim: case-insensitive,
    /// surrounding whitespace ignored). Missing, empty, "1", "yes", "on" or anything else is OFF. The same answer as
    /// CareCrew's CareTreeDraftCaptureSwitch.IsOn for the same input (library design §4.11 SEC-6).
    /// </summary>
    public static bool IsOn(string raw) => raw != null && bool.TryParse(raw.Trim(), out var value) && value;

    /// <summary>Pure decision: both raw values must parse as true.</summary>
    public static bool Decide(string globalRaw, string typeRaw) =>
        IsOn(globalRaw) && IsOn(typeRaw);

    /// <summary>Pure decision for ListView capture: the two DetailView keys and the list key must all parse as true.</summary>
    public static bool DecideList(string globalRaw, string typeRaw, string listRaw) =>
        Decide(globalRaw, typeRaw) && IsOn(listRaw);

    /// <summary>
    /// <paramref name="key"/> (named in the default section) in the section the host configured
    /// (<see cref="EditDraftSwitchOptions"/>); unchanged when none is configured or it is the default.
    /// </summary>
    public static string In(IServiceProvider services, string key)
    {
        var section = (services?.GetService(typeof(EditDraftSwitchOptions)) as EditDraftSwitchOptions)?.Section;
        if (string.IsNullOrEmpty(section) || section == DefaultSection || key == null || !key.StartsWith(DefaultSection + ":", StringComparison.Ordinal))
            return key;
        return section + key.Substring(DefaultSection.Length);
    }

    public static bool IsListEnabled(IServiceProvider services, string policyId)
    {
        if (string.IsNullOrEmpty(policyId)) return false;
        try
        {
            var configuration = services?.GetService<IConfiguration>();
            return configuration != null && DecideList(configuration[In(services, EnabledKey)], configuration[In(services, TypeKey(policyId))], configuration[In(services, ListViewsKey)]);
        }
        catch { return false; }
    }

    public static bool IsGlobalEnabled(IServiceProvider services)
    {
        try
        {
            var configuration = services?.GetService<IConfiguration>();
            return configuration != null && IsOn(configuration[In(services, EnabledKey)]);
        }
        catch { return false; }
    }

    public static bool IsEnabled(IServiceProvider services, string policyId)
    {
        if (string.IsNullOrEmpty(policyId)) return false;
        try
        {
            var configuration = services?.GetService<IConfiguration>();
            return configuration != null && Decide(configuration[In(services, EnabledKey)], configuration[In(services, TypeKey(policyId))]);
        }
        catch { return false; }
    }

    /// <summary>
    /// Restore and the 「入力控」 list: available exactly while dbo.EditDraft EXISTS, whatever the
    /// switches say — switching capture off (globally or per type) must not hide drafts still inside
    /// their seven days (design §6), and a database without the table shows nothing (Codex review C5:
    /// the chart switch answers true on "global on" before probing, which would show the entries on a
    /// development database whose table the owner has not created yet). Probe cached five minutes.
    /// </summary>
    public static bool IsRestoreAvailable(IServiceProvider services)
    {
        try { return services != null && new EditDraftWriter(services).TableExists(); }
        catch { return false; }
    }
}
