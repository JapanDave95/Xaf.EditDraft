# Xaf.EditDraft library — milestone M2 (Blazor package, Llamachant replacements, seam retargeting)

Run `2026-10-02-editdraft-m2-6e2ced` (collaborator: Claude Opus 5.5 implements; Codex gpt-6-astra at xhigh reviews, read-only).
Branch `feature/edit-draft-library`, base `40405d1072ca3cc030a22e79d95f4a8dd44a8e53`
(M1 committed: 1fe4865 / 61f8d91 / 40405d1). Committed 2026-10-02 as ed1c8b9 (feat) / 07d56ee (test) / 34491d1 (docs); the text below
was written before the commit and still says "uncommitted". No deploy, no database, no schema change, no
NursingHome_Chart.Module change, no NHM change (D2-A).

> Note 2026-10-02 (M3 run `2026-10-02-editdraft-m3-1b4d82`): the three red new tests of §7c were corrected in 07d56ee on the owner's
> ruling O-6 (E2 exempts the InternalsVisibleTo entries and matches the key literal; E19 skips the XafDisplayName lines; E8 compares with
> `Equal(new[] { ... })`). O-7 was decided "Build a small library label editor (Recommended)": M3 replaces the read-only memo boxes of §3 with
> the library's own caption-style editor and localises the model captions §11 listed (`docs/xaf-editdraft-library-m3-2026-10-02.md`).
> O-8, O-9 and O-10 are not part of the M3 brief; no ruling on them is recorded in it, and M3 leaves their code as it is.

Owner's words (verbatim): "I want to make this a generic library that any project can use. Create a separate branch and make this a
DevExpress data restore feature". Decisions applied (labels verbatim from the brief): Platform "Platform-agnostic core + Blazor package" — "a
Blazor package adds the popup, badges, CSS"; Name "Xaf.EditDraft" → Xaf.EditDraft.Blazor; D3 "Platform-agnostic ones in Core"; D4 DI registry;
D9 replace the two Llamachant dependencies with plain XAF; O-2 "Accept for M1; remove in M2 when chart forwarders are retargeted"; O-3 "writer
goes internal in M2"; O-4 texts default en, CareCrew ja (keep); D2-A NHM untouched.

## 0. Combined answer

The Blazor half of the 入力控 engine now builds as its own Razor class library, `Xaf.EditDraft.Blazor`: the six Blazor-bound controllers, the popup
and list classes, the list bridge, the offer hand-over and the row-badge stylesheet moved out of CareCrew (served at
`_content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css`), behind a module that requires the Core module and a DI extension for the per-circuit
services; they now reach the owner, record-access, member-write, clock, text and log seams of Core instead of CareCrew helpers, and their
Japanese strings live in the library's Japanese text set (English is the default). Llamachant is gone from the library: the popup classes derive
from DevExpress's `NonPersistentBaseObject` (what NPOBase wrapped) and the text lines use XAF's own read-only string editor, which renders a
read-only box rather than the old caption-style text — a visible difference for the owner to judge at M4. The static chart-policy cache is
removed (chart policies come from the registry the host registers in DI) and the writer is internal to Core with its 65 owner-predicate lines
unchanged. Every baseline test identity keeps its outcome (full suite 5542 → 5557, 0 missing, 0 changed, no new skip, golden byte-equal); three of
the fifteen new tests are red because of defects in the tests themselves, confirmed by Codex, and are escalated unchanged. Nothing was run in a
browser or a host; the Dev2 pass (M4) has to show the popup, badges, list and chart offer working from the library.

## 1. Status

Implemented, uncommitted, 2026-10-02. Builds green (Core alone, Blazor library alone, CareCrew.Blazor.Server, CareCrew.Win, Rostering.Tests).
Tests: §7. Codex: `tests` call before the code, one `diffreview` (7 findings, no code change made after it). Five owner decisions in §13.
Next: owner decisions, then git-committer (topic branch exists), then M3 and the M4 browser pass.

## 2. What moved, what is new, what changed

Paths: `C/` = `CareCrew.Blazor.Server/Controllers/Common/EditDrafts/`, `B/` = `Xaf.EditDraft.Blazor/`. Moved files were copied byte for byte and
then edited in place (UTF-8 without BOM, CRLF, checked: 0 lone LF in every changed or new file; BOM kept only where HEAD had one).

| From | To | Change beyond namespace/usings |
|---|---|---|
| C/EditDraftListCaptureControllerBlazor.cs | B/ | owner seam; `EditDraftTypePolicy.IsGeneric`; log lines through `EditDraftLog` (same text) |
| C/EditDraftRestoreControllerBlazor.cs | B/ | owner, record-access and member-write seams; clock; texts; library row type + mapper; log |
| C/EditDraftRestorePopupControllerBlazor.cs | B/ | owner seam; clock (one "now" per 破棄 click); texts; log |
| C/EditDraftListControllerBlazor.cs | B/ | owner and record-access seams; clock; texts; `IsGeneric`; log |
| C/EditDraftListPopupControllerBlazor.cs | B/ | owner seam; clock; texts; log |
| C/EditDraftListBadgeControllerBlazor.cs | B/ | owner and record-access seams; clock (`Now()`); texts; log |
| C/EditDraftModels.cs | B/ | D9: `NonPersistentBaseObject` base, XAF string editor read-only (no alias), `RowCount`; rows `EditDraftRestoreItem` |
| CareCrew.Blazor.Server/Services/EditDraftListBridge.cs | B/ | `Caption` from the text set (static property); log |
| CareCrew.Blazor.Server/Infrastructure/EditDrafts/EditDraftOfferRequests.cs | B/ | none (comment) |
| CareCrew.Blazor.Server/wwwroot/css/edit-draft-row-badge.css | B/wwwroot/ | none (content identical: same git blob 660156b1) |

