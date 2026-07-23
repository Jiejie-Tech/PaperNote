using PaperNote.Core.Models;
using PaperNote.Core.Services;
using PaperNote.Mobile.Controls;

namespace PaperNote.Mobile.Pages;

public sealed partial class EditorPage
{
    private LocalExtensionService MobileExtensionService => new(Path.Combine(FileSystem.AppDataDirectory, "extensions"));

    private async Task ShowAdvancedOfflineToolsAsync()
    {
        if (_page is null || _repository.Current is null) return;
        var choice = await DisplayActionSheetAsync("高级离线工具", "取消", null,
            "PDF 原文查看与复制", "PDF 表单与签名", "PDF 测量比例校准", "数学公式", "合并本地笔记本", "素材与模板资源包", "安全本地扩展", "录音精细编辑");
        switch (choice)
        {
            case "PDF 原文查看与复制": await ShowMobilePdfTextAsync(); break;
            case "PDF 表单与签名": await ShowMobilePdfFormsAsync(); break;
            case "PDF 测量比例校准": await CalibrateMobilePdfAsync(); break;
            case "数学公式": await ShowMobileFormulaToolsAsync(); break;
            case "合并本地笔记本": await MergeMobileNotebookAsync(); break;
            case "素材与模板资源包": await ShowMobileResourcePackToolsAsync(); break;
            case "安全本地扩展": await ShowMobileExtensionToolsAsync(); break;
            case "录音精细编辑": await EditMobileAudioTimelineAsync(); break;
        }
    }

    private async Task ShowMobilePdfTextAsync()
    {
        if (_page is null) return;
        var text = _page.PdfTextBlocks.Count > 0
            ? string.Join(" ", _page.PdfTextBlocks.OrderBy(item => item.ReadingOrder).Select(item => item.Text))
            : _page.PdfText;
        if (string.IsNullOrWhiteSpace(text))
        {
            await DisplayAlertAsync("没有 PDF 原文", "当前页没有可提取的 PDF 文字。扫描件可先运行离线 OCR。", "知道了");
            return;
        }
        var editor = new Editor { Text = text, IsReadOnly = true, AutoSize = EditorAutoSizeOption.TextChanges, FontSize = 16, TextColor = UiTheme.Text, BackgroundColor = UiTheme.Surface };
        var copy = UiTheme.Button("复制全部", async (_, _) => { await Clipboard.Default.SetTextAsync(editor.Text); await DisplayAlertAsync("已复制", "PDF 原文已复制到剪贴板。", "知道了"); }, primary: true);
        var page = new ContentPage
        {
            Title = "PDF 原文",
            BackgroundColor = UiTheme.Background,
            Content = new Grid
            {
                Padding = 12,
                RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
                Children = { new ScrollView { Content = editor }, copy }
            }
        };
        Grid.SetRow(copy, 1);
        await Navigation.PushAsync(page);
    }

    private async Task ShowMobilePdfFormsAsync()
    {
        if (_page is null) return;
        var choice = await DisplayActionSheetAsync("PDF 表单与签名", "取消", null,
            "添加文本填写框", "添加复选框", "编辑或切换字段", "校验必填字段", "从图片插入本地签名");
        switch (choice)
        {
            case "添加文本填写框": await AddMobilePdfTextFieldAsync(); break;
            case "添加复选框": await AddMobilePdfCheckboxAsync(); break;
            case "编辑或切换字段": await EditMobilePdfFieldAsync(); break;
            case "校验必填字段":
                var validation = PdfAdvancedWorkflowService.Validate(_page);
                await DisplayAlertAsync("PDF 表单校验", validation.IsValid ? "所有必填字段均已完成。" : $"尚未完成：\n{string.Join("\n", validation.MissingRequiredFields)}", "知道了");
                break;
            case "从图片插入本地签名": await AddMobileSignatureAsync(); break;
        }
    }

    private async Task AddMobilePdfTextFieldAsync()
    {
        if (_page is null) return;
        var name = await DisplayPromptAsync("添加文本字段", "字段名称", initialValue: "姓名", maxLength: 60);
        if (string.IsNullOrWhiteSpace(name)) return;
        var required = await DisplayAlertAsync("必填设置", "是否设为必填字段？", "必填", "可选");
        var count = PdfAdvancedWorkflowService.GetFormFields(_page).Count;
        var field = PdfAdvancedWorkflowService.AddTextField(_page, name, 150 + count * 16, 210 + count * 16, required: required);
        field.LayerId = PageLayerService.EnsureDefault(_page).Id;
        ReloadMobilePage(field.Id);
    }

