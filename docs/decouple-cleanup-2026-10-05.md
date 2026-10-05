# Xaf.EditDraft 0.4.1-preview.1: first-host cleanup (2026-10-05)

Run `2026-10-05-decouple-cleanup-cca120`, collaborator agent (Claude Opus 5.5 implementing; Codex reviewing the frozen
diff at `xhigh`, read-only). Branch `chore/decouple-cleanup` from `5f66b99` (0.4.0-preview.1). Not committed, not
published.

## 0. Combined answer

The library source, texts and tests no longer name the application the library was extracted from (its classes,
files, screens, test project or vocabulary). Provenance comments are rewritten in library terms, one Japanese text is
reworded, four tests are renamed, and the one host-comparison test keeps only its checks on this repository's own
files, so no test depends on a file of another repository (the tests that read repository files skip only outside a
git checkout). The version is 0.4.1-preview.1. There is no API change and no behaviour change other than the Japanese
`PersonalLoginOnly` text. The guard tests that forbid the first host's assembly names and its real localStorage key
stay; the NA8 source scan now also covers the names this cleanup removed and the project files. The first Codex review
found three defects and Claude four more; all seven were fixed in a follow-up pass. The second and last Codex review of
that pass found four further defects (one more first-host fixture and three statements in this write-up), reported
unfixed in section 9.

## 1. Status

2026-10-05: implemented in the working tree, two review passes done, not committed (git-committer follows), not
published (a tag `v0.4.1-preview.1` runs the publish workflow).

## 2. Scan before and after

Command (step 5 of the brief): `grep -rnE 'CareCrew|NursingHome|事業所|SubSection|StaffMember|HostDefined|\bF2\b|TenantChart|職員|ケア樹|CareTree|Progress\.|Llamachant'`
over `Xaf.EditDraft.Core` and `Xaf.EditDraft.Blazor` (`*.cs *.js *.razor *.css *.csproj`, obj/bin excluded), and the
same over `Xaf.EditDraft.Tests` (`*.cs *.js *.mjs *.json *.csproj`, node_modules excluded).

| Area | Before (5f66b99) | After |
|---|---|---|
| Core + Blazor | 19 lines: DraftWriteSlot.cs:6,12; EditDraftRestoreRow.cs:6; EditDraftRowContext.cs:11; EditDraftSwitch.cs:11,37; EditDraftTexts.cs:232; EditDraftTypePolicy.cs:120,123,140; Xaf.EditDraft.Core.csproj:3,5,6; EditDraftListControllerBlazor.cs:18; EditDraftRestoreItem.cs:14,16; EditDraftRestoreItemListControllerBlazor.cs:16; Xaf.EditDraft.Blazor.csproj:6,7 | none |
| Tests | 65 lines in 15 files | 36 lines in 8 files, all GUARD (section 5) |

A broader case-insensitive scan of Core and Blazor (`chart|勤怠|残業|有給|苦情|カルテ|StaffOverTime|attendance|Rostering|first host|care|meal|ShiftType|staff`, with word boundaries on care, meal and staff)
also returns nothing after the change. Before, it found the first host's own draft types ("chart"), screen names
(勤怠管理, カルテ入力控, 苦情対応, 残業・有給), class names (`AttendanceDraftSlot`, `StaffOverTimeHoliday_ListView`,
`TenantChartAccident`), its domain words ("attendance store", "care text") and "first host" provenance in comments.
The same scan over the tests matches only the NA8 guard list.

## 3. Text decisions

| Member | Before | After | Reason |
|---|---|---|---|
| `Japanese.PersonalLoginOnly` | この画面の入力控は、職員個人のログインで使えます。 | このログインでは入力控を使えません。 | Still used: shown when the owner seam returns no owner (five call sites in the list, badge and list-popup controllers). Says what the English text says ("Drafts are not available for this login."). Gap G9 had already removed "personal login" from the English set as first-host vocabulary. |
| `English.PersonalLoginOnly` | Drafts are not available for this login. | unchanged | already neutral |
| `Japanese.DraftCannotOpen` | …ほかの利用者のものです | unchanged | 利用者 means another user of the system here (English: "belongs to another user") |
| every other member (ja, en) | | unchanged | no other host vocabulary; 入力控 is the library's Japanese product term |

