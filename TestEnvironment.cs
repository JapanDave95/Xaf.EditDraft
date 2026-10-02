using NUnit.Framework;
using Xaf.EditDraft.Core;

namespace Xaf.EditDraft.Tests;

/// <summary>
/// Assembly-wide one-time setup of the library tests (milestone M3). The tests moved from
/// NursingHome_Chart.Rostering.Tests expect the Japanese texts the application chooses at startup (EditDraftTexts.Use);
/// this harness makes the same one choice, so their expectations are unchanged. Tests that switch the set restore it.
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
