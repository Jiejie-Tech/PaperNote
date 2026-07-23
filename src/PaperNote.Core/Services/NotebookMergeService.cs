using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public sealed record NotebookMergeResult(int AddedPages, int AddedOutlineEntries, IReadOnlyList<Guid> PageIds);

/// <summary>Merges local notebooks while assigning fresh identities and remapping internal links.</summary>
public static class NotebookMergeService
{
    public static NotebookMergeResult MergeInto(NotebookDocument target, IEnumerable<NotebookDocument> sources, int? insertIndex = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sources);

        var addedIds = new List<Guid>();
        var outlineCount = 0;
        var index = Math.Clamp(insertIndex ?? target.Pages.Count, 0, target.Pages.Count);

        foreach (var source in sources.Where(item => item is not null))
        {
            var sourcePages = source.Pages.Where(page => page is not null).ToList();
            var pageMap = new Dictionary<Guid, Guid>();
            var clones = new List<NotebookPage>(sourcePages.Count);

            foreach (var sourcePage in sourcePages)
            {
                var clone = sourcePage.Clone();
                pageMap[sourcePage.Id] = clone.Id;
                RemapPageContentIdentities(sourcePage, clone);
                clones.Add(clone);
                addedIds.Add(clone.Id);
            }

            foreach (var clone in clones)
            {
                foreach (var item in clone.Objects)
                {
                    if (item.LinkTargetPageId is not Guid oldTarget) continue;
                    item.LinkTargetPageId = pageMap.TryGetValue(oldTarget, out var newTarget) ? newTarget : null;
                }

                foreach (var link in clone.PdfLinks)
                {
                    if (link.TargetPageId is not Guid oldTarget) continue;
                    link.TargetPageId = pageMap.TryGetValue(oldTarget, out var newTarget) ? newTarget : null;
                }
            }

            target.Pages.InsertRange(index, clones);
            index += clones.Count;

            foreach (var entry in source.OutlineEntries.Where(entry => entry is not null))
            {
                if (entry.TargetPageId is not Guid oldId || !pageMap.TryGetValue(oldId, out var newId)) continue;
                var clone = entry.Clone();
                clone.Id = Guid.NewGuid();
                clone.TargetPageId = newId;
                target.OutlineEntries.Add(clone);
                outlineCount++;
            }
        }

        if (addedIds.Count > 0)
        {
            target.CurrentPageId = addedIds[0];
            target.ModifiedAt = DateTimeOffset.Now;
            PageBatchService.CleanupNavigation(target);
        }

        return new NotebookMergeResult(addedIds.Count, outlineCount, addedIds);
    }

    private static void RemapPageContentIdentities(NotebookPage source, NotebookPage clone)
    {
        var layerMap = new Dictionary<Guid, Guid>();
        for (var index = 0; index < Math.Min(source.Layers.Count, clone.Layers.Count); index++)
        {
            var oldId = source.Layers[index].Id;
            var newId = Guid.NewGuid();
            clone.Layers[index].Id = newId;
            layerMap[oldId] = newId;
        }
        clone.ActiveLayerId = source.ActiveLayerId is Guid activeLayer && layerMap.TryGetValue(activeLayer, out var mappedActiveLayer)
            ? mappedActiveLayer
            : clone.Layers.FirstOrDefault()?.Id;

        var strokeMap = new Dictionary<Guid, Guid>();
        for (var index = 0; index < Math.Min(source.Ink.Strokes.Count, clone.Ink.Strokes.Count); index++)
        {
            var sourceStroke = source.Ink.Strokes[index];
            var cloneStroke = clone.Ink.Strokes[index];
            var newId = Guid.NewGuid();
            cloneStroke.Id = newId;
            strokeMap[sourceStroke.Id] = newId;
            cloneStroke.LayerId = sourceStroke.LayerId is Guid layerId && layerMap.TryGetValue(layerId, out var mappedLayer)
                ? mappedLayer
                : clone.ActiveLayerId;
        }

        var groupMap = new Dictionary<Guid, Guid>();
        for (var index = 0; index < Math.Min(source.Objects.Count, clone.Objects.Count); index++)
        {
            var sourceObject = source.Objects[index];
            var cloneObject = clone.Objects[index];
            cloneObject.Id = Guid.NewGuid();
            cloneObject.LayerId = sourceObject.LayerId is Guid layerId && layerMap.TryGetValue(layerId, out var mappedLayer)
                ? mappedLayer
                : clone.ActiveLayerId;
            if (sourceObject.GroupId is Guid groupId)
            {
                if (!groupMap.TryGetValue(groupId, out var mappedGroup))
                {
                    mappedGroup = Guid.NewGuid();
                    groupMap[groupId] = mappedGroup;
                }
                cloneObject.GroupId = mappedGroup;
            }
        }

        foreach (var block in clone.OcrBlocks) block.Id = Guid.NewGuid();
        foreach (var block in clone.PdfTextBlocks) block.Id = Guid.NewGuid();
        foreach (var link in clone.PdfLinks) link.Id = Guid.NewGuid();
        foreach (var comment in clone.Comments) comment.Id = Guid.NewGuid();

        for (var recordingIndex = 0; recordingIndex < Math.Min(source.AudioRecordings.Count, clone.AudioRecordings.Count); recordingIndex++)
        {
            var sourceRecording = source.AudioRecordings[recordingIndex];
            var cloneRecording = clone.AudioRecordings[recordingIndex];
            cloneRecording.Id = Guid.NewGuid();
            for (var cueIndex = 0; cueIndex < Math.Min(sourceRecording.Cues.Count, cloneRecording.Cues.Count); cueIndex++)
            {
                var sourceCue = sourceRecording.Cues[cueIndex];
                var cloneCue = cloneRecording.Cues[cueIndex];
                cloneCue.Id = Guid.NewGuid();
                cloneCue.StrokeId = sourceCue.StrokeId is Guid strokeId && strokeMap.TryGetValue(strokeId, out var mappedStroke)
                    ? mappedStroke
                    : null;
            }
        }
    }
}
