# Xaf.EditDraft client-side journal — M1b-C: per-tab clears, per-load budget, capture time, O1-A cleanup, logoff (2026-10-04)

Collaborator run `2026-10-04-journal-m1b-c-32c9f1`. Claude: Opus 5.5 (claude-opus-5-5). Codex: gpt-6-astra, `-Effort xhigh`
passed explicitly on both calls (codex-cli 0.153.4): `tests` a1 (requirement-only directory, before any code) and
`diffreview` a1 (pass 2 of 2, frozen candidate). Worktree `CareCrew-journal`, branch `design/edit-draft-client-journal`,
HEAD `9dcf84ab` (the design note commit). No commit, no deploy, no database.

Spec: `docs/edit-draft-client-journal-per-tab-clears-2026-10-04.md` ("note"). Owner rulings 2026-10-04 (labels verbatim):
"Simplify: clears are per-tab only, conflicts resolved at intake; short design note first" -> "Yes, both"; O1 "O1-A: remove
only when the writer's Web Lock is gone"; O2 "One-shot sweep of the whole owner namespace at logoff, no notice"; O3-O5 "Keep
the existing draft rules: D16 newest-first, Q6 selectable-but-unticked" (M3, recorded only). Evidence:
a local scratch folder outside the repository (cited `ev/<file>`).

## 0. Combined answer

M1b-C makes the browser journal per-tab: each page load writes, evicts and clears only the keys that carry its own load id,
`clear(ns)` reaches only its own tab and sends nothing to other tabs, storage events only count removals, and the stored `at`
is the capture time of the value. Another page load's keys are removed only by expiry or `retire`, and only when Web Locks
show that load's lock gone and its heartbeat absent or stale (O1-A); the new `logoff(ns)` sweeps the owner namespace across
page loads once, with no notice (O2). The marker machinery is deleted (module 1,124 -> 1,099 lines; 36 marker-dependent
tests deleted, 37 requirement-derived tests added); js 114 (113 pass, 1 todo), .NET 271/271, 42/42, 300/300, solution build
0 errors, and a Chrome smoke showed a per-tab clear, a per-load Web Lock visible on 127.0.0.1, `writer-locked` for a live tab
and retirement once that tab closed. Codex's review (pass 2 of 2) found three behavioural defects that Claude reproduced and
left UNFIXED: a resumed writer's new value can still be removed between the cleanup's last read and its removal, or through
a stale lock snapshot (C1); an old retry admitted at a full budget evicts a newer capture instead of itself (C2); and a clear
misses a second field that shares a journal key, so its pre-clear text returns on blur (C3, a regression of the marker
removal). These three and Claude's seven reading choices (D-1..D-7, §4.3) are for the owner; nothing is deployed, the switch
is off, and the M3 intake and M4 logoff call are not built.

## 1. Status

Implemented, uncommitted, 2026-10-04, on top of HEAD 9dcf84ab. Not deployed; the journal switch is off everywhere. The
reviewed candidate is frozen in `candidate-a1.json` (local scratch; 9 files; `candidate.diff` SHA-256 89A07DC3…;
see C4). Codex's review defects are reported UNFIXED in §5 (two passes used). Open for the
owner: C1-C3 (§5) and decisions D-1..D-7 (§4.3). Committing is git-committer's job; nothing is pushed or published.

## 2. The rule that was implemented

1. Each page load owns the keys that carry its load id (entry and incomplete-copy keys: the load segment; heartbeat: the
   suffix). It writes, removes and evicts only those keys. Read from the KEY (`loadOfKey`), never from the stored JSON.
2. `clear(ns)` cancels this load's pending work of the namespace, starts fresh field states from the text shown, and removes
   this load's own `(ns, load)` entries and copies in ONE pass. No marker, nothing sent to other tabs. A refused removal is
   reported (`{ok:false, removed, failed, error}`) and not retried.
3. Storage events only count removals (`removedElsewhere`); they never write, remove, cancel or replace a field state.
4. The stored `at` is the capture time of the value (when the user action made it). Retries and unchanged re-reads keep it.
5. Per-load budget: 60 journal keys and 1,048,576 serialized characters over this load's own keys (all its namespaces,
   incomplete copies included), evicted oldest-first by `at` then key, after a successful write only.
6. O1-A: another load's key is removed only by expiry (in `start()` and `list()`) or by `retire`, and only when a successful
   `navigator.locks.query()` shows that load's lock neither held nor requested AND its heartbeat is absent or 120,000 ms old
   or older. Without Web Locks, or when the query fails, nothing of another load is removed (expired entries are not listed
   and `value()` refuses them). Each load requests the lock `XafEditDraft.w|<load>` at start and again after a
   back/forward-cache restore when a query shows it is no longer held.
7. O2: `logoff(ns)` (new API) removes every load's entries and copies of the namespace in one sweep, with no notice, cancels
   this load's pending work of the namespace, and reports `{ok, removed, own, others}` (+ `failed`, `error`).
