using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PaperNote.Core.Models;
using PaperNote.Desktop.Services;
using PaperNote.Core.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private readonly PaperTemplateLibraryService _paperTemplateLibraryService = new();
    private readonly List<SharedPaperTemplate> _sharedPaperTemplates = [];

    private async Task InitializeSharedPaperTemplatesAsync()
    {
        _sharedPaperTemplates.Clear();
        _sharedPaperTemplates.AddRange(await _paperTemplateLibraryService.LoadAsync());
        UpdateSharedPaperTemplatesButton();
    }

    private void UpdateSharedPaperTemplatesButton()
    {
        if (SharedPaperTemplatesButton is null) return;
        SharedPaperTemplatesButton.Content = _sharedPaperTemplates.Count == 0 ? "模板库⌄" : $"模板库 {_sharedPaperTemplates.Count}⌄";
        SharedPaperTemplatesButton.ToolTip = $"跨笔记共享纸张模板 · {_sharedPaperTemplates.Count}/{PaperTemplateLibraryService.MaximumTemplates}";
    }

    private void SharedPaperTemplates_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var canEdit = !_isReadOnly && _currentNotebook is not null && _currentPage is not null;
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("保存当前页纸张到共享模板库", "", async (_, _) => await SaveCurrentPageAsSharedTemplateAsync(), canEdit && _sharedPaperTemplates.Count < PaperTemplateLibraryService.MaximumTemplates));
        if (_sharedPaperTemplates.Count == 0)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "还没有共享模板", IsEnabled = false });
        }
        else
        {
            menu.Items.Add(new Separator());
            var applyMenu = new MenuItem { Header = "应用到当前页" };
            var insertMenu = new MenuItem { Header = "在当前页后插入" };
            var deleteMenu = new MenuItem { Header = "删除共享模板", IsEnabled = !_isReadOnly };
            foreach (var template in _sharedPaperTemplates.ToArray())
            {
                var capturedId = template.Id;
                var label = GetSharedPaperTemplateDisplayName(template);
                applyMenu.Items.Add(CreateMenuItem(label, "", (_, _) => ApplySharedPaperTemplateToCurrent(capturedId), canEdit));
                insertMenu.Items.Add(CreateMenuItem(label, "", (_, _) => InsertSharedPaperTemplateAfterCurrent(capturedId), canEdit));
                deleteMenu.Items.Add(CreateMenuItem(label, "", async (_, _) => await DeleteSharedPaperTemplateAsync(capturedId), !_isReadOnly));
            }
            menu.Items.Add(applyMenu);
            menu.Items.Add(insertMenu);
            menu.Items.Add(deleteMenu);
        }
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private async Task SaveCurrentPageAsSharedTemplateAsync()
    {
        if (_currentPage is null || _isReadOnly || _sharedPaperTemplates.Count >= PaperTemplateLibraryService.MaximumTemplates) return;
        var template = CreateSharedPaperTemplateFromPage(_currentPage);
        _sharedPaperTemplates.Add(template);
        await _paperTemplateLibraryService.SaveAsync(_sharedPaperTemplates);
        UpdateSharedPaperTemplatesButton();
        StatusText.Text = $"已保存共享纸张模板：{template.Name}";
    }

    private static SharedPaperTemplate CreateSharedPaperTemplateFromPage(NotebookPage page)
    {
        var sourceName = page.BackgroundSourceType is "Image" or "PDF" ? page.BackgroundSourceName : string.Empty;
        var baseName = !string.IsNullOrWhiteSpace(sourceName)
            ? Path.GetFileNameWithoutExtension(sourceName)
            : GetPaperTemplateDisplayName(page.PaperTemplate, page.PaperColor);
        return new SharedPaperTemplate
        {
            Name = string.IsNullOrWhiteSpace(baseName) ? $"自定义纸张 {DateTime.Now:MM-dd HHmm}" : baseName,
            PaperTemplate = page.PaperTemplate,
            PaperColor = page.PaperColor,
            BackgroundImageData = page.BackgroundImageData,
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? string.Empty : Path.GetFileName(sourceName)
        };
    }

    private static string GetPaperTemplateDisplayName(string paperTemplate, string paperColor)
    {
        var template = paperTemplate switch { "Blank" => "空白", "Lined" => "横线", "Grid" => "方格", _ => "点阵" };
        var color = paperColor switch { "#FFFBEA" => "米黄", "#F2F7FF" => "浅蓝", "#F2FAF3" => "浅绿", _ => "白色" };
        return $"{color}{template}";
    }

    private static string GetSharedPaperTemplateDisplayName(SharedPaperTemplate template)
    {
        return string.IsNullOrWhiteSpace(template.SourceName) ? $"{template.Name} · {GetPaperTemplateDisplayName(template.PaperTemplate, template.PaperColor)}" : $"{template.Name} · 图片纸张";
    }

    private void ApplySharedPaperTemplateToCurrent(Guid templateId)
    {
        if (_currentPage is null || _isReadOnly) return;
        var template = _sharedPaperTemplates.FirstOrDefault(item => item.Id == templateId);
        if (template is null) return;
        ApplySharedPaperTemplate(_currentPage, template);
        ApplyPageAppearance(_currentPage);
        UpdateCurrentPageMetadataControls(_currentPage);
        UpdateCurrentPageThumbnail();
        MarkDirty();
        StatusText.Text = $"已应用共享纸张模板：{template.Name}";
    }

    private void InsertSharedPaperTemplateAfterCurrent(Guid templateId)
    {
        if (_currentNotebook is null || _currentPage is null || _isReadOnly) return;
        var template = _sharedPaperTemplates.FirstOrDefault(item => item.Id == templateId);
        if (template is null) return;
        CaptureCurrentPage();
        var page = new NotebookPage { Title = template.Name };
        ApplySharedPaperTemplate(page, template);
        var index = Math.Max(0, _currentNotebook.Pages.IndexOf(_currentPage) + 1);
        _currentNotebook.Pages.Insert(index, page);
        _currentPage = page;
        _currentNotebook.CurrentPageId = page.Id;
        RefreshPageItems(page.Id);
        LoadPage(page);
        MarkDirty();
        StatusText.Text = $"已插入共享纸张模板：{template.Name}";
    }

    private static void ApplySharedPaperTemplate(NotebookPage page, SharedPaperTemplate template)
    {
        page.PaperTemplate = template.PaperTemplate;
        page.PaperColor = template.PaperColor;
        page.BackgroundImageData = template.BackgroundImageData;
        page.BackgroundSourceType = string.IsNullOrWhiteSpace(template.BackgroundImageData) ? string.Empty : "Image";
        page.BackgroundSourceName = string.IsNullOrWhiteSpace(template.BackgroundImageData) ? string.Empty : template.SourceName;
        page.BackgroundPageNumber = 0;
        page.BackgroundRotation = 0;
        page.BackgroundCropLeft = page.BackgroundCropTop = page.BackgroundCropRight = page.BackgroundCropBottom = 0;
        page.ModifiedAt = DateTimeOffset.Now;
    }

    private async Task DeleteSharedPaperTemplateAsync(Guid templateId)
    {
        var template = _sharedPaperTemplates.FirstOrDefault(item => item.Id == templateId);
        if (template is null || _isReadOnly) return;
        _sharedPaperTemplates.Remove(template);
        await _paperTemplateLibraryService.SaveAsync(_sharedPaperTemplates);
        UpdateSharedPaperTemplatesButton();
        StatusText.Text = $"已删除共享纸张模板：{template.Name}";
    }
}
