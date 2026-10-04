# Xaf.EditDraft library — milestone M3 (label editor, row icon, model captions, Xaf.EditDraft.Tests)

Run `2026-10-02-editdraft-m3-1b4d82` (collaborator: Claude Opus 5.5 implements; Codex gpt-6-astra at xhigh reviews, read-only).
Branch `feature/edit-draft-library`, base `34491d1bdd535b1f6abf69cd554c4fc82ac05253`
(M1: 1fe4865 / 61f8d91 / 40405d1; M2: ed1c8b9 / 07d56ee / 34491d1). Everything below is UNCOMMITTED. No commit, no deploy, no database, no
schema change, no NursingHome_Chart.Module change, no NHM change.

> **Status note, 2026-10-02 (main session, after this run):** committed as a2b6b25 (feat) / 9fc8222 (test) / ef84151 (docs)
> and merged into master as dd0e79d, pushed. Owner rulings after the run: O-11 "Keep for the merge; fail closed before NuGet";
> E6_E7_D9 alias expectation, InternalsVisibleTo Xaf.EditDraft.Tests and the omitted Google.OrTools confirmed. The §10
> browser items 1–3 were run on Dev2 (host from this build): label editor (lead on two lines, provenance as
> plain caption text, no box or grip), folder icon only on the badged ToDo row, Japanese popup column captions from the
> generator updaters, chart offer still working — all pass. Codex C2 (updaters not exercised offline) is closed by that run.
> Not deployed. KB: fix-537.

Owner rulings applied (labels verbatim): O-7 "Build a small library label editor (Recommended)" — "A read-only caption-style editor in
Xaf.EditDraft.Blazor so both popups look like the chart one. Small, Blazor-only, part of M3."; Next "M3 now (tests into Xaf.EditDraft.Tests,
localisation, label editor, icon-on-badged-rows), then merge"; earlier: O-4 texts default en, CareCrew ja (keep); D6 mechanical test edits
allowed, expectations unchanged; two-pass cap; red test → one solo rerun, both reported, never revised.

## 0. Combined answer

M3 is built and every test identity of the baseline is kept. The generic popups' text lines (lead, provenance, conflict) now use the
library's own read-only caption-style editor, `EditDraftLabelEditor` under the alias `Xaf.EditDraft.Label`: one plain text div with the same
DevExpress caption classes as the chart popup, no box or border, wrapping, and line breaks kept. The row icon 「入力控を開く」 now shows only on
rows in the badge set, through XAF 26.1's per-row `ListEditorInlineActionControl.CustomizeInlineActionButton`; the toolbar button is unchanged.
The library's model captions are now part of its ja/en text set and are written into the generated application model by generator
updaters, so CareCrew (Japanese set) keeps today's captions with no CareCrew Model.xafml line. 94 test identities (71 methods) moved from
Rostering.Tests to the new `Xaf.EditDraft.Tests`, which references only the two libraries; 20 new tests cover the M3 work. Full runs: 0
missing, 0 changed, 0 new skips, golden byte-equal. Codex's review found two defects, both confirmed, neither changed after the review: (C1)
the per-row enable override can switch on an icon that XAF disabled for that row alone — the owner decides (O-11); (C2) the caption tests do
not run the updaters inside real model generation — an offline attempt produced an empty model, so the browser pass is the check. Nothing
was run in a browser; the screen look, the per-row icon and the captions on screen are §10.

## 1. Status

Implemented, uncommitted, 2026-10-02. Builds green: Core, Blazor library, Xaf.EditDraft.Tests, CareCrew.Blazor.Server (2209 unique
warnings = baseline set, 0 differences), CareCrew.Win. Tests: §6. Codex: `tests` before any code, one `diffreview` (2 findings). Owner
decisions in §12. Next: owner decisions and the main session's browser pass (§10), then git-committer on `feature/edit-draft-library`, then the
merge the owner named.

## 2. What changed

Paths: `C/` = `Xaf.EditDraft.Core/`, `B/` = `Xaf.EditDraft.Blazor/`, `T/` = `Xaf.EditDraft.Tests/`, `R/` = `NursingHome_Chart.Rostering.Tests/`.
Every changed or new file is UTF-8 without BOM and CRLF (checked: 0 lone LF), except `CareCrew.sln`, which keeps its BOM.

| File | Change |
|---|---|
| B/EditDraftLabelEditor.cs (new) | the label editor, its component model and component (§3) |
| B/EditDraftRowOpenRule.cs (new) | the per-row icon rule (§4) |
| B/EditDraftPopupCaptions.cs (new) | the 20 popup/list captions and two generator updaters (§5) |
| B/EditDraftListBadgeControllerBlazor.cs | subscribes `OpenAction.CustomizeControl`; per-row handler; class comment |
| B/EditDraftModels.cs | English captions; `[EditorAlias(EditDraftLabelEditor.Alias)]` on the six text lines; class comment |
| B/EditDraftRestoreItem.cs | English captions; class comment |
| B/EditDraftBlazorModule.cs | `AddGeneratorUpdaters` (class + member updaters); class comment |
| C/EditDraftModelCaptions.cs (new) | caption record, store table, `Find` / `ApplyToClasses` / `ApplyToMembers`, `EditDraftStoreCaptionUpdater` |
| C/EditDraftTexts.cs | 23 caption texts in both sets; class comment |
| C/EditDraftStoreBase.cs | three member captions English; class comment |
| C/EditDraftCoreModule.cs | `AddGeneratorUpdaters` (store updater) |
| C/Xaf.EditDraft.Core.csproj | `InternalsVisibleTo Xaf.EditDraft.Tests` (beside Blazor and Rostering.Tests) |
| T/ (new project, 9 files) | csproj, `TestEnvironment` (Japanese set, as CareCrew), 6 files of moved tests, `EditDraftLibraryM3Tests.cs` (20 new) |
| R/ 7 files | moved tests removed (6 files), mechanism updates (E8_D9, E17_O3, store mapping), one header line each |
| CareCrew.sln | `Xaf.EditDraft.Tests` project + its 18 configuration lines |
| docs/xaf-editdraft-library-m2-2026-10-02.md | dated commit note (brief item 5) |

