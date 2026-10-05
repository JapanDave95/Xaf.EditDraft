using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor.Templates;
using DevExpress.ExpressApp.Utils;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Blazor;

/// <summary>
/// AUTO-OFFER for the registered non-chart types (generic edit-draft engine, wave 1): when an EXISTING
/// record opens in an approved root DetailView and the logged-in user has live 入力控 drafts for it, a
/// popup lists every drafted member against the record as it is NOW. Owner D16: ALL live drafts of the
/// record (type + TargetOid + owner) are offered together, newest first, each with its provenance line;
/// a member drafted in two drafts is shown twice, the newer one pre-ticked.
///
/// SINGLE-MODEL parts (design §3 S3): ownership, revision, View.AllowEdit, per-member write permission
/// and the access check on the record (EditDraftServices.MayRestore: XAF Write on the record plus the host's
/// IEditDraftAccessCheck) are checked when the offer is built and AGAIN immediately before anything is
/// applied. Claim first (every contributing draft), attach the newest to this screen's capture, then
/// fill in UNSAVED through EditDraftRestorer.ApplyExisting under EditDraftRestoreGuard (owner D12).
/// A draft whose entries are all 戻せません gets a read-only full-text display (owner D9).
///
/// Library milestone M2: in Xaf.EditDraft.Blazor (it watches the TabbedMDI template). Owner, access check and member
/// write permission through the Core seams (EditDraftServices.CurrentOwner, EditDraftServices.MayRestore,
/// EditDraftMemberAccess), "now" from the host clock, texts from EditDraftTexts, log lines through EditDraftLog (same
/// text); the popup rows are the library's EditDraftRestoreItem.
/// </summary>
public class EditDraftRestoreControllerBlazor : ObjectViewController<DetailView, object>
{
    private EditDraftWriter _writer;
    private EditDraftTypePolicy _policy;
    private bool _offeredThisActivation;
    private bool _offerPosted;
    private SynchronizationContext _circuit;
    private DetailView _openPopup;
    private ITabbedMdiMainFormTemplate _mdi;

    protected override void OnActivated()
    {
        base.OnActivated();
        _writer = new EditDraftWriter(Application.ServiceProvider);
        _offeredThisActivation = false;
        _offerPosted = false;
        _openPopup = null;
        _circuit = SynchronizationContext.Current;
        _policy = EditDraftServices.Registry(Application?.ServiceProvider).Find(View?.CurrentObject?.GetType());
        if (!EditDraftTypePolicy.IsGeneric(_policy)) { _policy = null; return; }
        View.CurrentObjectChanged += View_CurrentObjectChanged;
        EditDraftLog.Info($"[EditDraft] offer watcher ready view={View?.Id} type={_policy.TypeName} context={(_circuit != null ? "circuit" : "none")}");
        // Deferred: a popup raised inside OnActivated re-enters the activation pipeline (KB fix-188).
        Post(() => Post(() => TryOffer("activated")));
    }

    protected override void OnDeactivated()
    {
        if (View != null) View.CurrentObjectChanged -= View_CurrentObjectChanged;
        UnwatchMdi();
        base.OnDeactivated();
    }

    private void View_CurrentObjectChanged(object sender, EventArgs e)
    {
        _offeredThisActivation = false;
        _policy = EditDraftServices.Registry(Application?.ServiceProvider).Find(View?.CurrentObject?.GetType());
        if (!EditDraftTypePolicy.IsGeneric(_policy)) { _policy = null; return; }
        Post(() => Post(() => TryOffer("record changed")));
    }

    /// <summary>"Now" from the host clock (TimeProvider in DI; the system clock otherwise), local wall time.</summary>
    private DateTime Now() => EditDraftClock.Now(EditDraftServices.Clock(Application?.ServiceProvider));

    private void Post(Action action)
    {
        var ctx = SynchronizationContext.Current ?? _circuit;
        if (ctx != null) ctx.Post(_ => { if (View != null) action(); }, null); else action();
    }

