using System;

namespace Xaf.EditDraft.Core;

/// <summary>
/// The switch predicate of ONE capture slot, bound to the policy that created the slot (Codex diffreview
/// a2 C1). A slot that was retired (the screen moved to another record, type or no admitted type) still
/// rewrites its never-stored input after an expiry/破棄 fresh start; that write must check the slot's OWN
/// policy's switch, never the current screen's. Pure: the configuration reader is injected, so the
/// binding is testable without XAF.
/// </summary>
public static class EditDraftWriteGate
{
    /// <summary>
    /// A predicate that answers "may this slot write now?" for <paramref name="policyId"/> only. The
    /// policy id is fixed at creation; <paramref name="isEnabled"/> is re-evaluated at every call (the
    /// keys are re-read before every write, design §6). A null or empty policy id never writes.
    /// </summary>
    public static Func<bool> Bind(string policyId, Func<string, bool> isEnabled)
    {
        if (isEnabled == null) throw new ArgumentNullException(nameof(isEnabled));
        if (string.IsNullOrEmpty(policyId)) return () => false;
        return () =>
        {
            try { return isEnabled(policyId); }
            catch { return false; }
        };
    }
}
