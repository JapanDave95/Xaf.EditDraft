# Xaf.EditDraft client-side journal — M1b-D: resume self-repair, evictable written key, state per field (2026-10-04)

Collaborator run `2026-10-04-journal-m1b-d-2d52b6`. Claude: Opus 5.5 (claude-opus-5-5). Codex: gpt-6-astra, `-Effort xhigh`
passed explicitly on both calls (codex-cli 0.153.4): `tests` a1 (requirement-only directory, before any code was shown)
and `diffreview` a1 (pass 2 of 2, frozen candidate). Worktree `CareCrew-journal`, branch `design/edit-draft-client-journal`,
HEAD `865f8b10` (M1b-C committed). No commit, no deploy, no database. Evidence:
a local scratch folder outside the repository (cited `ev/<file>`).

Owner ruling 2026-10-04 (labels verbatim): "Commit M1b-C now; one bounded pass for C1 (pageshow re-check), C2
(just-written key is evictable), C3 (state per field), Codex re-check"; "Accept all seven" for D-1..D-7 (unchanged here).

## 0. Combined answer

M1b-D repairs the three M1b-C defects inside the browser module only. A page load restored from the back/forward cache
re-checks the keys it last wrote, on the persisted page show and again when its re-requested Web Lock is granted, and
writes back the current intent of any key that is missing or replaced (C1); the key just written now competes in the
per-load eviction by capture time then key, so an old retry evicts itself (C2); every field state of a shared key is
kept, so a clear resets all of them (C3). Codex's five reproductions were red on HEAD 865f8b10 and are green, with 26
more tests from Codex's requirement-only expectations (js 145: 143 pass, 0 fail, 2 todo), seven single-fix mutants all
killed, .NET 271/42/300 and a 0-error solution build. Codex's review found two defects, both reproduced by Claude and
left UNFIXED: one failed read right after the write brings the written key's exemption back (DR1), and the repair can
write a shared key's entry while an earlier field of that key is composing (DR2). For the owner: the repair also brings
back a hidden tab's entries after another tab's logoff (new and automatic), the P21 sibling overwrite (red on HEAD too),
and the C# `PlanEviction` twin that still states the old exemption; the remover-side window stays the accepted residual.

## 1. Status

Implemented, uncommitted, 2026-10-04, on top of HEAD 865f8b10. Not deployed; the journal switch is off everywhere. The
reviewed candidate is frozen in `candidate-a1.json` (local scratch; 3 files; `git diff HEAD` SHA-256 09C3661F…).
Two passes used. Open for the owner: DR1, DR2 (§5), E-11 logoff, P21, the C# twin (§4). This
write-up and the status note in the M1b-C write-up were written after the review and are not cross-reviewed. No browser
host was run: the exported API, the result shapes and the key layout are unchanged.

## 2. The change, red-before / green-after per item

Module `Xaf.EditDraft.Blazor/wwwroot/edit-draft-journal.js` only (+89 / −23); `m1b-c.test.js` +1 / −1 (N34b text); new
`m1b-d.test.js` (31 tests). `openOperation` and `onMutations` are
unchanged (Codex compared them with HEAD).

| Item | Change | Red on HEAD 865f8b10 | Green on the candidate |
|---|---|---|---|
| C1 | new `repairOwn()`: for each intent log of this load, pending work is applied at once (like a retry); otherwise a value intent younger than 60 min whose key is missing, unparseable or holds another `seq`/`val` than this load's last write is written again through `apply` (capture time kept). Called by `resumeLiveness` (persisted `pageshow`, after the heartbeat) and in the grant callback of the lock re-request (`requestLock(true)`). `removeKey` marks this load's own deliberate removals (`dropped`: evicted, expired, retired) so they are never written back | R1, R2, R3 fail (`ev/m1b-d-repro-red-before-on-HEAD865f8b10.tap`) | R1-R3 pass; P1-P9 pass |
| C2 | `planEviction`: when the written key is among the scanned items it competes with its own `at` and size (order `evictionOrder`: at, then key); a self-evicted key stays recorded as written (no rewrite by a re-read) and is marked `dropped` | R4 fails | R4, P10-P16 pass |
| C3 | `stateByKey`: entry key -> `Set` of field states (one per field); `refreshStates` (clear, logoff) walks them all; `latestState()` gives the composition hold and own-retire pending check exactly what the old single-valued map held | R5 fails | R5, P17-P20, P21a, P22, P23 pass |
| N34b | title and todo text only: the remover cannot close the window; a resumed writer repairs on page show and lock re-acquisition; the test has no writer page | — | todo (unchanged body) |

