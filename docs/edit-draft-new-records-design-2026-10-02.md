# 入力控 for NEW (never saved) records — design (2026-10-02)

Collaborator run `2026-10-02-edit-draft-new-records-139e0a`. Analysts: Claude (Opus 5.5) and ChatGPT (Codex CLI 0.153.4, gpt-6-astra, effort xhigh).
Worktree `C:\Users\owner\source\repos\CareCrew-newrecord`, branch `feature/edit-draft-new-records`, HEAD `132782b1`. Design only: no code, no database, no schema.
Paths below are repo-relative; `Core/` = `Xaf.EditDraft.Core/`, `Blazor/` = `Xaf.EditDraft.Blazor/`, `Charts/` = `CareCrew.Blazor.Server/Controllers/Tenants/TenantCharts/`.

## 0. Combined answer

A new record's unsaved input is lost on F5 today because every part of the generic edit-draft engine assumes an EXISTING record: capture refuses a new object (the owner's 18:21:24 log line), the offer and the 「入力控」 list can only reopen a record by its Oid, and apply re-reads the record from the database; after F5 the new-record screen is not restored at all and the person lands on the type's list. The design keys a new-record draft by `TargetOid = Guid.Empty` in the existing `dbo.EditDraft` row (one row per editing screen, owner-scoped as today), stores with the first real edit the context the record depends on (for 残業・有給: 職員, 日付, 開始時刻, 終了時刻), and keeps the screen objects' own Oids in the payload so that a record which was in fact saved is not created twice. The person gets the input back from the 「入力控」 list — a 「新規」 row whose 開く creates a fresh, unsaved record of that type filled from the draft, after a Create-permission and 事業所 check — and from a notice on the type's list; the first 保存 deletes the draft. No schema change and no NHM change; the first types are 残業・有給, ToDo and 夜間巡回時間. It does not cover new rows typed inline in a list, new 利用者事業所, graphs of several unsaved objects, or a new record whose original screen is still open in another tab when the draft is recreated (a duplicate is then possible — owner decision D11).

## 1. Status

Design only, 2026-10-02. Nothing implemented, nothing committed. The write-up is uncommitted in the worktree. Fifteen owner decisions are open (§8); six of them are points where the two analysts still disagree, stated with both positions.

## 2. Why a new record's input is not restorable today

| # | Gate | Evidence | Found by |
|---|---|---|---|
| G1 | Capture admission refuses a new object | `Core/EditDraftCaptureControllerBlazor.cs:63-65` (`isRoot && !isNew`), `:207-216` writes exactly the owner's line (dev host log 2026-10-02 18:21:24.355) | both |
| G2 | The new object already has its final Oid: `BaseObject.AfterConstruction` assigns it, `OnSaving` keeps it (installed DevExpress 26.1.4 source `DevExpress.Persistent.BaseImpl.Xpo/BaseObject.cs:59, :60-66, :68-72`; CareCrew never changes `OidInitializationMode`). Lifting G1 alone would write rows keyed by that Oid, which no later screen has | `:266`, `:469-484`, `:508`; `Core/EditDraftWriter.cs:162` | both |
| G3 | The offer is for existing records and matches on the Oid | `Blazor/EditDraftRestoreControllerBlazor.cs:140` (new object → return), `:184`, `:190` | both |
| G4 | The list only reopens an existing record; every row says 既存 | `Blazor/EditDraftListControllerBlazor.cs:297`, `:353-355` | both |
| G5 | Apply needs the Oid and a fresh database copy | `Core/EditDraftAccessRule.cs:22`; `Blazor/EditDraftRestoreControllerBlazor.cs:456-458` | both |
| G6 | After F5 there is no screen for the new record: the layout restore skips new-object shortcuts; in the owner's run the list became the active view (18:21:35.729 main window registered, 18:21:36.312 `StaffOverTimeHoliday_ListView` capture ready, no DetailView activation, 18:21:39 the per-type list read 0 rows) | `CareCrew.Blazor.Server/SafeMdiShowViewStrategy.cs:44-52`; log | both |
| G7 | The store has no new-record marker columns (the class comment says a new-record wave "adds them") | `NursingHome_Chart.Module/BusinessObjects/EditDraft.cs:17-18`; `docs/generic-edit-draft-design-2026-09-30.md:136` | Claude |
| G8 | Inline ListView capture also refuses new rows | `Core/EditDraftListRules.cs:21-23` | Codex |

The 2026-09-30 reason for excluding new records ("an unsaved record has no stable Oid to find again after F5") is right about F5 (G6: the object is gone) but not about the Oid itself (G2).

## 3. Ruled out

| Question | Option | Why not | Raised by |
|---|---|---|---|
| Identity | Remove only the `!isNew` refusal | G3–G5 remain; rows would be keyed by an Oid no later screen has | both |
| Identity | TargetOid = the new object's own Oid, "new" flag in the payload | `TrySupersede` never rewrites TargetOid (`Core/EditDraftWriter.cs:204-209`), so after a recreate the row names the first object; every existing-record path would have to read the payload to tell "never saved" from "deleted" | Claude (Codex review: a reason to reject this implementation, not proof no such design works) |
| Identity | "New" whenever the target cannot be loaded | A deleted existing record must not become permission to create one; the chart keeps the two apart (`Charts/TenantChartDraftListControllerBlazor.cs:428-436`) | Codex |
| Identity | New columns IsNew + ProvisionalOid (chart store shape) or a NewRecordKey | Not needed: TargetOid = Empty says "new" (an existing-record row can never have it), the provisional Oids fit in the payload, DraftKey already identifies a row; a column is schema = owner + single-model + NHM release | both |
| Identity | ContextText or SubSectionOid as parent identity | display text and office scope, not a resident or a creation route (`Core/EditDraftStoreBase.cs:83-95`) | Codex |
| Baseline | Empty baseline for new (chart rule, `Charts/TenantChartDraftCaptureControllerBlazor.cs:231-235`) | every bare notification of an unchanged default would become an entry; the generic BindTo already snapshots after AfterConstruction and the InitializingGetters (`Core/EditDraftCaptureControllerBlazor.cs:220-221`) | both |
| Baseline | Store only the members the person changed | defaults that depend on the time or the login are re-evaluated on the recreated object: `StaffOverTimeHoliday.AfterConstruction` sets 職員 = the login's staff member, 日付 = today, 開始/終了 = now / now + 15 min (`NursingHome_Chart.Module/BusinessObjects/TimeTracking/StaffOverTimeHoliday.cs:48-60`); a draft typed yesterday would move to today | both |
| Offer | Merge all new drafts of a type into one offer (D16) | two new drafts are two intended records; D16 merges drafts of ONE record (`Core/EditDraftOfferMerge.cs:7-15`) | Codex |
| Offer | Recover only through a reopened DetailView | G6 | both |
| Offer | Offer when 新規 is pressed, in this wave | the New action's object can already carry context set by its route (`Charts/NewTenantChartEventControllerBlazor.cs:184-192`), several drafts need a chooser, it re-offers on every 新規 until 破棄; the owner ruling names the list / list header | both (deferred, not rejected) |
| Recreate | Run the type's own New action | its items depend on the list's model (KB fix-531: AllowNew=False → EmptyItems; ShowOnView on a DetailView); the list popup is not the list frame | both |
| Recreate | Unrestricted `EditDraftRestorer.Apply` | does not enforce 戻せません; the current `isNew` branch of `BuildItems` skips `NotRestorableOnExisting` (`Core/EditDraftRestorer.cs:29-31`) | Codex |
| Recreate | Copy the chart recreate as is | it has no explicit Create check (`Charts/TenantChartDraftListControllerBlazor.cs:547-586`); fix-531's direct creation has one (`Charts/NewTenantChartEventControllerBlazor.cs:199-208`) | Codex |
| Types | All five wave-1 types at once | TenantSubSection: new records come from nested-space clone flows (`CareCrew.Blazor.Server/Controllers/Tenants/TenantSubSections/NewTenantSubSectionControllerBlazor.cs:494-521, :582-604`) and a Room change creates a TenantTenshitsu and enqueues CareTree sync (`:351-376`); TenantCase: `CaseNumber` is `[AutoIncrement]` (`NursingHome_Chart.Module/BusinessObjects/Tenants/TenantCase.cs:66-73`) with unknown assignment time | both |

