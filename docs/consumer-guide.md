# Xaf.EditDraft consumer guide

How to add Xaf.EditDraft to a DevExpress XAF Blazor application, what the library checks at startup, and what stays the
application's responsibility. Version 0.3.0-preview.2 (the client-side input journal it adds is off by default and
not covered here; see the `edit-draft-client-journal-*` write-ups). The working example is `samples/Xaf.EditDraft.Sample` (one class,
`Note`); each step below names the sample file that does it.

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
| Owner | an XAF login whose key is a Guid (library default); another owner rule through `IEditDraftOwnerResolver` |
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

   The table has the 19 members of the base plus `Oid` and `OptimisticLockField`. Two members were renamed in
   0.2.0-preview.1 and keep their old column names, so an existing table needs no migration: `OwnerFlag` (column
   `LoginIsStaffMember`) and `ScopeOid` (column `SubSectionOid`).

3. **A policy per type** (`EditDraftTypePolicy`): the type, a `PolicyId`, the approved DetailView id(s), the ListView
   id(s), the owner kind, and one decision per member. A type without a policy is never captured; a member without a
   decision is never captured. Write the decisions with the library helpers; each takes the member, a reason and where the
   decision can be checked (both required by the decision gate `EditDraftDecisions.Check`):

   | Helper | Meaning | Policy list it must agree with | Label |
   |---|---|---|---|
   | `EditDraftDecisions.Restorable` | captured and put back as typed | — | A |
   | `EditDraftDecisions.Group` | moves with a driver | `Groups` | B |
   | `EditDraftDecisions.SideEffect` | its setter changes another record; selectable, never pre-ticked | `SideEffectMembers` | C |
   | `EditDraftDecisions.NotRestorable` | shown, never put back on an existing record | `NotRestorableOnExisting` | D |
   | `EditDraftDecisions.Excluded` | never captured | `Excluded` | X |

   The label is a record only; the gate does not read it. `ScopeOf` (optional) returns the record's access scope as a
   Guid (for example the Oid of its department); it is stored with the draft and asked of
   `IEditDraftRecordAccess.IsScopeVisible` before a never-saved record is recreated. Sample:
   `.../EditDrafts/NoteEditDraftPolicy.cs`.

4. **Service registrations** (Startup):

   ```csharp
   services.AddEditDraftStore<SampleEditDraft>();                  // table: from the store class's XPO mapping
   services.AddEditDraftRegistry(NoteEditDraftPolicy.Register);
   services.AddEditDraftBlazor();                                  // options: o => o.HeaderActionOnEveryView = true
   services.AddEditDraftRetention();                               // optional: the retention hosted service (section 6)
   ```

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
  another's. XAF role permissions do not apply to the engine.
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
| Owner of a draft | the XAF login's key when it is a non-empty Guid; no login, no draft | register an `IEditDraftOwnerResolver` |
| Record access | XAF security only (records are loaded through a secured object space first); `IsScopeVisible` allows every scope | register an `IEditDraftRecordAccess` |
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
