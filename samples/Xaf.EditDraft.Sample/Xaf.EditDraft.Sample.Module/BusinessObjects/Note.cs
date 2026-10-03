using System.ComponentModel;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;

namespace Xaf.EditDraft.Sample.Module.BusinessObjects;

public enum NotePriority
{
    Low,
    Normal,
    High
}

/// <summary>
/// The sample's own business class. What a user types into a Note and has not saved yet is captured by the
/// Xaf.EditDraft library and offered back later (policy: EditDrafts/NoteEditDraftPolicy.cs).
///
/// What the library needs from a captured class (supported contract v1): an XPO class keyed by a Guid
/// (DevExpress BaseObject), plain properties written through SetPropertyValue. Every setter below is
/// SetPropertyValue only, which is why each member is decided "A" (restorable) in the policy.
/// </summary>
[DefaultClassOptions]
[DefaultProperty(nameof(Title))]
public class Note : BaseObject
{
    public Note(Session session) : base(session) { }

    private string _title;
    [Size(100)]
    public string Title { get => _title; set => SetPropertyValue(nameof(Title), ref _title, value); }

    private string _body;
    [Size(SizeAttribute.Unlimited)]
    public string Body { get => _body; set => SetPropertyValue(nameof(Body), ref _body, value); }

    private NotePriority _priority;
    public NotePriority Priority { get => _priority; set => SetPropertyValue(nameof(Priority), ref _priority, value); }

    private DateTime _dueOn;
    public DateTime DueOn { get => _dueOn; set => SetPropertyValue(nameof(DueOn), ref _dueOn, value); }
}