    private async Task AddMobilePdfCheckboxAsync()
    {
        if (_page is null) return;
        var name = await DisplayPromptAsync("添加复选框", "字段名称", initialValue: "确认", maxLength: 60);
        if (string.IsNullOrWhiteSpace(name)) return;
        var required = await DisplayAlertAsync("必填设置", "是否必须勾选？", "必须", "可选");
        var count = PdfAdvancedWorkflowService.GetFormFields(_page).Count;
        var field = PdfAdvancedWorkflowService.AddCheckbox(_page, name, 170 + count * 18, 240 + count * 18, required);
        field.LayerId = PageLayerService.EnsureDefault(_page).Id;
        ReloadMobilePage(field.Id);
    }

    private async Task EditMobilePdfFieldAsync()
    {
        if (_page is null) return;
        var fields = PdfAdvancedWorkflowService.GetFormFields(_page);
        if (fields.Count == 0) { await DisplayAlertAsync("没有字段", "请先添加文本字段或复选框。", "知道了"); return; }
        var labels = fields.Select((item, index) => $"{index + 1}. {item.FormFieldName} · {(item.FormFieldKind == "Checkbox" ? (item.FormFieldChecked ? "已勾选" : "未勾选") : item.FormFieldValue)}").ToArray();
        var choice = await DisplayActionSheetAsync("选择表单字段", "取消", null, labels);
        var index = Array.IndexOf(labels, choice);
        if (index < 0) return;
        var field = fields[index];
        if (field.FormFieldKind == "Checkbox") PdfAdvancedWorkflowService.SetFieldValue(field, null);
        else
        {
            var value = await DisplayPromptAsync("填写 PDF 字段", field.FormFieldName, initialValue: field.FormFieldValue, maxLength: 500);
            if (value is null) return;
            PdfAdvancedWorkflowService.SetFieldValue(field, value);
        }
        ReloadMobilePage(field.Id);
    }

