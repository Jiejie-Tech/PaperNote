using PaperNote.Core.Models;
using PaperNote.Core.Services;

namespace PaperNote.Mobile.Pages;

public sealed partial class EditorPage
{
    private async Task PdfStudySearchAsync()
    {
        var notebook = _repository.Current?.Document;
        if (notebook is null) return;
        var query = await DisplayPromptAsync("PDF 文本搜索", "搜索 PDF 文本层、文字对象、OCR/手写识别文本和文字评论。", "搜索", "取消", keyboard: Keyboard.Text);
        if (string.IsNullOrWhiteSpace(query)) return;
        var search = new OfflineSearchService();
        search.Index(notebook);
        var hits = search.Search(query, 200).Where(hit => hit.PageId != Guid.Empty).ToArray();
        if (hits.Length == 0)
        {
            await DisplayAlertAsync("没有结果", "没有找到匹配内容。扫描版 PDF 需要先经过离线 OCR 才能搜索文字。", "知道了");
            return;
        }
        var labels = hits.Select(hit => $"第 {hit.PageNumber} 页 · {hit.Source} · {Compact(hit.Snippet, 54)}").ToArray();
        var choice = await DisplayActionSheetAsync($"找到 {hits.Length} 个结果", "取消", null, labels);
        var index = Array.IndexOf(labels, choice);
        if (index >= 0) LoadPage(notebook.Pages.First(page => page.Id == hits[index].PageId));
    }

    private async Task PdfBookmarksAndOutlineAsync()
    {
        var notebook = _repository.Current?.Document;
        if (notebook is null || _page is null) return;
        var action = await DisplayActionSheetAsync("书签与大纲", "取消", null,
            _page.IsBookmarked ? "取消当前页书签" : "为当前页添加书签",
            "当前页不加入目录", "设为一级标题", "设为二级标题", "设为三级标题", "浏览书签", "浏览全部目录");
        switch (action)
        {
            case "为当前页添加书签": PageBatchService.SetBookmark(_page, true); await SavePdfStudyChangeAsync("已添加当前页书签"); break;
            case "取消当前页书签": PageBatchService.SetBookmark(_page, false); await SavePdfStudyChangeAsync("已取消当前页书签"); break;
            case "当前页不加入目录": _page.OutlineLevel = 0; await SavePdfStudyChangeAsync("当前页已移出目录"); break;
            case "设为一级标题": _page.OutlineLevel = 1; await SavePdfStudyChangeAsync("当前页已设为一级标题"); break;
            case "设为二级标题": _page.OutlineLevel = 2; await SavePdfStudyChangeAsync("当前页已设为二级标题"); break;
            case "设为三级标题": _page.OutlineLevel = 3; await SavePdfStudyChangeAsync("当前页已设为三级标题"); break;
            case "浏览书签": await BrowseBookmarksAsync(notebook); break;
            case "浏览全部目录": await BrowseOutlineAsync(notebook); break;
        }
    }

    private async Task BrowseBookmarksAsync(NotebookDocument notebook)
    {
        var pages = notebook.Pages.Select((page, index) => (page, index)).Where(item => item.page.IsBookmarked).ToArray();
        if (pages.Length == 0) { await DisplayAlertAsync("书签", "当前笔记本还没有书签。", "知道了"); return; }
        var labels = pages.Select(item => $"第 {item.index + 1} 页 · {PageDisplayTitle(item.page, item.index)}").ToArray();
        var choice = await DisplayActionSheetAsync("书签", "取消", null, labels);
        var selected = Array.IndexOf(labels, choice);
        if (selected >= 0) LoadPage(pages[selected].page);
    }

