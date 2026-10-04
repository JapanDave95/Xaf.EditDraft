# Xaf.EditDraft sample consumer — samples/Xaf.EditDraft.Sample

Run `2026-10-03-editdraft-sample-e817ca` (collaborator: Claude Opus 5.5 + Codex gpt-6-astra at xhigh). Branch
`feature/edit-draft-sample-consumer`, base `04c69fdc` (master).
Not committed, not deployed.

Owner (2026-10-03, verbatim): "Create the sample consumer project to prove it" — the proof of "a generic library that any
project can use".

## 0. Combined answer

A separate XAF Blazor application, built from the DevExpress 26.1.4 `dx.xaf` template (XPO, SQL Server LocalDB, password
login), consumes `Xaf.EditDraft.Core` and `Xaf.EditDraft.Blazor`. It has its own `Note` class, its own store class
`SampleEditDraft` and one policy, and it uses every library default seam. It builds with 0 errors and 0 warnings, with no
change to the library or to CareCrew. 42 tests pass: references, policy, a headless XAF model, default seams, the switch
matrix and the store deny. The host on :5006 returned `/` and the library stylesheet with HTTP 200 (the stylesheet
byte-identical to its source) and logged both library modules as loaded. This proves the library can be consumed from
outside CareCrew at build, registration, model and startup level. It does not prove capture and restore in a browser;
that is the main session's checklist (§8). Codex's diff review found seven defects; all were accepted (two of them are
library gaps). The important output is the list of 15 library gaps for the NuGet step (§5). The main ones: consumer
obligations that are not documented (the non-persistent object space provider; the store deny and its limits for
administrators and object/member grants; the retention sweep), CareCrew vocabulary in the public API and in the English
texts, and no built-in way to open the list of drafts for every type.

## 1. Status

> **Status note, 2026-10-03 (main session, after this run):** the §8 browser pass was run on 127.0.0.1:5006 with the seed login User: 「Drafts」 header button present; a new Note typed and never saved → reload → notice "There are 1 draft(s) of new records…" → Drafts row with State New → Open recreated the Note with both fields and the library toast → Save → `delete after save: rows=1` → the Note in the list. One transient "Application Error" appeared on the login page when typing before the circuit was ready; the second attempt was fine. Column count: XPO created 21 columns (E8 is right; the "19" in §5 counts the 19 declared members, Oid and OptimisticLockField come from BaseObject). Committed e4a8c5e5 / 6d7c9099, merged as a0354521, pushed. KB fix-551.

