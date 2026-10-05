# Xaf.EditDraft client-side journal — milestone M1 build (2026-10-04)

Collaborator run `2026-10-04-edit-draft-journal-m1-96623a`. Claude: Opus 5.5. Codex: gpt-6-astra, `-Effort xhigh` passed
explicitly (codex-cli 0.153.4): `tests` a1 (requirement-only, before any code) and `diffreview` a1. Worktree
`CareCrew-journal`, branch `design/edit-draft-client-journal`, HEAD `34ae23ce` (a0354521 + the two design docs). Owner
ruling 2026-10-04 (label verbatim): "Start M1 now, §7 in parallel". No commit, no deploy, no database write.

Design: `docs/edit-draft-client-journal-design-2026-10-03.md` ("design"). M0 gate: `docs/edit-draft-client-journal-m0-2026-10-03.md`
("M0"; §5 binding changes, §9 M1 brief). Evidence: a local scratch folder outside the repository
(cited `ev/<file>`). Codex outputs (same folder): `tests` a1 (T1-T88), `diffreview` a1 (C1-C14).

## 0. Combined answer

M1 adds the browser half of the input journal to the library: `edit-draft-journal.js`, an ES module served from the
Xaf.EditDraft.Blazor RCL and imported through IJSRuntime. It journals only editors that carry a server-built
`data-editdraft` descriptor. Each change goes synchronously into that entry's own localStorage key. Composition, masked
editors, refused writes, budgets, retirement by server echo, clears and two tabs are handled under the rules of M0 §5/§9.

`EditDraftJournalAttributeControllerBlazor` sets the descriptor on admitted DetailView editors, behind a new fail-closed
switch `EditDraftCapture:Journal:Enabled` (plus the type key). Core gains the entry model, the pure rules (key, copy-only,
baseline fingerprint, eviction, format- and culture-bound conversion, a three-way reconcile stub) and the
「入力中（未確定）」 row label.

Codex's requirement-only list (88 expectations) drove the tests. Its first review found 14 defects; the ten behavioural
ones (C1-C10) were reproduced by tests Claude wrote from Codex's decisive checks, then fixed, and all 72 js tests pass.

The owner ruled on the six red tests (all now green after the merge), accepted the five decisions, and allowed one bounded
delta review (a2). a2 found ten more defects (D1-D10), which are reported UNFIXED for the owner (§5b). Claude reproduced
every behavioural one; D8 and D9 were confirmed by reading the source. Most concern the C1-C4 fixes:
- batched replies;
- a masked composition whose reply never comes;
- refused-write retries replaying an older value;
- same-millisecond and unreadable-marker clears;
- a delayed clear notice dropping a newer edit.

M1 does not recover anything yet: there is no intake, no offer row and no draft-row promotion (M2/M3). CareCrew's two
custom time components are not journaled until M2 adds a format seam. M1 closes only after the owner's M0 §7 checks and
the owner's decision on D1-D10.

## 1. Status

Implemented, uncommitted, 2026-10-04. M1 stays OPEN until M0 §7 (IME PC + masked, iPad, TimeOnlyDateEdit, 保存 re-render,
physical typing + F5 / Task Manager kill) is run.

Master: `git merge master` was blocked by the collab-guard hook (this agent never merges). The main session then merged
master into the branch: HEAD `d235bd1b` = 34ae23ce + master 53089fc2. That merge includes the F6 chart-journal fix
(fix-552), the E22b pin and the C25_C31 skip. The M1 working files were untouched.

**Owner rulings 2026-10-04** (labels verbatim, relayed by the main session):
1. Red tests: "Yes, all six as proposed". Applied as in §7.
2. Decisions 1-5 (§4): "Accept all five; revisit the switch rule at M3 when intake exists".
3. D5: "One bounded Codex delta review of the post-review changes, then commit". This is `diffreview` a2 (§5b). Its
   defects are reported UNFIXED; the owner decides.

## 2. What was built

