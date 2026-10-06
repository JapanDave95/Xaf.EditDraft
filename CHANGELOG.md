# Changelog

All versions are pre-releases. Packages are published to this repository's GitHub Packages feed when a tag `v<Version>`
is pushed (see README, "Publishing a version"). The package release notes (`<PackageReleaseNotes>` in
`Directory.Build.props`) carry the same content in one paragraph per version.

Each section is headed `## <version> — <yyyy-mm-dd> — <short label>`. The publish workflow refuses a tag whose version
has no section, and creates the GitHub Release from the section: title `<version> — <label>`, body the section text.
Changes not yet released collect under "Unreleased"; at release time that heading becomes the version's heading.

## Unreleased

- Reproducible builds: `Deterministic` is on, and `ContinuousIntegrationBuild` is on in GitHub Actions, so the same
  commit gives byte-identical DLLs on any machine and in any folder. To check a published package, rebuild its tag with
  `dotnet pack -c Release -p:ContinuousIntegrationBuild=true` and compare the DLLs inside the two packages (the .nupkg
  files themselves differ only in their zip entry timestamps).
- The publish workflow creates the GitHub Release from this file after a successful publish, attaches the `.nupkg`
  files, and refuses a tag whose version has no section here. Its scripts read the tag and repository from environment
  variables rather than interpolated expressions.

## 0.4.1-preview.1 — 2026-10-05 — no first-host traces

Removes all first-host traces from the library source and tests (`docs/decouple-cleanup-2026-10-05.md`).

- The only behaviour change: the Japanese text `PersonalLoginOnly`, shown when a login cannot use drafts, now reads
  「このログインでは入力控を使えません。」, matching the English.
- No API change. Provenance comments rewritten in library terms; test fixtures neutralized; the guard scan (NA8) extended.
- The test that compared against the first host's files now checks this repository's own project and solution, so it
  runs instead of skipping.

## 0.4.0-preview.1 — 2026-10-05 — XAF-native access (breaking)

Access to drafts is decided by XAF security plus one optional host check; the library no longer knows a host's scope or
owner kind (`docs/xaf-native-access-2026-10-05.md`).

**Access rules**

- Restoring onto a saved record: the record must load through the application's secured object space and Write must be
  granted on it.
- Recreating a record from a new-record draft: Create, Write and Read must be granted on the rebuilt object with its own
  values, as XAF checks a new object before it saves it.
- Applications without request security: allowed, as XAF allows.

**New**

- `IEditDraftAccessCheck` (`MayRestore`, `MayRecreate`), asked in addition to the XAF check, so a host can only narrow
  access.
- `XafSecurityEditDraftAccessCheck`; `EditDraftServices.AccessCheck`, `MayRestore`, `MayRecreate`; recreate outcome
  `FilledNotPermitted`.

**Removed**

- `EditDraftTypePolicy.ScopeOf` and `OwnerKind`; `EditDraftOwnerKind`; `EditDraftOwnerRule`.
- `EditDraftStoreBase.ScopeOid` and `OwnerFlag` (a host that keeps those columns declares them on its own store class;
  the library no longer reads or writes them).
- The scope members of `EditDraftSeed`, `EditDraftOwnerInfo` and `EditDraftRecreateDraft`.
- `IEditDraftRecordAccess`, `XafSecurityEditDraftRecordAccess`, `EditDraftServices.RecordAccess`;
  `IEditDraftRecreateHost.IsScopeVisible`; `IEditDraftRecreateCandidate.IsVisible`; recreate outcomes
  `SubSectionNotVisible` and `FilledNotVisible`; text `RecreateSubSectionNotVisible`.

**Changed**

- `IEditDraftOwnerResolver.Current` receives the policy of the draft's type (null when the type is not registered);
  `EditDraftRecreate.Run` and `IEditDraftRecreateHost.CurrentOwner` take the type.
- The drafts list lists a row only under the owner the owner seam names for its type.
- A new-record row whose type the login may not create stays in the list with Open disabled.
- `RecordNotVisible` reads "no permission" in both text sets.

