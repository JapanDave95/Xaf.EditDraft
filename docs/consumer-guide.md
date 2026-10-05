# Xaf.EditDraft consumer guide

How to add Xaf.EditDraft to a DevExpress XAF Blazor application, what the library checks at startup, and what stays the
application's responsibility. Version 0.4.1-preview.1 (the client-side input journal added in 0.3.0 is off by default
and not covered here; see the `edit-draft-client-journal-*` write-ups). The working example is `samples/Xaf.EditDraft.Sample` (one class,
`Note`); each step below names the sample file that does it. Upgrading from 0.3.0-preview.x: section 12. Upgrading from
0.4.0-preview.1: no code change; the Japanese `PersonalLoginOnly` text now reads 「このログインでは入力控を使えません。」.

## 1. What the library does

While a user edits a record, the library keeps a server-side copy of what they typed and did not save (a *draft*) and
offers it back the next time that user opens the record. Drafts belong to the XAF login that typed them, expire 7 days
after the first capture, and are never applied without the user's confirmation. A record that was never saved can be
recreated from its draft. The main header action "Drafts" (「入力控」 in the Japanese text set) lists the user's drafts.

## 2. Requirements and limits

| Item | Supported |
|---|---|
| .NET | 8 (`net8.0`) |
| DevExpress | 26.1.4 is the tested floor: the version the library is built and tested with. Older versions are not tested. |
| ORM | XPO only |
| Database | **SQL Server only.** The writer and the retention sweep use T-SQL. The startup check stops an application whose store is in another database. |
| Store table schema | `dbo` by default; another schema through the store class's XPO mapping, `[Persistent("myschema.MyEditDraft")]` (the only source of the schema and table name) |
| Records | XPO classes keyed by a Guid (DevExpress `BaseObject`), properties written through `SetPropertyValue` |
| Owner | an XAF login whose key is a Guid (library default); another owner rule, per type if needed, through `IEditDraftOwnerResolver` |
| Access | XAF security (roles, type, object and member permissions); one optional extra check through `IEditDraftAccessCheck` (section 11) |
| Registered types | different CLR class names (the payload stores `Type.Name`) |

## 3. What a consumer supplies

In this order.

1. **A business class** keyed by a Guid: an XPO class deriving from `BaseObject`, properties through `SetPropertyValue`.
   Sample: `Xaf.EditDraft.Sample.Module/BusinessObjects/Note.cs`.

2. **A store class**: one persistent class deriving from `Xaf.EditDraft.Core.EditDraftStoreBase`. The library ships only
   the non-persistent base; your class is the persistent one and names the table (`dbo.SampleEditDraft` in the sample).
   You own the class name: the security deny rows key on it. Sample: `.../BusinessObjects/SampleEditDraft.cs`.

   ```csharp
   public class SampleEditDraft : EditDraftStoreBase
   {
       public SampleEditDraft(Session session) : base(session) { }
   }
   ```

   The table has the 17 members of the base plus `Oid` and `OptimisticLockField`. A table created by 0.3.0-preview.x or
   earlier also has the columns `LoginIsStaffMember` and `SubSectionOid`; the library no longer reads or writes them
   (section 12).

3. **A policy per type** (`EditDraftTypePolicy`): the type, a `PolicyId`, the approved DetailView id(s), the ListView
   id(s), and one decision per member. A type without a policy is never captured; a member without a decision is never
   captured; a policy without a decision table is never admitted (a host may keep such policies in the same registry for
   its own code). Write the decisions with the library helpers; each takes the member, a reason and where the
   decision can be checked (both required by the decision gate `EditDraftDecisions.Check`):

   | Helper | Meaning | Policy list it must agree with | Label |
   |---|---|---|---|
   | `EditDraftDecisions.Restorable` | captured and put back as typed | — | A |
   | `EditDraftDecisions.Group` | moves with a driver | `Groups` | B |
   | `EditDraftDecisions.SideEffect` | its setter changes another record; selectable, never pre-ticked | `SideEffectMembers` | C |
   | `EditDraftDecisions.NotRestorable` | shown, never put back on an existing record | `NotRestorableOnExisting` | D |
   | `EditDraftDecisions.Excluded` | never captured | `Excluded` | X |

   The label is a record only; the gate does not read it. The policy says nothing about who may restore: that is XAF
   security (section 11). Sample: `.../EditDrafts/NoteEditDraftPolicy.cs`.

