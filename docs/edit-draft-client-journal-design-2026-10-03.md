# Xaf.EditDraft phase 2: client-side input journal — design (2026-10-03)

Collaborator run `2026-10-03-edit-draft-client-journal-0f2191`. Claude: Opus 5.5. Codex: gpt-6-astra, effort `xhigh`
requested (codex-cli 0.153.4). Worktree `CareCrew-journal`, branch `design/edit-draft-client-journal`, base master
`a0354521` as named in the brief; master moved to `342fc7ea` during the run (see §2). Design only: no code, no build,
no database, no commit.

Citation conventions: `_Host@a035` = `CareCrew.Blazor.Server/Pages/_Host.cshtml` at a0354521; `_Host@342f` = the same
file at 342fc7ea. `DX-XAF/…` = the installed DevExpress 26.1 sources (`…\DevExpress.ExpressApp\DevExpress.ExpressApp.Blazor\…`);
`DX-Blazor/…` = the same sources (`…\Blazor\DevExpress.Blazor\…`). Unprefixed paths are repository paths at a0354521.

## 0. Combined answer

A value still inside a focused XAF text, memo or time editor is lost on F5, a dead circuit or a crash because the
generic engine captures only after the editor posts (`ObjectSpace.ObjectChanged`), XAF's text and memo editors post on
lost focus, and DxTimeEdit posts on focus-out; the existing chart journal keeps a copy of most typed text in
localStorage, but per page-load DOM element and caption, with no link to a record or member and a copy-only panel. Both
analysts recommend the same mapping: a library controller puts a server-built `data-editdraft` descriptor on each
captured editor through XAF's documented `ComponentModelBase.SetAttribute` hook (it lands on the editor's root element),
and a library ES module journals only those editors, writes each change to its own localStorage key at once, handles IME
composition and masked editors, and keeps clears and reversals. Recovery goes through the existing 入力控 offer popup
and list after a circuit-scoped intake that converts browser text to canonical values, reconciles it with the same
editing context's server draft, and never applies anything without the user's tick; the chip stays off. Five product
choices are left to the owner with both positions (U1 to U5) — chiefly whether intake promotes validated entries into
ordinary draft rows (Claude) or keeps them as browser-local candidates until the user applies them (Codex). The design
will not repair editors that do not render the attribute (CareCrew's two custom time components, unless U5 adds them),
reference lookups, or anything before the generic engine's EditDraft table exists in production.

## 1. Status

Design proposed, 2026-10-03. Not implemented. Uncommitted in the worktree. Owner decisions §8 are open.

## 2. Root cause and current state

The generic engine subscribes to `ObjectSpace.ObjectChanged` and writes a draft row off the circuit after each change
(`Xaf.EditDraft.Core/EditDraftCaptureControllerBlazor.cs:215`, `:344-391`, `:588-621`). XAF's text and memo adapters
bind `BindValueMode.OnLostFocus` unless ImmediatePostData is set (`DX-XAF/Editors/Adapters/DxTextBoxAdapter.cs:81-82`,
`DxMemoAdapter.cs:67-68`); DxTimeEdit has no BindValueMode and posts on focus-out, Enter, Up/Down, wheel, picker or clear
(KB fix-550; `DX-Blazor/Scripts/editors/masks/masked-input.ts:253-258`). Text in a focused editor therefore never raises
ObjectChanged, so no draft value exists for it (an earlier draft row of the same screen may exist; what is lost is the
latest focused value). The chart journal is the only client copy, and it cannot be mapped back or applied.

| Fact | Evidence |
|---|---|
| The chart journal records the latest whole value per eligible DOM element (inputs except password/checkbox/radio/file/hidden/button/submit, textareas, contenteditable), document-wide, on DOM `input` | `_Host@a035:481-505` (`_Host@342f:581-605`) |
| One localStorage item `CareCrew_InputJournal` = `{e:[{k,l,v,t}]}`; key = per-load id + caption + element counter; caps 60 entries, 12,000 chars, 60-min keep, 20-min fresh; persist 400 ms after the last input; storage errors swallowed; an emptied field removes its entry | `_Host@a035:424-501` |
| No record or member identity in the journal | `_Host@a035:476-499` |
| Panel: per-row コピー, すべてコピー, two-tap 控えを破棄, 閉じる; it never applies; opened from the dead-connection banner and the planned-restart popup; chip OFF (owner 2026-09-25) | `_Host@a035:510-537`, `:542-649`, `:724-731`, `:913-923`; `_Host@342f:613`, `:826`, `:1018` |
| At a0354521 a save cleared the whole journal; on master 342fc7ea (fix-548, merged during this run) a save never touches it — entries leave only by expiry, the cap, an emptied field or 控えを破棄. A journal entry after reload does not mean "unsaved" | `_Host@a035:402-407`; `_Host@342f:478`, `:521` |
| No interaction today between the journal and either draft store (chart or generic) | grep "journal" in CareCrew.Blazor.Server and Xaf.EditDraft.*; `Xaf.EditDraft.Blazor/EditDraftRestoreControllerBlazor.cs:176-205` |
| Masked editors (DxTimeEdit, masked DxMaskedInput) cancel ordinary `beforeinput` and let the server's mask manager compute the text, so ordinary masked typing raises no DOM `input`; composition takes a separate path (non-Android composing input returns before the cancel) | `DX-Blazor/Scripts/editors/masks/masked-input.ts:268-302`, `:274-281`, `:88-92`; `DX-Blazor/Editors/Base/MaskedInputBase/BaseMaskManagerHelper.cs:234-257` |
| Pre-existing defect (escalated, out of scope): the chart journal loses entries across two tabs, because each tab rewrites the whole item from the copy it loaded | `_Host@a035:445-448`, `:501`; Codex executed the real IIFE twice in `node:vm` with one mocked storage — only the second tab's entry remained (review a1, activity item 20). Not a browser run |

Master movement: the brief names a0354521; master moved to 342fc7ea (merges of fix/reload-save-signal,
fix/univer-lifecycle-xlsx, fix/reload-refresh-scope, fix/caretree-residuals). Xaf.EditDraft.Core/Blazor and the sample
are unchanged between the two (`git diff --stat a0354521 342fc7ea -- Xaf.EditDraft.Core Xaf.EditDraft.Blazor samples`
empty). The `_Host.cshtml` delta was pasted to Codex; this design builds on 342fc7ea's journal behaviour.

## 3. Ruled out

| Option / hypothesis | Disposition | Evidence |
|---|---|---|
| Q1(b) derive the mapping from XAF's rendered ids | Rejected. Layout ids are generated GUIDs (`DX-XAF/Layout/LayoutElementsCache.cs:92-99`); the caption CSS class uses the layout item id plus a per-component GUID (`LayoutComponent.razor.cs:63-66`); the hidden `data-item-name` div holds `Caption ?? Id` and is rendered in DetailView layouts too, not only grids (`LayoutComponent.razor:174-178`; Codex corrected Claude here); none carries a record identity | source |
| Q1(c) record per DOM element path | Rejected. No record identity; XAF recreates editor DOM; same defect class as fix-419's per-load counter | `_Host@a035:476-479` |
| Put `data-*` on the `<input>` itself | Not possible through the attribute hook: DX passes only accessibility attributes, a fixed input list and input events to the input; everything else goes to the root | `DX-Blazor/Editors/Base/Models/AttributesHelper.cs:101-205`; `InputDataEditorModel.cs:77-88`; `TextInputDataEditorModel.cs:207-212` |
| Extend the chart journal in `_Host.cshtml` as the library mechanism | Rejected: host-owned script; the library must work in the sample with no host change | `Xaf.EditDraft.Blazor.csproj:1-8` |
| Rely on DOM `input` only | Rejected: masked editors (§2) | — |
| Make editors push on input | Rejected by the owner's 2026-10-03 rule (per-member binding only after a reaction review) | KB fix-550 |
| Every combo's typed text is search text | Wrong (Claude's first draft): string members with predefined values render an editable DxComboBox bound to its Text, which posts on focus loss or Enter; only reference lookups have separate text and key | `DX-XAF/Editors/StringPropertyEditor.cs:70-90`; `Adapters/DxComboBoxAdapter.cs:73-91`; `LookupPropertyEditor.cs:194-208`; dxdocs DxComboBox.TextChanged (26.1) |
| A static `[JSInvokable]` entry point or a new HTTP endpoint for the post-back | Not needed: a circuit-scoped service calling the module through the circuit's IJSRuntime runs as the logged-in circuit | — |
| A save signal or connection ping proves which journal value can be deleted | Rejected (Codex): only an exact entry generation covered by a durable write proves it | `_Host@342f:475-501`; §4 Q2 settlement |
| Client copy panel alone | Rejected as the whole solution: no apply, no list, no new-record recreate | `_Host@a035:542-649` |
| Recovery chip | Rejected: owner decision 2026-09-25 | `_Host@a035:510-516` |

