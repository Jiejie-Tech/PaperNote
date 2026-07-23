using PaperNote.Core.Ink;
using PaperNote.Core.Services;
using PaperNote.Mobile.Controls;

namespace PaperNote.Mobile.Pages;

public sealed partial class EditorPage
{
    private async Task ShowGeometryToolsAsync()
    {
        if (_page is null) return;
        var action = await DisplayActionSheetAsync("标尺与测量", "取消", null,
            "测量当前选区", "插入横向 100 mm 标尺", "插入纵向 100 mm 标尺", "插入 45° 标尺",
            "插入角度辅助线", "构造平行线", "构造垂线");
        switch (action)
        {
            case "测量当前选区":
                var measurement = GeometryMeasurementService.MeasureSelection(_page, _canvas.SelectedStrokeIds, _canvas.SelectedObjectIds);
                await DisplayAlertAsync("选区测量结果", measurement?.ToDisplayText() ?? "请先用套索选择笔迹、文字、图片或形状。", "完成");
                if (measurement is not null) _pageStatus.Text = $"选区测量：{measurement.DirectDistanceMillimeters:0.0} mm · {measurement.AngleDegrees:0.0}°";
                break;
            case "插入横向 100 mm 标尺": AddGeometryStrokes(GeometryMeasurementService.CreateRuler(160, 360, 100, 0, _color, 2, PageLayerService.EnsureDefault(_page).Id), "已插入横向标尺"); break;
            case "插入纵向 100 mm 标尺": AddGeometryStrokes(GeometryMeasurementService.CreateRuler(240, 280, 100, 90, _color, 2, PageLayerService.EnsureDefault(_page).Id), "已插入纵向标尺"); break;
            case "插入 45° 标尺": AddGeometryStrokes(GeometryMeasurementService.CreateRuler(180, 560, 100, -45, _color, 2, PageLayerService.EnsureDefault(_page).Id), "已插入 45° 标尺"); break;
            case "插入角度辅助线":
                var angleChoice = await DisplayActionSheetAsync("角度辅助线", "取消", null, "30°", "45°", "60°", "90°", "120°");
                if (double.TryParse(angleChoice?.TrimEnd('°'), out var angle)) AddGeometryStrokes(GeometryMeasurementService.CreateAngleGuide(330, 560, angle, 45, _color, 2, PageLayerService.EnsureDefault(_page).Id), $"已插入 {angle:0}° 辅助线");
                break;
            case "构造平行线":
                var parallelReference = GetSelectedMobileReferenceStroke();
                if (parallelReference is null) await DisplayAlertAsync("构造平行线", "请先套索选择一条参考笔迹。", "知道了");
                else AddGeometryStrokes([GeometryMeasurementService.CreateParallelLine(parallelReference, 10, _color, PageLayerService.EnsureDefault(_page).Id)], "已构造相距 10 mm 的平行线");
                break;
            case "构造垂线":
                var perpendicularReference = GetSelectedMobileReferenceStroke();
                if (perpendicularReference is null) await DisplayAlertAsync("构造垂线", "请先套索选择一条参考笔迹。", "知道了");
                else AddGeometryStrokes([GeometryMeasurementService.CreatePerpendicularLine(perpendicularReference, 50, _color, PageLayerService.EnsureDefault(_page).Id)], "已在参考线中点构造垂线");
                break;
        }
    }

    private PaperInkStroke? GetSelectedMobileReferenceStroke()
    {
        if (_page is null) return null;
        var id = _canvas.SelectedStrokeIds.FirstOrDefault();
        return id == Guid.Empty ? null : _page.Ink.Strokes.FirstOrDefault(stroke => stroke.Id == id && stroke.Points.Count >= 2);
    }

    private void AddGeometryStrokes(IReadOnlyList<PaperInkStroke> strokes, string status)
    {
        if (_page is null || strokes.Count == 0) return;
        _page.Ink.Strokes.AddRange(strokes);
        _page.ModifiedAt = DateTimeOffset.Now;
        _canvas.Page = null;
        _canvas.Page = _page;
        _canvas.Document = _page.Ink;
        SelectTool(InkCanvasTool.Select);
        ScheduleSave();
        _pageStatus.Text = status;
    }
}
