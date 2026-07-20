using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PaperNote.Desktop.Models;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private void ObjectArrange_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = CreateObjectArrangeMenu();
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private ContextMenu CreateObjectArrangeMenu()
    {
        var menu = new ContextMenu();
        foreach (var item in CreateObjectArrangeMenuItems()) menu.Items.Add(item);
        return menu;
    }

    private MenuItem CreateObjectArrangeSubmenu()
    {
        var root = new MenuItem { Header = "排列与锁定", IsEnabled = _selectedPageObjectIds.Count > 0 };
        foreach (var item in CreateObjectArrangeMenuItems()) root.Items.Add(item);
        return root;
    }

    private IEnumerable<Control> CreateObjectArrangeMenuItems()
    {
        var selected = SelectedPageObjects().ToArray();
        var editable = !_isReadOnly && selected.Length > 0 && selected.All(item => !item.IsLocked);
        var canAlign = editable && selected.Length >= 2;
        var canDistribute = editable && selected.Length >= 3;
        var canLock = !_isReadOnly && selected.Any(item => !item.IsLocked);
        var canUnlock = !_isReadOnly && selected.Any(item => item.IsLocked);

        var align = new MenuItem { Header = "对齐", IsEnabled = canAlign };
        align.Items.Add(CreateMenuItem("左对齐", "", (_, _) => AlignSelectedPageObjectsLeft(), canAlign));
        align.Items.Add(CreateMenuItem("水平居中", "", (_, _) => AlignSelectedPageObjectsHorizontalCenter(), canAlign));
        align.Items.Add(CreateMenuItem("右对齐", "", (_, _) => AlignSelectedPageObjectsRight(), canAlign));
        align.Items.Add(new Separator());
        align.Items.Add(CreateMenuItem("顶部对齐", "", (_, _) => AlignSelectedPageObjectsTop(), canAlign));
        align.Items.Add(CreateMenuItem("垂直居中", "", (_, _) => AlignSelectedPageObjectsVerticalCenter(), canAlign));
        align.Items.Add(CreateMenuItem("底部对齐", "", (_, _) => AlignSelectedPageObjectsBottom(), canAlign));
        yield return align;
        yield return CreateMenuItem("水平等距分布", "", (_, _) => DistributeSelectedPageObjectsHorizontally(), canDistribute);
        yield return CreateMenuItem("垂直等距分布", "", (_, _) => DistributeSelectedPageObjectsVertically(), canDistribute);
        yield return new Separator();
        yield return CreateMenuItem("左转 90°", "Ctrl+Alt+←", (_, _) => RotateSelectedPageObjectsLeft(), editable);
        yield return CreateMenuItem("右转 90°", "Ctrl+Alt+→", (_, _) => RotateSelectedPageObjectsRight(), editable);
        yield return new Separator();
        yield return CreateMenuItem("上移一层", "Ctrl+Alt+]", (_, _) => BringSelectedPageObjectsForward(), editable);
        yield return CreateMenuItem("下移一层", "Ctrl+Alt+[", (_, _) => SendSelectedPageObjectsBackward(), editable);
        yield return new Separator();
        yield return CreateMenuItem("锁定", "Ctrl+L", (_, _) => LockSelectedPageObjects(), canLock);
        yield return CreateMenuItem("解锁", "Ctrl+Shift+L", (_, _) => UnlockSelectedPageObjects(), canUnlock);
    }

    private bool AlignSelectedPageObjectsLeft() => ApplySelectedPageObjectLayout(
        items => { var target = items.Min(item => item.X); foreach (var item in items) item.X = target; },
        2, "所选对象已左对齐");

    private bool AlignSelectedPageObjectsHorizontalCenter() => ApplySelectedPageObjectLayout(
        items =>
        {
            var center = (items.Min(item => item.X) + items.Max(item => item.X + item.Width)) / 2;
            foreach (var item in items) item.X = center - item.Width / 2;
        },
        2, "所选对象已水平居中");

    private bool AlignSelectedPageObjectsRight() => ApplySelectedPageObjectLayout(
        items => { var target = items.Max(item => item.X + item.Width); foreach (var item in items) item.X = target - item.Width; },
        2, "所选对象已右对齐");

    private bool AlignSelectedPageObjectsTop() => ApplySelectedPageObjectLayout(
        items => { var target = items.Min(item => item.Y); foreach (var item in items) item.Y = target; },
        2, "所选对象已顶部对齐");

    private bool AlignSelectedPageObjectsVerticalCenter() => ApplySelectedPageObjectLayout(
        items =>
        {
            var center = (items.Min(item => item.Y) + items.Max(item => item.Y + item.Height)) / 2;
            foreach (var item in items) item.Y = center - item.Height / 2;
        },
        2, "所选对象已垂直居中");

    private bool AlignSelectedPageObjectsBottom() => ApplySelectedPageObjectLayout(
        items => { var target = items.Max(item => item.Y + item.Height); foreach (var item in items) item.Y = target - item.Height; },
        2, "所选对象已底部对齐");

    private bool DistributeSelectedPageObjectsHorizontally() => ApplySelectedPageObjectLayout(
        items =>
        {
            var ordered = items.OrderBy(item => item.X + item.Width / 2).ToArray();
            var first = ordered[0].X + ordered[0].Width / 2;
            var last = ordered[^1].X + ordered[^1].Width / 2;
            var step = (last - first) / (ordered.Length - 1);
            for (var index = 1; index < ordered.Length - 1; index++)
                ordered[index].X = first + step * index - ordered[index].Width / 2;
        },
        3, "所选对象已水平等距分布");

    private bool DistributeSelectedPageObjectsVertically() => ApplySelectedPageObjectLayout(
        items =>
        {
            var ordered = items.OrderBy(item => item.Y + item.Height / 2).ToArray();
            var first = ordered[0].Y + ordered[0].Height / 2;
            var last = ordered[^1].Y + ordered[^1].Height / 2;
            var step = (last - first) / (ordered.Length - 1);
            for (var index = 1; index < ordered.Length - 1; index++)
                ordered[index].Y = first + step * index - ordered[index].Height / 2;
        },
        3, "所选对象已垂直等距分布");

    private bool ApplySelectedPageObjectLayout(Action<PageObject[]> layout, int minimumCount, string status)
    {
        if (_isReadOnly || _currentPage is null) return false;
        var selected = SelectedPageObjects().ToArray();
        if (selected.Length < minimumCount || selected.Any(item => item.IsLocked)) return false;

        layout(selected);
        foreach (var item in selected)
        {
            item.X = Math.Clamp(item.X, 0, Math.Max(0, PageObjectCanvasWidth - item.Width));
            item.Y = Math.Clamp(item.Y, 0, Math.Max(0, PageObjectCanvasHeight - item.Height));
            RefreshPageObjectGeometry(item);
        }
        PageObjectChanged();
        UpdatePageObjectSelectionVisuals();
        StatusText.Text = status;
        return true;
    }

    private bool RotateSelectedPageObjectsLeft() => RotateSelectedPageObjects(-90, "所选对象已左转 90°");
    private bool RotateSelectedPageObjectsRight() => RotateSelectedPageObjects(90, "所选对象已右转 90°");

    private bool RotateSelectedPageObjects(double delta, string status)
    {
        if (_isReadOnly || _currentPage is null) return false;
        var selected = SelectedPageObjects().ToArray();
        if (selected.Length == 0 || selected.Any(item => item.IsLocked)) return false;
        foreach (var item in selected)
        {
            item.Rotation = NormalizePageObjectRotation(item.Rotation + delta);
            RefreshPageObjectStyle(item);
        }
        PageObjectChanged();
        UpdatePageObjectSelectionVisuals();
        StatusText.Text = status;
        return true;
    }

    private static double NormalizePageObjectRotation(double rotation)
    {
        if (!double.IsFinite(rotation)) return 0;
        var normalized = rotation % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private bool LockSelectedPageObjects()
    {
        if (_isReadOnly) return false;
        var targets = SelectedPageObjects().Where(item => !item.IsLocked).ToArray();
        if (targets.Length == 0) return false;
        foreach (var item in targets)
        {
            item.IsLocked = true;
            RebuildPageObjectContainer(item);
        }
        PageObjectChanged();
        UpdatePageObjectSelectionVisuals();
        StatusText.Text = targets.Length == 1 ? "对象已锁定" : $"已锁定 {targets.Length} 个对象";
        return true;
    }

    private bool UnlockSelectedPageObjects()
    {
        if (_isReadOnly) return false;
        var targets = SelectedPageObjects().Where(item => item.IsLocked).ToArray();
        if (targets.Length == 0) return false;
        foreach (var item in targets)
        {
            item.IsLocked = false;
            RebuildPageObjectContainer(item);
        }
        PageObjectChanged();
        UpdatePageObjectSelectionVisuals();
        StatusText.Text = targets.Length == 1 ? "对象已解锁" : $"已解锁 {targets.Length} 个对象";
        return true;
    }

    private bool BringSelectedPageObjectsForward() => MoveSelectedPageObjectsOneLayer(forward: true);
    private bool SendSelectedPageObjectsBackward() => MoveSelectedPageObjectsOneLayer(forward: false);

    private bool MoveSelectedPageObjectsOneLayer(bool forward)
    {
        if (_isReadOnly || _currentPage is null || _selectedPageObjectIds.Count == 0) return false;
        var selected = SelectedPageObjects().ToArray();
        if (selected.Any(item => item.IsLocked)) return false;
        var objects = _currentPage.Objects;
        var changed = false;
        if (forward)
        {
            for (var index = objects.Count - 2; index >= 0; index--)
            {
                if (!_selectedPageObjectIds.Contains(objects[index].Id) || _selectedPageObjectIds.Contains(objects[index + 1].Id)) continue;
                (objects[index], objects[index + 1]) = (objects[index + 1], objects[index]);
                changed = true;
            }
        }
        else
        {
            for (var index = 1; index < objects.Count; index++)
            {
                if (!_selectedPageObjectIds.Contains(objects[index].Id) || _selectedPageObjectIds.Contains(objects[index - 1].Id)) continue;
                (objects[index], objects[index - 1]) = (objects[index - 1], objects[index]);
                changed = true;
            }
        }
        if (!changed) return false;
        ReorderObjectLayer();
        PageObjectChanged();
        UpdatePageObjectSelectionVisuals();
        StatusText.Text = forward ? "所选对象已上移一层" : "所选对象已下移一层";
        return true;
    }

    private void RefreshPageObjectGeometry(PageObject pageObject)
    {
        if (!_pageObjectContainers.TryGetValue(pageObject.Id, out var container)) return;
        container.Width = pageObject.Width;
        container.Height = pageObject.Height;
        Canvas.SetLeft(container, pageObject.X);
        Canvas.SetTop(container, pageObject.Y);
    }

    private void RebuildPageObjectContainer(PageObject pageObject)
    {
        if (!_pageObjectContainers.TryGetValue(pageObject.Id, out var previous)) return;
        var index = ObjectLayer.Children.IndexOf(previous);
        if (index < 0) return;
        var replacement = CreatePageObjectContainer(pageObject);
        ObjectLayer.Children.RemoveAt(index);
        ObjectLayer.Children.Insert(index, replacement);
        _pageObjectContainers[pageObject.Id] = replacement;
        if (_selectedPageObject?.Id == pageObject.Id) _selectedObjectContainer = replacement;
        SetPageObjectsReadOnly(_isReadOnly);
    }
}