The whole new file on HEAD: 31 tests, 4 pass (P19, P21a, P22, P23 — behaviour HEAD already had), 26 fail, 1 todo
(`ev/m1b-d-all-on-HEAD865f8b10.tap`).

## 3. Verification (artifacts `artifacts/claude-test/m1b-d-2d52b6`, deleted after)

| Check | Result |
|---|---|
| js suite on the frozen candidate (`node --test "test/*.test.js"`) | **145: 143 pass, 0 fail, 2 todo** (N34b, P21) (`ev/js-green-after-frozen.tap`); 114 retained + 31 new |
| Regression sensitivity (one mutant per fix, module file untouched, `JOURNAL_JS`) | 7/7 killed: D1 no repair (14 fail), D2 no repair on lock grant (6), D3 no `dropped` mark (3), D4 sequence ignored (1), D7 pending work skipped (1), D5 written key exempt again (9), D6 one state per key again (4); module SHA-256 9AF24CB1… before and after (`ev/mutants-m1b-d-run.txt`) |
| `dotnet build CareCrew.sln -c Debug --artifacts-path …` | full build exit 0, 0 errors, 2,209 warnings; rebuild after the last module edit exit 0, 0 errors, 1 warning (NETSDK1086, incremental) (`ev/build-sln.log`) |
| Xaf.EditDraft.Tests / Sample / Rostering filter `EditDraft\|ChartDraft\|TimeEditor\|JapaneseDateColumn\|StaffOverTimeHoliday` (`--no-build`) | **271/271**, **42/42**, **300/300** |

What these prove: module behaviour in jsdom with the harness's storage, event and fake-lock model; that the .NET side
(T72 pins included) still builds and passes. Not proven: real back/forward-cache lock and storage behaviour, iPad/Safari,
the XAF host. The .NET suites do not exercise the changed eviction rule (the C# twin still states the old one, §4).

## 4. Reading choices and escalations

Where Codex's `tests` a1 named two readings, Claude implemented one and pinned it (owner may re-rule; Codex judged each
consistent with a named reading, with the two caveats marked):

| # | Point (Codex P-id) | Implemented |
|---|---|---|
| E-1 | stale predicate (P2) | missing, unparseable, other `seq` or `val` -> rewrite the CURRENT intent; same `seq`+`val` with another `at` -> left; a read that throws -> left, `storageErrors` +1 |
| E-2 | refused work at a trigger (P2, P6) | applied at once; retry allowance not renewed |
| E-3 | invalidation (P3, P15) | never written back: own evicted (incl. self-eviction), expired, retired values, expired intents; a newer value after a retire is |
| E-4 | open-composition copy (P4) | a missing copy of an open composition is rewritten; a finished one's is not. Caveat: DR2 |
| E-5 | triggers (P5) | persisted `pageshow` (also without Web Locks, failing query, refused heartbeat) and the grant of the re-requested lock; not a non-persisted show, not the first (late) grant, not storage events |
| E-6 | removal after the last re-check (P7) | stays until the next named trigger (accepted remover-side residual) |
| E-7 | ties, oversize (P11, P12) | ties by key (ordinal), the written key included; a sole oversize entry evicts itself. Caveat: DR1 |
| E-8 | rewrite of an existing key (P14) | counts once; if it is the oldest and the budget is exceeded, the key itself goes, with its older stored value |
| E-9 | shared-key pending (P22) | per-key intent log: a refused write from any field keeps `retire` (`pending`); the field-state part reads the latest state (unchanged) |
| E-10 | refused self-eviction removal (P16) | D-5 unchanged: reported, over budget, no retry |

Escalated to the owner (no executable check decides them; not changed):
- **E-11 logoff, new consequence of C1.** `logoff(ns)` in tab A while tab B is in the back/forward cache; B restored with
  no new input -> B writes its entry back with its capture time. HEAD leaves it absent (`ev/logoff-restore-candidate.txt`,
  `ev/logoff-restore-HEAD.txt`; Codex reproduced it too). The page cannot tell a logoff sweep from the C1 removal (O2: no
  notice). Claude first called this the D-3 residual class; Codex: it is a NEW, automatic restoration. Options: accept;
  a per-namespace logoff signal checked before repair (a cross-tab signal again); repair only values written after the
  resume (then C1c is not repaired). The same applies to an entry another tab retired while B was gone (replay; M3 must
  tolerate replays, M1b-C §8 item 10) — Claude's statement, not separately checked by Codex.
