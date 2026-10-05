# Xaf.EditDraft client-side journal — M1b cluster A: refused-write retry and clear ordering (2026-10-04)

Collaborator run `2026-10-04-edit-draft-journal-m1b-a-89eeeb`. Claude: Opus 5.5. Codex: gpt-6-astra, `-Effort xhigh`
passed explicitly (codex-cli 0.153.4): `tests` a1 (requirement-only, before any code was reviewed) and `diffreview` a1
(pass 2 of 2). Worktree `CareCrew-journal`, branch `design/edit-draft-client-journal`, HEAD `63622641` (M1 committed WIP).
Owner ruling 2026-10-04 (label verbatim): "Commit M1 as is on the design branch, labelled WIP; redesign the two clusters
as M1b, requirement-first". This run is cluster A (D3, D4, D5, D6; D8, D9, D10). Cluster B (D1, D2, D7) is not touched.
No commit, no deploy, no database.

Inputs: M1 write-up `docs/edit-draft-client-journal-m1-2026-10-04.md` (§5b D1-D10); Codex M1 a2
`diffreview` output (run 96623a, local scratch) and its reproductions. Evidence of
this run: a local scratch folder outside the repository (cited `ev/<file>`). Codex
outputs (same folder): `tests` a1 (O1-O9, X1-X44), `diffreview` a1.

## 0. Combined answer

M1b-A replaces the M1 module's refused-write snapshot, time comparison and epoch counter with one intent log per journal
key: a retry applies the key's current intent, a clear is identified by its marker value (never its time), every write
checks the marker before and after, and an unreadable marker closes the intent. All D3-D6 reproductions and Codex's
requirement-only schedules were red on HEAD 63622641 and pass on the candidate (js 112 pass + 1 todo; 22 of 23 mutants
killed); D8 makes unknown-baseline conversion copy-only, D9 starts the module for unsupported-only views and reports
later controls, D10 now has a distinguishing test. Codex's review (pass 2 of 2) then found ten defects, eight of which
Claude reproduced: a clear can still be undone or a post-clear edit lost under a refused marker with exhausted retries
(C1b), a refused take-back removal (C2), a post-clear return to a value stored before the clear (C3), two overlapping
clears (C4), a clear notice replayed after its marker expired (C5), and a closed value written on blur (C6); a stale
write that is immediately taken back (C1a) cannot be prevented with localStorage, which Codex counts as a violation of
R-A1 and Claude as a limit of the platform. These are reported UNFIXED for the owner, together with one M1 .NET test
that contradicts R-A5 (T73_T76). Cluster B (D1, D2, D7) is unchanged and still reproduces as before.

## 1. Status

Implemented, uncommitted, 2026-10-04, on top of HEAD 63622641. Not deployed (the journal switch is off everywhere).
Open for the owner: the T73_T76 conflict (§6.3), the two stated residuals (§4.3 items 1 and 2), the open choices (§4.3
items 3-5), and the review's defects (§5), which are reported UNFIXED (two passes used).

## 2. What was wrong (M1, HEAD 63622641)

The M1 module kept a refused write as a snapshot (`failed` map: value, field state, root) and retried that snapshot;
it ordered a clear against pending work by comparing the clear marker's TIME with the field state's last action time
(`clearedSince(ns, s.actedAt)`, with `>`), and dropped scheduled work with a per-tab epoch counter bumped when a clear
notice arrived. Codex M1 a2 showed, and Claude reproduced (`96623a\evidence\a2-repro.txt`):
- D3a: A stored, B refused, back to A: the de-duplication cache said A was stored, so nothing superseded the refused B and
  its retry stored B while A was shown. D3b: the old retry also removed a newer composition copy.
- D4a/D4b/D4c: time equality, a later key that changed nothing (it moved `actedAt`), and a clear landing between the
  marker read and the write each let a pre-clear value back.
- D5: an unreadable marker counted as "no clear"; a marker refused by quota left no durable signal.
- D6: the epoch bump on a late notice dropped a refused edit made AFTER the clear.
- D8: unknown baseline converted seconds-or-finer formats; D9: unsupported-only views and later controls were not
  reported; D10: the surviving M1 mutant was not equivalent (test gap).

All D3-D6 schedules were turned into tests and recorded RED on the HEAD module before the restructure (§4.4).

## 3. Ruled out

| Option | Why not |
|---|---|
| Patch the M1 branches (snapshot retry + time compare + epoch) | The owner ruled "restructured, not patched"; each a2 defect is a different way the time/epoch model loses order |
| Order clears by wall-clock time | Same-millisecond clears and edits are indistinguishable (D4a, X43); a key that changes nothing moves an action time (D4b) |
| Web Locks around clear and write (atomic read-check-write) | Optional (none on the plain-HTTP dev origin) and no cross-renderer visibility guarantee (KB fix-552); still needed: the take-back |
| Keep dropping work by a per-tab epoch on notice arrival | Arrival time stands in for the clear's order (D6) |
| Writing the marker before removing the entries (M1 order) | Under a full quota the marker cannot be written while the entries still occupy the room (D5) |

## 4. The fix (implemented)

