# Xaf.EditDraft 0.4.0-preview.1: XAF-native access

Run 2026-10-05-xaf-native-access-a0c6. Single model (Claude), by the owner's guardrails for XAF security-strategy code: no
second model designed, reviewed or tested this change. The owner reviews it (section 12). Base: `main` at 72c6469
(0.3.0-preview.2). Branch `feature/xaf-native-access`, merged into `main` and released as 0.4.0-preview.1 by owner
decision 2026-10-05 ("Accept the save-time check", "Keep: require Write", "Merge and publish now"); the owner's
security read of section 12 is pending and gates the first host's next production deploy.

## 1. Summary

The library no longer knows its first host's concepts. The scope (`ScopeOf`, `ScopeOid`), the owner kind
(`EditDraftOwnerKind`), the stored owner flag (`OwnerFlag`), the host's owner rule (`EditDraftOwnerRule`) and the
record-access seam (`IEditDraftRecordAccess`) are removed. Who may restore a draft onto a record, or recreate a record
from a draft, is now decided by the application's XAF security (roles, type, object and member permissions). One
optional host check, `IEditDraftAccessCheck`, is asked in addition and can only narrow. The owner seam
`IEditDraftOwnerResolver` stays; it answers "who owns a draft" and is now asked with the policy of the draft's type.

One deviation from the brief's wording, with source evidence (section 5): "IsGranted(Create, rebuiltObject) so role object
criteria are evaluated on its values" does not do that in DevExpress 26.1.4. XAF evaluates no object criterion for
Create, and its public `PermissionRequest` path answers a new object at type level only. The library therefore checks a
rebuilt record the way XAF checks a new object when it saves it: Create, then Write, then Read, on the object, with its
values, through XAF's server-side permission request. The sample test X2 shows the difference: with the plain
`PermissionRequest` calls a High Note was allowed; with the save-time check it is refused.

## 2. Status and counts

| Check | Result |
|---|---|
| `dotnet build Xaf.EditDraft.sln -c Release` | 0 warnings, 0 errors |
| Xaf.EditDraft.Tests | 358 total, 357 passed, 0 failed, 1 skipped (the known C25_C31 skip). Before: 352 total at 90f67cc (350 passed, 1 failed: G13, fixed on main by 0.3.0-preview.2, 1 skipped) |
| `npm test` in Xaf.EditDraft.Tests/js | 140 tests: 138 passed, 0 failed, 2 todo (unchanged; the journal was not touched) |
| Xaf.EditDraft.Sample.Tests | 65 total, 65 passed (59 before + 6 new; LocalDB was available, so the LocalDB fixtures ran) |
| `dotnet pack` Core and Blazor | Xaf.EditDraft.Core.0.4.0-preview.1 and Xaf.EditDraft.Blazor.0.4.0-preview.1 (+ snupkg), exit 0, no warning; package dependencies unchanged (Core: DevExpress.ExpressApp, .Xpo, Persistent.Base, Persistent.BaseImpl.Xpo 26.1.4, Newtonsoft.Json; Blazor: Core, DevExpress.ExpressApp.Blazor, .ConditionalAppearance 26.1.4) |
| Browser, sample on 127.0.0.1:5006 | section 9 |

Library test count: 352 − 4 deleted (section 10) + 10 added = 358.

## 3. Removed public surface