    // ---- active tab (KB fix-530: an offer must not appear over another tab's record) ------------

    private bool IsTabbedMdi =>
        Application?.Model?.Options is DevExpress.ExpressApp.Blazor.SystemModule.IModelOptionsBlazor o
        && o.UIType == UIType.TabbedMDI;

    /// <summary>
    /// True when this view is the one on show: under TabbedMDI the active tab's view, or a view that is
    /// not a tab at all (a modal window, which is how the 「入力控」 list opens a record; Codex diffreview C2).
    /// Without TabbedMDI, always.
    /// </summary>
    private bool IsShownNow()
    {
        if (!IsTabbedMdi) return true;
        var mdi = Application?.MainWindow?.Template as ITabbedMdiMainFormTemplate;
        if (mdi == null) return true;
        if (ReferenceEquals(mdi.ActiveTemplate?.View, View)) return true;
        // A modal window (TargetWindow.NewModalWindow, the list's 開く) has the PopupWindow template context; it
        // is on top of every tab and never IS a tab, so it counts as shown.
        return Frame?.Context == TemplateContext.PopupWindow;
    }

    private void WatchMdi()
    {
        var mdi = Application?.MainWindow?.Template as ITabbedMdiMainFormTemplate;
        if (mdi == null || ReferenceEquals(mdi, _mdi)) return;
        UnwatchMdi();
        _mdi = mdi;
        _mdi.ActiveTemplateChanged += OnActiveTabChanged;
    }

    private void UnwatchMdi()
    {
        if (_mdi != null) _mdi.ActiveTemplateChanged -= OnActiveTabChanged;
        _mdi = null;
    }

    private void OnActiveTabChanged(object sender, DetailFormTemplateChangedEventArgs e)
    {
        if (!IsShownNow() || _offerPosted) return;
        _offerPosted = true;
        Post(() => { _offerPosted = false; TryOffer("tab activated"); });
    }

    // ---- the offer --------------------------------------------------------------------------------

