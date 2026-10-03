# Generic edit-draft restore (入力控): Phase A design (2026-09-30)

Run `2026-09-30-generic-edit-draft-7faa17`, collaborator agent (Claude + Codex). Design only. No production code, no schema, no database access, no commit. Worktree `C:\Users\owner\source\repos\CareCrew-editdraft`, branch `feature/generic-edit-draft` from `7907169`. The census it builds on sits beside it (`docs/generic-edit-draft-setter-census-2026-09-30.md` and `.csv`, uncommitted).

**Assumption (main-session reading, stated as instructed):** "Lets continue with the generic implimentation" starts with this design pass, because the table, security and ownership parts need owner review before code (CLAUDE.md single-model carve-outs), the same way カルテ入力控 went design run (2026-09-27) → owner decisions → Phase 0/1. The first build milestone after Phase A is the generic engine + カルテ入力控 moved onto it with no behaviour change (its existing tests are the check) + the Q3 accident groups. Wave-1 types come after that milestone.

Sections 2 and 3 are **SINGLE-MODEL (Claude only, no Codex input)**, because they cover schema / production data and security-relevant code. Every other section went through both models.

## 0. Combined answer

The generic feature is the existing カルテ入力控 code with the chart-specific decisions moved behind an explicit registry of per-type policies (Excluded, several named Groups, 他の記録も変わります members, 戻せません members, Companions, approved views, owner kind). It lives in CareCrew.Blazor.Server. カルテ入力控 moves onto it first in three steps: extract with no behaviour change, then support several groups per type, then add the two Q3 accident groups. The proof is its existing tests unchanged, a golden snapshot taken before extraction, and new scenario tests. Restore stays "claim the draft, then fill in through the setters, never save". It is made safe mainly by admission: no type with a commit, rollback, sync or cross-record path joins a wave. A Committing/RollingBack cancel guard is only a logged backstop with stated limits. Capture is NOT suppressed during a restore, which is today's behaviour, so a restore never creates a second draft. For storage (single-model), Claude recommends a new `EditDraft` table owned by the logged-in user, keeping the chart table untouched, so milestone 1 needs no schema change. This design does not restore NEW records for non-chart types, ListView edits, embedded property paths or sync types. Wave 1 is the owner's pick from two candidate lists, which share TenantCase and ToDo.

## 0a. Status

Design only (Phase A), 2026-09-30. Nothing implemented, committed, migrated or deployed. Phase B starts only on the owner's explicit go after the decisions in §10 are answered.

During this run, another session committed the census to branch `docs/generic-edit-draft-census-20260930` (820acab, 19:37:57) and removed the untracked copies from the main repo. The copies in this worktree are byte-identical (md SHA-256 D63B03D3…DBF, checked against `git show 820acab`). Before `feature/generic-edit-draft` is committed or merged, delete the two untracked census copies here, or rebase onto that branch, so the same files are not added twice.

## 1. Engine shape

### 1.1 What exists today and what is already type-neutral

| Piece (CareCrew.Blazor.Server unless marked) | Type-neutral? | Evidence |
|---|---|---|
| Payload + entry (baseline fixed at first capture) | yes | `Infrastructure/TenantChartDraftPayload.cs:9-80` |
| Codec (invariant text both ways) | yes | `Infrastructure/TenantChartDraftPolicy.cs:268-301` |
| Three-way comparison, status texts, group pre-tick rule | yes | `Infrastructure/TenantChartDraftPayload.cs:82-147` |
| Member discovery | NO: restricted to members declared on TenantChartEvent types, filtered by a chart-only name list | `TenantChartDraftPolicy.cs:152-173` (`:158` restriction, `:159` Excluded) |
| Dependency groups | NO: one group per type, constant id `type:meal` | `TenantChartDraftPolicy.cs:74-81`, `:101-105`; `TenantChartDraftRestorer.cs:88-94` |
| Apply order / group expansion / keep undrafted group members | yes apart from the one-group limit | `TenantChartDraftRestorer.cs:83-191` |
| Write slot (single flight, fresh start after expiry/破棄) | yes (already shared with 勤怠入力控) | `Infrastructure/AttendanceDraftSlot.cs`; used at `Controllers/Tenants/TenantCharts/TenantChartDraftCaptureControllerBlazor.cs:40` |
| Writer: fenced single-statement mutations, owner predicate in every statement | shape yes; owner column chart-specific | `Infrastructure/TenantChartDraftWriter.cs:121-183` |
| Owner = F2 staff member | chart-only by owner ruling 2026-09-28 | `Infrastructure/TenantChartDraftAuthor.cs:8-43` |
| D5 close/reopen on F2 change | chart-only | `TenantChartDraftCaptureControllerBlazor.cs:609-695` |
| NEW-record seeding (ReconstructionOrder), list NEW-record creation, duplicate rule | chart-only | `TenantChartDraftCaptureControllerBlazor.cs:271-288`; `TenantChartDraftListControllerBlazor.cs:435-590`; `TenantChartDraftPolicy.cs:256-265` |
| Restore popup model | reusable | `NursingHome_Chart.Module/BusinessObjects/NonPersistent/TenantChartDraftRestorePlan.cs` |

### 1.2 Proposed pieces

These are proposed names, not implemented classes.

