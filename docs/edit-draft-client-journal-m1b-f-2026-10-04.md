# Xaf.EditDraft client-side journal — M1b-F: self-repair removed; M1b closing summary (2026-10-04)

Collaborator run `2026-10-04-journal-m1b-f-45a751`. Claude: Opus 5.5 (claude-opus-5-5). Codex: gpt-6-astra, `-Effort xhigh`
(codex-cli 0.153.4), one `diffreview` call on the frozen candidate (pass 2 of 2 for M1b-F, as the brief numbers it; no
`tests` call — this pass only deletes). Worktree `CareCrew-journal`, branch `design/edit-draft-client-journal`, HEAD
`191a7f59` (M1b-D committed) with the uncommitted M1b-E state. No commit, no deploy, no database. Evidence:
a local scratch folder outside the repository (cited `ev/<file>`).

Owner ruling 2026-10-04 (label verbatim): "Remove the self-repair entirely; keep G2 + G4; accept the C1 window as the
residual".

## 0. Summary

The back/forward-cache self-repair added in M1b-D and gated in M1b-E is gone. A restored page again only rewrites its
heartbeat and requests its Web Lock (the M1b-C text, byte for byte in those functions). G2 (DR1: a written key that a
failed read left out of the post-write scan still competes for eviction) and G4 (the C# `PlanEviction` twin, its two
updated assertions and T72b renamed to the new rule) stay. The 23 repair and gate tests were deleted. A logoff's entries
now stay absent after a restore with or without Web Locks (E1, E1b, Q4 green by construction). Results: js 140 (138 pass,
0 fail, 2 todo: N34b, P21), .NET 271/42/300, solution build with 0 errors. Codex's review found no defects. The C1 window
and DE2 are recorded residuals.

## 1. Status

Implemented, uncommitted, 2026-10-04. Not deployed; the journal switch is off everywhere. Frozen candidate
`candidate-a1.json` (local scratch; 6 files; `git diff HEAD` SHA-256 963F0DD0…). This write-up and
the status notes in the M1b-E and M1b-D write-ups were written after the review and are not cross-reviewed. No browser
host was run (no exported name or key layout changed; `report()` loses `repairLog`, which nothing consumed).

## 2. What was removed and what was kept

Module `Xaf.EditDraft.Blazor/wwwroot/edit-draft-journal.js` (CRLF kept):
- Removed: `repairOwn()`, `ownHeartbeatGate()`, `repairLog` and its `report()` field. The `again` parameter of
  `requestLock` and its grant-time repair, the repair call in `resumeLiveness`, and the `dropped` marking in `removeKey`
  (only `repairOwn` read it) are also gone.
- `requestLock`, `resumeLiveness`, `removeKey` and `report()` are the M1b-C (865f8b10) text again.
- Kept: M1b-D C2 (the written key competes in `planEviction`), M1b-D C3 (`stateByKey` holds every field state), M1b-E G2.
  The header comment was rewritten to say so.
- `git diff 865f8b10` of the module now shows only C2, C3, G2 and the header.

C#: G4 unchanged from M1b-E. T72b was renamed to
`T72b_the_size_budget_alone_and_with_the_count_the_written_entry_competes_by_at_then_key_and_the_60_minute_boundary`
(body unchanged). CRLF kept.

Tests deleted (whole blocks, `ev/delete-repair-tests.js`, `ev/delete-repair-tests-run.txt`):
- `m1b-d.test.js` (14): R1, R2, R3, P1, P2, P3, P3b, P4, P5, P6, P7, P8, P9, and P29. P29's ownership audit asserted a
  re-check write-back (`n1` deleted, page show, `n1` expected back) and failed once the repair was removed.
