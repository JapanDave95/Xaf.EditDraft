using System;
using System.Collections.Generic;
using System.Linq;

namespace Xaf.EditDraft.Core;

/// <summary>
/// Owner D16: the rows of SEVERAL live drafts of one record in ONE offer, newest first. Pure, so it is
/// tested without XAF. Rules:
///  - each draft gets a marker (①②③…) that prefixes its rows' 項目 when more than one draft is shown;
///  - a member the login may not write becomes 戻せません (not selectable, not ticked) in every draft;
///  - a group id is made draft-local (marker + id): one tick per group PER draft;
///  - a member drafted again in an OLDER draft is shown again, never pre-ticked, with
///    「（新しい入力控に同じ項目があります）」; if both rows are ticked the newer draft's value is kept
///    (EditDraftRestoreControllerBlazor.Apply skips the older one and counts it).
/// </summary>
public static class EditDraftOfferMerge
{
    /// <summary>「（新しい入力控に同じ項目があります）」 in the Japanese set (EditDraftTexts).</summary>
    public static string DuplicateNote => EditDraftTexts.Of(t => t.DuplicateNote);

    public sealed record Row(EditDraftRestoreRow Item, Guid DraftOid);

    public static string MarkerFor(int index) => index switch { 0 => "①", 1 => "②", 2 => "③", 3 => "④", 4 => "⑤", _ => $"({index + 1})" };

    /// <param name="drafts">Newest first: the draft's Oid and its plan rows (EditDraftRestorer.BuildItems output).</param>
    /// <param name="notWritable">Paths the login may not write (design §3 S3 check 3).</param>
    public static List<Row> Merge(IReadOnlyList<(Guid DraftOid, List<EditDraftRestoreRow> Items)> drafts, ISet<string> notWritable)
    {
        var result = new List<Row>();
        if (drafts == null) return result;
        var multi = drafts.Count > 1;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < drafts.Count; i++)
        {
            var (draftOid, items) = drafts[i];
            var marker = MarkerFor(i);
            foreach (var item in items ?? new List<EditDraftRestoreRow>())
            {
                if (notWritable != null && notWritable.Contains(item.Path))
                {
                    item.StatusCode = (int)EditDraftItemStatus.Unavailable;
                    item.StatusText = EditDraftComparison.StatusText(EditDraftItemStatus.Unavailable, false);
                    item.Selectable = false;
                    item.Selected = false;
                }
                if (item.Group != null) item.Group = marker + item.Group;
                if (multi) item.Label = marker + " " + item.Label;
                if (!seen.Add(item.Path))
                {
                    item.Selected = false;
                    if (item.Selectable) item.StatusText += DuplicateNote;
                }
                result.Add(new Row(item, draftOid));
            }
        }
        return result;
    }

    /// <summary>
    /// The chosen rows reduced to one entry per member path: rows are newest-draft first, so the first
    /// chosen row of a path wins; later duplicates are counted in <paramref name="skippedDuplicates"/>.
    /// </summary>
    public static List<Row> FirstPerPath(IEnumerable<Row> chosen, out int skippedDuplicates)
    {
        skippedDuplicates = 0;
        var result = new List<Row>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in chosen ?? Enumerable.Empty<Row>())
        {
            if (seen.Add(row.Item.Path)) result.Add(row);
            else skippedDuplicates++;
        }
        return result;
    }
}