## 4. Proposed design

Sections are marked **both** (agreed), **A** (Claude) or **B** (Codex). Where the analysts disagree and no executable
check decides, the item is marked **U1-U5** and both positions are in §8; this write-up does not choose.

### Q1. Mapping (both)

- A library `ViewController<DetailView>` in Xaf.EditDraft.Blazor registers
  `View.CustomizeViewItemControl<BlazorPropertyEditorBase>(this, editor => …)` and, for each editor whose member is a
  journaled member of the record's policy, calls `(editor.ComponentModel as ComponentModelBase)?.SetAttribute("data-editdraft", descriptor)`.
  Documented hook: dxdocs XAF 26.1 topic 404767 ("SetAttribute … an equivalent of CaptureUnmatchedValues") and the
  `BlazorPropertyEditorBase.ComponentModel` topic; source `DX-XAF/Components/Models/ComponentModelBase.cs:95-100`,
  `ComponentModelRenderer.cs:139-141` (splatted only when the DX component captures unmatched values — every DX input
  editor does: `DxInputDataEditorBase.cs:48`), `DX-XAF/Editors/BlazorPropertyEditorBase.cs:55-65`. XAF itself sets
  `data-xaf-<id>` and `aria-label` this way (`Adapters/DxComponentAdapterBase.cs:94-109`). The customizer handles existing
  controls, later control creation and deactivation (`DevExpress.ExpressApp/Utils/ViewExtensions.cs:57-110`).
- The attribute lands on the editor root (`<dxbl-input-editor>`, `<dxbl-memo-editor>`, `<dxbl-date-time-edit>`,
  `<dxbl-combo-box>`: `DxTextBox.razor:10,21`, `DxMemo.razor:15-18`, `DxTimeEdit.razor:16,43`, `DxComboBox.razor:20`).
  The module resolves an event target with `target.closest('[data-editdraft]')`.
- Admission = the capture's admission (`IsAdmittedViewIncludingNew`, policy and type switches, an owner present) plus a
  new fail-closed key `EditDraftCapture:Journal:Enabled`, read through the configurable section (`EditDraftSwitch.In`,
  `EditDraftSwitch.cs:53-59`).
- Descriptor (server-built JSON, persisted with each browser entry so recovery never needs the old circuit): version;
  policy id; type name; screen object Oid; context id = the capture controller's `CurrentEditorInstanceId`
  (`EditDraftCaptureControllerBlazor.cs:58`, renewed on record change `:254-265`); view id; member path; editor family
  and format hint; a fingerprint (hash) of the capture's canonical baseline raw for that member (S11); a generation
  number bumped when the context, the control or the baseline changes (record change, control recreation, after a save
  `:711-722`). New versus existing is decided at intake by a fresh read, never by a flag in the descriptor (S12).
- Coverage report: the module reports, per view, members admitted versus attributes found in the DOM, to the server log.
- Custom components (U5): CareCrew's `TimeOnlyMaskedInput.razor:6-15` and `TimeOnlyDateEdit.razor:1-7` declare no
  unmatched-attribute parameter, so they do not render the attribute. Options in §8.

### Q2. What is journaled and when (both; durability guard from B)