## 4. Design

### 4.1 Identity and finding the draft again (brief item 1) — both

| Value | Meaning for a new-record draft |
|---|---|
| `TargetOid` | **Guid.Empty** = never saved (same rule as the chart store, `NursingHome_Chart.Module/BusinessObjects/TenantCharts/TenantChartEditDraft.cs:84-87`) |
| `DraftKey` | identity of one row (random per row, `Core/EditDraftWriter.cs:171`); unchanged |
| `EditorInstanceId` | the screen activation that holds the row; changes on a claim; unchanged rules |
| `OwnerUserOid` | the login; in every statement; unchanged |
| `SubSectionOid` | `policy.SubSectionOf(screen object)` at each write (`TrySupersede` already updates it, `:205-206`) |
| `ContextText` | type caption + `ContextDateOf` (e.g. 「残業・有給／2026/10/02」); never a name; unchanged rule |
| `ViewId` | the DetailView id |
| payload header (new, optional) | `prov`: the Oids of every screen object this draft has been typed on or recreated into, newest first (the original object and each recreated one) |
| payload entries | the changed members PLUS the seeded context members (`Seeded = true`, `Core/EditDraftPayload.cs:22-23`) |

- **After F5**: owner (in the query) + ObjectType + TargetOid = Empty + live. `ListOwn` (`Core/EditDraftWriter.cs:343-361`) already returns these rows; the row kind is derived from TargetOid. No editor id from the dead circuit is needed.
- **Two tabs**: two activations, two rows, two 「新規」 list rows; never merged; each recreated into its own record.
- **Expiry**: unchanged (first capture + 7 days, never extended; claims and writes require `ExpiresOn > now`).
- **Parent context** (Codex): a later wave that admits a type whose creating route FIXES a parent (a new 利用者事業所 under a 利用者) needs the payload header to say which context was fixed by the route and which members are ordinary edits, so that the restore never moves a draft to another parent. None of the first three types has such a route (they are created from their own root lists).

### 4.2 Capture (brief item 2)

