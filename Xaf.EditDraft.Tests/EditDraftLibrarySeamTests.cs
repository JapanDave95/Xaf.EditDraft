using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests
{
    // Xaf.EditDraft library, milestone M1 (run 2026-10-01-editdraft-m1-c3f4de): the seams Core exposes. Expectations from
    // the Codex requirement-only list of this run (tests a1): E9-E18, E21, E22.
    // The owner and record-access seams (design §4.11) are SINGLE-MODEL: their checks here are Claude's alone.
    // Library milestone M3 (run 2026-10-02-editdraft-m3-1b4d82): the tests of the library's own seams.

    /// <summary>A service provider of fixed instances (no container): what a host's DI answers.</summary>
    internal sealed class FixedServices : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services = new();
        public FixedServices Add<T>(T service) { _services[typeof(T)] = service; return this; }
        public object GetService(Type serviceType) => _services.TryGetValue(serviceType, out var s) ? s : null;
    }

    [TestFixture]
    public class EditDraftLibraryRegistryTests
    {
        private static EditDraftTypePolicy Probe(Type t, string id) => new(t) { PolicyId = id };

        [Test]
        public void E9_two_registries_never_see_each_other_s_policies_in_either_creation_order()
        {
            foreach (var aFirst in new[] { true, false })
            {
                EditDraftRegistry a = null, b = null;
                if (aFirst) { a = EditDraftRegistry.Create(r => r.Register(Probe(typeof(EditDraftProbeA), "A"))); b = EditDraftRegistry.Create(r => r.Register(Probe(typeof(EditDraftProbeB), "B"))); }
                else { b = EditDraftRegistry.Create(r => r.Register(Probe(typeof(EditDraftProbeB), "B"))); a = EditDraftRegistry.Create(r => r.Register(Probe(typeof(EditDraftProbeA), "A"))); }
                a.All.Select(p => p.PolicyId).Should().Equal("A");
                b.All.Select(p => p.PolicyId).Should().Equal("B");
                a.Find(typeof(EditDraftProbeB)).Should().BeNull();
                b.Find("EditDraftProbeA").Should().BeNull();
            }
            typeof(EditDraftRegistry).GetProperty("Default").Should().BeNull("owner D4: no static default registry");
        }

        [Test]
        public void E9_a_frozen_registry_refuses_registration_and_the_empty_registry_admits_nothing()
        {
            var r = EditDraftRegistry.Create(x => x.Register(Probe(typeof(EditDraftProbeA), "A")));
            r.IsFrozen.Should().BeTrue();
            FluentActions.Invoking(() => r.Register(Probe(typeof(EditDraftProbeB), "B"))).Should().Throw<InvalidOperationException>();
            r.All.Should().HaveCount(1);
            EditDraftRegistry.Empty.All.Should().BeEmpty();
            EditDraftRegistry.Empty.IsFrozen.Should().BeTrue();
            FluentActions.Invoking(() => EditDraftRegistry.Empty.Register(Probe(typeof(EditDraftProbeA), "A"))).Should().Throw<InvalidOperationException>();
            EditDraftServices.Registry(null).Should().BeSameAs(EditDraftRegistry.Empty, "a host that registered nothing admits nothing (fail closed)");
            EditDraftServices.Registry(new FixedServices()).Should().BeSameAs(EditDraftRegistry.Empty);
            new EditDraftRegistry().IsFrozen.Should().BeFalse("an unfrozen registry for tests and hosts that compose by hand");
        }

        [Test]
        public void E10_two_types_with_the_same_short_name_cannot_both_register()
        {
            var r = new EditDraftRegistry();
            r.Register(Probe(typeof(EditDraftProbeA), "A"));
            FluentActions.Invoking(() => r.Register(Probe(typeof(SameName.EditDraftProbeA), "A2")))
                .Should().Throw<InvalidOperationException>("the registry and the payload key on Type.Name (v1 contract)");
            r.Find("EditDraftProbeA").Type.Should().Be(typeof(EditDraftProbeA));
        }
    }

    [TestFixture]
    public class EditDraftLibrarySwitchTests
    {
        /// <summary>An IConfiguration over a mutable dictionary (re-read at every use).</summary>
        private static IConfigurationRoot Config(Dictionary<string, string> values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        [Test]
        public void E14_E15_the_keys_are_read_from_the_configured_section_and_re_read_at_every_use()
        {
            var values = new Dictionary<string, string>
            {
                ["EditDraftCapture:Enabled"] = "true", ["EditDraftCapture:Types:ToDo:Enabled"] = "true", ["EditDraftCapture:ListViews:Enabled"] = "true",
                ["Other:Enabled"] = "true", ["Other:Types:ToDo:Enabled"] = "false", ["Other:ListViews:Enabled"] = "false"
            };
            var config = Config(values);
            var defaults = new FixedServices().Add<IConfiguration>(config);
            var other = new FixedServices().Add<IConfiguration>(config).Add(new EditDraftSwitchOptions { Section = "Other" });

            EditDraftSwitch.IsEnabled(defaults, "ToDo").Should().BeTrue("no options = the default section EditDraftCapture");
            EditDraftSwitch.IsListEnabled(defaults, "ToDo").Should().BeTrue();
            EditDraftSwitch.IsGlobalEnabled(defaults).Should().BeTrue();
            EditDraftSwitch.IsEnabled(other, "ToDo").Should().BeFalse("the configured section decides");
            EditDraftSwitch.IsGlobalEnabled(other).Should().BeTrue();
            EditDraftSwitch.IsListEnabled(other, "ToDo").Should().BeFalse();

            config["EditDraftCapture:Types:ToDo:Enabled"] = "false";   // changed after the first read
            EditDraftSwitch.IsEnabled(defaults, "ToDo").Should().BeFalse("re-read at every use");
            config["EditDraftCapture:Types:ToDo:Enabled"] = "nonsense";
            EditDraftSwitch.IsEnabled(defaults, "ToDo").Should().BeFalse("unparsable = off");
            config["EditDraftCapture:Types:ToDo:Enabled"] = "true";
            EditDraftSwitch.IsEnabled(defaults, "ToDo").Should().BeTrue();

            EditDraftSwitch.IsEnabled(new FixedServices(), "ToDo").Should().BeFalse("no configuration = off");
            EditDraftSwitch.IsEnabled(null, "ToDo").Should().BeFalse();
            EditDraftSwitch.IsEnabled(defaults, null).Should().BeFalse();
        }
    }

    [TestFixture]
    [NonParallelizable]
    public class EditDraftLibraryLogAndTextTests
    {
        [Test]
        public void E16_the_Japanese_set_is_today_s_text_byte_for_byte()
        {
            var ja = EditDraftTextSet.Japanese;
            new[] { ja.StatusAlreadyApplied, ja.StatusClean, ja.StatusConflict, ja.StatusUnverifiable, ja.StatusUnavailable, ja.StatusNew, ja.SideEffectSuffix }
                .Should().Equal("反映済み", "戻せます", "他で変更されています", "変更前の値が不明です", "戻せません", "新規", "（他の記録も変わります）");
            new[] { ja.Empty, ja.Unknown, ja.Yes, ja.No, ja.DuplicateNote, ja.ProvenanceUnknown, ja.FromList, ja.FromDetail, ja.ProvenanceFormat, ja.ContextSeparator }
                .Should().Equal("（空）", "（不明）", "はい", "いいえ", "（新しい入力控に同じ項目があります）", "由来不明", "一覧から", "詳細から", "{0}（{1}）", "／");
            typeof(EditDraftTextSet).GetProperties().Where(p => p.PropertyType == typeof(string))
                .Should().OnlyContain(p => p.GetValue(EditDraftTextSet.English) != null && p.GetValue(EditDraftTextSet.Japanese) != null, "both built-in sets are complete");
        }

        [Test]
        public void E16_the_set_is_chosen_explicitly_and_the_thread_culture_never_switches_it()
        {
            var culture = Thread.CurrentThread.CurrentUICulture;
            var current = EditDraftTexts.Current;
            try
            {
                EditDraftTexts.Use(EditDraftLanguage.Japanese);
                Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                EditDraftComparison.StatusText(EditDraftItemStatus.Clean, false).Should().Be("戻せます");
                EditDraftDisplay.TextOf(true).Should().Be("はい");

                EditDraftTexts.Use(EditDraftLanguage.English);
                Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
                EditDraftComparison.StatusText(EditDraftItemStatus.Clean, false).Should().Be("Can be restored");
                EditDraftComparison.StatusText(EditDraftItemStatus.Clean, true).Should().Be("Can be restored (other records change too)");
                EditDraftDisplay.ChangeText(null, "x", false).Should().Be("(unknown) → x");
                EditDraftProvenance.Origin(true, "Notes").Should().Be("From the list (Notes)");
                EditDraftCaptureController.ContextTextFor("ToDo", new DateTime(2026, 9, 30)).Should().Be("ToDo / 2026/09/30");

                EditDraftTexts.Use((EditDraftTextSet)null);
                EditDraftTexts.Current.Should().BeSameAs(EditDraftTextSet.English, "null returns to the English default");
            }
            finally
            {
                EditDraftTexts.Use(current);
                Thread.CurrentThread.CurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
            }
            EditDraftCaptureController.ContextTextFor("メモ", new DateTime(2026, 9, 30)).Should().Be("メモ／2026/09/30", "the Japanese set's separator");
        }
    }

    [TestFixture]
    public class EditDraftLibraryClockAndCacheTests
    {
        [Test]
        public void E22_table_presence_is_cached_per_database_in_either_probe_order_and_a_failed_probe_is_absent()
        {
            foreach (var aFirst in new[] { true, false })
            {
                var cache = new EditDraftTableCache();
                var t0 = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
                var probes = new List<string>();
                bool Probe(string db, bool exists) { probes.Add(db); return exists; }
                if (aFirst)
                {
                    cache.Get("A", t0, () => Probe("A", true)).Should().BeTrue();
                    cache.Get("B", t0, () => Probe("B", false)).Should().BeFalse();
                }
                else
                {
                    cache.Get("B", t0, () => Probe("B", false)).Should().BeFalse();
                    cache.Get("A", t0, () => Probe("A", true)).Should().BeTrue();
                }
                cache.Get("A", t0.AddMinutes(4), () => Probe("A", false)).Should().BeTrue("A's answer is cached for A only");
                cache.Get("B", t0.AddMinutes(4), () => Probe("B", true)).Should().BeFalse("B's answer is cached for B only");
                probes.Should().HaveCount(2);
                cache.Get("A", t0.AddMinutes(5), () => Probe("A", false)).Should().BeFalse("five minutes later the probe runs again");
            }
            var c = new EditDraftTableCache();
            c.Get("X", DateTime.UtcNow, () => throw new InvalidOperationException()).Should().BeFalse("a failed probe counts as absent");
            c.Get(null, DateTime.UtcNow, () => true).Should().BeTrue("no database identity: probed, never cached");
            c.Get(null, DateTime.UtcNow, () => false).Should().BeFalse();
        }

        [Test]
        public void SEC_the_writer_without_a_registered_store_fails_closed_without_touching_a_database()
        {
            var writer = new EditDraftWriter(new FixedServices());
            writer.TableExists().Should().BeFalse();
            writer.Create(new EditDraftSeed { OwnerUserOid = Guid.NewGuid(), TargetOid = Guid.NewGuid() }, "{}", 0, DateTime.Now).Should().Be(Guid.Empty);
            writer.TrySupersede(Guid.NewGuid(), 1, Guid.NewGuid(), "{}", 0, "", DateTime.Now).Should().BeFalse();
            writer.ReadRowState(Guid.NewGuid(), 1, Guid.NewGuid(), DateTime.Now).Should().Be(EditDraftRowState.ReadFailed, "a failed read is never 'row gone'");
            writer.TryClaim(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), DateTime.Now).Should().Be(0);
            writer.DeleteOwn(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Should().Be(-1);
            writer.TrySoftDiscard(Guid.NewGuid(), Guid.NewGuid(), DateTime.Now).Should().BeFalse();
            using (var os = writer.CreateReadSpace(out var scope))
            using (scope)
            {
                writer.ReadOwn(os, Guid.NewGuid(), Guid.NewGuid()).Should().BeNull();
                writer.ListOwn(os, Guid.NewGuid(), null, true, DateTime.Now, out _).Should().BeEmpty();
                writer.ListOwnTargets(os, Guid.NewGuid(), "ToDo", DateTime.Now, out _).Should().BeEmpty();
            }
        }

        [Test]
        public void SEC1_SEC2_the_library_defaults_fail_closed_without_a_login_and_hosts_override_them()
        {
            // No XAF security in this process: SecuritySystem.CurrentUserId is not a Guid -> no owner.
            XafLoginEditDraftOwnerResolver.Instance.Current((DevExpress.ExpressApp.IObjectSpace)null, null).IsNone.Should().BeTrue();
            XafLoginEditDraftOwnerResolver.Instance.Current((DevExpress.ExpressApp.XafApplication)null, null).IsNone.Should().BeTrue();
            EditDraftServices.Owner(null).Should().BeSameAs(XafLoginEditDraftOwnerResolver.Instance);
            EditDraftServices.CurrentOwner(new FixedServices().Add<IEditDraftOwnerResolver>(new ThrowingOwner()), (DevExpress.ExpressApp.IObjectSpace)null, null).IsNone
                .Should().BeTrue("an exception inside a resolver is no owner");
            EditDraftServices.CurrentOwner(new FixedServices().Add<IEditDraftOwnerResolver>(new ThrowingOwner()), (DevExpress.ExpressApp.XafApplication)null, null).IsNone
                .Should().BeTrue("the same for the application overload");
            var me = new EditDraftOwnerInfo(Guid.NewGuid());
            EditDraftServices.CurrentOwner(new FixedServices().Add<IEditDraftOwnerResolver>(new FixedOwner(me)), (DevExpress.ExpressApp.IObjectSpace)null, null).Should().Be(me);

            // 0.4.0-preview.1: the record-access seam is gone; with no host check registered, XAF security alone decides, and a
            // missing argument is a refusal.
            EditDraftServices.AccessCheck(null).Should().BeNull();
            EditDraftServices.MayRestore(null, null, new object()).Should().BeFalse("a missing argument is a refusal");
        }

        private sealed class ThrowingOwner : IEditDraftOwnerResolver
        {
            public EditDraftOwnerInfo Current(DevExpress.ExpressApp.IObjectSpace objectSpace, EditDraftTypePolicy policy) => throw new InvalidOperationException();
            public EditDraftOwnerInfo Current(DevExpress.ExpressApp.XafApplication application, EditDraftTypePolicy policy) => throw new InvalidOperationException();
        }

        private sealed class FixedOwner : IEditDraftOwnerResolver
        {
            private readonly EditDraftOwnerInfo _owner;
            public FixedOwner(EditDraftOwnerInfo owner) { _owner = owner; }
            public EditDraftOwnerInfo Current(DevExpress.ExpressApp.IObjectSpace objectSpace, EditDraftTypePolicy policy) => _owner;
            public EditDraftOwnerInfo Current(DevExpress.ExpressApp.XafApplication application, EditDraftTypePolicy policy) => _owner;
        }
    }
}

namespace Xaf.EditDraft.Tests.SameName
{
    /// <summary>A second CLR type named EditDraftProbeA (E10: the registry keys on the short name).</summary>
    public class EditDraftProbeA { }
}