    private async Task AddMobileSignatureAsync()
    {
        if (_page is null) return;
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "选择本地签名图片", FileTypes = FilePickerFileType.Images });
        if (file is null) return;
        await using var stream = await file.OpenReadAsync();
        if (stream.CanSeek && stream.Length > 12 * 1024 * 1024) { await DisplayAlertAsync("文件过大", "签名图片不能超过 12 MB。", "知道了"); return; }
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        var field = PdfAdvancedWorkflowService.AddSignature(_page, Path.GetFileNameWithoutExtension(file.FileName), Convert.ToBase64String(memory.ToArray()), 170, 270);
        field.LayerId = PageLayerService.EnsureDefault(_page).Id;
        ReloadMobilePage(field.Id);
    }

    private async Task CalibrateMobilePdfAsync()
    {
        if (_page is null) return;
        var measurement = GeometryMeasurementService.MeasureSelection(_page, _canvas.SelectedStrokeIds, _canvas.SelectedObjectIds);
        if (measurement is null || measurement.DirectDistanceMillimeters <= 0)
        {
            await DisplayAlertAsync("需要选区", "先用套索选中一条已知长度的笔迹或对象，再进行校准。", "知道了");
            return;
        }
        var pixels = measurement.DirectDistanceMillimeters * GeometryMeasurementService.PixelsPerMillimeter;
        var input = await DisplayPromptAsync("校准 PDF 测量比例", $"选区像素长度约 {pixels:0.##}，请输入真实毫米数", initialValue: "10", keyboard: Keyboard.Numeric);
        if (!double.TryParse(input, out var known) || known <= 0) return;
        var scale = PdfAdvancedWorkflowService.CalibrateMeasurement(_page, pixels, known);
        ScheduleSave();
        await DisplayAlertAsync("校准完成", $"当前页比例为 {scale:0.###} 像素/毫米。", "知道了");
    }

    private async Task ShowMobileFormulaToolsAsync()
    {
        if (_page is null) return;
        var formulas = _page.Objects.Where(item => !string.IsNullOrWhiteSpace(item.FormulaLatex)).ToArray();
        var choice = await DisplayActionSheetAsync("数学公式", "取消", null, "识别文字转基础公式", "编辑公式 LaTeX", "复制公式 LaTeX", "导出公式 SVG");
        if (choice == "识别文字转基础公式")
        {
            var selectedText = _page.Objects.FirstOrDefault(item => _canvas.SelectedObjectIds.Contains(item.Id))?.Text;
            var source = await DisplayPromptAsync("基础数学公式", "输入识别文字，例如 1/2 + x^2 ≤ π", initialValue: selectedText ?? _page.OcrText, maxLength: 500);
            if (string.IsNullOrWhiteSpace(source)) return;
            var latex = MathFormulaService.ToLatex(source);
            latex = await DisplayPromptAsync("确认 LaTeX", "可继续修改", initialValue: latex, maxLength: 800) ?? string.Empty;
            try
            {
                var item = MathFormulaService.CreateFormulaObject(latex);
                item.LayerId = PageLayerService.EnsureDefault(_page).Id;
                _page.Objects.Add(item);
                ReloadMobilePage(item.Id);
            }
            catch (Exception exception) { await DisplayAlertAsync("公式无效", exception.Message, "知道了"); }
            return;
        }
        if (formulas.Length == 0) { await DisplayAlertAsync("没有公式", "请先创建公式对象。", "知道了"); return; }
        var labels = formulas.Select((item, index) => $"{index + 1}. {item.FormulaLatex}").ToArray();
        var selected = await DisplayActionSheetAsync("选择公式", "取消", null, labels);
        var selectedIndex = Array.IndexOf(labels, selected);
        if (selectedIndex < 0) return;
        var formula = formulas[selectedIndex];
        switch (choice)
        {
            case "编辑公式 LaTeX":
                var edited = await DisplayPromptAsync("编辑 LaTeX", "LaTeX", initialValue: formula.FormulaLatex, maxLength: 800);
                if (edited is null) return;
                if (!MathFormulaService.IsBalanced(edited)) { await DisplayAlertAsync("公式无效", "LaTeX 花括号不匹配。", "知道了"); return; }
                formula.FormulaLatex = edited.Trim(); formula.Text = MathFormulaService.CreateFormulaObject(edited).Text; formula.AltText = $"数学公式：{formula.FormulaLatex}"; ReloadMobilePage(formula.Id);
                break;
            case "复制公式 LaTeX": await Clipboard.Default.SetTextAsync(formula.FormulaLatex); break;
            case "导出公式 SVG":
                var path = Path.Combine(FileSystem.CacheDirectory, $"PaperNote-Formula-{DateTime.Now:yyyyMMdd-HHmmss}.svg");
                await File.WriteAllTextAsync(path, MathFormulaService.ExportSvg(formula.FormulaLatex));
                await Share.Default.RequestAsync(new ShareFileRequest { Title = "导出公式 SVG", File = new ShareFile(path, "image/svg+xml") });
                break;
        }
    }

    private async Task MergeMobileNotebookAsync()
    {
        if (_repository.Current is null) return;
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "选择要合并的 PaperNote 笔记本" });
        if (file is null) return;
        var cache = Path.Combine(FileSystem.CacheDirectory, $"merge-{Guid.NewGuid():N}.papernote");
        try
        {
            await using (var source = await file.OpenReadAsync()) await using (var target = File.Create(cache)) await source.CopyToAsync(target);
            var sourceDocument = await _repository.Storage.LoadAsync(cache);
            var result = NotebookMergeService.MergeInto(_repository.Current.Document, [sourceDocument], _repository.Current.Document.Pages.IndexOf(_page!) + 1);
            RefreshPageCards(); ScheduleSave();
            await DisplayAlertAsync("合并完成", $"已加入 {result.AddedPages} 页和 {result.AddedOutlineEntries} 条目录。加密笔记请先在原设备导出未加密副本。", "知道了");
        }
        catch (Exception exception) { await DisplayAlertAsync("合并失败", exception.Message, "知道了"); }
        finally { if (File.Exists(cache)) File.Delete(cache); }
    }

    private async Task ShowMobileResourcePackToolsAsync()
    {
        var choice = await DisplayActionSheetAsync("素材与模板资源包", "取消", null, "导出资源包", "导入资源包", "搜索与整理素材");
        var materials = (await _repository.MaterialLibrary.LoadAsync()).ToList();
        var templates = (await _repository.TemplateLibrary.LoadAsync()).ToList();
        if (choice == "导出资源包")
        {
            if (materials.Count == 0 && templates.Count == 0) { await DisplayAlertAsync("没有资源", "当前没有可导出的个人素材或共享模板。", "知道了"); return; }
            var path = Path.Combine(FileSystem.CacheDirectory, $"PaperNote-Resources-{DateTime.Now:yyyyMMdd-HHmmss}.pnpack");
            await LocalResourcePackService.ExportAsync(path, "我的离线资源", materials, templates);
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "导出 PaperNote 资源包", File = new ShareFile(path, "application/zip") });
        }
        else if (choice == "导入资源包")
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "选择 PaperNote 资源包" });
            if (file is null) return;
            var path = Path.Combine(FileSystem.CacheDirectory, $"resources-{Guid.NewGuid():N}.pnpack");
            try
            {
                await using (var source = await file.OpenReadAsync()) await using (var target = File.Create(path)) await source.CopyToAsync(target);
                var result = await LocalResourcePackService.ImportAsync(path, materials, templates);
                materials.AddRange(result.Materials); templates.AddRange(result.Templates);
                await _repository.MaterialLibrary.SaveAsync(materials); await _repository.TemplateLibrary.SaveAsync(templates);
                await DisplayAlertAsync("导入完成", $"新增 {result.Materials.Count} 个素材、{result.Templates.Count} 个模板；跳过 {result.SkippedDuplicates} 个重复项。", "知道了");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        else if (choice == "搜索与整理素材")
        {
            var query = await DisplayPromptAsync("搜索素材", "名称或关键词", maxLength: 60);
            if (query is null) return;
            var matches = LocalResourcePackService.SortMaterials(materials, query).Take(30).ToArray();
            if (matches.Length == 0) { await DisplayAlertAsync("没有结果", "没有匹配的素材。", "知道了"); return; }
            var labels = matches.Select((item, index) => $"{index + 1}. {(item.IsFavorite ? "★ " : string.Empty)}{item.Name} · {item.Category}").ToArray();
            var selected = await DisplayActionSheetAsync("素材管理", "取消", null, labels);
            var index = Array.IndexOf(labels, selected);
            if (index < 0) return;
            var material = matches[index];
            var action = await DisplayActionSheetAsync(material.Name, "取消", "删除", material.IsFavorite ? "取消收藏" : "设为收藏", "修改分类");
            if (action == "删除") materials.RemoveAll(item => item.Id == material.Id);
            else if (action is "设为收藏" or "取消收藏") material.IsFavorite = !material.IsFavorite;
            else if (action == "修改分类") material.Category = (await DisplayPromptAsync("修改分类", "分类", initialValue: material.Category, maxLength: 40) ?? material.Category).Trim();
            material.UpdatedAt = DateTimeOffset.Now;
            await _repository.MaterialLibrary.SaveAsync(materials);
        }
    }

    private async Task ShowMobileExtensionToolsAsync()
    {
        var service = MobileExtensionService;
        var extensions = await service.LoadAsync();
        var actions = new List<string> { "导入声明式扩展包" };
        actions.AddRange(extensions.Select(item => $"运行 · {item.Name}"));
        actions.AddRange(extensions.Select(item => $"删除 · {item.Name}"));
        var choice = await DisplayActionSheetAsync("安全本地扩展", "取消", null, actions.ToArray());
        if (choice == "导入声明式扩展包")
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "选择 PaperNote 扩展包" });
            if (file is null) return;
            var path = Path.Combine(FileSystem.CacheDirectory, $"extension-{Guid.NewGuid():N}.zip");
            try
            {
                await using (var source = await file.OpenReadAsync()) await using (var target = File.Create(path)) await source.CopyToAsync(target);
                var manifest = await service.ImportAsync(path);
                await DisplayAlertAsync("安装完成", $"已安装“{manifest.Name}”。扩展只执行受限的文字处理命令，不运行第三方代码。", "知道了");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            return;
        }
        var run = extensions.FirstOrDefault(item => choice == $"运行 · {item.Name}");
        if (run is not null)
        {
            var labels = run.Commands.Select((item, index) => $"{index + 1}. {item.Name}").ToArray();
            var commandChoice = await DisplayActionSheetAsync(run.Name, "取消", null, labels);
            var index = Array.IndexOf(labels, commandChoice);
            if (index < 0) return;
            var selectedText = _page?.Objects.FirstOrDefault(item => _canvas.SelectedObjectIds.Contains(item.Id))?.Text;
            var input = await DisplayPromptAsync(run.Commands[index].Name, "输入文字", initialValue: selectedText, maxLength: 4000);
            if (input is null) return;
            try
            {
                var output = LocalExtensionService.Execute(run.Commands[index], input);
                await Clipboard.Default.SetTextAsync(output);
                await DisplayAlertAsync("处理完成", $"结果已复制：\n\n{(output.Length > 800 ? output[..800] + "…" : output)}", "知道了");
            }
            catch (Exception exception) { await DisplayAlertAsync("扩展执行失败", exception.Message, "知道了"); }
            return;
        }
        var remove = extensions.FirstOrDefault(item => choice == $"删除 · {item.Name}");
        if (remove is not null && await DisplayAlertAsync("删除扩展", $"确定删除“{remove.Name}”吗？", "删除", "取消")) service.Remove(remove.Id);
    }

    private async Task ConfigureMobileToolbarAsync()
    {
        var choice = await DisplayActionSheetAsync("工具栏设置", "取消", null, "完整", "专注书写", "阅读批注", "极简");
        if (choice is null or "取消") return;
        Preferences.Default.Set("ToolbarLayout", choice);
        ApplyMobileToolbarLayout(choice);
    }

    private void ApplySavedMobileToolbarLayout() => ApplyMobileToolbarLayout(Preferences.Default.Get("ToolbarLayout", "完整"));

    private void ApplyMobileToolbarLayout(string layout)
    {
        _toolRow.IsVisible = true; _settingRow.IsVisible = true; _advancedRow.IsVisible = true;
        if (layout == "专注书写") _advancedRow.IsVisible = false;
        else if (layout == "阅读批注") _settingRow.IsVisible = false;
        else if (layout == "极简") { _settingRow.IsVisible = false; _advancedRow.IsVisible = false; }
        SemanticProperties.SetDescription(_toolbar, $"当前工具栏布局：{layout}");
    }

    private async Task ShowMobileReadingToolsAsync()
    {
        if (_repository.Current is null || _page is null) return;
        var notebook = _repository.Current.Document;
        var choice = await DisplayActionSheetAsync("双页阅读与参考", "取消", null, "当前页 + 下一页", "选择参考页 + 当前页");
        if (choice is null or "取消") return;
        NotebookPage left;
        NotebookPage right;
        if (choice == "当前页 + 下一页")
        {
            var index = Math.Max(0, notebook.Pages.IndexOf(_page));
            left = _page; right = notebook.Pages[Math.Min(index + 1, notebook.Pages.Count - 1)];
        }
        else
        {
            var input = await DisplayPromptAsync("选择参考页", $"页码 1-{notebook.Pages.Count}", initialValue: "1", keyboard: Keyboard.Numeric);
            if (!int.TryParse(input, out var pageNumber)) return;
            left = notebook.Pages[Math.Clamp(pageNumber - 1, 0, notebook.Pages.Count - 1)]; right = _page;
        }
        var leftCanvas = CreateReadOnlyCanvas(left);
        var rightCanvas = CreateReadOnlyCanvas(right);
        var grid = new Grid { ColumnSpacing = 8, Padding = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        grid.Add(leftCanvas); grid.Add(rightCanvas, 1);
        var readingPage = new ContentPage { Title = choice == "当前页 + 下一页" ? "双页阅读" : "参考分屏", BackgroundColor = Color.FromArgb("#D9DDE5"), Content = grid };
        await Navigation.PushAsync(readingPage);
    }

    private static InkCanvasView CreateReadOnlyCanvas(NotebookPage page)
    {
        var canvas = new InkCanvasView { Page = page, Document = page.Ink, Tool = InkCanvasTool.Pan, FingerDrawingEnabled = false, BackgroundColor = Color.FromArgb("#E7EAF1") };
        UiTheme.Describe(canvas, "只读参考页面", "双指缩放和平移，返回可继续编辑原笔记");
        return canvas;
    }

    private void ReloadMobilePage(Guid? selectedObjectId = null)
    {
        if (_page is null) return;
        _page.ModifiedAt = DateTimeOffset.Now;
        _canvas.Page = null; _canvas.Page = _page; _canvas.Document = _page.Ink;
        if (selectedObjectId.HasValue) { SelectTool(InkCanvasTool.Select); _canvas.SelectObject(selectedObjectId.Value); }
        ScheduleSave(); UpdatePageStatus();
    }

    private async Task EditMobileAudioTimelineAsync()
    {
        if (_page is null) return;
        var quality = Preferences.Default.Get("AudioQuality", "标准");
        var actions = new List<string> { $"录音质量：{quality}" };
        if (_page.AudioRecordings.Count > 0)
            actions.AddRange(["移动时间标记", "重命名时间标记", "删除时间标记", "设置播放裁剪范围", "清除播放裁剪范围"]);
        var choice = await DisplayActionSheetAsync("录音精细编辑", "取消", null, actions.ToArray());
        if (choice?.StartsWith("录音质量：", StringComparison.Ordinal) == true)
        {
            var selected = await DisplayActionSheetAsync("录音质量", "取消", null, "省空间 · 32 kHz / 64 kbps", "标准 · 44.1 kHz / 128 kbps", "高质量 · 48 kHz / 192 kbps");
            var value = selected switch { string text when text.StartsWith("省空间", StringComparison.Ordinal) => "省空间", string text when text.StartsWith("高质量", StringComparison.Ordinal) => "高质量", string text when text.StartsWith("标准", StringComparison.Ordinal) => "标准", _ => null };
            if (value is not null) Preferences.Default.Set("AudioQuality", value);
            return;
        }
        var recording = await ChooseRecordingAsync("选择要编辑的录音");
        if (recording is null) return;
        if (choice is "移动时间标记" or "重命名时间标记" or "删除时间标记")
        {
            if (recording.Cues.Count == 0) { await DisplayAlertAsync("没有时间标记", "这段录音还没有时间标记。", "知道了"); return; }
            var labels = recording.Cues.OrderBy(cue => cue.OffsetMilliseconds).Select((cue, index) => $"{index + 1}. {AudioTimelineService.FormatDuration(cue.OffsetMilliseconds)} · {(string.IsNullOrWhiteSpace(cue.Label) ? "时间标记" : cue.Label)}").ToArray();
            var cueChoice = await DisplayActionSheetAsync("选择时间标记", "取消", null, labels);
            var cueIndex = Array.IndexOf(labels, cueChoice);
            if (cueIndex < 0) return;
            var cue = recording.Cues.OrderBy(item => item.OffsetMilliseconds).ElementAt(cueIndex);
            if (choice == "移动时间标记")
            {
                var seconds = await DisplayPromptAsync("移动时间标记", "新的秒数", initialValue: (cue.OffsetMilliseconds / 1000d).ToString("0.###"), keyboard: Keyboard.Numeric);
                if (double.TryParse(seconds, out var value) && value >= 0) AudioTimelineService.MoveCue(recording, cue.Id, (long)Math.Round(value * 1000));
            }
            else if (choice == "重命名时间标记")
            {
                var name = await DisplayPromptAsync("重命名时间标记", "名称", initialValue: cue.Label, maxLength: 60);
                if (name is not null) AudioTimelineService.RenameCue(recording, cue.Id, name);
            }
            else if (await DisplayAlertAsync("删除时间标记", "确定删除这个时间标记吗？录音文件不会被删除。", "删除", "取消"))
                AudioTimelineService.RemoveCue(recording, cue.Id);
            ScheduleSave();
            return;
        }
        if (choice == "设置播放裁剪范围")
        {
            var start = await DisplayPromptAsync("裁剪起点", "起始秒数", initialValue: (recording.TrimStartMilliseconds / 1000d).ToString("0.###"), keyboard: Keyboard.Numeric);
            var defaultEnd = recording.TrimEndMilliseconds > 0 ? recording.TrimEndMilliseconds : recording.DurationMilliseconds;
            var end = await DisplayPromptAsync("裁剪终点", "结束秒数", initialValue: (defaultEnd / 1000d).ToString("0.###"), keyboard: Keyboard.Numeric);
            if (double.TryParse(start, out var startSeconds) && double.TryParse(end, out var endSeconds))
            {
                AudioTimelineService.SetTrimRange(recording, (long)Math.Round(startSeconds * 1000), (long)Math.Round(endSeconds * 1000));
                ScheduleSave();
            }
        }
        else if (choice == "清除播放裁剪范围")
        {
            AudioTimelineService.SetTrimRange(recording, 0, 0);
            ScheduleSave();
        }
    }
}