    private async Task BrowseOutlineAsync(NotebookDocument notebook)
    {
        var items = new List<(NotebookPage Page, int PageNumber, int Level, string Title, bool Imported)>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < notebook.Pages.Count; i++)
        {
            var page = notebook.Pages[i];
            if (page.OutlineLevel <= 0) continue;
            var title = PageDisplayTitle(page, i);
            keys.Add($"{page.Id:N}|{page.OutlineLevel}|{title}");
            items.Add((page, i + 1, page.OutlineLevel, title, false));
        }
        foreach (var entry in notebook.OutlineEntries)
        {
            if (!entry.TargetPageId.HasValue) continue;
            var index = notebook.Pages.FindIndex(page => page.Id == entry.TargetPageId.Value);
            if (index < 0) continue;
            var level = Math.Clamp(entry.Level, 1, 6);
            var title = string.IsNullOrWhiteSpace(entry.Title) ? PageDisplayTitle(notebook.Pages[index], index) : entry.Title.Trim();
            if (!keys.Add($"{entry.TargetPageId.Value:N}|{level}|{title}")) continue;
            items.Add((notebook.Pages[index], index + 1, level, title, entry.IsImported));
        }
        items = items.OrderBy(item => item.PageNumber).ThenBy(item => item.Level).ToList();
        if (items.Count == 0) { await DisplayAlertAsync("目录", "当前笔记本还没有目录项。", "知道了"); return; }
        var labels = items.Select(item => $"{(item.Imported ? "PDF" : "笔记")} {item.Level} 级 · 第 {item.PageNumber} 页 · {Compact(item.Title, 48)}").ToArray();
        var choice = await DisplayActionSheetAsync("笔记目录", "取消", null, labels);
        var selected = Array.IndexOf(labels, choice);
        if (selected >= 0) LoadPage(items[selected].Page);
    }

    private async Task PdfInternalLinksAsync()
    {
        var notebook = _repository.Current?.Document;
        if (notebook is null || _page is null) return;
        PdfDocumentContentService.ResolveInternalLinks(notebook);
        if (_page.PdfLinks.Count == 0) { await DisplayAlertAsync("PDF 内部链接", "当前页没有可用的内部链接。", "知道了"); return; }
        var labels = _page.PdfLinks.Select(link => string.IsNullOrWhiteSpace(link.Label) ? $"前往 PDF 原第 {link.TargetSourcePageNumber} 页" : Compact(link.Label, 58)).ToArray();
        var choice = await DisplayActionSheetAsync("当前页内部链接", "取消", null, labels);
        var index = Array.IndexOf(labels, choice);
        if (index < 0) return;
        var selected = _page.PdfLinks[index];
        if (!selected.TargetPageId.HasValue)
        {
            await DisplayAlertAsync("目标页未导入", $"PDF 原第 {selected.TargetSourcePageNumber} 页尚未导入到当前笔记本。", "知道了");
            return;
        }
        var target = notebook.Pages.FirstOrDefault(page => page.Id == selected.TargetPageId.Value);
        if (target is not null) LoadPage(target);
    }

    private async Task PdfAnnotationsAsync()
    {
        var notebook = _repository.Current?.Document;
        if (notebook is null || _page is null) return;
        var action = await DisplayActionSheetAsync("批注列表", "取消", null, "浏览全部批注", "按类型筛选", "添加当前页文字评论", "删除文字评论");
        switch (action)
        {
            case "浏览全部批注": await BrowseAnnotationsAsync(notebook, null); break;
            case "按类型筛选":
                var kindChoice = await DisplayActionSheetAsync("批注类型", "取消", null, "文字评论", "荧光笔", "钢笔", "文字", "图片", "形状");
                var kind = kindChoice switch { "文字评论" => PageAnnotationKind.Comment, "荧光笔" => PageAnnotationKind.Highlighter, "钢笔" => PageAnnotationKind.Pen, "文字" => PageAnnotationKind.Text, "图片" => PageAnnotationKind.Image, "形状" => PageAnnotationKind.Shape, _ => (PageAnnotationKind?)null };
                if (kind.HasValue) await BrowseAnnotationsAsync(notebook, kind);
                break;
            case "添加当前页文字评论": await AddPdfCommentAsync(); break;
            case "删除文字评论": await DeletePdfCommentAsync(notebook); break;
        }
    }

    private async Task BrowseAnnotationsAsync(NotebookDocument notebook, PageAnnotationKind? kind)
    {
        var items = PageAnnotationService.Build(notebook, kind).Take(300).ToArray();
        if (items.Length == 0) { await DisplayAlertAsync("批注列表", "没有符合条件的批注。", "知道了"); return; }
        var labels = items.Select(item => $"第 {item.PageNumber} 页 · {AnnotationKindName(item.Kind)} · {Compact(item.Preview, 48)}").ToArray();
        var choice = await DisplayActionSheetAsync($"批注列表 · {items.Length} 条", "取消", null, labels);
        var index = Array.IndexOf(labels, choice);
        if (index >= 0) LoadPage(notebook.Pages.First(page => page.Id == items[index].PageId));
    }

    private async Task AddPdfCommentAsync()
    {
        if (_page is null) return;
        var text = await DisplayPromptAsync("添加文字评论", "评论保存在 .papernote 中，不会修改原 PDF。", "添加", "取消", maxLength: 2000, keyboard: Keyboard.Text);
        if (string.IsNullOrWhiteSpace(text)) return;
        var colorChoice = await DisplayActionSheetAsync("评论颜色", "取消", null, "黄色", "蓝色", "红色", "绿色");
        var color = colorChoice switch { "蓝色" => "#3978F6", "红色" => "#D64545", "绿色" => "#1E8F65", _ => "#F0B429" };
        PageAnnotationService.AddComment(_page, text, color);
        await SavePdfStudyChangeAsync("已添加当前页文字评论");
    }

    private async Task DeletePdfCommentAsync(NotebookDocument notebook)
    {
        var items = PageAnnotationService.Build(notebook, PageAnnotationKind.Comment).ToArray();
        if (items.Length == 0) { await DisplayAlertAsync("文字评论", "当前笔记本没有文字评论。", "知道了"); return; }
        var labels = items.Select(item => $"第 {item.PageNumber} 页 · {Compact(item.Preview, 54)}").ToArray();
        var choice = await DisplayActionSheetAsync("选择要删除的评论", "取消", null, labels);
        var index = Array.IndexOf(labels, choice);
        if (index < 0) return;
        var page = notebook.Pages.First(page => page.Id == items[index].PageId);
        if (PageAnnotationService.DeleteComment(page, items[index].Id)) await SavePdfStudyChangeAsync("已删除文字评论");
    }

    private async Task PdfPageBatchAsync(string? forcedAction = null)
    {
        var notebook = _repository.Current?.Document;
        if (notebook is null) return;
        var pageNumbers = await PromptNotebookPageRangeAsync("选择要批量处理的页码");
        if (pageNumbers is null) return;
        var pages = pageNumbers.Select(number => notebook.Pages[number - 1]).ToArray();
        var ids = pages.Select(page => page.Id).ToHashSet();
        var action = forcedAction ?? await DisplayActionSheetAsync($"已选择 {pages.Length} 页", "取消", null,
            "左转 90°", "右转 90°", "复制所选页面", "删除所选页面", "移至笔记开头", "移至笔记末尾", "添加书签", "取消书签", "导出所选页面为 PDF", "提取为新笔记本");
        switch (action)
        {
            case "左转 90°": foreach (var page in pages.Where(IsPdfPage)) PageBatchService.RotateBackground(page, -1); await RefreshAfterPageBatchAsync(pages[0], "已批量左转 PDF 页面"); break;
            case "右转 90°": foreach (var page in pages.Where(IsPdfPage)) PageBatchService.RotateBackground(page, 1); await RefreshAfterPageBatchAsync(pages[0], "已批量右转 PDF 页面"); break;
            case "复制所选页面":
                var copies = PageBatchService.Duplicate(notebook, ids);
                if (copies.Count > 0) await RefreshAfterPageBatchAsync(copies[0], $"已复制 {copies.Count} 页");
                break;
            case "删除所选页面":
                if (ids.Count >= notebook.Pages.Count) { await DisplayAlertAsync("无法删除", "每个笔记本至少需要保留一页。", "知道了"); return; }
                if (await DisplayAlertAsync("删除页面", $"确定删除所选的 {ids.Count} 页吗？", "删除", "取消") && PageBatchService.Delete(notebook, ids))
                    await RefreshAfterPageBatchAsync(notebook.Pages.First(page => page.Id == notebook.CurrentPageId), $"已删除 {ids.Count} 页");
                break;
            case "移至笔记开头": if (PageBatchService.MoveToStart(notebook, ids)) await RefreshAfterPageBatchAsync(pages[0], $"已将 {pages.Length} 页移至开头"); break;
            case "移至笔记末尾": if (PageBatchService.MoveToEnd(notebook, ids)) await RefreshAfterPageBatchAsync(pages[0], $"已将 {pages.Length} 页移至末尾"); break;
            case "添加书签": foreach (var page in pages) PageBatchService.SetBookmark(page, true); await RefreshAfterPageBatchAsync(pages[0], $"已为 {pages.Length} 页添加书签"); break;
            case "取消书签": foreach (var page in pages) PageBatchService.SetBookmark(page, false); await RefreshAfterPageBatchAsync(pages[0], $"已取消 {pages.Length} 页书签"); break;
            case "导出所选页面为 PDF": await _pdf.ExportAndShareAsync(notebook, pages); break;
            case "提取为新笔记本": await ShareExtractedNotebookAsync(notebook, ids); break;
        }
    }

    private async Task<IReadOnlyList<int>?> PromptNotebookPageRangeAsync(string title)
    {
        var notebook = _repository.Current?.Document;
        if (notebook is null) return null;
        var input = await DisplayPromptAsync(title, $"共 {notebook.Pages.Count} 页。可输入 1-5,8,10-12。", "继续", "取消", PdfPageRangeService.DefaultSelection(notebook.Pages.Count), maxLength: 200, keyboard: Keyboard.Text);
        if (input is null) return null;
        try { return PdfPageRangeService.Parse(input, notebook.Pages.Count); }
        catch (ArgumentException exception) { await DisplayAlertAsync("页码范围无效", exception.Message, "知道了"); return null; }
    }

    private async Task ShareExtractedNotebookAsync(NotebookDocument notebook, IReadOnlySet<Guid> ids)
    {
        var extracted = PageBatchService.ExtractDocument(notebook, ids, $"{notebook.Title} - 提取页面");
        var fileName = $"PaperNote-Extract-{DateTime.Now:yyyyMMdd-HHmmss}.papernote";
        var output = Path.Combine(FileSystem.CacheDirectory, fileName);
        await _repository.Storage.SaveAsync(extracted, output);
        await Share.Default.RequestAsync(new ShareFileRequest { Title = $"导出 {extracted.Title}", File = new ShareFile(output, "application/octet-stream") });
        _pageStatus.Text = $"已提取 {extracted.Pages.Count} 页为新笔记本";
    }

    private async Task RefreshAfterPageBatchAsync(NotebookPage preferred, string status)
    {
        var notebook = _repository.Current!.Document;
        if (!notebook.Pages.Any(page => page.Id == preferred.Id)) preferred = notebook.Pages.First(page => page.Id == notebook.CurrentPageId);
        notebook.CurrentPageId = preferred.Id;
        RefreshPageCards();
        LoadPage(preferred);
        await _repository.SaveCurrentAsync();
        _pageStatus.Text = status;
    }

    private async Task SavePdfStudyChangeAsync(string status)
    {
        if (_page is not null) _page.ModifiedAt = DateTimeOffset.Now;
        RefreshPageCards();
        await _repository.SaveCurrentAsync();
        _pageStatus.Text = status;
    }

    private static bool IsPdfPage(NotebookPage page) => page.BackgroundSourceType == "PDF" && !string.IsNullOrWhiteSpace(page.BackgroundImageData);
    private static string PageDisplayTitle(NotebookPage page, int zeroBasedIndex) => string.IsNullOrWhiteSpace(page.Title) ? $"第 {zeroBasedIndex + 1} 页" : page.Title.Trim();
    private static string Compact(string? text, int length)
    {
        var value = string.Join(' ', (text ?? string.Empty).Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return value.Length <= length ? value : value[..length] + "…";
    }
    private static string AnnotationKindName(PageAnnotationKind kind) => kind switch { PageAnnotationKind.Comment => "评论", PageAnnotationKind.Highlighter => "荧光笔", PageAnnotationKind.Pen => "钢笔", PageAnnotationKind.Text => "文字", PageAnnotationKind.Image => "图片", PageAnnotationKind.Shape => "形状", _ => kind.ToString() };
}
