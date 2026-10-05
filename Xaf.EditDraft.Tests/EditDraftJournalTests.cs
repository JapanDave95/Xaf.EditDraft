using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor.Components.Models;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Xaf.EditDraft.Blazor;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests
{
    // Client-side input journal, phase 2 milestone M1 (docs/edit-draft-client-journal-design-2026-10-03.md;
    // docs/edit-draft-client-journal-m0-2026-10-03.md section 5 and section 9). Expectations come from the Codex
    // requirement-only list of run 2026-10-04-edit-draft-journal-m1-96623a (tests a1, T1-T88), written before any M1 code
    // existed; labels Tn refer to it. The browser module's rules are tested in Xaf.EditDraft.Tests/js (npm test). Here: the
    // switch, the attribute controller's pure parts and lifecycle (source pins: no XAF frame runs in this project), the Core
    // rules, the trust-boundary parsers (single-model, Claude's alone) and the texts.

    [TestFixture]
    public class EditDraftJournalSwitchTests
    {
        private static IServiceProvider Services(Dictionary<string, string> values, string section = null)
        {
            var s = new FixedServices().Add<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
            return section == null ? s : s.Add(new EditDraftSwitchOptions { Section = section });
        }

        [TestCase("true", "true", null, false, true)]
        [TestCase(null, "true", null, false, false)]
        [TestCase("", "true", null, false, false)]
        [TestCase("false", "true", null, false, false)]
        [TestCase("1", "true", null, false, false)]
        [TestCase("yes", "true", null, false, false)]
        [TestCase(" TRUE ", "True", null, false, true)]
        [TestCase("true", null, null, false, false)]
        [TestCase("true", "false", null, false, false)]
        [TestCase("true", "true", null, true, false)]
        [TestCase("true", "true", "false", true, false)]
        [TestCase("true", "true", "true", true, true)]
        public void T59_T61_the_journal_needs_its_own_key_and_the_type_key_and_for_a_new_record_the_new_record_key_all_true(string journal, string type, string newRecords, bool isNew, bool expected)
        {
            EditDraftSwitch.DecideJournal(journal, type, newRecords, isNew).Should().Be(expected);
        }

        [Test]
        public void T59_the_key_is_read_at_every_use_fails_closed_and_the_global_capture_key_does_not_gate_it()
        {
            EditDraftSwitch.JournalKey.Should().Be("EditDraftCapture:Journal:Enabled");
            var values = new Dictionary<string, string> { ["EditDraftCapture:Types:ToDo:Enabled"] = "true" };
            var services = Services(values);
            EditDraftSwitch.IsJournalEnabled(services, "ToDo", false).Should().BeFalse("missing key = off");
            var config = (IConfiguration)services.GetService(typeof(IConfiguration));
            config["EditDraftCapture:Journal:Enabled"] = "true";
            config["EditDraftCapture:Enabled"] = "false";
            EditDraftSwitch.IsJournalEnabled(services, "ToDo", false).Should().BeTrue(
                "M1 brief 2026-10-04: journal key AND type key; the browser check runs with server capture off (deviation from the design's wording, reported)");
            EditDraftSwitch.IsJournalEnabled(services, "ToDo", true).Should().BeFalse("a never-saved record also needs the new-record key");
            config["EditDraftCapture:Journal:Enabled"] = "false";
            EditDraftSwitch.IsJournalEnabled(services, "ToDo", false).Should().BeFalse("re-read at every use");
            EditDraftSwitch.IsJournalEnabled(services, null, false).Should().BeFalse();
            EditDraftSwitch.IsJournalEnabled(new FixedServices(), "ToDo", false).Should().BeFalse("no configuration = off");
            EditDraftSwitch.IsJournalEnabled(null, "ToDo", false).Should().BeFalse();
        }

        [Test]
        public void T60_the_key_is_read_in_the_configured_section_and_a_default_section_value_does_not_leak_into_it()
        {
            var values = new Dictionary<string, string>
            {
                ["EditDraftCapture:Journal:Enabled"] = "true", ["EditDraftCapture:Types:ToDo:Enabled"] = "true",
                ["Other:Types:ToDo:Enabled"] = "true"
            };
            EditDraftSwitch.IsJournalEnabled(Services(values), "ToDo", false).Should().BeTrue();
            EditDraftSwitch.IsJournalEnabled(Services(values, "Other"), "ToDo", false).Should().BeFalse("Other:Journal:Enabled is absent");
            values["Other:Journal:Enabled"] = "true";
            EditDraftSwitch.IsJournalEnabled(Services(values, "Other"), "ToDo", false).Should().BeTrue();
        }
    }

    [TestFixture]
    public class EditDraftJournalControllerTests
    {
        private const string Controller = "Xaf.EditDraft.Blazor/EditDraftJournalAttributeControllerBlazor.cs";

        [Test]
        public void T62_T69_DetailViews_only_attribute_through_the_documented_hook_removed_when_no_longer_admitted()
        {
            typeof(EditDraftJournalAttributeControllerBlazor).BaseType.Should().Be(typeof(ViewController<DetailView>), "DetailViews only (owner U2/U4)");
            var src = Wave1.Source(Controller);
            src.Should().Contain("View.CustomizeViewItemControl<BlazorPropertyEditorBase>(this, Customize);", "existing and later controls; released on deactivation by XAF")
                .And.Contain("if (editor?.ComponentModel is not ComponentModelBase model) return;", "a model that is not a ComponentModelBase is skipped")
                .And.Contain("model.SetAttribute(AttributeName, json);")
                .And.Contain("if (_attributed.Remove(editor)) model.RemoveAttribute(AttributeName);", "an editor that is no longer admitted loses the attribute")
                .And.Contain("EditDraftCaptureController.IsAdmittedViewIncludingNew(policy, View.Id, View.IsRoot, isNew)")
                .And.Contain("EditDraftSwitch.IsJournalEnabled(services, policy.PolicyId, isNew)")
                .And.Contain("if (owner.IsNone) return null;", "no attribute for a login without an owner");
            EditDraftJournalAttributeControllerBlazor.AttributeName.Should().Be("data-editdraft");
            EditDraftJournalAttributeControllerBlazor.ModulePath.Should().Be("./_content/Xaf.EditDraft.Blazor/edit-draft-journal.js");
        }

        [Test]
        public void T68_generation_moves_on_for_a_recreated_control_a_record_change_and_a_save_and_a_record_change_drops_the_attributes_at_once()
        {
            var src = Wave1.Source(Controller).Replace("\r\n", "\n");
            var customize = src.Substring(src.IndexOf("private void Customize(", StringComparison.Ordinal));
            customize.Substring(0, customize.IndexOf("\n    }", StringComparison.Ordinal))
                .Should().Contain("_generation[editor] = _generation.TryGetValue(editor, out var g) ? g + 1 : 1;");
            var changed = src.Substring(src.IndexOf("private void View_CurrentObjectChanged(", StringComparison.Ordinal));
            changed = changed.Substring(0, changed.IndexOf("\n    }", StringComparison.Ordinal));
            changed.IndexOf("RemoveAttribute(AttributeName)", StringComparison.Ordinal)
                .Should().BePositive().And.BeLessThan(changed.IndexOf("NextGeneration()", StringComparison.Ordinal), "removed in the same render as the new record's values");
            src.Should().Contain("private void ObjectSpace_Committed(").And.Contain("_circuit.Post(_ => { if (View != null) NextGeneration(); }, null);");
        }

        [Test]
        public void T63_the_known_component_models_map_to_their_kind_and_effective_format()
        {
            EditDraftJournalAttributeControllerBlazor.KindOf(new DxMemoModel(), "string").Should().Be(("memo", (string)null));
            EditDraftJournalAttributeControllerBlazor.KindOf(new DxTextBoxModel(), "string").Should().Be(("text", (string)null));
            EditDraftJournalAttributeControllerBlazor.KindOf(new DxComboBoxModel<string, string>(), "string").Should().Be(("combo", (string)null));
            EditDraftJournalAttributeControllerBlazor.KindOf(new DxMaskedInputModel<string> { Mask = "000-0000" }, "string").Should().Be(("masked", "000-0000"));
            EditDraftJournalAttributeControllerBlazor.KindOf(new DxTimeEditModel<TimeSpan> { Format = "HH:mm" }, "timespan").Should().Be(("time", "HH:mm"));
            EditDraftJournalAttributeControllerBlazor.KindOf(new DxTimeEditModel<TimeSpan>(), "timespan").Should().Be(("time", (string)null), "format unknown: copy-only by the descriptor");
            EditDraftJournalAttributeControllerBlazor.KindOf(new DxTimeEditModel<DateTime> { Format = "HH:mm" }, "datetime").Should().Be(("time", "HH:mm"));
            EditDraftJournalRules.IsJournaledMemberKind("string", false).Should().BeTrue();
            EditDraftJournalRules.IsJournaledMemberKind("timespan", false).Should().BeTrue();
            EditDraftJournalRules.IsJournaledMemberKind("datetime", false).Should().BeFalse("a DateTime member only when the policy lists it");
            EditDraftJournalRules.IsJournaledMemberKind("datetime", true).Should().BeTrue();
            new EditDraftTypePolicy(typeof(EditDraftNewProbe)).JournalTimeOfDayMembers.Should().BeEmpty("opt-in per member, none by default");
        }

        [Test]
        public void T64_lookups_numbers_booleans_dates_and_unknown_components_get_no_attribute()
        {
            foreach (var kind in new[] { "ref", "int", "double", "decimal", "bool", "enum", "guid" })
                EditDraftJournalRules.IsJournaledMemberKind(kind, true).Should().BeFalse(kind);
            EditDraftJournalAttributeControllerBlazor.KindOf(new DxSpinEditModel<int>(), "string").Should().Be(((string)null, (string)null));
            EditDraftJournalAttributeControllerBlazor.KindOf(new DxDateEditModel<DateTime>(), "datetime").Should().Be(((string)null, (string)null), "a date editor is not a time editor");
            EditDraftJournalAttributeControllerBlazor.KindOf(new object(), "string").Should().Be(((string)null, (string)null), "an unknown (custom) component model: no guess from a descendant input");
            EditDraftJournalAttributeControllerBlazor.KindOf(new DxMemoModel(), "ref").Should().Be(((string)null, (string)null));
            // 0.4.0-preview.1: the owner kind is gone; a policy without a decision table (a host's own policy) stays out.
            var chart = new EditDraftTypePolicy(typeof(EditDraftNewProbe)) { PolicyId = "chart", AllowNewRecords = true };
            EditDraftCaptureController.IsAdmittedViewIncludingNew(chart, NewProbe.View, true, false).Should().BeFalse("policies without a decision table stay out (owner decision 15)");
        }

        [Test]
        public void T67_reconstruction_raws_are_the_policy_s_order_members_as_canonical_raws_and_none_without_an_order()
        {
            using var os = NewProbe.Space();
            var record = os.CreateObject<EditDraftNewProbe>();
            var raws = EditDraftJournalAttributeControllerBlazor.ReconstructionRaws(NewProbe.Policy(), record);
            raws.Keys.Should().Equal("Owner", "Day", "Start");
            raws["Owner"].Should().Be("login-1");
            raws["Day"].Should().Be(EditDraftCodec.RawOf(record.Day));
            raws["Start"].Should().Be(EditDraftCodec.RawOf(record.Start));
            var noOrder = new EditDraftTypePolicy(typeof(EditDraftNewProbe)) { PolicyId = "p", Decisions = NewProbe.Policy().Decisions };
            EditDraftJournalAttributeControllerBlazor.ReconstructionRaws(noOrder, record).Should().BeNull("no invented reconstruction requirement");
        }

        [Test]
        public void T30_the_coverage_line_compares_admitted_members_with_the_roots_of_the_same_view_and_context_only()
        {
            var json = JsonSerializer.Serialize(new
            {
                invalid = 1,
                groups = new object[]
                {
                    new { w = "ToDo_DetailView", ctx = "abc", members = new object[] { new { m = "Description", field = true }, new { m = "ToDoItem", field = false } } },
                    new { w = "ToDo_DetailView", ctx = "other", members = new object[] { new { m = "Description", field = true } } }
                }
            });
            var admitted = new Dictionary<string, string> { ["Description"] = "memo", ["Start"] = "time", ["ToDoItem"] = "combo" };
            var line = EditDraftJournalAttributeControllerBlazor.CoverageLine("ToDo_DetailView", "abc", admitted, json);
            line.Should().Contain("admitted=3 found=2").And.Contain("notRendered=[Start]").And.Contain("noField=[ToDoItem]")
                .And.Contain("otherGroups=1").And.Contain("invalid=1");
            EditDraftJournalAttributeControllerBlazor.CoverageLine("v", "c", admitted, "{bad").Should().Contain("unreadable coverage report");
        }
    }

    [TestFixture]
    public class EditDraftJournalDescriptorTests
    {
        private static readonly Guid Ctx = Guid.Parse("0123456789abcdef0123456789abcdef");

        private static EditDraftJournalDescriptor Build(string member = "Reason", string kind = "memo", string format = null, int generation = 1,
                                                        string ns = "a1b2", string baselineRaw = "base", bool known = true, IReadOnlyDictionary<string, string> rc = null) =>
            EditDraftJournalDescriptor.Build(ns, NewProbe.Policy(), "11111111-2222-3333-4444-555555555555", Ctx, "EditDraftNewProbe_DetailView", member,
                                            kind, format, baselineRaw, known, generation, rc);

        [Test]
        public void T65_the_descriptor_carries_the_full_identity_and_no_new_record_flag()
        {
            var d = Build(kind: "time", format: "HH:mm", member: "Start");
            using var doc = JsonDocument.Parse(d.ToJson());
            var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            names.Should().BeEquivalentTo(new[] { "v", "ns", "p", "t", "o", "ctx", "w", "m", "k", "f", "co", "bh", "g" });
            doc.RootElement.GetProperty("v").GetInt32().Should().Be(1);
            doc.RootElement.GetProperty("p").GetString().Should().Be("test:New");
            doc.RootElement.GetProperty("t").GetString().Should().Be("EditDraftNewProbe");
            doc.RootElement.GetProperty("ctx").GetString().Should().Be("0123456789abcdef0123456789abcdef");
            doc.RootElement.GetProperty("m").GetString().Should().Be("Start");
            doc.RootElement.GetProperty("f").GetString().Should().Be("HH:mm");
            doc.RootElement.GetProperty("bh").GetString().Should().Be(EditDraftJournalRules.BaselineHash("base"));
            names.Should().NotContain(new[] { "n", "isNew", "new" }, "new versus existing is decided at intake by a fresh read (design S12)");
        }

        [Test]
        public void T61_T70_no_descriptor_for_a_member_outside_the_policy_an_unknown_kind_or_an_incomplete_identity()
        {
            Build(member: "Nope").Should().BeNull();
            Build(member: "FilledCount").Should().BeNull("read-only, not captured");
            Build(kind: "rich").Should().BeNull();
            Build(generation: 0).Should().BeNull();
            Build(ns: null).Should().BeNull();
            Build(ns: "a|b").Should().BeNull("a separator in a key part");
            EditDraftJournalDescriptor.Build("ns", NewProbe.Policy(), null, Guid.Empty, "v", "Reason", "memo", null, null, false, 1).Should().BeNull("no editing context");
            EditDraftJournalDescriptor.Build("ns", null, null, Ctx, "v", "Reason", "memo", null, null, false, 1).Should().BeNull();
            Build(known: false).BaselineHash.Should().BeNull("baseline not known");
            Build(rc: new Dictionary<string, string>()).Reconstruction.Should().BeNull("no empty reconstruction map");
        }

        [Test]
        public void T70_build_then_parse_keeps_every_field_and_parsing_recomputes_copy_only()
        {
            var rc = new Dictionary<string, string> { ["Owner"] = "login-1", ["Day"] = "2026-10-04T00:00:00.0000000", ["Start"] = null };
            var d = EditDraftJournalDescriptor.Build("a1b2", NewProbe.Policy(), "11111111-2222-3333-4444-555555555555", Ctx, "画面_DetailView|x", "Start",
                                                     "time", @"HH\:mm", null, true, 3, rc);
            EditDraftJournalBoundary.TryParseDescriptor(d.ToJson(), out var back).Should().BeTrue();
            back.Should().BeEquivalentTo(d, o => o.Excluding(x => x.Reconstruction));
            back.Reconstruction.Should().BeEquivalentTo(rc);
            var forged = d.ToJson().Replace("\"co\":false", "\"co\":true");
            EditDraftBoundaryHelpers.Parse(forged).CopyOnly.Should().BeFalse("copy-only is recomputed from kind and format, never taken from the input");
        }

        [TestCase("{")]
        [TestCase("[]")]
        [TestCase("")]
        [TestCase("{\"v\":2,\"ns\":\"n\",\"p\":\"p\",\"t\":\"t\",\"ctx\":\"0123456789abcdef0123456789abcdef\",\"w\":\"w\",\"m\":\"m\",\"k\":\"memo\",\"co\":false,\"g\":1}")]
        [TestCase("{\"v\":1,\"ns\":\"\",\"p\":\"p\",\"t\":\"t\",\"ctx\":\"0123456789abcdef0123456789abcdef\",\"w\":\"w\",\"m\":\"m\",\"k\":\"memo\",\"co\":false,\"g\":1}")]
        [TestCase("{\"v\":1,\"ns\":\"n|x\",\"p\":\"p\",\"t\":\"t\",\"ctx\":\"0123456789abcdef0123456789abcdef\",\"w\":\"w\",\"m\":\"m\",\"k\":\"memo\",\"co\":false,\"g\":1}")]
        [TestCase("{\"v\":1,\"ns\":\"n\",\"p\":\"p\",\"t\":\"t\",\"ctx\":\"not-a-guid\",\"w\":\"w\",\"m\":\"m\",\"k\":\"memo\",\"co\":false,\"g\":1}")]
        [TestCase("{\"v\":1,\"ns\":\"n\",\"p\":\"p\",\"t\":\"t\",\"ctx\":\"0123456789abcdef0123456789abcdef\",\"w\":\"w\",\"m\":\"m\",\"k\":\"rich\",\"co\":false,\"g\":1}")]
        [TestCase("{\"v\":1,\"ns\":\"n\",\"p\":\"p\",\"t\":\"t\",\"ctx\":\"0123456789abcdef0123456789abcdef\",\"w\":\"w\",\"m\":\"m\",\"k\":\"memo\",\"co\":false,\"g\":0}")]
        [TestCase("{\"v\":1,\"ns\":\"n\",\"p\":\"p\",\"t\":\"t\",\"ctx\":\"0123456789abcdef0123456789abcdef\",\"w\":\"w\",\"m\":\"m\",\"k\":\"memo\",\"co\":\"no\",\"g\":1}")]
        [TestCase("{\"v\":1,\"ns\":\"n\",\"p\":\"p\",\"t\":\"t\",\"ctx\":\"0123456789abcdef0123456789abcdef\",\"w\":\"w\",\"m\":\"m\",\"k\":\"memo\",\"co\":false,\"g\":1,\"o\":\"not-a-guid\"}")]
        [TestCase("{\"v\":1,\"ns\":\"n\",\"p\":\"p\",\"t\":\"t\",\"ctx\":\"0123456789abcdef0123456789abcdef\",\"w\":\"w\",\"m\":\"m\",\"k\":\"memo\",\"co\":false,\"g\":1,\"rc\":[1]}")]
        [TestCase("{\"v\":1,\"ns\":\"n\",\"p\":\"p\",\"t\":\"t\",\"ctx\":\"0123456789abcdef0123456789abcdef\",\"w\":\"w\",\"m\":\"m\",\"k\":\"memo\",\"co\":false,\"g\":1,\"f\":5}")]
        public void T70_an_invalid_or_unsupported_descriptor_never_parses(string json)
        {
            EditDraftJournalBoundary.TryParseDescriptor(json, out var d).Should().BeFalse();
            d.Should().BeNull();
        }

        [Test]
        public void T70_a_stored_entry_parses_only_under_the_key_its_own_fields_make()
        {
            var key = EditDraftJournalRules.Key("a1b2", "load1", "0123456789abcdef0123456789abcdef", "Reason", 2);
            key.Should().Be("XafEditDraft.j1|a1b2|load1|0123456789abcdef0123456789abcdef|Reason|2");
            const string stored = "{\"at\":1791104400000,\"f\":1,\"seq\":7,\"ns\":\"a1b2\",\"load\":\"load1\",\"p\":\"test:New\",\"t\":\"EditDraftNewProbe\",\"o\":null,"
                                + "\"ctx\":\"0123456789abcdef0123456789abcdef\",\"w\":\"v\",\"m\":\"Reason\",\"k\":\"memo\",\"fmt\":null,\"co\":false,\"bh\":null,\"g\":2,\"val\":\"会議\",\"tr\":false}";
            EditDraftJournalBoundary.TryParseEntry(key, stored, out var e).Should().BeTrue();
            e.Value.Should().Be("会議");
            e.Sequence.Should().Be(7);
            e.Composing.Should().BeFalse();
            e.Descriptor.Generation.Should().Be(2);
            EditDraftJournalBoundary.TryParseEntry(key.Replace("|2", "|3"), stored, out _).Should().BeFalse("key and fields disagree");
            EditDraftJournalBoundary.TryParseEntry(key + "|c", stored, out _).Should().BeFalse("a composing key needs the composing flag");
            EditDraftJournalBoundary.TryParseEntry(key + "|c", stored.Replace("\"tr\":false}", "\"tr\":false,\"comp\":true}"), out var c).Should().BeTrue();
            c.Composing.Should().BeTrue();
            EditDraftJournalBoundary.TryParseEntry(key, stored.Replace("\"val\":\"会議\"", "\"val\":\"" + new string('x', 12001) + "\""), out _).Should().BeFalse("over the value limit");
            // "CareCrew_InputJournal" is the first host's real localStorage key, which the library must never touch.
            EditDraftJournalBoundary.TryParseEntry("CareCrew_InputJournal", stored, out _).Should().BeFalse("not a journal key");
        }

        [Test]
        public void T61_the_owner_token_is_stable_per_owner_differs_between_owners_and_is_absent_without_an_owner()
        {
            var a = Guid.NewGuid();
            EditDraftJournalBoundary.OwnerToken(a).Should().Be(EditDraftJournalBoundary.OwnerToken(a)).And.MatchRegex("^[0-9a-f]{32}$");
            EditDraftJournalBoundary.OwnerToken(a).Should().NotBe(EditDraftJournalBoundary.OwnerToken(Guid.NewGuid()));
            EditDraftJournalBoundary.OwnerToken(a).Should().NotContain(a.ToString("N"), "the Oid is not readable from the token");
            EditDraftJournalBoundary.OwnerToken(Guid.Empty).Should().BeNull();
        }
    }

    internal static class EditDraftBoundaryHelpers
    {
        public static EditDraftJournalDescriptor Parse(string json) =>
            EditDraftJournalBoundary.TryParseDescriptor(json, out var d) ? d : throw new AssertionException("did not parse: " + json);
    }

    [TestFixture]
    public class EditDraftJournalRuleTests
    {
        [Test]
        public void T71_the_baseline_fingerprint_is_stable_and_null_empty_and_text_all_differ()
        {
            EditDraftJournalRules.BaselineHash("abc").Should().Be(EditDraftJournalRules.BaselineHash("abc")).And.MatchRegex("^[0-9a-f]{16}$");
            new[] { EditDraftJournalRules.BaselineHash(null), EditDraftJournalRules.BaselineHash(""), EditDraftJournalRules.BaselineHash("abc"), EditDraftJournalRules.BaselineHash("abd") }
                .Should().OnlyHaveUniqueItems();
        }

        private static EditDraftJournalStoredItem I(string key, long at, int size = 10) => new(key, at, size);

        [Test]
        public void T72_eviction_keeps_60_and_the_serialized_budget_oldest_first_ties_by_key_in_any_input_order()
        {
            var items = Enumerable.Range(0, 59).Select(i => I("k" + i.ToString("D2"), 1000 + i)).ToList();
            EditDraftJournalRules.PlanEviction(items, "new", 10).Should().BeEmpty("59 + 1 = 60");
            items.Add(I("k59", 1059));
            EditDraftJournalRules.PlanEviction(items, "new", 10).Should().Equal("k00");
            EditDraftJournalRules.PlanEviction(items, "k10", 10).Should().BeEmpty("rewriting an existing key does not add an entry");
            var tied = new[] { I("b", 5), I("a", 5), I("c", 9) };
            EditDraftJournalRules.PlanEviction(tied, "new", 1, maxEntries: 3).Should().Equal("a");
            // Expectation form changed by owner ruling 2026-10-04 ("Yes, all six as proposed"): the reason was being read as a
            // second expected element.
            EditDraftJournalRules.PlanEviction(tied.Reverse(), "new", 1, maxEntries: 3).Should().Equal(new[] { "a" }, "input order does not matter");
            EditDraftJournalRules.PlanEviction(new[] { I("x", 1, 600), I("y", 2, 300) }, "new", 200, maxSerializedChars: 1000).Should().Equal(new[] { "x" }, "size budget alone");
            EditDraftJournalRules.PlanEviction(new[] { I("x", 1, 600), I("y", 2, 300) }, "new", 200, maxEntries: 1, maxSerializedChars: 1000).Should().Equal("x", "y");
            EditDraftJournalRules.PlanEviction(new[] { I("new", 1, 5000) }, "new", 5000, maxSerializedChars: 1000).Should().Equal(new[] { "new" }, "the written entry competes by At then key; alone over the budget it evicts itself (owner ruling 2026-10-04, M1b-E G4)");
            EditDraftJournalRules.IsExpired(0, 60 * 60000 - 1).Should().BeFalse();
            EditDraftJournalRules.IsExpired(0, 60 * 60000).Should().BeTrue("60 minutes old is expired");
        }

        [Test]
        public void T72b_the_size_budget_alone_and_with_the_count_the_written_entry_competes_by_at_then_key_and_the_60_minute_boundary()
        {
            // Added after T72's first run stopped early at its own faulty assertion (escalated to the owner, NOT changed): these
            // are the assertions T72 never reached, as a separate test.
            EditDraftJournalRules.PlanEviction(new[] { I("b", 5), I("a", 5), I("c", 9) }.Reverse(), "new", 1, maxEntries: 3)
                .Should().Equal(new[] { "a" }, "input order does not matter");
            EditDraftJournalRules.PlanEviction(new[] { I("x", 1, 600), I("y", 2, 300) }, "new", 200, maxSerializedChars: 1000)
                .Should().Equal(new[] { "x" }, "size budget alone");
            EditDraftJournalRules.PlanEviction(new[] { I("x", 1, 600), I("y", 2, 300) }, "new", 200, maxEntries: 1, maxSerializedChars: 1000)
                .Should().Equal(new[] { "x", "y" }, "count and size together");
            EditDraftJournalRules.PlanEviction(new[] { I("new", 1, 5000) }, "new", 5000, maxSerializedChars: 1000).Should().Equal(new[] { "new" }, "the written entry competes by At then key; alone over the budget it evicts itself (owner ruling 2026-10-04, M1b-E G4)");
            EditDraftJournalRules.IsExpired(0, 60 * 60000 - 1).Should().BeFalse();
            EditDraftJournalRules.IsExpired(0, 60 * 60000).Should().BeTrue("60 minutes old is expired");
        }

        [Test]
        public void T72_the_rules_and_limits_are_the_same_in_the_browser_module()
        {
            var js = Wave1.Source("Xaf.EditDraft.Blazor/wwwroot/edit-draft-journal.js");
            js.Should().Contain("export const PREFIX = '" + EditDraftJournalRules.KeyPrefix + "';")
                .And.Contain("export const COMPOSING_SUFFIX = '" + EditDraftJournalRules.ComposingSuffix + "';")
                .And.Contain("entries: " + EditDraftJournalRules.MaxEntries + ",")
                .And.Contain("valueChars: " + EditDraftJournalRules.MaxValueChars + ",")
                .And.Contain("keepMs: " + EditDraftJournalRules.RetentionMinutes + " * 60 * 1000,")
                .And.Contain("serializedChars: " + EditDraftJournalRules.MaxSerializedChars + ",")
                .And.Contain("pendingMs: " + EditDraftJournalRules.PostBlurWindowMs + ",")
                .And.Contain("export const KINDS = Object.freeze(['" + string.Join("', '", EditDraftJournalKinds.All) + "']);")
                .And.Contain("return (a.at - b.at) || (a.key < b.key ? -1 : (a.key > b.key ? 1 : 0));", "oldest first, ties by key (ordinal)")
                .And.Contain("now - at >= (limits || LIMITS).keepMs");
            js.Should().NotContain("innerHTML").And.NotContain("insertAdjacentHTML").And.NotContain("preventDefault").And.NotContain("stopPropagation")
                .And.NotContain("requestAnimationFrame").And.NotContain("CareCrew");
        }

        [TestCase("text", null, false)]
        [TestCase("memo", "anything", false)]
        [TestCase("combo", null, false)]
        [TestCase("time", "HH:mm", false)]
        [TestCase("time", "HH:mm:ss", false)]
        [TestCase("time", @"hh\:mm", true)]
        [TestCase("time", "h:mm", true)]
        [TestCase("time", "hh:mm tt", false)]
        [TestCase("time", "'h'HH:mm", false)]
        [TestCase("time", null, true)]
        [TestCase("time", " ", true)]
        [TestCase("masked", "000", true)]
        [TestCase("custom", "HH:mm", true)]
        public void T66_copy_only_for_unknown_or_12_hour_time_formats_and_for_masked_and_custom_kinds(string kind, string format, bool expected)
        {
            EditDraftJournalRules.IsCopyOnly(kind, format).Should().Be(expected);
        }

        private static EditDraftJournalDescriptor D(string kind, string format) =>
            EditDraftJournalDescriptor.Build("ns", NewProbe.Policy(), null, Guid.NewGuid(), "v", "Reason", kind, format, null, false, 1);

        [Test]
        public void T73_T76_complete_text_converts_with_the_effective_format_and_a_clear_and_midnight_stay_different()
        {
            EditDraftJournalConvert.ToCanonical(D("time", "HH:mm"), "08:30", typeof(TimeSpan), baselineRaw: "00:00:00", baselineKnown: true)
                .Should().Be(new EditDraftJournalConversion(EditDraftJournalOutcome.Converted, "08:30:00", null));
            EditDraftJournalConvert.ToCanonical(D("time", "HH:mm"), "00:00", typeof(TimeSpan), baselineRaw: "09:00:00", baselineKnown: true).Raw.Should().Be("00:00:00", "midnight is a time");
            EditDraftJournalConvert.ToCanonical(D("time", "HH:mm"), "", typeof(TimeSpan)).Outcome.Should().Be(EditDraftJournalOutcome.Clear, "empty text is a clear");
            // R-A5 / D8 (owner ruling 2026-10-04 "Expect CopyOnly"): with an unknown baseline, a format showing seconds or
            // finer stays copy-only because hidden fractions cannot be ruled out.
            EditDraftJournalConvert.ToCanonical(D("time", "HH:mm:ss"), "08:30:15", typeof(TimeSpan?)).Outcome.Should().Be(EditDraftJournalOutcome.CopyOnly, "seconds shown, baseline unknown: copy-only");
            EditDraftJournalConvert.ToCanonical(D("time", "HH:mm"), "08:30", typeof(DateTime), baselineRaw: null, baselineKnown: true)
                .Should().Be(new EditDraftJournalConversion(EditDraftJournalOutcome.TimeOfDay, "08:30:00", null), "a DateTime member gets a time of day; its date rule is M2's");
            EditDraftJournalConvert.ToCanonical(D("memo", null), "本日\n二行目", typeof(string)).Should().Be(new EditDraftJournalConversion(EditDraftJournalOutcome.Converted, "本日\n二行目", null));
            EditDraftJournalConvert.ToCanonical(D("text", null), "", typeof(string)).Outcome.Should().Be(EditDraftJournalOutcome.Clear);
            EditDraftJournalConvert.ToCanonical(D("time", null), "08:30", typeof(TimeSpan)).Outcome.Should().Be(EditDraftJournalOutcome.CopyOnly, "no host default replaces an unknown format");
        }

        [Test]
        public void T74_T75_incomplete_malformed_truncated_composing_and_ambiguous_text_has_no_canonical_value()
        {
            foreach (var text in new[] { "8:3_", "25:00", "abc", "08:30:00" })
                EditDraftJournalConvert.ToCanonical(D("time", "HH:mm"), text, typeof(TimeSpan), baselineRaw: "00:00:00", baselineKnown: true).Outcome.Should().Be(EditDraftJournalOutcome.Rejected, text);
            EditDraftJournalConvert.ToCanonical(D("memo", null), "x", typeof(string), truncated: true).Outcome.Should().Be(EditDraftJournalOutcome.CopyOnly);
            EditDraftJournalConvert.ToCanonical(D("memo", null), "x", typeof(string), composing: true).Outcome.Should().Be(EditDraftJournalOutcome.CopyOnly);
            EditDraftJournalConvert.ToCanonical(D("memo", null), "x", typeof(int)).Outcome.Should().Be(EditDraftJournalOutcome.Rejected, "not a string member");
            EditDraftJournalConvert.ToCanonical(D("time", @"hh\:mm"), "03:00", typeof(TimeSpan), baselineKnown: true).Outcome.Should().Be(EditDraftJournalOutcome.CopyOnly, "12-hour without AM/PM");
            EditDraftJournalConvert.ToCanonical(D("time", "HH:mm"), "09:31", typeof(TimeSpan), baselineRaw: "09:30:45", baselineKnown: true)
                .Should().Be(new EditDraftJournalConversion(EditDraftJournalOutcome.CopyOnly, null, "hidden seconds"));
            EditDraftJournalConvert.ToCanonical(D("time", "HH:mm"), "09:31", typeof(TimeSpan), baselineKnown: false).Outcome.Should().Be(EditDraftJournalOutcome.CopyOnly, "seconds unknown");
            EditDraftJournalConvert.ToCanonical(D("masked", "000"), "123", typeof(string)).Outcome.Should().Be(EditDraftJournalOutcome.CopyOnly, "masked text is copy-only in v1");
            EditDraftJournalConvert.ToCanonical(null, "x", typeof(string)).Outcome.Should().Be(EditDraftJournalOutcome.Rejected);
        }

        private static EditDraftJournalConversion C(string raw) => new(EditDraftJournalOutcome.Converted, raw, null);

        [Test]
        public void T77_the_reconcile_stub_classifies_baseline_server_and_journal_three_ways()
        {
            var bh = EditDraftJournalRules.BaselineHash("base");
            EditDraftJournalReconcile.Classify(bh, "base", C("base")).Should().Be(EditDraftJournalClass.AlreadyApplied, "all equal");
            EditDraftJournalReconcile.Classify(bh, "base", C("typed")).Should().Be(EditDraftJournalClass.Clean, "journal-only change");
            EditDraftJournalReconcile.Classify(bh, "server", C("base")).Should().Be(EditDraftJournalClass.Conflict, "server-only change: the journal would undo it");
            EditDraftJournalReconcile.Classify(bh, "same", C("same")).Should().Be(EditDraftJournalClass.AlreadyApplied, "matching concurrent changes");
            EditDraftJournalReconcile.Classify(bh, "server", C("typed")).Should().Be(EditDraftJournalClass.Conflict, "divergent changes");
            EditDraftJournalReconcile.Classify(null, "server", C("typed")).Should().Be(EditDraftJournalClass.BaselineUnknown);
            var clear = new EditDraftJournalConversion(EditDraftJournalOutcome.Clear, null, null);
            EditDraftJournalReconcile.Classify(bh, null, clear).Should().Be(EditDraftJournalClass.AlreadyApplied);
            EditDraftJournalReconcile.Classify(bh, "", clear).Should().Be(EditDraftJournalClass.AlreadyApplied);
            EditDraftJournalReconcile.Classify(bh, "base", clear).Should().Be(EditDraftJournalClass.Clean);
            EditDraftJournalReconcile.Classify(EditDraftJournalRules.BaselineHash(null), null, C("typed")).Should().Be(EditDraftJournalClass.Clean, "null baseline");
            foreach (var o in new[] { EditDraftJournalOutcome.CopyOnly, EditDraftJournalOutcome.Rejected, EditDraftJournalOutcome.TimeOfDay })
                EditDraftJournalReconcile.Classify(bh, "base", new EditDraftJournalConversion(o, "x", null)).Should().Be(EditDraftJournalClass.NotConvertible, o.ToString());
            EditDraftJournalReconcile.Classify(bh, "base", null).Should().Be(EditDraftJournalClass.NotConvertible);
        }

        [Test]
        public void T79_T80_the_row_label_is_in_both_sets_and_both_sets_stay_complete()
        {
            EditDraftTextSet.Japanese.JournalRowLabel.Should().Be("入力中（未確定）", "owner decision 10 default");
            EditDraftTextSet.English.JournalRowLabel.Should().Be("Being typed (not confirmed)");
            typeof(EditDraftTextSet).GetProperties().Where(p => p.PropertyType == typeof(string))
                .Should().OnlyContain(p => p.GetValue(EditDraftTextSet.English) != null && p.GetValue(EditDraftTextSet.Japanese) != null);
            var current = EditDraftTexts.Current;
            try
            {
                EditDraftTexts.Use(EditDraftLanguage.Japanese);
                EditDraftTexts.Current.JournalRowLabel.Should().Be("入力中（未確定）");
                EditDraftTexts.Use(new EditDraftTextSet());
                EditDraftTexts.Of(t => t.JournalRowLabel).Should().Be("Being typed (not confirmed)", "a host set that leaves it out falls back to English");
            }
            finally { EditDraftTexts.Use(current); }
        }
    }
}
