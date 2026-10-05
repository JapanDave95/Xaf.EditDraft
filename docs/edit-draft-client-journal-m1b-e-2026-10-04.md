# Xaf.EditDraft client-side journal — M1b-E: heartbeat gate on the repair, written key in a failed scan, C# twin (2026-10-04)

Collaborator run `2026-10-04-journal-m1b-e-74b9e0`. Claude: Opus 5.5 (claude-opus-5-5). Codex: gpt-6-astra, `-Effort xhigh`
passed explicitly on both calls (codex-cli 0.153.4): `tests` a1 (requirement-only directory, before any code was shown)
and `diffreview` a1 (pass 2 of 2, frozen candidate). Worktree `CareCrew-journal`, branch `design/edit-draft-client-journal`,
HEAD `191a7f59` (M1b-D committed). No commit, no deploy, no database. Evidence:
a local scratch folder outside the repository (cited `ev/<file>`).

Owner rulings 2026-10-04 (labels verbatim): "Gate the repair on the tab's own heartbeat still existing"; "Yes, run M1b-E
as described" (the gate, DR1, DR2, the C# PlanEviction twin with its two pinned test lines; P21 deferred to M2).

## 0. Combined answer

M1b-E makes three of the four ruled changes and stops the fourth, as the brief directs. G1: a page restored from the
back/forward cache now repairs its keys only when its own heartbeat key exists at the trigger (page show: read before the
heartbeat is rewritten; lock re-grant: read when the grant arrives), and logs each decision in `report().repairLog`. This
keeps a logoff's entries absent without Web Locks and when the cached page keeps its lock. It does not when the browser
drops the cached page's lock: page hide already removes the heartbeat and logoff leaves heartbeats, so the re-grant check
finds the heartbeat the page show just wrote. Both analysts reproduced this. The choice between the per-trigger reading
and Codex's "old intents stay ineligible" reading goes to the owner. G2 fixes DR1 (a written key that a failed read left
out of the post-write scan still competes, with its capture time). G4 aligns the C# `PlanEviction` twin, and its two
pinned lines now expect `["new"]`. G3 was not made, because a hold over every field state also defers a masked server
reply's write in a sibling field (cluster B). DR2 stays open, with a repair-path-only alternative checked in scratch.
Results: js 163 (156 pass, 0 fail, 7 todo), 6/6 mutants killed, .NET 271/42/300, solution build with 0 errors. C1c is
the accepted residual (R3 and P9 marked); P1 is escalated.

## 1. Status

Implemented, uncommitted, 2026-10-04, on top of HEAD 191a7f59: G1, G2, G4. G3 STOPPED (no code change). Not deployed;
the journal switch is off everywhere. The reviewed candidate is frozen in `candidate-a1.json` (local scratch; 5 files;
`git diff HEAD` SHA-256 1887FF89…). Two passes used. This write-up and the status note in
the M1b-D write-up were written after the review and are not cross-reviewed. No browser host was run: no exported name,
key layout or existing result field changed (`report()` gains `repairLog`).

## 2. The change, red-before / green-after per item