| Editor / content | Treatment |
|---|---|
| Text box, memo, editable string combo | Exact text; an empty field is a clear (recorded) |
| Masked string, TimeSpan (DxTimeEdit) | Displayed text plus completeness; typed recovery only through a conversion proven against the real editor in M0 (hidden seconds, incomplete masks, 00:00) |
| DateTime time-of-day members | Only per member, opted in by the policy (setters differ: StaffOverTimeHoliday re-dates; others keep the date) |
| Reference lookups | No typed recovery (caption is not a key). Copy-only search text is a scope choice (§8 item 8) |
| Numbers, typed dates | Deferred, or raw text only with a tested adapter (§8 item 8) |
| DxHtmlEditor / rich text | Out |
| Unsupported custom component | Reported as uncovered; never guessed from a descendant input |

Capture rules:
1. Listen only inside `[data-editdraft]`. Triggers: native `input` that is not composing; `compositionstart` /
   `compositionupdate` / `compositionend` (composing text is kept as an incomplete copy, never a typed value; on
   `compositionend` read at once and again after the component update, then de-duplicate by generation and value);
   for masked editors, a MutationObserver on the root plus a next-animation-frame read while the editor has focus, and a
   bounded record of pending operations (probe candidates, gated by M0); `focusout` (final read; hand-over sequence +1).
2. Write each recorded change synchronously to its own localStorage key (one key per owner token / page load / context /
   member generation). No shared read-modify-write item (the chart journal's two-tab loss, §2). Debounce only secondary
   work. Lifecycle events (`pagehide`, `visibilitychange`) are extra capture points, not the persistence boundary.
3. Record clears and reversals. Never remove an entry because its value equals its focus-in text (S6).
4. Values up to 12,000 characters; a longer value is stored as truncated and never offered as a complete typed value.
   Storage refusal or truncation is visible in the recovery status. Budgets (to fix after device measurement in M0):
   A proposed 100 entries per owner; B proposed 60 entries and 512 KiB per writer plus a 2 MiB best-effort total.
5. Retention 60 minutes in the browser, checked on every read, intake and module start (a closed browser runs no purge).
6. Multi-tab: each page load holds a Web Lock for its writer id; intake skips entries whose writer still holds its lock
   and retries when that lock is released, on reconnect, on view activation and on explicit retry; a heartbeat key is the
   fallback where Web Locks is missing (an expired heartbeat alone does not prove a writer is gone). Intake itself is
   serialized per owner with a second Web Lock.
7. Settlement (S7): at `focusout` the module reports (context, path, hand-over sequence) to the server; after a durable
   draft write the server sends `settle(context, path, sequence-of-that-snapshot, captured value)`; the module deletes the
   entry only if sequence and value both match and no later typing is pending. If the association is uncertain, keep
   the entry. Settlement is an optimisation: correctness must hold with a lost settle and repeated intake.

### Q3. Recovery flow (both; U1 and U3 open)

Common sequence: capture → browser entry persisted → circuit-scoped intake collects complete generations (chunked) →
canonical conversion → reconcile with the same context's server draft → offer / list → explicit user tick and apply →
durable acknowledgement → browser entry deleted.

Common rules:
- Intake is a scoped service registered inside the existing `AddEditDraftBlazor()` (`EditDraftBlazorServices.cs:15-23`).
  It imports the RCL module through IJSRuntime and pulls entries; no host page change and no HTTP endpoint (zero host
  change to be confirmed in M4).
- Transfer: values in bounded chunks under the smallest supported SignalR message limit, envelope included; the
  generation being transferred is frozen, reassembled completely and acknowledged by generation. A 12,000-character
  Japanese value is about 36 KB in UTF-8 (Codex executed byte counts), so whole-entry pages under 24 KB fail on a
  consumer with a small limit. CareCrew sets 512 KB (`CareCrew.Blazor.Server/Startup.cs:92-95`); the sample's effective
  limit was not established. Base text is never transferred.
- Baseline (S11): the descriptor's canonical-baseline fingerprint is compared with the hash of the record's current
  canonical raw: equal → BaseKnown with BaseRaw = current raw; different → baseline unknown (unticked). Display text is
  never converted into a baseline raw (null vs "", hidden seconds, dates: `EditDraftCodec.cs:14-19`, `:28-37`).
- Reconcile before classifying (S6): a same-context journal value becomes that context's latest value (the context's
  first baseline kept, `EditDraftPayload.cs:66-87`); only then does the normal three-way classification run
  (`EditDraftPayload.cs:126-142`). A record already holding the journal value does not by itself drop the entry while an
  older same-context draft value remains offerable (A→B drafted, B→A typed must not offer B).
- Existing selection rules stay: side-effect members, groups and newer duplicate paths are not pre-ticked
  (`EditDraftPayload.cs:140-142`, `:165-173`; `EditDraftOfferMerge.cs:49-52`). One final source per member before apply;
  keep the existing ordered setter passes and their intentional repeats (`EditDraftRestorer.cs:79-93`, `:237-245`, `:276`).
- Retired content (S17): intake must recognise a context the user discarded (破棄), a row claimed into another screen,
  an expired row, or a row hard-deleted by a save; `ListOwn` hides discarded and expired rows (`EditDraftWriter.cs:372-382`)
  and claims change `EditorInstanceId` (`:253-270`). When the state cannot be determined, keep the browser entry and do
  not promote it automatically.
- Offer readiness (S2): `EditDraftRestoreControllerBlazor` sets `_offeredThisActivation` before reading drafts (`:159`,
  `:202-206`). It must not be set until intake has finished; a timeout may release the screen, but a late intake
  completion re-runs the offer for the same, still-shown view (stale callbacks dropped on navigation); list badges
  refresh through `EditDraftBadgeNotifier`.
- Where recovery is offered: the existing automatic offer popup when the record opens, and the 入力控 list. No chip. A
  notice about recovered work for records not on screen must lead somewhere the consumer can reach (G11: some consumer
  screens have no list route, `docs/xaf-editdraft-sample-consumer-2026-10-03.md:140`). Dead-connection banner and
  planned-restart popup: U3.

