# 入力控 for NEW (never saved) records — build M1–M3 (2026-10-03)

Collaborator run `2026-10-03-edit-draft-new-records-build-f4b916`. Implementer: Claude (Opus 5.5). Reviewer: ChatGPT (Codex CLI 0.153.4, gpt-6-astra, effort xhigh), read-only.
Worktree `C:\Users\owner\source\repos\CareCrew-newrecord`, branch `feature/edit-draft-new-records`, HEAD `132782b1` (= master). Everything below is UNCOMMITTED in the worktree. Design: `docs/edit-draft-new-records-design-2026-10-02.md` (untracked). Owner rulings 2026-10-03 as given in the brief.
Paths are repo-relative; `Core/` = `Xaf.EditDraft.Core/`, `Blazor/` = `Xaf.EditDraft.Blazor/`, `Server/` = `CareCrew.Blazor.Server/`. Line numbers are of the final working tree.

## 0. Combined answer

A never-saved record's typed input now survives F5 for 残業・有給, ToDo, 夜間巡回時間 and 苦情対応. The generic engine captures a new record when its policy opts in (`AllowNewRecords`) and the runtime key `EditDraftCapture:NewRecords:Enabled` is on. It keys the `dbo.EditDraft` row by `TargetOid = Guid.Empty`, seeds the context the record's construction defaults depend on at the first genuine edit, and keeps every screen object's Oid in an optional `prov` header of the version-1 payload. The 「入力控」 list shows such a draft as 「新規」. Its 開く first runs the owner, "already saved?", Create-permission and 事業所 checks. It then recreates the record in a modal window, claims the row in one fenced statement that adds the new object's Oid, fills the fresh object directly, and reports success only after the new screen has attached the draft. The type's list shows 「新規の入力控が n 件あります。上の「入力控」から開けます。」 once per activation, and the first 保存 deletes the draft.

There is no schema, Module, NHM or chart-controller change. All four projects build with 0 errors, and the 79 new tests pass. Every existing test keeps its outcome except W38b, which pins "exactly four writer mutations"; the owner-required claim-with-header statement is a fifth, so W38b is escalated, not revised. Codex's diff review found six further defects, all fixed after the review and not cross-reviewed. Out of scope: new rows typed inline in a list, TenantSubSection, unsaved object graphs, and the duplicate that arises when the original screen is still open and both screens save (D11, accepted with the warning text). Nothing is browser-verified yet (M4).

## 1. Status

> **Status note, 2026-10-03 (main session, after this run):** W38b's pin was updated to 5 statements on the owner's ruling (185/185); the owner's Dev2 M4 run: "They all worked as expected". Committed d225ad66 / 8abc3ca6 / f062a326, merged into master as 04c69fdc, pushed. Not deployed (`EditDraftCapture:NewRecords:Enabled` is false in Production). KB fix-549. Still owed: the owner's review of the §14 security files (D14) and the chart Create-check follow-up (D12). The paragraph below is the state at the end of the build run.

