using System;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor
{
    /// <summary>
    /// Per-circuit (scoped) entry point to the list of the login's drafts of EVERY registered type, for a host's own UI (a
    /// settings panel, a menu item, a Razor component): inject it, show the entry while <see cref="IsAvailable"/>, and call
    /// <see cref="Open"/>. It makes no decision: the main-window list controller (EditDraftListControllerBlazor) registers
    /// itself here and answers "available?" (the store table exists) when asked, and again when the entry is executed;
    /// the host UI is not trusted to have hidden it. Without a host UI, the header action can open the same list on every
    /// view (EditDraftBlazorOptions.HeaderActionOnEveryView, gap G11).
    /// (Library milestone M2: registered per circuit by EditDraftBlazorServiceCollectionExtensions.AddEditDraftBlazor.)
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
