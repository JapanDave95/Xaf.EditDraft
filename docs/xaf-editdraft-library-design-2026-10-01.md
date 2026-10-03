# Xaf.EditDraft library — design (generic DevExpress XAF data-restore feature)

Run `2026-10-01-editdraft-library-c8ceda` (collaborator: Claude Opus 5.5 + Codex gpt-6-astra at xhigh). Branch
`feature/edit-draft-library`, worktree `C:\Users\owner\source\repos\CareCrew-library`, snapshot `6bfbf876c4f35a6755026d1c38aa6b35cbcc6deb`
(= master 6bfbf87). Design only: no code, no build of the projects, no database, no commit. This file is uncommitted.

Owner's words (verbatim): "I want to make this a generic library that any project can use. Create a separate branch and make
this a DevExpress data restore feature". Owner decisions already taken: projects inside CareCrew.sln first; platform-agnostic
Core + Blazor package; names Xaf.EditDraft.Core / Xaf.EditDraft.Blazor; the library handles the generic store, charts migrate later,
勤怠 stays separate.

## 0. Combined answer

The generic edit-draft engine can become `Xaf.EditDraft.Core` + `Xaf.EditDraft.Blazor` mostly by moving files: 15 of the 37
feature files go to Core and 6 to Blazor without dispute, and the chart path already reaches the engine only through four
forwarders, so `TenantChartEditDraft` keeps working unchanged. Four couplings must be cut first: the popup row type and its tick
controller (chart code), the write slot (`AttendanceDraftSlot`, a 勤怠 class), `GlobalLogger` (104 calls in 13 files), and two
Llamachant dependencies of the popup models (`NPOBase` and the `LabelPropertyEditor` alias). The one decision that is not
mechanical is the persistent store class: Claude recommends a library `[NonPersistent]` base with CareCrew keeping
`NursingHome_Chart.Module.BusinessObjects.EditDraft` (same CLR name, table, columns, deny rows), while Codex recommends a
library-named class mapped to the same table — the second needs a deny-row transition seeded through NHM, which is a security
item for the owner. The existing tests cannot stay "unchanged except namespaces" (28 source-text reads that turn into silent
SKIPS when files move), so the build plan gates on test identities, not totals, and runs the Dev2 browser pass on the final
arrangement. What this design does not do: support EF Core, non-Guid keys or non-SQL-Server databases in the first release,
migrate the chart store, or touch 勤怠.

## 1. Status

Design proposed, 2026-10-01. Not implemented. Five design choices and several smaller ones are owner decisions (§8). Nothing
here changes production; the first code milestone needs the owner's go.

## 2. Design basis (the "root cause" of the work) and evidence

The engine was written inside CareCrew.Blazor.Server and is already close to library shape; what ties it to CareCrew is a
short, enumerable list of references. A library extraction is therefore MOVE + SEAM, except for the store class, whose CLR
identity is used by three existing controls.

| # | Fact | Evidence (file:line at 6bfbf876) | Found by |
|---|---|---|---|
| E1 | Engine files reference CareCrew only through GlobalLogger, the chart popup row type, the store class, the registry's default list, two policy helpers and two owner/switch helpers | parity pack 2a coupling grep; `EditDraftRegistry.cs:18-26`; `EditDraftRestorer.cs:5-6,19-63`; `EditDraftOfferMerge.cs:4,22-38`; `EditDraftWriter.cs:8,45`; `EditDraftOwner.cs:3,48-54,71`; `EditDraftSwitch.cs:5,31-36` | both |
| E2 | The DetailView capture controller and the row context compile without DevExpress.ExpressApp.Blazor (the test project links them with only a Module reference) | `NursingHome_Chart.Rostering.Tests.csproj:789,827-831` | Claude |
| E3 | Five controllers are Blazor-bound (MDI template, header toolbar, DxGridListEditor, GridModel.CustomizeElement, IModelListViewBlazor) | `EditDraftRestoreControllerBlazor.cs:7,41,83,94,104`; `EditDraftListControllerBlazor.cs:38,40,88-150`; `EditDraftListCaptureControllerBlazor.cs:6,131,136`; `EditDraftListBadgeControllerBlazor.cs:4,7-8,122-138`; `EditDraftListPopupControllerBlazor.cs:5,80` | both |
| E4 | The popup row type is the Module's `TenantChartDraftRestoreItem`, shared with the chart popup, and its tick/group controller lives in chart code | `EditDraftModels.cs:32,48,56`; `TenantChartDraftRestorePopupControllerBlazor.cs:89-141`; `BlazorGridCustomizationController.cs:34` | both |
| E5 | The capture writes through `AttendanceDraftSlot<T>` (a pure 270-line class also used by 勤怠 and chart capture), and the row context uses the capture controller's internal nested types | `AttendanceDraftSlot.cs:1,33`; `EditDraftRowContext.cs:24,42,56-57`; `EditDraftCaptureControllerBlazor.cs:352-366,419,485,534,650`; consumed at `EditDraftListCaptureControllerBlazor.cs:370-445,497` | both (internal access: Codex) |
| E6 | The popup models depend on Llamachant: base class `NPOBase` and editor alias `LabelPropertyEditor`; CareCrew does not define that alias; the string occurs in `Llamachant.ExpressApp.Module.Blazor` 26.1.3.1 | `EditDraftModels.cs:10,30,34-40,78-83,101,120-124`; grep of `CareCrew.Blazor.Server/Editors` (no match); byte search of the package DLL (2 hits, not decompiled) | NPOBase: both; alias: Codex raised, Claude confirmed |
| E7 | The store's deny rows and audit exclusion are keyed on the CLR type; the rollback names TargetType by full name; CareCrew never seeds (DatabaseUpdateMode.Never), only the NHM Debug run does | `AttendanceDraftPermissions.cs:85-93,143-148`; `AuditTrailExclusions.cs:54,67-75`; `DraftStoreTypes.cs:50-67`; `BlazorApplication.cs:21-23`; NHM `NursingHome_Chart.Module/DatabaseUpdate/Updater.cs:125` | both (consequence: Claude) |
| E8 | The writer is XPO- and SQL-Server-specific (XPObjectSpace cast, bracketed T-SQL, `OBJECT_ID(N'dbo.…')`), Guid/BaseObject-keyed, and caches table presence in static fields for the whole process | `EditDraftWriter.cs:54,115-117,156-157,170,174-204`; `EditDraftCodec.cs:21`; `EditDraftMembers.cs:104`; `EditDraftRestorer.cs:146`; `EditDraftCaptureControllerBlazor.cs:265,499-501` | Codex |
| E9 | Tests read source files by repo path and assert literal text; a missing file makes the test call `Assert.Ignore` (a skip, not a failure); one test reflects over the capture controller's nested types | `EditDraftWave1Tests.cs:75-79,619-640,791-795`; `EditDraftWave1bTests.cs:174-183,728-761`; 28 `Source()` reads in Wave1/Wave1b | Codex |
| E10 | CareCrew is net8.0 / DevExpress 26.1.4; NHM is net10.0 / DevExpress 26.1.3; DevExpress packages pin each other exactly (`[26.1.3]` in the 26.1.3 nuspec) | `Directory.Build.props:3-4`; `Directory.Packages.props:3-4`; NHM `Directory.Build.props:3,7`; local nuspec read (parity v2 D2) | both |
| E11 | While a namespace `Xaf.EditDraft` exists, code inside the `Xaf` namespace tree that names a type `EditDraft` by its simple name through a file-level `using` gets CS0118 ("is a namespace but is used like a type"); the same code in another namespace compiles. So no library type may be named `EditDraft` (Codex review: a `using` declared inside the namespace block would change the lookup — the tested arrangement is file-level) | executed scratch compile (parity v3 E1) | Claude |
| E12 | The wave-1 "20 columns" and "Dev2 deny rows" lines are an owner RUNBOOK, not observed results; the class declares 19 members (+ Oid + OptimisticLockField = 21 expected) | `docs/generic-edit-draft-wave1-2026-10-01.md:108,114`; `EditDraft.cs:39,55-144`; one commit only (9f3fb58) | count: both; runbook reading: Codex |

