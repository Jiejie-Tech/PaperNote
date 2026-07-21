using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PaperNote.Core.Models;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private static readonly (string Label, string Value)[] ObjectStyleColors =
    [
        ("黑色", "#202124"),
        ("蓝色", "#3978F6"),
        ("红色", "#E5484D"),
        ("绿色", "#2E9D64"),
        ("紫色", "#7C5CE5"),
        ("橙色", "#F08C2E")
    ];

    private static readonly (string Label, string Value)[] ObjectFillColors =
    [
        ("透明", "#00FFFFFF"),
        ("浅蓝", "#303978F6"),
        ("浅黄", "#40F4C542"),
        ("浅红", "#30E5484D"),
        ("浅绿", "#302E9D64"),
        ("浅紫", "#307C5CE5")
    ];

    private void ObjectStyles_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = CreateObjectStylesMenu();
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private ContextMenu CreateObjectStylesMenu()
    {
        var menu = new ContextMenu();
        foreach (var item in CreateObjectStyleMenuItems()) menu.Items.Add(item);
        return menu;
    }

    private MenuItem CreateObjectStyleSubmenu()
    {
        var root = new MenuItem
        {
            Header = "对象样式",
            IsEnabled = SelectedPageObjects().Any(item => !item.IsLocked) && !_isReadOnly
        };
        foreach (var item in CreateObjectStyleMenuItems()) root.Items.Add(item);
        return root;
    }

    private IEnumerable<Control> CreateObjectStyleMenuItems()
    {
        var selected = SelectedPageObjects().Where(item => !item.IsLocked).ToArray();
        var hasSelection = selected.Length > 0 && !_isReadOnly;
        var hasText = hasSelection && selected.Any(item => item.Kind == "Text");
        var hasShape = hasSelection && selected.Any(item => item.Kind == "Shape");
        var hasFillableShape = hasSelection && selected.Any(item => item.Kind == "Shape" && item.ShapeKind != "Line");

        yield return CreateColorStyleMenu("文字/描边颜色", ObjectStyleColors, ChangeSelectedPageObjectsStrokeColor, hasText || hasShape);
        yield return CreateColorStyleMenu("形状填充", ObjectFillColors, ChangeSelectedPageObjectsFillColor, hasFillableShape);
        yield return CreateNumberStyleMenu("文字大小", [("14", 14), ("18", 18), ("20", 20), ("24", 24), ("32", 32), ("40", 40)], ChangeSelectedTextFontSize, hasText);
        yield return CreateNumberStyleMenu("形状线宽", [("1", 1), ("2", 2), ("3", 3), ("5", 5), ("8", 8), ("12", 12)], ChangeSelectedShapeStrokeThickness, hasShape);
        yield return CreateNumberStyleMenu("透明度", [("100%", 1), ("75%", 0.75), ("50%", 0.5), ("25%", 0.25)], ChangeSelectedPageObjectsOpacity, hasSelection);
        yield return new Separator();
        yield return CreateMenuItem("恢复默认样式", "", (_, _) => ResetSelectedPageObjectStyle(), hasSelection);
    }

    private static MenuItem CreateColorStyleMenu(
        string header,
        IEnumerable<(string Label, string Value)> choices,
        Func<string, bool> apply,
        bool enabled)
    {
        var root = new MenuItem { Header = header, IsEnabled = enabled };
        foreach (var (label, value) in choices)
        {
            root.Items.Add(CreateMenuItem(label, "", (_, _) => apply(value), enabled));
        }
        return root;
    }

    private static MenuItem CreateNumberStyleMenu(
        string header,
        IEnumerable<(string Label, double Value)> choices,
        Func<double, bool> apply,
        bool enabled)
    {
        var root = new MenuItem { Header = header, IsEnabled = enabled };
        foreach (var (label, value) in choices)
        {
            root.Items.Add(CreateMenuItem(label, "", (_, _) => apply(value), enabled));
        }
        return root;
    }

    private bool ChangeSelectedPageObjectsStrokeColor(string color)
    {
        return ApplySelectedPageObjectStyle(
            item => item.Kind is "Text" or "Shape",
            item => item.StrokeColor = color,
            "已修改文字或描边颜色");
    }

    private bool ChangeSelectedPageObjectsFillColor(string color)
    {
        return ApplySelectedPageObjectStyle(
            item => item.Kind == "Shape" && item.ShapeKind != "Line",
            item => item.FillColor = color,
            "已修改形状填充颜色");
    }

    private bool ChangeSelectedTextFontSize(double fontSize)
    {
        var normalized = Math.Clamp(fontSize, 10, 96);
        return ApplySelectedPageObjectStyle(
            item => item.Kind == "Text",
            item => item.FontSize = normalized,
            $"文字大小已设为 {normalized:0}");
    }

    private bool ChangeSelectedShapeStrokeThickness(double thickness)
    {
        var normalized = Math.Clamp(thickness, 1, 20);
        return ApplySelectedPageObjectStyle(
            item => item.Kind == "Shape",
            item => item.StrokeThickness = normalized,
            $"形状线宽已设为 {normalized:0.#}");
    }

    private bool ChangeSelectedPageObjectsOpacity(double opacity)
    {
        var normalized = Math.Clamp(opacity, 0.1, 1);
        return ApplySelectedPageObjectStyle(
            _ => true,
            item => item.Opacity = normalized,
            $"对象透明度已设为 {normalized:P0}");
    }

    private bool ResetSelectedPageObjectStyle()
    {
        return ApplySelectedPageObjectStyle(
            _ => true,
            item =>
            {
                item.FontSize = PageObjectDefaults.FontSize;
                item.Opacity = PageObjectDefaults.Opacity;
                item.StrokeThickness = 3;
                if (item.Kind == "Text")
                {
                    item.StrokeColor = "#202124";
                }
                else if (item.Kind == "Shape")
                {
                    item.StrokeColor = PageObjectDefaults.StrokeColor;
                    item.FillColor = item.ShapeKind == "Line" ? "#00FFFFFF" : PageObjectDefaults.FillColor;
                }
            },
            "所选对象已恢复默认样式");
    }

    private bool ApplySelectedPageObjectStyle(
        Func<PageObject, bool> filter,
        Action<PageObject> update,
        string status)
    {
        if (_isReadOnly || _currentPage is null || _selectedPageObjectIds.Count == 0) return false;
        var targets = SelectedPageObjects().Where(item => !item.IsLocked).Where(filter).ToArray();
        if (targets.Length == 0) return false;

        foreach (var pageObject in targets)
        {
            update(pageObject);
            RefreshPageObjectStyle(pageObject);
        }

        PageObjectChanged();
        UpdatePageObjectSelectionVisuals();
        StatusText.Text = targets.Length == 1 ? status : $"{status}（{targets.Length} 个对象）";
        return true;
    }

    private void RefreshPageObjectStyle(PageObject pageObject)
    {
        if (!_pageObjectContainers.TryGetValue(pageObject.Id, out var container) ||
            container.Child is not Grid root ||
            root.Children.Count == 0 ||
            root.Children[0] is not Border contentHost)
        {
            return;
        }

        contentHost.Opacity = Math.Clamp(pageObject.Opacity, 0.1, 1);
        contentHost.RenderTransformOrigin = new Point(0.5, 0.5);
        contentHost.RenderTransform = new RotateTransform(pageObject.Rotation);
        contentHost.Child = CreatePageObjectContent(pageObject);
    }
}
