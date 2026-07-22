using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public static class PageLayerService
{
    public static PageLayer EnsureDefault(NotebookPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.Layers ??= [];
        var layer = page.Layers.FirstOrDefault(item => item.Id == page.ActiveLayerId) ?? page.Layers.FirstOrDefault();
        if (layer is null)
        {
            layer = new PageLayer { Name = "图层 1" };
            page.Layers.Add(layer);
        }
        page.ActiveLayerId = layer.Id;
        AssignUnlayeredContent(page, layer.Id);
        return layer;
    }

    public static PageLayer Add(NotebookPage page, string name)
    {
        ArgumentNullException.ThrowIfNull(page);
        var layer = new PageLayer { Name = string.IsNullOrWhiteSpace(name) ? "新图层" : name.Trim()[..Math.Min(name.Trim().Length, 40)] };
        page.Layers ??= [];
        page.Layers.Add(layer);
        page.ActiveLayerId = layer.Id;
        Touch(page);
        return layer;
    }

    public static bool SetActive(NotebookPage page, Guid layerId)
    {
        if (page.Layers.All(item => item.Id != layerId) || page.ActiveLayerId == layerId) return false;
        page.ActiveLayerId = layerId;
        Touch(page);
        return true;
    }

    public static bool SetVisibility(NotebookPage page, Guid layerId, bool visible)
    {
        var layer = page.Layers.FirstOrDefault(item => item.Id == layerId);
        if (layer is null || layer.IsVisible == visible) return false;
        layer.IsVisible = visible;
        Touch(page);
        return true;
    }

    public static bool SetLocked(NotebookPage page, Guid layerId, bool locked)
    {
        var layer = page.Layers.FirstOrDefault(item => item.Id == layerId);
        if (layer is null || layer.IsLocked == locked) return false;
        layer.IsLocked = locked;
        Touch(page);
        return true;
    }

    public static bool SetOpacity(NotebookPage page, Guid layerId, double opacity)
    {
        var layer = page.Layers.FirstOrDefault(item => item.Id == layerId);
        var normalized = double.IsFinite(opacity) ? Math.Clamp(opacity, .05, 1) : 1;
        if (layer is null || Math.Abs(layer.Opacity - normalized) < .001) return false;
        layer.Opacity = normalized;
        Touch(page);
        return true;
    }

    public static bool Rename(NotebookPage page, Guid layerId, string name)
    {
        var layer = page.Layers.FirstOrDefault(item => item.Id == layerId);
        var normalized = string.IsNullOrWhiteSpace(name) ? "图层" : name.Trim()[..Math.Min(name.Trim().Length, 40)];
        if (layer is null || string.Equals(layer.Name, normalized, StringComparison.Ordinal)) return false;
        layer.Name = normalized;
        Touch(page);
        return true;
    }

    public static bool MergeInto(NotebookPage page, Guid sourceId, Guid targetId)
    {
        if (sourceId == targetId) return false;
        var source = page.Layers.FirstOrDefault(item => item.Id == sourceId);
        var target = page.Layers.FirstOrDefault(item => item.Id == targetId);
        if (source is null || target is null || source.IsLocked || target.IsLocked) return false;
        foreach (var stroke in page.Ink.Strokes.Where(stroke => stroke.LayerId == sourceId)) stroke.LayerId = targetId;
        foreach (var item in page.Objects.Where(item => item.LayerId == sourceId)) item.LayerId = targetId;
        page.Layers.Remove(source);
        page.ActiveLayerId = targetId;
        Touch(page);
        return true;
    }

    public static bool Delete(NotebookPage page, Guid layerId, Guid? moveContentTo = null)
    {
        var layer = page.Layers.FirstOrDefault(item => item.Id == layerId);
        if (layer is null || page.Layers.Count <= 1 || layer.IsLocked) return false;
        var target = moveContentTo is Guid targetId
            ? page.Layers.FirstOrDefault(item => item.Id == targetId && item.Id != layerId)
            : page.Layers.FirstOrDefault(item => item.Id != layerId);
        if (target is null || target.IsLocked) return false;
        foreach (var stroke in page.Ink.Strokes.Where(stroke => stroke.LayerId == layerId)) stroke.LayerId = target.Id;
        foreach (var item in page.Objects.Where(item => item.LayerId == layerId)) item.LayerId = target.Id;
        page.Layers.Remove(layer);
        if (page.ActiveLayerId == layerId) page.ActiveLayerId = target.Id;
        Touch(page);
        return true;
    }

    public static bool IsContentVisible(NotebookPage? page, Guid? layerId)
    {
        if (page is null || layerId is null) return true;
        return page.Layers.FirstOrDefault(item => item.Id == layerId)?.IsVisible != false;
    }

    public static bool IsContentLocked(NotebookPage? page, Guid? layerId)
    {
        if (page is null || layerId is null) return false;
        return page.Layers.FirstOrDefault(item => item.Id == layerId)?.IsLocked == true;
    }

    public static double GetEffectiveOpacity(NotebookPage? page, Guid? layerId, double contentOpacity)
    {
        var layerOpacity = page?.Layers.FirstOrDefault(item => item.Id == layerId)?.Opacity ?? 1;
        return Math.Clamp(contentOpacity, 0, 1) * Math.Clamp(layerOpacity, 0, 1);
    }

    public static void AssignUnlayeredContent(NotebookPage page, Guid layerId)
    {
        foreach (var stroke in page.Ink.Strokes.Where(stroke => stroke.LayerId is null)) stroke.LayerId = layerId;
        foreach (var item in page.Objects.Where(item => item.LayerId is null)) item.LayerId = layerId;
    }

    private static void Touch(NotebookPage page) => page.ModifiedAt = DateTimeOffset.Now;
}
