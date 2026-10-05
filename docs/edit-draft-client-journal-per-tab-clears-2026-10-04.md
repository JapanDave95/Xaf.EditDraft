# Xaf.EditDraft client-side journal — per-tab clears, conflicts resolved at intake (design note, 2026-10-04)

Collaborator run `2026-10-04-journal-per-tab-clears-991428`. Claude: Opus 5.5 (claude-opus-5-5). Codex: gpt-6-astra,
`-Effort xhigh` passed explicitly on every call (codex-cli 0.153.4): `diag` a1, `review` a1, `combined` a1. Worktree
`CareCrew-journal`, branch `design/edit-draft-client-journal`, HEAD `9a41df63` (M1b-A committed by git-committer during
this run; the run started on `63622641` with the M1b-A files uncommitted, and every pack and citation below is at
`9a41df63` with a clean working tree). Design note only: no code, no build, no database, no host, no commit.

Owner ruling 2026-10-04 (label verbatim): "Simplify: clears are per-tab only, conflicts resolved at intake; short design
note first" — described as: "Removes the cross-tab clear notices and the C1–C5 class with them. One collaborator design
pass (~30 min) to state the new rule and what the three-way intake must do, then one code pass for in-tab items (C3, C6
reopening rule, C7/C8, T73_T76)." (T73_T76 is already done.)

Citations: `js` = `Xaf.EditDraft.Blazor/wwwroot/edit-draft-journal.js`; `Core` = `Xaf.EditDraft.Core/EditDraftJournal.cs`;
`Payload` = `Xaf.EditDraft.Core/EditDraftPayload.cs`; `Offer` = `Xaf.EditDraft.Core/EditDraftOfferMerge.cs`; `design` =
`docs/edit-draft-client-journal-design-2026-10-03.md`; `m1` / `m1b` = the M1 and M1b-A write-ups; test files under
`Xaf.EditDraft.Tests/js/test/`. "M1b-C1..C10" are the M1b-A review defects (m1b §5); "C1..C7" without a prefix are this
run's Codex findings. `XPack` = the M1b-A requirement-only expectations (the `tests` a1 output of run 89eeeb, local scratch).

## 0. Combined answer

Two page loads never write the same journal entry key: the key carries a per-page-load id (js:45-47, 166-168), and the only
key several page loads write is the namespace clear marker (js:23). The M1b-C1..C5 races all come from `clear(ns)`
reaching other tabs through that marker and through cross-load sweeps (js:585-679, 848-863, 1059-1084); making clears
per-tab deletes the marker, the notice handling, the owed-marker retries and the take-back, and M1b-C1..C5 become not
applicable. Each page load then writes and removes only its own keys and evicts only its own entries (both analysts
recommend a per-load budget of 60 entries and 1,048,576 characters), and conflicts between tabs are left to the intake,
where each tab's entries are a separate editing context and the existing multi-draft offer shows them side by side, with
nothing applied without the user's tick. Two cross-load removals remain in today's code, expiry and server-echo `retire`,
and Codex showed with executed harness probes that both can delete fresh text when they race the owner's write; whether
to drop them entirely (Codex: strict ownership) or keep them only when the writer is proven gone by its Web Lock
(Claude) is the main owner decision, because strict ownership leaves nothing that ever removes a closed tab's entries.
The code pass is mostly deletion (8-12 hours) and does not settle the intake rules (M3), the logoff reach (a security
decision) or the browser's Web Lock lifetime.

## 1. Status

Design proposed, 2026-10-04. Not implemented. The note is uncommitted in the worktree. Owner decisions §8 are open.
Nothing of the module is deployed (m1b:32, 240-247; not re-checked against the server in this run).

> **Status note (2026-10-04).** Owner rulings after this note was committed (labels verbatim): O1 "O1-A: remove only
> when the writer's Web Lock is gone"; O2 "One-shot sweep of the whole owner namespace at logoff, no notice"; O3–O5
> "Keep the existing draft rules: D16 newest-first, Q6 selectable-but-unticked"; proceed "Yes, both". The code pass for
> O1-A, O2 and the §8 items both analysts agreed is implemented in M1b-C (docs/edit-draft-client-journal-m1b-c-2026-10-04.md);
> O3–O5 belong to the M3 intake and are not implemented. §4.4 and §8 below are left as written.

## 2. The decisive fact and the root cause (both)

| Fact | Evidence |
|---|---|
| Entry key = `XafEditDraft.j1|<ns>|<load>|<ctx>|<member>|<g>`; incomplete copy = that key + `|c` | js:20-21, 45-47, 714 |
| `<load>` is made once per module instance (one per document): base-36 time + 6 base-36 characters of `Math.random()`, no uniqueness check; `ns`, `ctx`, `m` may not contain `|` | js:61, 166-168, 187, 1111-1115 |
| So two page loads write the same entry key only on a load-id collision (same millisecond and same random suffix; `env.loadId` is a test-only injection, `journal()` passes none, js:1113). Isolation is conditional on distinct load ids | js:166-168, 187 |
| Heartbeat `XafEditDraft.hb1|<load>` and round-trip `XafEditDraft.rt1|<load>` keys are per load | js:869, 940-943 |
| The ONLY key several page loads write is `XafEditDraft.clr1|<ns>` (written by every clearing tab and by `landOwed`) | js:23, 654, 1066 |
| Other loads' keys are REMOVED today by: `clear`/`sweep` (every load of the namespace), `landOwed`, eviction (oldest of any load and namespace), `scan` expiry (any load's entries, heartbeats, markers), `value` expiry, `retire` | js:614-629, 669-678, 507-509, 342-365, 995, 1026-1049 |
| No application caller of `clear`, `list`, `value` or `retire` exists yet; the server calls `import`, `start` and `coverage` only | EditDraftJournalAttributeControllerBlazor.cs:278-279, 304 |

Root cause of M1b-C1..C5: `clear(ns)` (logoff/discard, js:1051-1058) removes every page load's keys of the namespace,
publishes a marker, sweeps again, and owes a refused marker; every other tab must read that marker before and after
each write (js:464-467, 491-502), carry it on every intent and field state (js:265-267, 295, 449, 478 `cm`, 704-711), and
act on its storage event (js:851-861 -> `clearSeen` js:603-611 -> `supersede` js:585-600). localStorage has no
compare-and-swap and Web Locks give no cross-renderer visibility guarantee (KB fix-552 prevention rule), so that protocol
has windows: M1b-C1a stale write taken back (js:491-502), C1b owed marker stops after 5 retries (js:637), C2 refused
take-back (js:494-495), C3 de-duplication against a value stored before an undelivered clear (js:468), C4 the older
clear's second sweep (js:1071), C5 a notice replayed after its marker expired (js:855-858).