8. Legacy `XafEditDraft.clr1|*` keys are never read, written or removed.

## 3. Ruled out (from the note, unchanged)

Keeping any cross-tab clear signal; a global (origin-wide) budget with eviction of other loads' keys; heartbeat-only foreign
removal (both analysts rejected it in the note; Codex's probes C1/C2); a closed state with a new trigger (note S4).

## 4. The change

### 4.1 Deletion table (note §4.9; line ranges at 9a41df63/9dcf84ab)

| Deleted or replaced | Note location | Now |
|---|---|---|
| `CLEAR_MARKER_PREFIX` export; marker branch of `scan` | js:23, 333-357 | deleted; `scan` skips every non-journal, non-heartbeat key |
| header comment on marker identity | js:15-17 | replaced by the per-tab rule |
| `clearsSeen`, `owed`, `clearCount`; stats `clearedElsewhere`, `closed`; `report().owedClears` | js:192-203, 1101-1103 | deleted (`droppedAfterClear` kept for local clears) |
| field-state `clearMark`, `actionMark`, `valueMark`; `refreshStates`' `keepMark` | js:248-295, 704-711 | deleted; `markedVal`/`valueAt` kept (capture time) |
| intent-log rules 2-5, `UNREAD`, `sameMark`, `readMark`, `markMatches`, `storedMark` | js:384-415 | deleted |
| `intend`'s marker argument; `cur.mark`, `written.mark`; serialized `cm` | js:442-451, 478, 504 | deleted |
| marker checks, `clearSeen` calls, after-write take-back in `apply` | js:464-467, 491-502 | deleted |
| `close()` and its resets | js:419-420, 451, 524-530, 566-567, 593 | deleted |
| owed loop in `retryFailed` | js:550 | deleted |
| remote branch of `supersede`; `clearSeen` | js:585-611 | deleted; `supersede(ns)` is local only |
| namespace-wide / keep-marker `sweep` | js:613-629 | replaced by `sweepPrefix` (one pass) |
| `owe`, `scheduleOwed`, `landOwed` | js:631-679 | deleted |
| marker branch of `onStorage` | js:851-861 | deleted |
| marker creation, second sweep, owed state, `marker:false` in `clear` | js:1051-1084 | deleted; `clear` = cancel + one own-key pass |
| heartbeat-only foreign removal (`scan`, `value`, `writerAlive`) | js:34, 342-365, 995, 1012-1046 | replaced by the O1-A gate |

Additions: `loadOfKey` and `LOCK_PREFIX` (exported), `isOwnKey`, the per-load eviction filter, `at: it.at`, `scan`'s `own`
flag and `stale` list, `lockedLoads`, `writerGone`, `removeStaleForeign`, `requestLock`, `resumeLiveness` + `pageshow`
listener, `retireNow` (key-derived load; lock, then heartbeat, then entry), `sweepPrefix`, `validNamespace` (`|` refused in
`clear`/`logoff`), `logoff` (+ module entry point), `list` load from the key, `value()` removes only own expired keys.

Size: module 1,124 -> 1,099 lines (git: +254 / -279). The marker machinery (about 250 lines) went; the O1-A gate, `logoff`,
key-derived loads and comments (about 225 lines) came in. The brief's "mostly deletion" holds for the whole change
(+290 / -1,068 in tracked files) but not for the module alone. Core: three comments only (`MaxEntries` and
`MaxSerializedChars` per page load, `At` = capture time). `EditDraftJournalBoundary.TryParseEntry` (lines 52-80) does not
read `cm`, so removing it needs no server change (verified by reading the file; single-model file, not sent to Codex).

### 4.2 Files

| File | Change |
|---|---|
| `Xaf.EditDraft.Blazor/wwwroot/edit-draft-journal.js` | §4.1 |
| `Xaf.EditDraft.Core/EditDraftJournal.cs` | three doc comments (CRLF kept) |
| `Xaf.EditDraft.Tests/js/test/m1b-a.test.js` | 28 tests deleted, unused helpers removed, header note; 13 kept unchanged |
| `Xaf.EditDraft.Tests/js/test/api-tabs.test.js` | T35b, T37, T40, T42 and `seedAt` deleted; header note |
| `Xaf.EditDraft.Tests/js/test/capture.test.js` | T22, T25 deleted; header note |
| `Xaf.EditDraft.Tests/js/test/review-a1.test.js` | C4, C6 and unused `PREFIX` deleted; header note |
| NEW `Xaf.EditDraft.Tests/js/test/m1b-c.test.js` | 37 tests from Codex's N1-N38 (36 + todo N34b) |
| `docs/edit-draft-client-journal-per-tab-clears-2026-10-04.md` | dated status note under §1 (coordinator's addition) |
| `docs/edit-draft-client-journal-m1b-a-2026-10-04.md` | dated status notes at §4.2, §6.1, §6.3 |

The brief named `docs/edit-draft-client-journal-m1-2026-10-04.md` §4.2/§6.1/§6.3 for the stale T73_T76 / 271 lines; that
document has no §6.1/§6.3 and contains no T73_T76 or 271 line. The sections and lines exist in the M1b-A write-up, which was
updated instead; the M1 document was not touched. No NuGet/npm package added; harness unchanged.

### 4.3 Owner decisions — Claude's reading choices (implemented; each can be re-ruled)

Codex's requirement-only list (tests a1) named two readings for these points. Claude implemented the reading marked ★ and
pinned it in a test; a different ruling changes the module and that test only.

| # | Decision | ★ Implemented | Other reading | Pinned by |
|---|---|---|---|---|
| D-1 | Physical expiry at exactly 60 minutes | removed at "60 minutes old or older" (the rule T72 pins in the module and `EditDraftJournalRules.IsExpired` uses; one rule for listing, `value()` and removal) | R-C5's literal "> 60 min": logically expired at 60:00.000 but physically removed only after it | N30 |
| D-2 | A lock request still waiting for its grant (`query().pending`) | counts as a live writer, like a held lock (fewer removals) | only `held` protects; a waiting request with no fresh heartbeat can be removed | N31 |
| D-3 | `logoff(ns)` result and scope | `{ok, removed, own, others}` (+ `failed`, `error`); enumeration failure `{ok:false, removed:0, own:0, others:0, error}`; also cancels this page load's pending work of the namespace and restarts its field states; one sweep per call (no latch) | `clear`'s shape `{ok, removed}`; no local cancellation; a once-per-namespace latch | N35, N36 |
| D-4 | `retire` naming another load's key | returns a Promise when another load's key is named AND Web Locks exist (the lock query is asynchronous); every other call answers synchronously, so the retained T26/T27/T34/C7/T21 stay green unedited | always a Promise (the retained tests would need revising, which needs the owner) | N28; smoke part 2 |
| D-5 | Budget accounting edges | heartbeats do not count toward 60 keys / 1,048,576 characters; only this load's entries and incomplete copies do. A refused eviction removal is reported and the load stays over budget until its next write (no rollback, no second candidate) | count heartbeats; roll back, or evict the next own key | N13, N14, N16 (refused-eviction case not tested) |
| D-6 | Legacy `XafEditDraft.clr1\|*` keys | never read, written or removed; they stay on development browsers | read and disregard; or remove once | N12 |
| D-7 | Two additions not in the note's §4.9 list | (a) `pageshow` with `persisted` rewrites the heartbeat at once and requests the lock again when a query shows it not held (R-C5 "for its lifetime"); (b) `clear`/`logoff` refuse a namespace containing `\|` (a `\|` would let the prefix sweep reach another namespace) | leave either out | N2b, N32 (a); N36 (b) |

Related, unchanged rules: own-key `retire` keeps "nothing newer pending" per key; the writer report's freshness stays 30 s
while removal freshness is 120,000 ms (N29).

### 4.4 Restated X40 inventory (114 js tests)

| File | Tests | Ids |
|---|---|---|
| capture.test.js | 25 | T1-T21 (incl. T6b, T20b), T23, T24 |
| api-tabs.test.js | 14 | T26-T36, T38, T39, T41 |
| m0-scenarios.test.js | 16 | T43-T58 |
| review-a1.test.js | 9 | C1, C2, C3, C5, C7, C8, C9, C10, T9b |
| m1b-a.test.js | 13 | D3a, D3a', D3b, D10, X3, X11, X13, X14, X18, X28, X35, X41, X42 |
| m1b-c.test.js | 37 | N1, N2, N2b, N3-N18, N21, X14b, N24, N26-N28, N28b, N29-N34, N34b (todo), N35-N38 |

Deleted (36, not revised): m1b-a D4a, D4b, D4c, D4c', D5a, D5b, D6, X1, X2, X4, X5, X7/X8, X9, X16, X17, X17b, X19, X20,
X21, X43, X44, X44b, X1b, X8b, X16b, X17c, X20b, X44c; api-tabs T35b, T37, T40, T42; capture T22, T25; review-a1 C4, C6.
Function set: `createJournal(env)` returns `start, list, value, retire, clear, logoff, coverage, report, loadId`; the module
exports the same eight entry points plus `entryKey, loadOfKey, parseDescriptor, evictionOrder, planEviction, isExpired,
tailClass, createStorage` and the constants; `CLEAR_MARKER_PREFIX` is gone (N38).

## 5. Codex diffreview a1 (pass 2 of 2) — defects reported UNFIXED

> **Status note (2026-10-04, M1b-D).** Owner ruling (label verbatim): "Commit M1b-C now; one bounded pass for C1
> (pageshow re-check), C2 (just-written key is evictable), C3 (state per field), Codex re-check". C1, C2 and C3 below are
> **fixed in M1b-D** (docs/edit-draft-client-journal-m1b-d-2026-10-04.md; uncommitted at the time of this note): a
> restored page load re-checks its own keys on the persisted page show and on lock re-acquisition and writes back any
> that are missing or replaced; the key just written competes in the eviction; every field state of a shared key is
> reset by a clear. Residual: the remover-side window of C1 stays (localStorage has no compare-and-swap); a removal that
> lands after the resumed page load's last re-check stays until its next page show or lock re-acquisition (N34b stays a
> todo with that wording). The M1b-D review found two new defects in the fix (DR1, DR2) and the write-up escalates three
> points to the owner; see its §4 and §5. The table below is left as written.

Call: `diffreview` a1, success / ok / exit 0, 9.2 min, candidate unchanged during the review (frozen manifest
`candidate-a1.json`, local scratch). Codex re-ran the js suite (114: 113 pass, 1 todo N34b), ran in-memory probes against the
frozen module, confirmed the deletion audit (exactly the 36 tests of note §4.10; the 77 retained test bodies unchanged after
newline normalisation; `openOperation` and `onMutations` unchanged), and found no NursingHomeManagement counterpart of any of
the nine paths (NHM HEAD 0e4f5ac5). Claude then reproduced every behavioural finding against the frozen candidate
(`ev/review-a1-verify.js`; output `ev/review-a1-verify-candidate.txt`, and on the HEAD module `ev/review-a1-verify-HEAD.txt`).

Rank rule (the owner can re-rank): nothing here is observed in production (never deployed, switch off). A defect reproduced by
an executed check ranks above a static one; an artifact-only finding ranks lowest.

| ID | Finding (Codex) | Claude's check | Verified | Smallest change that would address it (NOT made) |
|---|---|---|---|---|
| C1 | Foreign cleanup can delete a resumed writer's new value: both cleanup paths decide on a lock snapshot and a heartbeat read taken BEFORE the final entry read and the removal. (a) expiry: B resumes (persisted `pageshow`, heartbeat, new value) during A's final re-read -> A removes the new value; (b) retire: the same during A's entry read; (c) B's lock is restored while its heartbeat write is refused -> A's earlier snapshot still lets it retire. The new value stays absent after B's blur. | candidate: (a) `newValuePresent:false`, B's lock held at the end; (b) `retired:1, newValuePresent:false`; (c) `retired:1, entryPresent:false`, lock held at the end. HEAD loses the value in all three too (no lock gate at all). | **yes** (jsdom/shared-storage model; browser occurrence not verified) | No compare-and-swap exists in localStorage, so the window between the last read and `removeItem` cannot be closed by the remover. Options for the owner: (i) accept it as an O1-A residual (N34b stays `todo`); (ii) let a resumed page load re-check its own written keys at `pageshow` and write back a missing or replaced last value (with its original capture time) — a self-repair on resume, not a storage-event rewrite; (iii) publish the heartbeat before any lock re-request and treat a refused heartbeat as "not resumed" |
| C2 | A late retry evicts a newer capture while keeping the oldest one: `planEviction` never chooses the key just written, and since `at` is now the capture time an old retry can be the oldest entry. Refused X, then 60 newer captures, then X's retry: 60 keys, X kept, N00 evicted. | candidate: `count:60, oldestRetryRetained:true, xAt=T0, newerEvicted:["N00"]`. HEAD: same keys survive, but there X's `at` is its write time (T0+60), so evicting N00 follows HEAD's rule; the defect comes from capture time + the written-key exemption. | **yes** | Let the written key compete: if it is the oldest by `at` then key, it is the one evicted (or, before the write, skip an intent that would be evicted at once and report it); a ruling is needed because it drops the retried text rather than a newer one |
| C3 | Clear misses a field state when two fields share one journal key: `stateByKey` keeps one state per key, so `refreshStates` replaces only that one; the other field's pre-clear text is written again on blur. | candidate: `clear {ok:true, removed:1}`, key absent after the clear, present again after blur with `val:"one"`. HEAD: not reproduced (the old marker check dropped it) — a **regression** of the marker removal. | **yes** (production reachability not verified: needs two live editors with the same namespace, context, member and generation in one page) | Keep every field state per key (a set) or walk all states of the namespace in `refreshStates`; add the two-field schedule as a test |
| C4 | `input_mismatch` (artifact): the pack's "diff SHA-256" label does not hash `candidate.diff` (89A07DC3…); no source difference. | `pasted-diff.txt` (header lines + `git diff HEAD` + the new test file, as pasted) has a different hash; `candidate.diff` = 89A07DC3…. The pack's label was imprecise. | **yes** (metadata only) | Corrected here (§1); no code change |

Codex's assessment of Claude's reading choices (pack §5 items 1-11, now D-1..D-7 in §4.3): item 1 (inclusive expiry) matches
one reading and differs from R-C5's literal "> 60 min" — an unresolved wording conflict (D-1); items 2-7 and 9-10 are
consistent with a reading Codex had named; item 8 (accepting the remaining deletion window) contradicts the N32/N34
preservation expectations — that is C1; item 11 (namespace guard) was out of its scope.

Disagreement that no executable check settles (both positions, for the owner):
- **C1 / N34b.** Codex: marking N34b `todo` does not establish owner acceptance; the newer value must survive. Claude: with
  localStorage the remover cannot close the last-read-to-remove window; the lock gate keeps a live writer safe, and the
  resume path can be repaired by the writer itself (option ii) if the owner wants it.

Data-loss escalation (guardrails table): C1 and C2 lose recovery text; C3 brings discarded text back. The module is not
deployed and the switch is off; the run STOPS here and hands them to the owner.

`could_not_determine` (Codex): production occurrence, deployed bytes and configuration; real browser scheduling, lock
lifetime and storage visibility during back/forward-cache restoration; whether production views can render the C3
arrangement; which artifact the pack's diff digest identified (answered in C4 above); the owner's intended expiry at exactly 60
minutes and acceptance of the C1 residual; behaviour covered only by the supplied .NET/build/browser evidence; the excluded
security, intake and coverage-coalescing work.

## 6. Verification

### 6.1 Red-before / green-after

| Check | Module | Result |
|---|---|---|
| js baseline | HEAD 9dcf84ab | 113: 112 pass, 0 fail, 1 todo (`ev/js-baseline-HEAD.tap`) |
| old 113-test suite, before deletions | candidate | 83 pass, 29 fail, 1 todo — every failure and the todo are on the note's deletion list; X4, X7/X8, X8b, X44, X44b, X44c (also listed) still pass (`ev/js-old-suite-on-new-module.tap`) |
| retained 77 | HEAD and candidate | 77/77 on both (`ev/js-retained-on-HEAD-module.tap`, `ev/js-retained-on-new-module.tap`) |
| new m1b-c (37) | HEAD (red-before) | 9 pass, 27 fail, 1 todo; the 9 that pass cover behaviour HEAD already had: N1, N2, N6, N7, N9, N17, X14b, N27, N29 (`ev/m1b-c-red-before-on-HEAD9dcf84ab.tap`) |
| new m1b-c (37) | candidate | 36 pass, 0 fail, 1 todo (N34b) |
| full js suite | candidate | **114: 113 pass, 0 fail, 1 todo** (`ev/js-green-after.tap`) |
| mutation check | candidate | 16 single-rule mutants built, 16 killed (M1 guard -> X14b only); M15 (retire read order) and M18 not built (no single-line mutation; N33 and T20b/N17 pin them) (`ev/mutants-m1b-c-run1.txt`) |

Every red-before failure is a behavioural difference: write-time `at` (N10, N15, N18, N21), foreign eviction or expiry (N3,
N13, N14, N16, N26, N28b, N30), marker reads and marker-triggered cancellation (N11, N12), cross-load clear (N4, N5, N8), no
`logoff` (N35, N36, N38), unguarded foreign retire (N28, N31, N32, N33, N37: HEAD removed the entry, so `kept` was empty).

### 6.2 Suites and build (artifacts `artifacts/claude-test/m1b-c-32c9f1`, deleted after)

| Check | Command | Result |
|---|---|---|
| Build | `dotnet build CareCrew.sln -c Debug --artifacts-path …` | exit 0, **0 errors**, 2,209 warnings (same count as M1/M1b-A), none in a changed file (`ev/build-sln.log`) |
| Xaf.EditDraft.Tests | `dotnet test … --no-build --artifacts-path …` | **271 / 271** (T72 module pins and T73_T76 green) |
| Sample consumer | `dotnet test samples/…/Xaf.EditDraft.Sample.Tests.csproj --no-build` | **42 / 42** |
| Rostering | filter `EditDraft|ChartDraft|TimeEditor|JapaneseDateColumn|StaffOverTimeHoliday`, `--no-build` | **300 / 300** |

### 6.3 Browser smoke (the exported API changed: `logoff` added, `retire` may return a Promise)

Chrome on the dev PC; host CareCrew.Blazor.Server from this worktree, built into an isolated artifacts folder (0 errors), started by
the run's host script with `EditDraftCapture__Enabled=false`, `TenantChartDraftCapture__Enabled=false`,
`EditDraftCapture__Journal__Enabled=true`, `EditDraftCapture__Types__ToDo__Enabled=true` (Development). The served module has
`logoff` and no `CLEAR_MARKER_PREFIX` (the candidate). Full record: `ev/smoke-summary.md`.

Part 1 — the dev host's LAN origin (http, port 5003) (signed in; **not a secure context, no Web Locks**, as expected for the dev LAN
origin). Two tabs on the M1 test ToDo record; log: `capture ready … enabled=False` and `journal module started` for loads
mut9gkov5vma01 (A) and mut9hai6b6rsec (B), 0 draft write lines, nothing saved.