Untouched: NursingHome_Chart.Module (0 changes), NHM, every CareCrew.Blazor.Server file (the chart popup keeps the Llamachant alias —
Module class), CareCrew Model.xafml, appsettings*, the writer, the owner and record-access seams.

### 2a. Tests moved to Xaf.EditDraft.Tests (namespace NursingHome_Chart.Rostering.Tests -> Xaf.EditDraft.Tests; bodies byte-identical unless marked)

| From (Rostering.Tests file) | Class | Tests moved | Stayed in Rostering.Tests (why) |
|---|---|---|---|
| EditDraftEngineTests.cs | EditDraftRegistryTests | M1_a_matching_shape…, A_type_is_registered_once, M2_M3_E2… (3) | M1_E1 (one line reads CareCrewRegistry.Default), M3 (TenantChartDraftPolicy), M4, M4_M60, Codex_C1, Every_concrete_class…, InitOrder_registry_first, InitOrder_wrapper_first (CareCrew registry / chart types) |
| | EditDraftPayloadTextTests | M50_E6 (1) | M10 x2 (TenantChartDraftPayload, CareCrew) |
| | EditDraftMultiGroupTests | all 15 | — |
| | EditDraftRestoreGuardTests | all 8 | — |
| | (helpers) | EditDraftProbeA/B/ASub, ProbeSpace copied | kept in Rostering too (M1_E1 uses them) |
| EditDraftLibrarySeamTests.cs | EditDraftLibraryRegistryTests | E9 x2, E10 (3) | E11 x2 (CareCrew composition) |
| | EditDraftLibrarySwitchTests | E14_E15 (1) | E14_SEC6 x15 cases (CareTreeDraftCaptureSwitch, Module) |
| | EditDraftLibraryLogAndTextTests | E16 x2 (2) | E12_E13 (GlobalLoggerEditDraftLog, CareCrew) |
| | EditDraftLibraryRowMappingTests | — | E17_E18 (CareCrew row mapping + Module row type) |
| | EditDraftLibraryClockAndCacheTests | E22, SEC, SEC1_SEC2 (3) | E21_now (Module EditDraft.RetentionDays), E21_D1 (Wave1 space on CareCrew types) |
| | (helpers) | FixedServices, Probe, Config, ThrowingOwner, FixedOwner, SameName.EditDraftProbeA moved (no remaining Rostering test uses them) | — |
| EditDraftLibraryIsolationTests.cs | EditDraftLibraryIsolationTests | E5, E6 (IVT list edited, see 2c), E8_the_Core_module… (3) | E7 (Module EditDraft), E8_CareCrew… (Startup), E19_E20 (AttendanceDraftSlot, CareCrew) |
| EditDraftLibraryBlazorTests.cs | EditDraftLibraryBlazorTests | E1_E2, E2, E4 (module), E12, E6_E7_D9 (edited, see 2c), E8 (row mapping), E19, E22 (8) | E3, E4 (CareCrew registration), E8_D9 (edited, see 2c), E23, E14_O2, E17_O3 (edited, see 2c), E24 |
| EditDraftWave1Tests.cs | EditDraftWave1GatingTests | W7 (9 cases), W7_the_per_type_key…, W10_D6…, W20b… (4 methods) | W9 (CareCrew registry), W43_W46 (Module EditDraft) |
| | EditDraftWave1OfferMergeTests | all 4 | — |
| | EditDraftWave1WiringScanTests | W38b, W58_W66 (2) | W37, W40, W28, W32, W8, W41, W12, W6 (CareCrew / Module / NHM) |
| EditDraftWave1bTests.cs | EditDraftWave1bAdmissionTests | E1_E4 (8 cases), E1_E5 (2 methods) | E1, E4, E23 (CareCrew policies, model files) |
| | EditDraftWave1bSwitchTests | E2 (9 cases), E2_one_global…, E3 (3 methods) | B7 (CareCrew appsettings) |
| | EditDraftWave1bWiringScanTests | E10b, C2, E13b_E14_E31, C6, C3_C4, E15, E16_E17 (7) | C1, E29 (StaffOverTimeHoliday space) |
| | EditDraftWave1bBadgeAndProvenanceTests | E24_E26, E21 (edited, see 2c) (2) | E25 (reads CareCrew Startup.cs) |
| | EditDraftWave1bRowContextTests, BrowserPassB1Tests | — | all (StaffOverTimeHoliday space) |

## 3. Label editor (O-7)

New file `Xaf.EditDraft.Blazor/EditDraftLabelEditor.cs` (written for the library; Llamachant's decompiled output was read only for the DOM
shape of the chart popup's lines, from M2's decompiled notes):

