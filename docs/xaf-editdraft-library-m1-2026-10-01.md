# Xaf.EditDraft library — milestone M1 (Core package + CareCrew adapters)

Run `2026-10-01-editdraft-m1-c3f4de` (collaborator: Claude Opus 5.5 implements; Codex gpt-6-astra at xhigh reviews, read-only).
Worktree `C:\Users\owner\source\repos\CareCrew-library`, branch `feature/edit-draft-library`, base `6bfbf876c4f35a6755026d1c38aa6b35cbcc6deb`
(= master 6bfbf87). Committed 2026-10-02 as 1fe4865 (feat) / 61f8d91 (test) / 40405d1 (docs); the text below was written before
the commit. No deploy, no database, no schema change.
Design: `docs/xaf-editdraft-library-design-2026-10-01.md` (committed in 40405d1). Scratch: `%LOCALAPPDATA%\collab\2026-10-01-editdraft-m1-c3f4de\`.

> Note 2026-10-02 (M2 run `2026-10-02-editdraft-m2-6e2ced`): W41 is no longer red. On the owner's ruling 2026-10-02 (O-1, option a) W41 was
> rewritten in 61f8d91 to compare the persistent member declarations of NHM's `EditDraft.cs` with `Xaf.EditDraft.Core.EditDraftStoreBase`
> (`DraftStoreTypes.cs` stays byte-identical); it passed in the M2 baseline (filter 698 = 694 passed / 4 not executed; full suite 5542 with 5
> failures, none of them W41). The W41 statements in §0, §1, §5b, §7, §8 O-1 and §11 K2 describe the state before that ruling. O-2 and O-3 were
> closed in M2 (`docs/xaf-editdraft-library-m2-2026-10-02.md` §4).

Owner's words (verbatim): "I want to make this a generic library that any project can use. Create a separate branch and make this a
DevExpress data restore feature". Rulings applied: D1-A (NonPersistent base in Core, CareCrew keeps `EditDraft`), D2-A (NHM keeps its
standalone `EditDraft.cs`), D3 (platform-agnostic controllers in Core), D4 (DI-registered registry, no static state), D5–D14 as the design
recommends, security SD-1..SD-3 exactly as design §4.11.

## 0. Combined answer

The platform-agnostic half of the 入力控 engine now builds as its own project, `Xaf.EditDraft.Core` (net8.0, DevExpress 26.1.4 from the
central versions), referenced by CareCrew.Blazor.Server and by `NursingHome_Chart.Module`, whose `EditDraft` is now a one-line subclass of
the library's `[NonPersistent] EditDraftStoreBase`; its XPO mapping (table, 21 columns, sizes, key, lock field, 4 named indexes, captions)
is identical before and after, checked by a dump taken before any change and pinned by a new test. Seventeen files moved into Core
(engine, writer, switch, list rules, DetailView capture controller, row context) and eleven new library files add the seams: store
registration, DI registry, owner and record-access seams, switch section option, `TimeProvider` clock, log facade, ja/en texts, neutral
restore row, per-database table cache and a renamed copy of the write slot. CareCrew supplies its policies (23 chart + 5 wave-1), the
StaffMember/GeneralUser owner rule, the 事業所 rule, the `EditDraftCapture` section, the GlobalLogger sink and the Japanese texts, so
behaviour is meant to be unchanged; every test identity of the baseline is present, the only changed outcome is W41 (the NHM byte-mirror
test), which D2-A makes red by design and which goes to the owner. The five Blazor-bound controllers, the popup models (Llamachant) and
the CSS stay in CareCrew for M2; nothing has been run in a browser.

## 1. Status

Implemented, uncommitted, 2026-10-01. Builds green (Core alone, CareCrew.Blazor.Server, CareCrew.Win, Rostering.Tests). Tests: see §5.
One red test escalated (W41; superseded 2026-10-02, see the note at the top). Codex diffreview: one pass, 4 findings; 1 fixed after the review (not cross-reviewed), 1 test run added,
2 escalated. Next: owner decisions in §8, then git-committer (topic branch exists) — and M2.

## 2. What moved

Paths: `E/` = `CareCrew.Blazor.Server/Infrastructure/EditDrafts/`, `C/` = `CareCrew.Blazor.Server/Controllers/Common/EditDrafts/`,
`X/` = `Xaf.EditDraft.Core/`. All moved and new files are UTF-8 without BOM, CRLF (checked).

| From | To | Change beyond namespace/usings |
|---|---|---|
| E/EditDraftAccessRule.cs | X/ | doc only (SINGLE-MODEL) |
| E/EditDraftCodec.cs | X/ | none |
| E/EditDraftDisplay.cs | X/ | はい/いいえ/（空）/（不明） via text set |
| E/EditDraftListRules.cs | X/ | `IsGeneric` from Core; provenance strings via text set; BadgeSet/BadgeNotifier now library types |
| E/EditDraftMemberDecision.cs | X/ | none |
| E/EditDraftMembers.cs | X/ | none |
| E/EditDraftOfferMerge.cs | X/ | rows are the neutral `EditDraftRestoreRow`; DuplicateNote via text set |
| E/EditDraftPayload.cs | X/ | status texts via text set (JSON contract untouched) |
| E/EditDraftRegistry.cs | X/ | static `Default` removed; `Create`, `Freeze`, `IsFrozen`, `Empty` (D4) |
| E/EditDraftRestoreGuard.cs | X/ | GlobalLogger → EditDraftLog |
| E/EditDraftRestorer.cs | X/ | neutral rows; （空） via text set; logger |
| E/EditDraftSwitch.cs | X/ | parse copied from CareTreeDraftCaptureSwitch (`IsOn`); configurable section (`EditDraftSwitchOptions`, `In`); `EditDraftOfferRequests` moved out to E/EditDraftOfferRequests.cs |
| E/EditDraftTypePolicy.cs | X/ | `static IsGeneric(policy)` added (was EditDraftWavePolicies.IsGeneric, which now forwards) |
| E/EditDraftWriteGate.cs | X/ | none |
| E/EditDraftWriter.cs | X/ | generic `EditDraftWriter<TStore>` + `IEditDraftWriter` + facade `EditDraftWriter` (fails closed with no store); table name from the store's XPO class info; per-database probe cache; logger (SINGLE-MODEL) |
| C/EditDraftCaptureControllerBlazor.cs | X/ | registry/owner/clock from the host's services; `IsGeneric` from Core; `DraftWriteSlot`; nested snapshot/mark and the static write loop made public (the CareCrew list capture uses them until M2); context separator via text set; snapshot carries its clock (post-review D1); logger |
| C/EditDraftRowContext.cs | X/ | public; `DraftWriteSlot`; `BuildSnapshot(seed, clock = null)` |

New in `X/` (11): `Xaf.EditDraft.Core.csproj`, `EditDraftCoreModule.cs` (exports nothing; controllers from the assembly; ILogger default sink
when the host chose none), `EditDraftStoreBase.cs` (19 members, 4 indexes, `[NonPersistent]`, `[DeferredDeletion(false)]`), `DraftWriteSlot.cs`
(copy of `AttendanceDraftSlot<T>`, D5), `EditDraftRestoreRow.cs`, `EditDraftServices.cs` (store registration, switch options, clock, registry
lookup, DI extensions), `EditDraftLog.cs`, `EditDraftTexts.cs`, `EditDraftTableCache.cs`, `EditDraftOwnerSeam.cs` and `EditDraftAccessSeam.cs`
(SINGLE-MODEL).

New in CareCrew (3): `E/CareCrewEditDraftSetup.cs` (composition: store, 28 policies, owner seam, switch section, GlobalLogger sink, ja texts),
`E/EditDraftRestoreRows.cs` (neutral row ↔ `TenantChartDraftRestoreItem`, every field, one new item per row), `E/EditDraftOfferRequests.cs`.

Edited in place, CareCrew (21, csproj included): `E/EditDraftOwner.cs` (rule/info moved to Core; `CareCrewEditDraftOwnerResolver` added — SINGLE-MODEL),
`Infrastructure/EditDraftAccess.cs` (member-write check forwards to Core; `CareCrewEditDraftRecordAccess` added — SINGLE-MODEL), the 6 policy
files (using; `IsGeneric` forwards), `TenantChartDraftTypePolicy.cs` (the 23 chart policies cached here and registered as the same
instances), `TenantChartDraftPolicy/Payload/Display/Restorer.cs` (usings; chart `BuildItems` maps the neutral rows), the 6 CareCrew controllers
(using; `EditDraftServices.Registry(...)` instead of `EditDraftRegistry.Default`; `EditDraftStoreBase` in the restore controller's tuples;
neutral-row mapping in BuildPlan/Apply; list capture passes the host clock), `Startup.cs` (module + `AddCareCrewEditDraft` + record-access seam;
badge notifier type), `CareCrew.Blazor.Server.csproj` (ProjectReference). Module (2): `EditDraft.cs`, `NursingHome_Chart.Module.csproj` (§10).
`CareCrew.sln` (+1 project). Untouched: `Model.xafml`, appsettings*, `create-draft-purge-job.sql`, `DraftStoreTypes.cs`,
`AttendanceDraftPermissions.cs`, `AuditTrailExclusions.cs`, `_Host.cshtml`, CSS, `EditDraftModels.cs`, `EditDraftListBridge.cs`, the chart
controllers, 勤怠 (its slot is the original).

Counts: 17 moved to Core, 11 new in Core, 3 new in CareCrew, 21 CareCrew files and 2 Module files edited, 1 solution file; tests: 5 edited
(Wave1, Wave1b, Engine, AccidentGroup, TestEnvironment) + csproj, 4 new (StoreMapping, LibraryIsolation, LibrarySeam, CareCrewRegistry). `git diff HEAD --stat` (tracked only): 47 files, +207 / −2,884 (the 17 moved files count as deletions there; they are
untracked under `Xaf.EditDraft.Core/` until committed).

Placement against the brief (D3): the brief lists "capture / list-capture / restore / list controllers" for Core "platform-agnostic". Only the
DetailView capture controller and the row context have no `DevExpress.ExpressApp.Blazor` dependency; the list-capture (DxGridListEditorBase,
IGridEditingLifeCycle), restore (ITabbedMdiMainFormTemplate), list (header toolbar, IModelOptionsBlazor) and badge (DxGridListEditor) controllers
do (design E3). They stay in CareCrew and move to `Xaf.EditDraft.Blazor` in M2, as the design's M2 row says. The popup controllers and
models stay because of Llamachant (`NPOBase`, `LabelPropertyEditor`), as the brief says.

## 3. The store class, before vs after

Scratch tool `artifacts/claude-tools-c3f4de/StoreDump` (XPO `ReflectionDictionary` + `GetDataStoreSchema`) run on the unchanged tree
(`baseline/store-mapping.txt`) and on the candidate (`after/store-mapping.txt`). Compared section — type full name, assembly, IsPersistent,
TableName `EditDraft`, IdClass, key `oid`, OptimisticLockField, no GCRecord, DeferredDeletion off, class caption 入力控, the 21 persistent
members (19 + Oid + OptimisticLockField) with mapping field, CLR type, size (ObjectType 100, ContextText 200, ViewId 100, Payload unlimited,
OriginHost 100, LastError 2000), key/read-only/delayed flags, `[Indexed]` and captions, and the schema (pk Oid, 21 columns, indexes
`uxEditDraft_Key` unique (DraftKey, OwnerUserOid), `iEditDraft_Owner_Expiry` (OwnerUserOid, ExpiresOn), `iEditDraft_Target`, `iEditDraft_Expiry`)
— 56 lines before, 56 after: IDENTICAL. Only the declaring class of the 19 members changed (now `Xaf.EditDraft.Core.EditDraftStoreBase`).
Pinned permanently by `EditDraftStoreMappingTests` (passes). This proves the XPO mapping; the Dev2/production columns, SQL types and
nullability are the owner's read-only M0 query (not run).

The member captions (入力者, 記録種別, 対象) stay as Japanese `ModelDefault` attributes on the base in M1 so CareCrew's model is unchanged;
English attributes with an embedded ja aspect are M3 localisation work.

## 4. Seams (library default → CareCrew implementation)

| Seam | Library default | CareCrew (registered by `CareCrewEditDraftSetup.AddCareCrewEditDraft` + Startup) |
|---|---|---|
| Store | none: the facade fails closed (nothing written or read, table "absent", one warning) | `AddEditDraftStore<EditDraft>()` (table EditDraft) |
| Registry (D4) | `EditDraftRegistry.Empty` (frozen, admits nothing) | DI singleton: 23 chart + 5 wave-1 policies, frozen |
| Owner (SEC-1, single-model) | `XafLoginEditDraftOwnerResolver`: `SecuritySystem.CurrentUserId` as non-empty Guid, else none; flag false | `CareCrewEditDraftOwnerResolver` → unchanged `EditDraftOwner.Current` (StaffMember lookup, D6 GeneralUser refusal) |
| Record access (SEC-2, single-model) | `XafSecurityEditDraftRecordAccess`: no rule beyond XAF security (records are loaded through the secured space first) | `CareCrewEditDraftRecordAccess` → unchanged `EditDraftAccess.IsRecordVisible` (事業所). M1: registered; its callers are the CareCrew controllers, which still call `EditDraftAccess` directly until M2 |
| Member write (S3 check 3) | `EditDraftMemberAccess` (moved unchanged) | `EditDraftAccess.CanWrite/NotWritable` forward to it |
| Switches | section `EditDraftCapture`, keys Enabled / Types:&lt;id&gt;:Enabled / ListViews:Enabled, re-read every use, fail closed (`IsOn` = the CareTree parse) | `EditDraftSwitchOptions { Section = "EditDraftCapture" }` |
| Clock | `TimeProvider.System`, local wall time (`EditDraftClock.Now`) | none registered → system clock |
| Log | ILogger "Xaf.EditDraft" set by the module when the host chose no sink; Trace before that | `GlobalLoggerEditDraftLog` (same lines, same `[EditDraft]` prefix) |
| Texts | English, chosen explicitly, never by culture | `EditDraftTexts.Use(Japanese)` = today's strings |
| Restore row | neutral `EditDraftRestoreRow` | mapped to `TenantChartDraftRestoreItem` (chart popup, generic popup until M2) |

Writer visibility: §4.11 says "the writer stays internal to Core". In M1 it is public, because the CareCrew restore/list/badge/popup
controllers that call it are still in CareCrew; an `InternalsVisibleTo` naming CareCrew.Blazor.Server would put the application's name into
the library. Every writer method still takes the owner and filters on it in the statement (22 predicate/guard lines compared before vs after:
identical except one empty-list type). Owner decision O-3 in §8.

## 5. Tests

### 5a. Codex expectations first
The `tests` call ran in a requirement-only directory (brief + design, §4.11 withheld) before any code was shown: 34 expectations (E1–E34)
and a could_not_determine list. New tests implement E1–E22 where M1 can test them (labels in the test names).

### 5b. Identity comparison (identity = class + test name incl. TestCase arguments, case-sensitive; outcome multiset)
Baseline taken on the unchanged tree before any edit (`baseline/*.trx`). FINAL = the final candidate bytes (`final/*.trx`).

| Run | Baseline | Final | Missing | Changed outcome | Newly skipped | New |
|---|---|---|---|---|---|---|
| Rostering full (28 m 48 s) | 5504: Passed 5488, NotExecuted 11, Failed 5 | 5542: Passed 5525, NotExecuted 11, Failed 6 | 0 | W41 Passed → Failed | 0 | 38, all passed |
| Rostering draft/golden filter | 433: Passed 429, NotExecuted 4 | 471: Passed 466, NotExecuted 4, Failed 1 | 0 | W41 | 0 | 38, all passed |
| AuditTrail.Tests | 7 passed | 7 passed | 0 | 0 | 0 | 0 |
| Search.Tests | 106 passed | 106 passed | 0 | 0 | 0 | 0 |
| CareTree.Tests | 457 passed, 27 NotExecuted | 457 passed, 27 NotExecuted | 0 | 0 | 0 | 0 |
| Payments.Tests | 54 passed | 54 passed | 0 | 0 | 0 | 0 |

(Result counts are TRX results: a TestCase name that differs only by case is its own identity. The `dotnet test` summary for the final full
run reads Total 5537: Passed 5525, Failed 6, Skipped 6; the summary leaves out 5 of the 11 NotExecuted TRX results — the same gap as in the
baseline (Total 5499, Skipped 6). Cause not investigated.)

Every difference explained:
- **W41** `EditDraftWave1WiringScanTests.W41_the_NHM_mirror_of_EditDraft_and_DraftStoreTypes_is_byte_identical`: compares CareCrew's
  `EditDraft.cs` byte-for-byte with NHM's. D1-A changes the CareCrew file (base class); D2-A keeps NHM's standalone. Failed in the full run,
  failed again alone (solo rerun, `after-quick/w41-solo.trx`), failed in the final filter run. Not revised (its expectation, not its path,
  would have to change). Escalated: §8 O-1.
- **New identities**: the 4 new files (`EditDraftLibraryIsolationTests` 6, `EditDraftLibrarySeamTests` incl. a 15-case parse table,
  `EditDraftStoreMappingTests` 2), all passed.
- **Pre-existing failures, unchanged** (identical before and after): 4 × `AuditTrailExclusionWiringTests` ("must be able to find the repository
  root" — they look for a `.git` directory; a worktree has a `.git` file) and `RosterTelemetryRecorderTests.MultiDepartment_CompletedGeneration_
  PersistsOneRecord_WithColumnsAndParseablePayload`. Follow-ups, not fixed here (scope).
- Golden file `NursingHome_Chart.Rostering.Tests/Golden/TenantChartDraft.golden.txt`: SHA-256 `72325EE15DD4E28A2AA4C1C19B1FCA84401E7F459332FB22B42C1C08358C3144`,
  1,767 lines, unmodified; its tests pass — the chart restore plans through the neutral-row mapping are byte-identical.

### 5c. Test edits (mechanical: paths, literals naming moved symbols, reflection; expectations unchanged — D6)
- 28 source-text reads: every read of a moved file now points at `Xaf.EditDraft.Core/…` (capture controller ×6, writer ×3, switch ×1,
  `EditDraft.cs` → `EditDraftStoreBase.cs` for `HasExpired`); reads of files that stay are unchanged. No read can fall into `Assert.Ignore`:
  the 471-test filter shows no new skip.
- Literals that name a moved symbol: `EditDraftWavePolicies.IsGeneric(policy) && isRoot && !isNew` → `EditDraftTypePolicy.IsGeneric(...)` (W9);
  `d.ExpiresOn = EditDraft.CalculateExpiry(now);` → `EditDraftStoreBase.CalculateExpiry(now)` (W43); `PolicyForListView(EditDraftRegistry.Default, …)`
  → `PolicyForListView(EditDraftServices.Registry(Application?.ServiceProvider), …)` (E23); Startup `AddScoped<Infrastructure.EditDrafts.EditDraftBadgeNotifier>`
  → `AddScoped<Xaf.EditDraft.Core.EditDraftBadgeNotifier>` (E25).
- `EditDraftRegistry.Default` (26 uses in 4 files) → test helper `CareCrewRegistry.Default` = `CareCrewEditDraftSetup.CreateRegistry()` once per
  process (what the DI singleton holds); the `BeSameAs` checks hold because the chart policies are the registered instances.
- OfferMerge tests build `EditDraftRestoreRow` instead of `TenantChartDraftRestoreItem` (same fields, same assertions).
- Retired-slot reflection fixture (W67): controller type `Xaf.EditDraft.Core.EditDraftCaptureControllerBlazor`, `BindingFlags.Public` (the members
  are public now), slot type `DraftWriteSlot<>`, writer `new EditDraftWriter(provider, typeof(EditDraft))` so the attempt still reaches the
  counting scope factory.
- `using Xaf.EditDraft.Core;` added in 3 test files; Wave1b's `using CareCrew.Blazor.Server.Controllers.Common.EditDrafts;` replaced by it.
- `TestEnvironment` (the existing assembly SetUpFixture) applies CareCrew's statics (GlobalLogger sink, ja texts) — the same call Startup makes.
  A first version used a new SetUpFixture named `EditDraftLibraryTestSetup`; with it every `FullyQualifiedName~EditDraft` filter selected the
  whole namespace (5,536 tests). Removed before the review.
- Test csproj: the two Compile links of the moved controller files removed; ProjectReference to `Xaf.EditDraft.Core` added (the engine is tested as
  the compiled library).

### 5d. New tests (expectations from Codex tests a1)
`EditDraftStoreMappingTests` (E1–E4: mapping identical, members on the base, one-line subclass); `EditDraftLibraryIsolationTests` (E5 referenced
assemblies of Core contain no NursingHome_Chart/CareCrew/Progress/Llamachant/CareTree/DevExpress.ExpressApp.Blazor; E6 csproj has no
ProjectReference/linked source, exactly the 5 packages, no InternalsVisibleTo; E7 engine types compiled in Core, no type named `EditDraft`; E8 module
exports nothing, collects exactly the capture controller, Startup registers the module once, no file name exists in both host and library;
E19/E20 the slot copy equals the application's slot text with the type renamed); `EditDraftLibrarySeamTests` (E9 two registries isolated in both
orders, no static `Default`, frozen/Empty; E10 same short name refused; E11 CareCrew's 28 policies and chart wrappers return the registered
instances, DI composition resolves store/registry/owner seam/switch section/sink/ja; E12/E13 log lines pass through unchanged, failing sink
harmless, default Trace; E14/SEC-6 copied parse = CareTree parse over 15 inputs, section option, re-read; E16 ja set byte-identical, explicit
choice survives a culture change; E17/E18 row mapping keeps all 11 fields and distinct objects; E21 clock local time + Kind Local, 7-day expiry;
E21/D1 snapshot carries its clock; E22 per-database cache in both orders, failed probe = absent; SEC defaults fail closed; writer without a
store fails closed).

## 6. Builds (all `--artifacts-path artifacts/claude-test/20261001-c3f4de`, logs in `builds/` and `final/`)
- `Xaf.EditDraft.Core` alone: exit 0, 0 warnings, 0 errors.
- `CareCrew.Blazor.Server`: exit 0, 0 errors (full build 45 warnings, none in a touched file).
- `CareCrew.Win`: exit 0, 0 errors, 1 warning (not in a touched file).
- `NursingHome_Chart.Rostering.Tests`: exit 0.
- Store dump tool before/after: exit 0 (§3).

## 7. Codex review and post-review edits

Diffreview a1 (one pass) reviewed everything except the six single-model files. Findings and outcome:

| Codex | Finding | Claude's check | Outcome |
|---|---|---|---|
| D1 | The host clock is lost in `RetiredFreshStart` (system clock) and in the list capture's `BuildSnapshot` | Confirmed by source read | FIXED after the review: `DraftSnapshot.Clock` (init property; positional shape unchanged), set by both BuildSnapshot paths, used by `RetiredFreshStart`; list capture passes `EditDraftServices.Clock(...)`. Test `E21_D1_…`. NOT cross-reviewed |
| D2 | `TenantChartDraftTypePolicy` keeps a static dictionary of the 23 chart policies, so the chart wrappers do not follow a provider-owned registry | Confirmed (design debt; same 28 instances in CareCrew's single host) | Not changed in M1 (chart path is static and migrates later). Owner question O-2 |
| D3 | W41 red | Same as Claude's finding | Escalated O-1 |
| D4 | The full-suite evidence was taken before the final fixture arrangement | Confirmed | Final full run on the final bytes (§5b) |

Files edited after the review (bytes not seen by Codex): `Xaf.EditDraft.Core/EditDraftCaptureControllerBlazor.cs`, `Xaf.EditDraft.Core/EditDraftRowContext.cs`,
`CareCrew.Blazor.Server/Controllers/Common/EditDrafts/EditDraftListCaptureControllerBlazor.cs`, `NursingHome_Chart.Rostering.Tests/EditDraftLibrarySeamTests.cs`.
All executable checks were re-run on the new bytes (§5b final, §6). A delta review is an owner decision.

## 8. Owner decisions and escalations

- **O-1 W41 (red test).** [Decided 2026-10-02: option (a); W41 now compares the persistent members and passes.] It requires CareCrew's and NHM's `EditDraft.cs` to be byte-identical; D1-A + D2-A make them differ by the base
  class. Options: (a) rewrite W41 to compare the XPO mapping of the two (or the files modulo the base class) — an expectation change the
  owner must authorise; (b) mirror the library into NHM (D2-B/C, rejected for M1). Both models agree no check decides it.
- **O-2 Static chart-policy cache (Codex D2).** Claude: the library holds no static registry (D4 met in the library); CareCrew's chart
  wrappers are static helpers used by the chart screens and keep a cache of the same 23 instances registered in the DI registry, until the
  chart store migrates. Codex: that is static registration state the D4 wording excludes. Decide whether M2/M3 must thread the registry
  through the chart path.
- **O-3 Writer visibility (§4.11 "internal to Core").** Public in M1 (callers still in CareCrew); internal + InternalsVisibleTo
  `Xaf.EditDraft.Blazor` when M2 moves the controllers? (security wording; single-model).
- **O-4 Library defaults chosen in M1**: texts default English (CareCrew selects Japanese at startup); log default ILogger via the module,
  Trace before setup; store captions Japanese until M3 localisation; library version follows `ChartApplicationVersion` (Directory props)
  until the NuGet step.
- **O-5 NHM mirror of `EditDraft.cs`**: under D2-A NHM keeps its file; carecrew-sync must treat the base-class difference as intended (§10).

## 9. What M2 needs
- `Xaf.EditDraft.Blazor` (RCL): the restore, list, list-capture, badge and list-popup controllers, `EditDraftListBridge`, `EditDraftOfferRequests`,
  the popup controllers and models with the two Llamachant dependencies replaced (D9) and chosen by a popup test, a tick/group controller
  for the neutral row, the CSS as a static web asset and the `_Host` link, the module, the DI extension.
- Switch those controllers to the seams: `EditDraftServices.RecordAccess` instead of `EditDraftAccess.IsRecordVisible`,
  `EditDraftServices.CurrentOwner` instead of `EditDraftOwner.Current`, `EditDraftServices.Clock` instead of `DateTime.Now`, `EditDraftLog`
  instead of GlobalLogger, the neutral row in the popup models instead of `TenantChartDraftRestoreItem` (then the chart popup keeps the
  mapping).
- Rename `EditDraftCaptureControllerBlazor` (it is platform-agnostic now); decide O-3.
- M3: localisation (English attributes + ja aspect), slot behavioural equivalence, two-provider availability test against real data layers,
  registry lifecycle, `AuditTrailExclusionWiringTests` worktree root detection.
- M4: Dev2 browser pass on the final build (wave-1, wave-1b, chart checklists). Nothing in M1 was run in a browser.

## 10. The exact Module diff (for the NHM mirror decision)

```diff
--- a/NursingHome_Chart.Module/BusinessObjects/EditDraft.cs
+++ b/NursingHome_Chart.Module/BusinessObjects/EditDraft.cs
@@ -1,8 +1,6 @@
-using System;
 using DevExpress.ExpressApp.Model;
-using DevExpress.Persistent.Base;
-using DevExpress.Persistent.BaseImpl;
 using DevExpress.Xpo;
+using Xaf.EditDraft.Core;
@@ -35,117 +33,16 @@
     /// BaseObject, not CustomBaseObject: its stamping does a FindObject per save, too costly for a
     /// per-edit write (KB fix-497) — the same choice as TenantChartEditDraft.
+    ///
+    /// Xaf.EditDraft library, milestone M1 (owner decision D1-A, 2026-10-01): the 19 members, their sizes and
+    /// the four named indexes are declared on Xaf.EditDraft.Core.EditDraftStoreBase ([NonPersistent]; XPO maps
+    /// them into THIS table). This class keeps the CLR name, the table, the deny rows, the audit exclusion and
+    /// the purge. NHM keeps its standalone copy of this file (D2-A): the two differ by this base class.
     /// </summary>
     [DeferredDeletion(false)]
     [ModelDefault("Caption", "入力控")]
-    public class EditDraft : BaseObject
+    public class EditDraft : EditDraftStoreBase
     {
         public EditDraft(Session session) : base(session) { }
-        ... the 19 members, RetentionDays, CurrentPayloadSchemaVersion, CalculateExpiry, HasExpired, IsPayloadReadable
-        (110 lines, moved verbatim to Xaf.EditDraft.Core/EditDraftStoreBase.cs) ...
     }
 }
--- a/NursingHome_Chart.Module/NursingHome_Chart.Module.csproj
+++ b/NursingHome_Chart.Module/NursingHome_Chart.Module.csproj
@@ -493,6 +493,7 @@
 	  <ProjectReference Include="..\CareTree\CareTree.Client\CareTree.Client.csproj" />
 	  <ProjectReference Include="..\NursingHome_Chart.Rostering\NursingHome_Chart.Rostering.csproj" />
+	  <ProjectReference Include="..\Xaf.EditDraft.Core\Xaf.EditDraft.Core.csproj" />
 	</ItemGroup>
```
NHM (master 7bf13f39) is unchanged: its `EditDraft.cs` still equals CareCrew's pre-M1 file (7 insertions, 110 deletions between them). Its
Debug run creates the same table because the mapping is identical (§3). Recommendation under D2-A: no NHM change; carecrew-sync treats the
CareCrew file as intentionally different.

## 11. Contribution log

### What each model did
- **Claude (Opus 5.5):** Phase 0; baseline (5 test projects, per-test TRX; store dump) before any edit; the Core project, the moves and
  every edit; seams and CareCrew adapters; the single-model security files and their tests; the 4 new test files and the mechanical test edits;
  builds and test runs; the identity comparisons; the D1 fix; this write-up. Found and fixed: the SetUpFixture name that made every
  `~EditDraft` filter run the whole suite. Found and escalated: W41. Observed and left as follow-ups: the 5 pre-existing failures.
  Process slip, caught by its own check: the first full "filtered" runs silently ran the whole suite (the fixture above).
- **ChatGPT (Codex gpt-6-astra, xhigh, codex-cli 0.153.4):** `tests` call — 34 requirement-only expectations before seeing code (they
  shaped the new tests: per-database cache in both orders, two registries in both orders, explicit text choice under a culture change, the
  copied parse table, the isolation proof's blind spots); `diffreview` — 4 findings, all confirmed (1 fixed, 1 test run, 2 escalated).
  Read only; no file_change; stayed out of the withheld security files (its activity filtered them by name).

### Found issues, by tool
| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| K1 | A SetUpFixture named EditDraftLibraryTestSetup made every `FullyQualifiedName~EditDraft` filter select all 5,536 tests | Claude | correct | 34-min "filtered" runs; 470 tests once removed | slow/incorrect filtered runs / certain / executed / no | filtered run count | fixed before review |
| K2/C-D3 | W41 byte-mirror test red under D1-A + D2-A | Claude first; Codex independently | correct | full, solo and final runs | test gate red / certain / executed / no | owner decision | escalated O-1 |
| K3 | 5 pre-existing failures (4 worktree root detection, 1 roster telemetry) | Claude | correct, pre-existing | baseline TRX | none from M1 / — / executed / no | — | follow-up |
| C-D1 | Host clock lost in RetiredFreshStart and list BuildSnapshot | Codex | correct | source read | non-system clocks inconsistent / only with a custom TimeProvider / high / no (CareCrew uses the system clock) | test E21_D1 | fixed post-review, not cross-reviewed |
| C-D2 | Static chart-policy dictionary | Codex | correct as design debt | source read | no CareCrew behaviour change / — / high / no | two-provider chart lookup (not a CareCrew scenario) | escalated O-2 |
| C-D4 | Full suite not run on the final fixture arrangement | Codex | correct | pack §2b | evidence gap / — / high / no | final full run | done (§5b) |

Found independently by both: W41 (coverage, not confidence).

### Codex calls
| Run | Call | Attempt | Path | Started | Duration | state | validation | Exit | PID | Model / effort | Effective effort | reasoning tokens | Search | MCP calls | activity (cmds / non-zero / file_change / outside-repo) | prompt / out sha256 | Pack | CLI |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| c3f4de | tests | a1 | `…\tests\a1` (cwd `tests\req`, `-SkipGitCheck`) | 20:31:26 | 5.8 min | success | ok | 0 | 41332 | gpt-6-astra / xhigh | not observable | 2,898 | off | 0 | 8 / 1 / 0 / 1 (powershell.exe) | CBE8DB01 / 01C3E487 | REQUIREMENT.md (brief + design, §4.11 withheld) | 0.153.4 |
| c3f4de | diffreview | a1 | `…\diffreview\a1` | 22:52:17 | 8.1 min | success | ok (candidate unchanged during review) | 0 | 57060 | gpt-6-astra / xhigh | not observable | 4,870 | off | 6 (KB lookup, dxdocs search/get) | 15 / 4 / 0 / 3 (NHM repo read, powershell.exe, a TRX namespace string) | F6E1BAA5 / E8C22314 | v1 (359 KB) | 0.153.4 |

Input tokens: tests 251,106 (cached 210,176); diffreview 2,504,391 (cached 2,316,544). The `tests` isolation is by convention (absolute reads
remained possible); its out.md cites only REQUIREMENT.md.

### Setup checks (Phase 0; outputs under `…\preflight\`)
| # | Item | Result |
|---|---|---|
| 1 | BASH_MAX_TIMEOUT_MS | present (2400000) |
| 2 | Read-only query connection (HARD) | not applicable: no database used, no query run |
| 3 | Repo trusted (HARD) | present (the hook fired, item 5) |
| 4 | Manifest (HARD) | 7/7 hashes match (`manifest-worktree.txt`) |
| 5 | Hook fires (HARD) | push dry-run blocked by collab-guard; a harmless Monitor ran unblocked |
| 6 | collab.rules | file present with the forbidden `git push` rule; the execpolicy check was indeterminate (PowerShell 5.1 dropped the `--` separator: `matchedRules: []`); the second attempt was refused by the auto-mode classifier and not retried |
| 7 | prompt-input | saved; AGENTS.md present, CLAUDE.md body absent (pasted as pack item 0) |
| 8 | Tool boundary (HARD) | no MCP tool writes a database, deploys, pushes or restarts; KB write tools not used |
| 9 | Tool parity (HARD) | KB with the 9 read tools (`enabled_tools`), dxdocs; DEVIATION as in earlier runs: node_repl and cua_repl enabled for Codex — Codex ran node scripts through powershell, no node_repl/cua_repl MCP call |
| 10 | Models (HARD) | gpt-6-astra listed with xhigh |
| 11 | Run setup | run c3f4de, scratch, salt (unused), codex.exe from PATH, codex-cli 0.153.4; doctor overall "warning"; login ChatGPT |
| 12 | Snapshot | worktree HEAD 6bfbf876, status at start: only the untracked design doc |
| 13 | Policy drift | CLAUDE.md identical to the main repo (AF56E4B3…); AGENTS.md as in the design run |
| 14 | Web search | off; no web_search item in either call |

### Redaction
None needed: no personal data read or sent. Withheld from Codex by rule, not for personal data: the six single-model security files and
design §4.11.

### Inputs Codex did not have
The six security files (by design; their public surface was described in the pack). Conclusions therefore not cross-checked: the owner seam,
the record-access seam, the member-write check, the writer (predicates, facade, per-database cache wiring), `EditDraftAccessRule`, and the
CareCrew owner/access adapters — single-model, for owner review. Claude's private context: the auto-memory index (not relied on).

### Passes used
2 cross-model calls (tests, diffreview), 2 attempts, no retries.

### KB
No `log_new_fix` in this run: the KB server writes into `repos\CareCrew\mcp-blazor-knowledge-base\records`, and the brief forbids work in
`repos\CareCrew`; the record is owed when the owner accepts M1 (suggested: one fix record for the extraction, pointing at this file).

### Clean-up
`artifacts/claude-test/20261001-c3f4de` and the scratch dump tool folder were deleted after the runs; the tool's source is kept in the scratch
folder (`storedump-tool\`), its two dumps in `baseline\` and `after\`.

### Run ledger
Appended to `%LOCALAPPDATA%\collab\ledger.jsonl` by `tools/collab/append-ledger.ps1`:
```json
{"run":"2026-10-01-editdraft-m1-c3f4de","date":"2026-10-01","topic":"xaf-editdraft-library-m1","attempts":[{"call":"diffreview","attempt":1,"state":"success","validation":"ok","accepted":true,"exit":0,"minutes":8.1,"commands":15,"nonzero_exits":4,"outside_repo":3,"file_changes":0,"reasoning_tokens":4870,"output_tokens":13388,"search":false},{"call":"tests","attempt":1,"state":"success","validation":"ok","accepted":true,"exit":0,"minutes":5.8,"commands":8,"nonzero_exits":1,"outside_repo":1,"file_changes":0,"reasoning_tokens":2898,"output_tokens":10783,"search":false}],"findings":{"claude_confirmed":3,"claude_rejected":0,"codex_confirmed":4,"codex_rejected":0,"both":1,"unverifiable":0,"open":4},"correlated_error_events":0,"escalated_to_owner":5,"passes":2,"hook_false_positives":0}
```

## 12. Not verified / open questions
- Runtime: XAF controller discovery and module order in the running CareCrew host, the capture/offer/list/badge behaviour, log lines in
  `C:\Progress\logs`, Japanese texts on screen — a Dev2 browser pass (M4); nothing was run in a browser or a host.
- The ILogger default sink (set in `EditDraftCoreModule.Setup`) is not exercised by a test; CareCrew sets its own sink.
- The writer's per-database cache with a real XPO data layer (the cache is tested with synthetic keys; `DatabaseKeyOf` falls back to the data
  layer object when no connection string is exposed — CareCrew's `ThreadSafe`/`UseSharedDataStoreProvider` setup was not observed).
- Dev2/production `dbo.EditDraft` columns, types, nullability, indexes and deny rows (owner M0 query).
- DevExpress 26.1.4 dependency list beyond what the restore showed (Core restored DevExpress.ExpressApp/Xpo/Persistent.*, Data, DataAccess and
  Microsoft.Extensions.* 8.0.x transitively; no package added).
- Codex could_not_determine (diffreview): runtime controller discovery and module order; database state; the withheld security
  implementation; the writer's real provider-to-cache wiring; production switch values and clock registrations.