## 3. Ruled out

| Hypothesis | Verdict | Evidence |
|---|---|---|
| Moving namespaces alone produces independent packages | Ruled out | E1, E4-E6, E9 |
| All generic controllers can sit in Core unchanged | Ruled out | E3 |
| Core can reference only DevExpress.ExpressApp + Xpo | Ruled out as worded; it also needs DevExpress.Persistent.Base and .BaseImpl.Xpo (BaseObject) and Newtonsoft.Json — all already in `Directory.Packages.props` (no NuGet add). IConfiguration/ILogger/DI abstractions are dependencies of DevExpress.ExpressApp 26.1.3 (nuspec); 26.1.4 not checked | `EditDraftCodec.cs:3,21`; `EditDraftPayload.cs:4`; parity v2 D2 |
| The chart store must move or be abstracted now | Ruled out (owner decision; not needed) | chart controllers/writer use no `EditDraft*` type (parity v2 D4); forwarders `TenantChartDraftRestorer.cs:22-47` |
| The 5 wave policies or 23 chart policies belong in the library | Ruled out: each names a Module type | coupling grep (`TenantCaseEditDraftPolicy.cs:2,13` etc.) |
| Tests can pass "unchanged except namespaces" | Ruled out | E9 |
| A test-count gate proves preserved coverage | Ruled out: moved files make tests skip | E9 (`Assert.Ignore`) |
| Keeping the table name requires keeping the class name | Ruled out: `[Persistent("EditDraft")]` also keeps it; keeping the CLR identity is a separate concern | XPO 26.1 PersistentAttribute; E7 |
| The purge SQL must change | Ruled out while table `dbo.EditDraft` and its columns stay | `create-draft-purge-job.sql:291-335` |
| Dev2 has a 20-column table (a missing column) | Not established: runbook text, not a result | E12 |
| The library supports any XAF project | Ruled out for v1: XPO + Guid BaseObject + SQL Server (`dbo` convention) only; registered target types must have distinct CLR short names because the registry and the payload key on `Type.Name` (`EditDraftRegistry.cs:15-16,36-39`, `EditDraftPayload.cs:36-37`) | E8; Codex combined |

## 4. Proposed design

### 4.1 Projects and XAF modules (both)

- `Xaf.EditDraft.Core` — net8.0 class library (consumable from net10.0). PackageReferences at the central versions:
  DevExpress.ExpressApp, DevExpress.ExpressApp.Xpo, DevExpress.Persistent.Base, DevExpress.Persistent.BaseImpl.Xpo,
  Newtonsoft.Json (+ DevExpress.ExpressApp.ConditionalAppearance only if the popup models live in Core, decision D3).
  Module `EditDraftCoreModule : ModuleBase`. Never references Blazor, CareCrew, NursingHome_Chart or Llamachant.
- `Xaf.EditDraft.Blazor` — Razor class library, net8.0; PackageReference DevExpress.ExpressApp.Blazor; ProjectReference Core.
  Module `EditDraftBlazorModule : ModuleBase` with `RequiredModuleTypes.Add(typeof(EditDraftCoreModule))`; controllers collected from
  its assembly; static web asset `wwwroot/css/edit-draft-row-badge.css` (served at `_content/Xaf.EditDraft.Blazor/css/…` per the
  ASP.NET Core 8 RCL page Codex cited; not fetched by Claude). A DI extension registers the per-circuit services (list bridge,
  offer requests, badge notifier) that `Startup.cs:129-131` registers today.
- XAF collects business classes and controllers from each registered module's assembly (XAF 26.1 ModuleBase). Therefore the
  Core module must NOT auto-export any concrete persistent class mapped to a consumer's table (XPO 26.1: only one class may map
  to a table, otherwise SameTableNameException) — an optional ready-made store class for new consumers is opt-in (A).
- Naming rule (E11): no library type is named `EditDraft`; library namespaces use `Xaf.EditDraft.*` with types such as
  `EditDraftStoreBase`, `EditDraftWriter`, or the root namespace becomes `Xaf.EditDrafts` (decision D10).
- CareCrew: ProjectReference both; `.Add<EditDraftCoreModule>()` and `.Add<EditDraftBlazorModule>()` beside
  `Startup.cs:210-211`; NursingHome_Chart.Module references Core only if its store class derives from the library base (D1-A).

### 4.2 File-by-file table (A = Claude, B = Codex; one value where both agree)

Prefixes: `E/` = CareCrew.Blazor.Server/Infrastructure/EditDrafts/, `C/` = CareCrew.Blazor.Server/Controllers/Common/EditDrafts/.

