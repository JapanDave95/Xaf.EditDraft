# Xaf.EditDraft: client-side input journal ported from the first host's design branch (M1 + M1b-A..F)

Run `2026-10-05-editdraft-journal-port-1f3f34` (collaborator: Claude Opus 5.5 ports; Codex gpt-6-astra at xhigh,
read-only, one `diffreview`). Branch `design/client-journal`, created from `main` 2c855a2 (0.2.0-preview.1). Source of
the port: CareCrew branch `design/edit-draft-client-journal` at d5461197 (merge-base with CareCrew master 53089fc2),
read only. Not committed, not tagged, not published, not deployed. The journal switch is off by default and nothing
enables it.

Owner brief, 2026-10-05 (first sentence verbatim): "PORT the client-side journal work (M1 + M1b-A..F) from CareCrew's
design branch into the standalone Xaf.EditDraft repository as a design branch there."

## 0. Combined answer

The library-side journal work from the first host's design branch (M1, M1b-A, M1b-C, M1b-D, M1b-E/F) is now on branch
`design/client-journal` of this repository, uncommitted. Of the 33 files, 18 are byte-identical to the design branch at
d5461197; the four existing library files whose base changed carry exactly the design-branch hunks on the
0.2.0-preview.1 base; two new files differ only by the renamed capture controller and owner kind; and the nine documents
differ only by the public-repository sanitization. The build is clean; the library tests pass (266 + the 85 ported
journal tests, 1 skip as before), the js suite has the M1b-F counts (138 pass, 2 todo), and the sample's 59 tests pass.
The journal stays off by default. Codex's fidelity review confirmed all 33 hashes, the hunks and the renames. It found
two defects, not fixed: the documents still carry run telemetry and first-host operational detail (the same scope
question as the gaps run's open D4), and the ported tests keep first-host names plus one local scratch path in a
comment. Those, and two more first-host test texts Claude found, are owner decisions; nothing was changed for them.

## 1. Status

Ported, uncommitted, 2026-10-05.
- `dotnet build Xaf.EditDraft.sln -c Release --no-incremental`: 0 warnings, 0 errors (all six projects).
- Xaf.EditDraft.Tests: 351 passed, 0 failed, 1 skipped, 352 in total. Baseline on `main`: 266 passed, 1 skipped
  (`close-gaps-2026-10-04.md` §13). The 85 new tests are the ported journal fixtures (§4). The skip is C25_C31, unchanged
  (it reads the first host's project files).
- `npm test` in `Xaf.EditDraft.Tests/js`: 140 tests, 138 pass, 0 fail, 2 todo (N34b, P21). These are the M1b-F counts.
- Xaf.EditDraft.Sample.Tests: 59 passed (unchanged; the sample has no file change and no journal key).
- Codex `diffreview` a1: success, validation ok, exit 0, 11.3 min; the frozen candidate (33 files) did not change during
  the review. Two defects (C1, C2), reported unfixed (§6).

Next: owner review of §5 and §6, then git-committer on `design/client-journal`. M2 continues in this repository.

> **Status note, 2026-10-05 (after the review):** the C1 and C2/H1-H5 decisions were applied as post-review edits; see
> §10. Sections 0-9 describe the candidate as Codex reviewed it.

## 2. What was ported

### 2.1 Files

Source = the design branch at d5461197; "Source commit" = the last commit that changed the file there. Blob ids are git
blob ids of the LF-normalized content, so "Same" = yes means byte-identical apart from line endings. Every destination
file is CRLF, UTF-8 without BOM. Full 40-character ids and the SHA-256 of each working-tree file are in the run's
port map; Codex checked them (§6).

The five design commits: 62c01cdf (M1), 1d72e0cb (M1b-A), c7605e99 (M1b-C), a1e54374 (M1b-D; its files were changed
again in 3b7c3495), 3b7c3495 (M1b-E/F). The documents come from the documentation commits on the same branch
(34ae23ce, 63622641, 865f8b10, 191a7f59, d5461197).

| # | Destination (library) | Source commit | Source blob (d5461197) | Destination blob | Same | Difference |
|---|---|---|---|---|---|---|
| 1 | `Xaf.EditDraft.Blazor/EditDraftJournalAttributeControllerBlazor.cs` | 1d72e0cb | `aa51163baa9c` | `be7998298667` | no | renames (3 lines) |
| 2 | `Xaf.EditDraft.Blazor/wwwroot/edit-draft-journal.js` | 3b7c3495 | `1d7f1f606b21` | `1d7f1f606b21` | yes | - |
| 3 | `Xaf.EditDraft.Core/EditDraftCaptureController.cs` (source: `Xaf.EditDraft.Core/EditDraftCaptureControllerBlazor.cs`) | 62c01cdf | `f3177f4a3721` | `7453b453557b` | no | design hunk on the library file (renamed file) |
| 4 | `Xaf.EditDraft.Core/EditDraftJournal.cs` | 3b7c3495 | `3be46c4b5746` | `3be46c4b5746` | yes | - |
| 5 | `Xaf.EditDraft.Core/EditDraftJournalBoundary.cs` | 62c01cdf | `0c916e51f756` | `0c916e51f756` | yes | - |
| 6 | `Xaf.EditDraft.Core/EditDraftSwitch.cs` | 62c01cdf | `90488c310a81` | `90488c310a81` | yes | - |
| 7 | `Xaf.EditDraft.Core/EditDraftTexts.cs` | 62c01cdf | `22c92f087028` | `91576d2ed1e8` | no | design hunk on the library base |
| 8 | `Xaf.EditDraft.Core/EditDraftTypePolicy.cs` | 62c01cdf | `54f76706f30c` | `bdd0d9c9cad3` | no | design hunk on the library base |
| 9 | `Xaf.EditDraft.Tests/EditDraftJournalM1bTests.cs` | 1d72e0cb | `c9bd4c411bf6` | `c9bd4c411bf6` | yes | - |
| 10 | `Xaf.EditDraft.Tests/EditDraftJournalReviewTests.cs` | 62c01cdf | `87558d38afcf` | `87558d38afcf` | yes | - |
| 11 | `Xaf.EditDraft.Tests/EditDraftJournalTests.cs` | 3b7c3495 | `17f71de13a6c` | `02ff56b09697` | no | renames (3 lines) |
| 12 | `Xaf.EditDraft.Tests/EditDraftLibraryBlazorTests.cs` | 62c01cdf | `02984374bc82` | `7eca5b150626` | no | design hunk on the library base |
| 13 | `Xaf.EditDraft.Tests/Xaf.EditDraft.Tests.csproj` | 62c01cdf | `e11f198e0912` | `e11f198e0912` | yes | - |
| 14 | `Xaf.EditDraft.Tests/js/.gitignore` | 62c01cdf | `504afef81fba` | `504afef81fba` | yes | - |
| 15 | `Xaf.EditDraft.Tests/js/helpers/harness.js` | 1d72e0cb | `720d9bc550f2` | `720d9bc550f2` | yes | - |
| 16 | `Xaf.EditDraft.Tests/js/package.json` | 62c01cdf | `ced8efec2ab0` | `ced8efec2ab0` | yes | - |
| 17 | `Xaf.EditDraft.Tests/js/test/api-tabs.test.js` | c7605e99 | `9f20d9b3fe0e` | `9f20d9b3fe0e` | yes | - |
| 18 | `Xaf.EditDraft.Tests/js/test/capture.test.js` | c7605e99 | `df2742204b82` | `df2742204b82` | yes | - |
| 19 | `Xaf.EditDraft.Tests/js/test/m0-scenarios.test.js` | 62c01cdf | `c94cc4292f37` | `c94cc4292f37` | yes | - |
| 20 | `Xaf.EditDraft.Tests/js/test/m1b-a.test.js` | c7605e99 | `58e8b69ce68c` | `58e8b69ce68c` | yes | - |
| 21 | `Xaf.EditDraft.Tests/js/test/m1b-c.test.js` | 3b7c3495 | `0ad43cbc2903` | `0ad43cbc2903` | yes | - |
| 22 | `Xaf.EditDraft.Tests/js/test/m1b-d.test.js` | 3b7c3495 | `1e8ce2c7abdf` | `1e8ce2c7abdf` | yes | - |
| 23 | `Xaf.EditDraft.Tests/js/test/m1b-e.test.js` | 3b7c3495 | `b5c499676b37` | `b5c499676b37` | yes | - |
| 24 | `Xaf.EditDraft.Tests/js/test/review-a1.test.js` | c7605e99 | `90f7136f81ca` | `90f7136f81ca` | yes | - |
| 25 | `docs/edit-draft-client-journal-design-2026-10-03.md` | 34ae23ce | `df633d900572` | `3955c9ceb4eb` | no | sanitization (section 2.3) |
| 26 | `docs/edit-draft-client-journal-m0-2026-10-03.md` | 34ae23ce | `f37de31f7937` | `ae78efe8dba0` | no | sanitization (section 2.3) |
| 27 | `docs/edit-draft-client-journal-m1-2026-10-04.md` | 63622641 | `81fb78153b02` | `dc2e403a89a4` | no | sanitization (section 2.3) |
| 28 | `docs/edit-draft-client-journal-m1b-a-2026-10-04.md` | 865f8b10 | `fcad5f40420a` | `ccea7e2666dd` | no | sanitization (section 2.3) |
| 29 | `docs/edit-draft-client-journal-m1b-c-2026-10-04.md` | 191a7f59 | `9835fe09eed2` | `67787f80f366` | no | sanitization (section 2.3) |
| 30 | `docs/edit-draft-client-journal-m1b-d-2026-10-04.md` | d5461197 | `616bba0faa63` | `08861609228a` | no | sanitization (section 2.3) |
| 31 | `docs/edit-draft-client-journal-m1b-e-2026-10-04.md` | d5461197 | `005d6ada2af3` | `7ea95ccab52c` | no | sanitization (section 2.3) |
| 32 | `docs/edit-draft-client-journal-m1b-f-2026-10-04.md` | d5461197 | `c9bbb745a892` | `8a2a77374acb` | no | sanitization (section 2.3) |
| 33 | `docs/edit-draft-client-journal-per-tab-clears-2026-10-04.md` | 865f8b10 | `b8d9a82fb366` | `ef929d73ee74` | no | sanitization (section 2.3) |

`m1b-a.test.js` is ported at its d5461197 bytes, so the tests M1b-C deleted from it stay deleted. The whole of
`Xaf.EditDraft.Tests/js/**` at d5461197 is ported (8 test files, the harness, `package.json`, `.gitignore`), because the
M1b-F counts (140 / 138 / 2 todo) are the counts of all eight files together.

`EditDraftLibraryBlazorTests.cs` (row 12) is not named in the brief. It is ported because M1 changed its expected
controller list (owner ruling 2026-10-04, recorded in the file): without it, the existing controller-set test fails once
the journal attribute controller is in the Blazor assembly.

### 2.2 Renames applied (0.2.0-preview.1 names)

| File | Line(s) | Old | New |
|---|---|---|---|
| `Xaf.EditDraft.Blazor/EditDraftJournalAttributeControllerBlazor.cs` | 127, 176 | `GetController<EditDraftCaptureControllerBlazor>()` | `GetController<EditDraftCaptureController>()` |
| same | 181 | `EditDraftCaptureControllerBlazor.IsAdmittedViewIncludingNew(...)` | `EditDraftCaptureController.IsAdmittedViewIncludingNew(...)` |
| `Xaf.EditDraft.Tests/EditDraftJournalTests.cs` | 99 | source pin `"EditDraftCaptureControllerBlazor.IsAdmittedViewIncludingNew(...)"` | `"EditDraftCaptureController.IsAdmittedViewIncludingNew(...)"` (it pins the renamed controller line above) |
| same | 146 | `EditDraftOwnerKind.F2StaffMember` | `EditDraftOwnerKind.HostDefined` |
| same | 147 | `EditDraftCaptureControllerBlazor.IsAdmittedViewIncludingNew(...)` | `EditDraftCaptureController.IsAdmittedViewIncludingNew(...)` |
| `Xaf.EditDraft.Core/EditDraftCaptureController.cs` | file | the `TryGetBaselineRaw` hunk targeted `EditDraftCaptureControllerBlazor.cs` | applied to `EditDraftCaptureController.cs` |

No ported file uses `LoginIsStaffMember`, `SubSectionOid`, `SubSectionOf` or `IsSubSectionVisible` (grep of the source
files), so `OwnerFlag`, `ScopeOid`, `ScopeOf` and `IsScopeVisible` needed no change. The documents keep the old names:
they describe the state at the time they were written.

### 2.3 Document sanitization

The nine documents were sanitized for this public repository with the gaps run's F7 rules plus the brief's "Codex
internals" and "personal names":
- Removed: absolute local paths (scratch folders, the DevExpress source install root, the host log folder, the Codex
  binary path), LAN addresses of the first host's dev and production servers, process ids (including the PID columns of
  the Codex-call tables), Codex prompt, output, instruction, parity-pack and requirement hashes (and the Path and
  Prompt/output SHA-256 columns), the path and line numbers of Codex's configuration file (the text now says "Codex's MCP
  configuration"), every "Run ledger" section, and one dev
  database record id in a URL.
- Kept: all technical content, run ids, candidate `git diff` and source-file SHA-256 values, durations, states,
  reasoning-token counts, MCP tool names, KB fix numbers, and the first host's type, screen and file names (the earlier
  documents in this repository keep them too).