4. **Service registrations** (Startup):

   ```csharp
   services.AddEditDraftStore<SampleEditDraft>();                  // table: from the store class's XPO mapping
   services.AddEditDraftRegistry(NoteEditDraftPolicy.Register);
   services.AddEditDraftBlazor();                                  // options: o => o.HeaderActionOnEveryView = true
   services.AddEditDraftRetention();                               // optional: the retention hosted service (section 6)
   // optional: services.AddSingleton<IEditDraftAccessCheck, MyAccessCheck>();   // an extra access rule (section 11)
   // optional: services.AddSingleton<IEditDraftOwnerResolver, MyOwnerResolver>(); // another owner rule (section 11)
   ```

   `AddEditDraftBlazor()` also registers what the recreate needs to check XAF permissions on a rebuilt record's values;
   without it a recreate is refused.

   The library reads the store table's schema and name only from the store class's XPO mapping, the same name XPO's own
   reads and inserts use. For a schema other than `dbo`, set `[Persistent("myschema.MyEditDraft")]` on the store class.
   Sample: `Startup.AddEditDrafts` in `Xaf.EditDraft.Sample.Blazor.Server/Startup.cs`.

5. **Two modules** in the XAF module list: `.Add<EditDraftCoreModule>()` and `.Add<EditDraftBlazorModule>()`.

6. **The non-persistent object space provider**: `builder.ObjectSpaceProviders ... .AddNonPersistent();`. The restore
   popup and the drafts list are non-persistent objects. The DevExpress template has the line, and XAF 26.1 adds the
   provider itself when none is registered (`XafApplication.EnsureNonPersistentObjectSpaceProvider`); the Blazor module
   stops the application at setup if it is still missing (an application that overrides that method).

7. **The switches** in configuration, section `EditDraftCapture` (another section through
   `services.AddSingleton(new EditDraftSwitchOptions { Section = "..." })`). Only a value that reads as the boolean `true`
   is on; missing, empty, `1`, `yes` or anything else is off.

   | Key | Turns on |
   |---|---|
   | `EditDraftCapture:Enabled` | capture at all (with the per-type key) |
   | `EditDraftCapture:Types:<PolicyId>:Enabled` | capture of one policy |
   | `EditDraftCapture:ListViews:Enabled` | capture in the policy's ListViews |
   | `EditDraftCapture:NewRecords:Enabled` | capture of never-saved records of policies with `AllowNewRecords` |
   | `EditDraftCapture:Retention:Enabled` | deletion of expired drafts by the hosted service (section 6) |
   | `EditDraftCapture:Retention:IntervalMinutes` | minutes between sweeps (default 60, at most 1440; 0 or below turns the hosted sweep off) |
   | `EditDraftCapture:StartupChecks:Table` | the optional startup check that the store table exists (section 4) |

   Restore, the drafts list and recreating new records work while the store table exists, whatever the capture switches
   say, so switching capture off does not hide drafts still inside their 7 days.

8. **The row-badge stylesheet** in the host page:
   `<link href="_content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css" rel="stylesheet" />`. Not checked at startup.

9. **The store's security**: in the ModuleUpdater, on every database update,
   `EditDraftSecurity.DenyStoreToAllRoles(ObjectSpace, typeof(SampleEditDraft));` then `ObjectSpace.CommitChanges();`.
   See section 5. Sample: `.../DatabaseUpdate/Updater.cs`.

10. Optional: the header action's look. It shows caption and image by default (id `EditDraftListBlazor`); an
    `ActionDesign` node in the application model can change it.

## 4. Startup checks

When the application's setup completes, the modules check the configuration and stop the application with an
`EditDraftConfigurationException` whose message lists each problem and its fix:

