using System;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor
{
    /// <summary>
    /// Per-circuit (scoped) bridge for the 「入力控」 entry in the 復元 section of the gear panel (generic
    /// edit-draft restore, owner D4). STAFF-accessible, on its own bridge — not ChartDraftListBridge
    /// (F2 identity, chart table), not AttendanceDraftListBridge, not RecoveryToolsBridge (administrators
    /// only). It makes no decision: the registered main-window controller answers "available?" (the
    /// EditDraft table exists) when asked, and again when the entry is executed. The panel is plain UI
    /// and is not trusted to have hidden it.
    /// (Library milestone M2: moved from the application's Services folder; registered per circuit by
    /// EditDraftBlazorServiceCollectionExtensions.AddEditDraftBlazor; the host's settings panel injects it.)
    /// </summary>
    public class EditDraftListBridge
    {
        private object _owner;
        private Func<bool> _isAvailable;
        private Action _open;

        /// <summary>The entry's caption (「入力控」 in the Japanese text set).</summary>
        public static string Caption => EditDraftTexts.Of(t => t.ListCaption);

        public event Action StateChanged;

        public void Register(object owner, Func<bool> isAvailable, Action open)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _isAvailable = isAvailable ?? throw new ArgumentNullException(nameof(isAvailable));
            _open = open ?? throw new ArgumentNullException(nameof(open));
            StateChanged?.Invoke();
        }

        public void Unregister(object owner)
        {
            if (!ReferenceEquals(_owner, owner)) return;
            _owner = null; _isAvailable = null; _open = null;
            StateChanged?.Invoke();
        }

        public void NotifyChanged(object owner)
        {
            if (ReferenceEquals(_owner, owner)) StateChanged?.Invoke();
        }

        /// <summary>Fails closed: nothing registered, or the probe throws, is "not available".</summary>
        public bool IsAvailable
        {
            get
            {
                try { return _owner != null && _isAvailable != null && _isAvailable(); }
                catch { return false; }
            }
        }

        /// <summary>Re-checks availability, then opens. False when refused. Never throws into the panel.</summary>
        public bool Open()
        {
            if (_owner == null || _open == null)
            {
                EditDraftLog.Warning("[EditDraft] list entry clicked but no list controller is registered for this session");
                return false;
            }
            if (!IsAvailable)
            {
                EditDraftLog.Info("[EditDraft] list entry clicked but refused: restore not available (table absent)");
                return false;
            }
            try { _open(); }
            catch (Exception ex)
            {
                EditDraftLog.Error($"[EditDraft] list entry open failed: {ex.GetType().Name}");
                return false;
            }
            return true;
        }
    }
}