| File | Destination | Deciding reference |
|---|---|---|
| E/EditDraftAccessRule.cs | Core (A, security section) / CareCrew (B) — decision O5 | pure owner/revision/target/expiry rule, no CareCrew type `:17-26` |
| E/EditDraftCodec.cs | Core | BaseObject only `:3,21` |
| E/EditDraftDisplay.cs | Core; strings → text table | `:19,33-34` |
| E/EditDraftListRules.cs | Core (admission, fallback baseline, provenance); BadgeSet/BadgeNotifier → Core (A) / Blazor (B) | `IsGeneric` from app `:23,39` moves into Core; strings `:64-70` → text table |
| E/EditDraftMemberDecision.cs | Core | pure |
| E/EditDraftMembers.cs | Core | DX attributes only |
| E/EditDraftOfferMerge.cs | Core; neutral row type | `:4,22-38` |
| E/EditDraftOwner.cs | SPLIT: resolver (StaffMember, GeneralUser) → CareCrew (both); `EditDraftOwnerRule`/`EditDraftOwnerInfo` → Core (A) / CareCrew (B) — O5 | `:21-38` generic, `:42-79` CareCrew |
| E/EditDraftPayload.cs | Core; JSON contract frozen; status texts → text table | `:11-81`, `:124-134`; pinned by `EditDraftEngineTests.cs:330-359` |
| E/EditDraftRegistry.cs | Core; default composition moves to CareCrew; lifetime = decision D4 | `:18-26` |
| E/EditDraftRestoreGuard.cs | Core | logger `:5,64` |
| E/EditDraftRestorer.cs | Core; neutral row type; logger | `:5-6,19-63,158,175` |
| E/EditDraftSwitch.cs | Core SPLIT: switch decision with a configurable section (default `EditDraftCapture`, same three keys, same fail-closed parse copied from CareTreeDraftCaptureSwitch); `EditDraftOfferRequests` separate | `:5,18-36,89-109` |
| E/EditDraftTypePolicy.cs | Core | `SubSectionOf` naming = D10 |
| E/EditDraftWriteGate.cs | Core | pure |
| E/EditDraftWriter.cs | Core; per-database availability cache; bound to the store per D1 | `:8,45,54,63-96,174-204,215-293` |
| E/Policies/EditDraftWavePolicies.cs + 5 policy files | CareCrew | Module types |
| C/EditDraftCaptureControllerBlazor.cs | Core whole (A) / Core capture service + thin Blazor controller (B) — decision D3; either way the snapshot/mark/write-loop types become a public Core API or InternalsVisibleTo (E5) | `:352-366,419,485,534-615,650` |
| C/EditDraftRowContext.cs | Core | no Blazor; `:24` internal → public/IVT (E5) |
| C/EditDraftModels.cs | Core (A) / Blazor (B) — D3; Llamachant removed (E6); neutral row | `:10,30-56,78,101,120` |
| C/EditDraftRestorePopupControllerBlazor.cs | Core (A) / Blazor (B) — D3 | no Blazor namespace; SystemModule controllers only `:53-55,86,123` |
| C/EditDraftRestoreControllerBlazor.cs | Blazor (B: algorithms later split into Core) | E3 |
| C/EditDraftListControllerBlazor.cs | Blazor | E3 |
| C/EditDraftListPopupControllerBlazor.cs | Blazor | `:80,83,111` |
| C/EditDraftListCaptureControllerBlazor.cs | Blazor | `:131,136` |
| C/EditDraftListBadgeControllerBlazor.cs | Blazor | `:122-138` |
| Services/EditDraftListBridge.cs | Blazor | generic per-circuit bridge; logger `:58-69`, caption `:19` → text table |
| Infrastructure/EditDraftAccess.cs | SPLIT: 事業所 rule → CareCrew (both); member-write check → Core (A) / CareCrew (B) — security section | `:29-48`, `:54-77` |
| Infrastructure/TenantChartDraftTypePolicy.cs | CareCrew | chart types |
| NursingHome_Chart.Module/BusinessObjects/EditDraft.cs | CareCrew Module, deriving from the library base (D1-A) / library class with a new CLR name mapped to `EditDraft` (D1-B) | E7 |
| …/NonPersistent/TenantChartDraftRestorePlan.cs | CareCrew Module, unchanged | chart popup |
| …/Security/DraftStoreTypes.cs | CareCrew Module; entry unchanged (D1-A) / repointed (D1-B) | `:50-57` |

Outside the 37: `Infrastructure/AttendanceDraftSlot.cs` — copy into Core as a neutral `DraftWriteSlot<T>` and leave 勤怠/chart on
the app copy (A) vs move into Core and retarget 勤怠/chart (B) — decision D5; chart forwarders `TenantChartDraft{Display,Payload,
Policy,Restorer}.cs` stay (namespace update; the Restorer maps neutral rows to `TenantChartDraftRestoreItem`, preserving row identity
because `EditDraftModels.cs:56` keys a dictionary by row); chart controllers, writer, author, switch, `ChartDraftListBridge.cs` unchanged;
a new generic tick/group controller for the neutral row in Blazor (the chart one stays); `BlazorGridCustomizationController.cs` adds the
neutral row type to its exclusion list; `Startup.cs`, `CareCrewSettingsPanel.razor:13,122-128,186-212`, `Pages/_Host.cshtml:274`
change; `Model.xafml:8` keeps its node (action id unchanged; B also puts reusable action defaults in the Blazor module's model
differences); `create-draft-purge-job.sql`, `AttendanceDraftPermissions.cs`, `AuditTrailExclusions.cs` unchanged under D1-A.

Counts over the 37 files: agreed Core 15, agreed Blazor 6, agreed CareCrew 9, disputed 7. Under A: Core 20 (2 split), Blazor 6,
CareCrew 10 (+2 split parts). Under B: Core 16, Blazor 9, CareCrew 12.

### 4.3 The store class and NHM — decisions D1 and D2 (both positions, no pick)

Persistent members (19, `EditDraft.cs:55-144`): DraftKey, EditorInstanceId, OwnerUserOid, LoginIsStaffMember, ObjectType(100),
TargetOid, SubSectionOid, ContextText(200), ViewId(100), PayloadSchemaVersion, Payload(unlimited), EntryCount, Revision,
FirstCapturedOn, LastCapturedOn, ExpiresOn, DeletedOn(nullable), OriginHost(100), LastError(2000); plus Oid and OptimisticLockField
from BaseObject; `[DeferredDeletion(false)]` (`:39`) means no GCRecord → 21 columns expected. Indexes: `uxEditDraft_Key` unique
(DraftKey, OwnerUserOid) `:57`; `iEditDraft_Owner_Expiry` (OwnerUserOid, ExpiresOn) `:68`; `iEditDraft_Target` `:86`; `iEditDraft_Expiry`
`:131`. Every name must stay byte-identical for "no migration"; SQL types, lengths and nullability must be compared on the database
(owner-run, read-only) — the repo does not hold them (E12).

| | D1-A (Claude) library `[NonPersistent] abstract EditDraftStoreBase` | D1-B (Codex) library class with a new CLR name mapped to `EditDraft` |
|---|---|---|
| CareCrew store | `NursingHome_Chart.Module.BusinessObjects.EditDraft : EditDraftStoreBase` (empty body) | Module file deleted; library class `[Persistent("EditDraft")]` (name must not be `EditDraft` under `Xaf`, E11) |
| Table / columns / indexes | unchanged: XPO stores inherited members of a non-persistent base in the descendant's table (XPO 26.1 docs 2064); `DeferredDeletion` is `Inherited = true` | unchanged table name; column/index equality to be proven |
| Deny rows, audit exclusion, purge, DraftStoreTypes | unchanged (same CLR type) | DraftStoreTypes repointed; deny rows for the NEW full name must be seeded by the NHM Debug run before any CareCrew build uses the type; old rows remain (SECURITY, §4.11) |
| Module → Core reference | yes (NursingHome_Chart.Module) | yes (DraftStoreTypes needs the type) |
| "Library ships one store class" | ships the base (all members) + an opt-in concrete class for new consumers | ships the concrete class |
| Codex's objection | keeps an application-owned concrete class; does not complete library ownership of the store | — |
| Claude's objection | — | a security transition and an NHM dependency become prerequisites of the first release |