"Clear" names three different operations; only the third is cross-tab (both):
- the user empties a field: an ordinary value intent `val:""` on the tab's own key (js:695-723; capture T7
  capture.test.js:173-189; m1b:126). Already per-tab. R-A1 calls it a clear because it is an ordered edit.
- a server echo: `retire(keys, echo)` with `echo = {seq, val}` per explicitly named key, after a durable server write
  (js:1020-1049). A server-set value with no user attempt is never journaled (`serverSet`, js:702, 827). It is not a clear.
- `clear(ns)`: the only path that sends anything to other tabs. The ruling changes this one.

## 3. Ruled out (both)

| Hypothesis | Disposition | Evidence |
|---|---|---|
| Two page loads with distinct ids share an entry key | Ruled out (collision only) | §2 |
| Removing the marker listener removes every foreign mutation | Ruled out: eviction, expiry and retire still remove other loads' keys | js:342-365, 509, 995, 1046 |
| A stale heartbeat proves a writer is gone | Ruled out: `writers()` reports a stale heartbeat as unknown; probe C1 below deletes a live writer's newer value | js:888-892, 1012-1046 |
| A marker-free write still needs the `close()` fail-closed state | Ruled out: every call of `close()` comes from a marker read; no remaining read decides whether the main write may happen | js:464-466, 497, 524-530 (settled in the review) |
| Baseline-equal or current-equal entries can be dropped before reconciliation | Ruled out: A->B drafted, B->A typed must stop offering B | design:160-163 |
| The existing multi-draft offer (D16) supplies every intake prerequisite | Ruled out: capture time, the fingerprint-to-payload hand-off, retired-context recognition and offer readiness are separate requirements | design:157-187; Offer:26-75 |
| The intake needs a new cross-tab merge in the browser | Ruled out: each page load has its own editing context (descriptor `ctx` = the capture's CurrentEditorInstanceId, Core:217-218), so tabs never share a context; conflicts are an offer-time question | Core:217-218; design:97-102 |

## 4. Proposed design

### 4.1 The new rule (both; line 4 is owner decision O1)

1. `clear(ns)` cancels this page load's pending work of the namespace and removes only its own `(ns, load)` entries and
   `|c` copies; it writes no marker and sends nothing to other tabs.
2. Emptying a field stays an ordinary empty-valued intent on the field's own key; capture and retries write only this
   load's keys and keep the entry's descriptor, ordering and capture time.
3. Storage events never rewrite or remove an entry or cancel an intent; a removal may be counted.
4. Another page load's keys — **O1-A (Claude):** removed by expiry or by a durable `retire` only when a successful
   `locks.query()` shows that load's writer lock is not held AND its heartbeat is absent or stale; with no Web Locks or a
   failed query, never. **O1-B (Codex):** never removed; expiry only makes them ineligible, `retire` answers `foreign-load`.
5. Conflicts between page loads are resolved at intake and in the offer; nothing is applied without the user's tick, and
   browser deletion is never the only protection against a replay.

### 4.2 Every storage path (both)

| Path | Today | Under the rule |
|---|---|---|
| `record` -> `intend` -> `apply` | own-key writes with marker checks before and after and a take-back | delete the marker parts; keep synchronous capture, current-intent retry, composition hold; store the capture time as `at` (§4.8) (js:442-512, 695-723) |
| `applyRemove`, `cleanCopy` | own-key removals with the newer-copy ordering guard | keep; a failed copy read is treated as "maybe present" and the removal is attempted (js:514-521, 557-570) |
| `clear` / `sweep` | every load's ns keys, marker, second sweep, owed marker | cancel local intents and replace local field states (local `supersede`, `refreshStates`); ONE pass over keys with prefix `PREFIX + ns + '|' + loadId + '|'`, copies included; truthful counts (js:585-629, 1059-1084) |
| remote `supersede`, `clearSeen` | cancel and remove this tab's own work on another tab's notice | delete (js:585-611) |
| `owe`, `scheduleOwed`, `landOwed`, the owed loop in `retryFailed` | retry the marker and remove other loads' keys | delete (js:550, 631-679) |
| `onStorage` | marker branch acts; removals are counted | delete the marker branch; the rest only counts (js:848-863) |
| eviction after a successful write | `planEviction` over all keys of all loads and namespaces | filter the candidates to this load, all its namespaces and copies (per-load budget, §4.3) (js:86-99, 507-509) |
| `scan` (start, list, after writes) | physically expires entries, heartbeats and markers of any load | ignore legacy markers; expire this load's own keys; other loads' keys per O1 (js:342-365, 508, 925, 962-981) |
| `value` | removes an expired key of any load | sequence and retention checks stay; foreign removal per O1 (js:988-996) |
| `retire` | any load's key on seq+value match, nothing pending here, heartbeat older than 2 min | own-key guards stay; foreign keys per O1; the old "removes only what the server holds" assurance is withdrawn (probe C1) (js:1000-1049) |
| heartbeat, writer report, pagehide | own heartbeat; reads all heartbeats and locks; removes own heartbeat at pagehide | keep (js:868-917) |
| round-trip probe | own key | keep (js:940-943) |

### 4.3 Eviction budget (both recommend; owner confirms)

Today the 60-entry budget is origin-wide over all namespaces and loads ("Owner decision 9 ... all namespaces", Core:46;
m1:119-124) and the 61st write evicts the oldest entry of any load. Recommended by both analysts independently: **per
page load, 60 keys and 1,048,576 serialized characters, all its namespaces and incomplete copies**. Reasons: rule line 1;
a closed or crashed tab's entries (the only copy of its text) are not evicted to make room for a live tab whose text is
still on screen; the design's original per-writer shape (design §8 item 9, :482-483). Consequences: the origin can hold
more than 60 keys (several tabs, and earlier loads until expiry or intake); with many heavy tabs the origin quota
(measured 5,242,880 characters, M0 §5 item 9) can be reached; then writes are refused, reported and retried and evict
nothing (capture T20b; XPack X27), and the host chart journal, which swallows storage errors, can lose entries. Rejected:
global budget with eviction of foreign keys (violates line 1); global budget evicting only own keys (a load cannot reclaim
foreign room; sixty abandoned foreign entries could block a new load). A different ceiling must change Core
`MaxSerializedChars` and the JS limit together: T72 pins them equal (EditDraftJournalTests.cs:345-353).

### 4.4 Foreign expiry and retire: the races and O1 (owner decision)

Codex executed three in-memory probes against the unchanged module, in the review and again in the combined call
(Claude did not re-run them):

```text
retire: echoed=v1; replacement=v2; retired=1; presentAfterBlur=false
expiry: predecessorAge=3600000; replacementAge=0; presentAfterBlur=false
time:   writeAtMinusCaptureAt=3300000; eligibleAtCapturePlus60Minutes=true
```

- **C1 (retire race):** tab A matched tab B's stored `v1` against the echo; B stored `v2` while A read B's heartbeat; A
  removed the key. B does not rewrite an unchanged value (js:573-576), so `v2` is gone from the journal. Window: B writes
  while its heartbeat is absent or older than 2 minutes (for example right after a bfcache restore: pagehide removes the
  heartbeat, js:917, and the next tick is up to 10 s later).
- **C2 (expiry race):** A's scan read B's expired entry; B wrote a fresh value to the same key; A removed it by the old
  entry's age. Window: B's user types again into a field whose entry is 60 minutes old while another tab scans.
- Both races exist in HEAD today. They are separate from M1b-C1..C5 and are not removed by deleting the markers.

The two positions (no executable check decides a policy; both stated):
- **O1-B, Codex — strict ownership.** No foreign removal at all; expiry makes foreign entries ineligible but leaves them;
  `retire` answers `foreign-load`. No race. Cost (Codex's own C2, agreed by Claude): nothing ever removes a closed load's
  keys, and after a reload every surviving entry belongs to an earlier load, so the M3 plan "`retire` after the durable
  promotion write" (m1:317-319) can never remove what the intake consumed. Claude's rough estimate, NOT measured: about
  600 characters per entry, 10 typed fields x 20 page loads a day is about 120,000 characters a day against about
  5.2 million, so a heavy user reaches the quota within weeks; then journal writes are refused and the chart journal loses
  entries. Codex: physical cleanup needs a later, separately designed exception.
- **O1-A, Claude — cleanup only with proof the writer is gone.** Foreign expiry and durable retire only when a
  successful `locks.query()` shows the writer lock `XafEditDraft.w|<load>` (js:877) is not held AND the heartbeat is absent
  or stale; where Web Locks are missing (the plain-HTTP dev origin, M0 §5 item 7) or the query fails, behave as O1-B.
  Not reviewed as an implementation and not tested: whether Chrome/Safari release the lock or keep it across bfcache,
  freezing and discard, and whether a restored page must re-request its lock and write a heartbeat before writing
  (pageshow). Codex (combined): the probes disprove the heartbeat-only behaviour; they do not establish the O1-A gate.
- Both reject keeping today's heartbeat-only foreign removal.

### 4.5 Discard and logoff reach

Discard: `clear(ns)` reaches only this page load's keys (both). A reload does not inherit the earlier load's ownership, so
"discard every entry currently listed" is no longer something the browser does; under the U1-A promotion model the
documents assume (m0:309, m1:317), a discard is a draft-row operation and the intake must recognise discarded contexts
(design S17, :167-170). Logoff: who else's entries a logoff removes is a security question, answered in §5 (Claude only).

### 4.6 Conflicts at intake (M3, not built)

Wording corrections to the brief (both): the descriptor's `t` is the TYPE name (js:58, 71, 477); the time is `at`. An
entry equal to its baseline is not dropped before reconciliation (design S6, :160-163). "Same (owner, type, Oid, member)"
across tabs means different editing contexts.

Intake sequence (both, with Codex's review additions marked B):
1. Read retained, sequence-frozen entries; validate every one as untrusted (server-side parser; §5). Incomplete copies,
   truncated values and copy-only kinds are not typed recovery (Core:343-366; copy-only states are out of v1, m0:309-311).
   Excluding an entry from typed recovery does not by itself authorise deleting it.
2. Skip entries whose writer is known to be alive (lock held or fresh heartbeat; design Q2 rule 6, :133-136); a stale
   heartbeat is "unknown", not "gone" (js:888-892).
3. Within one context and member: the newest generation wins, then the latest capture time; older values of that lineage
   are superseded, and a later no-op still supersedes them. Generation numbers of different contexts are not compared.
4. (B) Recognise retired contexts (discarded, claimed into another screen, expired, hard-deleted by a save) before
   promotion; when the state is unknown, keep the browser entry and do not promote it (design S17, :167-170).
5. Convert, then reconcile into the same context's server draft, keeping its first baseline (design S6; Payload:66-87);
   only then classify or omit.
6. Classify with the existing three-way rule: AlreadyApplied when the record holds the value (a Clear matches null or "",
   Core:437); with a known baseline raw, Clean when the current value equals it, else Conflict (Payload:126-134);
   (B) a journal-only entry whose fingerprint `bh` differs from the hash of the current value has no baseline raw after
   promotion, so it is **Unverifiable, unticked**, not Conflict (design S11, :157-159; Payload:15-20).
7. Promote each context into its own draft row (U1-A, deterministic DraftKey from owner and context, design :180-189).
   (B) Keep the entry's capture time as `LastCapturedOn` (max with the existing value) and the first-capture expiry anchor;
   never the intake time; operational "now" stays separate (writer time split, design F19; EditDraftWriter.cs:195-220).
8. (B) Finish intake before the automatic offer is consumed; a late intake re-runs the offer for the still-shown view
   (design S2, :171-174).
9. Offer: several contexts of one record -> several drafts in ONE offer (D16, Offer:7-16, 26-75): newest draft first,
   a member drafted again in an older draft shown but never pre-ticked with 「（新しい入力控に同じ項目があります）」, the
   newer value kept if both are ticked. Pre-tick only Clean/New without side effects (Payload:141-142). Apply re-checks
   everything (design Q5 item 5).
10. After a durable promotion, retire the browser entries per O1.

Resolution table (V = converted journal value, C = current canonical value, H = the descriptor's baseline fingerprint):

| Case | Resolution |
|---|---|
| V != C, hash(C) == H | Clean (current raw supplies BaseKnown/BaseRaw); pre-ticked subject to side effects, groups and duplicates |
| V == C | reconcile first (older lineage values must not reappear), then omit as AlreadyApplied |
| Reversal A -> B drafted -> A typed, record still A | reconcile A first, omit; a second intake must not offer B either |
| Journal-only, hash(C) != H | Unverifiable, unticked (not Conflict) |
| Same-context server draft supplies the baseline raw | ordinary three-way: Conflict when C differs from both baseline and V |
| No fingerprint, no server baseline | AlreadyApplied if V == C, else Unverifiable; never assumed Clean |
| V equals its baseline but the record changed elsewhere — **O4** | Claude: offer unticked (Unverifiable journal-only, Conflict with a server baseline), as the existing classifiers do (Core:433-441; Payload:126-134). Codex: omit after reconciliation, as the brief's literal no-op rule says |
| Two contexts (two tabs) edit the same member — **O3** | Claude: keep D16 (newest draft by `LastCapturedOn` first, older duplicate unticked). Codex: the member's latest capture time is primary, ordinal key as tie-break |
| A Conflict or Unverifiable row — **O5** | Claude: keep the generic engine's rule, selectable but not pre-ticked (owner Q6, Payload:111-116, 136-142). Codex: no ordinary restore without a clean baseline match; any override decided separately |
| Tab A ran `clear(ns)`; tab B kept text and is closed | A contributes nothing; B's text is offered on its own baseline. A's discard does not reach B (residual R1, O6) |
| Tab B still open | skipped while B's writer is alive; B still shows its own text |
| A's empty value is already the current value; B has text | A omitted as AlreadyApplied after reconciliation; B classified on B's own baseline (often Conflict/Unverifiable, not automatically Clean) |
| A has a newer unsaved empty value; B has older text — **O6** | "B always wins" is withdrawn (both): the newer value is primary and B stays available, subject to O3's ordering unit and O5's apply rule |
| Same context, two generations (after a save) | the newer generation supersedes; an old retry keeps its old key and capture time |
| Never-saved record | the descriptor carries the provisional Oid of that tab's screen object and its context (Core:215-218); two new-record contexts are never merged; journal-only recreation with the stored reconstruction raws and their original dates (U2-B; api-tabs T36) |
| Provisional Oid saved before intake | fresh read -> existing-record handling; never recreated (design S12) |
| Writer closed, frozen or stale | closure does not change classification; staleness alone authorises no deletion |
| Failed or partial promotion, lost acknowledgement, repeated intake | unconsumed material kept; consumed or retired lineage recognised without relying on browser deletion |

The canonical value of an emptied string (null or "") stays M2's decision (Core:328-329; m1:315-316); the browser keeps
`val:""`, Classify already accepts both as applied (Core:437), and their fingerprints differ (Core:163-168).

### 4.7 What M1b-C1..C10 become (both)

| M1b item | Under the rule |
|---|---|
| C1a stale write taken back; C1b owed marker exhausted | not applicable (no marker, no take-back, no owed marker). Check: A's clear neither deletes B's value nor cancels B's pending retry |
| C2 refused take-back | not applicable. A refused removal during this tab's own clear is reported and never re-activates pre-clear intent |
| C3 de-duplication against pre-clear storage | the cross-tab form is gone. In-tab it is correct today: a local clear resets `E.written` (js:595-598) and starts fresh field states whose baseline is the text shown (js:281-289), so re-typing exactly the shown text writes nothing and V->W->V writes V (Codex executed this probe). Code pass: one test pinning both halves |
| C4 older clear's second sweep | not applicable (one own-key pass). Simultaneous clears in A and B touch only their own keys |
| C5 notice replayed after marker expiry | not applicable. Old marker keys and events have no effect |
| C6 closed intent written on blur | the marker-triggered `close()` and its state are deleted; no replacement trigger is invented (settled in the review). A closed intent therefore no longer exists; ordinary pending retries still run on focusout (X13) |
| C7 M12 test gap | unchanged. Add a NEW test X14b (finish the composition under d1, then release the older d0 retry; the newer d0 copy survives; with a guard-removal mutation check). Codex ran the schedule: it passes on HEAD. X14 is not edited |
| C8 `clear().removed` undercount | the two-sweep scenario disappears. Report `{ok:true, removed:n}`; enumeration failure `{ok:false, removed:0, error}` (keeps M1 T35's last assertion green, api-tabs:238-239); a refused removal `{ok:false, removed:n, failed:k, error}` (X18, review-a1 C7 green) |
| C9 coverage coalescing | unchanged; needs a running XAF host; deferred to the M2 / §7 browser gate |
| C10 T73_T76 | already resolved in source by the owner's "Expect CopyOnly" ruling (EditDraftJournalTests.cs:390-392); run it in the code pass |

### 4.8 In-tab items and the M1b-A §4.3 open choices (both unless marked)

- Capture time (Codex found, both agree after checking js:449 vs js:477 and Core:317 "Write time"): serialize
  `at: it.at` (the time the value was made), never the write time; retries and unchanged re-reads must not advance it.
  Update the Core `At` contract to "capture time". This fixes ordering between tabs and stops a late retry from extending
  retention (probe: +3,300,000 ms).
- §4.3 item 3 — a refused d0 intent completes under d0's own key with d0's descriptor and capture time; never redirected.
- §4.3 item 4 — first attempt + 5 timer retries per value intent; unchanged re-reads do not renew the allowance; a new
  value does; afterwards focusout and the next event retry. The owed-marker cap that caused M1b-C1b goes with the markers.
- §4.3 item 6 — empty field = empty-valued intent; removing the entry instead would lose the evidence that supersedes an
  older same-context draft value.
- §4.3 items 1, 2 (X6 literal, lost marker) and 5 (reopening after fail-closed): withdrawn with the markers.
- Refused removal during this tab's own clear (Claude ★): report it, no automatic retry; the caller may call `clear` again.
  Codex: keep X18's guarantee (surviving bytes are not overwritten by cancelled intent); proposes no retrying clear.
- Legacy `XafEditDraft.clr1|*` keys on development browsers: ignore (both; never deployed).
- Cluster B (reply matching) is untouched; state replacement must keep fencing old callbacks (m1b §8).

### 4.9 Deletion list (both; line ranges at 9a41df63)

| Delete or replace | Location |
|---|---|
| `CLEAR_MARKER_PREFIX`; marker part of `scan`'s stamp handling | js:23, 333-357 |
| header comment on marker identity | js:15-17 |
| `clearsSeen`, `owed`, `clearCount`; stats `clearedElsewhere`, `closed`; `report().owedClears` | js:192-203, 1101-1103 (`droppedAfterClear` may stay for local clears) |
| field-state `clearMark`, `actionMark`, `valueMark`; `refreshStates`'s `keepMark` exception (local `refreshStates` stays) | js:248-295, 704-711 |
| intent-log rules 2-5 and `UNREAD`, `sameMark`, `readMark`, `markMatches`, `storedMark` | js:384-415 |
| `intend`'s marker argument; `cur.mark`, `written.mark`; serialized `cm` | js:442-451, 478, 504 |
| marker checks, `clearSeen` calls and the after-write take-back in `apply` | js:464-467, 491-502 |
| `close()` and its resets | js:419-420, 451, 524-530, 566-567, 593 |
| owed loop in `retryFailed` | js:550 |
| remote branch of `supersede`; `clearSeen` | js:585-611 |
| namespace-wide / keep-marker `sweep` -> own-key pass | js:613-629 |
| `owe`, `scheduleOwed`, `landOwed` | js:631-679 |
| marker branch of `onStorage` | js:851-861 |
| marker creation, second sweep, owed state and `marker:false` in `clear` | js:1051-1084 |
| O1-dependent: heartbeat-only foreign removal (`scan`, `value`, `retire`/`writerAlive`, `writerAliveMs`) | js:34, 342-365, 995, 1012-1046 — O1-B removes it, O1-A replaces it with the lock gate |

Stays: per-key intent sequence and page-wide order, current-intent retry, `pendingCount`, `markedVal`/`valueAt`, local
cancellation and state replacement, composition hold and the copy-ordering guard, de-duplication only after a successful
write, "a refused write evicts nothing", serverSet/acted rules, failure reporting, liveness reporting, sequence-frozen
`value`, start and coverage reports (js:259-320, 417-463, 514-575, 695-838, 868-981). "Fail closed on unreadable storage"
stays as an operation contract: a failed read never becomes a successful empty list, retire or clear (js:962-1049).
C#: Core `At` comment (capture time) and, if the per-load budget is chosen, the budget-scope comments (Core:46-56); no
other C# change unless the ceiling changes (then Core and JS together). The server-side entry parser is a single-model
security file (§5).

### 4.10 Test delta (both; conditional rows marked)

Delete — the requirement they assert is withdrawn by the ruling; deleted, not revised:
- `m1b-a.test.js`, 28 of 41: D4a (106), D4b (120), D4c (137), D4c' (170), D5a (185), D5b (205), D6 (232), X1 (272), X2 (297),
  X4 (344), X5 (359), X7/X8 (381), X9 (402), X16 (518), X17 (543), X17b todo (562), X19 (602), X20 (632), X21 (647), X43 (748),
  X44 (768), X44b (801), X1b (848), X8b (867), X16b (880), X17c (899), X20b (918), X44c (938). (X4, X7/X8 and X8b still pass
  under the rule but test the withdrawn notice mechanism; X1, X19, X20 and X21 carry local guarantees that new tests must
  keep.)
- `api-tabs.test.js` T35b (243-255); `review-a1.test.js` C4 (77-93).
- Per-load budget (both recommend): `capture.test.js` T22 (540-562); `api-tabs.test.js` T37 (280-302), T40 (334-352),
  T42 (368-382).
- Either O1 variant: `capture.test.js` T25 (602-621) and `review-a1.test.js` C6 (115-135), because their foreign-removal
  fixtures run without Web Locks (harness default `locks: null`, harness.js:171). Their own-key and pending protections are
  kept in new tests.

Keep: `m1b-a` D3a, D3a', D3b, D10, X3, X11, X13, X14, X18, X28, X35, X41, X42 (13); api-tabs T28, T35, T36, T38, T39, T41;
capture T7, T20b, T21, T23, T24; review-a1 C5, C7, C8; all C# tests (T72 pins limits, not markers; D8/D9 unaffected).

Add (from a requirement-only Codex `tests` call in the code pass): independent clears (A's and B's stored and pending work
survive the other's clear; simultaneous clears); mutation audit (no marker operation, no foreign capture/eviction/clear
mutation); local cancellation under enumeration and removal failures; local de-duplication (both C3 halves); empty values;
X14b; clear counts; per-load budget (59/60/61, serialized boundary, ties, namespaces, copies, reload-created ownership,
foreign entries untouched); shared-quota refusal; capture time; O1-B or O1-A cleanup cases (held lock, query failure, no
locks, fresh heartbeat, resumed writer); C1/C2 regressions (the newer value survives the read-then-delete interleavings);
legacy marker events have no effect; failure reports stay bounded and value-free. Use XPack's held-event, held-grant and
no-event profiles (XPack:338-374). The acceptance inventory (X40) is restated explicitly: retained, deleted and new tests.

X-expectations (XPack): withdrawn — O6, O7, X2, X4-X9, X15, X17, X43, X44, X21 (marker-closed reopening), the marker clauses
of X1, X16, X19, X20, X37; kept — O1-O5, O8, O9 (O1/O2 now for in-tab clears), X1 (local cancellation, counts, isolation),
X3, X10-X14 (+X14b), X18, X19-X20 (failure containment, bounded reports), X22-X30, X31 (own-key guards), X33-X35, X38-X39,
X41-X42; changed — X32 (heartbeat age alone no longer permits deletion; foreign behaviour per O1), X36 (scope per load if
chosen), X37 (anchored to capture time; physical foreign purge per O1), X40 (named inventory instead of "72 unchanged").

### 4.11 Estimate

8-12 engineering hours (Claude 8-10, Codex 8-12): marker deletion, own-key `clear`, eviction filter, capture-time fix,
test deletion and the new requirement-derived tests, suites, build, mutation check, the `tests` and `diffreview` calls and
the write-up. Not included: O1-A's lock gate and its browser verification (estimate after the owner chooses O1), M2/M3,
the XAF-host and IME gates, production prerequisites, deployment.

### How each disagreement was settled

| # | Item | State | Decided by |
|---|---|---|---|
| S1 | Claude A7/R2: "retire removes only what the server holds; no text is lost" | withdrawn | Codex's executed retire probe (C1) |
| S2 | Claude: "apply and scan expire at the same age" | withdrawn | source: apply uses the intent's capture time (js:449, 462), scan the stored write time (js:361, 477) |
| S3 | stored `at` is write time | adopted as a code-pass item | source (js:449, 477; Core:317) + Codex's executed probe |
| S4 | Codex diag: keep a closed state with a new trigger | dropped | source audit both accepted in the review: no remaining read decides the main write (js:464-466, 497, 524-530) |
| S5 | Claude's table: journal-only changed record = Conflict | corrected to Unverifiable | design S11 (:157-159); payload stores BaseKnown/BaseRaw, not a fingerprint |
| S6 | T42 obsolete under the per-load budget | adopted | api-tabs:380 asserts 60 keys origin-wide |
| S7 | T25 and review-a1 C6 obsolete "only under strict" (Claude's verdict) | corrected: under either O1 variant | harness default `locks: null` (harness.js:171) |
| S8 | Codex diag: new records have an "absent Oid" | corrected; conclusion kept | Core:215-218 (provisional Oid + context) |
| S9 | intake list lacked retired-context recognition and offer readiness | added | design S17/S2 (:167-174) |
| O1 | foreign expiry/retire | OPEN — owner | policy; facts agreed (§4.4) |
| O3 | ordering unit across contexts | OPEN — owner | product choice; D16 facts agreed |
| O4 | baseline-equal entry, record changed elsewhere | OPEN — owner | product choice; classifier facts agreed |
| O5 | Conflict/Unverifiable apply | OPEN — owner | product choice; existing behaviour agreed |
| O6 | newer empty vs older text; retained B after A's discard | both recommend; owner accepts | — |

Data-loss escalation (guardrails table): the C1/C2 races delete recovery text, and O1-B's accumulation ends in refused
writes; both are raised by the analysts and handed to the owner here. The run stops at this note.

## 5. Security (Claude only — single-model by the owner's rule; not sent to Codex)

S1. Per-tab clears remove a cross-tab deletion path in the module's own protocol. Today one `setItem` of
`XafEditDraft.clr1|<ns>` with a new value by any same-origin writer makes every open tab of that namespace cancel its
pending work and remove its own stored values written without that marker (js:851-861 -> js:603-611 -> js:595-596; and
`landOwed` removes other loads' keys, js:669-678). Under the rule the module never acts on a value another writer
controls, so an attacker-controlled tab cannot make the module delete another tab's journal. Limit: a same-origin script
can still call `removeItem`/`setItem` on any key directly (localStorage has no per-key access control); this is not an
XSS defence, it only stops one foreign write from being amplified into deletions in every tab. The marker value was
matched with a regex only (js:333-336); there was no injection path to remove.

S2. Logoff reach (owner decision O2). Design Q5 item 7 and §8 item 12 recommended removing this login's entries on logoff
(shared devices). Under per-tab clears a logoff removes only the logging-off tab's entries; the same owner's entries in
other open tabs and in closed or crashed tabs stay in plaintext localStorage for up to 60 minutes (and, under O1-B, until
something else removes them). Another person using the same browser profile can read them with developer tools; the
application does not offer them to another login (intake filters by the owner token the server recomputes, design Q5
item 1; a different login has a different namespace). The host chart journal already exposes all typed text for 60
minutes (design Q5 item 7), so the residual is not new in kind. Options: (a) per-tab only, the ruling's letter;
(b) logoff performs a one-shot sweep of every key with prefix `XafEditDraft.j1|<ns>|` (all page loads of this owner),
writes no marker and notifies nobody; a still-open tab may write again afterwards, and that text stays until expiry.
(b) promises no ordering, so it does not bring back M1b-C1..C5; it is an explicit exception to rule line 1 for logoff
only. **Claude recommends (b).** The call must run before XAF's sign-out navigation (M4; not verified).

S3. Intake's untrusted-input validation is unchanged: design Q5 items 1-6 apply to every entry whichever page load wrote
it. The server-side entry parser requires the key to equal the key rebuilt from the entry's own ns/load/ctx/member/
generation (Xaf.EditDraft.Core/EditDraftJournalBoundary.cs:71-72), rejects `|` in the load id (:65), bounds key and text
size (:55-56) and checks the format version (:62); it does not read `cm` (:52-80), so deleting `cm` needs no server change.
This settles the M1b-A could_not_determine item "the boundary parser's handling of `cm`".

S4. Browser hardening (small): `retire` checks liveness with the load from the entry JSON (`writerAlive(e.load)`,
js:1044) and `list` derives `self` from the JSON (js:975). The new own-keys `clear` filter must use the KEY segment; retire
and list should also take the load from the key segment, as the server parser does.

S5. Several journal drafts of one record in one offer add no trust boundary: apply re-checks owner, revision, AllowEdit,
record access, write permission, a fresh re-read and the restore guard (design Q5 item 5).

S6. Per-load budgets let many tabs, or a same-origin script, reach the origin quota; a same-origin script can do that
today. Not a new exposure. O1-A's `locks.query()` reads only lock names of this origin.

## 6. Deployment

- Build: CareCrew.Blazor.Server (project references to both libraries); the module
  is the RCL asset `_content/Xaf.EditDraft.Blazor/edit-draft-journal.js` imported by the attribute controller
  (controller:38, 278-279); published by the host's publish script; open pages need a reload.
- Mirror: none (no NHM counterpart; NHM has no Blazor host and no client journal). No schema change; no
  ChartWorkflowServiceV2, report layout or sync consumer. The sample host also references the library (Codex).
- Compatibility: the module has never been deployed, so there is no production transition. A development page still
  running the old module keeps its old cross-load sweeps until it reloads (Codex).
- Inert until `EditDraftCapture:Journal:Enabled` and the type key are set (m1b:240-242; not re-checked this run).

## 7. Verification plan (for the code pass)

1. Owner decisions O1-O6 and the budget scope recorded first.
2. Requirement-only Codex `tests` call from the restated rule (§4.1 with the chosen O1 variant) and the §4.6/§4.7 items.
3. Delete the tests listed in §4.10 in one commit-ready change; record the remaining suite as the baseline (non-zero
   totals).
4. Implement the deletions and the own-key `clear`, eviction filter and capture-time fix; run `npm test` in
   `Xaf.EditDraft.Tests/js`, `dotnet test` of Xaf.EditDraft.Tests and the solution build into
   `artifacts/claude-test/<run-id>`; mutation check of the remaining rules (including the M12 guard via X14b).
5. Codex `diffreview` on the frozen candidate.
6. Browser gate (M0 §7, owner): two tabs on https with Web Locks, a bfcache restore and a frozen tab, before O1-A's gate
   is enabled; M1b-C9 in the XAF host.

## 8. Owner decisions (★ = recommendation; where the analysts differ, each is named)

1. **O1 foreign expiry/retire** — ★ Claude: O1-A, cleanup only when the writer lock is confirmed not held and the
   heartbeat is absent/stale, strict where locks are missing. ★ Codex: O1-B, strict ownership; physical cleanup by a later
   separate exception. Both reject today's heartbeat-only removal.
2. **Budget scope** — ★ both: per page load, 60 keys and 1,048,576 characters (changes the scope of owner decision 9).
   A different ceiling changes Core and JS together.
3. **O2 logoff reach (security)** — per-tab only, or ★ Claude: one-shot owner-namespace sweep without a notice.
4. **Discard reach** — ★ both: this page load only.
5. **O3 ordering across contexts** — ★ Claude: keep D16. ★ Codex: per-member latest capture time.
6. **O4 baseline-equal entry with the record changed elsewhere** — ★ Claude: offer unticked. ★ Codex: omit.
7. **O5 Conflict/Unverifiable rows** — ★ Claude: selectable, not pre-ticked (owner Q6). ★ Codex: clean-only restore.
8. **O6** — ★ both: the newer value is primary, the other stays available; accept residual R1 (A's clear, reload, B's
   text offered when B is closed; never auto-applied).
9. ★ both: store the capture time as `at`.
10. ★ both: delete the marker-triggered closed state; no replacement trigger.
11. ★ both: §4.3 items 3, 4 and 6 as in §4.8.
12. ★ both: M1b-C9 deferred to the M2 / §7 host gate.
13. ★ both: delete the §4.10 tests and derive new ones from the restated requirement; restate the X40 inventory.
14. Refused removal during clear — ★ Claude: report, no automatic retry (X18 green). Codex: no retrying clear proposed.
15. ★ both: ignore legacy marker keys.

Residuals that remain under any choice: R1 (above); R3 many heavy tabs can reach the quota under the per-load budget and
the chart journal may lose entries; the load-id collision condition (§2).

## 9. Contribution log

### What Claude did

- Phase 0 (below); run id 991428; local scratch folder.
- Built parity pack v1 (CLAUDE.md, the brief verbatim, git facts, KB fix-552/548/419, the full module, Core, controller,
  harness and four test files, indexes of three more, the named document sections; security sections excluded) and
  launched `diag` before reading the code in depth.
- Independent analysis written to `claude-diagnosis.md` (saved 11:09, before Codex finished at 11:14): the key fact, the
  three meanings of "clear", the code-path inventory, the per-load budget, the intake via per-context promotion plus D16,
  the M1b-C table, the deletion list, the 28/13 test split, the X disposition, the estimate. Security written separately
  and not sent to Codex.
- Verified every Codex claim against source (`claude-verdicts-on-codex-diag.md`), built parity pack v2 (offer merge,
  payload classification, capture budget/expiry tests, the M1b-A requirement and XPack), ran the review and combined
  calls, and wrote this note.
- Got wrong (Claude): A7/R2 "retire loses no text" (disproved by Codex's probe); the verdict "apply and scan expire at the
  same age"; missed the write-time `at` (C3), T42, the S11 Unverifiable case, the S17/S2 prerequisites, and that T25 and
  review-a1 C6 break under O1-A too.

### What ChatGPT (Codex) did

- `diag` a1: independent design with path-by-path ownership table, strict-ownership recommendation, the C1-C5 findings
  (collision condition, cleanup gap under strict ownership, write-time `at`, reconcile-before-drop, "B remains
  available"), the same 28/13 split, N1-N15 test list, X disposition; read KB fix-552 and one dxdocs page; read the archived
  XPack itself.
- `review` a1: claim-by-claim check of Claude's analysis; ran the JS suite on HEAD (113: 112 pass, 0 fail, 1 todo) and five
  in-memory probes (retire race, expiry race, delayed-write time, local de-duplication, X14b); found C1-C6.
- `combined` a1: merged draft; re-ran three probes (3/3 reproduced); found C7 (T25/C6 under O1-A); kept O1, O3-O6 open.
- No file changes (0 `file_change`), no `node_repl` / `cua_repl` calls, no web search.

### Found issues, by tool

"Found by" = who raised it first; "both" only when raised independently in Phase 1. Nothing is observed in production
(never deployed).

| ID | Issue | Found by | Verdict | Evidence | Impact / likelihood / confidence / observed in prod | Decisive check | Outcome |
|---|---|---|---|---|---|---|---|
| F1 | entry keys per page load; marker is the only shared write key | both | confirmed (source) | js:23, 45-47, 166-168 | design basis / always / high / no | equal vs distinct loadId harness | adopted |
| F2 | load-id uniqueness is conditional | both | confirmed (source) | js:166-168, 187 | shared keys / collision only / high / unknown | equal-loadId harness | stated as a condition |
| F3 | M1b-C1..C5 come only from `clear(ns)` propagation | both | confirmed (source; M1b-A reproductions) | js:585-679, 848-863, 1059-1084 | — | — | not applicable after the change |
| F4 | no caller of clear/list/value/retire yet | both | confirmed (grep) | controller:278-279, 304 | — | — | no migration needed |
| F5 | three meanings of "clear" | both | confirmed (source) | js:695-723, 1026-1049, 1059-1084 | — | — | adopted |
| F6 | per-load budget 60 / 1,048,576 | both | design | Core:46-56; js:507-509 | — | — | owner decision 2 |
| F7 | strict ownership leaves closed loads' keys forever | Codex | confirmed (source consequence) | js:342-365, 995, 1026-1049 | refused writes after growth / every reload / mechanism high, rate unmeasured / no | full-capacity fixture after N loads | O1 |
| F8 | stored `at` is write time, not capture time | Codex | confirmed (source + Codex probe) | js:449, 477; Core:317 | ordering and retention drift / delayed retries / high / no | delayed-write probe | decision 9, code pass |
| F9 | reconcile before dropping a no-op | both | confirmed (design) | design:160-163 | wrong offer of B / reversal / high / n.a. | M3 intake test | adopted |
| F10 | "B always wins" withdrawn; B stays available | both | confirmed | capture:173-189 | — | — | O6 |
| F11 | `t` is the type name; time is `at` | both | confirmed (source) | js:58, 71, 477 | — | — | wording |
| F12 | D16 already merges several drafts of one record | Claude | confirmed (Codex review) | Offer:7-16, 26-75 | — | — | basis of O3 |
| F13 | heartbeat-gated foreign retire deletes a newer value | Codex | confirmed (Codex executed probe twice; not re-run by Claude) | js:1012-1046 | recovery text lost / writer active while heartbeat stale or absent / high in harness, browser rate unknown / no | interleaving probe | O1; F15 withdrawn |
| F14 | foreign expiry deletes a fresh replacement | Codex | confirmed (Codex executed probe twice) | js:352-361 | fresh text lost / overlap with a scan / harness / no | interleaving probe | O1 |
| F15 | retire "loses no text" (A7/R2) | Claude | REJECTED | F13 | — | — | withdrawn |
| F16 | apply and scan expire at the same age | Claude | REJECTED | js:361, 449, 462, 477 | — | — | withdrawn |
| F17 | journal-only changed record is Unverifiable after promotion | Codex | confirmed (design + source) | design:157-159; Payload:15-20 | mislabel / journal-only conflict / high / n.a. | promotion round-trip test (M3) | table corrected |
| F18 | intake must include S17 and S2 | Codex | accepted (design) | design:167-174 | re-promotion of retired work, missed offer / lost ack, late intake / high / n.a. | fake-store replay test | added to §4.6 |
| F19 | T42 obsolete under the per-load budget | Codex | confirmed (source) | api-tabs:368-382 | red test / always under E1 / high / n.a. | run T42 on the candidate | deletion list |
| F20 | a lower ceiling must change Core and JS together | Codex | confirmed (source) | EditDraftJournalTests.cs:345-353 | red T72 / if changed / high / n.a. | T72 | noted |
| F21 | T25 and review-a1 C6 break under either O1 variant | Codex | confirmed (source) | harness.js:171; capture:602-621; review-a1:115-135 | red tests / always / high / n.a. | run on the candidate | deletion list |
| F22 | keep a closed state with a new trigger | Codex | REJECTED (source audit, agreed in review) | js:464-466, 497, 524-530 | dead code / — / high / n.a. | reference audit | closed state deleted |
| F23 | new records have an absent Oid | Codex | REJECTED in part (conclusion kept) | Core:215-218 | — | — | wording corrected |
| F24 | per-member latest capture time as primary | Codex | open (product) | Offer:26-75 | — | two drafts with opposing orders | O3 |
| F25 | omit a baseline-equal entry when the record changed | Codex | open (product); existing classifiers offer it | Core:433-441; Payload:126-134 | — | pure classifier test | O4 |
| F26 | clean-only restore gate | Codex | open (product); existing rule selectable-unticked | Payload:136-142 | — | row status test | O5 |
| F27 | old pages keep cross-load sweeps during a transition | Codex | confirmed; development hosts only | js:614-679 | — | — | §6 note |
| F28 | in-tab C3 is correct today | Claude | confirmed (Codex executed probe) | js:281-289, 595-598 | — | probe | test only |
| F29 | X14b schedule covers M12 | Claude | confirmed (Codex ran it on HEAD) | js:557-570 | test gap / — / high / n.a. | X14b + mutant | add test |
| F30 | C8 undercount disappears with one pass | both | confirmed (source) | js:1065-1083 | — | count tests | adopted |
| F31 | X40 inventory must be restated | Codex | accepted | XPack:329-336 | — | — | §4.10 |
| F32 | O1-A lock-gated cleanup | Claude (after F13/F14) | unverifiable (not implemented; lock lifetime unknown) | js:877, 894-903 | — | browser gate §7 | O1 |
| F33 | per-load budget can starve the chart journal at quota | Claude | inferred, not run | design §2 table (chart journal swallows errors) | lost chart entries / many heavy tabs / low / no | multi-tab quota fixture | residual R3 |
| F34 | `cleanCopy` after a failed read | Codex | unclear as stated; current behaviour safe (agreed in review) | js:557-570 | — | copy-read failure test | no change |
| F35 | logoff reach | Claude (security) | owner decision | §5 S2 | — | — | O2 |

Found independently by both: F1, F2, F3, F4, F5, F6, F9, F10, F11, F30 and the 28/13 test split — coverage, not
confidence.

### Codex calls

| Run / call / attempt | Started | Duration | state | validation | exit | Model / effort req. | Effective effort | Reasoning tokens | Search | MCP tools | activity (commands / non-zero / file_change / outside-repo) | Pack | CLI |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 991428 / diag / a1 | 10:56:29 | 18.0 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 5,049 | off | KB lookup_known_fix, get_fix; dxdocs search, get_content | 13 / 0 / 0 / NHM checkout (read), powershell.exe | v1 | 0.153.4 |
| 991428 / review / a1 | 11:18:28 | 9.7 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 5,360 | off | KB lookup_known_fix, get_fix; dxdocs search, get_content | 9 / 0 / 0 / NHM checkout (read), powershell.exe | v2 | 0.153.4 |
| 991428 / combined / a1 | 11:30:55 | 11.9 min | success | ok | 0 | gpt-6-astra / xhigh | not observable | 4,372 | off | KB lookup_known_fix, get_fix | 8 / 0 / 0 / XPack file (read), powershell.exe | v2 | 0.153.4 |

No retries; no failed attempts.

### Setup checks (Phase 0; outputs in the local scratch folder)

| # | Item | Result |
|---|---|---|
| 1 | `BASH_MAX_TIMEOUT_MS` | present (2400000) |
| 2 | read-only query connection (HARD) | not applicable: no database query in this run (brief: no DB) |
| 3 | repo trusted (HARD) | present (the hook fired, item 5) |
| 4 | manifest (HARD) | 7/7 hashes match in the main repo and the worktree |
| 5 | hook fires (HARD) | `git push --dry-run origin HEAD` blocked with the hook's message; a Monitor running `Get-Date` was not blocked |
| 6 | collab.rules | file present; the `codex execpolicy check` command was itself BLOCKED by the hook (its argument text contains the push pattern) and was not retried with another syntax (ground rule 13); earlier today run 89eeeb recorded plain `forbidden`, wrapped shape no match. Counted as one hook false positive |
| 7 | `codex debug prompt-input` | AGENTS.md "Working with Claude (Codex)" present; CLAUDE.md not (pasted as pack item 0) |
| 8 | tool boundary (HARD) | no MCP tool that writes a database, migrates, deploys, pushes or restarts was used; KB write tools and claude-in-chrome unused |
| 9 | tool parity (HARD) | KB `enabled_tools` = the 9 read-only tools (in Codex's MCP configuration); dxdocs registered; DEVIATION (as in earlier runs): `node_repl` and `cua_repl` enabled for Codex, forbidden in every prompt, 0 calls in all three activity logs |
| 10 | models (HARD) | gpt-6-astra lists low, medium, high, xhigh, max, ultra |
| 11 | run id / scratch / salt / binary | 991428; salt written (unused: no personal data); codex-cli 0.153.4; login ChatGPT; `codex doctor` overall "warning" (optional MCP config issues, no Dev Drive, endpoint protection) |
| 12 | snapshot | start: HEAD 63622641 with the M1b-A files uncommitted (committer working); from pack v1 on: HEAD 9a41df63, clean |
| 13 | policy drift | agent file Phase 0 item 10 and the cost paragraph still say `medium` while ground rule 11 and the launcher say `xhigh`; CLAUDE.md says `could_not_determine` "must not be empty" while D1 (closed) allows the scope sentence |
| 14 | web search | off for all calls |

### Redaction

None needed: no database rows, logs or browser data; test values are synthetic.

### Inputs Codex did not have

- MEMORY.md (named in pack item 0; one line about this thread pasted).
- Claude's security section and the server-side entry parser `EditDraftJournalBoundary.cs` (single-model by the owner's
  rule): S1-S6 and the statement that the parser ignores `cm` were not cross-checked.
- O1-A (the lock gate) was formed after the review; Codex saw it only in the combined call, which did not test it.

### Passes used

2 cross-model passes (independent `diag`; `review` of Claude's analysis, with Claude's verdicts on `diag`), plus the
`combined` draft. Total Codex calls 3, attempts 3, all `success` / `ok`.

## 10. Not verified / open questions

- No build, test or browser run by Claude in this pass; every statement about current code is a source read at 9a41df63.
  Codex ran the JS suite on HEAD (113: 112 pass, 1 todo) and the probes quoted in §4.4; Claude did not re-run them.
- Whether the owner formally ruled U1-A (the M0/M1 documents assume it: m0:309, m1:317).
- Web Lock lifetime across bfcache, freezing and discard on the care-home browsers; heartbeat cadence in throttled tabs.
- Origin quota growth under either O1 variant and with the chart journal populated (the 120,000-characters-a-day figure
  is an estimate).
- The canonical value of an emptied string member (M2); the XAF logoff sequence for O2 (M4); M1b-C9 in a host.
- Production: deployed bytes and effective `EditDraftCapture:Journal:*` keys (not checked; no host).

could_not_determine:
- which O1 variant, O2 option and O3-O6 policies the owner chooses, and whether the per-load budget scope is authorised;
- whether O1-A's lock-plus-heartbeat gate gives a real "writer gone" guarantee across suspension and restoration;
- the durable M3 representation that recognises consumed or discarded contexts and keeps capture times through repeated
  intake;
- the extra engineering time O1-A's gate and its browser verification need.
