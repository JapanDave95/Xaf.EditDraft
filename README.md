# Xaf.EditDraft

A data-restore feature for DevExpress XAF Blazor applications: while a user edits a record, the library keeps a
server-side copy of what they typed and did not save (a *draft*), and offers it back the next time that user opens the
record — after a browser refresh, a lost connection, a closed tab, or an application restart. Drafts belong to the
user who typed them, expire, and are never applied without the user's confirmation.

Status: extracted from the CareCrew application on 2026-10-03, where it runs behind configuration switches. This
repository is the source of truth for the library from that date; CareCrew keeps an in-solution copy until the
library ships as a NuGet package. See "Known gaps" before using it in another application.

## Projects

| Project | Purpose |
|---|---|
| `Xaf.EditDraft.Core` | Platform-independent engine: capture on `ObjectSpace.ObjectChanged`, payload, writer (owner-fenced T-SQL), restorer, new-record capture, type policies, switches, seams, texts (English default, Japanese) |
| `Xaf.EditDraft.Blazor` | XAF Blazor UI: capture/offer/restore controllers, the drafts list and its header action, ListView row badges and 開く, the new-record recreate host, the label editor, the row-badge stylesheet (`_content/Xaf.EditDraft.Blazor/edit-draft-row-badge.css`) |
| `Xaf.EditDraft.Tests` | NUnit tests of the library alone (no application code) |
| `samples/Xaf.EditDraft.Sample` | A minimal XAF Blazor application (one class, `Note`) built on the library with its default seams — the proof that the library works outside its first host. Its README lists everything a consumer supplies |

Target: .NET 8, DevExpress 26.1.4 (`Directory.Packages.props`). SQL Server only: the writer uses T-SQL and expects
the store table in `dbo`.

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
   <PackageReference Include="Xaf.EditDraft.Blazor" Version="0.1.0-preview.1" />
   ```

3. The packages declare the DevExpress packages they need (26.1.4) as dependencies and do not contain them. DevExpress
   25.1+ packages are on nuget.org; building against them requires your own DevExpress licence key registered on the
   machine (`%AppData%\DevExpress\DevExpress_License.txt`, written by the DevExpress installer, or the
   `DevExpress_License` environment variable — see docs.devexpress.com/GeneralInformation/405494).

Current version: see `<Version>` in `Directory.Build.props`. Versions before 1.0 are previews; see "Known gaps".

## Using it

Read `samples/Xaf.EditDraft.Sample/README.md` first; it is the consumer guide. In short, a consumer supplies a
persistent store class deriving from `EditDraftStoreBase` (the consumer owns the class name — security deny rows key
on it), one `EditDraftTypePolicy` per captured type (which views, which members, how each member is restored),
three service registrations (`AddEditDraftStore<TStore>()`, `AddEditDraftRegistry(...)`, `AddEditDraftBlazor()`),
the two XAF modules (`EditDraftCoreModule`, `EditDraftBlazorModule`), the non-persistent object space provider, the
configuration section `EditDraftCapture` (`Enabled`, `Types:<PolicyId>:Enabled`, `ListViews:Enabled`,
`NewRecords:Enabled` — only the literal `true` is on), the stylesheet link, and an explicit DENY of the store class
to every role.

## Build and test

```
dotnet build Xaf.EditDraft.sln
dotnet test Xaf.EditDraft.Tests
dotnet test samples/Xaf.EditDraft.Sample/Xaf.EditDraft.Sample.Tests
```

Some tests in `Xaf.EditDraft.Tests` compare the library against files of its first host application and are
skipped (`Assert.Ignore`) when those files are not in the repository.

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

`docs/` holds the design and milestone write-ups in order: the original engine design (2026-09-30), the library
extraction design and its three milestones (2026-10-01/02), the new-record capture design and result (2026-10-02/03),
and the sample consumer report (2026-10-03), whose last section lists the known gaps.

The git history of the four project folders is the history they had inside CareCrew (`git subtree split`).

## Known gaps

From `docs/xaf-editdraft-sample-consumer-2026-10-03.md`: the `AddNonPersistent()` prerequisite is not enforced; the
security obligation (deny the store class to every role) is documented, not checked; the writer bypasses XAF
security by design and relies on the owner fence; the restore popup's English texts still carry one host-specific
term; there is no retention sweep in the library; no NuGet package yet. The DevExpress floor is 26.1.4 as built, not
yet confirmed lower.

## Licence

MIT (see `LICENSE`). The licence covers this library's own code. It depends on DevExpress components, which are not
included and require a DevExpress licence of your own.
