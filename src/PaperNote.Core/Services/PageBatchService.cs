using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public static class PageBatchService
{
    public static bool Move(NotebookDocument document, IReadOnlyCollection<Guid> pageIds, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pageIds);
        var selectedIds = pageIds.ToHashSet();
        var selected = document.Pages.Where(page => selectedIds.Contains(page.Id)).ToList();
        if (selected.Count == 0) return false;

        var originalOrder = document.Pages.Select(page => page.Id).ToArray();
        var removedBeforeTarget = document.Pages.Take(Math.Clamp(targetIndex, 0, document.Pages.Count)).Count(page => selectedIds.Contains(page.Id));
        document.Pages.RemoveAll(page => selectedIds.Contains(page.Id));
        targetIndex = Math.Clamp(targetIndex - removedBeforeTarget, 0, document.Pages.Count);
        document.Pages.InsertRange(targetIndex, selected);

        var changed = !document.Pages.Select(page => page.Id).SequenceEqual(originalOrder);
        if (changed) document.ModifiedAt = DateTimeOffset.Now;
        return changed;
    }

    public static bool MoveRelative(NotebookDocument document, IReadOnlyCollection<Guid> pageIds, int offset)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (offset == 0) return false;
        var selectedIds = pageIds.ToHashSet();
        var selectedIndices = document.Pages.Select((page, index) => (page, index)).Where(item => selectedIds.Contains(item.page.Id)).Select(item => item.index).ToArray();
        if (selectedIndices.Length == 0) return false;
        var target = offset < 0 ? Math.Max(0, selectedIndices[0] + offset) : Math.Min(document.Pages.Count, selectedIndices[^1] + 1 + offset);
        return Move(document, selectedIds, target);
    }

    public static bool MoveToStart(NotebookDocument document, IReadOnlyCollection<Guid> pageIds) => Move(document, pageIds, 0);
    public static bool MoveToEnd(NotebookDocument document, IReadOnlyCollection<Guid> pageIds) => Move(document, pageIds, document.Pages.Count);

    public static IReadOnlyList<NotebookPage> Duplicate(NotebookDocument document, IReadOnlyCollection<Guid> pageIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        var selectedIds = pageIds.ToHashSet();
        var selected = document.Pages.Where(page => selectedIds.Contains(page.Id)).ToArray();
        if (selected.Length == 0) return [];

        var idMap = selected.ToDictionary(page => page.Id, _ => Guid.NewGuid());
        var copies = selected.Select(page => CloneWithRemappedLinks(page, idMap, keepExternalLinks: true)).ToArray();
        for (var index = selected.Length - 1; index >= 0; index--)
        {
            var sourceIndex = document.Pages.FindIndex(page => page.Id == selected[index].Id);
            document.Pages.Insert(sourceIndex + 1, copies[index]);
        }
        document.ModifiedAt = DateTimeOffset.Now;
        return copies;
    }

    public static bool Delete(NotebookDocument document, IReadOnlyCollection<Guid> pageIds, bool keepAtLeastOnePage = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        var ids = pageIds.ToHashSet();
        var existing = document.Pages.Where(page => ids.Contains(page.Id)).Select(page => page.Id).ToHashSet();
        if (existing.Count == 0 || keepAtLeastOnePage && existing.Count >= document.Pages.Count) return false;

        var oldCurrentIndex = document.Pages.FindIndex(page => page.Id == document.CurrentPageId);
        document.Pages.RemoveAll(page => existing.Contains(page.Id));
        CleanupNavigation(document);
        if (document.Pages.Count > 0 && !document.Pages.Any(page => page.Id == document.CurrentPageId))
            document.CurrentPageId = document.Pages[Math.Clamp(oldCurrentIndex, 0, document.Pages.Count - 1)].Id;
        document.ModifiedAt = DateTimeOffset.Now;
        return true;
    }

    public static IReadOnlyList<NotebookPage> Extract(NotebookDocument document, IReadOnlyCollection<Guid> pageIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        var ids = pageIds.ToHashSet();
        var selected = document.Pages.Where(page => ids.Contains(page.Id)).ToArray();
        var idMap = selected.ToDictionary(page => page.Id, _ => Guid.NewGuid());
        return selected.Select(page => CloneWithRemappedLinks(page, idMap, keepExternalLinks: false)).ToArray();
    }

    public static NotebookDocument ExtractDocument(NotebookDocument document, IReadOnlyCollection<Guid> pageIds, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var selectedIds = pageIds.ToHashSet();
        var selected = document.Pages.Where(page => selectedIds.Contains(page.Id)).ToArray();
        if (selected.Length == 0) throw new ArgumentException("至少选择一页。", nameof(pageIds));

        var idMap = selected.ToDictionary(page => page.Id, _ => Guid.NewGuid());
        var pages = selected.Select(page => CloneWithRemappedLinks(page, idMap, keepExternalLinks: false)).ToList();
        var extracted = new NotebookDocument
        {
            FormatVersion = Math.Max(16, document.FormatVersion),
            Title = string.IsNullOrWhiteSpace(title) ? $"{document.Title} - 提取页面" : title.Trim(),
            FolderName = document.FolderName,
            CoverStyle = document.CoverStyle,
            Tags = document.Tags.ToList(),
            CurrentPageId = pages[0].Id,
            Pages = pages,
            PaperPresets = document.PaperPresets.Select(preset => new PaperPreset { Id = preset.Id, PaperTemplate = preset.PaperTemplate, PaperColor = preset.PaperColor }).ToList(),
            OutlineEntries = document.OutlineEntries
                .Where(entry => entry.TargetPageId.HasValue && idMap.ContainsKey(entry.TargetPageId.Value))
                .Select(entry =>
                {
                    var copy = entry.Clone();
                    copy.Id = Guid.NewGuid();
                    copy.TargetPageId = idMap[entry.TargetPageId!.Value];
                    return copy;
                }).ToList()
        };
        CleanupNavigation(extracted);
        return extracted;
    }

    public static void CleanupNavigation(NotebookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var valid = document.Pages.Select(page => page.Id).ToHashSet();
        document.OutlineEntries.RemoveAll(entry => !entry.TargetPageId.HasValue || !valid.Contains(entry.TargetPageId.Value));
        foreach (var page in document.Pages)
        {
            foreach (var item in page.Objects.Where(item => item.LinkTargetPageId.HasValue && !valid.Contains(item.LinkTargetPageId.Value)))
                item.LinkTargetPageId = null;
            foreach (var link in page.PdfLinks.Where(link => link.TargetPageId.HasValue && !valid.Contains(link.TargetPageId.Value)))
                link.TargetPageId = null;
        }
        PdfDocumentContentService.ResolveInternalLinks(document);
    }

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
        page.IsBookmarked = value;
        page.ModifiedAt = DateTimeOffset.Now;
        return true;
    }

    private static NotebookPage CloneWithRemappedLinks(NotebookPage source, IReadOnlyDictionary<Guid, Guid> idMap, bool keepExternalLinks)
    {
        var copy = source.Clone();
        copy.Id = idMap[source.Id];
        copy.CreatedAt = DateTimeOffset.Now;
        copy.ModifiedAt = copy.CreatedAt;
        foreach (var item in copy.Objects.Where(item => item.LinkTargetPageId.HasValue))
            item.LinkTargetPageId = idMap.TryGetValue(item.LinkTargetPageId!.Value, out var target) ? target : keepExternalLinks ? item.LinkTargetPageId : null;
        foreach (var link in copy.PdfLinks.Where(link => link.TargetPageId.HasValue))
            link.TargetPageId = idMap.TryGetValue(link.TargetPageId!.Value, out var target) ? target : keepExternalLinks ? link.TargetPageId : null;
        return copy;
    }
}
