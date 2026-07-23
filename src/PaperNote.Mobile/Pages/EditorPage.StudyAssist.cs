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
            "添加胶带遮挡", "常用元素", "我的素材", "标尺与测量", "激光笔", _mobilePresentationMode ? "退出演示模式" : "进入演示模式");
        switch (choice)
        {
            case "添加胶带遮挡":
                AddStudyObjects([StudyAssistService.CreateTape()]);
                break;
            case "常用元素":
                await AddStudyElementAsync();
                break;
            case "我的素材":
                await ShowSelectionMaterialsAsync();
                break;
            case "标尺与测量":
                await ShowGeometryToolsAsync();
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

    private async Task SaveSelectionAsMaterialAsync()
    {
        if (_page is null || _canvas.SelectedContentCount == 0) return;
        var materials = (await _repository.MaterialLibrary.LoadAsync()).ToList();
        if (materials.Count >= SelectionMaterialLibraryService.MaximumMaterials)
        {
            await DisplayAlertAsync("素材库已满", $"个人素材库最多保存 {SelectionMaterialLibraryService.MaximumMaterials} 项，请先到“课堂与复习工具 → 我的素材”删除不需要的素材。", "知道了");
            return;
        }

        var defaultName = $"个人素材 {DateTime.Now:MM-dd HHmm}";
        var name = await DisplayPromptAsync("保存到个人素材库", "素材名称", initialValue: defaultName, maxLength: 60, keyboard: Keyboard.Text);
        if (name is null) return;
        var material = SelectionMaterialLibraryService.Create(name, _page, _canvas.SelectedStrokeIds, _canvas.SelectedObjectIds);
        if (material is null)
        {
            await DisplayAlertAsync("无法保存素材", "当前选区没有可保存的内容。", "知道了");
            return;
        }

        materials.Insert(0, material);
        await _repository.MaterialLibrary.SaveAsync(materials);
        _pageStatus.Text = $"已保存到个人素材库：{material.Name}";
    }

    private async Task ShowSelectionMaterialsAsync()
    {
        var materials = (await _repository.MaterialLibrary.LoadAsync()).ToArray();
        if (materials.Length == 0)
        {
            await DisplayAlertAsync("我的素材", "素材库还是空的。先用套索选中笔迹、文字、图片或形状，再从“更多”中保存到个人素材库。", "知道了");
            return;
        }

        var labels = materials.Select((material, index) => $"{index + 1}. {SelectionMaterialDisplayName(material)}").ToArray();
        var choice = await DisplayActionSheetAsync($"我的素材 · {materials.Length}/{SelectionMaterialLibraryService.MaximumMaterials}", "取消", null, labels);
        var selectedIndex = Array.IndexOf(labels, choice);
        if (selectedIndex < 0) return;
        var selected = materials[selectedIndex];
        var action = await DisplayActionSheetAsync(selected.Name, "取消", null, "插入当前页", "删除素材");
        switch (action)
        {
            case "插入当前页":
                InsertSelectionMaterial(selected);
                break;
            case "删除素材":
                if (!await DisplayAlertAsync("删除素材", $"确定删除“{selected.Name}”吗？这不会删除已经插入笔记的内容。", "删除", "取消")) return;
                await _repository.MaterialLibrary.SaveAsync(materials.Where(item => item.Id != selected.Id));
                _pageStatus.Text = $"已删除个人素材：{selected.Name}";
                break;
        }
    }

    private void InsertSelectionMaterial(SelectionMaterial material)
    {
        if (_page is null) return;
        var layerId = PageLayerService.EnsureDefault(_page).Id;
        var placement = SelectionMaterialLibraryService.Instantiate(material, x: 150, y: 230, maximumWidth: 520, maximumHeight: 560, layerId: layerId);
        if (placement.Strokes.Count == 0 && placement.Objects.Count == 0) return;

        _page.Ink.Strokes.AddRange(placement.Strokes);
        _page.Objects.AddRange(placement.Objects);
        _page.ModifiedAt = DateTimeOffset.Now;
        _canvas.Page = null;
        _canvas.Page = _page;
        _canvas.Document = _page.Ink;
        SelectTool(InkCanvasTool.Select);
        if (placement.Objects.Count > 0) _canvas.SelectObject(placement.Objects[^1].Id);
        UpdatePageStatus();
        ScheduleSave();
        _pageStatus.Text = $"已插入个人素材：{material.Name}";
    }

    private static string SelectionMaterialDisplayName(SelectionMaterial material)
        => $"{material.Name} · {material.Strokes.Count} 笔迹/{material.Objects.Count} 对象";

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