### 4.1 Ordering model (module `Xaf.EditDraft.Blazor/wwwroot/edit-draft-journal.js`, section "intent log")

1. **One intent log per key.** Each journal key this page load writes (entry or incomplete copy) has one record with a
   monotonic per-entry sequence. Every recorded value, every removal of an incomplete copy and every clear is an intent
   with the next sequence; only the latest intent is ever applied, so a retry writes what the user means at retry time
   (D3a) and an older write never removes a newer composition's copy (D3b; entry writes also wait while the field
   composes, M0 §5 item 2).
2. **A clear is identified by its marker value, never its time.** The marker is `<time>|<load>|<counter>`, different for
   every clear. A value intent carries the marker under which the user action that produced it happened; a re-read of an
   unchanged value keeps its first marker and time, so neither a re-read nor a key that changed nothing moves a pre-clear
   value past the clear (D4b).
3. **Check before and after the write.** Another marker before `setItem`: nothing is written. Another marker after it: the
   value is removed before the call returns (D4a, D4c). A late notice acts on the marker in storage now (X9).
4. **A clear supersedes only what does not carry its marker.** Work made after another tab's clear carries its marker and
   survives a late notice, refused retries included (D6). Stored entries carry the marker (`cm`), so the clearing tab's
   second sweep removes only what was written without it, and a tab removes its own stored value written without it.
5. **Fail closed (D5).** An unreadable marker closes the intent (not written; `stats.closed`; error record
   `{op:'closed', error:'clear-unknown', m}`); the next genuine edit opens the entry again. A clear removes entries
   first, then writes its marker; a refused marker is owed and retried; when it lands, entries stored without it by other
   page loads are removed.

D8: `EditDraftJournalConvert` (`Xaf.EditDraft.Core/EditDraftJournal.cs`, `Time`) returns copy-only for every time format
when the baseline is unknown. D9: `EditDraftJournalCoverageRules` (pure; same file as the controller) decides the module
start (attributed OR unsupported) and a report for a control created after the first line (one pending at a time); the
controller takes the editing context before the kind check and no longer returns before the start decision.

### 4.2 Files

| File | Change |
|---|---|
| `Xaf.EditDraft.Blazor/wwwroot/edit-draft-journal.js` | write layer replaced by the intent log (`intend`, `apply`, `cleanCopy`, `supersede`, `clearSeen`, `sweep`, `owe`/`landOwed`); field states carry `clearMark`, `actionMark`, `markedVal`/`valueMark`/`valueAt`; clears replace states at once (`refreshStates`) instead of an epoch; `clear()` order: supersede, sweep, marker, second sweep; `report()` adds `owedClears` and stats `closed`, `droppedExpired`, `deferredComposing` |
| `Xaf.EditDraft.Core/EditDraftJournal.cs` | D8 (3 lines + doc comment) |
| `Xaf.EditDraft.Blazor/EditDraftJournalAttributeControllerBlazor.cs` | D9 (additive; the M1 C12 source pins are kept verbatim) + `EditDraftJournalCoverageRules` |
| `Xaf.EditDraft.Tests/js/helpers/harness.js` | additive options, off by default: `failKeys`, `capacity`, `silentUnchanged`, `afterGet`/`beforeSet`/`afterSet`, `readFilter` |
| NEW `Xaf.EditDraft.Tests/js/test/m1b-a.test.js` | 41 tests: D-ids (a2 reproductions), X-ids (Codex tests a1), mutation-gap schedules; 1 `todo` (X17b) |
| NEW `Xaf.EditDraft.Tests/EditDraftJournalM1bTests.cs` | D8 (14 cases), D9 (4 tests) |

No M1 test file was edited. No NuGet/npm package added. Storage KEY layout unchanged (marker value format and the `cm`
field are new), so no browser smoke was required by the brief and none was run.

> Status note (2026-10-04, M1b-C): the clear marker, `cm` and the closed state were removed by the per-tab-clears code
> pass; `m1b-a.test.js` now keeps 13 of its 41 tests (28 deleted, not revised). See
> docs/edit-draft-client-journal-m1b-c-2026-10-04.md.

### 4.3 Decisions and residuals (owner)