- **P21 (todo).** Two shared fields that both acted re-record their own text on blur and overwrite each other; after a
  retire the sibling writes "one" and the retired "two" follows. Red on the candidate and on HEAD alike (seq 4, "two").
  Outside C3 ("nothing else about key identity changes"); classified faulty-for-scope / ambiguous. Kept with its
  assertions unchanged as a todo; P21a pins the in-scope case. Codex: pre-existing, not shown invalid, owner decides.
- **C# twin.** `EditDraftJournalRules.PlanEviction` (Core, no runtime caller) still says "The written entry itself is
  never chosen", pinned by `Xaf.EditDraft.Tests/EditDraftJournalTests.cs:323` and `:339`. Aligning it revises those test
  lines (owner).

## 5. Codex diffreview a1 (pass 2 of 2) — defects reported UNFIXED

Call: success / ok / exit 0, 10.1 min, candidate unchanged during the review. Codex re-ran the js suite
(145: 143 pass, 0 fail, 2 todo), compared `openOperation`, `onMutations`, exported names, key construction and API bodies
with HEAD (unchanged), confirmed only N34b's declaration line changed in m1b-c, and found no NHM counterpart (NHM HEAD
0e4f5ac5). Claude reproduced both defects (`ev/review-a1-verify.js`; `…-candidate.txt`, `…-HEAD.txt`).

Rank rule (owner may re-rank): nothing is observed in production (never deployed, switch off); both are reproduced by an
executed check.