Sample implemented, 2026-10-03. Not committed (git-committer's step), not deployed (nothing to deploy; §7). Library and
CareCrew code unchanged: `git status` shows only `CareCrew.sln` (+67 lines) and the new `samples/` folder (§9 end).

## 2. Result and evidence

| # | Claim | Evidence | What the check proves |
|---|---|---|---|
| E1 | Both sample projects build with 0 errors and 0 warnings, and no library or CareCrew file changed | `--no-incremental` builds into `artifacts/claude-test/20261003-e817ca`; `git status --short` | It compiles against the unchanged library |
| E2 | The sample references both libraries and no `CareCrew.*`, `NursingHome_Chart.*`, `Progress.*`, `Llamachant*` or `CareTree*` assembly, directly or reachably | tests B1 (reference walk), B2 (project files) | Assembly isolation |
| E3 | The registry built by the sample's own `Startup` holds exactly the Note policy, which admits `Note_DetailView` (existing and new records) and `Note_ListView` and nothing else | tests C3, C3b (through `Startup.ConfigureServices`), C4, C5 | Registration and admission rules |
| E4 | XAF generates `Note_DetailView` and `Note_ListView` in a real application model of `SampleModule` + `EditDraftCoreModule`; ListView `AllowNew` and DetailView `AllowEdit` are true; the store class is in the BO model | test C6 (headless `XafApplication`, in-memory XPO) | Model level, without the Blazor module |
| E5 | Every seam the sample does not register is the library default: owner, record access, clock, switch section, texts (English), log sink (application `ILogger`); the four per-circuit services resolve | tests D1, D2, C3b | Default composition |
| E6 | The sample's `appsettings.json` turns on existing, new and ListView capture for `Note` only; each of the four keys fails closed over true/True/false/missing/empty/"yes"/"1" | tests D3, D4 (28 cases) | Switch configuration |
| E7 | The updater gives every role a DENY of all five operations on the store, idempotently | test E1; read-only query after three updater runs: one row per role (`Administrators`, `Default`), all states 0 (= Deny, matching E1) | Permission rows, not effective access |
| E8 | XPO UpdateSchema created `dbo.SampleEditDraft` with 21 columns and the library's four named indexes plus the primary key | read-only queries | Mapping reaches SQL Server |
| E9 | The host (Development, :5006) answered `/` with 200, served `_content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css` with 200 `text/css` and the source file's SHA-256 (`ae057675…`), linked it in the page, answered 404 for a missing asset, and logged `XAF modules loaded: …, EditDraftCoreModule, ConditionalAppearanceModule, EditDraftBlazorModule, …` | curl output, host stdout and stop records of both host runs | Static asset and module loading at startup |
| E10 | The tests can fail: removing the deny line and the registry registration failed E1 and C3; removing the `AddEditDrafts(services)` call from `ConfigureServices` failed C3b; files restored byte-identical (SHA-256 checked) | the outputs of the two mutated test runs | Sensitivity of those tests |

Hosts started and stopped by this run: one at 15:09:47 (first run) and one at 15:35:16 (final bytes), both on
port 5006, each confirmed as the listener and the recorded start time before `taskkill /PID … /T /F`; after each stop,
no process and no listener on 5006. No other port was used. LocalDB database `XafEditDraftSample` did not exist before
the run (`sys.databases`, 0 rows) and stays on this machine as a disposable sample database.

## 3. Checked and ruled out

| Hypothesis | Verdict | Evidence |
|---|---|---|
| The sample cannot be built without a library change | Ruled out | E1 |
| The SQLite fallback is needed | Not needed: LocalDB `MSSQLLocalDB` 17.0.4025.3 is present. No SQLite provider is pinned, so the fallback would have been a stop | `sqllocaldb info`; `Directory.Packages.props` |
| The consumer must add the 入力控 header action | Ruled out: the library creates action `EditDraftListBlazor` (category QuickAccess) | `Xaf.EditDraft.Blazor/EditDraftListControllerBlazor.cs:32,46-52`; the sample's `Model.xafml` node only sets `PaintStyle` |
| The library's writer is subject to XAF role permissions | Ruled out: non-secured object space throughout (create with CommitChanges, owner-filtered reads, owner-filtered T-SQL updates and deletes) | `Xaf.EditDraft.Core/EditDraftWriter.cs:160-167,176-200,211-220,253-290,299-334,337-415,417-425` |
| A type DENY keeps every role out of the store | Ruled out for `IsAdministrative` roles ("You cannot deny any rights for a role with the Administrative Permission", XAF 26.1 topic 404633) and for roles with object or member ALLOW grants on the store (Codex DR1; XAF topic 113152 shows a type Deny combined with an object Allow) | dxdocs fetched this run; Codex decompile of `PermissionSettingHelper` 26.1.4 |
| XAF's own log lists the loaded modules | Ruled out, even at `DevExpress.ExpressApp=Debug` | the host's XAF log of the second database update, at Debug level |
| XAF's `ModuleInfo` table records the modules | Not with the template's `CheckCompatibilityType.DatabaseSchema`: no `ModuleInfo` table is created | table list query |
| `UrlSigningKey` must be in appsettings | Ruled out: "If the property is not specified, XAF generates a random in-memory key" (XAF 26.1 topic 404691). Left out, so the sample holds no secret | dxdocs |
| The sample's tests belong in `Xaf.EditDraft.Tests` | Ruled out: that project's M3 tests pin that it references no application or Module assembly; a sample reference would break that rule | `docs/xaf-editdraft-library-m3-2026-10-02.md` §2a |

## 4. What was built

```
samples/Xaf.EditDraft.Sample/
  README.md                                    stranger's guide (English)
  Xaf.EditDraft.Sample.Module/                 net8.0, refs Xaf.EditDraft.Core + DevExpress
    BusinessObjects/Note.cs                    Title (100), Body (unlimited), Priority (enum), DueOn (DateTime)
    BusinessObjects/SampleEditDraft.cs         : EditDraftStoreBase  -> dbo.SampleEditDraft
    BusinessObjects/ApplicationUser*.cs        template user classes
    EditDrafts/NoteEditDraftPolicy.cs          the one policy
    DatabaseUpdate/Updater.cs                  template users/roles + store deny for every role
    SampleModule.cs, Model.DesignedDiffs.xafml
  Xaf.EditDraft.Sample.Blazor.Server/          net8.0 Web, refs the Module + Xaf.EditDraft.Blazor
    Startup.cs                                 modules, AddEditDrafts (store, registry, Blazor services)
    Pages/_Host.cshtml                         row-badge stylesheet link
    Model.xafml                                header action PaintStyle (optional)
    appsettings.json                           LocalDB XafEditDraftSample; EditDraftCapture section on
    Properties/launchSettings.json             http://localhost:5006
    Program.cs, SampleBlazorApplication.cs, SampleBlazorModule.cs, App.razor, _Imports.razor, Services/, wwwroot/
  Xaf.EditDraft.Sample.Tests/                  NUnit, 42 tests (centrally pinned packages)
CareCrew.sln                                   solution folder "samples" with the three projects
```

Assumptions, stated because the brief allowed two readings:
- "An Updater that seeds nothing" is read as "no business data". The template's `Admin` and `User` test logins and roles
  are created as the template creates them (non-RELEASE builds), because the brief also asks for the standard template
  login. The `Default` role also gets full access to Note, so a non-administrator can try the feature.
- "PermissionPolicyUser/Role": the template's `ApplicationUser` (a `PermissionPolicyUser`) and `PermissionPolicyRole`.
- Texts: the library default (English), because the point is that defaults work. The checklist gives both captions.
- The template's EasyTest branches are left out. The sample sets its own assembly version (1.0.0.0); otherwise the
  repository's `Directory.Packages.props:8-11` would stamp CareCrew's 2.6.42.0 on it.
- Not in the template: one log line in `SampleBlazorApplication` naming the loaded modules (the brief asks for startup
  evidence and XAF logs none), and the `AddEditDrafts` method, which is public and static so a test can call it.
- The model-level test uses a headless `XafApplication` without the Blazor module (that module needs a Blazor host).

### Consumer checklist (what a project must supply, in order)

| # | Supply | Sample file |
|---|---|---|
| 1 | A business class keyed by a Guid (XPO `BaseObject`), properties through `SetPropertyValue` | `Xaf.EditDraft.Sample.Module/BusinessObjects/Note.cs` |
| 2 | One store class deriving from `EditDraftStoreBase` (its name is the table name) | `…Module/BusinessObjects/SampleEditDraft.cs` |
| 3 | A policy per type: type, `PolicyId`, approved DetailView id(s), ListView id(s), owner kind, a decision per member, `AllowNewRecords` | `…Module/EditDrafts/NoteEditDraftPolicy.cs` |
| 4 | `services.AddEditDraftStore<T>()`, `services.AddEditDraftRegistry(...)`, `services.AddEditDraftBlazor()` | `…Blazor.Server/Startup.cs` (`AddEditDrafts`) |
| 5 | `.Add<EditDraftCoreModule>()` and `.Add<EditDraftBlazorModule>()` in the module list | `…Blazor.Server/Startup.cs` |
| 6 | The non-persistent object space provider (`.AddNonPersistent()`; template line) | `…Blazor.Server/Startup.cs` |
| 7 | Configuration section `EditDraftCapture` (`Enabled`, `Types:<PolicyId>:Enabled`, `ListViews:Enabled`, `NewRecords:Enabled`) | `…Blazor.Server/appsettings.json` |
| 8 | The stylesheet link `_content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css` in the host page | `…Blazor.Server/Pages/_Host.cshtml` |
| 9 | An explicit DENY of every operation on the store class for every role | `…Module/DatabaseUpdate/Updater.cs` (`DenyDraftStoreToEveryRole`) |
| 10 | Optional: header action paint style | `…Blazor.Server/Model.xafml` |
| — | Production only, not in the sample: retention sweep; audit-trail exclusion if the Audit Trail module is used | `README.md` "Security" |

Nothing else was registered: owner, record access, clock, log sink, texts and switch section are library defaults (E5).

## 5. What the library made easy, and the gaps (for the NuGet step)

Easy, as observed: the store is a one-line class, and XPO created all 19 columns, the indexes and the primary key from
it (E8). Registration is three extension calls plus two module lines. Every seam has a working default, so no custom
owner or record-access code was needed (E5). The stylesheet is served by the Razor class library without any host setup
(E9). The header action, popups, list and captions come with the modules (C6, `EditDraftListControllerBlazor.cs:46-52`).
`EditDraftDecisions.Check` lets a consumer check its own policy (C5).

Gaps: places where a consumer needs knowledge that only CareCrew's code or documents hold, or where the library's
behaviour surprises a consumer. None blocked the build, so the library was not changed (as the brief requires).

| ID | Gap | Library location | Found by |
|---|---|---|---|
| G1 | The non-persistent object space provider is a prerequisite, but nothing says so; `AddEditDraftBlazor` registers no provider. Without it a consumer builds fine and fails when the restore popup or drafts list opens | `Xaf.EditDraft.Blazor/EditDraftRestoreControllerBlazor.cs:274`; `EditDraftListControllerBlazor.cs:262`; `EditDraftBlazorServices.cs:15-22` | Codex (DR4) |
| G2 | The store's security obligations exist only as comments; no deny helper, and no verification helper even though design §4.11 SEC-4 names one. CareCrew's equivalents (`AttendanceDraftPermissions`, `DraftStoreTypes`) are application code | `Xaf.EditDraft.Core/EditDraftCoreModule.cs:12-16`; `EditDraftStoreBase.cs:124` | Claude |
| G3 | The limits of that deny are not stated anywhere in the library: it does nothing for `IsAdministrative` roles, object/member ALLOW grants override it, and roles created after the last update have none | same as G2 | Claude (admin, later roles); Codex (DR1, object/member) |
| G4 | No retention sweep in the library, but the default list lead text says drafts "are deleted when they expire" (Japanese set likewise). Without a consumer sweep, the rows stay | `Xaf.EditDraft.Core/EditDraftTexts.cs:337`; `EditDraftStoreBase.cs:124`; CareCrew's `scripts/create-draft-purge-job.sql` is application code | Codex (DR3, text); Claude (no sweep) |
| G5 | Expiry is written in the application server's local time; a SQL-side sweep must use that clock. Not documented | `Xaf.EditDraft.Core/EditDraftServices.cs:41-46`; `EditDraftWriter.cs:195-197` | Codex (DR2) |
| G6 | SQL Server and the `dbo` schema are hard-coded. A store table in another schema is probed as "absent", so restore and the list are hidden without an error. Stated only in a code comment | `Xaf.EditDraft.Core/EditDraftWriter.cs:139-140,311` | Claude |
| G7 | An "absent" probe result is cached for 5 minutes per database. An application started before its first database update keeps restore and the list hidden for up to 5 minutes after the table appears | `Xaf.EditDraft.Core/EditDraftTableCache.cs:16,35-45` | Claude |
| G8 | CareCrew vocabulary in the public API: `LoginIsStaffMember`, `SubSectionOid` (store columns), `SubSectionOf` (policy), `IsSubSectionVisible` (access seam), `EditDraftOwnerKind.F2StaffMember` | `EditDraftStoreBase.cs:66-68,83-85`; `EditDraftTypePolicy.cs:18-22,110-111`; `EditDraftAccessSeam.cs:18-23` | Claude |
| G9 | CareCrew terms in the English text set that any consumer shows: "personal login" (`PersonalLoginOnly`), "(事業所 permission)" (`RecordNotVisible`), "The office (事業所)" (`RecreateSubSectionNotVisible`) | `Xaf.EditDraft.Core/EditDraftTexts.cs:347,355,384` | Claude |
| G10 | The decision table uses CareCrew's setter-census vocabulary: `CensusCategory` "A/B/C/D", plus a required non-empty reason and evidence. The convenience helpers (`A`, `NotRestorable`, `Excluded`, `Views`) are CareCrew code, so the sample wrote its own | `Xaf.EditDraft.Core/EditDraftMemberDecision.cs:7-23,59,74`; CareCrew `EditDraftWavePolicies.cs:37-48` | Claude |
| G11 | No library entry point for the all-types drafts list. `EditDraftListBridge` is meant for "the host's settings panel" (CareCrew's gear panel). Without one, the list opens only from the header action while a registered ListView is shown | `Xaf.EditDraft.Blazor/EditDraftListBridge.cs:6-15`; `EditDraftListControllerBlazor.cs:174-192`; CareCrew `CareCrewSettingsPanel.razor:122-128,205-212` | Claude |
| G12 | The header action sets no paint style. CareCrew sets `CaptionAndImage` plus a CareCrew CSS class in its own model. Whether the caption shows without such a node is not verified | `EditDraftListControllerBlazor.cs:46-52`; CareCrew `Model.xafml:8` | Claude |
| G13 | Assembly identity: the library assemblies carry CareCrew's `ChartApplicationVersion` (observed `Version=2.6.42.0` in the sample's XAF log), and Core grants `InternalsVisibleTo` to `NursingHome_Chart.Rostering.Tests` | `Directory.Packages.props:8-11`; `Xaf.EditDraft.Core.csproj:32` | known (M3 §11), confirmed |
| G14 | No consumer guide in the library; the obligations are spread over class comments and four CareCrew design documents. The sample README is now the only guide | — | Claude |
| G15 | `EditDraftCaptureControllerBlazor` is platform-agnostic and lives in Core, but its name says Blazor | `Xaf.EditDraft.Core/EditDraftCaptureControllerBlazor.cs:38` | known (M3 §11.5) |

## 6. Security note

- **Writer path.** The engine never goes through XAF security for the store. Codex DR8 made the description more precise:
  create uses a non-secured object space and `CommitChanges` (`EditDraftWriter.cs:176-200`); reads use owner-filtered
  queries on a non-secured space (`:337-415`); updates and deletes use owner-filtered T-SQL through a non-secured session
  (`:211-220,253-290,329-334,417-425`). The owner condition in every read, update and delete is what keeps one login's
  drafts from another. The sample README says so plainly.
- **Store deny.** `Updater.DenyDraftStoreToEveryRole` writes a type DENY of Read, Write, Create, Delete and Navigate on
  `SampleEditDraft` for every role, on every database update (E7). It does not affect capture or restore. It does not
  cover `IsAdministrative` roles (an administrator can open `SampleEditDraft_ListView` and read every user's drafts),
  object/member ALLOW grants on the store, or roles created after the last update. README and code comments state all
  three.
- **Record (Note) authorization is unchanged library behaviour:** records are loaded through the secured object space;
  member write checks (`EditDraftAccessSeam.cs:99-112`) and the create check (`:65-89`) use XAF security. The sample's
  `Default` role has CRUD on Note.
- **Retention and audit:** no sweep and no Audit Trail module in the sample. The README shows the sweep with the
  application-clock condition (DR2).
- **Guardrail flag:** `DenyDraftStoreToEveryRole` is XAF permission code, one of the categories that run single-model
  with owner review (guardrails Part 4). It went through Codex's diff review because the brief asked for one. **The owner
  should review it, and README "Security", as a security item.**

## 7. Deployment

Nothing is deployed and nothing needs deploying. No production build runs this code: `publish.bat:30` publishes only
`CareCrew.Blazor.Server.csproj`, so the sample is not in CareCrew's publish output. The owner's full `CareCrew.sln`
build now also restores and builds the three sample projects (that is what the brief asked for). Consumers:
CareCrew Blazor — none (no CareCrew file changed); NHM WinForms — none, not mirrored (no mirrored file changed);
ChartWorkflowServiceV2 — none; report layouts in the database — not applicable. Schema: only the disposable LocalDB
database `XafEditDraftSample` on this machine (drop it at will). No pay-window or month-end dependency.

## 8. Verification plan — browser checklist for the main session

Setup (worktree `CareCrew-sample`, port 5006, artifacts rule):
1. `dotnet build samples/Xaf.EditDraft.Sample/Xaf.EditDraft.Sample.Blazor.Server/Xaf.EditDraft.Sample.Blazor.Server.csproj --artifacts-path artifacts/claude-devhost-5006`
2. In `artifacts/claude-devhost-5006/bin/Xaf.EditDraft.Sample.Blazor.Server/debug`, with `ASPNETCORE_ENVIRONMENT=Development`:
   `dotnet Xaf.EditDraft.Sample.Blazor.Server.dll --urls http://localhost:5006` (the database already exists; on a fresh
   machine first run the same with `--updateDatabase --forceUpdate --silent`).
3. Open `http://localhost:5006`, log in as `Admin` with an empty password. Keep the host stdout: `[EditDraft]` lines
   (category `Xaf.EditDraft`) record capture, offer, list and claim.

English captions (Japanese set in brackets). The library default is English.

A. New Note (brief: Note list → 新規 → type → Tab → F5 → notice → 入力控 → 新規 row → 開く)
1. Note list → **New**. Type a Title (also try 100 characters), a multi-line Body with Japanese text, Priority High and a
   DueOn date. Press Tab after the last field.
2. Reload (F5) without saving. Expect the Note list notice "There are 1 draft(s) of new records. Open them from "Drafts"
   at the top." [新規の入力控 notice].
3. Press **Drafts** [入力控] in the top bar. Expect a list filtered to Note with one row, state **New** [新規].
4. Select it, press **Open** [開く]. Expect a new Note with the four typed values, not saved, and the message "A record was
   created from the draft …".
5. Save. Expect the draft to be gone (Drafts list empty) and no notice on the list.

B. Existing Note (brief: type → close → reopen → offer)
6. Open a saved Note, change Title and Priority, close the tab, and do not save at the prompt.
7. Expect the row bar (row badge) and the **Open draft** [入力控を開く] icon on that row of the Note list.
8. Reopen the Note. Expect the popup **Unsaved input was found** [保存されていない入力が見つかりました] listing both fields,
   ticked. Press **Yes (put the selected fields back)**. Expect the values filled in and not saved. Save; expect the bar
   and the draft to be gone.
9. Repeat 6–8 and choose **Later** [あとで]; reopen; expect the offer again. Then **Discard** [破棄] from the Drafts list;
   expect no offer on reopen.

C. Owners and security
10. Log out; log in as `User` (empty password). Expect the Drafts list to show none of Admin's drafts. Type into a Note as
    User and leave it; log back in as Admin; expect none of User's drafts.
11. As `User`, go to `http://localhost:5006/SampleEditDraft_ListView`. Expect no access and no rows (store deny). As
    `Admin`, the same URL lists every user's drafts (documented administrator limit).

D. Presentation
12. The header action shows the caption "Drafts" with an image (Model.xafml node). Optional: remove the node and compare.
13. The badge stylesheet applies (row bar visible in 7). Expect no browser console errors on the Note list.

E. Switches (optional)
14. Set `EditDraftCapture:NewRecords:Enabled` to false and restart. Expect no capture of a new Note, while existing-Note
    capture and the Drafts list still work.

## 9. Contribution log

### What each model did

- **Claude (Opus 5.5):** Phase 0 preflight; read the design and M1–M3 write-ups and the library source; generated the
  DevExpress template outside the repo and wrote the sample from it; wrote the requirement-only pack for Codex's `tests`
  call; built, tested and ran sensitivity checks; created the database with XAF's updater and ran the host checks and
  read-only queries; added the module log line when XAF's own log proved to have no module list; framed the store-deny
  security text from the writer source and the XAF docs; verified every Codex finding against source and docs; applied
  the fixes; wrote the README and this write-up; compiled the library-gap list (G2, G6–G12, G14 first found by Claude).
  Claude was wrong once in process: after the second sensitivity restore, `Copy-Item` kept the backup's older timestamp,
  so an incremental build reused the mutated DLL and C3b failed. A `--no-incremental` rebuild gave 42/42. The first
  sensitivity restore was followed by `--no-incremental` builds, so the results from that run stand.
- **Codex (gpt-6-astra, xhigh, read-only):** `tests` a1 gave 32 requirement-only expectations (C1–C32). The tests were
  written from them, notably C3's reachable-reference walk, C6's model-level view existence, C13's byte-identical asset
  check with a negative control, C17's no-owner-without-login check, C19's switch variants and C24's role-row checks.
  `diffreview` a1 found seven defects and one wording correction: DR1–DR8 below. Codex also checked the 28 pasted files
  against their git blob hashes (no input mismatch), decompiled `PermissionSettingHelper` 26.1.4, and fetched
  XAF/Microsoft docs.

### Found issues, by tool

Severity rule: no item is "wrong in production today" (nothing is deployed). Real-path defects in the sample rank above
documentation and test-coverage items, and library gaps are listed separately in §5.

| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| DR1 | Deny comment overstated: object/member ALLOW grants survive a type deny | Codex | Confirmed | `PermissionSettingHelper` decompile (Codex); XAF topic 113152 pattern | A role with such grants could read drafts / only if someone adds them / high / no | Effective secured access with a retained grant (not run) | Comment and README qualified; library gap G3 |
| DR2 | README purge `GETDATE()` assumes one time zone for app and SQL Server | Codex | Confirmed | `EditDraftServices.cs:41-46`, `EditDraftWriter.cs:195-197`; Microsoft GETDATE docs | Early/late deletion / different time zones / high / no | Synthetic clock comparison (Codex ran it) | README states the application-clock condition; gap G5 |
| DR3 | Library list text says drafts are deleted at expiry; deletion needs a consumer sweep | Codex | Confirmed | `EditDraftTexts.cs:337`; `EditDraftWriter.cs:372-382` | Misleading user text / every populated list without a sweep / high / no | Expired row still present after reopening the list (not run) | Library gap G4; README notes it |
| DR4 | Checklist omitted the non-persistent object space provider | Codex | Confirmed | `EditDraftRestoreControllerBlazor.cs:274`, `EditDraftListControllerBlazor.cs:262` | Popup/list failure in a consumer without it / medium / high / no | Omit `.AddNonPersistent()` and open a popup (not run) | README step 6, Startup comment; library gap G1 |
| DR5 | Tests called the helpers, not their callers: removing `AddEditDrafts(services)` or the updater's deny call would pass | Codex | Confirmed | test bodies | Integration regressions undetected / medium / high / no | Remove each call | New test C3b, which failed under the mutation (E10). The updater call is shown by the executed database query (E7), not by a unit test. The module list is shown by the host log (E9) |
| DR6 | D4 did not vary every key (ListViews never; missing/malformed only for some) | Codex | Confirmed | test cases | Fail-closed regressions undetected / high | Full matrix | D4 is now 28 cases over all four keys |
| DR7 | Test names claimed more than they asserted (subclass, exact allowlists, PaintStyle, log sink) | Codex | Confirmed | test bodies | Coverage overstated / high | Add assertions | Subclass probe, exact allowlist, PaintStyle and default-log-sink assertions added |
| DR8 | README described the writer as "non-secured object space and plain T-SQL"; creation and reads use the object space | Codex (review prose) | Confirmed | `EditDraftWriter.cs:176-200,337-415` | Imprecise documentation / low | Source read | README and comments made precise |
| A1 | Roles created after the last database update get no deny | Claude (post-review) | Confirmed by source read | `Updater.UpdateDatabaseAfterUpdateSchema` runs only on update | Exposure for a later AllowAllByDefault role / low / high / no | Create a role, query its permissions (not run) | README and comment; part of gap G3 |
| A2 | Stale binary after a byte-identical restore (timestamps kept) | Claude | Process defect | C3b failed, then passed after a `--no-incremental` rebuild | Could misreport a test result / once / high | `--no-incremental` rebuild | Final evidence uses no-incremental builds only |
| G1–G15 | Library gaps | see §5 | — | §5 | — | — | Listed for the NuGet step; no library change (brief) |

Found independently by both: the writer bypasses XAF security, and administrators cannot be denied (Claude from source
and docs before the review; Codex in its review). This is coverage, not confidence; the source read and docs decided
it. No disagreement remained, so nothing went to the owner as an unresolved position. Correlated-error events: none.

### Codex calls

| Run | Call | Attempt | Started | Duration | state | validation | Exit | Model / effort requested | Effective effort | reasoning_output_tokens | Search | MCP calls | Commands (non-zero) | file_change | Outside-repo reads (count) | Parity pack | codex-cli |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| e817ca | tests | a1 | 14:48:11 | 7.0 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 5,670 | off | 0 | 5 (2) | 0 | 1 | requirement-only (isolation by convention: Codex's output cites only `REQUIREMENT.md` and says the repository was not inspected; the activity record names no repository path) | 0.153.4 |
| e817ca | diffreview | a1 | 15:15:15 | 15.3 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 9,385 | off (1 web lookup in the stream: Microsoft GETDATE doc) | 14 (KB 2, dxdocs 12) | 19 (3) | 0 | 9 | v1 (candidate: 30 files) | 0.153.4 |

Calls: 2, attempts: 2, both accepted. No retry.

### Setup checks (Phase 0)

| # | Item | Result |
|---|---|---|
| 1 | `BASH_MAX_TIMEOUT_MS` = 2400000 | present |
| 2 | Query connection read-only | not applicable: no diagnostic query ran against any shared database. The only database is the disposable LocalDB database the sample creates (owner-instructed); queries on it were SELECT only |
| 3 | Repo trusted (hook runs) | present (the hook blocked a command in this session) |
| 4 | Manifest | all 7 files match (`run-codex.ps1`, `watch-codex.ps1`, `append-ledger.ps1`, `codex-instructions.md`, `preflight-sql.sql`, `collab-guard.ps1`, `collaborator.md`) |
| 5 | Hook fires / Monitor not blocked | the hook blocked a PowerShell command containing a quoted push (block message captured); a `Get-Date` Monitor ran unblocked. The literal `git push --dry-run` probe was not issued separately |
| 6 | `collab.rules` execpolicy | file present; check **not run**: the combined command was blocked by the hook (quoted push text), and the separate check was denied by the auto-mode classifier. Not pursued |
| 7 | `codex debug prompt-input` | AGENTS.md section present; CLAUDE.md body not present (pasted as parity item 0) |
| 8 | Tool boundary | no MCP tool can write a database, migrate, deploy, push or restart a service (KB write tools write KB files only) |
| 9 | Tool parity | Codex has `blazor-knowledge-base` (with an `enabled_tools` key) and `dxdocs`; reading the whitelist content was denied by the classifier, so the nine-tool list is **not verified**. Codex also has `node_repl` and `cua_repl`, which Claude does not have |
| 10 | Models | `gpt-6-astra` listed; levels low/medium/high/xhigh/max/ultra |
| 11 | Run id, salt, binary | `e817ca`; `codex-cli 0.153.4` |
| 12 | Snapshot | `04c69fdc`, clean worktree at start |
| 13 | Policy drift | CLAUDE.md "Precedence" says `could_not_determine` "must not be empty"; collaborator D1 (closed) allows the scoped "no additional uncertainty" line (both Codex outputs had non-empty lists). collaborator.md "Cost" still says "At `medium`", against ground rule 11 (xhigh). Reported, not edited |
| 14 | Web search | off (`-Search` not passed) |

`AGENTS.md` exists in the worktree with the `## Working with Claude (Codex)` section; not changed.

### Redaction

None needed: no personal data was read or written. The only users are the template's test logins `Admin` and `User`.
The pasted connection string is LocalDB integrated security (no credential).

### Inputs Codex did not have

`tests`: by design, only the requirement and runtime facts. `diffreview`: everything Claude relied on up to the freeze.
After the freeze, Codex did not see: the post-review edits (README.md, Startup.cs comment, SampleEditDraft.cs comment,
Updater.cs comment, SampleConsumerTests.cs — **not cross-reviewed**), the second sensitivity run, the final host run, and
the first host's stop record. The conclusions on those rest on Claude's executed checks (E1, E9, E10).

Final SHA-256 of the files edited after the review: README.md `B56D8F4F…`, Startup.cs `22E04E39…`, SampleEditDraft.cs
`E4E035A1…`, Updater.cs `9E9F54C1…`, SampleConsumerTests.cs `98414378…`. The other 25 candidate files are unchanged
since the review.

### Passes used

2 (pass 1: requirement-only expectations; pass 2: diff review). Codex calls: 2.

### KB

No `log_new_fix` in this run: the KB server writes into the CareCrew repository, which this run may not touch. Owed: a record for
the sample consumer and the gaps G1–G15 (or an amendment to fix-537, whose "owed" list names this sample).

## 10. Not verified / open questions

- Capture, offer, restore, recreate, badges and the header action in a browser (expectations C27–C32; §8 A–D).
- Effective store access: `User` denied and `Admin` allowed through the UI (§8 C11). DR1's retained-grant case and a role
  created after the update were not executed.
- The header action caption without the Model.xafml node (G12).
- A full `CareCrew.sln` build. Only the three sample projects were built.
- Release configuration: no test logins are created there (template `#if !RELEASE`).
- The library's default log sink in a live circuit: no `[EditDraft]` line can appear without a circuit. D1 covers the
  default-sink call only.
- The Japanese text set in the sample (only the English identity is tested).
- Whether the current Codex KB whitelist exposes only the nine read tools (Phase 0 item 9, read denied).
- Owner decisions: (S1) keep the sample in `CareCrew.sln` or move it with the library to its own repository at the NuGet
  step; (S2) whether a store-deny helper and a verification helper move into the library (G2/G3); (S3) review of
  `DenyDraftStoreToEveryRole` and README "Security" as a security item (§6); (S4) which of G1–G15 the NuGet step fixes,
  for example renaming the CareCrew vocabulary (G8/G9) is an API break for CareCrew.
