using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xaf.EditDraft.Core;
using Xaf.EditDraft.Blazor;
using DevExpress.ExpressApp;
using FluentAssertions;
using NUnit.Framework;

namespace Xaf.EditDraft.Tests
{
    // Generic edit-draft restore, wave 1b — ListView capture (owner decisions B1-B10). Expectations come from the Codex
    // requirement-only list
    // written BEFORE any code was shown to it: run 2026-10-01-editdraft-wave1b-065de8, tests a1 (E1-E35).
    // Labels En refer to them. Logic tests run against pure code (EditDraftListRules, EditDraftRowContext); the grid
    // lifecycle, the badge rendering and the controllers' event wiring are pinned by source scans and verified in the
    // browser pass. Library milestone M3 (run 2026-10-02-editdraft-m3-1b4d82): the wave-1b tests on the library's own rules
    // and source; E21's model-caption check reads the caption from the text set (M3 localisation). The helper below names
    // the library source files these tests read.

    internal static class Wave1b
    {
        public const string ListCapture = "Xaf.EditDraft.Blazor/EditDraftListCaptureControllerBlazor.cs";
        public const string Badge = "Xaf.EditDraft.Blazor/EditDraftListBadgeControllerBlazor.cs";
    }

    [TestFixture]
    public class EditDraftWave1bAdmissionTests
    {
        [TestCase(true, true, false, CollectionSourceDataAccessMode.Client, true)]
        [TestCase(true, true, false, CollectionSourceDataAccessMode.Server, true)]
        [TestCase(false, true, false, CollectionSourceDataAccessMode.Client, false)]   // scheduler / other editor
        [TestCase(true, false, false, CollectionSourceDataAccessMode.Client, false)]   // AllowEdit false (the wave-1 main lists)
        [TestCase(true, true, true, CollectionSourceDataAccessMode.Client, false)]     // split view
        [TestCase(true, true, false, CollectionSourceDataAccessMode.ServerView, false)]
        [TestCase(true, true, false, CollectionSourceDataAccessMode.DataView, false)]
        [TestCase(true, true, false, CollectionSourceDataAccessMode.InstantFeedbackView, false)]
        public void E1_E4_a_list_edits_in_place_only_as_a_DxGrid_with_AllowEdit_not_split_and_not_light_access(bool grid, bool allowEdit, bool split, CollectionSourceDataAccessMode mode, bool expected)
        {
            EditDraftListAdmission.EditsInPlace(grid, allowEdit, split, mode).Should().Be(expected);
        }

        [Test]
        public void E1_E5_the_controller_uses_the_pure_gate_the_changed_object_and_never_the_focused_row()
        {
            var src = Wave1.Source(Wave1b.ListCapture);
            src.Should().Contain("EditDraftListAdmission.IsAdmittedList(policy, View.Id, View.IsRoot, editsInPlace, isNew: false)");
            src.Should().Contain("row.GetType() == _policy.Type && _objectSpace != null && !_objectSpace.IsNewObject(row)", "exact type, existing record, per event");
            // 0.4.0-preview.1: the owner seam takes the policy (owner ruling 2026-10-05); the pin follows the call.
            src.Should().Contain("if (!ListEnabled()) return;").And.Contain("EditDraftServices.CurrentOwner(Application?.ServiceProvider, objectSpace, _policy)");
            src.Should().Contain("var record = e.Object;");
            src.Should().NotContain("View.CurrentObject").And.NotContain("ViewCurrentObject").And.NotContain("SelectedObjects", "fix-032: the changed row, never the focused/selected one");
            src.Should().Contain("EditDraftListAdmission.EditsInPlace(editor is DxGridListEditorBase && editor is IGridEditingLifeCycle, allowEdit, split, mode)");
        }
    }

    [TestFixture]
    public class EditDraftWave1bSwitchTests
    {
        [TestCase("true", "true", "true", true)]
        [TestCase("true", "true", "false", false)]
        [TestCase("true", "true", null, false)]
        [TestCase("true", "true", "", false)]
        [TestCase("true", "true", "yes", false)]
        [TestCase("true", "false", "true", false)]
        [TestCase("false", "true", "true", false)]
        [TestCase(null, "true", "true", false)]
        [TestCase("True", "TRUE", "True", true)]
        public void E2_list_capture_needs_the_global_the_type_and_the_list_key_all_true(string global, string type, string list, bool expected)
        {
            EditDraftSwitch.DecideList(global, type, list).Should().Be(expected);
        }

