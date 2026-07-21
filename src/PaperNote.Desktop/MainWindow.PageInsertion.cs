using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PaperNote.Core.Models;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private void ShowAddPageMenu(Button button)
    {
        var menu = new ContextMenu();
        var presets = _currentNotebook?.PaperPresets ?? [];
        if (presets.Count > 0)
        {
            var presetMenu = new MenuItem { Header = "常用纸张（当前页后）", IsEnabled = !_isReadOnly };
            foreach (var preset in presets) presetMenu.Items.Add(CreatePaperPresetMenuItem(preset, "AfterCurrent"));
            menu.Items.Add(presetMenu);
        }
        else
        {
            menu.Items.Add(new MenuItem { Header = "常用纸张（尚未收藏）", IsEnabled = false });
        }

        menu.Items.Add(CreateMenuItem("沿用当前纸张，在当前页后添加", "Ctrl+Enter", (_, _) => InsertBlankPage(
            _currentPage?.PaperTemplate ?? PaperPageDefaults.Template,
            _currentPage?.PaperColor ?? PaperPageDefaults.Color,
            "AfterCurrent"), !_isReadOnly));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("☆ 收藏当前纸张", "", (_, _) => AddCurrentPaperPreset(), !_isReadOnly && _currentPage is not null));

        var managePresets = new MenuItem { Header = "管理常用纸张", IsEnabled = !_isReadOnly && presets.Count > 0 };
        foreach (var preset in presets)
        {
            var capturedId = preset.Id;
            managePresets.Items.Add(CreateMenuItem($"删除 · {GetPaperPresetDisplayName(preset)}", "", (_, _) => RemovePaperPreset(capturedId), true));
        }
        menu.Items.Add(managePresets);
        menu.Items.Add(new Separator());
        menu.Items.Add(CreatePageTemplateMenu("在当前页后添加", "AfterCurrent"));
        menu.Items.Add(CreatePageTemplateMenu("在当前页前添加", "BeforeCurrent"));
        menu.Items.Add(CreatePageTemplateMenu("添加到笔记末尾", "End"));
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    private MenuItem CreatePageTemplateMenu(string header, string placement)
    {
        var menu = new MenuItem { Header = header, IsEnabled = !_isReadOnly };
        if (_currentNotebook is { PaperPresets.Count: > 0 })
        {
            var favorites = new MenuItem { Header = "常用纸张" };
            foreach (var preset in _currentNotebook.PaperPresets) favorites.Items.Add(CreatePaperPresetMenuItem(preset, placement));
            menu.Items.Add(favorites);
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(CreatePageTemplateItem("空白纸", "Blank", placement));
        menu.Items.Add(CreatePageTemplateItem("点阵纸", "Dotted", placement));
        menu.Items.Add(CreatePageTemplateItem("横线纸", "Lined", placement));
        menu.Items.Add(CreatePageTemplateItem("方格纸", "Grid", placement));
        return menu;
    }

    private MenuItem CreatePageTemplateItem(string header, string template, string placement)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => InsertBlankPage(template, _currentPage?.PaperColor ?? PaperPageDefaults.Color, placement);
        return item;
    }

    private MenuItem CreatePaperPresetMenuItem(PaperPreset preset, string placement)
    {
        var item = new MenuItem { Header = GetPaperPresetDisplayName(preset) };
        item.Click += (_, _) => InsertBlankPage(preset.PaperTemplate, preset.PaperColor, placement);
        return item;
    }

    private bool AddCurrentPaperPreset()
    {
        if (_currentNotebook is null || _currentPage is null || _isReadOnly) return false;
        _currentNotebook.PaperPresets ??= [];
        var exists = _currentNotebook.PaperPresets.Any(preset =>
            string.Equals(preset.PaperTemplate, _currentPage.PaperTemplate, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(preset.PaperColor, _currentPage.PaperColor, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            StatusText.Text = "当前纸张已经在常用纸张中";
            return false;
        }
        if (_currentNotebook.PaperPresets.Count >= 8)
        {
            StatusText.Text = "常用纸张最多收藏 8 个，请先删除一个";
            return false;
        }

        var preset = new PaperPreset
        {
            PaperTemplate = _currentPage.PaperTemplate,
            PaperColor = _currentPage.PaperColor
        };
        _currentNotebook.PaperPresets.Add(preset);
        MarkDirty();
        StatusText.Text = $"已收藏常用纸张 · {GetPaperPresetDisplayName(preset)}";
        return true;
    }

    private bool RemovePaperPreset(Guid presetId)
    {
        if (_currentNotebook is null || _isReadOnly) return false;
        var preset = _currentNotebook.PaperPresets.FirstOrDefault(item => item.Id == presetId);
        if (preset is null) return false;
        _currentNotebook.PaperPresets.Remove(preset);
        MarkDirty();
        StatusText.Text = $"已删除常用纸张 · {GetPaperPresetDisplayName(preset)}";
        return true;
    }

    private static string GetPaperPresetDisplayName(PaperPreset preset) =>
        $"{GetPaperTemplateDisplayName(preset.PaperTemplate)} · {GetPaperColorDisplayName(preset.PaperColor)}";

    private static string GetPaperColorDisplayName(string paperColor) => paperColor.ToUpperInvariant() switch
    {
        "#FFFFFF" => "白色",
        "#FFFBEA" => "米黄",
        "#F2F7FF" => "浅蓝",
        "#F2FAF3" => "浅绿",
        _ => paperColor
    };

    private bool InsertBlankPage(string paperTemplate, string paperColor, string placement)
    {
        if (_currentNotebook is null || _isReadOnly) return false;
        CaptureCurrentPage();
        var currentIndex = _currentPage is null ? _currentNotebook.Pages.Count - 1 : _currentNotebook.Pages.IndexOf(_currentPage);
        var insertIndex = placement switch
        {
            "BeforeCurrent" => Math.Clamp(currentIndex, 0, _currentNotebook.Pages.Count),
            "End" => _currentNotebook.Pages.Count,
            _ => Math.Clamp(currentIndex + 1, 0, _currentNotebook.Pages.Count)
        };
        var newPage = new NotebookPage
        {
            PaperTemplate = paperTemplate is "Blank" or "Dotted" or "Lined" or "Grid" ? paperTemplate : "Dotted",
            PaperColor = string.IsNullOrWhiteSpace(paperColor) ? PaperPageDefaults.Color : paperColor
        };
        _currentNotebook.Pages.Insert(insertIndex, newPage);
        _currentNotebook.CurrentPageId = newPage.Id;
        _currentPage = newPage;
        RefreshPageItems(newPage.Id);
        LoadPage(newPage);
        RefreshOutlineIfVisible();
        MarkDirty();
        StatusText.Text = $"已在第 {insertIndex + 1} 页插入{GetPaperTemplateDisplayName(newPage.PaperTemplate)}";
        return true;
    }
}