| Removed | What replaces it |
|---|---|
| `EditDraftTypePolicy.ScopeOf` | nothing in the library; a host computes its scope from the record in its own `IEditDraftAccessCheck` |
| `EditDraftTypePolicy.OwnerKind`, enum `EditDraftOwnerKind` (`HostDefined`, `Login`) | nothing; `EditDraftTypePolicy.IsGeneric` is now "has a decision table"; the owner is the owner seam's answer |
| `EditDraftOwnerRule` (`Decide(login, loginIsStaffMember, staffFound, staffIsGeneralUser)`) | nothing; it existed only for the first host's owner rule |
| `EditDraftStoreBase.OwnerFlag` (column LoginIsStaffMember), `EditDraftStoreBase.ScopeOid` (column SubSectionOid) | nothing in the base (17 members instead of 19); a host keeps the columns by declaring them on its own store class |
| `EditDraftSeed.OwnerFlag`, `EditDraftSeed.ScopeOid` | nothing |
| `EditDraftOwnerInfo.OwnerFlag` (`EditDraftOwnerInfo(Guid, bool)`) | `EditDraftOwnerInfo(Guid Oid)` |
| `EditDraftRecreateDraft.ScopeOid` | nothing |
| `IEditDraftRecordAccess` (`IsRecordVisible`, `IsScopeVisible`), `XafSecurityEditDraftRecordAccess`, `EditDraftServices.RecordAccess` | `IEditDraftAccessCheck` (optional), `XafSecurityEditDraftAccessCheck`, `EditDraftServices.AccessCheck` / `MayRestore` / `MayRecreate` |
| `IEditDraftRecreateHost.IsScopeVisible` | nothing (the scope step of the recreate is gone) |
| `IEditDraftRecreateCandidate.IsVisible` | `IEditDraftRecreateCandidate.MayRecreate` |
| `EditDraftRecreateOutcome.SubSectionNotVisible` | nothing |
| `EditDraftRecreateOutcome.FilledNotVisible` | `EditDraftRecreateOutcome.FilledNotPermitted` |
| `EditDraftTextSet.RecreateSubSectionNotVisible` (ja and en values) | `RecreateNoPermission` is shown for both recreate refusals |

Changed signatures (breaking): `IEditDraftOwnerResolver.Current(IObjectSpace, EditDraftTypePolicy)` and
`Current(XafApplication, EditDraftTypePolicy)`; `EditDraftServices.CurrentOwner(services, objectSpace, policy)` and
`(services, application, policy)`; `IEditDraftRecreateHost.CurrentOwner(EditDraftTypePolicy)`;
`EditDraftRecreate.Run(host, draftOid, objectType, proceedWhenSavedCheckFails)`. Internal: the writer's `TrySupersede`
no longer takes a scope. Changed texts: `RecordNotVisible` is "この入力控はこのログインでは戻せません（権限がありません）。" /
"This draft cannot be restored for this login (no permission)." No text and no library source names 事業所 any more
(test NA8 scans Core and Blazor).

## 4. The new contract

**`IEditDraftAccessCheck`** (Core, public, optional, one registration in DI):

```csharp
public interface IEditDraftAccessCheck
{
    bool MayRestore(XafApplication application, EditDraftTypePolicy policy, object record);   // a saved record
    bool MayRecreate(XafApplication application, EditDraftTypePolicy policy, object record);  // rebuilt, filled, uncommitted
}
```

**Composition** (`EditDraftServices.MayRestore` / `MayRecreate`): a missing argument is a refusal; the library's XAF check
(`XafSecurityEditDraftAccessCheck.Instance`) is asked first; only if it allows, the host's check is asked (skipped when
the host registered the library's own instance); both must allow; an exception anywhere is a refusal and is logged.
Every refusal logs `[EditDraft] restore|recreate refused for <type>: XAF security` or `...: the host's access check`.

**`XafSecurityEditDraftAccessCheck`** (Core, public, the default, always asked):

- `MayRestore`: the record must be of the policy's exact type. No request security (`XafApplication.Security` is null or
  not `IRequestSecurity`): allowed. Otherwise the record is loaded again by its key through
  `XafApplication.CreateObjectSpace(policy.Type)` — the secured object space in integrated mode, where a record the login
  may not read is not found — and `IRequestSecurity.IsGranted(new PermissionRequest(objectSpace, type, Write, loaded))`
  must be true. Object criteria are evaluated on the stored values.
- `MayRecreate`: the record must be of the policy's exact type. No request security: allowed. Otherwise its own object
  space is found (`BaseObjectSpace.FindObjectSpaceByObject`), the object must still be new there, and Create, Write and
  Read must be granted on it with its values. That evaluation is done by `IEditDraftNewObjectPermissions` (Core,
  internal), implemented in Blazor (`EditDraftNewObjectPermissions`, registered by `AddEditDraftBlazor()`), because it
  needs XAF's security assembly, which Core does not reference (no package added). It sends, per operation,
  `new NoCacheablePermissionRequest(new ServerPermissionRequest(type, record, null, operation, new SecurityExpressionEvaluator(objectSpace, strategy)))`
  to the integrated `SecurityStrategy` — the request XAF's XPO security itself builds before it saves a new object. With
  any other security, or without the registration, the recreate is refused (fail closed, logged once).