| Step | Result |
|---|---|
| B types " tabB"; A types " tabA" | one own key each (seq 5) |
| A `clear(ns)` | `{ok:true, removed:1}`; B's key byte-for-byte unchanged (110:373, same `at`); `XafEditDraft.clr1\|*` keys: 0 |
| B `report()` after A's clear | `removedElsewhere=1`, `pendingWrites=0`, `droppedAfterClear=0`, own entry present |
| A types "2" after its clear | new entry, seq 6 |
| B `retire([A's key], [{seq:6, val}])` | synchronous, kept `no-web-locks`; A's key present |
| B `logoff(ns)` | `{ok:true, removed:2, own:1, others:1}`; only the two heartbeats left |

Part 2 — `http://127.0.0.1:5003` (**secure context, Web Locks available**). This browser has no session on 127.0.0.1 and no
credentials were entered, so the served module was imported on the login page and driven with one synthetic attributed
textarea per tab (real module, real Chrome storage, locks and events; the XAF attribute controller is not involved).

| Step | Result |
|---|---|
| Tab C `start()`, types | secure, `webLocks:true`, load mut9jdymompw7a, `lockHeld:true`; `navigator.locks.query().held` = `XafEditDraft.w\|mut9jdymompw7a` |
| Tab D `start()`, types | load mut9jlwb7kxg82, `survivorCount:1`; `query().held` lists both locks |
| D `retire([C's key])` | returned a Promise; kept `writer-locked`; C's key present |
| C `clear(ns)`, then types "!" | `{ok:true, removed:1}`; D's key present; markers 0; C's new entry seq 6 |
| C's tab closed; D checks after 500 ms | `query().held` = only D's lock (Chrome released C's lock on close); C's heartbeat null (removed at page hide) |
| D `retire([C's key])` | `{retired:[C's key], kept:[]}` (writer gone) |
| D `logoff(ns)` | `{ok:true, removed:1, own:1, others:0}`; only D's heartbeat left |

All tabs closed; host stopped (the host process and its dotnet run); port 5003 free. Not exercised: back/forward-cache restore,
frozen or discarded tabs, iPad/Safari, composition/IME, masked editors, the XAF host on a secure origin.

### 6.4 What each check proves

The js suite proves the module's behaviour in jsdom with the harness's storage, event and fake-lock model. The smoke proves
that Chrome on this PC serves the candidate bytes, journals in two tabs of the XAF host, keeps a per-tab clear inside its tab,
grants and lists the per-load Web Lock on a secure origin, releases it when a tab closes, and that the O1-A gate answers
`writer-locked` for a live tab and retires a closed tab's entry. It does not prove back/forward-cache, frozen or discarded tab
lock behaviour, iPad/Safari, cross-process timing, or the XAF host on a secure origin (part 2 used a synthetic editor).

## 7. Deployment

- Build: CareCrew.Blazor.Server (project references to both libraries); the module ships as the RCL asset
  `_content/Xaf.EditDraft.Blazor/edit-draft-journal.js`; published by the host's publish script; open
  pages need a reload. The sample host also references the library.
- Inert until `EditDraftCapture:Journal:Enabled` and the type key are set (not set anywhere).
- Mirror: none. NursingHomeManagement has no Blazor host and no client journal (no counterpart of any changed file). No
  schema change; no ChartWorkflowServiceV2, report-layout or sync consumer.
- Compatibility: never deployed, so no production transition. A development page still running an older module keeps its
  cross-load sweeps until it reloads. Legacy marker keys on development browsers stay in storage (never removed now).
- Production prerequisite for O1-A cleanup: a secure origin (Web Locks). On a plain-HTTP origin nothing of another load is
  ever removed by expiry or retire; only `logoff` and the per-load budget limit growth.

## 8. What the M3 intake must implement (from the note §4.6, with the owner's O3-O5 ruling)

1. Read retained, sequence-frozen entries (`list` -> `value(key, seq)`); validate every one server-side as untrusted
   (`EditDraftJournalBoundary.TryParseEntry`; the key must equal the key rebuilt from the entry's fields). Incomplete copies,
   truncated values and copy-only kinds are not typed recovery; excluding an entry does not authorise deleting it.
2. Skip entries whose writer is alive (`list().writers`: lock held or fresh heartbeat); a stale heartbeat is unknown, not gone.
3. Within one context and member: newest generation, then latest capture time (`at` is now the capture time); a later no-op
   still supersedes older values. Generations of different contexts are not compared.
4. Recognise retired contexts (discarded, claimed elsewhere, expired, hard-deleted by a save) before promotion; when unknown,
   keep the browser entry and do not promote it. A browser discard now reaches only its own page load (residual R1).
5. Convert, then reconcile into the same context's server draft (keep its first baseline); only then classify or omit.
6. Classify with the existing three-way rule; a journal-only entry whose fingerprint differs from the current value is
   Unverifiable, unticked (not Conflict).
7. Promote each context into its own draft row (U1-A, deterministic DraftKey); keep the capture time as `LastCapturedOn`
   (max with the existing value) and the first-capture expiry anchor; never the intake time.
8. Finish intake before the automatic offer is consumed; a late intake re-runs the offer for the still-shown view.
9. Offer (owner O3-O5: existing rules): several contexts of one record -> one offer with D16 newest-first; a member drafted
   again in an older draft shown but never pre-ticked; Conflict/Unverifiable rows selectable but not pre-ticked (Q6); a
   baseline-equal entry whose record changed elsewhere is offered unticked by the existing classifiers. Apply re-checks
   everything.
10. After a durable promotion, `await retire(keys, echo)` (it may return a Promise when another load's key is named and Web
    Locks exist); another load's entry is removed only when its writer is gone (O1-A); never rely on browser deletion as
    the only protection against a replay.
11. M4: call `logoff(ns)` before XAF's sign-out navigation (not wired; no server caller exists yet).

## 9. Contribution log

### What Claude did

- Phase 0 (table below); run id 32c9f1; local scratch folder.
- Built the requirement-only directory (`tests\req\REQUIREMENT.md`: R-C1..R-C9 verbatim, the rulings, interface and harness
  facts, the note's §4.1/§4.7/§4.8/§4.10 excerpts; no source) and launched `tests` a1 before reading Codex anything else.
- Implemented the deletions and additions (§4), wrote `m1b-c.test.js` from Codex's N1-N38, deleted the 36 listed tests,
  recorded red-before on the HEAD module and green-after, ran a 16-mutant check, the .NET suites, the solution build and the
  two-part Chrome smoke; added the coordinator's status note and the M1b-A status notes; built the parity pack and froze the
  candidate for `diffreview` a1; reproduced C1-C3 on the candidate and the HEAD module and checked C4's digests.
- Got wrong or left open: the module is not "mostly deletion" (net −25 lines, §4.1); the pack's diff digest label (C4); the
  C1 window was stated as residual N34b without an owner ruling; C2 and C3 were not foreseen (C3 is a regression of the
  marker removal that the retained and new tests did not cover). The report was first handed back while `diffreview` a1 was
  still running (forced handback); the run resumed on the coordinator's instruction and nothing was relaunched.

### What ChatGPT (Codex) did

- `tests` a1 (requirement-only, 12.0 min): 38 expectations N1-N38 with setups, outcomes and boundaries, and named the
  ambiguities that became D-1..D-7 (expiry at exactly 60 min, pending lock requests, logoff schema and cancellation, marker
  reads, budget accounting of heartbeats and refused evictions, resumed-writer startup, the 30 s writer-report edge).
- `diffreview` a1 (9.2 min): re-ran the js suite, ran probes, audited the deletions and the NHM mirror, found C1-C4, and
  assessed each reading choice against the requirement.
- No file changes (0 `file_change` in both streams); no web search; MCP: KB `lookup_known_fix` and `get_fix` in diffreview.

### Found issues, by tool

"Found by" = who raised it first. Nothing is observed in production (never deployed).

| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| F1 | C1 resumed writer's new value removed by foreign cleanup (last-read window; stale lock snapshot) | Codex (window first stated by Claude as N34b) | confirmed (reproduced, 3 schedules) | `ev/review-a1-verify-candidate.txt` | recovery text lost / resume during cleanup, or lock-only resume with refused heartbeat / harness high, browser unknown / no | extended N32 schedules | OPEN — owner (§5) |
| F2 | C2 old retry admitted at a full budget evicts a newer capture | Codex | confirmed (reproduced) | same | newer text evicted / late retry after the budget filled / high in harness / no | the schedule in §5 | OPEN — owner |
| F3 | C3 clear misses a second field sharing a key; pre-clear text returns on blur | Codex | confirmed (reproduced; regression vs HEAD) | same + `ev/review-a1-verify-HEAD.txt` | discarded text returns / two live editors with one key / reachability unverified / no | the two-field schedule | OPEN — owner |
| F4 | C4 pack digest label does not match `candidate.diff` | Codex | confirmed (artifact only) | `pasted-diff.txt` vs `candidate.diff` 89A07DC3… | — | hashes | corrected in §1/§5 |
| F5 | Expiry at exactly 60 min: R-C5 "> 60 min" vs the pinned ">= 60 min" | Codex (tests a1) | open wording conflict | N30; T72 pin | — | N30 | D-1 owner |
| F6 | Pending lock request, logoff schema/cancellation, retire Promise, budget edges, marker reads, the two additions | Codex (tests a1 ambiguities) | consistent with a named reading (diffreview) | N31, N35, N36, N28, N13-N16, N12, N2b | — | pinned tests | D-2..D-7 owner |
| F7 | The brief's M1-doc sections do not exist; the stale lines are in the M1b-A write-up | Claude | confirmed (grep) | §4.2 | — | — | M1b-A doc updated; coordinator agreed |
| F8 | The boundary parser does not read `cm`; no server change needed | Claude (single-model) | confirmed (source read) | EditDraftJournalBoundary.cs:52-80 | — | — | no change |
| F9 | "Mostly deletion" does not hold for the module alone | Claude | confirmed | git numstat | — | — | reported |
| F10 | Refused eviction removal policy unspecified | Codex (tests a1 N17) | open | — | over budget until next write | refused-oldest-removal schedule | D-5 |

Found independently by both: none (Claude's N34b residual and Codex's C1 overlap, but Claude stated it first in the pack).

### Codex calls

| Run / call / attempt | Started | Duration | state | validation | exit | Model / effort req. | Effective effort | Reasoning tokens | Search | MCP tools | activity (commands / non-zero / file_change / outside-repo) | Pack | CLI |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 32c9f1 / tests / a1 | 12:00:06 | 12.0 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 8,686 | off | none | 2 / 0 / 0 / powershell.exe only (both commands read REQUIREMENT.md; no repo path in out.md) | requirement-only (REQUIREMENT.md) | 0.153.4 |
| 32c9f1 / diffreview / a1 | 12:35:14 | 9.2 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 3,980 | off | KB lookup_known_fix, get_fix | 26 / 1 (first NHM git call; retried with safe.directory) / 0 / scratch pack, candidate.diff, REQUIREMENT.md, NHM checkout (read), powershell.exe | v1 | 0.153.4 |

No retries; no failed attempts.

### Setup checks (Phase 0; outputs in the local scratch folder)

| # | Item | Result |
|---|---|---|
| 1 | `BASH_MAX_TIMEOUT_MS` | present (2400000) |
| 2 | read-only query connection (HARD) | not applicable: no database query (brief: no DB) |
| 3 | repo trusted (HARD) | present (the hook fired, item 5) |
| 4 | manifest (HARD) | 7/7 in the main repo and in the worktree |
| 5 | hook fires (HARD) | `git push --dry-run origin HEAD` blocked with the hook's message; a Monitor running `Get-Date` was not blocked |
| 6 | collab.rules | present; plain `git push origin main` -> `forbidden`; the wrapped shape (`pwsh.exe -Command "git push"`) was blocked by the hook on its text (one false positive), not retried |
| 7 | `codex debug prompt-input` | AGENTS.md "Working with Claude (Codex)" present; CLAUDE.md not (pasted as pack item 0) |
| 8 | tool boundary (HARD) | no MCP tool that writes a database, migrates, deploys, pushes or restarts was used; KB write tools unused; claude-in-chrome used read/script only on the dev host |
| 9 | tool parity (HARD) | KB `enabled_tools` = the 9 read-only tools; dxdocs registered; deviation as in earlier runs: `node_repl` and `cua_repl` enabled, forbidden in both prompts, 0 calls |
| 10 | models (HARD) | gpt-6-astra lists low, medium, high, xhigh, max, ultra |
| 11 | run id / scratch / salt / binary | 32c9f1; salt written (unused); codex-cli 0.153.4; login ChatGPT; `codex doctor` overall "warning" |
| 12 | snapshot | HEAD 9dcf84ab, clean at start (the note commit had landed) |
| 13 | policy drift | as in run 991428: agent file Phase 0 item 10 and the cost paragraph say `medium` while rule 11 and the launcher say `xhigh` |
| 14 | web search | off for both calls |

### Redaction

None needed: no database rows; the smoke's log lines carry hashed record and editor ids only; test values are synthetic.

### Inputs Codex did not have

- `Xaf.EditDraft.Core/EditDraftJournalBoundary.cs` and every security section (single-model by the owner's rule): the
  statement that the parser ignores `cm` (F8) and the `|` namespace guard (D-7b) were not cross-checked.
- MEMORY.md (named in the pack; one line pasted).
- The browser smoke and the .NET runs were supplied as output, not re-run by Codex.

### Passes used

2 cross-model passes (`tests` requirement-first; `diffreview` on the frozen candidate). Total Codex calls 2, attempts 2, both
`success` / `ok`.

## 10. Not verified / open questions

- Owner: C1, C2, C3 (§5, unfixed) and D-1..D-7 (§4.3).
- Web Lock lifetime and heartbeat behaviour across back/forward-cache restore, frozen and discarded tabs, and on iPad/Safari;
  the `pageshow` resume (D-7a) is tested only in jsdom.
- The XAF host on a secure origin (smoke part 2 used a synthetic editor on the login page; production is https, the dev LAN
  origin has no Web Locks).
- Whether any production view renders two live editors with one journal key (C3's reachability).
- Origin quota growth with several heavy tabs under the per-load budget (residual R3 of the note); chart-journal interaction
  at quota.
- M3 intake (§8) and the M4 `logoff` call before XAF's sign-out navigation: not built; no server caller of `logoff` or
  `retire` exists.
- Production: deployed bytes and effective `EditDraftCapture:Journal:*` keys not checked (never deployed).

could_not_determine:
- whether the owner accepts the C1 window as an O1-A residual or wants the resume self-repair;
- which eviction outcome the owner wants for C2 (drop the old retry, or keep it at the cost of a newer entry);
- C3's production reachability;
- real-browser lock lifetime across suspension and restoration.