        [Test]
        public void E2_one_global_list_key_no_per_type_list_key_and_DetailView_capture_never_reads_it()
        {
            EditDraftSwitch.ListViewsKey.Should().Be("EditDraftCapture:ListViews:Enabled");
            EditDraftSwitch.Decide("true", "true").Should().BeTrue("the list key off leaves DetailView capture on");
            EditDraftSwitch.DecideList("true", "true", "false").Should().BeFalse();
            var sw = Wave1.Source("Xaf.EditDraft.Core/EditDraftSwitch.cs");
            sw.Should().NotContain(":ListViews:Enabled\" +", "no per-type list key is built");
            Regex.Matches(sw, "ListViewsKey").Count.Should().Be(2, "declared once, read once (in IsListEnabled)");
            Wave1.Source("Xaf.EditDraft.Core/EditDraftCaptureController.cs").Should().NotContain("IsListEnabled", "DetailView capture is unchanged");
            Wave1.Source("Xaf.EditDraft.Blazor/EditDraftRestoreControllerBlazor.cs").Should().NotContain("IsListEnabled", "restore availability stays 'table exists'");
        }

        [Test]
        public void E3_a_queued_list_write_re_reads_the_list_key_bound_to_its_own_policy()
        {
            var keys = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [EditDraftSwitch.EnabledKey] = "true", [EditDraftSwitch.TypeKey("Note")] = "true", [EditDraftSwitch.ListViewsKey] = "true"
            };
            bool IsListEnabled(string id) => EditDraftSwitch.DecideList(keys.GetValueOrDefault(EditDraftSwitch.EnabledKey), keys.GetValueOrDefault(EditDraftSwitch.TypeKey(id)), keys.GetValueOrDefault(EditDraftSwitch.ListViewsKey));
            var stillEnabled = EditDraftWriteGate.Bind("Note", IsListEnabled);
            stillEnabled().Should().BeTrue();
            keys[EditDraftSwitch.ListViewsKey] = "false";
            stillEnabled().Should().BeFalse("switched off after queueing: the queued write is skipped");
            keys[EditDraftSwitch.ListViewsKey] = "true";
            stillEnabled().Should().BeTrue();
            Wave1.Source(Wave1b.ListCapture)
                .Should().Contain("var stillEnabled = EditDraftWriteGate.Bind(_policy?.PolicyId, id => EditDraftSwitch.IsListEnabled(services, id));")
                .And.Contain("EditDraftCaptureController.RunOneWrite(slot, writer, running, mark.Raise, onFreshStart, stillEnabled)");
        }
    }

    [TestFixture]
    public class EditDraftWave1bWiringScanTests
    {
        [Test]
        public void E10b_only_Committed_retires_and_the_Committing_handler_only_marks_a_save_attempt()
        {
            // Codex diffreview C2 added a Committing subscription; the requirement E10/E11 is that retirement happens on
            // Committed only. The Committing handler is inspected for what it may NOT do.
            var src = Wave1.Source(Wave1b.ListCapture);
            var start = src.IndexOf("private void ObjectSpace_Committing(", StringComparison.Ordinal);
            start.Should().BeGreaterThan(0);
            var body = src.Substring(start, src.IndexOf("private void ObjectSpace_ObjectReloaded(", StringComparison.Ordinal) - start);
            body.Should().NotContain("OnSaved").And.NotContain("DeleteOwn").And.NotContain("_contexts.Clear").And.NotContain("Detach(");
            body.Should().Contain("_saveAttemptThisTurn = true;").And.Contain("Post(_ => _saveAttemptThisTurn = false, null)");
            src.Should().Contain("var own = ctx.Slot.OnSaved();");
        }

        [Test]
        public void C2_a_vetoed_save_completing_the_grid_edit_in_the_same_turn_is_not_a_cancel()
        {
            var src = Wave1.Source(Wave1b.ListCapture);
            src.Should().Contain("_objectSpace.Committing += ObjectSpace_Committing;").And.Contain("_objectSpace.Committing -= ObjectSpace_Committing;");
            var completed = src.Substring(src.IndexOf("private void Grid_EditingCompleted(", StringComparison.Ordinal));
            completed.IndexOf("if (_saveAttemptThisTurn)", StringComparison.Ordinal).Should().BeLessThan(completed.IndexOf("Detach(ctx, \"row cancelled (✕)\"", StringComparison.Ordinal),
                "the vetoed-save check comes before the cancel");
            src.Should().Contain("_saveAttemptThisTurn = false;\r\n        if (_contexts.Count == 0) return;", "a successful commit clears the mark");
        }

        [Test]
        public void E13b_E14_E31_cancel_reload_and_rollback_keep_the_draft_and_batch_or_commit_time_completions_do_not_detach()
        {
            // The original E13_E14_E31 test was deleted on owner ruling 2026-10-01: it pinned the pre-review call text
            // Detach(ctx, "...") that the Codex diffreview C1 fix changed. The same requirement on the current text:
            var src = Wave1.Source(Wave1b.ListCapture);
            src.Should().Contain("if (IsBatch()) return;", "a Batch row completion is neither a save nor a cancel");
            src.Should().Contain("if (_objectSpace == null || _objectSpace.IsCommitting) return;   // a reload inside a commit is not a cancel");
            src.Should().Contain("try { if (revertReversals) reverted = ctx.RevertReversals(); }").And.Contain("FlushPosted(ctx, why);");
            src.Should().Contain("Detach(ctx, \"row cancelled (✕)\", revertReversals: ctx.SessionStartedModified);")
                .And.Contain("Detach(ctx, \"row reloaded\", revertReversals: false);")
                .And.Contain("Detach(ctx, \"list reloaded/rolled back\", revertReversals: false);");
            src.Should().NotContain("TrySoftDiscard", "B3: cancel never discards the draft");
        }

        [Test]
        public void C6_switched_off_or_ownerless_EditingStarted_creates_no_context_and_runs_no_getter()
        {
            var src = Wave1.Source(Wave1b.ListCapture);
            var started = src.Substring(src.IndexOf("private void Grid_EditingStarted(", StringComparison.Ordinal));
            var gate = started.IndexOf("if (!ListEnabled() || EditDraftServices.CurrentOwner(Application?.ServiceProvider, _objectSpace, _policy).IsNone) return;", StringComparison.Ordinal);   // 0.4.0-preview.1: with the policy
            gate.Should().BeGreaterThan(0);
            gate.Should().BeLessThan(started.IndexOf("new EditDraftRowContext(", StringComparison.Ordinal));
            gate.Should().BeLessThan(started.IndexOf("RunInitializingGetters", StringComparison.Ordinal));
        }

        [Test]
        public void C3_C4_DetailView_capture_badges_the_record_and_open_decides_on_a_fresh_read()
        {
            Wave1.Source("Xaf.EditDraft.Core/EditDraftCaptureController.cs")
                .Should().Contain("as EditDraftBadgeNotifier)?.Written(_policy.TypeName, RecordOid());");
            var badge = Wave1.Source(Wave1b.Badge);
            var open = badge.Substring(badge.IndexOf("private void OpenAction_Execute(", StringComparison.Ordinal));
            open.Should().Contain("ReloadSet(\"開く\");\r\n        if (!_set.Contains(oid))", "the decision is made on a fresh owner-scoped read");
        }

        [Test]
        public void E15_close_and_detach_start_a_posted_write_and_a_retired_or_detached_posted_write_does_nothing()
        {
            var src = Wave1.Source(Wave1b.ListCapture);
            src.Should().Contain("foreach (var ctx in _contexts.Values.ToList()) FlushPosted(ctx, \"screen closed\");");
            src.Should().Contain("if (gesture != ctx.Gesture) return;").And.Contain("if (!_contexts.TryGetValue(ctx.TargetOid, out var live) || !ReferenceEquals(live, ctx)) return;");
        }

        [Test]
        public void E16_E17_open_goes_to_the_approved_root_DetailView_by_key_and_nothing_is_applied_into_a_grid_row()
        {
            var badge = Wave1.Source(Wave1b.Badge);
            badge.Should().Contain("Application.CreateDetailView(os, detailViewId, true, target)").And.Contain("TargetWindow.NewModalWindow");
            badge.Should().Contain("var detailViewId = _policy.ApprovedViewIds?.FirstOrDefault();");
            badge.Should().Contain("if (IsEditing())", "an open row edit is never resolved silently");
            badge.Should().Contain("EditDraftServices.MayRestore(Application, _policy, target)");   // 0.4.0-preview.1: replaces the record-access seam
            foreach (var file in new[] { Wave1b.Badge, Wave1b.ListCapture })
            {
                var s = Wave1.Source(file);
                s.Should().NotContain("ApplyExisting").And.NotContain("TryClaim").And.NotContain("TryAttachClaimed").And.NotContain("SetMemberValue")
                    .And.NotContain("StartEditRowAsync", file + ": B2 — no values into a grid row");
            }
        }
    }

    [TestFixture]
    public class EditDraftWave1bBadgeAndProvenanceTests
    {
        [Test]
        public void E24_E26_the_badge_set_is_one_hash_lookup_reveals_presence_only_and_adds_its_class_once()
        {
            var set = new EditDraftBadgeSet();
            var a = Guid.NewGuid(); var b = Guid.NewGuid();
            set.Replace(new[] { a, a, Guid.Empty });
            set.Count.Should().Be(1);
            set.Contains(a).Should().BeTrue(); set.Contains(b).Should().BeFalse(); set.Contains(Guid.Empty).Should().BeFalse();
            set.Add(b).Should().BeTrue(); set.Add(b).Should().BeFalse(); set.Add(Guid.Empty).Should().BeFalse();
            set.Replace(null); set.Count.Should().Be(0);
            EditDraftBadgeSet.AppendClass(null).Should().Be("edit-draft-row");
            EditDraftBadgeSet.AppendClass("dxbl-grid-row").Should().Be("dxbl-grid-row edit-draft-row");
            EditDraftBadgeSet.AppendClass("dxbl-grid-row edit-draft-row").Should().Be("dxbl-grid-row edit-draft-row", "never twice");

            var writer = Wave1.Source("Xaf.EditDraft.Core/EditDraftWriter.cs");
            writer.Should().Contain(".Where(d => d.OwnerUserOid == ownerOid && d.ExpiresOn > now && d.DeletedOn == null && d.ObjectType == objectType)")
                .And.Contain(".Select(d => d.TargetOid)", "a projection: no payload is read for a badge");
            var badge = Wave1.Source(Wave1b.Badge);
            badge.Should().Contain("writer.ListOwnTargets(readSpace, owner.Oid, _policy.TypeName, Now(), out var readFailed)");
            badge.Should().Contain("e.CssClass = EditDraftBadgeSet.AppendClass(e.CssClass)").And.NotContain("e.Style", "a class only, no inline text");
            foreach (Match m in Regex.Matches(badge, "EditDraftLog\\.\\w+\\(\\$\"[^\"]*\""))
                m.Value.Should().NotContain("oid}").And.NotContain("Oid}").And.NotContain("Short(", "S7: badge logs carry counts, never Oids");
        }

        [Test]
        public void E21_provenance_names_the_origin_and_the_view_caption_and_never_the_editor_id()
        {
            EditDraftProvenance.Origin(true, "メモ").Should().Be("一覧から（メモ）");
            EditDraftProvenance.Origin(false, "メモ").Should().Be("詳細から（メモ）");
            EditDraftProvenance.Origin(true, "  ").Should().Be("一覧から");
            EditDraftProvenance.Origin(null, "x").Should().Be(EditDraftProvenance.Unknown);
            EditDraftProvenance.Resolve(null, "Note_ListView").Should().Be("由来不明", "a view the model no longer has");
            var restore = Wave1.Source("Xaf.EditDraft.Blazor/EditDraftRestoreControllerBlazor.cs");
            restore.Should().Contain("EditDraftProvenance.Resolve(Application?.Model, draft.ViewId)");
            restore.Should().NotContain("EditorInstanceId}", "operational, never shown");
            Wave1.Source("Xaf.EditDraft.Blazor/EditDraftListControllerBlazor.cs").Should().Contain("Origin = EditDraftProvenance.Resolve(Application.Model, d.ViewId)");
            // Library M3 (localisation): the class declares the English caption; the caption shown is the text set's (here: Japanese).
            Wave1.Source("Xaf.EditDraft.Blazor/EditDraftModels.cs").Should().Contain("[ModelDefault(\"AllowEdit\", \"False\")] public string Origin { get; set; }");
            EditDraftModelCaptions.Find(EditDraftPopupCaptions.All, typeof(EditDraftListItem), nameof(EditDraftListItem.Origin), EditDraftTextSet.Japanese).Should().Be("由来");
        }
    }
}
