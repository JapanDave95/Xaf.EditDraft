using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.DC.Xpo;
using DevExpress.ExpressApp.Layout;
using DevExpress.ExpressApp.Xpo;
using DevExpress.Xpo;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Xaf.EditDraft.Blazor;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests
{
    // Close the library gaps (run 2026-10-04-editdraft-close-gaps-08c338), the parts that need the new API. Expectation ids
    // Tn are from the Codex requirement-only list of this run (tests a1). These tests did not compile on the unchanged main
    // (the API did not exist): that is their fail-before. The SQL Server behaviour (probe in a quoted schema, the sweep at
    // the exact cutoff) is executed against LocalDB in Xaf.EditDraft.Sample.Tests (SampleSqlServerTests).

    /// <summary>A store mapped into a named schema (XPO: "Schema.Table").</summary>
    [Persistent("sales.Order")]
    public class EditDraftSchemaStore : EditDraftStoreBase
    {
        public EditDraftSchemaStore(Session session) : base(session) { }
    }

    internal sealed class InMemoryNonSecuredFactory : INonSecuredObjectSpaceFactory
    {
        private readonly XPObjectSpaceProvider _provider;

        public InMemoryNonSecuredFactory(params Type[] types)
        {
            var typesInfo = new TypesInfo();
            var source = new XpoTypeInfoSource(typesInfo);
            typesInfo.AddEntityStore(source);
            foreach (var t in types) typesInfo.RegisterEntity(t);
            _provider = new XPObjectSpaceProvider((IXpoDataStoreProvider)new MemoryDataStoreProvider(), typesInfo, source, true, false);
        }

        public IObjectSpace CreateNonSecuredObjectSpace(Type objectType) => _provider.CreateObjectSpace();
    }

    [TestFixture]
    public class EditDraftStartupCheckTests
    {
        private sealed class HeadlessApplication : XafApplication
        {
            private readonly bool _suppressNonPersistent;
            public HeadlessApplication(bool suppressNonPersistent) => _suppressNonPersistent = suppressNonPersistent;
            protected override LayoutManager CreateLayoutManagerCore(bool simple) => null;

            // An application that removes XAF's own fallback (XafApplication.EnsureNonPersistentObjectSpaceProvider adds a
            // NonPersistentObjectSpaceProvider when none is registered, DevExpress 26.1.4).
            protected override void EnsureNonPersistentObjectSpaceProvider(IList<IObjectSpaceProvider> list)
            {
                if (!_suppressNonPersistent) base.EnsureNonPersistentObjectSpaceProvider(list);
            }
        }

        private static XPObjectSpaceProvider XpoProvider() =>
            new((IXpoDataStoreProvider)new MemoryDataStoreProvider(), XafTypesInfo.Instance, XpoTypesInfoHelper.GetXpoTypeInfoSource(), true, false);

        private static ServiceProvider Services(Dictionary<string, string> config, bool store = true, Action<EditDraftRegistry> registry = null, Action<EditDraftStoreOptions> options = null, Type storeType = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(config ?? new Dictionary<string, string>()).Build());
            if (store)
            {
                if (storeType == typeof(EditDraftSchemaStore)) services.AddEditDraftStore<EditDraftSchemaStore>(options);
                else services.AddEditDraftStore<EditDraftTestStore>(options);
            }
            if (registry != null) services.AddEditDraftRegistry(registry);
            return services.BuildServiceProvider();
        }

        private static void OnePolicy(EditDraftRegistry r) => r.Register(new EditDraftTypePolicy(typeof(EditDraftProbeB)) { PolicyId = "B" });

        [Test]
        public void G1_T2_without_a_non_persistent_provider_the_check_names_AddNonPersistent()
        {
            using var app = new HeadlessApplication(suppressNonPersistent: true);
            app.Setup("close-gaps", XpoProvider());
            EditDraftStartup.NonPersistentProviderProblem(app.ObjectSpaceProviders).Should().Contain("AddNonPersistent()");
            FluentActions.Invoking(() => EditDraftStartup.RequireNonPersistentProvider(app))
                .Should().Throw<EditDraftConfigurationException>().WithMessage("*AddNonPersistent()*");
        }

        [Test]
        public void G1_DevExpress_26_1_4_adds_the_non_persistent_provider_itself_when_none_is_registered()
        {
            using var app = new HeadlessApplication(suppressNonPersistent: false);
            app.Setup("close-gaps", XpoProvider());
            app.ObjectSpaceProviders.OfType<NonPersistentObjectSpaceProvider>().Should().ContainSingle();
            EditDraftStartup.NonPersistentProviderProblem(app.ObjectSpaceProviders).Should().BeNull();
            FluentActions.Invoking(() => EditDraftStartup.RequireNonPersistentProvider(app)).Should().NotThrow();
        }

        [Test]
        public void G1_T3_without_a_store_the_check_names_AddEditDraftStore()
        {
            using var services = Services(null, store: false, registry: OnePolicy);
            EditDraftStartup.ConfigurationProblems(services).Should().ContainSingle(p => p.Contains("AddEditDraftStore<"));
        }

        [TestCase(false, TestName = "G1_T4_global_switch_on_and_no_registry_registered")]
        [TestCase(true, TestName = "G1_T4_global_switch_on_and_an_empty_registry")]
        public void G1_T4_with_the_global_switch_on_an_empty_registry_is_a_problem(bool registerEmpty)
        {
            using var services = Services(new Dictionary<string, string> { [EditDraftSwitch.EnabledKey] = "true" }, registry: registerEmpty ? _ => { } : null);
            EditDraftStartup.ConfigurationProblems(services).Should().ContainSingle(p => p.Contains("AddEditDraftRegistry"));
        }

        [TestCase("false")]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("yes")]
        [TestCase("1")]
        public void G1_T5_with_the_global_switch_off_an_empty_registry_is_not_a_problem(string raw)
        {
            var config = new Dictionary<string, string>();
            if (raw != null) config[EditDraftSwitch.EnabledKey] = raw;
            using var services = Services(config);
            EditDraftStartup.ConfigurationProblems(services).Should().BeEmpty();
        }

        [Test]
        public void G1_T1_a_complete_registration_has_no_problem()
        {
            using var services = Services(new Dictionary<string, string> { [EditDraftSwitch.EnabledKey] = "true" }, registry: OnePolicy);
            EditDraftStartup.ConfigurationProblems(services).Should().BeEmpty();
        }

        [Test]
        public void G1_T25_a_store_on_a_database_that_is_not_SQL_Server_is_a_problem_without_running_a_statement()
        {
            var services = new ServiceCollection();
            services.AddSingleton<INonSecuredObjectSpaceFactory>(new InMemoryNonSecuredFactory(typeof(EditDraftTestStore)));
            services.AddEditDraftStore<EditDraftTestStore>();
            using var sp = services.BuildServiceProvider();
            foreach (var checkTable in new[] { false, true })
                EditDraftStartup.DatabaseProblems(sp, checkTable).Should().ContainSingle(p => p.Contains("SQL Server only"), $"checkTable={checkTable}");

            using var space = (XPObjectSpace)new InMemoryNonSecuredFactory(typeof(EditDraftTestStore)).CreateNonSecuredObjectSpace(typeof(EditDraftTestStore));
            EditDraftSqlServer.Classify(space.Session, out var provider).Should().Be(EditDraftDatabaseKind.NotSqlServer);
            provider.Should().Be("InMemoryDataStore");
        }

        [Test]
        public void G1_the_modules_run_the_checks_at_setup()
        {
            var core = Wave1.Source("Xaf.EditDraft.Core/EditDraftCoreModule.cs");
            core.Should().Contain("application.SetupComplete += ");
            core.Should().Contain("EditDraftStartup.Run(");
            Wave1.Source("Xaf.EditDraft.Blazor/EditDraftBlazorModule.cs").Should().Contain("EditDraftStartup.RequireNonPersistentProvider(");
        }

        [Test]
        public void G1_without_a_service_provider_the_checks_are_skipped_not_failed()
        {
            using var app = new HeadlessApplication(suppressNonPersistent: true);
            app.Modules.Add(new EditDraftCoreModule());
            FluentActions.Invoking(() => app.Setup("close-gaps", XpoProvider())).Should().NotThrow("a headless application has no service provider to read the registrations from");
        }

        // ---- G6 schema option, quoting --------------------------------------------------------------------------------

        [Test]
        public void G6_T21_T22_T23_the_store_name_is_schema_qualified_and_quoted()
        {
            var plain = new EditDraftStoreRegistration(typeof(EditDraftTestStore));
            (plain.Schema, plain.Table, plain.QualifiedName).Should().Be(("dbo", "EditDraftTestStore", "[dbo].[EditDraftTestStore]"));
            plain.TableName.Should().Be("EditDraftTestStore", "the XPO table name is unchanged");

            var named = new EditDraftStoreRegistration(typeof(EditDraftTestStore), new EditDraftStoreOptions { Schema = "edit drafts" });
            named.QualifiedName.Should().Be("[edit drafts].[EditDraftTestStore]");
            new EditDraftStoreRegistration(typeof(EditDraftTestStore), new EditDraftStoreOptions { Schema = "  " }).Schema.Should().Be("dbo", "blank = the default");

            var mapped = new EditDraftStoreRegistration(typeof(EditDraftSchemaStore));
            (mapped.Schema, mapped.Table, mapped.QualifiedName).Should().Be(("sales", "Order", "[sales].[Order]"), "XPO's own Schema.Table mapping");

            EditDraftSql.QuoteIdentifier("a]b").Should().Be("[a]]b]");
            EditDraftSql.QuoteIdentifier("Select").Should().Be("[Select]");
            FluentActions.Invoking(() => EditDraftSql.QuoteIdentifier(" ")).Should().Throw<ArgumentException>();
        }

        [Test]
        public void G6_a_schema_option_that_contradicts_the_XPO_mapping_is_a_problem()
        {
            using var conflict = Services(null, registry: OnePolicy, options: o => o.Schema = "other", storeType: typeof(EditDraftSchemaStore));
            EditDraftStartup.ConfigurationProblems(conflict).Should().ContainSingle(p => p.Contains("sales") && p.Contains("other"));
            using var same = Services(null, registry: OnePolicy, options: o => o.Schema = "sales", storeType: typeof(EditDraftSchemaStore));
            EditDraftStartup.ConfigurationProblems(same).Should().BeEmpty();
        }

        [Test]
        public void G6_the_writer_addresses_the_qualified_name_and_probes_with_a_parameter()
        {
            var writer = Wave1.Source("Xaf.EditDraft.Core/EditDraftWriter.cs");
            writer.Should().NotContain("dbo.", "no hard-coded schema");
            writer.Should().NotContain("[{Table}]", "the name is quoted once, in EditDraftStoreRegistration.QualifiedName");
            Regex.Matches(writer, @"\b(UPDATE|DELETE FROM) \{Table\}").Count.Should().Be(5);
            writer.Should().Contain("OBJECT_ID(@p0)");
        }

        // ---- G7 table cache --------------------------------------------------------------------------------------------

        [Test]
        public void G7_T29_an_absent_answer_is_rechecked_after_thirty_seconds_and_a_present_one_is_kept_five_minutes()
        {
            var cache = new EditDraftTableCache(EditDraftTableCache.AbsentRecheck);
            EditDraftTableCache.AbsentRecheck.Should().Be(TimeSpan.FromSeconds(30));
            var t0 = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc);
            var exists = false;
            var probes = 0;
            bool Probe() { probes++; return exists; }
            cache.Get("db", t0, Probe).Should().BeFalse();
            exists = true;                                                  // the database update creates the table
            cache.Get("db", t0.AddSeconds(29), Probe).Should().BeFalse("still inside the absent window");
            cache.Get("db", t0.AddSeconds(30), Probe).Should().BeTrue("the absent answer is re-probed");
            cache.Get("db", t0.AddMinutes(4), Probe).Should().BeTrue();
            probes.Should().Be(2, "the present answer is cached for five minutes");
            cache.Get("other", t0.AddMinutes(4), () => false).Should().BeFalse("T30: per database");
            Wave1.Source("Xaf.EditDraft.Core/EditDraftWriter.cs").Should().Contain("new EditDraftTableCache(EditDraftTableCache.AbsentRecheck)");
        }

        // ---- G10 decision helpers --------------------------------------------------------------------------------------

        [Test]
        public void G10_T40_T41_the_library_decision_helpers_fill_the_table_the_gate_reads()
        {
            EditDraftDecisions.Restorable("Note", "why", "where").Should().Be(new EditDraftMemberDecision("Note", EditDraftDisposition.Restorable, "A", "why", "where"));
            EditDraftDecisions.Group("Note", "why", "where").Disposition.Should().Be(EditDraftDisposition.Group);
            EditDraftDecisions.SideEffect("Note", "why", "where").Disposition.Should().Be(EditDraftDisposition.SideEffect);
            EditDraftDecisions.NotRestorable("Note", "why", "where").Disposition.Should().Be(EditDraftDisposition.NotRestorable);
            EditDraftDecisions.Excluded("Note", "why", "where").Disposition.Should().Be(EditDraftDisposition.Excluded);

            var policy = new EditDraftTypePolicy(typeof(EditDraftProbeB))
            {
                PolicyId = "B",
                MemberDeclaringBase = typeof(EditDraftProbeB),
                Decisions = EditDraftDecisions.Table(
                    EditDraftDecisions.Restorable("Note", "SetPropertyValue only", "EditDraftProbeB"),
                    EditDraftDecisions.Restorable("Status", "SetPropertyValue only", "EditDraftProbeB"),
                    EditDraftDecisions.Restorable("Number", "SetPropertyValue only", "EditDraftProbeB"))
            };
            EditDraftDecisions.Check(policy).Should().BeEmpty();
            var noReason = new EditDraftTypePolicy(typeof(EditDraftProbeB))
            {
                PolicyId = "B",
                MemberDeclaringBase = typeof(EditDraftProbeB),
                Decisions = EditDraftDecisions.Table(
                    EditDraftDecisions.Restorable("Note", "", "x"), EditDraftDecisions.Restorable("Status", "y", "x"), EditDraftDecisions.Restorable("Number", "y", "x"))
            };
            EditDraftDecisions.Check(noReason).Should().ContainSingle(p => p.Contains("without reason or evidence"), "the gate is unchanged");
        }

        // ---- G11 / G12 header action -----------------------------------------------------------------------------------

        [Test]
        public void G11_T42_the_header_action_can_be_shown_on_every_view_to_open_the_all_types_list()
        {
            new EditDraftBlazorOptions().HeaderActionOnEveryView.Should().BeFalse("default: only on a registered ListView, as before");
            using (var none = new ServiceCollection().AddEditDraftBlazor().BuildServiceProvider())
                EditDraftBlazorOptions.HeaderActionOnEveryViewIn(none).Should().BeFalse();
            using (var on = new ServiceCollection().AddEditDraftBlazor(o => o.HeaderActionOnEveryView = true).BuildServiceProvider())
                EditDraftBlazorOptions.HeaderActionOnEveryViewIn(on).Should().BeTrue();
            var list = Wave1.Source("Xaf.EditDraft.Blazor/EditDraftListControllerBlazor.cs");
            list.Should().Contain("EditDraftBlazorOptions.HeaderActionOnEveryViewIn(Application?.ServiceProvider)");
            list.Should().Contain("ShowList(includeDiscarded: false, objectTypeFilter: null)");
        }

        [Test]
        public void G12_T45_T46_the_header_action_shows_its_caption_and_image_without_a_model_node()
        {
            using var controller = new EditDraftListControllerBlazor();
            controller.HeaderListAction.PaintStyle.Should().Be(DevExpress.ExpressApp.Templates.ActionItemPaintStyle.CaptionAndImage);
        }
    }

    [TestFixture]
    public class EditDraftRetentionTests
    {
        [Test]
        public void G4_T13_the_cutoff_is_inclusive_like_the_hidden_rule()
        {
            var c = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Local);
            EditDraftRetention.IsExpired(c.AddTicks(-1), c).Should().BeTrue();
            EditDraftRetention.IsExpired(c, c).Should().BeTrue("hidden at the exact expiry instant, deleted at it too");
            EditDraftRetention.IsExpired(c.AddTicks(1), c).Should().BeFalse();
        }

        [TestCase("true", true)]
        [TestCase("True", true)]
        [TestCase(" true ", true)]
        [TestCase("false", false)]
        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("yes", false)]
        [TestCase("1", false)]
        public void G4_T10_T11_retention_is_off_unless_the_key_reads_as_true(string raw, bool on)
        {
            var config = new Dictionary<string, string> { [EditDraftSwitch.EnabledKey] = "true" };
            if (raw != null) config[EditDraftRetention.EnabledKey] = raw;
            using var services = new ServiceCollection().AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(config).Build()).BuildServiceProvider();
            EditDraftRetention.IsEnabled(services).Should().Be(on, "capture being on does not turn retention on");
        }

        [TestCase(null, 60)]
        [TestCase("15", 15)]
        [TestCase("1", 1)]
        [TestCase("0", 60)]
        [TestCase("-5", 60)]
        [TestCase("ten", 60)]
        public void G4_the_interval_defaults_to_sixty_minutes(string raw, int expected)
        {
            var config = new Dictionary<string, string>();
            if (raw != null) config[EditDraftRetention.IntervalMinutesKey] = raw;
            using var services = new ServiceCollection().AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(config).Build()).BuildServiceProvider();
            EditDraftRetention.IntervalMinutes(services).Should().Be(expected);
        }

        [Test]
        public void G4_G5_the_sweep_deletes_by_expiry_alone_with_the_application_clock_and_lives_outside_the_owner_fenced_writer()
        {
            var retention = Wave1.Source("Xaf.EditDraft.Core/EditDraftRetention.cs");
            retention.Should().Contain("DELETE TOP (@p1) FROM {");
            retention.Should().Contain("WHERE [ExpiresOn] <= @p0");
            retention.Should().NotContain("GETDATE", "G5: the cutoff is the application server's clock, passed as a parameter");
            retention.Should().Contain("EditDraftClock.Now(EditDraftServices.Clock(services))");
            Wave1.Source("Xaf.EditDraft.Core/EditDraftWriter.cs").Should().NotContain("ExpiresOn] <=", "the writer keeps only its five owner-fenced mutations");
        }

        [Test]
        public void G4_T15_a_sweep_on_a_database_that_is_not_SQL_Server_deletes_nothing_and_reports_failure()
        {
            var factory = new InMemoryNonSecuredFactory(typeof(EditDraftTestStore));
            var services = new ServiceCollection().AddSingleton<INonSecuredObjectSpaceFactory>(factory).AddEditDraftStore<EditDraftTestStore>().BuildServiceProvider();
            EditDraftRetention.Sweep(services).Should().Be(-1, "never a deletion count for work that did not run");
            using var none = new ServiceCollection().BuildServiceProvider();
            EditDraftRetention.Sweep(none).Should().Be(-1, "no store registered");
        }

        [Test]
        public void G4_T10_the_hosted_service_is_registered_only_by_AddEditDraftRetention()
        {
            new ServiceCollection().AddEditDraftBlazor().Any(d => d.ServiceType == typeof(IHostedService)).Should().BeFalse();
            new ServiceCollection().AddEditDraftRetention()
                .Should().ContainSingle(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(EditDraftRetentionService));
        }
    }
}
