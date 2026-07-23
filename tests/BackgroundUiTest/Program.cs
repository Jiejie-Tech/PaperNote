using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PaperNote.Desktop;
using PaperNote.Core.Ink;
using PaperNote.Core.Models;
using PaperNote.Desktop.Services;
using PaperNote.Core.Services;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var ownsApplication = Application.Current is null;
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainWindow? window = null;
        MainWindow? closeTestWindow = null;
        string? closeTestRoot = null;
        try
        {
            var imageData = CreateTestPng();
            var document = NotebookDocument.Create("后台界面测试");
            var page = document.Pages[0];
            page.PaperTemplate = "Grid";
            page.PaperColor = "#FFFBEA";
            page.BackgroundImageData = imageData;
            page.BackgroundSourceType = "PDF";
            page.BackgroundSourceName = "background-test.pdf";
            page.BackgroundPageNumber = 1;
            page.InkData = PageThumbnailService.Serialize(new StrokeCollection
            {
                new Stroke(new StylusPointCollection(new[]
                {
                    new StylusPoint(120, 160),
                    new StylusPoint(240, 220),
                    new StylusPoint(360, 170)
                }))
            });
            page.Objects.Add(new PageObject
            {
                Kind = "Text",
                X = 80,
                Y = 100,
                Width = 320,
                Height = 150,
                Text = "后台文本框验证",
                StrokeColor = "#202124"
            });
            page.Objects.Add(new PageObject
            {
                Kind = "Image",
                X = 110,
                Y = 320,
                Width = 260,
                Height = 170,
                ImageData = imageData
            });
            page.Objects.Add(new PageObject
            {
                Kind = "Shape",
                ShapeKind = "Ellipse",
                X = 450,
                Y = 180,
                Width = 220,
                Height = 170,
                StrokeColor = "#3978F6",
                FillColor = "#303978F6",
                StrokeThickness = 5
            });

            window = new MainWindow();
            SetField(window, "_currentNotebook", document);
            SetField(window, "_currentPage", page);
            Invoke(window, "LoadPage", page);

            Assert(window.FindName("RecoveryCenterButton") is Button, "The library should expose the recovery center.");
            var objectLayer = (Canvas)(window.FindName("ObjectLayer") ?? throw new InvalidOperationException("找不到隐藏对象画布。"));
            var backgroundImage = (Image)(window.FindName("PageBackgroundImage") ?? throw new InvalidOperationException("Missing background image layer."));
            Assert(backgroundImage.Source is BitmapSource, "PDF page background should load without showing a window.");
            Assert(((InkCanvas)(window.FindName("InkSurface") ?? throw new InvalidOperationException("Missing ink canvas."))).Strokes.Count == 1, "PDF background page should keep editable ink.");

            Assert(objectLayer.Children.Count == 3, "隐藏画布应恢复文本、图片和形状三个对象");
            Assert(objectLayer.Children.OfType<Border>().All(item => item.Visibility == Visibility.Visible), "页面对象应处于可渲染状态");

            objectLayer.Measure(new Size(840, 1188));
            objectLayer.Arrange(new Rect(0, 0, 840, 1188));
            objectLayer.UpdateLayout();
            var rendered = new RenderTargetBitmap(840, 1188, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(objectLayer);
            Assert(CountVisiblePixels(rendered) > 5000, "隐藏画布渲染结果不应为空白");

            var blankThumbnail = (RenderTargetBitmap)PageThumbnailService.Create([], "Grid", "#FFFBEA");
            var objectThumbnail = (RenderTargetBitmap)PageThumbnailService.Create([], "Grid", "#FFFBEA", page.Objects);
            Assert(PixelChecksum(blankThumbnail) != PixelChecksum(objectThumbnail), "对象缩略图应与空白纸缩略图不同");
            var backgroundThumbnail = (RenderTargetBitmap)PageThumbnailService.Create([], "Grid", "#FFFBEA", null, page.BackgroundImageData);
            Assert(PixelChecksum(blankThumbnail) != PixelChecksum(backgroundThumbnail), "PDF background should be included in the page thumbnail.");

            var containers = GetField<Dictionary<Guid, Border>>(window, "_pageObjectContainers");
            var shapeObject = page.Objects[2];
            Invoke(window, "SelectPageObject", shapeObject, containers[shapeObject.Id]);
            Assert((bool)(Invoke(window, "ChangeSelectedPageObjectsStrokeColor", "#E5484D") ?? false), "形状应能修改描边颜色");
            Assert((bool)(Invoke(window, "ChangeSelectedPageObjectsFillColor", "#40F4C542") ?? false), "形状应能修改填充颜色");
            Assert((bool)(Invoke(window, "ChangeSelectedShapeStrokeThickness", 8d) ?? false), "形状应能修改线宽");
            Assert((bool)(Invoke(window, "ChangeSelectedPageObjectsOpacity", 0.5d) ?? false), "形状应能修改透明度");
            Assert(shapeObject.StrokeColor == "#E5484D" && shapeObject.FillColor == "#40F4C542" && shapeObject.StrokeThickness == 8 && shapeObject.Opacity == 0.5, "形状样式应写回页面模型");
            var shapeContentHost = ((Grid)containers[shapeObject.Id].Child).Children[0] as Border;
            Assert(shapeContentHost is not null && Math.Abs(shapeContentHost.Opacity - 0.5) < 0.001, "隐藏画布应立即刷新对象透明度");
            var styledThumbnail = (RenderTargetBitmap)PageThumbnailService.Create([], "Grid", "#FFFBEA", page.Objects);
            Assert(PixelChecksum(styledThumbnail) != PixelChecksum(objectThumbnail), "对象样式变化应反映到页面缩略图");
            Assert((bool)(Invoke(window, "ResetSelectedPageObjectStyle") ?? false), "形状应能恢复默认样式");
            Assert(shapeObject.StrokeColor == PageObjectDefaults.StrokeColor && shapeObject.FillColor == PageObjectDefaults.FillColor && shapeObject.StrokeThickness == 3 && shapeObject.Opacity == 1, "恢复默认样式应重置形状属性");
            var styleMenu = (ContextMenu)(Invoke(window, "CreateObjectStylesMenu") ?? throw new InvalidOperationException("无法创建对象样式菜单。"));
            Assert(styleMenu.Items.Count >= 6, "对象样式菜单应包含颜色、填充、字号、线宽、透明度和恢复默认项");
            var actionsMenu = (ContextMenu)(Invoke(window, "CreateObjectActionsMenu") ?? throw new InvalidOperationException("无法创建对象操作菜单。"));
            Assert(actionsMenu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "对象样式")), "对象右键菜单应包含样式入口");

            var originalCount = page.Objects.Count;
            Invoke(window, "InsertText_Click", new Button(), new RoutedEventArgs());
            Assert(page.Objects.Count == originalCount + 1 && page.Objects[^1].Kind == "Text", "后台调用应能插入文本框");

            Invoke(window, "InsertShape_Click", new Button { Tag = "Rectangle" }, new RoutedEventArgs());
            Assert(page.Objects.Count == originalCount + 2 && page.Objects[^1].ShapeKind == "Rectangle", "后台调用应能插入矩形");

            var selectedObject = page.Objects[^1];
            var selectedContainer = GetField<Border>(window, "_selectedObjectContainer");
            InvokeStatic(typeof(MainWindow), "ResizePageObject", selectedObject, selectedContainer, 70d, 45d);
            Assert(selectedObject.Width > 240 && selectedObject.Height > 180, "调整大小逻辑应更新对象尺寸");

            var deleted = (bool)(Invoke(window, "DeleteSelectedPageObject") ?? false);
            Assert(deleted && page.Objects.Count == originalCount + 1, "后台删除应只删除当前选中对象");

            var firstObject = page.Objects[0];
            var secondObject = page.Objects[1];
            Invoke(window, "SelectPageObject", firstObject, containers[firstObject.Id]);
            Invoke(window, "SelectPageObject", secondObject, containers[secondObject.Id], true);
            var selectedIds = GetField<HashSet<Guid>>(window, "_selectedPageObjectIds");
            Assert(selectedIds.Count == 2, "按 Ctrl 追加选择应形成多对象选择");
            Assert((bool)(Invoke(window, "ChangeSelectedTextFontSize", 32d) ?? false), "多选时应能修改其中的文本大小");
            Assert((bool)(Invoke(window, "ChangeSelectedPageObjectsStrokeColor", "#7C5CE5") ?? false), "多选时应能修改其中的文字颜色");
            Assert((bool)(Invoke(window, "ChangeSelectedPageObjectsOpacity", 0.75d) ?? false), "多选对象应能统一修改透明度");
            Assert(firstObject.FontSize == 32 && firstObject.StrokeColor == "#7C5CE5", "文字样式应写回文本对象");
            Assert(firstObject.Opacity == 0.75 && secondObject.Opacity == 0.75, "统一透明度应应用到文本和图片");
            var textBox = FindVisualChildren<TextBox>(containers[firstObject.Id]).FirstOrDefault();
            Assert(textBox is not null && textBox.FontSize == 32, "隐藏文本框应立即刷新字号");
            Assert(textBox is not null && textBox.Foreground is SolidColorBrush textBrush && textBrush.Color == Color.FromRgb(124, 92, 229), "隐藏文本框应立即刷新文字颜色");

            Assert((bool)(Invoke(window, "GroupSelectedPageObjects") ?? false), "两个对象应能组合");
            Assert(firstObject.GroupId.HasValue && firstObject.GroupId == secondObject.GroupId, "组合对象应共享同一个组 ID");
            var originalGroupId = firstObject.GroupId;

            Assert((bool)(Invoke(window, "CopySelectedPageObjects") ?? false), "多选对象应能复制");
            var countBeforePaste = page.Objects.Count;
            Assert((bool)(Invoke(window, "PastePageObjects") ?? false), "对象剪贴板应能粘贴");
            Assert(page.Objects.Count == countBeforePaste + 2, "粘贴应新增两个对象");
            var pastedObjects = page.Objects.TakeLast(2).ToArray();
            Assert(pastedObjects.All(item => item.GroupId.HasValue && item.GroupId == pastedObjects[0].GroupId), "粘贴后的两个对象应继续保持组合");
            Assert(pastedObjects[0].GroupId != originalGroupId, "粘贴后的组合应生成新的组 ID");
            Assert(pastedObjects.All(item => page.Objects.Take(countBeforePaste).All(source => source.Id != item.Id)), "粘贴对象应生成新的对象 ID");
            Assert(pastedObjects[0].FontSize == 32 && pastedObjects.All(item => item.Opacity == 0.75), "复制粘贴应保留对象字号和透明度样式");

            Assert((bool)(Invoke(window, "SendSelectedPageObjectsToBack") ?? false), "所选对象应能置于底层");
            Assert(page.Objects.Take(2).Select(item => item.Id).SequenceEqual(pastedObjects.Select(item => item.Id)), "置于底层应保持所选对象的相对顺序");
            Assert((bool)(Invoke(window, "BringSelectedPageObjectsToFront") ?? false), "所选对象应能置于顶层");
            Assert(page.Objects.TakeLast(2).Select(item => item.Id).SequenceEqual(pastedObjects.Select(item => item.Id)), "置于顶层应保持所选对象的相对顺序");

            Assert((bool)(Invoke(window, "UngroupSelectedPageObjects") ?? false), "粘贴后的组合应能取消组合");
            Assert(pastedObjects.All(item => item.GroupId is null), "取消组合应清除组 ID");
            Assert((bool)(Invoke(window, "CutSelectedPageObjects") ?? false), "多选对象应能剪切");
            Assert(page.Objects.Count == countBeforePaste, "剪切应移除所选对象");
            Assert((bool)(Invoke(window, "PastePageObjects") ?? false), "剪切后应能再次粘贴对象");
            Assert(page.Objects.Count == countBeforePaste + 2, "剪切后粘贴应恢复对象数量");

            var newlyPastedObjects = page.Objects.TakeLast(2).ToArray();
            Invoke(window, "ClearPageObjectSelection");
            Invoke(window, "SelectPageObject", firstObject, containers[firstObject.Id]);
            Assert((bool)(Invoke(window, "UngroupSelectedPageObjects") ?? false), "原始组合应能取消，便于验证排列功能");

            var layoutObjects = new[] { page.Objects[1], page.Objects[2], page.Objects[3] };
            layoutObjects[0].X = 90; layoutObjects[0].Y = 110;
            layoutObjects[1].X = 330; layoutObjects[1].Y = 360;
            layoutObjects[2].X = 590; layoutObjects[2].Y = 650;
            foreach (var item in layoutObjects) Invoke(window, "RefreshPageObjectGeometry", item);
            Invoke(window, "ClearPageObjectSelection");
            Invoke(window, "SelectPageObject", layoutObjects[0], containers[layoutObjects[0].Id]);
            Invoke(window, "SelectPageObject", layoutObjects[1], containers[layoutObjects[1].Id], true);
            Invoke(window, "SelectPageObject", layoutObjects[2], containers[layoutObjects[2].Id], true);
            Assert((bool)(Invoke(window, "AlignSelectedPageObjectsLeft") ?? false), "三个对象应能左对齐");
            Assert(layoutObjects.Select(item => item.X).Distinct().Count() == 1, "左对齐后 X 坐标应一致");

            layoutObjects[0].X = 70; layoutObjects[1].X = 320; layoutObjects[2].X = 610;
            Assert((bool)(Invoke(window, "AlignSelectedPageObjectsHorizontalCenter") ?? false), "三个对象应能水平居中对齐");
            Assert(layoutObjects.Select(item => Math.Round(item.X + item.Width / 2, 3)).Distinct().Count() == 1, "水平居中后中心点应一致");
            layoutObjects[0].X = 60; layoutObjects[1].X = 300; layoutObjects[2].X = 580;
            Assert((bool)(Invoke(window, "AlignSelectedPageObjectsRight") ?? false), "三个对象应能右对齐");
            Assert(layoutObjects.Select(item => Math.Round(item.X + item.Width, 3)).Distinct().Count() == 1, "右对齐后右边界应一致");

            layoutObjects[0].Y = 90; layoutObjects[1].Y = 360; layoutObjects[2].Y = 700;
            Assert((bool)(Invoke(window, "AlignSelectedPageObjectsTop") ?? false), "三个对象应能顶部对齐");
            Assert(layoutObjects.Select(item => item.Y).Distinct().Count() == 1, "顶部对齐后 Y 坐标应一致");
            layoutObjects[0].Y = 80; layoutObjects[1].Y = 350; layoutObjects[2].Y = 720;
            Assert((bool)(Invoke(window, "AlignSelectedPageObjectsVerticalCenter") ?? false), "三个对象应能垂直居中对齐");
            Assert(layoutObjects.Select(item => Math.Round(item.Y + item.Height / 2, 3)).Distinct().Count() == 1, "垂直居中后中心点应一致");
            layoutObjects[0].Y = 80; layoutObjects[1].Y = 350; layoutObjects[2].Y = 720;
            Assert((bool)(Invoke(window, "AlignSelectedPageObjectsBottom") ?? false), "三个对象应能底部对齐");
            Assert(layoutObjects.Select(item => Math.Round(item.Y + item.Height, 3)).Distinct().Count() == 1, "底部对齐后底边界应一致");

            layoutObjects[0].X = 70; layoutObjects[1].X = 210; layoutObjects[2].X = 500;
            Assert((bool)(Invoke(window, "DistributeSelectedPageObjectsHorizontally") ?? false), "三个对象应能水平等距分布");
            var horizontalCenters = layoutObjects.OrderBy(item => item.X + item.Width / 2).Select(item => item.X + item.Width / 2).ToArray();
            Assert(Math.Abs((horizontalCenters[1] - horizontalCenters[0]) - (horizontalCenters[2] - horizontalCenters[1])) < 0.001, "水平中心点间距应一致");
            layoutObjects[0].Y = 80; layoutObjects[1].Y = 240; layoutObjects[2].Y = 800;
            Assert((bool)(Invoke(window, "DistributeSelectedPageObjectsVertically") ?? false), "三个对象应能垂直等距分布");
            var verticalCenters = layoutObjects.OrderBy(item => item.Y + item.Height / 2).Select(item => item.Y + item.Height / 2).ToArray();
            Assert(Math.Abs((verticalCenters[1] - verticalCenters[0]) - (verticalCenters[2] - verticalCenters[1])) < 0.001, "垂直中心点间距应一致");

            var preRotationThumbnail = (RenderTargetBitmap)PageThumbnailService.Create([], "Grid", "#FFFBEA", page.Objects);
            Assert((bool)(Invoke(window, "RotateSelectedPageObjectsRight") ?? false), "多选对象应能右转 90 度");
            Assert(layoutObjects.All(item => item.Rotation == 90), "右转后旋转角度应写回模型");
            Assert(((Grid)containers[layoutObjects[0].Id].Child).Children[0] is Border { RenderTransform: RotateTransform { Angle: 90 } }, "隐藏画布应立即刷新旋转效果");
            var rotatedThumbnail = (RenderTargetBitmap)PageThumbnailService.Create([], "Grid", "#FFFBEA", page.Objects);
            Assert(PixelChecksum(preRotationThumbnail) != PixelChecksum(rotatedThumbnail), "旋转应反映到页面缩略图");
            Assert((bool)(Invoke(window, "RotateSelectedPageObjectsLeft") ?? false) && layoutObjects.All(item => item.Rotation == 0), "左转应恢复对象角度");

            var arrangeMenu = (ContextMenu)(Invoke(window, "CreateObjectArrangeMenu") ?? throw new InvalidOperationException("无法创建排列菜单。"));
            Assert(MenuContainsHeader(arrangeMenu.Items, "对齐") && MenuContainsHeader(arrangeMenu.Items, "水平等距分布") && MenuContainsHeader(arrangeMenu.Items, "锁定"), "排列菜单应包含对齐、分布和锁定入口");
            actionsMenu = (ContextMenu)(Invoke(window, "CreateObjectActionsMenu") ?? throw new InvalidOperationException("无法刷新对象操作菜单。"));
            Assert(MenuContainsHeader(actionsMenu.Items, "排列与锁定"), "对象右键菜单应包含排列与锁定入口");

            Invoke(window, "ClearPageObjectSelection");
            var layerSelection = page.Objects.Skip(1).Take(2).ToArray();
            Invoke(window, "SelectPageObject", layerSelection[0], containers[layerSelection[0].Id]);
            Invoke(window, "SelectPageObject", layerSelection[1], containers[layerSelection[1].Id], true);
            var originalLayerOrder = page.Objects.Select(item => item.Id).ToArray();
            Assert((bool)(Invoke(window, "BringSelectedPageObjectsForward") ?? false), "多选对象应能上移一层");
            Assert(page.Objects.IndexOf(layerSelection[0]) == 2 && page.Objects.IndexOf(layerSelection[1]) == 3, "上移一层应保持多选相对顺序");
            Assert((bool)(Invoke(window, "SendSelectedPageObjectsBackward") ?? false), "多选对象应能下移一层");
            Assert(page.Objects.Select(item => item.Id).SequenceEqual(originalLayerOrder), "下移一层应恢复原顺序并保持多选相对顺序");

            Invoke(window, "ClearPageObjectSelection");
            var lockObject = page.Objects.First(item => item.Kind == "Text");
            Invoke(window, "SelectPageObject", lockObject, containers[lockObject.Id]);
            Assert((bool)(Invoke(window, "LockSelectedPageObjects") ?? false) && lockObject.IsLocked, "文本对象应能锁定");
            var lockedContainer = containers[lockObject.Id];
            var lockedTextBox = FindVisualChildren<TextBox>(lockedContainer).FirstOrDefault();
            Assert(lockedTextBox is not null && lockedTextBox.IsReadOnly, "锁定文本框应进入只读状态");
            Assert(FindVisualChildren<Thumb>(lockedContainer).All(item => item.Visibility == Visibility.Collapsed || !item.IsEnabled), "锁定对象应隐藏或禁用缩放手柄");
            var lockedWidth = lockObject.Width;
            InvokeStatic(typeof(MainWindow), "ResizePageObject", lockObject, lockedContainer, 80d, 60d);
            Assert(lockObject.Width == lockedWidth, "锁定对象不应被缩放");
            Assert(!(bool)(Invoke(window, "ChangeSelectedPageObjectsOpacity", 0.25d) ?? true), "锁定对象不应被修改样式");
            Assert(!(bool)(Invoke(window, "RotateSelectedPageObjectsRight") ?? true), "锁定对象不应被旋转");
            var lockedCount = page.Objects.Count;
            Assert(!(bool)(Invoke(window, "DeleteSelectedPageObject") ?? true) && page.Objects.Count == lockedCount, "锁定对象不应被删除");
            Assert((bool)(Invoke(window, "UnlockSelectedPageObjects") ?? false) && !lockObject.IsLocked, "锁定对象应能解锁");
            var unlockedTextBox = FindVisualChildren<TextBox>(containers[lockObject.Id]).FirstOrDefault();
            Assert(unlockedTextBox is not null && !unlockedTextBox.IsReadOnly, "解锁后文本框应恢复编辑");
            Assert((bool)(Invoke(window, "ChangeSelectedPageObjectsOpacity", 0.9d) ?? false), "解锁后应恢复样式编辑");
            Invoke(window, "ToggleReadOnly_Click", new Button(), new RoutedEventArgs());
            var readOnlyCount = page.Objects.Count;
            Invoke(window, "InsertShape_Click", new Button { Tag = "Ellipse" }, new RoutedEventArgs());
            Assert(page.Objects.Count == readOnlyCount, "只读模式应阻止插入对象");
            Assert(!(bool)(Invoke(window, "ChangeSelectedPageObjectsOpacity", 0.25d) ?? true), "只读模式应阻止对象样式修改");
            Assert(objectLayer.Children.OfType<Border>().All(item => !item.IsHitTestVisible), "只读模式应禁用对象交互");

            Invoke(window, "ToggleReadOnly_Click", new Button(), new RoutedEventArgs());
            Assert(objectLayer.Children.OfType<Border>().All(item => item.IsHitTestVisible), "退出只读模式应恢复对象交互");

            var lassoPage = new NotebookPage();
            lassoPage.InkData = PageThumbnailService.Serialize(new StrokeCollection
            {
                new Stroke(new StylusPointCollection(new[] { new StylusPoint(90, 90), new StylusPoint(180, 180) })),
                new Stroke(new StylusPointCollection(new[] { new StylusPoint(600, 800), new StylusPoint(720, 920) }))
            });
            var lassoInsideObject = new PageObject { Kind = "Shape", X = 110, Y = 110, Width = 100, Height = 80 };
            var lassoOutsideObject = new PageObject { Kind = "Text", X = 620, Y = 820, Width = 120, Height = 90, Text = "outside" };
            lassoPage.Objects.Add(lassoInsideObject);
            lassoPage.Objects.Add(lassoOutsideObject);
            SetField(window, "_currentPage", lassoPage);
            Invoke(window, "LoadPage", lassoPage);
            var lassoInkSurface = (InkCanvas)(window.FindName("InkSurface") ?? throw new InvalidOperationException("Missing ink canvas."));
            var lassoLockedLayer = new PageLayer { Name = "Locked lasso layer", IsLocked = true };
            lassoPage.Layers.Add(lassoLockedLayer);
            WpfInkAdapter.SetLayerId(lassoInkSurface.Strokes[1], lassoLockedLayer.Id);
            SetField(window, "_activeTool", "Select");
            Invoke(window, "ApplyMixedLassoSelection", new List<Point>
            {
                new(50, 50), new(280, 50), new(280, 280), new(50, 280), new(50, 50)
            }, false);
            Assert(lassoInkSurface.GetSelectedStrokes().Count == 1, "Region mixed lasso should hit only the ink inside the polygon.");
            Assert(GetField<HashSet<Guid>>(window, "_selectedPageObjectIds").SetEquals(new[] { lassoInsideObject.Id }), "Region mixed lasso should hit the page object inside the polygon.");
            SetField(window, "_mixedSelectionFilter", PageSelectionFilter.Objects);
            Invoke(window, "ApplyMixedLassoSelection", new List<Point>
            {
                new(50, 50), new(280, 50), new(280, 280), new(50, 280), new(50, 50)
            }, false);
            Assert(lassoInkSurface.GetSelectedStrokes().Count == 0 && GetField<HashSet<Guid>>(window, "_selectedPageObjectIds").SetEquals(new[] { lassoInsideObject.Id }), "Object-only lasso filtering should exclude ink.");
            SetField(window, "_mixedSelectionFilter", PageSelectionFilter.Highlighter);
            Invoke(window, "ApplyMixedLassoSelection", new List<Point>
            {
                new(50, 50), new(280, 50), new(280, 280), new(50, 280), new(50, 50)
            }, false);
            Assert(lassoInkSurface.GetSelectedStrokes().Count == 0 && GetField<HashSet<Guid>>(window, "_selectedPageObjectIds").Count == 0, "Highlighter-only lasso filtering should exclude pen ink and objects.");
            SetField(window, "_mixedSelectionFilter", PageSelectionFilter.All);
            Invoke(window, "ApplyMixedLassoSelection", new List<Point>
            {
                new(50, 50), new(280, 50), new(280, 280), new(50, 280), new(50, 50)
            }, false);
            Invoke(window, "ApplyMixedLassoSelection", new List<Point>
            {
                new(560, 760), new(800, 760), new(800, 1000), new(560, 1000), new(560, 760)
            }, true);
            Assert(lassoInkSurface.GetSelectedStrokes().Count == 1 && GetField<HashSet<Guid>>(window, "_selectedPageObjectIds").Count == 2, "Ctrl-style additive lasso should keep editable content while excluding ink on locked layers.");

            lassoOutsideObject.IsLocked = true;
            var inkOrigin = lassoInkSurface.GetSelectionBounds();
            var insideMoveX = lassoInsideObject.X;
            var insideMoveY = lassoInsideObject.Y;
            var lockedMoveX = lassoOutsideObject.X;
            var lockedMoveY = lassoOutsideObject.Y;
            Invoke(window, "BeginMixedSelectionEdit", CreateSelectionEditingArgs(inkOrigin, new Rect(inkOrigin.X + 30, inkOrigin.Y + 20, inkOrigin.Width, inkOrigin.Height)), false);
            Assert(Math.Abs(lassoInsideObject.X - (insideMoveX + 30)) < .001 && Math.Abs(lassoInsideObject.Y - (insideMoveY + 20)) < .001, "Mixed drag should move editable objects by the same delta as selected ink.");
            Assert(Math.Abs(lassoOutsideObject.X - lockedMoveX) < .001 && Math.Abs(lassoOutsideObject.Y - lockedMoveY) < .001, "Mixed drag must leave locked objects unchanged.");
            Invoke(window, "EndMixedSelectionEdit");

            var insideResizeOrigin = lassoInsideObject.Clone();
            var lockedResizeOrigin = lassoOutsideObject.Clone();
            Invoke(window, "BeginMixedSelectionEdit", CreateSelectionEditingArgs(inkOrigin, new Rect(inkOrigin.X, inkOrigin.Y, inkOrigin.Width * 1.5, inkOrigin.Height * 2)), true);
            Assert(Math.Abs(lassoInsideObject.Width - insideResizeOrigin.Width * 1.5) < .001 && Math.Abs(lassoInsideObject.Height - insideResizeOrigin.Height * 2) < .001, "Mixed resize should scale editable object dimensions with selected ink.");
            Assert(Math.Abs(lassoOutsideObject.Width - lockedResizeOrigin.Width) < .001 && Math.Abs(lassoOutsideObject.Height - lockedResizeOrigin.Height) < .001, "Mixed resize must leave locked objects unchanged.");
            Invoke(window, "EndMixedSelectionEdit");
            Invoke(window, "ClearMixedSelection");

            SetField(window, "_currentPage", page);
            Invoke(window, "LoadPage", page);
            var mixedInkSurface = (InkCanvas)(window.FindName("InkSurface") ?? throw new InvalidOperationException("Missing ink canvas."));
            var mixedLockObject = page.Objects.First();
            mixedLockObject.IsLocked = true;
            Invoke(window, "RefreshPageObjectStyle", mixedLockObject);
            SetField(window, "_activeTool", "Select");
            Assert((bool)(Invoke(window, "SelectAllInkAndPageObjects") ?? false), "Mixed select-all should select page ink and objects together.");
            Assert(mixedInkSurface.GetSelectedStrokes().Count == mixedInkSurface.Strokes.Count && GetField<HashSet<Guid>>(window, "_selectedPageObjectIds").Count == page.Objects.Count, "Mixed select-all counts are incorrect.");
            Assert(window.FindName("SelectionActionsButton") is Button, "The Windows toolbar should expose mixed selection actions.");
            var selectionMenu = (ContextMenu)(Invoke(window, "CreateSelectionActionsMenu") ?? throw new InvalidOperationException("Missing mixed selection menu."));
            Assert(MenuContainsHeader(selectionMenu.Items, "全部内容") && MenuContainsHeader(selectionMenu.Items, "仅钢笔") && MenuContainsHeader(selectionMenu.Items, "仅荧光笔") && MenuContainsHeader(selectionMenu.Items, "仅文字") && MenuContainsHeader(selectionMenu.Items, "仅图片") && MenuContainsHeader(selectionMenu.Items, "仅形状"), "Mixed selection menu should expose all ink and object filters.");
            Assert(MenuContainsHeader(selectionMenu.Items, "设置透明度") && MenuContainsHeader(selectionMenu.Items, "设置笔迹粗细") && MenuContainsHeader(selectionMenu.Items, "设置笔迹类型") && MenuContainsHeader(selectionMenu.Items, "复制到其他页面") && MenuContainsHeader(selectionMenu.Items, "移动到其他页面"), "Mixed selection menu should expose batch style and cross-page actions.");
            Assert(MenuContainsHeader(selectionMenu.Items, "导出选区为 PNG…"), "Mixed selection menu should expose local PNG export.");

            var penSettingsMenu = (ContextMenu)(Invoke(window, "BuildPenSettingsMenu") ?? throw new InvalidOperationException("Missing pen settings menu."));
            Assert(MenuContainsHeader(penSettingsMenu.Items, "直线/形状自动规整"), "Pen settings should expose geometry assistance.");

            var playbackStrokeId = WpfInkAdapter.GetStrokeId(mixedInkSurface.Strokes[0]);
            page.AudioRecordings.Add(new AudioRecording
            {
                DisplayName = "后台录音",
                DurationMilliseconds = 4_000,
                WaveformPeaks = [0, .25f, .8f, .4f, 1],
                Cues = [new AudioCue { OffsetMilliseconds = 500, StrokeId = playbackStrokeId, Label = "书写" }]
            });
            var audioMenu = (ContextMenu)(Invoke(window, "BuildAudioTimelineMenu") ?? throw new InvalidOperationException("Missing audio timeline menu."));
            Assert(MenuContainsHeader(audioMenu.Items, "波形与跳转") && MenuContainsHeader(audioMenu.Items, "跳到 25%") && MenuContainsHeader(audioMenu.Items, "时间标记（1）"), "Audio menu should expose waveform, percentage jumps, and cue timeline without mojibake.");
            Invoke(window, "UpdateAudioPlaybackHighlight", playbackStrokeId);
            var audioHighlight = (System.Windows.Shapes.Polyline)(window.FindName("AudioPlaybackHighlight") ?? throw new InvalidOperationException("Missing audio playback highlight."));
            Assert(audioHighlight.Visibility == Visibility.Visible && audioHighlight.Points.Count >= 2, "Audio cue should highlight its linked stroke.");
            Invoke(window, "UpdateAudioPlaybackHighlight", new object?[] { null });
            Assert(audioHighlight.Visibility == Visibility.Collapsed && audioHighlight.Points.Count == 0, "Stopping audio highlight should clear the overlay.");
            var objectsBeforeMixedDelete = page.Objects.Count;
            Assert((bool)(Invoke(window, "DeleteMixedSelection") ?? false), "Mixed delete should remove selected ink and editable objects in one operation.");
            Assert(mixedInkSurface.Strokes.Count == 0 && page.Objects.Count == 1 && page.Objects[0].Id == mixedLockObject.Id, "Mixed delete should retain the locked object only.");
            Assert(objectsBeforeMixedDelete > page.Objects.Count, "Mixed delete should remove unlocked objects.");
            Invoke(window, "ClearMixedSelection");
            Assert(mixedInkSurface.GetSelectedStrokes().Count == 0 && GetField<HashSet<Guid>>(window, "_selectedPageObjectIds").Count == 0, "Escape-equivalent mixed clear should clear ink and object selections.");

            var invalidImagePage = new NotebookPage { BackgroundImageData = "not-base64", BackgroundSourceType = "PDF", BackgroundPageNumber = 2 };
            invalidImagePage.Objects.Add(new PageObject { Kind = "Image", ImageData = "not-base64" });
            SetField(window, "_currentPage", invalidImagePage);
            Invoke(window, "LoadPage", invalidImagePage);
            Assert(objectLayer.Children.Count == 1, "损坏图片对象不应阻止页面加载");
            var fallbackText = FindVisualChildren<TextBlock>(objectLayer).FirstOrDefault(item => item.Text == "图片无法显示");
            Assert(fallbackText is not null, "损坏图片应显示安全占位提示");
            Assert(backgroundImage.Source is null, "Corrupted PDF background data should safely fall back without blocking page load.");

            var importDocument = NotebookDocument.Create("PDF insertion test");
            SetField(window, "_currentNotebook", importDocument);
            SetField(window, "_currentPage", importDocument.Pages[0]);
            Invoke(window, "LoadPage", importDocument.Pages[0]);
            Invoke(window, "InsertImportedPdfPages", new ImportedPdfPage[]
            {
                new(1, imageData),
                new(2, imageData)
            }, "two-pages.pdf");
            Assert(importDocument.Pages.Count == 3, "UI PDF insertion should add all rendered pages after the current page.");
            Assert(importDocument.Pages[1].BackgroundSourceType == "PDF" && importDocument.Pages[1].BackgroundSourceName == "two-pages.pdf" && importDocument.Pages[1].BackgroundPageNumber == 1, "First inserted PDF page metadata is incorrect.");
            Assert(importDocument.Pages[2].BackgroundPageNumber == 2 && importDocument.CurrentPageId == importDocument.Pages[1].Id, "UI PDF insertion should keep order and activate the first inserted page.");
            Assert(backgroundImage.Source is BitmapSource, "The first inserted PDF page should be visible on the hidden background layer.");
            Assert((bool)(Invoke(window, "RotateCurrentPdfPage", 90) ?? false) && importDocument.Pages[1].BackgroundRotation == 90, "PDF page rotation should update the current imported page.");
            Assert((bool)(Invoke(window, "SetCurrentPdfCrop", 0.1d) ?? false) && importDocument.Pages[1].BackgroundCropLeft == 0.1d && importDocument.Pages[1].BackgroundCropBottom == 0.1d, "PDF page crop should update all four crop edges.");
            Assert(backgroundImage.Source is BitmapSource, "Rotated and cropped PDF background should remain visible in hidden WPF.");

            Assert((bool)(Invoke(window, "OpenCustomPdfCropEditor") ?? false), "Custom PDF crop editor should open for an editable PDF page.");
            var cropOverlay = (Border)(window.FindName("PdfCropEditorOverlay") ?? throw new InvalidOperationException("Missing PDF crop editor overlay."));
            var cropLeftSlider = (Slider)(window.FindName("PdfCropLeftSlider") ?? throw new InvalidOperationException("Missing PDF crop left slider."));
            var cropTopSlider = (Slider)(window.FindName("PdfCropTopSlider") ?? throw new InvalidOperationException("Missing PDF crop top slider."));
            Assert(cropOverlay.Visibility == Visibility.Visible && cropLeftSlider.Value == 10 && cropTopSlider.Value == 10, "Crop editor should reflect the current crop values without showing a window.");
            Assert((bool)(Invoke(window, "SetPendingPdfCropEdge", "Left", 0.12d) ?? false), "Custom crop should accept a left edge value.");
            Assert((bool)(Invoke(window, "SetPendingPdfCropEdge", "Top", 0.08d) ?? false), "Custom crop should accept a top edge value.");
            Assert((bool)(Invoke(window, "SetPendingPdfCropEdge", "Right", 0.18d) ?? false), "Custom crop should accept a right edge value.");
            Assert((bool)(Invoke(window, "SetPendingPdfCropEdge", "Bottom", 0.06d) ?? false), "Custom crop should accept a bottom edge value.");
            Invoke(window, "ApplyPdfCropEditor_Click", window, new RoutedEventArgs());
            Assert(cropOverlay.Visibility == Visibility.Collapsed, "Applying custom crop should close the editor.");
            Assert(importDocument.Pages[1].BackgroundCropLeft == 0.12d && importDocument.Pages[1].BackgroundCropTop == 0.08d && importDocument.Pages[1].BackgroundCropRight == 0.18d && importDocument.Pages[1].BackgroundCropBottom == 0.06d, "Custom crop should preserve four independent edge values.");

            Assert((bool)(Invoke(window, "OpenCustomPdfCropEditor") ?? false), "Custom crop editor should reopen.");
            Assert((bool)(Invoke(window, "SetPendingPdfCropEdge", "Left", 0.9d) ?? false), "Oversized custom crop input should be normalized.");
            Assert(cropLeftSlider.Value == 45, "Each custom crop edge should be limited to 45 percent.");
            Invoke(window, "ClosePdfCropEditor");
            Assert(importDocument.Pages[1].BackgroundCropLeft == 0.12d, "Cancelling custom crop must not modify the page model.");

            Invoke(window, "ShowPdfExportOptions", importDocument.Pages.Take(2).ToArray(), "前两页");
            var exportOptionsOverlay = (Border)(window.FindName("PdfExportOptionsOverlay") ?? throw new InvalidOperationException("Missing PDF export options overlay."));
            var exportScopeText = (TextBlock)(window.FindName("PdfExportOptionsScopeText") ?? throw new InvalidOperationException("Missing PDF export scope text."));
            var exportQualityCombo = (ComboBox)(window.FindName("PdfExportQualityCombo") ?? throw new InvalidOperationException("Missing PDF export quality combo."));
            var exportPageSizeCombo = (ComboBox)(window.FindName("PdfExportPageSizeCombo") ?? throw new InvalidOperationException("Missing PDF export page-size combo."));
            Assert(exportOptionsOverlay.Visibility == Visibility.Visible && exportScopeText.Text.Contains("前两页") && exportScopeText.Text.Contains("2 页"), "PDF export options should show the selected scope and page count.");
            exportQualityCombo.SelectedIndex = 2;
            exportPageSizeCombo.SelectedIndex = 2;
            var selectedExportOptions = (PdfExportOptions)(Invoke(window, "GetSelectedPdfExportOptions") ?? throw new InvalidOperationException("Missing selected PDF export options."));
            Assert(selectedExportOptions.Quality == "High" && selectedExportOptions.PageSize == "Letter", "PDF export option selection should map to High and Letter.");
            var a4StandardSpec = PdfExportService.GetPageSpec(new PdfExportOptions { Quality = "Standard", PageSize = "A4" });
            var letterHighSpec = PdfExportService.GetPageSpec(selectedExportOptions);
            Assert(a4StandardSpec.Width == 595 && a4StandardSpec.Height == 842 && a4StandardSpec.PixelWidth == 840 && a4StandardSpec.PixelHeight == 1188, "A4 standard PDF export specification is incorrect.");
            Assert(letterHighSpec.Width == 612 && letterHighSpec.Height == 792 && letterHighSpec.PixelWidth == 1680 && letterHighSpec.PixelHeight == 2376, "Letter high-quality PDF export specification is incorrect.");
            Invoke(window, "ClosePdfExportOptions");
            Assert(exportOptionsOverlay.Visibility == Visibility.Collapsed, "Cancelling PDF export options should close the overlay.");

            var batchIds = new HashSet<Guid> { importDocument.Pages[1].Id, importDocument.Pages[2].Id };
            importDocument.Pages[0].Title = "课程封面";
            importDocument.Pages[1].Title = "第一章 概念";
            importDocument.Pages[2].Title = "第二章 练习";
            importDocument.Pages[2].Objects.Add(new PageObject { Kind = "Text", Text = "线性代数重点内容" });
            Assert((bool)(Invoke(window, "SetPagesBookmarked", batchIds, true) ?? false), "Batch bookmark action should bookmark selected pages.");
            Assert(importDocument.Pages.Skip(1).All(item => item.IsBookmarked), "Selected pages should keep bookmark state in the model.");
            var originalFirstId = importDocument.Pages[0].Id;
            Assert((bool)(Invoke(window, "MovePages", batchIds, -1) ?? false), "Batch page move-up should move the selected block.");
            Assert(importDocument.Pages[0].Id == batchIds.First() || batchIds.Contains(importDocument.Pages[0].Id), "Batch page move-up should place a selected page first.");
            Assert(importDocument.Pages[2].Id == originalFirstId, "Batch page move-up should keep selected pages in order and move the unselected page down.");
            Assert((bool)(Invoke(window, "MovePages", batchIds, 1) ?? false), "Batch page move-down should move the selected block back.");
            Assert(importDocument.Pages[0].Id == originalFirstId, "Batch page move-down should restore the leading unselected page.");

            Assert((bool)(Invoke(window, "RotatePdfPages", batchIds, -90) ?? false), "Batch PDF rotation should update all selected PDF pages.");
            Assert(importDocument.Pages[1].BackgroundRotation == 0 && importDocument.Pages[2].BackgroundRotation == 270, "Batch PDF rotation values are incorrect.");
            Assert((bool)(Invoke(window, "SetPdfPagesCrop", batchIds, 0.05d) ?? false), "Batch PDF crop should update selected PDF pages.");
            Assert(importDocument.Pages.Skip(1).Take(2).All(item => item.BackgroundCropLeft == 0.05d && item.BackgroundCropBottom == 0.05d), "Batch PDF crop should set all four edges.");

            Assert((bool)(Invoke(window, "DuplicatePages", batchIds) ?? false), "Batch page duplication should duplicate all selected pages.");
            Assert(importDocument.Pages.Count == 5, "Batch page duplication should add two pages.");
            var copiedIds = importDocument.Pages.Select(item => item.Id).Where(id => id != originalFirstId && !batchIds.Contains(id)).ToHashSet();
            Assert(copiedIds.Count == 2, "Batch page duplication should generate new page IDs.");
            Assert(importDocument.Pages.Where(item => copiedIds.Contains(item.Id)).All(item => item.BackgroundSourceType == "PDF" && item.BackgroundCropLeft == 0.05d), "Batch page copies should preserve PDF background transforms.");
            Assert(importDocument.Pages.Where(item => copiedIds.Contains(item.Id)).All(item => item.IsBookmarked && !string.IsNullOrWhiteSpace(item.Title)), "Batch page copies should preserve page titles and bookmarks.");
            Assert((bool)(Invoke(window, "DeletePages", copiedIds) ?? false), "Batch page deletion should delete all copied pages.");
            Assert(importDocument.Pages.Count == 3 && importDocument.Pages.Select(item => item.Id).ToHashSet().IsSupersetOf(batchIds), "Batch page deletion should keep unselected pages.");
            Assert(!(bool)(Invoke(window, "DeletePages", importDocument.Pages.Select(item => item.Id).ToHashSet()) ?? true), "Batch page deletion must keep at least one page.");
            Assert((bool)(Invoke(window, "ResetPdfPagesTransform", batchIds) ?? false) && importDocument.Pages.Skip(1).All(item => item.BackgroundRotation == 0 && item.BackgroundCropLeft == 0), "Batch PDF reset should restore original transforms.");

            var batchMenu = (ContextMenu)(Invoke(window, "CreateBatchPageActionsMenu", batchIds) ?? throw new InvalidOperationException("Missing page batch actions menu."));
            Assert(MenuContainsHeader(batchMenu.Items, "复制所选页面") && MenuContainsHeader(batchMenu.Items, "移至笔记开头") && MenuContainsHeader(batchMenu.Items, "移至笔记末尾"), "Batch page menu should expose duplicate and boundary move actions.");
            Assert(MenuContainsHeader(batchMenu.Items, "按所选页面导出 PDF…") && MenuContainsHeader(batchMenu.Items, "提取为新笔记本…") && MenuContainsHeader(batchMenu.Items, "删除所选页面"), "Batch page menu should expose range export, notebook extraction and deletion.");

            importDocument.Pages[1].PdfText = "offline eigenvalue PDF lesson";
            importDocument.Pages[1].OutlineLevel = 1;
            importDocument.Pages[1].PdfLinks = [new PdfPageLink { TargetPageId = importDocument.Pages[2].Id, TargetSourcePageNumber = 3, Label = "下一章" }];
            PageAnnotationService.AddComment(importDocument.Pages[1], "重点公式", "#F0B429");
            importDocument.Pages[1].Ink.Strokes.Add(new PaperInkStroke { Tool = PaperInkTool.Highlighter, Color = "#F0B429", Points = [new() { X = 10, Y = 10 }] });
            importDocument.OutlineEntries.Add(new DocumentOutlineEntry { Title = "PDF 原目录", Level = 2, TargetPageId = importDocument.Pages[2].Id, SourcePageNumber = 3, IsImported = true });
            SetField(window, "_currentPage", importDocument.Pages[1]);
            Invoke(window, "LoadPage", importDocument.Pages[1]);
            Invoke(window, "OpenPdfStudy");
            var pdfStudyOverlay = (Border)(window.FindName("PdfStudyOverlay") ?? throw new InvalidOperationException("Missing PDF study overlay."));
            var pdfStudySearchBox = (TextBox)(window.FindName("PdfStudySearchBox") ?? throw new InvalidOperationException("Missing PDF study search box."));
            var pdfStudyResults = (ListBox)(window.FindName("PdfStudyResultsList") ?? throw new InvalidOperationException("Missing PDF study results."));
            var pdfStudyOutline = (ListBox)(window.FindName("PdfStudyOutlineList") ?? throw new InvalidOperationException("Missing PDF study outline."));
            var pdfStudyLinks = (ListBox)(window.FindName("PdfStudyLinksList") ?? throw new InvalidOperationException("Missing PDF study links."));
            var pdfStudyAnnotations = (ListBox)(window.FindName("PdfStudyAnnotationList") ?? throw new InvalidOperationException("Missing PDF study annotations."));
            var pdfStudyKind = (ComboBox)(window.FindName("PdfStudyAnnotationKind") ?? throw new InvalidOperationException("Missing PDF study annotation kind filter."));
            var pdfStudyColor = (ComboBox)(window.FindName("PdfStudyAnnotationColor") ?? throw new InvalidOperationException("Missing PDF study annotation color filter."));
            var pdfStudyCommentBox = (TextBox)(window.FindName("PdfStudyCommentBox") ?? throw new InvalidOperationException("Missing PDF study comment box."));
            Assert(pdfStudyOverlay.Visibility == Visibility.Visible && window.FindName("PdfStudyCloseButton") is Button, "PDF study center should open and expose a close button without showing the window.");
            pdfStudySearchBox.Text = "eigenvalue";
            Assert(pdfStudyResults.Items.Count == 1 && pdfStudyOutline.Items.Count >= 2 && pdfStudyLinks.Items.Count == 1, "PDF study center should populate search, merged outline and internal links.");
            Assert(pdfStudyAnnotations.Items.Count >= 2, "PDF study center should list comments and ink annotations.");
            var commentsBeforeAdd = importDocument.Pages[1].Comments.Count;
            pdfStudyCommentBox.Text = "后台添加评论";
            Invoke(window, "PdfStudyAddComment_Click", window, new RoutedEventArgs());
            Assert(importDocument.Pages[1].Comments.Count == commentsBeforeAdd + 1 && pdfStudyCommentBox.Text.Length == 0, "PDF study center should add a local text comment.");
            pdfStudyKind.SelectedIndex = 1;
            pdfStudyColor.SelectedIndex = 1;
            Assert(pdfStudyAnnotations.Items.Count == importDocument.Pages[1].Comments.Count, "PDF study annotation filters should combine type and color.");
            pdfStudyAnnotations.SelectedIndex = 0;
            Invoke(window, "PdfStudyDeleteComment_Click", window, new RoutedEventArgs());
            Assert(importDocument.Pages[1].Comments.Count == commentsBeforeAdd, "PDF study center should delete the selected text comment.");
            pdfStudyKind.SelectedIndex = 0;
            pdfStudyColor.SelectedIndex = 0;
            pdfStudyLinks.SelectedIndex = 0;
            Assert((bool)(Invoke(window, "JumpToPdfStudyLink") ?? false) && importDocument.CurrentPageId == importDocument.Pages[2].Id && pdfStudyOverlay.Visibility == Visibility.Collapsed, "PDF internal link should jump to an imported target page and close the study center.");
            Invoke(window, "OpenPdfStudy");
            Invoke(window, "ClosePdfStudy");
            Assert(pdfStudyOverlay.Visibility == Visibility.Collapsed, "PDF study center should close cleanly through the same path used by the close button and Escape shortcut.");

            var pageTitleBox = (TextBox)(window.FindName("PageTitleBox") ?? throw new InvalidOperationException("Missing page title box."));
            var pageSearchBox = (TextBox)(window.FindName("PageSearchBox") ?? throw new InvalidOperationException("Missing page search box."));
            var bookmarkToggle = (ToggleButton)(window.FindName("BookmarkedPagesOnlyToggle") ?? throw new InvalidOperationException("Missing bookmark filter."));
            var pageListBox = (ListBox)(window.FindName("PageListBox") ?? throw new InvalidOperationException("Missing page list."));
            Invoke(window, "LoadPage", importDocument.Pages[1]);
            Assert(pageTitleBox.Text == "第一章 概念", "Loading a page should show its title without displaying a window.");
            pageTitleBox.Text = "重点公式";
            Assert(importDocument.Pages[1].Title == "重点公式", "Editing the hidden title box should update the current page title.");

            pageSearchBox.Text = "重点公式";
            Assert(pageListBox.Items.Count == 1, "Page title search should return the matching page only.");
            pageSearchBox.Text = "线性代数";
            Assert(pageListBox.Items.Count == 1, "Page text-object search should find page contents.");
            var hiddenCurrentSelection = (HashSet<Guid>)(Invoke(window, "GetSelectedPageIds") ?? throw new InvalidOperationException("Missing filtered selection result."));
            Assert(hiddenCurrentSelection.Count == 0, "A filtered-out current page must not be treated as an invisible batch selection.");

            pageSearchBox.Text = string.Empty;
            bookmarkToggle.IsChecked = true;
            Invoke(window, "BookmarkedPagesOnlyToggle_Click", bookmarkToggle, new RoutedEventArgs());
            Assert(pageListBox.Items.Count == 2, "Bookmark filter should show both bookmarked PDF pages.");
            Assert((bool)(Invoke(window, "SetPagesBookmarked", new HashSet<Guid> { importDocument.Pages[2].Id }, false) ?? false), "Batch unbookmark should update selected pages.");
            Assert(pageListBox.Items.Count == 1 && !importDocument.Pages[2].IsBookmarked, "Bookmark-only results should refresh after unbookmarking.");
            bookmarkToggle.IsChecked = false;
            Invoke(window, "BookmarkedPagesOnlyToggle_Click", bookmarkToggle, new RoutedEventArgs());
            Assert(pageListBox.Items.Count == 3, "Clearing page filters should restore all pages.");

            var cachedThumbnail1 = (ImageSource)(Invoke(window, "GetPageThumbnail", importDocument.Pages[0], 150, 212) ?? throw new InvalidOperationException("Missing cached thumbnail."));
            var cachedThumbnail2 = (ImageSource)(Invoke(window, "GetPageThumbnail", importDocument.Pages[0], 150, 212) ?? throw new InvalidOperationException("Missing repeated thumbnail."));
            Assert(ReferenceEquals(cachedThumbnail1, cachedThumbnail2), "Repeated thumbnail requests should reuse the same cached image.");
            importDocument.Pages[0].ModifiedAt = importDocument.Pages[0].ModifiedAt.AddTicks(1);
            var refreshedThumbnail = (ImageSource)(Invoke(window, "GetPageThumbnail", importDocument.Pages[0], 150, 212) ?? throw new InvalidOperationException("Missing refreshed thumbnail."));
            Assert(!ReferenceEquals(cachedThumbnail1, refreshedThumbnail), "Changing a page should invalidate its cached thumbnail.");

            var overviewOverlay = (Border)(window.FindName("PageOverviewOverlay") ?? throw new InvalidOperationException("Missing page overview overlay."));
            var overviewSearchBox = (TextBox)(window.FindName("PageOverviewSearchBox") ?? throw new InvalidOperationException("Missing overview search box."));
            var overviewBookmarkToggle = (ToggleButton)(window.FindName("PageOverviewBookmarkedOnlyToggle") ?? throw new InvalidOperationException("Missing overview bookmark filter."));
            var overviewListBox = (ListBox)(window.FindName("PageOverviewListBox") ?? throw new InvalidOperationException("Missing overview list."));
            Invoke(window, "OpenPageOverview");
            Assert(overviewOverlay.Visibility == Visibility.Visible && overviewListBox.Items.Count == 3, "Hidden page overview should list every page without showing a window.");
            overviewSearchBox.Text = "线性代数";
            Assert(overviewListBox.Items.Count == 1, "Overview search should find text inside a page.");
            overviewSearchBox.Text = string.Empty;
            overviewBookmarkToggle.IsChecked = true;
            Invoke(window, "PageOverviewBookmarkedOnlyToggle_Click", overviewBookmarkToggle, new RoutedEventArgs());
            Assert(overviewListBox.Items.Count == 1, "Overview bookmark filter should show the remaining bookmarked page.");
            overviewBookmarkToggle.IsChecked = false;
            Invoke(window, "PageOverviewBookmarkedOnlyToggle_Click", overviewBookmarkToggle, new RoutedEventArgs());
            Assert(overviewListBox.Items.Count == 3, "Clearing overview filters should restore all pages.");
            overviewListBox.SelectedItems.Clear();
            overviewListBox.SelectedItems.Add(overviewListBox.Items[1]);
            overviewListBox.SelectedItems.Add(overviewListBox.Items[2]);
            var overviewSelection = (HashSet<Guid>)(Invoke(window, "GetSelectedPageIds") ?? throw new InvalidOperationException("Missing overview selection."));
            Assert(overviewSelection.Count == 2, "Overview Ctrl/Shift-style selection should feed batch page operations.");

            Assert((bool)(Invoke(window, "NavigateToPage", 3, true) ?? false), "Overview should jump to a selected page.");
            Assert(overviewOverlay.Visibility == Visibility.Collapsed && importDocument.CurrentPageId == importDocument.Pages[2].Id, "Overview jump should close the overlay and activate page 3.");
            Assert(!(bool)(Invoke(window, "NavigateToPage", 99, false) ?? true), "Out-of-range page numbers should be rejected.");
            Assert((bool)(Invoke(window, "NavigateRelativePage", -1) ?? false) && importDocument.CurrentPageId == importDocument.Pages[1].Id, "Previous-page navigation should activate page 2.");
            Assert((bool)(Invoke(window, "NavigateRelativePage", 1) ?? false) && importDocument.CurrentPageId == importDocument.Pages[2].Id, "Next-page navigation should activate page 3.");

            var pageJumpBox = (TextBox)(window.FindName("PageJumpBox") ?? throw new InvalidOperationException("Missing quick page jump box."));
            pageJumpBox.Text = "1";
            Assert((bool)(Invoke(window, "NavigateFromPageJumpBox") ?? false) && importDocument.CurrentPageId == importDocument.Pages[0].Id, "Quick page jump should activate the requested page.");
            pageSearchBox.Text = "线性代数";
            Assert((bool)(Invoke(window, "NavigateToPage", 1, false) ?? false), "Jumping to the already active page should remain valid.");
            Assert(pageSearchBox.Text == "线性代数", "Jumping to the current page should not unnecessarily clear a filter.");
            Assert((bool)(Invoke(window, "NavigateToPage", 2, false) ?? false), "Jumping to a filtered-out page should succeed.");
            Assert(pageSearchBox.Text.Length == 0 && pageListBox.Items.Count == 3, "Quick jump should clear a filter that hides the destination page.");

            var insertionDocument = NotebookDocument.Create("Blank page insertion test");
            insertionDocument.Pages[0].PaperTemplate = "Grid";
            insertionDocument.Pages[0].PaperColor = "#FFFBEA";
            SetField(window, "_currentNotebook", insertionDocument);
            SetField(window, "_currentPage", insertionDocument.Pages[0]);
            Invoke(window, "LoadPage", insertionDocument.Pages[0]);
            Assert((bool)(Invoke(window, "InsertBlankPage", "Dotted", "#FFFBEA", "AfterCurrent") ?? false), "Blank page insertion should add after the current page.");
            Assert(insertionDocument.Pages.Count == 2 && insertionDocument.Pages[1].PaperTemplate == "Dotted" && insertionDocument.CurrentPageId == insertionDocument.Pages[1].Id, "After-current insertion should activate the new dotted page.");
            Assert((bool)(Invoke(window, "InsertBlankPage", "Lined", "#F2F7FF", "BeforeCurrent") ?? false), "Blank page insertion should add before the current page.");
            Assert(insertionDocument.Pages.Count == 3 && insertionDocument.Pages[1].PaperTemplate == "Lined" && insertionDocument.Pages[1].PaperColor == "#F2F7FF", "Before-current insertion should preserve template and paper color.");
            Assert((bool)(Invoke(window, "InsertBlankPage", "Blank", "#FFFFFF", "End") ?? false), "Blank page insertion should add at the notebook end.");
            Assert(insertionDocument.Pages.Count == 4 && insertionDocument.Pages[^1].PaperTemplate == "Blank" && insertionDocument.CurrentPageId == insertionDocument.Pages[^1].Id, "End insertion should append and activate a blank page.");
            SetField(window, "_isReadOnly", true);
            Assert(!(bool)(Invoke(window, "InsertBlankPage", "Grid", "#FFFFFF", "AfterCurrent") ?? true) && insertionDocument.Pages.Count == 4, "Read-only mode should reject blank page insertion.");
            SetField(window, "_isReadOnly", false);

            Assert(PdfImportService.ParsePageSelection("1-3,5,8-10", 10).SequenceEqual(new[] { 1, 2, 3, 5, 8, 9, 10 }), "PDF range parser should expand mixed ranges.");
            Assert(PdfImportService.ParsePageSelection("1，2；2,4", 5).SequenceEqual(new[] { 1, 2, 4 }), "PDF range parser should support Chinese separators and remove duplicates.");
            Invoke(window, "ShowPdfImportOptions", "three-pages.pdf", 10);
            var importOptionsOverlay = (Border)(window.FindName("PdfImportOptionsOverlay") ?? throw new InvalidOperationException("Missing PDF import options overlay."));
            var importRangeBox = (TextBox)(window.FindName("PdfImportRangeBox") ?? throw new InvalidOperationException("Missing PDF range box."));
            var importRangeHint = (TextBlock)(window.FindName("PdfImportRangeHintText") ?? throw new InvalidOperationException("Missing PDF range hint."));
            var importContinueButton = (Button)(window.FindName("PdfImportContinueButton") ?? throw new InvalidOperationException("Missing PDF import continue button."));
            var importProgressPanel = (StackPanel)(window.FindName("PdfImportProgressPanel") ?? throw new InvalidOperationException("Missing PDF import progress panel."));
            var importProgressBar = (ProgressBar)(window.FindName("PdfImportProgressBar") ?? throw new InvalidOperationException("Missing PDF import progress bar."));
            var importCancelButton = (Button)(window.FindName("PdfImportCancelButton") ?? throw new InvalidOperationException("Missing PDF import cancel button."));
            Assert(importOptionsOverlay.Visibility == Visibility.Visible && importRangeBox.Text == "1-10" && importContinueButton.IsEnabled, "PDF import options should open invisibly with a valid default range.");
            importRangeBox.Text = "3-1";
            Assert(!importContinueButton.IsEnabled && importRangeHint.Text.Contains("无法识别"), "Invalid PDF ranges should disable the continue button.");
            Invoke(window, "PdfImportPreset_Click", new Button { Tag = "Odd" }, new RoutedEventArgs());
            Assert(importRangeBox.Text == "1,3,5,7,9" && importContinueButton.IsEnabled, "Odd-page PDF preset should update and validate the range.");
            Invoke(window, "PdfImportPreset_Click", new Button { Tag = "Even" }, new RoutedEventArgs());
            Assert(importRangeBox.Text == "2,4,6,8,10", "Even-page PDF preset should update the range.");
            Invoke(window, "SetPdfImportBusy", true);
            Assert(importProgressPanel.Visibility == Visibility.Visible && !importRangeBox.IsEnabled && !importContinueButton.IsEnabled && Equals(importCancelButton.Content, "取消导入"), "PDF import busy state should expose progress and cancellation without blocking the hidden UI test.");
            importProgressBar.Value = 0.5;
            Invoke(window, "SetPdfImportBusy", false);
            Assert(importProgressPanel.Visibility == Visibility.Collapsed && importRangeBox.IsEnabled && importCancelButton.IsEnabled, "PDF import busy state should restore controls after completion or cancellation.");
            Invoke(window, "ClosePdfImportOptions");
            Assert(importOptionsOverlay.Visibility == Visibility.Collapsed, "PDF import options should close without showing a window.");

            var placementDocument = NotebookDocument.Create("PDF placement test");
            placementDocument.Pages.Add(new NotebookPage { Title = "Second" });
            SetField(window, "_currentNotebook", placementDocument);
            SetField(window, "_currentPage", placementDocument.Pages[1]);
            Invoke(window, "LoadPage", placementDocument.Pages[1]);
            Invoke(window, "InsertImportedPdfPages", new ImportedPdfPage[] { new(4, imageData) }, "before.pdf", "BeforeCurrent");
            Assert(placementDocument.Pages.Count == 3 && placementDocument.Pages[1].BackgroundPageNumber == 4, "PDF pages should insert before the current page.");
            Invoke(window, "InsertImportedPdfPages", new ImportedPdfPage[] { new(5, imageData) }, "end.pdf", "End");
            Assert(placementDocument.Pages.Count == 4 && placementDocument.Pages[^1].BackgroundPageNumber == 5, "PDF pages should append at the notebook end.");
            SetField(window, "_isReadOnly", true);
            Invoke(window, "InsertImportedPdfPages", new ImportedPdfPage[] { new(6, imageData) }, "blocked.pdf", "AfterCurrent");
            Assert(placementDocument.Pages.Count == 4, "Read-only mode should reject PDF insertion.");
            SetField(window, "_isReadOnly", false);

            var presetDocument = NotebookDocument.Create("Paper preset test");
            presetDocument.Pages[0].PaperTemplate = "Grid";
            presetDocument.Pages[0].PaperColor = "#FFFBEA";
            SetField(window, "_currentNotebook", presetDocument);
            SetField(window, "_currentPage", presetDocument.Pages[0]);
            Invoke(window, "LoadPage", presetDocument.Pages[0]);
            Assert((bool)(Invoke(window, "AddCurrentPaperPreset") ?? false), "Current paper should be added to favorites.");
            Assert(!(bool)(Invoke(window, "AddCurrentPaperPreset") ?? true) && presetDocument.PaperPresets.Count == 1, "Duplicate paper favorites should not be added.");
            for (var index = 1; index < 8; index++)
            {
                presetDocument.Pages[0].PaperTemplate = index % 2 == 0 ? "Lined" : "Dotted";
                presetDocument.Pages[0].PaperColor = $"#{index:X2}{index + 20:X2}{index + 40:X2}";
                Assert((bool)(Invoke(window, "AddCurrentPaperPreset") ?? false), "Distinct paper favorite should be added.");
            }
            presetDocument.Pages[0].PaperTemplate = "Blank";
            presetDocument.Pages[0].PaperColor = "#ABCDEF";
            Assert(!(bool)(Invoke(window, "AddCurrentPaperPreset") ?? true) && presetDocument.PaperPresets.Count == 8, "Paper favorites should be limited to eight.");
            var favoriteToUse = presetDocument.PaperPresets[0];
            var favoriteItem = (MenuItem)(Invoke(window, "CreatePaperPresetMenuItem", favoriteToUse, "AfterCurrent") ?? throw new InvalidOperationException("Missing paper favorite menu item."));
            favoriteItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert(presetDocument.Pages.Count == 2 && presetDocument.Pages[1].PaperTemplate == favoriteToUse.PaperTemplate && presetDocument.Pages[1].PaperColor == favoriteToUse.PaperColor, "Paper favorite should insert a page with the saved template and color.");
            SetField(window, "_isReadOnly", true);
            Assert(!(bool)(Invoke(window, "RemovePaperPreset", favoriteToUse.Id) ?? true) && presetDocument.PaperPresets.Count == 8, "Read-only mode should reject favorite deletion.");
            SetField(window, "_isReadOnly", false);
            Assert((bool)(Invoke(window, "RemovePaperPreset", favoriteToUse.Id) ?? false) && presetDocument.PaperPresets.Count == 7, "Paper favorite should be removable.");

            var outlineDocument = NotebookDocument.Create("Outline test");
            outlineDocument.Pages[0].Title = "绪论";
            outlineDocument.Pages.Add(new NotebookPage { Title = "线性代数" });
            outlineDocument.Pages.Add(new NotebookPage { Title = "矩阵分解" });
            outlineDocument.Pages.Add(new NotebookPage { Title = string.Empty, OutlineLevel = 1 });
            SetField(window, "_currentNotebook", outlineDocument);
            SetField(window, "_currentPage", outlineDocument.Pages[0]);
            Invoke(window, "LoadPage", outlineDocument.Pages[0]);
            Assert((bool)(Invoke(window, "SetCurrentPageOutlineLevel", 1) ?? false), "Current page should become a level-one outline entry.");
            SetField(window, "_currentPage", outlineDocument.Pages[1]);
            Invoke(window, "LoadPage", outlineDocument.Pages[1]);
            Assert((bool)(Invoke(window, "SetCurrentPageOutlineLevel", 2) ?? false), "Second page should become a level-two outline entry.");
            SetField(window, "_currentPage", outlineDocument.Pages[2]);
            Invoke(window, "LoadPage", outlineDocument.Pages[2]);
            Assert((bool)(Invoke(window, "SetCurrentPageOutlineLevel", 3) ?? false), "Third page should become a level-three outline entry.");
            Invoke(window, "OpenOutline");
            var outlineOverlay = (Border)(window.FindName("OutlineOverlay") ?? throw new InvalidOperationException("Missing outline overlay."));
            var outlineSearchBox = (TextBox)(window.FindName("OutlineSearchBox") ?? throw new InvalidOperationException("Missing outline search box."));
            var outlineListBox = (ListBox)(window.FindName("OutlineListBox") ?? throw new InvalidOperationException("Missing outline list."));
            var outlineButton = (Button)(window.FindName("CurrentPageOutlineButton") ?? throw new InvalidOperationException("Missing current outline button."));
            Assert(outlineOverlay.Visibility == Visibility.Visible && outlineListBox.Items.Count == 4 && Equals(outlineButton.Content, "三级⌄"), "Outline overlay should list only outlined pages and reflect the current level.");
            var untitledOutlineEntry = outlineListBox.Items.Cast<object>().Single(item => GetProperty<Guid>(item, "TargetPageId") == outlineDocument.Pages[3].Id);
            Assert(GetProperty<string>(untitledOutlineEntry, "Title") == "第 4 页", "Untitled outline page should use the readable page-number fallback.");
            outlineSearchBox.Text = "线性";
            Assert(outlineListBox.Items.Count == 1 && GetProperty<int>(outlineListBox.Items[0], "PageNumber") == 2, "Outline search should filter by title.");
            outlineSearchBox.Text = string.Empty;
            var movedOutlinePage = outlineDocument.Pages[2];
            outlineDocument.Pages.RemoveAt(2);
            outlineDocument.Pages.Insert(0, movedOutlinePage);
            Invoke(window, "RefreshPageItems", movedOutlinePage.Id, null);
            var movedOutlineEntry = outlineListBox.Items.Cast<object>().Single(item => GetProperty<Guid>(item, "Id") == movedOutlinePage.Id);
            Assert(GetProperty<int>(movedOutlineEntry, "PageNumber") == 1, "Outline page numbers should refresh after reordering.");
            outlineListBox.SelectedItem = movedOutlineEntry;
            Assert((bool)(Invoke(window, "JumpToSelectedOutlineEntry") ?? false), "Outline entry should jump to its page.");
            Assert(outlineOverlay.Visibility == Visibility.Collapsed && outlineDocument.CurrentPageId == movedOutlinePage.Id, "Outline jump should close the overlay and activate the page.");
            SetField(window, "_isReadOnly", true);
            Assert(!(bool)(Invoke(window, "SetCurrentPageOutlineLevel", 0) ?? true) && movedOutlinePage.OutlineLevel == 3, "Read-only mode should reject outline changes.");
            SetField(window, "_isReadOnly", false);
            Assert((bool)(Invoke(window, "SetCurrentPageOutlineLevel", 0) ?? false) && movedOutlinePage.OutlineLevel == 0, "Outlined page should be removable from the directory.");

            var navigationDocument = NotebookDocument.Create("Navigation test");
            navigationDocument.Pages[0].Title = "起点";
            navigationDocument.Pages.Add(new NotebookPage { Title = "第二页" });
            navigationDocument.Pages.Add(new NotebookPage { Title = "链接目标" });
            var linkedObject = new PageObject { Kind = "Text", Text = "跳转到第三页", X = 100, Y = 120, Width = 240, Height = 100 };
            navigationDocument.Pages[0].Objects.Add(linkedObject);
            SetField(window, "_currentNotebook", navigationDocument);
            SetField(window, "_currentPage", navigationDocument.Pages[0]);
            Invoke(window, "LoadPage", navigationDocument.Pages[0]);
            Invoke(window, "RefreshPageItems", navigationDocument.Pages[0].Id, null);
            Invoke(window, "ResetPageVisitHistory");
            Assert((bool)(Invoke(window, "NavigateToPage", 2, false) ?? false) && navigationDocument.CurrentPageId == navigationDocument.Pages[1].Id, "Page navigation should activate the second page.");
            Assert(GetField<List<Guid>>(window, "_pageBackHistory").SequenceEqual(new[] { navigationDocument.Pages[0].Id }), "Navigation history should remember the previous page.");
            Invoke(window, "NavigatePageHistory", true);
            Assert(navigationDocument.CurrentPageId == navigationDocument.Pages[0].Id && GetField<List<Guid>>(window, "_pageForwardHistory").Count == 1, "Back navigation should return to the previous visited page and populate forward history.");

            var linkContainers = GetField<Dictionary<Guid, Border>>(window, "_pageObjectContainers");
            Invoke(window, "SelectPageObject", linkedObject, linkContainers[linkedObject.Id]);
            Assert((bool)(Invoke(window, "SetSelectedPageObjectLink", navigationDocument.Pages[2].Id) ?? false), "Selected page object should accept an internal page link.");
            Assert(linkedObject.LinkTargetPageId == navigationDocument.Pages[2].Id, "Internal page link target should be stored on the object.");
            linkContainers = GetField<Dictionary<Guid, Border>>(window, "_pageObjectContainers");
            Assert(FindVisualChildren<TextBlock>(linkContainers[linkedObject.Id]).Any(item => item.Text == "↗"), "Linked object should display a link badge.");
            var linkMenu = (ContextMenu)(Invoke(window, "CreateObjectActionsMenu") ?? throw new InvalidOperationException("Missing object actions menu."));
            Assert(MenuContainsHeader(linkMenu.Items, "页面链接"), "Object actions menu should expose page links.");
            SetField(window, "_isReadOnly", true);
            Invoke(window, "SetPageObjectsReadOnly", true);
            Assert(linkContainers[linkedObject.Id].IsHitTestVisible, "Linked objects should remain clickable in read-only mode.");
            SetField(window, "_isReadOnly", false);
            Invoke(window, "SetPageObjectsReadOnly", false);

            var fountainAttributes = (DrawingAttributes)(InvokeStatic(typeof(MainWindow), "CreatePenDrawingAttributes", Colors.Black, 4d, "Fountain", "Standard", "Standard") ?? throw new InvalidOperationException("Missing fountain attributes."));
            var ballpointAttributes = (DrawingAttributes)(InvokeStatic(typeof(MainWindow), "CreatePenDrawingAttributes", Colors.Black, 2.2d, "Ballpoint", "Off", "Standard") ?? throw new InvalidOperationException("Missing ballpoint attributes."));
            var pencilAttributes = (DrawingAttributes)(InvokeStatic(typeof(MainWindow), "CreatePenDrawingAttributes", Colors.Black, 3d, "Pencil", "Soft", "Strong") ?? throw new InvalidOperationException("Missing pencil attributes."));
            Assert(fountainAttributes.StylusTip == StylusTip.Rectangle && !fountainAttributes.IgnorePressure && fountainAttributes.FitToCurve, "Fountain pen should use an angled pressure-sensitive nib with curve fitting.");
            Assert(ballpointAttributes.StylusTip == StylusTip.Ellipse && ballpointAttributes.IgnorePressure, "Ballpoint pen should use a stable pressure-independent round nib.");
            Assert(pencilAttributes.Color.A < 255 && pencilAttributes.FitToCurve, "Pencil should use a translucent smoothed stroke.");
            var profiledStroke = new Stroke(new StylusPointCollection(new[]
            {
                new StylusPoint(0, 0, 0.2f),
                new StylusPoint(10, 12, 0.45f),
                new StylusPoint(20, 0, 0.8f),
                new StylusPoint(30, 10, 0.55f)
            }));
            var originalMiddleY = profiledStroke.StylusPoints[1].Y;
            var originalPressure = profiledStroke.StylusPoints[1].PressureFactor;
            InvokeStatic(typeof(MainWindow), "ApplyStrokeProfile", profiledStroke, "Strong", "Strong", "Brush");
            Assert(profiledStroke.StylusPoints[1].Y != originalMiddleY && profiledStroke.StylusPoints[1].PressureFactor < originalPressure, "Strong smoothing and pressure curves should transform collected stylus points.");
            Assert((bool)(Invoke(window, "ApplyInkPreset", "RedReview") ?? false), "Quick pen presets should be applicable without visible UI interaction.");
            var penSettingsButton = (Button)(window.FindName("PenSettingsButton") ?? throw new InvalidOperationException("Missing pen settings button."));
            var thicknessSlider = (Slider)(window.FindName("ThicknessSlider") ?? throw new InvalidOperationException("Missing thickness slider."));
            Assert(Equals(penSettingsButton.Content, "圆珠笔⌄") && Math.Abs(thicknessSlider.Value - 3.8) < 0.01, "Red review preset should update the visible pen profile and thickness controls.");

            var imagePaperPage = new NotebookPage { PaperTemplate = "Grid", PaperColor = "#FFFBEA" };
            InvokeStatic(typeof(MainWindow), "ApplyImagePaper", imagePaperPage, ("paper.png", imageData));
            Assert(imagePaperPage.BackgroundSourceType == "Image" && imagePaperPage.PaperTemplate == "Blank", "Applying image paper should store the image source and switch to blank paper.");
            Assert(imagePaperPage.BackgroundSourceName == "paper.png" && imagePaperPage.BackgroundPageNumber == 0, "Image paper metadata should retain the source name and use page number zero.");
            Assert(PageBackgroundService.CreateImageSource(imagePaperPage) is BitmapSource, "Image paper should render through the background service without a visible window.");
            Assert(window.FindName("ImagePaperActionsButton") is Button { Content: "图片纸张⌄" }, "The paper toolbar should expose the image paper entry.");

            var contentNotebook = NotebookDocument.Create("高等数学课堂笔记");
            contentNotebook.FolderName = "大一课程";
            contentNotebook.Pages[0].Title = "极限与连续";
            contentNotebook.Pages[0].BackgroundSourceType = "PDF";
            contentNotebook.Pages[0].BackgroundSourceName = "微积分讲义.pdf";
            contentNotebook.Pages[0].BackgroundPageNumber = 8;
            contentNotebook.Pages[0].Objects.Add(new PageObject { Kind = "Text", Text = "洛必达法则 例题整理" });
            var linkedPage = new NotebookPage { Title = "导数复习" };
            contentNotebook.Pages.Add(linkedPage);
            contentNotebook.Pages[0].Objects.Add(new PageObject { Kind = "Shape", ShapeKind = "Arrow", LinkTargetPageId = linkedPage.Id });
            Assert(NotebookContentService.TryMatch(contentNotebook, "高等数学", out var titleMatch) && titleMatch == "笔记本标题", "Library search should match notebook titles.");
            Assert(NotebookContentService.TryMatch(contentNotebook, "大一课程", out var folderMatch) && folderMatch == "文件夹", "Library search should match folders.");
            Assert(NotebookContentService.TryMatch(contentNotebook, "极限", out var pageTitleMatch) && pageTitleMatch.Contains("标题"), "Library search should match page titles.");
            Assert(NotebookContentService.TryMatch(contentNotebook, "洛必达", out var textMatch) && textMatch.Contains("文字"), "Library search should match editable page text.");
            Assert(NotebookContentService.TryMatch(contentNotebook, "微积分讲义", out var sourceMatch) && sourceMatch.Contains("来源"), "Library search should match source filenames.");
            Assert(NotebookContentService.TryMatch(contentNotebook, "高等数学 洛必达 微积分", out _), "Library search should require and support multiple terms across fields.");
            Assert(!NotebookContentService.TryMatch(contentNotebook, "不存在的内容", out _), "Library search should reject missing content.");
            var pagePlainText = NotebookContentService.BuildPagePlainText(contentNotebook, contentNotebook.Pages[0]);
            var notebookPlainText = NotebookContentService.BuildNotebookPlainText(contentNotebook);
            Assert(pagePlainText.Contains("洛必达法则") && pagePlainText.Contains("页面链接：第 2 页 · 导数复习") && pagePlainText.Contains("PDF 来源：微积分讲义.pdf · 原第 8 页"), "Page text extraction should include editable text, internal links, and source metadata.");
            Assert(notebookPlainText.Contains("高等数学课堂笔记") && notebookPlainText.Contains("文件夹：大一课程") && notebookPlainText.Contains("共 2 页"), "Notebook text extraction should include notebook metadata and all pages.");

            var triangle = InvokeStatic(typeof(MainWindow), "CreateShape", new PageObject { Kind = "Shape", ShapeKind = "Triangle", StrokeThickness = 4 }) as System.Windows.Shapes.Path;
            var diamond = InvokeStatic(typeof(MainWindow), "CreateShape", new PageObject { Kind = "Shape", ShapeKind = "Diamond", StrokeThickness = 4 }) as System.Windows.Shapes.Path;
            var arrow = InvokeStatic(typeof(MainWindow), "CreateShape", new PageObject { Kind = "Shape", ShapeKind = "Arrow", StrokeThickness = 4 }) as System.Windows.Shapes.Path;
            var rounded = InvokeStatic(typeof(MainWindow), "CreateShape", new PageObject { Kind = "Shape", ShapeKind = "RoundedRectangle", StrokeThickness = 4 }) as System.Windows.Shapes.Rectangle;
            Assert(triangle?.Data is not null && diamond?.Data is not null && arrow?.Data is not null, "Triangle, diamond, and arrow should use visible path geometry.");
            Assert(rounded is { RadiusX: > 0, RadiusY: > 0 }, "Rounded rectangle should retain rounded corners.");
            var shapeCanvas = new Canvas { Width = 840, Height = 300, Background = Brushes.White };
            var shapeElements = new FrameworkElement[] { triangle!, diamond!, arrow!, rounded! };
            for (var index = 0; index < shapeElements.Length; index++)
            {
                shapeElements[index].Width = 170;
                shapeElements[index].Height = 120;
                Canvas.SetLeft(shapeElements[index], 20 + index * 200);
                Canvas.SetTop(shapeElements[index], 70);
                shapeCanvas.Children.Add(shapeElements[index]);
            }
            shapeCanvas.Measure(new Size(840, 300));
            shapeCanvas.Arrange(new Rect(0, 0, 840, 300));
            shapeCanvas.UpdateLayout();
            var shapeRender = new RenderTargetBitmap(840, 300, 96, 96, PixelFormats.Pbgra32);
            shapeRender.Render(shapeCanvas);
            Assert(CountVisiblePixels(shapeRender) > 100000, "New shapes should render in the hidden WPF canvas.");

            Assert(window.FindName("LibrarySearchBox") is TextBox, "The library should expose whole-library search.");
            Assert(window.FindName("LibrarySortCombo") is ComboBox, "The library should expose sorting.");
            Assert(window.FindName("LibraryBackupButton") is Button, "The library should expose backup-package actions.");
            Assert(window.FindName("ShapeActionsButton") is Button, "The editor should expose the expanded shape menu.");
            Assert(window.FindName("TextActionsButton") is Button, "The editor should expose text extraction actions.");
            Assert(window.FindName("SharedPaperTemplatesButton") is Button, "The editor should expose shared paper templates.");

            Invoke(window, "RegisterOpenNotebookTab", "C:\\temp\\alpha.papernote", "Alpha");
            Invoke(window, "RegisterOpenNotebookTab", "C:\\temp\\beta.papernote", "Beta");
            var openTabs = (System.Collections.ICollection)GetField<object>(window, "_openNotebookTabs");
            var tabsButton = (Button)(window.FindName("NotebookTabsButton") ?? throw new InvalidOperationException("Missing notebook tabs button."));
            var tabStrip = (StackPanel)(window.FindName("NotebookTabStripPanel") ?? throw new InvalidOperationException("Missing visible notebook tab strip."));
            Assert(openTabs.Count == 2 && Equals(tabsButton.Content, "标签页 2"), "Notebook quick tabs should keep multiple open notebooks and update the button.");
            Assert(tabStrip.Children.Count == 2, "The visible notebook tab strip should render one tab for each open notebook.");

            closeTestRoot = Path.Combine(Path.GetTempPath(), "PaperNote-BackgroundClose-" + Guid.NewGuid().ToString("N"));
            var closeNotebooks = Path.Combine(closeTestRoot, "Notebooks");
            var closeBackups = Path.Combine(closeTestRoot, "Backups");
            var closeWorkspacePath = Path.Combine(closeTestRoot, "workspace-state.json");
            var closeStorage = new NotebookStorageService(closeNotebooks, closeBackups);
            var closeWorkspace = new WorkspaceStateService(closeWorkspacePath);
            var closeDocument = NotebookDocument.Create("关闭前保存测试");
            var createTask = closeStorage.CreateAsync(closeDocument);
            PumpDispatcherUntil(() => createTask.IsCompleted, TimeSpan.FromSeconds(10), "创建关闭测试笔记本超时");
            var closeStoredNotebook = createTask.GetAwaiter().GetResult();

            closeTestWindow = new MainWindow(closeStorage, closeWorkspace, skipStartupInitialization: true)
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                Opacity = 0,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000
            };
            var closeCompleted = false;
            closeTestWindow.Closed += (_, _) => closeCompleted = true;
            closeTestWindow.Show();
            SetField(closeTestWindow, "_currentNotebook", closeDocument);
            SetField(closeTestWindow, "_currentPage", closeDocument.Pages[0]);
            SetField(closeTestWindow, "_currentNotebookPath", closeStoredNotebook.FilePath);
            Invoke(closeTestWindow, "LoadPage", closeDocument.Pages[0]);
            ((TextBox)(closeTestWindow.FindName("NotebookTitleBox") ?? throw new InvalidOperationException("Missing notebook title box."))).Text = "关闭保存验证";
            SetField(closeTestWindow, "_revision", 1L);
            SetField(closeTestWindow, "_isDirty", true);

            closeTestWindow.Close();
            closeTestWindow.Close();
            PumpDispatcherUntil(() => closeCompleted, TimeSpan.FromSeconds(10), "窗口关闭流程超时，可能发生界面线程死锁");
            Assert(closeCompleted, "Hidden window should close after asynchronous save completes.");
            Assert(!GetField<DispatcherTimer>(closeTestWindow, "_autosaveTimer").IsEnabled, "Autosave timer should stop after the window closes.");
            var loadAfterCloseTask = closeStorage.LoadAsync(closeStoredNotebook.FilePath);
            PumpDispatcherUntil(() => loadAfterCloseTask.IsCompleted, TimeSpan.FromSeconds(10), "读取关闭后笔记本超时");
            var savedAfterClose = loadAfterCloseTask.GetAwaiter().GetResult();
            Assert(savedAfterClose.Title == "关闭保存验证", "Window close should persist unsaved notebook changes before exiting.");
            Assert(File.Exists(closeWorkspacePath), "Window close should persist workspace state without blocking the UI thread.");

            Console.WriteLine("BACKGROUND WPF UI TEST PASS");
            Console.WriteLine("隐藏 WPF 的恢复中心、整库搜索、PDF 文本搜索与目录、内部链接跳转、批注筛选与文字评论、页面批量菜单、选区 PNG、几何辅助、录音波形与笔迹高亮、文字提取、更多形状、可见标签条、书架排序、资料库备份入口、共享模板、多笔型、压感曲线、笔迹平滑、PDF 工具、页面导航、数据容错，以及异步关闭保存均通过");
        }
        finally
        {
            if (closeTestWindow is not null && closeTestWindow.IsLoaded)
            {
                try
                {
                    SetField(closeTestWindow, "_allowClose", true);
                    closeTestWindow.Close();
                }
                catch { }
            }
            if (window is not null)
            {
                GetField<DispatcherTimer>(window, "_autosaveTimer").Stop();
                SetField(window, "_isDirty", false);
            }
            if (ownsApplication) application.Shutdown();
            if (!string.IsNullOrWhiteSpace(closeTestRoot) && Directory.Exists(closeTestRoot))
            {
                Directory.Delete(closeTestRoot, recursive: true);
            }
        }
    }

    private static void PumpDispatcherUntil(Func<bool> condition, TimeSpan timeout, string timeoutMessage)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed >= timeout) throw new TimeoutException(timeoutMessage);
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(15)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
    }

    private static string CreateTestPng()
    {
        const int width = 48;
        const int height = 32;
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 45;
            pixels[index + 1] = 115;
            pixels[index + 2] = 230;
            pixels[index + 3] = 255;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return Convert.ToBase64String(stream.ToArray());
    }

    private static long CountVisiblePixels(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        long count = 0;
        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] > 0) count++;
        }
        return count;
    }

    private static ulong PixelChecksum(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        ulong hash = 1469598103934665603;
        foreach (var value in pixels)
        {
            hash ^= value;
            hash *= 1099511628211;
        }
        return hash;
    }

    private static InkCanvasSelectionEditingEventArgs CreateSelectionEditingArgs(Rect oldRectangle, Rect newRectangle)
    {
        var constructor = typeof(InkCanvasSelectionEditingEventArgs).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Rect), typeof(Rect)],
            modifiers: null) ?? throw new MissingMethodException(typeof(InkCanvasSelectionEditingEventArgs).FullName, ".ctor(Rect, Rect)");
        return (InkCanvasSelectionEditingEventArgs)constructor.Invoke([oldRectangle, newRectangle]);
    }

    private static object? Invoke(object target, string methodName, params object?[] arguments)
    {
        var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(candidate => candidate.Name == methodName)
            .ToArray();
        static bool ParametersMatch(ParameterInfo[] parameters, object?[] values)
            => values.Select((value, index) => value is null
                ? !parameters[index].ParameterType.IsValueType || Nullable.GetUnderlyingType(parameters[index].ParameterType) is not null
                : parameters[index].ParameterType.IsInstanceOfType(value)).All(matches => matches);
        var method = methods.SingleOrDefault(candidate => candidate.GetParameters().Length == arguments.Length && ParametersMatch(candidate.GetParameters(), arguments))
            ?? methods.SingleOrDefault(candidate => candidate.GetParameters().Length > arguments.Length
                && candidate.GetParameters().Skip(arguments.Length).All(parameter => parameter.IsOptional)
                && ParametersMatch(candidate.GetParameters(), arguments))
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        var parameters = method.GetParameters();
        if (parameters.Length == arguments.Length) return method.Invoke(target, arguments);
        var expanded = new object?[parameters.Length];
        Array.Copy(arguments, expanded, arguments.Length);
        for (var index = arguments.Length; index < expanded.Length; index++) expanded[index] = Type.Missing;
        return method.Invoke(target, expanded);
    }

    private static object? InvokeStatic(Type type, string methodName, params object?[] arguments)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(type.FullName, methodName);
        return method.Invoke(null, arguments);
    }

    private static void SetField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, fieldName);
        field.SetValue(target, value);
    }

    private static T GetProperty<T>(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(target.GetType().FullName, propertyName);
        return (T)(property.GetValue(target) ?? throw new InvalidOperationException($"属性 {propertyName} 为空。"));
    }

    private static T GetField<T>(object target, string fieldName) where T : class
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, fieldName);
        return (T)(field.GetValue(target) ?? throw new InvalidOperationException($"字段 {fieldName} 为空。"));
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

    private static bool MenuContainsHeader(ItemCollection items, string header)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            if (Equals(item.Header, header) || MenuContainsHeader(item.Items, header)) return true;
        }
        return false;
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
