# Xaf.EditDraft sample consumer

A minimal DevExpress XAF Blazor application that uses the Xaf.EditDraft library (`Xaf.EditDraft.Core` and
`Xaf.EditDraft.Blazor`) to keep what a user typed into a record and did not save, and to offer it back later.
It has one business class, `Note`, and uses no code from CareCrew. It is built from the DevExpress 26.1.4 `dx.xaf`
project template (XPO, SQL Server, Blazor, password login) with the additions listed below. The library's consumer
guide is `docs/consumer-guide.md` at the repository root; this README shows how the sample follows it.

## Requirements

- .NET 8 SDK.
- DevExpress 26.1.4 NuGet packages (a DevExpress licence and the DevExpress NuGet feed).
- SQL Server LocalDB, instance `MSSQLLocalDB`, or another SQL Server (change `ConnectionStrings:ConnectionString` in
  `Xaf.EditDraft.Sample.Blazor.Server/appsettings.json`). The library supports SQL Server only (its writer uses T-SQL;
  the startup check stops the application otherwise). The sample's store table is `dbo.SampleEditDraft`, the default
  schema; another schema is set on the store class with `[Persistent("myschema.SampleEditDraft")]`.
- The two library projects, referenced by path: `../../Xaf.EditDraft.Core` and `../../Xaf.EditDraft.Blazor`.
  Package versions come from the repository's `Directory.Packages.props` (central package management).

## Projects

| Project | What it holds |
|---|---|
| `Xaf.EditDraft.Sample.Module` | `Note`, the draft store `SampleEditDraft`, the Note policy, the template's user classes, the database updater |
| `Xaf.EditDraft.Sample.Blazor.Server` | The XAF Blazor application: Startup, host page, model, configuration |
| `Xaf.EditDraft.Sample.Tests` | NUnit checks: references, policy, model views, default seams, switches, store deny |

## What a consumer supplies

In this order; the file in this sample is given for each step.

1. **A business class** keyed by a Guid: an XPO class deriving from DevExpress `BaseObject`, with properties written
   through `SetPropertyValue`. Sample: `Xaf.EditDraft.Sample.Module/BusinessObjects/Note.cs`.
2. **A store class**: one class deriving from `Xaf.EditDraft.Core.EditDraftStoreBase`. The library ships only the
   non-persistent base; your class is the persistent one and its name is the table name (`dbo.SampleEditDraft`
   here). Sample: `Xaf.EditDraft.Sample.Module/BusinessObjects/SampleEditDraft.cs`.
3. **A policy per type** (`EditDraftTypePolicy`): the type, a `PolicyId`, the approved DetailView id(s), the ListView
   id(s), and one decision per member (`EditDraftDecisions.Table(...)` with the library helpers such as
   `EditDraftDecisions.Restorable`). A type without a policy is never captured. A member without a decision is never
   captured. Sample: `Xaf.EditDraft.Sample.Module/EditDrafts/NoteEditDraftPolicy.cs`.
4. **Three service registrations**: `services.AddEditDraftStore<SampleEditDraft>()`,
   `services.AddEditDraftRegistry(NoteEditDraftPolicy.Register)` and `services.AddEditDraftBlazor()`. The sample also
   uses two optional ones: `AddEditDraftBlazor(o => o.HeaderActionOnEveryView = true)`, so the header action "Drafts"
   shows on every view and opens the list of every registered type off the Note list, and `AddEditDraftRetention()`, the
   retention hosted service (off until `EditDraftCapture:Retention:Enabled` is true; see "Security"). Sample:
   `Startup.AddEditDrafts` in `Xaf.EditDraft.Sample.Blazor.Server/Startup.cs`.
5. **Two modules** in the XAF module list: `.Add<EditDraftCoreModule>()` and `.Add<EditDraftBlazorModule>()`.
   Sample: `Startup.ConfigureServices`.
6. **The non-persistent object space provider**: `builder.ObjectSpaceProviders ... .AddNonPersistent()`. The library's
   restore popup and drafts list are non-persistent objects. The DevExpress template already has the line, XAF 26.1 adds
   the provider itself when none is registered, and `EditDraftBlazorModule` stops the application at setup with a message
   naming `.AddNonPersistent()` if it is still missing. Sample: `Startup.ConfigureServices`.