| Check | Fails when | Fix named in the message |
|---|---|---|
| Store registered | no `AddEditDraftStore<TStore>()` | register your store subclass |
| Policies | `EditDraftCapture:Enabled` is true and no policy is registered | `AddEditDraftRegistry(...)` with a policy, or switch capture off |
| SQL Server | the store's XPO data store is another provider (decided by its type; no statement is run) | keep the store in SQL Server |
| Schema and table name | XPO's SQL Server provider names the store table differently from the store class's mapping, for example because of a changed `ObjectsOwner` or table prefixes (no statement is run) | name schema and table in `[Persistent("schema.table")]` on the store class |
| Non-persistent provider (Blazor module) | no `NonPersistentObjectSpaceProvider` | `.AddNonPersistent()` |
| Table (optional, `EditDraftCapture:StartupChecks:Table` = true) | after the XAF database update's schema update, the table is still not found (the update stops). At setup a missing or unreadable table is only a warning. | run the XAF database update, or map the store class to the schema and table the table is in |

The table check does not stop a fresh database's first update: at setup the table may not exist yet, because the XAF
database update that creates it runs after setup, so a missing table is logged as a warning there. The check runs again
after the update's schema update, where a missing table stops the update with the message above. It can therefore stay
on from the first run. The blocking form of the check runs only during a database update: an application that starts
without one gets the setup warning for a missing table, not a stop.

The database checks and the role warning are remembered per store class and database (its connection string) once they
have passed, so they are not repeated for every circuit. A check that fails or cannot run, an unidentified data store, or
a missing table is logged and checked again at the next application setup. A data store the check cannot identify (a pool
or cache wrapper) is logged as a warning and not stopped. An application without a service provider (a headless or
design-time application) is not checked. Not checked: the stylesheet link, the two module lines, `AddEditDraftBlazor()`;
leaving one out shows as a missing feature, not as a startup error.

At the same point the library logs a warning for each role that may be able to read the store class through XAF security
(section 5).

## 5. Security

