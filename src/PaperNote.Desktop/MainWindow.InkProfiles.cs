using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private const string PenProfileBallpoint = "Ballpoint";
    private const string PenProfileFountain = "Fountain";
    private const string PenProfileBrush = "Brush";
    private const string PenProfilePencil = "Pencil";
    private const string PressureOff = "Off";
    private const string PressureSoft = "Soft";
    private const string PressureStandard = "Standard";
    private const string PressureStrong = "Strong";
    private const string SmoothingOff = "Off";
    private const string SmoothingStandard = "Standard";
    private const string SmoothingStrong = "Strong";

    private string _penProfile = PenProfileFountain;
    private string _pressureCurve = PressureStandard;
    private string _strokeSmoothing = SmoothingStandard;

    private void PenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = new ContextMenu();

        var profileMenu = new MenuItem { Header = "笔型" };
        AddCheckedMenuItem(profileMenu, "圆珠笔", PenProfileBallpoint, _penProfile, SetPenProfile);
        AddCheckedMenuItem(profileMenu, "钢笔", PenProfileFountain, _penProfile, SetPenProfile);
        AddCheckedMenuItem(profileMenu, "画笔", PenProfileBrush, _penProfile, SetPenProfile);
        AddCheckedMenuItem(profileMenu, "铅笔", PenProfilePencil, _penProfile, SetPenProfile);
        menu.Items.Add(profileMenu);

        var pressureMenu = new MenuItem { Header = "压感曲线" };
        AddCheckedMenuItem(pressureMenu, "关闭压感", PressureOff, _pressureCurve, SetPressureCurve);
        AddCheckedMenuItem(pressureMenu, "柔和", PressureSoft, _pressureCurve, SetPressureCurve);
        AddCheckedMenuItem(pressureMenu, "标准", PressureStandard, _pressureCurve, SetPressureCurve);
        AddCheckedMenuItem(pressureMenu, "强烈", PressureStrong, _pressureCurve, SetPressureCurve);
        menu.Items.Add(pressureMenu);

        var smoothingMenu = new MenuItem { Header = "笔迹平滑" };
        AddCheckedMenuItem(smoothingMenu, "关闭", SmoothingOff, _strokeSmoothing, SetStrokeSmoothing);
        AddCheckedMenuItem(smoothingMenu, "标准", SmoothingStandard, _strokeSmoothing, SetStrokeSmoothing);
        AddCheckedMenuItem(smoothingMenu, "强力", SmoothingStrong, _strokeSmoothing, SetStrokeSmoothing);
        menu.Items.Add(smoothingMenu);

        menu.Items.Add(new Separator());
        var presetMenu = new MenuItem { Header = "快捷笔预设" };
        presetMenu.Items.Add(CreateMenuItem("细黑圆珠笔", "", (_, _) => ApplyInkPreset("FineBlack"), true));
        presetMenu.Items.Add(CreateMenuItem("蓝色钢笔", "", (_, _) => ApplyInkPreset("BlueFountain"), true));
        presetMenu.Items.Add(CreateMenuItem("红色批注笔", "", (_, _) => ApplyInkPreset("RedReview"), true));
        presetMenu.Items.Add(CreateMenuItem("黄色荧光笔", "", (_, _) => ApplyInkPreset("YellowHighlighter"), true));
        menu.Items.Add(presetMenu);

        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private static void AddCheckedMenuItem(ItemsControl parent, string header, string value, string current, Action<string> apply)
    {
        var item = new MenuItem { Header = header, Tag = value, IsCheckable = true, IsChecked = value == current };
        item.Click += (_, _) => apply(value);
        parent.Items.Add(item);
    }

    private void SetPenProfile(string profile)
    {
        _penProfile = NormalizePenProfile(profile);
        UpdatePenSettingsButton();
        if (_activeTool == "Pen") ApplyCurrentTool();
        StatusText.Text = $"已切换为{GetPenProfileDisplayName(_penProfile)}";
    }

    private void SetPressureCurve(string curve)
    {
        _pressureCurve = NormalizePressureCurve(curve);
        UpdatePenSettingsButton();
        if (_activeTool == "Pen") ApplyCurrentTool();
        StatusText.Text = $"压感曲线：{GetPressureCurveDisplayName(_pressureCurve)}";
    }

    private void SetStrokeSmoothing(string smoothing)
    {
        _strokeSmoothing = NormalizeStrokeSmoothing(smoothing);
        UpdatePenSettingsButton();
        if (_activeTool == "Pen") ApplyCurrentTool();
        StatusText.Text = $"笔迹平滑：{GetStrokeSmoothingDisplayName(_strokeSmoothing)}";
    }

    private void UpdatePenSettingsButton()
    {
        if (PenSettingsButton is null) return;
        PenSettingsButton.Content = $"{GetPenProfileDisplayName(_penProfile)}⌄";
        PenSettingsButton.ToolTip = $"笔型：{GetPenProfileDisplayName(_penProfile)}；压感：{GetPressureCurveDisplayName(_pressureCurve)}；平滑：{GetStrokeSmoothingDisplayName(_strokeSmoothing)}";
    }

    private bool ApplyInkPreset(string preset)
    {
        switch (preset)
        {
            case "FineBlack":
                _penProfile = PenProfileBallpoint; _pressureCurve = PressureOff; _strokeSmoothing = SmoothingStandard; _penColor = Color.FromRgb(32, 33, 36); _penThickness = 2.2;
                _activeTool = "Pen"; PenTool.IsChecked = true;
                break;
            case "BlueFountain":
                _penProfile = PenProfileFountain; _pressureCurve = PressureStandard; _strokeSmoothing = SmoothingStandard; _penColor = Color.FromRgb(57, 120, 246); _penThickness = 3.2;
                _activeTool = "Pen"; PenTool.IsChecked = true;
                break;
            case "RedReview":
                _penProfile = PenProfileBallpoint; _pressureCurve = PressureSoft; _strokeSmoothing = SmoothingStrong; _penColor = Color.FromRgb(232, 74, 95); _penThickness = 3.8;
                _activeTool = "Pen"; PenTool.IsChecked = true;
                break;
            case "YellowHighlighter":
                _highlighterColor = Color.FromRgb(244, 197, 66); _highlighterThickness = 18;
                _activeTool = "Highlighter"; HighlighterTool.IsChecked = true;
                break;
            default:
                return false;
        }
        UpdatePenSettingsButton();
        RefreshActiveInkControls();
        ApplyCurrentTool();
        StatusText.Text = "已应用快捷笔预设";
        return true;
    }

    private void RefreshActiveInkControls()
    {
        if (_activeTool == "Pen")
        {
            _currentColor = _penColor;
            SelectColorButton(_penColor);
            ThicknessSlider.Maximum = 20;
            ThicknessSlider.Value = _penThickness;
        }
        else if (_activeTool == "Highlighter")
        {
            _currentColor = _highlighterColor;
            SelectColorButton(_highlighterColor);
            ThicknessSlider.Maximum = 40;
            ThicknessSlider.Value = _highlighterThickness;
        }
    }

    private DrawingAttributes CreatePenDrawingAttributes()
    {
        return CreatePenDrawingAttributes(_penColor, _penThickness, _penProfile, _pressureCurve, _strokeSmoothing);
    }

    private static DrawingAttributes CreatePenDrawingAttributes(Color color, double thickness, string profile, string pressureCurve, string smoothing)
    {
        profile = NormalizePenProfile(profile);
        pressureCurve = NormalizePressureCurve(pressureCurve);
        smoothing = NormalizeStrokeSmoothing(smoothing);
        thickness = Math.Clamp(thickness, 1, 20);
        var attributes = CreateDrawingAttributes(color, thickness, false);
        attributes.FitToCurve = smoothing != SmoothingOff;
        attributes.IgnorePressure = pressureCurve == PressureOff || profile == PenProfileBallpoint;

        switch (profile)
        {
            case PenProfileBallpoint:
                attributes.StylusTip = StylusTip.Ellipse;
                break;
            case PenProfileBrush:
                attributes.StylusTip = StylusTip.Ellipse;
                attributes.Width = thickness * 1.2;
                attributes.Height = thickness * 0.86;
                break;
            case PenProfilePencil:
                attributes.StylusTip = StylusTip.Ellipse;
                attributes.Color = Color.FromArgb(190, color.R, color.G, color.B);
                attributes.Width = Math.Max(1, thickness * 0.82);
                attributes.Height = Math.Max(1, thickness * 0.82);
                break;
            default:
                attributes.StylusTip = StylusTip.Rectangle;
                attributes.Width = Math.Max(1, thickness * 0.72);
                attributes.Height = thickness * 1.34;
                var transform = Matrix.Identity;
                transform.Rotate(-18);
                attributes.StylusTipTransform = transform;
                break;
        }
        return attributes;
    }

    private void InkSurface_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        if (_activeTool != "Pen" || _isReadOnly) return;
        ApplyStrokeProfile(e.Stroke, _pressureCurve, _strokeSmoothing, _penProfile);
    }

    private static void ApplyStrokeProfile(Stroke stroke, string pressureCurve, string smoothing, string profile)
    {
        pressureCurve = NormalizePressureCurve(pressureCurve);
        smoothing = NormalizeStrokeSmoothing(smoothing);
        profile = NormalizePenProfile(profile);
        var points = stroke.StylusPoints;
        if (points.Count < 2) return;

        if (smoothing != SmoothingOff)
        {
            points = SmoothStylusPoints(points, smoothing == SmoothingStrong ? 0.36 : 0.2);
            if (smoothing == SmoothingStrong && points.Count > 3) points = SmoothStylusPoints(points, 0.24);
        }
        if (pressureCurve != PressureOff && profile != PenProfileBallpoint) points = ApplyPressureCurve(points, pressureCurve);
        stroke.StylusPoints = points;
    }

    private static StylusPointCollection SmoothStylusPoints(StylusPointCollection source, double strength)
    {
        if (source.Count < 3) return source;
        var result = new StylusPointCollection(source.Description, source.Count);
        result.Add(source[0]);
        for (var index = 1; index < source.Count - 1; index++)
        {
            var previous = source[index - 1];
            var point = source[index];
            var next = source[index + 1];
            var averageX = (previous.X + point.X * 2 + next.X) / 4;
            var averageY = (previous.Y + point.Y * 2 + next.Y) / 4;
            point.X += (averageX - point.X) * strength;
            point.Y += (averageY - point.Y) * strength;
            result.Add(point);
        }
        result.Add(source[^1]);
        return result;
    }

    private static StylusPointCollection ApplyPressureCurve(StylusPointCollection source, string curve)
    {
        var result = new StylusPointCollection(source.Description, source.Count);
        foreach (var original in source)
        {
            var point = original;
            var pressure = Math.Clamp(point.PressureFactor, 0.05f, 1f);
            point.PressureFactor = curve switch
            {
                PressureSoft => (float)Math.Sqrt(pressure),
                PressureStrong => (float)Math.Pow(pressure, 1.65),
                _ => pressure
            };
            result.Add(point);
        }
        return result;
    }

    private static string NormalizePenProfile(string? value) => value is PenProfileBallpoint or PenProfileBrush or PenProfilePencil ? value : PenProfileFountain;
    private static string NormalizePressureCurve(string? value) => value is PressureOff or PressureSoft or PressureStrong ? value : PressureStandard;
    private static string NormalizeStrokeSmoothing(string? value) => value is SmoothingOff or SmoothingStrong ? value : SmoothingStandard;
    private static string GetPenProfileDisplayName(string value) => NormalizePenProfile(value) switch { PenProfileBallpoint => "圆珠笔", PenProfileBrush => "画笔", PenProfilePencil => "铅笔", _ => "钢笔" };
    private static string GetPressureCurveDisplayName(string value) => NormalizePressureCurve(value) switch { PressureOff => "关闭", PressureSoft => "柔和", PressureStrong => "强烈", _ => "标准" };
    private static string GetStrokeSmoothingDisplayName(string value) => NormalizeStrokeSmoothing(value) switch { SmoothingOff => "关闭", SmoothingStrong => "强力", _ => "标准" };
}
