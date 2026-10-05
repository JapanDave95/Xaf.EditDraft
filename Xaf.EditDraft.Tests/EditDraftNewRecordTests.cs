using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.DC.Xpo;
using DevExpress.ExpressApp.Xpo;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Xaf.EditDraft.Blazor;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests
{
    // NEW (never saved) records, milestones M1-M2 (design docs/edit-draft-new-records-design-2026-10-02.md; owner rulings
    // 2026-10-03). Expectations come from the Codex requirement-only list of run 2026-10-03-edit-draft-new-records-build-f4b916
    // (tests a1): E01-E44 reused or changed per the rulings, N01-N33 added; the design's test ids T1-T16 are named beside them.
    // Logic runs against pure code (EditDraftCaptureRules, EditDraftNewRecordRules, EditDraftRecreate with a fake host) or an
    // in-memory object space over TEST-ONLY types; controller wiring is pinned by source scans; reloads, tabs and popups are
    // the Dev2 browser pass. The security checks (EditDraftCreateAccess, MayRecreate) are SINGLE-MODEL: Claude's alone.

    /// <summary>A test-only record whose construction defaults depend on a clock, like 残業・有給 (日付 = today, times on 日付).</summary>
    public class EditDraftNewProbe : BaseObject
    {
        /// <summary>The probe's clock: what AfterConstruction calls "today" (tests set it; the fixture is non-parallel).</summary>
        public static DateTime Today = new(2026, 10, 2);

        public EditDraftNewProbe(Session session) : base(session) { }

        public override void AfterConstruction()
        {
            base.AfterConstruction();
            Owner = "login-1";
            Day = Today;
            Start = Today.AddHours(9);
        }

        private string _owner;
        public string Owner { get => _owner; set => SetPropertyValue(nameof(Owner), ref _owner, value); }

        private DateTime _day;
        public DateTime Day { get => _day; set => SetPropertyValue(nameof(Day), ref _day, value); }

        private DateTime _start;
        /// <summary>Like StaffOverTimeHoliday.StartTime: a time typed on another date is moved onto Day's date.</summary>
        public DateTime Start
        {
            get => _start;
            set
            {
                if (!IsLoading && !IsSaving && value != DateTime.MinValue && Day.Date != value.Date) value = Day.Date.Add(value.TimeOfDay);
                SetPropertyValue(nameof(Start), ref _start, value);
            }
        }

        private string _reason;
        public string Reason { get => _reason; set => SetPropertyValue(nameof(Reason), ref _reason, value); }

        private string _locked;
        public string Locked { get => _locked; set => SetPropertyValue(nameof(Locked), ref _locked, value); }

        private int _count;
        public int Count { get => _count; set => SetPropertyValue(nameof(Count), ref _count, value); }

        private bool _flag;
        public bool Flag { get => _flag; set => SetPropertyValue(nameof(Flag), ref _flag, value); }

        private EditDraftNewRef _ref;
        public EditDraftNewRef Ref { get => _ref; set => SetPropertyValue(nameof(Ref), ref _ref, value); }

        /// <summary>A getter that WRITES (KB fix-529 kind): fills Count = 7 when it is 0.</summary>
        public int FilledCount { get { if (Count == 0) Count = 7; return Count; } }
    }

    public class EditDraftNewRef : BaseObject
    {
        public EditDraftNewRef(Session session) : base(session) { }
        private string _name;
        public string Name { get => _name; set => SetPropertyValue(nameof(Name), ref _name, value); }
    }

    internal static class NewProbe
    {
        public const string View = "EditDraftNewProbe_DetailView";
        public const string List = "EditDraftNewProbe_ListView";

        private static readonly Lazy<XPObjectSpaceProvider> Provider = new(() =>
        {
            FrameworkSettings.DefaultSettingsCompatibilityMode = FrameworkSettingsCompatibilityMode.Latest;
            var typesInfo = new TypesInfo();
            var source = new XpoTypeInfoSource(typesInfo);
            typesInfo.AddEntityStore(source);
            typesInfo.RegisterEntity(typeof(EditDraftNewProbe));
            typesInfo.RegisterEntity(typeof(EditDraftNewRef));
            return new XPObjectSpaceProvider((IXpoDataStoreProvider)new MemoryDataStoreProvider(), (ITypesInfo)typesInfo, source, true, false);
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        public static IObjectSpace Space() => Provider.Value.CreateObjectSpace();

        private static EditDraftMemberDecision A(string path) => new(path, EditDraftDisposition.Restorable, "A", "test", "test");

        public static EditDraftTypePolicy Policy(bool allowNew = true, string[] getters = null) => new(typeof(EditDraftNewProbe))
        {
            PolicyId = "test:New",
            MemberDeclaringBase = typeof(EditDraftNewProbe),
            ApprovedViewIds = new HashSet<string> { View },
            ListViewIds = new HashSet<string> { List },
            AllowNewRecords = allowNew,
            NewRecordReconstructionOrder = new[] { "Owner", "Day", "Start" },
            NotRestorableOnExisting = new HashSet<string> { "Locked" },
            InitializingGetters = getters ?? Array.Empty<string>(),
            Decisions = EditDraftDecisions.Table(A("Owner"), A("Day"), A("Start"), A("Reason"), A("Count"), A("Flag"), A("Ref"),
                new EditDraftMemberDecision("Locked", EditDraftDisposition.NotRestorable, "C", "test", "test"))
        };

        /// <summary>
        /// The capture's BindTo for one screen, restated for the tests: admission (the controller's own static, so a disabled
        /// lift fails here — T17), the initializing getters, then the baseline snapshot.
        /// </summary>
        public static Dictionary<string, (string Raw, string Text)> Bind(EditDraftTypePolicy policy, IObjectSpace os, object record)
        {
            var isNew = os.IsNewObject(record);
            EditDraftCaptureController.IsAdmittedViewIncludingNew(policy, View, true, isNew)
                .Should().BeTrue("the screen is admitted (isNew=" + isNew + ")");
            EditDraftMembers.RunInitializingGetters(policy, record);
            var baseline = new Dictionary<string, (string Raw, string Text)>(StringComparer.Ordinal);
            foreach (var m in policy.Members)
            {
                var v = EditDraftMembers.GetValue(record, m.Path);
                baseline[m.Path] = (EditDraftCodec.RawOf(v), EditDraftDisplay.TextOf(v));
            }
            return baseline;
        }

        /// <summary>One ObjectChanged of the capture: a started payload that captured nothing is dropped (the controller's rule).</summary>
        public static bool Notify(EditDraftTypePolicy policy, ref EditDraftPayload payload, Dictionary<string, (string Raw, string Text)> baseline,
                                  IObjectSpace os, object record, string path)
        {
            var started = payload == null;
            if (started) payload = new EditDraftPayload { TypeName = policy.TypeName };
            var posted = EditDraftCaptureRules.Capture(policy, payload, baseline, record, path, os.IsNewObject(record));
            if (!posted && started && payload.Count == 0) payload = null;
            return posted;
        }

        public static string Raw(object v) => EditDraftCodec.RawOf(v);
    }

    [TestFixture]
    public class EditDraftNewRecordAdmissionTests
    {
        [TestCase(true, true, true, NewProbe.View, true, TestName = "T1_new_admitted_when_opted_in_root_approved")]
        [TestCase(false, true, true, NewProbe.View, false, TestName = "T1_new_refused_without_AllowNewRecords")]
        [TestCase(true, true, false, NewProbe.View, false, TestName = "T1_new_refused_in_a_nested_view")]
        [TestCase(true, true, true, "Other_DetailView", false, TestName = "T1_new_refused_in_an_unapproved_view")]
        [TestCase(true, true, true, null, false, TestName = "T1_new_refused_without_a_view_id")]
        [TestCase(false, false, true, NewProbe.View, true, TestName = "T1_existing_admitted_whatever_AllowNewRecords")]
        [TestCase(true, false, false, NewProbe.View, false, TestName = "T1_existing_refused_in_a_nested_view")]
        public void T1_N02_E06_admission_including_new_records(bool allowNew, bool isNew, bool isRoot, string viewId, bool expected)
        {
            EditDraftCaptureController.IsAdmittedViewIncludingNew(NewProbe.Policy(allowNew), viewId, isRoot, isNew).Should().Be(expected);
        }

        [Test]
        public void T1_E43_E30_the_existing_rule_the_chart_policies_and_the_inline_list_stay_existing_only()
        {
            var policy = NewProbe.Policy();
            EditDraftCaptureController.IsAdmittedView(policy, NewProbe.View, true, isNew: true).Should().BeFalse("the existing-record rule (used by the restore offer) is unchanged");
            EditDraftCaptureController.IsAdmittedView(policy, NewProbe.View, true, isNew: false).Should().BeTrue();
            var chart = new EditDraftTypePolicy(typeof(EditDraftNewProbe)) { PolicyId = "chart", AllowNewRecords = true };   // no decision table (0.4.0-preview.1: no owner kind)
            EditDraftCaptureController.IsAdmittedViewIncludingNew(chart, NewProbe.View, true, true).Should().BeFalse("a policy without a decision table never enters the generic capture");
            EditDraftCaptureController.IsAdmittedViewIncludingNew(null, NewProbe.View, true, true).Should().BeFalse("an unregistered type");
            EditDraftListAdmission.IsAdmittedList(policy, NewProbe.List, true, true, isNew: true).Should().BeFalse("inline new rows in a list stay out (design §3)");
            new EditDraftTypePolicy(typeof(EditDraftNewProbe)).AllowNewRecords.Should().BeFalse("opt-in: off by default");
            new EditDraftTypePolicy(typeof(EditDraftNewProbe)).NewRecordReconstructionOrder.Should().BeEmpty();
        }

        [TestCase("true", "true", "true", true)]
        [TestCase("true", "true", "false", false)]
        [TestCase("true", "true", null, false)]
        [TestCase("true", "true", "", false)]
        [TestCase("true", "true", "yes", false)]
        [TestCase("true", "false", "true", false)]
        [TestCase("false", "true", "true", false)]
        [TestCase("True", "TRUE", " true ", true)]
        public void N01_new_record_capture_needs_the_global_the_type_and_the_NewRecords_key_all_true(string global, string type, string newRecords, bool expected)
        {
            EditDraftSwitch.DecideNewRecords(global, type, newRecords).Should().Be(expected);
        }

        [Test]
        public void N01_the_NewRecords_key_is_read_from_the_configured_section_and_re_read_at_every_use()
        {
            EditDraftSwitch.NewRecordsKey.Should().Be("EditDraftCapture:NewRecords:Enabled");
            var values = new Dictionary<string, string>
            {
                ["EditDraftCapture:Enabled"] = "true", ["EditDraftCapture:Types:ToDo:Enabled"] = "true", ["EditDraftCapture:NewRecords:Enabled"] = "true",
                ["Other:Enabled"] = "true", ["Other:Types:ToDo:Enabled"] = "true", ["Other:NewRecords:Enabled"] = "false"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var defaults = new FixedServices().Add<IConfiguration>(config);
            var other = new FixedServices().Add<IConfiguration>(config).Add(new EditDraftSwitchOptions { Section = "Other" });
            EditDraftSwitch.IsNewRecordsEnabled(defaults, "ToDo").Should().BeTrue();
            EditDraftSwitch.IsNewRecordsEnabled(other, "ToDo").Should().BeFalse("the configured section decides");
            config["EditDraftCapture:NewRecords:Enabled"] = "false";
            EditDraftSwitch.IsNewRecordsEnabled(defaults, "ToDo").Should().BeFalse("re-read at every use: off without a release");
            EditDraftSwitch.IsEnabled(defaults, "ToDo").Should().BeTrue("existing-record capture never reads the key");
            config["EditDraftCapture:NewRecords:Enabled"] = "true";
            EditDraftSwitch.IsNewRecordsEnabled(defaults, "ToDo").Should().BeTrue();
            EditDraftSwitch.IsNewRecordsEnabled(new FixedServices(), "ToDo").Should().BeFalse("no configuration = off");
            EditDraftSwitch.IsNewRecordsEnabled(defaults, null).Should().BeFalse();
        }
    }

    [TestFixture]
    [NonParallelizable]
    public class EditDraftNewRecordCaptureTests
    {
        [SetUp] public void Clock() => EditDraftNewProbe.Today = new DateTime(2026, 10, 2);

        [Test]
        public void T2_N04_a_new_object_has_its_Oid_from_construction_and_keeps_it_through_the_save()
        {
            using var os = NewProbe.Space();
            var r = os.CreateObject<EditDraftNewProbe>();
            var oid = r.Oid;
            oid.Should().NotBe(Guid.Empty, "BaseObject assigns the Oid at construction (DX 26.1.4 BaseObject.AfterConstruction)");
            os.IsNewObject(r).Should().BeTrue();
            os.CommitChanges();
            r.Oid.Should().Be(oid, "the save keeps it");
            os.IsNewObject(r).Should().BeFalse();
            using var fresh = NewProbe.Space();
            fresh.GetObjectByKey<EditDraftNewProbe>(oid).Should().NotBeNull();
        }

        [Test]
        public void T3_E07_N06_opening_a_new_record_and_bare_notifications_capture_nothing_and_leave_no_payload()
        {
            var policy = NewProbe.Policy(getters: new[] { nameof(EditDraftNewProbe.FilledCount) });
            using var os = NewProbe.Space();
            var r = os.CreateObject<EditDraftNewProbe>();
            var baseline = NewProbe.Bind(policy, os, r);
            r.Count.Should().Be(7, "the writing getter ran before the baseline (fix-529)");
            EditDraftPayload payload = null;
            foreach (var m in policy.Members)
                NewProbe.Notify(policy, ref payload, baseline, os, r, m.Path).Should().BeFalse(m.Path + ": a notification of an unchanged default is not an edit");
            payload.Should().BeNull("a started payload with no genuine change is dropped, seeds included (nothing blocks a later adoption)");
            r.Day = r.Day;   // the same value through the setter
            NewProbe.Notify(policy, ref payload, baseline, os, r, nameof(EditDraftNewProbe.Day)).Should().BeFalse();
            payload.Should().BeNull();
        }

        [TestCase(nameof(EditDraftNewProbe.Owner))]
        [TestCase(nameof(EditDraftNewProbe.Day))]
        [TestCase(nameof(EditDraftNewProbe.Start))]
        public void T5_N05_each_seeded_member_as_the_ONLY_first_edit_is_typed_and_posts_a_write(string member)
        {
            var policy = NewProbe.Policy();
            using var os = NewProbe.Space();
            var r = os.CreateObject<EditDraftNewProbe>();
            var baseline = NewProbe.Bind(policy, os, r);
            object typed = member switch
            {
                nameof(EditDraftNewProbe.Owner) => "login-2",
                nameof(EditDraftNewProbe.Day) => new DateTime(2026, 9, 30),
                _ => new DateTime(2026, 10, 2, 13, 30, 0)
            };
            r.GetType().GetProperty(member).SetValue(r, typed);
            EditDraftPayload payload = null;
            NewProbe.Notify(policy, ref payload, baseline, os, r, member).Should().BeTrue("the seeding trap: the triggering member's change is a write");
            payload.Get(member).Seeded.Should().BeFalse("the triggering member is typed, never a seed");
            payload.Get(member).ValueRaw.Should().Be(NewProbe.Raw(EditDraftMembers.GetValue(r, member)), "its CHANGED value");
            payload.Entries.Select(e => e.Path).Should().BeEquivalentTo(new[] { "Owner", "Day", "Start" }, "the other context members are seeded beside it");
            payload.Entries.Where(e => e.Path != member).Should().OnlyContain(e => e.Seeded, "untouched context members are seeds");
        }

        [Test]
        public void T6_E08_N05_a_non_context_first_edit_seeds_the_three_context_members_with_their_construction_values()
        {
            var policy = NewProbe.Policy();
            using var os = NewProbe.Space();
            var r = os.CreateObject<EditDraftNewProbe>();
            var baseline = NewProbe.Bind(policy, os, r);
            r.Reason = "late shift";
            EditDraftPayload payload = null;
            NewProbe.Notify(policy, ref payload, baseline, os, r, nameof(EditDraftNewProbe.Reason)).Should().BeTrue();
            payload.Entries.Select(e => (e.Path, e.Seeded)).Should().Equal(("Reason", false), ("Owner", true), ("Day", true), ("Start", true));
            payload.Get("Day").ValueRaw.Should().Be(NewProbe.Raw(new DateTime(2026, 10, 2)));
            payload.Get("Start").ValueRaw.Should().Be(NewProbe.Raw(new DateTime(2026, 10, 2, 9, 0, 0)));
            payload.Get("Owner").ValueRaw.Should().Be("login-1");
            payload.Get("Count").Should().BeNull("an unchanged member outside the seed set is not added");
        }

        [Test]
        public void T6_N06_seeded_to_typed_flips_and_a_baseline_equal_notification_on_a_seed_writes_nothing()
        {
            var policy = NewProbe.Policy();
            using var os = NewProbe.Space();
            var r = os.CreateObject<EditDraftNewProbe>();
            var baseline = NewProbe.Bind(policy, os, r);
            r.Reason = "x";
            EditDraftPayload payload = null;
            NewProbe.Notify(policy, ref payload, baseline, os, r, "Reason");
            NewProbe.Notify(policy, ref payload, baseline, os, r, "Day").Should().BeFalse("a bare notification on an untouched SEEDED member is not promoted to an edit (Codex combined C18)");
            payload.Get("Day").Seeded.Should().BeTrue();
            r.Day = new DateTime(2026, 9, 29);
            NewProbe.Notify(policy, ref payload, baseline, os, r, "Day").Should().BeTrue("a value different from the baseline is a genuine edit");
            payload.Get("Day").Seeded.Should().BeFalse("seeded → typed");
            payload.Get("Day").ValueRaw.Should().Be(NewProbe.Raw(new DateTime(2026, 9, 29)));
            payload.Get("Day").BaseRaw.Should().Be(NewProbe.Raw(new DateTime(2026, 10, 2)), "the baseline stays the construction value");
        }

        [Test]
        public void E09_D8_a_member_typed_back_to_its_baseline_keeps_its_typed_entry_and_null_empty_zero_false_are_values()
        {
            var policy = NewProbe.Policy();
            using var os = NewProbe.Space();
            var r = os.CreateObject<EditDraftNewProbe>();
            var baseline = NewProbe.Bind(policy, os, r);
            EditDraftPayload payload = null;
            r.Reason = "";
            NewProbe.Notify(policy, ref payload, baseline, os, r, "Reason").Should().BeTrue("empty text is not the null baseline");
            r.Reason = null;
            NewProbe.Notify(policy, ref payload, baseline, os, r, "Reason").Should().BeTrue("typed back to the baseline: the entry moves (owner D8 'keep the entry')");
            payload.Get("Reason").Should().NotBeNull().And.Match<EditDraftEntry>(e => e.ValueRaw == null && !e.Seeded, "kept as a typed entry holding the returned value");
            r.Flag = true;
            NewProbe.Notify(policy, ref payload, baseline, os, r, "Flag").Should().BeTrue();
            r.Flag = false;
            NewProbe.Notify(policy, ref payload, baseline, os, r, "Flag").Should().BeTrue();
            payload.Get("Flag").ValueRaw.Should().Be(NewProbe.Raw(false), "false is a value, kept");
        }

        [Test]
        public void T6_seeding_happens_only_for_a_new_record()
        {
            var policy = NewProbe.Policy();
            Guid key;
            using (var setup = NewProbe.Space())
            {
                var created = setup.CreateObject<EditDraftNewProbe>();
                setup.CommitChanges();
                key = created.Oid;
            }
            using var os = NewProbe.Space();
            var r = os.GetObjectByKey<EditDraftNewProbe>(key);
            var baseline = NewProbe.Bind(policy, os, r);
            r.Reason = "existing";
            EditDraftPayload payload = null;
            NewProbe.Notify(policy, ref payload, baseline, os, r, "Reason").Should().BeTrue();
            payload.Entries.Select(e => e.Path).Should().Equal(new[] { "Reason" }, "an EXISTING record is captured exactly as before: no seed");
        }

        [Test]
        public void T4_N03_keying_per_write_and_the_write_guards()
        {
            var oid = Guid.NewGuid();
            EditDraftNewRecordRules.Key(true, oid).Should().Be((Guid.Empty, true), "while new: Guid.Empty and IsNew");
            EditDraftNewRecordRules.Key(false, oid).Should().Be((oid, false), "after the save: the record's Oid");
            EditDraftNewRecordRules.Key(null, oid).Should().Be((Guid.Empty, false), "undecidable: a key the guard refuses");
            var owner = Guid.NewGuid();
            EditDraftNewRecordRules.IsWritable(new EditDraftSeed { OwnerUserOid = owner, TargetOid = Guid.Empty, IsNew = true }).Should().BeTrue();
            EditDraftNewRecordRules.IsWritable(new EditDraftSeed { OwnerUserOid = owner, TargetOid = oid }).Should().BeTrue();
            EditDraftNewRecordRules.IsWritable(new EditDraftSeed { OwnerUserOid = owner, TargetOid = Guid.Empty }).Should().BeFalse("an empty target without IsNew stays impossible");
            EditDraftNewRecordRules.IsWritable(new EditDraftSeed { OwnerUserOid = owner, TargetOid = oid, IsNew = true }).Should().BeFalse("a NEW row never names a target");
            EditDraftNewRecordRules.IsWritable(new EditDraftSeed { OwnerUserOid = Guid.Empty, TargetOid = Guid.Empty, IsNew = true }).Should().BeFalse("never an ownerless row");
            EditDraftNewRecordRules.IsWritable(null).Should().BeFalse();

            var capture = Wave1.Source("Xaf.EditDraft.Core/EditDraftCaptureController.cs");
            capture.Should().Contain("var key = EditDraftNewRecordRules.Key(IsNewRecord(), RecordOid());")
                .And.Contain("if (key.IsNew) _payload.AddProvisional(RecordOid());")
                .And.Contain("TargetOid = key.TargetOid,").And.Contain("IsNew = key.IsNew,")
                .And.Contain("if (!EditDraftNewRecordRules.IsWritable(snapshot.Seed)) return;");
            Wave1.Source("Xaf.EditDraft.Core/EditDraftWriter.cs").Should().Contain("if (!EditDraftNewRecordRules.IsWritable(seed)) return Guid.Empty;");
        }

        [Test]
        public void N01_N06_the_controller_reads_the_NewRecords_key_per_event_and_binds_it_to_a_new_record_s_queued_writes()
        {
            var capture = Wave1.Source("Xaf.EditDraft.Core/EditDraftCaptureController.cs");
            capture.Should().Contain("if (!IsAdmittedViewIncludingNew(policy, View?.Id, View?.IsRoot ?? false, isNew))");
            capture.Should().Contain("if (IsNewRecord() != false && !EditDraftSwitch.IsNewRecordsEnabled(Application?.ServiceProvider, _policy.PolicyId)) return;");
            // Post-review (Codex diffreview D2): the new-record gate travels on a NEW record's snapshot and is checked per ticket.
            capture.Should().Contain("if (!key.IsNew) return snapshot;")
                .And.Contain("NewRecordsGate = EditDraftWriteGate.Bind(_policy?.PolicyId, id => EditDraftSwitch.IsNewRecordsEnabled(services, id)),");
            capture.Should().Contain("if ((stillEnabled != null && !stillEnabled()) || (s.NewRecordsGate != null && !s.NewRecordsGate()))");
            capture.Should().NotContain("stillEnabled = EditDraftWriteGate.Bind(_policy?.PolicyId, id => EditDraftSwitch.IsNewRecordsEnabled", "no loop-wide new-record gate");
            capture.Should().Contain("EditDraftCaptureRules.Capture(_policy, _payload, _baseline, _record, path, IsNewRecord() == true)");
        }
    }

    [TestFixture]
    public class EditDraftNewRecordPayloadTests
    {
        [Test]
        public void T7_N09_the_prov_history_is_newest_first_once_per_Oid_and_survives_the_claim_copy_and_serialisation()
        {
            var original = Guid.NewGuid(); var recreatedA = Guid.NewGuid(); var recreatedB = Guid.NewGuid();
            var p = new EditDraftPayload { TypeName = "X" };
            p.AddProvisional(original);
            p.AddProvisional(original);
            p.AddProvisional(Guid.Empty);
            p.ProvisionalOids.Should().Equal(original);
            var a = EditDraftNewRecordRules.WithProvisional(p, recreatedA);
            a.ProvisionalOids.Should().Equal(recreatedA, original);
            p.ProvisionalOids.Should().Equal(new[] { original }, "the claim's copy leaves the payload it read unchanged");
            var b = EditDraftNewRecordRules.WithProvisional(a, recreatedB);   // recreate A, no edit, then recreate B
            b.ProvisionalOids.Should().Equal(recreatedB, recreatedA, original);
            EditDraftPayload.FromJson<EditDraftPayload>(b.ToJson()).ProvisionalOids.Should().Equal(recreatedB, recreatedA, original);
            b.AddProvisional(recreatedB);
            b.ProvisionalOids.Should().HaveCount(3, "a rewrite by the same screen adds nothing");
        }

        [Test]
        public void T8_N08_a_payload_without_the_header_writes_today_s_text_and_both_shapes_read_at_version_1()
        {
            var p = new EditDraftPayload { TypeName = "X" };
            p.Upsert("A", "string", "a", true, null, null, "v", "v");
            var withoutHeader = p.ToJson();
            withoutHeader.Should().NotContain("prov", "a null header is not serialised: every existing payload's text is unchanged");
            withoutHeader.Should().StartWith("{\"schema\":1,\"type\":\"X\",\"entries\":[");
            var read = EditDraftPayload.FromJson<EditDraftPayload>(withoutHeader);
            read.Provisional.Should().BeNull();
            read.ProvisionalOids.Should().BeEmpty("absence alone is not unreadability");
            var oid = Guid.NewGuid();
            p.AddProvisional(oid);
            var withHeader = p.ToJson();
            withHeader.Should().EndWith(",\"prov\":[\"" + oid + "\"]}", "wire format: \"prov\":[\"<guid>\", …] after the entries");
            EditDraftPayload.FromJson<EditDraftPayload>(withHeader).Schema.Should().Be(1, "owner D2: version 1 stays");
            EditDraftStoreBase.CurrentPayloadSchemaVersion.Should().Be(1);
        }

        [Test]
        public void T9_N07_a_fresh_start_keeps_the_seeds_and_the_history_with_the_never_stored_typed_members()
        {
            var history = Guid.NewGuid();
            var old = new EditDraftPayload { TypeName = "X" };
            old.Upsert("Stored", "string", "s", true, null, null, "1", "1");
            old.Upsert("Fresh", "string", "f", true, null, null, "2", "2");
            old.Upsert("Day", "datetime", "d", true, "b", "b", "b", "b", seeded: true);
            old.AddProvisional(history);
            var fresh = EditDraftCaptureRules.FreshAfterGone(old, e => e.Path == "Fresh", out var kept);
            kept.Should().Be(1, "typed never-stored members only are counted");
            fresh.Entries.Select(e => (e.Path, e.Seeded)).Should().Equal(("Fresh", false), ("Day", true));
            fresh.ProvisionalOids.Should().Equal(history);
            EditDraftCaptureRules.FreshAfterGone(old, e => false, out var none).Should().BeNull("seeds alone are never written");
            none.Should().Be(0);

            var existing = new EditDraftPayload { TypeName = "X" };
            existing.Upsert("A", "string", "a", true, null, null, "1", "1");
            existing.Upsert("B", "string", "b", true, null, null, "2", "2");
            var rebuilt = EditDraftCaptureRules.FreshAfterGone(existing, e => e.Path == "B", out _);
            rebuilt.ToJson().Should().Be("{\"schema\":1,\"type\":\"X\",\"entries\":[{\"p\":\"B\",\"k\":\"string\",\"cap\":\"b\",\"bk\":true,\"br\":null,\"bt\":null,\"vr\":\"2\",\"vt\":\"2\",\"ctx\":false}]}",
                "an existing-record payload is rebuilt exactly as before (no seed, no header)");

            var capture = Wave1.Source("Xaf.EditDraft.Core/EditDraftCaptureController.cs");
            Regex.Matches(capture, @"EditDraftCaptureRules\.FreshAfterGone\(").Count.Should().Be(2, "both fresh-start paths (RebuildAfterFreshStart, RetiredFreshStart)");
        }
    }

    [TestFixture]
    [NonParallelizable]
    public class EditDraftNewRecordApplyTests
    {
        [SetUp] public void Clock() => EditDraftNewProbe.Today = new DateTime(2026, 10, 2);

        private static EditDraftPayload Draft(params (string Path, string Kind, string Raw, bool Seeded)[] entries)
        {
            var p = new EditDraftPayload { TypeName = nameof(EditDraftNewProbe) };
            foreach (var e in entries) p.Upsert(e.Path, e.Kind, e.Path, true, null, null, e.Raw, e.Raw, e.Seeded);
            return p;
        }

        [Test]
        public void T10_T11_N17_E25_context_first_date_before_times_and_a_draft_of_day_D_rebuilds_day_D_on_day_D_plus_1()
        {
            var policy = NewProbe.Policy();
            var dayD = new DateTime(2026, 10, 1);
            var draft = Draft(("Start", "datetime", NewProbe.Raw(dayD.AddHours(18)), false), ("Reason", "string", "night", false),
                              ("Day", "datetime", NewProbe.Raw(dayD), true), ("Owner", "string", "login-1", true));
            EditDraftNewProbe.Today = dayD.AddDays(1);   // recreated the next day: construction defaults moved to D+1
            using var os = NewProbe.Space();
            var r = os.CreateObject<EditDraftNewProbe>();
            r.Day.Should().Be(dayD.AddDays(1));
            var trace = new List<string>();
            os.ObjectChanged += (s, e) => { if (ReferenceEquals(e.Object, r) && !string.IsNullOrEmpty(e.PropertyName)) trace.Add(e.PropertyName); };
            var result = EditDraftRestorer.ApplyNew(policy, os, r, draft, null);
            trace.Take(3).Should().Equal(new[] { "Day", "Start", "Reason" }, "context in NewRecordReconstructionOrder first (Owner unchanged → no notification), 日付 before the time");
            r.Day.Should().Be(dayD, "the draft's day, not the new day's default");
            r.Start.Should().Be(dayD.AddHours(18), "the time lands on the draft's day");
            r.Reason.Should().Be("night");
            result.NotAppliedTyped.Should().BeEmpty();
            result.Applied.Should().Be(4);
        }

        [Test]
        public void T10_N15_N16_E27_E11_references_restrictions_and_permissions_on_a_fresh_record()
        {
            var policy = NewProbe.Policy();
            Guid refKey;
            using (var setup = NewProbe.Space())
            {
                var existing = setup.CreateObject<EditDraftNewRef>();
                existing.Name = "kept";
                setup.CommitChanges();
                refKey = existing.Oid;
            }
            using var os = NewProbe.Space();
            var r = os.CreateObject<EditDraftNewProbe>();
            var resolved = EditDraftRestorer.ApplyNew(policy, os, r, Draft(("Ref", "ref", refKey.ToString(), false)), null);
            r.Ref.Should().NotBeNull().And.Match<EditDraftNewRef>(x => x.Oid == refKey, "resolved by key in the destination space");
            resolved.Applied.Should().Be(1);

            var keep = r.Ref;
            var missing = EditDraftRestorer.ApplyNew(policy, os, r, Draft(("Ref", "ref", Guid.NewGuid().ToString(), false)), null);
            r.Ref.Should().BeSameAs(keep, "a reference that does not resolve is not applied — never nulled, never replaced");
            missing.Failed.Should().Be(1);
            missing.NotAppliedTyped.Should().Equal("Ref");

            EditDraftRestorer.ApplyNew(policy, os, r, Draft(("Ref", "ref", null, false)), null).Applied.Should().Be(1);
            r.Ref.Should().BeNull("a deliberately cleared reference is cleared");

            var restricted = EditDraftRestorer.ApplyNew(policy, os, r,
                Draft(("Locked", "string", "x", false), ("Gone", "string", "y", false), ("Count", "int", "3", false), ("Reason", "string", "ok", false)),
                p => p != "Count");
            r.Locked.Should().BeNull("戻せません is never assigned, even when the caller passes it directly (Codex SEC2)");
            r.Count.Should().Be(0, "a member the login may not write is not assigned");
            r.Reason.Should().Be("ok");
            restricted.NotAppliedTyped.Should().BeEquivalentTo(new[] { "Locked", "Gone", "Count" }, "every typed entry not on the record is reported for the read-only display");
            EditDraftRestorer.ApplyNew(policy, os, r, Draft(("Reason", "string", "z", false)), _ => throw new InvalidOperationException()).NotAppliedTyped
                .Should().Equal(new[] { "Reason" }, "a permission check that throws is 'not writable'");
        }

        [Test]
        public void T10_N14_E24_the_fresh_object_s_initializing_getters_run_before_the_replay()
        {
            var policy = NewProbe.Policy(getters: new[] { nameof(EditDraftNewProbe.FilledCount) });
            using var os = NewProbe.Space();
            var drafted = os.CreateObject<EditDraftNewProbe>();
            EditDraftRestorer.ApplyNew(policy, os, drafted, Draft(("Count", "int", "3", false)), null);
            drafted.Count.Should().Be(3, "the getter filled 7 first, then the drafted 3 was applied as the final value");
            var undrafted = os.CreateObject<EditDraftNewProbe>();
            EditDraftRestorer.ApplyNew(policy, os, undrafted, Draft(("Reason", "string", "r", false)), null);
            undrafted.Count.Should().Be(7, "the getter's fill is the fresh object's own value");
        }

        [Test]
        public void T10_E26_a_group_moves_whole_and_undrafted_members_keep_the_fresh_value_and_nothing_is_saved()
        {
            var policy = ProbeSpace.PolicyA();   // groups (Flag→Number, Text→When), Locked 戻せません, Status side effect
            using var os = ProbeSpace.Create();
            var a = os.CreateObject<EditDraftProbeA>();
            var payload = ProbeSpace.Payload("EditDraftProbeA", ("Flag", "bool", true, "false", "true"), ("Locked", "string", true, null, "l"), ("Note", "string", true, null, "n"));
            var result = EditDraftRestorer.ApplyNew(policy, os, a, payload, null);
            a.Flag.Should().BeTrue();
            a.Number.Should().Be(0, "the undrafted group member keeps the fresh object's value (the driver's fill is put back)");
            a.Locked.Should().BeNull();
            a.Note.Should().Be("n");
            result.NotAppliedTyped.Should().Equal("Locked");
            os.IsNewObject(a).Should().BeTrue("restored values stay on a NEW object pending the user's save (E41)");
            using var fresh = ProbeSpace.Create();
            fresh.GetObjectByKey<EditDraftProbeA>(a.Oid).Should().BeNull("nothing was saved");
        }

        [Test]
        public void N15_classification_restorable_and_unavailable_typed_entries()
        {
            var policy = NewProbe.Policy();
            var p = Draft(("Reason", "string", "r", false), ("Locked", "string", "l", false), ("Gone", "string", "g", false), ("Day", "datetime", "d", true));
            EditDraftNewRecordRules.RestorableTyped(policy, p).Select(e => e.Path).Should().Equal("Reason");
            EditDraftNewRecordRules.UnavailableTyped(policy, p).Select(e => e.Path).Should().Equal("Locked", "Gone");
            EditDraftNewRecordRules.TypedEntries(p).Select(e => e.Path).Should().NotContain("Day", "a seed is context, not typed input");
            EditDraftNewRecordRules.Classify(policy, p.Get("Locked")).Should().Be(EditDraftItemStatus.Unavailable);
            EditDraftNewRecordRules.Classify(policy, p.Get("Reason")).Should().Be(EditDraftItemStatus.New);
            EditDraftNewRecordRules.Classify(null, p.Get("Reason")).Should().Be(EditDraftItemStatus.Unavailable);
        }
    }

    [TestFixture]
    public class EditDraftNewRecordSavedCheckTests
    {
        [Test]
        public void T13_N11_E17_already_saved_looks_at_the_whole_history_and_a_failed_read_is_never_not_saved()
        {
            var original = Guid.NewGuid(); var recreated = Guid.NewGuid(); var newest = Guid.NewGuid();
            var history = new[] { newest, recreated, original };
            EditDraftNewRecordRules.SavedState(Array.Empty<Guid>(), _ => true).Should().Be((EditDraftSavedState.NotSaved, Guid.Empty));
            EditDraftNewRecordRules.SavedState(history, _ => false).Should().Be((EditDraftSavedState.NotSaved, Guid.Empty));
            EditDraftNewRecordRules.SavedState(history, o => o == original).Should().Be((EditDraftSavedState.Saved, original), "a saved original");
            EditDraftNewRecordRules.SavedState(history, o => o == recreated).Should().Be((EditDraftSavedState.Saved, recreated), "a saved intermediate recreation");
            EditDraftNewRecordRules.SavedState(history, o => o == newest).Should().Be((EditDraftSavedState.Saved, newest), "the newest recreation saved, cleanup failed, no edit after");
            EditDraftNewRecordRules.SavedState(history, o => o == newest ? null : o == original).Should().Be((EditDraftSavedState.Saved, original), "a found record beats a failed read");
            EditDraftNewRecordRules.SavedState(history, o => o == recreated ? null : false).Should().Be((EditDraftSavedState.CheckFailed, Guid.Empty), "failed and nothing found: ask");
            EditDraftNewRecordRules.SavedState(history, _ => throw new InvalidOperationException()).State.Should().Be(EditDraftSavedState.CheckFailed, "a throwing read is a failed read");
        }
    }

    [TestFixture]
    public class EditDraftNewRecordListTests
    {
        [Test]
        public void T14_E18_the_row_kind_is_new_for_an_empty_target_and_the_open_routes_on_it()
        {
            EditDraftNewRecordRules.IsNewRecordDraft(Guid.Empty).Should().BeTrue();
            EditDraftNewRecordRules.IsNewRecordDraft(Guid.NewGuid()).Should().BeFalse();
            EditDraftNewRecordRules.StateText(Guid.Empty, false).Should().Be("新規");
            EditDraftNewRecordRules.StateText(Guid.NewGuid(), false).Should().Be("既存");
            EditDraftNewRecordRules.StateText(Guid.Empty, true).Should().Be("新規・破棄済み");
            var list = Wave1.Source("Xaf.EditDraft.Blazor/EditDraftListControllerBlazor.cs");
            list.Should().Contain("StateText = EditDraftNewRecordRules.StateText(d.TargetOid, d.DeletedOn != null),");
            // 0.4.0-preview.1: OpenDraft and Recreate carry the row's type (the owner seam is asked per type).
            var open = list.Substring(list.IndexOf("internal void OpenDraft(Guid draftOid, string objectType)", StringComparison.Ordinal));
            open.IndexOf("if (EditDraftNewRecordRules.IsNewRecordDraft(targetOid)) { Recreate(draftOid, objectType, proceedWhenSavedCheckFails: false); return; }", StringComparison.Ordinal)
                .Should().BeGreaterThan(0).And.BeLessThan(open.IndexOf("os.GetObjectByKey(policy.Type, targetOid)", StringComparison.Ordinal),
                    "a 「新規」 row never goes through the existing-record open (no lookup by an empty Oid)");
            open.Should().Contain("var draft = writer.ReadOwn(readSpace, draftOid, owner.Oid);", "the exact row, owner-scoped");
        }

        [Test]
        public void T15_N22_E22_the_notice_counts_live_readable_new_record_rows_of_the_type()
        {
            var now = new DateTime(2026, 10, 3, 9, 0, 0);
            var later = now.AddDays(3);
            (Guid, int, bool, DateTime) Row(Guid target, int version = 1, bool discarded = false, DateTime? expires = null) => (target, version, discarded, expires ?? later);
            EditDraftNewRecordRules.NoticeCount(Array.Empty<(Guid, int, bool, DateTime)>(), now).Should().Be(0);
            EditDraftNewRecordRules.NoticeCount(null, now).Should().Be(0);
            EditDraftNewRecordRules.NoticeCount(new[] { Row(Guid.Empty) }, now).Should().Be(1);
            EditDraftNewRecordRules.NoticeCount(new[] { Row(Guid.Empty), Row(Guid.Empty) }, now).Should().Be(2, "rows, not distinct targets: two unsaved records are two rows");
            EditDraftNewRecordRules.NoticeCount(new[]
            {
                Row(Guid.Empty), Row(Guid.NewGuid()), Row(Guid.Empty, discarded: true), Row(Guid.Empty, expires: now), Row(Guid.Empty, expires: now.AddMinutes(-1)), Row(Guid.Empty, version: 2)
            }, now).Should().Be(1, "existing-record, discarded, expired (ExpiresOn <= now) and unreadable rows do not count");
            EditDraftNewRecordRules.NoticeText(0).Should().BeNull("zero produces no notice");
            EditDraftNewRecordRules.NoticeText(2).Should().Be("新規の入力控が 2 件あります。上の「入力控」から開けます。", "owner D5: Codex's sentence plus the pointer to 「入力控」");

            var badge = Wave1.Source("Xaf.EditDraft.Blazor/EditDraftListBadgeControllerBlazor.cs");
            var notice = badge.Substring(badge.IndexOf("private void PostNewRecordNotice()", StringComparison.Ordinal));
            notice = notice.Substring(0, notice.IndexOf("private void OnDraftWritten(", StringComparison.Ordinal));
            notice.Should().Contain("if (_policy == null || !_policy.AllowNewRecords) return;").And.Contain("Post(() =>")
                .And.Contain("writer.ListOwn(readSpace, owner.Oid, _policy.TypeName, false, now, out var readFailed)")
                .And.NotContain("ListOwnTargets", "the projection cannot tell readability (Codex C19)");
            badge.Should().Contain("ReloadSet(\"activated\", rerender: false);   // before the first render: no re-render needed\r\n        HookGrid();\r\n        PostNewRecordNotice();",
                "once per activation (OnActivated), never on tab activation");
        }
    }

    [TestFixture]
    public class EditDraftNewRecordRecreateTests
    {
        public sealed class FakeCandidate : IEditDraftRecreateCandidate
        {
            private readonly FakeHost _host;
            public FakeCandidate(FakeHost host) { _host = host; }
            public Guid Oid { get; } = Guid.NewGuid();
            public bool Disposed;
            public EditDraftPendingAdoption Adoption;
            public EditDraftPayload Filled;
            public EditDraftNewApplyResult Fill(EditDraftTypePolicy policy, EditDraftPayload payload)
            {
                _host.Calls.Add("fill");
                if (_host.FillThrows) throw new InvalidOperationException();
                Filled = payload;
                return EditDraftNewApplyResult_Of(payload);
            }
            public bool MayRecreate(EditDraftTypePolicy policy) { _host.Calls.Add("mayRecreate"); return _host.FilledPermitted; }
            public bool Show(EditDraftTypePolicy policy, EditDraftPendingAdoption adoption)
            {
                _host.Calls.Add("show");
                if (_host.ShowThrows) throw new InvalidOperationException();
                Adoption = adoption;
                if (_host.ScreenAttaches) adoption.Acknowledge();
                return adoption.IsAcknowledged;
            }
            public void Dispose() { Disposed = true; _host.Calls.Add("dispose"); }
            public bool GuardViolated => _host.GuardViolated;
        }

        private static EditDraftNewApplyResult EditDraftNewApplyResult_Of(EditDraftPayload payload)
        {
            using var os = NewProbe.Space();
            return EditDraftRestorer.ApplyNew(NewProbe.Policy(), os, os.CreateObject<EditDraftNewProbe>(), payload, null);
        }

        public sealed class FakeHost : IEditDraftRecreateHost
        {
            public readonly List<string> Calls = new();
            public EditDraftOwnerInfo Owner = new(Guid.NewGuid());
            public EditDraftRecreateDraft Draft;
            public EditDraftTypePolicy PolicyValue = NewProbe.Policy();
            public readonly List<EditDraftTypePolicy> OwnerAskedFor = new();
            public Func<Guid, bool?> Saved = _ => false;
            public bool Permitted = true, FilledPermitted = true, ScreenAttaches = true, FillThrows, ShowThrows, CandidateFails, GuardViolated;
            public Action OnCreate;   // e.g. the clock moves while the candidate is built
            public DateTime ClaimNow;
            public int Revision = 4;   // the row's current revision (the claim is fenced on it)
            public string ClaimedJson; public Guid ClaimedEditor; public int Claims;
            public FakeCandidate Candidate;
            public DateTime NowValue = new(2026, 10, 3, 9, 0, 0);

            public EditDraftOwnerInfo CurrentOwner(EditDraftTypePolicy policy) { Calls.Add("owner"); OwnerAskedFor.Add(policy); return Owner; }
            public DateTime Now() => NowValue;
            public EditDraftRecreateDraft ReadDraft(Guid draftOid, Guid ownerOid, DateTime now) { Calls.Add("read"); return Draft != null && ownerOid == Owner.Oid ? Draft : null; }
            public EditDraftTypePolicy Policy(string objectType) { Calls.Add("policy"); return PolicyValue; }
            public bool? IsSaved(EditDraftTypePolicy policy, Guid oid) { Calls.Add("saved"); return Saved(oid); }
            public bool MayCreate(EditDraftTypePolicy policy) { Calls.Add("mayCreate"); return Permitted; }
            public IEditDraftRecreateCandidate CreateCandidate(EditDraftTypePolicy policy)
            {
                Calls.Add("create");
                OnCreate?.Invoke();
                if (CandidateFails) return null;
                return Candidate = new FakeCandidate(this);
            }
            public int Claim(EditDraftRecreateDraft draft, Guid ownerOid, Guid editorInstanceId, string payloadJson, int entryCount, DateTime now)
            {
                Calls.Add("claim");
                Claims++;
                ClaimNow = now;
                if (ownerOid != Owner.Oid || draft.Revision != Revision) return 0;   // fenced on owner and revision, like the statement
                ClaimedJson = payloadJson; ClaimedEditor = editorInstanceId;
                return ++Revision;
            }
        }

        private static readonly Guid History = Guid.NewGuid();

        private const string ProbeType = nameof(EditDraftNewProbe);

        private static FakeHost Host(Action<EditDraftPayload> shape = null, bool live = true, Guid? target = null, int revision = 4)
        {
            var payload = new EditDraftPayload { TypeName = nameof(EditDraftNewProbe) };
            payload.Upsert("Reason", "string", "Reason", true, null, null, "typed", "typed");
            payload.Upsert("Day", "datetime", "Day", true, null, null, EditDraftCodec.RawOf(new DateTime(2026, 10, 1)), "2026/10/01", seeded: true);
            payload.AddProvisional(History);
            shape?.Invoke(payload);
            var host = new FakeHost { Revision = revision };
            host.Draft = new EditDraftRecreateDraft
            {
                DraftOid = Guid.NewGuid(), Revision = revision, ObjectType = nameof(EditDraftNewProbe), TargetOid = target ?? Guid.Empty,
                LastCapturedOn = new DateTime(2026, 10, 2, 18, 21, 0), Live = live, PayloadReadable = true,
                PayloadJson = payload.ToJson(), EntryCount = payload.Count
            };
            return host;
        }

        [Test]
        public void T12_N12_N19_E20_the_steps_run_in_the_designed_order_and_success_needs_the_screen_s_acknowledgement()
        {
            var host = Host();
            var r = EditDraftRecreate.Run(host, host.Draft.DraftOid, ProbeType);
            r.Outcome.Should().Be(EditDraftRecreateOutcome.Created);
            // 0.4.0-preview.1: the type's policy first (the owner seam is asked per type); no scope step; step 8 is the access
            // check on the filled, uncommitted object (MayRecreate).
            host.Calls.Should().Equal("policy", "owner", "read", "saved", "mayCreate", "create", "claim", "fill", "mayRecreate", "show");
            host.Candidate.Disposed.Should().BeFalse("the shown screen owns the candidate now");
            var claimed = EditDraftPayload.FromJson<EditDraftPayload>(host.ClaimedJson);
            claimed.ProvisionalOids.Should().Equal(new[] { host.Candidate.Oid, History }, "ONE statement claims and stores the candidate's Oid at the head of prov (D11)");
            host.Candidate.Filled.ProvisionalOids.Should().Equal(claimed.ProvisionalOids, "the screen continues with the history the claim stored");
            host.Candidate.Adoption.Should().NotBeNull();
            host.Candidate.Adoption.DraftOid.Should().Be(host.Draft.DraftOid);
            host.Candidate.Adoption.ClaimedRevision.Should().Be(5).And.Be(r.ClaimedRevision);
            host.Candidate.Adoption.EditorInstanceId.Should().Be(host.ClaimedEditor, "the screen attaches with the EXACT editor id the claim wrote");
            host.Candidate.Adoption.PayloadJson.Should().Be(host.ClaimedJson);
            host.Candidate.Adoption.OwnerOid.Should().Be(host.Owner.Oid);
            r.Draft.LastCapturedOn.Should().Be(new DateTime(2026, 10, 2, 18, 21, 0), "the input time shown with the D11 warning");
        }

        [Test]
        public void ACC_the_owner_seam_is_asked_with_the_type_s_policy_and_a_draft_of_another_type_is_not_live()
        {
            // 0.4.0-preview.1 (owner ruling 2026-10-05): the owner seam receives the policy, so a host can name an owner per type.
            var host = Host();
            EditDraftRecreate.Run(host, host.Draft.DraftOid, ProbeType).Outcome.Should().Be(EditDraftRecreateOutcome.Created);
            host.OwnerAskedFor.Should().Equal(new[] { host.PolicyValue }, "asked once, with the registered policy of the draft's type");

            var other = Host();
            other.Draft = new EditDraftRecreateDraft
            {
                DraftOid = other.Draft.DraftOid, Revision = other.Draft.Revision, ObjectType = "SomethingElse", TargetOid = Guid.Empty,
                Live = true, PayloadReadable = true, PayloadJson = other.Draft.PayloadJson, EntryCount = other.Draft.EntryCount
            };
            var r = EditDraftRecreate.Run(other, other.Draft.DraftOid, ProbeType);
            r.Outcome.Should().Be(EditDraftRecreateOutcome.NotLive, "the row read under the type's owner must be of that type");
            other.Claims.Should().Be(0);
        }

        private static IEnumerable<TestCaseData> RefusalsBeforeTheClaim()
        {
            yield return new TestCaseData((Action<FakeHost>)(h => h.Owner = EditDraftOwnerInfo.None), EditDraftRecreateOutcome.NoOwner).SetName("T12_refusal_no_owner");
            yield return new TestCaseData((Action<FakeHost>)(h => h.Draft = null), EditDraftRecreateOutcome.NotLive).SetName("T12_refusal_not_this_owner_s_row");
            yield return new TestCaseData((Action<FakeHost>)(h => h.Draft = Clone(h.Draft, live: false)), EditDraftRecreateOutcome.NotLive).SetName("T12_refusal_expired");
            yield return new TestCaseData((Action<FakeHost>)(h => h.Draft = Clone(h.Draft, readable: false)), EditDraftRecreateOutcome.Unreadable).SetName("T12_refusal_unreadable_version");
            yield return new TestCaseData((Action<FakeHost>)(h => h.Draft = Clone(h.Draft, json: "{not json")), EditDraftRecreateOutcome.Unreadable).SetName("T12_refusal_malformed_payload");
            yield return new TestCaseData((Action<FakeHost>)(h => h.Draft = Clone(h.Draft, target: Guid.NewGuid())), EditDraftRecreateOutcome.NotNewRecord).SetName("T12_refusal_existing_record_row");
            yield return new TestCaseData((Action<FakeHost>)(h => h.PolicyValue = NewProbe.Policy(allowNew: false)), EditDraftRecreateOutcome.TypeNotAllowed).SetName("T12_refusal_type_not_opted_in");
            yield return new TestCaseData((Action<FakeHost>)(h => h.PolicyValue = null), EditDraftRecreateOutcome.TypeNotAllowed).SetName("T12_refusal_type_not_registered");
            yield return new TestCaseData((Action<FakeHost>)(h => h.Saved = o => o == History), EditDraftRecreateOutcome.AlreadySaved).SetName("T12_refusal_already_saved");
            yield return new TestCaseData((Action<FakeHost>)(h => h.Saved = _ => null), EditDraftRecreateOutcome.SavedCheckFailed).SetName("T12_refusal_saved_check_failed_asks");
            yield return new TestCaseData((Action<FakeHost>)(h => h.Permitted = false), EditDraftRecreateOutcome.NotPermitted).SetName("T12_refusal_create_not_permitted");
            yield return new TestCaseData((Action<FakeHost>)(h => h.CandidateFails = true), EditDraftRecreateOutcome.CandidateFailed).SetName("T12_refusal_candidate_failed");
        }

        private static EditDraftRecreateDraft Clone(EditDraftRecreateDraft d, bool? live = null, bool? readable = null, string json = null, Guid? target = null) => new()
        {
            DraftOid = d.DraftOid, Revision = d.Revision, ObjectType = d.ObjectType, TargetOid = target ?? d.TargetOid,
            LastCapturedOn = d.LastCapturedOn, Live = live ?? d.Live, PayloadReadable = readable ?? d.PayloadReadable, PayloadJson = json ?? d.PayloadJson, EntryCount = d.EntryCount
        };

        [TestCaseSource(nameof(RefusalsBeforeTheClaim))]
        public void T12_N13_E38_E37_a_refusal_before_the_claim_claims_applies_and_shows_nothing(Action<FakeHost> arrange, EditDraftRecreateOutcome expected)
        {
            var host = Host();
            arrange(host);
            var r = EditDraftRecreate.Run(host, Guid.NewGuid(), ProbeType);
            r.Outcome.Should().Be(expected);
            host.Claims.Should().Be(0, "nothing is claimed");
            host.Calls.Should().NotContain(new[] { "claim", "fill", "mayRecreate", "show" });
            r.ClaimedRevision.Should().Be(0);
            host.Revision.Should().Be(4, "the row is unchanged");
            if (host.Candidate != null) host.Candidate.Disposed.Should().BeTrue();
        }

        [Test]
        public void T12_N15_a_draft_with_no_restorable_typed_entry_gets_the_read_only_outcome_without_a_candidate()
        {
            var host = Host(p => { p.Entries.RemoveAll(e => e.Path == "Reason"); p.Upsert("Locked", "string", "Locked", true, null, null, "l", "l"); });
            var r = EditDraftRecreate.Run(host, host.Draft.DraftOid, ProbeType);
            r.Outcome.Should().Be(EditDraftRecreateOutcome.NothingRestorable);
            host.Calls.Should().NotContain("saved").And.NotContain("create").And.NotContain("claim");
            EditDraftNewRecordRules.UnavailableTyped(r.Policy, r.Payload).Select(e => e.Path).Should().Equal("Locked");
        }

        [Test]
        public void T12_N11_a_failed_saved_check_proceeds_only_when_the_person_said_so_and_a_found_record_still_refuses()
        {
            var host = Host();
            host.Saved = _ => null;
            EditDraftRecreate.Run(host, host.Draft.DraftOid, ProbeType, proceedWhenSavedCheckFails: true).Outcome.Should().Be(EditDraftRecreateOutcome.Created);
            var found = Host();
            found.Saved = o => o == History;
            EditDraftRecreate.Run(found, found.Draft.DraftOid, ProbeType, proceedWhenSavedCheckFails: true).Outcome.Should().Be(EditDraftRecreateOutcome.AlreadySaved, "a found record is definitive");
            found.Claims.Should().Be(0);
        }

        private static IEnumerable<TestCaseData> FailuresAfterTheClaim()
        {
            yield return new TestCaseData((Action<FakeHost>)(h => h.FillThrows = true), EditDraftRecreateOutcome.FillFailed, new[] { "claim", "fill" }).SetName("T12_failure_fill");
            yield return new TestCaseData((Action<FakeHost>)(h => h.FilledPermitted = false), EditDraftRecreateOutcome.FilledNotPermitted, new[] { "claim", "fill", "mayRecreate" }).SetName("T12_failure_filled_record_not_permitted");
            yield return new TestCaseData((Action<FakeHost>)(h => h.ShowThrows = true), EditDraftRecreateOutcome.ShowFailed, new[] { "claim", "fill", "mayRecreate", "show" }).SetName("T12_failure_show");
            yield return new TestCaseData((Action<FakeHost>)(h => h.ScreenAttaches = false), EditDraftRecreateOutcome.NotAcknowledged, new[] { "claim", "fill", "mayRecreate", "show" }).SetName("T12_failure_not_acknowledged");
        }

        [TestCaseSource(nameof(FailuresAfterTheClaim))]
        public void T12_N13_N20_E31_a_failure_after_the_claim_disposes_the_candidate_reports_no_success_and_leaves_the_row_at_its_new_revision(
            Action<FakeHost> arrange, EditDraftRecreateOutcome expected, string[] reached)
        {
            var host = Host();
            arrange(host);
            var r = EditDraftRecreate.Run(host, host.Draft.DraftOid, ProbeType);
            r.Outcome.Should().Be(expected);
            r.Outcome.Should().NotBe(EditDraftRecreateOutcome.Created);
            host.Candidate.Disposed.Should().BeTrue("discarded unsaved");
            host.Calls.Last().Should().Be("dispose");
            host.Calls.Where(c => c != "dispose").Skip(6).Should().Equal(reached, "policy, owner, read, saved, mayCreate, create, then the steps reached");
            host.Revision.Should().Be(5, "the claim stands: the row stays live at its new revision (the next 開く re-reads it)");
            r.ClaimedRevision.Should().Be(5);
        }

        [Test]
        public void T12_E36_E44_two_screens_recreating_the_same_draft_exactly_one_wins_and_a_later_open_reads_the_current_revision()
        {
            var host = Host();
            var stale = host.Draft;
            EditDraftRecreate.Run(host, stale.DraftOid, ProbeType).Outcome.Should().Be(EditDraftRecreateOutcome.Created);
            var loser = EditDraftRecreate.Run(host, stale.DraftOid, ProbeType);   // the same revision 4, read before the winner's claim
            loser.Outcome.Should().Be(EditDraftRecreateOutcome.ClaimLost);
            host.Candidate.Disposed.Should().BeTrue("the loser's candidate is discarded: nothing applied, shown or saved");
            host.Calls.TakeLast(3).Should().Equal("create", "claim", "dispose");
            host.Draft = new EditDraftRecreateDraft
            {
                DraftOid = stale.DraftOid, Revision = host.Revision, ObjectType = stale.ObjectType, TargetOid = Guid.Empty, Live = true, PayloadReadable = true,
                PayloadJson = EditDraftPayload.FromJson<EditDraftPayload>(host.ClaimedJson).ToJson(), EntryCount = stale.EntryCount, LastCapturedOn = stale.LastCapturedOn
            };
            EditDraftRecreate.Run(host, stale.DraftOid, ProbeType).Outcome.Should().Be(EditDraftRecreateOutcome.Created, "sequential opens each follow the current revision; no lifetime lock (D11 residual)");
            EditDraftPayload.FromJson<EditDraftPayload>(host.ClaimedJson).ProvisionalOids.Should().HaveCount(3, "original, first recreation, second recreation");
        }
    }

    [TestFixture]
    public class EditDraftNewRecordAdoptionTests
    {
        [Test]
        public void N19_the_pending_adoption_is_a_Core_contract_taken_exactly_once_and_acknowledged_one_way()
        {
            typeof(EditDraftPendingAdoptions).Assembly.Should().BeSameAs(typeof(EditDraftCoreModule).Assembly, "Codex review C6: the capture (Core) takes it");
            var adoptions = new EditDraftPendingAdoptions();
            var record = new object(); var other = new object();
            var a = new EditDraftPendingAdoption(Guid.NewGuid(), 5, Guid.NewGuid(), Guid.NewGuid(), "{}");
            adoptions.Offer(record, a);
            adoptions.Take(other).Should().BeNull("another object's pending draft is never handed over");
            adoptions.Take(record).Should().BeSameAs(a);
            adoptions.Take(record).Should().BeNull("taken once");
            a.IsAcknowledged.Should().BeFalse();
            a.Acknowledge();
            a.IsAcknowledged.Should().BeTrue();
            adoptions.Offer(record, a);
            adoptions.Withdraw(record);
            adoptions.Take(record).Should().BeNull("withdrawn");
        }

        [Test]
        public void N19_E12_it_is_registered_per_circuit_and_two_circuits_never_share_it()
        {
            var services = new ServiceCollection();
            services.AddEditDraftBlazor();
            using var provider = services.BuildServiceProvider();
            using var a = provider.CreateScope();
            using var b = provider.CreateScope();
            var inA = a.ServiceProvider.GetRequiredService<EditDraftPendingAdoptions>();
            a.ServiceProvider.GetRequiredService<EditDraftPendingAdoptions>().Should().BeSameAs(inA);
            b.ServiceProvider.GetRequiredService<EditDraftPendingAdoptions>().Should().NotBeSameAs(inA);
            var record = new object();
            inA.Offer(record, new EditDraftPendingAdoption(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "{}"));
            b.ServiceProvider.GetRequiredService<EditDraftPendingAdoptions>().Take(record).Should().BeNull();
        }

        [Test]
        public void N19_N27_T16_the_capture_attaches_with_the_claimed_editor_id_and_the_save_deletes_that_row()
        {
            var capture = Wave1.Source("Xaf.EditDraft.Core/EditDraftCaptureController.cs");
            var attach = capture.Substring(capture.IndexOf("public bool TryAttachClaimed(Guid draftOid, int claimedRevision, Guid ownerOid, string payloadJson, Guid claimedEditorInstanceId)", StringComparison.Ordinal));
            attach = attach.Substring(0, attach.IndexOf("TakePendingAdoption(object record)", StringComparison.Ordinal));
            attach.Should().Contain("_editorInstanceId = claimedEditorInstanceId;").And.Contain("_slot.Attach(draftOid, claimedRevision)");
            attach.IndexOf("_slot.Attach(draftOid, claimedRevision)", StringComparison.Ordinal).Should().BeLessThan(attach.IndexOf("_editorInstanceId = claimedEditorInstanceId;", StringComparison.Ordinal),
                "the editor id is taken only once the row is attached");
            capture.Should().Contain("if (TryAttachClaimed(adoption.DraftOid, adoption.ClaimedRevision, adoption.OwnerOid, adoption.PayloadJson, adoption.EditorInstanceId))\r\n            adoption.Acknowledge();");
            // Post-review (Codex diffreview D4): the adoption is taken BEFORE the getters, which a recreated record does not run again.
            var bind = capture.Substring(capture.IndexOf("private void BindTo(object record)", StringComparison.Ordinal));
            var take = bind.IndexOf("var adoption = isNew ? TakePendingAdoption(record) : null;", StringComparison.Ordinal);
            var getters = bind.IndexOf("if (adoption == null) RunInitializingGetters(record);   // BEFORE the baseline (fix-529)", StringComparison.Ordinal);
            var baseline = bind.IndexOf("_baseline = SnapshotBaseline(record);", StringComparison.Ordinal);
            var attachAt = bind.IndexOf("if (adoption != null) AttachPendingAdoption(adoption);", StringComparison.Ordinal);
            take.Should().BeGreaterThan(0);
            take.Should().BeLessThan(getters);
            getters.Should().BeLessThan(baseline);
            baseline.Should().BeLessThan(attachAt, "the baseline is the filled record; the attached payload holds the draft");
            capture.Should().Contain("n = writer.DeleteOwn(own, owner, editor);", "the first save deletes the attached row with the screen's (claimed) editor id");

            // The slot side: the attached row is the one the save hands back for deletion; a failed save hands nothing back.
            var slot = new DraftWriteSlot<string>();
            var row = Guid.NewGuid();
            slot.Attach(row, 5).Should().BeTrue();
            slot.OnSaved().Should().Be(row, "delete on save (owner D3)");
            var pending = new DraftWriteSlot<string>();
            pending.TryBeginWrite("create", out var ticket).Should().BeTrue();
            pending.OnSaved().Should().Be(Guid.Empty, "the create is still running");
            pending.CompleteCreate(ticket, row).RowToDelete.Should().Be(row, "a create that finishes after the save hands its row back to be deleted");
        }
    }

    [TestFixture]
    public class EditDraftNewRecordWriterAndTextTests
    {
        [Test]
        public void N10_the_recreate_claim_is_one_statement_fenced_on_Oid_revision_owner_liveness_and_the_new_record_marker()
        {
            var writer = Wave1.Source("Xaf.EditDraft.Core/EditDraftWriter.cs");
            var m = writer.Substring(writer.LastIndexOf("public int TryClaimNew(", StringComparison.Ordinal));   // the SQL writer, after the forwarders
            m = m.Substring(0, m.IndexOf("public int DeleteOwn(", StringComparison.Ordinal));
            Regex.Matches(m, "Execute\\(").Count.Should().Be(1, "ONE statement");
            m.Should().Contain("SET [EditorInstanceId] = @p0, [Revision] = [Revision] + 1, [DeletedOn] = NULL, [LastCapturedOn] = @p1, [Payload] = @p2, [EntryCount] = @p3 ")
                .And.Contain("WHERE [Oid] = @p4 AND [Revision] = @p5 AND [OwnerUserOid] = @p6 AND [ExpiresOn] > @p1 AND [TargetOid] = @p7")
                .And.Contain("new object[] { editorInstanceId, now, payloadJson, entryCount, oid, expectedRevision, ownerOid, Guid.Empty }")
                .And.Contain("return n == 1 ? expectedRevision + 1 : 0;");
            Regex.Matches(writer, @"UPDATE \[\{Table\}\] SET[^""]*\[ExpiresOn\]\s*=").Count.Should().Be(0, "no statement moves the expiry");
            var noStore = new EditDraftWriter(new FixedServices());
            noStore.TryClaimNew(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "{}", 0, DateTime.Now).Should().Be(0, "no store: fail closed");
        }

        [Test]
        public void N24_the_new_texts_exist_in_both_sets_and_the_Japanese_ones_are_the_design_s_words()
        {
            var ja = EditDraftTextSet.Japanese;
            ja.StateNew.Should().Be("新規");
            ja.NewRecordNotice.Should().Be("新規の入力控が {0} 件あります。上の「入力控」から開けます。");
            ja.RecreateWarning.Should().Be("元の画面がまだ開いている場合は、そちらで保存してください。");
            ja.RecreateAlreadySaved.Should().Be("この入力控の記録はすでに保存されています。");
            ja.RecreateNoPermission.Should().Be("この記録を作成する権限がありません。");
            ja.RecreateClaimLost.Should().Be("この入力控は戻せません（ほかの画面で戻されたか、変更されたか、期限切れです）。");
            string.Format(ja.Recreated, new DateTime(2026, 10, 2, 18, 21, 0)).Should().Be("入力控（入力 2026/10/02 18:21）から記録を作成しました。まだ保存されていません — 内容を確認して保存してください。");
            string.Format(ja.RecreatedPartly, new DateTime(2026, 10, 2, 18, 21, 0), 2).Should().Contain("（2 項目は戻せませんでした）");
            foreach (var p in typeof(EditDraftTextSet).GetProperties().Where(p => p.PropertyType == typeof(string)))
            {
                p.GetValue(EditDraftTextSet.English).Should().NotBeNull(p.Name);
                p.GetValue(EditDraftTextSet.Japanese).Should().NotBeNull(p.Name);
            }
            var current = EditDraftTexts.Current;
            try
            {
                EditDraftTexts.Use(EditDraftLanguage.English);
                EditDraftNewRecordRules.StateText(Guid.Empty, false).Should().Be("New");
                EditDraftNewRecordRules.NoticeText(1).Should().Be("There are 1 draft(s) of new records. Open them from \"Drafts\" at the top.");
            }
            finally { EditDraftTexts.Use(current); }
        }

        [Test]
        public void SEC_D14_create_access_decision_fails_closed_and_the_recreate_host_asks_the_access_check_on_the_filled_record()
        {
            // SINGLE-MODEL (Claude only; owner review D14). 0.4.0-preview.1: the scope check is gone (its two assertions are
            // removed with it); the filled record is asked EditDraftServices.MayRecreate.
            EditDraftCreateAccess.Decide(true, true, true, true).Should().BeTrue();
            foreach (var (n, l, d, g) in new[] { (false, true, true, true), (true, false, true, true), (true, true, false, true), (true, true, true, false) })
                EditDraftCreateAccess.Decide(n, l, d, g).Should().BeFalse($"allowNew={n} listAllowNew={l} detailAllowEdit={d} granted={g}");
            EditDraftCreateAccess.MayCreate(null, NewProbe.Policy()).Should().BeFalse("no application");
            var recreateHost = Wave1.Source("Xaf.EditDraft.Blazor/EditDraftRecreateHostBlazor.cs");
            recreateHost.Should().Contain("public bool MayCreate(EditDraftTypePolicy policy) => EditDraftCreateAccess.MayCreate(_application, policy);")
                .And.Contain("public bool MayRecreate(EditDraftTypePolicy policy) => EditDraftServices.MayRecreate(_host._application, policy, _record);")
                .And.Contain("TargetWindow = TargetWindow.NewModalWindow", "owner D6: a modal window, like today's 開く")
                .And.Contain("var d = _writer.ReadOwn(readSpace, draftOid, ownerOid);   // owner-scoped (single-model)");
            recreateHost.Should().NotContain("RecordAccess").And.NotContain("IsScopeVisible");
            var seam = Wave1.Source("Xaf.EditDraft.Core/EditDraftAccessSeam.cs");
            seam.Should().Contain("if (application.Security is IRequestSecurity)")
                .And.Contain("DataManipulationRight.HasPermissionTo(policy.Type, null, null, objectSpace, SecurityOperations.Create)", "the call KB fix-531 uses");
        }
    }

    /// <summary>
    /// Fixes for the Codex diffreview of this run (diffreview a1, D1-D5). Written AFTER the review: not cross-reviewed.
    /// </summary>
    [TestFixture]
    public class EditDraftNewRecordReviewFixTests
    {
        /// <summary>A service provider whose scope factory counts and refuses: every write attempt is one scope request, a skipped write none.</summary>
        private sealed class CountingProvider : IServiceProvider, IServiceScopeFactory
        {
            public int ScopeRequests;
            public object GetService(Type t) { if (t == typeof(IServiceScopeFactory)) { Interlocked.Increment(ref ScopeRequests); return this; } return null; }
            public IServiceScope CreateScope() => throw new InvalidOperationException("no database in this test");
        }

        private static EditDraftCaptureController.DraftSnapshot Snap(bool isNew, Func<bool> gate) =>
            new(new EditDraftSeed { OwnerUserOid = Guid.NewGuid(), TargetOid = isNew ? Guid.Empty : Guid.NewGuid(), IsNew = isNew, ObjectType = "X", EditorInstanceId = Guid.NewGuid() },
                "{}", 0, new DateTime(2026, 10, 3, 9, 0, 0), 1L, new Dictionary<string, long>()) { NewRecordsGate = gate };

        [Test]
        public void D2_D15_the_new_record_gate_is_per_ticket_and_an_existing_record_write_coalesced_after_the_save_never_depends_on_it()
        {
            var provider = new CountingProvider();
            var writer = new EditDraftWriter(provider, typeof(EditDraftTestStore));
            var slot = new DraftWriteSlot<EditDraftCaptureController.DraftSnapshot>();
            slot.TryBeginWrite(Snap(isNew: true, gate: () => false), out var newWrite).Should().BeTrue();   // a new-record write in flight, key OFF
            slot.OnSaved();                                                                                    // the record is saved meanwhile
            slot.TryBeginWrite(Snap(isNew: false, gate: null), out _).Should().BeFalse("queued behind the running write");
            var next = EditDraftCaptureController.RunOneWrite(slot, writer, newWrite, _ => { }, (a, b) => { }, () => true);
            provider.ScopeRequests.Should().Be(0, "the never-saved record's write is skipped while the new-record key is off");
            next.Should().NotBeNull("the existing-record snapshot is handed on");
            EditDraftCaptureController.RunOneWrite(slot, writer, next.Value, _ => { }, (a, b) => { }, () => true);
            provider.ScopeRequests.Should().Be(1, "the saved record's write is attempted whatever the new-record key says");
            slot.IsWriteInFlight.Should().BeFalse();

            var on = new DraftWriteSlot<EditDraftCaptureController.DraftSnapshot>();
            on.TryBeginWrite(Snap(isNew: true, gate: () => true), out var allowed).Should().BeTrue();
            EditDraftCaptureController.RunOneWrite(on, writer, allowed, _ => { }, (a, b) => { }, () => true);
            provider.ScopeRequests.Should().Be(2, "key on: the new record's write is attempted");
            var off = new DraftWriteSlot<EditDraftCaptureController.DraftSnapshot>();
            off.TryBeginWrite(Snap(isNew: true, gate: () => true), out var global).Should().BeTrue();
            EditDraftCaptureController.RunOneWrite(off, writer, global, _ => { }, (a, b) => { }, () => false);
            provider.ScopeRequests.Should().Be(2, "the slot's own switch still applies first");
        }

        [Test]
        public void D1_N07_a_fresh_start_keeps_a_reconstruction_member_that_was_typed_and_already_stored()
        {
            var old = new EditDraftPayload { TypeName = "X" };
            old.Upsert("Day", "datetime", "d", true, "b", "b", "typed-day", "typed-day");     // typed context, already stored
            old.Upsert("Owner", "string", "o", true, "login-1", "login-1", "login-1", "login-1", seeded: true);
            old.Upsert("Note", "string", "n", true, null, null, "stored note", "stored note"); // typed, stored, not context
            old.Upsert("Reason", "string", "r", true, null, null, "fresh", "fresh");          // typed, never stored
            old.AddProvisional(Guid.NewGuid());
            var context = new[] { "Owner", "Day", "Start" };
            var fresh = EditDraftCaptureRules.FreshAfterGone(old, e => e.Path == "Reason", out var kept, context);
            kept.Should().Be(1, "only the never-stored typed member counts towards writing");
            fresh.Entries.Select(e => (e.Path, e.Seeded)).Should().Equal(("Day", false), ("Owner", true), ("Reason", false));
            fresh.Get("Note").Should().BeNull("an unrelated stored edit does not come back");
            EditDraftCaptureRules.FreshAfterGone(old, e => e.Path == "Reason", out _).Get("Day").Should().BeNull("without the context list (an existing record) the old rule holds");
            EditDraftCaptureRules.FreshAfterGone(old, e => false, out _, context).Should().BeNull("context and seeds alone are never written");

            var capture = Wave1.Source("Xaf.EditDraft.Core/EditDraftCaptureController.cs");
            capture.Should().Contain("isNew ? _policy?.NewRecordReconstructionOrder : null);")
                .And.Contain("if (fresh != null && isNew) EditDraftCaptureRules.Seed(_policy, fresh, _baseline, _record);")
                .And.Contain("out var kept, newest.Context);")
                .And.Contain("Context = _policy?.NewRecordReconstructionOrder");
        }

        [Test]
        public void D3_E39_the_claim_reads_now_when_it_runs_not_when_the_draft_was_read()
        {
            var payload = new EditDraftPayload { TypeName = nameof(EditDraftNewProbe) };
            payload.Upsert("Reason", "string", "Reason", true, null, null, "typed", "typed");
            var host = new EditDraftNewRecordRecreateTests.FakeHost();
            host.Draft = new EditDraftRecreateDraft
            {
                DraftOid = Guid.NewGuid(), Revision = 4, ObjectType = nameof(EditDraftNewProbe), TargetOid = Guid.Empty, Live = true, PayloadReadable = true,
                PayloadJson = payload.ToJson(), EntryCount = 1
            };
            var later = host.NowValue.AddMinutes(3);
            host.OnCreate = () => host.NowValue = later;   // the clock moves while the candidate is built
            EditDraftRecreate.Run(host, host.Draft.DraftOid, nameof(EditDraftNewProbe)).Outcome.Should().Be(EditDraftRecreateOutcome.Created);
            host.ClaimNow.Should().Be(later, "the [ExpiresOn] > now fence is tested at the claim");
        }

        [Test]
        public void D5_a_guard_violation_during_the_recreate_is_never_a_full_success()
        {
            var payload = new EditDraftPayload { TypeName = nameof(EditDraftNewProbe) };
            payload.Upsert("Reason", "string", "Reason", true, null, null, "typed", "typed");
            var host = new EditDraftNewRecordRecreateTests.FakeHost { GuardViolated = true };
            host.Draft = new EditDraftRecreateDraft
            {
                DraftOid = Guid.NewGuid(), Revision = 4, ObjectType = nameof(EditDraftNewProbe), TargetOid = Guid.Empty, Live = true, PayloadReadable = true,
                PayloadJson = payload.ToJson(), EntryCount = 1
            };
            var r = EditDraftRecreate.Run(host, host.Draft.DraftOid, nameof(EditDraftNewProbe));
            r.Outcome.Should().Be(EditDraftRecreateOutcome.Created, "the record is on screen, unsaved, and holds the draft");
            r.GuardViolated.Should().BeTrue();
            var list = Wave1.Source("Xaf.EditDraft.Blazor/EditDraftListControllerBlazor.cs");
            list.Should().Contain("if (r.GuardViolated) text += \" \" + EditDraftTexts.Of(t => t.ApplyGuardStopped);")
                .And.Contain("var full = notApplied.Count == 0 && !r.GuardViolated;")
                .And.Contain("full ? InformationType.Success : InformationType.Warning");
            Wave1.Source("Xaf.EditDraft.Blazor/EditDraftRecreateHostBlazor.cs").Should().Contain("public bool GuardViolated => _guard?.Violated ?? false;");
        }
    }
}
