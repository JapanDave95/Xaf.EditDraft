using NUnit.Framework;

namespace Xaf.EditDraft.Sample.Tests;

/// <summary>
/// Close the library gaps (run 2026-10-04-editdraft-close-gaps-08c338): the sample uses the library's own helpers instead of
/// its private copies, and its README points to the library's consumer guide. Expectation ids Tn are from the Codex
/// requirement-only list of that run (tests a1). Source checks, run red on the unchanged main first.
/// </summary>
[TestFixture]
public class SampleCloseGapsTests
{
    [Test]
    public void S1_G2_the_updater_denies_the_store_through_the_library_helper()
    {
        var updater = Read("Xaf.EditDraft.Sample.Module", "DatabaseUpdate", "Updater.cs");
        Assert.That(updater, Does.Contain("EditDraftSecurity.DenyStoreToAllRoles(objectSpace, typeof(SampleEditDraft))"));
        Assert.That(updater, Does.Not.Contain("AddTypePermission<SampleEditDraft>"), "no private copy of the deny");
    }

    [Test]
    public void S2_G10_T40_the_policy_uses_the_library_decision_helpers()
    {
        var policy = Read("Xaf.EditDraft.Sample.Module", "EditDrafts", "NoteEditDraftPolicy.cs");
        Assert.That(policy, Does.Contain("EditDraftDecisions.Restorable("));
        Assert.That(policy, Does.Not.Contain("new(member, EditDraftDisposition.Restorable"), "no private helper");
    }

    [Test]
    public void S3_G14_the_sample_README_points_to_the_consumer_guide()
    {
        var readme = Read("README.md");
        Assert.That(readme, Does.Contain("docs/consumer-guide.md"));
        Assert.That(readme, Does.Not.Contain("the store table must be in `dbo`"), "G6: the schema is an option now");
    }

    private static string Read(params string[] parts)
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(new[] { dir.FullName, "samples", "Xaf.EditDraft.Sample" }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        Assert.Fail(string.Join("/", parts) + " not found above " + TestContext.CurrentContext.TestDirectory);
        return null;
    }
}
