using PaperNote.Core.Models;
using PaperNote.Core.Services;
using System.Windows;
using System.Windows.Controls;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private MenuItem CreatePageLinkSubmenu()
    {
        var selected = SelectedPageObjects().ToArray();
        var editable = selected.Length > 0 && selected.All(item => !item.IsLocked) && !_isReadOnly;
        var root = new MenuItem
        {
            Header = "页面链接",
            IsEnabled = selected.Length > 0
        };
        var linked = selected.FirstOrDefault(item => item.LinkTargetPageId.HasValue);
        root.Items.Add(CreateMenuItem("打开链接", "Ctrl+单击", (_, _) => OpenSelectedPageObjectLink(), linked is not null));
        root.Items.Add(CreateMenuItem("移除链接", "", (_, _) => SetSelectedPageObjectLink(null), editable && selected.Any(item => item.LinkTargetPageId.HasValue)));
        root.Items.Add(new Separator());

        if (_currentNotebook is null)
        {
            root.Items.Add(new MenuItem { Header = "没有可链接的页面", IsEnabled = false });
            return root;
        }

        if (_currentNotebook.Pages.Count <= 30)
        {
            AddPageLinkTargets(root, _currentNotebook.Pages.Select((page, index) => (page.Id, index, page.Title)), editable);
        }
        else
        {
            for (var start = 0; start < _currentNotebook.Pages.Count; start += 25)
            {
                var end = Math.Min(start + 25, _currentNotebook.Pages.Count);
                var group = new MenuItem { Header = $"第 {start + 1}–{end} 页", IsEnabled = editable };
                AddPageLinkTargets(group, _currentNotebook.Pages.Skip(start).Take(end - start).Select((page, offset) => (page.Id, start + offset, page.Title)), editable);
                root.Items.Add(group);
            }
        }
        return root;
    }

    private void AddPageLinkTargets(ItemsControl parent, IEnumerable<(Guid Id, int Index, string Title)> pages, bool enabled)
    {
        foreach (var page in pages)
        {
            var title = string.IsNullOrWhiteSpace(page.Title) ? string.Empty : $" · {page.Title}";
            var item = new MenuItem
            {
                Header = $"第 {page.Index + 1} 页{title}",
                Tag = page.Id,
                IsEnabled = enabled
            };
            item.Click += (_, _) => SetSelectedPageObjectLink(page.Id);
            parent.Items.Add(item);
        }
    }

    private bool SetSelectedPageObjectLink(Guid? targetPageId)
    {
        if (_currentNotebook is null || _currentPage is null || _isReadOnly) return false;
        if (targetPageId.HasValue && _currentNotebook.Pages.All(page => page.Id != targetPageId.Value)) return false;
        var selected = SelectedPageObjects().Where(item => !item.IsLocked).ToArray();
        if (selected.Length == 0) return false;
        foreach (var pageObject in selected) pageObject.LinkTargetPageId = targetPageId;
        RebuildPageObjectContainers(selected.Select(item => item.Id));
        PageObjectChanged();
        StatusText.Text = targetPageId.HasValue
            ? $"已为 {selected.Length} 个对象设置页面链接；编辑模式下按 Ctrl 单击打开"
            : $"已移除 {selected.Length} 个对象的页面链接";
        return true;
    }

    private void RebuildPageObjectContainers(IEnumerable<Guid> selectedIds)
    {
        if (_currentPage is null) return;
        var ids = selectedIds.ToHashSet();
        LoadPageObjects(_currentPage);
        foreach (var id in ids)
        {
            if (_selectedPageObjectIds.Contains(id)) continue;
            if (_currentPage.Objects.FirstOrDefault(item => item.Id == id) is not { } pageObject) continue;
            if (!_pageObjectContainers.TryGetValue(id, out var container)) continue;
            SelectPageObject(pageObject, container, true);
        }
    }

    private bool OpenSelectedPageObjectLink()
    {
        var linked = SelectedPageObjects().FirstOrDefault(item => item.LinkTargetPageId.HasValue);
        return linked is not null && OpenPageObjectLink(linked);
    }

    private bool OpenPageObjectLink(PageObject pageObject)
    {
        if (_currentNotebook is null || !pageObject.LinkTargetPageId.HasValue) return false;
        var index = _currentNotebook.Pages.FindIndex(page => page.Id == pageObject.LinkTargetPageId.Value);
        if (index < 0)
        {
            StatusText.Text = "链接目标页面已经不存在";
            return false;
        }
        return NavigateToPage(index + 1);
    }

    private void RemoveBrokenPageLinks()
    {
        if (_currentNotebook is null) return;
        PageBatchService.CleanupNavigation(_currentNotebook);
    }
}