- **The writer does not use XAF security for the store.** It works on a non-secured object space: it creates a draft with
  `CommitChanges`, reads drafts with queries, and updates or deletes them with T-SQL. Every read, update and delete names
  the owner (`OwnerUserOid` = the XAF login's Guid) in the statement; that condition keeps one login's drafts from
  another's. XAF role permissions do not apply to the store table. Whether a draft may be put back onto a record, or a
  record recreated from one, is decided by XAF permissions on that record (section 11).
- **Why deny the store anyway.** A role can still reach the store class through ordinary XAF security — a generic list
  view such as `SampleEditDraft_ListView`, a lookup, the Web API — where only its permissions apply. A role with
  `AllowAllByDefault` could then read and change every user's drafts, and the payload is the typed text in readable JSON.
  `EditDraftSecurity.DenyStoreToAllRoles` gives every role an explicit DENY of Read, Write, Create, Delete and Navigate on
  the store class (idempotent; it reuses the role's existing row for the type). The deny does not affect capture,
  restore or the drafts list.
- **What the deny does not cover:**
  - a role with `IsAdministrative = true` — XAF does not apply deny rows to an administrative role ("You cannot deny any
    rights for a role with the Administrative Permission", XAF 26.1, Type, Object and Member Permissions). The rows are
    written anyway, so they apply if the flag is cleared. If administrators must not read drafts, do not give them an
    administrative role;
  - object or member permissions that ALLOW access to the store — XAF applies them over a type deny. Do not add object
    or member permissions for the store class;
  - a role created after the last database update — it has no deny until the next update runs the helper.
- **The warning.** `EditDraftSecurity.FindRolesThatCanReadStore` / `WarnRolesThatCanReadStore` report the roles that can
  still read the store: administrative roles, roles with an object or member ALLOW on the store, roles with a type ALLOW on
  the store or a base type, and roles whose permission policy is `AllowAllByDefault` or `ReadOnlyAllByDefault` with no
  deny. It reads the role rows; it does not run XAF's permission evaluation for a user. The deny helper logs the roles it
  could not bind, and the startup check logs every role it finds.
- **The role scan is best-effort.** It scans only roles of `PermissionPolicyRoleBase` and its subclasses: roles of a
  class that does not derive from `PermissionPolicyRoleBase` are not scanned at startup (the helpers take another role
  type as `roleType`; the startup warning does not). It does not evaluate the criteria of object and member grants, so a
  grant whose criterion never matches is still reported as able to read. Read the warning as a list of roles to review;
  no warning is not proof that no role can read the store.
- **Audit trail.** If the application uses the XAF Audit Trail module, exclude the store class, or every capture copies
  the typed text into the audit log.
- **Retention.** Turn on a sweep (section 6), or expired rows stay in the table.

## 6. Retention

A draft expires 7 days after its first capture (`ExpiresOn`, set once) and is hidden from that instant. Its row stays in
the table until something deletes it. The library deletes nothing unless the application turns a sweep on:

- **Hosted service:** `services.AddEditDraftRetention()` and `EditDraftCapture:Retention:Enabled` = `true`. One minute
  after start, then every `EditDraftCapture:Retention:IntervalMinutes` minutes (default 60), it deletes the rows with
  `ExpiresOn` at or before "now", for every owner, in batches of 1000, and logs the count
  (`[EditDraft] retention sweep: N expired draft row(s) deleted ...`). The switch is re-read before each sweep.
  The interval is at most 1440 minutes (one day); a larger value is used as 1440. A value of 0 or below turns the hosted
  sweep off: the service logs a warning and stops, and it starts again only when the application restarts with a
  positive value. A value that is missing or not a whole number means 60.
- **Your own scheduler:** call `EditDraftRetention.Sweep(serviceProvider)`; it returns the number deleted, or -1 when it
  could not run (logged).

**Clock.** `ExpiresOn` is written in the **application server's** local time (the registered `TimeProvider`, else the
system clock). The library's sweep uses that clock's "now" as a parameter. A SQL Agent job instead must use the same
clock: `DELETE FROM dbo.SampleEditDraft WHERE ExpiresOn <= GETDATE();` is right only when SQL Server and the application
server run in the same time zone; otherwise pass the application server's local time as the cutoff.

## 7. Library defaults

| Seam | Default | To change it |
|---|---|---|
| Owner of a draft | the XAF login's key when it is a non-empty Guid, for every type; no login, no draft | register an `IEditDraftOwnerResolver` (asked with the policy of the draft's type) |
| Access | XAF security only: Write on a saved record, Create, Write and Read on a recreated one (section 11) | register an `IEditDraftAccessCheck` (asked in addition; it can only narrow) |
| Switch section | `EditDraftCapture` | `new EditDraftSwitchOptions { Section = "..." }` |
| Store schema | `dbo` | `[Persistent("myschema.MyEditDraft")]` on the store class |
| Clock | `TimeProvider.System`, local time | register a `TimeProvider` |
| Log | the application's `ILogger`, category `Xaf.EditDraft`, lines start with `[EditDraft]` | set `EditDraftLog.Sink` at startup |
| Texts | English | `EditDraftTexts.Use(EditDraftLanguage.Japanese)` or your own `EditDraftTextSet` |

The Japanese set's list lead says drafts are deleted at expiry; that is true only with a retention sweep.

## 8. Opening the drafts list

- The header action "Drafts" shows while a ListView of a registered type is on show, and lists that type's drafts.
- With `AddEditDraftBlazor(o => o.HeaderActionOnEveryView = true)` it shows on every view; where no registered ListView
  is shown it lists the drafts of every registered type.
- From your own UI: inject the per-circuit `EditDraftListBridge`, show the entry while `IsAvailable`, call `Open()`.

## 9. Store table availability

Restore and the drafts list are offered only where the store table exists. The answer is cached per database: "present"
for 5 minutes, "absent" for 30 seconds, so an application started before its first database update shows them at most 30
seconds after the table appears.

## 10. Upgrading from 0.1.0-preview.1

| 0.1.0-preview.1 | 0.2.0-preview.1 |
|---|---|
| `EditDraftStoreBase.LoginIsStaffMember` | `OwnerFlag` (column unchanged) |
| `EditDraftStoreBase.SubSectionOid` | `ScopeOid` (column unchanged) |
| `EditDraftSeed.LoginIsStaffMember`, `EditDraftOwnerInfo.LoginIsStaffMember` | `OwnerFlag` |
| `EditDraftSeed.SubSectionOid`, `EditDraftRecreateDraft.SubSectionOid` | `ScopeOid` |
| `EditDraftTypePolicy.SubSectionOf` | `ScopeOf` |
| `IEditDraftRecordAccess.IsSubSectionVisible`, `IEditDraftRecreateHost.IsSubSectionVisible` | `IsScopeVisible` |
| `EditDraftOwnerKind.F2StaffMember` | `EditDraftOwnerKind.HostDefined` |
| `EditDraftCaptureControllerBlazor` | `EditDraftCaptureController` |

Behaviour changes: the startup checks above; the English texts `PersonalLoginOnly`, `RecordNotVisible`,
`RecreateSubSectionNotVisible` and `ListLead` are reworded; the header action shows caption and image; a grid row's
"Open draft" icon keeps the enabled state XAF gives it (with a row without a draft selected, the icons are disabled like
the toolbar button); an absent store table is re-checked after 30 seconds instead of 5 minutes.

## 11. Access: XAF security plus the optional IEditDraftAccessCheck

Who may put a draft back onto a record, or recreate a never-saved record from a draft, is decided by the application's XAF
security: its roles and their type, object and member permissions. The library has no access rule of its own besides the
owner condition of the store.

- **A saved record** (the offer when the record opens, the apply, the drafts list's Open and its record text, the Open
  of a ListView row): the record is loaded again by its key through `XafApplication.CreateObjectSpace` (the secured
  object space when the application uses integrated security, where a record the login may not read is not found) and
  **Write** must be granted on it; role object criteria are evaluated on its stored values. A refused offer shows "This
  draft cannot be restored for this login (no permission)." The offer asks only when there is a draft to offer, so a
  record the login may only read opens without a message. Member permissions still apply per field: a field the login
  may not write is shown as "cannot be restored", as before.
- **A never-saved record** (Open on a "New" row): the record is rebuilt from the draft in its own object space, filled,
  not committed, and **Create, then Write, then Read** must be granted on it with its own values — the check XAF makes
  before it saves a new object. XAF evaluates no object criterion for Create, and XAF's public `PermissionRequest`
  answers a new object at type level only, so the library evaluates the rebuilt object through the server-side
  permission request XAF itself uses at save (registered by `AddEditDraftBlazor()`; XAF's integrated `SecurityStrategy`
  only, any other security refuses). A refusal shows "You do not have permission to create this record." and discards
  the rebuilt record; the draft stays. Before anything is rebuilt, type-level Create and the views' AllowNew/AllowEdit
  are checked as before; a "New" row whose type fails that check stays in the drafts list with Open disabled.
- **No security strategy** (`XafApplication.Security` is null or not an `IRequestSecurity`): allowed, as XAF itself
  allows creating and editing then.
- **`IEditDraftAccessCheck`** (optional, one per application, registered in DI): `MayRestore(application, policy,
  record)` and `MayRecreate(application, policy, rebuiltRecord)`. It is asked after the XAF check and only when XAF
  allowed; both must allow, so it can only narrow access. An exception in it is a refusal. Register one only for a rule
  XAF security does not express, for example an assignment table that is not a role:

  ```csharp
  public sealed class MyAccessCheck : IEditDraftAccessCheck
  {
      public bool MayRestore(XafApplication application, EditDraftTypePolicy policy, object record) => MyRule(application, record);
      public bool MayRecreate(XafApplication application, EditDraftTypePolicy policy, object record) => MyRule(application, record);
  }
  ```

- **The owner seam** (`IEditDraftOwnerResolver`) answers "who owns a draft", never "who may restore it". It is asked with
  the policy of the draft's type (null when the type is not registered), so an application can name a different owner per
  type. The drafts list asks once per type, reads each distinct owner, and lists a row only when its stored owner is the
  owner named for its type. No owner refuses capture, offer, list and apply.

Sample: the `RestrictedNotes` role in `.../DatabaseUpdate/Updater.cs` (Note Read and Create; Write only where `Priority`
is not `High`) and its test user `Restricted` (empty password, Debug builds only), checked in
`Xaf.EditDraft.Sample.Tests/SampleXafNativeAccessTests.cs`.

## 12. Upgrading from 0.3.0-preview.1 and 0.3.0-preview.2

| 0.3.0-preview.x | 0.4.0-preview.1 |
|---|---|
| `EditDraftTypePolicy.ScopeOf` | none in the library; a host that needs a scope computes it from the record in its own `IEditDraftAccessCheck` |
| `EditDraftTypePolicy.OwnerKind`, `EditDraftOwnerKind` (`Login`, `HostDefined`) | none: a policy is admitted when it has a decision table (`IsGeneric`); the owner is the owner seam's answer for the policy |
| `EditDraftOwnerRule.Decide` | none; a host keeps its owner rule in its own `IEditDraftOwnerResolver` |
| `IEditDraftOwnerResolver.Current(IObjectSpace)`, `Current(XafApplication)` | `Current(IObjectSpace, EditDraftTypePolicy)`, `Current(XafApplication, EditDraftTypePolicy)` |
| `EditDraftServices.CurrentOwner(services, objectSpace)`, `CurrentOwner(services, application)` | the same with the policy as a third argument |
| `EditDraftOwnerInfo(Guid Oid, bool OwnerFlag)` | `EditDraftOwnerInfo(Guid Oid)` |
| `EditDraftStoreBase.OwnerFlag` (column `LoginIsStaffMember`), `EditDraftStoreBase.ScopeOid` (column `SubSectionOid`) | none in the base; keep the columns by declaring them on your store class (below) |
| `EditDraftSeed.OwnerFlag`, `EditDraftSeed.ScopeOid`, `EditDraftRecreateDraft.ScopeOid` | none |
| `IEditDraftRecordAccess.IsRecordVisible`, `XafSecurityEditDraftRecordAccess`, `EditDraftServices.RecordAccess` | XAF security (`XafSecurityEditDraftAccessCheck.MayRestore`) plus the optional `IEditDraftAccessCheck.MayRestore`; both through `EditDraftServices.MayRestore` |
| `IEditDraftRecordAccess.IsScopeVisible`, `IEditDraftRecreateHost.IsScopeVisible` | `IEditDraftAccessCheck.MayRecreate`, asked on the rebuilt record; both through `EditDraftServices.MayRecreate` |
| `IEditDraftRecreateCandidate.IsVisible` | `IEditDraftRecreateCandidate.MayRecreate` |
| `IEditDraftRecreateHost.CurrentOwner()` | `CurrentOwner(EditDraftTypePolicy)` |
| `EditDraftRecreate.Run(host, draftOid, proceedWhenSavedCheckFails)` | `Run(host, draftOid, objectType, proceedWhenSavedCheckFails)` |
| `EditDraftRecreateOutcome.SubSectionNotVisible` | none (the scope step is gone) |
| `EditDraftRecreateOutcome.FilledNotVisible` | `FilledNotPermitted` |
| `EditDraftTextSet.RecreateSubSectionNotVisible` | none; `RecreateNoPermission` is shown for both recreate refusals |

**An existing store table keeps its extra columns.** XPO never drops a column, so a table created by an earlier version
keeps `LoginIsStaffMember` and `SubSectionOid` (XPO created them nullable). The library no longer reads or writes them.
Without a mapping, XPO inserts NULL into them for new rows. To keep them mapped, with the same column names and types so
the table does not change, declare them on your own store class:

```csharp
public class EditDraft : EditDraftStoreBase
{
    public EditDraft(Session session) : base(session) { }

    private bool _loginIsStaffMember;
    [Persistent("LoginIsStaffMember")]
    public bool LoginIsStaffMember { get => _loginIsStaffMember; set => SetPropertyValue(nameof(LoginIsStaffMember), ref _loginIsStaffMember, value); }

    private Guid _subSectionOid;
    [Persistent("SubSectionOid")]
    public Guid SubSectionOid { get => _subSectionOid; set => SetPropertyValue(nameof(SubSectionOid), ref _subSectionOid, value); }
}
```

New rows then get XPO's default values (false, `Guid.Empty`) unless the host sets them, and a value the host writes is
never changed by the library's statements (checked in `SampleLegacyColumnsTests`).

Behaviour changes: restoring needs Write on the record (the 0.3 default only required that the record was found through
the secured object space); recreating needs Create, Write and Read on the rebuilt record with its values; the offer asks
the access check only when there is a draft to offer; the drafts list shows a record's text only when the login may
restore onto it; a "New" row whose type the login may not create stays listed with Open disabled; `RecordNotVisible`
reads "This draft cannot be restored for this login (no permission)." in English and ends in 「（権限がありません）」 in
Japanese; the log lines no longer name a host rule.
