using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using PaperNote.Desktop.Models;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private const double PageObjectCanvasWidth = 840;
    private const double PageObjectCanvasHeight = 1188;
    private readonly Dictionary<Guid, Border> _pageObjectContainers = [];
    private readonly HashSet<Guid> _selectedPageObjectIds = [];
    private readonly List<PageObject> _pageObjectClipboard = [];
    private readonly Dictionary<Guid, Point> _pageObjectDragOrigins = [];
    private PageObject? _selectedPageObject;
    private Border? _selectedObjectContainer;
    private bool _isDraggingPageObject;
    private Point _pageObjectDragStart;
    private int _pasteSequence;

    private void InsertText_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditPageObjects()) return;
        var pageObject = new PageObject
        {
            Kind = "Text",
            X = 210,
            Y = 230,
            Width = 320,
            Height = 150,
            Text = "双击这里输入文字"
        };
        AddPageObject(pageObject, "已插入文本框");

        if (_selectedObjectContainer is not null)
        {
            var textBox = FindVisualChildren<TextBox>(_selectedObjectContainer).FirstOrDefault();
            textBox?.Focus();
            textBox?.SelectAll();
        }
    }

    private void InsertImage_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditPageObjects()) return;
        var dialog = new OpenFileDialog
        {
            Title = "选择要插入的图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            if (bytes.Length > 20 * 1024 * 1024)
            {
                MessageBox.Show(this, "单张图片不能超过 20 MB。", "图片过大", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var bitmap = DecodeImage(bytes);
            var width = Math.Min(440d, Math.Max(180d, bitmap.PixelWidth));
            var height = Math.Min(360d, Math.Max(120d, width * bitmap.PixelHeight / Math.Max(1d, bitmap.PixelWidth)));
            var pageObject = new PageObject
            {
                Kind = "Image",
                X = (PageObjectCanvasWidth - width) / 2,
                Y = 190,
                Width = width,
                Height = height,
                ImageData = Convert.ToBase64String(bytes)
            };
            AddPageObject(pageObject, "已插入图片");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            MessageBox.Show(this, $"无法插入这张图片。\n\n{exception.Message}", "插入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShapeActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = new ContextMenu();
        foreach (var option in new[]
                 {
                     (Kind: "Rectangle", Label: "矩形  □"),
                     (Kind: "RoundedRectangle", Label: "圆角矩形  ▢"),
                     (Kind: "Ellipse", Label: "圆形 / 椭圆  ○"),
                     (Kind: "Triangle", Label: "三角形  △"),
                     (Kind: "Diamond", Label: "菱形  ◇"),
                     (Kind: "Arrow", Label: "箭头  ➜"),
                     (Kind: "Line", Label: "直线  ╱")
                 })
        {
            var capturedKind = option.Kind;
            menu.Items.Add(CreateMenuItem(option.Label, "", (_, _) => InsertShape(capturedKind), !_isReadOnly && _currentPage is not null));
        }
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void InsertShape_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string shapeKind }) InsertShape(shapeKind);
    }

    private void InsertShape(string shapeKind)
    {
        if (!CanEditPageObjects()) return;
        var isLine = shapeKind == "Line";
        var isArrow = shapeKind == "Arrow";
        var pageObject = new PageObject
        {
            Kind = "Shape",
            ShapeKind = shapeKind,
            X = 260,
            Y = 260,
            Width = isLine || isArrow ? 300 : 240,
            Height = isLine ? 100 : isArrow ? 130 : 180,
            StrokeColor = ColorToHex(_currentColor),
            FillColor = isLine ? "#00FFFFFF" : WithAlpha(_currentColor, isArrow ? (byte)0x40 : (byte)0x18),
            StrokeThickness = Math.Clamp(ThicknessSlider.Value, 2, 10)
        };
        var displayName = shapeKind switch
        {
            "RoundedRectangle" => "圆角矩形",
            "Ellipse" => "圆形",
            "Triangle" => "三角形",
            "Diamond" => "菱形",
            "Arrow" => "箭头",
            "Line" => "直线",
            _ => "矩形"
        };
        AddPageObject(pageObject, $"已插入{displayName}");
    }

    private void ObjectActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = CreateObjectActionsMenu();
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private ContextMenu CreateObjectActionsMenu()
    {
        var selected = SelectedPageObjects().ToArray();
        var hasSelection = selected.Length > 0;
        var hasLocked = selected.Any(item => item.IsLocked);
        var hasEditable = selected.Any(item => !item.IsLocked);
        var canModifyAll = hasSelection && !hasLocked && !_isReadOnly;

        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("复制", "Ctrl+C", (_, _) => CopySelectedPageObjects(), hasSelection));
        menu.Items.Add(CreateMenuItem("剪切", "Ctrl+X", (_, _) => CutSelectedPageObjects(), canModifyAll));
        menu.Items.Add(CreateMenuItem("粘贴", "Ctrl+V", (_, _) => PastePageObjects(), _pageObjectClipboard.Count > 0 && !_isReadOnly));
        menu.Items.Add(CreateMenuItem("复制一份", "", (_, _) => DuplicateSelectedPageObjects(), hasSelection && !_isReadOnly));
        menu.Items.Add(CreateObjectStyleSubmenu());
        menu.Items.Add(CreatePageLinkSubmenu());
        menu.Items.Add(CreateObjectArrangeSubmenu());
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("组合", "Ctrl+G", (_, _) => GroupSelectedPageObjects(), selected.Length > 1 && canModifyAll));
        menu.Items.Add(CreateMenuItem("取消组合", "Ctrl+Shift+G", (_, _) => UngroupSelectedPageObjects(), canModifyAll && selected.Any(item => item.GroupId.HasValue)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("置于顶层", "Ctrl+]", (_, _) => BringSelectedPageObjectsToFront(), canModifyAll));
        menu.Items.Add(CreateMenuItem("置于底层", "Ctrl+[", (_, _) => SendSelectedPageObjectsToBack(), canModifyAll));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("删除", "Delete", (_, _) => DeleteSelectedPageObject(), hasEditable && !_isReadOnly));
        return menu;
    }
    private static MenuItem CreateMenuItem(string header, string gesture, RoutedEventHandler click, bool enabled)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
        item.Click += click;
        return item;
    }

    private bool CanEditPageObjects()
    {
        if (_currentPage is null) return false;
        if (!_isReadOnly) return true;
        StatusText.Text = "只读模式下不能插入或修改对象";
        return false;
    }

    private void AddPageObject(PageObject pageObject, string status)
    {
        if (_currentPage is null) return;
        _currentPage.Objects.Add(pageObject);
        var container = CreatePageObjectContainer(pageObject);
        _pageObjectContainers[pageObject.Id] = container;
        ObjectLayer.Children.Add(container);
        SelectPageObject(pageObject, container);
        PageObjectChanged();
        StatusText.Text = status;
    }

    private void LoadPageObjects(NotebookPage page)
    {
        ClearPageObjectSelection();
        _pageObjectContainers.Clear();
        ObjectLayer.Children.Clear();
        foreach (var pageObject in page.Objects)
        {
            var container = CreatePageObjectContainer(pageObject);
            _pageObjectContainers[pageObject.Id] = container;
            ObjectLayer.Children.Add(container);
        }
        SetPageObjectsReadOnly(_isReadOnly);
    }

    private Border CreatePageObjectContainer(PageObject pageObject)
    {
        var container = new Border
        {
            Width = pageObject.Width,
            Height = pageObject.Height,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            Tag = pageObject,
            CornerRadius = new CornerRadius(4),
            ContextMenu = CreateObjectActionsMenu(),
            ToolTip = pageObject.LinkTargetPageId.HasValue ? "页面链接：只读模式下单击打开；编辑模式下按 Ctrl 单击打开" : null
        };
        Canvas.SetLeft(container, pageObject.X);
        Canvas.SetTop(container, pageObject.Y);

        var root = new Grid();
        container.Child = root;

        var contentHost = new Border
        {
            Margin = new Thickness(7, 22, 7, 7),
            Background = pageObject.Kind == "Text" ? new SolidColorBrush(Color.FromArgb(12, 57, 120, 246)) : Brushes.Transparent,
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true,
            Opacity = Math.Clamp(pageObject.Opacity, 0.1, 1),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(pageObject.Rotation),
            Child = CreatePageObjectContent(pageObject)
        };
        root.Children.Add(contentHost);

        var dragHandle = new Border
        {
            Height = 22,
            VerticalAlignment = VerticalAlignment.Top,
            Background = pageObject.IsLocked
                ? new SolidColorBrush(Color.FromArgb(52, 107, 114, 128))
                : new SolidColorBrush(Color.FromArgb(42, 57, 120, 246)),
            Cursor = pageObject.IsLocked ? Cursors.Arrow : Cursors.SizeAll,
            CornerRadius = new CornerRadius(4, 4, 0, 0),
            ToolTip = pageObject.IsLocked ? "对象已锁定；仍可选择并解锁" : "拖动对象；按住 Ctrl 点击可多选",
            IsHitTestVisible = !pageObject.IsLocked
        };
        dragHandle.Child = new Border
        {
            Width = 34,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = pageObject.IsLocked
                ? new SolidColorBrush(Color.FromArgb(150, 107, 114, 128))
                : new SolidColorBrush(Color.FromArgb(120, 57, 120, 246)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        dragHandle.MouseLeftButtonDown += PageObjectDragHandle_MouseLeftButtonDown;
        dragHandle.MouseMove += PageObjectDragHandle_MouseMove;
        dragHandle.MouseLeftButtonUp += PageObjectDragHandle_MouseLeftButtonUp;
        root.Children.Add(dragHandle);

        var resizeThumb = new Thumb
        {
            Width = 15,
            Height = 15,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 1, 1),
            Cursor = Cursors.SizeNWSE,
            Background = new SolidColorBrush(Color.FromRgb(57, 120, 246)),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            ToolTip = pageObject.IsLocked ? "对象已锁定" : "调整大小",
            Visibility = pageObject.IsLocked ? Visibility.Collapsed : Visibility.Visible,
            IsEnabled = !pageObject.IsLocked
        };
        resizeThumb.DragStarted += (_, _) =>
        {
            if (!_selectedPageObjectIds.Contains(pageObject.Id)) SelectPageObject(pageObject, container);
        };
        resizeThumb.DragDelta += (_, args) => ResizePageObject(pageObject, container, args.HorizontalChange, args.VerticalChange);
        resizeThumb.DragCompleted += (_, _) => PageObjectChanged();
        root.Children.Add(resizeThumb);

        if (pageObject.IsLocked)
        {
            root.Children.Add(new TextBlock
            {
                Text = "\U0001F512",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 7, 0),
                ToolTip = "对象已锁定",
                IsHitTestVisible = false
            });
        }

        if (pageObject.LinkTargetPageId.HasValue)
        {
            root.Children.Add(new Border
            {
                Width = 22, Height = 18, CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromArgb(225, 57, 120, 246)),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, pageObject.IsLocked ? 28 : 5, 0),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = "↗", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            });
        }

        container.PreviewMouseLeftButtonDown += (_, args) =>
        {
            var controlPressed = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (pageObject.LinkTargetPageId.HasValue && (_isReadOnly || controlPressed))
            {
                OpenPageObjectLink(pageObject);
                args.Handled = true;
                return;
            }
            var toggle = controlPressed;
            SelectPageObject(pageObject, container, toggle);
            if (toggle) args.Handled = true;
        };
        container.PreviewMouseRightButtonDown += (_, _) =>
        {
            if (!_selectedPageObjectIds.Contains(pageObject.Id)) SelectPageObject(pageObject, container);
            container.ContextMenu = CreateObjectActionsMenu();
        };
        return container;
    }

    private FrameworkElement CreatePageObjectContent(PageObject pageObject)
    {
        switch (pageObject.Kind)
        {
            case "Image":
                try
                {
                    return new Image
                    {
                        Source = DecodeImage(Convert.FromBase64String(pageObject.ImageData)),
                        Stretch = Stretch.Uniform,
                        IsHitTestVisible = false
                    };
                }
                catch
                {
                    return new TextBlock
                    {
                        Text = "图片无法显示",
                        Foreground = Brushes.Gray,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
            case "Shape":
                return CreateShape(pageObject);
            default:
                var textBox = new TextBox
                {
                    Text = pageObject.Text,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Padding = new Thickness(7),
                    FontSize = Math.Clamp(pageObject.FontSize, 10, 96),
                    Foreground = new SolidColorBrush(ParseColor(pageObject.StrokeColor, Colors.Black)),
                    IsReadOnly = _isReadOnly || pageObject.IsLocked
                };
                textBox.TextChanged += (_, _) =>
                {
                    if (_isSwitchingPage) return;
                    pageObject.Text = textBox.Text;
                    PageObjectChanged();
                };
                return textBox;
        }
    }

    private static FrameworkElement CreateShape(PageObject pageObject)
    {
        var stroke = new SolidColorBrush(ParseColor(pageObject.StrokeColor, Color.FromRgb(57, 120, 246)));
        var fill = new SolidColorBrush(ParseColor(pageObject.FillColor, Color.FromArgb(24, 57, 120, 246)));
        if (pageObject.ShapeKind == "Line")
        {
            return new System.Windows.Shapes.Path
            {
                Data = new LineGeometry(new Point(0, 1), new Point(1, 0)),
                Stretch = Stretch.Fill,
                Stroke = stroke,
                StrokeThickness = pageObject.StrokeThickness,
                Margin = new Thickness(8),
                IsHitTestVisible = false
            };
        }

        if (pageObject.ShapeKind is "Triangle" or "Diamond" or "Arrow")
        {
            return new System.Windows.Shapes.Path
            {
                Data = CreateNormalizedShapeGeometry(pageObject.ShapeKind),
                Stretch = Stretch.Fill,
                Stroke = stroke,
                Fill = fill,
                StrokeThickness = pageObject.StrokeThickness,
                StrokeLineJoin = PenLineJoin.Round,
                Margin = new Thickness(8),
                IsHitTestVisible = false
            };
        }

        Shape shape = pageObject.ShapeKind switch
        {
            "Ellipse" => new Ellipse(),
            "RoundedRectangle" => new Rectangle { RadiusX = 18, RadiusY = 18 },
            _ => new Rectangle()
        };
        shape.Stroke = stroke;
        shape.Fill = fill;
        shape.StrokeThickness = pageObject.StrokeThickness;
        shape.Margin = new Thickness(7);
        shape.IsHitTestVisible = false;
        return shape;
    }

    private static Geometry CreateNormalizedShapeGeometry(string shapeKind)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            switch (shapeKind)
            {
                case "Triangle":
                    context.BeginFigure(new Point(0.5, 0), true, true);
                    context.PolyLineTo([new Point(1, 1), new Point(0, 1)], true, true);
                    break;
                case "Diamond":
                    context.BeginFigure(new Point(0.5, 0), true, true);
                    context.PolyLineTo([new Point(1, 0.5), new Point(0.5, 1), new Point(0, 0.5)], true, true);
                    break;
                default:
                    context.BeginFigure(new Point(0, 0.34), true, true);
                    context.PolyLineTo([
                        new Point(0.62, 0.34), new Point(0.62, 0), new Point(1, 0.5),
                        new Point(0.62, 1), new Point(0.62, 0.66), new Point(0, 0.66)
                    ], true, true);
                    break;
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private void SelectPageObject(PageObject pageObject, Border container) => SelectPageObject(pageObject, container, false);

    private void SelectPageObject(PageObject pageObject, Border container, bool toggle)
    {
        var selectionUnit = GetSelectionUnit(pageObject).Select(item => item.Id).ToArray();
        if (!toggle) ClearPageObjectSelection();

        if (toggle && selectionUnit.All(id => _selectedPageObjectIds.Contains(id)))
        {
            foreach (var id in selectionUnit) _selectedPageObjectIds.Remove(id);
        }
        else
        {
            foreach (var id in selectionUnit) _selectedPageObjectIds.Add(id);
        }

        if (_selectedPageObjectIds.Contains(pageObject.Id))
        {
            _selectedPageObject = pageObject;
            _selectedObjectContainer = container;
        }
        else
        {
            SetPrimarySelectionFromCurrentIds();
        }
        UpdatePageObjectSelectionVisuals();
    }

    private IReadOnlyList<PageObject> GetSelectionUnit(PageObject pageObject)
    {
        if (_currentPage is null || !pageObject.GroupId.HasValue) return [pageObject];
        return _currentPage.Objects.Where(item => item.GroupId == pageObject.GroupId).ToArray();
    }

    private void ClearPageObjectSelection()
    {
        foreach (var container in _pageObjectContainers.Values) container.BorderBrush = Brushes.Transparent;
        _selectedPageObjectIds.Clear();
        _selectedPageObject = null;
        _selectedObjectContainer = null;
    }

    private void SetPrimarySelectionFromCurrentIds()
    {
        if (_currentPage is null)
        {
            _selectedPageObject = null;
            _selectedObjectContainer = null;
            return;
        }
        _selectedPageObject = _currentPage.Objects.LastOrDefault(item => _selectedPageObjectIds.Contains(item.Id));
        _selectedObjectContainer = _selectedPageObject is not null && _pageObjectContainers.TryGetValue(_selectedPageObject.Id, out var container) ? container : null;
    }

    private void UpdatePageObjectSelectionVisuals()
    {
        var accent = new SolidColorBrush(Color.FromRgb(57, 120, 246));
        foreach (var pair in _pageObjectContainers)
        {
            pair.Value.BorderBrush = _selectedPageObjectIds.Contains(pair.Key) ? accent : Brushes.Transparent;
        }
        if (_selectedPageObjectIds.Count == 0) return;
        var lockedCount = SelectedPageObjects().Count(item => item.IsLocked);
        if (lockedCount > 0)
        {
            StatusText.Text = _selectedPageObjectIds.Count == 1
                ? "已选择锁定对象 · 可复制或解锁"
                : $"已选择 {_selectedPageObjectIds.Count} 个对象，其中 {lockedCount} 个已锁定";
            return;
        }
        StatusText.Text = _selectedPageObjectIds.Count == 1
            ? "已选择 1 个对象 · 可复制、拖动、缩放、排列或删除"
            : $"已选择 {_selectedPageObjectIds.Count} 个对象 · 可组合、对齐、分布或一起拖动";
    }

    private IEnumerable<PageObject> SelectedPageObjects()
    {
        return _currentPage?.Objects.Where(item => _selectedPageObjectIds.Contains(item.Id)) ?? [];
    }

    private void PageObjectDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isReadOnly || sender is not Border handle || FindObjectContainer(handle) is not Border container || container.Tag is not PageObject pageObject) return;
        if (pageObject.IsLocked) return;
        if (!_selectedPageObjectIds.Contains(pageObject.Id)) SelectPageObject(pageObject, container);
        if (SelectedPageObjects().Any(item => item.IsLocked)) return;
        _isDraggingPageObject = true;
        _pageObjectDragStart = e.GetPosition(ObjectLayer);
        _pageObjectDragOrigins.Clear();
        foreach (var item in SelectedPageObjects()) _pageObjectDragOrigins[item.Id] = new Point(item.X, item.Y);
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void PageObjectDragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingPageObject || sender is not Border || e.LeftButton != MouseButtonState.Pressed || _currentPage is null) return;
        var current = e.GetPosition(ObjectLayer);
        var requestedX = current.X - _pageObjectDragStart.X;
        var requestedY = current.Y - _pageObjectDragStart.Y;
        var selected = SelectedPageObjects().ToArray();
        if (selected.Length == 0) return;

        var minX = selected.Max(item => -_pageObjectDragOrigins[item.Id].X);
        var maxX = selected.Min(item => PageObjectCanvasWidth - (_pageObjectDragOrigins[item.Id].X + item.Width));
        var minY = selected.Max(item => -_pageObjectDragOrigins[item.Id].Y);
        var maxY = selected.Min(item => PageObjectCanvasHeight - (_pageObjectDragOrigins[item.Id].Y + item.Height));
        var deltaX = Math.Clamp(requestedX, minX, maxX);
        var deltaY = Math.Clamp(requestedY, minY, maxY);

        foreach (var item in selected)
        {
            var origin = _pageObjectDragOrigins[item.Id];
            item.X = origin.X + deltaX;
            item.Y = origin.Y + deltaY;
            if (_pageObjectContainers.TryGetValue(item.Id, out var itemContainer))
            {
                Canvas.SetLeft(itemContainer, item.X);
                Canvas.SetTop(itemContainer, item.Y);
            }
        }
        e.Handled = true;
    }

    private void PageObjectDragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingPageObject || sender is not Border handle) return;
        _isDraggingPageObject = false;
        handle.ReleaseMouseCapture();
        PageObjectChanged();
        e.Handled = true;
    }

    private static Border? FindObjectContainer(DependencyObject child)
    {
        DependencyObject? current = child;
        while (current is not null)
        {
            if (current is Border { Tag: PageObject } border) return border;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static void ResizePageObject(PageObject pageObject, Border container, double horizontalChange, double verticalChange)
    {
        if (pageObject.IsLocked) return;
        var minWidth = pageObject.Kind == "Text" ? 150 : 80;
        var minHeight = pageObject.Kind == "Text" ? 90 : 60;
        pageObject.Width = Math.Clamp(pageObject.Width + horizontalChange, minWidth, PageObjectCanvasWidth - pageObject.X);
        pageObject.Height = Math.Clamp(pageObject.Height + verticalChange, minHeight, PageObjectCanvasHeight - pageObject.Y);
        container.Width = pageObject.Width;
        container.Height = pageObject.Height;
    }

    private bool CopySelectedPageObjects()
    {
        var selected = SelectedPageObjects().ToArray();
        if (selected.Length == 0) return false;
        _pageObjectClipboard.Clear();
        _pageObjectClipboard.AddRange(selected.Select(item => item.Clone()));
        _pasteSequence = 0;
        StatusText.Text = $"已复制 {selected.Length} 个对象";
        return true;
    }

    private bool CutSelectedPageObjects()
    {
        if (_isReadOnly || SelectedPageObjects().Any(item => item.IsLocked) || !CopySelectedPageObjects()) return false;
        var count = _selectedPageObjectIds.Count;
        DeleteSelectedPageObject();
        StatusText.Text = $"已剪切 {count} 个对象";
        return true;
    }

    private bool PastePageObjects()
    {
        if (_isReadOnly || _currentPage is null || _pageObjectClipboard.Count == 0) return false;
        _pasteSequence++;
        var offset = 24d * ((_pasteSequence - 1) % 6 + 1);
        var groupMap = _pageObjectClipboard
            .Where(item => item.GroupId.HasValue)
            .Select(item => item.GroupId!.Value)
            .Distinct()
            .ToDictionary(groupId => groupId, _ => Guid.NewGuid());
        var pasted = new List<PageObject>();
        foreach (var clipboardObject in _pageObjectClipboard)
        {
            var copy = clipboardObject.Clone();
            copy.GroupId = clipboardObject.GroupId.HasValue ? groupMap[clipboardObject.GroupId.Value] : null;
            copy.X = Math.Clamp(clipboardObject.X + offset, 0, Math.Max(0, PageObjectCanvasWidth - copy.Width));
            copy.Y = Math.Clamp(clipboardObject.Y + offset, 0, Math.Max(0, PageObjectCanvasHeight - copy.Height));
            _currentPage.Objects.Add(copy);
            var container = CreatePageObjectContainer(copy);
            _pageObjectContainers[copy.Id] = container;
            ObjectLayer.Children.Add(container);
            pasted.Add(copy);
        }
        ClearPageObjectSelection();
        foreach (var item in pasted) _selectedPageObjectIds.Add(item.Id);
        SetPrimarySelectionFromCurrentIds();
        UpdatePageObjectSelectionVisuals();
        PageObjectChanged();
        StatusText.Text = $"已粘贴 {pasted.Count} 个对象";
        return true;
    }

    private bool DuplicateSelectedPageObjects()
    {
        if (!CopySelectedPageObjects()) return false;
        return PastePageObjects();
    }

    private bool GroupSelectedPageObjects()
    {
        if (_isReadOnly || _selectedPageObjectIds.Count < 2 || SelectedPageObjects().Any(item => item.IsLocked)) return false;
        var groupId = Guid.NewGuid();
        foreach (var pageObject in SelectedPageObjects()) pageObject.GroupId = groupId;
        PageObjectChanged();
        StatusText.Text = $"已组合 {_selectedPageObjectIds.Count} 个对象";
        return true;
    }

    private bool UngroupSelectedPageObjects()
    {
        if (_isReadOnly || SelectedPageObjects().Any(item => item.IsLocked)) return false;
        var grouped = SelectedPageObjects().Where(item => item.GroupId.HasValue).ToArray();
        if (grouped.Length == 0) return false;
        foreach (var pageObject in grouped) pageObject.GroupId = null;
        PageObjectChanged();
        StatusText.Text = "已取消组合";
        return true;
    }

    private bool BringSelectedPageObjectsToFront() => MoveSelectedPageObjectsLayer(toFront: true);
    private bool SendSelectedPageObjectsToBack() => MoveSelectedPageObjectsLayer(toFront: false);

    private bool MoveSelectedPageObjectsLayer(bool toFront)
    {
        if (_isReadOnly || _currentPage is null || _selectedPageObjectIds.Count == 0) return false;
        var selected = _currentPage.Objects.Where(item => _selectedPageObjectIds.Contains(item.Id)).ToArray();
        if (selected.Any(item => item.IsLocked)) return false;
        var remaining = _currentPage.Objects.Where(item => !_selectedPageObjectIds.Contains(item.Id)).ToArray();
        _currentPage.Objects.Clear();
        if (toFront)
        {
            _currentPage.Objects.AddRange(remaining);
            _currentPage.Objects.AddRange(selected);
        }
        else
        {
            _currentPage.Objects.AddRange(selected);
            _currentPage.Objects.AddRange(remaining);
        }
        ReorderObjectLayer();
        PageObjectChanged();
        StatusText.Text = toFront ? "所选对象已置于顶层" : "所选对象已置于底层";
        return true;
    }

    private void ReorderObjectLayer()
    {
        if (_currentPage is null) return;
        ObjectLayer.Children.Clear();
        foreach (var pageObject in _currentPage.Objects)
        {
            if (_pageObjectContainers.TryGetValue(pageObject.Id, out var container)) ObjectLayer.Children.Add(container);
        }
    }

    private bool DeleteSelectedPageObject()
    {
        if (_isReadOnly || _currentPage is null || _selectedPageObjectIds.Count == 0) return false;
        var deleting = SelectedPageObjects().Where(item => !item.IsLocked).Select(item => item.Id).ToHashSet();
        if (deleting.Count == 0) return false;
        _currentPage.Objects.RemoveAll(item => deleting.Contains(item.Id));
        foreach (var id in deleting)
        {
            if (_pageObjectContainers.Remove(id, out var container)) ObjectLayer.Children.Remove(container);
        }
        var count = deleting.Count;
        ClearPageObjectSelection();
        PageObjectChanged();
        StatusText.Text = $"已删除 {count} 个对象";
        return true;
    }

    private void PageObjectChanged()
    {
        if (_isSwitchingPage || _currentPage is null) return;
        _currentPage.ModifiedAt = DateTimeOffset.Now;
        UpdateCurrentPageThumbnail();
        MarkDirty();
    }

    private void SetPageObjectsReadOnly(bool isReadOnly)
    {
        foreach (var container in _pageObjectContainers.Values)
        {
            var linked = container.Tag is PageObject linkedObject && linkedObject.LinkTargetPageId.HasValue;
            container.IsHitTestVisible = !isReadOnly || linked;
            var isLocked = container.Tag is PageObject pageObject && pageObject.IsLocked;
            foreach (var textBox in FindVisualChildren<TextBox>(container)) textBox.IsReadOnly = isReadOnly || isLocked;
        }
        if (isReadOnly) ClearMixedSelection();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private static BitmapImage DecodeImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static Color ParseColor(string? value, Color fallback)
    {
        try { return (Color)(ColorConverter.ConvertFromString(value) ?? fallback); }
        catch { return fallback; }
    }

    private static string ColorToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    private static string WithAlpha(Color color, byte alpha) => $"#{alpha:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}