No public member is removed or renamed. The member name `PersonalLoginOnly` itself still says "personal login";
renaming it would be a breaking change and was not asked.

## 4. Comment rewrites

Comments and project-file comments only; no code line changed apart from the text value above.

- Xaf.EditDraft.Core: DraftWriteSlot.cs, EditDraftCaptureController.cs, EditDraftDisplay.cs, EditDraftListRules.cs,
  EditDraftMemberDecision.cs, EditDraftMembers.cs, EditDraftPayload.cs, EditDraftRestoreGuard.cs, EditDraftRestoreRow.cs,
  EditDraftRestorer.cs, EditDraftRowContext.cs, EditDraftStoreBase.cs, EditDraftSwitch.cs, EditDraftTexts.cs,
  EditDraftTypePolicy.cs, Xaf.EditDraft.Core.csproj.
- Xaf.EditDraft.Blazor: EditDraftListBadgeControllerBlazor.cs, EditDraftListCaptureControllerBlazor.cs,
  EditDraftListControllerBlazor.cs, EditDraftModels.cs, EditDraftRestoreControllerBlazor.cs, EditDraftRestoreItem.cs,
  EditDraftRestoreItemListControllerBlazor.cs, Xaf.EditDraft.Blazor.csproj.
- Root: Directory.Build.props, Directory.Packages.props (header comments).

Host examples are replaced by neutral ones (an Order with Customer, Date, Start and End; a Note; 「メモ」). Comments
that described the first host's copy of a class now describe what the library does, for example: the restore guard
is opened by the restore and the recreate controllers; `ReconstructionOrder` orders the existing-record apply and
admits non-browsable members, while a new record's seed is `NewRecordReconstructionOrder`. The project files state
their allowed references without listing the forbidden prefixes: Core "DevExpress packages, Newtonsoft.Json and the
BCL only; no application assembly (enforced by EditDraftLibraryIsolationTests)"; Blazor "Xaf.EditDraft.Core,
DevExpress packages and the ASP.NET Core framework only; no application assembly (enforced by
EditDraftLibraryBlazorTests)". Newtonsoft.Json and the Blazor test are named because those are the facts the tests pin.
The three references to a wave-1b design document that is not in this repository (EditDraftListRules.cs,
EditDraftListCaptureControllerBlazor.cs and the EditDraftWave1bTests.cs header) are dropped; the owner decision ids
stay.

Left as they are: references to the first host's knowledge base (`KB fix-NNN`), owner decision ids, wave and
milestone names, the release notes of earlier versions, and README's history paragraph.

## 5. Tests

No test deleted, none added. `dotnet test --list-tests` on the base and on the candidate lists 358 tests each; the
four that differ are the renames below.

