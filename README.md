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

Not yet decided; the repository is private. The library depends on DevExpress components, which require a DevExpress
licence.