1. `EditDraftTypePolicy.AllowNewRecords` (default false). Admission: generic policy, root view, approved view, and `!isNew || AllowNewRecords`. A runtime switch `EditDraftCapture:NewRecords:Enabled` (default off in Production) beside the existing per-type switch, so new-record capture can be turned off without a release (D15). — Claude; Codex `AllowNew` opt-in the same.
2. Baseline: the existing order — InitializingGetters under suppression, then the snapshot (`Core/EditDraftCaptureControllerBlazor.cs:220-221`) — gives the post-construction defaults. A FIRST notification whose value equals the baseline adds nothing (`:339-340`). Once an entry exists it stays even if the person returns the value to the baseline (`Core/EditDraftPayload.cs:45-65`) — see O4 / D8. — both (wording corrected by Codex).
3. **Seeding** (both): on a new object the first real edit also records the policy's `ReconstructionOrder` members with `Seeded = true`. Proposed lists: 残業・有給 `StaffMember, Date, StartTime, EndTime` (Date first: the time setters move their value onto `Date.Date`, `StaffOverTimeHoliday.cs:104-115, :127-138`); ToDo and 夜間巡回時間 none (no defaults, `ToDo.cs:24-27`, `NightRoundsTime.cs:16-20`). The members already have decision lines (needed: `Core/EditDraftMembers.cs:31-35`).
4. **Seeding rules** (Codex; Claude's first design missed them). Today, seeding inside `StartPayload` would record the triggering member's NEW value as a seed, `CaptureMember` would then find it equal and return false, and no write would be posted (`Core/EditDraftCaptureControllerBlazor.cs:298-306, :341`). Copying the chart's `!existing.Seeded` condition (`Charts/TenantChartDraftCaptureControllerBlazor.cs:329`) alone is not enough either: a bare notification on an untouched seeded member would then be promoted to an edit and written (Codex combined). The rule: for a member with no entry or only a SEEDED entry, the change is genuine only when its value differs from the BASELINE (`:339-340`); a genuine change is upserted as a typed entry (`Seeded` flips to false, `Core/EditDraftPayload.cs:59-62`) and posts a write; a non-genuine one does nothing. A just-started payload that holds no genuine change is discarded whole (seeds included), so a bare notification leaves nothing that would block an adoption (`:82-86`, `:304`).
5. **Fresh-start paths** (Codex): `RebuildAfterFreshStart` (`:386-406`) and `RetiredFreshStart` (`:426-446`) rebuild a payload from never-stored entries only; both must keep the seeded entries and the header, as the chart does (`Charts/TenantChartDraftCaptureControllerBlazor.cs:395, :438`).
6. **Keying per write** (both): `BuildSnapshot` decides each time — while `IsNewObject(record)`: TargetOid = Empty, header `prov` led by the screen object's Oid, `EditDraftSeed.IsNew = true` (plain data, not a column); after the save: TargetOid = the Oid. `StartWrite` (`:508`) and `EditDraftWriter.Create` (`:162`) accept an empty TargetOid ONLY with `IsNew`; a targetless existing row stays impossible.
7. **References** (職員, 事業所): stored as Oid + display text; restored only if the Oid resolves in the destination secured space, otherwise counted as failed, never nulled (`Core/EditDraftRestorer.cs:139-146`). A reference to another never-saved object cannot be restored by Oid (Codex) — no recovery of unsaved object graphs in this design.
8. **First 保存 = delete** (both recommend; owner wording "re-key on save" — D3): today's `ObjectSpace_Committed` deletes the screen's row after a successful commit (`:628-655`; a failed commit raises no Committed, KB fix-497; a create still running hands its row back, `Core/DraftWriteSlot.cs:117-133, :265-277`). The screen's next edit is then captured as an existing-record draft keyed by the saved Oid (item 6). That is what "re-key" would achieve, without keeping a row whose values are now on the record. (Claude first wrote "every drafted value is on the record"; Codex showed that is not true after a partial restore — the not-applied entries are shown to the person instead, §4.4 step 10.)
9. Unchanged: D6 (a GeneralUser login has no owner — nothing captured), switches, fix-529 getters.

### 4.3 Offer surface (brief item 3) — decision: (a) + (b) now, (c) later — both

- **(a) 「入力控」 list** (gear entry, and the per-type header action on the list tab, which was already active in the owner's run at 18:21:36): a new-record row shows 状態 「新規」 (today always 既存, `Blazor/EditDraftListControllerBlazor.cs:297`) and 対象 = ContextText (`ResolveTarget` already returns it for Guid.Empty, `:318`). 開く on that row = recreate (§4.4). This is the owner's ruling.
- **(b) a notice on the type's own ListView**, once per activation, when the login has live, readable new-record draft ROWS of that type. After F5 the person is on that list (G6). It counts live rows with TargetOid = Empty and a readable `PayloadSchemaVersion`; `ListOwnTargets` cannot tell readability (it projects only target Oids, `Core/EditDraftWriter.cs:375-378`), so the notice reads through `ListOwn` (`:343-361`) or a projection that includes the version (Codex review C8, combined). Whether a row's record was in fact saved is decided at 開く, so the wording claims no more than a count of rows — Codex: 「新規の入力控が n 件あります。」; Claude: 「保存されていない新規入力が n 件あります。上の「入力控」から開けます。」 (D5).
- **(c) offer when 新規 is pressed**: later wave (§3).

### 4.4 Recreate (開く on a 「新規」 row) and restore on a fresh object (brief item 4)

Order. Every refusal before step 5 changes nothing; every failure after step 5 disposes the new object space, presents nothing as success, logs the step, and leaves the row live at its new revision (the next 開く re-reads it).

1. Owner = the login, re-resolved now; `ReadOwn` (owner in the query); live; payload readable; policy generic with `AllowNewRecords`; switches.
2. Determinable refusals first: a draft whose entries are all 戻せません/unknown → the D9 read-only display (`Blazor/EditDraftRestoreControllerBlazor.cs:297-327`), nothing created.
3. "Already saved?": if any Oid in `prov` names a record that exists (secured read), nothing is created; 「この入力控の記録はすでに保存されています。」 with an action to open that record (chart precedent `Charts/TenantChartDraftListControllerBlazor.cs:490-513`); a failed check asks and never creates silently (chart C8, `:441-460`).
4. Security checks before creating (§5: Create permission, list model AllowNew, approved view AllowEdit, the draft's SubSectionOid).
5. Create the object in memory: `os = Application.CreateObjectSpace(type)`, `obj = os.CreateObject(type)` (AfterConstruction runs; nothing is saved until the person saves — dxdocs 26.1 CreateObject). Because step 6 needs `obj.Oid`, the candidate exists BEFORE the claim (a change from the chart's claim-before-create order, Codex combined "Structural changes" 5): a losing attempt applies nothing, shows nothing, saves nothing and disposes the candidate's object space. AfterConstruction side effects of a discarded candidate must therefore be checked per type before it opts in (none in the three first-wave types' `AfterConstruction`; TenantCase's `[AutoIncrement]` is one reason it waits, D10).
6. **Claim and header in ONE statement**: a new owner-scoped, revision-fenced writer method sets the new editor id, Revision + 1 and the payload with `obj.Oid` added to `prov`. A lost claim → dispose, 「この入力控は戻せません（ほかの画面で戻されたか、変更されたか、期限切れです）。」. (Claude's answer to Codex review C3 case 1 — "recreate, then save with no further edit" would otherwise leave only the old Oid in the row. Written after pass 2; not cross-reviewed.) The attached payload and every later rewrite keep the same history.
7. InitializingGetters on the fresh object under suppression, then `ApplyNew` under `EditDraftRestoreGuard` closed with `CloseAfterPostedWork` (`Blazor/EditDraftRestoreControllerBlazor.cs:423-438`): every entry with a member spec except 戻せません (the `NotRestorableOnExisting` set applies to new records too, enforced in the engine — both; Codex SEC2), in `ApplyOrder` (seeded context first, group drivers, then group members as final values, `Core/EditDraftRestorer.cs:79-94`); undrafted group members keep the fresh object's value (`:113-125, :160-175`); member write permission checked on the fresh object, not-writable members not assigned (`Core/EditDraftAccessSeam.cs:45-70`).
8. 事業所 check on the filled object (§5 S5 ii). Refused → dispose.
9. Pending adoption: a Core-owned contract (as `EditDraftBadgeNotifier` is, `Core/EditDraftListRules.cs:126-155`; registered by the host/Blazor — Codex review C6) holds {object → draft Oid, claimed revision, editor id, payload}. The new screen's capture takes it in `BindTo` and attaches with THAT editor id (new overload of `TryAttachClaimed`; `DeleteOwn` is editor-scoped, `Core/EditDraftWriter.cs:257-262`).
10. `CreateDetailView(os, approvedViewId, isRoot: true, obj)` and show (window: O1/D6). Success is reported only after the capture acknowledged the adoption: 「入力控から記録を作成しました。まだ保存されていません — 内容を確認して保存してください。」; with not-applied entries 「…（n 項目は戻せませんでした）」 and the D9 read-only display of exactly those entries' full text, so the person can retype them before the save retires the whole draft. If the adoption was not acknowledged, the view is closed without saving and the row stays live. (Codex review C5; Claude's resolution, not cross-reviewed.)
11. The restore controller's own offer stays existing-only (`Blazor/EditDraftRestoreControllerBlazor.cs:140`), so the recreated screen is not offered the draft a second time.

Restore on a fresh object needs no fresh-database re-check (G5): there is no stored row; step 3 replaces it. Review before apply: O2/D7.

**TenantSubSection parent case**: excluded from this wave (§3). Rule for the wave that admits it: a parent fixed by the creating route is construction context — resolved first, never offered as a selectable replacement, and a mismatch refuses the draft rather than moving it to another parent (Codex). In the (a) path of the first wave the object is fresh, so a seeded value SETS the context; it does not change one.

### 4.5 Library vs CareCrew; schema (brief item 5)

| Piece | Where |
|---|---|
| `AllowNewRecords`; admission; seeding + its two rules; fresh-start preservation; `IsNew`-aware snapshot and write guards; payload header `prov`; claim-with-header writer method; `ApplyNew` and the new-record classification (no spec / 戻せません / unresolved reference → Unavailable, else New); pure rules (row kind, recreate admission, "already saved" decision); the pending-adoption contract; texts in `EditDraftTexts` | Xaf.EditDraft.Core |
| list row kind and 開く → recreate orchestration with the failure outcomes; capture attach-with-editor-id registration; the ListView notice (the badge controller already reads once per activation, `Blazor/EditDraftListBadgeControllerBlazor.cs:103, :237`, but through `ListOwnTargets`; the notice needs `ListOwn` or a projection with the payload version — §4.3) | Xaf.EditDraft.Blazor |
| record-access seam: a second method deciding by a 事業所 Oid (§5 S5) | Core seam; CareCrew implementation |
| policies opt in: StaffOverTimeHoliday (with `ReconstructionOrder`), ToDo, NightRoundsTime; TenantCase after D10; TenantSubSection deferred; the 23 chart types keep their own new-record path | CareCrew.Blazor.Server |
| NHM | nothing |

**Schema: none** (both). TargetOid = Guid.Empty, the payload header and the existing columns (SubSectionOid, ContextText, ViewId, EditorInstanceId, DraftKey) carry everything; `EditDraft.cs` in CareCrew and in NHM (`NursingHome_Chart.Module/BusinessObjects/EditDraft.cs:89-114` there) stay unchanged. Payload format: O3/D2. Older dev builds on Dev2 that read such a row: list → ContextText and 「元の記録が見つかりません」 (by source reading; `GetObjectByKey(type, Guid.Empty)` returning null is not verified), offer never matches, badge skips Guid.Empty (`Core/EditDraftListRules.cs:101-109`).

### 4.6 How each disagreement was settled

| # | Item | Positions | Settled by |
|---|---|---|---|
| S3 | seeding hides the first edit; bare notification leaves a payload | Codex found; Claude agrees | source read; future check M1 tests T5/T6 |
| S4 | fresh-start paths drop seeds/header | Codex found | source read; M1 test T9 |
| S5 | adoption contract cannot live in Blazor | Codex found | project references (`Xaf.EditDraft.Blazor.csproj` references Core); M1 isolation tests |
| S10 | post-claim failures / partial apply | Codex found; Claude wrote the outcomes (§4.4) | not cross-reviewed |
| S11 | `prov` after a recreate | Codex found (C3 case 1); Claude wrote the one-statement claim | not cross-reviewed |
| S8 | notice counts targetless rows | Codex found; wording narrowed | source read |
| O1 | window for the recreated record | **Claude**: `NewModalWindow` — the list's 開く for existing records (`Blazor/EditDraftListControllerBlazor.cs:359-361`) and the chart recreate (`Charts/TenantChartDraftListControllerBlazor.cs:591`) use it; the list popup is already closed when 開く runs (`Blazor/EditDraftListPopupControllerBlazor.cs:43-44`), so fix-531's reason for NewWindow (its popup was still registered) does not apply. **Codex**: `NewWindow` — a new MDI tab in TabbedMDI (dxdocs 26.1 TargetWindow), as fix-531's direct creation | open → D6 (browser check decides usability) |
| O2 | review before apply | **Claude**: apply directly, then show the unsaved record (chart `CreateFromDraft`, `Charts/TenantChartDraftListControllerBlazor.cs:546-586`); every row would be 新規, nothing stored to conflict with. **Codex**: a new-record plan that records the fresh object's values when shown and re-checks them before apply, so the person can untick | open → D7 |
| O3 | payload format | **Claude**: keep `PayloadSchemaVersion` 1 with an optional header (Newtonsoft ignores unknown members; `IsPayloadReadable` unchanged). **Codex**: a distinguishable version, readers accepting both, so an unsupported new payload is refused as a whole | open → D2 (neither old-reader path executed) |
| O4 | a member returned to its baseline | **Claude**: keep today's rule (the entry stays; on recreate it restores the value the screen showed). **Codex** (requirement-only E09): remove the entry | open → D8 |
| O5 | first save | both recommend delete; the owner's ruling says "re-key on save" | open → D3 |
| O6 | duplicate after (i) interrupted cleanup or (ii) the original screen still open elsewhere saving after another tab recreated | Codex flagged (SEC1, review C3); Claude's mitigations: the `prov` history check (step 3), the one-statement claim (step 6), the confirmation text; case (ii) is not prevented | open → D11 |

## 5. Security — single-model (Claude only; owner review required)

Codex was told not to design this section; its three SEC flags are in §9. The new path adds one capability: the engine CREATES a business object (unsaved) from stored data.

| # | Rule | Where | Evidence for existing parts |
|---|---|---|---|
| S1 | A draft never creates a record the login may not create: before step 5, `DataManipulationRight.HasPermissionTo(type, null, null, os, SecurityOperations.Create)` on the destination object space, guarded by `Application.Security is IRequestSecurity` — the call fix-531 uses; denied → 「この記録を作成する権限がありません。」, nothing claimed, nothing created | recreate | `Charts/NewTenantChartEventControllerBlazor.cs:199-208`; KB fix-531 |
| S2 | No creation the UI itself does not offer: `AllowNewRecords` AND at least one of the policy's `ListViewIds` with model `AllowNew = true` AND the approved DetailView's `AllowEdit` (read from `Application.Model` at the click) | recreate | the owner created a 残業・有給 from its list (log 18:21:24); the other lists' model values are not read here |
| S3 | Only the draft's owner, re-resolved at the click; owner predicate in every statement; claim owner-scoped and revision-fenced | recreate, writer | `Core/EditDraftWriter.cs:242-250, :308-313`; chart re-authorise `Charts/TenantChartDraftListControllerBlazor.cs:553-558` |
| S4 | D6 unchanged: a GeneralUser login has no owner — no capture, no list, no recreate | capture, list | `Core/EditDraftCaptureControllerBlazor.cs:283-292`; `Blazor/EditDraftListControllerBlazor.cs:251-257, :337-338`; `CareCrew.Blazor.Server/Infrastructure/EditDrafts/EditDraftOwner.cs:24-47` |
| S5 | 事業所 on a record that has none yet: (i) before creating, a non-empty `SubSectionOid` of the draft must be visible to the login — a second seam method decides by a 事業所 Oid; CareCrew implements it with the same `GlobalSearchAssignment.IsRecordVisible` call it already makes after computing the 事業所 from a record; (ii) after apply and before showing, `IsRecordVisible(app, policy, filledObject)`; refused → dispose, nothing shown or saved, 「この入力控の記録の事業所は表示できません。」. A record with no 事業所 at all is not restricted by the helper's own rule, so (ii) decides for a draft captured before a 事業所 was chosen | Core seam + CareCrew; recreate | `CareCrew.Blazor.Server/Infrastructure/EditDraftAccess.cs:30-49`; `CareCrew.Blazor.Server/Services/GlobalSearchJump.cs:229-246` (no 事業所 → visible `:236-238`; non-StaffMember login → visible `:241-242`) |
| S6 | References resolve only through the secured destination space; an unreadable object stays unresolved (failed, never nulled or replaced) | ApplyNew | `Core/EditDraftRestorer.cs:139-146` |
| S7 | Member write permission on the fresh object; not-writable members not assigned, counted | recreate | `Core/EditDraftAccessSeam.cs:45-70` |
| S8 | Restore guard (D12) open on the new object space during apply | recreate | `Core/EditDraftRestoreGuard.cs:21-90` |
| S9 | Store controls unchanged: same `dbo.EditDraft` table — denied to every role, excluded from the audit trail, purged. Seeding stores the DEFAULT 職員 reference (Oid + display text, a staff name) in a new 残業・有給 draft even when not typed; the payload already holds display texts of typed references; the list's 対象 still never shows a name | store | `NursingHome_Chart.Module/BusinessObjects/EditDraft.cs:31-32`; `Core/EditDraftStoreBase.cs:87-91` |
| S10 | The "already saved?" read is secured: a saved record the login can no longer read reads as "not saved", so a duplicate is possible in that case (part of D11) | recreate | — |
| S11 | A claim from the list's search un-discards (`DeletedOn = NULL`), as for existing drafts; only the owner can recreate a 破棄'd new draft, from the search view | writer (unchanged) | `Core/EditDraftWriter.cs:242-250` |

Not designed (owner may ask): refusing CAPTURE of a new record whose type the login may not create. The check sits at recreate because permissions can change within the 7 days; capture only records what a screen XAF opened let the person type.

## 6. Deployment

- Build: CareCrew Blazor only. `Xaf.EditDraft.Core` and `.Blazor` are projects referenced by `CareCrew.Blazor.Server.csproj:79-80`. Not NHM WinForms, not ChartWorkflowServiceV2, no report layout, no database script.
- Mirror: none (no Module change). Schema: none.
- Switches: per type (`EditDraftCapture:Types:<PolicyId>:Enabled`, existing) plus the proposed `EditDraftCapture:NewRecords:Enabled` (D15), off in Production until the owner turns it on.
- Production prerequisite (not part of this design): the generic `dbo.EditDraft` table and its deny rows / purge are not yet in production according to the session memory (prod CareCrew 2.6.42.0 / NHM 2.6.25.0; not re-verified in this run); `docs/generic-edit-draft-production-rollout-2026-10-01.md` holds that runbook.
- Timing: no pay-window or month-end dependency.

## 7. Verification plan

Executable (library and CareCrew tests; builds and tests into `artifacts/claude-test/<run-id>`; a run counts only with a test total above zero):

| # | Check | Level |
|---|---|---|
| T1 | admission truth table incl. `AllowNewRecords`, the new switch, opted-out and excluded types, chart policies untouched, inline list still existing-only | pure |
| T2 | a new probe object's Oid is non-empty after `CreateObject` and equal after `CommitChanges` (G2 as behaviour) | in-memory XPO (`Xaf.EditDraft.Tests/EditDraftEngineTests.cs:98-112`) |
| T3 | opening a new object and raising unchanged notifications writes nothing and leaves no payload | engine |
| T4 | keying per write: Empty + `prov` while new, the Oid after commit; guards refuse Empty without `IsNew` | engine |
| T5 | each seeded member as the ONLY first edit schedules a write with the changed value (seeding trap) | engine |
| T6 | seeding happens only for new objects and only with a real edit; seeded→typed flips `Seeded` | engine |
| T7 | `prov` history: original Oid, recreated Oid added by the claim statement's payload, survives supersedes | engine (pure payload) |
| T8 | payload with and without the header reads; v1 drafts still offered (D2 either way) | serialization |
| T9 | fresh-start rebuilds keep seeds and header (both paths) | engine |
| T10 | `ApplyNew`: order (Date before times), references resolved / missing → failed not nulled / cleared → null, groups, 戻せません never assigned even when passed directly, companion missing → failed | in-memory XPO |
| T11 | across midnight: a draft from day D restores 日付 D on day D+1 (fake clock) | engine |
| T12 | recreate orchestration as pure steps: refusal before claim changes nothing; failure after claim leaves the row at its new revision and disposes; success only after adoption acknowledged | pure + fakes |
| T13 | "already saved" decision over the whole `prov` history; failed check asks | pure |
| T14 | list row kind 新規/既存, type filter, exact-row 開く | pure |
| T15 | notice count: zero/one/many, discarded/expired/unreadable excluded | pure |
| T16 | first save deletes (or re-keys per D3), failed save keeps, in-flight create handed back | slot tests (existing) + engine |
| T17 | sensitivity: with the admission lift disabled T3–T7 go red | run |

Not executable here: the SQL text of the writer (SQL Server, `@pN`), and the browser checklist (Dev2 dev host on 5002–5004):
1. 残業・有給 新規 → type 残業理由 → F5 → on the list the notice says 1 → header 「入力控」 → 「新規」 row → 開く → the record shows 職員/日付/時刻 of the draft and the typed reason, unsaved → 保存 → log `delete after save: rows=1` → list empty.
2. 新規 then close without typing → no `write create` line, no notice (also catches a DetailView controller that sets values after activation — Codex).
3. First edit = 日付 only → `write create ok=True`.
4. Two tabs, two new records → two 「新規」 rows → each recreates its own.
5. A draft from the previous day keeps its 日付.
6. Another login sees nothing; a GeneralUser login captures nothing.
7. A role without Create on the type → 開く refused with the message.
8. 破棄 a new row → hidden; 破棄済みも表示 → 開く recreates.
9. 保存して新規 on a recreated record → the next new record has its own draft.
10. Window behaviour per D6; 戻せません/not-applied display per §4.4 step 10.

Codex's requirement-only expectations E01–E44 (tests/a1) against this plan: E01–E04 T4/T14/browser 4; E05/E19/E21 browser 1, 10; E06/E42/E43 T1; E07–E10 T3/T5/T6 (E09 per D8); E11/E25–E27 T10/T11; E12 existing slot tests + T9; E13–E14 T16 per D3; E15–E17 T16/T13 (E17 partly — D11); E18/E20/E34 T14/T12; E22 T15; E23 deferred with (c); E24 T12 (getters before apply); E30 deferred with TenantSubSection — assert its exclusion in T1; E28–E29/E37–E38 §5 + browser 6–7; E31/E41 T12/T10; E32–E33 browser 4; E35–E36/E39–E40/E44 T12/T15 + existing expiry tests.

## 8. Milestones and owner decisions

| Milestone | Scope | Size | Exit check |
|---|---|---|---|
| M0 | owner decisions D1–D15 | S | answers recorded |
| M1 | Core: §4.2, header, claim-with-header writer method, `ApplyNew`, classification, pure rules, adoption contract, texts | M | Core build 0 errors; T1–T13, T16 pass (total > 0); T17 red with the lift disabled; isolation tests pass |
| M2 | Blazor: list row kind, recreate orchestration and outcomes, attach overload wiring, notice | M | Blazor build 0 errors; T12, T14, T15 pass; existing list/restore tests pass |
| M3 | CareCrew: three policies opt in, `ReconstructionOrder` for 残業・有給, seam Oid method, T3-gate test; check that the root ToDo and 夜間巡回時間 routes set no untouched context (`ToDoItem`, 事業所) before relying on "no seeds" (Codex combined) | S | CareCrew build 0 errors; `NursingHome_Chart.Rostering.Tests` EditDraft tests pass |
| M4 | Dev2 browser checklist §7 | S–M | all ten items observed, log lines quoted |

Then the Phase-4 Codex diff review, then git-committer (topic branch) and release-publisher — owner-triggered.

Owner decisions (recommendation first):
1. **D1 Schema**: no column — TargetOid = Guid.Empty + payload header (both). The 2026-09-30 design expected IsNew/ProvisionalOid columns; confirm they are not needed.
2. **D2 Payload format** (O3): Claude — keep version 1 with an optional header; Codex — a distinguishable version read alongside v1. No recommendation across the two; both are safe by source reading.
3. **D3 First save** (O5): delete (both) — vs re-key (the owner's wording).
4. **D4 Offer surfaces**: list 「新規」 row + ListView notice now; offer at 新規 later (both).
5. **D5 Notice**: once per list activation, counts live readable new-record rows read through `ListOwn` (both). Wording: Codex 「新規の入力控が n 件あります。」 (claims only rows) — Claude 「保存されていない新規入力が n 件あります。上の「入力控」から開けます。」 (tells the person where to go). Recommendation: Codex's sentence plus the pointer to 「入力控」.
6. **D6 Window** (O1): Claude `NewModalWindow` — Codex `NewWindow`.
7. **D7 Review before apply** (O2): Claude direct apply — Codex a review plan.
8. **D8 Reverted member** (O4): Claude keep the entry — Codex remove it.
9. **D9 First types**: 残業・有給, ToDo, 夜間巡回時間 (both).
10. **D10 TenantCase** after the `[AutoIncrement]` timing check; **TenantSubSection** deferred until the fixed-parent context rule exists (both).
11. **D11 Duplicate residual** (O6, Codex SEC1): accept for this wave with the mitigations (history check, one-statement claim, 開く confirmation text 「元の画面がまだ開いている場合は、そちらで保存してください。」, 入力日時 shown) — or require more (a liveness signal would need its own design and possibly a column).
12. **D12 Chart recreate Create check** (Codex SEC3, pre-existing in shipped code): a separate, single-model follow-up on the chart branch — not in this scope.
13. **D13 Close with いいえ** on a new record keeps its draft (same as existing records) — Claude; Codex did not take a position.
14. **D14 Security section §5**: owner review (single-model).
15. **D15 Runtime switch** `EditDraftCapture:NewRecords:Enabled`, default off in Production — Claude.

## 9. Contribution log

### What Claude did
Phase 0 preflight (§9 setup checks); read the engine, the CareCrew policies, the chart new-record path, the five business objects and the installed DevExpress 26.1.4 `BaseObject` source; read the owner's run in the dev host log; built the parity packs v1–v3 (594/611/614 KB); wrote an independent design before reading any Codex output (`claude-diagnosis.md`); checked every Codex citation it relied on (all correct); adjudicated pass 2 against source; wrote §5 alone; wrote this write-up. Right: the gate analysis, Oid at construction, seeding need, TargetOid = Empty, the TrySupersede/K2 staleness, the editor-id attach gap, the list popup closing before 開く, the first-type choice. Wrong or incomplete (caught by Codex): missed the seeding trap and the fresh-start paths; wrote that capture keeps "only changed members" (a reverted entry stays); claimed `prov` stays current after a recreate; counted Guid.Empty targets as eligible new drafts; placed the adoption service in Blazor; did not define post-claim failures; argued for delete with "every drafted value is on the record".

### What ChatGPT (Codex) did
`tests` (requirement-only): 44 expectations E01–E44 before seeing any design. `diag`: an independent design with the same identity, seeding, surfaces, first types and schema answer; it alone found the seeding trap, the reverted-entry wording, the unsaved-object-graph limit, the parent-context kinds, the inline-list boundary, and raised SEC1–SEC3. `review`: confirmed the gate analysis and structure, and found C1–C8 (seeding trap again, reverted entries, `prov` after a recreate and the second-tab duplicate, fresh-start paths, post-claim failures and partial apply, adoption placement, test coverage gaps, notice count). `combined`: kept the structure and O1–O6 open, added C18–C20 and the design-rule list (§10). Read only; no `file_change`; it read KB records and fetched dxdocs pages (some returned 25.2/26.1.5 content, which it did not use as exact-version evidence).

### Found issues, by tool

| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| A1 | Gates G1, G3–G5 refuse every new record | both | correct | §2 | input lost on F5 / every new record / source + log / yes in dev (owner's run), prod unknown (feature not in prod) | T1, browser 1 | design §4 |
| A2 | Oid assigned at construction, kept at save | both | correct (source) | DX 26.1.4 `BaseObject.cs:59-72` | identity choice / — / installed source / n/a | T2 | used in §4.1 |
| A3 | Changed-only loses time/login defaults | both | correct | `StaffOverTimeHoliday.cs:48-60` | wrong 日付/時刻 on recreate / any next-day recreate / source / no | T11 | seeding §4.2.3 |
| A4 | Generic apply cannot run on a new object | both | correct | G5 | — / always / source / no | T10, T12 | separate recreate path |
| A5 | TrySupersede never rewrites TargetOid (K2 stale) | Claude | correct | `Core/EditDraftWriter.cs:204-209` | — / every recreate / source / no | — | K2 rejected |
| A6 | Generic attach takes no editor id | Claude | correct | `Core/EditDraftCaptureControllerBlazor.cs:108-123`; `Core/EditDraftWriter.cs:257-262` | save would not delete the row / every recreate / source / no | T12 | new overload |
| A7 | After F5 the person lands on the list | both | correct for this run | log 18:21:35-39; `SafeMdiShowViewStrategy.cs:44-52` | surface choice / observed once / log / dev | browser 1 | (a)+(b) |
| A8 | TenantSubSection new records: nested clone flows, Room D path | both | correct | §3 | side effects / if admitted / source / no | — | deferred |
| A9 | TenantCase `[AutoIncrement]` timing unknown | both | open | `TenantCase.cs:66-73` | a number consumed by a recreate / if admitted / source / no | read LlamachantFramework or a test | D10 |
| A10 | The list popup closes before 開く runs | Claude | correct | `Blazor/EditDraftListPopupControllerBlazor.cs:43-44` | window choice / always / source / n/a | browser 10 | O1 evidence |
| C1 | Seeding hides the first triggering edit; seeded-only payload blocks adoption | Codex (diag; review C1) | correct | `Core/EditDraftCaptureControllerBlazor.cs:298-306, :341` | first 日付-only edit not saved / likely / source / no | T5, T3 | §4.2.4 |
| C2 | A reverted entry stays in the payload | Codex | correct | `Core/EditDraftPayload.cs:45-65` | restores the shown value / whenever reverted / source / no | T6 | O4/D8 |
| C3 | `prov` not updated by claim/attach (recreate then save, delete fails) | Codex (review C3 i) | correct | `Core/EditDraftWriter.cs:242-249` | duplicate record / rare (delete failure) / source / no | T7, T13 | §4.4 step 6 (not cross-reviewed) |
| C4 | Original screen still open elsewhere saves after another tab recreated | Codex (review C3 ii, SEC1) | correct | `Core/EditDraftWriter.cs:257-261` (editor-scoped delete) | duplicate record / two tabs + both saved / static / no | browser (two tabs, both saved) | open D11 |
| C5 | Fresh-start paths drop seeds and header | Codex (review C4) | correct | `Core/EditDraftCaptureControllerBlazor.cs:386-446` | lost context after expiry/破棄 race / rare / source / no | T9 | §4.2.5 |
| C6 | Post-claim failures, unacknowledged adoption, partial apply undefined | Codex (review C5) | correct | `Core/EditDraftCaptureControllerBlazor.cs:108-123`; `Core/EditDraftRestorer.cs:129-156` | silent loss or false success / on failure / source / no | T12 | §4.4 outcomes (not cross-reviewed) |
| C7 | Adoption service cannot be Blazor-only | Codex (review C6) | correct | project references | build break / certain if done so / source / no | Core build + isolation tests | §4.4 step 9 |
| C8 | Test list missed E01–E44 cases | Codex (review C7) | correct | §7 mapping | weak acceptance / — / comparison / no | §7 | T1–T17 + mapping |
| C9 | Notice count includes stale/unreadable rows | Codex (review C8) | correct | `Core/EditDraftWriter.cs:369-386` | misleading notice / rare / source / no | T15 | wording narrowed |
| C10 | Existing `isNew` branch skips 戻せません (SEC2) | Codex | correct | `Core/EditDraftRestorer.cs:29-31` | W6 members replayed / if a W6 type opted in / source / no | T10 | engine-enforced on new |
| C11 | Chart recreate has no explicit Create check (SEC3) | Codex | correct (pre-existing, shipped) | `Charts/TenantChartDraftListControllerBlazor.cs:547-586` | a form shown that cannot be saved (commit-time security still applies — not executed) / permission revoked within 7 days / source / unknown | Create-denied role test | D12 follow-up |
| C12 | No recovery of unsaved object graphs | Codex | correct | `Core/EditDraftRestorer.cs:139-146` | — / when a reference is itself new / source / no | T10 | stated limit |
| C13 | Parent context kinds needed before a fixed-parent type | Codex | correct | policies `:62`, `:29` | wrong parent / if admitted / source / no | — | requirement for later wave |
| C14 | Initialization order vs other controllers | Codex | open | — | phantom write / unknown / — / no | browser 2 | checklist |
| C15 | Window target | Codex vs Claude | open | §4.6 O1 | UX / — / docs + source / no | browser 10 | D6 |
| C16 | Review plan before apply | Codex vs Claude | open | §4.6 O2 | UX / — / reasoning / no | — | D7 |
| C17 | Distinguishable payload version | Codex vs Claude | open | §4.6 O3 | compatibility / — / source / no | old-build 開く on a new row | D2 |
| A11 | "every drafted value is on the record" (delete argument) | Claude | incorrect (partial apply) | `Core/EditDraftRestorer.cs:129-156` | — | — | reworded §4.2.8 |
| C18 | Copying the chart's `!existing.Seeded` alone turns a bare notification on an untouched seeded member into a write | Codex (combined) | correct (source) | `Core/EditDraftCaptureControllerBlazor.cs:339-342`; `Charts/TenantChartDraftCaptureControllerBlazor.cs:329` | phantom write / every untouched seeded member notified / source / no | T3, T5 | baseline-based rule §4.2.4 |
| C19 | The notice cannot judge readability from `ListOwnTargets` | Codex (combined; extends C9) | correct | `Core/EditDraftWriter.cs:375-378` | wrong count / unreadable rows / source / no | T15 | read via `ListOwn` §4.3 |
| C20 | One-statement claim needs the candidate before the claim; discarded candidates' constructor effects need a per-type check | Codex (combined) | correct | §4.4 steps 5-6 | consumed numbers etc. / per type / reasoning / no | per-type admission review | §4.4 step 5 |

Found independently by both: G1–G7 gates, Oid at construction, TargetOid = Empty with no column, ReconstructionOrder seeding with Date before the times, delete on first save, surfaces (a)+(b), first types, schema none. This is coverage, not confidence; no behaviour in this list was executed.

### Codex calls

| Run | Call | Attempt | Path | Started | Duration | state | validation | Exit | PID | Model / effort requested | Effective effort | reasoning tokens | Search | MCP calls | activity (cmds / non-zero / file_change / outside-repo) | prompt / out sha256 (first 8) | Pack | CLI |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 139e0a | tests | a1 | `…\tests\a1` (cwd `tests\req`, `-SkipGitCheck`) | 18:35:18 | 3.9 min | success | ok | 0 | 44892 | gpt-6-astra / xhigh | not observable | 1,848 | off | 0 | 4 / 1 / 0 / 1 (powershell.exe) | 2328EE41 / 33E67A51 | REQUIREMENT.md (brief + runtime facts) | 0.153.4 |
| 139e0a | diag | a1 | `…\diag\a1` | 18:35:18 | 12.8 min | success | ok | 0 | 26280 | gpt-6-astra / xhigh | not observable | 4,207 | off | 16 (KB lookup 1, dxdocs search 6, get_content 9) | 14 / 1 / 0 / 1 (powershell.exe) | AFD10924 / E93EFC5C | v1 | 0.153.4 |
| 139e0a | review | a1 | `…\review\a1` | 18:51:13 | 18.4 min | success | ok | 0 | 47304 | gpt-6-astra / xhigh | not observable | 8,725 | off | 9 (KB 2, dxdocs 7) | 15 / 1 / 0 / 1 (powershell.exe; 3 "unc" entries are regex text, not paths) | 2BDC7D3A / 5048BDDC | v2 | 0.153.4 |
| 139e0a | combined | a1 | `…\combined\a1` | 19:12:17 | 9.7 min | success | ok | 0 | 64164 | gpt-6-astra / xhigh | not observable | 2,240 | off | 5 (KB lookup 1, get_fix 1, dxdocs 3) | 6 / 0 / 0 / 1 (powershell.exe) | A978E609 / 2ACF1464 | v3 | 0.153.4 |

Input tokens: tests 137,875 (cached 105,216); diag 2,480,998 (cached 2,241,664); review 2,683,779 (cached 2,365,696); combined 1,444,201 (cached 1,228,928). Prompt sizes: diag 599 KB, review 634 KB, combined 709 KB.

The `tests` isolation is by convention (absolute-path reads remain possible); its out.md cites only REQUIREMENT.md and its four commands read only that file.

### Setup checks (Phase 0; outputs under `%LOCALAPPDATA%\collab\2026-10-02-edit-draft-new-records-139e0a\preflight\`)

| # | Item | Result |
|---|---|---|
| 1 | BASH_MAX_TIMEOUT_MS | present (2400000) |
| 2 | Read-only query connection (HARD) | not applicable: no database used (brief: no DB) |
| 3 | Repo trusted (HARD) | session project `repos\CareCrew` trusted (`hasTrustDialogAccepted=true`); the hook fired (item 5) |
| 4 | Manifest (HARD) | 7/7 hashes match in the worktree and in the main repo |
| 5 | Hook fires (HARD) | `git push --dry-run origin HEAD` blocked by collab-guard; a harmless Monitor (`Get-Date`) ran unblocked |
| 6 | collab.rules | file present; the execpolicy check was blocked by the hook (its text contains a push) and not retried (rule 13) |
| 7 | prompt-input | saved; AGENTS.md "Working with Claude (Codex)" present; CLAUDE.md body absent (pasted as pack item 0) |
| 8 | Tool boundary (HARD) | no MCP tool of this agent writes a database, migrates, deploys, pushes or restarts; KB write tools, claude-in-chrome and Claude Docs present, not used |
| 9 | Tool parity (HARD) | KB with the 9 read tools (`enabled_tools`, `~/.codex/config.toml:127-134`), dxdocs; node_repl and cua_repl enabled for Codex (no call); code-review and codex_app listed but disabled |
| 10 | Models (HARD) | gpt-6-astra listed, low…ultra incl. medium and xhigh |
| 11 | Run setup | run 139e0a, scratch, salt (unused), codex.exe from PATH, codex-cli 0.153.4; doctor exit 0 (overall "warning"); login ChatGPT |
| 12 | Snapshot | worktree HEAD 132782b1, branch feature/edit-draft-new-records, clean; NHM HEAD 7bf13f39 (master), read only |
| 13 | Policy drift | CLAUDE.md and AGENTS.md identical to the main repo (AF56E4B3…, BEE30186…); `~/.codex/config.toml` effort medium, every call passed xhigh |
| 14 | Web search | off in all calls |

### Redaction
One identifier: the 8-hex owner prefix in the dev host log line `list: 0 row(s) read for owner …` was replaced by `<login-1>` in the pack. No query was run; the salt was not needed. No names or birth dates were read or sent.

### Inputs Codex did not have
- Claude's auto-memory index (session context): relied on only for "the EditDraft table is not in production", which is stated as memory, not verified.
- The rest of the dev host log (only the 18:21 `[EditDraft]` window was pasted).
- Claude's verdict file on the diag (`claude-verdicts-on-codex-diag.md`); its substance went to `combined` as the settled list.
- §5 (security) by design. §4.4 steps 6 and 10 and the failure outcomes were written after pass 2 and are NOT cross-reviewed.

### Passes used
2 cross-model passes (diag ↔ review) plus `tests` and `combined`: 4 Codex calls, 4 attempts, no retries. Hook false positives: 1 (the execpolicy check text). Monitor slip: one Claude-written wait command was expanded by Bash and reported early; it was ignored and the proper watcher used.

### Run ledger
Appended to `%LOCALAPPDATA%\collab\ledger.jsonl` by `tools/collab/append-ledger.ps1`:
```json
{"run":"2026-10-02-edit-draft-new-records-139e0a","date":"2026-10-02","topic":"edit-draft-new-records-design","attempts":[{"call":"combined","attempt":1,"state":"success","validation":"ok","accepted":true,"exit":0,"minutes":9.7,"commands":6,"nonzero_exits":0,"outside_repo":1,"file_changes":0,"reasoning_tokens":2240,"output_tokens":13413,"search":false},{"call":"diag","attempt":1,"state":"success","validation":"ok","accepted":true,"exit":0,"minutes":12.8,"commands":14,"nonzero_exits":1,"outside_repo":1,"file_changes":0,"reasoning_tokens":4207,"output_tokens":17009,"search":false},{"call":"review","attempt":1,"state":"success","validation":"ok","accepted":true,"exit":0,"minutes":18.4,"commands":15,"nonzero_exits":1,"outside_repo":1,"file_changes":0,"reasoning_tokens":8725,"output_tokens":18718,"search":false},{"call":"tests","attempt":1,"state":"success","validation":"ok","accepted":true,"exit":0,"minutes":3.9,"commands":4,"nonzero_exits":1,"outside_repo":1,"file_changes":0,"reasoning_tokens":1848,"output_tokens":5197,"search":false}],"findings":{"claude_confirmed":3,"claude_rejected":1,"codex_confirmed":16,"codex_rejected":0,"both":7,"unverifiable":0,"open":8},"correlated_error_events":0,"escalated_to_owner":8,"passes":2,"hook_false_positives":1}
```

## 10. Codex merge (combined/a1) and design rules

Codex's combined write-up (`…\combined\a1\out.md`, 44 KB) kept this structure, put O1–O6 side by side without choosing, marked S10/S11 as Claude's unreviewed proposals, and named where the security checks sit without designing them. Its three refinements are adopted above (C18 §4.2.4, C19 §4.3, C20 §4.4 step 5). Its "Structural changes": (1) keep the organisation; (2) one decision register for O1–O6; (3) lifecycle subsections expanded with the review's corrections; (4) S10/S11 recorded apart from shared findings; (5) the candidate exists before the claim under S11. It also asks, for ToDo, to verify that the root route supplies no untouched `ToDoItem` association before relying on "no seeds" (added to M3).

**Design rules the next implementation must respect** (Codex combined, verbatim):

1. **Ordering:** initialize context/getters before replay; identify the genuine first edit before seeding can suppress it; apply date before times and group drivers before final values; claim before replay; acknowledge adoption before reporting success.
2. **Shared state:** each editor has its own slot, owner, policy, sequence mark, and immutable snapshots. A retired worker must not read a newer screen's state. Adoption must use the exact claimed editor ID and revision.
3. **Identity:** one selected new draft reconstructs one intended record. Empty target values do not authorize merging, and missing existing records do not authorize creation.
4. **Context and dates:** preserve approved reconstruction values across changed construction defaults and all payload rewrite paths. Fixed parents are not ordinary replayable references. Do not substitute display text or office scope for parent identity.
5. **Retention:** preserve first-capture-plus-seven-days expiry for each row; updates/claims must not extend it. Recheck liveness at execution, including the exact expiry boundary.
6. **Idempotence:** same-revision claim success is not a lifetime lock on the business record. Preserve O6's interruption and overlapping-editor cases until the owner's required behavior has executable evidence.
7. **Failure handling:** failed capture retains the prior usable draft; failed reconstruction commits nothing; failed adoption reports no success; retries read current revision; partial/unavailable inputs remain visibly identifiable; guard lifetime includes posted work.
8. **Scope:** keep nested/inline-new routes, TenantSubSection, TenantCase, and chart migration outside this wave as specified. Preserve O1–O6 until owner disposition. Security implementation remains Claude-only; persistent mapping changes require a separate owner-controlled phase.

Claude adds: 9. **Seeding:** a seeded entry is never evidence of a user edit and never turns a baseline-equal notification into one (§4.2.4). 10. **Security order:** Create permission and the draft's 事業所 before the candidate is built; owner and revision in the claim; the filled object's 事業所 before it is shown (§5).

## 11. Not verified / open questions
- Every behaviour in this design: no build, no test, no browser run. The Oid-at-construction fact is from the installed 26.1.4 source, not an executed check (T2).
- `GetObjectByKey(type, Guid.Empty)` on a secured XPO space returns null rather than throwing (older dev builds opening a new row).
- LlamachantFramework `[AutoIncrement]` assignment time (D10).
- Whether XAF Blazor's close-with-いいえ on a new record can be told apart from a dead circuit (D13).
- Whether the capture controller on the recreated view activates — and acknowledges the adoption — synchronously within `ShowView` (§4.4 step 10).
- DetailView controllers of the three types that set members after activation (phantom first write — browser 2).
- `DataManipulationRight.HasPermissionTo` for a type whose Create permission has object criteria; whether commit-time security refuses a Create-denied save of a recreated chart record (C11).
- The model `AllowNew` of `ToDo_ListView` and `NightRoundsTime_ListView` (S2).
- EditDraft table absent in production (memory only).
- The six open disagreements O1–O6 and the decisions D1–D15.
