using System;
using System.Collections.Generic;
using System.Linq;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.DC.Xpo;
using DevExpress.ExpressApp.Xpo;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Persistent.BaseImpl.PermissionPolicy;
using FluentAssertions;
using NUnit.Framework;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests
{
    // G2/G3 store security helpers (run 2026-10-04-editdraft-close-gaps-08c338). SECURITY-RELEVANT: written single-model by
    // Claude (binding guardrails Part 4); the Codex requirement-only list of this run excluded them on purpose. The owner
    // reviews both the helper and these expectations. The checks read the role ROWS (type, object and member permissions,
    // the permission policy, IsAdministrative); they do not run XAF's permission evaluation for a logged-in user.

    [TestFixture]
    public class EditDraftSecurityHelperTests
    {
        private static IObjectSpace SecuritySpace()
        {
            var typesInfo = new TypesInfo();
            var source = new XpoTypeInfoSource(typesInfo);
            typesInfo.AddEntityStore(source);
            foreach (var type in new[]
                     {
                         typeof(PermissionPolicyRole), typeof(PermissionPolicyTypePermissionObject), typeof(PermissionPolicyMemberPermissionsObject),
                         typeof(PermissionPolicyObjectPermissionsObject), typeof(PermissionPolicyNavigationPermissionObject),
                         typeof(PermissionPolicyActionPermissionObject), typeof(EditDraftTestStore)
                     })
                typesInfo.RegisterEntity(type);
            return new XPObjectSpaceProvider((IXpoDataStoreProvider)new MemoryDataStoreProvider(), typesInfo, source, true, false).CreateObjectSpace();
        }

        private static PermissionPolicyRole Role(IObjectSpace os, string name, SecurityPermissionPolicy policy = SecurityPermissionPolicy.DenyAllByDefault, bool administrative = false)
        {
            var role = os.CreateObject<PermissionPolicyRole>();
            role.Name = name;
            role.PermissionPolicy = policy;
            role.IsAdministrative = administrative;
            return role;
        }

        private static PermissionPolicyTypePermissionObject TypePermission(PermissionPolicyRole role, Type target, SecurityPermissionState? read)
        {
            var p = (PermissionPolicyTypePermissionObject)role.CreateTypePermissionObject(target);
            p.ReadState = read;
            return p;
        }

        private static List<string> Exposed(IObjectSpace os) =>
            EditDraftSecurity.FindRolesThatCanReadStore(os, typeof(EditDraftTestStore)).Select(e => e.RoleName).OrderBy(n => n, StringComparer.Ordinal).ToList();

        [Test]
        public void SEC_G2_the_deny_helper_denies_all_five_operations_to_every_role_and_is_idempotent()
        {
            using var os = SecuritySpace();
            var roles = new[]
            {
                Role(os, "Administrators", administrative: true), Role(os, "Default"),
                Role(os, "AllowAll", SecurityPermissionPolicy.AllowAllByDefault), Role(os, "ReadOnlyAll", SecurityPermissionPolicy.ReadOnlyAllByDefault)
            };
            TypePermission(roles[2], typeof(EditDraftTestStore), SecurityPermissionState.Allow);   // an existing ALLOW row is turned into a deny
            os.CommitChanges();

            EditDraftSecurity.DenyStoreToAllRoles(os, typeof(EditDraftTestStore)).Should().Be(4);
            os.CommitChanges();
            EditDraftSecurity.DenyStoreToAllRoles(os, typeof(EditDraftTestStore)).Should().Be(4, "second run");
            os.CommitChanges();

            foreach (var role in roles)
            {
                var rows = role.TypePermissions.Where(p => p.TargetType == typeof(EditDraftTestStore)).ToList();
                rows.Should().ContainSingle($"{role.Name}: one row after two runs");
                new[] { rows[0].ReadState, rows[0].WriteState, rows[0].CreateState, rows[0].DeleteState, rows[0].NavigateState }
                    .Should().AllBeEquivalentTo(SecurityPermissionState.Deny, role.Name);
            }
        }

        [Test]
        public void SEC_G2_G3_before_the_deny_every_role_that_can_read_the_store_is_found_and_after_it_only_the_ones_a_deny_cannot_bind()
        {
            using var os = SecuritySpace();
            Role(os, "Administrators", administrative: true);
            Role(os, "Default");                                                        // DenyAllByDefault, no row: cannot read
            Role(os, "AllowAll", SecurityPermissionPolicy.AllowAllByDefault);
            Role(os, "ReadOnlyAll", SecurityPermissionPolicy.ReadOnlyAllByDefault);
            TypePermission(Role(os, "TypeAllow"), typeof(EditDraftTestStore), SecurityPermissionState.Allow);
            TypePermission(Role(os, "BaseTypeAllow"), typeof(BaseObject), SecurityPermissionState.Allow);
            var objectGrant = TypePermission(Role(os, "ObjectAllow"), typeof(EditDraftTestStore), null);
            ((PermissionPolicyObjectPermissionsObject)objectGrant.CreateObjectPermission()).ReadState = SecurityPermissionState.Allow;
            var memberGrant = TypePermission(Role(os, "MemberAllow"), typeof(EditDraftTestStore), null);
            var member = (PermissionPolicyMemberPermissionsObject)memberGrant.CreateMemberPermission();
            member.Members = "Payload";
            member.ReadState = SecurityPermissionState.Allow;
            var readDenied = TypePermission(Role(os, "ReadDeniedWriteOpen", SecurityPermissionPolicy.AllowAllByDefault), typeof(EditDraftTestStore), SecurityPermissionState.Deny);
            readDenied.WriteState = null;
            os.CommitChanges();

            Exposed(os).Should().Equal("Administrators", "AllowAll", "BaseTypeAllow", "MemberAllow", "ObjectAllow", "ReadOnlyAll", "TypeAllow");
            var reasons = EditDraftSecurity.FindRolesThatCanReadStore(os, typeof(EditDraftTestStore)).ToDictionary(e => e.RoleName, e => e.Reason);
            reasons["Administrators"].Should().Contain("IsAdministrative");
            reasons["AllowAll"].Should().Contain("AllowAllByDefault");
            reasons["BaseTypeAllow"].Should().Contain(typeof(BaseObject).FullName);
            reasons["ObjectAllow"].Should().Contain("object permission");
            reasons["MemberAllow"].Should().Contain("member permission");

            EditDraftSecurity.DenyStoreToAllRoles(os, typeof(EditDraftTestStore));
            os.CommitChanges();
            Exposed(os).Should().Equal(new[] { "Administrators", "MemberAllow", "ObjectAllow" }, "G3: a type deny binds neither an administrative role nor object/member ALLOW grants");
        }

        [Test]
        public void SEC_G3_the_warning_names_each_role_that_can_still_read_the_store()
        {
            using var os = SecuritySpace();
            Role(os, "Administrators", administrative: true);
            Role(os, "Default");
            os.CommitChanges();
            var sink = new CapturingLog();
            var before = EditDraftLog.Sink;
            EditDraftLog.Sink = sink;
            try
            {
                EditDraftSecurity.WarnRolesThatCanReadStore(os, typeof(EditDraftTestStore)).Should().Be(1);
                sink.Warnings.Should().ContainSingle().Which.Should().Contain("Administrators").And.Contain(typeof(EditDraftTestStore).FullName).And.StartWith("[EditDraft]");
                EditDraftSecurity.DenyStoreToAllRoles(os, typeof(EditDraftTestStore));
                sink.Warnings.Should().HaveCount(2, "the deny helper reports what it could not bind");
            }
            finally { EditDraftLog.Sink = before; }
        }

        [Test]
        public void SEC_the_helpers_refuse_a_type_that_is_not_a_concrete_store_and_a_missing_object_space()
        {
            using var os = SecuritySpace();
            FluentActions.Invoking(() => EditDraftSecurity.DenyStoreToAllRoles(null, typeof(EditDraftTestStore))).Should().Throw<ArgumentNullException>();
            FluentActions.Invoking(() => EditDraftSecurity.DenyStoreToAllRoles(os, null)).Should().Throw<ArgumentNullException>();
            FluentActions.Invoking(() => EditDraftSecurity.DenyStoreToAllRoles(os, typeof(EditDraftStoreBase))).Should().Throw<ArgumentException>("the abstract base is not a table");
            FluentActions.Invoking(() => EditDraftSecurity.DenyStoreToAllRoles(os, typeof(BaseObject))).Should().Throw<ArgumentException>();
            FluentActions.Invoking(() => EditDraftSecurity.FindRolesThatCanReadStore(os, typeof(string))).Should().Throw<ArgumentException>();
        }

        [Test]
        public void SEC_the_module_warns_once_at_startup_and_the_writer_keeps_its_owner_fence()
        {
            Wave1.Source("Xaf.EditDraft.Core/EditDraftStartup.cs").Should().Contain("EditDraftSecurity.WarnRolesThatCanReadStore(");
            // The writer's five mutations each name the owner (W38b keeps the full text); the owner-agnostic retention delete
            // is the only other delete and lives in EditDraftRetention.cs.
            var writer = Wave1.Source("Xaf.EditDraft.Core/EditDraftWriter.cs");
            System.Text.RegularExpressions.Regex.Matches(writer, @"\[OwnerUserOid\] = @p\d").Count.Should().Be(5);
        }

        private sealed class CapturingLog : IEditDraftLog
        {
            public List<string> Warnings { get; } = new();
            public void Info(string message) { }
            public void Warning(string message) => Warnings.Add(message);
            public void Error(string message) { }
        }
    }
}
