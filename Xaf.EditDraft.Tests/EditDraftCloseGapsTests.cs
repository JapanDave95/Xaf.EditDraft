using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DevExpress.ExpressApp.Blazor.Components;
using DevExpress.ExpressApp.Utils;
using DevExpress.Xpo.Metadata;
using FluentAssertions;
using NUnit.Framework;
using Xaf.EditDraft.Blazor;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests
{
    // Close the library gaps (run 2026-10-04-editdraft-close-gaps-08c338; requirement: docs/xaf-editdraft-sample-consumer-2026-10-03.md
    // §5 G1-G15 and the owner's "Close the gaps"). Expectation ids Tn are from the Codex requirement-only list of this run
    // (tests a1), written before any code was shown to it. The tests in this file use only names that exist before and
    // after the change (reflection by name, texts, files), so they were run red on the unchanged main first.

    [TestFixture]
    public class EditDraftCloseGapsTests
    {
        private static readonly Assembly Core = typeof(EditDraftCoreModule).Assembly;
        private static readonly Assembly Blazor = typeof(EditDraftBlazorModule).Assembly;

        private static string RepoFile(string rel)
        {
            var path = Path.Combine(Wave1.Root(), rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) Assert.Fail($"{rel} does not exist in the repository");
            return File.ReadAllText(path);
        }

        private static IEnumerable<(string Key, string Text)> Texts(EditDraftTextSet set) =>
            typeof(EditDraftTextSet).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(string))
                .Select(p => (p.Name, (string)p.GetValue(set)));

        // ---- G9 / G4 texts ------------------------------------------------------------------------------------------

        [Test]
        public void G9_T34_T35_the_English_set_has_no_host_term_and_no_Japanese_character()
        {
            var cjk = new Regex("[　-ヿ㐀-鿿＀-￯]");
            foreach (var (key, text) in Texts(EditDraftTextSet.English))
            {
                text.Should().NotBeNullOrEmpty($"English.{key} is set");
                cjk.IsMatch(text).Should().BeFalse($"English.{key} has no Japanese character: \"{text}\"");
                text.Should().NotContainEquivalentOf("personal login", $"English.{key}");
                text.Should().NotContainEquivalentOf("office", $"English.{key}");
            }
            // The three strings G9 names are reworded (owner-ruled expectation change: the gap list).
            EditDraftTextSet.English.PersonalLoginOnly.Should().NotBe("Drafts are available to a personal login only.");
            EditDraftTextSet.English.RecordNotVisible.Should().NotBe("This draft cannot be restored for this login (事業所 permission).");
            EditDraftTextSet.English.RecreateSubSectionNotVisible.Should().NotBe("The office (事業所) of this draft's record cannot be shown.");
        }

        [Test]
        public void G9_T36_the_Japanese_wording_is_unchanged()
        {
            var ja = EditDraftTextSet.Japanese;
            ja.PersonalLoginOnly.Should().Be("この画面の入力控は、職員個人のログインで使えます。");
            ja.RecordNotVisible.Should().Be("この入力控はこのログインでは戻せません（事業所の権限）。");
            ja.RecreateSubSectionNotVisible.Should().Be("この入力控の記録の事業所は表示できません。");
            ja.ListLead.Should().Be("保存されていない入力です。選んで「開く」を押すと記録に戻せます（戻した内容はまだ保存されません）。保存期限を過ぎると削除されます。");
        }

        [Test]
        public void G4_T17_the_English_list_lead_does_not_promise_deletion_at_expiry()
        {
            var lead = EditDraftTextSet.English.ListLead;
            lead.Should().NotContainEquivalentOf("deleted", "rows are deleted only by a retention sweep the host turns on");
            lead.Should().ContainEquivalentOf("expire");
        }

        // ---- O-11 (M3 §12): the row icon fails closed ------------------------------------------------------------------

        private const string RowKey = "EditDraftRowHasDraft";

        private static BoolList Reasons(params (string Key, bool Value)[] reasons)
        {
            var list = new BoolList();
            foreach (var (key, value) in reasons) list.SetItemValue(key, value);
            return list;
        }

        private static CustomizeInlineActionButtonEventArgs Row(bool enabled) =>
            new() { ActionId = EditDraftListBadgeControllerBlazor.OpenActionId, Visible = true, Enabled = enabled, DataItem = new object() };

        [Test]
        public void O11_T66_T67_T68_T70_the_row_rule_never_enables_an_icon_XAF_disabled_and_keeps_the_visibility_rule()
        {
            var onlyRowReason = Reasons(("Admitted", true), ("HasDrafts", true), (RowKey, false));
            foreach (var state in new[] { onlyRowReason, Reasons(("Admitted", true), ("Security", false), (RowKey, false)), Reasons(), null })
            {
                var badged = Row(enabled: false);   // XAF disabled it: selected row without a draft, TargetObjectsCriteria, BoundItemCreating ...
                EditDraftRowOpenRule.Apply(badged, badged: true, state, RowKey);
                badged.Enabled.Should().BeFalse("O-11 fail closed: the rule never turns an icon on");
                badged.Visible.Should().BeTrue("a badged row keeps its icon");
            }
            var usable = Row(enabled: true);         // T69: an eligible badged row with nothing disabling it
            EditDraftRowOpenRule.Apply(usable, badged: true, Reasons(("Admitted", true), (RowKey, true)), RowKey);
            (usable.Visible, usable.Enabled).Should().Be((true, true));
            var unbadged = Row(enabled: true);
            EditDraftRowOpenRule.Apply(unbadged, badged: false, Reasons(("Admitted", true), (RowKey, true)), RowKey);
            unbadged.Visible.Should().BeFalse("an unbadged row has no icon");
        }

        // ---- G13 assembly identity, version, DX floor ------------------------------------------------------------------

        [Test]
        public void G13_T77_T78_Core_grants_its_internals_to_the_Blazor_part_and_the_library_tests_only()
        {
            Core.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName)
                .Should().BeEquivalentTo(new[] { "Xaf.EditDraft.Blazor", "Xaf.EditDraft.Tests" });
            RepoFile("Xaf.EditDraft.Core/Xaf.EditDraft.Core.csproj").Should().NotContain("<InternalsVisibleTo Include=\"NursingHome_Chart");
        }

        [Test]
        public void G13_T47_T72_T74_the_version_is_set_once_and_the_release_notes_and_assemblies_follow_it()
        {
            // Version-agnostic since 0.3.0-preview.2 (owner ruling 2026-10-05): the pin of the literal 0.2.0-preview.1
            // broke the 0.3.0-preview.1 release run. What it protects stays: one <Version>, release notes that start
            // with that version, and assemblies built from it.
            var props = RepoFile("Directory.Build.props");
            var match = Regex.Match(props, @"<Version>(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.]+)?</Version>");
            match.Success.Should().BeTrue("Directory.Build.props sets one semantic <Version>");
            Regex.Matches(props, @"<Version>\d").Count.Should().Be(1, "the version is set in one place");
            var version = match.Value.Substring("<Version>".Length, match.Value.Length - "<Version>".Length * 2 - 1);
            var notes = Regex.Match(props, "<PackageReleaseNotes>(.*?)</PackageReleaseNotes>", RegexOptions.Singleline).Groups[1].Value;
            notes.Should().StartWith(version + " (", "the release notes begin with the version being released");
            var numeric = new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value), 0);
            foreach (var assembly in new[] { Core, Blazor })
            {
                assembly.GetName().Version.Should().Be(numeric, assembly.GetName().Name);
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Should().StartWith(version);
            }
        }

        [Test]
        public void T73_DevExpress_26_1_4_stays_the_tested_floor()
        {
            RepoFile("Directory.Packages.props").Should().Contain("<DevExpressVersion>26.1.4</DevExpressVersion>");
            var readme = RepoFile("README.md");
            readme.Should().Contain("26.1.4");
            readme.Should().MatchRegex("(?i)tested");
        }

        // ---- G14 consumer guide, README -----------------------------------------------------------------------------

        [Test]
        public void G14_T49_T50_T63_the_consumer_guide_exists_and_the_README_links_it_and_is_current()
        {
            var readme = RepoFile("README.md");
            readme.Should().Contain("docs/consumer-guide.md");
            readme.Should().Contain("SQL Server only");
            readme.Should().NotContain("there is no retention sweep in the library");
            readme.Should().NotContain("no NuGet package yet");

            var guide = RepoFile("docs/consumer-guide.md");
            foreach (var fact in new[]
                     {
                         "AddEditDraftStore", "AddEditDraftRegistry", "AddEditDraftBlazor", "EditDraftCoreModule", "EditDraftBlazorModule",
                         "AddNonPersistent", "_content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css", "EditDraftCapture:Enabled",
                         "Types:<PolicyId>:Enabled", "ListViews:Enabled", "NewRecords:Enabled", "SQL Server only", "Schema", "dbo"
                     })
                guide.Should().Contain(fact);
        }

        [Test]
        public void G3_G5_T9_T20_T61_the_guide_states_the_security_limits_the_clock_and_the_audit_obligation()
        {
            var guide = RepoFile("docs/consumer-guide.md");
            guide.Should().Contain("IsAdministrative", "G3: administrators cannot be denied");
            guide.Should().MatchRegex("(?i)object or member", "G3: object/member ALLOW grants override a type deny");
            guide.Should().MatchRegex("(?i)created after", "G3: roles created after the last database update");
            guide.Should().Contain("ExpiresOn");
            guide.Should().MatchRegex("(?i)application server", "G5: expiry is in the application server's local time");
            guide.Should().Contain("GETDATE()");
            guide.Should().MatchRegex("(?i)audit trail");
        }

        // ---- G8 / G15 names --------------------------------------------------------------------------------------------

        private static readonly string[] HostNames = { "LoginIsStaffMember", "SubSectionOid", "SubSectionOf", "IsSubSectionVisible", "F2StaffMember" };

        [Test]
        public void G8_T32_the_listed_host_names_are_gone_from_the_public_surface()
        {
            foreach (var type in Core.GetExportedTypes().Concat(Blazor.GetExportedTypes()))
            {
                var names = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.Name);
                if (type.IsEnum) names = names.Concat(Enum.GetNames(type));
                foreach (var name in names)
                    HostNames.Should().NotContain(name, $"{type.FullName}.{name} is host vocabulary (G8)");
            }
            typeof(EditDraftStoreBase).GetProperty("OwnerFlag").Should().NotBeNull();
            typeof(EditDraftStoreBase).GetProperty("ScopeOid").Should().NotBeNull();
            typeof(EditDraftTypePolicy).GetProperty("ScopeOf").Should().NotBeNull();
            typeof(IEditDraftRecordAccess).GetMethod("IsScopeVisible").Should().NotBeNull();
            Enum.GetNames(typeof(EditDraftOwnerKind)).Should().BeEquivalentTo(new[] { "HostDefined", "Login" });
        }

        [Test]
        public void G8_the_renamed_store_members_keep_their_database_column_names()
        {
            // Decision G8 (owner decision O-1 in docs/close-gaps-2026-10-04.md): the CLR names change, the columns do not,
            // so an existing store table needs no migration.
            var info = new ReflectionDictionary().GetClassInfo(typeof(EditDraftTestStore));
            info.FindMember("OwnerFlag")?.MappingField.Should().Be("LoginIsStaffMember");
            info.FindMember("ScopeOid")?.MappingField.Should().Be("SubSectionOid");
            info.FindMember("OwnerFlag").Should().NotBeNull();
            info.FindMember("ScopeOid").Should().NotBeNull();
        }

        [Test]
        public void G15_T64_the_capture_controller_name_is_platform_neutral()
        {
            Core.GetType("Xaf.EditDraft.Core.EditDraftCaptureController").Should().NotBeNull();
            Core.GetExportedTypes().Select(t => t.Name).Should().NotContain(n => n.EndsWith("Blazor"), "Core is platform-agnostic");
        }
    }
}
