using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests
{
    // Generic edit-draft restore, wave 1 (owner decisions D1-D17; design docs/generic-edit-draft-design-2026-09-30.md).
    // Expectations come from the Codex requirement-only lists written BEFORE any code was shown to it:
    //   W1-W46  run 2026-10-01-editdraft-wave1-ea729d, tests a1 (the brief)
    //   W47-W57 run 2026-10-01-editdraft-wave1-ea729d, tests a2 (the D16/D17 addendum)
    // Labels Wn refer to them. Logic tests run against pure code or an in-memory object space; scan
    // tests read source text. Controller timing (arming, the popup, the guard inside the dispatcher)
    // and every behaviour marked B in the design are the Dev2 browser pass, not this file.
    // Library milestone M3 (run 2026-10-02-editdraft-m3-1b4d82): moved here from NursingHome_Chart.Rostering.Tests
    // (EditDraftWave1Tests.cs) with only the namespace changed — the wave-1 tests on the library's own rules and source.
    // The tests on the application's wave-1 classes, policies, files and the NHM mirror stay in that project. The helper
    // below is the part of that project's Wave1 helper these tests use (repository root, source text, payload).

    internal static class Wave1
    {
        public static string Root()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            // A git WORKTREE has a .git FILE, a clone a .git DIRECTORY: both are the repository root here.
            while (dir != null && !dir.EnumerateDirectories(".git").Any() && !dir.EnumerateFiles(".git").Any()) dir = dir.Parent;
            if (dir == null) Assert.Ignore("repository root not found");
            return dir.FullName;
        }

        public static string Source(string rel)
        {
            var path = Path.Combine(Root(), rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) Assert.Ignore($"{rel} is not in this repository (CareCrew-only host file).");
            return File.ReadAllText(path);
        }

        public static EditDraftPayload Payload(string type, params (string Path, string Kind, bool BaseKnown, string BaseRaw, string ValueRaw)[] entries)
        {
            var p = new EditDraftPayload { TypeName = type };
            foreach (var e in entries) p.Upsert(e.Path, e.Kind, e.Path, e.BaseKnown, e.BaseRaw, e.BaseRaw, e.ValueRaw, e.ValueRaw);
            return p;
        }
    }

    [TestFixture]
    public class EditDraftWave1GatingTests
    {
        [TestCase("true", "true", true)]
        [TestCase("true", "false", false)]
        [TestCase("false", "true", false)]
        [TestCase("false", "false", false)]
        [TestCase(null, "true", false)]
        [TestCase("true", null, false)]
        [TestCase("", "true", false)]
        [TestCase("yes", "true", false)]
        [TestCase("True", "TRUE", true)]
        public void W7_capture_needs_both_switches_true_and_anything_else_is_off(string global, string type, bool expected)
        {
            EditDraftSwitch.Decide(global, type).Should().Be(expected);
        }

        [Test]
        public void W7_the_per_type_key_names_the_policy_id_so_another_type_s_switch_cannot_enable_it()
        {
            EditDraftSwitch.TypeKey("ToDo").Should().Be("EditDraftCapture:Types:ToDo:Enabled");
            EditDraftSwitch.EnabledKey.Should().Be("EditDraftCapture:Enabled");
            EditDraftSwitch.TypeKey("ToDo").Should().NotBe(EditDraftSwitch.TypeKey("TenantCase"));
        }

        [Test]
        public void W10_D6_owner_rule_login_owns_general_user_never_unreadable_staff_never_non_staff_login_owns()
        {
            var login = Guid.NewGuid();
            EditDraftOwnerRule.Decide(login, loginIsStaffMember: true, staffFound: true, staffIsGeneralUser: false).Should().Be(login);
            EditDraftOwnerRule.Decide(login, true, true, staffIsGeneralUser: true).Should().Be(Guid.Empty, "D6: a GeneralUser login never owns a draft");
            EditDraftOwnerRule.Decide(login, true, staffFound: false, false).Should().Be(Guid.Empty, "a StaffMember that cannot be read: no owner");
            EditDraftOwnerRule.Decide(login, loginIsStaffMember: false, false, false).Should().Be(login, "an administrator (non-StaffMember) login owns its drafts");
            EditDraftOwnerRule.Decide(null, true, true, false).Should().Be(Guid.Empty);
            EditDraftOwnerRule.Decide(Guid.Empty, true, true, false).Should().Be(Guid.Empty);
        }

        [Test]
        public void W20b_no_owner_at_apply_time_refuses()
        {
            // W20_W24 above is RED because its helper replaces every Guid.Empty argument by `me` (a test defect,
            // escalated, not revised). The no-owner case is asserted here with explicit arguments.
            var rec = Guid.NewGuid();
            EditDraftAccessRule.MayApply(Guid.Empty, Guid.Empty, Guid.Empty, rec, rec, rec, false, false, false, 3, 3, true).Should().BeFalse("no owner");
            var me = Guid.NewGuid();
            EditDraftAccessRule.MayApply(me, me, me, rec, rec, rec, false, false, false, 3, 3, true).Should().BeTrue();
            EditDraftAccessRule.MayApply(me, me, me, Guid.Empty, Guid.Empty, Guid.Empty, false, false, false, 3, 3, true).Should().BeFalse("no record");
        }
    }

    [TestFixture]
    public class EditDraftWave1OfferMergeTests
    {
        private static EditDraftRestoreRow Item(string path, EditDraftItemStatus status, string group = null) => new()
        {
            Path = path, Label = path, Group = group, StatusCode = (int)status,
            Selectable = EditDraftComparison.IsSelectable(status), Selected = EditDraftComparison.IsPreSelected(status, false),
            StatusText = EditDraftComparison.StatusText(status, false)
        };

        [Test]
        public void W47_W49_two_live_drafts_of_one_record_are_offered_together_newest_first_each_row_marked_with_its_draft()
        {
            var newer = Guid.NewGuid(); var older = Guid.NewGuid();
            var rows = EditDraftOfferMerge.Merge(new List<(Guid, List<EditDraftRestoreRow>)>
            {
                (newer, new List<EditDraftRestoreRow> { Item("Description", EditDraftItemStatus.Clean) }),
                (older, new List<EditDraftRestoreRow> { Item("ToDoEnum", EditDraftItemStatus.Clean) })
            }, new HashSet<string>());
            rows.Select(r => r.DraftOid).Should().Equal(newer, older);
            rows[0].Item.Label.Should().Be("① Description");
            rows[1].Item.Label.Should().Be("② ToDoEnum");
            rows.Should().OnlyContain(r => r.Item.Selected, "disjoint clean members of both drafts are pre-ticked");
            EditDraftOfferMerge.MarkerFor(2).Should().Be("③");
            EditDraftOfferMerge.MarkerFor(7).Should().Be("(8)");
        }

        [Test]
        public void W50_W51_the_same_member_in_an_older_draft_stays_available_but_is_not_pre_ticked_and_the_newer_value_wins_when_both_are_ticked()
        {
            var newer = Guid.NewGuid(); var older = Guid.NewGuid();
            var rows = EditDraftOfferMerge.Merge(new List<(Guid, List<EditDraftRestoreRow>)>
            {
                (newer, new List<EditDraftRestoreRow> { Item("Description", EditDraftItemStatus.Clean) }),
                (older, new List<EditDraftRestoreRow> { Item("Description", EditDraftItemStatus.Clean), Item("ToDoEnum", EditDraftItemStatus.Clean) })
            }, new HashSet<string>());
            rows.Should().HaveCount(3, "W50: the older draft's rows are not masked by the newer one");
            var dup = rows[1];
            dup.DraftOid.Should().Be(older);
            dup.Item.Selectable.Should().BeTrue();
            dup.Item.Selected.Should().BeFalse("never pre-ticked");
            dup.Item.StatusText.Should().EndWith(EditDraftOfferMerge.DuplicateNote);
            rows[0].Item.Selected.Should().BeTrue();

            foreach (var r in rows) r.Item.Selected = true;   // the person ticks everything
            var chosen = EditDraftOfferMerge.FirstPerPath(rows, out var skipped);
            chosen.Select(r => (r.Item.Path, r.DraftOid)).Should().Equal(("Description", newer), ("ToDoEnum", older));
            skipped.Should().Be(1);
        }

        [Test]
        public void W22_a_member_the_login_may_not_write_is_shown_as_not_restorable_in_every_draft_and_groups_are_draft_local()
        {
            var a = Guid.NewGuid(); var b = Guid.NewGuid();
            var rows = EditDraftOfferMerge.Merge(new List<(Guid, List<EditDraftRestoreRow>)>
            {
                (a, new List<EditDraftRestoreRow> { Item("X", EditDraftItemStatus.Clean, "T:g"), Item("Y", EditDraftItemStatus.Clean, "T:g") }),
                (b, new List<EditDraftRestoreRow> { Item("X", EditDraftItemStatus.Clean, "T:g") })
            }, new HashSet<string> { "Y" });
            var y = rows.Single(r => r.Item.Path == "Y").Item;
            y.Selectable.Should().BeFalse(); y.Selected.Should().BeFalse(); y.StatusText.Should().Be("戻せません");
            rows.Where(r => r.Item.Path == "X").Select(r => r.Item.Group).Should().Equal("①T:g", "②T:g");
        }

        [Test]
        public void W49_a_single_draft_is_shown_without_markers()
        {
            var rows = EditDraftOfferMerge.Merge(new List<(Guid, List<EditDraftRestoreRow>)>
            {
                (Guid.NewGuid(), new List<EditDraftRestoreRow> { Item("Description", EditDraftItemStatus.Clean, "T:g") })
            }, null);
            rows.Single().Item.Label.Should().Be("Description");
            rows.Single().Item.Group.Should().Be("①T:g");
        }
    }

    [TestFixture]
    public class EditDraftWave1WiringScanTests
    {
        private const string Writer = "Xaf.EditDraft.Core/EditDraftWriter.cs";

        [Test]
        public void W38b_the_four_mutations_each_name_the_owner_including_the_multi_line_supersede()
        {
            // W38 above is RED on its statement COUNT: the supersede statement is assigned to a variable and ends
            // in ';', which its regex (the chart C28 shape) does not match — a test defect, escalated, not revised.
            // Each mutation is asserted here by its own text.
            var writer = Wave1.Source(Writer);
            writer.Should().Contain("WHERE [Oid] = @p5 AND [Revision] = @p6 AND [OwnerUserOid] = @p7 AND [DeletedOn] IS NULL AND [ExpiresOn] > @p2", "supersede");
            writer.Should().Contain("WHERE [Oid] = @p2 AND [Revision] = @p3 AND [OwnerUserOid] = @p4 AND [ExpiresOn] > @p1", "claim");
            writer.Should().Contain("DELETE FROM [{Table}] WHERE [Oid] = @p0 AND [OwnerUserOid] = @p1 AND [EditorInstanceId] = @p2", "delete on save");
            writer.Should().Contain("SET [DeletedOn] = @p2 WHERE [Oid] = @p0 AND [OwnerUserOid] = @p1 AND [DeletedOn] IS NULL", "discard");
            Regex.Matches(writer, @"\b(UPDATE|DELETE FROM) \[\{Table\}\]").Count.Should().Be(4, "exactly these four mutations exist");
            Regex.Matches(writer, @"\[OwnerUserOid\] = @p\d").Count.Should().Be(4, "and each names the owner once");
        }

        /// <summary>
        /// Codex diffreview a2 C1 / tests a3 W58-W66: a retired slot's fresh-start rewrite checks ITS OWN policy's
        /// switch (global AND per-type), bound at write start and re-evaluated at the callback. The predicate is the
        /// pure EditDraftWriteGate.Bind; the switch reader is simulated by a dictionary that the test flips between
        /// "write start" and the delayed callback. The controller's wiring is pinned by a source scan.
        /// </summary>
        [Test]
        public void W58_W66_a_retired_slot_checks_its_own_policy_switch_never_the_current_screen_s()
        {
            var keys = new Dictionary<string, bool>(StringComparer.Ordinal);
            bool IsEnabled(string policyId) => EditDraftSwitch.Decide(keys.TryGetValue(EditDraftSwitch.EnabledKey, out var g) ? g.ToString() : null,
                                                                      keys.TryGetValue(EditDraftSwitch.TypeKey(policyId), out var t) ? t.ToString() : null);
            void Set(bool global, bool a, bool b) { keys[EditDraftSwitch.EnabledKey] = global; keys[EditDraftSwitch.TypeKey("A")] = a; keys[EditDraftSwitch.TypeKey("B")] = b; }

            // W58: bound to A at write start (A on). The context then moves to B, to no policy, or stays on A: the
            // predicate carries "A" itself, so the current policy never enters the decision.
            Set(global: true, a: true, b: false);
            var slotA = EditDraftWriteGate.Bind("A", IsEnabled);
            var currentPolicyAfterMove = (string)null;                        // W61: no admitted policy on the screen
            var predicateOfCurrentScreen = EditDraftWriteGate.Bind(currentPolicyAfterMove, IsEnabled);
            slotA().Should().BeTrue("W59: A on, B off -> A's pending input is rewritten");
            predicateOfCurrentScreen().Should().BeFalse("the current screen (no policy) would refuse; the retired slot must not use it");

            Set(global: true, a: false, b: true);
            slotA().Should().BeFalse("W60: A off, B on -> dropped; B's switch does not authorise A");
            EditDraftWriteGate.Bind("B", IsEnabled)().Should().BeTrue("B's own slot would write; irrelevant to A");

            Set(global: true, a: true, b: true);
            slotA().Should().BeTrue("W62: same policy A on another record: still A, still on");
            Set(global: false, a: true, b: true);
            slotA().Should().BeFalse("W63: the global key off drops A whatever the current context");

            Set(global: true, a: true, b: false);
            slotA().Should().BeTrue();
            keys[EditDraftSwitch.TypeKey("B")] = true;
            slotA().Should().BeTrue("W64: only B's key changed; A's outcome is unchanged");
            keys[EditDraftSwitch.TypeKey("A")] = false;
            slotA().Should().BeFalse("W65: A on at write start, off before the callback -> dropped (identity bound, evaluation live)");
            Set(global: true, a: true, b: false);
            slotA().Should().BeTrue("W66: retirement alone (context moved, B disabled) loses nothing while global and A are on");

            EditDraftWriteGate.Bind(null, IsEnabled)().Should().BeFalse("a slot without a policy never writes");
            EditDraftWriteGate.Bind("A", _ => throw new InvalidOperationException())().Should().BeFalse("a failing reader is off (fail closed)");

            // The controller binds the predicate ONCE at StartWrite and hands it to every fresh-start path.
            var capture = Wave1.Source("Xaf.EditDraft.Core/EditDraftCaptureControllerBlazor.cs");
            capture.Should().Contain("var stillEnabled = EditDraftWriteGate.Bind(_policy?.PolicyId, id => EditDraftSwitch.IsEnabled(services, id));");
            capture.Should().Contain("circuit.Post(_ => ResumeAfterFreshStart(slot, mark, newest, writer, stillEnabled), null);");
            capture.Should().Contain("if (!ReferenceEquals(slot, _slot)) { RetiredFreshStart(slot, mark, newest, writer, stillEnabled); return; }");
            capture.Should().NotContain("() => EditDraftSwitch.IsEnabled(Application?.ServiceProvider, _policy?.PolicyId)", "no predicate is rebuilt from the current screen's policy");
        }
    }
}
