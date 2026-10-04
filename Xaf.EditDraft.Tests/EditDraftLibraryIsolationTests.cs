using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests
{
    // Xaf.EditDraft library, milestone M1 (run 2026-10-01-editdraft-m1-c3f4de; design docs/xaf-editdraft-library-design-2026-10-01.md
    // §4.7). Expectations from the Codex requirement-only list of this run (tests a1): E5, E6, E7, E19, E20.
    // The library must compile and be exercised as its OWN assembly, with no reference to the application.
    // Library milestone M3 (run 2026-10-02-editdraft-m3-1b4d82): E5, E6 and the module test E8 moved here from
    // NursingHome_Chart.Rostering.Tests (EditDraftLibraryIsolationTests.cs) with only the namespace changed, except E6's
    // InternalsVisibleTo list, which names this test project too (it exercises the internal writer). E7, the host
    // registration test E8 and E19_E20 stay in that project (they use the application's types and files).

    [TestFixture]
    public class EditDraftLibraryIsolationTests
    {
        private static readonly Assembly Core = typeof(EditDraftCoreModule).Assembly;

        [Test]
        public void E5_the_Core_assembly_references_no_application_Llamachant_or_Blazor_assembly()
        {
            Core.GetName().Name.Should().Be("Xaf.EditDraft.Core");
            var referenced = Core.GetReferencedAssemblies().Select(a => a.Name).ToList();
            referenced.Should().NotBeEmpty();
            foreach (var name in referenced)
            {
                name.Should().NotStartWith("NursingHome_Chart", "Core never references the application Module");
                name.Should().NotStartWith("CareCrew", "Core never references the Blazor/Win hosts");
                name.Should().NotStartWith("Progress", "Core never references Progress.Common");
                name.Should().NotStartWith("Llamachant", "Core never references Llamachant");
                name.Should().NotStartWith("DevExpress.ExpressApp.Blazor", "Core is platform-agnostic");
                name.Should().NotStartWith("CareTree", "Core never references the CareTree client");
            }
        }

        [Test]
        public void E6_the_Core_project_has_no_project_reference_and_no_linked_source_and_names_no_application_assembly()
        {
            var csproj = File.ReadAllText(Path.Combine(Wave1.Root(), "Xaf.EditDraft.Core", "Xaf.EditDraft.Core.csproj"));
            csproj.Should().NotContain("<ProjectReference").And.NotContain("<Compile Include").And.NotContain("Link=");
            Regex.Matches(csproj, "<PackageReference Include=\"([^\"]+)\"").Select(m => m.Groups[1].Value)
                .Should().BeEquivalentTo(new[] { "DevExpress.ExpressApp", "DevExpress.ExpressApp.Xpo", "DevExpress.Persistent.Base", "DevExpress.Persistent.BaseImpl.Xpo", "Newtonsoft.Json" },
                    "design §3: the brief's list plus Persistent.Base, BaseImpl.Xpo (BaseObject) and Newtonsoft.Json, all already pinned centrally");
            // Owner decision O-3 (2026-10-02, "writer goes internal in M2"): the writer is internal, visible to the library's
            // Blazor part and to this test project, which exercises it; still no application (CareCrew.*) assembly is granted
            // the internals. Expectation changed by gap G13 (run 2026-10-04-editdraft-close-gaps-08c338): the first host's test
            // project NursingHome_Chart.Rostering.Tests is no longer a friend.
            Core.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName)
                .Should().BeEquivalentTo(new[] { "Xaf.EditDraft.Blazor", "Xaf.EditDraft.Tests" })
                .And.NotContain(n => n.StartsWith("CareCrew"), "no application assembly is granted the library's internals");
        }

        [Test]
        public void E8_the_Core_module_exports_no_business_class_and_the_base_is_not_persistent()
        {
            var exported = (System.Collections.Generic.IEnumerable<Type>)typeof(EditDraftCoreModule)
                .GetMethod("GetDeclaredExportedTypes", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(new EditDraftCoreModule(), null);
            exported.Should().BeEmpty("only the consumer's subclass maps to a table");
            var controllers = ((System.Collections.Generic.IEnumerable<Type>)typeof(DevExpress.ExpressApp.ModuleBase)
                .GetMethod("GetDeclaredControllerTypes", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(new EditDraftCoreModule(), null)).ToList();
            controllers.Should().ContainSingle(t => t == typeof(EditDraftCaptureController), "XAF collects the module's controllers from its assembly (one class, moved not copied)");
            controllers.Should().OnlyContain(t => t.Assembly == Core);
            typeof(EditDraftStoreBase).IsAbstract.Should().BeTrue();
            typeof(EditDraftStoreBase).GetCustomAttributes(typeof(DevExpress.Xpo.NonPersistentAttribute), false).Should().NotBeEmpty();
        }
    }
}
