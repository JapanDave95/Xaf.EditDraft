using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using DevExpress.ExpressApp.Blazor.Components;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using DevExpress.ExpressApp.Model.NodeGenerators;
using DevExpress.ExpressApp.Utils;
using DevExpress.Persistent.Base;
using DevExpress.Xpo;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Xaf.EditDraft.Blazor;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests
{
    // Xaf.EditDraft library, milestone M3 (run 2026-10-02-editdraft-m3-1b4d82). Expectations from the Codex
    // requirement-only list of this run (tests a1, C1-C47), written before any code was shown to it; labels Cn refer to
    // them. Offline checks only: the rendering in a host (look, wrapping on screen, focus, the icon per row, captions on
    // screen) is the main session's browser pass (docs/xaf-editdraft-library-m3-2026-10-02.md).

    /// <summary>A host's store class (test only): the store base's member captions apply to it.</summary>
    public class EditDraftTestStore : EditDraftStoreBase
    {
        public EditDraftTestStore(Session session) : base(session) { }
    }

    [TestFixture]
    public class EditDraftLabelEditorTests
    {
        private static readonly (Type Type, string Member)[] TextLines =
        {
            (typeof(EditDraftRestorePlan), nameof(EditDraftRestorePlan.Lead)), (typeof(EditDraftRestorePlan), nameof(EditDraftRestorePlan.Provenance)),
            (typeof(EditDraftRestorePlan), nameof(EditDraftRestorePlan.ConflictBanner)), (typeof(EditDraftReadOnlyView), nameof(EditDraftReadOnlyView.Lead)),
            (typeof(EditDraftReadOnlyView), nameof(EditDraftReadOnlyView.Provenance)), (typeof(EditDraftList), nameof(EditDraftList.Lead))
        };

        private static string Render(string text)
        {
            var services = new ServiceCollection().BuildServiceProvider();
            using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
            return renderer.Dispatcher.InvokeAsync(async () =>
            {
                var parameters = ParameterView.FromDictionary(new Dictionary<string, object> { [nameof(EditDraftLabel.Text)] = text });
                var output = await renderer.RenderComponentAsync<EditDraftLabel>(parameters);
                return output.ToHtmlString();
            }).GetAwaiter().GetResult();
        }

        [Test]
        public void C1_the_editor_is_registered_under_its_own_alias_for_any_member_type_and_is_never_a_default_editor()
        {
            var registration = typeof(EditDraftLabelEditor).GetCustomAttribute<PropertyEditorAttribute>();
            registration.Should().NotBeNull();
            registration.IsDefaultEditor.Should().BeFalse("unrelated string properties keep XAF's own editor");
            registration.PropertyType.Should().Be(typeof(object), "an alias registration for any member type (the text lines are strings)");
            // The attribute keeps its alias internal: read it from the constructor arguments.
            typeof(EditDraftLabelEditor).GetCustomAttributesData().Single(a => a.AttributeType == typeof(PropertyEditorAttribute))
                .ConstructorArguments.Select(a => a.Value).Should().Equal(typeof(object), "Xaf.EditDraft.Label", false);
            EditDraftLabelEditor.Alias.Should().Be("Xaf.EditDraft.Label");
            EditDraftLabelEditor.Alias.Should().NotBe("LabelPropertyEditor", "the third-party alias a host may still register");
            typeof(EditDraftLabelEditor).Assembly.Should().BeSameAs(typeof(EditDraftBlazorModule).Assembly,
                "XAF collects [PropertyEditor] classes from each module's own assembly (ModuleBase.RegisterEditorDescriptors)");
            typeof(EditDraftLabelEditor).BaseType.Should().Be(typeof(DevExpress.ExpressApp.Blazor.Editors.BlazorPropertyEditorBase));
            new EditDraftLabelModel().ComponentType.Should().Be(typeof(EditDraftLabel));
        }

        [Test]
        public void C2_C3_the_six_text_lines_use_the_alias_keep_an_empty_caption_and_stay_read_only_and_the_typed_text_memo_does_not()
        {
            foreach (var (t, member) in TextLines)
            {
                var p = t.GetProperty(member);
                p.GetCustomAttributes<EditorAliasAttribute>().Select(a => a.Alias).Should().Equal(new[] { EditDraftLabelEditor.Alias }, t.Name + "." + member);
                p.GetCustomAttribute<XafDisplayNameAttribute>().DisplayName.Should().BeEmpty(t.Name + "." + member + ": no caption");
                p.GetCustomAttributes<ModelDefaultAttribute>().Should().Contain(a => a.PropertyName == "AllowEdit" && a.PropertyValue == "False", t.Name + "." + member);
            }
            var memo = typeof(EditDraftReadOnlyView).GetProperty(nameof(EditDraftReadOnlyView.Text));
            memo.GetCustomAttributes<EditorAliasAttribute>().Should().BeEmpty("the full typed text stays XAF's read-only memo");
            memo.GetCustomAttributes<ModelDefaultAttribute>().Should().Contain(a => a.PropertyName == "RowCount" && a.PropertyValue == "14")
                .And.Contain(a => a.PropertyName == "AllowEdit" && a.PropertyValue == "False");
        }

        [Test]
        public void C5_C6_C9_the_line_renders_as_one_plain_div_with_the_caption_classes_and_no_input_box_or_focus_target()
        {
            var html = Render("前回この記録に入力されました。");
            html.Should().StartWith("<div class=\"xaf-editdraft-label dxbl-fl-cpt dxbl-text\" style=\"white-space: pre-line; overflow-wrap: anywhere; padding-left: 4px; padding-right: 4px;\">");
            html.Should().EndWith("</div>");
            foreach (var forbidden in new[] { "<textarea", "<input", "tabindex", "contenteditable", "<button", "dxbl-memo", "dxbl-text-edit" })
                html.Should().NotContain(forbidden, "no editor, no box, nothing focusable");
            Regex.Matches(html, "<div").Count.Should().Be(1);
        }

        [Test]
        public void C9_C10_html_sensitive_text_stays_text_and_is_encoded_once()
        {
            var html = Render("<b>x</b> & \"q\" &amp; <script>alert(1)</script>");
            html.Should().NotContain("<b>").And.NotContain("<script>");
            html.Should().Contain("&lt;b&gt;x&lt;/b&gt;").And.Contain("&amp;amp;", "an entity-like text is shown as typed, encoded once");
        }

        [Test]
        public void C8_line_breaks_are_kept_as_new_lines_and_CR_LF_and_CR_become_LF()
        {
            EditDraftLabel.TextOf("a\r\nb\rc\n\nd", null).Should().Be("a\nb\nc\n\nd", "consecutive and boundary breaks are kept");
            EditDraftLabel.Style.Should().Contain("white-space: pre-line", "a line break in the value starts a new line; long lines wrap");
            EditDraftLabel.Style.Should().Contain("overflow-wrap: anywhere", "a long unbroken text wraps instead of overflowing");
            var html = Render("line 1\r\nline 2");
            Regex.IsMatch(html, "line 1(\n|&#xA;)line 2").Should().BeTrue(html + ": one line break between the lines (the encoder may write it as &#xA;)");
            html.Should().NotContain("\r").And.NotContain("&#xD;", "the CR is gone");
        }

        [Test]
        public void C11_null_and_empty_values_render_an_empty_line_without_a_placeholder()
        {
            EditDraftLabel.TextOf(null, null).Should().BeEmpty();
            EditDraftLabel.TextOf(string.Empty, null).Should().BeEmpty();
            EditDraftLabel.TextOf(5, "{0:000}").Should().Be("005", "a non-text value uses the member's display format");
            EditDraftLabel.TextOf(5, null).Should().Be("5");
            var html = Render(null);
            html.Should().NotContain("null").And.EndWith("\"></div>");
        }

        [Test]
        public void C12_the_empty_conflict_line_keeps_its_hide_rule_and_no_other_text_line_gets_one()
        {
            var banner = typeof(EditDraftRestorePlan).GetProperty(nameof(EditDraftRestorePlan.ConflictBanner));
            var rule = banner.GetCustomAttributes<DevExpress.ExpressApp.ConditionalAppearance.AppearanceAttribute>().Single();
            rule.Criteria.Should().Be("IsNullOrEmpty(ConflictBanner)");
            rule.TargetItems.Should().Be(nameof(EditDraftRestorePlan.ConflictBanner));
            rule.Visibility.Should().Be(DevExpress.ExpressApp.Editors.ViewItemVisibility.Hide);
            foreach (var (t, member) in TextLines.Where(x => x.Member != nameof(EditDraftRestorePlan.ConflictBanner)))
                t.GetProperty(member).GetCustomAttributes<DevExpress.ExpressApp.ConditionalAppearance.AppearanceAttribute>().Should().BeEmpty(t.Name + "." + member);
        }
    }

    [TestFixture]
    public class EditDraftRowOpenIconTests
    {
        private const string RowKey = "EditDraftRowHasDraft";

        private static BoolList Enabled(params (string Key, bool Value)[] reasons)
        {
            var list = new BoolList();
            foreach (var (key, value) in reasons) list.SetItemValue(key, value);
            return list;
        }

        private static CustomizeInlineActionButtonEventArgs Row(bool visible = true, bool enabled = true) =>
            new() { ActionId = EditDraftListBadgeControllerBlazor.OpenActionId, Visible = visible, Enabled = enabled, DataItem = new object() };

        [Test]
        public void C13_the_icon_shows_on_a_badged_row_only_and_an_unbadged_row_has_no_icon_rather_than_a_disabled_one()
        {
            var badged = Row();
            EditDraftRowOpenRule.Apply(badged, badged: true, Enabled(("Admitted", true)), RowKey);
            (badged.Visible, badged.Enabled).Should().Be((true, true));

            var plain = Row();
            EditDraftRowOpenRule.Apply(plain, badged: false, Enabled(("Admitted", true)), RowKey);
            plain.Visible.Should().BeFalse("no icon on a row without a draft");

            var hiddenAlready = Row(visible: false);
            EditDraftRowOpenRule.Apply(hiddenAlready, badged: false, Enabled(), RowKey);
            hiddenAlready.Visible.Should().BeFalse();
        }

        [Test]
        public void C16_selecting_an_unbadged_row_disables_the_toolbar_and_the_icons_on_badged_rows_fail_closed()
        {
            // Expectation changed by owner ruling O-11 ("Keep for the merge; fail closed before NuGet"; M3 §12 option (b),
            // applied in run 2026-10-04-editdraft-close-gaps-08c338): the rule no longer turns a badged row's icon on, so with an
            // unbadged row selected every badged row's icon is disabled, like the toolbar button. Name changed with it (was
            // C16_..._but_the_icons_on_badged_rows_stay_usable).
            // The action's own state with an unbadged row selected: the row reason is false (the toolbar button follows it).
            var actionState = Enabled(("Admitted", true), ("HasDrafts", true), (RowKey, false));
            var badged = Row(enabled: false);                                    // XAF passes the action's enabled state to every row
            EditDraftRowOpenRule.Apply(badged, badged: true, actionState, RowKey);
            badged.Enabled.Should().BeFalse("O-11 fail closed: the rule never turns an icon on");
            EditDraftRowOpenRule.OnlySelectedRowReasonDisables(actionState, RowKey).Should().BeTrue();

            var otherReason = Enabled(("Admitted", true), ("Security", false), (RowKey, false));
            var stillOff = Row(enabled: false);
            EditDraftRowOpenRule.Apply(stillOff, badged: true, otherReason, RowKey);
            stillOff.Enabled.Should().BeFalse("any other reason that disables the action still disables the icon");

            var unbadged = Row(enabled: false);
            EditDraftRowOpenRule.Apply(unbadged, badged: false, actionState, RowKey);
            (unbadged.Visible, unbadged.Enabled).Should().Be((false, false));
        }

        [Test]
        public void C15_C23_the_rule_never_enables_without_the_row_reason_and_tolerates_missing_input()
        {
            EditDraftRowOpenRule.OnlySelectedRowReasonDisables(Enabled(("Admitted", true), (RowKey, true)), RowKey).Should().BeFalse("nothing disables it");
            EditDraftRowOpenRule.OnlySelectedRowReasonDisables(Enabled(("Admitted", false)), RowKey).Should().BeFalse("the row reason is absent");
            EditDraftRowOpenRule.OnlySelectedRowReasonDisables(null, RowKey).Should().BeFalse();
            EditDraftRowOpenRule.OnlySelectedRowReasonDisables(Enabled((RowKey, false)), null).Should().BeFalse();
            FluentActions.Invoking(() => EditDraftRowOpenRule.Apply(null, true, Enabled(), RowKey)).Should().NotThrow();
            var e = Row(enabled: false);
            EditDraftRowOpenRule.Apply(e, badged: true, null, RowKey);
            e.Enabled.Should().BeFalse("no action state: nothing is enabled by the rule");
        }

        [Test]
        public void C14_C15_C23_the_badge_controller_applies_the_rule_per_row_and_keeps_the_toolbar_rule()
        {
            var badge = Wave1.Source(Wave1b.Badge);
            badge.Should().Contain("OpenAction.CustomizeControl += OpenAction_CustomizeControl;");
            badge.Should().Contain("if (e.Control is not ListEditorInlineActionControl inline) return;");
            badge.Should().Contain("inline.CustomizeInlineActionButton -= Inline_CustomizeButton;\r\n        inline.CustomizeInlineActionButton += Inline_CustomizeButton;", "subscribed once per control");
            badge.Should().Contain("var badged = _policy != null && _set.Count > 0 && _set.Contains(KeyOf(e.DataItem));", "the row's own key, never the selected row");
            badge.Should().Contain("EditDraftRowOpenRule.Apply(e, badged, OpenAction.Enabled, RowKey);");
            badge.Should().Contain("if (e == null || e.ActionId != OpenActionId) return;");
            // The toolbar copy is unchanged: shown while the screen has badged rows, enabled for a selected row with a draft.
            badge.Should().Contain("OpenAction.Active[HasDraftsKey] = _set.Count > 0;")
                .And.Contain("OpenAction.Enabled[RowKey] = selected == null || _set.Contains(KeyOf(selected));");
            badge.Should().Contain("ReloadSet(\"開く\");\r\n        if (!_set.Contains(oid))", "a click still decides on a fresh read");
            new EditDraftBadgeSet().Contains(Guid.Empty).Should().BeFalse("a row that cannot be keyed (Guid.Empty) is never badged");
        }
    }

    [TestFixture]
    [NonParallelizable]
    public class EditDraftModelCaptionTests
    {
        private static IEnumerable<EditDraftModelCaption> All => EditDraftPopupCaptions.All.Concat(EditDraftModelCaptions.Store);

        /// <summary>The caption the class or member declares in code (the English default).</summary>
        private static string Declared(EditDraftModelCaption c)
        {
            if (c.Member == null) return c.Type.GetCustomAttribute<XafDisplayNameAttribute>()?.DisplayName;
            var p = c.Type.GetProperty(c.Member);
            return p.GetCustomAttribute<XafDisplayNameAttribute>()?.DisplayName
                   ?? p.GetCustomAttributes<ModelDefaultAttribute>().FirstOrDefault(a => a.PropertyName == "Caption")?.PropertyValue;
        }

        [Test]
        public void C42_CareCrew_s_Japanese_set_gives_every_model_caption_of_today_byte_for_byte()
        {
            string Ja(Type t, string member) => EditDraftModelCaptions.Find(All, t, member, EditDraftTextSet.Japanese);
            Ja(typeof(EditDraftRestorePlan), null).Should().Be("保存されていない入力");
            Ja(typeof(EditDraftRestorePlan), nameof(EditDraftRestorePlan.Items)).Should().Be("内容");
            Ja(typeof(EditDraftReadOnlyView), null).Should().Be("戻せない入力");
            Ja(typeof(EditDraftReadOnlyView), nameof(EditDraftReadOnlyView.Text)).Should().Be("入力した内容（表示のみ）");
            Ja(typeof(EditDraftRestoreItem), null).Should().Be("入力控の項目");
            new[] { nameof(EditDraftRestoreItem.Selected), nameof(EditDraftRestoreItem.Label), nameof(EditDraftRestoreItem.ChangeText), nameof(EditDraftRestoreItem.CurrentText), nameof(EditDraftRestoreItem.StatusText) }
                .Select(m => Ja(typeof(EditDraftRestoreItem), m)).Should().Equal("戻す", "項目", "入力した内容", "現在の値", "状態");
            Ja(typeof(EditDraftList), null).Should().Be("入力控");
            Ja(typeof(EditDraftList), nameof(EditDraftList.Items)).Should().Be("入力控");
            Ja(typeof(EditDraftListItem), null).Should().Be("入力控");
            new[] { nameof(EditDraftListItem.TypeCaption), nameof(EditDraftListItem.Target), nameof(EditDraftListItem.Origin), nameof(EditDraftListItem.LastCapturedOn),
                    nameof(EditDraftListItem.EntryCount), nameof(EditDraftListItem.StateText), nameof(EditDraftListItem.ExpiresOn) }
                .Select(m => Ja(typeof(EditDraftListItem), m)).Should().Equal("画面（種類）", "対象", "由来", "入力日時", "項目数", "状態", "保存期限");
            new[] { nameof(EditDraftStoreBase.OwnerUserOid), nameof(EditDraftStoreBase.ObjectType), nameof(EditDraftStoreBase.ContextText) }
                .Select(m => Ja(typeof(EditDraftTestStore), m)).Should().Equal(new[] { "入力者", "記録種別", "対象" }, "on the host's store class (inherited members)");
            All.Should().HaveCount(23, "20 popup/list captions + 3 store member captions");
        }

        [Test]
        public void C41_with_no_host_choice_every_caption_is_the_English_one_the_class_declares()
        {
            foreach (var c in All)
            {
                var declared = Declared(c);
                declared.Should().NotBeNullOrEmpty(c.Type.Name + "." + c.Member);
                c.In(EditDraftTextSet.English).Should().Be(declared, c.Type.Name + "." + c.Member + ": the English set equals the declared default");
                c.In(null).Should().Be(declared, "no set = English");
                Regex.IsMatch(declared, "[^\\x20-\\x7E]").Should().BeFalse(c.Type.Name + "." + c.Member + ": the declared default is English");
            }
            foreach (var (t, member) in new[] { (typeof(EditDraftRestorePlan), "Lead"), (typeof(EditDraftRestorePlan), "Provenance"), (typeof(EditDraftRestorePlan), "ConflictBanner") })
                EditDraftModelCaptions.Find(All, t, member, EditDraftTextSet.Japanese).Should().BeNull("an empty caption is not a text");
        }

        [Test]
        public void C43_the_caption_follows_the_chosen_set_and_never_the_thread_culture()
        {
            var culture = Thread.CurrentThread.CurrentUICulture;
            var current = EditDraftTexts.Current;
            var origin = EditDraftPopupCaptions.All.Single(c => c.Type == typeof(EditDraftListItem) && c.Member == nameof(EditDraftListItem.Origin));
            try
            {
                EditDraftTexts.Use(EditDraftLanguage.Japanese);
                Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                origin.Current.Should().Be("由来");
                EditDraftTexts.Use(EditDraftLanguage.English);
                Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
                origin.Current.Should().Be("Origin");
                EditDraftTexts.Use(new EditDraftTextSet { CaptionListItemOrigin = null });
                origin.Current.Should().Be("Origin", "a host set that leaves a caption null falls back to English");
            }
            finally
            {
                EditDraftTexts.Use(current);
                Thread.CurrentThread.CurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        private static Mock<IModelClass> Class(Type type)
        {
            var info = new Mock<ITypeInfo>();
            info.SetupGet(i => i.Type).Returns(type);
            var cls = new Mock<IModelClass>();
            cls.SetupGet(c => c.TypeInfo).Returns(info.Object);
            cls.SetupProperty(c => c.Caption, "generated");
            return cls;
        }

        private static Mock<IModelMember> Member(string name)
        {
            var member = new Mock<IModelMember>();
            member.SetupGet(m => m.Name).Returns(name);
            member.SetupProperty(m => m.Caption, "generated");
            return member;
        }

        [Test]
        public void C41_C42_C44_the_updaters_write_the_set_in_use_into_the_generated_class_and_member_nodes_only_where_an_entry_applies()
        {
            var current = EditDraftTexts.Current;
            try
            {
                EditDraftTexts.Use(EditDraftLanguage.Japanese);
                var plan = Class(typeof(EditDraftRestorePlan)); var store = Class(typeof(EditDraftTestStore)); var other = Class(typeof(object));
                EditDraftModelCaptions.ApplyToClasses(new[] { plan.Object, store.Object, other.Object }, EditDraftPopupCaptions.All).Should().Be(1);
                plan.Object.Caption.Should().Be("保存されていない入力");
                store.Object.Caption.Should().Be("generated", "the host's store class keeps its own class caption");
                other.Object.Caption.Should().Be("generated");

                var owner = Member(nameof(EditDraftStoreBase.OwnerUserOid)); var payload = Member(nameof(EditDraftStoreBase.Payload));
                EditDraftModelCaptions.ApplyToMembers(typeof(EditDraftTestStore), new[] { owner.Object, payload.Object }, EditDraftModelCaptions.Store).Should().Be(1);
                owner.Object.Caption.Should().Be("入力者", "an inherited store member on the host's class");
                payload.Object.Caption.Should().Be("generated", "a member without an entry is left as generated");
                var unrelated = Member(nameof(EditDraftStoreBase.OwnerUserOid));
                EditDraftModelCaptions.ApplyToMembers(typeof(object), new[] { unrelated.Object }, EditDraftModelCaptions.Store).Should().Be(0);
                var lead = Member(nameof(EditDraftRestorePlan.Lead));
                EditDraftModelCaptions.ApplyToMembers(typeof(EditDraftRestorePlan), new[] { lead.Object }, EditDraftPopupCaptions.All).Should().Be(0, "the empty text-line captions are not written");

                EditDraftTexts.Use(EditDraftLanguage.English);
                var english = Class(typeof(EditDraftRestorePlan));
                EditDraftModelCaptions.ApplyToClasses(new[] { english.Object }, EditDraftPopupCaptions.All);
                english.Object.Caption.Should().Be("Unsaved input");
                EditDraftModelCaptions.ApplyToClasses(null, EditDraftPopupCaptions.All).Should().Be(0);
                EditDraftModelCaptions.ApplyToMembers(typeof(EditDraftTestStore), null, EditDraftModelCaptions.Store).Should().Be(0);
            }
            finally { EditDraftTexts.Use(current); }
        }

        [Test]
        public void C44_C45_both_modules_register_their_updaters_at_the_generated_layer_and_no_host_model_file_is_needed()
        {
            var updaters = (ModelNodesGeneratorUpdaters)Activator.CreateInstance(typeof(ModelNodesGeneratorUpdaters), nonPublic: true);
            new EditDraftCoreModule().AddGeneratorUpdaters(updaters);
            new EditDraftBlazorModule().AddGeneratorUpdaters(updaters);
            updaters[typeof(ModelBOModelMemberNodesGenerator)].Select(u => u.GetType())
                .Should().Contain(new[] { typeof(EditDraftStoreCaptionUpdater), typeof(EditDraftPopupMemberCaptionUpdater) });
            updaters[typeof(ModelBOModelClassNodesGenerator)].Select(u => u.GetType()).Should().Contain(typeof(EditDraftPopupClassCaptionUpdater));
            foreach (var project in new[] { "Xaf.EditDraft.Core", "Xaf.EditDraft.Blazor" })
                Directory.GetFiles(Path.Combine(Wave1.Root(), project), "*.xafml", SearchOption.AllDirectories).Should().BeEmpty(project + ": no model file; the captions come from the text set");
        }

        [Test]
        public void C46_O6_no_library_source_declares_a_Japanese_model_caption_any_more()
        {
            var caption = new Regex("(XafDisplayName\\(\\s*\"|ModelDefault\\(\\s*\"Caption\"\\s*,\\s*\")(?<text>[^\"]*)\"");
            foreach (var project in new[] { "Xaf.EditDraft.Core", "Xaf.EditDraft.Blazor" })
                foreach (var file in Directory.GetFiles(Path.Combine(Wave1.Root(), project), "*.cs", SearchOption.AllDirectories)
                             .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
                    foreach (System.Text.RegularExpressions.Match m in caption.Matches(File.ReadAllText(file)))
                        Regex.IsMatch(m.Groups["text"].Value, "[^\\x20-\\x7E]").Should().BeFalse(Path.GetFileName(file) + ": " + m.Value + " (the caption comes from the text set)");
        }

        [Test]
        public void C46_member_names_and_classes_are_unchanged_so_view_ids_stay_the_same()
        {
            typeof(EditDraftListItem).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(p => p.Name)
                .Should().Equal("TypeCaption", "Target", "Origin", "LastCapturedOn", "EntryCount", "StateText", "ExpiresOn", "DraftOid", "IsDiscarded");
            typeof(EditDraftRestoreItem).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(p => p.Name)
                .Should().Equal("Selected", "Label", "ChangeText", "CurrentText", "StatusText", "Path", "Group", "StatusCode", "Selectable", "SideEffect", "CurrentRaw");
            new[] { typeof(EditDraftRestorePlan), typeof(EditDraftReadOnlyView), typeof(EditDraftList), typeof(EditDraftListItem), typeof(EditDraftRestoreItem) }
                .Select(t => t.FullName).Should().Equal("Xaf.EditDraft.Blazor.EditDraftRestorePlan", "Xaf.EditDraft.Blazor.EditDraftReadOnlyView",
                    "Xaf.EditDraft.Blazor.EditDraftList", "Xaf.EditDraft.Blazor.EditDraftListItem", "Xaf.EditDraft.Blazor.EditDraftRestoreItem");
        }
    }

    [TestFixture]
    public class EditDraftTestProjectIsolationTests
    {
        private static readonly string[] Forbidden = { "CareCrew", "NursingHome_Chart", "Progress", "Llamachant", "CareTree" };

        [Test]
        public void C26_C27_this_test_assembly_and_everything_it_references_name_no_application_Module_Progress_Llamachant_or_CareTree_assembly()
        {
            var self = typeof(EditDraftTestProjectIsolationTests).Assembly;
            self.GetName().Name.Should().Be("Xaf.EditDraft.Tests");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<AssemblyName>(self.GetReferencedAssemblies());
            while (queue.Count > 0)
            {
                var name = queue.Dequeue();
                if (!seen.Add(name.Name)) continue;
                foreach (var f in Forbidden) name.Name.Should().NotStartWith(f, "referenced, directly or through " + string.Join(", ", seen.Take(5)) + "…");
                if (name.Name.StartsWith("System") || name.Name.StartsWith("Microsoft") || name.Name == "netstandard" || name.Name == "mscorlib") continue;
                Assembly loaded;
                try { loaded = Assembly.Load(name); } catch (FileNotFoundException) { continue; } catch (FileLoadException) { continue; }
                foreach (var next in loaded.GetReferencedAssemblies()) queue.Enqueue(next);
            }
            seen.Should().Contain(new[] { "Xaf.EditDraft.Core", "Xaf.EditDraft.Blazor" });
            var deps = Path.Combine(AppContext.BaseDirectory, "Xaf.EditDraft.Tests.deps.json");
            File.Exists(deps).Should().BeTrue("the resolved dependency list is published beside the test assembly");
            var json = File.ReadAllText(deps);
            foreach (var f in Forbidden) json.Should().NotContain("\"" + f, "the resolved dependency closure names no " + f + " assembly");
            Directory.GetFiles(AppContext.BaseDirectory, "*.dll").Select(Path.GetFileName)
                .Should().NotContain(n => Forbidden.Any(f => n.StartsWith(f, StringComparison.OrdinalIgnoreCase)), "nothing of the application is copied into the output");
        }

        [Test]
        public void C25_C31_the_project_uses_the_versions_the_application_tests_pin_adds_no_package_and_is_in_the_solution_once()
        {
            var root = Wave1.Root();
            var mine = File.ReadAllText(Path.Combine(root, "Xaf.EditDraft.Tests", "Xaf.EditDraft.Tests.csproj"));
            // The application's test project and solution exist only in the CareCrew repository; outside it this test
            // is skipped (owner ruling 2026-10-03), the same way the other host-file comparisons are.
            var theirs = Wave1.Source("NursingHome_Chart.Rostering.Tests/NursingHome_Chart.Rostering.Tests.csproj");
            static Dictionary<string, string> Packages(string csproj) => Regex.Matches(Regex.Replace(csproj, "<!--.*?-->", string.Empty, RegexOptions.Singleline),
                    "<PackageReference Include=\"([^\"]+)\" Version=\"([^\"]+)\"").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            var pinned = Packages(theirs);
            var used = Packages(mine);
            used.Should().NotBeEmpty();
            foreach (var (id, version) in used)
            {
                pinned.Should().ContainKey(id, id + " is already pinned by NursingHome_Chart.Rostering.Tests (no new package)");
                pinned[id].Should().Be(version, id + ": the same version");
            }
            mine.Should().Contain("<TargetFramework>net8.0</TargetFramework>").And.Contain("<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>");
            Regex.Matches(mine, "<ProjectReference Include=\"([^\"]+)\"").Select(m => m.Groups[1].Value)
                .Should().Equal(@"..\Xaf.EditDraft.Core\Xaf.EditDraft.Core.csproj", @"..\Xaf.EditDraft.Blazor\Xaf.EditDraft.Blazor.csproj");
            mine.Should().NotContain("<Compile Include").And.NotContain("Link=");
            var sln = Wave1.Source("CareCrew.sln");
            Regex.Matches(sln, "\"Xaf\\.EditDraft\\.Tests\", \"Xaf\\.EditDraft\\.Tests\\\\Xaf\\.EditDraft\\.Tests\\.csproj\"").Count.Should().Be(1);
        }
    }
}
