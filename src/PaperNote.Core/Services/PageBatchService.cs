using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public static class PageBatchService
{
    public static bool Move(NotebookDocument document, IReadOnlyCollection<Guid> pageIds, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        var selected = document.Pages.Where(page => pageIds.Contains(page.Id)).ToList();
        if (selected.Count == 0) return false;
        document.Pages.RemoveAll(page => pageIds.Contains(page.Id));
        targetIndex = Math.Clamp(targetIndex, 0, document.Pages.Count);
        document.Pages.InsertRange(targetIndex, selected);
        document.ModifiedAt = DateTimeOffset.Now;
        return true;
    }

    public static IReadOnlyList<NotebookPage> Extract(NotebookDocument document, IReadOnlyCollection<Guid> pageIds)
        => document.Pages.Where(page => pageIds.Contains(page.Id)).Select(page => page.Clone(preserveIdentity: false)).ToArray();

    public static bool RotateBackground(NotebookPage page, int quarterTurns)
    {
        var normalized = ((quarterTurns % 4) + 4) % 4;
        if (normalized == 0) return false;
        page.BackgroundRotation = (page.BackgroundRotation + normalized * 90) % 360;
        page.ModifiedAt = DateTimeOffset.Now;
        return true;
    }

    public static bool SetBookmark(NotebookPage page, bool value)
    {
        if (page.IsBookmarked == value) return false;
        page.IsBookmarked = value; page.ModifiedAt = DateTimeOffset.Now; return true;
    }
}
