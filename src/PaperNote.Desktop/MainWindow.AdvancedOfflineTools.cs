using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PaperNote.Core.Models;
using PaperNote.Core.Services;
using PaperNote.Desktop.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private readonly LocalExtensionService _localExtensionService = new();
    private IReadOnlyList<LocalExtensionManifest> _localExtensions = [];

    private async void AdvancedOfflineTools_Click(object sender, RoutedEventArgs e)
    {
        _localExtensions = await _localExtensionService.LoadAsync();
        var button = sender as Button ?? AdvancedOfflineToolsButton;
        var menu = new ContextMenu();
        var canEdit = _currentPage is not null && !_isReadOnly;
        var hasPdfText = _currentPage?.PdfTextBlocks.Count > 0 || !string.IsNullOrWhiteSpace(_currentPage?.PdfText);

        var pdf = new MenuItem { Header = "高级 PDF", IsEnabled = _currentPage is not null };
        pdf.Items.Add(CreateMenuItem("查看与复制 PDF 原文", "", (_, _) => ShowCurrentPdfText(), hasPdfText));
        pdf.Items.Add(CreateMenuItem("添加文本填写框", "", (_, _) => AddPdfTextField(), canEdit));
        pdf.Items.Add(CreateMenuItem("添加复选框", "", (_, _) => AddPdfCheckbox(), canEdit));
        pdf.Items.Add(CreateMenuItem("编辑选中表单字段", "", (_, _) => EditSelectedPdfField(), canEdit && PdfAdvancedWorkflowService.GetFormFields(_currentPage!).Count > 0));
        pdf.Items.Add(CreateMenuItem("校验必填字段", "", (_, _) => ValidatePdfFields(), _currentPage is not null));
        pdf.Items.Add(CreateMenuItem("从选区保存并插入签名", "", (_, _) => AddLocalSignature(), canEdit && HasMixedSelection()));
        pdf.Items.Add(CreateMenuItem("从本机图片插入签名", "", (_, _) => AddSignatureFromFile(), canEdit));
        pdf.Items.Add(CreateMenuItem("校准 PDF 测量比例", "", (_, _) => CalibratePdfMeasurement(), canEdit));
        pdf.Items.Add(CreateGeometryToolsMenu());
        menu.Items.Add(pdf);

        var formula = new MenuItem { Header = "数学公式", IsEnabled = _currentPage is not null };
        formula.Items.Add(CreateMenuItem("识别文字转基础公式", "", (_, _) => InsertFormulaFromText(), canEdit));
        formula.Items.Add(CreateMenuItem("编辑选中公式 LaTeX", "", (_, _) => EditSelectedFormula(), canEdit));
        formula.Items.Add(CreateMenuItem("复制选中公式 LaTeX", "", (_, _) => CopySelectedFormula(), _currentPage is not null));
        formula.Items.Add(CreateMenuItem("导出选中公式 SVG", "", (_, _) => ExportSelectedFormulaSvg(), _currentPage is not null));
        menu.Items.Add(formula);

        menu.Items.Add(CreateMenuItem("合并本地笔记本…", "", async (_, _) => await MergeLocalNotebooksAsync(), _currentNotebook is not null && !_isReadOnly));
        menu.Items.Add(CreateMenuItem("双页阅读窗口", "", (_, _) => ShowTwoPageReadingWindow(), _currentNotebook?.Pages.Count > 0));
        menu.Items.Add(CreateMenuItem("打开参考页窗口（主窗口继续编辑）", "", (_, _) => ShowReferencePageWindow(), _currentNotebook?.Pages.Count > 1));

        var resources = new MenuItem { Header = "素材与模板资源包" };
        resources.Items.Add(CreateMenuItem("导出本地素材和模板包…", "", async (_, _) => await ExportResourcePackAsync(), _selectionMaterials.Count > 0 || _sharedPaperTemplates.Count > 0));
        resources.Items.Add(CreateMenuItem("导入本地素材和模板包…", "", async (_, _) => await ImportResourcePackAsync(), true));
        resources.Items.Add(CreateMenuItem("管理素材分类与收藏", "", async (_, _) => await ManageMaterialsAsync(), _selectionMaterials.Count > 0));
        menu.Items.Add(resources);

        var extensions = new MenuItem { Header = "安全本地扩展" };
        extensions.Items.Add(CreateMenuItem("导入声明式扩展包…", "", async (_, _) => await ImportLocalExtensionAsync(), true));
        if (_localExtensions.Count == 0)
        {
            extensions.Items.Add(new MenuItem { Header = "尚未安装扩展", IsEnabled = false });
        }
        foreach (var extension in _localExtensions)
        {
            var extensionMenu = new MenuItem { Header = $"{extension.Name} · {extension.Version}" };
            foreach (var command in extension.Commands)
            {
                var captured = command;
                extensionMenu.Items.Add(CreateMenuItem(captured.Name, "", (_, _) => RunLocalExtensionCommand(captured), true));
            }
            extensionMenu.Items.Add(new Separator());
            var extensionId = extension.Id;
            extensionMenu.Items.Add(CreateMenuItem("删除扩展", "", async (_, _) => await RemoveLocalExtensionAsync(extensionId), true));
            extensions.Items.Add(extensionMenu);
        }
        menu.Items.Add(extensions);

        var toolbar = new MenuItem { Header = "工具栏布局" };
        toolbar.Items.Add(CreateMenuItem("完整", "", (_, _) => ApplyDesktopToolbarLayout("Full"), true));
        toolbar.Items.Add(CreateMenuItem("专注书写", "", (_, _) => ApplyDesktopToolbarLayout("Writing"), true));
        toolbar.Items.Add(CreateMenuItem("阅读批注", "", (_, _) => ApplyDesktopToolbarLayout("Review"), true));
        toolbar.Items.Add(CreateMenuItem("极简", "", (_, _) => ApplyDesktopToolbarLayout("Minimal"), true));
        menu.Items.Add(toolbar);

        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ShowCurrentPdfText()
    {
        if (_currentPage is null) return;
        var text = _currentPage.PdfTextBlocks.Count > 0
            ? string.Join(" ", _currentPage.PdfTextBlocks.OrderBy(item => item.ReadingOrder).Select(item => item.Text))
            : _currentPage.PdfText;
        ShowTextWindow("PDF 原文（可选择和复制）", text, readOnly: true);
    }

    private void AddPdfTextField()
    {
        if (!CanEditPageObjects() || _currentPage is null) return;
        var name = PromptForText("添加 PDF 文本字段", "字段名称", "姓名");
        if (name is null) return;
        var required = MessageBox.Show(this, "是否设为必填字段？", "文本字段", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        var count = PdfAdvancedWorkflowService.GetFormFields(_currentPage).Count;
        var field = PdfAdvancedWorkflowService.AddTextField(_currentPage, name, 160 + count * 18, 190 + count * 18, required: required);
        field.LayerId = PageLayerService.EnsureDefault(_currentPage).Id;
        RefreshCurrentPageFromModel([], [field.Id]);
        StatusText.Text = $"已添加 PDF 文本字段：{field.FormFieldName}";
    }

    private void AddPdfCheckbox()
    {
        if (!CanEditPageObjects() || _currentPage is null) return;
        var name = PromptForText("添加 PDF 复选框", "字段名称", "确认");
        if (name is null) return;
        var required = MessageBox.Show(this, "是否必须勾选？", "复选框", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        var count = PdfAdvancedWorkflowService.GetFormFields(_currentPage).Count;
        var field = PdfAdvancedWorkflowService.AddCheckbox(_currentPage, name, 180 + count * 22, 230 + count * 22, required);
        field.LayerId = PageLayerService.EnsureDefault(_currentPage).Id;
        RefreshCurrentPageFromModel([], [field.Id]);
        StatusText.Text = $"已添加 PDF 复选框：{field.FormFieldName}";
    }

    private PageObject? ResolvePdfField()
    {
        if (_currentPage is null) return null;
        var fields = PdfAdvancedWorkflowService.GetFormFields(_currentPage);
        var selected = fields.FirstOrDefault(item => _selectedPageObjectIds.Contains(item.Id));
        if (selected is not null || fields.Count <= 1) return selected ?? fields.FirstOrDefault();
        var name = PromptForText("选择表单字段", "输入字段名称", fields[0].FormFieldName);
        return fields.FirstOrDefault(item => string.Equals(item.FormFieldName, name?.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    private void EditSelectedPdfField()
    {
        if (!CanEditPageObjects() || _currentPage is null) return;
        var field = ResolvePdfField();
        if (field is null) { StatusText.Text = "没有找到要编辑的表单字段"; return; }
        if (field.FormFieldKind == "Checkbox")
        {
            PdfAdvancedWorkflowService.SetFieldValue(field, null);
        }
        else
        {
            var value = PromptForText("填写 PDF 字段", field.FormFieldName, field.FormFieldValue);
            if (value is null) return;
            PdfAdvancedWorkflowService.SetFieldValue(field, value);
        }
        RefreshCurrentPageFromModel([], [field.Id]);
        StatusText.Text = $"已更新字段：{field.FormFieldName}";
    }

    private void ValidatePdfFields()
    {
        if (_currentPage is null) return;
        var result = PdfAdvancedWorkflowService.Validate(_currentPage);
        MessageBox.Show(this, result.IsValid ? "所有必填字段均已填写。" : $"以下必填字段尚未完成：\n\n{string.Join("\n", result.MissingRequiredFields)}", "PDF 表单校验", MessageBoxButton.OK, result.IsValid ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void AddLocalSignature()
    {
        if (_currentPage is null || !CanEditPageObjects()) return;
        var imageData = CreateSelectionPngBase64();
        if (string.IsNullOrWhiteSpace(imageData)) return;
        var name = PromptForText("保存本地签名", "签名名称", "我的签名");
        if (name is null) return;
        var field = PdfAdvancedWorkflowService.AddSignature(_currentPage, name, imageData, 180, 260);
        field.LayerId = PageLayerService.EnsureDefault(_currentPage).Id;
        RefreshCurrentPageFromModel([], [field.Id]);
        StatusText.Text = "已把选区作为本地签名插入当前页";
    }

    private void AddSignatureFromFile()
    {
        if (_currentPage is null || !CanEditPageObjects()) return;
        var dialog = new OpenFileDialog { Title = "选择签名图片", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
        if (dialog.ShowDialog(this) != true) return;
        var bytes = File.ReadAllBytes(dialog.FileName);
        if (bytes.Length > 12 * 1024 * 1024) { MessageBox.Show(this, "签名图片不能超过 12 MB。", "文件过大", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var field = PdfAdvancedWorkflowService.AddSignature(_currentPage, Path.GetFileNameWithoutExtension(dialog.FileName), Convert.ToBase64String(bytes), 180, 260);
        field.LayerId = PageLayerService.EnsureDefault(_currentPage).Id;
        RefreshCurrentPageFromModel([], [field.Id]);
        StatusText.Text = "已插入本地签名图片";
    }

    private string? CreateSelectionPngBase64()
    {
        if (_currentPage is null || !HasMixedSelection()) return null;
        SyncCurrentInkModelForSelection();
        var selection = SelectionExportService.Create(_currentPage, GetSelectedStrokeIds(), _selectedPageObjectIds);
        if (selection is null) return null;
        const double scale = 2;
        var bitmap = PageThumbnailService.CreatePageBitmap(selection.Page, 1680, 2376);
        var x = Math.Clamp((int)Math.Floor(selection.X * scale), 0, bitmap.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Floor(selection.Y * scale), 0, bitmap.PixelHeight - 1);
        var width = Math.Clamp((int)Math.Ceiling(selection.Width * scale), 1, bitmap.PixelWidth - x);
        var height = Math.Clamp((int)Math.Ceiling(selection.Height * scale), 1, bitmap.PixelHeight - y);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(bitmap, new Int32Rect(x, y, width, height))));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return Convert.ToBase64String(memory.ToArray());
    }

    private void CalibratePdfMeasurement()
    {
        if (_currentPage is null || !CanEditPageObjects()) return;
        CaptureCurrentPage();
        var measurement = GeometryMeasurementService.MeasureSelection(_currentPage, GetSelectedStrokeIds(), _selectedPageObjectIds);
        if (measurement is null || measurement.DirectDistanceMillimeters <= 0) { MessageBox.Show(this, "请先选中一条已知长度的笔迹或对象。", "校准比例", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var knownText = PromptForText("校准 PDF 测量比例", $"当前选区像素长度约 {measurement.DirectDistanceMillimeters * GeometryMeasurementService.PixelsPerMillimeter:0.##}，请输入真实毫米数", "10");
        if (!double.TryParse(knownText, out var known) || known <= 0) return;
        var scale = PdfAdvancedWorkflowService.CalibrateMeasurement(_currentPage, measurement.DirectDistanceMillimeters * GeometryMeasurementService.PixelsPerMillimeter, known);
        MarkDirty();
        StatusText.Text = $"测量比例已校准：{scale:0.###} 像素/毫米";
    }

    private void InsertFormulaFromText()
    {
        if (_currentPage is null || !CanEditPageObjects()) return;
        var initial = _currentPage.Objects.FirstOrDefault(item => _selectedPageObjectIds.Contains(item.Id))?.Text;
        if (string.IsNullOrWhiteSpace(initial)) initial = _currentPage.OcrText;
        var source = PromptForText("基础数学公式", "输入识别文字，例如 1/2 + x^2 ≤ π", initial ?? string.Empty);
        if (source is null) return;
        var latex = MathFormulaService.ToLatex(source);
        var edited = PromptForText("确认 LaTeX", "可继续修改", latex);
        if (edited is null) return;
        try
        {
            var formula = MathFormulaService.CreateFormulaObject(edited);
            formula.LayerId = PageLayerService.EnsureDefault(_currentPage).Id;
            _currentPage.Objects.Add(formula);
            RefreshCurrentPageFromModel([], [formula.Id]);
            StatusText.Text = "已插入基础公式对象";
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            MessageBox.Show(this, exception.Message, "公式无效", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private PageObject? GetSelectedFormula() => _currentPage?.Objects.FirstOrDefault(item => _selectedPageObjectIds.Contains(item.Id) && !string.IsNullOrWhiteSpace(item.FormulaLatex));

    private void EditSelectedFormula()
    {
        if (!CanEditPageObjects()) return;
        var formula = GetSelectedFormula();
        if (formula is null) { StatusText.Text = "请先选中一个公式对象"; return; }
        var latex = PromptForText("编辑公式 LaTeX", "LaTeX", formula.FormulaLatex);
        if (latex is null || !MathFormulaService.IsBalanced(latex)) { if (latex is not null) MessageBox.Show(this, "LaTeX 花括号不匹配。", "公式无效", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        formula.FormulaLatex = latex.Trim();
        formula.Text = MathFormulaService.CreateFormulaObject(latex).Text;
        formula.AltText = $"数学公式：{formula.FormulaLatex}";
        RefreshCurrentPageFromModel([], [formula.Id]);
    }

    private void CopySelectedFormula()
    {
        var formula = GetSelectedFormula();
        if (formula is null) { StatusText.Text = "请先选中一个公式对象"; return; }
        Clipboard.SetText(formula.FormulaLatex);
        StatusText.Text = "公式 LaTeX 已复制";
    }

    private void ExportSelectedFormulaSvg()
    {
        var formula = GetSelectedFormula();
        if (formula is null) { StatusText.Text = "请先选中一个公式对象"; return; }
        var dialog = new SaveFileDialog { Title = "导出公式 SVG", Filter = "SVG 文件|*.svg", DefaultExt = ".svg", AddExtension = true, FileName = "PaperNote-Formula.svg" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, MathFormulaService.ExportSvg(formula.FormulaLatex), new UTF8Encoding(false));
        StatusText.Text = $"公式已导出：{Path.GetFileName(dialog.FileName)}";
    }

    private async Task MergeLocalNotebooksAsync()
    {
        if (_currentNotebook is null || _isReadOnly) return;
        var dialog = new OpenFileDialog { Title = "选择要合并的本地笔记本", Filter = "PaperNote 笔记本|*.papernote|所有文件|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        CaptureCurrentPage();
        var sources = new List<NotebookDocument>();
        var failed = new List<string>();
        foreach (var file in dialog.FileNames)
        {
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(_currentNotebookPath ?? string.Empty), StringComparison.OrdinalIgnoreCase)) continue;
            try { sources.Add(await _notebookStorage.LoadAsync(file)); }
            catch (Exception exception) { failed.Add($"{Path.GetFileName(file)}：{exception.Message}"); }
        }
        var result = NotebookMergeService.MergeInto(_currentNotebook, sources, _currentNotebook.Pages.IndexOf(_currentPage!) + 1);
        RefreshPageItems(_currentPage?.Id);
        MarkDirty();
        var message = $"已合并 {result.AddedPages} 页、{result.AddedOutlineEntries} 条目录。";
        if (failed.Count > 0) message += $"\n\n未能导入：\n{string.Join("\n", failed)}";
        MessageBox.Show(this, message, "合并完成", MessageBoxButton.OK, failed.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async Task ExportResourcePackAsync()
    {
        var dialog = new SaveFileDialog { Title = "导出素材与模板包", Filter = "PaperNote 资源包|*.pnpack", DefaultExt = ".pnpack", AddExtension = true, FileName = $"PaperNote-Resources-{DateTime.Now:yyyyMMdd}.pnpack" };
        if (dialog.ShowDialog(this) != true) return;
        await LocalResourcePackService.ExportAsync(dialog.FileName, "我的离线资源", _selectionMaterials, _sharedPaperTemplates);
        StatusText.Text = $"资源包已导出：{Path.GetFileName(dialog.FileName)}";
    }

    private async Task ImportResourcePackAsync()
    {
        var dialog = new OpenFileDialog { Title = "导入素材与模板包", Filter = "PaperNote 资源包|*.pnpack;*.zip|所有文件|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        var result = await LocalResourcePackService.ImportAsync(dialog.FileName, _selectionMaterials, _sharedPaperTemplates);
        _selectionMaterials.AddRange(result.Materials);
        _sharedPaperTemplates.AddRange(result.Templates);
        await _selectionMaterialLibraryService.SaveAsync(_selectionMaterials);
        await _paperTemplateLibraryService.SaveAsync(_sharedPaperTemplates);
        UpdateSelectionMaterialsButton();
        UpdateSharedPaperTemplatesButton();
        MessageBox.Show(this, $"已导入 {result.Materials.Count} 个素材、{result.Templates.Count} 个模板；跳过 {result.SkippedDuplicates} 个重复项。", "资源包导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task ManageMaterialsAsync()
    {
        if (_selectionMaterials.Count == 0) return;
        var query = PromptForText("管理素材", "搜索名称或关键词（留空显示全部）", string.Empty);
        if (query is null) return;
        var list = LocalResourcePackService.SortMaterials(_selectionMaterials, query).Take(30).ToArray();
        if (list.Length == 0) { MessageBox.Show(this, "没有匹配的素材。", "素材管理", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var name = PromptForText("选择素材", $"输入要编辑的素材名称：\n{string.Join("、", list.Select(item => item.Name))}", list[0].Name);
        var material = list.FirstOrDefault(item => string.Equals(item.Name, name?.Trim(), StringComparison.CurrentCultureIgnoreCase));
        if (material is null) return;
        var category = PromptForText("素材分类", "分类", material.Category);
        if (category is null) return;
        material.Category = category.Trim();
        material.IsFavorite = MessageBox.Show(this, "把它设为收藏素材？", "素材收藏", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        material.UpdatedAt = DateTimeOffset.Now;
        await _selectionMaterialLibraryService.SaveAsync(_selectionMaterials);
        StatusText.Text = $"已更新素材：{material.Name}";
    }

    private async Task ImportLocalExtensionAsync()
    {
        var dialog = new OpenFileDialog { Title = "导入安全本地扩展", Filter = "PaperNote 扩展包|*.pnext;*.zip|所有文件|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        var manifest = await _localExtensionService.ImportAsync(dialog.FileName);
        _localExtensions = await _localExtensionService.LoadAsync();
        StatusText.Text = $"已安装本地扩展：{manifest.Name}";
    }

    private void RunLocalExtensionCommand(LocalTextCommand command)
    {
        var selectedText = _currentPage?.Objects.FirstOrDefault(item => _selectedPageObjectIds.Contains(item.Id) && item.Kind == "Text")?.Text ?? string.Empty;
        var input = PromptForText(command.Name, "输入要处理的文字", selectedText);
        if (input is null) return;
        try
        {
            var output = LocalExtensionService.Execute(command, input);
            ShowTextWindow($"扩展结果 · {command.Name}", output, readOnly: false);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "扩展执行失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task RemoveLocalExtensionAsync(string extensionId)
    {
        if (_localExtensionService.Remove(extensionId)) StatusText.Text = "已删除本地扩展";
        _localExtensions = await _localExtensionService.LoadAsync();
    }

    private void ApplyDesktopToolbarLayout(string layout)
    {
        WritingToolsPanel.Visibility = Visibility.Visible;
        InsertToolsPanel.Visibility = Visibility.Visible;
        InkStylePanel.Visibility = Visibility.Visible;
        switch (layout)
        {
            case "Writing": InsertToolsPanel.Visibility = Visibility.Collapsed; break;
            case "Review": WritingToolsPanel.Visibility = Visibility.Collapsed; break;
            case "Minimal": InsertToolsPanel.Visibility = Visibility.Collapsed; InkStylePanel.Visibility = Visibility.Collapsed; break;
        }
        StatusText.Text = layout switch { "Writing" => "工具栏已切换为专注书写", "Review" => "工具栏已切换为阅读批注", "Minimal" => "工具栏已切换为极简", _ => "已显示完整工具栏" };
    }

    private void ShowTwoPageReadingWindow()
    {
        if (_currentNotebook is null || _currentPage is null) return;
        CaptureCurrentPage();
        var index = Math.Max(0, _currentNotebook.Pages.IndexOf(_currentPage));
        var second = _currentNotebook.Pages[Math.Min(index + 1, _currentNotebook.Pages.Count - 1)];
        ShowPagePreviewWindow("双页阅读", [_currentPage, second]);
    }

    private void ShowReferencePageWindow()
    {
        if (_currentNotebook is null || _currentPage is null || _currentNotebook.Pages.Count < 2) return;
        var pageText = PromptForText("参考页", $"输入参考页码（1-{_currentNotebook.Pages.Count}）", "1");
        if (!int.TryParse(pageText, out var pageNumber)) return;
        var page = _currentNotebook.Pages[Math.Clamp(pageNumber - 1, 0, _currentNotebook.Pages.Count - 1)];
        ShowPagePreviewWindow($"参考页 · 第 {pageNumber} 页（主窗口可继续编辑）", [page]);
    }

    private void ShowPagePreviewWindow(string title, IReadOnlyList<NotebookPage> pages)
    {
        var panel = new UniformGrid { Rows = 1, Columns = pages.Count, Margin = new Thickness(14) };
        foreach (var page in pages)
        {
            panel.Children.Add(new Border
            {
                Margin = new Thickness(6), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1),
                Child = new Image { Source = PageThumbnailService.CreatePageBitmap(page, 1260, 1782), Stretch = Stretch.Uniform }
            });
        }
        var window = new Window { Owner = this, Title = title, Width = pages.Count > 1 ? 1180 : 650, Height = 820, Content = panel, Background = Brushes.DimGray, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) window.Close(); };
        window.Show();
    }

    private void ShowTextWindow(string title, string text, bool readOnly)
    {
        var editor = new TextBox { Text = text ?? string.Empty, IsReadOnly = readOnly, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12), FontSize = 15 };
        var copy = new Button { Content = "复制全部", MinWidth = 90, Margin = new Thickness(6), Padding = new Thickness(12, 6, 12, 6) };
        var close = new Button { Content = "关闭", MinWidth = 90, Margin = new Thickness(6), Padding = new Thickness(12, 6, 12, 6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(6) };
        buttons.Children.Add(copy); buttons.Children.Add(close);
        var root = new DockPanel(); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons); root.Children.Add(editor);
        var window = new Window { Owner = this, Title = title, Width = 760, Height = 560, Content = root, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        copy.Click += (_, _) => { Clipboard.SetText(editor.Text); StatusText.Text = "文字已复制"; };
        close.Click += (_, _) => window.Close();
        window.PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) window.Close(); };
        window.ShowDialog();
    }
}