| Today | Generic piece | Stays chart-specific |
|---|---|---|
| TenantChartDraftPolicy: lists, discovery, OwnerOf/GetValue/PathFor | `EditDraftTypePolicy` (per-type data), `EditDraftMembers` (discovery by policy, member access, change-to-path mapping), `EditDraftRegistry` | `TenantChartDraftTypePolicy`: the 23 types, the chart Excluded names, ReconstructionOrder, Companions, meal groups, BedSore lists, Q3 accident groups |
| TenantChartDraftCodec | `EditDraftCodec` | duplicate rule (`TenantChartDraftDuplicateRule`) |
| TenantChartDraftPayload / Entry / ItemStatus / Comparison | `EditDraftPayload`, `EditDraftEntry`, `EditDraftItemStatus`, `EditDraftComparison` | — |
| TenantChartDraftRestorer (one group) | `EditDraftRestorer` with MULTIPLE named groups per type: ApplyOrder, ExpandGroups, Apply | — |
| TenantChartDraftDisplay | `EditDraftDisplay` | — |
| TenantChartDraftWriter | storage port `IEditDraftStore`; the chart implementation IS today's writer, SQL unchanged; the generic implementation depends on §2 | chart writer |
| TenantChartDraftAuthor | owner port `IEditDraftOwner`: F2 (today's code) or Login | F2 resolver, D5 close/reopen |
| TenantChartDraftSwitch | `EditDraftSwitch` (global + per type) | `TenantChartDraftCapture:Enabled` unchanged |
| TenantChartDraftCaptureControllerBlazor | shared capture session (payload, baseline, slot, fresh start, save retires the draft) used by `EditDraftCaptureControllerBlazor : ObjectViewController<DetailView, object>`, active only for a registered type in an approved view | the chart controller keeps its class and file name, F2/D5 and NEW seeding, and calls the shared session |
| TenantChartDraftRestoreControllerBlazor + RestorePopup | `EditDraftRestoreControllerBlazor` (offer on open, re-check, claim first, then apply) | chart F2 re-offer on sign-in |
| TenantChartDraftList(+Popup) + ChartDraftListBridge | `EditDraftListControllerBlazor` + its own bridge (§5) | chart list, header action, NEW-record creation |
| TenantChartDraftRestorePlan (NonPersistent, Module) | reused for all types; caption set at runtime | — |
| AttendanceDraftSlot | reused unchanged (the name stays; renaming it is not needed) | — |

There is one engine per screen. The chart screens do not get a second, generic subscription alongside the chart one. The generic controllers go in `CareCrew.Blazor.Server/Controllers/Common/EditDrafts`. The popup's 破棄 action is chart-wired today (`TenantChartDraftRestorePopupControllerBlazor.cs:18, :59-68`, which calls the chart author and the chart writer; Codex review C3). So the generic popup routes 破棄 through the store and owner ports, and exactly one handler acts on a plan.

For milestone 1 the public `TenantChartDraft*` names stay as thin forwarding wrappers, so `TenantChartDraftTests.cs` compiles and runs unchanged. It calls `TenantChartDraftPolicy.MembersOf/GroupOf/HasSideEffect`, `TenantChartDraftRestorer.ApplyOrder`, `TenantChartDraftCodec` and `TenantChartDraftComparison` directly (`:50-140`). The chart controller file names also stay, because the wiring scan tests read them (`:436-510`). Removing the wrappers is a later clean-up.

**Project placement: the models disagree (owner decision D2).**

- Claude: everything in `CareCrew.Blazor.Server/Infrastructure/EditDrafts` (namespace `CareCrew.Blazor.Server.Infrastructure.EditDrafts`), linked into `NursingHome_Chart.Rostering.Tests` as today (`NursingHome_Chart.Rostering.Tests.csproj:38-50`). Reasons: NHM is excluded from restore; the Module has no direct `Newtonsoft.Json` reference, while the payload uses it (`TenantChartDraftPayload.cs:4`; Module csproj lists only GraphQL.* Newtonsoft packages); and Apply needs `IObjectSpace` (`TenantChartDraftRestorer.cs:103-104`).
- Codex (diag): the framework-free parts (payload, comparison, groups, plan builder, scalar codec) go in `NursingHome_Chart.Module/EditDrafts`; registry, accessors, restorer, controllers in Blazor. Reason: a framework-independent core, with reference handling kept out of the scalar codec (the codec recognises `BaseObject` directly, `TenantChartDraftPolicy.cs:281`).
- **Settled in pass 2:** Codex's review withdrew the Module placement, in its words: "My earlier Module proposal lacks a demonstrated requirement". It also noted that the missing direct reference does not prove Newtonsoft is unavailable transitively. **Placement: Blazor.** The owner can still ask for the Module variant. Only persistent classes go into the Module (and are mirrored to NHM).

### 1.3 Per-type opt-in

**Recommended: an explicit code registry of per-type policy classes** (both models, independently). A type that is not registered is never captured (fail closed).

| Option | Verdict | Why |
|---|---|---|
| Attribute on the business class | rejected | An attribute changes a Module BO file, so it must be mirrored to NHM for a Blazor-only feature. It also cannot carry ordered groups, companions, approved views or controller-effect decisions |
| Global policy with one name list | rejected (owner Q1) | The chart name list hides 85 non-A rows elsewhere, for example SchedulePeriod.Status (C) and DailyMealCost.Date (C) (census §2) |
| **Registry + per-type policy class** | recommended | One file per wave to review; structured data; fail closed; tests can enumerate it |

Each registration (`EditDraftTypePolicy`) carries:

- PolicyId + version, and the concrete type.
- ApprovedViewIds: capture and restore run only in these DetailView contexts, never in an unaudited view whose object space is shared with other objects.
- Captured members with their audited disposition. Reflection proposes candidates; it never admits a new property silently.
- Per-type **Excluded**, **Groups** (id, driver, members, order), **SideEffectMembers** (他の記録も変わります), **NotRestorableOnExisting** (戻せません), **Companions** (named paths only, no traversal of references or embedded paths).
- ReconstructionOrder (charts only).
- OwnerKind (F2 for TenantChart types, Login for every other type; §3).
- SubSectionOf (the 事業所 the §3 re-check uses).
- SwitchKey (§6).

The 23 chart types register through one `TenantChartDraftTypePolicy` built from today's static lists, so `MembersOf` returns exactly today's list per type. The chart name list stays with the chart types (owner Q1). The 85 name-hidden rows become visible C/D members that each get a decision when their type is proposed. Here "visible" means visible in the audit table, not a change to any property editor.

**Q1 enforcement (T3):** a test reflects over every registered non-chart type and fails on any captured member that has no disposition in the checked-in per-type table. The table records the census category and its file:line.

**Limit, accepted from Codex review C7:** that gate only notices a change to the member list. A setter, helper or controller of an admitted type can gain a new effect while the member names stay the same (for example the PaidLeaveRequest helpers, `PaidLeaveRequest.cs:96-154, :395-458, :504-543`). Proposed renewed-audit trigger (T3b): the decision table also records the SHA-256 of each admitted type's source file(s), and a scan test fails when one changes, which forces a re-audit. A NEW controller that subscribes to ObjectChanged somewhere else is still not caught by this. The per-type T14 run before each release of a wave is the check for that, and the design says so rather than claiming full coverage.

## 2. Storage — SINGLE-MODEL (Claude only, no Codex)

**Facts (source read at 7907169 unless marked):**

- `dbo.TenantChartEditDraft` is chart-shaped: `AuthorStaffMember` (StaffMember reference, the F2 owner), `TenantOid`, `TenantSubSectionOid`, `EventStartOn`, `ChartType` (`NursingHome_Chart.Module/BusinessObjects/TenantCharts/TenantChartEditDraft.cs:51-150`). Unique index `uxTenantChartEditDraft_Key` on (DraftKey, AuthorStaffMember) (`:53`).
- Every read, update and delete has `AuthorStaffMember = @author` in its WHERE (`TenantChartDraftWriter.cs:125-131, :166-170, :181, :221, :229-230, :242-247, :270-273`). The rows are denied to every role and read through a non-secured object space (`:37-41, :62-70`), so that predicate is the protection.
- The all-role Deny and the audit exclusion follow `DraftStoreTypes.All` (`NursingHome_Chart.Module/Security/DraftStoreTypes.cs:46-52`; `AttendanceDraftPermissions.cs:85-93`; `AuditTrailExclusions.cs:54`). The purge procedure is NOT registry-driven: it has one hand-written section per table (`scripts/create-draft-purge-job.sql:243-287` for TenantChartEditDraft). A test compares the registry with the SQL (`NursingHome_Chart.Rostering.Tests/CareTreeInputDraftTests.cs:334`).
- Production, from the brief and KB fix-523 (not observed in this run, because no database access was allowed): the table exists with live rows; capture has been on since CareCrew 2.6.42.0; the table was created by an NHM Debug run under the debugger with a temporary Live connection, after an NHM version bump. The Release WinForms app did not create schema (fix-453). The production purge procedure is a procedure-only copy of the repo script (fix-523 step 4).
- CareCrew runs with `DatabaseUpdateMode.Never`. The XPO schema update only adds; it never alters or drops (fix-403, fix-491).

**Options:**

| | (a) Generalize `dbo.TenantChartEditDraft` in place | **(b) New generic table + keep the chart table (recommended)** | (c) New table + migrate the chart rows |
|---|---|---|---|
| Schema change in production | Adds columns (for example OwnerUserOid, ObjectType, SubSectionOid, ContextText) to a LIVE table that capture writes to all day. AuthorStaffMember becomes optional for login-owned rows | One new, empty table (class `EditDraft : BaseObject` in the Module). The chart table is not touched | New table, then the chart table is retired |
| Live chart rows | unchanged; the old build ignores new columns | unchanged | copied (a production data write) or drained |
| Owner predicate | One table with two owner kinds. Every generic statement must say `OwnerKind = Login AND OwnerUserOid = @login`, and every chart statement `AuthorStaffMember = @f2`. One missed predicate mixes the two populations | One owner column per table and one predicate per writer. The chart writer is not touched | as (b), after the migration |
| Unique index | (DraftKey, AuthorStaffMember) with a NULL author on generic rows. SQL Server treats NULLs as equal in a unique index, so uniqueness then rests on DraftKey alone. It works, but the index no longer states the rule | New index for the new owner column | as (b) |
| Deny rows | already present | One line in `DraftStoreTypes.All`. The Updater then seeds a Deny for every role on the next NHM schema run (19 roles in production per fix-523). The existing "every role denies every registered type" test covers it | as (b) |
| Audit exclusion | already covered | automatic through DraftStoreTypes | as (b) |
| Purge | unchanged | A new hand-written section in `usp_PurgeDraftStores` plus the registry-vs-SQL test. Production gets a procedure-only copy, as in fix-523 step 4 | as (b), then remove the old section |
| NHM mirror | the changed class (new members) | the new class + the DraftStoreTypes line | as (b) |
| Production steps | fix-523 runbook, adding columns to a table in live use | fix-523 runbook, creating an empty table; the live chart feature is not touched | fix-523 runbook + a migration script (row-counted WHERE, proof script, rollback kept) |
| Rollback | XPO cannot remove the columns. An old build keeps working (it never reads them), and generic rows are invisible to it (NULL author) | An old build ignores the new table. If the feature is withdrawn, the owner drops the table by DDL and removes the deny rows (`AttendanceDraftPermissions.DescribeRollback`, `:143-148`) | Hardest: rows exist in two shapes |
| Effect on milestone 1 (charts on the engine) | The chart writer's SQL changes, so "no behaviour change" cannot hold at the storage level | **No schema change at all.** Charts stay on their table. The new table is needed only when the first non-chart type is switched on | A production data write inside milestone 1 |

**Recommendation: (b).** Milestone 1 ships with no schema change, and the chart writer's SQL (its owner predicate is the protection) stays untouched. The generic table gets its own single owner predicate (the logged-in user), so a missed WHERE clause cannot mix the two identity rules. No production data is migrated. Cost: two writers and two purge sections. The lists are separate anyway (§5). If the owner later wants one table, it can be done after the chart table's 7-day rows have drained, never by copying rows.

**Proposed generic table (for review, not built):** `EditDraft : BaseObject`, `[DeferredDeletion(false)]`, no navigation item, caption 「入力控」. It follows the chart class's choice of BaseObject over CustomBaseObject, because CustomBaseObject stamping costs a FindObject per save (KB fix-497). Columns:

- DraftKey (Guid), EditorInstanceId
- OwnerUserOid: Guid of the XAF login. NOT a reference to StaffMember, so a non-StaffMember login is representable. Indexed with ExpiresOn.
- LoginIsStaffMember (bool, record only)
- ObjectType (string): mapped back only through the registry, never through `Type.GetType`
- TargetOid
- SubSectionOid: the 事業所 at capture, for the re-check and the list
- ContextText: short display text taken on the circuit, for example 「ToDo／2026/09/30」 (see §3 S4)
- ViewId, PayloadSchemaVersion, Payload (unlimited), EntryCount, Revision
- FirstCapturedOn, LastCapturedOn, ExpiresOn (= FirstCapturedOn + 7 days, set once), DeletedOn
- OriginHost, LastError

Unique on (OwnerUserOid, DraftKey). There is no IsNew/ProvisionalOid in wave 1, because only existing records are restored. They are added with the NEW-record wave (an additive change).

**Production sequence when the first non-chart type is switched on** (owner-run, fix-523 runbook):

1. NHM version bump, plus the mirror of `EditDraft` and the DraftStoreTypes line.
2. COPY_ONLY backup, verified in msdb.
3. NHM Debug run under the debugger with the temporary Live switch. Prove the right build ran with the `Startup: FileVersion` log line, then revert the switch and check it by hash.
4. Read-only verification: INFORMATION_SCHEMA columns, indexes, deny rows (count = role count, all Deny).
5. NHM release.
6. Procedure-only copy of the updated `usp_PurgeDraftStores`, parse-checked, owner-run, verified in `sys.procedures`.
7. CareCrew publish with the EditDraftCapture switches still off.
8. The owner turns the switches on.

Milestone 1 (charts on the engine) needs none of these steps.

## 3. Ownership and security — SINGLE-MODEL (Claude only, no Codex)

**S1. Owner identity.** TenantChart types: the F2 staff member, unchanged (`TenantChartDraftAuthor.cs:25-100`). Every other type: the XAF login, `SecuritySystem.CurrentUserId`, read on the circuit. The 勤怠 and ケア樹 stores use the same identity (owner ruling 2026-09-28, verbatim: "F2 is only used in the "TenantChartXxxx" views. Every where else in the logged in user."). The type's policy chooses the owner resolver (`OwnerKind`); the view and the current F2 sign-in do not. A TenantChart type is never login-owned, and a non-chart type is never F2-owned, even while someone is F2-signed-in on the circuit.

**S2. GeneralUser logins (owner decision D6).** The owner's deployment note (2026-09-29): one GeneralUser account per department, with staff signing in with F2 inside it. Under "elsewhere the logged-in user", a draft typed on a non-chart screen under a department GeneralUser is owned by that shared login. Everyone who uses the login is then offered it and sees it in 「入力控」. Options:

- (i) Refuse capture when the login's StaffMember has `GeneralUser = true` (fail closed). **Recommended** until the owner confirms which logins use the wave-1 screens.
- (ii) Allow it. The draft is visible to everyone on that login, and they can already open and edit the record itself.
- (iii) F2 on those screens. This contradicts the 2026-09-28 ruling and is listed only to be rejected.

(i) reuses the GeneralUser flag that `TenantChartDraftAuthorRule` already uses (`:27-33`).

**S3. Re-check at restore (existing records).** Checked when the offer is built and again immediately before anything is applied, as the chart feature does (`TenantChartDraftRestoreControllerBlazor.cs:252-271`):

1. **Draft.** Current login == the draft's owner. The draft is live: not expired, and not discarded unless it was opened from the list's search. Same revision, same TargetOid.
2. **Read.** The record is the open DetailView's object in its SECURED object space, so XAF type and object criteria have already decided the person may read it.
3. **Write.** The view allows editing (`View.AllowEdit`), and every chosen member is writable for this user: `SecuritySystem.IsGranted(new PermissionRequest(objectSpace, type, SecurityOperations.Write, obj, member))`. A member that is not writable is shown as 戻せません, never silently skipped.
4. **事業所 assignment.** The record's 事業所 (`policy.SubSectionOf`) must belong to a Section in the login's `StaffMember.SecuritySections`. This is the owner's rule of 2026-09-29, implemented by the existing helper `GlobalSearchAssignment.IsRecordVisible` (`Services/GlobalSearchJump.cs:220-246`) and read fresh at the check (`Controllers/Common/GlobalSearchControllerBlazor.cs:359-369`). If the record is not visible, the person sees 「この記録は表示できません」 and nothing from the draft is shown.
5. **References.** References in the payload are resolved in the secured destination space, as today (`TenantChartDraftRestorer.cs:139-146`). An unreadable reference is 戻せません, never null.

The 「入力控」 list shows only rows owned by the current login. For a row whose record is no longer visible under the 事業所 rule, the context text is replaced by 「（表示できません）」, following the chart list (`TenantChartDraftListControllerBlazor.cs:357-371`).

The chart feature already covers checks 1, 2 and 5. Adding checks 3 and 4 to the 23 chart types would change live behaviour. That is a separate owner decision (D7) and is not part of milestone 1.

**S4. What a draft may contain.** The payload holds whatever the person typed (care text, staff notes).

- (a) The same codec kinds as today. No byte[], FileData, images or colours (`TenantChartDraftPolicy.cs:202-216`; census: 17 blob rows not captured).
- (b) Security, login and credential types are never registrable as whole types: StaffMember, ApplicationUser, roles, login info, BedrockCredentials (census §7 excluded list; census Q4). For every registered type, per-type Excluded lists any member that holds a credential or token.
- (c) The table has no resident or staff name column. ContextText is a caption plus a date, and names are resolved at read time through the secured space (chart design 2026-09-27 §4.1).
- (d) Logs carry type names and short Oid prefixes only, never values (`TenantChartDraftWriter.cs:115, :296`).

**S5. Deny / audit / purge coverage.** Registering `EditDraft` in `DraftStoreTypes.All` brings the all-role Deny (`AttendanceDraftPermissions.Apply` iterates the registry, `:89-92`) and the audit exclusion (`AuditTrailExclusions.Types => DraftStoreTypes.All`, `:54`) automatically. The purge needs its own SQL section, and the existing registry-vs-SQL test fails if it is missing. The generic writer reads and writes through a non-secured space with the owner predicate in every statement, like the chart writer. So the Deny rows do not block the feature, and they close every other way in.

**S6. Never registrable.** Security, draft-store, system, sync, log, obsolete and debug types (census §7, 38 excluded types). A test fails if any of them is in the registry.

## 4. Restore safety

A restore calls each member's public setter, so everything that runs when a person types the value also runs on restore (census §0, §4). Property-editor callbacks do not run; `ObjectSpace.ObjectChanged` subscribers do.

### 4.1 Controllers that save or roll back on a value change

| Controller | Trigger | Effect | Evidence |
|---|---|---|---|
| SubSectionServiceItemTemplateControllerBlazor | `e.PropertyName == NormalReductionDailyServiceEnum`. It checks the NAME only, not `e.Object` (Codex) | reloads two collections, then `ObjectSpace.CommitChanges()` | `Controllers/Tenants/SubSectionServiceItemTemplateControllerBlazor.cs:21-29` |
| LongTermHolidayControllerBlazor | LongTermHolidayEnum on an existing record | `ObjectSpace.Rollback(false)`, which discards every pending change | `Controllers/Staff/LongTermHolidayControllerBlazor.cs:76-88` |
| AttendanceAutoSaveControllerBlazor | ANY member of StaffAttendance_V2 / OverTimeShiftCountByMonth_V2 when `AttendanceAutoSave:Enabled` | posts a `CommitChanges` after the gesture | `Controllers/Staff/AttendanceAutoSaveControllerBlazor.cs:101-123, :126-153` |

Three layers, strongest first (both models):

1. **Admission (the guarantee).** No type is admitted while any of its members has a path that commits or rolls back. SubSectionServiceItemTemplate, LongTermHoliday, StaffAttendance, StaffAttendance_V2 and OverTimeShiftCountByMonth_V2 are out of every wave until the owner decides otherwise (D8). Excluding one member is not enough for the any-member autosave. For LongTermHoliday, Codex recommends keeping the enum 戻せません rather than bypassing the controller's business rule, and Claude agrees. Admission is per view: a type is restored only in its approved DetailViews, never in an unaudited view whose object space holds other objects.
2. **Runtime backstop (limited).** While a restore is applying, the engine handles the view object space's `Committing` and `RollingBack` and sets `Cancel = true`. DevExpress 26.1 documents both as `CancelEventArgs` whose Cancel prevents the commit or rollback (dxdocs `IObjectSpace.Committing`, `IObjectSpace.RollingBack`, fetched this session; the project pins 26.1.4, not executed). The guard stays open until a sentinel posted after apply has run. Limits, accepted from Codex review C1:
   - a callback that posts another callback can run after the sentinel;
   - cancelling does not undo work done before Committing (SubSectionServiceItemTemplate reloads its collections first);
   - a commit on a DIFFERENT object space is outside the guard (for example `TenantCostChangedControllerBlazor.cs:253-257`).

   It is a backstop that turns a violation into a logged, visible failure, not the guarantee. The engine never uses a rollback as error recovery, because that would discard unrelated edits (Codex diag).
3. **Per-type test before admission (T14).** Open an existing record in its real view, add an unrelated pending edit, restore every captured member, and drain posted callbacks (including a nested post). Assert: no commit, no rollback, the unrelated edit survives, and the modified-object set equals the PRE-restore set plus what the policy allows (Codex review C1). Then check that an ordinary manual save still works.

### 4.2 Q2: the capture and dirty-flag controllers (owner: conditional D on every captured field)

The owner's Q2 answer is applied as written. Every captured member of every type carries a conditional D path through TenantChartDraftCapture, AttendanceDraftCapture and UnsavedChangesDirtySync, next to its business category. The engine keeps these invariants:

- **Claim first, apply second.** The restore claims the draft into this editing context before any setter runs, and applies nothing if the claim loses (`TenantChartDraftRestoreControllerBlazor.cs:273-280` → `TryAdopt`, `TenantChartDraftCaptureControllerBlazor.cs:63-87`).
- **Capture is NOT suppressed during apply.** That is today's behaviour, and keeping it preserves カルテ入力控. The only suppression flag is `_suppressCapture`, set only in `CloseForNewAuthor` (`:671-682`). Applied values are captured into the ADOPTED payload, with the first baseline kept (`:291-310`; `TenantChartDraftPayload.cs:41-62`). So a restore never starts a second draft of itself. Codex's diag proposed suppressing capture during apply; its review withdrew that, because it would change the chart behaviour. Settled.
- **Dirty flag follows the real state.** UnsavedChangesDirtySync marks the browser dirty from ModifiedChanged and ObjectChanged (`UnsavedChangesDirtySyncControllerBlazor.cs:50-56, :88-99`). A restore that changed values leaves unsaved work, and the flag must be set. A restore that changed nothing (claim lost, nothing selected, あとで) must not set it. A setter can change state and then throw, so the test asserts the flag against the object space's actual modified state, not against the applied count (Codex review C4).
- AttendanceDraftCapture runs only on the attendance view, whose types are not admitted (§4.1), so it never sees a generic restore.

### 4.3 Same-record groups (B)

The engine supports several named groups per type, each with a driver, members and an order (both models). The rules are unchanged:

- one tick per group; a conflict or an unknown baseline unticks the whole group (`TenantChartDraftPayload.cs:139-146`);
- drivers are applied before the dependents' explicit final values;
- an undrafted member that a driver would change is put back (`TenantChartDraftRestorer.cs:111-125, :160-175`);
- the apply re-check drops a group whole (`TenantChartDraftRestoreControllerBlazor.cs:339-343`);
- an unavailable dependency makes the group unavailable.

For a new policy, overlapping write dependencies are merged into one group, and cyclic setters are never replayed "until they settle" (Codex diag). A group must list EVERY member its drivers can write, not only the drivers. PaidLeaveRequest shows why (Codex review C2, confirmed):

- LeaveType writes IsPaid, Priority, CanBeOverridden and RequiresMedicalCertificate (`PaidLeaveRequest.cs:504-545`);
- StaffMember, StartDate and EndDate call CheckNightShiftImpact, which queries ScheduleEntry, sets AffectsNightShift and prefixes Reason with a 警告 text (`:96-101, :395-458`);
- ApprovalStatus = Approved sets ApprovalDate = Now (`:175-182`).

### 4.4 Other-record members (C)

| Treatment | Chart precedent | Use for new types |
|---|---|---|
| Selectable, never pre-ticked, 「（他の記録も変わります）」 | meal absences (`TenantChartDraftPolicy.cs:88-99`) | only an audited, bounded effect that stays unsaved, by owner decision per member |
| Shown, 戻せません | BedSore on existing records (`:120-124`) | **recommended default**: the typed value is kept and shown for retyping; nothing crosses to another record |
| Excluded (not captured) | chart name list | only where the value must not be stored (§3 S4) |

A draft whose members are ALL 戻せません is never shown today: `HasNothingToOffer` is true (`TenantChartDraftRestorePlan.cs:74`), and the offer returns (`TenantChartDraftRestoreControllerBlazor.cs:197-201`) (Codex review C8). The generic engine therefore needs a read-only display of such a draft (「戻せない入力が N 件あります（表示のみ）」), or else 戻せません is not a real default. That display must show the FULL text: the popup grid shortens values to 60 characters (`TenantChartDraftDisplay.cs:37-38`; Codex combined). The chart offer stays as it is in milestone 1 (D9).

### 4.5 External effects (D)

CareTree forward sync is enqueued by controllers on a value change (`Controllers/Tenants/TenantRecordForwardSyncControllerBlazor.cs:33, :42-68`, and the TenantSubSections controllers, census §4). It creates a CareTreeSyncJob row immediately, and ChartWorkflowServiceV2 (built from NHM) consumes it. No type with a sync member is in wave 1 (both models). A later admission needs a rule for what happens on restore and on the eventual save, including repeated restores (Codex). Static-state D (attendance dictionaries) belongs to types that are not admitted. The log-only D (`TenantSubSection.Room`, the [ROOM-TRACE] log) is also a sync member and is not in wave 1.

## 5. UI

**Milestone 1: no UI change for charts.** 「カルテ入力控」 keeps its gear entry, its header action (exact view id rule, `TenantChartDraftListControllerBlazor.cs:35-37, :207-229`; test H3 `TenantChartDraftTests.cs:813`), its popup wording and its NEW-record flow. (Codex raised this; Claude agrees.)

**Offer on reopen (wave 1).** When an EXISTING record of a registered type opens in an approved view, the offer is deferred until the view is ready (KB fix-188 pattern, `TenantChartDraftRestoreControllerBlazor.cs:52-53`). It is the same popup model (`TenantChartDraftRestorePlan`) and the same texts:

- caption 「保存されていない入力が見つかりました」 (the chart keeps 「保存されていないカルテ入力が見つかりました」, `:222`)
- lead 「前回この記録に入力され、保存されていない内容が N 件あります。戻す項目を選んでください。戻した内容はまだ保存されません — 確認してから保存してください。」 (`:203-204`)
- conflict banner (`:205-207`)
- buttons 「はい（選択した項目を戻す）」 / 「あとで」 (`:239-240`), and 破棄 as in the chart popup
- status texts 反映済み / 戻せます / 他で変更されています / 変更前の値が不明です / 戻せません / 新規, plus 「（他の記録も変わります）」 (`TenantChartDraftPayload.cs:119-132`)

**Central list (wave 1): a separate 「入力控」 for the generic types.** Both models recommend this. The entry goes in the gear panel's 復元 section, next to 「カルテ入力控」 (`Components/CareCrewSettingsPanel.razor:85-99`), on its own staff-accessible bridge (the pattern of `Services/ChartDraftListBridge.cs:12-75`). It does NOT use the admin-only RecoveryToolsBridge. Why separate: the two lists have different owners (F2 vs login). One mixed list would show login-owned rows to whoever is F2-signed-in, or F2-owned rows to the login.

- Columns: 画面（種類） / 対象 / 入力日時 / 項目数 / 状態 / 保存期限. The chart columns 利用者 and 記録日時 are not forced onto unrelated types.
- 開く opens the existing record, and its screen offers exactly that draft (chart `RequestOffer` pattern, `TenantChartDraftSwitch.cs:79-94`).
- Search includes 破棄済み rows until they expire (Q7 precedent).
- Messages follow the chart list's wording without 「カルテ」, for example 「保存されていない入力控はありません。」.
- No header action in wave 1.

A single list for all types, charts included, is possible later, once the owner has ruled on the ownership question. It is not recommended now (D4).

## 6. Switches and rollout

A type is captured only when all of these hold (both models):

`registered policy AND approved view AND EditDraftCapture:Enabled AND EditDraftCapture:Types:<PolicyId>:Enabled`

- Only a boolean true is on. Missing, empty, invalid or unreadable = off, using the same parse as the chart switch (`TenantChartDraftSwitch.cs:20-28` → `CareTreeDraftCaptureSwitch.IsOn`). The keys are re-read at every capture and before every queued write, including coalesced and fresh-start writes (`TenantChartDraftCaptureControllerBlazor.cs:237, :394, :446`).
- **Charts keep `TenantChartDraftCapture:Enabled`** (no behaviour change). A new global key whose Production default is off must not switch off a feature that is live. Moving charts under the new keys would be a separate, coordinated owner decision (D5).
- appsettings.Development.json: global true + each wave-1 type true. appsettings.json and Production: keys absent (off) until the owner decides. A scan test pins this, the way C7 pins the chart key (`TenantChartDraftTests.cs:502-508`).
- With capture off, restore and the list stay available while the table exists, so switching off never hides drafts that are still inside their 7 days (chart rule `TenantChartDraftSwitch.cs:35-40`). A type switched off per type keeps its existing drafts restorable until they expire (recommended; D5).

## 7. Moving カルテ入力控 onto the engine with no behaviour change

Milestone 1 is three commits. Each is built into `artifacts/claude-test/<run-id>` and tested on its own; a test run counts only with a total above zero.

**Commit 1: extract the generic core, no behaviour change.**

- The engine pieces of §1.2, the registry, and `TenantChartDraftTypePolicy` built from today's lists.
- The `TenantChartDraft*` wrappers keep the public names.
- The chart controllers keep their files and classes and call the shared core.
- The chart writer keeps its SQL; the storage port wraps it.
- No view-id allowlist for charts: capture today has none (`TenantChartDraftCaptureControllerBlazor.cs:31, :112-143, :231-269`; Codex review C5). Adding one would be a behaviour change, so ApprovedViewIds applies only to non-chart types.
- One engine subscription per screen, never a second one alongside the chart one.
- Stored payloads stay readable (schema 1, `TenantChartEditDraft.cs:45`, `:155`).

Proof:

- (a) Every existing test passes unchanged: the 9 fixtures of `TenantChartDraftTests.cs` (PolicyTests `:50`, PayloadCodecTests `:143`, ComparisonTests `:237`, LiveObjectTests `:276`, AuthorAndBridgeTests `:386`, WiringScanTests `:436`, DraftSlotAttachTests `:513`, ReviewFixTests `:551`, HeaderActionTests `:760`), plus the draft-store and audit tests (`CareTreeInputDraftTests.cs:286-340`, `AttendanceEditDraftTests.cs:267-315`, `AuditTrailExclusionWiringTests.cs`). By name these include C1, C2, C3, C4, C5, C13, C15, C16, C18, C21, C23, Review_C4/C5/C6, the two BedSore tests, H3, H8_H10 and C7.
- (b) Golden snapshot T5: taken from the pre-refactor code BEFORE extraction and checked in. It covers exact member order and the repeated final assignments of ApplyOrder. It is never regenerated from the wrappers afterwards (Codex review C6).
- (c) New scenario tests, because the snapshot is necessary but not sufficient (C6): capture → restore → edit → save on an in-memory object space; capture-off restore; close/deactivate flush; companion (Weight); restore with two editing-context rows present (only the claimed one changes, no row is added; C4).
- (d) A Dev2 browser pass of the existing カルテ入力控 checklist before any publish, because this code runs live in production.

The wiring scan tests read source text. If a file moves, updating the scan is a test edit, and red-test rule (c) applies: escalate, do not revise. Keeping the chart file names avoids that.

**Commit 2: multi-group model, no behaviour change.** Groups become named lists per type. The meal group ids keep their current strings (`TenantChartDraftPolicy.cs:103`); they are not stored in payloads (`TenantChartDraftPayload.cs:9-24`). Proof: T5 is still equal, and C16, C18, C23 and Review_C6 still pass.

**Commit 3: Q3 accident groups, a deliberate change approved by the owner.**

- G1 `government-report` = [IsGovernmentAccidentReport (driver), AccidentReportNumber]. The setter fills 1 only when the number is 0, and not while loading or saving (`TenantChartAccident.cs:74-76`).
- G2 `accident-description` = [AccidentDescription (driver), AccidentTime]. The setter fills StartOn only when the time is MinValue (`:269-271`).

StartOn is already applied first (ReconstructionOrder, `TenantChartDraftPolicy.cs:54-58`; `TenantChartDraftRestorer.cs:87`), before the G2 driver that reads it (Codex diag). Both dependents are captured A members (census CSV). The groups contain only same-record defaults, so they are groups, not side effects. New tests are T16 and T17. After commit 3, T5 differs ONLY in the two accident groups, and the test asserts exactly that.

## 8. Wave 1

**Criteria (both models agree on 1–6; they differ on 7):**

1. In the census wave-1 list: every captured member A, or B inside a COMPLETE group (§4.3).
2. No member on a commit, rollback, sync, static-state or other-record path (§4).
3. Existing records only; no blob or collection restore; no embedded property paths (census Q6).
4. An own navigation item with a root DetailView (not nested-only). This is static evidence only; the actual opening paths are checked before admission (Codex).
5. Not a run or generated object (BonusRun, SalaryReportRun, OperatingReport*), not a security, draft, system or obsolete type.
6. Lifecycle hooks write nothing beyond what typing does (census §5).
7. **Claude:** at least one free-text member, because a draft is worth keeping when the person would otherwise retype text. **Codex:** a small member surface, to prove the engine on simple types first.

**Candidates (the owner picks):**

| Type | Proposed by | Captured members (census CSV) | Navigation | Notes |
|---|---|---|---|---|
| TenantCase | both | CaseDate, CaseNumber, Description, TenantSubSection | `CareCrew.Blazor.Server/Model.xafml:915` | resident-linked, so the 事業所 re-check applies; child Entries (B, TenantCaseEntry) not covered |
| ToDo | both | Description, ToDoEnum, ToDoItem | `Model.xafml:954` | smallest free-text case |
| StaffOverTimeHoliday | Claude | BusinessTripReason, ManagerNote, Reason (ref), Date, StartTime, EndTime, Shift, StaffMember, OverTimeHolidayEnum, FamilyDeathNote, PlannedHolidayNote | `NursingHome_Chart.Module/Model.DesignedDiffs.xafml:154` | two free-text fields; staff data |
| PriorityShiftAssignment | Claude | Notes, Date, IsActive, PriorityType, SchedulePeriod, ShiftType, StaffMember, CreatedDate | `DesignedDiffs.xafml:172` | OnSaving validation queries throw at 保存 only (census §5) |
| StaffConflict | Claude | Reason, ResolvedDate, IsActive, StaffMember1, StaffMember2, SubSection, CreatedDate | `DesignedDiffs.xafml:180` | as above |
| NightRoundsTime | Codex | 2 members (time/reference) | `DesignedDiffs.xafml:192` | no free text |
| ResidentAcceptability | Codex | SubSection (SubSectionView, `[NoForeignKey]`), Acceptability, Status | `DesignedDiffs.xafml:278` | business `Status` that the chart name list would have hidden (Q1 example) |
| CostItem | Codex | 2 members | `DesignedDiffs.xafml:86` | cost master; collection TenantCostByHomes not restored |

**Removed after pass 2:** PaidLeaveRequest (Claude's one A+B candidate). Its group was incomplete (Codex review C2, confirmed in source, §4.3), and one of its drivers rewrites the free text Reason. It goes to wave 2 with full groups.

**Left for wave 2 (both):** FaceSheet (its 31 members are references; the text lives in child objects edited through an embedded view, so it needs path members); TenantAccident and CloseCall (edited through TenantEvent_DetailView property paths; TenantEvent has the C member AccidentOrCloseCall).

**Recommendation (Claude):** start with the two both models proposed, TenantCase and ToDo, plus the owner's choice from the rest. The two criteria lead to different lists and no source check decides between them. Usage data was not read (no database access), so which screens are edited most is the owner's knowledge.

## 9. Test plan

**Source of the expectations.** The Codex `tests` call ran in a requirement-only directory: exactly one file, `REQUIREMENT.md`, holding the owner's words and rulings verbatim, with no source, no git and no AGENTS.md (`%LOCALAPPDATA%\collab\2026-09-30-generic-edit-draft-7faa17\tests\a1\out.md`, SHA-256 428B3793…0E8C). It returned 36 expectations, E1–E36, before Codex saw Claude's design. The isolation is by convention only: absolute-path reads remain possible. Its out.md cites only REQUIREMENT.md. Codex reported that some Japanese in REQUIREMENT.md came out garbled in its PowerShell read, so the exact UI strings in the tests come from the source, not from Codex.

Phase B writes the tests from this list. The owner rulings for red tests apply: a red test is escalated, never revised; one isolated rerun is allowed and both results go to the owner. Test type: **L** = logic (pure or in-memory XPO object space, as in `ChartSpace`, `TenantChartDraftTests.cs:30-47`); **S** = source/config scan; **B** = Dev2 browser script (behaviour).

| # | Test (what it asserts) | From | Type | Milestone |
|---|---|---|---|---|
| T1 | An unregistered type gets no capture and no restore, even on a DetailView; ListView edit and NEW-record recreation are not switched on for generic types | E1, E11 | L + S | 1 |
| T2 | Two registered types with a member of the same name follow their own policies; the chart Excluded names do not apply to a non-chart type | E2 | L | 1 |
| T3 | Census gate: every captured member of every registered non-chart type has an explicit disposition in the per-type table (A, group, 他の記録も変わります, 戻せません, excluded, each with a reason); an unknown member fails | E3 | L | wave 1 |
| T4 | NotRestorableOnExisting members are never applied to an existing record, including through group expansion or a companion | E4, E5, E16 | L | 1 |
| T5 | Golden snapshot: for each of the 23 chart types, MembersOf / GroupOf / HasSideEffect / IsNotRestorableOnExisting / ApplyOrder output equals the snapshot taken from the pre-refactor code | E24 | L | 1 |
| T6 | Every existing test in `TenantChartDraftTests.cs` (9 fixtures) and the DraftStoreTypes / audit tests pass unchanged | E24 | L + S | 1 |
| T7 | Capture: successive edits keep the first baseline; clearing a value is captured as null, not ""; each codec kind round-trips; an unrelated record's change does not enter the draft | E6 | L | 1 (generic core), wave 1 |
| T8 | Restore claims first: after a restore, the owner has exactly ONE live row for the record, at a higher revision, holding the applied values (no draft of the draft); a lost claim applies nothing | E7, E8 | L + B | 1 |
| T9 | Dirty flag: a restore that applied something leaves the object space modified; a restore that applied nothing (claim lost, all members failed, nothing selected, あとで) leaves `IsModified` as it was | E9, E13 | L + B | 1 |
| T10 | A failed or slow draft write never commits, rolls back or clears the business object space | E10, E36 | L | 1 |
| T11 | Fill in, never save: after はい, the database value (read through a fresh object space) is unchanged until 保存 | E12 | L + B | 1 |
| T12 | Conflict: 「他で変更されています」 + current value, unticked; covers an empty current value, a formatted value, a value equal to the draft (反映済み) and a change made after the popup opened (dropped at apply) | E14, E15 | L | 1 |
| T13 | Commit / rollback controllers: with the restore guard, a Committing or RollingBack raised during apply or by work posted during apply is cancelled and reported; after the guard closes, ordinary saving works (guard removed on success, cancel and exception) | E17, E18, E19 | L + B | 1 (guard), wave 1 (per type) |
| T14 | Per admitted type: open an existing record in its real view, add an unrelated pending edit, restore every captured member, drain posted callbacks: no commit, no rollback, and GetObjectsToSave holds only what the policy allows (census §8 plan) | E17, E18, E20 | B (Dev2) | wave 1 |
| T15 | C / D dispositions: 他の記録も変わります items are never pre-ticked; 戻せません items are shown and not applicable; no CareTreeSyncJob row is created by a restore of an admitted type | E20 | L + B | 1, wave 1 |
| T16 | Accident G1: IsGovernmentAccidentReport + AccidentReportNumber move as one tick; a driver-only restore leaves an undrafted AccidentReportNumber at its pre-restore value; the default 1 is filled only from 0 | E21, E23 | L | 1 |
| T17 | Accident G2: AccidentDescription + AccidentTime; StartOn applied before the driver; a populated AccidentTime is never overwritten by the default; a conflict on AccidentTime unticks the group | E22, E23 | L | 1 |
| T18 | Ownership: chart types keep F2 (all existing author tests); a generic type is owned by the login even while F2 is signed in; another login is never offered, listed or able to apply/delete the draft (owner predicate in the query, by Oid too) | E26, E27, E28 | L + B | 1 (chart), wave 1 |
| T19 | Every role denies EditDraft fully; the audit trail excludes it; the purge SQL has an EditDraft section (registry-vs-SQL test) | E28, E30, E31 | S | wave 1 |
| T20 | Retention: hidden at FirstCapturedOn + 7 days exactly (`ExpiresOn <= now` is hidden), not before; restore, search, discard and further typing never move ExpiresOn | E29 | L | wave 1 |
| T21 | Switches: global × per-type × missing × invalid = capture only when both are boolean true; chart switch unchanged; capture off keeps existing drafts restorable while the table exists | E32, E33 | L + S | 1, wave 1 |
| T22 | 事業所 re-check: a record whose 事業所 is not in the login's SecuritySections shows 「この記録は表示できません」 and no draft content; a non-writable member is 戻せません | S3 (single-model) | L + B | wave 1 |
| T23 | GeneralUser login: no capture under decision D6 (i) | S2 (single-model) | L | wave 1 |
| T24 | NHM: the Module mirror of EditDraft and the DraftStoreTypes line exist; NHM has no restore controllers | E35 | S | wave 1 |

T1–T12, T16, T17 and T21 need no database. T8, T9, T11, T13, T14, T15, T18 and T22 also need the Dev2 browser script. That script runs on the dev host (ports 5002–5004, `--artifacts-path`), never against production.

## 10. Owner decisions

Each decision lists its options, with the recommended one marked. Where the models differed, both positions are given. Nothing was averaged. The single-model decisions (D1, D6, D7) carry Claude's position only.

| # | Decision | Options (★ = recommended) | Positions |
|---|---|---|---|
| D1 | Storage for generic drafts (SINGLE-MODEL) | (a) generalize `dbo.TenantChartEditDraft` in place; ★(b) new `EditDraft` table, chart table kept; (c) new table + migrate chart rows | Claude only (§2) |
| D2 | Engine placement | ★ CareCrew.Blazor.Server/Infrastructure/EditDrafts; framework-free core in NursingHome_Chart.Module | Claude: Blazor. Codex diag: Module core; Codex review withdrew it. Settled in pass 2 |
| D3 | Opt-in mechanism | ★ explicit registry + per-type policy classes; attribute on the BO | both: registry |
| D4 | Central list | ★ separate 「入力控」 for generic types next to 「カルテ入力控」 in 歯車 → 復元; one list for everything | both: separate. Claude's reason is ownership (F2 vs login). Codex's review notes that separate lists are "not logically required merely because identities differ" and leaves that reason to the security section |
| D5 | Switches | ★ charts keep `TenantChartDraftCapture:Enabled`; generic = global AND per-type key; Development on, Production off; per-type off keeps existing drafts restorable until expiry — or move charts under the new keys now | both: keep the chart key during migration |
| D6 | GeneralUser logins on non-chart screens (SINGLE-MODEL) | ★(i) no capture under a GeneralUser login; (ii) allow (the draft is shared with everyone on that login); (iii) F2 (contradicts the 2026-09-28 ruling) | Claude only (§3 S2) |
| D7 | Write-permission + 事業所 re-check (§3 S3 checks 3–4) for the 23 chart types too (SINGLE-MODEL) | ★ generic types only in milestone 1; the charts get it as a separate, owner-approved behaviour change — or include it in milestone 1 | Claude only |
| D8 | Types with commit / rollback controllers (SubSectionServiceItemTemplate, LongTermHoliday, StaffAttendance(_V2), OverTimeShiftCountByMonth_V2) | ★ keep them out (admission); exclude only the trigger member; suppress the controller during restore | both: keep out; LongTermHoliday enum 戻せません if it is ever admitted |
| D9 | Default for other-record (C) members of new types | ★ 戻せません + a read-only display for drafts with nothing restorable; selectable with 「（他の記録も変わります）」; not captured | Claude proposed 戻せません; Codex review C8 showed a draft with only 戻せません members is never displayed today, so the ★ option needs the read-only display. Codex diag: C types join only by a separate decision per type (compatible) |
| D10 | Types with CareTree sync (D) members | ★ out of the early waves; later with an explicit restore/save rule | both |
| D11 | Wave 1 types | (see §8) both: TenantCase, ToDo. Claude also: StaffOverTimeHoliday, PriorityShiftAssignment, StaffConflict. Codex also: NightRoundsTime, ResidentAcceptability, CostItem | criteria differ (free text vs small surface); no check decides it. Claude recommends TenantCase + ToDo + the owner's picks |
| D12 | Runtime cancel guard (§4.1 layer 2) | ★ include it as a logged backstop, with its limits stated; leave it out and rely on admission + T14 | Claude proposed it; Codex review C1 accepted the API but showed it does not cover nested posts, work done before Committing, or other object spaces |
| D13 | Publishing milestone 1 | ★ only after the Dev2 browser pass of the カルテ入力控 checklist (the code is live in production) | Claude |

## 11. Deployment and consumers

| Item | Build / channel | Notes |
|---|---|---|
| Milestone 1: generic engine + charts on it + Q3 groups | CareCrew Blazor publish only | No schema change under storage option (b). This code runs LIVE, because `TenantChartDraftCapture:Enabled` is true in Production (`TenantChartDraftTests.cs:506` pins it). Hence the golden-snapshot test (T5), and the Dev2 browser pass before publishing |
| Generic table EditDraft (wave 1) | NHM schema run (fix-523 runbook) + NHM release | Only when the first non-chart type is switched on; §2 sequence |
| Deny rows / audit exclusion | through DraftStoreTypes (the Updater seeds them on the NHM schema run) | verify: deny row count = role count, all Deny |
| Purge section | owner-installed procedure-only copy | registry-vs-SQL test |
| Switches | appsettings.Development.json on; Production off | owner turns them on per type |
| Consumers | CareCrew Blazor only. NHM WinForms: class mirror only, no restore (owner ruling). ChartWorkflowServiceV2: not a consumer (it never reads draft tables). ReportDataV2 / report layouts: not applicable. Mirrored files: the EditDraft class and DraftStoreTypes. | |
| Timing | no pay-window or month-end dependency | |

## 12. Contribution log

### What Claude did

- Phase 0 preflight (below).
- Created the worktree and copied the census into it.
- Read the census in full and the カルテ入力控 source: policy, restorer, payload, writer, author, switch, display, capture, restore, popup and list controllers, bridge, BO, DraftStoreTypes, permissions, purge SQL, tests.
- Wrote an independent design (`claude-diagnosis.md`) BEFORE reading any Codex output.
- Wrote the single-model storage and security sections alone. Codex never saw them.
- Verified every Codex citation it relied on: the test names, `csproj:38-50`, `SubSectionServiceItemTemplateControllerBlazor.cs:21-29`, `PaidLeaveRequest.cs`, the popup controller, `TenantChartDraftRestorePlan.cs:74`, `TenantChartDraftDisplay.cs:37-38`.
- Fetched the DevExpress 26.1 docs for the Committing/RollingBack cancel.
- Combined the result and wrote this document.

Claude's errors, caught by Codex:

- PaidLeaveRequest's group listed only the drivers, and one driver rewrites the free text Reason (review C2).
- Claude counted 19 captured members for PaidLeaveRequest; there are 18.
- The "exactly one row" test wording was wrong when other editing contexts hold rows (C4).
- The approved-view list would have changed chart capture, which has none today (C5).
- The golden snapshot alone was presented as enough proof (C6).
- The census gate misses changed effects on existing members (C7).
- 戻せません as the default hides drafts that have nothing restorable (C8).
- The sentinel's coverage was overstated (C1).

### What ChatGPT (Codex) did

codex-cli 0.153.4, model gpt-6-astra, effort medium requested (the global `~/.codex/config.toml` says max; the launcher passes `-c model_reasoning_effort=medium`).

- **diag:** an independent design. It found the one-group limit and the commit/rollback controllers independently, and that SubSectionServiceItemTemplate checks the property name, not the object. It insisted that chart NEW-record restore is preserved. It proposed Module placement and suppressing capture during restore; its review withdrew both. Wave list ToDo, NightRoundsTime, ResidentAcceptability, CostItem, TenantCase.
- **tests:** 36 requirement-only expectations, E1–E36 (§9).
- **review:** 8 defects (C1–C8), all accepted, 4 confirmed by Claude in source. It raised input_mismatch on the PaidLeaveRequest count (correct).
- **combined:** a merged draft consistent with the settled points. It added that the read-only display must show the full text. It claimed the chart switch test (`Tests:495-508`) "must not be run"; that is rejected for Phase B (F21).

### Found issues, by tool

| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| F1 | One dependency group per type; Q3 needs two | both (A2 / C1) | confirmed | `TenantChartDraftPolicy.cs:74-105`; `TenantChartDraftRestorer.cs:88-94` | Q3 impossible without it / certain / source / n.a. | T16, T17 | multi-group, commit 2 |
| F2 | Controllers commit / roll back on a value change; a restore-local flag cannot stop them | both | confirmed | `SubSectionServiceItemTemplateControllerBlazor.cs:29`; `LongTermHolidayControllerBlazor.cs:88`; `AttendanceAutoSaveControllerBlazor.cs:153` | restore saves or discards / when admitted / source / unknown | T14 | admission first (§4.1) |
| F3 | Q1: per-type dispositions, never the chart name list | both | confirmed | census §2; `TenantChartDraftPolicy.cs:40-51` | hidden C/D effects / any new type / source / n.a. | T3 | registry + gate |
| F4 | Capture during apply: suppress (Codex diag) vs keep (Claude) | disagreement | settled by source read: today it is not suppressed | `TenantChartDraftCaptureControllerBlazor.cs:63-87, :231-310, :671-682` | behaviour change for charts if suppressed / certain / source / n.a. | T8 scenario | not suppressed (Codex withdrew) |
| F5 | Engine placement Module (Codex diag) vs Blazor (Claude) | disagreement | settled in pass 2 (Codex withdrew) | Tests csproj:38-50; Module csproj Newtonsoft | structure / — / source / n.a. | — | Blazor (D2) |
| F6 | Template controller checks name, not object | Codex | confirmed | `SubSectionServiceItemTemplateControllerBlazor.cs:23` | commit from another object in the space / conditional / source / unknown | — | type out (D8) |
| F7 | Chart NEW-record restore must be preserved | both | confirmed | `TenantChartDraftListControllerBlazor.cs:435-590` | shipped behaviour lost / certain if removed / source / live feature | existing tests | kept chart-specific |
| F8 | Golden snapshot of the 23 chart policies before extraction | Claude | accepted, insufficient alone (Codex C6) | `TenantChartDraftTests.cs:23-27` | migration regressions / moderate / reasoning / n.a. | T5 + scenarios | §7 |
| F9 | Committing/RollingBack cancel guard during restore | Claude | accepted as backstop; limits from Codex C1 | dxdocs 26.1; `TenantCostChangedControllerBlazor.cs:253-257` | late commit / conditional / docs + reasoning / unknown | T13 | D12 |
| F10 | PaidLeaveRequest group incomplete; driver rewrites Reason | Codex | confirmed | `PaidLeaveRequest.cs:96-101, :175-182, :395-458, :504-545` | unticked fields change / when restored / source / unknown | replay each driver | removed from wave 1 |
| F11 | Popup 破棄 is chart-wired | Codex | confirmed | `TenantChartDraftRestorePopupControllerBlazor.cs:18, :59-68` | wrong store/owner / certain if reused / source / n.a. | spy test | generic routing |
| F12 | "Exactly one row" test is wrong with several contexts | Codex | accepted | `TenantChartDraftCaptureControllerBlazor.cs:63-87, :303-306` | false test failures / likely / reasoning / n.a. | T8 wording | "no additional row" |
| F13 | Chart capture has no view allowlist; adding one changes behaviour | Codex | confirmed | `TenantChartDraftCaptureControllerBlazor.cs:31, :112-143` | capture stops on a chart route / if added / source / live | route inventory | allowlist non-chart only |
| F14 | Census gate misses changed effects on existing members | Codex | accepted | `PaidLeaveRequest.cs` helpers | silent new effect / over time / reasoning / n.a. | mutation test | T3b source-hash trigger |
| F15 | All-戻せません drafts are never shown | Codex | confirmed | `TenantChartDraftRestorePlan.cs:74`; `TenantChartDraftRestoreControllerBlazor.cs:197-201` | typed text not recoverable / when default applies / source / n.a. | open such a draft | read-only display (D9) |
| F16 | Read-only display must show full text (grid shortens to 60 chars) | Codex (combined) | confirmed | `TenantChartDraftDisplay.cs:37-38` | text cut / always for long text / source / n.a. | UI check | §4.4 |
| F17 | PaidLeaveRequest has 18 captured members, not 19 | Codex | confirmed (Claude error) | census CSV | count only | CSV count | corrected |
| F18 | StartOn must be applied before the G2 driver | Codex | confirmed, already true | `TenantChartDraftPolicy.cs:54-58`; `TenantChartDraftRestorer.cs:87` | wrong AccidentTime default / if order changed / source / n.a. | T17 | kept |
| F19 | Reject unaudited shared-object-space contexts | Codex | accepted | F6 example | effects from other objects / conditional / reasoning / unknown | T14 | ApprovedViewIds (non-chart) |
| F20 | Wave-1 list and criteria | disagreement | open (no check decides it) | §8 | — | owner | D11 |
| F21 | "Do not run the chart switch test Tests:495-508" | Codex (combined) | rejected for Phase B | `TenantChartDraftTests.cs:496-500` reads one key in-process and prints nothing; it has been part of the suite since 2026-09-29; the restriction was Codex's own call rule | — | — | test runs in Phase B as today |
| F22 | LongTermHoliday enum should be 戻せません if ever admitted | Codex | accepted | `LongTermHolidayControllerBlazor.cs:76-88` | business rule bypassed / if admitted / source / n.a. | — | D8 note |
| F23 | GeneralUser login would own shared non-chart drafts | Claude (single-model) | owner decision | owner note 2026-09-29; `TenantChartDraftAuthor.cs:27-33` | drafts visible across staff on one login / likely under department logins / reasoning / unknown | T23 | D6 |
| F24 | Chart restore has no write-permission / 事業所 re-check | Claude (single-model) | owner decision | `TenantChartDraftRestoreControllerBlazor.cs:252-271` | restore of a member the user cannot write / low (secured OS fails at save) / source / unknown | T22 | D7 |
| F25 | Storage: new table vs generalize vs migrate | Claude (single-model) | owner decision | §2 | production schema/data / — / source + KB / n.a. | — | D1 (b) |
| F26 | Japanese in REQUIREMENT.md read garbled by Codex | Codex (tests) | noted | tests out.md | UI strings taken from source instead | — | recorded |

Found independently by both: F1, F2, F3, F7, and the separate 「入力控」 list, the registry opt-in and admission before suppression. This is coverage, not confidence: no build, test or runtime check was run.

### Codex calls

| Run | Call | Attempt | Path | Started | Duration | state | validation | exit | PID | Model / effort requested | Effective effort | reasoning_output_tokens | Search | MCP tools called | activity: commands / non-zero / file_change / outside-repo | prompt SHA-256 | out SHA-256 | Parity pack | codex-cli |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2026-09-30-generic-edit-draft-7faa17 | diag | a1 | %LOCALAPPDATA%\collab\2026-09-30-generic-edit-draft-7faa17\diag\a1 | 19:39:51 | 5.2 min | success | ok | 0 | 23580 | gpt-6-astra / medium | not observable | 304 | off | lookup_known_fix, dxdocs search, dxdocs get_content | 7 / 0 / 0 / 2 (powershell.exe, plugin cache skill file) | FC17F0F4…9E89 | 916F00E7…06FF | v1 | 0.153.4 |
| same | tests | a1 | …\tests\a1 (Repo = requirement-only `tests\req`, -SkipGitCheck) | 19:42:10 | 2.0 min | success | ok | 0 | 36448 | gpt-6-astra / medium | not observable | 71 | off | none | 3 / 0 / 0 / 1 (powershell.exe) | A6569597…F14D | 428B3793…0E8C | none (requirement only) | 0.153.4 |
| same | review | a1 | …\review\a1 | 19:46:27 | 3.6 min | success | ok | 0 | 35960 | gpt-6-astra / medium | not observable | 552 | off | lookup_known_fix, dxdocs search ×2, get_content ×2 | 7 / 0 / 0 / 1 | 21FCBCBF…06C4 | 02F4DC13…F63E | v2 | 0.153.4 |
| same | combined | a1 | …\combined\a1 | 19:50:59 | 3.7 min | success | ok | 0 | 32700 | gpt-6-astra / medium | not observable | 75 | off | lookup_known_fix, dxdocs search, get_content ×2 | 3 / 0 / 0 / 1 | 34140D10…9810 | F09E9C85…BEF | v3 | 0.153.4 |

No failed attempts, no retries. Isolation of the `tests` call is by convention: absolute-path reads remain possible. Its out.md cites only REQUIREMENT.md.

### Setup checks (Phase 0)

1. BASH_MAX_TIMEOUT_MS: present.
2. Read-only query connection: **not run**. The brief forbids any database access, and no query connection was used (preflight-sql.sql not executed).
3. Repo trusted: present (the hook fired).
4. Manifest: all 7 hashes OK.
5. Hook fires: the hook blocked the item-6 command because its text contains a push (that counts as the item-5 evidence). The harmless Monitor was not blocked. The first Monitor attempt used `Get-Date`, which failed with exit 127 because Monitor runs bash; `date` worked. This was not a hook block.
6. collab.rules: the file exists and contains `decision = "forbidden"` rules. **The execpolicy check was not run**, because the hook blocked it. It was reported, not reworded (ground rule 13).
7. prompt-input: the AGENTS.md "Working with Claude" section is present; CLAUDE.md is not (it was pasted as item 0 of the parity pack).
8. Tool boundary: no DB, deploy or push MCP tool is exposed. The KB write tools were not used.
9. Tool parity: Codex has blazor-knowledge-base (`enabled_tools` = the 9 read tools) and dxdocs.
10. Models: gpt-6-astra is listed and supports medium.
11. Run id 7faa17; scratch and salt.txt created; binary `C:\Users\owner\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe`, 0.153.4.
12. Snapshot 7907169 (main repo and worktree). Status: the 2 untracked census files (+ this design doc in the worktree).
13. Drift: `~/.codex/config.toml` has `model_reasoning_effort = "max"` against the owner rule of medium. The launcher overrides it per call. The owner edits the config if wanted.
14. Web search off.

Outputs: `%LOCALAPPDATA%\collab\2026-09-30-generic-edit-draft-7faa17\preflight\`.

### Redaction

None needed. Only source, model files, the census and one KB lookup were read. No database was touched, and no personal data was queried or pasted.

### Inputs Codex did not have

- Sections 2 and 3 (storage, security) and the MEMORY.md note on the 事業所 access model. Excluded on purpose (single-model); D1, D6 and D7 rest on them.
- The full census CSV was not pasted (1.3 MB); Codex read it from the worktree.
- The KB fix-523 / fix-453 text Claude read; Codex ran its own KB lookup.
- Claude's in-progress design doc (untracked in the worktree during the review and combined calls; Codex declared it did not read it).

### Passes used

2 cross-model passes (diag, then review), plus `combined` and `tests`, which adjudicate nothing. 4 Codex calls, 4 attempts, all success/ok.

### Run ledger

```
{"run":"2026-09-30-generic-edit-draft-7faa17","date":"2026-09-30","topic":"generic-edit-draft-design","attempts":[{"call":"combined","attempt":1,"state":"success","validation":"ok","accepted":true,"exit":0,"minutes":3.7,"commands":3,"nonzero_exits":0,"outside_repo":1,"file_changes":0,"reasoning_tokens":75,"output_tokens":6717,"search":false},{"call":"diag","attempt":1,"state":"success","validation":"ok","accepted":true,"exit":0,"minutes":5.2,"commands":7,"nonzero_exits":0,"outside_repo":2,"file_changes":0,"reasoning_tokens":304,"output_tokens":8349,"search":false},{"call":"review","attempt":1,"state":"success","validation":"ok","accepted":true,"exit":0,"minutes":3.6,"commands":7,"nonzero_exits":0,"outside_repo":1,"file_changes":0,"reasoning_tokens":552,"output_tokens":5960,"search":false},{"call":"tests","attempt":1,"state":"success","validation":"ok","accepted":true,"exit":0,"minutes":2,"commands":3,"nonzero_exits":0,"outside_repo":1,"file_changes":0,"reasoning_tokens":71,"output_tokens":3420,"search":false}],"findings":{"claude_confirmed":7,"claude_rejected":1,"codex_confirmed":12,"codex_rejected":3,"both":4,"unverifiable":0,"open":1},"correlated_error_events":0,"escalated_to_owner":1,"passes":2,"hook_false_positives":1}
```

## 13. Not verified / open questions

- Nothing was built, tested or run in a browser. Everything is a source read, one KB lookup (fix-523, fix-453) and one docs fetch: DevExpress 26.1 `Committing` / `RollingBack` cancel, not executed on 26.1.4.
- Production state is quoted from the brief and KB fix-523, not observed (no database access).
- Whether the cancel guard behaves as documented inside the real dispatcher, including nested posts, exceptions and view deactivation (T13).
- Whether the wave-1 candidates open as root DetailViews in real use, and which controllers are active on their screens (T14 before admission).
- Which screens staff edit most (no usage data).
- Whether XPO raises ObjectChanged when a restored value equals the current value (census §8). Apply already skips unchanged preserved values (`TenantChartDraftRestorer.cs:167`).
- NHM source parity for DraftStoreTypes and the future EditDraft class was not checked.
- The generic payload schema version and whether chart payloads need any change: the design keeps schema 1 for charts; not tested.
- Open owner questions: D1–D13 (§10).
