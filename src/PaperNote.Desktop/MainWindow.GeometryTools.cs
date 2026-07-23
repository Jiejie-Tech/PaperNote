using System.Windows;
using System.Windows.Controls;
using PaperNote.Core.Ink;
using PaperNote.Core.Services;
using PaperNote.Desktop.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private MenuItem CreateGeometryToolsMenu()
    {
        var root = new MenuItem { Header = "标尺与测量", IsEnabled = _currentPage is not null };
        root.Items.Add(CreateMenuItem("测量当前选区", "", (_, _) => MeasureCurrentSelection(), HasMixedSelection()));
        root.Items.Add(new Separator());
        var rulers = new MenuItem { Header = "插入刻度标尺", IsEnabled = !_isReadOnly };
        foreach (var (label, length, angle) in new[]
        {
            ("横向 100 mm", 100d, 0d), ("纵向 100 mm", 100d, 90d), ("45° 100 mm", 100d, -45d), ("横向 150 mm", 150d, 0d)
        })
        {
            var capturedLength = length; var capturedAngle = angle;
            rulers.Items.Add(CreateMenuItem(label, "", (_, _) => InsertRuler(capturedLength, capturedAngle), !_isReadOnly));
        }
        root.Items.Add(rulers);
        var angles = new MenuItem { Header = "插入角度辅助线", IsEnabled = !_isReadOnly };
        foreach (var value in new[] { 30d, 45d, 60d, 90d, 120d })
        {
            var captured = value;
            angles.Items.Add(CreateMenuItem($"{captured:0}°", "", (_, _) => InsertAngleGuide(captured), !_isReadOnly));
        }
        root.Items.Add(angles);
        root.Items.Add(new Separator());
        root.Items.Add(CreateMenuItem("根据选中直线构造平行线", "", (_, _) => InsertParallelLine(), !_isReadOnly && GetSelectedReferenceStroke() is not null));
        root.Items.Add(CreateMenuItem("根据选中直线构造垂线", "", (_, _) => InsertPerpendicularLine(), !_isReadOnly && GetSelectedReferenceStroke() is not null));
        return root;
    }

    private void MeasureCurrentSelection()
    {
        if (_currentPage is null) return;
        CaptureCurrentPage();
        var result = GeometryMeasurementService.MeasureSelection(_currentPage, GetSelectedStrokeIds(), _selectedPageObjectIds);
        if (result is null)
        {
            MessageBox.Show(this, "请先用套索选择笔迹、文字、图片或形状。", "标尺与测量", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MessageBox.Show(this, result.ToDisplayText(), "选区测量结果", MessageBoxButton.OK, MessageBoxImage.Information);
        StatusText.Text = $"选区测量：{result.DirectDistanceMillimeters:0.0} mm · {result.AngleDegrees:0.0}°";
    }

    private void InsertRuler(double lengthMillimeters, double angleDegrees)
    {
        if (_currentPage is null) return;
        CaptureCurrentPage();
        var layer = PageLayerService.EnsureDefault(_currentPage).Id;
        var strokes = GeometryMeasurementService.CreateRuler(170, 360, lengthMillimeters, angleDegrees, _currentColor.ToString(), Math.Max(1.5, _penThickness * .55), layer);
        AddConstructedStrokes(strokes, $"已插入 {lengthMillimeters:0} mm 刻度标尺");
    }

    private void InsertAngleGuide(double angleDegrees)
    {
        if (_currentPage is null) return;
        CaptureCurrentPage();
        var layer = PageLayerService.EnsureDefault(_currentPage).Id;
        var strokes = GeometryMeasurementService.CreateAngleGuide(330, 520, angleDegrees, 45, _currentColor.ToString(), Math.Max(1.5, _penThickness * .55), layer);
        AddConstructedStrokes(strokes, $"已插入 {angleDegrees:0}° 角度辅助线");
    }

    private PaperInkStroke? GetSelectedReferenceStroke()
    {
        if (_currentPage is null) return null;
        CaptureCurrentPage();
        var id = GetSelectedStrokeIds().FirstOrDefault();
        return id == Guid.Empty ? null : _currentPage.Ink.Strokes.FirstOrDefault(stroke => stroke.Id == id && stroke.Points.Count >= 2);
    }

    private void InsertParallelLine()
    {
        if (_currentPage is null) return;
        var reference = GetSelectedReferenceStroke();
        if (reference is null) return;
        var line = GeometryMeasurementService.CreateParallelLine(reference, 10, _currentColor.ToString(), PageLayerService.EnsureDefault(_currentPage).Id);
        AddConstructedStrokes([line], "已在参考线 10 mm 处构造平行线");
    }

    private void InsertPerpendicularLine()
    {
        if (_currentPage is null) return;
        var reference = GetSelectedReferenceStroke();
        if (reference is null) return;
        var line = GeometryMeasurementService.CreatePerpendicularLine(reference, 50, _currentColor.ToString(), PageLayerService.EnsureDefault(_currentPage).Id);
        AddConstructedStrokes([line], "已在参考线中点构造 50 mm 垂线");
    }

    private void AddConstructedStrokes(IReadOnlyList<PaperInkStroke> strokes, string status)
    {
        if (_currentPage is null || strokes.Count == 0) return;
        _history.Record(InkSurface.Strokes);
        _currentPage.Ink.Strokes.AddRange(strokes);
        RefreshCurrentPageFromModel(strokes.Select(stroke => stroke.Id), Array.Empty<Guid>());
        UpdateHistoryButtons();
        StatusText.Text = status;
    }
}