| File | Change |
|---|---|
| `Xaf.EditDraft.Blazor/wwwroot/edit-draft-journal.js` | NEW. The module: `createJournal(env)` (window, storage, clock, timers injectable) and the entry points `start`, `list(ns)`, `value(key, seq)` (Uint8Array), `retire(keys, echo)`, `clear(ns)`, `coverage`, `report`. Storage access in `createStorage` (never throws); composition in `COMPOSITION` (replaceable after §7) |
| `Xaf.EditDraft.Blazor/EditDraftJournalAttributeControllerBlazor.cs` | NEW (replaces the M0 spike controller). `View.CustomizeViewItemControl<BlazorPropertyEditorBase>` → `ComponentModelBase.SetAttribute("data-editdraft", json)`; imports the module; logs the platform line and a coverage line per view and context |
| `Xaf.EditDraft.Core/EditDraftJournal.cs` | NEW. `EditDraftJournalKinds`, `EditDraftJournalRules` (key, limits, copy-only, 12-hour, culture/format expansion, precision, baseline hash, expiry, eviction), `EditDraftJournalDescriptor` (Build/ToJson), `EditDraftJournalEntry`, `EditDraftJournalConvert`, `EditDraftJournalReconcile` (stub) |
| `Xaf.EditDraft.Core/EditDraftJournalBoundary.cs` | NEW, SECURITY-RELEVANT (single-model, Claude only, owner review): owner token, strict descriptor/entry parsers |
| `Xaf.EditDraft.Core/EditDraftSwitch.cs` | `JournalKey`, `DecideJournal`, `IsJournalEnabled` |
| `Xaf.EditDraft.Core/EditDraftTypePolicy.cs` | `JournalTimeOfDayMembers` (opt-in for DateTime members; empty by default) |
| `Xaf.EditDraft.Core/EditDraftCaptureControllerBlazor.cs` | `TryGetBaselineRaw(path, out raw)` (read accessor of the capture's baseline snapshot) |
| `Xaf.EditDraft.Core/EditDraftTexts.cs` | `JournalRowLabel`: ja 「入力中（未確定）」, en "Being typed (not confirmed)" |
| `CareCrew.Blazor.Server/Components/TimeOnlyMaskedInput.razor`, `TimeOnlyDateEdit.razor` | U5 pass-through kept (`[Parameter(CaptureUnmatchedValues = true)]` + `@attributes` first); "SPIKE, NEVER MERGE" comments replaced with plain comments |
| `CareCrew.Blazor.Server/Startup.cs` | `EDITDRAFT_M0_DEFAULT_HUB` switch removed: identical to HEAD |
| deleted | `EditDraftJournalM0SpikeControllerBlazor.cs`, `wwwroot/edit-draft-journal-m0-spike.js` (were untracked) |
| `Xaf.EditDraft.Tests/js/` | NEW node:test + jsdom suite (`npm test`): `package.json` (same pattern as caretree-scraper-tests: jsdom `^24.0.0`, installed 24.1.3), `.gitignore`, `helpers/harness.js`, `test/capture.test.js`, `test/api-tabs.test.js`, `test/m0-scenarios.test.js`, `test/review-a1.test.js` |
| `Xaf.EditDraft.Tests/EditDraftJournalTests.cs`, `EditDraftJournalReviewTests.cs` | NEW NUnit |
| `Xaf.EditDraft.Tests/Xaf.EditDraft.Tests.csproj` | `DefaultItemExcludes` += `js/node_modules/**` (about 7,000 npm files were becoming project items) |
| `NursingHome_Chart.Rostering.Tests/NightRoundsTimeEditorModelTests.cs`, `StaffOverTimeHolidayTimeEditorModelTests.cs` | U5 tests added (U5_T81; U5_T82, U5_T82b) |

No NuGet package added. No host page change. No appsettings change (the journal key is off everywhere until set).

## 3. Requirement coverage (brief items → code → tests)

| Brief / M0 rule | Where | Tests (labels = Codex T-ids, review C-ids) |
|---|---|---|
| Only `[data-editdraft]` editors, `closest()`, defensive parse | `parseDescriptor`, `rootOf`, `fieldOf` | T1 |
| One key per (owner token, load, ctx, member, generation); descriptor fields, value, t, g, LOAD_ID in the entry | `entryKey`, `writeEntry` | T2, T3, T72 pin vs C# |
| Synchronous write per change | `record` → `writeEntry` in the event handler | T4; browser smoke |
| Entry only on user action AND value change; A→B→A recorded | `markAction`, `s.changed` | T5-T7, T43, T44, T55, T56; C10 |
| Composition: no entry while composing on any path; stale delayed reads dropped; incomplete copy cleaned | `COMPOSITION`, `record` (incomplete branch), state fencing | T9-T14, T45, T46, T50-T54, C2, C5, C9, T9b |
| Masked: MutationObserver on field-text, read in a microtask, no rAF; post-blur window keyed on the operation; unresolved reported | `onMutations`, `openOperation` (one reply answers one attempt, oldest first) | T15-T18, T47, T49, T53; C1, C3 |
| Refused writes retried, storage failures reported not thrown | `failed` map + `retryOne`; `createStorage` | T20, T20b, T21, T48, T57; C7 |
| Budgets 60 / 12,000 / 60 min by serialized size; evict oldest, key tie-break | `planEviction` after a successful write; `truncate`; `isExpired` | T22-T25, T37; C8 |
| Clears and reversals kept | `record` | T7, T44 |
| Retire only on echo (seq + value) | `retire`, `pendingFor`, `writerAlive` | T26, T27, T34; C6 |
| Multi-tab via per-key + storage events; Web Locks optional | `onStorage`, heartbeat, optional lock, durable clear marker | T28, T29, T35b, T37-T42; C4 |
| Coverage per view instance and context | `coverage()`; controller `CoverageLine` | T30; C12 |
| Intake API `list(ns)`, `retire`, `clear`; Uint8Array for streams | `list`, `value`, `retire`, `clear` | T32-T35 |
| New records: reconstruction raws with the first edit | controller `ReconstructionRaws` → descriptor `rc` → entry | T36, T67 |
| Controller: CustomizeViewItemControl + SetAttribute; DetailViews only; switch fail closed; descriptor contents; 12-hour copy-only | controller, `EditDraftSwitch`, `EditDraftJournalDescriptor.Build` | T59-T69 (pure parts + source pins); C11 |
| Core rules: descriptor build/parse, baseline hash, budget/eviction, conversion only with the effective format, reconcile stub | `EditDraftJournal.cs`, `EditDraftJournalBoundary.cs` | T70-T78; C11 |
| Texts | `EditDraftTexts` | T79, T80 |
| U5 pinned editor-model tests | the two Rostering test files | T81 (green), T82/T82b (red, faulty tests) |

## 4. Decisions taken in this build

Owner 2026-10-04: "Accept all five; revisit the switch rule at M3 when intake exists". That covers the five decisions in
the run report: switch rule (1); no attribute on custom editors (2); serialized ceiling 1,048,576 (4); S13 without the
chart journal (11); Development key not set. The rest below are implementation rules Codex reviewed in a1/a2.

1. **Switch rule.** The attribute needs `EditDraftCapture:Journal:Enabled` AND the type key, and for a never-saved record
   the new-record key too. The global `EditDraftCapture:Enabled` is NOT read. This follows the brief ("…reads true … and
   the policy's type is on") and its browser check with server capture off. The design's Q1 wording also listed the
   global capture switch. Codex: consistent with the later brief.
2. **Members and editors.**
   - Journaled: string and TimeSpan policy members, and DateTime members only when listed in the new
     `JournalTimeOfDayMembers` (no CareCrew policy lists one).
   - Editors: XAF's DxTextBox, DxMemo, string DxComboBox, DxMaskedInput and DxTimeEdit models.
   - Custom component models get no attribute and are named in the coverage line as `unsupported=[member(type)]`. This
     includes CareCrew's `TimeSpanMaskedModel` and `StringToDateTimeMaskedModel`, which render the U5 components.
   - So in M1 the U5 pass-through is in place but no CareCrew custom editor carries the attribute yet (M2 seam, §9).
3. **Copy-only.** Masked kinds are always copy-only (conversion not proven). Time kinds are copy-only when the format is
   unknown, or 12-hour without a designator after expanding a standard format with the circuit culture.
4. **Budgets.**
   - Count: 60 journal keys origin-wide (all namespaces, incomplete copies included).
   - Value: 12,000 UTF-16 units, never splitting a surrogate pair.
   - **Serialized total: 1,048,576 characters (key + value). This number is Claude's choice, not the owner's.**
   - Retention: 60 minutes, expired at ≥ 60 min.
   - Eviction runs only after a successful write, oldest first, ties by key.
5. **Retire.**
   - `retire(keys, echo)` with `echo[i] = {seq, val}` (or a map keyed by entry key).
   - An entry is removed only when its stored seq and value match the echo.
   - It is kept while this page load has pending work on it (refused write, incomplete copy, open composition,
     unanswered attempt).
   - It is also kept while its writer (another page load) has a heartbeat younger than 2 minutes.
6. **Freeze.** `value(key, seq)` returns bytes only for the listed seq and only within retention.
7. **Server values are never edits.**
   - Text editors are recorded from input events.
   - A field-text change with no attempt of that field waiting sets `serverSet`; focusout, page hide and retry then
     skip that field until the next user input.
8. **Record change.** The controller removes the attributes synchronously, in the same render as the new values, then
   posts the new descriptors. After a save it posts a generation bump.
9. **Operations.** One keystroke (keydown + its beforeinput) is one attempt, and one field-text reply answers one attempt,
   oldest first. A button inside an editor (pointerdown off the field, for example a clear button) also opens an attempt.
10. **Clear.** `clear(ns)` leaves a durable marker `XafEditDraft.clr1|<ns>` (expires after 60 min). A write produced by a
    user action before the latest clear never happens, in this tab or another. A new edit after the clear is journaled.
11. **S13 (T58).** The start report lists survivors value-free (member, composing flag, age, length, tail class). The
    host's chart journal (`CareCrew_InputJournal`) is NOT read by the library; that part of the M0 spike's report is gone.

## 5. Codex diffreview a1 — findings and how each was settled

Rank rule: nothing here is proven in production (M1 is not deployed). A defect reproduced by an executed test ranks above
a static one.

| ID | Finding (Codex) | Claude's check | Outcome |
|---|---|---|---|
| C1 | One masked reply marked every pending attempt answered; the second reply was lost | `review-a1` C1 FAILED on the reviewed bytes (`ev/review-a1-fail-before.tap`) | Fixed: one reply answers the oldest attempt (decision 9). C1 passes |
| C2 | Masked compositionend: blur/page hide before the mask reply promoted the raw composed text | C2 FAILED before | Fixed: `awaitMask` keeps every path on the incomplete copy until the reply. Passes |
| C3 | An earlier action authorized later server values (blur after a server value; masked update while focused) | C3 FAILED before | Fixed: `serverSet`; masked reads only answer attempts (decision 7). Passes |
| C4 | A retry ran before the other tab's clear notification and rewrote cleared text | C4 FAILED before | Fixed: durable clear marker checked before every write (decision 10). Passes |
| C5 | A refused committed write deleted the only incomplete copy | C5 FAILED before | Fixed: the copy is removed only after the entry is written. Passes |
| C6 | Retire ignored pending masked attempts; cross-tab read/write/remove interleaving | C6 FAILED before (pending case) | Fixed for pending attempts and live writers (decision 5). The interleaving with a writer whose heartbeat is older than 2 minutes remains a residual (not reproduced in a browser) |
| C7 | Partial storage failures reported as success (list with unreadable keys; clear whose removals failed) | C7 FAILED before | Fixed: `ok:false` (+ `partial`/`failed`). Passes |
| C8 | `value()` returned expired text | C8 FAILED before | Fixed. Passes |
| C9 | Delayed reads not fenced to their descriptor | C9 FAILED before | Fixed: callbacks compare the field's state object. Passes |
| C10 | A first input with no prior state became its own baseline | C10 FAILED before | Fixed: baseline = the root's `field-text` (else any value counts as changed). Passes |
| C11 | Conversion ignores culture; Format chosen before Mask; standard formats; hidden fractions; display format | static (no .NET run by Codex) | Fixed: descriptor `cu` (circuit culture), Mask first, standard formats expanded with the culture, fractions counted (`EditDraftJournalReviewTests`). OPEN: a separate display format after blur is not represented (M2) |
| C12 | Coverage dropped unsupported editors; ran once | static | Fixed: `unsupported=[...]` in the line; coverage again after each record change or save |
| C13 | Red .NET gates; three malformed new assertions | agrees with Claude's classification (§7) | Escalated, not changed |
| C14 | Labels claim more coverage than the assertions (T19 plain field, T24 quota pressure, T26 hand-over report, T28-T29 interleavings/intervals, T30/T61-T69 executed controller lifecycle, T33-T34 real streams, T42 older-release fixture, T73-T75, T80 in the E22 file, T81-T82 DOM) | accepted as stated | Ten schedules added (`review-a1`, T9b). The rest are listed in §11 as not verified |

Decisions per Codex: 5, 7 and 9 failed their guarantees on the reviewed bytes, and are fixed as above. Decision 2 failed
the reporting requirement, and is fixed (C12). Decision 10 departs from S13/T58 and is for the owner. Decision 1 follows
the later brief. Decisions 3, 4 and 6 are not contradicted, except that the serialized ceiling is Claude's number.
Decision 8's same-render guarantee is not proven by source pins: a browser check is needed (§7 item 6, 保存 re-render).

**Everything changed after the review is NOT cross-reviewed** (two passes used: tests a1, diffreview a1; a delta review is
the owner's call, D5). Reviewed candidate: the frozen manifest `candidate-a1.json` (local scratch; diff SHA-256 369E92D8…). Changed after it:

| File | Reviewed SHA-256 → final |
|---|---|
| `wwwroot/edit-draft-journal.js` | 97AA0593… → 6B7AD01F… (journal body rewritten for C1-C10) |
| `EditDraftJournalAttributeControllerBlazor.cs` | → 6264FB2A… (Mask first, culture, unsupported, coverage rerun, lifetime guard) |
| `EditDraftJournal.cs` | → 1550446F… (culture, precision, `CultureOf` predefined cultures only) |
| `EditDraftJournalBoundary.cs` | → B0A97595… (reads `cu`; single-model file, not reviewed by rule) |
| NEW `js/test/review-a1.test.js` | 8BF661FC… |
| NEW `EditDraftJournalReviewTests.cs` | E7BA7AD8… |

## 5b. Pass 2 — the owner's bounded delta review (`diffreview` a2), defects reported UNFIXED

Owner D5 ruling 2026-10-04: "One bounded Codex delta review of the post-review changes, then commit".

Scope:
- the files changed after a1;
- the six red-test resolutions;
- the post-merge state (HEAD d235bd1b).

Inputs:
- frozen candidate manifest `candidate-a2.json` (local scratch; diff SHA-256 in that file);
- parity pack v2 (`parity-pack-diffreview-v2.md`, local scratch): the rulings, the C1-C14 settlement, fail-before / green-after,
  the mutation result and the final suite counts;
- `EditDraftJournalBoundary.cs` was excluded.

Codex ran the js suite (72/72) and in-memory probes against the final module. It also ran both real journal scripts (the
library module and the host's chart journal) against a shared-capacity model. Claude then reproduced every behavioural
finding against the final module (`ev/a2-repro.js`, `ev/a2-repro.txt`, `ev/a2-repro-d10.js`). Nothing was changed
afterwards: two passes have run again (a1, a2), so the owner decides.

| ID | Severity (Codex) | Finding | Claude's check | State |
|---|---|---|---|---|
| D1 | High | One-reply-per-attempt accounting still misattributes: (a) two replies in ONE observer batch answer one attempt, so a later server-only update is journaled; (b) masked composition with Process keydown + beforeinput + compositionend and one reply: the processed value stays only in the incomplete copy, and the composition attempt later reports unresolved | reproduced: (a) stored `server-only`; (b) main absent, incomplete copy `12:30` | OPEN |
| D2 | High | `awaitMask` cleared when its attempt times out: unprocessed composed text becomes the entry on blur | reproduced: main `1230` after 60 s + blur | OPEN |
| D3 | High | Retries replay the refused snapshot: (a) A → refused B → back to A, the retry stores B; (b) an old retry deletes a newer composition copy | reproduced: (a) stored `AB` while `A` shows; (b) copy removed by the retry | OPEN |
| D4 | High | The durable clear check does not order a clear and pending work: (a) clear in the same millisecond as the refused edit; (b) a later rejected key moves `actedAt` past the clear; (c) cross-tab interleaving read marker → clear → write | reproduced (a), (b); (c) not re-run | OPEN |
| D5 | High | An unreadable clear marker lets a cleared value be written again; with the shared quota, the marker cannot be written and pending retries recreate entries (the clear itself reports `ok:false`) | reproduced (marker read failure); the shared-quota part not re-run | OPEN |
| D6 | Medium | A delayed clear notification bumps the epoch and drops a post-clear edit that was refused and waiting for retry | reproduced: post-clear entry absent | OPEN |
| D7 | Medium | `pointerdown` on any non-field element inside the editor (decoration, wrapper) opens an attempt, so the next server value is journaled | reproduced: stored `server-only` | OPEN |
| D8 | Medium | Unknown baseline: seconds-or-finer formats convert although finer hidden precision cannot be excluded | confirmed by source (`EditDraftJournal.cs:386`) | OPEN |
| D9 | Medium | An unsupported-only view never starts the module, so `unsupported=[...]` is never logged; a control created later is not reported until a save or record change | confirmed by source (controller lines 152, 175, 182) | OPEN |
| D10 | Medium | The surviving de-duplication mutant is NOT equivalent: refuse the first write, then the same value arrives before the retry timer; final module stores it at once, the mutant does not (pending until the timer) | reproduced: final `stored=new pending=0`, mutant `stored=undefined pending=1`. Claude's equivalence claim (§7) was wrong | OPEN (test gap) |

C1-C12 disposition per a2:
- C8, C9 and C10 are closed for their schedules.
- C1, C2 and C3 pass their original schedules but are incomplete (D1, D2, D7).
- C4 is incomplete (D4-D6); C5 is incomplete (D3).
- C6: the old-heartbeat interleaving is still unsettled.
- C7: the marker-failure path is D5.
- C11: D8, and the display format is deferred.
- C12: D9.

The six red-test resolutions follow the rulings (E4, E22b, O3s, T72, U5_T82, U5_T82b), as source assertions only. Merge
interaction: independent keys; neither journal's clear removes the other's entries; no chart rewrite from a library
marker event. The shared quota is the one coupling (D5). NHM: no counterpart for any of the 22 non-excluded paths.

## 6. Deployment

- Build: CareCrew.Blazor.Server (project references to both libraries); published by
  the host's publish script. The module ships as an RCL static asset
  (`_content/Xaf.EditDraft.Blazor/edit-draft-journal.js`). Open pages need a reload.
- Inert until `EditDraftCapture:Journal:Enabled` = true (and the type key) is set. It is not set anywhere, Development
  included. Owner decision 14 says "Development on", but appsettings was not edited in this run.
- Mirror: none. No NHM counterpart exists for any changed file (Claude: file search in the four NursingHomeManagement
  checkouts; Codex: NHM inventory at `0e4f5ac5`). No schema change, no ChartWorkflowServiceV2 or report-layout consumer.
- Prerequisite for recovery (M3): the generic engine's EditDraft table in production (not checked).
- Sample consumer: builds, 42/42 tests; no new obligation (switch off by default).

## 7. Verification

| Check | Command (artifacts path `artifacts/claude-test/m1-96623a`) | Result |
|---|---|---|
| Build, whole solution | `dotnet build CareCrew.sln -c Debug --artifacts-path …` | first: 0 errors, 2,209 warnings (`ev/build-sln.log`); after the fixes: 0 errors (`ev/build-sln-2.log`); no warning in a journal file |
| js suite | `npm test` in `Xaf.EditDraft.Tests/js` | **72 / 72 pass** on the final module (`ev/js-after-fix.tap`) |
| Sensitivity, review schedules | `review-a1.test.js` on the reviewed bytes, before any fix | 1 pass, 10 fail: C1-C10 (`ev/review-a1-fail-before.tap`); full suite on the pre-splice copy (reviewed + two behaviour-neutral additions): 62 pass, the same 10 fail (`ev/js-on-reviewed-candidate.tap`) |
| Mutation check | `ev/mutants2.js`: 26 mutants, one rule each | 25 killed; 1 survivor ("de-duplication advances before the write succeeded"). Claude called it equivalent; a2 D10 showed it is NOT (a same-value input before the retry timer tells them apart), so this is a test gap left open (`ev/mutants2-run1.txt`, `ev/a2-repro-d10.js`). Before the review: 15/15 killed after adding T6b (`ev/mutants-run2.txt`) |
| Xaf.EditDraft.Tests | `dotnet test … --no-build` | **253 total, 250 pass, 3 fail** (`ev/test-xaf-editdraft-4.log`); journal tests alone 67 total, 66 pass, 1 fail |
| Sample consumer | `dotnet test samples/…/Xaf.EditDraft.Sample.Tests` | **42 / 42** |
| Rostering: application-side EditDraft + the two pinned classes | filter `EditDraft|ChartDraft|TimeEditor|JapaneseDateColumn|StaffOverTimeHoliday` | **300 total, 297 pass, 3 fail** (`ev/test-rostering-3.log`) |
| Browser smoke 1 (reviewed bytes) | Chrome 154, :5003, capture OFF (`capture ready … enabled=False`), journal ON | attribute on 説明 root `dxbl-memo-editor` (closest from the textarea); extension typing X, Y, Z → one key, seq 1→2→3; F5 with focus in the field → entry survived (seq 3), field showed the saved text; `retire` with seq−1 kept "sequence", with the listed seq retired; 0 keys left |
| Browser smoke 2 (final bytes) | same host, rebuilt | descriptor now carries `cu: "ja-JP"`; window hidden, so dispatched events (keydown, beforeinput, value, input) instead of extension typing: seq 1→2→3; reload → survived; wrong echo kept "value", right echo retired; coverage line `admitted=1 found=1 … unsupported=[]`. No draft write in the log during either smoke. Hosts stopped (54368/12556, 51784/60960) |

**After the owner's rulings and the merge (HEAD d235bd1b)**:

| Check | Result |
|---|---|
| Build | `dotnet build CareCrew.sln` into `artifacts/claude-test/m1-96623a`: 0 errors, 2,209 warnings, none in a changed file (`ev/build-sln-3.log`) |
| Xaf.EditDraft.Tests | **253 / 253** (`ev/test-xaf-editdraft-5.log`); journal tests 67 / 67; E4, E22b, T72 and master's C25_C31 pass |
| Sample consumer | **42 / 42** |
| Rostering (EditDraft, ChartDraft, TimeEditor, JapaneseDateColumn, StaffOverTimeHoliday) | **300 / 300** (`ev/test-rostering-4.log`). The two pinned editor-model classes plus the push-on-input class: 49 / 49 (O3s_T3_T21, U5_T81, U5_T82, U5_T82b pass) |
| js (`npm test`) | **72 / 72** (`ev/js-final.tap`) |

The artifacts folder is deleted after these runs.

How the six rulings were applied:
- **E4:** `EditDraftJournalAttributeControllerBlazor` added to the inventory pin (10).
- **E22b:** green from the merge, no edit.
- **O3s_T3_T21:** `@attributes` moved right after `Time="@Value"` in `TimeOnlyDateEdit.razor`; the test is unchanged.
- **T72:** both misused calls (lines 318 and 319) now use `Equal(new[] {...}, reason)`; T72b is kept.
- **U5_T82 / U5_T82b:** the start tag is cut at its real end / after `">`. As a consequence of the O3s ruling, their
  order assertion now expects `Time="@Value"` < `@attributes` < `TimeChanged`. The ruling named only the cut; this change
  follows from the razor move. The method name of U5_T82b still says "attributes_first" (not renamed).
- **TimeOnlyMaskedInput** keeps `@attributes` first (U5_T81 unchanged).

**Red tests before the rulings — escalated to the owner, not changed at the time** (each rerun once on its own: still red,
`ev/test-xaf-editdraft-rerun-reds.log`):

| Test | Classification | Proposed change (owner decides) |
|---|---|---|
| `EditDraftLibraryBlazorTests.E4_…collects_each_controller_once` (existing) | requirement adds a controller (brief item 2); also red on the M0 tree with the spike controller | add `nameof(EditDraftJournalAttributeControllerBlazor)` to `Controllers` |
| `EditDraftLibraryBlazorTests.E22b_…` (existing) | pre-existing on this base; master d44f9a07 changed the pin 1→2 | merge master |
| `StaffOverTimeHolidayPushOnInputEditorTests.O3s_T3_T21_…` (existing) | pin `<DxTimeEdit Time="@Value"` predates U5; the pass-through puts `@attributes` first (its first failure, a comment naming TimeOnlyMaskedInput in TimeOnlyDateEdit.razor, was fixed in the comment) | expect `<DxTimeEdit @attributes="AdditionalAttributes"` and `Time="@Value"`; or move `@attributes` after `Time` (then a splatted "Time" would override the parameter) |
| `EditDraftJournalRuleTests.T72_…` (new, Claude) | faulty assertion: `Equal("a", "reason")` takes the reason as an element; the code returned `["a"]` | `Should().Equal(new[] { "a" }, "input order does not matter")` (twice); its other assertions run in T72b (green) |
| `U5_T82_…` (new, Claude) | faulty: start tag cut at the `>` of `=>` | cut at the end of the start tag |
| `U5_T82b_…` (new, Claude) | faulty: cut at `">` drops CssClass's closing quote | cut after `">`. The intended assertions hold on the component text (`ev/u5-t82-scratch-check.txt`) |

Two of Claude's new tests failed on their first run and were green after IMPLEMENTATION fixes, the tests unchanged: T18
(one keystroke counted as two attempts) and `C11_the_descriptor_carries_the_culture…` (`CultureOf` accepted unknown
names under ICU). js T39 turned red after the C4 fix (same-millisecond clear vs new action) and was fixed in the
implementation.

## 8. What §7 can still change (kept local and small)

- **IME, PC and iPad** (§7 items 1-3): event order, a `compositionend` without composing input, iPadOS `input` with
  `isComposing`. All of it is in `COMPOSITION` and the incomplete branch of `record`. Masked IME depends on `awaitMask`
  and the field-text reply.
- **iPad storage, Web Locks, secure context**: `createStorage`, heartbeat and the optional lock. Production's
  secure-context status is unknown.
- **TimeOnlyDateEdit forwarding** (item 4): only the component pin changes, because M1 attributes no custom model.
- **保存 re-render** (item 6): decision 8 (same-render removal on record change, generation bump after save) is
  unverified in a browser.
- **Physical typing + F5** was exercised in smoke 1 with the extension's typing (not a human, not a Task Manager kill).
  Item 5 stays required.

## 9. M2 brief

1. **Custom component seam** (owner decision): a way for a host component model to declare kind and format, for
   example an interface on CareCrew's `TimeSpanMaskedModel` / `StringToDateTimeMaskedModel` or a resolver in DI. Then the
   U5 components are journaled. The pinned editor sources (`NightRoundsTimeEditorModelTests` O1_T3) would change.
2. **DateTime opt-in** for 残業・有給 開始/終了 via `JournalTimeOfDayMembers`, and the date rule per member (re-dating
   setter): a `TimeOfDay` outcome is already returned for DateTime members.
3. **Reconcile** (design S6): same-context server draft merged before `EditDraftJournalReconcile.Classify`; clear =
   null vs ""; display format versus edit format after blur (C11 open).
4. **Intake transport** (M3 with U1-A): `list` metadata, `value` → `IJSStreamReference` with an explicit
   `maxAllowedSize`, `retire` after the durable promotion write; the focusout hand-over report of design rule 7 (T26,
   not built); a writer-alive rule with Web Locks where available (C6 residual).
5. **Logoff clear** (decision 12): `clear(ns)` exists; wiring it to the host's logoff is not built.
6. **Development key on** (decision 14) once the owner wants the journal running on dev hosts.
7. Coverage: count laid-out items only (M0 §5.5a). Today it compares admitted members with attributed roots in the DOM.

## 10. Contribution log

### What Claude did

- Phase 0 preflight (below).
- Wrote the M1 brief extract and `REQUIREMENT.md` (verbatim extracts of the brief, design Q1/Q2/Q4/§6, M0 §3/§5/§9 and the
  F6 D1-D6 table; design Q5 excluded as single-model) and launched the requirement-only `tests` call before any code.
- Wrote all code and all tests:
  - the js tests from Codex's T1-T58;
  - the NUnit tests from T59-T82;
  - the review tests from Codex's C1-C12 decisive checks.
- Ran the first mutation check, found the T6b gap and added T6b. Built the solution and ran every suite.
- Ran two browser smokes on :5003 with capture off and stopped both hosts.
- Wrote the diffreview pack and the candidate manifest.
- After the review:
  - reproduced C1-C10 with executed tests (fail-before on the reviewed bytes);
  - fixed them, plus C11 and C12;
  - re-ran everything and a 26-mutant check.
- Wrote this document.

Got wrong:
- Three new tests are faulty (T72, U5_T82, U5_T82b).
- One keystroke opened two attempts (found by T18, fixed before the review).
- The U5 comment named TimeOnlyMaskedInput inside TimeOnlyDateEdit.razor.
- The C4 fix first dropped same-millisecond new edits (T39).
- `CultureOf` accepted any name under ICU.
- The ten review defects C1-C10 were in Claude's code.
- After the owner's rulings, Claude:
  - applied the six red-test resolutions;
  - moved `@attributes` in TimeOnlyDateEdit;
  - rebuilt and re-ran all suites on the merged HEAD (all green);
  - built the frozen a2 candidate and parity pack v2;
  - reproduced every behavioural a2 finding.
- The a2 defects D1-D7 and D10 are in Claude's post-review fixes and test claims. The mutant "equivalence" claim (D10) was
  wrong.

### What ChatGPT (Codex) did

`tests` a1 (requirement-only directory, nothing but `REQUIREMENT.md`): 88 expectations T1-T88 in eight groups, with
boundary and failure cases, plus a `could_not_determine` list. It flagged the "capture OFF" ambiguity of the smoke (T87);
the brief's wording settles it.

`diffreview` a1:
- ran the js suite (61/61) and in-memory counterexample scripts against the real module through the harness;
- read DevExpress 26.1 sources (`masked-input.ts`, `MaskedInputModelBase.cs`, XAF `ViewExtensions.cs`) and dxdocs (DxTimeEdit 26.1.4);
- inventoried NHM;
- returned C1-C14 and a decision-by-decision verdict.

Every behavioural claim C1-C10 was confirmed by Claude's executed tests; no Codex claim was rejected. Not established by
Codex: the controller lifecycle and the .NET conversion results (it ran no .NET; a PowerShell probe was blocked by
constrained language mode). No file changes (0 `file_change` in both streams); no `node_repl` / `cua_repl` calls; no web
search.

`diffreview` a2 (owner's bounded delta review):
- ran the js suite (72/72) and in-memory probes against the final module;
- ran both real journal scripts against a shared-capacity model;
- checked the six red-test resolutions and NHM;
- returned D1-D10 and a C1-C12 disposition.

Every behavioural claim was reproduced by Claude (`ev/a2-repro.txt`) except D4(c) and D5's shared-quota part, which were
not re-run. D8 and D9 were confirmed by source. No Codex claim was rejected. 0 `file_change`; no `node_repl` / `cua_repl`;
no web search.

### Found issues, by tool

"Found by" = who raised it first. Nothing is observed in production (not deployed).

| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| L1 | Mutation "field-text read on text editors" survived | Claude | confirmed | `ev/mutants-run1.txt` | test gap / — / high / n.a. | mutant killed by T6b | T6b added |
| L2 | One keystroke = two pending attempts (double unresolved) | Claude | confirmed | T18 first run | wrong report / every masked key / high / n.a. | T18 | fixed (pre-review) |
| L3 | U5 comment names TimeOnlyMaskedInput in TimeOnlyDateEdit.razor | Claude | confirmed | O3s_T3_T21 first failure | pin red / always / high / n.a. | rerun | comment fixed |
| L4 | C4 fix dropped same-ms new edit | Claude | confirmed | T39 | new edit lost / same-ms / high / n.a. | T39 | fixed (`>`) |
| L5 | `CultureOf` accepts any name (ICU) | Claude | confirmed | C11 test first run | wrong culture / malformed `cu` / high / n.a. | C11 test | fixed (predefinedOnly) |
| L6 | E4 controller inventory vs new controller | Claude | confirmed | log | red gate / always / high / n.a. | — | escalated |
| L7 | O3s_T3_T21 `<DxTimeEdit Time=` pin vs U5 | Claude | confirmed | log | red gate / always / high / n.a. | — | escalated |
| L8 | E22b pre-existing on base | Claude | confirmed | master d44f9a07 | red gate / base only / high / n.a. | merge master | escalated |
| L9 | T72, U5_T82, U5_T82b faulty assertions | Claude | confirmed (Codex C13 agrees) | logs | red gates / always / high / n.a. | — | escalated, not changed |
| C1 | One reply answers all attempts | Codex | confirmed (executed) | review-a1 C1 | last masked value lost / fast typing + blur / high / n.a. | C1 | fixed, not cross-reviewed |
| C2 | Masked composition promoted before the mask reply | Codex | confirmed (executed) | C2 | raw text as entry / IME on masked / high / n.a. | C2 | fixed |
| C3 | Server values journaled after an earlier action | Codex | confirmed (executed) | C3 | typed text overwritten / server refresh while editing / high / n.a. | C3 | fixed |
| C4 | Cross-tab clear lost to an early retry | Codex | confirmed (executed) | C4 | cleared text returns / refused write + clear elsewhere / high / n.a. | C4 | fixed |
| C5 | Refused committed write deleted the incomplete copy | Codex | confirmed (executed) | C5 | only copy lost / quota + IME / high / n.a. | C5 | fixed |
| C6 | Retire ignores pending attempts; cross-tab interleaving | Codex | confirmed (executed, pending case) | C6 | newer text retired / intake during typing / high / n.a. | C6 | fixed; interleaving with stale-heartbeat writer residual |
| C7 | Partial storage failures reported as success | Codex | confirmed (executed) | C7 | silent failure / blocked storage / high / n.a. | C7 | fixed |
| C8 | `value()` bypasses retention | Codex | confirmed (executed) | C8 | expired text transferred / slow intake / high / n.a. | C8 | fixed |
| C9 | Delayed reads not fenced to descriptor | Codex | confirmed (executed) | C9 | wrong entry / save or record change mid-composition / high / n.a. | C9 | fixed |
| C10 | First input without state lost | Codex | confirmed (executed) | C10 | autofill lost / input-only paths / high / n.a. | C10 | fixed |
| C11 | Culture/format/precision in conversion | Codex | confirmed (static + new tests) | `EditDraftJournalReviewTests` | wrong or rejected time / localized formats / medium / n.a. | review tests | fixed in part; display format open |
| C12 | Coverage hides unsupported editors; runs once | Codex | confirmed (static) | source | silent gap / custom editors / high / n.a. | C12 test | fixed |
| C13 | Red gates and faulty new assertions | Codex | agrees with L6-L9 | logs | — | — | escalated |
| C14 | Labels broader than assertions | Codex | confirmed | audit | over-claimed coverage / — / high / n.a. | listed checks | 10 schedules added; rest in §11 |
| L6-L9 after rulings | E4, E22b, O3s_T3_T21, T72, U5_T82, U5_T82b | Claude | owner ruling "Yes, all six as proposed" | logs | — | rerun | resolved; all green after the merge |
| D1 | Batched replies / composition attempts misattributed | Codex (a2) | confirmed (executed) | `ev/a2-repro.txt` D1a, D1b | server value journaled; processed composition left incomplete / batched replies, IME on masked / high / n.a. | D1 schedules | OPEN, unfixed (owner) |
| D2 | `awaitMask` cleared at timeout promotes raw composed text | Codex (a2) | confirmed (executed) | D2 | raw text as entry / mask reply lost / high / n.a. | C2 past 60 s | OPEN |
| D3 | Retry replays the refused snapshot; deletes a newer composition copy | Codex (a2) | confirmed (executed) | D3a, D3b | wrong value; newest copy lost / refused write + more typing / high / n.a. | D3 schedules | OPEN |
| D4 | Clear ordering: same ms; later rejected key; cross-tab interleaving | Codex (a2) | confirmed (executed a, b); c not re-run | D4a, D4b | cleared text returns / refused write near a clear / high / n.a. | D4 schedules | OPEN |
| D5 | Unreadable marker authorizes stale write; shared-quota marker failure | Codex (a2) | confirmed (executed, read failure); quota part not re-run | D5 | cleared text returns / blocked storage, full quota / high / n.a. | D5 | OPEN |
| D6 | Delayed clear notice drops a post-clear refused edit | Codex (a2) | confirmed (executed) | D6 | new text lost / refused write + late notice / medium / n.a. | D6 | OPEN |
| D7 | Decorative pointerdown authorizes the next server value | Codex (a2) | confirmed (executed) | D7 | server value journaled / click on editor chrome / medium / n.a. | D7 | OPEN |
| D8 | Unknown baseline converts with seconds-or-finer formats | Codex (a2) | confirmed (source) | `EditDraftJournal.cs:386` | hidden fractions dropped at intake (M3) / — / medium / n.a. | baselineKnown:false tests | OPEN |
| D9 | Unsupported-only view / later control not reported | Codex (a2) | confirmed (source) | controller :152, :175, :182 | silent coverage gap / custom-only views / medium / n.a. | controller lifecycle test | OPEN |
| D10 | Surviving mutant is not equivalent | Codex (a2) | confirmed (executed) | `ev/a2-repro-d10.js` | test gap; Claude's claim wrong / — / high / n.a. | same-value input before retry | OPEN |

Found independently by both: none (Codex reviewed Claude's code; the requirement-only list was independent of the code
by isolation-by-convention).

### Codex calls

| Run / call / attempt | Started | Duration | state | validation | exit | Model / effort req. | Effective effort | Reasoning tokens | Search | MCP tools | activity (commands / non-zero / file_change / outside-repo) | Pack | CLI |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 96623a / tests / a1 | 07:53:55 | 9.0 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 3,956 | off | none | 5 / 1 (its own python call) / 0 / powershell.exe path only | REQUIREMENT.md | 0.153.4 |
| 96623a / diffreview / a1 | 08:36:13 | 7.5 min | success | ok (candidate unchanged) | 0 | gpt-6-astra / xhigh | not observable | 7,647 | off | KB lookup_known_fix ×1; dxdocs search ×3, get_content ×4 | 28 / 2 / 0 / DX sources, NHM checkout (read), powershell.exe | parity-pack-diffreview-v1 | 0.153.4 |
| 96623a / diffreview / a2 (owner's bounded delta review, D5; not a transport retry) | 09:26:31 | 7.2 min | success | ok (candidate unchanged) | 0 | gpt-6-astra / xhigh | not observable | 6,003 | off | KB lookup_known_fix ×2, get_fix ×1; dxdocs search ×1, get_content ×2 | 24 / 0 / 0 / NHM checkout (read), DX docs | parity-pack-diffreview-v2 | 0.153.4 |

Requirement-only isolation was by convention: `out.md` cites only
`REQUIREMENT.md`, and the only outside path is the PowerShell executable. No retries.

### Setup checks (Phase 0; outputs in the local scratch folder)

| # | Item | Result |
|---|---|---|
| 1 | `BASH_MAX_TIMEOUT_MS` | present (2400000) |
| 2 | Read-only query connection (HARD) | not applicable: no database query was run. The dev host used its own connection; nothing was saved |
| 3 | Repo trusted (HARD) | present (the hook fired) |
| 4 | Manifest (HARD) | all 7 hashes match in the worktree (`manifest-check.txt`). The live hook in repos\CareCrew was NOT hashed: that command was denied by the auto-mode classifier, not retried |
| 5 | Hook fires (HARD) | the hook blocked `git merge master` with its block message (the formal `git push --dry-run` probe was not issued); a Monitor running `Get-Date` was not blocked |
| 6 | collab.rules | present; `git push origin main` → `forbidden`. The wrapped-shape result was not read: denied by the auto-mode classifier |
| 7 | `codex debug prompt-input` | AGENTS.md "Working with Claude (Codex)" present; CLAUDE.md not (pasted in the diffreview pack) |
| 8 | Tool boundary (HARD) | no Claude MCP tool writes a database, migrates, deploys, pushes or restarts a service; KB write tools unused |
| 9 | Tool parity (HARD) | KB 9 read tools (`enabled_tools` in Codex's MCP configuration) and dxdocs. DEVIATION as in earlier runs: `node_repl` and `cua_repl` enabled for Codex; forbidden in both prompts, 0 calls. claude-in-chrome, Claude Docs, Gmail, Calendar and Drive are not registered for Codex |
| 10 | Models (HARD) | gpt-6-astra listed (`models.txt`). Its effort list was not read: denied together with item 6 |
| 11 | Run id / scratch / salt / binary | 96623a; salt (unused); codex-cli 0.153.4; login ChatGPT |
| 12 | Snapshot | HEAD 34ae23ce; status at start: the U5 edits, the Startup spike switch, two spike files |
| 13 | Policy drift | agent file Phase 0 item 10 still says "supports `medium`" while ground rule 11 and the launcher say `xhigh` (reported in earlier runs too) |
| 14 | Web search | off |

### Redaction

No database rows and no personal data entered either model. The smoke used the ToDo test record; DOM reads returned
lengths and the last three characters of test text only. The owner token in keys is a hash.

### Inputs Codex did not have

- `tests` a1: everything except `REQUIREMENT.md`, by design. Missing from it: design Q5 (security, single-model), CLAUDE.md, the code.
- `diffreview` a1: `EditDraftJournalBoundary.cs` (single-model by rule; its shape was described); MEMORY.md (named, not pasted).
- Never: everything after the review (§5 table). Those conclusions are Claude's alone.

### Passes used

Before the owner's D5 ruling: two cross-model passes (requirement-only `tests` a1; `diffreview` a1). After it: one bounded
delta review (`diffreview` a2), whose defects are left unfixed. Total Codex calls: 3; attempts: 3, all `success` / `ok`.
The earlier line read: Total Codex calls: 2; attempts: 2, both
`success` / `ok`.

## 11. Not verified / open questions

- Every §7 item (IME on PC and iPad, iPad storage and Web Locks, TimeOnlyDateEdit in the DOM, 保存 re-render, physical
  typing + F5 / kill).
- Masked editors, composition and two tabs in a real browser. Only jsdom ran them, and jsdom cannot prove DevExpress event
  order.
- Production secure context; the module's behaviour on HTTPS with Web Locks.
- The controller lifecycle executed in XAF (source pins and pure helpers only): admission per editor, generation bumps,
  same-render removal, coverage rerun.
- Real `IJSStreamReference` transfer, size limit, interruption (M3).
- Older-release compatibility (no prior release of this module exists; foreign prefixes are left alone).
- C6 residual: cross-tab read/write/remove interleaving when the writer's heartbeat is older than 2 minutes.
- C11 open: display format different from the edit format after blur.
- **a2 defects D1-D10, unfixed, for the owner** (§5b).
- Owner decisions settled 2026-10-04: the six red tests, decisions 1-5, D5. Still open: the M2 seam (§9.1); the switch rule
  is to be revisited at M3.
- Merge with master: done by the main session (d235bd1b).
- The razor move (`@attributes` after `Time`) is not checked in a browser: TimeOnlyDateEdit is not attributed in M1.

could_not_determine: whether iPadOS Safari delivers composition events as Chrome does; whether production is a secure
context; whether a heavily throttled background tab can be alive with a heartbeat older than 2 minutes while it still
types; whether real DevExpress editors batch two replies into one observer callback (D1a) or send one reply per
composition (D1b); real-browser quota behaviour with both journals full (D5).

## 12. KB record draft (for `log_new_fix`; the main session allocates the id)

State 2026-10-04: implemented, uncommitted on design/edit-draft-client-journal (HEAD d235bd1b + M1 working files). Owner
rulings applied: six red tests resolved, all suites green; decisions 1-5 accepted (switch rule revisited at M3). Delta
review a2 found D1-D10, left unfixed for the owner. M1 open until M0 §7. Not deployed; journal key off everywhere.

Title: Client-side input journal M1: one localStorage key per entry, server-built data-editdraft descriptor
(Xaf.EditDraft.Blazor). Category: Blazor / XAF. Components: XAF ComponentModelBase.SetAttribute, DxMemo, DxTextBox,
DxMaskedInput, DxTimeEdit, localStorage, MutationObserver.

Problem: typed text in a focused editor was lost on F5 or a dead circuit, because XAF text and memo editors post on blur.

Fix:
- A library controller puts `data-editdraft` (JSON) on admitted editors with `View.CustomizeViewItemControl` +
  `ComponentModelBase.SetAttribute`. The attribute lands on the DevExpress editor root.
- An RCL ES module journals only those editors, one localStorage key per (owner token, load, context, member,
  generation), written synchronously.
- Masked editors are read from the root's `field-text` mutation in a microtask. One reply answers one attempt.
- Composition and masked-composition text stays an incomplete copy until committed.
- A refused write evicts nothing and is retried with the refused value.
- A durable clear marker stops writes from actions made before a clear in any tab.
- Server-set values are never edits.

Lessons:
- A component without a CaptureUnmatchedValues parameter (CareCrew's FilteredEnumEdit, JapaneseEraDatePicker) does not
  render the attribute.
- `CultureInfo.GetCultureInfo(name)` accepts any name under ICU; use `predefinedOnly: true`.
- FluentAssertions `Equal("a", "reason")` treats the reason as an expected element.

Files: see §2.

<!-- claude-only:start -->
## 13. Security note (Claude only — single-model by the owner's rule; not sent to Codex)

`EditDraftJournalBoundary` (owner token, parsers) and the security posture were not reviewed by Codex.

- **Owner token.** It is SHA-256 of a fixed library string plus the owner Oid ("N"), first 32 hex digits. It is a filter
  only. The M3 intake must recompute it from `EditDraftServices.CurrentOwner` and never trust a token from the browser.
- **Parsers.** They check shape only: closed kind list, Guid context and Oid, lengths, a small string-only `rc`, culture
  ≤ 64 characters, entry key equal to the key its own fields make. CopyOnly is recomputed from kind, format and culture,
  never taken from the input.
- **No owner, no attribute.** A login without an owner gets no attribute.
- **No markup.** The module renders no markup; it never uses innerHTML (pinned in T72's js pin).
- **Logs.** Log lines carry member names, kinds and counts, never values. The platform and coverage lines are value-free.
- **Data at rest.** Plaintext localStorage (owner decision 13), narrowed to admitted members, purged at 60 minutes.
  `clear(ns)` exists but is not wired to logoff yet (decision 12, M2/M3).
- **Debug logging (carried from M0 §12).** In Development, hub invocations and JS interop results log at Debug level with
  their arguments. The M1 platform and coverage results are value-free, but an M3 intake returning values through interop
  must not run where `Microsoft.AspNetCore.SignalR` logs at Debug.
<!-- claude-only:end -->