| Class | Test | Category | Change |
|---|---|---|---|
| EditDraftCloseGapsTests | G9_T34_T35_the_English_set_has_no_host_term_and_no_Japanese_character | GUARD | kept (old texts stay gone); comments |
| EditDraftCloseGapsTests | G13_T77_T78_Core_grants_its_internals_to_the_Blazor_part_and_the_library_tests_only | GUARD | unchanged |
| EditDraftCloseGapsTests | G8_T32_the_listed_host_names_are_gone_from_the_public_surface | GUARD | unchanged |
| EditDraftCloseGapsTests | G9_T36_the_Japanese_wording_is_unchanged → G9_T36_the_Japanese_wording_is_pinned_and_names_no_host_concept | incidental | renamed (the old name contradicted the changed expectation); `PersonalLoginOnly` expectation changed (owner instruction "make the Japanese text generic"); comments |
| EditDraftJournalDescriptorTests | T70_a_stored_entry_parses_only_under_the_key_its_own_fields_make | GUARD | kept (`CareCrew_InputJournal` never parses); comment |
| EditDraftJournalRuleTests | T72_the_rules_and_limits_are_the_same_in_the_browser_module | GUARD | unchanged (`.NotContain("CareCrew")`) |
| EditDraftJournalControllerTests | T64_lookups_numbers_booleans_dates_and_unknown_components_get_no_attribute | incidental | policy id "chart" → "NoTable" |
| EditDraftMultiGroupTests | M20_apply_order_is_context_then_every_driver_then_the_rest_then_each_groups_final_values | incidental | reason text "as the meal members are today" → "at the end" |
| EditDraftLibraryBlazorTests | E1_E2_the_Blazor_assembly_references_Core_and_no_application_Llamachant_or_Progress_assembly | GUARD | unchanged |
| EditDraftLibraryBlazorTests | E2_the_Blazor_project_references_only_Core_and_DevExpress_and_its_code_names_no_application_symbol_logger_or_configuration_key | GUARD | unchanged |
| EditDraftLibraryBlazorTests | E6_E7_D9_the_popup_classes_are_plain_XAF_non_persistent_objects_and_no_property_names_an_editor_alias | GUARD + incidental | `NotContain("Llamachant")` kept; one reason text neutralized |
| EditDraftLibraryBlazorTests | E19_the_moved_controllers_use_the_Core_seams_and_no_application_helper_clock_or_literal_UI_text | incidental | the retired 事業所 text in the no-literal-UI-text list replaced by the current `RecordNotVisible` text |
| EditDraftLibraryBlazorTests | E22_…, E22b_… | incidental | comments and one reason text |
| EditDraftLibraryIsolationTests | E5_…, E6_… | GUARD | unchanged; comments |
| EditDraftTestProjectIsolationTests | C26_C27_this_test_assembly_and_everything_it_references_name_no_application_Module_Progress_Llamachant_or_CareTree_assembly | GUARD | unchanged |
| EditDraftTestProjectIsolationTests | C25_C31_the_project_uses_the_versions_the_application_tests_pin_adds_no_package_and_is_in_the_solution_once → C25_C31_the_project_references_the_two_library_projects_links_no_source_and_is_in_the_solution_once | HOST-COMPARISON | renamed; the comparison with the first host's test project and solution removed; the checks on this repository's own test project kept, with `Xaf.EditDraft.sln` |
| EditDraftModelCaptionTests | C42_CareCrew_s_Japanese_set_gives_every_model_caption_of_today_byte_for_byte → C42_the_Japanese_set_gives_every_model_caption_byte_for_byte | incidental | renamed; body unchanged |
| EditDraftXafNativeAccessTests | NA1_…, NA2_… | GUARD | unchanged (removed members and columns stay removed) |
| EditDraftXafNativeAccessTests | NA6_the_drafts_list_asks_the_owner_seam_per_type_… | incidental | owner fixture `staff` → `secondOwner` |
| EditDraftXafNativeAccessTests | NA8_no_text_and_no_library_source_names_a_host_term_and_the_refusals_say_no_permission | GUARD | extended: 職員 checked in both text sets; the source word list also holds CareCrew, NursingHome, TenantChart, 職員, ケア樹, CareTree, Progress., Llamachant; `.razor` and `.csproj` files scanned |
| EditDraftNewRecordAdmissionTests | T1_E43_E30_the_existing_rule_the_chart_policies_and_the_inline_list_stay_existing_only → T1_E43_E30_the_existing_rule_policies_without_a_decision_table_and_the_inline_list_stay_existing_only | incidental | renamed; policy id "chart" → "NoTable" |
| EditDraftNewRecordCaptureTests | T6_E08_N05_a_non_context_first_edit_seeds_the_three_context_members_with_their_construction_values | incidental | fixture text "late shift" → "late order" |
| EditDraftWave1GatingTests | W7_the_per_type_key_names_the_policy_id_so_another_type_s_switch_cannot_enable_it | incidental | type key "TenantCase" → "Note" |
| EditDraftWave1WiringScanTests | W38b_the_five_mutations_each_name_the_owner_including_the_multi_line_supersede | incidental | comment |
| EditDraftWave1bSwitchTests | E3_a_queued_list_write_re_reads_the_list_key_bound_to_its_own_policy | incidental | type key "StaffOverTimeHoliday" → "Note" |
| EditDraftWave1bBadgeAndProvenanceTests | E21_provenance_names_the_origin_and_the_view_caption_and_never_the_editor_id | incidental | 残業・有給 → メモ, `StaffOverTimeHoliday_ListView` → `Note_ListView`; comment |
| EditDraftLibraryLogAndTextTests | E16_the_set_is_chosen_explicitly_and_the_thread_culture_never_switches_it | incidental | 苦情対応 → メモ; reason text |
| js api-tabs.test.js | T30 coverage groups attributed editors by view and context … | incidental | second view id `ShiftType_DetailView` → `Order_DetailView` |
| js capture.test.js | T2 every change of one … rewrites that identity's own key … | GUARD | kept (`CareCrew_InputJournal` is never created); comment |
| js m1b-c.test.js | N3 mutation audit: … | GUARD | kept (`CareCrew_InputJournal` is never touched); comment |