- No personal name was found (checked by grep for honorifics, the names known from the first host's records, and
  surname-given-name patterns).
- Each document changed only on the lines the rules touch; lines removed = the Run ledger sections (4 to 10 lines per
  document). The full source-versus-destination diff was in the review pack (§6).

## 3. What stayed in CareCrew

Not ported (CareCrew-side, for a later CareCrew branch, as the brief says):
- `CareCrew.Blazor.Server/Components/TimeOnlyDateEdit.razor` (+9) and `TimeOnlyMaskedInput.razor` (+8): the U5
  pass-through of unmatched attributes, so the journal attribute reaches the rendered editor.
- `NursingHome_Chart.Rostering.Tests/NightRoundsTimeEditorModelTests.cs` (+17) and
  `StaffOverTimeHolidayTimeEditorModelTests.cs` (+39): their tests.

The first host also needs, when it moves to a package version with the journal: the journal key
`EditDraftCapture:Journal:Enabled` (off until its browser pass), each policy's `JournalTimeOfDayMembers`, and the U5 edits
above.

## 4. Checks

| Check | Command (library worktree root; `--artifacts-path artifacts/claude-test/20261005-1f3f34`, deleted after) | Result |
|---|---|---|
| Build | `dotnet build Xaf.EditDraft.sln -c Release --no-incremental` | 0 warnings, 0 errors; the Core and Blazor assemblies contain the journal types and the test assembly the journal fixtures |
| Library tests | `dotnet test Xaf.EditDraft.Tests -c Release --no-build` | 351 / 0 / 1 skipped of 352. Journal fixtures: Switch 14, Descriptor 18, Review 8, M1b 18, Controller 6, Rule 21 = 85, all passed. 267 + 85 = 352 |
| js suite | `npm install` (jsdom 24.1.3, the version the source worktree used), `npm test` | 140: 138 pass, 0 fail, 2 todo (N34b residual, P21 escalated; both todo on the design branch too) |
| Sample tests | `dotnet test samples/.../Xaf.EditDraft.Sample.Tests -c Release --no-build` | 59 / 59, including the LocalDB tests |
| Journal off by default | `EditDraftSwitch.DecideJournal` needs the journal key and the type key to be the boolean true (and the new-record key for a never-saved record); T59/T60 pin "missing key = off"; the sample has no `Journal` key (grep) | off |
| Sample host on :5006 | not run | The brief asks for it only if an API changed in the port. Nothing changed beyond the renames, which the build and the source-pin tests cover. The journal module's start in this repository's sample is therefore not observed (§9) |