Upgrade steps: `docs/consumer-guide.md`, "Upgrading from 0.3.0-preview.x".

## 0.3.0-preview.2 — 2026-10-05 — client-side input journal (off by default)

Adds the client-side input journal, off by default (`docs/edit-draft-client-journal-design-2026-10-03.md` and the
M1/M1b write-ups). What a user is typing in a focused editor is kept in the browser, so it survives a refresh, a lost
connection or a crash.

- An ES module served as a static web asset (`_content/Xaf.EditDraft.Blazor/edit-draft-journal.js`); it journals only
  editors carrying a server-built `data-editdraft` descriptor, one localStorage key per entry and page load.
- `EditDraftJournalAttributeControllerBlazor` adds the descriptor to captured DetailView editors.
- Switch `EditDraftCapture:Journal:Enabled`, fail closed, together with the type's own switch.
- `EditDraftTypePolicy.JournalTimeOfDayMembers`; `EditDraftTexts.JournalRowLabel` (ja/en).
- Budgets per page load: 60 entries, 12,000 characters, 60 minutes.
- Clears act only on the page load's own entries; another page load's entries are removed only when its Web Lock is
  gone; logoff sweeps the namespace.

Not yet included: the server intake that turns journal entries into restorable drafts, and masked-editor reply
matching. Enabling the switch journals typing but does not restore it yet.

## 0.3.0-preview.1 — 2026-10-05 — not published

No package was published for this tag: the release run stopped at a test that pinned the previous version number. That
test now follows the version instead. The same content shipped as 0.3.0-preview.2.

## 0.2.0-preview.1 — 2026-10-04 — the library gaps closed

Closes the 15 gaps found by running the library from a second, independent application
(`docs/close-gaps-2026-10-04.md`).

**New**

- Startup checks that stop the application with a message naming the fix (store registered, a policy while capture is
  on, SQL Server, the store table named the same by XPO and the library, the non-persistent object space provider),
  remembered per store class and database only after they pass.
- An optional table check that warns at setup and stops the XAF database update when the table is still missing after
  the schema update.
- `EditDraftSecurity.DenyStoreToAllRoles` for the module updater, and a best-effort startup warning for each role that
  may read the store.
- Opt-in retention sweep: `EditDraftRetention.Sweep`, `services.AddEditDraftRetention()`, key
  `EditDraftCapture:Retention:Enabled`; uses the application clock; interval at most 1,440 minutes; 0 or below turns the
  hosted sweep off.
- The store table's schema and name come only from the store class's XPO mapping (`[Persistent("schema.table")]`,
  default `dbo`), with quoted identifiers.
- `EditDraftDecisions` helpers; `EditDraftBlazorOptions.HeaderActionOnEveryView`; `docs/consumer-guide.md`.

**Changed**

- Renames: `LoginIsStaffMember` → `OwnerFlag`, `SubSectionOid` → `ScopeOid`, `SubSectionOf` → `ScopeOf`,
  `IsSubSectionVisible` → `IsScopeVisible`, `EditDraftOwnerKind.F2StaffMember` → `HostDefined`,
  `EditDraftCaptureControllerBlazor` → `EditDraftCaptureController` (database columns unchanged; no migration).
- English texts are host-neutral and no longer promise deletion at expiry; the header action shows caption and image;
  a row icon never re-enables one that XAF disabled; an absent store table is re-checked after 30 seconds; Core grants
  `InternalsVisibleTo` only to `Xaf.EditDraft.Blazor` and `Xaf.EditDraft.Tests`.

Unchanged: SQL Server only, XPO, Guid keys, DevExpress 26.1.4 as the tested floor, the Japanese text set.

## 0.1.0-preview.1 — 2026-10-04 — first preview

First package release: draft capture and restore for DevExpress XAF Blazor applications on XPO, extracted on 2026-10-03
from the application where it was built and is in use behind configuration switches. Includes server-side capture of
unsaved edits, the restore offer, the drafts list, ListView row badges and new-record recreation. The known gaps at
this version are closed in 0.2.0-preview.1.