U1-A (Claude) — promote into ordinary draft rows. Validated, converted, reconciled entries are written into the owner's
draft rows before the offer: revision-fenced merge into the same context's row; otherwise a new row with a deterministic
`DraftKey` derived from (owner, context) so a concurrent second intake hits the unique index (`EditDraftStoreBase.cs:49-52`;
`EditDraftWriter.cs:182`) and rereads; capture time kept (the row's `LastCapturedOn` = max(existing, entry time), never
the intake time — D16 offers sort by it, `EditDraftWriter.cs:382`); first-capture expiry anchor kept; optional entry
provenance field omitted when absent (schema 1, `prov` precedent `EditDraftPayload.cs:40-47`, readers accept schema 1
only `EditDraftStoreBase.cs:43`, `:143`). Needs a writer change: `TrySupersede` uses one `now` for both
`LastCapturedOn` and the liveness test `ExpiresOn > now` (`EditDraftWriter.cs:216-218`). Offer, list, badges and
new-record recreate then work unchanged. Cost: draft rows are written from browser data without a user gesture (bounded
by the security rules in Q5); more idempotence, retirement and time-handling work.

U1-B (Codex) — browser-local candidates. Intake classifies entries but writes nothing; the offer and list source model
is extended to show stored drafts and browser-local generations (stable local-source identities, not `Guid.Empty` in
claim code — the current source model assumes draft Oid + revision, `Xaf.EditDraft.Blazor/EditDraftModels.cs:56`, and
attachment expects a claimed row, `EditDraftCaptureControllerBlazor.cs:144-159`); a local-source adoption path for
journal-only new records; browser copies kept through partial apply and attachment failure. Cost: no automatic writes,
but more change to offer, list, adoption and failure handling; unadopted candidates live only as long as browser
retention.

### Q4. Push-on-input editors and new records (both; U2 open)

- Push-on-input editors are journaled too and their binding is not changed: posting to the object and writing a draft
  are separate stages (`EditDraftCaptureControllerBlazor.cs:517-533`, `:588-621`); on a live circuit the settle retires
  the entry quickly; on a dead circuit the journal is the only copy. Masked push-on-input editors mostly show nothing on
  a dead circuit, except composed text (§2).
- New records: the descriptor carries the screen object Oid (the `prov` Oid, `EditDraftPayload.cs:41-62`) and the
  context id. An untouched form is never a candidate. At intake a fresh read decides whether that Oid has been saved.