| ID | Finding (Codex) | Claude's check | Smallest change that would address it (NOT made) |
|---|---|---|---|
| DR1 | One failed `getItem(X)` in the post-write scan drops X from the items; `planEviction` then treats X as unlisted and exempt again, and the newest-but-oldest N00 is evicted (C2 regresses under a read failure; not D-5) | candidate: read ok -> X absent, newer missing []; one read fails -> X kept, N00 missing (same as HEAD's normal result) | in `apply`, add the written key to the items with `it.at` and its size when the scan did not list it (or pass `it.at` to `planEviction`) |
| DR2 | Repair writes the shared key's entry while an EARLIER-created field of that key is composing: `apply`'s composition hold reads only `latestState()` | candidate, field 0 composing: entry "complete" written once, copy "incomplete"; field 1 composing: no entry write; HEAD: no entry write (no repair path) | composition hold over every state of the key (`incompleteNow` walks the set), or skip entry repair while the key's copy is pending; the guard's one-state limit predates M1b-D (also reachable by a sibling's blur) |

`could_not_determine` (Codex): production occurrence, deployed bytes and configuration; real-browser reachability of the
transient read failure and of shared-field composition across a restore; the owner's disposition of P21; the .NET and
build results (supplied, not re-run); M3 intake, security and database effects (out of scope).

**Status 2026-10-04 (M1b-E, run 2026-10-04-journal-m1b-e-74b9e0; uncommitted; see docs/edit-draft-client-journal-m1b-e-2026-10-04.md):**
- DR1: fixed in M1b-E (G2). A written key that a failed read left out of the post-write scan is added with its capture
  time and size; E2 and Q12-Q17 are green.
- DR2: NOT fixed. G3 stopped under the brief's condition, because a hold over every field state also defers a masked
  server reply's write in a sibling field (cluster B). It stays open for the owner, with a scratch-checked
  repair-path-only alternative. The DR2 schedule as reported (no Web Locks) no longer writes because of the G1 gate;
  through the lock re-grant it still does (E3b, todo).
- E-11 logoff (section 4): partly closed by the G1 heartbeat gate. The entries stay absent without Web Locks and when the
  cached page keeps its lock. They are still restored when the browser drops the cached page's lock (E1b, todo), because
  page hide removes the page's own heartbeat and logoff leaves heartbeats. Escalated (M1b-E X1), with Codex's second
  reading.
- C1c (R3, P9): accepted residual under the gate (marked todo; Q9 pins it).
- C# twin (section 4): fixed in M1b-E (G4). Lines 323 and 339 expect `["new"]`.
- P21: unchanged, deferred to M2.

**Status 2026-10-04 (M1b-F, run 2026-10-04-journal-m1b-f-45a751; uncommitted; see docs/edit-draft-client-journal-m1b-f-2026-10-04.md).**
Owner ruling: "Remove the self-repair entirely; keep G2 + G4; accept the C1 window as the residual".
- C1 (the self-repair added by this pass, `repairOwn`): removed. A restored page only rewrites its heartbeat and requests
  its lock (M1b-C behaviour). The C1 window is the accepted residual (N34b todo). R1-R3, P1-P9 and P29 were deleted.
- DR1: stays fixed (M1b-E G2). DR2: moot (no repair path).
- E-11 logoff: closed. The entries stay absent after a restore with and without Web Locks.
- C2, C3 and E-7..E-10: unchanged; E-1..E-6 (repair readings): moot.
- C# twin: stays aligned (G4); T72b renamed to the new rule.
- P21: unchanged, deferred to M2.

## 6. Deployment

Ships in the CareCrew.Blazor.Server build as the RCL asset `_content/Xaf.EditDraft.Blazor/edit-draft-journal.js`; inert
until `EditDraftCapture:Journal:Enabled` and a type key are set (not set anywhere). No NursingHomeManagement counterpart,
no schema change, no ChartWorkflowServiceV2, report layout or sync consumer. Open pages need a reload after a publish.

## 7. Contribution log

### What Claude did

- Phase 0 (below); local scratch folder; KB `lookup_known_fix` (fix-419, 552, 376;
  fix-552's rule "key a repair on what the writer intends" followed: the repair writes the intent log's current intent).
- Wrote `tests\req\REQUIREMENT.md` (the brief's scope verbatim, M1b-C rules, interface facts, reported schedules, harness
  facts; no source) and launched `tests` a1 first; turned Codex's M1b-C reproductions into R1-R5 and recorded them red on
  HEAD 865f8b10 before any change; implemented C1-C3; wrote P1-P29 from Codex's list; ran red-before (HEAD copy via
  `JOURNAL_JS`), green-after, 7 mutants, the build and three .NET suites; froze the candidate; reproduced DR1, DR2 and E-11.
- Got wrong or left open: the unlisted-key fallback in `planEviction` (DR1); relying on `latestState()` for the
  composition hold on the new repair path (DR2); first described the logoff restoration as an existing residual class.

### What ChatGPT (Codex) did

- `tests` a1 (8.7 min, requirement-only): P1-P30 with setups and outcomes; named every ambiguity that became E-1..E-10,
  and flagged that the C1 schedules need a post-removal trigger and that P21 depends on an unspecified invalidation rule.
- `diffreview` a1 (10.1 min): re-ran the suite, ran probes, found DR1 and DR2, assessed E-1..E-10, P21, the C# twin and
  the logoff observation (reproduced; qualified it as a new consequence), checked cluster B, API and NHM.
- No file changes (0 `file_change` in both streams); no web search; MCP: KB `lookup_known_fix` once (diffreview).

### Found issues, by tool

| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| F1 | C1 resumed writer's value lost to a foreign removal pass | Codex (M1b-C review) | fixed | R1-R3 red -> green | text lost / resume during cleanup / harness / no | R1-R3, P1-P9 | FIXED (remover window = residual) |
| F2 | C2 old retry evicts a newer entry | Codex (M1b-C review) | fixed | R4 red -> green | newer text evicted / late retry at full budget / harness / no | R4, P10-P16 | FIXED except DR1 |
| F3 | C3 clear resets one of two shared-key fields | Codex (M1b-C review) | fixed | R5 red -> green | discarded text returns / two live editors one key / harness / no | R5, P17-P23 | FIXED |
| F4 | DR1 failed post-write read restores the exemption | Codex | confirmed | `ev/review-a1-verify-candidate.txt` | newer text evicted / read failure at full budget / harness / no | DR1 schedule | OPEN — owner |
| F5 | DR2 repair writes the entry during an earlier sibling's composition | Codex | confirmed | same | completed value written mid-composition / shared key + composition + restore / harness / no | DR2 schedule | OPEN — owner |
| F6 | E-11 logoff then restore writes the entry back | Claude (observation); Codex reproduced and re-classified as new | confirmed | `ev/logoff-restore-*.txt` | entries outlive a logoff until expiry / tab cached during another tab's logoff / harness / no | logoff schedule | OPEN — owner (escalated) |
| F7 | P21 sibling overwrite after retire | Codex (P21 expectation); Claude found it red on candidate and HEAD | confirmed pre-existing | P21 todo; HEAD run | retired value rewritten / two acted shared fields / harness / no | P21 | OPEN — owner (escalated) |
| F8 | C# `PlanEviction` twin states the old exemption | Claude | confirmed by Codex | EditDraftJournal.cs:181-200; tests :323, :339 | documentation/test divergence, no runtime caller | source read | OPEN — owner |
| F9 | Ambiguities of C1-C3 (stale predicate, triggers, invalidation, ties, pending) | Codex (tests a1) | readings chosen | E-1..E-10 | — | pinned tests | owner may re-rule |

Found independently by both: none.

### Codex calls

| Run / call / attempt | Started | Duration | state | validation | exit | Model / effort req. | Effective effort | Reasoning tokens | Search | MCP | activity (commands / non-zero / file_change / outside-repo) | Pack | CLI |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2d52b6 / tests / a1 | 13:24:28 | 8.7 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 4,885 | off | none | 1 / 0 / 0 / powershell.exe only; no repo path in out.md | requirement-only (REQUIREMENT.md) | 0.153.4 |
| 2d52b6 / diffreview / a1 | 13:46:24 | 10.1 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 5,699 | off | KB lookup_known_fix | 19 / 2 (NHM git without safe.directory, retried with it; one hash path) / 0 / REQUIREMENT.md, NHM checkout (read), powershell.exe | v1 | 0.153.4 |

No retries; no failed attempts.

### Setup checks (Phase 0; outputs in the local scratch folder)

| # | Item | Result |
|---|---|---|
| 1 | `BASH_MAX_TIMEOUT_MS` | present (2400000) |
| 2 | read-only query connection (HARD) | not applicable: no database query (brief: no DB) |
| 3 | repo trusted (HARD) | present (the hook fired, item 5) |
| 4 | manifest (HARD) | 7/7 in the worktree and in the main repo (`manifest.txt`) |
| 5 | hook fires (HARD) | `git push --dry-run origin HEAD` blocked with the hook's message; a Monitor running `Get-Date` was not blocked |
| 6 | collab.rules | present; `git push origin main` -> `forbidden`; the wrapped shape was not re-checked (the hook blocked it on its text last run) |
| 7 | `codex debug prompt-input` | AGENTS.md "Working with Claude (Codex)" present; CLAUDE.md not (pasted in the diffreview pack) |
| 8 | tool boundary (HARD) | no MCP tool that writes a database, migrates, deploys, pushes or restarts was used; KB write tools unused |
| 9 | tool parity (HARD) | KB `enabled_tools` = the 9 read-only tools; dxdocs registered; `node_repl` and `cua_repl` enabled (as in earlier runs), forbidden in both prompts, 0 calls |
| 10 | models (HARD) | gpt-6-astra lists low, medium, high, xhigh, max, ultra |
| 11 | run id / scratch / salt / binary | 2d52b6; salt written (unused); codex-cli 0.153.4; login ChatGPT; `codex doctor` overall "warning" |
| 12 | snapshot | HEAD 865f8b10, clean at start |
| 13 | policy drift | as in runs 991428 / 32c9f1: agent file Phase 0 item 10 and the cost paragraph say `medium` while rule 11 and the launcher say `xhigh` |
| 14 | web search | off for both calls |

### Redaction

None needed: no database rows, no logs; test values are synthetic.

### Inputs Codex did not have

- `tests` a1: only REQUIREMENT.md (by design).
- `diffreview` a1: MEMORY.md (named, one line pasted); the .NET runs and build were supplied as output, not re-run;
  `EditDraftJournalBoundary.cs` and security sections (excluded; not touched by this change). Claude's replay statement
  in E-11 (retire while gone) was not separately checked.

### Passes used

2 cross-model passes (`tests` requirement-first; `diffreview` on the frozen candidate). Total Codex calls 2, attempts 2,
both `success` / `ok`.

## 8. Not verified / open questions

- Owner: DR1, DR2 (§5); E-11 logoff restoration, P21, the C# twin (§4); E-1..E-10 may be re-ruled.
- Real browsers: Web Lock release and re-grant across back/forward-cache restore, frozen and discarded tabs, iPad/Safari;
  whether a restored page's lock grant comes before or after another tab's removal in practice (the harness grants after).
- The XAF host on a secure origin; no browser run in this pass.
- M3 intake and the M4 `logoff` call: not built.

could_not_determine:
- whether the owner accepts that a restored tab writes back entries removed by another tab's logoff or retire;
- whether DR1's read failure and DR2's shared-field composition occur in production views;
- real-browser ordering of the re-request grant against a remover's last read.
