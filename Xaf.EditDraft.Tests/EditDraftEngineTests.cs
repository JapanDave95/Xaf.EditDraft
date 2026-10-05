using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.DC.Xpo;
using DevExpress.ExpressApp.Xpo;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using FluentAssertions;
using NUnit.Framework;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests
{
    // Generic edit-draft engine, milestone 1 (design docs/generic-edit-draft-design-2026-09-30.md).
    // Expectations come from the Codex requirement-only lists, not from the code:
    //   E1–E36  run 2026-09-30-generic-edit-draft-7faa17, tests a1
    //   M1–M65  run 2026-09-30-generic-edit-draft-m1-43aad2, tests a1
    // Labels En / Mn refer to them. The engine is exercised with TEST-ONLY types and policies in a
    // registry of its own; nothing here registers a production type. XPO paths run against an
    // in-memory object space (no database, no host). Controller and browser behaviour is NOT covered
    // here (capture, the claim, the popup): that is the browser pass.
    // Library milestone M3 (run 2026-10-02-editdraft-m3-1b4d82): the tests that need the two library assemblies only.

    /// <summary>A test-only record with two setter cascades (a driver that fills another member only when it is empty), an outside member and a locked one.</summary>
    public class EditDraftProbeA : BaseObject
    {
        public EditDraftProbeA(Session session) : base(session) { }

        private string _note;
        public string Note { get => _note; set => SetPropertyValue(nameof(Note), ref _note, value); }

        private string _status;
        public string Status { get => _status; set => SetPropertyValue(nameof(Status), ref _status, value); }

        private string _locked;
        public string Locked { get => _locked; set => SetPropertyValue(nameof(Locked), ref _locked, value); }

        private DateTime _stamp;
        public DateTime Stamp { get => _stamp; set => SetPropertyValue(nameof(Stamp), ref _stamp, value); }

        private bool _flag;
        /// <summary>Driver of group 1: fills Number = 1 only from 0.</summary>
        public bool Flag
        {
            get => _flag;
            set
            {
                SetPropertyValue(nameof(Flag), ref _flag, value);
                if (!IsLoading && !IsSaving && value && Number == 0) Number = 1;
            }
        }

        private int _number;
        public int Number { get => _number; set => SetPropertyValue(nameof(Number), ref _number, value); }

        private string _text;
        /// <summary>Driver of group 2: fills When = Stamp only when When is empty.</summary>
        public string Text
        {
            get => _text;
            set
            {
                SetPropertyValue(nameof(Text), ref _text, value);
                if (!IsLoading && !IsSaving && When == DateTime.MinValue) When = Stamp;
            }
        }

        private DateTime _when;
        public DateTime When { get => _when; set => SetPropertyValue(nameof(When), ref _when, value); }
    }

    /// <summary>A second test-only record with members of the SAME names as <see cref="EditDraftProbeA"/>.</summary>
    public class EditDraftProbeB : BaseObject
    {
        public EditDraftProbeB(Session session) : base(session) { }

        private string _note;
        public string Note { get => _note; set => SetPropertyValue(nameof(Note), ref _note, value); }

        private string _status;
        public string Status { get => _status; set => SetPropertyValue(nameof(Status), ref _status, value); }

        private int _number;
        public int Number { get => _number; set => SetPropertyValue(nameof(Number), ref _number, value); }
    }

    /// <summary>A subclass of a registered test type: NOT registered itself.</summary>
    public class EditDraftProbeASub : EditDraftProbeA
    {
        public EditDraftProbeASub(Session session) : base(session) { }
    }

    internal static class ProbeSpace
    {
        private static readonly Lazy<XPObjectSpaceProvider> Provider = new(() =>
        {
            FrameworkSettings.DefaultSettingsCompatibilityMode = FrameworkSettingsCompatibilityMode.Latest;
            var typesInfo = new TypesInfo();
            var source = new XpoTypeInfoSource(typesInfo);
            typesInfo.AddEntityStore(source);
            typesInfo.RegisterEntity(typeof(EditDraftProbeA));
            typesInfo.RegisterEntity(typeof(EditDraftProbeB));
            typesInfo.RegisterEntity(typeof(EditDraftProbeASub));
            return new XPObjectSpaceProvider((IXpoDataStoreProvider)new MemoryDataStoreProvider(), (ITypesInfo)typesInfo, source, true, false);
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        public static IObjectSpace Create() => Provider.Value.CreateObjectSpace();

        public const string G1 = "EditDraftProbeA:flag";
        public const string G2 = "EditDraftProbeA:text";

        public static EditDraftTypePolicy PolicyA() => new EditDraftTypePolicy(typeof(EditDraftProbeA))
        {
            PolicyId = "test:A",
            ReconstructionOrder = new[] { "Stamp" },
            Groups = new[]
            {
                new EditDraftGroup(G1, new[] { "Flag", "Number" }),
                new EditDraftGroup(G2, new[] { "Text", "When" }),
            },
            SideEffectMembers = new HashSet<string> { "Status" },
            NotRestorableOnExisting = new HashSet<string> { "Locked" },
        };

        public static EditDraftTypePolicy PolicyB() => new EditDraftTypePolicy(typeof(EditDraftProbeB))
        {
            PolicyId = "test:B",
            Excluded = new HashSet<string> { "Note" },
        };

        public static EditDraftPayload Payload(string typeName, params (string Path, string Kind, bool BaseKnown, string BaseRaw, string ValueRaw)[] entries)
        {
            var p = new EditDraftPayload { TypeName = typeName };
            foreach (var e in entries) p.Upsert(e.Path, e.Kind, e.Path, e.BaseKnown, e.BaseRaw, e.BaseRaw, e.ValueRaw, e.ValueRaw);
            return p;
        }

        public static readonly string Empty = EditDraftCodec.RawOf(DateTime.MinValue);
        public static readonly DateTime Stamp0 = new(2026, 9, 27, 10, 0, 0);
        public static readonly string Stamp0Raw = EditDraftCodec.RawOf(Stamp0);

        /// <summary>The assignments an apply makes, as "Member=raw" in order (ObjectChanged of the record).</summary>
        public static List<string> Trace(IObjectSpace os, object record, Action act)
        {
            var trace = new List<string>();
            void OnChanged(object s, ObjectChangedEventArgs e)
            {
                if (ReferenceEquals(e.Object, record) && !string.IsNullOrEmpty(e.PropertyName))
                    trace.Add(e.PropertyName + "=" + (EditDraftCodec.RawOf(EditDraftMembers.GetValue(record, e.PropertyName)) ?? "<null>"));
            }
            os.ObjectChanged += OnChanged;
            try { act(); } finally { os.ObjectChanged -= OnChanged; }
            return trace;
        }
    }

    [TestFixture]
    public class EditDraftRegistryTests
    {
        [Test]
        public void M1_a_matching_shape_or_a_subclass_does_not_register_a_type()
        {
            var registry = new EditDraftRegistry();
            registry.Register(ProbeSpace.PolicyA());
            registry.Find(typeof(EditDraftProbeA)).Should().NotBeNull();
            registry.Find(typeof(EditDraftProbeASub)).Should().BeNull("matching is by the exact type; a subclass needs its own audited policy");
            registry.Find("EditDraftProbeASub").Should().BeNull();
        }

        [Test]
        public void A_type_is_registered_once()
        {
            var registry = new EditDraftRegistry();
            registry.Register(ProbeSpace.PolicyA());
            FluentActions.Invoking(() => registry.Register(ProbeSpace.PolicyA())).Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => registry.Register(null)).Should().Throw<ArgumentNullException>();
            registry.All.Should().HaveCount(1);
        }

        [Test]
        public void M2_M3_E2_two_types_with_same_named_members_follow_their_own_policies_in_either_query_order()
        {
            foreach (var aFirst in new[] { true, false })
            {
                var registry = new EditDraftRegistry();
                if (aFirst) { registry.Register(ProbeSpace.PolicyA()); registry.Register(ProbeSpace.PolicyB()); }
                else { registry.Register(ProbeSpace.PolicyB()); registry.Register(ProbeSpace.PolicyA()); }

                EditDraftTypePolicy A() => registry.Find(typeof(EditDraftProbeA));
                EditDraftTypePolicy B() => registry.Find("EditDraftProbeB");
                void CheckA()
                {
                    A().Members.Select(m => m.Path).Should().Equal("Stamp", "Flag", "Locked", "Note", "Number", "Status", "Text", "When");
                    A().Find("Note").Should().NotBeNull("A captures Note");
                    A().HasSideEffect("Status").Should().BeTrue();
                    A().GroupOf("Number").Should().Be(ProbeSpace.G1);
                    A().IsNotRestorableOnExisting("Locked").Should().BeTrue();
                }
                void CheckB()
                {
                    B().Members.Select(m => m.Path).Should().Equal("Number", "Status");
                    B().Find("Note").Should().BeNull("B excludes Note; A's list does not apply to B and B's does not apply to A");
                    B().HasSideEffect("Status").Should().BeFalse();
                    B().GroupOf("Number").Should().BeNull();
                    B().IsNotRestorableOnExisting("Locked").Should().BeFalse();
                }
                if (aFirst) { CheckA(); CheckB(); CheckA(); } else { CheckB(); CheckA(); CheckB(); }
            }
        }
    }

    [TestFixture]
    public class EditDraftPayloadTextTests
    {
        [Test]
        public void M50_E6_a_cleared_value_is_null_and_stays_distinct_from_an_empty_text()
        {
            var p = new EditDraftPayload { TypeName = "X" };
            p.Upsert("A", "string", "a", true, "old", "old", null, "");
            p.Upsert("B", "string", "b", true, "old", "old", "", "");
            var back = EditDraftPayload.FromJson<EditDraftPayload>(p.ToJson());
            back.Get("A").ValueRaw.Should().BeNull();
            back.Get("B").ValueRaw.Should().Be("").And.NotBeNull();
            back.Get("A").BaseRaw.Should().Be("old", "the first baseline is kept");
        }
    }

    [TestFixture]
    public class EditDraftMultiGroupTests
    {
        private static readonly EditDraftTypePolicy A = ProbeSpace.PolicyA();

        private static EditDraftProbeA NewA(IObjectSpace os)
        {
            var a = os.CreateObject<EditDraftProbeA>();
            a.Stamp = ProbeSpace.Stamp0;
            return a;
        }

        [Test]
        public void M13_a_policy_keeps_several_named_groups_each_with_its_driver_and_order()
        {
            A.Groups.Select(g => g.Id).Should().Equal(ProbeSpace.G1, ProbeSpace.G2);
            A.Groups[0].Driver.Should().Be("Flag");
            A.Groups[1].Driver.Should().Be("Text");
            A.GroupOf("Flag").Should().Be(ProbeSpace.G1);
            A.GroupOf("Number").Should().Be(ProbeSpace.G1);
            A.GroupOf("Text").Should().Be(ProbeSpace.G2);
            A.GroupOf("When").Should().Be(ProbeSpace.G2, "the last group is not skipped");
            A.GroupOf("Note").Should().BeNull("a member outside every group");
            A.GroupOf("Stamp").Should().BeNull("an ordering prerequisite is not a group member");
        }

        [Test]
        public void M25_overlapping_duplicate_or_incomplete_groups_are_refused()
        {
            EditDraftTypePolicy With(params EditDraftGroup[] groups) => new(typeof(EditDraftProbeA)) { Groups = groups };
            FluentActions.Invoking(() => With(new EditDraftGroup("g1", new[] { "Flag", "Number" }), new EditDraftGroup("g2", new[] { "Text", "Number" })))
                .Should().Throw<ArgumentException>("a member written by two drivers means ONE group");
            FluentActions.Invoking(() => With(new EditDraftGroup("g", new[] { "Flag", "Number" }), new EditDraftGroup("g", new[] { "Text", "When" })))
                .Should().Throw<ArgumentException>("two groups with one id would tick together in the popup");
            FluentActions.Invoking(() => With(new EditDraftGroup("", new[] { "Flag", "Number" }))).Should().Throw<ArgumentException>();
            FluentActions.Invoking(() => With(new EditDraftGroup("g", new[] { "Flag" }))).Should().Throw<ArgumentException>("a driver alone is not a group");
            FluentActions.Invoking(() => With(new EditDraftGroup("g", new[] { "Flag", "Flag" }))).Should().Throw<ArgumentException>();
            With().Groups.Should().BeEmpty();
        }

        [Test]
        public void M20_apply_order_is_context_then_every_driver_then_the_rest_then_each_groups_final_values()
        {
            EditDraftRestorer.ApplyOrder(A, new[] { "When", "Note", "Number", "Text", "Stamp", "Flag", "Status" })
                .Should().Equal("Stamp", "Flag", "Text", "Note", "Number", "Status", "When", "Number", "When");
            EditDraftRestorer.ApplyOrder(A, new[] { "When", "Text", "Note" })
                .Should().Equal(new[] { "Text", "Note", "When", "When" }, "only the second group is touched; its dependent comes again at the end");
            EditDraftRestorer.ApplyOrder(A, new[] { "Number", "Note" })
                .Should().Equal(new[] { "Note", "Number", "Number" }, "a dependent without its driver is still repeated at the end");
            EditDraftRestorer.ApplyOrder(A, new[] { "Note", "Status" }).Should().Equal("Note", "Status");
        }

        [Test]
        public void M15_a_chosen_member_expands_to_its_own_group_only_and_only_to_drafted_members()
        {
            var all = ProbeSpace.Payload("EditDraftProbeA", ("Flag", "bool", true, "false", "true"), ("Number", "int", true, "0", "5"),
                ("Text", "string", true, null, "t"), ("When", "datetime", true, ProbeSpace.Empty, ProbeSpace.Stamp0Raw), ("Note", "string", true, null, "n"));
            EditDraftRestorer.ExpandGroups(A, new[] { "Number" }, all).Should().Equal("Number", "Flag");
            EditDraftRestorer.ExpandGroups(A, new[] { "Text" }, all).Should().Equal("Text", "When");
            EditDraftRestorer.ExpandGroups(A, new[] { "Flag", "Number", "Flag" }, all).Should().Equal(new[] { "Flag", "Number" }, "two members of one group are one group application");
            EditDraftRestorer.ExpandGroups(A, new[] { "Note" }, all).Should().Equal(new[] { "Note" }, "a member outside every group selects no group");
            EditDraftRestorer.ExpandGroups(A, new[] { "When", "Flag" }, all).Should().Equal("When", "Flag", "Number", "Text");

            var driverOnly = ProbeSpace.Payload("EditDraftProbeA", ("Flag", "bool", true, "false", "true"));
            EditDraftRestorer.ExpandGroups(A, new[] { "Flag" }, driverOnly).Should().Equal(new[] { "Flag" }, "an undrafted member is never added");
        }

        [Test]
        public void M14_M16_M17_M23_a_conflict_or_an_unknown_baseline_unticks_its_own_group_only()
        {
            using var os = ProbeSpace.Create();
            var a = NewA(os);
            a.Number = 9;                                                        // group 1 dependent changed elsewhere
            var p = ProbeSpace.Payload("EditDraftProbeA",
                ("Flag", "bool", true, "false", "true"), ("Number", "int", true, "0", "5"),            // Number: conflict (now 9)
                ("Text", "string", true, null, "t"), ("When", "datetime", true, ProbeSpace.Empty, ProbeSpace.Stamp0Raw),
                ("Note", "string", true, null, "n"));
            var items = EditDraftRestorer.BuildItems(A, p, a, isNew: false).ToDictionary(i => i.Path);

            items["Number"].StatusCode.Should().Be((int)EditDraftItemStatus.Conflict);
            items["Number"].StatusText.Should().Be("他で変更されています");
            items["Number"].CurrentText.Should().Be("9", "the current value is shown");
            items["Flag"].Selected.Should().BeFalse("the driver follows its group's conflict");
            items["Number"].Selected.Should().BeFalse();
            items["Flag"].Selectable.Should().BeTrue("unticked, not unavailable");
            items["Text"].Selected.Should().BeTrue("the other group keeps its own state");
            items["When"].Selected.Should().BeTrue();
            items["Note"].Selected.Should().BeTrue("a member outside every group keeps ordinary behaviour");
            items["Flag"].Group.Should().Be(ProbeSpace.G1);
            items["When"].Group.Should().Be(ProbeSpace.G2);
            items["Note"].Group.Should().BeNull();

            // Unknown baseline on the DRIVER of group 2, known EMPTY baseline on its dependent.
            var q = ProbeSpace.Payload("EditDraftProbeA",
                ("Text", "string", false, null, "t"), ("When", "datetime", true, ProbeSpace.Empty, ProbeSpace.Stamp0Raw),
                ("Flag", "bool", true, "false", "true"));
            a.Number = 0;
            var second = EditDraftRestorer.BuildItems(A, q, a, isNew: false).ToDictionary(i => i.Path);
            second["Text"].StatusCode.Should().Be((int)EditDraftItemStatus.Unverifiable);
            second["When"].StatusCode.Should().Be((int)EditDraftItemStatus.Clean, "a known empty baseline is not an unknown one");
            second["Text"].Selected.Should().BeFalse();
            second["When"].Selected.Should().BeFalse("an unknown baseline anywhere unticks the group");
            second["Flag"].Selected.Should().BeTrue("group 1 is independent");
        }

        [Test]
        public void M16_a_conflict_with_an_empty_current_value_shows_the_empty_marker()
        {
            using var os = ProbeSpace.Create();
            var a = NewA(os);
            var p = ProbeSpace.Payload("EditDraftProbeA", ("Note", "string", true, "before", "draft"));
            var item = EditDraftRestorer.BuildItems(A, p, a, isNew: false).Single();
            item.StatusCode.Should().Be((int)EditDraftItemStatus.Conflict, "the record now holds nothing, which is neither the baseline nor the draft");
            item.CurrentText.Should().Be("（空）");
            item.Selected.Should().BeFalse();
        }

        [Test]
        public void M24_E20_a_side_effect_member_is_never_preticked_and_a_locked_member_is_shown_but_not_selectable_on_an_existing_record()
        {
            using var os = ProbeSpace.Create();
            var a = NewA(os);
            var p = ProbeSpace.Payload("EditDraftProbeA", ("Status", "string", true, null, "s"), ("Locked", "string", true, null, "l"), ("Note", "string", true, null, "n"));
            var existing = EditDraftRestorer.BuildItems(A, p, a, isNew: false).ToDictionary(i => i.Path);
            existing["Status"].Selectable.Should().BeTrue();
            existing["Status"].Selected.Should().BeFalse("他の記録も変わります is the person's decision");
            existing["Status"].StatusText.Should().Be("戻せます（他の記録も変わります）");
            existing["Locked"].StatusCode.Should().Be((int)EditDraftItemStatus.Unavailable);
            existing["Locked"].StatusText.Should().Be("戻せません");
            existing["Locked"].Selectable.Should().BeFalse();
            existing["Locked"].Selected.Should().BeFalse();
            existing["Note"].Selected.Should().BeTrue();
        }

        [Test]
        public void M21_M28_a_driver_only_restore_leaves_an_undrafted_dependent_at_its_pre_restore_value()
        {
            using var os = ProbeSpace.Create();
            var empty = NewA(os);                                                // Number 0, When empty
            var p = ProbeSpace.Payload("EditDraftProbeA", ("Flag", "bool", true, "false", "true"), ("Text", "string", true, null, "t"));
            var trace = ProbeSpace.Trace(os, empty, () =>
            {
                var (applied, failed, appliedPaths) = EditDraftRestorer.Apply(A, os, empty, p, new[] { "Flag", "Text" });
                applied.Should().Be(2, "the put-back of an undrafted member is not counted as applied");
                failed.Should().Be(0);
                appliedPaths.Should().Equal("Flag", "Text");
            });
            empty.Flag.Should().BeTrue();
            empty.Number.Should().Be(0, "the driver filled 1 and the pre-restore 0 was put back");
            empty.Text.Should().Be("t");
            empty.When.Should().Be(DateTime.MinValue);
            trace.Should().Equal("Flag=true", "Number=1", "Text=t", "When=" + ProbeSpace.Stamp0Raw, "Number=0", "When=" + ProbeSpace.Empty);

            var populated = NewA(os);
            populated.Number = 7;
            populated.When = new DateTime(2026, 9, 27, 9, 0, 0);
            EditDraftRestorer.Apply(A, os, populated, p, new[] { "Flag", "Text" });
            populated.Number.Should().Be(7, "a populated dependent is never replaced by the default");
            populated.When.Should().Be(new DateTime(2026, 9, 27, 9, 0, 0));
        }

        [Test]
        public void M22_M30_a_drafted_dependent_wins_over_the_drivers_default_including_an_explicit_empty_value()
        {
            using var os = ProbeSpace.Create();
            var a = NewA(os);
            a.Number = 5;
            a.When = new DateTime(2026, 9, 27, 9, 0, 0);
            var p = ProbeSpace.Payload("EditDraftProbeA",
                ("Flag", "bool", true, "false", "true"), ("Number", "int", true, "5", "0"),
                ("Text", "string", true, null, "t"), ("When", "datetime", true, EditDraftCodec.RawOf(a.When), ProbeSpace.Empty));
            var trace = ProbeSpace.Trace(os, a, () => EditDraftRestorer.Apply(A, os, a, p, new[] { "Number" }).Applied.Should().Be(2, "choosing the dependent brings the drafted driver"));
            a.Flag.Should().BeTrue();
            a.Number.Should().Be(0, "the explicit drafted 0 is the final value, not the default 1");
            a.Text.Should().BeNull("the other group was not chosen");
            a.When.Should().Be(new DateTime(2026, 9, 27, 9, 0, 0));
            trace.Last().Should().Be("Number=0");
            trace.First().Should().Be("Flag=true", "the driver runs before its dependent's final value");

            EditDraftRestorer.Apply(A, os, a, p, new[] { "Text", "When" });
            a.When.Should().Be(DateTime.MinValue, "the explicit drafted empty time is the final value");
            a.Number.Should().Be(0);
        }

        [Test]
        public void M23_either_group_alone_both_or_neither_changes_only_what_was_chosen()
        {
            var p = ProbeSpace.Payload("EditDraftProbeA",
                ("Flag", "bool", true, "false", "true"), ("Number", "int", true, "0", "3"),
                ("Text", "string", true, null, "t"), ("When", "datetime", true, ProbeSpace.Empty, "2026-09-27T09:45:00.0000000"),
                ("Note", "string", true, null, "n"));
            using var os = ProbeSpace.Create();

            var neither = NewA(os);
            EditDraftRestorer.Apply(A, os, neither, p, new[] { "Note" });
            (neither.Flag, neither.Number, neither.Text, neither.When, neither.Note).Should().Be((false, 0, (string)null, DateTime.MinValue, "n"));

            var first = NewA(os);
            EditDraftRestorer.Apply(A, os, first, p, new[] { "Flag" });
            (first.Flag, first.Number, first.Text, first.When, first.Note).Should().Be((true, 3, (string)null, DateTime.MinValue, (string)null));

            var second = NewA(os);
            EditDraftRestorer.Apply(A, os, second, p, new[] { "When" });
            (second.Flag, second.Number, second.Text, second.When).Should().Be((false, 0, "t", new DateTime(2026, 9, 27, 9, 45, 0)));

            var both = NewA(os);
            EditDraftRestorer.Apply(A, os, both, p, new[] { "Flag", "Text" }).Applied.Should().Be(4);
            (both.Flag, both.Number, both.Text, both.When, both.Note).Should().Be((true, 3, "t", new DateTime(2026, 9, 27, 9, 45, 0), (string)null));

            var nothing = NewA(os);
            var none = EditDraftRestorer.Apply(A, os, nothing, p, Array.Empty<string>());
            (none.Applied, none.Failed, none.AppliedPaths.Count).Should().Be((0, 0, 0));
            (nothing.Flag, nothing.Number, nothing.Text, nothing.Note).Should().Be((false, 0, (string)null, (string)null));
        }

        [Test]
        public void M33_M34_the_ordering_prerequisite_is_applied_before_the_driver_that_reads_it_and_is_not_added_by_the_group()
        {
            using var os = ProbeSpace.Create();
            var a = NewA(os);
            var newStamp = new DateTime(2026, 9, 26, 8, 30, 0);
            var p = ProbeSpace.Payload("EditDraftProbeA", ("Text", "string", true, null, "t"), ("Stamp", "datetime", true, ProbeSpace.Stamp0Raw, EditDraftCodec.RawOf(newStamp)));

            var trace = ProbeSpace.Trace(os, a, () => EditDraftRestorer.Apply(A, os, a, p, new[] { "Text", "Stamp" }));
            trace.Take(3).Should().Equal("Stamp=" + EditDraftCodec.RawOf(newStamp), "Text=t", "When=" + EditDraftCodec.RawOf(newStamp));

            var b = NewA(os);
            var second = ProbeSpace.Trace(os, b, () => EditDraftRestorer.Apply(A, os, b, p, new[] { "Text" }));
            second.Should().NotContain(x => x.StartsWith("Stamp="), "choosing the group does not bring its prerequisite");
            second.Should().Contain("When=" + ProbeSpace.Stamp0Raw, "the driver read the LIVE prerequisite, not the unapplied draft");
            b.Stamp.Should().Be(ProbeSpace.Stamp0);
        }

        [Test]
        public void M36_repeating_a_driver_only_restore_never_lets_the_default_in()
        {
            using var os = ProbeSpace.Create();
            var a = NewA(os);
            var p = ProbeSpace.Payload("EditDraftProbeA", ("Flag", "bool", true, "false", "true"), ("Text", "string", true, null, "t"));
            for (var n = 0; n < 3; n++)
            {
                a.Flag = false;                                                  // so the driver's setter runs again
                a.Number = 0;
                EditDraftRestorer.Apply(A, os, a, p, new[] { "Flag", "Text" });
                a.Number.Should().Be(0);
                a.When.Should().Be(DateTime.MinValue);
            }
        }

        [Test]
        public void M52_E9_a_restore_that_applies_something_leaves_unsaved_work_and_one_that_applies_nothing_does_not()
        {
            using var os = ProbeSpace.Create();
            var a = NewA(os);
            os.CommitChanges();
            os.IsModified.Should().BeFalse("clean after the save");
            var p = ProbeSpace.Payload("EditDraftProbeA", ("Note", "string", true, null, "n"), ("Gone", "string", true, null, "x"));

            EditDraftRestorer.Apply(A, os, a, p, Array.Empty<string>());
            os.IsModified.Should().BeFalse("nothing selected");
            var gone = EditDraftRestorer.Apply(A, os, a, p, new[] { "Gone" });
            (gone.Applied, gone.Failed).Should().Be((0, 1));
            os.IsModified.Should().BeFalse("every member failed");
            EditDraftRestorer.Apply(null, os, a, p, new[] { "Note" });
            os.IsModified.Should().BeFalse("no policy");

            EditDraftRestorer.Apply(A, os, a, p, new[] { "Note" }).Applied.Should().Be(1);
            os.IsModified.Should().BeTrue("a restore that changed a value leaves unsaved work");

            EditDraftRestorer.Apply(A, os, a, p, Array.Empty<string>());
            os.IsModified.Should().BeTrue("an already dirty screen stays dirty");
        }

        [Test]
        public void M53_E12_restore_fills_in_and_never_saves_the_value_is_stored_only_by_the_later_save()
        {
            Guid key;
            using (var setup = ProbeSpace.Create())
            {
                var created = setup.CreateObject<EditDraftProbeA>();
                created.Note = "stored";
                setup.CommitChanges();
                key = created.Oid;
            }

            using var screen = ProbeSpace.Create();
            var a = screen.GetObjectByKey<EditDraftProbeA>(key);
            var p = ProbeSpace.Payload("EditDraftProbeA", ("Note", "string", true, "stored", "draft"));
            EditDraftRestorer.BuildItems(A, p, a, isNew: false).Single().Selected.Should().BeTrue("clean: the record still holds the baseline");
            EditDraftRestorer.Apply(A, screen, a, p, new[] { "Note" }).Applied.Should().Be(1);
            a.Note.Should().Be("draft");

            using (var fresh = ProbeSpace.Create())
                fresh.GetObjectByKey<EditDraftProbeA>(key).Note.Should().Be("stored", "the restore did not save");

            a.Note = "draft, then edited";                                       // capture → restore → edit → save
            screen.CommitChanges();
            using (var after = ProbeSpace.Create())
                after.GetObjectByKey<EditDraftProbeA>(key).Note.Should().Be("draft, then edited");

            EditDraftRestorer.BuildItems(A, p, a, isNew: false).Single().StatusCode.Should().Be((int)EditDraftItemStatus.Conflict,
                "after the save the record differs from both the baseline and the draft");
        }

        [Test]
        public void M54_a_value_equal_to_the_draft_is_already_applied_and_not_selectable()
        {
            using var os = ProbeSpace.Create();
            var a = NewA(os);
            a.Note = "draft";
            var item = EditDraftRestorer.BuildItems(A, ProbeSpace.Payload("EditDraftProbeA", ("Note", "string", true, "stored", "draft")), a, isNew: false).Single();
            item.StatusCode.Should().Be((int)EditDraftItemStatus.AlreadyApplied);
            item.StatusText.Should().Be("反映済み");
            item.Selectable.Should().BeFalse();
        }
    }

    /// <summary>D12: the commit / rollback cancel guard (design §4.1 layer 2). Built in milestone 1.</summary>
    [TestFixture]
    public class EditDraftRestoreGuardTests
    {
        private sealed class QueueContext : SynchronizationContext
        {
            public readonly Queue<(SendOrPostCallback, object)> Posted = new();
            public override void Post(SendOrPostCallback d, object state) => Posted.Enqueue((d, state));
            public void Drain() { while (Posted.Count > 0) { var (d, s) = Posted.Dequeue(); d(s); } }
        }

        private static Guid Stored(string note)
        {
            using var setup = ProbeSpace.Create();
            var a = setup.CreateObject<EditDraftProbeA>();
            a.Note = note;
            setup.CommitChanges();
            return a.Oid;
        }

        private static string StoredNote(Guid key)
        {
            using var fresh = ProbeSpace.Create();
            return fresh.GetObjectByKey<EditDraftProbeA>(key).Note;
        }

        [Test]
        public void M40_M41_a_commit_during_the_guard_is_cancelled_counted_and_reported_every_time()
        {
            var key = Stored("stored");
            using var os = ProbeSpace.Create();
            var a = os.GetObjectByKey<EditDraftProbeA>(key);
            var reports = new List<string>();
            using var guard = new EditDraftRestoreGuard(os, "Test", reports.Add);

            a.Note = "restored, unsaved";
            os.CommitChanges();
            os.CommitChanges();

            StoredNote(key).Should().Be("stored", "the save was cancelled");
            a.Note.Should().Be("restored, unsaved", "the filled-in value is still on the screen");
            os.IsModified.Should().BeTrue();
            guard.CancelledCommits.Should().Be(2);
            guard.CancelledRollbacks.Should().Be(0);
            guard.Violated.Should().BeTrue("the restore must be reported as a failure, not as a success");
            reports.Should().HaveCount(2, "never silent");
        }

        [Test]
        public void M40_M47_a_rollback_during_the_guard_is_cancelled_and_pending_edits_survive()
        {
            var key = Stored("stored");
            using var os = ProbeSpace.Create();
            var a = os.GetObjectByKey<EditDraftProbeA>(key);
            a.Status = "an unrelated pending edit";
            var reports = new List<string>();
            using var guard = new EditDraftRestoreGuard(os, "Test", reports.Add);

            a.Note = "restored, unsaved";
            os.Rollback();

            a.Note.Should().Be("restored, unsaved");
            a.Status.Should().Be("an unrelated pending edit", "a rollback would have discarded it");
            guard.CancelledRollbacks.Should().Be(1);
            guard.CancelledCommits.Should().Be(0);
            reports.Should().HaveCount(1);
        }

        [Test]
        public void M43_after_the_guard_closes_an_ordinary_save_and_an_ordinary_rollback_work()
        {
            var key = Stored("stored");
            using var os = ProbeSpace.Create();
            var a = os.GetObjectByKey<EditDraftProbeA>(key);
            var guard = new EditDraftRestoreGuard(os);
            a.Note = "restored";
            guard.Dispose();
            guard.IsOpen.Should().BeFalse();
            guard.Dispose();                                                     // closing twice is harmless

            os.CommitChanges();
            StoredNote(key).Should().Be("restored", "the manual save after the restore works");

            a.Note = "typed, then discarded";
            os.Rollback();
            os.GetObjectByKey<EditDraftProbeA>(key).Note.Should().Be("restored", "an ordinary rollback is no longer cancelled");
            guard.Violated.Should().BeFalse();
        }

        [Test]
        public void M44_M45_an_exception_or_a_restore_that_applied_nothing_leaves_no_guard_behind()
        {
            var key = Stored("stored");
            using var os = ProbeSpace.Create();
            var a = os.GetObjectByKey<EditDraftProbeA>(key);

            FluentActions.Invoking(() =>
            {
                using var guard = new EditDraftRestoreGuard(os);
                a.Note = "set before the failure";
                throw new InvalidOperationException("a setter failed");
            }).Should().Throw<InvalidOperationException>();
            os.CommitChanges();
            StoredNote(key).Should().Be("set before the failure", "the guard of the failed restore is gone");

            using (new EditDraftRestoreGuard(os)) { }                           // nothing applied
            a.Note = "later edit";
            os.CommitChanges();
            StoredNote(key).Should().Be("later edit");
        }

        [Test]
        public void M42_M43_the_guard_stays_open_for_work_posted_during_the_apply_and_closes_behind_it()
        {
            var key = Stored("stored");
            using var os = ProbeSpace.Create();
            var a = os.GetObjectByKey<EditDraftProbeA>(key);
            var context = new QueueContext();
            var guard = new EditDraftRestoreGuard(os);

            a.Note = "restored";
            context.Post(_ => os.CommitChanges(), null);                         // a controller posts its save during the apply
            guard.CloseAfterPostedWork(context);                                 // the sentinel goes behind it
            guard.IsOpen.Should().BeTrue("not closed until the posted work has run");

            context.Drain();
            guard.CancelledCommits.Should().Be(1, "the posted save ran before the sentinel and was cancelled");
            guard.IsOpen.Should().BeFalse();
            StoredNote(key).Should().Be("stored");

            os.CommitChanges();
            StoredNote(key).Should().Be("restored", "after the sentinel an ordinary save works");

            var now = new EditDraftRestoreGuard(os);
            now.CloseAfterPostedWork(null);
            now.IsOpen.Should().BeFalse("without a context the guard closes at once");
        }

        [Test]
        public void M46_a_later_restore_gets_its_own_guard_and_closed_guards_do_not_accumulate()
        {
            var key = Stored("stored");
            using var os = ProbeSpace.Create();
            var a = os.GetObjectByKey<EditDraftProbeA>(key);
            var firstReports = new List<string>();
            var secondReports = new List<string>();

            var first = new EditDraftRestoreGuard(os, "Test", firstReports.Add);
            first.Dispose();
            using var second = new EditDraftRestoreGuard(os, "Test", secondReports.Add);
            a.Note = "second restore";
            os.CommitChanges();

            first.CancelledCommits.Should().Be(0, "the closed guard no longer listens");
            firstReports.Should().BeEmpty();
            second.CancelledCommits.Should().Be(1);
            secondReports.Should().HaveCount(1, "one attempt, one report");
        }

        [Test]
        public void M48_the_stated_limits_another_object_space_and_a_post_behind_the_sentinel_are_not_covered()
        {
            var key = Stored("stored");
            using var guarded = ProbeSpace.Create();
            using var other = ProbeSpace.Create();
            var context = new QueueContext();
            var guard = new EditDraftRestoreGuard(guarded);

            // Limit: a commit on a DIFFERENT object space is outside the guard.
            other.GetObjectByKey<EditDraftProbeA>(key).Note = "saved through another object space";
            other.CommitChanges();
            StoredNote(key).Should().Be("saved through another object space");
            guard.CancelledCommits.Should().Be(0);

            // Limit: work posted BY posted work runs after the sentinel.
            var a = guarded.GetObjectByKey<EditDraftProbeA>(key);
            a.Status = "restored";
            context.Post(_ => context.Post(__ => guarded.CommitChanges(), null), null);
            guard.CloseAfterPostedWork(context);
            context.Drain();
            guard.CancelledCommits.Should().Be(0, "the nested post ran after the guard closed: this is a recorded limit, not protection");
            using var fresh = ProbeSpace.Create();
            fresh.GetObjectByKey<EditDraftProbeA>(key).Status.Should().Be("restored", "that save went through");
        }

        [Test]
        public void The_guard_needs_an_object_space()
        {
            FluentActions.Invoking(() => new EditDraftRestoreGuard(null)).Should().Throw<ArgumentNullException>();
        }
    }
}
