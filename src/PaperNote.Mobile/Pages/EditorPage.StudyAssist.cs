using PaperNote.Core.Models;
using PaperNote.Core.Services;
using PaperNote.Mobile.Controls;

namespace PaperNote.Mobile.Pages;

public sealed partial class EditorPage
{
    private bool _mobilePresentationMode;

    private async Task ShowStudyAssistAsync()
    {
        if (_page is null) return;
        var choice = await DisplayActionSheetAsync("课堂与复习工具", "取消", null,
            "添加胶带遮挡", "常用元素", "激光笔", _mobilePresentationMode ? "退出演示模式" : "进入演示模式");
        switch (choice)
        {
            case "添加胶带遮挡":
                AddStudyObjects([StudyAssistService.CreateTape()]);
                break;
            case "常用元素":
                await AddStudyElementAsync();
                break;
            case "激光笔":
                SelectTool(InkCanvasTool.Laser);
                break;
            case "进入演示模式":
                _mobilePresentationMode = true;
                _mainGrid.ColumnDefinitions[0].Width = 0;
                SelectTool(InkCanvasTool.Laser);
                await DisplayAlertAsync("演示模式", "已隐藏页面侧栏并启用激光笔。激光轨迹不会写入笔记。", "知道了");
                break;
            case "退出演示模式":
                _mobilePresentationMode = false;
                _mainGrid.ColumnDefinitions[0].Width = Width >= 900 ? 230 : 0;
                SelectTool(InkCanvasTool.Pen);
                break;
        }
    }

    private async Task AddStudyElementAsync()
    {
        var choice = await DisplayActionSheetAsync("常用元素", "取消", null, "重点 !", "疑问 ？", "完成 ✓", "星标 ★", "分隔线", "公式框");
        var kind = choice switch
        {
            "重点 !" => "Important",
            "疑问 ？" => "Question",
            "完成 ✓" => "Check",
            "星标 ★" => "Star",
            "分隔线" => "Divider",
            "公式框" => "Formula",
            _ => string.Empty
        };
        if (kind.Length > 0) AddStudyObjects(StudyAssistService.CreateElement(kind));
    }

    private void AddStudyObjects(IReadOnlyList<PageObject> objects)
    {
        if (_page is null || objects.Count == 0) return;
        var layer = PageLayerService.EnsureDefault(_page).Id;
        foreach (var item in objects)
        {
            item.LayerId = layer;
            _page.Objects.Add(item);
        }
        _page.ModifiedAt = DateTimeOffset.Now;
        _canvas.Page = null;
        _canvas.Page = _page;
        SelectTool(InkCanvasTool.Select);
        _canvas.SelectObject(objects[^1].Id);
        ScheduleSave();
    }

    private async Task JumpSelectedStrokeToAudioAsync()
    {
        if (_page is null) return;
        var strokeId = _canvas.SelectedStrokeIds.FirstOrDefault();
        var location = StudyAssistService.FindAudioForStroke(_page, strokeId);
        if (location is null)
        {
            await DisplayAlertAsync("没有关联录音", "选中的笔迹没有录音时间点。只有录音期间书写的笔迹才能反向跳转。", "知道了");
            return;
        }
        PlayRecording(location.Recording, location.Cue.OffsetMilliseconds);
    }
}