- `m1b-e.test.js` (9): Q2, Q3, Q6, Q7/Q8, Q9, Q10, Q11 (heartbeat gate); E3, E3b (DR2, the repair path's composition hold).
- Now-unused helpers were removed: LEGACY, LOCK, MIN, decode, hide (m1b-d); setHeartbeat, restoredWithGrantHeld,
  lastDecision (m1b-e).

Kept tests that changed, and why:

| Test | Change | Why |
|---|---|---|
| m1b-e E1 | the `repairLog` decision assertion removed; title "no self-repair" | the field no longer exists; its other assertions are unchanged |
| m1b-e E1b | `todo` removed; title rewritten | it passes now: nothing writes a removed entry back |
| m1b-e Q4, Q12 | title wording only ("(logoff)"; "a persisted page show" instead of "a repair") | the repair no longer exists |
| m1b-c N34b | title and todo text: "no self-repair; remover-side window accepted" | as the brief asked; body unchanged |
| m1b-d header | a note on the deletion | — |

## 3. Verification (artifacts `artifacts/claude-test/m1b-f-45a751`, deleted after)

| Check | Result |
|---|---|
| js suite on the frozen candidate | **140: 138 pass, 0 fail, 2 todo** (N34b, P21) (`ev/js-green-after.tap`); 163 − 23 deleted |
| js suite before the deletion (module already without the repair) | 163: 137 pass, 19 fail, 7 todo. The 19: R1, R2, P2–P8 (P3b incl.), P29, Q2, Q3, Q6, Q7/Q8, Q9, Q10, Q11, and E1 (only its `repairLog` line). E1b and E3b passed (`ev/js-before-test-deletion.tap`) |
| m1b-e.test.js on HEAD 191a7f59 (with repair) / on M1b-C 865f8b10 (no repair, old exemption) | E1, E1b, Q4 fail / pass; the G2 tests fail on both (`ev/m1b-e-on-*.tap`) |
| G2 mutant (written key not added) | killed, 5 fail; module hash unchanged (`ev/mutants-m1b-f-run.txt`) |
| `dotnet build CareCrew.sln -c Debug --artifacts-path …` | exit 0, 0 errors, 2,209 warnings |
| Xaf.EditDraft.Tests / Sample / Rostering (filtered) | **271/271** (T72b passes under its new name), **42/42**, **300/300** |

## 4. Status of each open item

| Item | Status after M1b-F |
|---|---|
| G3 / DR2 (repair writes a shared key during a sibling's composition) | moot: no repair path |
| DE1 (the lock re-grant restores a logoff's entry) | moot: no repair at the re-grant; E1, E1b, Q4 green |
| X1 (G1 premise and its two readings) | moot: no gate |
| E-11 logoff restoration (M1b-D) | closed: a logoff's entries stay absent after a restore, with and without Web Locks |
| DE2 (a refused write still pending across a page hide writes its old value after another tab's logoff, by timer or a later blur) | residual: unchanged HEAD and M1b-C behaviour (Codex re-ran it on all three); D-3 "a tab still open may write again afterwards" |
| C1 window (a value removed by another tab's expiry or retire that raced the restore, or written into the remover's read-then-remove window) | accepted residual (ruling); N34b todo |
| M1b-D reading choices E-1..E-6 (repair) | moot; E-7..E-10 (C2, C3, D-5) stand |
| M1b-E reading choices F-1..F-3, F-6 | moot; F-4 (G2 placement) stands; F-5 done (T72b renamed) |
| P21 | todo, deferred to M2 (unchanged) |

## 5. Codex diffreview a1 — defects: none

Call: success / ok / exit 0, 7.6 min, candidate files unchanged. Codex re-ran the js suite (140: 138 pass,
0 fail, 2 todo), removed G2 in memory (5 failures), probed restores (no repair writes), reproduced DE2 on the candidate,
HEAD and M1b-C, and found no NHM counterpart (NHM HEAD 0e4f5ac5). Its output: `defects: []`.

`could_not_determine` (Codex): real-browser reachability, deployed bytes, production occurrence, configuration; independent
.NET results (supplied, not re-run); exact preservation of the untracked `m1b-e.test.js` bodies against their pre-M1b-F
versions (the earlier snapshot was not supplied: Claude's notes list the changes, and the M1b-E pack holds the earlier
file); this write-up and the status notes (written after the freeze).

## 6. Residuals (accepted or recorded)

1. C1 window (owner ruling): no compare-and-swap in localStorage; a remover's read-then-remove can delete a value written
   in between, and a restored page does not write back what another tab's expiry or retire removed (N34b).
2. DE2: a refused write pending across a page hide can be written after another tab's logoff (D-3 class).
3. From M1b-C: a browser discard reaches only its own page load (R1, M1b-C §8 item 4); origin quota growth with several
   heavy tabs under the per-load budget (R3, M1b-C §10); on a plain-HTTP origin (no Web Locks) nothing of another load is
   ever removed by expiry or retire (M1b-C §7).

## 7. M1b closing summary: what M1 and M1b-A..F leave for M2, M3 and M4

Built (in the module and Core, off by switch):
- the per-key localStorage journal (M1);
- the intent log with current-intent retries (M1b-A cluster A; M1 D8/D9/D10 also handled there);
- per-tab clears, lock-gated removal of other loads' keys (O1-A) and the logoff sweep (O2) (M1b-C);
- the written key competing for eviction and per-field states for shared keys (M1b-D);
- DR1 and the C# twin (M1b-E);
- no self-repair (M1b-F).

Left for M2 (module and Core, before intake):
- Cluster B, open since the M1 review: D1 (reply accounting), D2 (`awaitMask` cleared at timeout promotes raw composed
  text), D7 (`pointerdown` on non-field elements opens an attempt) (M1 §5b; the seam contract is in M1b-A §8).
- Shared keys: the composition hold and the own-retire pending check read only the latest field state of a key. M1b-D
  noted that this limit predates it and is also reachable by a sibling's blur. P21 (two acted shared fields overwrite each
  other after a retire) is deferred to M2.
- The M1 §9 brief: the custom component seam (U5 time components), DateTime opt-in, reconcile (null vs "", display format
  C11), coverage of laid-out items, the development key.
- Coverage coalescing (M1b-A C9) needs a running host (M2 / §7 browser gate).
- M1 closes only after the owner's M0 §7 browser checks (M1 write-up §0).

Left for M3 (intake):
- The eleven-point list in M1b-C §8 (sequence-frozen reads validated as untrusted; skip live writers; newest generation,
  then capture time; recognise retired contexts; reconcile before classify; promotion per context; offer rules O3-O5;
  `await retire` after the durable write).
- Tolerate replays: entries a still-open tab writes again after a retire or logoff (D-3, DE2).
- The transport: `IJSStreamReference` with an explicit `maxAllowedSize` (M1 §9.4).
- Recompute the owner token server-side (M1 §13).
- Confirm the production SignalR log level is above Debug (M0).
- The EditDraft table in production as a prerequisite (M1 §6).
- Revisit the switch rule (owner decision at M1).

Left for M4: call `logoff(ns)` before XAF's sign-out navigation (M1b-C §8 item 11; not wired); consumer and browser
regression (design M4 row).

## 8. Deployment

Unchanged from M1b-E: the RCL asset in the CareCrew.Blazor.Server build, inert until `EditDraftCapture:Journal:Enabled`
and a type key are set (not set anywhere); the C# twin has no runtime caller; no NHM counterpart, no schema change, no
ChartWorkflowServiceV2, report layout or sync consumer.

## 9. Contribution log

### What Claude did
- Removed the repair from the module (M1b-C text restored in the resume functions) and kept G2/G4.
- Ran the suite before deleting to confirm which tests depend on the repair; found P29 does.
- Deleted the 23 tests with a script and made the kept-test edits listed in §2. Renamed T72b.
- Ran the js suite, the cross-module m1b-e runs, the G2 mutant, the build and the three .NET suites.
- Refreshed Phase 0, froze the candidate, built the pack (incl. the module diff against 865f8b10) and launched the review.

### What ChatGPT (Codex) did
- One `diffreview` (7.6 min): re-ran the suite, ran in-memory checks (G2 removed, restore probes, DE2 on three module
  versions) and checked NHM. Found no defects. 0 file changes; no web search; MCP: KB `lookup_known_fix` once.

### Found issues, by tool

| ID | Issue | Found by | Verdict | Evidence | Outcome |
|---|---|---|---|---|---|
| F1 | P29 asserts the re-check write-back, so it is a repair test | Claude | confirmed | `ev/js-before-test-deletion.tap` (P29 fails without the repair) | deleted with the repair tests |
| F2 | DE2 remains on the candidate | Codex (M1b-E DE2), re-run here by Codex | confirmed, pre-existing | Codex probes on candidate, HEAD, M1b-C | recorded residual |

Found independently by both: none.

### Codex calls

| Run / call / attempt | Started | Duration | state | validation | exit | Model / effort | Effective effort | Reasoning tokens | Search | MCP | activity (commands / non-zero / file_change / outside-repo) | Pack | CLI |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 45a751 / diffreview / a1 | 15:00:15 | 7.6 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 3,777 | off | KB lookup_known_fix | 19 / 3 (a doc-reading script, rerun; NHM git; an `rg` with no match for the removed names) / 0 / NHM checkout (read), powershell.exe | v1 | 0.153.4 |

### Setup checks (Phase 0)
- Refreshed for this run (outputs in the local scratch folder):
  - manifest 7/7 OK;
  - codex-cli 0.153.4, login ChatGPT, `codex doctor` overall "warning";
  - snapshot: HEAD 191a7f59 plus the uncommitted M1b-E state.
- Unchanged from run 74b9e0 in this session:
  - hook fires (push blocked, Monitor `Get-Date` not blocked);
  - the collab.rules check was blocked by the hook on its text;
  - KB read-only `enabled_tools`; gpt-6-astra lists xhigh;
  - `BASH_MAX_TIMEOUT_MS`;
  - policy drift (`medium` in the agent file vs `xhigh` in rule 11 and the launcher).
- Item 2 (database): not applicable. Web search: off.

### Redaction
None needed (no database rows, no logs; synthetic test values).

### Inputs Codex did not have
MEMORY.md (named, one line pasted); the .NET results (supplied as logs); `EditDraftJournalBoundary.cs` and the security
sections (excluded); the pre-M1b-F snapshot of the untracked `m1b-e.test.js` (in the M1b-E pack only).

### Passes used
One cross-model call (the brief's "pass 2 of 2 for M1b-F"); no `tests` call (deletion only). Attempts 1, `success` / `ok`.

## 10. Not verified / open questions

- Real browsers: back/forward-cache lock and storage behaviour, iPad/Safari; the XAF host. No browser run in this pass.
- M2/M3/M4 items in §7.

could_not_determine:
- whether DE2 or the C1 window occur in production views (never deployed; switch off);
- real-browser ordering of a restore against another tab's removal.