**Owner seam** (`IEditDraftOwnerResolver`, kept): default unchanged (the XAF login's key when it is a non-empty Guid; an
exception or no login is no owner, which refuses capture, offer, list and apply). New: every call passes the policy of
the draft's type, as `EditDraftRegistry.Find` returns it (null when the type is not registered). Screens that know the
type pass it (capture, list capture, offer, apply, badges, journal attributes, recreate). The drafts list, which shows
several types, uses `EditDraftOwnersByType` (Core, internal): it asks the seam once per generic policy and once for "type
not registered", reads each distinct owner (owner in the query, as before), and lists a row only when its stored owner is
the owner named for its type. 開く and 破棄 on a list row ask the seam for that row's type (`EditDraftList.ObjectTypes`).
With the default seam this is one owner and one query, as before.

## 5. XAF APIs used, verified in the DevExpress 26.1.4 sources (paths relative to the DevExpress source folder)

The installed sources report 26.1.4.0 (`DevExpress.ExpressApp.Design/DevExpress.ExpressApp.Design.ModelEditor.Protocol/AssemblyVersion.cs:52`);
the build resolves 26.1.4 from the DevExpress offline package folder.

| API | Where | What it shows |
|---|---|---|
| `IRequestSecurity.IsGranted(IPermissionRequest)` | DevExpress.ExpressApp/DevExpress.ExpressApp/Security/ISecurity.cs:110-113 | the request interface (DevExpress.ExpressApp assembly) |
| `PermissionRequest(IObjectSpace, Type, string, object, string)`, `SecurityOperations` | DevExpress.ExpressApp/DevExpress.ExpressApp/Security/PermissionRequest.cs:47-65 | constructor and operation names |
| `XafApplication.CreateObjectSpace(Type)` | DevExpress.ExpressApp/DevExpress.ExpressApp/XafApplication.cs:2420-2431, 1558-1571 | uses the registered provider (or a DI factory) |
| `BaseObjectSpace.FindObjectSpaceByObject` | DevExpress.ExpressApp/DevExpress.ExpressApp/BaseObjectSpace.cs:1262-1269; XPObjectSpace.cs:1640-1661 | XPO registers a locator in its static constructor |
| `XPObjectSpace.GetKeyValue` | DevExpress.ExpressApp/DevExpress.ExpressApp.Xpo/XPObjectSpace.cs:762-781 | key of an object of another session |
| `PermissionRequestProcessor.IsGrantedInSameRole` | DevExpress.ExpressApp/DevExpress.ExpressApp.Security/SecurityStrategy/PermissionPolicy/PermissionRequestProcessor.cs:414-428 | `targetObject != null && operation != Create` → object criteria; Create → type level only |
| `IPermissionPolicyObjectPermissionsObject` | DevExpress.Persistent/DevExpress.Persistent.Base/PermissionPolicyInterfaces.cs:104-111 | object permissions have Read, Write, Delete, Navigate states, no Create |
| `SelectCriteriaProcessor.GetObjectCriteriaForDeleteOrCreate` | .../SecurityStrategy/PermissionPolicy/SelectCriteriaProcessor.cs:286-298 | the Create criterion includes Write's, but is only compared with "false" |
| `PermissionRequestProcessorWrapper.IsGranted` | .../SecurityStrategy/PermissionPolicy/PermissionRequestProcessorWrapper.cs:100-125 | line 109: a NEW target object is dropped → type-level answer |
| `SecurityAdapterBase.IsGranted` | DevExpress.ExpressApp/DevExpress.ExpressApp.Security/Adapters/SecurityAdapterBase.cs:69-90 | line 79: the same for new objects |
| `SecurityRule2.ValidateObjectOnSave`, `IsGrantedCore` | DevExpress.ExpressApp.Modules/DevExpress.ExpressApp.Security.Xpo/ClientServer/SecurityRule2.cs:173-197, 83-89 | a new object is saved only when Create, then Write, then Read are granted; the request is a `ServerPermissionRequest` on the object, uncached |
| `SecurityStrategy.IsGranted`, `GetObjectSpace` | .../SecurityStrategy/SecurityStrategy.cs:370-374, 404-406 | dispatch to the select-data security |
| processors per request type | .../SecurityStrategy/SelectDataSecurityProvider.cs:134-136 | `ServerPermissionRequest` → `PermissionRequestProcessor` |
| `SelectDataSecurity.IsGranted` | .../SecurityStrategy/SelectDataSecurity.cs:225-246 | administrators granted first; `NoCacheablePermissionRequest` unwrapped, not cached |
| `ServerPermissionRequest`, `NoCacheablePermissionRequest` | .../SecurityStrategy/ServerPermissionRequest.cs:58-60, 99-107 | public constructors |
| `SecurityExpressionEvaluator(IObjectSpace, ISecurityStrategyBase)` | .../SecurityStrategy/ServerPermissionRequestProcessor.cs:60-90 | public, marked `[EditorBrowsable(Never)]`; evaluates the criterion on the object in memory |
| `DataManipulationRightService.CanCreate` / `CanEdit` | DevExpress.ExpressApp/DevExpress.ExpressApp/Services/Security/Internal/DataManipulationRightService.cs:183-199 | security asked only when it is `IRequestSecurity`; otherwise allowed |
| `SecuritySystem.IsGranted(IRequestSecurity, ...)` | DevExpress.ExpressApp/DevExpress.ExpressApp/SecuritySystem.cs:64-79 | returns false when security is null (see section 13, member writes) |
| `SecuredXPObjectSpace : ISecuredObjectSpace` | DevExpress.ExpressApp.Modules/DevExpress.ExpressApp.Security.Xpo/ClientServer/SecuredXPObjectSpace.cs:45 | the sample test asserts the application's space is secured |

