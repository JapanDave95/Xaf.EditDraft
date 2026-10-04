using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.DC.Xpo;
using DevExpress.ExpressApp.Layout;
using DevExpress.ExpressApp.Xpo;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.PermissionPolicy;
using DevExpress.Xpo;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Xaf.EditDraft.Blazor;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests
{
    // Bounded fix pass on 0.2.0-preview.1 (run 2026-10-04-editdraft-fix-pass-3b8147; owner rulings 2026-10-04: C1 derive the
    // schema from XPO and drop the option, C2, C3, A1, C5/C6 documented). Expectation ids Tn are from the Codex
    // requirement-only list of this run (tests a1), written before any code was shown to it. These tests use only names that
    // exist before and after the change (reflection by name, texts, files), so they were run red on the unchanged 748f2f9
    // first. The SQL Server parts (one table for XPO and the T-SQL, the per-connection cache, the database-update path) are in
    // Xaf.EditDraft.Sample.Tests (SampleFixPassTests), against LocalDB.

    /// <summary>An explicit table name without a schema (T2).</summary>
    [Persistent("FixPassPlainRows")]
    public class EditDraftPlainNamedStore : EditDraftStoreBase
    {
        public EditDraftPlainNamedStore(Session session) : base(session) { }
    }

    /// <summary>An explicit dbo-qualified table name (T2).</summary>
    [Persistent("dbo.FixPassDboRows")]
    public class EditDraftDboNamedStore : EditDraftStoreBase
    {
        public EditDraftDboNamedStore(Session session) : base(session) { }
    }

    /// <summary>The store of the F2 retry test only, so no other test's remembered state can hide its checks.</summary>
    public class EditDraftRecheckStore : EditDraftStoreBase
    {
        public EditDraftRecheckStore(Session session) : base(session) { }
    }

    [TestFixture]
    [NonParallelizable]
    public class EditDraftFixPassTests
    {
        private sealed class HeadlessApplication : XafApplication
        {
            protected override LayoutManager CreateLayoutManagerCore(bool simple) => null;
        }

        /// <summary>Throws on its first <c>failures</c> calls, then hands out the inner factory's object spaces.</summary>
        private sealed class FlakyFactory : INonSecuredObjectSpaceFactory
        {
            private readonly INonSecuredObjectSpaceFactory _inner;
            private readonly int _failures;
            public int Calls { get; private set; }
            public FlakyFactory(int failures, INonSecuredObjectSpaceFactory inner) { _failures = failures; _inner = inner; }

            public IObjectSpace CreateNonSecuredObjectSpace(Type objectType)
            {
                Calls++;
                if (Calls <= _failures) throw new InvalidOperationException("the database is not reachable (test)");
                return _inner.CreateNonSecuredObjectSpace(objectType);
            }
        }

        private sealed class CapturingLog : IEditDraftLog
        {
            public List<string> Lines { get; } = new();
            public List<string> Warnings { get; } = new();
            public void Info(string message) { lock (Lines) Lines.Add(message); }
            public void Warning(string message) { lock (Lines) { Lines.Add(message); Warnings.Add(message); } }
            public void Error(string message) { lock (Lines) Lines.Add(message); }
        }

        private static T WithLog<T>(Func<CapturingLog, T> body)
        {
            var log = new CapturingLog();
            var before = EditDraftLog.Sink;
            EditDraftLog.Sink = log;
            try { return body(log); }
            finally { EditDraftLog.Sink = before; }
        }

        private static string RepoFile(string rel)
        {
            var path = Path.Combine(Wave1.Root(), rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) Assert.Fail($"{rel} does not exist in the repository");
            return File.ReadAllText(path);
        }

        private static ServiceProvider Config(params (string Key, string Value)[] values)
        {
            var config = values.ToDictionary(v => v.Key, v => v.Value);
            return new ServiceCollection().AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(config).Build()).BuildServiceProvider();
        }

        // ---- F1 (Codex C1): one source of truth for the store table ------------------------------------------------------

        [Test]
        public void F1_T1_the_schema_option_is_gone_and_the_store_mapping_is_the_only_input()
        {
            var core = typeof(EditDraftStoreBase).Assembly;
            core.GetType("Xaf.EditDraft.Core.EditDraftStoreOptions").Should().BeNull("owner ruling 2026-10-04: drop the option");
            var add = typeof(EditDraftServiceCollectionExtensions).GetMethods().Where(m => m.Name == "AddEditDraftStore").ToList();
            add.Should().ContainSingle();
            add[0].GetParameters().Select(p => p.ParameterType).Should().Equal(new[] { typeof(IServiceCollection) }, "no configure delegate: nothing but the store class");
            typeof(EditDraftStoreRegistration).GetConstructors().Select(c => c.GetParameters().Length).Should().Equal(new[] { 1 }, "the store type only");
            typeof(EditDraftStoreRegistration).GetProperty("ConfiguredSchema").Should().BeNull();
            typeof(EditDraftStoreRegistration).GetProperty("SchemaConflicts").Should().BeNull();
        }

        [Test]
        public void F1_T2_T3_each_store_class_selects_its_own_mapped_table_quoted_part_by_part()
        {
            static string Q(Type t) => new EditDraftStoreRegistration(t).QualifiedName;
            Q(typeof(EditDraftTestStore)).Should().Be("[dbo].[EditDraftTestStore]", "class name, no mapping: XPO's SQL Server default schema");
            Q(typeof(EditDraftPlainNamedStore)).Should().Be("[dbo].[FixPassPlainRows]", "explicit unqualified table name");
            Q(typeof(EditDraftDboNamedStore)).Should().Be("[dbo].[FixPassDboRows]", "explicit dbo.table");
            Q(typeof(EditDraftSchemaStore)).Should().Be("[sales].[Order]", "explicit non-dbo mapping");
            Q(typeof(EditDraftTestStore)).Should().Be("[dbo].[EditDraftTestStore]", "no state leaks between registrations");
            EditDraftSql.QuoteIdentifier("a]b").Should().Be("[a]]b]");
            FluentActions.Invoking(() => EditDraftSql.QuoteIdentifier("")).Should().Throw<ArgumentException>();
        }

        [Test]
        public void F1_T6_the_guide_and_READMEs_name_the_store_mapping_and_no_schema_option()
        {
            var guide = RepoFile("docs/consumer-guide.md");
            guide.Should().Contain("[Persistent(\"myschema.MyEditDraft\")]");
            foreach (var rel in new[] { "docs/consumer-guide.md", "README.md", "samples/Xaf.EditDraft.Sample/README.md" })
            {
                var text = RepoFile(rel);
                text.Should().NotContain("o.Schema", rel);
                text.Should().NotContain("EditDraftStoreOptions", rel);
            }
        }

        // ---- F2 (Codex C2): only a passed check is remembered --------------------------------------------------------------

        [Test]
        public void F2_T11_a_failed_database_check_and_a_failed_role_scan_are_not_remembered_and_run_again_at_the_next_setup()
        {
            var factory = new FlakyFactory(3, new InMemoryNonSecuredFactory(typeof(EditDraftRecheckStore)));
            using var services = new ServiceCollection().AddSingleton<INonSecuredObjectSpaceFactory>(factory).AddEditDraftStore<EditDraftRecheckStore>().BuildServiceProvider();
            WithLog(log =>
            {
                // Setup 1: the database check throws (call 1), the role scan throws (call 2). Table check off: logged, no stop.
                using (var app = new HeadlessApplication { ServiceProvider = services })
                    FluentActions.Invoking(() => EditDraftStartup.Run(app)).Should().NotThrow();
                // Setup 2: both run again; the database check throws again (call 3), the role scan now completes (call 4).
                using (var app = new HeadlessApplication { ServiceProvider = services })
                    FluentActions.Invoking(() => EditDraftStartup.Run(app)).Should().NotThrow();
                // Setup 3: the database check runs a third time and now reaches the store (in memory: not SQL Server).
                using (var app = new HeadlessApplication { ServiceProvider = services })
                    FluentActions.Invoking(() => EditDraftStartup.Run(app)).Should().Throw<EditDraftConfigurationException>().WithMessage("*SQL Server only*");

                log.Warnings.Count(w => w.Contains("database check of EditDraftRecheckStore could not run")).Should().Be(2, "logged each time it failed");
                log.Lines.Count(l => l.Contains("role check failed")).Should().Be(1);
                log.Lines.Count(l => l.Contains("no XPO PermissionPolicyRole in this application")).Should().Be(1, "the failed role scan ran again at the next setup");
                log.Lines.Should().NotContain(l => l.Contains("startup checks passed: store EditDraftRecheckStore"), "nothing here passed");
                factory.Calls.Should().Be(5);
                return 0;
            });
        }

        // ---- F3 (Codex C3): the retention interval is bounded ------------------------------------------------------------

        [TestCase(null, 60, TestName = "F3_T17_missing_is_the_default_60")]
        [TestCase("1", 1, TestName = "F3_T17_1")]
        [TestCase("1439", 1439, TestName = "F3_T17_max_minus_1")]
        [TestCase("1440", 1440, TestName = "F3_T17_max_1440")]
        [TestCase(" 30 ", 30, TestName = "F3_T17_spaces_trimmed")]
        [TestCase("1441", 1440, TestName = "F3_T18_max_plus_1_is_clamped")]
        [TestCase("71582", 1440, TestName = "F3_T18_71582_is_clamped")]
        [TestCase("71583", 1440, TestName = "F3_T18_71583_is_clamped")]
        [TestCase("2147483647", 1440, TestName = "F3_T18_int_max_is_clamped")]
        [TestCase("99999999999999999999", 1440, TestName = "F3_T18_beyond_long_is_clamped")]
        [TestCase("0", 0, TestName = "F3_T19_zero_is_off")]
        [TestCase("-1", 0, TestName = "F3_T19_minus_1_is_off")]
        [TestCase("-2147483648", 0, TestName = "F3_T19_int_min_is_off")]
        [TestCase("-99999999999999999999", 0, TestName = "F3_T19_beyond_long_negative_is_off")]
        [TestCase("ten", 60, TestName = "F3_not_a_number_is_the_default")]
        [TestCase("1.5", 60, TestName = "F3_not_a_whole_number_is_the_default")]
        public void F3_the_interval_is_between_1_and_1440_minutes_or_0_for_off(string raw, int expected)
        {
            using var services = raw == null ? Config() : Config((EditDraftRetention.IntervalMinutesKey, raw));
            EditDraftRetention.IntervalMinutes(services).Should().Be(expected);
        }

        [Test]
        [Category("Slow")]
        public async Task F3_T19_T20_the_hosted_service_stops_with_a_warning_at_interval_zero_and_keeps_running_with_a_huge_interval()
        {
            // About one minute: the service's first pass is one minute after start (unchanged). Decisive check of Codex C3.
            var log = new CapturingLog();
            var before = EditDraftLog.Sink;
            EditDraftLog.Sink = log;
            try
            {
                using var zeroServices = Config((EditDraftRetention.IntervalMinutesKey, "0"), (EditDraftRetention.EnabledKey, "true"));
                using var hugeServices = Config((EditDraftRetention.IntervalMinutesKey, "72000"), (EditDraftRetention.EnabledKey, "false"));
                using var zero = new EditDraftRetentionService(zeroServices);
                using var huge = new EditDraftRetentionService(hugeServices);
                await zero.StartAsync(CancellationToken.None);
                await huge.StartAsync(CancellationToken.None);

                var first = await Task.WhenAny(zero.ExecuteTask, Task.Delay(TimeSpan.FromSeconds(100)));
                first.Should().BeSameAs(zero.ExecuteTask, "interval 0: the service stops at its first pass");
                zero.ExecuteTask.Status.Should().Be(TaskStatus.RanToCompletion);
                log.Warnings.Should().ContainSingle(w => w.Contains("is 0 or below"));
                log.Lines.Should().NotContain(l => l.StartsWith("[EditDraft] retention sweep"), "interval 0: no sweep, not even the first one");

                await Task.Delay(TimeSpan.FromSeconds(3));
                huge.ExecuteTask.IsCompleted.Should().BeFalse("72000 minutes is clamped to 1440; the delay does not throw, the host keeps running");
                await huge.StopAsync(CancellationToken.None);
                huge.ExecuteTask.IsFaulted.Should().BeFalse();
            }
            finally { EditDraftLog.Sink = before; }
        }

        [Test]
        public void F3_T21_the_guide_states_the_maximum_and_what_0_or_below_means()
        {
            var guide = RepoFile("docs/consumer-guide.md");
            guide.Should().Contain("1440");
            guide.Should().MatchRegex("(?i)0 or below");
            guide.Should().MatchRegex("(?i)restart");
        }

        // ---- F4 (Codex A1): the table check after the schema update --------------------------------------------------------

        [Test]
        public void F4_the_core_module_adds_the_table_check_updater_and_without_a_service_provider_it_does_nothing()
        {
            using var space = new InMemoryNonSecuredFactory(typeof(EditDraftTestStore)).CreateNonSecuredObjectSpace(typeof(EditDraftTestStore));
            var updaters = new EditDraftCoreModule().GetModuleUpdaters(space, new Version(0, 0, 0, 0)).ToList();
            var check = updaters.SingleOrDefault(u => u.GetType().Name == "EditDraftTableCheckUpdater");
            check.Should().NotBeNull("A1: the optional table check runs after XAF's schema update");
            FluentActions.Invoking(() => check.UpdateDatabaseAfterUpdateSchema()).Should().NotThrow("a module that was not set up has no service provider: nothing to check");
        }

        [Test]
        public void F4_T30_the_guide_explains_when_the_table_check_runs()
        {
            var guide = RepoFile("docs/consumer-guide.md");
            guide.Should().MatchRegex("(?i)does not stop a fresh database's first update");
            guide.Should().MatchRegex("(?i)after the update's schema update");
            guide.Should().MatchRegex("(?i)only a warning");
        }

        // ---- F6 (Codex C5/C6): the role scan is documented as best-effort -------------------------------------------------

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

        [Test]
        public void F6_T38_the_guide_section_5_states_that_the_role_scan_is_best_effort()
        {
            var guide = RepoFile("docs/consumer-guide.md");
            var start = guide.IndexOf("## 5. Security", StringComparison.Ordinal);
            var end = guide.IndexOf("## 6.", StringComparison.Ordinal);
            start.Should().BeGreaterThan(0);
            var section5 = guide.Substring(start, end - start);
            section5.Should().MatchRegex("(?i)best-effort");
            section5.Should().Contain("PermissionPolicyRoleBase");
            section5.Should().MatchRegex("(?i)criteria");
        }

        [Test]
        public void F6_T39_T41_the_warning_says_best_effort_and_a_grant_whose_criterion_never_matches_is_still_reported()
        {
            using var os = SecuritySpace();
            var role = os.CreateObject<PermissionPolicyRole>();
            role.Name = "ImpossibleGrant";
            role.PermissionPolicy = SecurityPermissionPolicy.DenyAllByDefault;
            var type = (PermissionPolicyTypePermissionObject)role.CreateTypePermissionObject(typeof(EditDraftTestStore));
            type.ReadState = SecurityPermissionState.Deny;
            var grant = (PermissionPolicyObjectPermissionsObject)type.CreateObjectPermission();
            grant.Criteria = "1 = 0";
            grant.ReadState = SecurityPermissionState.Allow;
            os.CommitChanges();

            EditDraftSecurity.FindRolesThatCanReadStore(os, typeof(EditDraftTestStore)).Select(e => e.RoleName)
                .Should().Contain("ImpossibleGrant", "T41: criteria are not evaluated (documented, not changed)");
            var warnings = WithLog(log =>
            {
                EditDraftSecurity.WarnRolesThatCanReadStore(os, typeof(EditDraftTestStore));
                return log.Warnings.ToList();
            });
            warnings.Should().ContainSingle();
            warnings[0].Should().Contain("ImpossibleGrant").And.MatchRegex("(?i)best-effort").And.Contain("PermissionPolicyRoleBase").And.MatchRegex("(?i)criteria");
        }
    }
}