New in `B/` (5): `Xaf.EditDraft.Blazor.csproj` (Razor class library, net8.0, FrameworkReference Microsoft.AspNetCore.App, PackageReference
DevExpress.ExpressApp.Blazor + DevExpress.ExpressApp.ConditionalAppearance from the central versions, ProjectReference Core);
`EditDraftBlazorModule.cs` (requires EditDraftCoreModule, SystemBlazorModule, ConditionalAppearanceModule; exports the five popup/list classes;
9 controllers collected from the assembly; `RowBadgeStylesheet` constant); `EditDraftBlazorServices.cs` (`AddEditDraftBlazor`: bridge, offer
requests, badge notifier, scoped); `EditDraftRestoreItem.cs` (the popup row + `EditDraftRestoreItems` mapper); `EditDraftRestoreItemListControllerBlazor.cs`
(the tick/group controller for the library row; copy of the chart one).

Edited in Core (5): `EditDraftTexts.cs` (57 UI texts added to both sets), `EditDraftWriter.cs` (O-3: 3 declarations internal + 1 doc line;
SINGLE-MODEL), `EditDraftCaptureControllerBlazor.cs` (`RunOneWrite`, `RetiredFreshStart` internal; comment), `EditDraftRowContext.cs` (comment),
`Xaf.EditDraft.Core.csproj` (InternalsVisibleTo Xaf.EditDraft.Blazor + NursingHome_Chart.Rostering.Tests).

Edited in CareCrew (12 + solution): `TenantChartDraftTypePolicy.cs` (O-2), `TenantChartDraftPolicy.cs` and `TenantChartDraftRestorer.cs` (forwarders
take the registry), the three chart controllers (capture, restore, list: pass the DI registry), `Startup.cs` (`AddEditDraftBlazor`, Blazor module),
`Pages/_Host.cshtml:274` (link), `CareCrewSettingsPanel.razor` (library bridge), `CareCrew.Blazor.Server.csproj` (ProjectReference),
`BlazorGridCustomizationController.cs` (the new row type keeps its own columns), `EditDraftRestoreRows.cs` (comment: chart only), `CareCrew.sln`
(+1 project). Untouched: NursingHome_Chart.Module (0 changes), NHM, Model.xafml, appsettings*, `EditDraftOwner.cs`, `EditDraftAccess.cs`,
`CareCrewEditDraftSetup.cs`, the 6 wave-1 policy files, 勤怠.

Counts: 10 files moved to the Blazor library (their 10 CareCrew paths are gone; the empty `Controllers/Common/EditDrafts` folder removed),
5 new library files, 5 Core files and 12 CareCrew files + the solution edited; tests: 9 test files + the test csproj edited, 1 new test file.
`git diff HEAD --stat` (tracked only, code and tests): 38 files, +467 / −2,423; with the M1 doc note 39 files, +478 / −2,427 (the moved files count
as deletions; they are untracked under `Xaf.EditDraft.Blazor/` until committed; this write-up is untracked).

Which model pieces moved: none. The only 入力控 model node is `Model.xafml:8` `<Action Id="EditDraftListBlazor" PaintStyle="CaptionAndImage"
AdaptivePriority="0" CustomCSSClassName="cc-keep-caption" IsNewNode="True" />` — a CareCrew header layout choice with a CareCrew CSS class;
it stays in CareCrew with the id unchanged (`EditDraftListControllerBlazor.HeaderActionId` = "EditDraftListBlazor"). There is no 入力控 ListView
node; the popup/list views are generated, and their ids do not change (they derive from the class names, which are unchanged).

`EditDraftListBridge` moved: it had no CareCrew dependency except GlobalLogger and the caption constant (both replaced by seams).

## 3. D9 — the two Llamachant dependencies

| | Before (M1) | After (M2) |
|---|---|---|
| Base class of the 4 popup/list classes | `LlamachantFramework.Module.NonPersistent.NPOBase` | `DevExpress.ExpressApp.NonPersistentBaseObject` |
| What the old base was (decompiled with ilspycmd from the built Llamachant.ExpressApp.Module 26.1.4.1) | `abstract class NPOBase : NonPersistentBaseObject` + `SetPropertyValue<T>(string name, ref T store, T value) { store = value; OnPropertyChanged(name); }` | the same base; the classes never called the helper |
| Text lines (Lead, Provenance, ConflictBanner; read-only view Lead/Provenance; list Lead) | `[EditorAlias("LabelPropertyEditor")]` = Llamachant `BlazorLabelPropertyEditor`: a `<div class="ImageContainer dxbl-fl-cpt dxbl-text">` with a `<span>` of the display text (caption-style plain text; a line break collapses to a space; conditional-appearance colours; null text) | XAF's built-in `StringPropertyEditor` (no alias), read-only by the existing `[ModelDefault("AllowEdit","False")]`, with `[ModelDefault("RowCount", "3"/"2")]` so it renders as a read-only DxMemo (several lines, line breaks shown) |
| Popup row type | the Module's `TenantChartDraftRestoreItem : NPOBase` (shared with the chart popup) | `Xaf.EditDraft.Blazor.EditDraftRestoreItem : NonPersistentBaseObject`, same members, order, captions, visibility and ModelDefault attributes (pinned by test E8_D9); `Selected` raises PropertyChanged on every assignment as the old helper did |

No XAF 26.1 built-in "label" property editor exists in the documentation searched (dxdocs: string properties, custom component editors,
read-only rendering). The brief asks for "a standard DX Blazor editor giving the same read-only display"; the standard read-only string editor
gives a read-only box, not caption-style text. **Known visual difference for M4:** the lead/provenance lines show in a read-only memo box instead of as
plain caption text, and the lead's line break becomes visible. If the owner wants the old look, the alternative is a ~30-line library property
editor (BlazorPropertyEditorBase rendering a plain span) — not built, because the brief names a standard editor.

## 4. O-2 and O-3