C25_C31 was not deleted whole: its checks on this repository's own test project (net8.0, central package management
off, project references to Core and Blazor, no linked source) test something the library owns, and no first-host test
can hold them, since the first host consumes the published packages and its solution no longer contains the library
projects. Those checks had never run here: the test read a first-host file first and was skipped
(`Assert.Ignore`) when it was absent. The removed parts compared package versions with the first host's test project
and counted the test project in the first host's solution; they test nothing the library owns.

Helpers: `Wave1.Root`/`Wave1.Source` stay (about 70 call sites read library files). `Wave1.Source` now fails
(`Assert.Fail`) instead of skipping when a file is missing, as `RepoFile` in EditDraftCloseGapsTests already did: no
test reads a first-host file any more, so a missing file is a defect. `Wave1.Root` still skips
(`Assert.Ignore("repository root not found")`) when the tests run outside a git checkout; it is the only skip left in
the project. The unused constant `Wave1b.CaptureList` (a host view id) is removed. File headers and comments are
rewritten in TestEnvironment.cs, Xaf.EditDraft.Tests.csproj, EditDraftEngineTests.cs, EditDraftFixPassTests.cs (the
label "F2" there named a review finding, not a login kind), EditDraftLibrarySeamTests.cs, EditDraftNewRecordTests.cs,
EditDraftWave1Tests.cs, EditDraftWave1bTests.cs and the js `harness.js`, `package.json` and `m0-scenarios.test.js`.

## 6. Version and documents

`Directory.Build.props` `<Version>0.4.1-preview.1</Version>`; the release notes start with "0.4.1-preview.1
(2026-10-05): cleanup, no API change" and keep the earlier notes after "Earlier:". README: status sentence, install
snippet, and the paragraph about skipped tests, which now says that no test depends on a file of another repository
and that the tests that read repository files skip only outside a git checkout. Consumer guide: version line and a
one-line note for upgrading from 0.4.0-preview.1 (no code change; the Japanese text).

## 7. Checks on the final bytes

Run on 2026-10-05 after the follow-up fixes, on the bytes the pass-2 review saw, with this write-up present (no test
reads this file). Build and test output went to a separate artifacts folder, deleted afterwards. The same checks had
also passed on the pass-1 candidate.

| Check | Result |
|---|---|
| `dotnet build Xaf.EditDraft.sln -c Release` | 0 warnings, 0 errors |
| `dotnet test Xaf.EditDraft.Tests -c Release` | 358 total, 358 passed, 0 failed, 0 skipped (base: 358 total, 357 passed, 1 skipped — C25_C31) |
| `dotnet test samples/Xaf.EditDraft.Sample/Xaf.EditDraft.Sample.Tests -c Release` | 65 total, 65 passed |
| `npm test` (Xaf.EditDraft.Tests/js) | 140 tests: 138 pass, 0 fail, 2 todo (the documented residuals) |
| `dotnet pack` Core and Blazor | Xaf.EditDraft.Core and Xaf.EditDraft.Blazor 0.4.1-preview.1 (.nupkg and .snupkg), 0 warnings; the Blazor package depends on Core 0.4.1-preview.1 |
| `dotnet test --list-tests`, base vs candidate | 358 and 358; the only differences are the four renames in section 5 |
| Step-5 scan | Core and Blazor: no match; tests: the 36 GUARD lines of section 5 |

## 8. Review, pass 1

One Codex `diffreview` of the frozen candidate (43 files, manifest bound by SHA-256; no file changed during the review;
accepted: success, validation ok, no file change by Codex). Codex was asked for defects only. Each finding was checked
against the source by Claude. As the brief required, nothing was fixed after that review; the main session then
decided to apply all seven (section 9).

