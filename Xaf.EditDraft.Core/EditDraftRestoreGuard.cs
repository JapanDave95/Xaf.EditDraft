using System;
using System.ComponentModel;
using System.Threading;
using DevExpress.ExpressApp;

namespace Xaf.EditDraft.Core;

/// <summary>
/// Runtime BACKSTOP for a restore (design §4.1 layer 2, owner D12). While it is open, a commit or a
/// rollback of the guarded object space is cancelled, logged and reported, so a controller that saves
/// or rolls back on a value change cannot turn "fill in" into "save" or discard pending edits.
///
/// It is NOT the guarantee — admission is (no type with a commit/rollback path joins a wave). Limits:
///  - work posted by posted work can run after the guard has closed;
///  - cancelling does not undo what a controller did BEFORE it asked to commit;
///  - a commit or rollback of a DIFFERENT object space is not seen.
/// The engine never rolls back as error recovery.
///
/// Opened by the restore (EditDraftRestoreControllerBlazor) and the recreate (EditDraftRecreateHostBlazor).
/// </summary>
public sealed class EditDraftRestoreGuard : IDisposable
{
    private readonly object _gate = new();
    private readonly string _tag;
    private readonly Action<string> _report;
    private IObjectSpace _objectSpace;

    public EditDraftRestoreGuard(IObjectSpace objectSpace, string logTag = "EditDraft", Action<string> report = null)
    {
        _objectSpace = objectSpace ?? throw new ArgumentNullException(nameof(objectSpace));
        _tag = string.IsNullOrEmpty(logTag) ? "EditDraft" : logTag;
        _report = report;
        _objectSpace.Committing += OnCommitting;
        _objectSpace.RollingBack += OnRollingBack;
    }

    public int CancelledCommits { get; private set; }
    public int CancelledRollbacks { get; private set; }

    /// <summary>True once any commit or rollback was cancelled: the restore must be reported as a failure.</summary>
    public bool Violated => CancelledCommits + CancelledRollbacks > 0;

    public bool IsOpen { get { lock (_gate) return _objectSpace != null; } }

    private void OnCommitting(object sender, CancelEventArgs e)
    {
        if (!IsOpen) return;
        e.Cancel = true;
        CancelledCommits++;
        Report("a save (commit) was attempted while a restore was filling in values; it was cancelled");
    }

    private void OnRollingBack(object sender, CancelEventArgs e)
    {
        if (!IsOpen) return;
        e.Cancel = true;
        CancelledRollbacks++;
        Report("a rollback was attempted while a restore was filling in values; it was cancelled");
    }

    private void Report(string what)
    {
        try { EditDraftLog.Error($"[{_tag}] restore guard: {what}"); } catch { }
        try { _report?.Invoke(what); } catch { }
    }

    /// <summary>
    /// Closes the guard AFTER the work already posted to <paramref name="context"/> has run (a sentinel
    /// posted behind it). With no context the guard closes now.
    /// </summary>
    public void CloseAfterPostedWork(SynchronizationContext context)
    {
        if (context == null) { Dispose(); return; }
        context.Post(_ => Dispose(), null);
    }

    /// <summary>Closes the guard. Safe to call more than once.</summary>
    public void Dispose()
    {
        IObjectSpace os;
        lock (_gate) { os = _objectSpace; _objectSpace = null; }
        if (os == null) return;
        try
        {
            os.Committing -= OnCommitting;
            os.RollingBack -= OnRollingBack;
        }
        catch { }
    }
}
