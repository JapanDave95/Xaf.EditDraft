using System;
using FluentAssertions;
using NUnit.Framework;
using Xaf.EditDraft.Blazor;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests
{
    // Client-side input journal, milestone M1b cluster A (collaborator run 2026-10-04-edit-draft-journal-m1b-a-89eeeb):
    // R-A5 / Codex M1 diffreview a2 D8 (conversion with an unknown baseline) and D9 (coverage of unsupported-only views and
    // of controls created later). Expectations from Codex's requirement-only list of this run (X22-X25). The browser
    // module's retry and clear ordering (R-A1..R-A4) is tested in Xaf.EditDraft.Tests/js/test/m1b-a.test.js.
    [TestFixture]
    public class EditDraftJournalM1bTests
    {
        private static EditDraftJournalDescriptor D(string kind, string format, string culture = null) =>
            EditDraftJournalDescriptor.Build("ns", NewProbe.Policy(), null, Guid.NewGuid(), "v", "Reason", kind, format, null, false, 1, null, culture);

        // ---------------------------------------------------------------------------------------------------- D8 (X22, X23)

        [TestCase("HH:mm:ss", "09:30:46")]
        [TestCase("HH:mm:ss", "09:30:00")]
        [TestCase("HH:mm:ss.f", "09:30:46.5")]
        [TestCase("HH:mm:ss.f", "09:30:46.0")]
        [TestCase("HH:mm:ss.ff", "09:30:46.25")]
        [TestCase("HH:mm:ss.fff", "09:30:46.125")]
        [TestCase("HH:mm:ss.fff", "09:30:46.000")]
        [TestCase("HH:mm:ss.fffffff", "09:30:46.1234567")]
        [TestCase("HH:mm:ss.fffffff", "09:30:46.0000000")]
        public void D8_X22_with_an_unknown_baseline_a_format_showing_seconds_or_finer_stays_copy_only(string format, string text)
        {
            EditDraftJournalConvert.ToCanonical(D("time", format), text, typeof(TimeSpan), baselineKnown: false)
                .Outcome.Should().Be(EditDraftJournalOutcome.CopyOnly, "unrepresented precision cannot be excluded without the baseline");
            EditDraftJournalConvert.ToCanonical(D("time", format), text, typeof(DateTime), baselineKnown: false)
                .Outcome.Should().Be(EditDraftJournalOutcome.CopyOnly, "the same for a DateTime member's time of day");
        }

        [Test]
        public void D8_X22_the_minute_side_control_and_the_empty_clear_keep_their_outcomes()
        {
            EditDraftJournalConvert.ToCanonical(D("time", "HH:mm"), "09:31", typeof(TimeSpan), baselineKnown: false)
                .Should().Be(new EditDraftJournalConversion(EditDraftJournalOutcome.CopyOnly, null, "hidden seconds, baseline unknown"));
            EditDraftJournalConvert.ToCanonical(D("time", "HH:mm:ss"), "", typeof(TimeSpan), baselineKnown: false)
                .Outcome.Should().Be(EditDraftJournalOutcome.Clear, "empty text is a clear, with or without a baseline");
        }

        [TestCase("T", "ja-JP", "9:30:46")]
        [TestCase("T", "en-US", "9:30:46 AM")]
        [TestCase("T", null, "09:30:46")]
        public void D8_X23_an_expanded_standard_format_with_seconds_stays_copy_only_with_an_unknown_baseline(string format, string culture, string text)
        {
            EditDraftJournalRules.PrecisionTicks(EditDraftJournalRules.ExpandFormat(format, culture)).Should().Be(TimeSpan.TicksPerSecond);
            EditDraftJournalConvert.ToCanonical(D("time", format, culture), text, typeof(TimeSpan), baselineKnown: false)
                .Outcome.Should().Be(EditDraftJournalOutcome.CopyOnly);
        }

        [Test]
        public void D8_X23_known_baselines_and_the_other_copy_only_rules_are_unchanged()
        {
            EditDraftJournalConvert.ToCanonical(D("time", "HH:mm:ss"), "09:30:46", typeof(TimeSpan), baselineRaw: "09:30:45", baselineKnown: true)
                .Should().Be(new EditDraftJournalConversion(EditDraftJournalOutcome.Converted, "09:30:46", null));
            EditDraftJournalConvert.ToCanonical(D("time", "HH:mm:ss"), "09:30:46", typeof(TimeSpan), baselineRaw: "09:30:45.5000000", baselineKnown: true)
                .Should().Be(new EditDraftJournalConversion(EditDraftJournalOutcome.CopyOnly, null, "hidden fractions"));
            EditDraftJournalConvert.ToCanonical(D("time", null), "09:30:46", typeof(TimeSpan), baselineKnown: true)
                .Outcome.Should().Be(EditDraftJournalOutcome.CopyOnly, "unknown format");
            EditDraftJournalConvert.ToCanonical(D("time", @"hh\:mm"), "03:00", typeof(TimeSpan), baselineKnown: true)
                .Outcome.Should().Be(EditDraftJournalOutcome.CopyOnly, "12-hour without a designator");
            EditDraftJournalConvert.ToCanonical(D("masked", "00:00:00"), "09:30:46", typeof(TimeSpan), baselineKnown: true)
                .Outcome.Should().Be(EditDraftJournalOutcome.CopyOnly, "masked kinds");
        }

        // ---------------------------------------------------------------------------------------------------- D9 (X24, X25)
        // What runs here is the controller's decision state and its source; the XAF lifecycle itself (CustomizeViewItemControl
        // calls, the posted callbacks, the JS interop) is not started in this project (no XAF application in tests).

        [Test]
        public void D9_X24_the_module_starts_for_the_first_admitted_editor_attributed_or_unsupported_once()
        {
            EditDraftJournalCoverageRules.StartModule(false, 0, 1).Should().BeTrue("a view whose only eligible editors are unsupported starts the module");
            EditDraftJournalCoverageRules.StartModule(false, 1, 0).Should().BeTrue();
            EditDraftJournalCoverageRules.StartModule(false, 0, 0).Should().BeFalse("no admitted journaled editor: nothing to report");
            EditDraftJournalCoverageRules.StartModule(true, 0, 1).Should().BeFalse("once per activation");
        }

        [Test]
        public void D9_X24_an_unsupported_only_view_gets_a_coverage_line_naming_each_omission()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new { invalid = 0, groups = new object[0] });
            EditDraftJournalAttributeControllerBlazor.CoverageLine("V", "0123456789abcdef", new System.Collections.Generic.Dictionary<string, string>(), json,
                    new System.Collections.Generic.Dictionary<string, string> { ["RoundsTime"] = "TimeSpanMaskedModel", ["ManagerNote"] = "DxRichEditModel" })
                .Should().Be("view=V context=01234567 admitted=0 found=0 notRendered=[] noField=[] otherGroups=0 invalid=0 unsupported=[ManagerNote(DxRichEditModel),RoundsTime(TimeSpanMaskedModel)]");
        }

        [Test]
        public void D9_X25_a_control_created_after_the_first_line_is_reported_one_pending_report_at_a_time()
        {
            EditDraftJournalCoverageRules.ReportForLaterControl(moduleStarted: true, reportPending: false).Should().BeTrue();
            EditDraftJournalCoverageRules.ReportForLaterControl(moduleStarted: true, reportPending: true).Should().BeFalse("the pending report reads the DOM after it");
            EditDraftJournalCoverageRules.ReportForLaterControl(moduleStarted: false, reportPending: false).Should().BeFalse("before the start the first report covers it");
        }

        [Test]
        public void D9_X24_X25_the_controller_uses_the_rules_and_names_the_context_before_the_kind_check()
        {
            var src = Wave1.Source("Xaf.EditDraft.Blazor/EditDraftJournalAttributeControllerBlazor.cs");
            src.Should().Contain("if (EditDraftJournalCoverageRules.StartModule(_moduleRequested, _attributed.Count, _unsupported.Count))", "the start decision counts unsupported editors")
                .And.Contain("EditDraftJournalCoverageRules.ReportForLaterControl(_module != null, _reportPending)", "Customize reports a later control")
                .And.Contain("finally { _reportPending = false; }")
                .And.NotContain("if (!_moduleRequested)", "the start no longer waits for an attribute");
            var context = src.IndexOf("_lastContext = context.ToString(\"N\");", StringComparison.Ordinal);
            context.Should().BeGreaterThan(0);
            context.Should().BeLessThan(src.IndexOf("var (kind, format) = KindOf(editor.ComponentModel, spec.Kind);", StringComparison.Ordinal), "an unsupported-only view has a context");
            var apply = src.Substring(src.IndexOf("private void Apply(", StringComparison.Ordinal));
            apply = apply.Substring(0, apply.IndexOf("private EditDraftJournalDescriptor Describe(", StringComparison.Ordinal));
            apply.Replace("\r\n", "\n").Should().NotContain("RemoveAttribute(AttributeName);\n            return;", "no early return before the start decision");
        }
    }
}
