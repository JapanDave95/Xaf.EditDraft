using NUnit.Framework;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests;

/// <summary>
/// Assembly-wide one-time setup of the library tests (milestone M3). Most tests expect the Japanese text set, the choice
/// a host makes once at startup (EditDraftTexts.Use); this harness makes that one choice. Tests that switch the set restore it.
/// The log sink stays the library default (TraceEditDraftLog): no application logger is referenced here.
/// </summary>
[SetUpFixture]
public class TestEnvironment
{
    [OneTimeSetUp]
    public void GlobalSetup()
    {
        EditDraftTexts.Use(EditDraftLanguage.Japanese);
    }
}