1. **X6 literal not met (residual).** Codex X6 asks that no `setItem` of the pre-clear intent succeeds after the clear.
   For a clear that lands between a tab's last marker read and its `setItem`, that is not achievable with localStorage
   (no compare-and-swap; Web Locks optional and not a visibility guarantee). The module removes the value before the
   write call returns, and on the clear notice in any case (tests D4c, D4c').
2. **Lost marker (residual).** When a clear's marker cannot be written, another tab has no signal (no marker; no removal
   event when nothing of the namespace was stored); its pre-clear retry can write before the owed marker lands. It is
   removed when the marker lands (by that tab on the event, or by the clearing tab's sweep for a closed page load, X17c).
   If the clearing tab closes before its marker lands, the write stays (X17b, `todo`, fails by design). Also: entries
   other tabs stored in that gap are removed when the marker lands even if they were typed after the clear (fail closed).
   **Codex C1b showed this residual is wider than stated here:** the owed marker stops after five timer retries; with no
   focusout in the clearing tab it never lands, and the other tab's pre-clear value stays (§5).
3. **Descriptor change (Codex O9 open choice).** A refused intent of descriptor d0 completes under d0's own key after a
   record change or save (M1 dropped it). Nothing moves to the new key.
4. **Retry allowance (X13 open choice).** First attempt + 5 timer retries per value intent; a new value resets it, a
   re-read of the same pending value does not; afterwards focusout and the next event still try; `pendingWrites` counts it.
5. **Reopening after fail-closed (X21 open choice).** The closed intent is consumed. What the code does (Codex C6,
   reproduced): a re-read of the unchanged value (blur, page hide) makes a new intent with the original marker and time,
   which IS written once the marker can be read and shows no clear (and is dropped when it shows one). Claude's account
   to Codex said "the next genuine edit opens the entry"; that statement was wrong. The rule is the owner's choice (§5).
6. Empty field = an empty-valued entry (as M1 T7). Pending values older than 60 minutes (from when they were made) are
   dropped. A post-clear value whose marker has expired matches an empty marker slot.

### 4.4 Red-before / green-after per D

Red-before: `test/m1b-a.test.js` against the HEAD 63622641 module (`ev/red-before-final-on-HEAD63622641.tap`: 41 tests,
13 pass, 27 fail, 1 todo); .NET: `ev/dotnet-m1b-red-before.log` (13 of 15 failed on the unchanged Core/controller).
Green-after: `ev/js-green-after.tap`, `ev/dotnet-xaf-editdraft-after.log`.

| D | Test(s) | HEAD 63622641 | Candidate (and Codex a1's closure, §5) |
|---|---|---|---|
| D3a | D3a, D3a', X10 (= D3a), X11, X14 | D3a, D3a', X14 FAIL (X11 passes on HEAD) | pass; post-clear reversal open (C3) |
| D3b | D3b (X12) | FAIL | pass; M12 coverage gap (C7) |
| D4a | D4a, X43, X4 converse | D4a, X43 FAIL (X4 passes on HEAD) | pass; closed per Codex |
| D4b | D4b, X5 | FAIL | pass; closed per Codex |
| D4c | D4c (immediate write and retry), D4c' (stale view), X1b | FAIL | pass by take-back; NOT closed against X6 per Codex (C1a, C2) |
| D5 | D5a, D5b, X15 (= D5a), X16, X16b, X17, X17c, X19, X20, X20b, X21, X44b, X44c | FAIL (X18 passes on HEAD) | pass; X17b `todo`; NOT closed overall per Codex (C1b, C2, C6) |
| D6 | D6, X7/X8, X8b, X9 | FAIL | pass; broader post-clear preservation open (C3, C4, C5) |
| D8 | `D8_X22_*` (9 cases), `D8_X23_*` (3 cases) + 2 preservation tests | 12 FAIL, 2 pass | pass |
| D9 | `D9_*` (4 tests: rules + source pins) | pin test FAIL; rules class absent | pass (XAF lifecycle not executed) |
| D10 | D10 (same value at T+100, before the timer) | passes on HEAD (test gap); FAILS on the M1 survivor mutant (`ev/d10-on-m1-survivor-mutant.tap`) | pass |

Mutation check of the new rules (`ev/mutants-m1b.js`, `ev/mutants-m1b-run3.txt`): 23 mutants, 22 killed; M12 (the
composition-copy ordering guard in `cleanCopy`) survives. Claude called it unreachable while the composition deferral
(M11, killed) holds; Codex C7 showed a reachable schedule (descriptor change mid-composition), so it is a test gap.
Run 1 of the mutation check was invalid (the runner itself failed; every mutant "killed by 1 [test]"), run 2 found seven
survivors, run 3 is after the part-3 tests.

## 5. Codex diffreview a1 (pass 2 of 2) — defects reported UNFIXED

Call: `diffreview` a1, success / ok, exit 0, 13.4 min, candidate unchanged (manifest
`candidate-a1.json` in local scratch, diff SHA-256 46E2B113…). Codex ran the js suite (113 / 112 / 0 / 1 todo) and
in-memory probes against the frozen module, read DevExpress docs for the controller lifecycle, and checked NHM. It noted
`input_mismatch`: this write-up appeared in the working tree during the review (not part of the candidate; the six
candidate files were unchanged). Claude then reproduced every behavioural finding against the frozen candidate
(`ev/review-a1-verify.js`, `ev/review-a1-verify-c7.js`, output `ev/review-a1-verify.txt`).

Rank rule (the owner can re-rank): nothing here is observed in production (not deployed, switch off). A defect reproduced
by an executed check ranks above a static one; a test gap or a report-count error ranks lower.

| ID | Severity (Codex) | Finding | Claude's check | Verdict | Smallest change that would address it (NOT made) |
|---|---|---|---|---|---|
| C1a | High | D4c: a `setItem` of the pre-clear value succeeds after the other tab's clear, then is taken back; X6 forbids the write itself | reproduced: stale `setItem` seen by `afterSet`, final storage empty | correct as observed; DISAGREEMENT on what it means (below) | none within localStorage (Claude); Codex: the requirement is not met |
| C1b | High | marker refused; after the owed marker's 5 retries, storage recovers; the other tab's pre-clear value stays (`owedClears=1`, no timers), clearing tab still open | reproduced: `value=pre-clear, owedClears=1, timers=0` | correct; Claude's account understated it (C.4.2 said it is removed when the marker lands; with the retries used up the marker does not land without a focusout in the clearing tab) | keep retrying the owed marker until it lands or 60 min pass (no attempt cap), also from every event in the clearing tab |
| C2 | High | the take-back `removeItem` is refused: the stale entry stays and nothing tracks it | reproduced: `value=pre-clear, pendingWrites=0` | correct | on a refused take-back keep the written value recorded under the OLD marker (or a pending removal intent), so the notice or a retry removes it |
| C3 | High | de-duplication compares with a value stored before an undelivered clear: a post-clear return to that value is never written | reproduced: post-clear `A` absent, `pendingWrites=0` | correct | de-duplicate only when the stored value was written under the intent's own marker (`written.mark === it.mark`) |
| C4 | High | overlapping clears: A pauses after writing M1, B clears (M2) and types under M2, A's second sweep removes B's entry and reports ok | reproduced: `{ok:true, removed:1}`, B's post-M2 entry gone | correct | before the second sweep re-read the marker; if it is no longer this clear's, skip the sweep (a newer clear owns the namespace) |
| C5 | High | a clear notice delivered after its marker expired (marker slot empty) is acted on with the event's value and removes a newer entry | reproduced: `post-expiry` stored, gone after the notice | correct | when the slot is empty, ignore a notice whose marker time is 60 min old or older |
| C6 | High | a closed intent is written on blur without a new edit once the marker can be read again | reproduced (no clear involved): `closed=1`, then `closed text` written on blur | correct that Claude's stated policy (C.4.5 "next genuine edit") is not what the code does; DISPUTED that this alone breaks R-A4 (below) | owner chooses the reopening rule; to enforce "genuine edit": remember the closed value on the field state and skip re-reads of it |
| C7 | Medium | M12 (copy-ordering guard) is reachable: d0 copy of a newer composition, descriptor change, composition finished under d1, then the d0 retry | reproduced: candidate keeps the d0 copy, the M12 mutant removes it | correct; Claude's "unreachable" claim was wrong | extend X14 through the composition's end before releasing the d0 retry |
| C8 | Medium | the first enumeration fails, the second sweep removes the entry, `clear()` reports `removed:0` | reproduced: `{ok:false, removed:0}`, 0 keys left | correct | report `first.removed + second.removed` also when the first enumeration failed |
| C9 | Medium | coverage coalescing: a control customized after the browser's `coverage` snapshot but before the continuation clears `_reportPending` is not reported again | not reproducible here (needs an XAF host); the source path exists as described | unverifiable (static) | count controls customized while a report is pending and schedule one more read when the count changed |
| C10 | Medium | the .NET acceptance stays red (T73_T76 vs R-A5) | same as §6.3 | correct (known) | owner decision (§6.3) |

Disagreements that no executable check settles (both positions, for the owner):
- **C1a / X6.** Codex: R-A1 says a pre-clear value "may never be written after" the clear; a write that is taken back is
  still a write, so D4c and D5 are not closed. Claude: with localStorage there is no compare-and-swap, and Web Locks are
  optional and give no cross-renderer visibility (KB fix-552), so a write already past its last marker read cannot be
  stopped; the strongest feasible rule is "removed before the write call returns, and on the notice"; the requirement's
  outcome for D4c in R-A3 ("must leave the field cleared") is met. Owner: accept the platform limit as a stated residual,
  or require a different mechanism (for example a single writer per origin), which is a design change.
- **C6.** Codex: reopening on a lifecycle read reactivates text whose clear status was unknown. Claude: once the marker
  can be read and shows no clear, writing the value does not re-create a cleared value (R-A4); the variant that does
  re-create one needs a lost marker (C1b). Claude's write-up statement was wrong either way. Owner: pick the reopening
  rule (status known again, or a genuine edit).

Data-loss / privacy escalation (guardrails escalation table): C1-C5 concern discarded recovery text coming back (C1, C2)
and post-clear text being lost (C3, C4, C5). The module is not deployed and the switch is off; the run STOPS here and
hands them to the owner.

Codex's per-D closure (verbatim substance): D3a closed for the original schedules, post-clear reversal open (C3); D3b
closed for the original schedule, M12 coverage missing (C7); D4a closed; D4b closed (also X44b/X44c); D4c NOT closed
against X6 (C1/C2); D5 closed for marker-read and the tested quota cases, NOT closed overall (C1/C2/C6); D6 closed for
the original schedule, broader post-clear preservation open (C3-C5); D8 guard present, acceptance conflict C10; D9
present in source, lifecycle unverified (C9); D10 closed. M1 test files unchanged; no reply-matching edits outside the
listed seam; no NHM counterpart.

`could_not_determine` (Codex): production occurrence, deployed bytes, effective configuration; the D9 lifecycle on
DevExpress 26.1.4 (no XAF host run; the docs fetched did not settle it); the boundary parser's handling of `cm`
(excluded file); cluster-B behaviour beyond the unchanged handlers; owner acceptance of the weaker stale-write guarantee
and of the retained .NET assertion; who added the write-up during the review (Claude did: it was drafted while the call
ran and is not part of the candidate).

## 6. Verification

### 6.1 Suites and build (artifacts path `artifacts/claude-test/m1b-a-89eeeb`, deleted after)

| Check | Command | Result |
|---|---|---|
| js | `npm test` in `Xaf.EditDraft.Tests/js` | **113 tests: 112 pass, 0 fail, 1 todo** (72 M1 + 41 new; `ev/js-green-after.tap`) |
| Xaf.EditDraft.Tests | `dotnet test Xaf.EditDraft.Tests.csproj --artifacts-path …` | **271 total: 270 pass, 1 fail** (`T73_T76`, §6.3; `ev/dotnet-xaf-editdraft-after.log`) |
| Sample consumer | `dotnet test samples/…/Xaf.EditDraft.Sample.Tests.csproj --no-build` | **42 / 42** (`ev/test-sample.log`) |
| Rostering | filter `EditDraft|ChartDraft|TimeEditor|JapaneseDateColumn|StaffOverTimeHoliday`, `--no-build` | **300 / 300** (`ev/test-rostering.log`) |
| Build | `dotnet build CareCrew.sln -c Debug --artifacts-path …` | 19 projects, **0 errors**, 2,209 warnings (same count as M1), none in a changed file (`ev/build-sln.log`) |

> Status note (2026-10-04, M1b-C): `T73_T76` now expects `CopyOnly` by the owner's ruling "Expect CopyOnly"
> (EditDraftJournalTests.cs:390-392); Xaf.EditDraft.Tests is **271 / 271** in the M1b-C run. The js suite is now 114 tests
> (113 pass, 1 todo). See docs/edit-draft-client-journal-m1b-c-2026-10-04.md.

### 6.2 What each check proves

The js suite proves the module's behaviour in jsdom with the harness's event and storage model; it does not prove
DevExpress event order, real-browser quota or cross-process localStorage propagation. The .NET tests prove D8's
conversion results and D9's decisions and source; the XAF controller lifecycle (CustomizeViewItemControl calls, posted
callbacks, JS interop) is not executed anywhere.

### 6.3 Red test for the owner (escalated, not edited)

> Status note (2026-10-04): resolved. The owner ruled "Expect CopyOnly"; the assertion expects `CopyOnly` and is green
> (271 / 271 in the M1b-C run).

`EditDraftJournalRuleTests.T73_T76_complete_text_converts_with_the_effective_format_and_a_clear_and_midnight_stay_different`
(M1, `Xaf.EditDraft.Tests/EditDraftJournalTests.cs:390`) asserts
`ToCanonical(D("time","HH:mm:ss"), "08:30:15", typeof(TimeSpan?)).Raw == "08:30:15"` with the default
`baselineKnown:false` ("seconds shown: no baseline needed"). R-A5/D8 (and Codex X22) require copy-only for that call.
Rerun once on its own: still red (`ev/dotnet-T73_T76-rerun.log`). Classification: (d) ambiguous requirement — R-A5
contradicts R-A6 for this one assertion. Proposed (owner decides): pass `baselineRaw: "00:00:00", baselineKnown: true` in
that call (then "08:30:15" converts), or expect `CopyOnly`.

## 7. Deployment

- Build: CareCrew.Blazor.Server (project references to both libraries), published by
  the host's publish script; the module ships as an RCL static asset. Inert until
  `EditDraftCapture:Journal:Enabled` and the type key are set (not set anywhere).
- Mirror: none (no NHM counterpart of any changed file). No schema change; no ChartWorkflowServiceV2, report layout or
  sync consumer. The host's chart journal (`_Host.cshtml`) does not read the library's keys or marker values (grep: no
  `XafEditDraft` in CareCrew.Blazor.Server pages or scripts).
- Compatibility: no prior release of the module exists. Markers written by the M1 WIP bytes (plain time values) are read
  as markers; entries without `cm` are treated as written without any marker.

## 8. What cluster B needs from the seam

- Unchanged: `openOperation`, `oldestOpen`, `hasOpenOperation`, `onMutations`, `COMPOSITION`, `onEvent`, `isEditKey`.
- Changed shared code: `stateFor`/`newState` (clears replace states at once via `refreshStates`; states carry
  `clearMark`, `actionMark`, `markedVal`/`valueMark`/`valueAt`, `fieldRef`, `d`, `ord`; `actedAt` removed), `markAction`
  (reads the marker), `record` (computes the intent's marker and time; calls `intend`/`cleanCopy`), `onStorage`, `pendingFor`;
  `apply` reads `composing`/`awaitMask` of the field's current state (read-only) for the composition deferral.
- Contract for cluster B: a matched reply must produce its value through `record(…, 'mutation')`; the existing fences
  (`states.get(field) !== s`) keep dropping delayed reads after a clear because the state object is replaced. The marker
  of a masked value is the field state's `actionMark` (the latest action), not per attempt: if cluster B lets replies
  answer attempts out of order across a clear, it should carry the marker on the attempt object and pass it to `record`.
- Cluster B's D2 fix will interact with the deferral: entry writes of a field wait while `awaitMask` is unanswered.

## 9. Contribution log

### What Claude did

- Phase 0 preflight (below); run id 89eeeb; requirement-only directory with `REQUIREMENT.md` only (R-A1..R-A6 verbatim, the
  D-definitions verbatim from the M1 write-up and Codex a2, the M1 rules that stay, the module's public API names and the
  harness's affordances); launched `tests` a1 before reviewing any code with Codex.
- Wrote the part-1 tests from Codex's a2 reproductions and recorded them RED on the HEAD module (all D3-D6), D10 against
  the M1 survivor; then the part-2 tests from Codex's X-list, and part 3 from the mutation gaps. Extended the harness
  (additive).
- Restructured the module's write layer (intent log), then fixed the holes its own tests found:
  - the first cut took a value's marker at recording time, so a focusout re-read of a pre-clear value resurrected it
    (found by D6) — markers now come from the user action, and re-reads keep the first marker;
  - a re-read after the marker expired (found while writing X44) — re-reads keep the production time, and expired
    post-clear markers match an empty slot;
  - `landOwed` abandoned an owed marker when the prior marker was unreadable (found by X19 `all`);
  - the D4c test placed the clear at the keydown's marker read (a test-placement error, corrected to the write).
- Adopted from Codex's X-list: no entry write while composing (X12) and acting on the current marker for late notices (X9).
- D8 in Core; D9 additively in the controller, keeping M1's C12 source pins verbatim (an earlier, more restructured D9 would
  have turned C12 red; reverted before any run).
- Built the solution, ran every suite, ran the mutation check three times (runner fixed after run 1, whose results were
  invalid: every mutant "killed by 1 [test]" because `node --test test/` itself failed).
- Built the frozen candidate and the diffreview pack, launched `diffreview` a1, reproduced C1-C8 afterwards.
- Wrote this document (it was drafted in the working tree while the review ran; not part of the candidate).

Got wrong (Claude): the M12 "unreachable" claim (C7); the C.4.2 residual was understated (C1b); the C.4.5 reopening
statement did not describe the code (C6); C2, C3, C4, C5, C8 are defects in Claude's code; the first D9 restructure would
have broken an M1 test; the first mutation runner.

### What ChatGPT (Codex) did

- `tests` a1 (requirement-only, REQUIREMENT.md only): ordering rules O1-O9 and expectations X1-X44 in six groups, each
  with schedule, observable, boundary/failure case and predicted red-before; it named the open choices (empty-field
  representation, marker representation, lost-marker signalling, retry allowance, reopening, old-descriptor retries,
  reporting shape) instead of choosing. Its X12 and X9 found two defects in Claude's first cut; X6/X17's literal reading
  is the C1a disagreement.
- `diffreview` a1: verified the manifest, ran the js suite and in-memory probes (including mutants of its own), read
  DevExpress docs for the controller lifecycle, inventoried NHM; returned C1-C10 and a per-D closure table. No file
  changes (0 `file_change`), no `node_repl` / `cua_repl`, no web search.

### Found issues, by tool

"Found by" = who raised it first. Nothing is observed in production (not deployed).

| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| D3-D6, D8-D10 | M1 defects (input to this run) | Codex (M1 a2) | confirmed (executed) | `ev/red-before-final-on-HEAD63622641.tap`, `ev/dotnet-m1b-red-before.log` | see M1 §5b / — / high / no | part-1 tests | fixed for their schedules (§4.4) |
| L1 | first cut: focusout re-read wrote a pre-clear value under the new marker | Claude | confirmed (D6 red) | D6 run during the build | resurrected discarded text / any blur after a late clear / high / no | D6 | fixed before review |
| L2 | D4c test placed the clear at the keydown's marker read | Claude | confirmed (test defect) | D4c run | test did not reach its path / — / high / n.a. | D4c rewritten (beforeSet) | fixed |
| L3 | owed marker abandoned when the prior marker was unreadable | Claude | confirmed (X19 `all` red) | X19 run | lost clear / storage failing on all calls / high / no | X19 | fixed before review |
| L4 | re-read after marker expiry could write a pre-clear value | Claude | confirmed (X44b red on HEAD; mutant M9 via X44c) | X44b, X44c | resurrected text / tab missing the notice for 60 min / medium / no | X44b, X44c | fixed before review |
| L5 | M1 `T73_T76` contradicts R-A5 | Claude | confirmed (red, rerun red) | `ev/dotnet-T73_T76-rerun.log` | acceptance conflict / always / high / n.a. | — | escalated (§6.3); also Codex C10 |
| L6 | a full D9 restructure breaks M1 `C12` source pins | Claude | confirmed (source) | `EditDraftJournalReviewTests.cs:72-75` | red M1 test / always / high / n.a. | — | avoided: D9 additive |
| L7 | "M12 unreachable while the deferral holds" | Claude | REJECTED by C7 | `ev/review-a1-verify.txt` C7 | test gap / descriptor change mid-composition / high / n.a. | X14 extended | open (test gap) |
| L8 | mutation survivors M5, M9, M15, M16, M18, M20, M23 | Claude | confirmed (run 2) | `ev/mutants-m1b-run2.txt` | test gaps / — / high / n.a. | run 3 | tests added; all killed |
| X12 | entry write during an open composition on the retry path | Codex (tests a1) | confirmed (D3b red on the first cut) | D3b | M0 §5 item 2 broken / IME + refused write / high / no | D3b (X12) | fixed |
| X9 | a late notice of an older marker treated as a new clear | Codex (tests a1) | confirmed (X9 reversed red; mutant M7) | X9 | post-clear edit lost / reordered notices / high / no | X9 | fixed |
| X6/X17 | no successful stale `setItem` at all | Codex (tests a1) | disputed (feasibility) | D4c, X17 | see C1a | — | owner (C1a) |
| C1a | stale write taken back (D4c) | Codex (a2 review) | correct as observed; meaning disputed | `ev/review-a1-verify.txt` | transient pre-clear text in storage / clear between read and write / high / no | write history | OPEN, owner |
| C1b | refused marker + exhausted retries: pre-clear value stays | Codex | confirmed (executed) | same | discarded text returns / quota full at clear time / high / no | C1b probe | OPEN, unfixed |
| C2 | refused take-back removal loses the cleanup | Codex | confirmed (executed) | same | discarded text returns / removal failure / high / no | C2 probe | OPEN, unfixed |
| C3 | de-duplication against a pre-clear write | Codex | confirmed (executed) | same | post-clear text lost / return to the old value before the notice / high / no | C3 probe | OPEN, unfixed |
| C4 | older clear's second sweep removes post-newer-clear entries | Codex | confirmed (executed) | same | post-clear text lost / overlapping clears / high / no | C4 probe | OPEN, unfixed |
| C5 | notice replayed after marker expiry removes newer entries | Codex | confirmed (executed) | same | post-clear text lost / suspended tab / high / no | C5 probe | OPEN, unfixed |
| C6 | closed intent written on blur | Codex | confirmed (executed); requirement impact disputed | same | closed text written once status is known / blur after a read failure / high / no | C6 probe | OPEN, owner policy |
| C7 | M12 reachable (coverage gap) | Codex | confirmed (executed) | same | test gap / — / high / n.a. | C7 probe | OPEN (test gap) |
| C8 | `removed` undercount | Codex | confirmed (executed) | same | wrong report count / transient enumeration failure / high / no | C8 probe | OPEN, unfixed |
| C9 | coverage coalescing window | Codex | unverifiable (static) | source | a later control reported late / render during the interop call / medium / no | held coverage response in a host | OPEN |
| C10 | .NET acceptance red | Codex (from Claude's logs) | confirmed | logs | — | — | = L5, owner |

Found independently by both: none (the T73_T76 conflict was raised by Claude and restated by Codex from Claude's logs).

### Codex calls

| Run / call / attempt | Started | Duration | state | validation | exit | Model / effort req. | Effective effort | Reasoning tokens | Search | MCP tools | activity (commands / non-zero / file_change / outside-repo) | Pack | CLI |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 89eeeb / tests / a1 | 09:48:10 | 11.7 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 7,221 | off | none | 2 / 0 / 0 / powershell.exe only | REQUIREMENT.md | 0.153.4 |
| 89eeeb / diffreview / a1 | 10:28:46 | 13.4 min | success | ok (candidate unchanged) | 0 | gpt-6-astra / xhigh | not observable | 7,797 | off | KB lookup_known_fix ×1, get_fix ×1 (fix-552); dxdocs search ×2, get_content ×3 | 23 / 1 / 0 / NHM checkout (read), powershell.exe | diffreview-v1 (in the prompt) | 0.153.4 |

Requirement-only isolation was by convention: `out.md` of `tests` a1 cites only
`REQUIREMENT.md`, and the only outside path is the PowerShell executable. No retries.

### Setup checks (Phase 0; outputs in the local scratch folder)

| # | Item | Result |
|---|---|---|
| 1 | `BASH_MAX_TIMEOUT_MS` | present (2400000) |
| 2 | Read-only query connection (HARD) | not applicable: no database query was run |
| 3 | Repo trusted (HARD) | present (the hook fired, item 5) |
| 4 | Manifest (HARD) | 7 / 7 hashes match in the worktree (`manifest-check.txt`) |
| 5 | Hook fires (HARD) | `git push --dry-run origin HEAD` was blocked with the hook's message; a Monitor running `date` was not blocked |
| 6 | collab.rules | present; `git push origin main` → `forbidden`; the wrapped shape `pwsh.exe -Command "git push"` matched NO rule (absent; Codex runs read-only anyway) (`execpolicy.txt`) |
| 7 | `codex debug prompt-input` | AGENTS.md "Working with Claude (Codex)" present; CLAUDE.md not (pasted as P0 of the diffreview pack) |
| 8 | Tool boundary (HARD) | no Claude MCP tool that writes a database, migrates, deploys, pushes or restarts a service was used; KB write tools unused |
| 9 | Tool parity (HARD) | as in earlier runs: KB read tools (`enabled_tools`) and dxdocs registered for Codex (`codex-mcp-list.json`); DEVIATION: `node_repl` and `cua_repl` are enabled for Codex, forbidden in both prompts, 0 calls |
| 10 | Models (HARD) | gpt-6-astra listed with low, medium, high, xhigh, max, ultra (`models.txt`) |
| 11 | Run id / scratch / salt / binary | 89eeeb; salt (unused: no personal data); codex-cli 0.153.4; login ChatGPT |
| 12 | Snapshot | HEAD 63622641, clean status at the start |
| 13 | Policy drift | agent file Phase 0 item 10 still says "supports `medium`" while ground rule 11 and the launcher say `xhigh` (reported in earlier runs) |
| 14 | Web search | off for both calls |

### Redaction

None needed: no database rows, no logs with personal data, no browser host. Test values are synthetic.

### Inputs Codex did not have

- `tests` a1: everything except `REQUIREMENT.md`, by design (no source, no CLAUDE.md, no existing tests).
- `diffreview` a1: had the frozen diff, the evidence excerpts, CLAUDE.md, the brief, REQUIREMENT.md, its own tests output and
  Claude's account; not MEMORY.md (named) and not `EditDraftJournalBoundary.cs` (excluded by the owner's single-model rule;
  unchanged). Claude's reproductions of C1-C8 (after the review) were not seen by Codex: those verdicts are Claude's.

### Passes used

2 of 2: requirement-only `tests` a1 and `diffreview` a1. Total Codex calls 2, attempts 2, both `success` / `ok`. The
defects of pass 2 are reported unfixed; a further pass is the owner's decision.

## 10. Not verified / open questions

- **Codex diffreview a1 defects C1-C9, unfixed, for the owner** (§5), with the two disagreements (C1a feasibility, C6
  reopening rule) and the data-loss / privacy escalation of C1-C5.
- `T73_T76` conflict (R-A5 vs R-A6), §6.3.
- Residuals stated in §4.3 items 1-2 (C1b shows item 2 is wider than first stated: the owed marker stops retrying after
  five attempts).
- Open choices §4.3 items 3-5 (old-descriptor retries complete under the old key; retry allowance; reopening rule).
- The XAF controller lifecycle for D9 (start for unsupported-only views, later-control reporting, cancellation and
  reactivation) is not executed anywhere; C9 is a scheduling window found by reading only.
- Real-browser behaviour: DevExpress event order, IME (M0 §7), cross-process localStorage visibility (stale per-key views:
  a late notice acts on the marker read from storage, which a stale view can return as an older marker), real quota with
  both journals full.
- Cluster B (D1, D2, D7): unchanged; still reproduces (`ev/a2-repro-on-candidate.txt`). Its fix must respect the seam in §8.
- M1's own open items (M0 §7 checks, C6 residual of M1, C11 display format) are unchanged by this run.

could_not_determine:
- whether a single-writer design (for example one tab owns writes per origin) is acceptable to close C1a, or whether the
  owner accepts the platform limit;
- whether real browsers deliver a `storage` event to a tab while it is suspended or only on resume, and in what order
  (affects C5 and the late-notice rules);
- the reopening rule the owner wants after fail-closed (C6);
- whether the owed marker should retry until it lands (C1b) and for how long;
- the D9 lifecycle on DevExpress 26.1.4 in a running XAF Blazor host.

## 11. KB record draft (for `log_new_fix`; the main session allocates the id)

State 2026-10-04: implemented, uncommitted on design/edit-draft-client-journal (HEAD 63622641 + M1b-A working files).
Codex diffreview a1 found C1-C10 (eight reproduced by Claude), unfixed for the owner. Not deployed; journal switch off.

Title: Client-side input journal M1b-A: refused-write retry and clear ordering by an intent log per key and marker
identity (Xaf.EditDraft.Blazor). Category: Blazor / XAF. Components: localStorage, StorageEvent, navigator.locks,
edit-draft-journal.js, EditDraftJournalConvert, EditDraftJournalAttributeControllerBlazor.

Problem: the M1 journal retried a refused write as a snapshot, ordered a clear against pending work by comparing times,
and dropped work by a per-tab epoch on notice arrival; a retry could store an older value, a pre-clear value could come
back (same millisecond, a key that changed nothing, a clear between read and write, an unreadable marker), and a
post-clear refused edit was dropped by a late notice.

Fix:
- One intent log per journal key with a per-entry sequence; only the latest intent is applied; retries re-derive from it.
- A clear is identified by its marker value `<time>|<load>|<n>`; a value carries the marker of the user action that made
  it; re-reads keep the first marker and time.
- The marker is read before and after every write; a mismatch writes nothing or takes the write back; an unreadable
  marker closes the intent; entries carry `cm`; a clear removes entries before writing its marker.
- Unknown-baseline time conversion is copy-only; the controller starts the module for unsupported-only views and
  reports later controls.

Lessons:
- Take a value's clear marker from the user action that produced it, not from the moment it is recorded: a focusout
  re-read otherwise moves a pre-clear value past the clear.
- De-duplication against "what I last stored" must also check under which clear it was stored (C3).
- A sweep after writing a marker must check that the marker is still its own (C4); a late notice must not act on an
  expired marker (C5).
- localStorage has no compare-and-swap: a write already past its last check cannot be prevented, only taken back.

Files: see §4.2.