## 5. Host-specific text left in the ported files (Claude; ported verbatim, not changed)

The brief limits code changes to the renames, so these stay as they are on the design branch. Each is an owner decision.

| ID | File:line | Text | Kind |
|---|---|---|---|
| H1 | `Xaf.EditDraft.Tests/js/package.json:4` | description "Same pattern as caretree-scraper-tests" (a first-host test project) | text only |
| H2 | `Xaf.EditDraft.Tests/js/helpers/harness.js:2` | comment naming `caretree-scraper-tests/helpers/harness.js` | text only |
| H3 | `Xaf.EditDraft.Tests/EditDraftJournalTests.cs:275`, `js/test/capture.test.js:58`, `js/test/m1b-c.test.js:181` | the first host's own journal key `CareCrew_InputJournal` (KB fix-552), used to prove the library neither parses nor touches another journal's item | functional (test fixtures); a neutral name would keep the tests' meaning |
| H4 | `Xaf.EditDraft.Tests/js/test/api-tabs.test.js:248` | reconstruction fixture members `StaffMember`, `Date`, `StartTime`, `EndTime` (the first host's overtime type) | test data |
| H5 | `Xaf.EditDraft.Tests/EditDraftJournalTests.cs:146-147` | policy id `chart` and the reason text "chart (F2) policies stay out (owner decision 15)" after the enum rename to `HostDefined` | text only |

`EditDraftJournalTests.cs:359` asserts that the browser module does NOT contain "CareCrew"; it passes. Claude's scan
missed one item that Codex found: `js/test/m0-scenarios.test.js:2`, a comment that names the M0 harness by its local
scratch path (under the local application-data folder). It is listed under C2 in §6. `js/helpers/harness.js:160` uses `192.0.2.10`, a
documentation-only address (TEST-NET-1), not a first-host address.

## 6. Codex review (diffreview a1): defects, not fixed

One pass (the brief: one is enough for a fidelity port). Codex had the frozen candidate, the port map, the design-branch
hunks beside the library hunks, the rename diffs, the full sanitization diff and the four runtime files in full; it read
the source branch with git. It reported:
- All 33 destination blob ids and SHA-256 values match the pack; the source change set has the same 33 mapped paths; the
  six tracked patches reproduce the source hunks; the two renamed new files match exactly after the declared renames.
- `EditDraftJournalBoundary.cs` matches source blob `0c916e51…`; its design was not reviewed (single-model file).
- The journal switch still decides false for absent or non-true values and on configuration failures; the sample has no
  change; the NHM mirror item does not apply; the port adds no schema or database write.
- No technical meaning was changed by the document sanitization.

| ID | Defect (Codex) | Claude's check | Outcome |
|---|---|---|---|
| C1 | The nine documents still contain material the brief excludes: Codex model and effort, reasoning-token counts, tool inventories, launcher configuration, hook diagnostics, login and doctor status, the path `~/.claude.json` (design:423), and first-host operational detail (the first host's storage key, its development database name (m0:37), its build, publish-script and deployment paths). Cited ranges: the Codex-call and setup-check tables of every document, and the deployment sections. Text only. | Confirmed present at the cited lines (design:46, 290-295, 423; m0:37; m1b-c:225-227; per-tab-clears:398-402 read). They were kept on purpose: the gaps run's F7 rules removed only local paths, scratch folders, PIDs, prompt/output hashes, Codex binary paths and ledger lines, and the ten earlier documents in this repository keep the same tables and the first host's build and deployment notes. The question is the same as the gaps fix pass's open D4 (scope of "Codex internals"). No executable check decides a scope question. | **Open, owner decision.** Claude's position: keep, as in the existing documents, and decide D4 once for all documents. Codex's position: remove the run telemetry and the operational detail from these nine documents, keeping the requirements, findings, technical evidence and accepted residuals. |
| C2 | The ported tests keep first-host fixtures and one internal path: `CareCrew_InputJournal` (`EditDraftJournalTests.cs:275`, `js/test/capture.test.js:58`, `js/test/m1b-c.test.js:181,186-199`; functional), `.NotContain("CareCrew")` (`EditDraftJournalTests.cs:359`; functional), the M0 harness's local scratch path and run reference (`js/test/m0-scenarios.test.js:1-3`; text), `caretree-scraper-tests` (`js/helpers/harness.js:1-2`, `js/package.json:4`; text). A neutral unrelated-storage key keeps the tests' meaning. | Confirmed. Claude had found the storage key, the harness comment and the package description independently (H1-H3, written before the review output was read) and missed the scratch path at `m0-scenarios.test.js:2`. The `.NotContain("CareCrew")` line is a guard that keeps the first host's name out of the module, not a leak; renaming it only makes sense if the host vocabulary is neutralized everywhere. | **Open, owner decision** (with H4 and H5 in §5). The brief allowed only the renames, so nothing was changed. |
| — | `input_mismatch` (inventory only): this write-up appeared in the working tree during the review, outside the frozen candidate. | Expected: the write-up was drafted while Codex ran. The 33 candidate files did not change (launcher check). | No action; this write-up is not cross-reviewed. |

Codex could_not_determine: the effective values of the journal, type and new-record keys in any deployment
(configuration files not read); module loading and attribute presence in the sample at run time (no host run); an
independent reproduction of the build and test results (excluded from the call); the content of this write-up beyond its
first line.

## 7. Deployment

Nothing is deployed and nothing is packed. The journal ships in no package until a later version is tagged (owner).
Schema: none (the journal lives in the browser's localStorage; no table, no column). The CareCrew Blazor app, NHM
WinForms, ChartWorkflowServiceV2 and report layouts are not affected: CareCrew consumes 0.2.0-preview.1, which has no
journal code. Mirror: not applicable to this repository.

## 8. Contribution log

### What each model did
- **Claude (Opus 5.5):**
  - Did Phase 0 (below), created the worktree and recorded both snapshots (library `main` 2c855a2 clean; source
    d5461197 clean).
  - Copied the 24 code, test and js files from the source working tree, converting the two LF files (`m1b-d.test.js`,
    `m1b-e.test.js`) to CRLF, and checked every blob id against the source before any edit (all 24 equal).
  - Applied the six design-branch hunks to the library files by hand and the three plus three rename lines (§2.2).
  - Ran the build, the library tests, `npm install` + `npm test` and the sample tests (§4).
  - The nine documents were sanitized by a forked Claude session (same model, same rules, run in parallel with the code
    port; a Node script for the mechanical rules plus two hand edits). Claude reviewed its full diff against the
    source and re-ran the residual greps.
  - Built the review pack and froze the candidate. Wrote this report.
  - Found H1-H5 (§5) before the review output was read. Missed what Codex found: the scratch path in
    `m0-scenarios.test.js:2` (the grep looked for drive-letter paths, not environment-variable paths), and it did not
    treat the run telemetry in the documents as excluded material (C1, a scope question).
- **ChatGPT (Codex gpt-6-astra, xhigh, codex-cli 0.153.4, read-only):**
  - `diffreview` a1: re-derived all 33 blob ids and SHA-256 values, compared the six tracked hunks and the two rename
    diffs with the source, confirmed the Boundary file's blob, the switch defaults, the unchanged sample and the
    non-applicable NHM item.
  - Raised C1 and C2 and the inventory note; four could_not_determine items.
  - 13 commands (4 non-zero: a combined status command, a git read of the source worktree without `safe.directory`, a
    Python script and a PowerShell script; each was followed by a variant that succeeded), one KB `lookup_known_fix`,
    no `file_change`. Outside the worktree it read the source worktree with git and used powershell.exe. No web search
    item in the stream.

### Found issues, by tool
Severity rule: nothing here is wrong in production (nothing is deployed or packed). Text a public repository should not
carry ranks above wording; functional fixtures are listed with the tests that depend on them.

| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| C1 | Documents keep run telemetry, `~/.claude.json` and first-host operational detail | Codex | Confirmed present; scope question | cited lines read | excluded text published / certain once pushed / high / no | owner's reading of "Codex internals" (D4) | Open, owner |
| C2a | `CareCrew_InputJournal` in three tests | both (H3) | Confirmed | the three lines | host name in public tests / certain / high / no | rename to a neutral key, tests stay green | Open, owner |
| C2b | Local scratch path in `m0-scenarios.test.js:2` | Codex | Confirmed | line 2 | local path published / certain / high / no | grep | Open, owner |
| C2c | `.NotContain("CareCrew")` guard | Codex | Present; Claude: a guard, not a leak | line 359 | none functional / — / high / no | — | Open, owner (only with a full neutralization) |
| H1 | `caretree-scraper-tests` in `package.json:4` | both | Confirmed | line 4 | host project name / certain / high / no | grep | Open, owner |
| H2 | `caretree-scraper-tests` in `harness.js:2` | both | Confirmed | line 2 | host project name / certain / high / no | grep | Open, owner |
| H4 | First-host overtime members as a test fixture (`api-tabs.test.js:248`) | Claude | Confirmed | line 248 | host vocabulary in test data / certain / high / no | grep | Open, owner |
| H5 | "chart (F2)" reason text after the `HostDefined` rename (`EditDraftJournalTests.cs:146-147`) | Claude | Confirmed | lines 146-147 | stale host wording / certain / high / no | read | Open, owner |
| — | Write-up added during the review | Codex | Expected | launcher check (33 files unchanged) | none | — | Recorded |

Found independently by both: the first-host storage key in the tests and the `caretree-scraper-tests` references (H1-H3 /
C2). Coverage, not confidence.

### Codex calls

| Run | Call | Attempt | Started | Duration | state | validation | Exit | Model / effort requested | Effective effort | reasoning_output_tokens | Search | MCP calls | Commands (non-zero) | file_change | Pack | codex-cli |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1f3f34 | diffreview | a1 | 12:04:07 | 11.3 min | success | ok (candidate unchanged) | 0 | gpt-6-astra / xhigh | not observable | 5,805 | off | KB lookup_known_fix ×1 | 13 (4) | 0 | v1 (candidate: 33 files; `candidate.diff` SHA-256 762C530E…) | 0.153.4 |

Calls: 1, attempts: 1, accepted. No retry. Passes used: 1 (the diff review).

### Setup checks (Phase 0)

| # | Item | Result |
|---|---|---|
| 1 | BASH_MAX_TIMEOUT_MS 2400000 | present |
| 2 | Read-only query connection | not applicable: no shared database was queried; only the sample tests' throwaway LocalDB databases |
| 3 | Repo trusted | present (the hook fired in this session) |
| 4 | Manifest | all 7 files match |
| 5 | Hook fires / Monitor not blocked | `git push --dry-run origin HEAD` blocked by the hook; a `Get-Date` Monitor ran |
| 6 | collab.rules | plain `git push origin main` → forbidden; the wrapped `pwsh.exe -Command "git push"` check was blocked by the hook (its text matches the push pattern; false positive), not retried |
| 7 | prompt-input | no AGENTS.md in this repository; the first host's CLAUDE.md was pasted in the review pack |
| 8 | Tool boundary | no MCP tool can write a database, migrate, deploy, push or restart a service |
| 9 | Tool parity | Codex: blazor-knowledge-base with `enabled_tools` = the 9 read tools, and dxdocs. DEVIATION as in earlier runs: node_repl and cua_repl are enabled for Codex (forbidden in the prompt; 0 calls). Claude's browser and document connectors are not registered for Codex (unused) |
| 10 | Models | gpt-6-astra supports xhigh |
| 11 | Run id / salt / binary | 1f3f34; salt written (unused); codex-cli 0.153.4; doctor exit 0; login ChatGPT |
| 12 | Snapshot | library `main` 2c855a2, clean at start; source d5461197, clean |
| 13 | Policy drift | as in earlier runs: the agent file's Phase 0 item 10 and cost paragraph say medium while rule 11 and the launcher say xhigh; Codex's global configuration says medium (the launcher passes xhigh); CLAUDE.md "must not be empty" vs D1. Reported, not edited |
| 14 | Web search | off (`-Search` not passed); no web search item in the stream |

AGENTS.md: not created in this repository, for the reason the gaps run gave (the template points to the first host's
CLAUDE.md and deployment). Deviation from Phase 0 for the owner, unchanged.

### Redaction
None needed: no database rows, logs or personal data entered either model. The documents' sanitization is §2.3.

### Inputs Codex did not have
- Claude's host-vocabulary grep output (withheld on purpose so the leak check was independent; Codex had every file).
- The full text of the ported test files and of the js suite was not pasted; they are in the worktree, their blob ids
  were in the pack, and Codex read them there.
- This write-up (written during and after the review). It is not cross-reviewed.

## 9. Not verified / could_not_determine

- The journal at run time in this repository's sample: module import, `start()` and the `data-editdraft` attribute on
  the Note DetailView with the journal key on. Not run (the brief's condition for the smoke, an API change in the port,
  did not occur). Covered only by the build, the source-pin tests and the js suite.
- Codex did not rebuild or rerun the tests; the counts in §4 are Claude's runs.
- C1's scope (what "Codex internals" covers) and the host-vocabulary items C2/H1-H5: owner decisions.
- Inherited from the design branch and unchanged by the port (M1b-F write-up): P21 (escalated, todo, deferred to M2),
  N34b (accepted residual, todo), DE2 (residual), and the logoff call before sign-out (left for M4, not wired). The port
  neither fixes nor re-reviews them.
- Whether the journal's design-branch assumptions about the first host's editors (U5 pass-through) hold in other hosts:
  a consumer whose editor components drop unmatched attributes gets no attribute (the coverage line names it).

## 10. Status 2026-10-05: post-review edits (decisions on C1 and C2/H1-H5)

Decisions from the main session, 2026-10-05, consistent with the owner's 2026-10-04 public-repository hygiene rulings; no
further review was asked for. These edits were made AFTER the Codex review and are NOT cross-reviewed. Those files no
longer have the source blob ids in §2.1; their new blob ids are below.

| Decision | Change | File (new blob) |
|---|---|---|
| C2 (a) | The comment no longer gives the M0 harness's local scratch path; it names the harness file and its run instead ("journal-rules-check-v2.mjs of the M0 gate run 2026-10-03-edit-draft-journal-m0-455e4d; not in this repository"). | `Xaf.EditDraft.Tests/js/test/m0-scenarios.test.js:2` (`a3e2aba986d5`) |
| C2 (b) / H1, H2 | "Same pattern as caretree-scraper-tests" / "Same shape as caretree-scraper-tests/helpers/harness.js" became "mirrors the host application's jsdom harness pattern". | `Xaf.EditDraft.Tests/js/package.json:4` (`2503771bf978`), `Xaf.EditDraft.Tests/js/helpers/harness.js:1-2` (`8f1ba8632859`) |
| C2 (c) / H5 | Reason text "chart (F2) policies stay out (owner decision 15)" became "policies with the host-defined owner kind stay out (owner decision 15)". The assertion is unchanged. | `Xaf.EditDraft.Tests/EditDraftJournalTests.cs:147` |
| C2 (d) / H3 | KEPT `CareCrew_InputJournal` (the first host's real localStorage key, which the library must not touch). Added a one-line comment saying so above each use. | `EditDraftJournalTests.cs:275` (`e491cf547b7d`, with the line above), `js/test/capture.test.js:58` (`e1e49ad9c4d0`), `js/test/m1b-c.test.js:181` (`6b82bc6324eb`) |
| C2 (d) | KEPT the `.NotContain("CareCrew")` guard (`EditDraftJournalTests.cs:360` after the added comment line). | none |
| C2 (d) / H4 | RENAMED. The fixture's members `StaffMember`, `Date`, `StartTime` and `EndTime` became `Owner`, `Day`, `Start` and `End` (the names of the C# probe type). This is a pure fixture: the module treats `rc` as an opaque object (`edit-draft-journal.js:95,100,543,968`), the test compares the stored `rc` only with the same literal, and no other test or source pins the names (grep). | `Xaf.EditDraft.Tests/js/test/api-tabs.test.js:248` (`2616ac83a684`) |
| C1 | In the documents: removed the first host's development database name (m0: 7 places, now "the development database" / "dev database"), its build, publish and deployment paths (the project file path with line numbers, the publish script path, the dev-host artifacts folder and the run's host script name in design, m0, m1, m1b-a, m1b-c, per-tab-clears; now "the host's publish script" / "an isolated artifacts folder" / "the run's host script"), and the `~/.claude.json` mention (design Phase-0 item 3, now "the repository is trusted in Claude Code"). Telemetry (durations, token counts, states, run ids, hashes) is kept. 17 exact replacements; residual grep for the removed names and paths: no hits; every file still CRLF without BOM. | design (`d66503559198`), m0 (`ee652b377d29`), m1 (`364f23e19abb`), m1b-a (`2441d1e38314`), m1b-c (`9a8b253e309c`), per-tab-clears (`bc4e78a49b33`); m1b-d/e/f unchanged (their deployment sections name only the host project, no path) |

Checks on the edited bytes (run into a fresh artifacts folder, deleted after):
- `dotnet build Xaf.EditDraft.sln -c Release --no-incremental`: 0 warnings, 0 errors.
- Xaf.EditDraft.Tests: 351 passed, 0 failed, 1 skipped (C25_C31), 352 total, the same as before.
- `npm test`: 140 tests, 138 pass, 0 fail, 2 todo, the same as before.
- Sample.Tests was not rerun: no sample or library source changed.

After these edits, §5 H1-H5 and §6 C1/C2 are closed by these decisions, except what was kept on purpose
(`CareCrew_InputJournal`, the `.NotContain("CareCrew")` guard, and the run telemetry in the documents).