## 6. Decision table (the library's XAF check, then the host check)

| Record | Application security | Host check | Result |
|---|---|---|---|
| saved | integrated `SecurityStrategy` | none | allowed when the record loads through the secured space and Write is granted on it |
| saved | integrated `SecurityStrategy` | registered | the above AND the host's `MayRestore` |
| saved | none (`Security` null or not `IRequestSecurity`) | none | allowed (as XAF's DataManipulationRight) |
| saved | none | registered | the host's `MayRestore` |
| saved | another `IRequestSecurity` | either | as the first two rows: `PermissionRequest` Write on the loaded record, then the host |
| rebuilt (new) | integrated `SecurityStrategy` | none | allowed when Create, Write and Read are granted on the object with its values |
| rebuilt (new) | integrated `SecurityStrategy` | registered | the above AND the host's `MayRecreate` |
| rebuilt (new) | none | none | allowed |
| rebuilt (new) | none | registered | the host's `MayRecreate` |
| rebuilt (new) | another `IRequestSecurity` (for example a middle-tier client) | either | refused (no evaluation of the values possible; fail closed) |
| any | any | any | a missing argument, a record of another type, a host exception or an XAF exception: refused |
| any | administrator | none | allowed (XAF grants administrators first); a registered host check can still refuse |

Before anything is rebuilt, the recreate still checks type-level Create and the views' AllowNew/AllowEdit
(`EditDraftCreateAccess.MayCreate`, unchanged).

## 7. Where the checks run

| Place | Before | Now |
|---|---|---|
| Offer when a record opens (`EditDraftRestoreControllerBlazor.TryOffer`) | `IsRecordVisible` before reading drafts | `MayRestore` after the drafts are read and before anything is shown; no draft, no check, no message |
| Apply (`EditDraftRestoreControllerBlazor.Apply`) | `IsRecordVisible` | `MayRestore`, immediately before the claim (with the owner, revision, view and member re-checks, unchanged) |
| Drafts list record text (`EditDraftListControllerBlazor.ResolveTarget`) | `IsRecordVisible` | `MayRestore`; otherwise "(cannot be shown)" |
| Drafts list 開く on a saved record (`OpenDraft`) | `IsRecordVisible` | `MayRestore` |
| Drafts list, open the saved record found by the already-saved check (`OpenSaved`) | `IsRecordVisible` | `MayRestore` |
| ListView row 開く (`EditDraftListBadgeControllerBlazor.OpenRecord`) | `IsRecordVisible` | `MayRestore` |
| Recreate step 4 | `MayCreate` and `IsScopeVisible` | `MayCreate` (unchanged) |
| Recreate step 8 (`EditDraftRecreateHostBlazor.Candidate`) | `IsRecordVisible` on the filled object | `MayRecreate` on the filled, uncommitted object; refusal → `FilledNotPermitted`, "You do not have permission to create this record.", the object is discarded, the row stays live at its new revision |
| Drafts list 「新規」 rows | listed, 開く refused at the click | listed; 開く disabled when `EditDraftCreateAccess.MayCreate` is false for the type (`EditDraftList.NotOpenable` → the action's TargetObjectsCriteria); the click path still re-checks everything |

The list behaviour picked for 「新規」 rows of a type the login may not create is "shown disabled", the one closest to
before (the row stayed visible and only the click was refused).

## 8. The sample

- `Updater.CreateRestrictedNotesRole` (public, used by the updater in Debug builds and by the tests): role
  `RestrictedNotes` with the template's user permissions, Note Read and Create (type), Note Write through an object
  permission `[Priority] <> 2` (not High), no Delete, the Note navigation item. Test user `Restricted`, empty password,
  Debug builds only, created through the template's `UserManager` like `User` and `Admin`. The Default role is unchanged
  (its template lines moved into a helper shared with the new role).
- `NoteEditDraftPolicy` lost its `OwnerKind` line; nothing else is declared for access.
- `SampleXafNativeAccessTests` (new; `SecurityStrategyComplex`, `SecuredObjectSpaceProvider`, an in-memory store, logon
  through `AuthenticationStandard`):
  X1 restore: as `Restricted`, both Notes load (Read granted), `MayRestore` is true for the Low Note and false for the
  High Note, and the refusal is XAF's;
  X2 recreate: type-level Create passes step 4; a Note rebuilt from a draft through `EditDraftRestorer.ApplyNew` with
  Priority High is refused, with Normal allowed; the same test shows that XAF's `PermissionRequest` grants Create and
  even Write on the new High Note (type level);
  X3 an administrator is allowed for both;
  X4 a host check that allows everything does not widen the restricted user's refusals; one that refuses everything
  refuses an administrator;
  X5 the role is the Updater's and is found by name.
- `SampleLegacyColumnsTests.L2` (new, LocalDB): a host store class that declares `LoginIsStaffMember` and
  `SubSectionOid` (the first host's column names) works with the writer; new rows get XPO's defaults; host values in
  the two columns survive the supersede, discard and claim statements; delete removes the row.

## 9. Browser proof (Development, 127.0.0.1:5006, LocalDB `XafEditDraftSample`, Chrome)

The database was updated once (`--updateDatabase --forceUpdate --silent`), which created role `RestrictedNotes` and user
`Restricted`. The Chrome window was hidden, so the XAF splash overlay stayed on top; it was hidden with a style change and
the checks used page text, element references, screenshots and the host log.

1. As `Admin`: Notes "BP-1 allowed" and "BP-2 becomes High" saved with Priority Low.
2. As `Restricted`: typed into each Note's Body and left without saving (`write create ok=True` ×2); a new Note with
   Priority High typed and left unsaved (`write create`, `write supersede ... entries=2`). Reopening BP-2 while still Low
   showed the offer (closed with Later).
3. As `Admin`: BP-2's Priority set to High and saved (database: Priority 2).
4. As `Restricted`:
   - BP-1: the offer "Unsaved input was found" listed Body; Yes put it back ("field(s) put back. Check them, then save.").
   - BP-2: no offer; message "This draft cannot be restored for this login (no permission)."; the form was read-only
     (XAF denies Write); log `restore refused for Note: XAF security` and `offer refused at 'activated': record FDF6309F
     of Note: this login may not restore onto it`.
   - Drafts list: three rows; BP-2's record text "(cannot be shown)" (log `restore refused for Note: XAF security`).
   - Drafts list 開く on the BP-2 row: the same refusal message.
   - Drafts list 開く on the 「新規」 High row: "You do not have permission to create this record."; log `recreate refused
     for Note: XAF security` and `recreate 470B3983: FilledNotPermitted at step 8 ... applied=2`.
   - A new Note "BP-new Low" typed and left unsaved, then 開く on its 「新規」 row: recreated in a modal window ("A record
     was created from the draft ..."); Save stored it and deleted the draft (`delete after save: rows=1`).

Not exercised in the browser: a 「新規」 row with 開く disabled. It needs a login that may not create the type; the
sample has no such role (`Restricted` may create Notes).

## 10. Tests deleted, adapted and added

Deleted, because this ruling removes the member they test:
- `EditDraftWave1Tests.W10_D6_owner_rule_login_owns_general_user_never_unreadable_staff_never_non_staff_login_owns` (`EditDraftOwnerRule`);
- `EditDraftNewRecordTests.T12_E29_N18_an_empty_stored_office_is_not_asked_but_the_filled_record_always_is` (the scope step);
- `EditDraftCloseGapsTests.G8_the_renamed_store_members_keep_their_database_column_names` (`OwnerFlag`, `ScopeOid`);
- the test case `T12_refusal_office_not_visible` (`SubSectionNotVisible`).

Adapted, because a removed or re-signed member or a removed 事業所 text is what they pin (each change carries a comment):
- `EditDraftCloseGapsTests`: G9_T34_T35 (RecreateSubSectionNotVisible line removed), G9_T36 (ja `RecordNotVisible` new
  text; RecreateSubSectionNotVisible line removed), G8_T32 (five lines asserting the removed members exist removed);
- `EditDraftLibraryBlazorTests`: E19 (pins follow `CurrentOwner(..., _policy)` and `MayRestore`), E22 (ja text), E22b
  (ja and en texts, the reworded log line; the per-file site counts 2/2/1 are unchanged);
- `EditDraftLibrarySeamTests`: SEC_the_writer_without_a_registered_store... (`TrySupersede` signature), SEC1_SEC2 (owner
  seam signatures, one-argument `EditDraftOwnerInfo`; the two `RecordAccess` lines replaced by `AccessCheck`/`MayRestore`);
- `EditDraftNewRecordTests`: the probe policy without `OwnerKind`; T1_E43_E30 (a policy without a decision table);
  T14_E18 pin (`OpenDraft`/`Recreate` with the type); the fake host and candidate (`CurrentOwner(policy)`,
  `MayRecreate`, no `IsScopeVisible`); T12 order test (now `policy, owner, read, saved, mayCreate, create, claim, fill,
  mayRecreate, show`); refusal and failure cases (`FilledNotPermitted`, `mayRecreate`); every `Run` call with the type;
  N24 (removed text line); SEC_D14 renamed `..._fails_closed_and_the_recreate_host_asks_the_access_check_on_the_filled_record`
  (scope assertions removed, pins updated); D3 and D5 `Run` calls;
- `EditDraftJournalTests.T64_lookups_numbers_booleans_dates_and_unknown_components_get_no_attribute` (journal test that
  built a `HostDefined` policy: now a policy without a decision table; minimal);
- `EditDraftWave1Tests.W38b` (the supersede's parameters renumbered; still five mutations, each naming the owner once);
- `EditDraftWave1bTests`: E1_E5, C6, E16_E17 (pins follow the policy argument and `MayRestore`);
- sample `SampleConsumerTests`: C2 (19 → 17 members), C3 (OwnerKind line removed), C3b (`IEditDraftAccessCheck` not
  registered), D1 (`AccessCheck` is null), D2 (signatures); `SampleFixPassTests.L1` (a fresh table has neither column;
  `TrySupersede` signature; ContextText instead of ScopeOid).

Added: library `EditDraftXafNativeAccessTests` NA1-NA9 (removed surface and new shapes; store and writer columns;
`IsGeneric`; no request security; host check narrows, cannot widen, exception refuses; owners per type; wiring of every
former access site; texts and sources name no host term; the Blazor registration of the new-object evaluation) and
`EditDraftNewRecordRecreateTests.ACC_the_owner_seam_is_asked_with_the_type_s_policy_and_a_draft_of_another_type_is_not_live`;
sample X1-X5 and L2 (section 8). No red test was revised.

`EditDraftLibraryM3Tests.C46` pins `EditDraftListItem`'s member list. To keep it, the per-row type and the "not openable"
set live on `EditDraftList` (`ObjectTypes`, `NotOpenable`) and 開く's disabled state is a TargetObjectsCriteria on the row
key, not a new row member.

Sensitivity checks: the first implementation of `MayRecreate` used `PermissionRequest`; X2 and X4 were red with it
(observed "But was: True"). A temporary removal of the host check from the composition turned NA5 and X4 red (reverted;
no mutation is left). A temporary removal of the Write check from `MayRestore` was refused by the tool environment's
guard and not run; X1's dependence on that check is read from the code, not executed.

## 11. Grep accounting (Scope, SubSection, OwnerFlag, StaffMember, F2, HostDefined, 事業所)

- Core and Blazor: no hit except `IServiceScope`/`CreateScope`/`AddScoped` (dependency injection) and the store base's
  upgrade note naming the two removed members. No 事業所, SubSection, StaffMember, HostDefined or F2.
- Library tests: the removal pins (NA1, NA2, NA8 word list), the adapted-test comments, `EditDraftFixPassTests` "F2"
  (a fix-pass item name, unrelated), and the historical G8 host-name list in `EditDraftCloseGapsTests`.
- Sample: dependency-injection scopes; the comments of the adapted tests; `SampleFixPassTests` "F2" (fix-pass item); the
  legacy-columns store class of `SampleLegacyColumnsTests` (by design).
- Docs: the consumer guide's upgrade table and legacy-columns example (by design); the dated design and milestone
  write-ups (2026-09-30 to 2026-10-05) are records of earlier runs and are left unchanged.
- Package release notes: the 0.2.0 history line names the old renames; the 0.4.0 line names none.

## 12. SECURITY — for the owner's review

Files whose authorization behaviour changed:

| File | Change |
|---|---|
| `Xaf.EditDraft.Core/EditDraftAccessSeam.cs` | new `IEditDraftAccessCheck`, `XafSecurityEditDraftAccessCheck`, composition `EditDraftServices.MayRestore/MayRecreate`, internal `IEditDraftNewObjectPermissions`; removed `IEditDraftRecordAccess` and its default. `EditDraftCreateAccess` and `EditDraftMemberAccess` unchanged |
| `Xaf.EditDraft.Blazor/EditDraftNewObjectPermissions.cs` (new) | XAF save-time evaluation of a new object (Create, Write, Read; `ServerPermissionRequest` + `SecurityExpressionEvaluator`, uncached); refuses anything but the integrated `SecurityStrategy` |
| `Xaf.EditDraft.Blazor/EditDraftBlazorServices.cs` | registers the evaluator (singleton, stateless) |
| `Xaf.EditDraft.Core/EditDraftOwnerSeam.cs` | owner seam takes the policy; `EditDraftOwnersByType` (per-type owners for the list); `EditDraftOwnerRule` removed |
| `Xaf.EditDraft.Core/EditDraftRecreate.cs` | step 1 resolves the owner for the type and requires the read row to be of that type; step 4 loses the scope check; step 8 is `MayRecreate` |
| `Xaf.EditDraft.Core/EditDraftTypePolicy.cs` | `IsGeneric` = decision table present (a policy with decisions and the former `HostDefined` kind would now be admitted; the first host has none) |
| `Xaf.EditDraft.Core/EditDraftWriter.cs` | seed and supersede no longer carry the scope; the owner fence is unchanged (W38b: five mutations, each `[OwnerUserOid] = @pN`) |
| `Xaf.EditDraft.Core/EditDraftCaptureController.cs`, `Xaf.EditDraft.Blazor/EditDraftListCaptureControllerBlazor.cs`, `EditDraftJournalAttributeControllerBlazor.cs` | owner asked with the policy; no scope in the seed |
| `Xaf.EditDraft.Blazor/EditDraftRestoreControllerBlazor.cs` | offer and apply use `MayRestore`; the offer check moved after the draft read (nothing is shown before it) |
| `Xaf.EditDraft.Blazor/EditDraftListControllerBlazor.cs` | per-type owners; `MayRestore` for the record text, 開く and the saved-record open; `FilledNotPermitted` mapped to "no permission to create"; 「新規」 rows of a type the login may not create are not openable |
| `Xaf.EditDraft.Blazor/EditDraftListPopupControllerBlazor.cs` | 開く/破棄 ask the owner of the row's type; 開く disabled for not-openable rows |
| `Xaf.EditDraft.Blazor/EditDraftRestorePopupControllerBlazor.cs` | 破棄 on the plan and on the read-only display asks the owner of the drafts' type |
| `Xaf.EditDraft.Blazor/EditDraftListBadgeControllerBlazor.cs`, `EditDraftRecreateHostBlazor.cs` | owner with the policy; `MayRestore` / `MayRecreate` |
| `samples/.../DatabaseUpdate/Updater.cs` | new role and test user (Debug builds) |

Points to decide or confirm:
1. Restore now needs XAF Write on the record. Before, the library default needed only that the record was found through
   the secured object space; the first host's rule added its 事業所 check. A role that can read but not write a record
   type can no longer restore drafts of it (its DetailView was read-only anyway).
2. The new-object check depends on `ServerPermissionRequest` and `SecurityExpressionEvaluator`. Both are public in 26.1.4;
   the evaluator is marked `[EditorBrowsable(Never)]`. They are the request XAF's XPO security builds at save. Re-verify
   on every DevExpress upgrade (X2 fails if the behaviour changes).
3. Unsecured applications are allowed (XAF's own rule). A host that wants fail-closed there registers a check.
4. With a security other than the integrated `SecurityStrategy` (middle tier), every recreate is refused. Restore uses
   the public `PermissionRequest` path and works there in principle; not tested (contract: integrated mode).
5. The drafts list hides a record's text when the login may not restore onto it (before: hidden only by the record
   rule). A read-only user sees "(cannot be shown)" for drafts of records it can read.
6. The owner seam per type: a row is listed only under the owner named for its type. With one owner (the default and the
   first host today) nothing changes.
7. The tests were written by the same model as the code (single-model run); agreement with the code is not independent
   evidence. The executed evidence is the XAF security run in the sample tests, the LocalDB runs and the browser run.

## 13. Upgrading an existing host

A host that used the removed members moves its own rules into its own code: a scope or organisational-unit rule
becomes its `IEditDraftAccessCheck` (`MayRestore` on the saved record, `MayRecreate` on the rebuilt one — the rebuilt
record carries the values the rule needs, so no stored scope is required); per-type ownership becomes its
`IEditDraftOwnerResolver` (which now receives the policy); extra columns an existing store table already has are
declared on the host's own store class with the same names and types, so the table does not change (see the
consumer guide §12 and `SampleLegacyColumnsTests`). The first host's own checklist is kept in that host's repository.

## 14. Not verified, follow-ups (not changed in this run)

- 開く disabled on a 「新規」 row: implemented and pinned, not seen in a browser (no sample role lacks Create).
- `EditDraftMemberAccess.CanWrite` asks the static `SecuritySystem.IsGranted`, which returns false when there is no
  security (`SecuritySystem.cs:64-79`). In an application without a security strategy every field would show as "cannot
  be restored" although the new record check allows. Pre-existing; not changed (scope).
- The Japanese `PersonalLoginOnly` text still says 「職員個人のログイン」 (a first-host wording, not a scope term); the badge
  controller's comments name a first-host ListView id. Left as they are.
- Existing-record rows of the drafts list keep 開く enabled when the login may not restore (refused at the click);
  only 「新規」 rows are disabled, as the brief asked.
