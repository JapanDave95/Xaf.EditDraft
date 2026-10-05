using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Xaf.EditDraft.Blazor;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests
{
    // Xaf.EditDraft library, milestone M2 (run 2026-10-02-editdraft-m2-6e2ced; brief items 1-7). Expectations from the
    // Codex requirement-only list of this run (tests a1, E1-E34), written before any code was shown to it; labels En refer
    // to them. The library is exercised as its OWN assemblies (ProjectReference), never as linked source.
    // Runtime behaviour in a host (controller activation, popup rendering, the static asset being served) is the M4 browser pass.
    // Library milestone M3 (run 2026-10-02-editdraft-m3-1b4d82): E6_E7_D9 follows owner ruling O-7 (the six text lines name
    // the library label editor's alias). GUARD: E1_E2 and E2 list the application assembly prefixes and tokens the library
    // must never reference.

    [TestFixture]
    public class EditDraftLibraryBlazorTests
    {
        private static readonly Assembly Blazor = typeof(EditDraftBlazorModule).Assembly;
        private static readonly Assembly Core = typeof(EditDraftCoreModule).Assembly;
        private static string Root => Wave1.Root();

        private static readonly string[] Controllers =
        {
            nameof(EditDraftListCaptureControllerBlazor), nameof(EditDraftRestoreControllerBlazor), nameof(EditDraftRestorePopupControllerBlazor),
            nameof(EditDraftReadOnlyViewControllerBlazor), nameof(EditDraftListControllerBlazor), nameof(EditDraftListItemControllerBlazor),
            nameof(EditDraftListViewControllerBlazor), nameof(EditDraftListBadgeControllerBlazor), nameof(EditDraftRestoreItemListControllerBlazor),
            // Expectation changed by owner ruling 2026-10-04 ("Yes, all six as proposed"; client journal M1, run
            // 2026-10-04-edit-draft-journal-m1-96623a): the journal attribute controller is the tenth controller.
            nameof(EditDraftJournalAttributeControllerBlazor)
        };

        private static readonly Type[] PopupClasses =
        {
            typeof(EditDraftRestorePlan), typeof(EditDraftRestoreItem), typeof(EditDraftReadOnlyView), typeof(EditDraftList), typeof(EditDraftListItem)
        };

        private static IEnumerable<string> LibrarySources(string project) =>
            Directory.GetFiles(Path.Combine(Root, project), "*.*", SearchOption.AllDirectories)
                .Where(p => (p.EndsWith(".cs") || p.EndsWith(".razor") || p.EndsWith(".csproj")) && !Built(p));

        private static bool Built(string p) =>
            p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) || p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar);

        /// <summary>The code of a source file: line comments, XML doc comments and XML/MSBuild comments removed.</summary>
        private static string Code(string path)
        {
            var text = Regex.Replace(File.ReadAllText(path), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            return string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
        }

        [Test]
        public void E1_E2_the_Blazor_assembly_references_Core_and_no_application_Llamachant_or_Progress_assembly()
        {
            Blazor.GetName().Name.Should().Be("Xaf.EditDraft.Blazor");
            var referenced = Blazor.GetReferencedAssemblies().Select(a => a.Name).ToList();
            referenced.Should().Contain("Xaf.EditDraft.Core");
            foreach (var name in referenced)
            {
                name.Should().NotStartWith("NursingHome_Chart", "the library never references the application Module");
                name.Should().NotStartWith("CareCrew", "the library never references the hosts");
                name.Should().NotStartWith("Progress", "the library never references Progress.Common");
                name.Should().NotStartWith("Llamachant", "D9: no Llamachant dependency");
                name.Should().NotStartWith("CareTree", "the library never references the CareTree client");
            }
            Core.GetReferencedAssemblies().Select(a => a.Name).Should().NotContain(n => n.StartsWith("Xaf.EditDraft.Blazor"), "Core does not depend on the Blazor part");
        }

        [Test]
        public void E2_the_Blazor_project_references_only_Core_and_DevExpress_and_its_code_names_no_application_symbol_logger_or_configuration_key()
        {
            var csproj = File.ReadAllText(Path.Combine(Root, "Xaf.EditDraft.Blazor", "Xaf.EditDraft.Blazor.csproj"));
            csproj.Should().StartWith("<Project Sdk=\"Microsoft.NET.Sdk.Razor\">", "a Razor class library serves wwwroot as _content/Xaf.EditDraft.Blazor");
            Regex.Matches(csproj, "<ProjectReference Include=\"([^\"]+)\"").Select(m => m.Groups[1].Value)
                .Should().Equal(new[] { @"..\Xaf.EditDraft.Core\Xaf.EditDraft.Core.csproj" });
            Regex.Matches(csproj, "<PackageReference Include=\"([^\"]+)\"").Select(m => m.Groups[1].Value)
                .Should().BeEquivalentTo(new[] { "DevExpress.ExpressApp.Blazor", "DevExpress.ExpressApp.ConditionalAppearance" }, "nothing new on the feed");
            csproj.Should().NotContain("<Compile Include").And.NotContain("Link=").And.NotContain("Version=", "versions come from Directory.Packages.props");
            foreach (var project in new[] { "Xaf.EditDraft.Blazor", "Xaf.EditDraft.Core" })
                foreach (var file in LibrarySources(project))
                {
                    // Owner ruling 2026-10-02 (O-6): the InternalsVisibleTo friend grant to the test project is allowed (O-3).
                    var code = string.Join("\n", Code(file).Split('\n').Where(l => !l.Contains("<InternalsVisibleTo ")));
                    foreach (var token in new[] { "CareCrew", "NursingHome_Chart", "Llamachant", "GlobalLogger", "Progress.", "appsettings" })
                        code.Should().NotContain(token, Path.GetFileName(file) + " (brief item 6)");
                }
            foreach (var file in LibrarySources("Xaf.EditDraft.Blazor"))
                Code(file).Should().NotContain("\"EditDraftCapture", Path.GetFileName(file) + ": the Blazor part reads switches only through EditDraftSwitch (a key literal, not the EditDraftCapture… identifiers)");
        }

        [Test]
        public void E4_the_Blazor_module_requires_Core_exports_only_the_five_popup_classes_and_collects_each_controller_once()
        {
            var module = new EditDraftBlazorModule();
            module.RequiredModuleTypes.Should().Contain(typeof(EditDraftCoreModule));
            var exported = ((IEnumerable<Type>)typeof(EditDraftBlazorModule)
                .GetMethod("GetDeclaredExportedTypes", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(module, null)).ToList();
            exported.Should().BeEquivalentTo(PopupClasses);
            foreach (var t in exported)
            {
                t.GetCustomAttributes(typeof(DomainComponentAttribute), false).Should().NotBeEmpty(t.Name + " is a non-persistent XAF class");
                typeof(DevExpress.Xpo.PersistentBase).IsAssignableFrom(t).Should().BeFalse(t.Name + " maps to no table");
            }
            var controllers = ((IEnumerable<Type>)typeof(ModuleBase)
                .GetMethod("GetDeclaredControllerTypes", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(module, null)).ToList();
            foreach (var name in Controllers)
                controllers.Count(t => t.Name == name).Should().Be(1, name + " is collected once");
            controllers.Should().OnlyContain(t => t.Assembly == Blazor);
            controllers.Should().HaveCount(Controllers.Length, "no other controller lives in the Blazor part");
        }

        [Test]
        public void E12_the_per_circuit_services_are_scoped_and_two_circuits_never_share_them()
        {
            var services = new ServiceCollection();
            services.AddEditDraftBlazor();
            using var provider = services.BuildServiceProvider();
            using var a = provider.CreateScope();
            using var b = provider.CreateScope();
            foreach (var t in new[] { typeof(EditDraftListBridge), typeof(EditDraftOfferRequests), typeof(EditDraftBadgeNotifier) })
            {
                var inA = a.ServiceProvider.GetRequiredService(t);
                a.ServiceProvider.GetRequiredService(t).Should().BeSameAs(inA, t.Name + ": one per circuit");
                b.ServiceProvider.GetRequiredService(t).Should().NotBeSameAs(inA, t.Name + ": never shared between circuits");
            }
            var seenB = new List<string>();
            b.ServiceProvider.GetRequiredService<EditDraftBadgeNotifier>().DraftsChanged += x => seenB.Add(x);
            a.ServiceProvider.GetRequiredService<EditDraftBadgeNotifier>().Changed("ToDo");
            seenB.Should().BeEmpty("a badge notification of circuit A never reaches circuit B");
            var record = new object();
            a.ServiceProvider.GetRequiredService<EditDraftOfferRequests>().RequestOffer(record, Guid.NewGuid());
            b.ServiceProvider.GetRequiredService<EditDraftOfferRequests>().TakeOfferRequest(record).Should().Be(Guid.Empty);
        }

        [Test]
        public void E6_E7_D9_the_popup_classes_are_plain_XAF_non_persistent_objects_and_no_property_names_an_editor_alias()
        {
            var textLines = new[] { (typeof(EditDraftRestorePlan), "Lead"), (typeof(EditDraftRestorePlan), "Provenance"), (typeof(EditDraftRestorePlan), "ConflictBanner"),
                                    (typeof(EditDraftReadOnlyView), "Lead"), (typeof(EditDraftReadOnlyView), "Provenance"), (typeof(EditDraftList), "Lead") };
            foreach (var t in PopupClasses)
            {
                t.BaseType.Should().Be(typeof(NonPersistentBaseObject), t.Name + ": XAF's own non-persistent base, no third-party base");
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    // Owner ruling O-7 (2026-10-02, library M3): the six text lines use the library's own label editor; every
                    // other property keeps XAF's own editor (no alias). No third-party alias anywhere.
                    var aliases = p.GetCustomAttributes(typeof(DevExpress.Persistent.Base.EditorAliasAttribute), true)
                        .Cast<DevExpress.Persistent.Base.EditorAliasAttribute>().Select(a => a.Alias).ToList();
                    if (textLines.Contains((t, p.Name))) aliases.Should().Equal(new[] { EditDraftLabelEditor.Alias }, t.Name + "." + p.Name + ": the library label editor (O-7)");
                    else aliases.Should().BeEmpty(t.Name + "." + p.Name + ": XAF's own editor, no alias");
                }
            }
            foreach (var (t, name) in textLines)
            {
                var defaults = t.GetProperty(name).GetCustomAttributes<ModelDefaultAttribute>().ToDictionary(a => a.PropertyName, a => a.PropertyValue);
                defaults.Should().Contain("AllowEdit", "False", t.Name + "." + name + " stays read-only");
                int.Parse(defaults["RowCount"]).Should().BeGreaterThan(1, t.Name + "." + name + ": a read-only memo, so long and multi-line text is not cut to one line");
            }
            var src = Wave1.Source("Xaf.EditDraft.Blazor/EditDraftModels.cs");
            src.Should().NotContain("NPOBase").And.NotContain("LabelPropertyEditor").And.NotContain("Llamachant");
            src.Should().Contain("[Appearance(\"EditDraftRestorePlan_HideEmptyConflictBanner\"", "the empty banner is still hidden");
        }

        [Test]
        public void E8_the_row_mapping_keeps_every_field_both_ways_and_makes_one_item_per_row()
        {
            var row = new EditDraftRestoreRow
            {
                Path = "Bikou", Group = "①g", Label = "① 備考", ChangeText = "a → b", CurrentText = "c", CurrentRaw = "c",
                StatusText = "他で変更されています", StatusCode = (int)EditDraftItemStatus.Conflict, Selectable = true, SideEffect = true, Selected = false
            };
            var item = EditDraftRestoreItems.ToItem(row);
            item.Should().BeEquivalentTo(row, o => o.ExcludingMissingMembers());
            EditDraftRestoreItems.ToRow(item).Should().BeEquivalentTo(row);
            var twins = EditDraftRestoreItems.ToItems(new[] { row, row });
            ReferenceEquals(twins[0], twins[1]).Should().BeFalse();
            new Dictionary<EditDraftRestoreItem, Guid> { [twins[0]] = Guid.NewGuid(), [twins[1]] = Guid.NewGuid() }.Should().HaveCount(2);
            EditDraftRestoreItems.ToItem(null).Should().BeNull();
            EditDraftRestoreItems.ToItems(null).Should().BeEmpty();

            var raised = new List<string>();
            item.PropertyChanged += (s, e) => raised.Add(e.PropertyName);
            item.Selected = false;
            item.Selected = true;
            raised.Should().Equal(new[] { "Selected", "Selected" }, "every assignment notifies, as the replaced base's helper did");
        }

        [Test]
        public void E19_the_moved_controllers_use_the_Core_seams_and_no_application_helper_clock_or_literal_UI_text()
        {
            // 0.4.0-preview.1 (owner ruling 2026-10-05): the owner seam takes the policy, and the record-access seam is replaced
            // by EditDraftServices.MayRestore (XAF security plus the optional IEditDraftAccessCheck); the pins follow the calls.
            var restore = Wave1.Source("Xaf.EditDraft.Blazor/EditDraftRestoreControllerBlazor.cs");
            restore.Should().Contain("EditDraftServices.CurrentOwner(Application?.ServiceProvider, ObjectSpace, _policy)")
                .And.Contain("EditDraftServices.MayRestore(Application, _policy, record)")
                .And.Contain("EditDraftMemberAccess.NotWritable(ObjectSpace, record, allPaths)");
            Wave1.Source("Xaf.EditDraft.Blazor/EditDraftListControllerBlazor.cs")
                .Should().Contain("EditDraftServices.MayRestore(Application, policy, target)");
            foreach (var file in LibrarySources("Xaf.EditDraft.Blazor").Where(f => f.EndsWith(".cs")))
            {
                // Owner ruling 2026-10-02 (O-6): model captions ([XafDisplayName]) stay Japanese until the M3 localisation.
                var code = string.Join("\n", Code(file).Split('\n').Where(l => !l.Contains("XafDisplayName(")));
                code.Should().NotContain("DateTime.Now", Path.GetFileName(file) + ": 'now' comes from the host clock");
                code.Should().NotContain("EditDraftOwner.").And.NotContain("EditDraftAccess.").And.NotContain("EditDraftWavePolicies", Path.GetFileName(file));
                foreach (var ui in new[] { "この入力控はこのログインでは戻せません（権限がありません）。", "入力控を開く", "\"破棄\"", "はい（選択した項目を戻す）", "保存されていない入力が見つかりました", "\"閉じる\"", "\"入力控\"" })
                    code.Should().NotContain(ui, Path.GetFileName(file) + ": UI text comes from EditDraftTexts");
            }
        }

        [Test]
        public void E22_the_Japanese_set_carries_today_s_UI_text_byte_for_byte_and_the_library_default_is_English()
        {
            var ja = EditDraftTextSet.Japanese;
            new[] { ja.ListCaption, ja.ActionOpen, ja.ActionDiscard, ja.ActionSelectAll, ja.RowOpenAction, ja.OfferOk, ja.OfferLater, ja.Close, ja.RecordNotVisible }
                .Should().Equal("入力控", "開く", "破棄", "すべて選択", "入力控を開く", "はい（選択した項目を戻す）", "あとで", "閉じる",
                    "この入力控はこのログインでは戻せません（権限がありません）。");   // expectation changed by owner rulings 2026-10-02 (E22b) and 2026-10-05 (no host scope term)
            ja.OfferLeadCount.Should().Be("前回この記録に入力され、保存されていない内容が {0} 件あります。");
            ja.OfferProvenance.Should().Be("{0} {1}／入力 {2:yyyy/MM/dd HH:mm}（{3} 項目）／{4}");
            ja.AppliedPartly.Should().Be("{0} 件を戻しました。{1} 件は戻せませんでした（その後に変更されたか、参照先がありません）。内容を確認してください。");
            var current = EditDraftTexts.Current;
            try
            {
                EditDraftTexts.Use(EditDraftLanguage.English);
                EditDraftListBridge.Caption.Should().Be("Drafts");
                EditDraftTexts.Use(EditDraftLanguage.Japanese);
                EditDraftListBridge.Caption.Should().Be("入力控");
            }
            finally { EditDraftTexts.Use(current); }
        }

        [Test]
        public void E22b_RecordNotVisible_says_the_draft_cannot_be_restored_for_this_login_and_the_refusal_log_line_is_unchanged()
        {
            // Expectation changed by owner ruling 2026-10-02 ("the refusal toast reworded to say the draft cannot be restored
            // for this login"); Codex tests a1 T23-T25 of that run.
            // Expectation changed again by owner ruling 2026-10-05 (0.4.0-preview.1: no host scope term in the library; the check
            // is XAF security plus the optional IEditDraftAccessCheck, so the reason is "no permission").
            EditDraftTextSet.Japanese.RecordNotVisible.Should().Be("この入力控はこのログインでは戻せません（権限がありません）。");
            // Expectation changed by gap G9 (run 2026-10-04-editdraft-close-gaps-08c338): no host term in the English set.
            EditDraftTextSet.English.RecordNotVisible.Should().Be("This draft cannot be restored for this login (no permission).");
            Wave1.Source("Xaf.EditDraft.Blazor/EditDraftRestoreControllerBlazor.cs").Should().Contain(
                "EditDraftLog.Info($\"[EditDraft] offer refused at '{trigger}': record {S(recordOid)} of {_policy.TypeName}: this login may not restore onto it\");",
                "T25: the log line (reworded by the 2026-10-05 ruling: no host scope term)");
            // T24: the list open, the badge open, the offer and the apply all show the text through EditDraftTexts. Counted per
            // file (diffreview a1 C5): losing one of the two restore-controller sites must fail, not only losing both.
            // The list controller has two sites since new-record capture: the 開く path and the 「新規」 recreate path
            // (owner ruling 2026-10-03: pin updated to 2, naming the recreate path).
            const string call = "Message(EditDraftTexts.Of(t => t.RecordNotVisible), InformationType.Warning);";
            foreach (var (file, sites) in new[] { ("Xaf.EditDraft.Blazor/EditDraftRestoreControllerBlazor.cs", 2), ("Xaf.EditDraft.Blazor/EditDraftListControllerBlazor.cs", 2), ("Xaf.EditDraft.Blazor/EditDraftListBadgeControllerBlazor.cs", 1) })
                (Wave1.Source(file).Split(call).Length - 1).Should().Be(sites, file);
        }
    }
}
