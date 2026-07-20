using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using PaperNote.Desktop.Models;
using PaperNote.Desktop.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private readonly LibraryBackupPackageService _libraryBackupPackageService = new();
    private string _librarySort = "Modified";

    private void LibrarySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized) return;
        ApplyLibraryFilter();
    }

    private void LibrarySortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || LibrarySortCombo.SelectedItem is not ComboBoxItem { Tag: string sort }) return;
        _librarySort = sort is "Opened" or "Title" ? sort : "Modified";
        ApplyLibraryFilter();
        SaveWorkspaceStateSoon();
    }

    private static IReadOnlyList<(StoredNotebook Stored, string MatchSummary)> FilterAndSortLibraryNotebooks(
        IEnumerable<StoredNotebook> source,
        string selectedFilter,
        string? query,
        string sort)
    {
        IEnumerable<StoredNotebook> filtered = selectedFilter switch
        {
            "trash" => source.Where(item => item.Document.IsInTrash),
            "unfiled" => source.Where(item => !item.Document.IsInTrash && string.IsNullOrWhiteSpace(item.Document.FolderName)),
            "recent" => source.Where(item => !item.Document.IsInTrash && item.Document.LastOpenedAt.HasValue),
            "all" => source.Where(item => !item.Document.IsInTrash),
            _ when selectedFilter.StartsWith("folder:", StringComparison.OrdinalIgnoreCase) =>
                source.Where(item => !item.Document.IsInTrash && string.Equals(item.Document.FolderName, selectedFilter[7..], StringComparison.CurrentCultureIgnoreCase)),
            _ => source.Where(item => !item.Document.IsInTrash)
        };

        var matches = new List<(StoredNotebook Stored, string MatchSummary)>();
        foreach (var stored in filtered)
        {
            if (!NotebookContentService.TryMatch(stored.Document, query, out var summary)) continue;
            matches.Add((stored, summary));
        }

        return sort switch
        {
            "Title" => matches.OrderBy(item => item.Stored.Document.Title, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            "Opened" => matches.OrderByDescending(item => item.Stored.Document.LastOpenedAt ?? DateTimeOffset.MinValue)
                .ThenByDescending(item => item.Stored.Document.ModifiedAt).ToArray(),
            _ when selectedFilter == "recent" => matches.OrderByDescending(item => item.Stored.Document.LastOpenedAt ?? DateTimeOffset.MinValue)
                .ThenByDescending(item => item.Stored.Document.ModifiedAt).ToArray(),
            _ => matches.OrderByDescending(item => item.Stored.Document.IsInTrash ? item.Stored.Document.TrashedAt : item.Stored.Document.ModifiedAt).ToArray()
        };
    }

    private void LibraryBackupActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("导出整个资料库备份包", "", async (_, _) => await ExportLibraryBackupAsync(), true));
        menu.Items.Add(CreateMenuItem("从备份包导入并恢复", "", async (_, _) => await ImportLibraryBackupAsync(), true));
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private async Task ExportLibraryBackupAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出 PaperNote 资料库备份包",
            Filter = "PaperNote 资料库备份包|*.papernote-library.zip|ZIP 压缩包|*.zip",
            FileName = $"PaperNote资料库-{DateTime.Now:yyyyMMdd-HHmm}.papernote-library.zip",
            AddExtension = true,
            DefaultExt = ".zip"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            LibraryBackupButton.IsEnabled = false;
            LibraryStatusText.Text = "正在打包整个资料库…";
            var result = await _libraryBackupPackageService.ExportAsync(dialog.FileName, _notebookStorage.NotebooksDirectory, _notebookStorage.BackupsDirectory);
            LibraryStatusText.Text = $"资料库备份完成 · {result.NotebookCount} 本笔记 · {result.BackupCount} 个历史版本";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            MessageBox.Show(this, $"无法导出资料库备份包。\n\n{exception.Message}", "备份失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { LibraryBackupButton.IsEnabled = true; }
    }

    private async Task ImportLibraryBackupAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 PaperNote 资料库备份包",
            Filter = "PaperNote 资料库备份包|*.papernote-library.zip;*.zip|所有文件|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, "将从备份包导入笔记本和历史版本。现有同名且内容不同的笔记会作为副本保留，不会直接覆盖。\n\n继续吗？", "恢复资料库", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            LibraryBackupButton.IsEnabled = false;
            LibraryStatusText.Text = "正在验证并恢复资料库…";
            var result = await _libraryBackupPackageService.ImportAsync(dialog.FileName, _notebookStorage.NotebooksDirectory, _notebookStorage.BackupsDirectory);
            await RefreshLibraryAsync();
            LibraryStatusText.Text = $"恢复完成 · 导入 {result.ImportedNotebooks} 本 · 跳过相同 {result.SkippedNotebooks} 本 · 历史版本 {result.ImportedBackups} 个";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException)
        {
            MessageBox.Show(this, $"无法从这个备份包恢复资料库。\n\n{exception.Message}", "恢复失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { LibraryBackupButton.IsEnabled = true; }
    }
}
