using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public readonly record struct PageObjectBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

/// <summary>
/// Platform-neutral page object editing commands shared by desktop and mobile clients.
/// All operations preserve locked objects, clamp geometry to the page, and keep group membership coherent.
/// </summary>
public static class PageObjectEditingService
{
    public const double DefaultPageWidth = 840;
    public const double DefaultPageHeight = 1188;
    public const double MinimumObjectSize = 30;

    public static PageObject? HitTest(NotebookPage page, double x, double y, double tolerance = 8)
    {
        ArgumentNullException.ThrowIfNull(page);
        for (var index = page.Objects.Count - 1; index >= 0; index--)
        {
            var item = page.Objects[index];
            if (item.IsHidden || !PageLayerService.IsContentVisible(page, item.LayerId)) continue;
            var radians = -item.Rotation * Math.PI / 180d;
            var centerX = item.X + item.Width / 2;
            var centerY = item.Y + item.Height / 2;
            var dx = x - centerX;
            var dy = y - centerY;
            var localX = centerX + dx * Math.Cos(radians) - dy * Math.Sin(radians);
            var localY = centerY + dx * Math.Sin(radians) + dy * Math.Cos(radians);
            if (localX >= item.X - tolerance && localX <= item.X + item.Width + tolerance &&
                localY >= item.Y - tolerance && localY <= item.Y + item.Height + tolerance)
                return item;
        }
        return null;
    }

    public static IReadOnlyList<Guid> ExpandSelection(NotebookPage page, IEnumerable<Guid> objectIds)
    {
        ArgumentNullException.ThrowIfNull(page);
        var requested = objectIds.ToHashSet();
        var groupIds = page.Objects
            .Where(item => requested.Contains(item.Id) && item.GroupId.HasValue)
            .Select(item => item.GroupId!.Value)
            .ToHashSet();
        return page.Objects
            .Where(item => requested.Contains(item.Id) || item.GroupId is Guid groupId && groupIds.Contains(groupId))
            .Select(item => item.Id)
            .ToArray();
    }

    public static PageObjectBounds? GetBounds(NotebookPage page, IEnumerable<Guid> objectIds)
    {
        var selected = Selected(page, objectIds, includeLocked: true).ToArray();
        if (selected.Length == 0) return null;
        var left = selected.Min(item => item.X);
        var top = selected.Min(item => item.Y);
        var right = selected.Max(item => item.X + item.Width);
        var bottom = selected.Max(item => item.Y + item.Height);
        return new PageObjectBounds(left, top, right - left, bottom - top);
    }

    public static bool Move(NotebookPage page, IEnumerable<Guid> objectIds, double deltaX, double deltaY,
        double pageWidth = DefaultPageWidth, double pageHeight = DefaultPageHeight)
    {
        var selected = Selected(page, ExpandSelection(page, objectIds), includeLocked: false).ToArray();
        if (selected.Length == 0 || (!double.IsFinite(deltaX) || !double.IsFinite(deltaY))) return false;
        var bounds = Bounds(selected);
        deltaX = Math.Clamp(deltaX, -bounds.X, pageWidth - bounds.Right);
        deltaY = Math.Clamp(deltaY, -bounds.Y, pageHeight - bounds.Bottom);
        if (Math.Abs(deltaX) < .001 && Math.Abs(deltaY) < .001) return false;
        foreach (var item in selected) { item.X += deltaX; item.Y += deltaY; }
        Touch(page);
        return true;
    }

    public static bool Resize(NotebookPage page, IEnumerable<Guid> objectIds, double targetWidth, double targetHeight,
        double pageWidth = DefaultPageWidth, double pageHeight = DefaultPageHeight)
    {
        var selected = Selected(page, ExpandSelection(page, objectIds), includeLocked: false).ToArray();
        if (selected.Length == 0 || !double.IsFinite(targetWidth) || !double.IsFinite(targetHeight)) return false;
        var bounds = Bounds(selected);
        targetWidth = Math.Clamp(targetWidth, MinimumObjectSize, pageWidth - bounds.X);
        targetHeight = Math.Clamp(targetHeight, MinimumObjectSize, pageHeight - bounds.Y);
        var scaleX = targetWidth / Math.Max(bounds.Width, .001);
        var scaleY = targetHeight / Math.Max(bounds.Height, .001);
        if (Math.Abs(scaleX - 1) < .001 && Math.Abs(scaleY - 1) < .001) return false;
        foreach (var item in selected)
        {
            item.X = bounds.X + (item.X - bounds.X) * scaleX;
            item.Y = bounds.Y + (item.Y - bounds.Y) * scaleY;
            item.Width = Math.Max(MinimumObjectSize, item.Width * scaleX);
            item.Height = Math.Max(MinimumObjectSize, item.Height * scaleY);
        }
        Touch(page);
        return true;
    }

    public static bool Rotate(NotebookPage page, IEnumerable<Guid> objectIds, double deltaDegrees)
    {
        var selected = Selected(page, ExpandSelection(page, objectIds), includeLocked: false).ToArray();
        if (selected.Length == 0 || !double.IsFinite(deltaDegrees) || Math.Abs(deltaDegrees) < .001) return false;
        var bounds = Bounds(selected);
        var radians = deltaDegrees * Math.PI / 180d;
        foreach (var item in selected)
        {
            var itemCenterX = item.X + item.Width / 2;
            var itemCenterY = item.Y + item.Height / 2;
            var dx = itemCenterX - bounds.CenterX;
            var dy = itemCenterY - bounds.CenterY;
            var rotatedX = bounds.CenterX + dx * Math.Cos(radians) - dy * Math.Sin(radians);
            var rotatedY = bounds.CenterY + dx * Math.Sin(radians) + dy * Math.Cos(radians);
            item.X = rotatedX - item.Width / 2;
            item.Y = rotatedY - item.Height / 2;
            item.Rotation = NormalizeRotation(item.Rotation + deltaDegrees);
        }
        Touch(page);
        return true;
    }

