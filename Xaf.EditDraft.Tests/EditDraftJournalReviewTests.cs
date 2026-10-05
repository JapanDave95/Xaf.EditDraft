using System;
using System.Collections.Generic;
using System.Text.Json;
using DevExpress.ExpressApp.Blazor.Components.Models;
using FluentAssertions;
using NUnit.Framework;
using Xaf.EditDraft.Blazor;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests
{
    // Client-side input journal, milestone M1: checks for Codex diffreview a1 findings C11 (effective culture and format) and
    // C12 (coverage names unsupported editors), run 2026-10-04-edit-draft-journal-m1-96623a. Written after the review from
    // its decisive checks; the fixes they cover are NOT cross-reviewed (two-pass limit).
    [TestFixture]
    public class EditDraftJournalReviewTests
    {
        private static EditDraftJournalDescriptor D(string kind, string format, string culture) =>
            EditDraftJournalDescriptor.Build("ns", NewProbe.Policy(), null, Guid.NewGuid(), "v", "Reason", kind, format, null, false, 1, null, culture);

        [Test]
        public void C11_a_time_editor_s_mask_wins_over_its_format()
        {
            EditDraftJournalAttributeControllerBlazor.KindOf(new DxTimeEditModel<TimeSpan> { Format = "HH:mm:ss", Mask = "HH:mm" }, "timespan")
                .Should().Be(("time", "HH:mm"));
            EditDraftJournalAttributeControllerBlazor.KindOf(new DxTimeEditModel<TimeSpan> { Format = "HH:mm" }, "timespan").Should().Be(("time", "HH:mm"));
        }

        [Test]
        public void C11_the_descriptor_carries_the_culture_and_conversion_parses_with_it_never_with_a_host_default()
        {
            var ja = D("time", "tt h:mm", "ja-JP");
            using (var doc = JsonDocument.Parse(ja.ToJson())) doc.RootElement.GetProperty("cu").GetString().Should().Be("ja-JP");
            var pm = System.Globalization.CultureInfo.GetCultureInfo("ja-JP").DateTimeFormat.PMDesignator;
            EditDraftJournalConvert.ToCanonical(ja, pm + " 3:30", typeof(TimeSpan), baselineRaw: "00:00:00", baselineKnown: true)
                .Should().Be(new EditDraftJournalConversion(EditDraftJournalOutcome.Converted, "15:30:00", null));
            EditDraftJournalConvert.ToCanonical(D("time", "tt h:mm", null), pm + " 3:30", typeof(TimeSpan), baselineRaw: "00:00:00", baselineKnown: true)
                .Outcome.Should().Be(EditDraftJournalOutcome.Rejected, "the invariant culture does not read the Japanese designator");
            EditDraftJournalBoundary.TryParseDescriptor(ja.ToJson(), out var back).Should().BeTrue();
            back.Culture.Should().Be("ja-JP");
            EditDraftJournalRules.CultureOf("xx-not-a-culture").Should().Be(System.Globalization.CultureInfo.InvariantCulture);
        }

        [TestCase("t", "en-US", false)]
        [TestCase("t", null, false)]
        [TestCase("h:mm", "en-US", true)]
        [TestCase("T", "ja-JP", false)]
        public void C11_a_standard_format_is_expanded_with_the_culture_before_the_12_hour_check(string format, string culture, bool copyOnly)
        {
            EditDraftJournalRules.IsCopyOnly("time", format, culture).Should().Be(copyOnly);
        }

        [Test]
        public void C11_hidden_fractions_and_hidden_seconds_stay_copy_only_and_the_shown_precision_is_measured()
        {
            EditDraftJournalRules.PrecisionTicks("HH:mm").Should().Be(TimeSpan.TicksPerMinute);
            EditDraftJournalRules.PrecisionTicks("HH:mm:ss").Should().Be(TimeSpan.TicksPerSecond);
            EditDraftJournalRules.PrecisionTicks("HH:mm:ss.fff").Should().Be(TimeSpan.TicksPerMillisecond);
            EditDraftJournalRules.PrecisionTicks("HH 'mins'").Should().Be(TimeSpan.TicksPerHour, "quoted text is not a field");
            EditDraftJournalRules.PrecisionTicks(EditDraftJournalRules.ExpandFormat("T", null)).Should().Be(TimeSpan.TicksPerSecond);
            EditDraftJournalConvert.ToCanonical(D("time", "HH:mm:ss", null), "09:30:46", typeof(TimeSpan), baselineRaw: "09:30:45.5000000", baselineKnown: true)
                .Should().Be(new EditDraftJournalConversion(EditDraftJournalOutcome.CopyOnly, null, "hidden fractions"));
            EditDraftJournalConvert.ToCanonical(D("time", "HH:mm:ss", null), "09:30:46", typeof(TimeSpan), baselineRaw: "09:30:45", baselineKnown: true)
                .Raw.Should().Be("09:30:46");
        }

        [Test]
        public void C12_the_coverage_line_names_journaled_members_whose_component_the_library_does_not_know()
        {
            var json = JsonSerializer.Serialize(new { invalid = 0, groups = new object[] { new { w = "V", ctx = "c", members = new object[] { new { m = "Description", field = true } } } } });
            var line = EditDraftJournalAttributeControllerBlazor.CoverageLine("V", "c", new Dictionary<string, string> { ["Description"] = "memo" }, json,
                new Dictionary<string, string> { ["RoundsTime"] = "TimeSpanMaskedModel", ["ManagerNote"] = "DxRichEditModel" });
            line.Should().Contain("admitted=1 found=1").And.EndWith("unsupported=[ManagerNote(DxRichEditModel),RoundsTime(TimeSpanMaskedModel)]");
            var src = Wave1.Source("Xaf.EditDraft.Blazor/EditDraftJournalAttributeControllerBlazor.cs");
            src.Should().Contain("_unsupported[spec.Path] = editor.ComponentModel?.GetType().Name ?? \"none\";")
                .And.Contain("if (_module != null) _ = ReportCoverageAsync(_module, lifetime.Token);", "coverage is reported again after a record change or a save")
                .And.Contain("if (lifetime == null || lifetime.IsCancellationRequested || View == null) return;", "a posted callback does nothing after deactivation");
        }
    }
}