NHM (D2): (A) NHM keeps its standalone `EditDraft.cs` (today byte-identical to CareCrew's, parity 2d); the schema its Debug run
creates is the same because the column set is the same; the CareCrew Module file then differs from NHM's only by its base class, so
carecrew-sync must treat that as intended. (B) Mirror the Core SOURCE project into NHM and build it against NHM's net10.0 /
DevExpress 26.1.3, so NHM's Debug run sees the library type. (C) Align DevExpress patches first, then reference one Core build.
A shared Core binary is blocked today by E10 (exact DevExpress pins; restore not executed). Codex adds: if NHM registers the Core
module for the store, no capture controller may live in Core (D3-B rationale); Claude adds: under D1-A NHM needs only the Core
assembly for the base type, not the Core module.

### 4.4 Seams (non-security in full; security seams by name — §4.11)

| Seam | Core default | CareCrew implementation | Source |
|---|---|---|---|
| Owner resolver (security) | §4.11 SEC-1 | today's `EditDraftOwner.Current` | A/B named |
| Record access (security) | §4.11 SEC-2 | today's 事業所 rule | A/B named |
| Member-write check | XAF member permission (A) | — | A |
| Switches | IConfiguration under a configurable section, same keys `EditDraftCapture:Enabled`, `:Types:<PolicyId>:Enabled`, `:ListViews:Enabled`, re-read at every use, fail closed | default with section `EditDraftCapture` | both |
| Store availability | per-database cache (not process-wide), same "table exists" meaning | default | B (A agrees) |
| Clock / expiry | `TimeProvider` with LOCAL time semantics kept (today `DateTime.Now`); 7 days from first capture, never extended | default | A (TimeProvider), B (keep local time) |
| Logging | facade with pluggable sink; message text kept including `[EditDraft]`; default sink `ILogger` | sink = `GlobalLogger` (same lines in `C:\Progress\logs`) | both |
| Texts | key → text table, two built-in sets (ja = today's exact strings, en), chosen explicitly by option, never by culture sniffing | ja | both |
| Model captions | English attributes + module-level `Model.DesignedDiffs.Localization.ja.xafml` embedded in the library (XAF 26.1 docs 112580) | fallback: overrides in CareCrew's Model.xafml if the ja aspect is not applied (PreferredLanguage not established) | both |
| Registry | decision D4 (static configured once vs DI instance) | CareCrew registers 23 chart + 5 wave policies | both (lifetime differs) |
| View captions (provenance) | XAF model read, as today `EditDraftListRules.cs:74-87` | — | both |
| Dispatch | posted work on the UI context; guard closes after posted work (`EditDraftRestoreGuard.cs:72-75`) | Blazor circuit | B |

Restore guard (D12), offer merge, three-way comparison and group rules stay in Core unchanged.

### 4.5 Controllers, models and tests

- Controller placement is decision D3 (A: platform-neutral concrete controllers + NP models in Core; B: Core services only, all
  concrete controllers + models in Blazor). Both require a public Core capture API or InternalsVisibleTo (E5).
- Llamachant removal (E6): replace `NPOBase` with a DevExpress non-persistent base (`NonPersistentLiteObject` or
  `NonPersistentBaseObject`, XAF 26.1 docs 116516 — they differ in IObjectSpaceLink) and `LabelPropertyEditor` with a library
  editor or an XAF built-in; choose by an executable popup test (toggle single/group, accept, cancel, reopen).
- Tests: all existing draft tests stay in `NursingHome_Chart.Rostering.Tests` (they use Module types; NHM's copy of that project has
  none of them, parity v3 E3); its Compile links to moved engine files are replaced by ProjectReferences; a new `Xaf.EditDraft.Tests`
  holds library-contract tests on synthetic types (B also proposes moving the app-free parts of `EditDraftEngineTests.cs`).
  Required adaptations (E9): repoint `Source()` paths, update literal assertions (NPOBase, Startup lines, attribute text), update
  the reflection fixture (`EditDraftWave1Tests.cs:791-795`) — all with unchanged behavioural expectations; this needs the owner's
  approval (D6) because the brief said "unchanged except namespaces" and the guardrails forbid revising tests.
- Gate: per-test identity comparison against a baseline run (passed/failed/skipped by name), no newly skipped test, golden file
  equal (1,767 lines), Japanese text set pinned in the fixture.

### 4.6 Chart compatibility (both)

Chart controller → chart writer → `TenantChartEditDraft` (unchanged, app); chart forwarders → Core engine (namespace change,
row mapping). No `IEditDraftStore` abstraction now. A later chart migration needs: an owner model change (the chart store's owner is a
`StaffMember` reference, `TenantChartEditDraft.cs:53-66`, the generic store's a Guid), NEW-record support in the generic store
(IsNew, ProvisionalOid — additive columns, as `EditDraft.cs:19-20` anticipates), chart context columns (TenantOid, TenantSubSectionOid,
EventStartOn, ChartType ≈ ObjectType) or payload carriage, adoption/offer behaviour, the F2 owner on the chart views, purge and
registration updates, and a transition that can drain the old table through its 7-day retention instead of copying rows.

### 4.7 Zero-CareCrew-reference proof (both)

1. `dotnet build` of each library project alone, `--artifacts-path artifacts/claude-test/<run-id>`; 2. evaluated Compile and
ProjectReference items show no application source and no application project; 3. a reflection test over the built assemblies:
`GetReferencedAssemblies()` contains no `NursingHome_Chart.*`, `CareCrew.*`, `Progress.*`, `Llamachant*`, and Core none of
`DevExpress.ExpressApp.Blazor*`; 4. CareCrew.Blazor.Server references both; 5. the module/controller inventory at startup lists each
library controller once. `Xaf.EditDraft.Sample` is out of scope.

### 4.8 Later: own repository and NuGet (both)

Own SemVer (independent of ChartApplicationVersion; payload schema version separate); declare DevExpress as a minimum of the lowest
patch served (26.1.3 today) and publish only tested framework/DevExpress pairs; the package carries no DevExpress binaries and the
consumer needs its own DevExpress licence (precedent: the Llamachant.ExpressApp.* packages this repo already uses,
`Directory.Packages.props:62-70`); "XAF" is a DevExpress product name — check naming/trademark before publishing (owner); README with
the supported contract (XPO, Guid BaseObject, SQL Server), registration, policies, keys, localisation, and the three store
obligations (deny all roles, audit exclusion, purge job).

### 4.9 Risks and the check for each (both)

| Risk | Check |
|---|---|
| Store exported twice / wrong table owner (SameTableNameException) | startup inventory of exported types; one class per table |
| Missing or duplicate controllers across assemblies | controller inventory; open each popup/list once |
| Library model defaults overridden by app/user layers | fresh and existing user model; action ids and popup layouts compared |
| Llamachant replacement changes popup behaviour | popup test (E6) |
| Static asset not served after publish | request the CSS from a published dev host |
| Tests silently skipped after moves | identity-level gate (§4.5) |
| Internal members across assemblies | separate-assembly build (E5) |
| Process-wide state in a library | two providers with opposite table presence, both orders (E8) |
| DevExpress patch mismatch with NHM | restore + build per host (E10) |
| Store identity / deny rows | D1; owner-run read-only Dev2 query before and after (§6) |
| Purge assumptions | SQL unchanged; documented as consumer obligation |

### 4.10 Build plan (both; order from Codex's merge: decisions first, every milestone builds, browser pass on the final arrangement)

| Milestone | Work | Executable check | Estimated size |
|---|---|---|---|
| M0 decisions + baseline | owner decides D1-D14 (§8); Claude runs the full `NursingHome_Chart.Rostering.Tests` suite with `--artifacts-path` and saves per-test identities and outcomes (passed/failed/skipped, parameterised cases); owner runs the read-only Dev2 query (`dbo.EditDraft` columns with types/lengths/nullability, indexes, deny rows for the store's full name); the public Core capture API (or InternalsVisibleTo) is specified | saved baseline; golden SHA-256 `72325EE15DD4E28A2AA4C1C19B1FCA84401E7F459332FB22B42C1C08358C3144`; query output | no feature code |
| M1 Core + every consumer it needs | Core project; engine move; neutral row + chart mapping; text/log/time/switch contracts; registry composition (D4); per-database availability cache; write slot (D5); the store arrangement (D1; Module edit under D1-A); the NHM work D2 requires; thin CareCrew adapters that forward to today's owner/access/switch code unchanged; the controllers/models D3 places in Core | Core builds alone; CareCrew (and NHM if touched) build; compiled-library tests; no duplicate store mapping; test identities = baseline except declared, mapped relocations; golden byte-equal; reference test (§4.7) | ~30-55 files; 2,500-4,000 lines relocated; 400-1,000 integration lines |
| M2 Blazor package | 5 Blazor controllers, bridge, models/controllers D3 places in Blazor, generic tick controller, both Llamachant replacements (E6), CSS static asset, module, `_Host` link, Startup registration | Blazor builds alone; hosts build; exported-type/controller/editor inventory; popup test; `GET /_content/Xaf.EditDraft.Blazor/css/edit-draft-row-badge.css` = 200 on development and published dev hosts | ~15-25 files; 2,000-3,500 relocated; 250-650 changed |
| M3 compatibility + security review | remaining test relocations (D6), two-provider availability test, registry lifecycle test (D4), slot equivalence or lifecycle tests (D5), localisation, retained payloads; owner security review of §4.11 as implemented | per-test identity comparison, no new missing/skipped test; golden byte-equal; owner-rule and switch-parse table tests; dependency/reference inventories | ~8-18 files; up to 2,000 relocated; 150-450 harness lines |
| M4 Dev2 browser pass + Codex diff review | wave-1, wave-1b and chart checklists (DetailView/ListView capture, offer, restore, save, cancel, close, active tab, badges, localisation) on a dev host (ports 5002-5004) running the FINAL M3 build | observations tied to the build id and the `[EditDraft]`/`[ChartDraft]` log lines; golden; diffreview | no planned code; any fix repeats the earlier gates |
| M5 release handoff | the exact tested artefacts, mirror manifest, compatibility evidence, owner prerequisites (deny rows / schema if D1-B) | evidence names the same build M4 tested | documentation only |

Structural change from the brief's M1-M5 (Codex review C3 + combined merge; Claude agreed): the store arrangement, the CareCrew adapters
and the NHM work move into M1 because the writer and CareCrew's build depend on them; the NHM choice is made in M0, not M5; the browser
pass runs after the last code milestone.

### 4.11 Security section — owner and access seams (SINGLE-MODEL: Claude only, for owner review)

Codex did not design or review this section (the two seam lines were redacted from the copy sent to Codex).

What must not change: every read, update and delete of a generic draft is owner-scoped in the statement itself
(`EditDraftWriter.cs:110-120,152-160,167-172,207-212,215-220,226-243,250-268,276-293`); the store is read through a non-secured object
space (`:60-67`), so that predicate is the protection (`:34-37`). The writer stays internal to Core with every predicate byte-for-byte; no
public API returns a draft without an owner argument.

- SEC-1 Owner seam. Contract: called on the UI thread; returns `(Guid Oid, bool flag)` or None; None = refuse capture, offer, list
  and apply (today `EditDraftCaptureControllerBlazor.cs:282-291`, `EditDraftRestoreControllerBlazor.cs:150-153`,
  `EditDraftListControllerBlazor.cs:249-252,334-335`); an exception is None (`EditDraftOwner.cs:57-62`). Library default:
  `SecuritySystem.CurrentUserId` when it is a non-empty Guid, else None; non-Guid user keys are not supported (column is a Guid) and
  resolve to None. CareCrew: today's `EditDraftOwner.Current` (StaffMember lookup, D6 GeneralUser refusal, unreadable staff refused,
  `:21-31,42-63`) moved unchanged into an app class; the pure `EditDraftOwnerRule.Decide` stays in Core. The chart F2 owner stays
  app-only. `LoginIsStaffMember` is recorded only, never used for access (`EditDraft.cs:72-74`).
- SEC-2 Record-access seam. The brief proposed "default allows". Precisely: the library default adds no rule beyond XAF security, which is
  safe only because every path loads the record through the application's SECURED object space first
  (`EditDraftListControllerBlazor.cs:350-353`, `EditDraftListBadgeControllerBlazor.cs:312-315`: `Application.CreateObjectSpace` →
  `GetObjectByKey`, null = not readable) and the offer runs inside the record's own DetailView. That order becomes a documented library
  invariant with a test. CareCrew: the 事業所 rule exactly as today (`EditDraftAccess.cs:29-48`), fail closed. The member-write check
  (`:54-77`) is plain XAF and moves to Core; an unwritable path is shown as 戻せません, never silently skipped (`EditDraftOfferMerge.cs:40-46`).
- SEC-3 The apply-time re-check `EditDraftAccessRule.MayApply` (`EditDraftAccessRule.cs:17-26`) moves to Core unchanged (Codex's table
  placed it in CareCrew; this section keeps it in Core because it names no CareCrew type — owner to confirm, O5).
- SEC-4 Store controls stay the app's obligation: all-role deny (`AttendanceDraftPermissions.cs:85-93`), audit exclusion (`AuditTrailExclusions.cs:54,67-75`,
  wired in `CareCrew.Blazor.Server/Startup.cs:181-184` and `CareCrew.Win/Startup.cs:33`), purge (`create-draft-purge-job.sql:291-335`), all
  driven by `DraftStoreTypes`. Under D1-A none changes. Under D1-B the deny rows keyed on the old full name stop applying to the type CareCrew
  queries; with 5 of 19 roles AllowAllByDefault (`AttendanceDraftPermissions.cs:23-27`) the table would be reachable through generic views until
  rows for the new name are seeded by an NHM Debug run — that seeding must precede any CareCrew build that uses the new type. The library
  ships a verification helper a consumer test can call (deny present for every role; type absent from AuditTrailSettings).
- SEC-5 Non-persistent popup/list objects are not secured by XAF (XAF 26.1 docs 404633) — unchanged; the re-check (SEC-3) re-reads
  everything it trusts.
- SEC-6 The switch parse copied into Core must answer the same for the same input; a table test over today's inputs is the check.

Security decisions: SD-1 store identity (D1) — Claude recommends D1-A because D1-B opens SEC-4's window; SD-2 library default for record
access = "XAF security only, record always loaded through a secured space" (recommended) vs "allow" (brief) vs "deny unless the app registers
a rule"; SD-3 non-Guid user keys unsupported in v1 (recommended) vs a string owner column (schema change).

### 4.12 Design rules the next change must respect (both; from Codex's merge, checked by Claude against the cited lines)

1. Ordering: initializing getters run under capture suppression BEFORE the baseline (`EditDraftCaptureControllerBlazor.cs:199-242`);
   reconstruction order, group drivers, companions and the re-application of undrafted group members stay as they are
   (`EditDraftRestorer.cs:81-178`); the restore guard closes behind already-posted work (`EditDraftRestoreGuard.cs:72-75`).
2. Shared state: table availability belongs to the configured database; the registry follows the chosen D4 lifetime; offer requests,
   badge notifications, bridges, live records and object spaces stay per circuit (`Startup.cs:129-131`), never process-wide.
3. Date windows: expiry = first capture + 7 days, hidden at the exact instant (`EditDraft.cs:45-51,146-149`), never moved by supersede
   or claim (`EditDraftWriter.cs:94-96,115-117,156-157`); persisted times stay local; switching capture off never hides drafts still
   inside their 7 days (`EditDraftSwitch.cs:70-80`).
4. Idempotence and concurrency: one write in flight per slot with coalescing; a failed or skipped ticket is completed; a save retires the
   whole draft only on Committed; a create still in flight at save time deletes its own row; after expiry/破棄 only never-stored edits are
   rewritten; a failed read is never "row gone" (`EditDraftCaptureControllerBlazor.cs:534-648`; `EditDraftWriter.cs:126-142`).
5. Restore boundary: restore fills in and never saves; newest draft wins per path; group ticks move together; values are re-checked
   against a fresh read before apply; the security re-check (§4.11) stays where it is (`EditDraftOfferMerge.cs:26-74`).
6. Compatibility: payload bytes and identifiers (`EditDraftPayload.cs:11-81`, pinned by `EditDraftEngineTests.cs:330-359`), chart
   behaviour, action ids (`Model.xafml:8`), log text, and the store's mapped schema stay identical; never register two concrete
   classes for table `EditDraft`.
7. Scope: chart-store migration, 勤怠 storage, EF Core/other providers and a WinForms restore UI are separate tasks; O1-O5 are chosen
   by the owner, not by an implementation.

## 5. Deployment

- Which build runs it: CareCrew.Blazor.Server (publish.bat) runs the engine, controllers and both packages; the store's schema comes
  from the NHM WinForms Debug run (KB fix-523); CareCrew runs `DatabaseUpdateMode.Never` outside the debugger (`BlazorApplication.cs:21-23`).
- Consumers: CareCrew Blazor — all of it; NHM WinForms — schema only (D2); ChartWorkflowServiceV2 — no draft code path was found in the
  reviewed files (Codex notes this was not established across the whole service); report layouts in the database — not applicable.
- Mirror: under D1-A + D2-A only the CareCrew Module file changes (base class) and NHM keeps its own file; under D1-B or D2-B NHM changes.
- Schema: none under D1-A; under D1-B the same table, but a permission-row transition (SEC-4).
- Timing: no pay-window or month-end dependency; production stays off until the owner turns the `EditDraftCapture` keys on, as today.

## 6. Verification plan

1. M0 baseline: per-test outcomes; owner-run read-only Dev2 query of `INFORMATION_SCHEMA.COLUMNS` (types, lengths, nullability),
   `sys.indexes`, and `PermissionPolicyTypePermissionsObject` rows for the store's full name; correct the runbook's "20 columns" (21 expected).
2. Each milestone: separate-assembly builds with `--artifacts-path`; reference test (§4.7); test-identity comparison; golden equal.
3. M2: CSS served from `_content/…`; controller/exported-type inventory; popup test for the Llamachant replacement.
4. M3: owner-rule tests, switch-parse table test, two-provider availability test; owner security review of §4.11.
5. M4: Dev2 browser pass (wave-1, wave-1b, chart checklists) on the final arrangement; schema and deny-row query again (unchanged under D1-A).

## 7. Contribution log

### What each model did
- **Claude (Opus 5.5):** Phase 0 checks; parity packs v1-v3 (494 KB / 508 KB / 515 KB); independent design saved before reading any
  Codex output (`claude-diagnosis.md`, sha256 A85282E8…, 19:48); dxdocs fetches (XPO inheritance mapping, PersistentAttribute,
  DeferredDeletion, BaseObject, ModuleBase, model layers, non-persistent objects, type permissions); local nuspec read; an executed
  scratch compile (CS0118); verified every Codex citation used here; the security section alone. Got right: store-identity/deny-row
  consequence, NonPersistent-base option, CS0118, SameTableNameException rule, Llamachant NPOBase, write-slot and row-type couplings,
  DX patch mismatch. Got wrong (corrected by Codex): "tests unchanged except namespaces + one SetUpFixture"; the "20 columns" read as an
  observation (it is a runbook); GlobalLogger count (124/16 → 104/13); milestone order; missed the internal-accessibility boundary,
  the SQL-Server/XPO-only contract and the process-wide availability cache.
- **ChatGPT (Codex, gpt-6-astra, xhigh, codex-cli 0.153.4):** independent design (diag), review of Claude's design, combined draft
  (which supplied this document's milestone order M0-M5, the "Design rules" list, the distinct-short-name constraint and the side-by-side
  O1-O5 tables; Claude checked each cited line before adopting it).
  Got right: supported-contract limits, source-text test assertions and the `Assert.Ignore` skip trap, internal accessibility, milestone
  order, runbook-vs-observation, process-wide cache, logging count, Blazor dependency of the list popup, raised `LabelPropertyEditor`.
  Got wrong (corrected by Claude): its recommended store name `Xaf.EditDraft.Core.BusinessObjects.EditDraft` fails to compile from library
  namespaces (CS0118, executed check); placed `EditDraftAccessRule` app-side (security section keeps it in Core, owner to confirm).
- Codex stayed out of the security design as instructed.

### Found issues, by tool

| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| A1 | New store CLR name drops the all-role deny in CareCrew | both (dependency); Claude (security consequence) | correct statically; Dev2 rows unverified | E7 | table reachable by AllowAll roles / only if D1-B / high (repo) / no | Dev2 query + IsGranted under an AllowAll role after a rename on a copy | D1, SD-1 |
| A2 | NonPersistent base keeps table and columns; 21 columns expected | both | correct (docs); "only if" wording too strong (Codex) | XPO 2064, PersistentAttribute | — / — / docs / no | Dev2 schema compare | D1-A basis |
| A3 | Library must not auto-export a concrete class on a consumer table | Claude | correct (docs) | XPO PersistentAttribute; XAF ModuleBase | startup failure / if exported / docs / no | exported-type inventory | design rule |
| A4 | Type `EditDraft` under `Xaf` → CS0118 | Claude | correct (executed compile); Codex: wording broader than C# rule | parity v3 E1 | compile errors / certain for that arrangement / executed / no | done | design rule; defeats Codex's proposed name |
| A5/C6 | DevExpress patch + TFM mismatch with NHM | both | correct; restore failure not executed | E10 | NHM cannot reference a 26.1.4 Core / if shared binary / medium / no | NHM restore + build | D2 |
| A6 | Popup models need Llamachant `NPOBase` | both | correct | E6 | library needs Llamachant / certain / high / no | popup test after replacement | D9 |
| A7/C2 | Row type and tick controller are chart code | both | correct | E4 | lost tick/group behaviour / certain on move / high / no | browser tick test | design |
| A8 | GlobalLogger coupling | Claude | coupling correct; count wrong (Codex R7: 104/13) | re-run Select-String | — | done | facade |
| A9/C2 | Write slot is a 勤怠 class | both | correct | E5 | — | build | D5 |
| A10 | Registry default list is CareCrew's | both | correct; Claude's "tests unchanged + SetUpFixture" wrong | E1, E9 | — | — | D4, D6 |
| A11 | Japanese literals; golden pins them | both | correct; PreferredLanguage unknown | E1; golden 718 lines | wrong language in CareCrew / if ja aspect not applied / medium / no | M2 browser | text table + fallback |
| A12 | Five controllers are Blazor-bound | both | correct; list popup confirmed Blazor (`:80`) | E3 | — | — | design |
| A13 | Chart path needs no store abstraction | both | correct | parity v2 D4 | — | golden + chart browser pass | design |
| C1/R5 | v1 contract is XPO + Guid BaseObject + SQL Server | Codex | correct | E8 | non-matching consumers fail / certain / high / no | synthetic-type tests at the boundary | D7 |
| C4/R2 | Source-text test assertions; `Assert.Ignore` turns moved files into skips | Codex | correct | E9 | lost coverage hidden by counts / certain on move / high / no | identity-level gate | D6 |
| C5/R6 | Process-wide availability cache and static registry | Codex (cache); both (registry) | correct | E8 | cross-database wrong answer / multi-provider hosts only / high / no | two-provider test | design + D4 |
| R1 | Internal members cross the assembly boundary | Codex | correct | E5 | compile failure / certain on split / high / no | separate-assembly build | public API or IVT |
| R3 | Milestones consumed prerequisites before they existed; browser pass before final arrangement | Codex | correct | `EditDraft.cs:39-43`; `EditDraftWriter.cs:63-96` | broken intermediate builds / certain / high / no | build per milestone | plan reordered |
| R4 | "20 columns"/deny rows are runbook text | Codex | correct | E12 | false defect / — / high / no | Dev2 query | M0 |
| B-name | Codex's store name fails to compile in library code | Claude | correct (executed) | E11 | compile errors / certain / executed / no | done | design rule |
| L1 | `LabelPropertyEditor` alias is Llamachant's | Codex raised; Claude confirmed (byte search) | correct statically, not decompiled | E6 | popup labels lose their editor without Llamachant / certain / medium / no | popup test | D9 |
| P1 | Parity inventory line counts excluded blank lines | Codex | correct (pack defect) | parity v3 E6 | — | — | noted |

Found independently by both: the store CLR-identity dependency, the 19-member/21-column discrepancy, the row-type and write-slot couplings,
the DevExpress/TFM mismatch, Llamachant NPOBase, the Blazor-bound controllers, chart compatibility through the forwarders (coverage, not confidence).

### Codex calls

| Run | Call | Attempt | Path | Started | Duration | state | validation | Exit | PID | Model / effort requested | Effective effort | reasoning_output_tokens | Search / queries | MCP tools called | activity (commands / non-zero / file_change / outside-repo) | prompt / out sha256 (first 8) | Pack | CLI |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| c8ceda | diag | a1 | `%LOCALAPPDATA%\collab\2026-10-01-editdraft-library-c8ceda\diag\a1` | 19:39:49 | 15.7 min | success | ok | 0 | 65468 | gpt-6-astra / xhigh | not observable | 4272 | off; 3 web_search items in default mode (RCL docs, "license") | KB lookup 1, dxdocs search 15, get 9 | 17 / 1 / 0 / 2 (powershell.exe; a Codex skill file under ~/.codex) | 6786BCAE / 20A5C51C | v1 | 0.153.4 |
| c8ceda | review | a1 | `…\review\a1` | 19:55:56 | 10.1 min | success | ok | 0 | 56456 | gpt-6-astra / xhigh | not observable | 4123 | off; 3 web_search items (C# name-lookup spec) | KB 1, dxdocs search 9, get 6 | 12 / 1 / 0 / 1 (powershell.exe) | 4B5EA461 / 3B203B73 | v2 | 0.153.4 |
| c8ceda | combined | a1 | `…\combined\a1` | 20:07:41 | 9.9 min | success | ok | 0 | 63892 | gpt-6-astra / xhigh | not observable | 2545 | off; 1 web_search item (RCL docs) | KB lookup 1, get_fix 1, dxdocs search 9, get 6 | 9 / 0 / 0 / 1 (powershell.exe) | 887DD8C7 / DEFBE3ED | v3 | 0.153.4 |

Input tokens per call (cached share): diag 2,673,171 (2,305,408), review 2,028,686 (1,702,784), combined 2,167,986 (1,928,704). The
parity packs were large (494-515 KB); this is the cost driver of the run. Claude's draft of this write-up was created in `docs/` during the
combined call (20:11); Codex's `git status` at its item 29 printed only a global-ignore permission warning and no path, and its merge does
not cite the draft — so the merge was not informed by it.

No node_repl or cua_repl call appears in any activity record. No file_change item in any call. No secrets file was opened.

### Setup checks (Phase 0; outputs under `%LOCALAPPDATA%\collab\2026-10-01-editdraft-library-c8ceda\preflight\`)

| # | Item | Result |
|---|---|---|
| 1 | `BASH_MAX_TIMEOUT_MS` | present (2400000) |
| 2 | Read-only query connection (HARD) | not run: no query connection was used (brief: "no DB"); no query was executed |
| 3 | Repo trusted (HARD) | present: the PreToolUse hook fired in this session (item 5) |
| 4 | Manifest (HARD) | 7/7 hashes match in the worktree and in the main repo (`manifest-worktree.txt`, `manifest-main.txt`) |
| 5 | Hook fires (HARD) | `git push --dry-run origin HEAD` → blocked by collab-guard; a harmless Monitor (`Get-Date`) ran unblocked |
| 6 | `collab.rules` | file present; the `codex execpolicy check … git push …` command was itself blocked by the hook (its text contains a push) — hook false positive, not retried (ground rule 13); last recorded result (wave-1b run): plain push forbidden, `pwsh.exe -Command "git push"` not forbidden |
| 7 | `codex debug prompt-input` | saved (`prompt-input.txt`); AGENTS.md sections present; CLAUDE.md body absent (pasted as parity item 0) |
| 8 | Tool boundary (HARD) | no MCP tool of this agent writes a database, migrates, deploys, pushes or restarts a service; KB write tools exist and were not used; browser and claude.ai connectors not used |
| 9 | Tool parity (HARD) | Codex: blazor-knowledge-base with `enabled_tools` = the 9 read tools; dxdocs (26.1). DEVIATION (same as runs 065de8/8eab5b/c71e94): `node_repl` and `cua_repl` are enabled for Codex (`codex-mcp-list.json`); guard: no call to either appears in any activity record. Claude's browser/Docs/Gmail/Calendar/Drive connectors have no Codex counterpart; not knowledge sources here, not used |
| 10 | Models (HARD) | `gpt-6-astra` listed; supported efforts low…ultra incl. medium and xhigh (`debug-models.txt`) |
| 11 | Run setup | run id c8ceda; scratch created; `salt.txt` written (unused — no personal data handled); binary `C:\Users\owner\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe` (PATH), codex-cli 0.153.4; `codex doctor` overallStatus = warning (dev drive, optional MCP config issues, endpoint protection); login: ChatGPT |
| 12 | Snapshot | worktree `C:\Users\owner\source\repos\CareCrew-library`, branch feature/edit-draft-library, HEAD 6bfbf876c4f35a6755026d1c38aa6b35cbcc6deb, status empty at start; NHM read-only reference: master 7bf13f39 |
| 13 | Policy drift | CLAUDE.md in the worktree = main repo (sha256 AF56E4B3…); AGENTS.md carries the template's "Working with Claude (Codex)" section plus an extra "Multi-model guardrails" section not in the template (reported, not edited); `~/.codex/config.toml` top-level `model_reasoning_effort = "medium"` (each call overrides with `-c model_reasoning_effort=xhigh`) |
| 14 | Web search | `-Search` OFF (D9); Codex still issued web_search items in its default (cached) mode — recorded in the Codex calls table |

### Redaction
None needed: no personal data was read or sent (no database, no logs). The security seam lines were redacted from the copy of Claude's
analysis sent to Codex (owner rule, single-model security), not for personal data.

### Inputs Codex did not have
Pasted in full except: the chart controllers and forwarders (`TenantChartDraft*ControllerBlazor.cs`, `TenantChartDraft{Author,Display,Payload,
Policy,Restorer,Switch,Writer}.cs`), the settings panel, the purge SQL, the tests, the golden file and the design docs — listed with SHA-256 and
readable by Codex in the same clean tree (it read several). Claude's private context: the auto-memory index (not relied on); Codex's private
context: AGENTS.md. Conclusions not cross-checked: the security section (§4.11), by design.

### Passes used
2 cross-model passes (diag; review) + 1 combined call = 3 Codex calls, 3 attempts, no retries.

### Run ledger
Appended to `%LOCALAPPDATA%\collab\ledger.jsonl` by `tools/collab/append-ledger.ps1`:
```json
{"run":"2026-10-01-editdraft-library-c8ceda","date":"2026-10-01","topic":"xaf-editdraft-library-design","attempts":[{"call":"combined","attempt":1,"state":"success","validation":"ok","accepted":true,"exit":0,"minutes":9.9,"commands":9,"nonzero_exits":0,"outside_repo":1,"file_changes":0,"reasoning_tokens":2545,"output_tokens":17880,"search":false},{"call":"diag","attempt":1,"state":"success","validation":"ok","accepted":true,"exit":0,"minutes":15.7,"commands":17,"nonzero_exits":1,"outside_repo":2,"file_changes":0,"reasoning_tokens":4272,"output_tokens":20053,"search":false},{"call":"review","attempt":1,"state":"success","validation":"ok","accepted":true,"exit":0,"minutes":10.1,"commands":12,"nonzero_exits":1,"outside_repo":1,"file_changes":0,"reasoning_tokens":4123,"output_tokens":12506,"search":false}],"findings":{"claude_confirmed":4,"claude_rejected":3,"codex_confirmed":8,"codex_rejected":1,"both":10,"unverifiable":3,"open":7},"correlated_error_events":0,"escalated_to_owner":14,"passes":2,"hook_false_positives":1}
```

## 8. Owner decisions, not verified, open questions

Owner decisions (both positions where the models differ; no pick where marked):
- D1 Store identity (O1, security-flagged): A library base, CareCrew class keeps its name (Claude) vs B library-named class on table
  `EditDraft` with a deny-row transition (Codex). No executable check decides it.
- D2 NHM (O2): A standalone store file (Claude) vs B mirror the Core source into NHM (Codex) vs C align DevExpress patches first.
- D3 Controller/model placement (O3): A concrete platform-neutral controllers + models in Core (Claude) vs B services only in Core,
  controllers + models in Blazor (Codex).
- D4 Registry lifetime (O4): A static default configured once (Claude) vs B DI instance frozen before use (Codex).
- D5 Write slot: A copy into Core, 勤怠/chart untouched (Claude) vs B move into Core and retarget 勤怠/chart (Codex).
- D6 Test adaptations: allow mechanical path/literal/reflection changes with unchanged behavioural expectations and an identity-level gate
  (both recommend; needed because the brief said "unchanged except namespaces"). Timing: A keep the mixed fixtures in Rostering.Tests at
  first and add new library-contract tests (Claude) vs B split the app-free cases into `Xaf.EditDraft.Tests` during the extraction (Codex).
- D7 v1 contract: XPO + Guid BaseObject + SQL Server, stated explicitly (both recommend).
- D8 Texts: explicit option with built-in ja/en sets, CareCrew selects ja (both recommend).
- D9 Llamachant replacement: DevExpress NP base + library/built-in label editor, chosen by a popup test (both recommend).
- D10 Naming: no library type named `EditDraft` (required, E11); optional neutral CLR names for `SubSectionOf`/`SubSectionOid`/
  `LoginIsStaffMember` with column names kept via `[Persistent("…")]`.
- D11 Logging: facade with a GlobalLogger sink in CareCrew (both recommend).
- D12 Security SD-1..SD-3 (§4.11, single-model).
- D13 NuGet step: naming/trademark check for "Xaf", DevExpress floor 26.1.3, licence wording.
- D14 Run the M0 read-only Dev2 query (owner) and correct the wave-1 runbook's "20 columns".

Not verified / open:
- Dev2/production columns, types, indexes and deny rows of `dbo.EditDraft` (no database access; the wave-1 lines are a runbook).
- XAF behaviour when a permission row's TargetType no longer resolves (D1-B).
- DevExpress 26.1.4 dependency list (only 26.1.3 cached); NuGet outcome of NHM(26.1.3) → Core(26.1.4).
- What Llamachant `NPOBase` provides; that `LabelPropertyEditor` is Llamachant's (byte search, not decompiled).
- CareCrew's XAF PreferredLanguage and whether a module ja aspect applies.
- RCL static asset path (Codex's fetch; not fetched by Claude).
- That the baseline test suites pass today (not run in this design pass).
- That ChartWorkflowServiceV2 has no draft code path anywhere (only the reviewed files).
- Whether "any project" must include EF Core, other keys or providers.
- Codex combined call, could_not_determine: which O1-O5, slot and test-timing options the owner selects; actual Dev2/production columns,
  indexes, type-reference records, permission registrations, purge installation and deployed build; whether either store arrangement keeps
  all mapped metadata without a transition; restore/build/runtime under both package graphs; baseline suite outcomes; NPOBase semantics and
  the runtime LabelPropertyEditor provider; effective XAF language with existing user model differences; the `EditDraftCapture:*` values;
  final security seam contracts (Claude + owner); distribution/licensing terms and package-name availability.