    private void TryOffer(string trigger)
    {
        if (_offeredThisActivation || _policy == null) return;
        try
        {
            if (!EditDraftSwitch.IsRestoreAvailable(Application?.ServiceProvider))
            {
                EditDraftLog.Info($"[EditDraft] offer skipped at '{trigger}': restore not available (table absent)");
                return;
            }
            var record = View?.CurrentObject;
            if (record == null || ObjectSpace.IsNewObject(record)) return;        // existing records only (wave 1)
            if (!EditDraftCaptureController.IsAdmittedView(_policy, View?.Id, View.IsRoot, false))
            {
                EditDraftLog.Info($"[EditDraft] offer skipped at '{trigger}': view {View?.Id} (isRoot={View.IsRoot}) is not an approved root DetailView of {_policy.TypeName}");
                return;
            }
            if (!IsShownNow())
            {
                // Not on show: offer when this tab becomes active, never over another record (fix-530).
                EditDraftLog.Info($"[EditDraft] offer deferred at '{trigger}': view {View?.Id} is not the active tab");
                WatchMdi();
                return;
            }
            var owner = EditDraftServices.CurrentOwner(Application?.ServiceProvider, ObjectSpace, _policy);
            if (owner.IsNone)
            {
                EditDraftLog.Info($"[EditDraft] offer skipped at '{trigger}': no owner (not logged in, or the owner seam named none)");
                return;
            }
            _offeredThisActivation = true;
            UnwatchMdi();

            var recordOid = (record as BaseObject)?.Oid ?? Guid.Empty;
            var ownInstance = Frame?.GetController<EditDraftCaptureController>()?.CurrentEditorInstanceId ?? Guid.Empty;
            var requested = (Application?.ServiceProvider?.GetService(typeof(EditDraftOfferRequests)) as EditDraftOfferRequests)
                ?.TakeOfferRequest(record) ?? Guid.Empty;

            var drafts = new List<(EditDraftStoreBase Draft, EditDraftPayload Payload)>();
            using (var readSpace = _writer.CreateReadSpace(out var scope))
            using (scope)
            {
                if (requested != Guid.Empty)
                {
                    // Owner-scoped read: someone else's Oid returns nothing. The exact draft the list asked for, alone.
                    var d = _writer.ReadOwn(readSpace, requested, owner.Oid);
                    if (d != null && !d.HasExpired(Now()) && d.TargetOid == recordOid && d.ObjectType == _policy.TypeName) drafts.Add((d, null));
                }
                else
                {
                    // D16: every live draft of this record (newest first), not only the newest one.
                    foreach (var d in _writer.ListOwn(readSpace, owner.Oid, _policy.TypeName, false, Now(), out _)
                                 .Where(d => d.TargetOid == recordOid && d.EditorInstanceId != ownInstance))
                        drafts.Add((d, null));
                }
                for (var i = 0; i < drafts.Count; i++)
                {
                    var d = drafts[i].Draft;
                    var payload = d.IsPayloadReadable ? EditDraftPayload.FromJson<EditDraftPayload>(d.Payload) : null;
                    if (payload == null) EditDraftLog.Warning($"[EditDraft] draft {d.Oid} schema {d.PayloadSchemaVersion}: payload unreadable; not offered");
                    drafts[i] = (d, payload);
                }
                drafts.RemoveAll(x => x.Payload == null);
            }
            if (drafts.Count == 0)
            {
                EditDraftLog.Info($"[EditDraft] no draft to offer for {_policy.TypeName} record={S(recordOid)} owner={S(owner.Oid)} excludedEditor={S(ownInstance)} requested={S(requested)} at '{trigger}'");
                return;
            }

            // Design §3 S3.4 (0.4.0-preview.1): the login may restore onto this record — XAF Write on the record, loaded
            // through the application's secured object space, plus the host's IEditDraftAccessCheck. Asked once there is a
            // draft to offer, so a record the login may only read opens without a message. Nothing from the draft is shown otherwise.
            if (!EditDraftServices.MayRestore(Application, _policy, record))
            {
                EditDraftLog.Info($"[EditDraft] offer refused at '{trigger}': record {S(recordOid)} of {_policy.TypeName}: this login may not restore onto it");
                Message(EditDraftTexts.Of(t => t.RecordNotVisible), InformationType.Warning);
                return;
            }

            var plan = BuildPlan(record, recordOid, owner.Oid, drafts, requested != Guid.Empty);
            var unavailable = plan.Items.Count(i => i.StatusCode == (int)EditDraftItemStatus.Unavailable);
            if (plan.HasNothingToOffer)
            {
                if (unavailable == 0)
                {
                    EditDraftLog.Info($"[EditDraft] drafts [{string.Join(",", plan.Drafts.Select(s => S(s.DraftOid)))}] record={S(recordOid)}: every drafted value is already on the record; not offered");
                    return;
                }
                ShowReadOnly(plan, drafts);   // D9: nothing restorable, but typed text worth retyping
                return;
            }
            var conflicts = plan.Items.Count(i => i.StatusCode == (int)EditDraftItemStatus.Conflict);
            plan.Lead = string.Format(EditDraftTexts.Of(t => t.OfferLeadCount), plan.Items.Count(i => i.Selectable)) + Environment.NewLine +
                        EditDraftTexts.Of(t => t.OfferLeadInstruction);
            plan.ConflictBanner = conflicts > 0
                ? string.Format(EditDraftTexts.Of(t => t.OfferConflictBanner), conflicts)
                : string.Empty;
            EditDraftLog.Info($"[EditDraft] offering {plan.Drafts.Count} draft(s) [{string.Join(",", plan.Drafts.Select(s => S(s.DraftOid)))}] record={S(recordOid)} owner={S(owner.Oid)} items={plan.Items.Count} conflicts={conflicts} unavailable={unavailable} preselected={plan.SelectedCount}");
            ShowPopup(plan);
        }
        catch (Exception ex)
        {
            EditDraftLog.Error($"[EditDraft] offer failed at '{trigger}', screen opened without it: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// One plan for all live drafts of the record, newest first. Each row keeps its source draft; a
    /// member drafted in an older draft too is shown again, unticked, marked 「（新しい入力控に同じ項目があります）」.
    /// A member the login may not write is 戻せません (S3 check 3). The provenance lines carry the ①②③ markers.
    /// </summary>
    internal EditDraftRestorePlan BuildPlan(object record, Guid recordOid, Guid ownerOid, List<(EditDraftStoreBase Draft, EditDraftPayload Payload)> drafts, bool fromSearch)
    {
        var plan = new EditDraftRestorePlan
        {
            OwnerOid = ownerOid, TargetOid = recordOid, ObjectType = _policy.TypeName, PolicyId = _policy.PolicyId, FromSearch = fromSearch
        };
        var caption = CaptionHelper.GetClassCaption(_policy.Type.FullName);
        var provenance = new StringBuilder();
        var allPaths = drafts.SelectMany(x => x.Payload.Entries.Select(e => e.Path)).Distinct(StringComparer.Ordinal).ToList();
        var notWritable = EditDraftMemberAccess.NotWritable(ObjectSpace, record, allPaths);
        var perDraft = new List<(Guid DraftOid, List<EditDraftRestoreRow> Items)>();
        for (var i = 0; i < drafts.Count; i++)
        {
            var (draft, payload) = drafts[i];
            var marker = EditDraftOfferMerge.MarkerFor(i);
            plan.Drafts.Add(new EditDraftRestorePlan.Source(draft.Oid, draft.Revision, draft.LastCapturedOn, draft.ViewId, marker));
            if (provenance.Length > 0) provenance.AppendLine();
            // Owner B8 (wave 1b): the origin (一覧から／詳細から + the view caption of the stored ViewId).
            provenance.Append(string.Format(EditDraftTexts.Of(t => t.OfferProvenance), marker, caption, draft.LastCapturedOn, draft.EntryCount,
                EditDraftProvenance.Resolve(Application?.Model, draft.ViewId)));
            perDraft.Add((draft.Oid, EditDraftRestorer.BuildItems(_policy, payload, record, isNew: false)));
        }
        foreach (var row in EditDraftOfferMerge.Merge(perDraft, notWritable))   // D16 rules (pure, tested)
        {
            var item = EditDraftRestoreItems.ToItem(row.Item);   // the engine's neutral row -> this popup's row (M2: the library's row type)
            plan.Items.Add(item);
            plan.Sources[item] = row.DraftOid;
        }
        plan.Provenance = provenance.ToString();
        return plan;
    }

    private void ShowPopup(EditDraftRestorePlan plan)
    {
        var popupSpace = Application.CreateObjectSpace(typeof(EditDraftRestorePlan));
        var view = Application.CreateDetailView(popupSpace, plan);
        view.Caption = EditDraftTexts.Of(t => t.OfferViewCaption);
        view.Closing += (s, e) =>
        {
            if (ReferenceEquals(_openPopup, view)) _openPopup = null;
            if (!plan.Answered) EditDraftLog.Info($"[EditDraft] drafts [{string.Join(",", plan.Drafts.Select(x => S(x.DraftOid)))}] closed without choosing; left for next time");
        };
        _openPopup = view;
        Application.ShowViewStrategy.ShowViewInPopupWindow(
            view,
            okDelegate: () => { plan.Answered = true; Apply(plan); },
            cancelDelegate: () =>
            {
                if (plan.Answered) return;                                   // 破棄 closes through here: already reported
                plan.Answered = true;
                EditDraftLog.Info($"[EditDraft] drafts [{string.Join(",", plan.Drafts.Select(x => S(x.DraftOid)))}] dismissed (あとで); left for next time");
            },
            okButtonCaption: EditDraftTexts.Of(t => t.OfferOk),
            cancelButtonCaption: EditDraftTexts.Of(t => t.OfferLater));
    }

    /// <summary>D9: the full typed text of every 戻せません entry, read-only. Nothing here applies anything.</summary>
    private void ShowReadOnly(EditDraftRestorePlan plan, List<(EditDraftStoreBase Draft, EditDraftPayload Payload)> drafts)
    {
        var text = new StringBuilder();
        var count = 0;
        foreach (var source in plan.Drafts)
        {
            var payload = drafts.First(x => x.Draft.Oid == source.DraftOid).Payload;
            foreach (var item in plan.Items.Where(i => plan.Sources[i] == source.DraftOid && i.StatusCode == (int)EditDraftItemStatus.Unavailable))
            {
                var entry = payload.Get(item.Path);
                if (entry == null) continue;
                count++;
                if (text.Length > 0) text.AppendLine().AppendLine();
                text.Append(drafts.Count > 1 ? source.Marker + " " : string.Empty).Append(entry.Caption ?? entry.Path).Append(EditDraftTexts.Of(t => t.ReadOnlyEntrySuffix)).AppendLine();
                text.Append(string.IsNullOrEmpty(entry.ValueText) ? EditDraftTexts.Of(t => t.Empty) : entry.ValueText);   // FULL value, never Short()
            }
        }
        var view = new EditDraftReadOnlyView
        {
            OwnerOid = plan.OwnerOid,
            ObjectType = plan.ObjectType,
            Lead = string.Format(EditDraftTexts.Of(t => t.ReadOnlyLead), count),
            Provenance = plan.Provenance,
            Text = text.ToString()
        };
        view.DraftOids.AddRange(plan.Drafts.Select(x => x.DraftOid));
        EditDraftLog.Info($"[EditDraft] read-only display for drafts [{string.Join(",", plan.Drafts.Select(x => S(x.DraftOid)))}] record={S(plan.TargetOid)}: {count} 戻せません entr(ies)");
        var space = Application.CreateObjectSpace(typeof(EditDraftReadOnlyView));
        var detail = Application.CreateDetailView(space, view);
        detail.Caption = EditDraftTexts.Of(t => t.ReadOnlyViewCaption);
        Application.ShowViewStrategy.ShowViewInPopupWindow(detail, okButtonCaption: EditDraftTexts.Of(t => t.Close), cancelButtonCaption: null);
    }

    // ---- apply --------------------------------------------------------------------------------------

    /// <summary>Puts the ticked values back — UNSAVED — after re-checking every contributing draft and the record.</summary>
    private void Apply(EditDraftRestorePlan plan)
    {
        var chosenItems = plan.Items.Where(i => i.Selected && i.Selectable).ToList();
        if (chosenItems.Count == 0) { Message(EditDraftTexts.Of(t => t.NothingSelected), InformationType.Warning); return; }

        var record = View?.CurrentObject;
        var recordOid = (record as BaseObject)?.Oid ?? Guid.Empty;
        var owner = EditDraftServices.CurrentOwner(Application?.ServiceProvider, ObjectSpace, _policy);
        var capture = Frame?.GetController<EditDraftCaptureController>();
        if (record == null || capture == null || capture.Policy == null || capture.Policy.Type != _policy.Type)
        {
            Message(EditDraftTexts.Of(t => t.ApplyScreenChanged), InformationType.Warning);
            return;
        }

        // RE-AUTHORISE AT APPLY (single-model): owner, revision, liveness, record, view edit, access check, write permission.
        if (!View.AllowEdit)
        {
            Message(EditDraftTexts.Of(t => t.ApplyViewNotEditable), InformationType.Warning);
            return;
        }
        if (!EditDraftServices.MayRestore(Application, _policy, record))
        {
            Message(EditDraftTexts.Of(t => t.RecordNotVisible), InformationType.Warning);
            return;
        }
        var contributing = plan.Drafts.Where(s => chosenItems.Any(i => plan.Sources[i] == s.DraftOid)).ToList();   // newest first
        var payloads = new Dictionary<Guid, (string Json, EditDraftPayload Payload)>();
        using (var readSpace = _writer.CreateReadSpace(out var scope))
        using (scope)
        {
            foreach (var source in contributing)
            {
                var draft = _writer.ReadOwn(readSpace, source.DraftOid, owner.Oid);
                var ok = draft != null && EditDraftAccessRule.MayApply(owner.Oid, draft.OwnerUserOid, plan.OwnerOid, recordOid, plan.TargetOid, draft.TargetOid,
                             draft.HasExpired(Now()), draft.DeletedOn != null, plan.FromSearch, draft.Revision, source.Revision, View.AllowEdit);
                if (!ok || draft.ObjectType != _policy.TypeName)
                {
                    EditDraftLog.Warning($"[EditDraft] apply refused for {source.DraftOid}: not this login's live draft for this record at rev {source.Revision}");
                    Message(EditDraftTexts.Of(t => t.ApplyNotOwnLiveDraft), InformationType.Warning);
                    return;
                }
                var payload = EditDraftPayload.FromJson<EditDraftPayload>(draft.Payload);
                if (payload == null) { Message(EditDraftTexts.Of(t => t.DraftUnreadable), InformationType.Warning); return; }
                payloads[source.DraftOid] = (draft.Payload, payload);
            }
        }

        // A member the login may not write is never assigned (re-checked now, not only when the popup was built).
        var notWritable = EditDraftMemberAccess.NotWritable(ObjectSpace, record, chosenItems.Select(i => i.Path).Distinct());
        chosenItems.RemoveAll(i => notWritable.Contains(i.Path));

        // CLAIM FIRST, every contributing draft (fenced on the revision the popup showed). A lost claim applies NOTHING.
        var claimed = new Dictionary<Guid, int>();
        foreach (var source in contributing)
        {
            var rev = _writer.TryClaim(source.DraftOid, source.Revision, owner.Oid, capture.CurrentEditorInstanceId, Now());
            if (rev <= 0)
            {
                EditDraftLog.Info($"[EditDraft] claim lost for {source.DraftOid} at rev {source.Revision}; nothing applied (already-won claims stay with this screen)");
                Message(EditDraftTexts.Of(t => t.ApplyClaimLost), InformationType.Warning);
                return;
            }
            claimed[source.DraftOid] = rev;
        }
        // The NEWEST contributing draft becomes this screen's own row (typing continues in it; the save retires it).
        var primary = contributing[0];
        if (!capture.TryAttachClaimed(primary.DraftOid, claimed[primary.DraftOid], owner.Oid, payloads[primary.DraftOid].Json))
        {
            Message(EditDraftTexts.Of(t => t.ApplyScreenHoldsDraft), InformationType.Warning);
            return;
        }

        // One combined payload: for a member chosen from two drafts the NEWER draft's value is kept.
        var merged = new EditDraftPayload { TypeName = _policy.TypeName };
        var chosenPaths = new List<string>();
        var rows = EditDraftOfferMerge.FirstPerPath(chosenItems.Select(i => new EditDraftOfferMerge.Row(EditDraftRestoreItems.ToRow(i), plan.Sources[i])), out var skippedDuplicates);
        foreach (var row in rows)   // plan rows are newest draft first, so the newer value wins
        {
            var entry = payloads[row.DraftOid].Payload.Get(row.Item.Path);
            if (entry == null) continue;
            merged.Entries.Add(entry);
            chosenPaths.Add(row.Item.Path);
        }

        // Current values are compared AGAIN, as VALUES: each chosen member must still hold, on this
        // screen AND in the database, exactly the value the person reviewed. A changed member is left alone.
        var toAssign = EditDraftRestorer.AssignableOnExisting(_policy, chosenPaths, merged);
        var stillSafe = RecheckChosen(plan, record, toAssign);
        var skipped = chosenPaths.Count(p => !stillSafe.Contains(p)) + skippedDuplicates + chosenItems.Count(i => notWritable.Contains(i.Path));

        int applied, failed;
        // D12. NOT a `using`: the guard must stay open behind the work already posted to the circuit and is
        // disposed by the sentinel CloseAfterPostedWork posts (Codex diffreview C1: a using block disposed it at once).
        var guard = new EditDraftRestoreGuard(ObjectSpace, _policy.LogTag,
            what => Message(EditDraftTexts.Of(t => t.ApplyGuardStopped), InformationType.Warning));
        try
        {
            (applied, failed, _) = EditDraftRestorer.ApplyExisting(_policy, ObjectSpace, record, merged, stillSafe);
            if (guard.Violated) failed++;
        }
        catch
        {
            guard.Dispose();   // an exception closes it now; nothing was posted on its behalf
            throw;
        }
        guard.CloseAfterPostedWork(SynchronizationContext.Current ?? _circuit);

        EditDraftLog.Info($"[EditDraft] drafts [{string.Join(",", contributing.Select(s => S(s.DraftOid)))}] applied={applied} failed={failed} changedSincePopup={skipped} record={S(recordOid)} (unsaved; saving retires the attached draft)");
        Message(failed + skipped == 0
                ? string.Format(EditDraftTexts.Of(t => t.Applied), applied)
                : string.Format(EditDraftTexts.Of(t => t.AppliedPartly), applied, failed + skipped),
            failed + skipped == 0 ? InformationType.Success : InformationType.Warning);
    }

    /// <summary>
    /// The paths whose reviewed value still equals the screen's value and a FRESH database read. A read
    /// failure keeps nothing. A group with one failing member is dropped whole. Unavailable paths are never assigned.
    /// </summary>
    private List<string> RecheckChosen(EditDraftRestorePlan plan, object record, List<string> chosen)
    {
        var safe = new List<string>();
        try
        {
            using var freshSpace = Application.CreateObjectSpace(record.GetType());
            var stored = freshSpace.GetObjectByKey(record.GetType(), (record as BaseObject)?.Oid ?? Guid.Empty);
            if (stored == null) return safe;
            // KB fix-529: the screen's value is the one the writing getters filled; the fresh read gets the same fill
            // before comparing. Accepted residual (chart branch, Codex C2): a stored value CLEARED to empty after the
            // popup opened reads as the fill and is not detected; the run is logged so it is visible.
            var ran = EditDraftMembers.RunInitializingGetters(_policy, stored);
            if (ran > 0) EditDraftLog.Info($"[EditDraft] apply re-check: {ran} initializing getter(s) run on the fresh read of {_policy.TypeName} before comparing {chosen.Count} member(s)");
            foreach (var path in chosen)
            {
                // The row the person reviewed for this path: the newest selected one, else any row of the path.
                var item = plan.Items.FirstOrDefault(i => i.Path == path && i.Selected) ?? plan.Items.FirstOrDefault(i => i.Path == path);
                if (item == null) continue;
                var assignable = item.Selectable || item.StatusCode == (int)EditDraftItemStatus.AlreadyApplied;
                if (!assignable) continue;
                string screenRaw, storedRaw;
                try
                {
                    screenRaw = EditDraftCodec.RawOf(EditDraftMembers.GetValue(record, path));
                    storedRaw = EditDraftCodec.RawOf(EditDraftMembers.GetValue(stored, path));
                }
                catch { continue; }
                if (string.Equals(screenRaw, item.CurrentRaw, StringComparison.Ordinal) &&
                    string.Equals(storedRaw, item.CurrentRaw, StringComparison.Ordinal))
                    safe.Add(path);
            }
        }
        catch (Exception ex)
        {
            EditDraftLog.Warning($"[EditDraft] apply re-check read failed ({ex.GetType().Name}); nothing applied");
            return new List<string>();
        }
        foreach (var g in plan.Items.Where(i => i.Group != null && chosen.Contains(i.Path)).GroupBy(i => i.Group))
        {
            if (g.All(i => safe.Contains(i.Path))) continue;
            foreach (var i in g) safe.Remove(i.Path);
        }
        return safe;
    }

    private static string S(Guid g) => EditDraftCaptureController.Short(g);

    private void Message(string text, InformationType type)
    {
        try { Application?.ShowViewStrategy?.ShowMessage(text, type, 8000); } catch { }
    }
}