| Part | What it is |
|---|---|
| `EditDraftLabelEditor : BlazorPropertyEditorBase` | `[PropertyEditor(typeof(object), "Xaf.EditDraft.Label", false)]` — an alias registration for any member type, never a default editor. `CreateComponentModel` returns the model; `ReadValueCore` puts the display text into it; `GetControlValueCore` returns the member's own value (display only, nothing is written back). |
| `EditDraftLabelModel : ComponentModelBase` | one property, `Text`; `ComponentType` = `EditDraftLabel` (the XAF 26.1 pattern, docs 405922). |
| `EditDraftLabel : ComponentBase` | renders `<div class="xaf-editdraft-label dxbl-fl-cpt dxbl-text" style="white-space: pre-line; overflow-wrap: anywhere; padding-left: 4px; padding-right: 4px;">text</div>` — the same DevExpress caption classes and side padding as the chart popup's lines (`ImageContainer dxbl-fl-cpt dxbl-text` + `<span>`); text node only (Blazor encodes it); no input, textarea, tabindex or border; `pre-line` keeps line breaks (the old chart editor collapsed them; the brief asks for them) and wraps long lines; `TextOf` turns CR LF / CR into LF, null into "", a non-string value through the member's display format. Unmatched attributes XAF puts on the component model (title, aria-*) are rendered on the div before the library's class and style. |

Registration: XAF collects `[PropertyEditor]` classes from each module's own assembly (decompiled `DevExpress.ExpressApp.v26.1.dll`
`ModuleBase.RegisterEditorDescriptors` → `PropertyEditorDescriptorCollector.Collect(GetType().Assembly)`), so the Blazor module registers it.
`typeof(object)` follows KB rule-051/fix-099 (a string-typed alias registration is the one case the KB records as unreliable).