| New-record case | Outcome |
|---|---|
| Same-context server draft exists (with seeded reconstruction members) | Reconcile the journal into it; seeded values and first baselines kept |
| No server draft; policy has no `NewRecordReconstructionOrder` (the sample's Note: `NoteEditDraftPolicy.cs:20`, `:30-40`) | Journal-only recovery covered in v1 |
| No server draft; policy requires reconstruction context | U2 |
| The record was saved before intake | Treated as an existing record (fresh read), never recreated |
| Apply or attachment fails partly | Remaining material kept; partial result reported |

"Apply the journal last" means final-value precedence, not setter order: `NewRecordReconstructionOrder` first, then the
existing dependency order; journal origin changes neither (`EditDraftRestorer.cs:237-245`, `:276`).

### Q5. Security (A — single-model, Claude only, by the owner's rule)

Not sent to Codex for design or review. Everything returned by the browser at intake is untrusted.

1. Owner: intake runs on the authenticated circuit (server-initiated IJSRuntime call). The owner is
   `EditDraftServices.CurrentOwner` at intake time, never a value from the entry. Entries carry a non-reversible owner
   token (hash of the owner Oid and a fixed library string) used only as a filter; the server recomputes it. No intake and
   no attributes for a login without an owner (GeneralUser, D6).
2. Type, policy, member: the type name must resolve through the registry to the exact type of a generic policy whose id
   matches; the member must be in `policy.Members` with a journaled kind; switches re-read at intake. Unknown fields are
   ignored; nothing is evaluated or used as a property name outside the policy's member list.
3. Record binding: the Oid is never trusted. Existing record = fresh `GetObjectByKey` of the policy type; it must pass the
   record-access seam (CareCrew: the 事業所 rule) and the member must be writable for this login
   (`EditDraftMemberAccess`). A context id only finds THIS owner's rows; every store call carries the owner Oid.
4. Size and shape: chunk and page limits, entry count limit per intake, value limit (12,000 characters, or the member's
   size), ids parsed as Guids, kind and hint from a closed list, strings only. Malformed or oversized input is dropped and
   acknowledged so it cannot loop.
5. Nothing is applied at intake. U1-A writes only the owner's own draft rows through the existing fenced writer; U1-B
   writes nothing. Applying still goes through the restore popup with every existing re-check at apply (owner, revision,
   AllowEdit, record access, write permission, fresh re-read, restore guard: `EditDraftRestoreControllerBlazor.cs:347-438`).
6. Output: the module renders no markup from values and never uses innerHTML; values reach the screen through XAF views
   (text-bound properties). Log lines carry ids, counts and kinds, never values.
7. Data at rest: same posture as the chart journal (plaintext localStorage per browser profile), narrowed to admitted
   members, per-owner token, 60-minute retention, purge at module start, deletion after durable acknowledgement and (§8
   item 12) on logoff. Residual: on a shared device another person with devtools can read up to 60 minutes of one login's
   typed text in admitted members — the chart journal already exposes all typed text for 60 minutes. Encryption (§8
   item 13) needs a key source the library does not have and WebCrypto in a secure context (unverified for the
   self-signed host).
8. Load: intake is paged, capped and runs store writes off the circuit, as capture does.

### Q6. Library versus CareCrew (both)

| Location | Common work | Option-dependent |
|---|---|---|
| Xaf.EditDraft.Core | Journal entry / candidate model; canonical conversion per kind; baseline fingerprint; reconciliation; ordering; texts ja/en in neutral wording (G9); switch key through the section mapping | U1-A: promotion and provenance field; writer time split. U1-B: local-source planning and adoption |
| Xaf.EditDraft.Blazor | RCL module `wwwroot/edit-draft-journal.js` imported through IJSRuntime; attribute controller; circuit-scoped intake in `AddEditDraftBlazor()`; offer readiness; coverage report | U1-A: intake-to-store. U1-B: local candidates in offer and list. U3-B: library copy panel. U4-B: grid adapter |
| CareCrew | Keep 342fc7ea's dirty/save behaviour and the chart journal during transition | U3-B: banner / restart-popup wiring. U5: component forwarding |
| Sample consumer | No change except turning the key on; exercises default seams, English texts, module loading, Note first-field recovery | Exercises the chosen U1 path |

Consumer guide (library-facing, not only the sample README — G14): prerequisites that affect journal recovery (non-
persistent object space provider G1, store table and the 5-minute absent cache G6/G7, browser retention 60 minutes
then draft retention 7 days from the entry time G4/G5, a reachable recovery route G11). Intake failure keeps browser
copies.

The chart journal stays unchanged during transition (both). No record identity is invented for old `{k,l,v,t}` entries.
Chart (F2) policies are outside this design.

### How each disagreement was settled

| # | Item | Settled by |
|---|---|---|
| S1 | `data-item-name` also in DetailView layout (Claude A11 wrong in part) | source read, `LayoutComponent.razor:174-178` |
| S2 | Offer flag consumed before reading drafts | source read, `EditDraftRestoreControllerBlazor.cs:159` |
| S3 | Debounced persistence (Claude) vs synchronous (Codex) | requirement text ("survives … a crash") |
| S4 | Editable string combos and masked strings | source read, `StringPropertyEditor.cs:70-90` |
| S5 | Custom components do not forward the attribute | source read, `TimeOnlyMaskedInput.razor:6-15`, `TimeOnlyDateEdit.razor:1-7` |
| S6 | Reversal dropped before reconciliation (Claude's rule) | Codex rule-level execution + source |
| S7 | Settlement by value only (Claude's rule) | Codex rule-level execution |
| S8 | Concurrent intake duplicates | source read, unique index on (owner, DraftKey) |
| S9 | Promotion order and intake time | source read, `EditDraftWriter.cs:382` |
| S10 | Whole-entry pages too small | Codex executed byte counts |
| S11 | Display text as baseline (Claude's rule) | source read, codec |
| S12 | Stale "new" flag after the first save | source read; fresh read decides |
| S13 | Masked composition on a dead circuit (Claude's claim too broad) | source read, `masked-input.ts:274-281` |
| S14-S15 | Pre-tick and apply-order wording | source read |
| S16 | Journal-only new records possible where no reconstruction order | source read, `NoteEditDraftPolicy.cs` |
| S17-S19 | Retired content; library gaps; optional payload field | source read |
| U1-U5 | Product choices | not settled — §8 |

## 5. Deployment

- Which build: CareCrew.Blazor.Server (project references to both libraries),
  published by the host's publish script; the module ships as an RCL static asset
  (`_content/Xaf.EditDraft.Blazor/…`). Already-open pages need a reload to load it.
- Mirror: none. Blazor and library only; NHM has no Blazor host and no Xaf.EditDraft.
- Schema: none for U1-B; for U1-A one optional payload field (schema stays 1) — no table change.
- Consumers: CareCrew Blazor only. NHM WinForms, ChartWorkflowServiceV2 and database report layouts do not run this.
- Prerequisite: the generic engine's EditDraft table is not in production (KB fix-536, not re-verified this run); the
  journal is useless before it exists. Turn `EditDraftCapture:Journal:Enabled` on in Production only after the browser
  pass.
- Timing: no pay-window or month-end dependency.

## 6. Verification plan

Engineering estimates, not measurements; they exclude Q5 work, production prerequisites and owner review (synthesised by
Codex from both scopes; Claude agrees with the ranges).

| Milestone | Executable checks | Days |
|---|---|---|
| M0 Feasibility (gate) | On the dev host with the real DX 26.1.4 editors: attribute on the root for text box, memo, string combo, masked string, DxTimeEdit; masked-text observation incl. composition, paste, autofill, slow and dead circuit; IME order on iPadOS Safari and Chrome; Web Locks; localStorage in private mode; storage refusal; custom-editor option if chosen. Note: automation cannot type digits into masked inputs (KB fix-550) — a human or arrow keys | 1 |
| M1 Browser journal | node:test + jsdom (as `caretree-scraper-tests/test/host-*.test.js`): synchronous write, clears and reversals, interrupted composition, generations, quota, expiry, two-writer isolation, chunk freeze and reassembly. jsdom cannot prove DX ordering — M0 does | 2-3 |
| M2 Reconciliation (Core, NUnit in Xaf.EditDraft.Tests) | baseline fingerprint (null vs "", hidden seconds, dates), A→B→A, delayed settle B1→C2→B3, same vs independent context, partial conversion, capture-time ordering, lost acknowledgement | 1-1.5 |
| M3-A Promotion (if U1-A) | concurrent intake vs ordinary capture, unique-key conflict reread, revision loss, discard / claim / save replay, generation acknowledgement, payload byte compatibility without the field, late offer and badge refresh, writer time split | 3-4 |
| M3-B Local candidates (if U1-B) | zero-draft offer and list, stable local identities, delayed readiness, local-only adoption, partial apply and attachment failure, reload before durable persistence | 4-6 |
| M4 Consumer and browser regression | CareCrew and sample build and tests (artifacts path rule); module loads with no host change; Note first-field recovery; chip absent; chart journal copies still reachable; save in tab A while tab B holds typing; typing during save | 2-2.5 |

Totals: common 6-8 days; with U1-A 9-12 days; with U1-B 10-14 days. Increments: U2-B +1-2; U3-B +1-2; U4-B +1.5-3; U5
forwarding +0.5-1, layout-id fallback +1-2. Number/date or lookup-selection adapters: +1-2 days of feasibility first.
Every check asserts resulting values and recoverability, not event counts. A test total of zero is a failure.

Risks: double apply (reconcile-then-classify, claim fencing); stale journal after a save (fix-419 lesson; 342fc7ea never
clears the chart journal — settlement by generation); per-load key collisions (keys use the server context id and
generation, never counters); localStorage quota shared with the chart journal; IME; DX-internal behaviour that can change
between versions (masked re-render path, attribute filtering) — pinned by M0; intake-before-offer ordering; SignalR
message size; Web Locks on older iPadOS.

## 7. Contribution log

### What Claude did

Phase 0 preflight (below). Built parity packs v1-v3. Independent
diagnosis from source before reading any Codex output: chart-journal facts (A1-A6), the masked-input path (A7), the
two-tab loss (A8), the XAF attribute hook and root placement from DX source plus dxdocs 404767 (A9-A10), the SignalR size
point (A12), the import-without-include point (A13), and the promotion design. Wrote the Claude-only security section.
Checked every Codex claim against source (all diag claims correct as source facts; R2 corrected Claude's A11) and every
Codex review finding (C1-C11 confirmed by source; the C1, C2 and C5 counterexamples were Codex executions, not re-run by
Claude). Verified Codex's combined point about `TrySupersede`'s single time parameter. Wrote this document.
Got wrong (corrected by Codex): A11's "only in grids"; the combo exclusion; 250 ms debounce; removal on `val === base`;
value-only settlement; display text as baseline; whole-entry paging; "journal applied after reconstruction members";
"new record only when a row exists"; the categorical dead-circuit claim for masked editors; "clean journal value
pre-ticked" without the existing selection rules.

### What ChatGPT (Codex) did

codex-cli 0.153.4, gpt-6-astra, `xhigh` requested (effective effort not observable). Diag a1: independent design;
found the offer flag consumed before reading drafts (C4), the browser-text-vs-codec gap (C5), the new-record seeding gap
(C6, also raised by Claude), `data-item-name` in DetailView layouts (R2), the layout-id fallback for custom editors, and
proposed browser-local candidates, synchronous persistence and a library copy panel. Review a1: executed the real chart
journal IIFE in `node:vm` (A8 reproduced outside a browser), ran rule-level counterexamples (reversal, settlement) and
UTF-8 byte counts, and found C1-C11 plus the retired-content and library-gap items. Combined a1: merged both into one
structure keeping U1-U5 open; found the `TrySupersede` time-parameter issue and the expiry-anchor rule; synthesised the
option estimates. Got wrong or unproven: "schema 1 readers" presented as a reason to keep journal data out of the store
(not a blocker: optional-field precedent); a script include "if required" (an IJSRuntime module import avoids it,
unverified until built); the layout-id fallback and grid customisation are unverified. No file changes (0 `file_change`
in all three streams); no `node_repl` / `cua_repl` call; no web search.

### Found issues, by tool

Rank rule: top rank only for a defect proven wrong in production today; a real path not yet observed is one lower;
design defects (in this proposal, not in code) lower again. Nothing here is proven in production. "Found by" = who raised
it first; "both" only when independent.

| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| F1 | Server-only capture misses a focused, unposted value | both | confirmed (source) | `EditDraftCaptureControllerBlazor.cs:215,344-391`; `DxTextBoxAdapter.cs:81-82`; `DxMemoAdapter.cs:67-68` | latest focused value lost / every F5 before blur / source / unknown | focused F5 vs blur F5 | design premise |
| F2 | Chart journal: per element, caption key, single item, copy-only, chip off | both | confirmed | `_Host@a035:416-649`; `_Host@342f:521-613` | — / — / source / n.a. | localStorage read in browser | §2 facts |
| F3 | Save never clears the journal on 342fc7ea | Claude | confirmed | `_Host@342f:478,521` | stale entries after save / always / source / unknown | — | classification required |
| F4 | No journal ↔ draft-store interaction | both | confirmed | grep; `EditDraftRestoreControllerBlazor.cs:176-205` | — | — | §2 |
| F5 | Masked editors raise no `input`; composition differs | both | confirmed (static), qualified by Codex | `masked-input.ts:268-302,274-281`; `BaseMaskManagerHelper.cs:234-257` | masked text missed / masked editors / source / unknown | M0 | mutation + frame read + composition |
| F6 | Chart journal loses entries across two tabs | Claude | confirmed; executed by Codex in node:vm | `_Host@a035:445-448,501`; review item 20 | copy entries lost / two tabs / harness, not browser / unknown | two-tab browser test | **escalated**, out of scope |
| F7 | Attribute hook via CustomizeViewItemControl + SetAttribute | both | confirmed (source + dxdocs) | `ComponentModelBase.cs:95-100`; `ComponentModelRenderer.cs:139-141`; dxdocs 404767 | — | M0 DOM | Q1 |
| F8 | `data-*` lands on the root, not the input | both | confirmed (source) | `AttributesHelper.cs:101-205`; `TextInputDataEditorModel.cs:207-212` | — | M0 DOM | `closest()` |
| F9 | `data-item-name` only in grids | Claude | incorrect in part (Codex R2) | `LayoutComponent.razor:174-178` | — | — | corrected; conclusion unchanged |
| F10 | SignalR message size; CareCrew 512 KB | Claude | confirmed for CareCrew; default unverified | `Startup.cs:92-95` | intake fails on small-limit consumer / long values / source / n.a. | sample transfer test | chunking |
| F11 | Module import needs no host include | Claude | unverifiable until built | `EditDraftBlazorModule.cs:17,26` | — | M4 | Q6 |
| F12 | `_offeredThisActivation` set before drafts are read | Codex | confirmed | `EditDraftRestoreControllerBlazor.cs:159,202-206` | candidate not offered / slow intake / source / unknown | delayed-intake test | readiness contract |
| F13 | Browser text is not a codec raw; lookup text ≠ key | Codex | confirmed | `EditDraftCodec.cs:14-39`; `LookupPropertyEditor.cs:194-208` | wrong restore / masks, lookups / source / unknown | conversion tests | canonical conversion |
| F14 | New-record context seeded only after a server capture | both | confirmed | `EditDraftCaptureRules.cs:44-58,63-84` | wrong defaults / first focused field / source / unknown | two-new-records test | cases table, U2 |
| F15 | Reversal dropped before reconciliation offers an undone edit | Codex | confirmed (rule-level execution + source) | `EditDraftCaptureRules.cs:38-57`; `EditDraftPayload.cs:126-142` | undone value pre-ticked / posted intermediate + focused reversal / executed rule / n.a. | A→B→A test | reconcile first |
| F16 | Value-only settlement deletes the newest value | Codex | confirmed (rule-level execution) | Claude design Q2 | latest value lost / repeated values + delayed settle / executed rule / n.a. | B1→C2→B3 test | sequence echo |
| F17 | Concurrent intake creates duplicate rows | Codex | confirmed (source) | `EditDraftStoreBase.cs:49-52`; `EditDraftWriter.cs:182` | duplicates / two recovering tabs / source / n.a. | barrier test | deterministic key + lock + reread |
| F18 | Promotion with intake time reorders drafts | Codex | confirmed (source) | `EditDraftWriter.cs:382` | old value preferred / older orphan + newer draft / source / n.a. | t1/t2/t3 test | keep capture time |
| F19 | `TrySupersede` uses one time for capture and liveness | Codex (combined) | confirmed (source) | `EditDraftWriter.cs:216-218` | expired row superseded / historical time passed / source / n.a. | writer test | writer change (U1-A) |
| F20 | 24 KB pages cannot carry a 12,000-char Japanese value | Codex | confirmed (executed byte counts) | — | entry not transferable / long memos / executed / n.a. | max-size round trip | chunked transfer |
| F21 | Display text is not a canonical baseline | Codex | confirmed (source) | `EditDraftCodec.cs:14-19,28-37` | false conflict or applied / null, seconds / source / n.a. | null/""/seconds tests | fingerprint |
| F22 | String combos and masked strings omitted; custom editors do not forward | Codex | confirmed (source; dxdocs TextChanged) | `StringPropertyEditor.cs:70-90`; `TimeOnlyMaskedInput.razor:6-15`; `TimeOnlyDateEdit.razor:1-7` | coverage gap / those members / source / n.a. | type + F5 tests | scope table, U5 |
| F23 | Stale "new" flag after a first save | Codex | confirmed | `EditDraftCaptureControllerBlazor.cs:539-550` | post-save typing skipped / lost update / source / n.a. | suppressed-update test | fresh read |
| F24 | Debounce contradicts the crash requirement | Codex | confirmed (requirement) | `_Host@a035:500-501` (precedent) | newest typing lost / crash inside window / requirement / n.a. | kill-before-deadline | synchronous write |
| F25 | Masked composition visible on dead circuit; mutation read timing | Codex | confirmed (static) | `masked-input.ts:88-92,274-281` | missed or one behind / composition / source / n.a. | M0 instrumentation | M0 gate |
| F26 | "Clean journal value pre-ticked" ignores selection rules | Codex | confirmed | `EditDraftPayload.cs:140-142,165-173`; `EditDraftOfferMerge.cs:49-52` | — | side-effect group test | wording fixed |
| F27 | "Journal applied after reconstruction members" wrong in general | Codex | confirmed | `EditDraftRestorer.cs:237-245,276` | driver order / journaled driver / source / n.a. | assignment trace | existing order kept |
| F28 | Sample policy has no reconstruction order | Codex | confirmed | `NoteEditDraftPolicy.cs:20,30-40` | — | Note first-field test | covered in v1 |
| F29 | Retired content invisible to `ListOwn`; claims and hard deletes | Codex | confirmed | `EditDraftWriter.cs:253-290,372-382` | discarded content revived / discard then reload / source / n.a. | replay tests | keep entry, defer |
| F30 | Library gaps G1/G4/G5/G6/G7/G9/G11/G14; switch section | Codex | confirmed | sample consumer doc `:130-144`; `EditDraftSwitch.cs:53-59` | consumer friction / — / doc + source / n.a. | sample tests | Q6 guide |
| F31 | Keep journal data out of the schema-1 payload | Codex | not a blocker | `EditDraftPayload.cs:40-47`; `EditDraftStoreBase.cs:43,143` | — | byte-compat test | U1 cost |
| F32 | Sample needs a script include | Codex | not needed if imported (unverified) | — | — | M4 | prefer import |
| F33 | Promote at intake vs browser-local candidates | both (different) | open | — | — | none decides | U1 |
| F34 | Journal-only new records with a reconstruction order | both (different) | open | — | — | none decides | U2 |
| F35 | Library copy panel and host wiring | Codex | open | — | — | none decides | U3 |
| F36 | Inline ListView editing | Codex | open | — | — | none decides | U4 |
| F37 | Layout-id fallback for custom editors | Codex | plausible, unverified | `BlazorLayoutManager.cs:54,86-88`; `DxFormLayoutItem.razor:14` | — | browser feasibility | U5 option |
| F38 | Grid inline editors through `DxGridListEditorBase.CustomizeViewItemControl` | Codex | unverifiable (not read by Claude) | `DxGridListEditorBase.cs:124-155` (cited) | — | — | U4 |
| F39 | Security rules S1-S8 | Claude | single-model, not cross-checked | §4 Q5 | — | owner review | owner |

Found independently by both (coverage, not confidence): F1 server-only capture gap, F2/F4 journal facts, F5 masked
path, F7/F8 the attribute hook and root placement, F14 new-record seeding gap, and separate per-entry keys for tabs.

### Codex calls

| Run / call / attempt | Started | Duration | state | validation | exit | Model / effort req. | Effective effort | Reasoning tokens | Search | MCP tools | activity (commands / non-zero / file_change / outside-repo) | Pack | CLI |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0f2191 / diag / a1 | 16:08:24 | 14.9 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 5,346 | off | KB lookup_known_fix ×1; dxdocs search ×7, get_content ×8 | 21 / 3 / 0 / DX sources + powershell.exe path | v1 | 0.153.4 |
| 0f2191 / review / a1 | 16:23:32 | 14.1 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 6,778 | off | KB ×1; dxdocs search ×5, get_content ×7 | 14 / 2 / 0 / DX sources + powershell.exe path | v2 | 0.153.4 |
| 0f2191 / combined / a1 | 16:41:53 | 10.4 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 3,730 | off | KB ×1; dxdocs search ×2, get_content ×4 | 6 / 0 / 0 / DX sources + powershell.exe path | v3 | 0.153.4 |

Non-zero exits were Codex's own failed search commands (rg / Get-Content
range errors). No retries. No candidate files (design only).

### Setup checks (Phase 0)

| # | Item | Result (outputs in the local scratch folder) |
|---|---|---|
| 1 | `BASH_MAX_TIMEOUT_MS` | present (2400000) |
| 2 | Read-only query connection (HARD) | not applicable: no database connection was used (design only, brief "No DB"); `preflight-sql.sql` not run |
| 3 | Repo trusted (HARD) | present (the repository is trusted in Claude Code; the hook fired) |
| 4 | Manifest (HARD) | all 7 hashes match in the main repo and in the worktree |
| 5 | Hook fires (HARD) | `git push --dry-run origin HEAD` blocked by collab-guard; a Monitor running `date` was not blocked |
| 6 | collab.rules | file present; the `codex execpolicy check` command was blocked by collab-guard because its text contains the push pattern (hook false positive); not retried |
| 7 | `codex debug prompt-input` | AGENTS.md with the "Working with Claude (Codex)" section present; CLAUDE.md not (pasted as pack item 0) — `prompt-input.txt` |
| 8 | Tool boundary (HARD) | no Claude MCP tool writes a database, migrates, deploys, pushes or restarts a service; KB write tools unused |
| 9 | Tool parity (HARD) | KB with the 9 read tools (`enabled_tools` in Codex's MCP configuration) and dxdocs. DEVIATION as in earlier runs: `node_repl` and `cua_repl` enabled for Codex — forbidden in every prompt, 0 calls; proposal in `proposed-1.txt`. Claude's Claude Docs, Gmail, Calendar, Drive and claude-in-chrome servers are not registered for Codex (unused) |
| 10 | Models (HARD) | gpt-6-astra listed; supports low, medium, high, xhigh, max, ultra — `models.txt` |
| 11 | Run id / scratch / salt / binary | 0f2191; scratch created; salt written (unused, no personal data); Codex binary from PATH (`COLLAB_CODEX_EXE` unset), codex-cli 0.153.4; doctor "warning" (dev drive, node_repl env var, endpoint protection), login: ChatGPT |
| 12 | Snapshot | worktree HEAD a0354521, status clean; master moved to 342fc7ea during the run (§2) |
| 13 | Policy drift | AGENTS.md section present. Drift in the agent file: Phase 0 item 10 still says "supports `medium`" while ground rule 11 and the launcher default say `xhigh` — reported, not edited |
| 14 | Web search | off for all calls |

### Redaction

None needed: no database rows, log lines or personal data entered either model. The salt was allocated and not used.

### Inputs Codex did not have

The Claude-only security section (by rule). Claude's MEMORY.md index (named, not pasted; the one memory note relied on
was pasted). Everything else Claude relied on was pasted in packs v1-v3.

### Passes used

Two cross-model passes (diag, then review each way) plus the combined draft. Total Codex calls: 3, attempts: 3, all
`success` / `ok`.

## 8. Owner decisions, not verified, open questions

### Owner decisions (★ = both analysts recommend; otherwise each analyst's position is named)

1. **U1 Recovery model.** Claude: promote validated entries into ordinary draft rows at intake (offer, list, badges,
   recreate unchanged; 9-12 days). Codex: keep browser-local candidates and extend offer and list (no automatic writes;
   10-14 days).
2. **U2 Journal-only new records for policies with a reconstruction order.** Claude: defer in v1 (policies without one,
   like the sample's Note, are covered). Codex: include by persisting the reconstruction context with the first browser
   edit (+1-2 days).
3. **U3 Copy panel.** Claude: keep the chart copy panel unchanged (banner and restart popup unchanged). Codex: add a
   library copy panel for incomplete and local states and wire CareCrew's banner and restart popup to it (+1-2 days).
4. **U4 Inline ListView edits.** Claude: exclude from v1. Codex: include through the grid's customisation surface
   (+1.5-3 days).
5. **U5 CareCrew custom editors** (TimeOnlyMaskedInput, TimeOnlyDateEdit). Options: forward unmatched attributes in the
   components (+0.5-1 day), layout-id fallback (+1-2 days, unverified), or exclusion with the coverage report. Claude:
   exclude for now (the two in generic policies push on input; revisit when a blur-bound custom editor joins a policy).
   Codex: proposed the layout-id fallback; no selection in the combined draft.
6. ★ Explicit server-built descriptors; synchronous persistence; clears and reversals kept; chip off; no binding changes.
7. ★ Canonical conversion before any typed recovery; no caption-to-reference inference for lookups.
8. Copy-only states in v1 (lookup search text, number/date raw text, incomplete masks): include (Codex) or out (Claude,
   with the chart copy panel as the only copy path). Tied to U3.
9. Storage budgets after M0 device measurement: Claude 100 entries per owner; Codex 60 entries + 512 KiB per writer,
   2 MiB total.
10. Row label: 「（入力中だった値）」 (Claude) or 「入力中（未確定）」 (Codex / brief wording).
11. ★ Browser retention 60 minutes.
12. Clear this login's entries on logoff — Claude recommends yes (security section; Codex did not review).
13. At-rest encryption in localStorage — Claude recommends plaintext in v1 (same as the chart journal, narrowed); the
    alternative needs a key source and a secure-context check.
14. ★ Key `EditDraftCapture:Journal:Enabled`, fail closed, through the configurable section; Development on, Production
    off until the browser pass.
15. ★ Chart (F2) policies stay outside this engine's journal.
16. Escalated pre-existing defect F6 (chart journal two-tab loss): open a separate fix or accept.

### Not verified

- Every browser behaviour: attribute placement in the rendered DOM, masked-text observation, IME order on iPadOS Safari,
  Web Locks and private-mode storage on the devices in use, crash durability, storage limits.
- Exact 26.1.4 correspondence of installed source and dxdocs pages (some dxdocs pages returned 25.2 or 26.1.5 metadata).
- The ASP.NET Core 32 KB SignalR default and the sample's effective limit.
- Module import readiness and base-path handling without a host change.
- The layout-id fallback and grid inline customisation.
- Context lineage through capture, intake, claims and hard deletion.
- Production: deployed build, EditDraft table, effective `EditDraftCapture` keys (not checked; no DB).
- No build, test or browser run in this pass. Codex's node:vm and rule-level executions were not re-run by Claude.

### could_not_determine

- Which U1-U5 options the owner selects.
- Whether iPadOS Safari on the care-home devices supports Web Locks and treats the self-signed host as a secure context.
- Whether masked-editor observation records every displayed edit without disturbing caret or composition.
- How often staff run two app tabs in one browser profile (weight of F6 and of the liveness design).
