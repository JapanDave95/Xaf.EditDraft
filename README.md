# Xaf.EditDraft

A data-restore feature for DevExpress XAF Blazor applications: while a user edits a record, the library keeps a
server-side copy of what they typed and did not save (a *draft*), and offers it back the next time that user opens the
record — after a browser refresh, a lost connection, a closed tab, or an application restart. Drafts belong to the
user who typed them, expire, and are never applied without the user's confirmation.

Status: extracted from the CareCrew application on 2026-10-03, where it runs behind configuration switches. This
repository is the source of truth for the library from that date; CareCrew consumes the package. Version
0.2.0-preview.1 closed the library gaps found by the sample consumer (see "Status of the gaps"). Version
0.3.0-preview.2 adds the client-side input journal, off by default and not yet connected to restore (see the
`docs/edit-draft-client-journal-*` write-ups). Version 0.4.0-preview.1 (breaking) leaves access to XAF security: who may
restore or recreate a record is decided by the application's roles and permissions, plus one optional host check
(`IEditDraftAccessCheck`); the scope and owner-kind concepts of the first host are removed (see
`docs/xaf-native-access-2026-10-05.md` and the consumer guide's upgrade section). Version 0.4.1-preview.1 is a cleanup
with no API change: the library source, texts and tests no longer name the first host's classes, files, screens or
projects, and the Japanese `PersonalLoginOnly` text is reworded (`docs/decouple-cleanup-2026-10-05.md`).

## Projects

| Project | Purpose |
|---|---|
| `Xaf.EditDraft.Core` | Platform-independent engine: capture on `ObjectSpace.ObjectChanged`, payload, writer (owner-fenced T-SQL), restorer, new-record capture, type policies, switches, seams, texts (English default, Japanese) |
| `Xaf.EditDraft.Blazor` | XAF Blazor UI: capture/offer/restore controllers, the drafts list and its header action, ListView row badges and 開く, the new-record recreate host, the label editor, the row-badge stylesheet (`_content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css`) |
| `Xaf.EditDraft.Tests` | NUnit tests of the library alone (no application code) |
| `samples/Xaf.EditDraft.Sample` | A minimal XAF Blazor application (one class, `Note`) built on the library with its default seams — the proof that the library works outside its first host. Its README lists everything a consumer supplies |

Target: .NET 8. DevExpress 26.1.4 (`Directory.Packages.props`) is the tested floor: the version the library is built
and tested with; older versions are not tested. XPO only. SQL Server only: the writer and the retention sweep use T-SQL,
and the startup check stops an application whose store is in another database. The store table's schema is `dbo`
unless the store class's XPO mapping names another (`[Persistent("myschema.MyEditDraft")]`).

## Installing the packages

The library is published as two NuGet packages, `Xaf.EditDraft.Core` and `Xaf.EditDraft.Blazor`, on this
repository's GitHub Packages feed. The repository is public, but GitHub's NuGet registry requires authentication even
for public packages: a consumer needs a GitHub personal access token (classic) with the `read:packages` scope.

1. Add the feed once, with your own GitHub user name and token (never commit the token; the user-level
   `NuGet.Config` is the usual place):

   ```
   dotnet nuget add source https://nuget.pkg.github.com/JapanDave95/index.json --name xaf-editdraft --username <github-user> --password <token> --store-password-in-clear-text
   ```

2. Reference the Blazor package from the XAF Blazor application project and the Core package from the module project
   (the Blazor package depends on Core at the same version):

   ```xml
   <PackageReference Include="Xaf.EditDraft.Blazor" Version="0.4.1-preview.1" />
   ```

3. The packages declare the DevExpress packages they need (26.1.4) as dependencies and do not contain them. DevExpress
   25.1+ packages are on nuget.org; building against them requires your own DevExpress licence key registered on the
   machine (`%AppData%\DevExpress\DevExpress_License.txt`, written by the DevExpress installer, or the
   `DevExpress_License` environment variable — see docs.devexpress.com/GeneralInformation/405494).

Current version: see `<Version>` in `Directory.Build.props`; release notes in `<PackageReleaseNotes>` there. Versions
before 1.0 are previews.

## Using it

Read [docs/consumer-guide.md](docs/consumer-guide.md): it lists what a consumer supplies, what the library checks at
startup, the security obligations and their limits, how access is decided (XAF security plus the optional
`IEditDraftAccessCheck`), retention, and the upgrade steps from earlier versions. The working example
is `samples/Xaf.EditDraft.Sample`. In short, a consumer supplies a persistent store class deriving from
`EditDraftStoreBase` (the consumer owns the class name — security deny rows key on it), one `EditDraftTypePolicy` per
captured type (which views, which members, how each member is restored; `EditDraftDecisions` helpers), three service
registrations (`AddEditDraftStore<TStore>()`, `AddEditDraftRegistry(...)`, `AddEditDraftBlazor()`), the two XAF modules
(`EditDraftCoreModule`, `EditDraftBlazorModule`), the non-persistent object space provider, the configuration section
`EditDraftCapture` (`Enabled`, `Types:<PolicyId>:Enabled`, `ListViews:Enabled`, `NewRecords:Enabled` — only the
literal `true` is on), the stylesheet link, and `EditDraftSecurity.DenyStoreToAllRoles` in its ModuleUpdater. A
retention sweep (`AddEditDraftRetention()` + `EditDraftCapture:Retention:Enabled`) is off unless turned on.

## Build and test

```
dotnet build Xaf.EditDraft.sln
dotnet test Xaf.EditDraft.Tests
dotnet test samples/Xaf.EditDraft.Sample/Xaf.EditDraft.Sample.Tests
```

No test in `Xaf.EditDraft.Tests` depends on a file of another repository. The tests that read this repository's files
find it by walking up to `.git`, so they are skipped only when run outside a git checkout (for example from a source
archive).

## Publishing a version

`dotnet pack Xaf.EditDraft.Core/Xaf.EditDraft.Core.csproj -c Release` and the same for `Xaf.EditDraft.Blazor`
produce the two packages (and symbol packages) locally. The
`.github/workflows/publish-package.yml` workflow builds, tests, packs and pushes them to GitHub Packages when a tag
`v<Version>` is pushed, where `<Version>` is the value in `Directory.Build.props`; the job refuses a tag that does
not match, and a tag is the only trigger (no manual run). Symbol packages are kept as a workflow artifact only,
because GitHub Packages does not accept them. It needs one repository secret, `DEVEXPRESS_LICENSE`: the contents of
the licence-holder's `DevExpress_License.txt` (DevExpress.com Download Manager → "Download License Key"), which the
job exposes as the `DevExpress_License` environment variable so the DevExpress build analyzers can license the build.
To release: bump `<Version>`, commit, `git tag v<Version>`, `git push --tags`.

## Design and history

`CHANGELOG.md` lists what changed in each version. `docs/` holds the consumer guide and the design and milestone write-ups in order: the original engine design
(2026-09-30), the library extraction design and its three milestones (2026-10-01/02), the new-record capture design and
result (2026-10-02/03), the sample consumer report (2026-10-03), whose last section lists the gaps G1-G15, the
report of their closing (2026-10-04), and the report of the move to XAF-native access (2026-10-05).

The git history of the four project folders is the history they had inside CareCrew (`git subtree split`).

## Status of the gaps

The gaps G1-G15 listed in `docs/xaf-editdraft-sample-consumer-2026-10-03.md` are closed in 0.2.0-preview.1
(`docs/close-gaps-2026-10-04.md`): startup checks for the prerequisites, a deny helper and a startup warning for roles
that can read the store, an opt-in retention sweep, a schema option, host-neutral names and English texts, decision
helpers, an all-types entry point for the drafts list, and the consumer guide. Still true by design: the writer does
not use XAF security and relies on the owner condition in every statement; a type deny cannot bind an administrative
role or object/member ALLOW grants (the startup warning names such roles); SQL Server and XPO only. Decided in
0.4.0-preview.1: the store base no longer declares the two columns the first host's tables have; a host that keeps them
declares them on its own store class.

## Licence

MIT (see `LICENSE`). The licence covers this library's own code. It depends on DevExpress components, which are not
included and require a DevExpress licence of your own.