**O-2 (static chart-policy cache) — removed.** `TenantChartDraftTypePolicy` holds no static field or property (test E14_O2 reflects over it).
`RegisterAll(registry)` creates the 23 policies for the registry it is given (CareCrew's DI singleton through `CareCrewEditDraftSetup`);
`ForName(registry, name)` and `For(registry, type)` answer from that registry; a type outside the 23 gets the chart rules from `Create(type)`
per call, never cached (what `MembersOf(Type)` answered before). The forwarders that need a chart policy (`TenantChartDraftPolicy.GroupOf/
HasSideEffect/IsNotRestorableOnExisting/MembersOf/Find/CompanionOf/PathFor`, `TenantChartDraftRestorer.BuildItems/ApplyOrder/Apply/ExpandGroups/
ChooseOffer`) take the registry as first parameter; the chart capture, restore and list controllers pass
`Xaf.EditDraft.Core.EditDraftServices.Registry(Application?.ServiceProvider)`. The chart store (`TenantChartEditDraft`), its writer, the F2 author and
the chart popup row are unchanged — the chart path reaches the engine only through these forwarders.
Reading of the brief ("forwarders and the chart controllers use the Core/Blazor types directly"): the forwarders were retargeted from the static cache
to the Core registry from DI; the chart screens keep their TenantChartDraft* call names, which the chart tests pin.

**O-3 (writer internal) — done.** `IEditDraftWriter`, `EditDraftWriter` and `EditDraftWriter<TStore>` are internal; `RunOneWrite` and
`RetiredFreshStart` (they take the writer) are internal. InternalsVisibleTo: `Xaf.EditDraft.Blazor` (its restore, list, badge and list-capture
controllers call the writer) and `NursingHome_Chart.Rostering.Tests` (the tests construct the writer). No CareCrew assembly is a friend and no
CareCrew code constructs the writer (test E17). Owner-predicate lines (every line of EditDraftWriter.cs naming `OwnerUserOid` or `ownerOid`):
**65 before, 65 after, identical text and order**; SQL owner predicates `[OwnerUserOid] = @pN`: 4. `EditDraftSeed` and `EditDraftRowState` stay
public (plain values). The writer diff was not sent to Codex (single-model).

## 5. CareCrew after M2 (brief item 7)

| Kind | Files | Why it stays in CareCrew |
|---|---|---|
| Policies | `Infrastructure/EditDrafts/Policies/` (EditDraftWavePolicies + 5 wave-1 policies); `Infrastructure/TenantChartDraftTypePolicy.cs` + `TenantChartDraftPolicy.cs` (23 chart policies and their member lists) | each names a Module type |
| Seam implementations (4) | owner: `EditDraftOwner.cs` (`CareCrewEditDraftOwnerResolver`: StaffMember lookup, D6 GeneralUser refusal); record access: `Infrastructure/EditDraftAccess.cs` (`CareCrewEditDraftRecordAccess`: 事業所 rule); log sink: `GlobalLoggerEditDraftLog`; composition: `CareCrewEditDraftSetup.AddCareCrewEditDraft` (store `EditDraft`, registry, owner seam, switch section `EditDraftCapture`, Japanese texts) | CareCrew types, CareCrew rules |
| Settings panel entry | `Components/CareCrewSettingsPanel.razor` (injects the library's bridge, shows its caption) | host UI |
| Model | `Model.xafml:8` action node `EditDraftListBlazor` | host header layout and CSS class |
| Host wiring | `Startup.cs` (AddEditDraftBlazor, AddCareCrewEditDraft, record-access singleton, Core + Blazor modules), `Pages/_Host.cshtml:274`, the csproj reference, `BlazorGridCustomizationController.cs` (excludes the library row type) | host composition |
| Chart-specific controllers | `TenantChartDraftCaptureControllerBlazor` | F2 staff author (not the login), NEW-record capture with reconstruction seeding, own store `TenantChartEditDraft` through `TenantChartDraftWriter`; the generic engine captures existing records only and owns by login |
| | `TenantChartDraftRestoreControllerBlazor` | offers from the chart store with the F2 author re-check and the fix-529 single-draft choice; plan type `TenantChartDraftRestorePlan` is a Module class (NHM-mirrored; the Module is out of scope) |
| | `TenantChartDraftRestorePopupControllerBlazor` + `TenantChartDraftRestoreItemListControllerBlazor` | 破棄 through the F2 author and the chart writer; row type is the Module class |
| | `TenantChartDraftListControllerBlazor` + `TenantChartDraftListPopupControllerBlazor` | the chart store list, NEW-record recreation with the duplicate notice (同じ記録が既にあります), F2 identity, its own header action and bridge |
| Chart support | `TenantChartDraftAuthor`, `TenantChartDraftSwitch` (key `TenantChartDraftCapture`), `TenantChartDraftWriter`, `TenantChartDraftAdoptions`, `Services/ChartDraftListBridge.cs`, forwarders `TenantChartDraft{Payload,Display,Restorer}.cs`, `Infrastructure/EditDrafts/EditDraftRestoreRows.cs` (neutral row → chart row) | the chart path until the chart store migrates (design §4.6 lists what that needs) |

Left as found, reported: `EditDraftAccess.CanWrite/NotWritable` (forwarders to Core) have no caller after M2 (single-model file, not edited);
`EditDraftWavePolicies.IsGeneric` forwarder kept (tests use it).

## 6. Static asset

`Xaf.EditDraft.Blazor/wwwroot/edit-draft-row-badge.css` → served at **`_content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css`** (the brief's path;
the design's `.../css/...` subfolder was not used). `_Host.cshtml:274`:
`<link href="_content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css" asp-append-version="true" rel="stylesheet" />`.
CareCrew `Program.cs:129` already calls `webBuilder.UseStaticWebAssets()` outside Development (KB fix-120/rule-059) and `publish.bat:182` copies
without `/XO` (KB fix-185). Not verified in M2: that a host serves the path (dev host and published host) — M4.


## 7. Tests

### 7a. Codex expectations first
The `tests` call ran in a requirement-only directory (one file, `REQUIREMENT.md`: the brief verbatim, the design without §4.11, M1 §0/§4/§8/§9,
the baseline facts) before any M2 code was shown: 34 expectations E1–E34 and a could_not_determine list (it flagged the "no appsettings key name"
vs the Core default section and the CSS path conflict, both resolved or escalated below). The new file `EditDraftLibraryBlazorTests.cs` implements
the offline-checkable ones (labels in the test names: E1–E4, E6–E8, E12, E14, E17, E19, E22–E24); E9–E11, E13, E16, E20, E21, E33 need M4 or are
covered by the unchanged existing tests; E18 is the executed predicate check (§4); E25–E32 are the gate below.

### 7b. Identity comparison (identity = class + test name incl. TestCase arguments, case-sensitive; outcome multiset)
Baseline on the unchanged tree (HEAD 40405d1, clean) before any edit; candidate = the reviewed bytes (no code changed after the review).

| Run | Baseline | Candidate | Missing | Changed outcome | Newly skipped | New |
|---|---|---|---|---|---|---|
| Rostering full (27 m 52 s → 33 m 52 s) | 5542: Passed 5526, NotExecuted 11, Failed 5 | 5557: Passed 5538, NotExecuted 11, Failed 8 | 0 | 0 | 0 | 15: 12 passed, 3 failed |
| Filter `FullyQualifiedName~Draft\|FullyQualifiedName~Golden` (the brief's) | 698: Passed 694, NotExecuted 4 | 713: Passed 706, NotExecuted 4, Failed 3 | 0 | 0 | 0 | 15 (same) |
| Filter `EditDraft\|TenantChartDraft\|AttendanceDraft\|Golden` (M1's) | 471: Passed 467, NotExecuted 4 | 486: Passed 479, NotExecuted 4, Failed 3 | 0 | 0 | 0 | 15 (same) |

(`dotnet test` summaries: baseline full "Total 5537, Failed 5, Passed 5526, Skipped 6"; candidate full "Total 5552, Failed 8, Passed 5538, Skipped 6";
the summary leaves out 5 of the 11 NotExecuted TRX results in both runs, as in M1.)

- Pre-existing failures, unchanged: 4 × `AuditTrailExclusionWiringTests` (repository root detection in a worktree) and
  `RosterTelemetryRecorderTests.MultiDepartment_CompletedGeneration_PersistsOneRecord_WithColumnsAndParseablePayload`.
- Golden `NursingHome_Chart.Rostering.Tests/Golden/TenantChartDraft.golden.txt`: SHA-256 `72325EE15DD4E28A2AA4C1C19B1FCA84401E7F459332FB22B42C1C08358C3144`,
  unmodified; its tests pass through the registry-taking forwarders.
- W41 (persistent-member comparison, owner ruling 2026-10-02) passes in both runs.

### 7c. The three red new tests — escalated, NOT changed (owner rule 2026-09-24; one solo rerun each, owner rule 2026-09-30)
First run (quick filter) and solo rerun both red; full run red. Classification: test defects (Claude), confirmed by
Codex C3–C5. Proposed correction for each (owner to approve or reject):
- `E2_the_Blazor_project_references_only_Core_and_DevExpress_...`: (1) the token scan rejects `NursingHome_Chart` in the Core csproj line
  `<InternalsVisibleTo Include="NursingHome_Chart.Rostering.Tests" />` (the friend grant the brief allows); (2) Codex C3: the second check rejects
  `EditDraftCapture` inside the identifier `EditDraftCaptureControllerBlazor` (list capture lines 371/430). Proposal: exempt `<InternalsVisibleTo …>` lines
  and match the key as a string literal (`"EditDraftCapture`) instead of a substring.
- `E19_the_moved_controllers_use_the_Core_seams_...`: rejects `"入力控"` in `[XafDisplayName("入力控")]` model captions (EditDraftModels.cs:103/121/130),
  which M2 keeps Japanese for M3 localisation (as M1 kept the store captions). Proposal: skip lines with `XafDisplayName(` and keep an M3 check for captions.
- `E8_the_row_mapping_keeps_every_field_...`: `raised.Should().Equal("Selected", "Selected", "every assignment notifies ...")` — FluentAssertions takes
  the third string as an expected element; observed `{"Selected","Selected"}` is the intended behaviour. Proposal: `Should().Equal(new[] { "Selected", "Selected" }, ...)`.
- A fourth first-run failure, `E6_E7_D9_...`, was caused by words in a doc comment of EditDraftModels.cs (NPOBase / LabelPropertyEditor / Llamachant).
  The comment was reworded (implementation side); the test was not changed; the solo rerun passed. Reported here because it is a change made in
  response to a red test.

### 7d. Test edits (D6: mechanical — paths, literals naming retargeted symbols, reflection; expectations unchanged)
- Source paths of moved files → `Xaf.EditDraft.Blazor/…`: Wave1b `ListCapture`/`Badge` constants and 6 inline reads; Wave1 W33, W28, W32 (3), W12;
  Seam E21_D1.
- Literals naming retargeted calls: `EditDraftOwner.Current(objectSpace)` → `EditDraftServices.CurrentOwner(Application?.ServiceProvider, objectSpace)`
  (E1_E5); `EditDraftOwner.Current(Application).IsNone` → `EditDraftServices.CurrentOwner(Application?.ServiceProvider, Application).IsNone` (E23);
  the C6 gate line with `_objectSpace`; `EditDraftAccess.IsRecordVisible(Application, _policy, target)` →
  `EditDraftServices.RecordAccess(Application?.ServiceProvider).IsRecordVisible(Application, _policy, target)` (E16_E17); `DateTime.Now` → `Now()` in the
  badge read (E24_E26); the badge log regex `GlobalLogger\.\w+\(\$"…"` → `EditDraftLog\.\w+\(\$"…"` so the "no Oid in badge logs" check still matches
  the log lines (without it the loop would match nothing and pass silently); `: NPOBase` → `: NonPersistentBaseObject` (W32, anticipated by design D6).
- Registration literals: W32 and E25 read the library's `EditDraftBlazorServices.cs` (`services.AddScoped<EditDraftListBridge>();`,
  `…<EditDraftOfferRequests>();`, `…<EditDraftBadgeNotifier>();`) and assert Startup calls `EditDraftBlazorServiceCollectionExtensions.AddEditDraftBlazor(services);`
  (one line added in each test so the host registration stays asserted).
- Reflection: W67 `RetiredFreshStart` with `BindingFlags.NonPublic` (internal with the writer, O-3).
- O-3 expectation: Isolation E6 — Core's InternalsVisibleTo is exactly {Xaf.EditDraft.Blazor, NursingHome_Chart.Rostering.Tests} and none starts with
  CareCrew (was "no InternalsVisibleTo"; changed by the owner's O-3 ruling).
- O-2: Seam E11 wrappers take the registry; 119 chart forwarder call sites in 5 test files gain `CareCrewRegistry.Default` as first argument
  (TenantChartDraftTests 32, Golden 39, AccidentGroup 36, Masking 7 (the source-text literal `"TenantChartDraftRestorer.ChooseOffer("` kept), Engine 5).
- Test csproj: ProjectReference to Xaf.EditDraft.Blazor (the library is inspected as its own assembly).
- Process slip, caught by its own check: the Edit tool dropped the trailing space of the inserted `CareCrewRegistry.Default, `; every site was repaired
  to `Default, <arg>` before the review (checked: 0 sites without the space).

### 7e. New tests (`EditDraftLibraryBlazorTests.cs`, 15)
Blazor assembly references (Core yes; no NursingHome_Chart/CareCrew/Progress/Llamachant/CareTree; Core does not reference Blazor); project file
(Razor SDK, only Core + the two DX packages, no versions, no linked source) and a code scan of both libraries (E2, red); placement of the 9
controllers + models + bridge + offer note in the library and nothing left in the host (E3); module requires Core, exports exactly the 5 NP classes,
collects each controller once (E4); Startup registers the module once after Core and calls AddEditDraftBlazor, csproj and sln entries (E4); scoped
services, two circuits never share (E12); D9 base class and no editor alias, read-only + RowCount (E6/E7); the library row equals the chart row's
member shape (E8_D9); row mapping and PropertyChanged (E8, red); static asset and host link (E23); O-2 no static state, two registries in both orders,
empty registry (E14); O-3 internal writer, friends, no CareCrew writer use (E17); seams/clock/no literal UI text (E19, red); Japanese UI texts and
English default (E22); CareCrew residue (E24).

## 8. Builds (all `--artifacts-path artifacts/claude-test/20261002-6e2ced`)
- Baseline: Core exit 0; CareCrew.Blazor.Server exit 0; CareCrew.Win exit 0.
- Candidate: Xaf.EditDraft.Core alone exit 0, 0 warnings; Xaf.EditDraft.Blazor alone exit 0, 0 warnings; CareCrew.Blazor.Server exit 0 (2209 unique
  warnings on a `--no-incremental` rebuild = baseline 2209, none in a touched file); CareCrew.Win exit 0 (it references no changed project);
  NursingHome_Chart.Rostering.Tests exit 0.
- Not run: `dotnet publish` (the `_content` copy into the publish output is M4).

## 9. Codex review and post-review edits

`diffreview` a1 (pack v1, 383 KB; candidate frozen, unchanged during the review; validation ok). The six single-model files were not sent (the writer
diff was described). Findings and outcome:

| Codex | Finding | Claude's check | Outcome |
|---|---|---|---|
| C1 | `TenantChartDraftTypePolicy.For(registry, type)` manufactures a policy (`Create`) for one of the 23 chart types that the registry does not hold, while `ForName` returns null: offer (name) and apply (type) would resolve differently in a host with a partial chart registration | Confirmed in source (`TenantChartDraftTypePolicy.cs:59–64`, `TenantChartDraftRestorer.cs:26,43`). Unreachable in CareCrew (all 23 registered); before M2 the case could not exist (static cache). Claude agrees fail-closed (null) is the more consistent rule, but its own new test E14 asserts the fallback | Not changed: changing it turns E14's last block red, which needs the owner (§13 O-8) |
| C2 | D9: read-only memo boxes instead of the label's caption text — does not meet "look and behave the same" | Agreed; stated in the pack | Owner decision at M4 (§13 O-7) |
| C3 | E2 red test has a second false positive (`EditDraftCapture` inside `EditDraftCaptureControllerBlazor`) | Confirmed; missed by Claude's first classification | Added to the escalation (§7c) |
| C4 | E8 red test: the explanation is taken as an expected element | Same as Claude | Escalated (§7c) |
| C5 | E19 red test scans model captions deferred to M3 | Same as Claude | Escalated (§7c) |
| C6 | The generic tick log changed from `[ChartDraft] restore tick` to `[EditDraft] restore tick` | Agreed (stated in the pack): the generic popup used the chart's tick controller before M2 | Kept: a generic library line cannot name the chart feature; owner decision (§13 O-9) |
| C7 | Core still holds the configuration key names (`EditDraftCapture:Enabled`, …) the brief excludes from both libraries | Agreed (stated in the pack); M1 default, pinned by tests E2/E14 | Owner decision (§13 O-10) |

Files edited after the review (bytes not seen by Codex): `docs/xaf-editdraft-library-m1-2026-10-01.md` (the dated W41/commit note, §1 and §8 O-1
markers) and this write-up. No code or test file changed after the review; the full run (§7b) is on the reviewed bytes.

## 10. M4 browser checklist (Dev2, dev host on 5002-5004, the FINAL build; owner)

Nothing in M2 was run in a browser or a host. Record the build id and the `[EditDraft]` / `[ChartDraft]` log lines for each item.

> **Status note, 2026-10-02 (main session):** M2 committed as ed1c8b9 / 07d56ee / 34491d1 and merged with M3 as dd0e79d.
> This checklist was run on Dev2 from the M2 build (host :5004): A1 startup/discovery, A2/A3 stylesheet
> (`_content/…` 200, old path 404), B offer→restore→保存 (`applied=2`, `delete after save: rows=1`), fresh capture, C8 badge,
> C9 row 開く, D 入力控 list with 由来, E chart offer/restore and 破棄 prompt — all pass. B4 (O-7 look) was decided afterwards:
> the memo-box rendering was replaced by the library label editor in M3. Owner rulings on this run: O-6 test fixes applied,
> O-8 fail-closed `For()` tried and REVERTED (it removed the TenantChartEvent base-class line from the golden snapshot),
> O-9/O-10 accepted. KB: fix-537.

A. Host start and wiring
1. The app starts; the startup log shows no module or controller error; the 入力控 header action and the 歯車 → 復元 → 入力控 entry appear as before
   (caption 入力控, the header button keeps its caption — `cc-keep-caption`).
2. `GET /_content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css` returns 200 on the dev host AND on a published dev host (publish output contains
   `wwwroot/_content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css`); the `<link>` carries the `?v=` hash from `asp-append-version`.
3. The old `css/edit-draft-row-badge.css` is no longer requested (no 404 in the request log).

B. Wave-1 (DetailView) — docs/generic-edit-draft-wave1-2026-10-01.md §6.3, every item, plus:
4. Offer popup appearance (D9): the lead, provenance and conflict lines show as read-only boxes (StringPropertyEditor, memo) instead of the earlier
   caption-style text; check wrapping, height (RowCount 3/2), that nothing can be typed into them, that the empty conflict line is hidden, and that the
   lead's two sentences show on two lines. Decide whether this look is acceptable (owner; alternative in §3).
5. The item grid: columns 戻す / 項目 / 入力した内容 / 現在の値 / 状態 in that order, no selection column, no pager, no group panel, all rows shown;
   one click ticks a row; a group ticks together; すべて選択 toggles; はい applies; あとで leaves the drafts; 破棄 discards all shown and closes; the
   log says `[EditDraft] restore tick ...` (before M2 the generic popup logged `[ChartDraft] restore tick ...`).
6. The read-only display (all entries 戻せません): lead and provenance boxes, the full typed text in the 14-row memo, 破棄, 閉じる.
7. Messages and captions in Japanese exactly as before (the texts moved into the library's Japanese set): e.g. 「この記録は表示できません。」,
   「n 件を戻しました。内容を確認して保存してください。」, popup caption 「保存されていない入力が見つかりました」.

C. Wave-1b (ListView) — docs/generic-edit-draft-wave1b-2026-10-01.md §6 items 1-10, every item, plus:
8. Badge colour bar from the library stylesheet (`tr.edit-draft-row > td:first-child`), after typing and after DetailView drafts; gone after ✓ / save / 破棄.
9. 入力控を開く on each wave-1 main list (TenantCase / ToDo / NightRoundsTime / TenantSubSection / StaffOverTimeHoliday_ListView(_Manager)).

D. 入力控 list
10. Header action on a wave-1 list tab → filtered list 「入力控（<type>）」; gear entry → all types; columns incl. 由来; 開く opens the record modally
    with exactly that draft offered; 破棄 marks 「既存・破棄済み」; 破棄済みも表示 / 破棄済みを隠す toggles; empty list text; 閉じる.

E. Chart path (O-2)
11. カルテ入力控: on a chart DetailView type, close without saving, reopen → the chart offer appears (保存されていないカルテ入力が見つかりました) with the
    chart provenance; restore applies; 破棄 works; the chart list (header + gear) recreates a NEW record from a draft; TenantChartAccident
    getter-filled AccidentTime is not a phantom draft (fix-529).
12. Logs: `[ChartDraft]` lines unchanged.

F. Owner and access (unchanged seams, now called through the Core seam)
13. A GeneralUser (department) login: no capture, no offer, no badges, the list refuses with 「この画面の入力控は、職員個人のログインで使えます。」.
14. A record of a 事業所 the login is not assigned to: offer refused with 「この記録は表示できません。」; the list shows 「（表示できません）」.


## 11. What M3 (test relocation into Xaf.EditDraft.Tests) needs

- A new test project `Xaf.EditDraft.Tests` referencing Core and Blazor only (no Module), with the library-contract tests on synthetic types: registry
  (E9/E10), texts (E16, M2 E22), log facade (E12/E13), clock and expiry (E21), table cache (E22), row mapping (M1 E17/E18, M2 E8), writer fail-closed
  (SEC), owner/record-access defaults (SEC1/SEC2), isolation (M1 E5–E8, M2 E1–E4, E17), module inventory, the per-circuit services (M2 E12), the
  engine parts of `EditDraftEngineTests` that need no Module type. CareCrew-specific tests (policies, chart golden, wave-1/1b wiring scans, the CareCrew
  composition) stay in NursingHome_Chart.Rostering.Tests.
- InternalsVisibleTo: replace `NursingHome_Chart.Rostering.Tests` by `Xaf.EditDraft.Tests` once the writer tests move (then no application test
  assembly is a friend); W67 and the SEC writer test move with it or use a public test seam.
- The owner's disposition of the three red tests (§7c) before they move; the E14 assertion if O-8 is decided for fail-closed.
- Source-text tests that read `Xaf.EditDraft.Blazor/*.cs` (Wave1/Wave1b scans) can move or stay; either way keep the identity-level gate (no new skip).
- Localisation (M1 O-4 → M3): model captions of the store and of the five popup/list classes as English attributes + an embedded ja model
  aspect, or overrides in CareCrew's Model.xafml if the aspect is not applied (CareCrew's PreferredLanguage not established).
- Open from M1: rename `EditDraftCaptureControllerBlazor` (platform-agnostic); two-provider availability test on real data layers; registry
  lifecycle; slot behavioural equivalence; `AuditTrailExclusionWiringTests` worktree root detection (4 pre-existing failures).

## 12. Deployment

- Which build runs it: CareCrew.Blazor.Server (publish.bat). The publish output must contain `wwwroot/_content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css`
  (Razor SDK static web assets; not verified, no publish run); `Program.cs:129` loads the static web asset manifest outside Development; robocopy
  `/E /PURGE` without `/XO` copies the new folder (`publish.bat:182`).
- Consumers: CareCrew Blazor — all of it. NHM WinForms — none (D2-A; no Module change; the store mapping is unchanged since M1).
  ChartWorkflowServiceV2 — no draft code path in the files reviewed. Report layouts in the database — not applicable.
- Schema: none. Deny rows, audit exclusion and purge: unchanged (same store class `EditDraft`).
- Production stays off until the owner turns the `EditDraftCapture` keys on, as before; no pay-window or month-end dependency.

## 13. Owner decisions and escalations

- **O-6 Three red new tests (§7c).** E2, E19, E8 are test defects (Claude; Codex C3–C5 agree); proposed one-line corrections are in §7c. Not changed
  (owner rule: a failing test, even a new one, comes to the owner before it is changed).
- **O-7 D9 look of the popup text lines (Codex C2).** XAF's standard read-only string editor (now: read-only memo boxes, line breaks visible) vs a small
  library label editor that reproduces the old caption-style text. Both models agree the look differs; only the M4 browser comparison and the owner
  decide.
- **O-8 Chart policy for a chart type the registry does not hold (Codex C1).** Codex: `For` must not manufacture a policy (fail closed, consistent with
  `ForName`). Claude: agrees that is more consistent; the current code (and Claude's new test E14) falls back to the chart rules. Unreachable in
  CareCrew. Choosing fail-closed changes one block of E14 (needs the owner).
- **O-9 Generic tick log prefix (Codex C6).** `[EditDraft] restore tick` (now) vs the old `[ChartDraft] restore tick` (an artefact of the shared
  row type). Claude recommends keeping `[EditDraft]`.
- **O-10 Configuration key names in Core (Codex C7; brief item 6).** Core keeps the M1 default section `EditDraftCapture` (keys `EditDraftCapture:Enabled`,
  `:Types:<id>:Enabled`, `:ListViews:Enabled`), pinned by tests E2 (wave 1b) and E14 (M1). The Blazor library names no key. Removing the default
  (e.g. a neutral library default, CareCrew passing `EditDraftCapture` explicitly) is an expectation change for the owner.
- Follow-ups (not decisions): CareCrew `EditDraftAccess.CanWrite/NotWritable` now have no caller (single-model file, left untouched);
  `EditDraftRowState` stays public; the M4 checklist (§10).

## 14. Contribution log

### What each model did
- **Claude (Opus 5.5):** Phase 0; the requirement-only `tests` prompt; the per-test baseline (builds, two filters, full suite) on the unchanged tree
  before any edit; read Llamachant's NPOBase and label editor by decompiling the built assembly (behaviour only, no code copied); dxdocs searches for a
  built-in label editor and for NonPersistentBaseObject; the KB lookup; the Blazor project, the moves, every edit (Core, CareCrew, tests), the O-3
  predicate comparison, the encoding/CRLF and CSS blob checks, the text-literal coverage check (76 Japanese UI literals of the HEAD files, all in the
  Japanese set); the 15 new tests; builds and runs; the identity comparisons; parity pack v1 and the frozen candidate; verified every Codex finding in
  source; this write-up and the M1 note.
  Got right: the move set and seams; the D9 base-class finding (NPOBase is NonPersistentBaseObject + a helper); the three red tests as test defects
  (confirmed). Got wrong or missed: three defects in its own new tests (E2, E19, E8) and the E6 comment words — all self-caught by the first run;
  missed the second E2 false positive (Codex C3); the For/ForName asymmetry for a partial chart registration (Codex C1); a process slip in test edits
  (the Edit tool trimmed a trailing space; repaired before the review).
- **ChatGPT (Codex gpt-6-astra, xhigh, codex-cli 0.153.4):** `tests` — 34 requirement-only expectations before seeing code (they shaped the new tests:
  two registries in both orders, a type present in one registry only, two circuits never sharing services, the silently-weakened-gate checks, the
  O-3 non-friend probe); it flagged the CSS-path and config-key conflicts in could_not_determine. `diffreview` — 7 findings, all confirmed in source;
  0 code changes resulted (2 confirm red-test classifications and add one blocker, 1 new design inconsistency, 4 restate deviations the pack declared,
  now owner decisions). Read only; no file_change; it read the evidence files the pack named.

### Found issues, by tool
| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| K1 | New test E2 scans the allowed InternalsVisibleTo line | Claude (run); Codex C3 added a 2nd false positive | correct (test defect) | first run + solo rerun + full run; `Xaf.EditDraft.Core.csproj:30`, list capture :371/:430 | red gate / certain / executed / no | owner disposition | escalated O-6 |
| K2 | New test E19 scans model captions kept for M3 | Claude; Codex C5 | correct (test defect) | runs; `EditDraftModels.cs:103,121,130` | red gate / certain / executed / no | owner disposition | escalated O-6 |
| K3 | New test E8 passes its explanation as an element | Claude; Codex C4 | correct (test defect) | runs; observed {"Selected","Selected"} | red gate / certain / executed / no | owner disposition | escalated O-6 |
| K4 | New test E6 failed on doc-comment words | Claude | correct (comment) | first run; rerun passed | none / — / executed / no | solo rerun | comment reworded, test unchanged |
| K5 | D9 editor renders a read-only box, not caption text | Claude (pack); Codex C2 | correct (static; rendering unverified) | `EditDraftModels.cs`; decompiled label editor | visible popup change / every offer / medium / no | M4 browser comparison | O-7 |
| K6 | Generic tick log prefix changes | Claude (pack); Codex C6 | correct | `EditDraftRestoreItemListControllerBlazor.cs:69` vs chart :139 | log searches / every tick / high / no | log comparison | O-9 |
| K7 | Core default config section remains | Claude (pack); Codex C7 | correct | `EditDraftSwitch.cs:20–32`; tests E2/E14 | brief item 6 incomplete / — / high / no | owner | O-10 |
| K8 | Edit tool dropped the trailing space in 119 inserted arguments | Claude | correct (process) | regex count 0 after repair | formatting only / — / executed / no | count | repaired pre-review |
| K9 | CareCrew EditDraftAccess forwarders unused | Claude | correct | grep: no caller | none / — / high / no | — | follow-up |
| K10 | 5 pre-existing failures | Claude | correct, pre-existing | baseline TRX | none from M2 / — / executed / no | — | unchanged |
| C1 | `For` manufactures a policy for a chart type missing from the registry; `ForName` returns null | Codex | correct (static); unreachable in CareCrew | `TenantChartDraftTypePolicy.cs:59–64` | inconsistent offer/apply in a partial host / partial registration only / high / no | two-registry test (TenantChartWeight in A only) | O-8 |

Found independently by both: none (Codex read the pack's statements of K1–K7 before reviewing; C1 and the second E2 blocker are Codex's alone).

### Codex calls
| Run | Call | Attempt | Started | Duration | state | validation | Exit | Model / effort requested | Effective effort | reasoning tokens | Search | MCP calls | activity (cmds / non-zero / file_change / outside-repo) | Pack | CLI |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 6e2ced | tests | a1 (requirement-only directory) | 08:14:12 | 5.1 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 3,338 | off | 0 | 3 / 0 / 0 / 1 | REQUIREMENT.md (brief + design without §4.11 + M1 excerpts) | 0.153.4 |
| 6e2ced | diffreview | a1 | 09:04:42 | 9.3 min | success | ok (candidate unchanged during review) | 0 | gpt-6-astra / xhigh | not observable | 5,590 | off | 9 (KB lookup 1, get_fix 1, dxdocs search 5, get 2) | 16 / 3 / 0 / 7 | v1 (383 KB) | 0.153.4 |

Input tokens: tests 144,424 (cached 107,904); diffreview 2,386,137 (cached 2,178,304). The `tests` isolation is by convention (absolute reads
remained possible); its out.md cites only REQUIREMENT.md.

### Setup checks (Phase 0)
| # | Item | Result |
|---|---|---|
| 1 | BASH_MAX_TIMEOUT_MS | present (2400000) |
| 2 | Read-only query connection (HARD) | not applicable: no database used, no query run |
| 3 | Repo trusted (HARD) | present (the hook fired, item 5) |
| 4 | Manifest (HARD) | 7/7 hashes match in the worktree and the main repo |
| 5 | Hook fires (HARD) | `git push --dry-run` blocked by collab-guard; a harmless Monitor (`date`) ran unblocked |
| 6 | collab.rules | file present; the execpolicy check was not run — a script that assembled the command was refused by the auto-mode classifier and not retried |
| 7 | prompt-input | saved; AGENTS.md present; CLAUDE.md body absent (pasted as pack item 0) |
| 8 | Tool boundary (HARD) | no MCP tool of this agent writes a database, migrates, deploys, pushes or restarts; KB write tools not used |
| 9 | Tool parity (HARD) | KB with the 9 read tools (`enabled_tools`), dxdocs; DEVIATION as in earlier runs: node_repl and cua_repl enabled for Codex — Codex ran node scripts through powershell, no node_repl/cua_repl MCP call |
| 10 | Models (HARD) | gpt-6-astra listed with low…ultra incl. medium and xhigh |
| 11 | Run setup | run 6e2ced, salt (unused), codex from PATH, codex-cli 0.153.4; doctor overall "warning"; login ChatGPT |
| 12 | Snapshot | worktree HEAD 40405d1, branch feature/edit-draft-library, status clean at start |
| 13 | Policy drift | CLAUDE.md identical to the main repo (AF56E4B3…); AGENTS.md has the template section plus the guardrails section (as before); `~/.codex/config.toml` effort medium, each call overrides with xhigh |
| 14 | Web search | off; no web_search item in either call |

### Redaction
None needed: no personal data read or sent. Withheld from Codex by rule (single-model security, not personal data): the writer diff and the
owner/access seam files.

### Inputs Codex did not have
The writer diff (described: 3 declarations internal + 1 doc line; 65 predicate lines identical) and the unchanged owner/access seam files — the O-3
change and the seams' behaviour are therefore not cross-checked (single-model, owner review). The candidate full-suite run (still running during the
review; the two filter runs were available). Claude's private context: the auto-memory index (not relied on).

### Passes used
2 cross-model calls (`tests`, `diffreview`), 2 attempts, no retries.

### KB
No `log_new_fix` in this run: the KB server writes into the CareCrew repository, and the brief forbids work in
that repository; owed when the owner accepts M2 (one record for the Blazor package: RCL static asset path, D9 replacement, O-2/O-3).

### Clean-up
`artifacts/claude-test/20261002-6e2ced` deleted after the runs. The decompiled Llamachant behaviour notes are not in the repo.

## 15. Not verified / open questions
- Runtime in a host: XAF module order and controller discovery from the library assembly, the popup and list rendering (D9 editor), the tick
  controller on the library row, the 入力控 header action and gear entry, badges, the static asset being served (dev and published) and its
  `asp-append-version` hash, Japanese texts on screen — the M4 browser pass; nothing ran in a browser or a host.
- `dotnet publish` output containing `_content/Xaf.EditDraft.Blazor/...` (no publish run).
- Model differences already stored per user for the generated views (`EditDraftListItem_ListView`, `EditDraftRestorePlan_Items_ListView`, …): the
  view ids are unchanged, the class node names are now `Xaf.EditDraft.Blazor.*`; effect on stored user differences not observed.
- XAF 26.1.4 rendering of a read-only StringPropertyEditor with RowCount 2/3 (documented as DxMemo for RowCount > 1; not seen).
- The owner/access seams and the writer change: single-model, not cross-reviewed.
- Codex could_not_determine (diffreview): runtime activation, effective editor selection, popup lifecycle and stylesheet serving; exact 26.1.4 visual
  behaviour; the withheld writer and security behaviour; the full-suite result (now in §7b); the final docs (now written); production behaviour.
- Codex could_not_determine (tests): the chosen NP pattern/editor, baseline popup presentation, model placement, bridge disposition, seam
  implementations and chart residue (now §2–§5); whether "no appsettings key name" covers the Core default (O-10); the CSS route (brief's path used).