7. **The switches** in configuration: section `EditDraftCapture`, keys `Enabled`, `Types:<PolicyId>:Enabled`,
   `ListViews:Enabled` and `NewRecords:Enabled`. Only a value that reads as the boolean `true` is on; a missing,
   empty or other value is off. Sample: `Xaf.EditDraft.Sample.Blazor.Server/appsettings.json`.
8. **The row-badge stylesheet** in the host page:
   `<link href="_content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css" rel="stylesheet" />`. Sample:
   `Xaf.EditDraft.Sample.Blazor.Server/Pages/_Host.cshtml`.
9. **The store's security**: an explicit DENY of every operation on the store class for every role, written by the
   library helper `EditDraftSecurity.DenyStoreToAllRoles`. See "Security" below. Sample:
   `Updater.DenyDraftStoreToEveryRole` in `Xaf.EditDraft.Sample.Module/DatabaseUpdate/Updater.cs`.
10. Optional: the header action's paint style. The library creates the header action itself (id
   `EditDraftListBlazor`) with caption and image; this sample's model node sets the same. Sample:
   `Xaf.EditDraft.Sample.Blazor.Server/Model.xafml`.

At startup the library checks the registrations (store, policy, SQL Server, non-persistent provider) and stops with a
message naming the fix (consumer guide, "Startup checks"). Not in the sample, but needed in production: the retention
switch turned on (see "Security"), and, if you use the XAF Audit Trail module, excluding the store class from auditing.

## Library defaults this sample relies on

Nothing below is registered by the sample; each is the library default.

| Seam | Default | To change it |
|---|---|---|
| Owner of a draft | The XAF login's key, when it is a non-empty Guid. No login, no draft. | Register an `IEditDraftOwnerResolver` (asked with the policy) |
| Access | XAF security only: Write on a saved Note, Create, Write and Read on a recreated one (see "Try it", restricted user) | Register an `IEditDraftAccessCheck` (asked in addition; it can only narrow) |
| Switch section | `EditDraftCapture` | Register `new EditDraftSwitchOptions { Section = "..." }` |
| Store table schema | `dbo` | `[Persistent("myschema.SampleEditDraft")]` on the store class |
| Clock | `TimeProvider.System`, local time | Register a `TimeProvider` |
| Log | The application's `ILogger`, category `Xaf.EditDraft`, lines start with `[EditDraft]` | Set `EditDraftLog.Sink` at startup |
| Texts | English | `EditDraftTexts.Use(EditDraftLanguage.Japanese)` at startup |

## Run

From the repository root:

1. Build:
   `dotnet build samples/Xaf.EditDraft.Sample/Xaf.EditDraft.Sample.Blazor.Server/Xaf.EditDraft.Sample.Blazor.Server.csproj`
2. Create the database (`XafEditDraftSample` on LocalDB). Either start the application from Visual Studio with the
   debugger attached (the template then updates the database itself), or run once:
   `dotnet run --project samples/Xaf.EditDraft.Sample/Xaf.EditDraft.Sample.Blazor.Server -- --updateDatabase --forceUpdate --silent`
3. Start: `dotnet run --project samples/Xaf.EditDraft.Sample/Xaf.EditDraft.Sample.Blazor.Server`, then open
   `http://localhost:5006`.
4. Log in as `Admin`, `User` or `Restricted`, all with an empty password. These test users are created by the
   updater in Debug builds only. `User` has the `Default` role (Notes only); `Admin` is an administrator; `Restricted`
   has the `RestrictedNotes` role: it reads and creates Notes but may edit only Notes whose Priority is not High.

In this repository, builds and test runs by tools must use `--artifacts-path artifacts/claude-test/<run-id>` and a
dev host must use `artifacts/claude-devhost-<port>` (repository rule; Visual Studio stays open on the main tree).

## Try it

Captions are the English text set's.

- **New Note**: open Note, press New, type a Title, press Tab, then reload the page (F5) without saving. The Note list
  shows a notice that there are drafts of new records. Press **Drafts** in the top bar; the list shows the draft with
  state **New**. Select it and press **Open**: a new Note opens with what you typed, not yet saved.
- **Existing Note**: open a saved Note, change a field, close it without saving, then open the same Note again. A
  popup **Unsaved input was found** offers the typed values; press **Yes (put the selected fields back)**. The values
  are filled in, not saved.