| Item | Change | Red on HEAD 191a7f59 | Candidate |
|---|---|---|---|
| G1 | `ownHeartbeatGate(trigger)` reads this load's `XafEditDraft.hb1\|<load>` once; `repairOwn()` runs only when the key exists. Page show: read BEFORE the synchronous heartbeat rewrite; lock re-grant: read when the grant arrives. Absent or unreadable -> no repair; the heartbeat is still written; the page continues. Each decision -> `report().repairLog` (last 20, `{ at, trigger, heartbeat, repaired }`) | E1 (logoff, no Web Locks): entry restored (`ev/m1b-e-repro-red-before-on-HEAD191a7f59.tap`) | E1, Q2-Q11 green; C1a/C1b (R1, R2, P7, P8, Q7/Q8) green; C1c: R3 marked todo (accepted residual), P9 marked with it, Q9 pins the residual |
| G2 | `apply()`: when the post-write scan did not list the key just written (its read failed), it is added with the intent's capture time and its written size before `planEviction` | E2 (DR1): X kept, N00 evicted | E2, Q12-Q17 green |
| G3 | none — STOPPED (section 5) | E3b (DR2 via the lock re-grant) fails | E3b still red (todo, owner) |
| G4 | `EditDraftJournalRules.PlanEviction`: a listed written key competes by At then key with `writtenSize`; an unlisted one is counted and never chosen (= the module's planEviction). Test lines 323 and 339 expect `["new"]` (owner-ruled). CRLF kept | T72, T72b fail against HEAD's twin with the new lines (`ev/g4-sensitivity-test.log`) | 271/271 |

Diff: module +36/−6, `EditDraftJournal.cs` +12/−5, `EditDraftJournalTests.cs` +2/−2, `m1b-d.test.js` +3/−3 (declaration
lines of R3, P9, P1 only; bodies unchanged), new `m1b-e.test.js` (18 tests).

## 3. Verification (artifacts `artifacts/claude-test/m1b-e-74b9e0`, deleted after)

| Check | Result |
|---|---|
| js suite on the frozen candidate (`node --test "test/*.test.js"`) | **163: 156 pass, 0 fail, 7 todo** (`ev/js-green-after.tap`): 145 retained + 18 new. Todo: N34b, P21 (retained); R3, P9, P1 (marked here); E1b, E3b (new, escalated) |
| m1b-e.test.js against the HEAD module | 1 pass (Q13, pins unchanged behaviour), 15 fail, 2 todo (`ev/m1b-e-all-on-HEAD191a7f59.tap`) |
| Regression sensitivity (one mutant per change, module untouched, `JOURNAL_JS`) | 6/6 killed: G1a page-show gate ignored (6 fail), G1b re-grant gate ignored (2), G1c read after the rewrite (7), G1d unreadable = present (1), G1e no log (7), G2 written key not added (5); module SHA-256 838C36A8… before and after (`ev/mutants-m1b-e-run.txt`). G4: HEAD twin compiled in place -> T72, T72b fail; candidate restored byte-identical |
| Codex's C# table Q25/Q27 (temporary NUnit file, deleted) | 5/5 with T72/T72b (`ev/csharp-table-test2.log`); a first run failed on stale binaries (section 8) |
| `dotnet build CareCrew.sln -c Debug --artifacts-path …` | full build exit 0, 0 errors, 2,209 warnings; final incremental build on the frozen bytes exit 0, 0 errors, 107 warnings |
| Xaf.EditDraft.Tests / Sample / Rostering filter `EditDraft\|ChartDraft\|TimeEditor\|JapaneseDateColumn\|StaffOverTimeHoliday` (`--no-build`) | **271/271**, **42/42**, **300/300** |

What these prove: module behaviour in jsdom with the harness's storage, event and fake-lock model; that the C# twin
returns the tabled results; that the solution builds. Not proven: real back/forward-cache behaviour (whether a browser
keeps or drops a cached page's Web Lock), iPad/Safari, the XAF host.

## 4. Reading choices (owner may re-rule)

| # | Point | Implemented |
|---|---|---|
| F-1 | when the gate is read | at each trigger on its own (page show: before its rewrite; re-grant: when the grant arrives). C1a/C1b are then repaired at the re-grant; C1c is not (heartbeat write refused). Codex Q5 names the other reading (section 5, X1) |
| F-2 | "present" | the key exists, whatever its value; a read that throws -> not shown present: no repair, logged `unreadable`, `storageErrors` +1 |
| F-3 | decision log | `report().repairLog`, last 20; `repaired` = the repair ran (not "something was written") |
| F-4 | G2 placement | in `apply()` (the caller); `planEviction`'s documented unlisted fallback unchanged and mirrored in C# |
| F-5 | C# method name | `T72b_..._the_written_entry_never_chosen_...` keeps its name (the ruling covers the two assertion lines); it now names the old rule — follow-up |
| F-6 | red retained tests | R3 accepted residual (brief); P9 ("C1c continued") marked with it on Claude's reading; P1 escalated (X1). Bodies unchanged |

## 5. Escalations to the owner (no executable check decides them)

**X1 — G1: the ruling's premise does not hold in this code, and the gate has two readings.** The ruling says "the logoff
sweep (and any sweep) removes heartbeats". In the module, `logoff(ns)` sweeps only entry keys (retained N35: heartbeats
"stay"), and a page load's OWN `pagehide` removes its own heartbeat (retained N2). Codex reached the same conflict in its
requirement-only Q1 (prompted by Claude's question). Probe `ev/probe-g1-paths.js`:

| Schedule | HEAD 191a7f59 | Candidate |
|---|---|---|
| K1 lock kept while cached, key removed | restored | stays absent (page show: absent; no re-grant) |
| K2 lock dropped while cached, another tab's logoff | restored | restored (page show: absent; re-grant: present -> repair) |
| K3 no Web Locks, another tab's logoff | restored | stays absent |

Consequences:
- After any real page hide, the page-show check finds the heartbeat absent. So the page-show repair no longer runs after
  a real back/forward-cache cycle, and repairs come only from the lock re-grant (P1's page-show variant is red, marked
  todo).
- When the browser drops the cached page's lock, the re-grant check finds the heartbeat the page show just wrote, so a
  logoff's entries come back (E1b, todo). This is the same path that repairs C1a/C1b.
- With the lock kept, expiry and retire cannot remove the page's keys (O1-A needs the lock released). Only a logoff can,
  and the candidate keeps those entries absent.
- Without Web Locks, expiry and retire remove nothing of another load, and the candidate keeps a logoff's entries absent.

The positions:
- Claude's position (implemented): the scope text gates each trigger "at the moment of the persisted pageshow / lock
  re-grant check", and the brief gives the refused heartbeat as the cause of the C1c residual, which is how this reading
  works.
- Codex's position (tests a1 Q5, diffreview finding 1): only a reading in which a skipped repair makes the intents that
  existed at that moment permanently ineligible ("continues normally for NEW input only") keeps a logoff's entries absent
  through the later grant. Under it, C1a/C1b stay repaired (their values are new input), C1c stays unrepaired, E1b turns
  green, and P1 is red in both variants. Codex Q6 and diffreview finding 2 add that pending retries of refused pre-hide
  writes must then not write either. Under F-1 they still can, the same as on HEAD (section 6).
- Other options: stop removing the own heartbeat at page hide and make logoff remove every heartbeat (this changes the
  N2/N35 rules, and expiry and retire then wait for a stale heartbeat, 120 s); or a per-namespace logoff signal (M1b-D
  E-11 option 2).

**G3 — STOPPED.** The brief: "if that changes cluster-B behaviour (openOperation/onMutations) STOP and report instead".
- Probe `ev/probe-g3-clusterb.js`: two masked fields share a key, and field 1's masked keystroke is answered by its
  field-text reply (onMutations -> record).
- HEAD: one write ("5") in each schedule.
- With the hold over every field state (`ev/mutant-g3-allstates.js`): no write, `deferredComposing` +1 and one pending
  write when field 0 composes (S1) or awaits its own mask reply (S2). Typed text (S3) is also deferred.
- Codex reproduced both masked schedules independently in its review. So the change was not made, and DR2 stays open
  (E3b).
- DR2 as reported (no Web Locks) no longer writes, but that is because the G1 page-show gate skips the repair (E3 green),
  not because of G3.
- Scratch-only alternative for the owner (`ev/alt-g3-repair-only.js`, not in the worktree): skip the entry repair in
  `repairOwn` while any field state of the key composes or awaits its reply, and keep the latest-state hold everywhere
  else. With it, S1/S2/S3 match HEAD, E3b is green, and the rest of the suite matches the candidate.

**P1 (todo).** Classified as a conflict between the ruling and a pinned expectation (X1), not revised.

**Status 2026-10-04 (M1b-F, run 2026-10-04-journal-m1b-f-45a751; uncommitted; see docs/edit-draft-client-journal-m1b-f-2026-10-04.md).**
Owner ruling: "Remove the self-repair entirely; keep G2 + G4; accept the C1 window as the residual".
- G1 (heartbeat gate): removed with the self-repair, along with `report().repairLog`. Q2, Q3, Q6, Q7/Q8, Q9, Q10 and Q11
  were deleted. R3, P9 and P1 were deleted with the other repair tests.
- X1, DE1 (section 6): moot. E1, E1b and Q4 pass: a logoff's entries stay absent with and without Web Locks.
- G3 / DR2: moot (no repair path); E3 and E3b were deleted.
- DE2: recorded residual (unchanged HEAD and M1b-C behaviour).
- G2 and G4: kept. F-5 is done (T72b renamed to the new rule).
- js suite: 140 tests, 0 fail, 2 todo (N34b, P21).

## 6. Codex diffreview a1 (pass 2 of 2) — defects reported UNFIXED

Call: success / ok / exit 0, 9.4 min, candidate files unchanged during the review. Codex re-ran the js suite
(163: 156 pass, 0 fail, 7 todo). It confirmed:
- G1 ordering, the unreadable case and the log (Q2/Q3/Q11);
- G2's single addition with `it.at` and the written size;
- the G3 STOP (it reproduced both masked schedules);
- G4 (the same rule as the module, CRLF, only lines 323/339 changed);
- the todo classifications, and that no retained test body changed;
- that `openOperation`, `onMutations`, `COMPOSITION` and the exports are unchanged, and that report() only adds
  `repairLog`;
- that NHM HEAD 0e4f5ac5 has no counterpart.

Rank rule (owner may re-rank): nothing is observed in production (never deployed, switch off). Both findings come from the
reading question in X1.

| ID | Finding (Codex) | Claude's check | Note |
|---|---|---|---|
| DE1 | The lock re-grant restores an entry removed by logoff (E1b; a grant held until after the page show) | the same as Claude's E1b and K2, found before Codex's output was read (`ev/probe-g1-candidate.txt`) | F-1's literal per-trigger gate; owner decides the reading (X1) |
| DE2 | After a skipped repair, a refused pre-hide write still pending (timer, an exhausted allowance then a blur, a pending copy) writes the old intent without new input (six schedules) | reproduced for timer and exhausted-then-blur: "v2" written with its pre-hide capture time on the candidate AND on HEAD (`ev/review-a1-verify-candidate.txt`, `…-HEAD.txt`) | predates the diff (retry rule, D-3); a defect only under Codex's reading |

`could_not_determine` (Codex): which G1 outcome the owner intends, including whether pending pre-restore retries must
become ineligible; real-browser reachability, deployed bytes, production occurrence, configuration; independent .NET runs
(logs inspected, not re-run); database or M3 consequences.

## 7. Deployment

Ships in the CareCrew.Blazor.Server build as the RCL asset `_content/Xaf.EditDraft.Blazor/edit-draft-journal.js`; inert
until `EditDraftCapture:Journal:Enabled` and a type key are set (not set anywhere). The C# twin has no runtime caller. No
NursingHomeManagement counterpart, no schema change, no ChartWorkflowServiceV2, report layout or sync consumer. Open pages
need a reload after a publish.

## 8. Contribution log

### What Claude did

- Phase 0 (below); local scratch folder; KB `lookup_known_fix` (fix-552 only,
  the _Host journal; its rule "key a repair on what the writer intends" unchanged here).
- Wrote `tests\req\REQUIREMENT.md` (the brief's rulings and G1-G4 verbatim, the rules in force, interface facts incl. the
  planned `repairLog`, the reported schedules, harness facts; no source) and launched `tests` a1 first. Claude's prompt
  asked Codex whether rules 7/10/12 are consistent with the G1 premise.
- Turned the M1b-D reproductions into E1-E3b and recorded them red on HEAD 191a7f59 before any change. Ran the G3
  stop probe before implementing anything and stopped G3. Implemented G1, G2, G4. Ran the G1 path probe K1-K3. Wrote
  Q2-Q17 from Codex's list, marked R3/P9/P1 and ran red-before, green-after, six mutants, the G4 sensitivity check, the
  C# table, the build and the three .NET suites. Froze the candidate and reproduced Codex's finding 2.
- Got wrong: the first run of Codex's C# table used stale binaries. When the candidate `EditDraftJournal.cs` was restored
  after the G4 check, Copy-Item kept the older timestamp, so the incremental build skipped Core. Caught when Q25 row 1
  failed; the file was touched (bytes unchanged), rebuilt and rerun (5/5). The final .NET runs came after that rebuild.

### What ChatGPT (Codex) did

- `tests` a1 (13.6 min, requirement-only): Q1-Q29.
  - Q1: the premise conflict (prompted).
  - Q5: the second G1 reading and the logoff-with-locks schedule (unprompted).
  - Q6: the "new input only" consequences for pending retries.
  - Q13: a failed read of another key is not compensated.
  - Q22/Q23: the concrete cluster-B STOP schedules.
  - Q25/Q27: the C# table.
- `diffreview` a1 (9.4 min): re-ran the suite and probes, found DE1 and DE2, and confirmed G2, G3-stop, G4, the todos, the
  unchanged surfaces and the NHM mirror.
- No file changes (0 `file_change` in both streams); no web search; MCP: KB `lookup_known_fix` once (diffreview).

### Found issues, by tool

| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| F1 | G1 premise: sweeps do not remove heartbeats; the own page hide does (N2, N35) | Claude (source read); Codex Q1 (prompted) | confirmed | `J:934`, `sweepPrefix`, N2/N35 | the gate cannot tell a logoff from a normal hide / every restore / source + harness / no | probe K1-K3 | OPEN — owner (X1) |
| F2 | A logoff's entries come back through the lock re-grant when the cached page's lock was dropped | both, independently (Claude E1b/K2; Codex Q5, then diffreview DE1) | confirmed | `ev/probe-g1-candidate.txt`, E1b | logoff not kept / lock dropped while cached / harness / no | E1b | OPEN — owner (X1) |
| F3 | P1's page-show variant red under the gate | Claude | confirmed | `ev/js-first-run-candidate.tap` | repair moves to the re-grant / every real hide / harness / no | P1 | ESCALATED (todo) |
| F4 | A hold over every field state defers a masked reply's write (cluster B) | Claude (probe); Codex reproduced in review | confirmed | `ev/probe-g3-*.txt` | G3 stop condition met / shared key + masked reply / harness / no | probe S1/S2 | G3 STOPPED — owner |
| F5 | Pending pre-hide retries write after a skipped repair | Codex (Q6, DE2) | confirmed, predates the diff | `ev/review-a1-verify-*.txt` | old value after logoff / refused write pending across hide / harness / no | timer / blur schedule | OPEN — owner (with X1) |
| F6 | DR1 | Codex (M1b-D) | fixed | E2, Q12-Q17 | — | E2 | FIXED |
| F7 | C# twin states the old rule | Claude (M1b-D) | fixed | T72, T72b | — | G4 sensitivity | FIXED |
| F8 | T72b's method name still names the old rule | Claude | confirmed | `EditDraftJournalTests.cs:329` | naming only / — / source / no | source read | follow-up |
| F9 | A failed read of another key leaves 61 keys (not compensated) | Codex (Q13) | confirmed behaviour | Q13 | over the count by one / rare / harness / no | Q13 | pinned as is (G2 covers the written key only) |
| F10 | C1c (heartbeat write refused) not repaired | brief | by design | Q9 | — | Q9 | ACCEPTED residual |

Found independently by both: F2 (Claude's E1b was written and run before Codex's tests output was read).

### Codex calls

| Run / call / attempt | Started | Duration | state | validation | exit | Model / effort req. | Effective effort | Reasoning tokens | Search | MCP | activity (commands / non-zero / file_change / outside-repo) | Pack | CLI |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 74b9e0 / tests / a1 | 14:11:01 | 13.6 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 11,047 | off | none | 2 / 0 / 0 / powershell.exe only; out.md names only REQUIREMENT.md | requirement-only (REQUIREMENT.md) | 0.153.4 |
| 74b9e0 / diffreview / a1 | 14:32:36 | 9.4 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 4,505 | off | KB lookup_known_fix | 23 / 2 (NHM git without safe.directory, retried with it; a probe path error, retried) / 0 / scratch evidence (read), NHM checkout (read), powershell.exe | v1 | 0.153.4 |

No retries; no failed attempts. The watcher's "UNC paths" for diffreview are
regex text in a probe command, not paths.

### Setup checks (Phase 0; outputs in the local scratch folder)

| # | Item | Result |
|---|---|---|
| 1 | `BASH_MAX_TIMEOUT_MS` | present (2400000) |
| 2 | read-only query connection (HARD) | not applicable: no database query (brief: no DB) |
| 3 | repo trusted (HARD) | present (the hook fired, item 5) |
| 4 | manifest (HARD) | 7/7 in the worktree and in the main repo (`manifest.txt`) |
| 5 | hook fires (HARD) | `git push --dry-run origin HEAD` blocked with the hook's message; a Monitor running `Get-Date` was not blocked |
| 6 | collab.rules | present; the `codex execpolicy check` command was blocked by the hook on its text (its arguments name a push); not rephrased, not run (hook false positive) |
| 7 | `codex debug prompt-input` | AGENTS.md present with "Working with Claude"; CLAUDE.md content not (pasted in the diffreview pack) |
| 8 | tool boundary (HARD) | no write-capable MCP tool used; KB write tools unused |
| 9 | tool parity (HARD) | KB `enabled_tools` = the 9 read-only tools; dxdocs registered; `node_repl`, `cua_repl` enabled (as before), forbidden in both prompts, 0 calls |
| 10 | models (HARD) | gpt-6-astra lists low, medium, high, xhigh, max, ultra |
| 11 | run id / scratch / salt / binary | 74b9e0; salt written (unused); codex-cli 0.153.4; login ChatGPT; `codex doctor` overall "warning" |
| 12 | snapshot | HEAD 191a7f59, clean at start |
| 13 | policy drift | as in earlier runs: agent file Phase 0 item 10 and the cost paragraph say `medium` while rule 11 and the launcher say `xhigh`; Codex's global configuration has `model_reasoning_effort = "medium"` (the launcher passes `xhigh`) |
| 14 | web search | off for both calls |

### Redaction

None needed: no database rows, no logs; test values are synthetic.

### Inputs Codex did not have

- `tests` a1: only REQUIREMENT.md (by design).
- `diffreview` a1: MEMORY.md (named, one line pasted); the .NET runs and builds were supplied as output, not re-run;
  `EditDraftJournalBoundary.cs` and security sections (excluded; not touched).

### Passes used

2 cross-model passes (`tests` requirement-first; `diffreview` on the frozen candidate). Total Codex calls 2, attempts 2,
both `success` / `ok`.

## 9. Not verified / open questions

- Owner: X1 (the G1 premise and the reading: per-trigger, implemented, vs "old intents stay ineligible", Codex), with
  E1b, P1 and DE2 following from it; the G3 stop and the repair-path-only alternative; P9's classification; F8 rename.
- Real browsers: whether a cached page keeps or drops its Web Lock (decides whether K1 or K2 applies), frozen and
  discarded tabs, iPad/Safari; the XAF host on a secure origin; no browser run in this pass.
- M3 intake and the M4 `logoff` call: not built.

could_not_determine:
- which G1 outcome the owner intends (Codex and Claude);
- the real-browser lock behaviour across the back/forward cache;
- whether DR2's shared-field composition or DE2's pending retry across a hide occur in production views.