Implemented, not committed, not deployed, not browser-checked (M4 is the owner's Dev2 pass, §11). No schema change, no `NursingHome_Chart.Module` change, no NHM change, no chart controller change, no database used. Builds: Core, Blazor, CareCrew.Blazor.Server and CareCrew.Win, 0 errors. Tests: one PRE-EXISTING library test is red as a consequence of the owner's ruling (W38b, §6.3); it is not revised and is escalated. Owner decisions needed: W38b's disposition (§6.3), the §5 security review (D14, files in §14).

## 2. What was built

### M1 — Core (`Xaf.EditDraft.Core`)

| Piece | Where | What it does |
|---|---|---|
| `AllowNewRecords` | `Core/EditDraftTypePolicy.cs:147` | per-type opt-in, default false (owner D9) |
| `NewRecordReconstructionOrder` | `Core/EditDraftTypePolicy.cs:156` | members seeded at a new record's first genuine edit and applied first on recreate (deviation 1, §8) |
| Runtime key | `Core/EditDraftSwitch.cs:66-81` | `EditDraftCapture:NewRecords:Enabled`, read through the configured section (`In(...)`), fail-closed parse; `DecideNewRecords` = global AND type AND new-record key (D15) |
| Admission lift | `Core/EditDraftCaptureControllerBlazor.cs:77-79` (`IsAdmittedViewIncludingNew`), used at `:276` | a NEW record is admitted when its policy has AllowNewRecords and the view part of the existing rule holds (generic, root, approved view). The existing `IsAdmittedView` (`:67-69`) is unchanged and still used by the restore offer (existing records only) |
| Per-event key check | `Core/EditDraftCaptureControllerBlazor.cs:351` | while the record is new (or cannot be told), the new-record key must be on, per event |
| Genuine-edit rule + seeding | `Core/EditDraftCaptureRules.cs:30-60` (`Capture`), `:67-86` (`Seed`); called at `Core/EditDraftCaptureControllerBlazor.cs:403-404` | no entry or only a SEEDED entry → a change only when the value differs from the BASELINE; a typed entry → a change when it differs from the entry (a member typed back keeps its entry, D8). On a new record the first genuine edit records the triggering member as TYPED, then seeds the missing context members (`Seeded = true`). For existing records it is exactly the old rule (no entry is ever seeded) |
| Fresh-start paths | `Core/EditDraftCaptureRules.cs:97-114` (`FreshAfterGone`); `Core/EditDraftCaptureControllerBlazor.cs:471-473` (rebuild, also re-seeds), `:504` (retired) | never-stored TYPED entries + every SEEDED entry + the reconstruction members even when typed and stored (post-review D1) + the prov history; null when no never-stored typed entry survives |
| Keying per write | `Core/EditDraftCaptureControllerBlazor.cs:537-566` (`BuildSnapshot`), `Core/EditDraftNewRecordRules.cs:33-38` (`Key`) | while new: `TargetOid = Guid.Empty`, `IsNew = true`, the screen object's Oid at the head of prov, plus the new-record gate and the context list on the snapshot; after the save: the record's Oid; undecidable → a key the guard refuses |
| `EditDraftSeed.IsNew` | `Core/EditDraftWriter.cs:28` | plain data, not a column |
| Write guards | `Core/EditDraftNewRecordRules.cs:44-45` (`IsWritable`), used at `Core/EditDraftCaptureControllerBlazor.cs:590` (StartWrite) and `Core/EditDraftWriter.cs:173` (Create) | owner required; `TargetOid == Empty` exactly when `IsNew` |
| New-record write gate | `Core/EditDraftCaptureControllerBlazor.cs:424` (`DraftSnapshot.NewRecordsGate`), `:630` (`RunOneWrite`, per ticket) | a new record's queued writes re-read the new-record key at every write; an existing-record write never depends on it (post-review D2) |
| prov header | `Core/EditDraftPayload.cs:47-63` | §3 |
| Claim with the header | `Core/EditDraftWriter.cs:271-279` (`TryClaimNew`) + interface, forwarder, no-store | ONE statement: `SET EditorInstanceId, Revision+1, DeletedOn = NULL, LastCapturedOn, Payload, EntryCount WHERE Oid AND Revision AND OwnerUserOid AND ExpiresOn > now AND TargetOid = Guid.Empty` |
| Delete on save | unchanged path `Core/EditDraftCaptureControllerBlazor.cs:733` (`DeleteOwn(own, owner, editor)` from `Committed`) | the first save deletes the screen's row; for a recreated record that is the row the recreate claimed, because the screen took the claimed editor id (D3) |
| Attach with the claimed editor id | `Core/EditDraftCaptureControllerBlazor.cs:144-163` (overload), `:166-179` (take / attach + acknowledge), used in `BindTo` `:290-293` | the recreated screen takes the pending draft BEFORE the getters (a recreated record does not run them again — post-review D4), snapshots the baseline, attaches the claimed row with the claimed editor id and acknowledges |
| Pending-adoption contract | `Core/EditDraftPendingAdoptions.cs` | Core-owned, per circuit, weak-keyed on the record, taken once, one-way acknowledgement |
| `ApplyNew` | `Core/EditDraftRestorer.cs:258-279` (+ `Apply(..., leading)` `:103-105`, `:130`; `LeadingFirst` `:237-245`; result `:282-313`) | InitializingGetters first; 戻せません and no-spec members dropped by the engine (a group dropped whole); not-writable members dropped (a group dropped whole); seeded context first in NewRecordReconstructionOrder; an unresolved reference is not nulled; reports typed / seeded entries not applied |
| Recreate order | `Core/EditDraftRecreate.cs:156-247` | §4 |
| Pure rules | `Core/EditDraftNewRecordRules.cs` | row kind / state text, classification, typed entries, `WithProvisional`, `SavedState` over the whole history, `NoticeCount`, `NoticeText` |
| Create access (SINGLE-MODEL) | `Core/EditDraftAccessSeam.cs:59-89` (`EditDraftCreateAccess`) | S1 + S2 |
| 事業所-by-Oid seam (SINGLE-MODEL) | `Core/EditDraftAccessSeam.cs:23` (interface member, default fail-closed), `:47-48` (library default) | S5 (i) |
| Texts ja + en | `Core/EditDraftTexts.cs:118-141` (properties), `:248-270` (ja), `:370-392` (en) | 新規 row, notice, recreate messages, D11 warning, failure outcomes, read-only display |

### M2 — Blazor (`Xaf.EditDraft.Blazor`)

| Piece | Where |
|---|---|
| 「新規」 row kind in the gear list and the per-type header list | `Blazor/EditDraftListControllerBlazor.cs:299` |
| 開く on a 「新規」 row → recreate, before any lookup by an empty Oid | `Blazor/EditDraftListControllerBlazor.cs:353` |
| Recreate outcome handling: success toast with the input time and the D11 warning, Warning instead of Success when entries were not applied or the restore guard cancelled a save/rollback (post-review D5); the not-applied read-only display without 破棄; the D9 display; the already-saved notice with 「保存済みの記録を開く」; the saved-check-failed question with 「それでも作成する」/「やめる」; refusal messages | `Blazor/EditDraftListControllerBlazor.cs:377-435`, `ShowDraftEntries` `:440-471`, `OpenSaved` `:474-485`, `Defer` `:488-497` |
| XAF side of the recreate: owner-scoped read, secured "already saved?" read, security calls, candidate in its own object space, restore guard, modal window, acknowledgement, guard state, discard unsaved | `Blazor/EditDraftRecreateHostBlazor.cs` (new) |
| ListView notice, once per activation, through `ListOwn` | `Blazor/EditDraftListBadgeControllerBlazor.cs:105`, `:258-292` |
| Per-circuit registration of the adoption contract | `Blazor/EditDraftBlazorServices.cs:21` |
| Read-only view: hidden flag `HideDiscard` and the controller honouring it | `Blazor/EditDraftModels.cs:103`, `Blazor/EditDraftRestorePopupControllerBlazor.cs:137` |

### M3 — CareCrew (`CareCrew.Blazor.Server`)

| Piece | Where |
|---|---|
| 残業・有給 opt-in; seeds 職員, 日付, 開始時刻, 終了時刻 | `Server/Infrastructure/EditDrafts/Policies/StaffOverTimeHolidayEditDraftPolicy.cs:34-35` |
| ToDo opt-in; seeds ToDoItem (a parent supplied by a ToDoItem's nested list — post-review D6) | `ToDoEditDraftPolicy.cs:29-30` |
| 夜間巡回時間 opt-in; seeds SubSection (an [Association] a nested route would supply — post-review D6) | `NightRoundsTimeEditDraftPolicy.cs:29-30` |
| 苦情対応 (TenantCase) opt-in after the [AutoIncrement] check (§5) | `TenantCaseEditDraftPolicy.cs:29` |
| TenantSubSection: NOT opted in (D10) | unchanged |
| 事業所-by-Oid on the record-access seam (SINGLE-MODEL) | `Server/Infrastructure/EditDraftAccess.cs:57-79`, `:101-103` |
| Runtime key | `Server/appsettings.json`, `appsettings.Development.json`, `appsettings.Production.json`: `EditDraftCapture.NewRecords.Enabled` = false / true / false (added with Edit; only the section's boolean lines were read) |

## 3. The prov header

`EditDraftPayload.Provisional` (`Core/EditDraftPayload.cs:47`), JSON name `prov`, serialised only when not null, after `entries`:

```json
{"schema":1,"type":"StaffOverTimeHoliday","entries":[ … ],"prov":["<newest screen object Oid>","<older>","<original>"]}
```

- Newest first. Every write of a still-new record puts the screen object's Oid at the head (`BuildSnapshot`). The recreate's claim statement stores the recreated object's Oid at the head (`EditDraftNewRecordRules.WithProvisional`). An Oid already at the head is not added again, and Guid.Empty is ignored.
- The payload version stays 1 (D2). A payload without `prov` reads exactly as before (`ProvisionalOids` is empty). Every existing-record and chart payload keeps today's text, pinned by the golden snapshot and `T8_N08`.
- Both fresh-start rebuilds copy it (`FreshAfterGone`).

## 4. The recreate sequence as implemented (`Core/EditDraftRecreate.cs`)

1. Owner, re-resolved now (none → 「この画面の入力控は、職員個人のログインで使えます。」). The draft is read with the owner in the query (`ReadOwn`) and must be:
   - live (`ExpiresOn > now`); a 破棄'd row opened from the list's search is allowed, and the claim un-discards it;
   - a NEW-record row (TargetOid = Empty);
   - readable (payload version 1 and valid JSON);
   - of a registered generic policy with `AllowNewRecords`.
2. No typed entry a fresh record can take (all 戻せません or without a member spec) → the D9 read-only display of those entries; nothing is created.
3. "Already saved?" over every Oid of prov, read through a SECURED object space:
   - a found record → 「この入力控の記録はすでに保存されています。」 with 「保存済みの記録を開く」 (and 破棄 for the leftover draft);
   - a failed read with nothing found → the person is asked; 「それでも作成する」 re-runs the whole sequence allowing that failure, and a found record still refuses.
4. Security before anything is created (single-model):
   - `EditDraftCreateAccess.MayCreate`: S2 is the policy flag, a listed ListView with model AllowNew and the approved DetailView with model AllowEdit; S1 is `DataManipulationRight.HasPermissionTo(type, Create)` under `IRequestSecurity`;
   - a non-empty stored SubSectionOid → `IsSubSectionVisible`.
5. The candidate: `Application.CreateObjectSpace(type)` + `CreateObject(type)`. AfterConstruction runs; nothing is saved.
6. ONE fenced statement, `TryClaimNew`, with a new editor id and the payload whose prov has the candidate's Oid at the head. "now" is read again here (post-review D3). A lost claim discards the candidate: 「この入力控は戻せません（ほかの画面で戻されたか、変更されたか、期限切れです）。」.
7. The restore guard is opened on the candidate's object space; then `ApplyNew` (InitializingGetters first).
8. `IsRecordVisible` on the FILLED object (S5 ii). A refusal discards it: 「この入力控の記録の事業所は表示できません。」.
9. An `EditDraftPendingAdoption` {draft row, claimed revision, owner, claimed editor id, claimed payload} is offered for the object.
10. `CreateDetailView(os, approved view, root, obj)` is shown with `TargetWindow.NewModalWindow` (D6). That screen's capture controller activates inside `ShowView` (DX 26.1.4 `BlazorShowViewStrategy.cs:53-63, :106-108`), takes the adoption, attaches with the claimed editor id and acknowledges.
    - Acknowledged → the guard closes after the posted work, and a success toast follows: 「入力控（入力 yyyy/MM/dd HH:mm）から記録を作成しました。まだ保存されていません — 内容を確認して保存してください。元の画面がまだ開いている場合は、そちらで保存してください。」.
    - Partly applied → 「…（n 項目は戻せませんでした）…」 plus the read-only display of exactly those entries, without 破棄. A guard violation → the guard's message is appended and the toast is a Warning.
    - Not acknowledged → guard disposed, object space rolled back, view closed: 「入力控を新しい画面に引き継げなかったため、…入力控は残っています。もう一度開いてください。」.

Every refusal up to step 5 changes nothing. Every failure after the claim discards the candidate, logs the step (`[EditDraft] recreate XXXXXXXX: <Outcome> at step n …`) and leaves the row live at its new revision.

## 5. The [AutoIncrement] check (D10)

`TenantCase.CaseNumber` carries `LlamachantFramework.AutoIncrementingID.Attributes.AutoIncrementAttribute` (`NursingHome_Chart.Module/BusinessObjects/Tenants/TenantCase.cs:66-73`). I decompiled the package `Llamachant.ExpressApp.AutoIncrementingID` 26.1.4.1 with ilspycmd; this is the version `Directory.Packages.props` resolves (`LlamachantFrameworkVersion` = 26.1.4 + ".1"; dll SHA-256 `2BAF45DF…C859`). The output went to the session scratchpad, with excerpts kept in `%LOCALAPPDATA%\collab\2026-10-03-edit-draft-new-records-build-f4b916\autoincrement-evidence\`, not in the repo. It shows:
- `TriggerUpdater.UpdateDatabaseAfterUpdateSchema` creates `CREATE OR ALTER TRIGGER [t<Type>_<Member>_Trigger] ON [<table>] AFTER INSERT … UPDATE t SET t.[<col>] = sub.MaxID + g.RowNum …`. SQL Server assigns the number when the row is INSERTed.
- `AutoIncrementingPropertyHelper` subscribes each non-nested object space's `Committing` (collects new objects of the type), `Committed` (`ReloadObject`, then `CommitChanges` if modified) and `RollingBack`. Nothing runs at construction.

Result: a recreate candidate discarded unsaved inserts nothing and consumes no number, so TenantCase is opted in. Executable evidence in this run is only reflection (`N29_D10_…`: the attribute's namespace, that `TriggerUpdater.UpdateDatabaseAfterUpdateSchema` exists, and that the helper's hooks are the three object-space handlers). The trigger itself is not executed here (no database); Codex lists the timing as could_not_determine. Browser item 12 settles it.

## 6. Tests

### 6.1 Runs (`--artifacts-path artifacts/claude-test/20261003-f4b916`, deleted at the end)

| Run | Xaf.EditDraft.Tests | Rostering (`FullyQualifiedName~Draft\|FullyQualifiedName~Golden`) |
|---|---|---|
| Baseline (unmodified) | Passed 114 / Failed 0 / Total 114 | Passed 615 / Skipped 4 / Total 619 |
| Reviewed candidate (before Codex) | Passed 180 / Failed 1 (W38b) / Total 181 | Passed 622 / Skipped 4 / Total 626 |
| W38b solo rerun | Failed 1 / Total 1 (same message) | — |
| T17 mutant on the reviewed candidate | Passed 172 / Failed 9 / Total 181 | — |
| T17 restored (byte-identical, SHA D94384F4…3844) | Passed 180 / Failed 1 / Total 181 (0 identity changes) | — |
| Post-review, first run | Passed 184 / Failed 1 (W38b) / Total 185 | Passed 622 / **Failed 1** / Skipped 4 / Total 627 — `E21_D1_a_snapshot_carries_its_screen_clock…` broken by my D2 edit (it pins `{ Clock = _clock }`); implementation changed back to keep that text, test not touched |
| **Final** | **Passed 184 / Failed 1 (W38b) / Total 185** | **Passed 623 / Skipped 4 / Total 627** |
| T17 mutant on the final bytes | Passed 176 / Failed 9 / Total 185 | — |
| T17 restored on the final bytes (byte-identical, SHA DE348D83…8C47) | Passed 184 / Failed 1 / Total 185 (0 identity changes vs Final) | — |

T17 red set (both times): `T1_new_admitted_when_opted_in_root_approved`, `T3_E07_N06_…`, `T5_N05_…` ×3, `T6_E08_N05_…`, `T6_N06_…`, `E09_D8_…`, plus the already-red W38b. As Codex noted, the pure keying, payload and recreate tests do not depend on admission, so they stay green under the mutation.

### 6.2 Identity comparison (TRX, className.testName), Final vs Baseline
- Library: 112 identities → 183. Missing 0. Outcome changed 1 (W38b, Passed → Failed). New 71, all Passed.
- Rostering: 610 → 618. Missing 0. Changed 0. New 8, all Passed. The 4 skipped are the same `AttendanceDraftWriterSqlTests.*` as at baseline.
- Golden `NursingHome_Chart.Rostering.Tests/Golden/TenantChartDraft.golden.txt`: SHA-256 `72325EE15DD4E28A2AA4C1C19B1FCA84401E7F459332FB22B42C1C08358C3144` before and after (byte-equal); its tests are green.
- Limitation: two library results share a className.testName (114 results, 112 identities), so the comparison counts them once.

### 6.3 The red pre-existing test — ESCALATED, not revised
`Xaf.EditDraft.Tests/EditDraftWave1Tests.cs:182-194` `W38b_the_four_mutations_each_name_the_owner_including_the_multi_line_supersede` asserts that the writer has exactly 4 `UPDATE|DELETE FROM [{Table}]` statements and 4 owner predicates. The brief's "claim-with-header statement (fenced on Oid + Revision like every writer statement)" is a fifth statement, with the owner predicate, so the counts are now 5 and 5.

Message: `Expected Regex.Matches(writer, @"\b(UPDATE|DELETE FROM) \[\{Table\}\]").Count to be 4 because exactly these four mutations exist, but found 5.` The solo rerun gave the same result. Classification (d): the owner's ruling changes what the test pins. Codex (D7) agrees: "consistent with the newly required statement… removing the required claim statement would not settle the requirement."

**Owner decision:** update the pin to 5 mutations and 5 owner predicates, naming `TryClaimNew`, or choose another arrangement.

### 6.4 New tests and what they cover
`Xaf.EditDraft.Tests/EditDraftNewRecordTests.cs` (71 results). Labels come from Codex's tests a1 of this run (E01–E44 reused or changed, N01–N33) and the design's T-ids:
- **Admission and switch:** T1 admission truth table, existing rule unchanged, chart and inline-list exclusions (N02, E06, E43, E30); N01 key parse and section re-read.
- **Capture:** T2 Oid from construction kept by the save (N04); T3 bare notifications capture nothing and a started payload is dropped (E07, N06); T5 each seed member as the only first edit (N05); T6 a non-context first edit seeds three, seed → typed flip, a baseline-equal notification on a seed writes nothing, seeding only for new records (E08, N06); E09/D8 a reverted member keeps its typed entry, and null/empty/false are values.
- **Keying and payload:** T4 keying and guards (N03); T7 prov history including two recreations without an edit (N09); T8 header absent → today's text, version 1 (N08); T9 fresh start keeps seeds and history, and an existing payload is rebuilt as before (N07).
- **ApplyNew:** T10/T11 context first, 日付 before the times, day D rebuilt on D+1 (N17, E25); T10 references, 戻せません passed directly, not-writable members, a throwing permission check (N15, N16, E27, E11); T10 getters before the replay (N14, E24); T10 groups move whole and nothing is saved (E26, E41); N15 classification.
- **Recreate:** T13 already-saved over the whole history (N11, E17); T14 row kind and routing (E18); T15 notice count and wiring (N22, E22); T12 recreate order, refusals before the claim (13 cases), failures after the claim (4 cases), the read-only outcome, the failed saved check, two contenders (N12, N13, N18, N19, N20, E20, E29, E31, E36, E37, E38, E44).
- **Adoption, writer, texts:** N19 adoption contract, per-circuit registration, attach with the claimed editor id, delete on save (T16, N27); N10 claim-statement scan and no-store fail-closed; N24 texts.
- **Security (single-model):** SEC create-access decision and fail-closed seam defaults.
- **Post-review (not cross-reviewed):** D2 per-ticket gate, executed through `RunOneWrite` with a counting writer; D1 context kept in a fresh start; D3 claim "now"; D5 guard violation.

`NursingHome_Chart.Rostering.Tests/EditDraftNewRecordCareCrewTests.cs` (8):
- opt-ins (N02, D9, D10, E30) and seed lists (N05, N28, post-review D6);
- the real 残業・有給 class seeds on the first genuine edit only (N05, N06);
- yesterday's 残業・有給 draft rebuilt on today's object (N17, T11);
- ToDo / 夜間巡回時間 construct with nothing set (N28);
- a ToDo whose ToDoItem was set by its route keeps it through capture and recreate (post-review D6, executed in memory);
- the three appsettings keys (N01, D15);
- TenantCase [AutoIncrement] reflection (N29).

Not covered by an executable test here (browser or database only): the SQL text executing against SQL Server; controller activation timing; the modal; close-with-いいえ (D13, N25); the notice's presentation; two real tabs (E33); the browser part of Save-and-New (N27).

## 7. Codex calls

| Run | Call | Attempt | Path | Started | Duration | state | validation | Exit | PID | Model / effort requested | Effective effort | reasoning tokens | Search | MCP calls | activity (cmds / non-zero / file_change / outside-repo) | prompt / out / candidate sha256 (first 8) | Pack | CLI |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| f4b916 | tests | a1 | `…\tests\a1` (cwd `tests\req`, `-SkipGitCheck`) | 09:23:28 | 9.2 min | success | ok | 0 | 60180 | gpt-6-astra / xhigh | not observable | 5,018 | off | 0 | 4 / 0 / 0 / 1 (powershell.exe) | AE3D0D47 / 3F3BE382 / — | REQUIREMENT.md (brief + design + its own E01–E44), SHA 40BD3C33 | 0.153.4 |
| f4b916 | diffreview | a1 | `…\diffreview\a1` | 10:07:35 | 11.1 min | success | ok (manifest frozen, 28 files unchanged during the review) | 0 | 34960 | gpt-6-astra / xhigh | not observable | 8,211 | off | 7 (KB lookup 1, dxdocs search 3, get_content 3) | 16 / 2 / 0 / 1 (powershell.exe) | 0876AACB / A80CACE7 / 555BDD3D (diff text 0B1D49AD) | parity-pack-v1 (198 KB) | 0.153.4 |

Input tokens: tests 143,687 (cached 109,440); diffreview 2,415,503 (cached 2,204,928). Codex found that the dxdocs pages resolved to 25.2 / 26.1.5 and did not use them as exact-version proof. The `tests` isolation is by convention (absolute-path reads remain possible); its out.md cites only REQUIREMENT.md, and its 4 commands read only that file. In the tests output Codex reported that the Japanese text of REQUIREMENT.md looked corrupted in its console (the file is UTF-8 and hash-matched), so it wrote semantic, not byte-exact, expectations for the Japanese literals.

## 8. Found issues, by tool

Severity rule: top rank only for a defect proven wrong in production; a real path not yet observed is one rank lower; debt lower again. Nothing here is observed in production (the feature is not deployed).

| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| X1 | W38b pins exactly 4 writer mutations; the owner-required claim-with-header is a 5th | Claude (test run); Codex D7 concurs | correct (requirement vs test) | §6.3 | acceptance gate red / certain / run + solo rerun / n/a | owner disposition of the pin | ESCALATED, test not revised |
| X2 | `ReconstructionOrder` (the brief's word) is pinned empty for generic policies by W1 and also orders the existing-record apply | Claude | design-vs-test conflict | `NursingHome_Chart.Rostering.Tests/EditDraftWave1Tests.cs:107`; `Core/EditDraftRestorer.cs` ApplyOrder; `Core/EditDraftMembers.cs` Candidates | an existing test red and existing-record order changed / certain / source / n/a | — | deviation 1: separate `NewRecordReconstructionOrder`; Codex: "no additional defect identified" |
| C-D1 | Fresh start drops a reconstruction member once it is typed and stored | Codex (diffreview) | correct (source read) | `Core/EditDraftCaptureRules.cs` (pre-fix :95-108) | recreated record gets today's 日付/login / rare (store, then expiry or 破棄, then an edit) / source / no | `D1_N07_…` | FIXED post-review |
| C-D2 | A new-record worker's gate suppresses an existing-record write coalesced after the save | Codex (Claude had documented it in a code comment as an accepted limitation) | correct (source read) | `Core/EditDraftCaptureControllerBlazor.cs` StartWrite (pre-fix) | a draft write skipped while the key is off / rare / source / no | `D2_D15_…` (RunOneWrite + counting writer) | FIXED post-review (per-ticket gate) |
| C-D3 | The claim's `ExpiresOn > now` used the "now" read at step 1 | Codex | correct (source read) | `Core/EditDraftRecreate.cs` (pre-fix) | an expiring draft claimed late / rare / source / no | `D3_E39_…` | FIXED post-review |
| C-D4 | InitializingGetters run again at bind after the replay and can overwrite a restored value | Codex | correct as a library contract; not reachable with today's CareCrew policies (all generic `InitializingGetters` empty) | `Core/EditDraftCaptureControllerBlazor.cs` BindTo (pre-fix) | a restored value lost / none today / source / no | scan in `N19_N27_T16_…` | FIXED post-review |
| C-D5 | A restore-guard violation during the recreate was reported as full success | Codex | correct (source read) | `Blazor/EditDraftRecreateHostBlazor.cs`, `Blazor/EditDraftListControllerBlazor.cs` (pre-fix) | misleading success / rare / source / no | `D5_…` (fake + scan) | FIXED post-review |
| C-D6 | A ToDo created from a ToDoItem's nested list loses its parent on recreate | Claude (as a limitation, pack §9 item 10); Codex (as a defect) | correct (conditional: the nested route reaching the admitted view is not verified) | `ToDo.ToDoItem` [Association], `ToDoItem.ToDos` | parent must be re-picked / when that route is used / source / no | `D6_N28_…` (in memory) + browser | FIXED post-review: ToDoItem seeded (and SubSection for 夜間巡回時間, same reason) |
| A1 | Post-review D2 edit broke the existing pin `{ Clock = _clock }` (`E21_D1_…`) | Claude (test run) | correct (implementation regression) | post-review first run | an existing test red / certain / run / n/a | rerun | FIXED in the implementation; final run green; test not touched |
| A2 | The [AutoIncrement] timing (D10) | Claude | assigned by an AFTER INSERT trigger (decompiled source) | §5 | a number consumed by a discarded candidate / not by the code read / decompile + reflection / n/a | browser item 12 | TenantCase opted in; Codex: could_not_determine |
| A3 | Incident: four EMPTY untracked files created in the MAIN repo `repos\CareCrew\Xaf.EditDraft.Core\` by a relative path resolved against the process directory | Claude | — | 09:49:56, 0 bytes, untracked | none (removed within a minute; `git status` of that folder clean) | — | removed; later normalisation used a script that takes absolute worktree paths and refuses tracked or outside files |
| A4 | My own new tests did not compile at first (private nested fakes in public test signatures, CS0051), plus two of my test-side mistakes found by reading (`Skip(7)`, `IndexOf` on the forwarder) | Claude | — | — | — | — | fixed before the new tests' first execution |
| CR-§9 | Codex dispositions of Claude's deviations 3, 4, 5, 6, 8, 9 | Codex | no additional defect identified; runtime unverified | out.md table | — | browser items 10, 11, 1 | — |

Found independently by both: the W38b conflict (X1) and the ToDo nested-route context loss (C-D6, Claude as a limitation, Codex as a defect). This is coverage, not confidence.

## 9. Edits made after the Codex review (NOT cross-reviewed)

Files whose bytes differ from the reviewed candidate manifest (`diffreview\a1\candidate.json`):
- `Xaf.EditDraft.Core/EditDraftCaptureControllerBlazor.cs`:
  - `DraftSnapshot.NewRecordsGate` + `Context` (`:419-430`);
  - per-ticket gate in `RunOneWrite` (`:630`); the loop-wide new-record gate removed (D2);
  - `BuildSnapshot` sets both via `with`, keeping `{ Clock = _clock }` (`:555-565`);
  - `RebuildAfterFreshStart` passes the context and re-seeds (`:466-473`), `RetiredFreshStart` passes `newest.Context` (`:504`) (D1);
  - `BindTo` takes the adoption before the getters (`:290-293`) and splits take / attach (`:166-179`) (D4).
- `Xaf.EditDraft.Core/EditDraftCaptureRules.cs` — `FreshAfterGone(..., context)` (D1).
- `Xaf.EditDraft.Core/EditDraftRecreate.cs` — the claim reads `host.Now()` (D3); `GuardViolated` on the candidate interface and the result (D5).
- `Xaf.EditDraft.Blazor/EditDraftRecreateHostBlazor.cs` — `GuardViolated` (D5).
- `Xaf.EditDraft.Blazor/EditDraftListControllerBlazor.cs` — a guard violation is never a full success (D5).
- `CareCrew.Blazor.Server/Infrastructure/EditDrafts/Policies/ToDoEditDraftPolicy.cs`, `NightRoundsTimeEditDraftPolicy.cs` — seeds ToDoItem / SubSection (D6).
- `Xaf.EditDraft.Tests/EditDraftNewRecordTests.cs`, `NursingHome_Chart.Rostering.Tests/EditDraftNewRecordCareCrewTests.cs` — my scan pins updated to the new code and five new tests (D1, D2, D3, D5, D6).

All executable checks were re-run on these bytes (§6.1 Final, T17 on final bytes, four builds). A delta review is an owner decision (D5 of the guardrails register), so none was started.

## 10. Deployment

- Build: CareCrew Blazor only (Core and Blazor are projects referenced by `CareCrew.Blazor.Server.csproj:79-80`). Not NHM WinForms, not ChartWorkflowServiceV2, no report layout, no database script. Mirror: none (no Module file changed). Schema: none.
- Switch: `EditDraftCapture:NewRecords:Enabled` is true in Development and false in base and Production. Recreate and the 新規 rows do not depend on it, just as restore and the list do not; capture of never-saved records does.
- Production prerequisite (unchanged, not part of this build): the generic `dbo.EditDraft` table, its deny rows and the purge are not in production according to the session memory and KB fix-536; this was not re-verified here.
- Timing: no pay-window or month-end dependency.

## 11. M4 — Dev2 browser checklist (owner; dev host on 5002–5004 from this worktree)

1. 残業・有給 新規 → type 残業理由 → F5.
   - On the list, the notice says 「新規の入力控が 1 件あります。上の「入力控」から開けます。」.
   - Open the header 「入力控」; the row shows 状態 「新規」. Press 開く.
   - A MODAL window shows the record with the draft's 職員/日付/時刻 and the typed reason, unsaved.
   - Toast: 「入力控（入力 …）から記録を作成しました。…元の画面がまだ開いている場合は、そちらで保存してください。」.
   - Log: `[EditDraft] recreate XXXXXXXX: Created at step 10` and `claimed draft … attached to the recreated record`.
   - 保存 → log `delete after save: rows=1` → the list has no row.
2. 新規, then close without typing → no `write create` line and no notice. Also watch for a `write create` right after opening, which would mean a DetailView controller sets values after activation.
3. First edit = 日付 only → `write create ok=True`; the draft holds 日付 typed plus 職員/開始/終了 seeded.
4. Two tabs, two new 残業・有給 → two 「新規」 rows → each 開く recreates its own values.
5. A draft typed on day D and opened on day D+1 → the recreated record keeps 日付 D and its times.
6. Another login sees no row and no notice; a GeneralUser login captures nothing (no `write create`).
7. A role without Create on 残業・有給 → 開く is refused with 「この記録を作成する権限がありません。」 and nothing is created (log `create refused …`).
8. 破棄 a 「新規」 row → it is hidden and the notice is gone; with 破棄済みも表示, 開く recreates it (the claim un-discards it).
9. 保存して新規 on a recreated record → the first row is deleted, and the next new record gets its own draft (a new editor id).
10. Window behaviour:
    - the record opens modal (D6);
    - a draft with an entry the login cannot write → the read-only display 「戻せなかった入力」 with no 破棄 button;
    - a draft with only 戻せません entries → the D9 display with 破棄.
11. Switch: set `EditDraftCapture:NewRecords:Enabled` = false → typing in a new record writes nothing (no `write create`), while existing-record capture still writes. Set it back to true → new records are captured again (the configuration is re-read per event).
12. 苦情対応 (TenantCase): 新規 → type 詳細 → F5 → 開く → 保存.
    - ケース番号 is assigned once, with no gap from discarded attempts; compare MAX before and after.
    - No `write create` appears after `delete after save` without typing (no phantom write from the trigger's reload).
13. ToDo and 夜間巡回時間: steps 1–2 once each. Also create a ToDo from a ToDoItem's nested ToDos list, type only 説明, F5, 開く → the recreated ToDo keeps its ToDoItem (post-review D6). Note the view id and whether the screen was a root view.
14. (if reproducible) Recreate and save, but make the row survive (e.g. open the list in a second tab before saving) → 開く on the old row → 「この入力控の記録はすでに保存されています。」 with 「保存済みの記録を開く」.

## 12. KB text (to log with `log_new_fix` after the browser pass)

Not logged in this run: the KB server writes into `repos\CareCrew\mcp-blazor-knowledge-base`, and this run must not touch `repos\CareCrew`.

- **Title:** Generic edit-draft (入力控) for NEW, never-saved records: TargetOid = Guid.Empty row, seeded construction context, prov Oid history, a 「新規」 list row recreates the record in a modal (残業・有給, ToDo, 夜間巡回時間, 苦情対応).
- **Category / components:** xpo-data; EditDraftCaptureControllerBlazor, EditDraftCaptureRules, EditDraftRecreate, EditDraftWriter, EditDraftListControllerBlazor, EditDraftListBadgeControllerBlazor, EditDraftTypePolicy.
- **Symptoms:**
  - Owner 2026-10-02: typing into a NEW 残業・有給 and pressing F5 lost everything.
  - Log `[EditDraft] capture not admitted … isNew=True`.
  - After F5 the person lands on the type's list.
- **Root cause:** the generic engine assumed an existing record everywhere: admission refused new objects, the offer and the list matched by TargetOid, apply re-read the record. A new object already has its final Oid (BaseObject.AfterConstruction), but after F5 no screen has that object.
- **Solution:**
  - Opt-in per policy (`AllowNewRecords`) plus a runtime key (`EditDraftCapture:NewRecords:Enabled`).
  - Rows keyed by `TargetOid = Guid.Empty`, with `EditDraftSeed.IsNew` as plain data.
  - The first GENUINE edit (compared with the post-construction baseline; a seed is never an edit) records the edit as typed and seeds `NewRecordReconstructionOrder`.
  - An optional `prov` header in the version-1 payload lists every screen object's Oid, newest first.
  - The 「入力控」 list shows 「新規」. 開く runs EditDraftRecreate: owner, "already saved?" over prov, Create permission + model AllowNew/AllowEdit, the draft's 事業所; then the candidate, ONE fenced `TryClaimNew` adding the candidate's Oid, getters + ApplyNew under the restore guard, the filled object's 事業所, and the modal; success only after the screen attaches with the claimed editor id.
  - The first save deletes the row; a notice on the type's list counts readable new-record rows.
  - No schema or Module change.
  - TenantCase [AutoIncrement] is assigned by an AFTER INSERT trigger, so a discarded candidate costs nothing.
- **Prevention rule:**
  - Copying the chart's seed condition alone turns a bare notification on an untouched seeded member into a write: decide "genuine" against the baseline, and record the triggering member before seeding.
  - Carry route-supplied parents (ToDoItem, SubSection) as seeds.
  - Keep per-ticket gates when one worker drains snapshots of a record that was saved meanwhile.
  - Read "now" again at the claim.
  - Never report success while the restore guard cancelled a save.
- **Tags:** draft-store, 入力控, generic-edit-draft, new-record, recreate, prov, seeding, TargetOid-empty, combined-analysis, codex, not-deployed.

## 13. Contribution log

### What Claude did
- **Preflight:** Phase 0 (below).
- **Reading:** read the design, the engine (Core and Blazor), the CareCrew policies and seams, the chart recreate precedent (read only), and the existing tests. Collected every source-text pin that constrains the edit:
  - `IsAdmittedView`'s text;
  - `{ Clock = _clock }`;
  - the 9-controller count of the Blazor assembly;
  - the `EditDraftListItem` member list;
  - W1's empty `ReconstructionOrder`;
  - the DraftWriteSlot byte copy;
  - the `RetiredFreshStart` signature used by reflection.
- **Baseline:** recorded the baseline test identities before any edit.
- **Implementation:** implemented M1–M3, the D10 decompile check, and the 79 new tests from Codex's E/N expectations.
- **Test integrity:** ran the T17 mutation twice and kept the red pre-existing test unrevised.
- **Review:** prepared the diffreview pack (KB records, design, Codex's expectations, full current text of the two most-changed files, build and test output, its deviations and the incident). Verified every Codex finding against source and fixed D1–D6 after the review.
- **What Claude got wrong:**
  - the main-repo incident (A3);
  - the CS0051 compile error and two test-side mistakes in its own new tests (A4);
  - a post-review regression of an existing pin (A1);
  - the D2 behaviour, which Claude had documented as acceptable;
  - the D1 and D3 to D5 defects, which Claude did not find.

### What ChatGPT (Codex) did
- **tests (requirement-only):** reused E01–E44 with per-ruling changes, marked E04/E13/E23/E30 out of scope, and added N01–N33. Notable additions:
  - per-ticket and post-save interleavings (N30);
  - "now" at the claim (E39/E40);
  - the ToDo/NightRoundsTime route-context check (N28);
  - TenantCase allocation side effects (N29);
  - T17's limits.
- **diffreview:** read the repo (16 commands, 0 file changes) and checked the two pasted file hashes and the golden hash. Found D1–D6 (all verified correct by Claude) and restated X1 as D7. It confirmed that no Module or NHM mirror is needed and gave "no additional defect identified" for deviations 1, 3, 4, 5, 6 and 8. Its could_not_determine covers production state, runtime reproduction of D1–D6, the exact DX modal behaviour, close-with-いいえ, Save-and-New, the TenantCase allocation, and the incident's cleanup.
- **Calls:** two, both accepted on the first attempt; no retry.

### Per changed file (who decided what)
- `Core/EditDraftCaptureControllerBlazor.cs`, `Core/EditDraftCaptureRules.cs`, `Core/EditDraftRecreate.cs`, `Blazor/EditDraftRecreateHostBlazor.cs`, `Blazor/EditDraftListControllerBlazor.cs`: written by Claude. Post-review changes were driven by Codex D1–D5.
- `ToDoEditDraftPolicy.cs`, `NightRoundsTimeEditDraftPolicy.cs`: Claude, with the seeds driven by C-D6 (both).
- Every other changed file: Claude; reviewed by Codex with no defect raised against it.
- Security files (§14): Claude only (single-model). Codex was told not to design them and raised no defect against them.

### Setup checks (Phase 0; outputs under `%LOCALAPPDATA%\collab\2026-10-03-edit-draft-new-records-build-f4b916\preflight\`)

| # | Item | Result |
|---|---|---|
| 1 | BASH_MAX_TIMEOUT_MS | present (2400000) |
| 2 | Read-only query connection (HARD) | not applicable: no database used (brief: no DB) |
| 3 | Repo trusted (HARD) | the hook fired in this session (item 5), so the project settings are in force |
| 4 | Manifest (HARD) | 7/7 hashes match (worktree) |
| 5 | Hook fires (HARD) | `git push --dry-run origin HEAD` blocked by collab-guard; a harmless Monitor (`Get-Date`) ran unblocked |
| 6 | collab.rules | file present; the execpolicy check was not run (its command text contains a push, which the hook blocks; rule 13) |
| 7 | prompt-input | not re-run this session (the previous run of this design, 139e0a, saved it; AGENTS.md unchanged) |
| 8 | Tool boundary (HARD) | no MCP tool of this agent writes a database, migrates, deploys, pushes or restarts; KB write tools, claude-in-chrome and Claude Docs are present but were not used |
| 9 | Tool parity (HARD) | KB limited to the 9 read tools (`enabled_tools`, `~/.codex/config.toml:130-134`), dxdocs; node_repl and cua_repl enabled (not called); code-review and codex_app disabled |
| 10 | Models (HARD) | gpt-6-astra listed with low…ultra, including xhigh |
| 11 | Run setup | run f4b916, scratch, salt (unused), codex.exe `C:\Users\owner\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe`, codex-cli 0.153.4; doctor exit 0; login ChatGPT |
| 12 | Snapshot | worktree HEAD 132782b1, branch feature/edit-draft-new-records, clean except the untracked design doc; NHM not touched |
| 13 | Policy drift | not re-checked this session (the previous run found CLAUDE.md / AGENTS.md identical to the main repo); `~/.codex/config.toml` effort medium, every call passed `-Effort xhigh` |
| 14 | Web search | off in both calls |

### Redaction
None needed. No personal data was read. Of the appsettings files, only the `EditDraftCapture` section's boolean lines were read (located by line number), and the diff hunks of those files carry only those lines.

### Inputs Codex did not have
- The decompiled AutoIncrement source files: only Claude's quoted summary reached Codex. The [AutoIncrement] conclusion is therefore not cross-checked (Codex: could_not_determine).
- Claude's session memory (CLAUDE.md auto-memory); relied on only for "production has no EditDraft table", which is also in KB fix-536.
- No dev host log or database was used by either.

### Passes used
1 cross-model review pass (diffreview) after the requirement-only `tests` call: 2 Codex calls, 2 attempts, no retries. Monitor slips: three watcher commands lost a `$` variable to bash expansion. One was discarded at once, one ended early with a false "state.json present" line (ignored), and the proper watcher and a bounded file check were used. Hook false positives: 0.

### Run ledger
See the final report; appended with `tools/collab/append-ledger.ps1`.

## 14. Security files for the owner's review (D14 — single-model, Claude only)

- `Xaf.EditDraft.Core/EditDraftAccessSeam.cs` — `IEditDraftRecordAccess.IsSubSectionVisible` (fail-closed default), the library default, and `EditDraftCreateAccess` (S1/S2: model AllowNew/AllowEdit, `DataManipulationRight.HasPermissionTo(type, Create)` under `IRequestSecurity`).
- `CareCrew.Blazor.Server/Infrastructure/EditDraftAccess.cs` — `EditDraftAccess.IsSubSectionVisible` and `CareCrewEditDraftRecordAccess.IsSubSectionVisible` (S5 i; the same `GlobalSearchAssignment.IsRecordVisible` facts as `IsRecordVisible`).
- `Xaf.EditDraft.Core/EditDraftWriter.cs` — `TryClaimNew` (owner predicate, Oid/Revision/liveness/new-record fences) and the Create guard.
- `Xaf.EditDraft.Core/EditDraftNewRecordRules.cs` — `IsWritable` (owner required; empty target only with IsNew).
- `Xaf.EditDraft.Core/EditDraftRecreate.cs` — the order of the owner, security and 事業所 checks relative to the candidate and the claim.
- `Xaf.EditDraft.Blazor/EditDraftRecreateHostBlazor.cs` — the owner-scoped read, the secured "already saved?" read, and the calls into S1/S2/S5.
- `Xaf.EditDraft.Core/EditDraftRestorer.cs` — `ApplyNew` (member write permission, 戻せません enforced in the engine).
- `Xaf.EditDraft.Core/EditDraftCaptureControllerBlazor.cs` — the `TryAttachClaimed` overload (owner re-check before attaching).

## 15. Not verified / open questions

- **W38b:** the owner's disposition (§6.3).
- **Security:** the §5 security review (D14).
- **Not executed:** all runtime behaviour in XAF Blazor.
  - modal activation and the synchronous acknowledgement under the real host;
  - close-with-いいえ (D13);
  - Save-and-New;
  - two tabs;
  - the notice's presentation;
  - the not-applied display stacked over the modal;
  - the closing of an unacknowledged view.
  - (§11; read from the DX 26.1.4 source, not executed.)
- **TenantCase:** the trigger behaviour at INSERT and the absence of a phantom write after its reload (§5, browser item 12).
- **Nested routes:** whether the ToDo nested route (and a 事業所 nested list for 夜間巡回時間) reaches the admitted root view.
- **SQL:** `TryClaimNew` executing on SQL Server (no database here).
- **Fixed limits** (design, D11, unchanged):
  - a saved record the login can no longer read reads as "not saved";
  - a duplicate when the original screen is still open and both screens save.
- **Production:** the `dbo.EditDraft` table, deny rows and purge are still not in production (memory and fix-536; not re-verified).
- **Wording:** the W1 message "existing records only, no NEW-record seeding" no longer describes 残業・有給, ToDo and 夜間巡回時間, which now seed through `NewRecordReconstructionOrder`. W1 itself still passes; its wording is the owner's to change.
- **Stale reason text:** TenantCase's `CaseNumber` decision text says "[AutoIncrement] assigns it at creation" (unchanged; §5 shows it is assigned at INSERT by a trigger).