- Saving a Note deletes its draft. A draft is kept for 7 days from the first capture and is then hidden.
- **Access is XAF security** (log in as `Restricted`): type into a Low Note and leave without saving; then, as `Admin`,
  set that Note's Priority to High and save. As `Restricted` again, opening the Note shows "This draft cannot be
  restored for this login (no permission)." instead of the offer, and the Drafts list shows its record as "(cannot be
  shown)". A new Note typed with Priority High and left unsaved cannot be recreated from the Drafts list: Open shows
  "You do not have permission to create this record." A Low one is recreated as usual.

## Security

- **How the library writes.** The engine works on the store table through a **non-secured** object space: it creates
  a draft with `CommitChanges`, reads drafts with queries, and updates or deletes them with T-SQL statements.
  **XAF role permissions do not apply to the engine.** What keeps one user's drafts from another is the owner
  condition (`OwnerUserOid` = the XAF login's Guid) that every read, update and delete carries.
- **Why deny the store anyway.** Without a deny, any way into the store class through ordinary XAF security — a
  generic list view such as `SampleEditDraft_ListView`, a lookup, an API — is governed by each role's permission
  policy. A role with `AllowAllByDefault` could then read and change every user's drafts, and the payload is the typed
  text in readable JSON. The updater therefore gives **every** role an explicit DENY of Read, Write, Create, Delete and
  Navigate on `SampleEditDraft`, on every database update, through `EditDraftSecurity.DenyStoreToAllRoles`. The deny does
  not affect capture or restore. The helper logs each role it could not bind, and at startup the library logs every role
  that can still read the store.
- **What the deny does not cover.** It is a type permission. In XAF, object and member permissions that allow access
  take priority over a type deny, and the helper leaves existing ones in place: do not add object or member
  permissions for the store class. The deny is written during a database update only, so a role created later (for
  example in the UI) has none until the next update; run the update again after creating roles, or add the deny when
  you create a role.
- **Administrators are not covered.** XAF cannot deny anything to a role with `IsAdministrative = true` ("You cannot
  deny any rights for a role with the Administrative Permission", XAF 26.1, Type, Object and Member Permissions). An
  administrator can open `SampleEditDraft_ListView` and read every user's drafts. The deny rows are still written for
  such a role so they apply if `IsAdministrative` is switched off. If administrators must not read drafts, do not give
  them an administrative role.
- **Retention.** A draft expires 7 days after its first capture and is then hidden, but its row stays until something
  deletes it. The sample registers the library's retention hosted service (`AddEditDraftRetention()`); it deletes the
  expired rows of every owner only while `EditDraftCapture:Retention:Enabled` is `true` (not set in `appsettings.json`,
  so off), every `EditDraftCapture:Retention:IntervalMinutes` minutes (default 60), and logs the count. `ExpiresOn` is
  written in the **application server's** local time and the library's sweep uses that clock. A SQL Agent job instead
  must use the same clock: `DELETE FROM dbo.SampleEditDraft WHERE ExpiresOn <= GETDATE();` is right only when SQL
  Server runs in the same time zone as the application server.
- **Audit trail.** This sample has no Audit Trail module. If you add one, exclude the store class so typed drafts are
  not copied into the audit log.

## Limits (library contract v1)

XPO only; records keyed by a Guid (`BaseObject`); SQL Server only (store table schema `dbo` unless set); the owner is
a login with a Guid key; registered types must have different class names (the payload stores `Type.Name`).

## Tests

`dotnet test samples/Xaf.EditDraft.Sample/Xaf.EditDraft.Sample.Tests/Xaf.EditDraft.Sample.Tests.csproj`

The tests check that the sample references the two libraries and no CareCrew assembly, that the Note policy admits
`Note_DetailView` and `Note_ListView` and XAF generates both views, that every other seam is the library default, the
switch values, the store deny, and that the sample uses the library's helpers. `SampleSqlServerTests` runs the library's
SQL Server parts (provider check, table check, the probe in a quoted schema, the retention sweep at the exact cutoff
across owners and with the application clock) against a throwaway LocalDB database it creates and drops; it is skipped
where LocalDB is not installed. `SampleXafNativeAccessTests` logs the sample's users on through XAF's own security
(`SecurityStrategyComplex`, a secured object space, an in-memory store) and checks that the `RestrictedNotes` role's
object criterion decides restore and recreate, that an administrator is unaffected, and that an extra
`IEditDraftAccessCheck` narrows but cannot widen; `SampleLegacyColumnsTests` (LocalDB) checks a store class that keeps
the two columns earlier versions declared. The tests do not run a browser; capture and restore are checked by hand (see
"Try it").