| ID | Finding | Found by | Verdict | Status 2026-10-05 |
|---|---|---|---|---|
| C1a | `Xaf.EditDraft.Core/EditDraftPayload.cs:28` explained the baseline rule by "the same rule as the attendance store" (the first host's attendance draft store) | Codex (Claude confirmed after seeing Codex's search in its command stream) | correct | fixed: the comparison dropped, the reason kept |
| C1b | `Xaf.EditDraft.Tests/EditDraftXafNativeAccessTests.cs:141,153,157-162` named the second owner fixture `staff` (NA6) | Codex | correct | fixed: `secondOwner` |
| C2 | `README.md:83` ("none is skipped") and the 0.4.1 release notes ("so no test is skipped") overstated: `Wave1.Root` (`EditDraftWave1Tests.cs:24-30`) still calls `Assert.Ignore` outside a git checkout | Codex | correct (static; an archive run was not executed) | fixed: both now say no test depends on a file of another repository and the tests that read repository files skip only outside a git checkout |
| C3 | `EditDraftCloseGapsTests.G9_T36_the_Japanese_wording_is_unchanged` kept a name that its new expectation contradicts | Codex | correct | fixed: renamed `G9_T36_the_Japanese_wording_is_pinned_and_names_no_host_concept` (the label `G9_T36` that older write-ups cite is kept; no other reference to the full name existed) |
| A1 | `Xaf.EditDraft.Core/EditDraftDisplay.cs:36` "Long care text is cut for a grid cell" ("care" is the first host's domain) | Claude, during the review | correct | fixed: "Long text is cut for a grid cell" |
| A2 | `Xaf.EditDraft.Tests/EditDraftEngineTests.cs:279` reason text "as the meal members are today" | Claude, during the review | correct | fixed: "a dependent without its driver is still repeated at the end" |
| A3 | `Xaf.EditDraft.Tests/js/test/api-tabs.test.js:138,149` fixture view id `ShiftType_DetailView` (a first-host class) | Claude, during the review | correct | fixed: `Order_DetailView` (T30 needs only a second view id besides `ToDo_DetailView`) |
| A4 | `Xaf.EditDraft.Tests/EditDraftNewRecordTests.cs:290` fixture text "late shift" | Claude, during the review | weak (plain English, taken from the first host's overtime screen) | fixed: "late order" |

Pass 1 also showed that the missing wave-1b design document was named in three places, not one as first reported;
all three references are dropped (section 4).

The NursingHomeManagement mirror is not applicable to any changed file: this is the standalone library repository.
Codex's `could_not_determine` in pass 1: which package build is deployed (source references do not establish
deployment); the build and test results were supplied, not rerun by Codex; the write-up and any edit after the freeze
were outside the reviewed candidate.

## 9. Status 2026-10-05: follow-up pass

Decision (main session, completing the owner's "run the cleanup pass"): apply the proposed fixes for all seven open
items before the tag, drop the reference to the design document that is not in this repository, re-run every check on
the final bytes, and have Codex review the delta only (pass 2 of 2; defects reported, not fixed).

Delta from the pass-1 candidate: 12 files (EditDraftPayload.cs, EditDraftDisplay.cs, EditDraftListRules.cs,
EditDraftListCaptureControllerBlazor.cs, EditDraftCloseGapsTests.cs, EditDraftEngineTests.cs, EditDraftNewRecordTests.cs,
EditDraftWave1bTests.cs, EditDraftXafNativeAccessTests.cs, js api-tabs.test.js, README.md, Directory.Build.props) plus
this write-up. All are comments, test names, test fixture values and documentation; no library code line changed.

Review, pass 2 (the last allowed): one Codex `diffreview` of the delta and this write-up (13 files bound by SHA-256; no
file changed during the review; accepted: success, validation ok, no file change by Codex). It found four defects,
each confirmed by Claude against the files. As decided, they are reported here and not fixed.

| ID | Finding | Verdict | Proposed change |
|---|---|---|---|
| P2-C1 | Section 2 of this write-up states the broader pattern without the word boundaries the scan used (`\bcare\b`, `\bmeal`, `\bstaff\b`); as written it matches "caret" (`Xaf.EditDraft.Blazor/wwwroot/edit-draft-journal.js:9`) and 24 test lines, most of them GUARD lines containing `NursingHome_Chart`, so "nothing" and "only the NA8 guard list" are false for the pattern as printed | correct (the scan that ran used the boundaries and returned nothing outside NA8) | print the pattern that ran, and list raw matches separately from their classification |
| P2-C2 | `Xaf.EditDraft.Tests/EditDraftLibrarySeamTests.cs:145` (E16) still uses the English caption fixture "Overtime" (the first host's overtime screen); the claim in section 0 that the tests name no first-host screen is therefore not complete | correct (missed in both passes; 苦情対応 in the same test was changed) | "Overtime" → "Notes" in both strings |
| P2-C3 | Section 3 says `PersonalLoginOnly` has four call sites; there are five (`EditDraftListControllerBlazor.cs:271,387,465`, `EditDraftListBadgeControllerBlazor.cs:380`, `EditDraftListPopupControllerBlazor.cs:65`) | correct (a counting error; the five lines were listed) | "five call sites" |
| P2-C4 | The decision named only the test header for the missing design document; the delta also removed the same reference from `EditDraftListRules.cs:10` and `EditDraftListCaptureControllerBlazor.cs:17` | correct as a scope statement; the extension was deliberate and was stated to the reviewer | owner or main session: keep the two library-comment removals, or restore the two references |

> **Resolved 2026-10-05 by the main session after pass 2 (no further Codex pass; both passes used):** P2-C1 — the
> printed pattern now states the word boundaries the scan used; P2-C2 — E16 fixture "Overtime" changed to "Notes";
> P2-C3 — the count corrected to five call sites; P2-C4 — the removal of the two comment references to a document that
> is not in this repository is kept (a pointer to a missing file helps no reader). Final checks re-run after these
> edits (see the release commit).

Codex's `could_not_determine` in pass 2: build, test and package results were supplied, not rerun; skip behaviour
outside a checkout was read in `Wave1.Root`, not executed; which package build is deployed.

## 10. Contribution log

- Claude (Opus 5.5): scans, text decision, every edit, classification, build, tests, pack, test-identity comparison,
  the follow-up fixes, this write-up.
- Codex (codex-cli 0.153.4, model gpt-6-astra, effort xhigh requested, read-only sandbox), pass 1: one `diffreview` of
  the frozen candidate (manifest of 43 files bound by SHA-256), about 7 minutes, 30 read-only commands and 1
  knowledge-base lookup, no file change; 4,425 reasoning tokens reported. It found C1-C3; Claude found A1-A4 while it
  ran (section 8).
- Codex, pass 2: one `diffreview` of the delta and this write-up (13 files bound by SHA-256), about 5 minutes, 16
  read-only commands, no file change; 3,174 reasoning tokens reported. It found P2-C1 to P2-C4 (section 9); none was
  found by Claude first.
- Found by both: the `EditDraftPayload.cs:28` trace (C1a) was reported by Codex; Claude found it after seeing Codex's
  search command in the activity stream, so it is not an independent find.
- Inputs Codex did not have: the owner's auto-memory index (personal data; not relevant to this change).
- Passes: two cross-model passes (the pass-1 diffreview and the pass-2 delta diffreview), the maximum.

## 11. Not verified / open

- The NuGet packages were packed locally, not published; the publish workflow runs on a tag.
- The first host still pins 0.4.0-preview.1; after it upgrades, its users see the new Japanese `PersonalLoginOnly`
  text. No first-host test pins that text (checked by search).
- Not part of this cleanup, reported for the owner: `samples/` still names the first host in comments, a README and
  sample tests (its upgrade tests use the first host's real column names `LoginIsStaffMember` and `SubSectionOid`);
  README's history paragraph and the earlier release notes name it; `KB fix-NNN` references in comments point to a
  knowledge base outside this repository.
- Open from review pass 2, unfixed: P2-C1 (this write-up's scan statement), P2-C2 (the "Overtime" fixture in E16),
  P2-C3 (this write-up's call-site count), P2-C4 (scope of the two library-comment removals) — section 9. Sections 0,
  2 and 3 of this write-up still carry the statements P2-C1 to P2-C3 correct.
- The skip behaviour outside a git checkout was read from the source, not executed.