Used by: `EditDraftRestorePlan.Lead / Provenance / ConflictBanner`, `EditDraftReadOnlyView.Lead / Provenance`, `EditDraftList.Lead` —
`[EditorAlias(EditDraftLabelEditor.Alias)]` added beside the existing empty caption, `AllowEdit=False` and `RowCount` (kept: a host can switch a
line back to XAF's memo with `PropertyEditorType` in its model). The 14-row typed-text memo of the read-only view is unchanged. The empty conflict
line keeps its conditional-appearance Hide rule (it hides the layout item, independent of the editor).

## 4. Icon only on badged rows (finding b)

Mechanism (DevExpress 26.1, decompiled `DevExpress.ExpressApp.Blazor.v26.1.dll`):
- `InlineRowActionController.OnViewControlsCreated` raises `ActionBase.CustomizeControl` for each inline action control
  (`ListEditorInlineActionControl`).
- `InlineActionButton.OnParametersSetAsync` builds a `CustomizeInlineActionButtonEventArgs` per data row (`DataItem` = the row; `Visible`,
  `Enabled`, `Tooltip`, `CssClass` …), raises the control's `CustomizeInlineActionButton`, and renders nothing when `Visible` comes back false
  (`BuildRenderTree`: `if (!Visible) return;`).
- `InlineRowActionController.Container_SetupInlineActionButton` runs first for the row: it ANDs the action's Enabled reasons except the
  context keys ("ByContext_RequireSingleObject", "By Criteria", "ByAppearance") — so the action's own "selected row has a draft" reason
  greys every row's icon when an unbadged row is selected.
- The toolbar button is a different control (`DxToolbarItemSimpleActionControl`), so a per-row rule never touches it.

Change (`Xaf.EditDraft.Blazor/EditDraftListBadgeControllerBlazor.cs`): the constructor subscribes `OpenAction.CustomizeControl`; for a
`ListEditorInlineActionControl` it subscribes `CustomizeInlineActionButton` once (unsubscribe, subscribe); the row handler computes
`badged = _policy != null && _set.Count > 0 && _set.Contains(KeyOf(e.DataItem))` and calls `EditDraftRowOpenRule.Apply(e, badged,
OpenAction.Enabled, RowKey)` (new file `EditDraftRowOpenRule.cs`): an unbadged row gets `Visible = false`; a badged row whose icon is off only
because of the selected-row reason gets `Enabled = true` (clicking the icon selects that row first — `ListEditorInlineActionControl.
CommandActionButtonModel_Click` calls the select function before executing); any other false reason keeps it off. `UpdateActionState` (toolbar:
shown while the screen has badged rows, enabled for a selected row with a draft) and the click-time fresh read (`ReloadSet("開く")`) are
unchanged. A row that cannot be keyed yields `Guid.Empty`, which the badge set never holds. The set changes already re-render the grid
(`Rerender` → `ComponentInstance.Reload()`), which re-runs the per-row customisation.

## 5. Localisation (model captions)

Mechanism: the text set (owner decision O-4), applied to the application model by XAF generator updaters — no xafml, no CareCrew model line.
- `EditDraftTextSet` gains 23 caption texts (`Caption…`, both built-in sets complete).
- The library classes declare the ENGLISH captions as attributes (the default set): `[XafDisplayName("Unsaved input")]` …, store base
  `[ModelDefault("Caption", "Owner" | "Record type" | "Record")]`.
- `Xaf.EditDraft.Core/EditDraftModelCaptions.cs`: `EditDraftModelCaption(Type, Member, Func<EditDraftTextSet,string>, AndDerived)`, the store
  table (3 member captions, applied on the host's store subclass), `Find`, `ApplyToClasses`, `ApplyToMembers`, and
  `EditDraftStoreCaptionUpdater : ModelNodesGeneratorUpdater<ModelBOModelMemberNodesGenerator>` (registered by `EditDraftCoreModule.AddGeneratorUpdaters`).
- `Xaf.EditDraft.Blazor/EditDraftPopupCaptions.cs`: the popup/list table (20 captions) and `EditDraftPopupClassCaptionUpdater`
  (`ModelBOModelClassNodesGenerator`) + `EditDraftPopupMemberCaptionUpdater` (`ModelBOModelMemberNodesGenerator`), registered by
  `EditDraftBlazorModule.AddGeneratorUpdaters` (precedent in this repo: `NursingHome_Chart.Module/ModelGenerators/BooleanImageDefaultNodesGenerator.cs`).
- When the application model is built, the updaters write the caption of the set in use (CareCrew: Japanese, chosen in
  `CareCrewEditDraftSetup.ApplyStatics` during ConfigureServices, before any model exists) into the generated layer. Docs 404125 (26.1):
  generator updaters work at the Application Model zero layer, so a host's Model.xafml or a user's stored differences still win.
  The application model language (`PreferredLanguage = "ja-JP"`, Languages `ja-JP;en-US;`) is not consulted, nor the thread culture.
- Why not the design's xafml aspect (`Model.DesignedDiffs.Localization.ja.xafml` in the library): it follows the XAF model language, not the
  host's text-set choice (a host could get Japanese texts with English captions or the reverse), and whether CareCrew's `ja-JP` selects a
  library `ja` satellite cannot be shown offline; the design's fallback for that case is a CareCrew Model.xafml line, which the brief forbids.
- View ids, member names, class names and the store mapping are unchanged (tests C46, EditDraftStoreMappingTests).

## 6. Tests

### 6a. Codex expectations first
The `tests` call ran in a requirement-only directory (one file, `REQUIREMENT.md`: the brief verbatim + runtime facts F1–F9, no source)
before any code was written: 47 expectations C1–C47 and a could_not_determine list. The new file `Xaf.EditDraft.Tests/EditDraftLibraryM3Tests.cs`
(20 tests) implements the offline-checkable ones (labels in the test names: C1, C2/C3, C5/C6/C9, C9/C10, C8, C11, C12, C13–C16, C23, C25–C27,
C31, C41–C46); the browser ones (C5–C8, C10–C12, C14, C16, C20, C24, C47) are §10; C34–C40 are the gate below. Divergences from Codex's list,
stated: C25 asks for the identical package list including Google.OrTools — the new project uses the subset it needs, each at the version
Rostering.Tests pins (no new package); C43 asks for application-model-language combinations — offline only the text-set and thread-culture
independence is checked (C43 test), the model-language part needs a host (§10 C).

### 6b. Identity comparison (identity = class + test name incl. TestCase arguments, case-sensitive, multiplicity kept)
Baseline on the unchanged tree (HEAD 34491d1, clean) before any edit: Rostering.Tests full suite. Candidate: Rostering.Tests full suite +
Xaf.EditDraft.Tests, on the reviewed bytes. The only reconciliation: each Xaf.EditDraft.Tests identity is compared under the namespace its class
had in Rostering.Tests (`Xaf.EditDraft.Tests.` → `NursingHome_Chart.Rostering.Tests.`); every mapped identity is listed in
the comparison report ("moved" section). No short class name was merged across namespaces (the mapping rewrites only the new project's prefix).

| Run | Baseline (HEAD, clean) | Candidate | Missing | Changed outcome | Duplicated | Newly skipped | New |
|---|---|---|---|---|---|---|---|
| Rostering.Tests full | 5557: Passed 5540, NotExecuted 11, Failed 6 (37 m 19 s) | 5463: Passed 5446, NotExecuted 11, Failed 6 (29 m 4 s) | — | — | — | — | — |
| Xaf.EditDraft.Tests | — (did not exist) | 114: Passed 114 | — | — | — | — | — |
| Union (library identities under their old namespace) | 5557 | 5577: Passed 5560, NotExecuted 11, Failed 6 | **0** | **0** | **0** | **0** | 20 (all Passed, all in Xaf.EditDraft.Tests) |

Moved: 94 identities (71 methods; TestCase sets W7 x9, E1_E4 x8, E2 x9). Stayed in Rostering.Tests: 5463. `dotnet test` summaries: baseline
"Total 5552, Failed 6, Passed 5540, Skipped 6"; candidate Rostering "Total 5458, Failed 6, Passed 5446, Skipped 6", library "Total 114, Passed
114" (the summary leaves out 5 NotExecuted TRX results, as in M1/M2). Not passed in both runs, unchanged: 4 x AuditTrailExclusionWiringTests
(repository root in a worktree), RosterTelemetryRecorderTests.MultiDepartment_CompletedGeneration_PersistsOneRecord_WithColumnsAndParseablePayload,
CpSatIncompatibilityTests.FullMonthSolve_NeverCoSchedulesAForbiddenPair (roster solver, not in M2's list; failed in both runs here), and the 11
NotExecuted (Explicit/diagnostic). The comparison report has missing / changed / duplicated / skipped / new / moved lists.

### 6c. Test edits that are not pure moves (owner to confirm; Claude's classification)
| Test (project) | Edit | Class |
|---|---|---|
| E6_E7_D9 (moved) | "no property names an editor alias" now excepts the six text lines, which must name exactly `EditDraftLabelEditor.Alias`; the RowCount and read-only checks unchanged; name kept for the identity gate | expectation change caused by owner ruling O-7 (D9's "no alias" reversed) |
| E6 (moved), E17_O3 (stays) | pinned InternalsVisibleTo set gains `Xaf.EditDraft.Tests` (the moved SEC test constructs the internal writer); "no CareCrew assembly" unchanged | consequence of the move (not one of D6's listed categories) |
| E8_D9 (stays), E21 (moved), EditDraftStoreMappingTests E1_E4 (stays) | the caption is read through the new mechanism (the Japanese set's caption for the library type) instead of the attribute; expected strings unchanged | brief item 4 ("checks updated to the new mechanism, expectations = today's strings") |
| 6 Rostering files | one header comment line each naming the move | comment |

Moved test bodies: 58 extracted blocks (methods, classes, helpers) are byte-identical in the new files (checked by exact substring), and none of
the moved methods remains in Rostering.Tests (checked the same way); the 4 blocks not byte-identical are the three edits above and the `SameName`
namespace line.

### 6d. Red during development (owner rule: one solo rerun, both reported, never revised)
New test C8 was red in the first library run and in its solo rerun (both saved as TRX): the component rendered the CR LF it
was given (the editor normalised only in `ReadValueCore`). The test was not changed; the component now normalises CR LF / CR itself
(`EditDraftLabel.BuildRenderTree` → `TextOf`) — an implementation change made in response to a red test, reported here as M2 reported its E6 case.
C25_C31 was red in the same first run because the solution entry had not been added yet; it was added afterwards, test unchanged.

### 6e. Regression sensitivity
Three mutations at once (row rule without `e.Visible = false`; the Origin caption entry pointing at another text; the label style without
`white-space: pre-line`) → 8 library tests red: C5, C8 (label), C41, C42, C43, E21 (captions), C13, C16 (icon). Restored; the three files'
SHA-256 equal before and after.

### 6f. Golden
`NursingHome_Chart.Rostering.Tests/Golden/TenantChartDraft.golden.txt` SHA-256 `72325EE15DD4E28A2AA4C1C19B1FCA84401E7F459332FB22B42C1C08358C3144`,
unchanged (git: not modified); its tests stay in Rostering.Tests.

## 7. Builds (all `--artifacts-path artifacts/claude-test/20261002-1b4d82`)
- Baseline (clean HEAD): Core exit 0 / 0 warnings; Blazor library exit 0 / 0; CareCrew.Blazor.Server exit 0 / 2209; CareCrew.Win exit 0 / 1.
- Candidate: Core exit 0 / 0; Blazor library exit 0 / 0; Xaf.EditDraft.Tests exit 0 / 0; Rostering.Tests exit 0; CareCrew.Win exit 0 / 1;
  CareCrew.Blazor.Server `--no-incremental` exit 0 / 2209 unique warnings — the same set as the baseline (0 differences), none in an Xaf.EditDraft file.
- Not run: `dotnet publish`; a host; a browser. The dev host on 5004 in this worktree was not touched (its folder was not built into).

## 8. Codex review and post-review edits

`diffreview` a1 (pack v1, 474 KB; candidate frozen, 29 files, unchanged during the review; validation ok). The full Rostering run was still
running during the review (Codex noted it as unavailable); the quick runs, the sensitivity check and the library run were in the pack.

| Codex | Finding | Claude's check | Outcome |
|---|---|---|---|
| C1 | `EditDraftRowOpenRule.Apply` (line 24) turns a badged row's icon on when the action's only false reason is the selected-row reason; XAF's per-row pass (`InlineRowActionController.Container_SetupInlineActionButton`) also ANDs row-specific inputs (`BoundItemCreatingEventArgs.Enabled`, `TargetObjectsCriteria`) that the override cannot see, so it can enable an icon XAF disabled for that row | Confirmed in the decompiled 26.1.4 source (pack §6). Reachability in CareCrew today: no `BoundItemCreating` handler and no `TargetObjectsCriteria` for `EditDraftRowOpen` in any .cs or .xafml of the repository (grep), so not reachable now; reachable for a future host that adds either. Not a data or record-access path (the click re-reads the store and re-checks access) | Not changed; owner decision O-11 (§12) |
| C2 | The caption tests call the helpers on mocks and check registration; they never run the three `UpdateNode` methods inside XAF model generation, nor a higher-layer override; empty `UpdateNode` bodies would still pass | Confirmed. Decisive check attempted after the review: a real model via XAF 26.1 docs 405947 "Approach 2" (XafApplication mock + module list + `ExpressApplicationSetupParameters`) with a probe module exporting the popup classes and registering the library updaters, first with a mocked entity store, then with real XPO + non-persistent object space providers — both produced an empty BOModel (0 classes); abandoned, the file removed | Open: the browser pass (§10 C14–C16) is the check; a real model-generation test is a follow-up (needs an XAF application setup that populates the BOModel offline) |

Codex also stated: the S5 test-edit classifications match its reading (alias exception = O-7 change; friend-list additions = relocation;
caption lookups = mechanism changes with the Japanese literals kept), and the saved TRXs show C8 red first and on its solo rerun, then green.

Files edited after the review: none of the 29 candidate code/test files (SHA-256 re-checked against the frozen manifest: 0 changed, same file
set). Written after the review, not seen by Codex: this write-up and the M2 doc note (the note was written during the baseline run, before the
review, but docs were excluded from the pack). The post-review model-generation test was written, run red (empty model), and deleted.

## 9. Deployment
- Which build runs it: CareCrew.Blazor.Server (publish.bat). New in the publish output: nothing beyond the two library DLLs (no new static asset;
  the label editor's style is inline). `Xaf.EditDraft.Tests` is a test project (not referenced by any host).
- Consumers: CareCrew Blazor — the generic popups, the 入力控 list, the main lists' row icon, model captions. NHM WinForms — none (no Module or
  NHM change; the store base change is a caption attribute, not a persistent member: W41 passes). ChartWorkflowServiceV2 — none. Report layouts
  in the database — not applicable.
- Schema: none (captions are model metadata; the store mapping test passes with the Japanese captions resolved through the new mechanism).
- User model differences already stored for these popup views: view ids and member names are unchanged (C46); not observed against stored rows.
- No pay-window or month-end dependency; capture stays behind the existing `EditDraftCapture` keys.

## 10. Browser items for the main session (Dev2, dev host on 5002-5004 from the M3 build; nothing here was run in a browser)

Record the build id and the `[EditDraft]` log lines. Compare each generic popup with the chart popup (カルテ入力控) side by side.

A. Label editor (O-7)
1. Wave-1 offer popup 「保存されていない入力が見つかりました」: the lead (two sentences) and provenance lines show as plain caption-style text,
   no grey box, no border, no resize grip; the lead's two sentences on two lines; the provenance line(s) for two drafts on separate lines.
   Same font, colour and side padding as the chart popup's lines (DOM: `div.xaf-editdraft-label.dxbl-fl-cpt.dxbl-text`).
2. A long provenance or lead wraps inside the popup width (try a narrow window); nothing is clipped or overlaps the grid below.
3. Tab / Shift+Tab never stops on a text line; clicking the text gives no caret; nothing can be typed or pasted.
4. Conflict line: hidden when there is no conflict; shown (plain text) when an entry was changed elsewhere.
5. Read-only display (all entries 戻せません): lead and provenance as plain text; the 14-row typed-text memo unchanged (still a memo).
6. 入力控 list popup: the lead line as plain text.
7. Text containing `<`, `&` or a quote shows literally (e.g. a draft of a ToDo whose type caption or view caption has such a character, if any).

B. Icon only on badged rows (finding b)
8. Each wave-1 main list with a mix of rows: the folder icon (入力控を開く) only on rows with the bar; none on other rows.
9. Select (tick) a row WITHOUT a draft: the toolbar 入力控を開く is disabled as before; the icons on badged rows stay clickable and open their own row.
10. Select a badged row: toolbar enabled; its icon opens that row.
11. Type in a DetailView (new draft) and return to the list: the bar AND the icon appear on that row; 破棄 / save the draft: both disappear
    (after the badge refresh); the last badged row gone → no icon and no toolbar button.
12. Sort / filter / page the list: icons stay on the badged rows.
13. GeneralUser login: no icon, no toolbar button, no bar.

C. Localisation (captions byte-identical)
14. Offer popup grid columns: 戻す / 項目 / 入力した内容 / 現在の値 / 状態; read-only view memo caption 入力した内容（表示のみ）; 入力控 list columns
    画面（種類） / 対象 / 由来 / 入力日時 / 項目数 / 状態 / 保存期限; window captions unchanged (保存されていない入力が見つかりました, 戻せない入力があります,
    入力控, 入力控（<type>）); any tab or breadcrumb that shows a class caption (保存されていない入力, 戻せない入力, 入力控).
15. Security / role editor type and member lists that show the store class (EditDraft): member captions 入力者 / 記録種別 / 対象, unchanged.
16. A user with stored model differences for these popup views: their layout still applies (view ids unchanged).

## 11. What the "own repository + NuGet" step still needs

Floor (what the code uses today): .NET 8 (`net8.0`); DevExpress XAF/XPO **26.1.4** (central `DevExpressMajorVersion`), Blazor part on
DevExpress.ExpressApp.Blazor 26.1 (uses `BlazorPropertyEditorBase.CreateComponentModel`, `ListEditorInlineActionControl.CustomizeInlineActionButton`
with `CustomizeInlineActionButtonEventArgs.Visible/Enabled` — present in 26.1.4, decompiled; not checked against older majors),
Newtonsoft.Json 13.0.3, SQL Server (the writer's SQL), XPO with Guid-keyed `BaseObject` records (contract v1, M1).

Still to do (none done in M3):
1. Packaging: `IsPackable=true`, package ids `Xaf.EditDraft.Core` / `Xaf.EditDraft.Blazor`, an own version (today the assemblies take
   CareCrew's `ChartApplicationVersion` 2.6.42.0 from `Directory.Packages.props`), DevExpress as a versioned dependency range with 26.1.4 as
   the floor, license and repository metadata, symbols, the static web asset in the Blazor package (`_content/Xaf.EditDraft.Blazor/...`).
2. Repository: move `Xaf.EditDraft.Core`, `Xaf.EditDraft.Blazor`, `Xaf.EditDraft.Tests` with their own `Directory.Build.props` /
   `Directory.Packages.props`; CareCrew then consumes the packages (or a submodule) instead of project references.
3. InternalsVisibleTo: drop `NursingHome_Chart.Rostering.Tests` (still a friend because two staying tests construct the internal writer over
   CareCrew's store: W67 and E7/E17's type references); the library then grants only `Xaf.EditDraft.Blazor` and `Xaf.EditDraft.Tests`.
4. Open owner decisions that shape the public API: O-8 (chart `For` vs `ForName` for an unregistered chart type), O-10 (the default
   configuration section `EditDraftCapture` in Core), O-9 (log prefix) — M2 §13.
5. Open from M1: rename `EditDraftCaptureControllerBlazor` (it is platform-agnostic), two-provider availability test, registry lifecycle,
   slot equivalence; the four `AuditTrailExclusionWiringTests` worktree-root failures (CareCrew, pre-existing).
6. A sample host and the obligations a consumer takes on (store subclass + `AddEditDraftStore<T>()`, explicit DENY for every role, audit
   exclusion, retention purge job, owner and record-access seams, text-set choice).

README draft outline: 1 What it does (unsaved-input drafts, offer/restore, 入力控 list, badges) · 2 Requirements (floor above) ·
3 Install (packages, modules `EditDraftCoreModule` + `EditDraftBlazorModule`, `AddEditDraftStore<T>`, `AddEditDraftRegistry`,
`AddEditDraftBlazor`, stylesheet link) · 4 Store class and database (table, columns, indexes, no migration for an existing table) ·
5 Policies (EditDraftTypePolicy, decisions table, groups, approved views, ListView ids) · 6 Seams (owner, record access, member write,
clock, log, switch options) · 7 Texts and captions (English default, Japanese set, a host set; model captions through the text set) ·
8 Security obligations (DENY rows, audit exclusion, purge, owner predicate in every statement) · 9 UI pieces (label editor alias, row badge
class, inline icon rule) · 10 Configuration keys · 11 Tests (Xaf.EditDraft.Tests) · 12 Limits and known gaps.

## 12. Owner decisions and escalations

- **O-11 Row icon enable override (Codex C1).** (a) keep (now): a badged row's icon stays clickable when an unbadged row is selected (Codex's
  own requirement-only C16 asked for this), at the cost that a future host adding a row-specific disable (`TargetObjectsCriteria` on the action,
  or a `BoundItemCreating` handler) would see it overridden on badged rows — not reachable in CareCrew today. (b) fail closed: delete line 24 of
  `EditDraftRowOpenRule.cs` (visibility only); then, with an unbadged row selected, every badged row's icon is greyed (as all icons were before
  M3) and new test C16 changes with it. Both models agree on the facts; the choice is a UX/safety trade-off no offline check decides. Claude's
  recommendation: (a) for the CareCrew merge, (b) or a guard before the NuGet step (§11).
- **O-7 test consequence.** E6_E7_D9's "no property names an editor alias" now excepts the six text lines (they must name the library alias);
  name kept for the identity gate. Confirm that this follows from O-7.
- **InternalsVisibleTo.** Core now also grants `Xaf.EditDraft.Tests`; E6 (moved) and E17_O3 (stays) pin the three-element set. Confirm; the
  alternative (keep the SEC writer test in Rostering.Tests, no friend change) contradicts "tests that need only the two libraries move".
- **Test project packages.** `Xaf.EditDraft.Tests` uses five of Rostering.Tests' six pinned packages at the same versions (Google.OrTools left
  out). Codex's requirement-only C25 read "the same packages" as the identical list; say if OrTools must be added.
- Follow-ups (not decisions): a real model-generation caption test (Codex C2); the KB record owed for M2 + M3 (the KB server writes into
  the CareCrew repository, which this run may not touch); O-8, O-9, O-10 from M2 not in this brief.

## 13. Contribution log

### What each model did
- **Claude (Opus 5.5):** Phase 0; the requirement-only `tests` prompt; the baseline full run on the clean tree before any edit; the KB lookups
  (fix-090/rule-041 inline actions, rule-039/051 editor type, fix-179/119 localisation); DevExpress 26.1.4 decompilation (InlineActionButton,
  ListEditorInlineActionControl(+Container), InlineRowActionController, InlineActionBindingController, CustomizeInlineActionButtonEventArgs,
  ModuleBase.RegisterEditorDescriptors, BlazorPropertyEditorBase, ComponentModelBase, ModelNodesGeneratorUpdaters, PropertyEditorAttribute,
  EditorAliasAttribute); dxdocs (inline actions 404559, custom editors 402189/405922, generator updaters 404125, unit tests 405947); the design
  and code of the label editor, the row rule, the caption mechanism; the test classification per method; the extraction/compose scripts (exact
  bytes) and every tracked-file edit by the Edit tool; the 20 new tests; builds, runs, the identity gate, the sensitivity check; the pack and the
  frozen candidate; the check of both Codex findings; the post-review model-generation attempt; this write-up and the M2 note.
  Got right: the DX mechanism for the per-row icon (CustomizeInlineActionButton.Visible); the caption mechanism that needs no CareCrew model line;
  a move with 0 missing / 0 changed. Got wrong or missed: the per-row enable override can mask row-specific disables (Codex C1); the caption
  tests do not exercise model generation (Codex C2); own new test C8 red at first (component did not normalise CR LF) — fixed in the component;
  process slips caught by their own checks (the Edit tool trimmed the trailing space of a regex string in a scratch script; a `Match` name clash
  with Moq; the PropertyEditorAttribute alias is internal; while assembling this write-up a PowerShell helper was named `R`, the
  Invoke-History alias the brief warns about — the call failed, wrote an empty file, and was redone with a non-alias name).
- **ChatGPT (Codex gpt-6-astra, xhigh, codex-cli 0.153.4):** `tests` — 47 requirement-only expectations C1–C47 before any code (they shaped the
  new tests: an unbadged row has no icon rather than a disabled one, toolbar gating unchanged, HTML-sensitive text, thread-culture
  independence, the closure-level isolation check, the identity-gate failure modes). `diffreview` — 2 findings, both confirmed in source;
  0 code changes resulted (1 owner decision, 1 open coverage gap). Read only; no file_change; it decompiled DevExpress assemblies itself and read
  the pack.

### Found issues, by tool
| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| K1 | New test C8 red: the component rendered CR LF as given | Claude (run) | correct | first-run TRX, solo-rerun TRX | display / every CR LF value / executed / no | rerun after the component change | component normalises; test unchanged |
| K2 | C25_C31 red before the solution entry existed | Claude (run) | correct (sequence) | first-run TRX | none / — / executed / no | final run | sln entry added; test unchanged |
| K3 | E6_E7_D9 pins "no alias", reversed by O-7 | Claude | correct | `T/EditDraftLibraryBlazorTests.cs` | gate / certain / high / no | owner | edited + escalated (§12) |
| K4 | IVT set pinned in E6/E17 | Claude | correct | Core csproj; E6, E17 | gate / certain / high / no | owner | edited + escalated (§12) |
| K5 | Captions read from attributes in E8_D9, E21, store mapping | Claude | correct | the three tests | gate / certain / high / no | full run | mechanism updated, strings unchanged |
| K6 | CpSat FullMonthSolve fails in this machine's runs | Claude | pre-existing, unrelated | baseline + candidate TRX | none from M3 / — / executed / no | — | unchanged, reported |
| K7 | The design's xafml aspect depends on the XAF model language and needs a CareCrew model line as fallback | Claude | correct (static) | design §181; Module.cs:132 | wrong-language captions / per host / medium / no | browser (§10 C) | text-set updaters chosen instead |
| K8 | Edit tool trims a trailing space at the end of new_string | Claude | correct (process) | the extraction script's regex | none in repo files / — / executed / no | 0 matches → fixed | fixed in the script |
| C1 | Enable override can mask row-specific disables | Codex | correct (static); unreachable in CareCrew now | `B/EditDraftRowOpenRule.cs:24`; decompiled InlineRowActionController | wrong enabled icon / host with row criteria or BoundItemCreating / high / no | runtime pipeline with a row-disabled badged row | owner O-11 |
| C2 | Caption tests do not run the updaters in model generation | Codex | correct (coverage) | `T/EditDraftLibraryM3Tests.cs:329–370` | English captions could pass unnoticed / if updaters do not run / high / no | real model generation — attempted, empty model | open → browser §10 C |

Found independently by both: none (Codex read Claude's statements first; C1 and C2 are Codex's alone).

### Codex calls
| Run | Call | Attempt | Started | Duration | state | validation | Exit | Model / effort requested | Effective effort | reasoning tokens | Search | MCP calls | activity (cmds / non-zero / file_change / outside-repo) | Pack | CLI |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1b4d82 | tests | a1 (requirement-only directory) | 11:26:11 | 5.9 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 5,510 | off | 0 | 5 / 1 / 0 / 1 | REQUIREMENT.md (brief + F1–F9) | 0.153.4 |
| 1b4d82 | diffreview | a1 | 12:21:10 | 10.2 min | success | ok (candidate 29 files unchanged during review) | 0 | gpt-6-astra / xhigh | not observable | 6,616 | off | 22 (KB lookup 2, get_fix 2 incl. 1 failed, get_rule 2 incl. 1 failed, dxdocs search 10 incl. 4 failed, get_content 6) | 26 / 1 / 0 / 3 | v1 (474 KB) | 0.153.4 |

Input tokens: tests 155,553 (cached 132,096); diffreview 3,654,289 (cached 3,207,936). The `tests` isolation is by convention; its out.md cites
only REQUIREMENT.md and its commands read only that file.

### Setup checks (Phase 0)
| # | Item | Result |
|---|---|---|
| 1 | BASH_MAX_TIMEOUT_MS | present (2400000) |
| 2 | Read-only query connection (HARD) | not applicable: no database used |
| 3 | Repo trusted (HARD) | present (the hook fired, item 5) |
| 4 | Manifest (HARD) | 7/7 hashes match in the worktree and in the main repo |
| 5 | Hook fires (HARD) | `git push --dry-run` blocked by collab-guard; a harmless Monitor (`date`) ran unblocked |
| 6 | collab.rules | file present; the execpolicy check was blocked by the hook (its command text contains "git push") and not retried |
| 7 | prompt-input | saved; AGENTS.md section present; CLAUDE.md body absent (pasted as pack item 0) |
| 8 | Tool boundary (HARD) | no MCP tool of this agent writes a database, migrates, deploys, pushes or restarts; KB write tools not used |
| 9 | Tool parity (HARD) | KB with the 9 read tools (`enabled_tools`), dxdocs; DEVIATION as in earlier runs: node_repl and cua_repl enabled for Codex (no call to them) |
| 10 | Models (HARD) | gpt-6-astra listed, low…ultra incl. medium and xhigh |
| 11 | Run setup | run 1b4d82, salt (unused), codex from PATH, codex-cli 0.153.4; doctor exit 0; login ChatGPT |
| 12 | Snapshot | worktree HEAD 34491d1, branch feature/edit-draft-library, clean at start |
| 13 | Policy drift | CLAUDE.md and AGENTS.md identical to the main repo (AF56E4B3…, BEE30186…); `~/.codex/config.toml` effort medium, each call passes xhigh |
| 14 | Web search | off in both calls |

### Redaction
None needed: no personal data read or sent.

### Inputs Codex did not have
The candidate full-suite results (running during the review; now in §6b). Claude's private context: the auto-memory index (not relied on).

### Passes used
2 cross-model calls (`tests`, `diffreview`), 2 attempts, no retries. Hook false positives: 2 (the execpolicy check text; one long PowerShell
command the hook could not parse — re-done with the Write tool).

### KB
No `log_new_fix`: the KB server writes into the CareCrew repository, and the brief forbids work in that repository.
Owed when the owner accepts M3: one record (inline-row action visibility per row via CustomizeInlineActionButton; library label editor; model
captions from a text set through generator updaters; the Edit tool trailing-space trim).

### Clean-up
`artifacts/claude-test/20261002-1b4d82` deleted after the runs. Decompiled DevExpress excerpts, the extraction scripts, the abandoned
model-generation test and all logs are not in the repo.

## 14. Not verified / open questions
- Everything in a host: the label editor's look against the chart popup, wrapping on screen, focus, the conflict-line hide; the per-row icon
  (CustomizeControl reaching the inline control, the per-row Visible, re-render after a badge change); captions on screen in CareCrew —
  the main session's browser pass (§10). Nothing ran in a browser or a host.
- The generator updaters inside real model generation (Codex C2): not executed offline (attempt failed, §8).
- Whether a host's or a user's stored model difference for these captions or views behaves as documented (docs 404125) — not observed.
- `dotnet publish` output (no publish run; nothing new to publish beyond the DLLs).
- Codex could_not_determine (diffreview): runtime alias resolution, appearance, focus, conflict transitions, icon refresh; generated-model captions
  and override precedence; the full-suite gate at review time (now done, §6b); C8's historical test bytes (the test file was not changed after its
  first run: it is part of the frozen candidate and its SHA-256 there is the one that ran green); production behaviour.
- Codex could_not_determine (tests): exact English strings and the full action-caption inventory (now in `EditDraftTexts.cs`); badge read-failure
  policy (unchanged from wave 1b: previous badges kept); whitespace-only conflict text; the final counts (§6b); fixture dependencies (§2a); the
  running Dev2 assembly identity.