    public static IReadOnlyList<Guid> Duplicate(NotebookPage page, IEnumerable<Guid> objectIds, double offset = 24,
        double pageWidth = DefaultPageWidth, double pageHeight = DefaultPageHeight)
    {
        var selected = Selected(page, ExpandSelection(page, objectIds), includeLocked: true).ToArray();
        if (selected.Length == 0) return [];
        var groupMap = selected.Where(item => item.GroupId.HasValue).Select(item => item.GroupId!.Value).Distinct().ToDictionary(id => id, _ => Guid.NewGuid());
        var result = new List<Guid>(selected.Length);
        foreach (var source in selected)
        {
            var clone = source.Clone();
            clone.X = Math.Clamp(source.X + offset, 0, Math.Max(0, pageWidth - clone.Width));
            clone.Y = Math.Clamp(source.Y + offset, 0, Math.Max(0, pageHeight - clone.Height));
            clone.IsLocked = false;
            clone.GroupId = source.GroupId is Guid groupId ? groupMap[groupId] : null;
            page.Objects.Add(clone);
            result.Add(clone.Id);
        }
        Touch(page);
        return result;
    }

    public static bool Delete(NotebookPage page, IEnumerable<Guid> objectIds)
    {
        var ids = ExpandSelection(page, objectIds).ToHashSet();
        var removed = page.Objects.RemoveAll(item => ids.Contains(item.Id) && !item.IsLocked);
        if (removed == 0) return false;
        Touch(page);
        return true;
    }

    public static bool BringToFront(NotebookPage page, IEnumerable<Guid> objectIds)
        => Reorder(page, objectIds, bringToFront: true);

    public static bool SendToBack(NotebookPage page, IEnumerable<Guid> objectIds)
        => Reorder(page, objectIds, bringToFront: false);

    public static bool SetLocked(NotebookPage page, IEnumerable<Guid> objectIds, bool locked)
    {
        var selected = Selected(page, ExpandSelection(page, objectIds), includeLocked: true).ToArray();
        if (selected.Length == 0 || selected.All(item => item.IsLocked == locked)) return false;
        foreach (var item in selected) item.IsLocked = locked;
        Touch(page);
        return true;
    }

    public static Guid? Group(NotebookPage page, IEnumerable<Guid> objectIds)
    {
        var selected = Selected(page, objectIds, includeLocked: false).DistinctBy(item => item.Id).ToArray();
        if (selected.Length < 2) return null;
        var groupId = Guid.NewGuid();
        foreach (var item in selected) item.GroupId = groupId;
        Touch(page);
        return groupId;
    }

    public static bool Ungroup(NotebookPage page, IEnumerable<Guid> objectIds)
    {
        var groups = Selected(page, objectIds, includeLocked: false).Where(item => item.GroupId.HasValue).Select(item => item.GroupId!.Value).ToHashSet();
        if (groups.Count == 0) return false;
        foreach (var item in page.Objects.Where(item => item.GroupId is Guid groupId && groups.Contains(groupId))) item.GroupId = null;
        Touch(page);
        return true;
    }

    public static bool UpdateStyle(NotebookPage page, IEnumerable<Guid> objectIds, string? strokeColor = null,
        string? fillColor = null, double? strokeThickness = null, double? opacity = null)
    {
        var selected = Selected(page, ExpandSelection(page, objectIds), includeLocked: false).ToArray();
        if (selected.Length == 0) return false;
        foreach (var item in selected)
        {
            if (!string.IsNullOrWhiteSpace(strokeColor)) item.StrokeColor = strokeColor;
            if (!string.IsNullOrWhiteSpace(fillColor)) item.FillColor = fillColor;
            if (strokeThickness is double thickness && double.IsFinite(thickness)) item.StrokeThickness = Math.Clamp(thickness, 1, 20);
            if (opacity is double alpha && double.IsFinite(alpha)) item.Opacity = Math.Clamp(alpha, .1, 1);
        }
        Touch(page);
        return true;
    }

    private static bool Reorder(NotebookPage page, IEnumerable<Guid> objectIds, bool bringToFront)
    {
        var ids = ExpandSelection(page, objectIds).ToHashSet();
        var selected = page.Objects.Where(item => ids.Contains(item.Id) && !item.IsLocked).ToArray();
        if (selected.Length == 0) return false;
        page.Objects.RemoveAll(item => selected.Contains(item));
        if (bringToFront) page.Objects.AddRange(selected); else page.Objects.InsertRange(0, selected);
        Touch(page);
        return true;
    }

    private static IEnumerable<PageObject> Selected(NotebookPage page, IEnumerable<Guid> ids, bool includeLocked)
    {
        ArgumentNullException.ThrowIfNull(page);
        var set = ids.ToHashSet();
        return page.Objects.Where(item => set.Contains(item.Id) && (includeLocked || !item.IsLocked));
    }

    private static PageObjectBounds Bounds(IReadOnlyCollection<PageObject> items)
    {
        var left = items.Min(item => item.X);
        var top = items.Min(item => item.Y);
        var right = items.Max(item => item.X + item.Width);
        var bottom = items.Max(item => item.Y + item.Height);
        return new PageObjectBounds(left, top, right - left, bottom - top);
    }

    private static double NormalizeRotation(double rotation)
    {
        var normalized = rotation % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static void Touch(NotebookPage page) => page.ModifiedAt = DateTimeOffset.Now;
}
