using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Layout;
using DevExpress.Xpo.Metadata;
using FluentAssertions;
using NUnit.Framework;
using Xaf.EditDraft.Blazor;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests
{
    // 0.4.0-preview.1 (owner ruling 2026-10-05): access is XAF security plus one optional IEditDraftAccessCheck, the scope and
    // owner-kind concepts are gone, and the owner seam receives the policy. SINGLE-MODEL (Claude only; owner review): these
    // tests were written by the same model that wrote the code. XAF security itself (roles, an object criterion, the secured
    // object space) is executed in samples/Xaf.EditDraft.Sample/Xaf.EditDraft.Sample.Tests/SampleXafNativeAccessTests.cs; the
    // tests here cover the library's own logic without a database.

    [TestFixture]
    public class EditDraftXafNativeAccessTests
    {
        private static readonly Assembly Core = typeof(EditDraftCoreModule).Assembly;
        private static readonly Assembly Blazor = typeof(EditDraftBlazorModule).Assembly;

        private sealed class HeadlessApplication : XafApplication
        {
            protected override LayoutManager CreateLayoutManagerCore(bool simple) => null;
        }

        private sealed class FixedCheck : IEditDraftAccessCheck
        {
            public bool Restore = true, Recreate = true, Throw;
            public int Calls;
            public bool MayRestore(XafApplication application, EditDraftTypePolicy policy, object record) { Calls++; if (Throw) throw new InvalidOperationException(); return Restore; }
            public bool MayRecreate(XafApplication application, EditDraftTypePolicy policy, object record) { Calls++; if (Throw) throw new InvalidOperationException(); return Recreate; }
        }

        [Test]
        public void NA1_the_scope_and_owner_kind_members_are_gone_and_the_two_seams_have_the_new_shape()
        {
            Core.GetExportedTypes().Concat(Blazor.GetExportedTypes()).Select(t => t.Name)
                .Should().NotContain(new[] { "IEditDraftRecordAccess", "XafSecurityEditDraftRecordAccess", "EditDraftOwnerKind", "EditDraftOwnerRule" });
            typeof(EditDraftTypePolicy).GetProperty("ScopeOf").Should().BeNull();
            typeof(EditDraftTypePolicy).GetProperty("OwnerKind").Should().BeNull();
            typeof(EditDraftStoreBase).GetProperty("ScopeOid").Should().BeNull();
            typeof(EditDraftStoreBase).GetProperty("OwnerFlag").Should().BeNull();
            typeof(EditDraftSeed).GetProperty("ScopeOid").Should().BeNull();
            typeof(EditDraftSeed).GetProperty("OwnerFlag").Should().BeNull();
            typeof(EditDraftOwnerInfo).GetProperty("OwnerFlag").Should().BeNull();
            typeof(EditDraftRecreateDraft).GetProperty("ScopeOid").Should().BeNull();
            typeof(IEditDraftRecreateHost).GetMethod("IsScopeVisible").Should().BeNull();
            typeof(IEditDraftRecreateCandidate).GetMethod("IsVisible").Should().BeNull();
            typeof(EditDraftServices).GetMethod("RecordAccess").Should().BeNull();
            typeof(EditDraftTextSet).GetProperty("RecreateSubSectionNotVisible").Should().BeNull();
            Enum.GetNames(typeof(EditDraftRecreateOutcome)).Should().NotContain(new[] { "SubSectionNotVisible", "FilledNotVisible" }).And.Contain("FilledNotPermitted");

            typeof(IEditDraftAccessCheck).GetMethods().Select(m => m.Name).Should().BeEquivalentTo(new[] { "MayRestore", "MayRecreate" });
            typeof(IEditDraftOwnerResolver).GetMethods().Should().HaveCount(2)
                .And.OnlyContain(m => m.GetParameters().Last().ParameterType == typeof(EditDraftTypePolicy), "the owner seam receives the policy");
            typeof(IEditDraftRecreateHost).GetMethod("CurrentOwner").GetParameters().Select(p => p.ParameterType).Should().Equal(typeof(EditDraftTypePolicy));
        }

        [Test]
        public void NA2_the_store_base_maps_neither_removed_column_and_no_writer_statement_names_one()
        {
            var info = new ReflectionDictionary().GetClassInfo(typeof(EditDraftTestStore));
            info.PersistentProperties.Cast<XPMemberInfo>().Select(m => m.MappingField).Should().NotContain(new[] { "LoginIsStaffMember", "SubSectionOid" });
            typeof(EditDraftStoreBase).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Count(p => p.CanWrite)
                .Should().Be(17, "19 members before 0.4.0-preview.1, less OwnerFlag and ScopeOid");
            var writer = Wave1.Source("Xaf.EditDraft.Core/EditDraftWriter.cs");
            writer.Should().NotContain("[SubSectionOid]").And.NotContain("[LoginIsStaffMember]");
            writer.Should().Contain("[ContextText] = @p3, [LastError] = NULL ", "the supersede sets the context text, no scope column");
        }

        [Test]
        public void NA3_a_policy_is_generic_exactly_when_it_has_a_decision_table()
        {
            EditDraftTypePolicy.IsGeneric(null).Should().BeFalse();
            EditDraftTypePolicy.IsGeneric(new EditDraftTypePolicy(typeof(EditDraftNewProbe)) { PolicyId = "host" }).Should().BeFalse("a host's own policy without decisions is never admitted");
            EditDraftTypePolicy.IsGeneric(NewProbe.Policy()).Should().BeTrue();
        }

        [Test]
        public void NA4_without_request_security_the_XAF_check_allows_as_XAF_does_and_a_missing_argument_or_another_type_is_refused()
        {
            using var app = new HeadlessApplication();
            app.Security.Should().BeNull("no security strategy");
            using var os = NewProbe.Space();
            var record = os.CreateObject<EditDraftNewProbe>();
            var policy = NewProbe.Policy();
            var xaf = XafSecurityEditDraftAccessCheck.Instance;
            xaf.MayRestore(app, policy, record).Should().BeTrue("XAF allows editing without request security (DataManipulationRight.CanEdit)");
            xaf.MayRecreate(app, policy, record).Should().BeTrue("XAF allows creating without request security (DataManipulationRight.CanCreate)");
            xaf.MayRestore(null, policy, record).Should().BeFalse();
            xaf.MayRestore(app, null, record).Should().BeFalse();
            xaf.MayRestore(app, policy, null).Should().BeFalse();
            xaf.MayRecreate(null, policy, record).Should().BeFalse();
            xaf.MayRestore(app, policy, new object()).Should().BeFalse("a record of another type than the policy's");
            xaf.MayRecreate(app, policy, new object()).Should().BeFalse();
            EditDraftServices.AccessCheck(app.ServiceProvider).Should().BeNull();
            EditDraftServices.MayRestore(app, policy, record).Should().BeTrue("no host check registered: XAF security alone decides");
            EditDraftServices.MayRecreate(app, policy, record).Should().BeTrue();
            EditDraftServices.MayRestore(app, policy, null).Should().BeFalse();
            EditDraftServices.MayRecreate(null, policy, record).Should().BeFalse();
        }

        [Test]
        public void NA5_the_host_check_is_asked_in_addition_it_narrows_but_cannot_widen_and_an_exception_refuses()
        {
            using var os = NewProbe.Space();
            var record = os.CreateObject<EditDraftNewProbe>();
            var policy = NewProbe.Policy();
            var check = new FixedCheck { Restore = false, Recreate = false };
            using var app = new HeadlessApplication { ServiceProvider = new FixedServices().Add<IEditDraftAccessCheck>(check) };
            EditDraftServices.AccessCheck(app.ServiceProvider).Should().BeSameAs(check);
            EditDraftServices.MayRestore(app, policy, record).Should().BeFalse("XAF allows, the host refuses: the host narrows");
            EditDraftServices.MayRecreate(app, policy, record).Should().BeFalse();
            check.Restore = check.Recreate = true;
            EditDraftServices.MayRestore(app, policy, record).Should().BeTrue("both allow");
            EditDraftServices.MayRecreate(app, policy, record).Should().BeTrue();
            var calls = check.Calls;
            EditDraftServices.MayRestore(app, policy, new object()).Should().BeFalse("the XAF check refuses; a host that allows everything cannot widen it");
            EditDraftServices.MayRecreate(app, policy, new object()).Should().BeFalse();
            check.Calls.Should().Be(calls, "the host is not asked once the XAF check refused");
            check.Throw = true;
            EditDraftServices.MayRestore(app, policy, record).Should().BeFalse("an exception in the host's check is a refusal");
            EditDraftServices.MayRecreate(app, policy, record).Should().BeFalse();
        }

        [Test]
        public void NA6_the_drafts_list_asks_the_owner_seam_per_type_reads_each_distinct_owner_and_lists_a_row_only_under_its_type_s_owner()
        {
            var probe = NewProbe.Policy();
            var refPolicy = new EditDraftTypePolicy(typeof(EditDraftNewRef)) { PolicyId = "test:Ref", Decisions = EditDraftDecisions.Table() };
            var hostOwn = new EditDraftTypePolicy(typeof(EditDraftTestStore)) { PolicyId = "host:Own" };   // no decision table: not generic
            var registry = EditDraftRegistry.Create(r => { r.Register(probe); r.Register(refPolicy); r.Register(hostOwn); });
            Guid login = Guid.NewGuid(), staff = Guid.NewGuid();

            // One owner for every type (the library default's shape): one owner to read, asked once per policy and once for
            // the type-not-registered bucket.
            var asked = new List<EditDraftTypePolicy>();
            var single = new EditDraftOwnersByType(registry, p => { asked.Add(p); return new EditDraftOwnerInfo(login); });
            single.ToRead(null).Should().Equal(login);
            asked.Should().BeEquivalentTo(new[] { probe, refPolicy, null }, "each generic policy and the unregistered bucket");
            single.ToRead(probe.TypeName).Should().Equal(login);
            asked.Should().HaveCount(3, "answers are cached for one call");
            single.Lists(login, probe.TypeName).Should().BeTrue();
            single.Lists(login, "TypeNoLongerRegistered").Should().BeTrue("the owner named for no policy");
            single.Lists(staff, probe.TypeName).Should().BeFalse();
            single.Lists(Guid.Empty, probe.TypeName).Should().BeFalse();

            // A host that names another owner for one type: both owners are read, each row only under its type's owner.
            var perType = new EditDraftOwnersByType(registry, p => new EditDraftOwnerInfo(ReferenceEquals(p, refPolicy) ? staff : login));
            perType.ToRead(null).Should().BeEquivalentTo(new[] { login, staff });
            perType.ToRead(refPolicy.TypeName).Should().Equal(staff);
            perType.Lists(staff, refPolicy.TypeName).Should().BeTrue();
            perType.Lists(login, refPolicy.TypeName).Should().BeFalse("a row of that type stored under the login is not listed for that type");
            perType.Lists(staff, probe.TypeName).Should().BeFalse();

            // No owner, and an exception in the seam, read nothing.
            new EditDraftOwnersByType(registry, _ => EditDraftOwnerInfo.None).ToRead(null).Should().BeEmpty();
            new EditDraftOwnersByType(registry, _ => throw new InvalidOperationException()).ToRead(null).Should().BeEmpty();
        }

        [Test]
        public void NA7_every_former_record_access_site_asks_MayRestore_the_recreate_asks_MayRecreate_and_no_removed_seam_is_asked()
        {
            foreach (var project in new[] { "Xaf.EditDraft.Core", "Xaf.EditDraft.Blazor" })
                foreach (var file in Directory.GetFiles(Path.Combine(Wave1.Root(), project), "*.cs"))
                    File.ReadAllText(file).Should().NotContain("RecordAccess").And.NotContain("IsRecordVisible").And.NotContain("IsScopeVisible")
                        .And.NotContain("ScopeOf", Path.GetFileName(file));
            int Count(string rel, string text) => Wave1.Source(rel).Split(text).Length - 1;
            Count("Xaf.EditDraft.Blazor/EditDraftRestoreControllerBlazor.cs", "EditDraftServices.MayRestore(Application, _policy, record)").Should().Be(2, "the offer and the apply");
            Count("Xaf.EditDraft.Blazor/EditDraftListControllerBlazor.cs", "EditDraftServices.MayRestore(Application, policy, ").Should().Be(3, "the record text, 開く and the saved record's open");
            Count("Xaf.EditDraft.Blazor/EditDraftListBadgeControllerBlazor.cs", "EditDraftServices.MayRestore(Application, _policy, target)").Should().Be(1, "the row 開く");
            Count("Xaf.EditDraft.Blazor/EditDraftRecreateHostBlazor.cs", "EditDraftServices.MayRecreate(_host._application, policy, _record)").Should().Be(1);

            // The offer asks once there is a draft to offer (a record the login may only read opens without a message), and
            // before anything from a draft is shown.
            var restore = Wave1.Source("Xaf.EditDraft.Blazor/EditDraftRestoreControllerBlazor.cs");
            var offer = restore.Substring(restore.IndexOf("private void TryOffer(", StringComparison.Ordinal));
            var check = offer.IndexOf("EditDraftServices.MayRestore(Application, _policy, record)", StringComparison.Ordinal);
            offer.IndexOf("if (drafts.Count == 0)", StringComparison.Ordinal).Should().BeLessThan(check);
            check.Should().BeLessThan(offer.IndexOf("BuildPlan(", StringComparison.Ordinal));

            var list = Wave1.Source("Xaf.EditDraft.Blazor/EditDraftListControllerBlazor.cs");
            list.Should().Contain("case EditDraftRecreateOutcome.FilledNotPermitted: Message(EditDraftTexts.Of(t => t.RecreateNoPermission), InformationType.Warning); return;")
                .And.Contain("new EditDraftOwnersByType(registry, p => EditDraftServices.CurrentOwner(services, Application, p))")
                .And.Contain("rows = rows.Where(d => owners.Lists(d.OwnerUserOid, d.ObjectType))")
                .And.Contain("list.ObjectTypes[d.Oid] = d.ObjectType;")
                .And.Contain("if (EditDraftNewRecordRules.IsNewRecordDraft(d.TargetOid) && !MayCreateNew(policy, creatable)) list.NotOpenable.Add(d.Oid);");
            var popup = Wave1.Source("Xaf.EditDraft.Blazor/EditDraftListPopupControllerBlazor.cs");
            popup.Should().Contain("new NotOperator(new InOperator(nameof(EditDraftListItem.DraftOid), blocked.Select(o => (object)o).ToArray())).ToString()")
                .And.Contain("EditDraftServices.CurrentOwnerOfType(Application?.ServiceProvider, Application, ObjectTypeOf(item))")
                .And.Contain("list.OpenDraftDeferred(draftOid, objectType);");
        }

        [Test]
        public void NA9_the_Blazor_package_registers_the_evaluation_of_a_new_object_s_permissions_and_it_refuses_what_it_cannot_decide()
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddEditDraftBlazor();
            using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
            var evaluator = provider.GetService(typeof(IEditDraftNewObjectPermissions)) as IEditDraftNewObjectPermissions;
            evaluator.Should().NotBeNull("AddEditDraftBlazor registers it; without it a recreate is refused");
            using var os = NewProbe.Space();
            var record = os.CreateObject<EditDraftNewProbe>();
            evaluator.IsGranted(null, os, typeof(EditDraftNewProbe), record, XafSecurityEditDraftAccessCheck.NewObjectOperations)
                .Should().BeFalse("not XAF's integrated SecurityStrategy: cannot be decided, refused");
            XafSecurityEditDraftAccessCheck.NewObjectOperations.Should().Equal(new[] { "Create", "Write", "Read" }, "XAF's order before it saves a new object");
        }

        [Test]
        public void NA8_no_text_and_no_library_source_names_a_host_term_and_the_refusals_say_no_permission()
        {
            foreach (var set in new[] { EditDraftTextSet.Japanese, EditDraftTextSet.English })
                foreach (var p in typeof(EditDraftTextSet).GetProperties().Where(p => p.PropertyType == typeof(string)))
                    ((string)p.GetValue(set)).Should().NotContain("事業所", p.Name);
            EditDraftTextSet.Japanese.RecordNotVisible.Should().Be("この入力控はこのログインでは戻せません（権限がありません）。");
            EditDraftTextSet.English.RecordNotVisible.Should().Be("This draft cannot be restored for this login (no permission).");
            EditDraftTextSet.Japanese.RecreateNoPermission.Should().Be("この記録を作成する権限がありません。");
            EditDraftTextSet.English.RecreateNoPermission.Should().Be("You do not have permission to create this record.");

            var words = new[] { "事業所", "SubSection", "StaffMember", "GeneralUser", "HostDefined", "OwnerKind" };
            var sep = Path.DirectorySeparatorChar;
            foreach (var project in new[] { "Xaf.EditDraft.Core", "Xaf.EditDraft.Blazor" })
                foreach (var file in Directory.GetFiles(Path.Combine(Wave1.Root(), project), "*.*", SearchOption.AllDirectories)
                             .Where(f => !f.Contains(sep + "bin" + sep) && !f.Contains(sep + "obj" + sep))
                             .Where(f => f.EndsWith(".cs") || f.EndsWith(".js") || f.EndsWith(".css") || f.EndsWith(".xafml")))
                {
                    var text = File.ReadAllText(file);
                    foreach (var word in words) text.Should().NotContain(word, Path.GetFileName(file));
                    Regex.IsMatch(text, @"\bF2\b").Should().BeFalse(Path.GetFileName(file) + " names F2");
                }
        }
    }
}
