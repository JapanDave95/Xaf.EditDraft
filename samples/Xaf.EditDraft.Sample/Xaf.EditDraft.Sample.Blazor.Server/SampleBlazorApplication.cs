using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor;

namespace Xaf.EditDraft.Sample.Blazor.Server;

/// <summary>
/// The DevExpress template's application class (unchanged apart from names; the template's EasyTest branch is left out).
/// With the debugger attached the database is created or updated on start; otherwise run the application once with
/// "--updateDatabase --forceUpdate --silent" (sample README, "Run").
/// </summary>
public class SampleBlazorApplication : BlazorApplication
{
    public SampleBlazorApplication()
    {
        ApplicationName = "Xaf.EditDraft.Sample";
        CheckCompatibilityType = CheckCompatibilityType.DatabaseSchema;
        DatabaseVersionMismatch += OnDatabaseVersionMismatch;
        SetupComplete += OnSetupCompleteLogModules;
    }

    // Sample addition (not in the template): one log line naming the modules XAF loaded, so a consumer can confirm that
    // EditDraftCoreModule and EditDraftBlazorModule are in. XAF's own trace log does not list module names.
    private void OnSetupCompleteLogModules(object sender, EventArgs e)
    {
        var logger = ServiceProvider?.GetService(typeof(ILogger<SampleBlazorApplication>)) as ILogger<SampleBlazorApplication>;
        logger?.LogInformation("XAF modules loaded: {Modules}", string.Join(", ", Modules.Select(m => m.GetType().Name)));
    }

    protected override void OnSetupStarted()
    {
        base.OnSetupStarted();
#if DEBUG
        if (System.Diagnostics.Debugger.IsAttached && CheckCompatibilityType == CheckCompatibilityType.DatabaseSchema)
            DatabaseUpdateMode = DatabaseUpdateMode.UpdateDatabaseAlways;
#endif
    }

    private void OnDatabaseVersionMismatch(object sender, DatabaseVersionMismatchEventArgs e)
    {
        if (System.Diagnostics.Debugger.IsAttached)
        {
            e.Updater.Update();
            e.Handled = true;
            return;
        }
        var message = "The application cannot connect to the specified database, " +
            "because the database doesn't exist, its version is older " +
            "than that of the application or its schema does not match " +
            "the ORM data model structure. To avoid this error, use one " +
            "of the solutions from the https://www.devexpress.com/kb=T367835 KB Article.";
        if (e.CompatibilityError?.Exception != null)
            message += "\r\n\r\nInner exception: " + e.CompatibilityError.Exception.Message;
        throw new InvalidOperationException(message);
    }
}
