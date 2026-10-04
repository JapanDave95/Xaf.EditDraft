# Xaf.EditDraft: closing the 15 library gaps (0.2.0-preview.1)

Run `2026-10-04-editdraft-close-gaps-08c338` (collaborator: Claude Opus 5.5 implements; Codex gpt-6-astra at xhigh, read-only).
Worktree `C:\Users\owner\source\repos\Xaf.EditDraft-gaps`, branch `feature/close-gaps`, base `main` 9725721. Not committed,
not tagged, not published, not deployed. Scratch: `%LOCALAPPDATA%\collab\2026-10-04-editdraft-close-gaps-08c338\`.

Owner (2026-10-04, verbatim): "Close the gaps". The requirement is `docs/xaf-editdraft-sample-consumer-2026-10-03.md` §5
(G1–G15) plus the main session's scope per gap. Where the main session's memory and the documents differed, the documents
were followed (O-11, see §2).

## 0. Combined answer

All 15 gaps have a change in the library, the sample or the documents, and the package version is now 0.2.0-preview.1.
The library now checks its prerequisites when the application starts and stops with a message that names the fix. It
also has a deny helper for the store, a warning for roles that can still read the store, an opt-in retention sweep that
uses the application clock, a schema option with quoted names, host-neutral public names and English texts, decision
helpers, an entry point to the all-types drafts list, and a consumer guide. The build is clean. Of the new tests, all
pass except two whose own assertions are wrong; those two are escalated, not changed. Six tests run against a throwaway
LocalDB database, and the browser run on the sample confirmed capture, recreate, save-delete, the all-types list, the
store deny for a non-admin role, the hosted sweep and the startup message. Gap G1 does not occur as described on
DevExpress 26.1.4: XAF adds the non-persistent object space provider itself (decompiled and tested). Codex's diff review
found six defects, none fixed (as the brief requires). The most serious is C1: the schema option moves the library's T-SQL
to another schema but not XPO's own reads and inserts, so a consumer who sets only the option splits its drafts across
two tables. That, and the column names kept for the first host, are owner decisions.

## 1. Status

Implemented, uncommitted, 2026-10-04.
- `dotnet build Xaf.EditDraft.sln -c Release --no-incremental`: 0 warnings, 0 errors (`final\build.txt`).
- Xaf.EditDraft.Tests: 239 passed, 2 failed, 1 skipped, 242 in total (baseline on main: 185 passed, 1 skipped). The 2 failures are §6. The skip
  is C25_C31 (reads the first host's project files; skipped outside CareCrew, unchanged).
- Xaf.EditDraft.Sample.Tests: 51 passed (baseline 42), including 6 SQL Server tests on a LocalDB database that each run
  creates and drops.
- `dotnet pack` of Core and Blazor: 0.2.0-preview.1, exit 0, no warning or NU line; Core's package dependencies are
  unchanged.
- Browser proof on 127.0.0.1:5006: §7. Codex: requirement-only `tests` before any code, then one `diffreview` (§9).

Next: owner review of the security code (§5) and the decisions in §10, then git-committer on `feature/close-gaps`. A
release is then the tag workflow (owner).

## 2. Per gap

Paths: `C/` = `Xaf.EditDraft.Core/`, `B/` = `Xaf.EditDraft.Blazor/`, `S/` = `samples/Xaf.EditDraft.Sample/`.
"Status" says what the executed checks show. A source read alone is marked as such.

| G | Decision (smallest correct change) | Files | Tests / check | Status |
|---|---|---|---|---|
| G1 non-persistent provider | Check at setup (Blazor module, SetupComplete): no `NonPersistentObjectSpaceProvider` → `EditDraftConfigurationException` naming `.AddNonPersistent()`. **Finding:** DX 26.1.4 `XafApplication.Setup` adds the provider itself when none is registered (`EnsureNonPersistentObjectSpaceProvider`, decompiled; BlazorApplication does not override it). The gap's failure happens only in an application that overrides that method. Documents corrected. | C/EditDraftStartup.cs, B/EditDraftBlazorModule.cs | G1_T2, G1_DevExpress_26_1_4_adds..., browser experiments A and B | Closed; experiment A: no `.AddNonPersistent()`, app runs and the list popup opens; experiment B: override → host stops with the message |
| G2 store security | SECURITY, Claude only: `EditDraftSecurity.DenyStoreToAllRoles` (the row XAF's AddTypePermission writes; idempotent), `FindRolesThatCanReadStore` / `WarnRolesThatCanReadStore` (role rows), startup warning once per process. Sample Updater uses the helper. | C/EditDraftSecurity.cs, C/EditDraftStartup.cs, S/…/Updater.cs | SEC_* (one red, §6), sample E1, host log, browser (User denied) | Closed, owner review (§5); Codex C2, C5, C6 open |
| G3 limits of the deny | Stated in the helper's XML docs, consumer guide §5, both READMEs; the warning names each such role. | docs | G3_G5_T9... | Closed |
| G4 retention | `EditDraftRetention.Sweep` (DI or object space) deletes `ExpiresOn <= cutoff` for every owner, in batches of 1000, and logs the count; returns -1 when it cannot run. `services.AddEditDraftRetention()` registers a hosted service that sweeps only while `EditDraftCapture:Retention:Enabled` is true. English list lead no longer promises deletion; the Japanese text is unchanged (brief). | C/EditDraftRetention.cs, B/EditDraftBlazorServices.cs, C/EditDraftTexts.cs | Retention tests, sample Q4–Q6 (LocalDB), host log (sweeps every minute, 0 live rows deleted) | Closed; Codex C3 open |
| G5 clock | Sweep cutoff = the host clock's local "now" (the clock that writes ExpiresOn), passed as a parameter; documented with the GETDATE() caveat. | C/EditDraftRetention.cs, docs | Q5 (fixed clock 2030 vs the DB clock) | Closed |
| G6 SQL Server / dbo | `EditDraftStoreOptions.Schema` (default dbo); XPO's own `Schema.Table` mapping respected; `EditDraftSql.QuoteIdentifier`; writer addresses `[schema].[table]`, probe `OBJECT_ID(@p0)`. SQL Server check by the XPO data store's type, and the startup check stops when the store is positively not SQL Server. A DataStorePool is classified by borrowing one provider; an unidentified wrapper only gets a warning. | C/EditDraftServices.cs, C/EditDraftWriter.cs, C/EditDraftStartup.cs | G6_*, G1_T25 (red, §6), Q1–Q3 (quoted schema "edit drafts", table "Order") | Closed with **Codex C1 (option vs XPO mapping) and C4 open** |
| G7 absent cache | The writer keeps "absent" 30 s and "present" 5 min; the default cache constructor is unchanged. | C/EditDraftTableCache.cs, C/EditDraftWriter.cs | G7_T29, E22 unchanged | Closed |
| G8 host names | Listed names renamed (§3); store columns kept through `[Persistent]` (no migration). | many | G8_T32, G8 column test, sample C2 | Closed; column names = owner decision O-1 |
| G9 English texts | `PersonalLoginOnly`, `RecordNotVisible`, `RecreateSubSectionNotVisible` reworded; Japanese unchanged. | C/EditDraftTexts.cs | G9_T34_T35, G9_T36, E22b (changed line, §4) | Closed |
| G10 decision vocabulary | `EditDraftDecisions.Restorable/Group/SideEffect/NotRestorable/Excluded`; the gate is unchanged; the record documents its fields. | C/EditDraftMemberDecision.cs, S/…/NoteEditDraftPolicy.cs | G10_T40_T41, sample C5, S2 | Closed |
| G11 all-types list | `AddEditDraftBlazor(o => o.HeaderActionOnEveryView = true)`: off a registered ListView the header action opens the all-types list; `EditDraftListBridge` documented for host UI. The sample turns the option on. | B/EditDraftBlazorServices.cs, B/EditDraftListControllerBlazor.cs | G11_T42, browser (My Details → Drafts → `filter=all`) | Closed |
| G12 header paint style | `PaintStyle = CaptionAndImage` set by the library. | B/EditDraftListControllerBlazor.cs | G12_T45_T46 | Closed in code. The browser showed the "Drafts" caption, but the sample's own model node sets the same style, so the run without a node was not checked |
| G13 assembly identity | The assemblies take this repository's `<Version>` (0.2.0.0; the 2.6.42.0 was CareCrew's props). The InternalsVisibleTo grant to `NursingHome_Chart.Rostering.Tests` is removed. | C/Xaf.EditDraft.Core.csproj | G13_* , E6 (changed line) | Closed |
| G14 consumer guide | `docs/consumer-guide.md`; README links it; the sample README points to it. | docs | G14_*, S3 | Closed |
| G15 controller name | `EditDraftCaptureControllerBlazor` → `EditDraftCaptureController` (file and class). | C/EditDraftCaptureController.cs | G15_T64, E8 | Closed |
| O-11 | As M3 §12 defines it (row icon enable override; owner ruling "Keep for the merge; fail closed before NuGet"): `EditDraftRowOpenRule.Apply` no longer sets `Enabled = true`. The brief's "For() fallback … golden snapshot" is O-8 (M2 §13: CareCrew's `TenantChartDraftTypePolicy.For`, tried and reverted because of CareCrew's golden snapshot). It is not in this repository, and this repository has no golden test, so nothing changes here for O-8. | B/EditDraftRowOpenRule.cs | O11_*, C16 (changed line) | Closed for O-11; O-8 not applicable here |
| Version / DX floor | `<Version>` 0.2.0-preview.1 with release notes. DevExpress 26.1.4 documented as the tested floor, not lowered. No tag. | Directory.Build.props, README | G13_T47_T72_T74, T73, pack | Closed |

## 3. Public-surface renames (0.1.0-preview.1 → 0.2.0-preview.1)

| Old | New |
|---|---|
| `EditDraftStoreBase.LoginIsStaffMember` | `OwnerFlag` (column `LoginIsStaffMember` kept) |
| `EditDraftStoreBase.SubSectionOid` | `ScopeOid` (column `SubSectionOid` kept) |
| `EditDraftSeed.LoginIsStaffMember` / `.SubSectionOid` | `OwnerFlag` / `ScopeOid` |
| `EditDraftOwnerInfo(Guid Oid, bool LoginIsStaffMember)` | `EditDraftOwnerInfo(Guid Oid, bool OwnerFlag)` |
| `EditDraftRecreateDraft.SubSectionOid` | `ScopeOid` |
| `EditDraftTypePolicy.SubSectionOf` | `ScopeOf` |
| `IEditDraftRecordAccess.IsSubSectionVisible` (and `XafSecurityEditDraftRecordAccess`) | `IsScopeVisible` |
| `IEditDraftRecreateHost.IsSubSectionVisible` | `IsScopeVisible` |
| `EditDraftOwnerKind.F2StaffMember` | `EditDraftOwnerKind.HostDefined` |
| `EditDraftCaptureControllerBlazor` | `EditDraftCaptureController` |

New public API: `EditDraftStoreOptions`, `AddEditDraftStore<T>(Action<EditDraftStoreOptions>)`,
`EditDraftStoreRegistration(Type, EditDraftStoreOptions)` with `Schema/Table/QualifiedName/ConfiguredSchema/SchemaConflicts`,
`EditDraftSql`, `EditDraftStartup`, `EditDraftConfigurationException`, `EditDraftSqlServer`, `EditDraftDatabaseKind`,
`EditDraftSecurity`, `EditDraftRoleExposure`, `EditDraftRetention`, `EditDraftTableCache(TimeSpan)` and `AbsentRecheck`,
the `EditDraftDecisions` helpers, `EditDraftBlazorOptions`, `AddEditDraftBlazor(Action<EditDraftBlazorOptions>)`,
`AddEditDraftRetention()`, `EditDraftRetentionService`. Not renamed (not in the G8 list): `EditDraftRecreateOutcome.SubSectionNotVisible`,
`EditDraftTextSet.RecreateSubSectionNotVisible`, the parameters of `EditDraftOwnerRule.Decide` (staff, GeneralUser), the
log lines that name 事業所 or GeneralUser. Those are left for a later change (§10).

## 4. Existing test lines changed by the gap list or the O-11 ruling

| Test | Change | Ruling |
|---|---|---|
| EditDraftLibraryIsolationTests E6 | friend set {Blazor, Rostering.Tests, Tests} → {Blazor, Tests} | G13 |
| EditDraftLibraryIsolationTests E8 | `typeof(EditDraftCaptureController)` | G15 (mechanical) |
| EditDraftLibraryBlazorTests E22b | English `RecordNotVisible` literal | G9 |
| EditDraftLibraryM3Tests C16 | `badged.Enabled` now expected false; method renamed `..._fail_closed` | O-11 |
| EditDraftWave1Tests W38b | `DELETE FROM [{Table}]` → `DELETE FROM {Table}`; regex `\[\{Table\}\]` → `\{Table\}` (same predicates, still 5 mutations, each naming the owner once) | G6 |
| EditDraftWave1Tests W58, EditDraftWave1bTests, EditDraftLibrarySeamTests, EditDraftNewRecordTests, SampleConsumerTests | type, member and source-path renames; NewRecord SEC_D14 pins the renamed source line `IsScopeVisible(_application, policy, scopeOid)` | G8 / G15 (mechanical) |

## 5. Security (Claude only; owner review)

Written single-model under guardrails Part 4. Codex reviewed the diff for defects only (C2, C5, C6 below).

- `EditDraftSecurity.DenyStoreToAllRoles(IObjectSpace, Type store, Type roleType = null)`. For every role of `roleType`
  (default XPO `PermissionPolicyRoleBase`) it finds the role's type-permission row for the store, or creates one with
  `IPermissionPolicyRole.CreateTypePermissionObject`. It then sets Read, Write, Create, Delete and Navigate to Deny. This
  is the same row `PermissionSettingHelper.AddTypePermission(FullAccess, Deny)` writes. It does not commit and returns the
  number of roles. It logs each role the deny cannot bind. It uses only `DevExpress.Persistent.Base` interfaces and
  `BaseImpl.Xpo` types that Core already references, so the package dependencies do not change.
- XPO stores a type-permission row's type as a full name and resolves it through the types info. For a new row with no
  object space, that is `XafTypesInfo.Instance`, so `TargetType` is null when that types info does not know the store
  (observed in a probe test). The helper therefore matches rows by their stored full name as well. This keeps the deny
  idempotent where XAF's own helper would add a second row.
- `FindRolesThatCanReadStore` reads role rows; it does not evaluate permissions for a user. It reports a role as able to
  read when one of these holds: the role is administrative; it has an object or member Read ALLOW on the store or a base
  type; its nearest type permission with a Read state allows Read; or its policy is AllowAll/ReadOnlyAll with no Read
  deny. It is conservative, and Codex C6 shows where it over-reports. The startup check logs these roles once per process.
- The writer's owner fence is unchanged: W38b still pins 5 mutations, each with `[OwnerUserOid] = @pN`. The retention
  delete is the only owner-agnostic statement, and it lives in `EditDraftRetention.cs`, outside the writer.
- What was executed: the sample E1 test (full run) shows the helper writes one all-Deny row per role, including after two
  runs. SEC_G2_G3 shows that before the deny the scan finds 7 of 8 roles, and after it only administrative and
  object/member-grant roles. The sensitivity mutation (no ReadState) turned SEC_G2_G3 red. In the database update and the
  host run, the warning named only `Administrators`. In the browser, `User` was refused `SampleEditDraft_ListView` ("Access
  to this resource is prohibited").

Owner review list: `C/EditDraftSecurity.cs` (whole file), `C/EditDraftStartup.cs` (`WarnRoles`, `Run`), `S/…/Updater.cs`,
`Xaf.EditDraft.Tests/EditDraftSecurityHelperTests.cs`, consumer guide §5. The owner seam and the access seam were renamed
only (`IsScopeVisible`, `OwnerFlag`); their behaviour did not change.

## 6. Red tests, escalated (not revised)

Both are new tests from this run and both stay red on a solo rerun, so they are not order-sensitive. In each, the wrong
part is the test's own assertion.
- `EditDraftSecurityHelperTests.SEC_G2_the_deny_helper_denies_all_five_operations_to_every_role_and_is_idempotent`. Its
  final check filters rows by `p.TargetType == typeof(EditDraftTestStore)`, which is null in this test's types info (§5).
  Proposed one-line correction: filter by
  `((PermissionPolicyTypePermissionObject)p).TargetTypeFullName == typeof(EditDraftTestStore).FullName`. The same behaviour
  is shown green by sample E1 through the helper.
- `EditDraftStartupCheckTests.G1_T25_...`. It asserts the provider name "InMemoryDataStore", but XAF's
  `MemoryDataStoreProvider` uses `DataSetDataStore`. Its requirement-derived assertions pass: the problem names SQL Server
  only, and the store is classified as not SQL Server. Proposed correction: `"DataSetDataStore"`.

Pre-existing (main and candidate): sample `E1` fails when run alone. It finds the row through `TargetType`, which resolves
only after `C6` has registered `SampleEditDraft` in `XafTypesInfo.Instance`, so it depends on test order. Not changed.

## 7. Browser and host proof (Development, 127.0.0.1:5006, LocalDB `XafEditDraftSample`, login `User`)

Logs are under `host\` in the scratch folder. Chrome screenshots timed out (the tab was hidden), so the checks used the
page's text, element references and the host log.
1. Database update (`--updateDatabase --forceUpdate --silent`): `startup checks passed: store SampleEditDraft at
   [dbo].[SampleEditDraft]`; the helper's warning names only `Administrators`. On the first run the data store was a
   `DataStorePool` and was classified Unknown. The classifier was fixed to look through it, and on the second run there
   was no warning.
2. Hosted retention (`Retention:Enabled=true`, `IntervalMinutes=1` through environment variables): `retention sweep: 0
   expired draft row(s) deleted from [dbo].[SampleEditDraft] (cutoff …, application clock)` every minute. It did not delete
   the live draft.
3. New Note: typed Title and Body (set through the page's input events, because the tab was hidden), then `write create
   … rev=1`, `write supersede … rev=2`. After the reload: `new-record notice … rows=1`. Drafts → `Drafts (Note)` list with
   the new English lead text and one `New` row. Open → `recreate … Created … applied=2`. Save → `delete after save: rows=1`.
4. G11: on `ApplicationUser_DetailView` the header action is active (`policy=none … active=True`); click → `list opened:
   0 draft(s) … filter=all`, caption "Drafts".
5. Store deny: `User` → `/SampleEditDraft_ListView` → `UserFriendlyException: Access to this resource is prohibited`.
6. G1 experiment A (`.AddNonPersistent()` removed): startup checks passed and the list popup opened. Experiment B
   (application overrides `EnsureNonPersistentObjectSpaceProvider` to do nothing): `Hosting failed to start …
   EditDraftConfigurationException … No NonPersistentObjectSpaceProvider is registered … Fix: add .AddNonPersistent()`.
   Both sample files were restored byte-identical (SHA-256).
7. Not done in the browser: a sweep removing an EXPIRED row of the live sample. Expiry is fixed at 7 days, and making a
   row expired needs an SQL UPDATE, which the agent's guard blocks. The deletion at and before the exact cutoff, for
   several owners and with the application clock, is executed in sample Q4/Q5 against LocalDB. For the main session: in
   the sample database, count the rows of one draft first
   (`SELECT COUNT(*) FROM dbo.SampleEditDraft WHERE Oid = '<oid>'`), then set its `ExpiresOn` to yesterday, start the host
   with the two retention variables, and expect `1 expired draft row(s) deleted` within two minutes.

## 8. Deployment

Nothing is deployed. The output is two NuGet packages, built locally and not tagged. CareCrew keeps its in-solution copy
and is not touched, so the CareCrew Blazor app, NHM WinForms, ChartWorkflowServiceV2 and report layouts are not
affected. Schema: none. The two renamed store members keep their columns, which the sample C2 mapping test and the
existing `dbo.SampleEditDraft` used by the host confirm. When CareCrew moves to the package, it must take the renames
(§3), the O-11 icon behaviour, and the English text changes (its Japanese set is unchanged).

## 9. Codex review (pass 2 of 2): defects, not fixed

| ID | Defect | Claude's check | Outcome |
|---|---|---|---|
| C1 | `EditDraftStoreOptions.Schema` changes only the library's T-SQL target. XPO's creates and reads still use XPO's mapping (`ObjectsOwner`, default dbo), so `Schema = "drafts"` on an unqualified store splits create/read (dbo) from update/delete/retention (drafts). The guide shows the option as sufficient (`docs/consumer-guide.md` §3 step 4). | Confirmed by source: `EditDraftServices.cs` registration vs `EditDraftWriter.cs` Create/ReadOwn through XPO. The option's XML doc says it must equal ObjectsOwner, but nothing enforces that and the guide comment is misleading. Data risk: updates and deletes miss the rows; retention deletes from a different table if one of that name exists in that schema. Real path, not observed. | **Open, owner decision O-2**: derive the schema only from XPO (mapping prefix or the provider's ObjectsOwner) and drop or validate the option. |
| C2 | `DatabaseChecked` is keyed by store type only, so a second database in the same process is not checked. A check that failed with the table check off is cached as passed, and so is a failed role scan. | Confirmed by source (`EditDraftStartup.Run`). | Open (follow-up) |
| C3 | `IntervalMinutes` ≥ 71,583 overflows `Task.Delay` and stops the host, even with retention off. | Confirmed by source; the .NET limit is Codex's citation; not reproduced. | Open (follow-up: clamp the interval) |
| C4 | An unidentified data store wrapper (Unknown) only gets a warning, so a non-SQL database behind it passes startup. | Confirmed. This was a deliberate choice: Unknown warns so that a wrapped SQL Server store does not stop the app. | Open, owner decision O-3 (fail closed on Unknown?) |
| C5 | The startup warning scans only `PermissionPolicyRoleBase` roles; a custom `IPermissionPolicyRole` class outside that tree gets no warning. | Confirmed (the helper takes a role type; startup does not pass one). | Open (follow-up) |
| C6 | The role scan counts object and member Read ALLOWs without their criteria, so an impossible criterion is reported as "can read". | Confirmed. Conservative by design, but the warning text claims more than it checks. | Open (wording or criteria; owner review) |
| A1 (from Codex's could_not_determine) | With `EditDraftCapture:StartupChecks:Table=true`, a fresh database's `--updateDatabase` run stops at SetupComplete before XPO can create the table. | Confirmed by source: `Run` runs at SetupComplete, before the updater. Not reproduced. | Open; until fixed, the guide should say "turn the table check on after the first update". |

Codex also said the NHM mirror item does not apply, that the nine new-file hashes matched, and that it ran no build,
test or database step.

## 10. Owner decisions and follow-ups

- **O-1** Store column names `LoginIsStaffMember` / `SubSectionOid`: keep them for the first host's existing tables (now),
  or rename the columns (a schema change for every existing store table, and an NHM change for CareCrew).
- **O-2** Codex C1: remove or validate the schema option. Until then, use only `[Persistent("Schema.Table")]` or the
  provider's ObjectsOwner, never the option alone.
- **O-3** Codex C4: should an unidentified data store stop the application?
- **O-4** The two red tests (§6): apply the proposed one-line corrections, or not.
- **O-5** Security review (§5), including C6's wording.
- Follow-ups: C2, C3, C5, A1; the names left out of G8 (§3); sample E1 order dependence; the package README links
  `docs/consumer-guide.md` relatively, which does not resolve on a package page; the KB record for this run (the KB server
  writes into `repos\CareCrew`, which this run may not touch); the CareCrew in-solution copy now differs from the library.

## 11. Contribution log

### What each model did
- **Claude (Opus 5.5):**
  - Did Phase 0 and wrote the requirement-only pack.
  - Recorded the baseline: build 0/0, 185/1 and 42 tests.
  - Wrote the red tests, recorded red on main (14 of 15 red, plus the new-API files not compiling), and implemented every
    gap.
  - Decompiled DevExpress 26.1.4: XafApplication.Setup, the role and permission types, BaseDataLayer, DataStorePool,
    ConnectionProviderSql.
  - Found that G1 does not occur on 26.1.4 and proved it with two host experiments.
  - Found and fixed two implementation faults during the run: the null `TargetType` row matching, and DataStorePool
    classified Unknown.
  - Ran the sensitivity mutations, the LocalDB tests, packing and the browser proof, then assembled the candidate and
    wrote this report.
  - Got wrong: two new tests with wrong assertions (§6), and an IVT test assertion that would have failed on a legitimate
    csproj comment (corrected before it ever ran against the implementation; it had failed on main for the right reason).
    Missed what Codex found: C1–C6.
  - Process slip: the first G9 and G14 tests needed doc text that was written later. Expected.
- **ChatGPT (Codex gpt-6-astra, xhigh, codex-cli 0.153.4, read-only):**
  - `tests` a1: 78 requirement-only expectations (T1–T78). They shaped the tests: the exact-cutoff and multi-owner sweep
    (T12–T14), the application-versus-database clock (T18/T19), quoted schema with a space and a keyword table (T23),
    a non-SQL provider rejected without running a statement (T25), the optional table check (T26/T27), and the O-11
    fail-closed pins (T66–T70).
  - `diffreview` a1: 6 defects (C1–C6) and 3 could_not_determine items. Claude confirmed all six by source and
    reproduced none of them; one could_not_determine item became A1.
  - No file_change. It read paths outside the worktree (AGENTS/CLAUDE files, the CareCrew guardrails doc) and made 4 web
    lookups in the stream (Task.Delay limits) although `-Search` was off (cached search mode).

### Found issues, by tool
Severity rule: nothing here is wrong in production (nothing is deployed). Real paths come first, then wording and
coverage.

| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| C1 | Schema option vs XPO mapping split | Codex | Confirmed | source | rows missed or another table swept / option set without XPO mapping / high / no | two-schema LocalDB trace | Open, O-2 |
| C2 | Startup DB check cached per type, failures cached as passed | Codex | Confirmed | source | missed checks / multi-database or transient failure / high / no | two databases in one process | Open |
| C3 | Huge interval stops the host | Codex | Confirmed (static) | source + .NET limit | host stop / interval ≥ 71,583 / medium / no | host with interval 72000 | Open |
| C4 | Unknown store passes startup | Codex | Confirmed (design) | source | late T-SQL failure / unknown wrapper / high / no | wrapped non-SQL provider | Open, O-3 |
| C5 | Custom role classes not scanned at startup | Codex | Confirmed | source | missing warning / custom IPermissionPolicyRole / high / no | custom role host | Open |
| C6 | Conditional grants reported as effective | Codex | Confirmed | source + XAF docs | false warning / criterion never matches / high / no | `1 = 0` object grant | Open, O-5 |
| A1 | Table check blocks a fresh database's update | Claude (from Codex's could_not_determine) | Confirmed (static) | source | update cannot create the table / check on with an empty database / medium / no | `--updateDatabase` with the check on | Open |
| A2 | G1 premise false on DX 26.1.4 | Claude | Confirmed | decompile + host experiments A/B | wrong guidance / every consumer / high / no | experiments | Documents corrected; check kept |
| A3 | Role rows with null TargetType missed (non-idempotent deny) | Claude | Confirmed, fixed | probe test, SEC_G2_G3 | duplicate rows, wrong scan / types info without the store / high / no | SEC_G2_G3 | Fixed by full-name match |
| A4 | XAF's DataStorePool classified Unknown | Claude | Confirmed, fixed | update log run 1 vs 2 | SQL Server check silent / every pooled app / high / no | update log | Fixed (look-through) |
| A5 | Two new tests with wrong assertions | Claude | Confirmed | test output, solo reruns | red suite / always / high / no | proposed corrections | Escalated, O-4 |
| A6 | Sample E1 depends on test order | Claude | Confirmed (pre-existing) | solo runs on main and candidate | false red / run alone / high / no | solo run | Reported |
| A7 | The brief's "O-11 For()" is O-8 in CareCrew | Claude | Confirmed | M2 §13, M3 §12 | wrong change / — / high / no | document read | O-11 done as the documents define it |

Found independently by both: none (Codex's review saw Claude's decisions first).

### Codex calls

| Run | Call | Attempt | Path | Started | Duration | state | validation | Exit | PID | Model / effort requested | Effective effort | reasoning_output_tokens | Search | MCP calls | Commands (non-zero) | file_change | Outside-repo paths | Prompt / output / candidate SHA-256 | Pack | codex-cli |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 08c338 | tests | a1 | `…\tests\a1` | 15:13:05 | 10.3 min | success | ok | 0 | 13520 | gpt-6-astra / xhigh | not observable | 8,727 | off | 0 | 2 (0) | 0 | `powershell.exe` only | `C7576F5D…` / `9782D2D3…` / — | requirement-only (`tests\req`, `-SkipGitCheck`; isolation by convention: out.md cites only REQUIREMENT.md) | 0.153.4 |
| 08c338 | diffreview | a1 | `…\diffreview\a1` | 16:07:53 | 12.4 min | success | ok | 0 | 41812 | gpt-6-astra / xhigh | not observable | 7,518 | off (4 web lookups in the stream: cached mode) | 18 (KB 1, dxdocs 17) | 13 (3) | 0 | AGENTS/CLAUDE candidates, CareCrew guardrails doc, powershell.exe | `727D9397…` / `82033741…` / candidate `E907A8E7…` (diff `A78B47F2…`, 48 files, unchanged during review) | v1 | 0.153.4 |

Calls: 2, attempts: 2, both accepted. No retry. Passes used: 2 (requirement-only expectations; diff review).

### Setup checks (Phase 0; outputs in `preflight\`)

| # | Item | Result |
|---|---|---|
| 1 | BASH_MAX_TIMEOUT_MS 2400000 | present |
| 2 | Read-only query connection | not applicable: no shared database was queried; only the throwaway LocalDB databases (sample, test-created) |
| 3 | Repo trusted | present (the hook blocked commands in this session) |
| 4 | Manifest | all 7 files match |
| 5 | Hook fires / Monitor not blocked | `git push --dry-run origin HEAD` blocked by the hook; a `Get-Date` Monitor ran |
| 6 | collab.rules | plain `git push origin main` → forbidden; the wrapped shape was not run (the hook blocked the command text, a false positive) |
| 7 | prompt-input | no AGENTS.md in this repository (Codex got the CLAUDE.md text in the review pack) |
| 8 | Tool boundary | no MCP tool can write a database, migrate, deploy, push or restart a service |
| 9 | Tool parity | Codex: blazor-knowledge-base with `enabled_tools` = the 9 read tools, dxdocs; also node_repl, cua_repl (Claude has neither) |
| 10 | Models | gpt-6-astra supports xhigh |
| 11 | Run id / scratch / salt / binary | 08c338; codex-cli 0.153.4 at `C:\Users\owner\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe` |
| 12 | Snapshot | 9725721, clean worktree at start |
| 13 | Policy drift | collaborator.md "Cost" still says medium (ground rule 11: xhigh); CLAUDE.md "must not be empty" vs D1 (scoped line allowed). Reported, not edited |
| 14 | Web search | off (`-Search` not passed); the diff review still made 4 lookups (cached mode) |

AGENTS.md: this repository has none. It was not created, because the template points to CareCrew's CLAUDE.md and describes
CareCrew's deployment, which would put host vocabulary into a public library repository (gap G9/G8 territory). This is a
deviation from Phase 0 for the owner to decide.

### Redaction
None needed: no personal data was read. The sample's test logins are `Admin` and `User`. The connection strings involved
are LocalDB with integrated security (no credential), and no appsettings file was printed.

### Inputs Codex did not have
- `tests`: by design, only the requirement and runtime facts.
- `diffreview`: everything up to the freeze. After the freeze, Codex did not see this write-up, which therefore was not
  cross-reviewed. No candidate file changed after the freeze (launcher check), and the conclusions above rest on Claude's
  source reads.

### Run ledger
Appended to `%LOCALAPPDATA%\collab\ledger.jsonl` with `tools/collab/append-ledger.ps1` (counts by hand from the table above:
A1–A7 Claude, C1–C6 Codex; open = C1–C6 + A1; escalated = O-1..O-5; hook false positives = the wrapped execpolicy check and
a Select-String over the hook file):

```
{"run":"2026-10-04-editdraft-close-gaps-08c338","date":"2026-10-04","topic":"xaf-editdraft-close-gaps","attempts":[{"call":"diffreview","attempt":1,"state":"success","validation":"ok","accepted":true,"exit":0,"minutes":12.4,"commands":13,"nonzero_exits":3,"outside_repo":8,"file_changes":0,"reasoning_tokens":7518,"output_tokens":15862,"search":false},{"call":"tests","attempt":1,"state":"success","validation":"ok","accepted":true,"exit":0,"minutes":10.3,"commands":2,"nonzero_exits":0,"outside_repo":1,"file_changes":0,"reasoning_tokens":8727,"output_tokens":15060,"search":false}],"findings":{"claude_confirmed":7,"claude_rejected":0,"codex_confirmed":6,"codex_rejected":0,"both":0,"unverifiable":0,"open":7},"correlated_error_events":0,"escalated_to_owner":5,"passes":2,"hook_false_positives":2}
```

## 12. Not verified / open questions

- C1–C6 and A1 were not reproduced (source reads only).
- An upgrade of a populated pre-change store table: the column names are pinned by mapping and the host used the existing
  `dbo.SampleEditDraft`, but no row written by 0.1.0-preview.1 was read back by 0.2.0-preview.1 code.
- In the live sample, the sweep deleting an expired row (§7 item 7; executed only in LocalDB tests).
- The header action caption without the sample's model node (G12).
- XAF's own permission evaluation for the roles the scan reports (§5); base-type permission semantics beyond the
  documented priority.
- The Japanese list lead's deletion statement in an application without a sweep (kept by the brief; documented).
- CI (GitHub, no LocalDB): the six SQL Server tests skip there; the CI run was not executed.
- Release configuration of the sample (no test logins).
