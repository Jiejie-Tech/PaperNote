using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PaperNote.Core.Services;
using PaperNote.Core.Models;
using PaperNote.Desktop.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private bool _presentationMode;
    private GridLength _presentationHeaderHeight;
    private GridLength _presentationToolbarHeight;
    private GridLength _presentationSidebarWidth;
    private WindowState _presentationPreviousState;
    private readonly SelectionMaterialLibraryService _selectionMaterialLibraryService = new();
    private readonly List<SelectionMaterial> _selectionMaterials = [];

    private async Task InitializeSelectionMaterialsAsync()
    {
        _selectionMaterials.Clear();
        _selectionMaterials.AddRange(await _selectionMaterialLibraryService.LoadAsync());
        UpdateSelectionMaterialsButton();
    }

    private void UpdateSelectionMaterialsButton()
    {
        if (StudyAssistButton is null) return;
        StudyAssistButton.Content = _selectionMaterials.Count == 0 ? "课堂⌄" : $"课堂 {_selectionMaterials.Count}⌄";
        StudyAssistButton.ToolTip = $"胶带遮挡、常用元素、个人素材和演示模式 · {_selectionMaterials.Count}/{SelectionMaterialLibraryService.MaximumMaterials}";
    }

    private void StudyAssist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("添加胶带遮挡", "", (_, _) => InsertStudyTape(), !_isReadOnly && _currentPage is not null));
        var elements = new MenuItem { Header = "常用元素" };
        foreach (var (kind, label) in new[] { ("Important", "重点 !"), ("Question", "疑问 ？"), ("Check", "完成 ✓"), ("Star", "星标 ★"), ("Divider", "分隔线"), ("Formula", "公式框") })
        {
            var captured = kind;
            elements.Items.Add(CreateMenuItem(label, "", (_, _) => InsertStudyElement(captured), !_isReadOnly && _currentPage is not null));
        }
        menu.Items.Add(elements);
        menu.Items.Add(CreateGeometryToolsMenu());
        var materials = new MenuItem { Header = "我的素材", IsEnabled = _selectionMaterials.Count > 0 && !_isReadOnly && _currentPage is not null };
        var deleteMaterials = new MenuItem { Header = "删除素材", IsEnabled = _selectionMaterials.Count > 0 };
        if (_selectionMaterials.Count == 0)
        {
            materials.Items.Add(new MenuItem { Header = "先用套索选择内容，再从“选区”保存", IsEnabled = false });
        }
        else
        {
            foreach (var material in _selectionMaterials.ToArray())
            {
                var capturedId = material.Id;
                var label = GetSelectionMaterialDisplayName(material);
                materials.Items.Add(CreateMenuItem(label, "", (_, _) => InsertSelectionMaterial(capturedId), !_isReadOnly && _currentPage is not null));
                deleteMaterials.Items.Add(CreateMenuItem(label, "", async (_, _) => await DeleteSelectionMaterialAsync(capturedId), true));
            }
        }
        menu.Items.Add(materials);
        if (_selectionMaterials.Count > 0) menu.Items.Add(deleteMaterials);
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("激光笔", "I", (_, _) => LaserTool.IsChecked = true, EditorView.IsVisible));
        menu.Items.Add(CreateMenuItem(_presentationMode ? "退出演示模式" : "进入演示模式", "Shift+F11", (_, _) => TogglePresentationMode(), EditorView.IsVisible));
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void InsertStudyTape()
    {
        if (_currentPage is null) return;
        var tape = StudyAssistService.CreateTape();
        tape.LayerId = PageLayerService.EnsureDefault(_currentPage).Id;
        AddPageObject(tape, "已添加胶带遮挡；可移动、缩放、旋转或调整透明度");
    }

    private void InsertStudyElement(string kind)
    {
        if (_currentPage is null) return;
        var layer = PageLayerService.EnsureDefault(_currentPage).Id;
        foreach (var item in StudyAssistService.CreateElement(kind))
        {
            item.LayerId = layer;
            AddPageObject(item, "已添加常用元素");
        }
    }

    private async Task SaveMixedSelectionAsMaterialAsync()
    {
        if (_currentPage is null || !HasMixedSelection()) return;
        if (_selectionMaterials.Count >= SelectionMaterialLibraryService.MaximumMaterials)
        {
            MessageBox.Show(this, $"个人素材库最多保存 {SelectionMaterialLibraryService.MaximumMaterials} 项，请先删除不需要的素材。", "素材库已满", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = PromptForText("保存到个人素材库", "素材名称", $"个人素材 {DateTime.Now:MM-dd HHmm}");
        if (name is null) return;
        SyncCurrentInkModelForSelection();
        var material = SelectionMaterialLibraryService.Create(name, _currentPage, GetSelectedStrokeIds(), _selectedPageObjectIds);
        if (material is null)
        {
            MessageBox.Show(this, "当前选区没有可保存的内容。", "无法保存素材", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _selectionMaterials.Insert(0, material);
        await _selectionMaterialLibraryService.SaveAsync(_selectionMaterials);
        UpdateSelectionMaterialsButton();
        StatusText.Text = $"已保存到个人素材库：{material.Name}";
    }

    private void InsertSelectionMaterial(Guid materialId)
    {
        if (_currentPage is null || _isReadOnly) return;
        var material = _selectionMaterials.FirstOrDefault(item => item.Id == materialId);
        if (material is null) return;

        _history.Record(InkSurface.Strokes);
        CaptureCurrentPage();
        var layerId = PageLayerService.EnsureDefault(_currentPage).Id;
        var placement = SelectionMaterialLibraryService.Instantiate(material, layerId: layerId);
        _currentPage.Ink.Strokes.AddRange(placement.Strokes);
        _currentPage.Objects.AddRange(placement.Objects);
        RefreshCurrentPageFromModel(placement.Strokes.Select(item => item.Id), placement.Objects.Select(item => item.Id));
        UpdateHistoryButtons();
        StatusText.Text = $"已插入个人素材：{material.Name}";
    }

    private async Task DeleteSelectionMaterialAsync(Guid materialId)
    {
        var material = _selectionMaterials.FirstOrDefault(item => item.Id == materialId);
        if (material is null) return;
        if (MessageBox.Show(this, $"确定删除个人素材“{material.Name}”吗？", "删除素材", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _selectionMaterials.Remove(material);
        await _selectionMaterialLibraryService.SaveAsync(_selectionMaterials);
        UpdateSelectionMaterialsButton();
        StatusText.Text = $"已删除个人素材：{material.Name}";
    }

    private static string GetSelectionMaterialDisplayName(SelectionMaterial material)
        => $"{material.Name} · {material.Strokes.Count} 笔迹/{material.Objects.Count} 对象";
    private void UpdateLaserPointer(Point point)
    {
        if (_activeTool != "Laser") return;
        LaserPointerTrail.Visibility = Visibility.Visible;
        LaserPointerDot.Visibility = Visibility.Visible;
        LaserPointerTrail.Points.Add(point);
        while (LaserPointerTrail.Points.Count > 14) LaserPointerTrail.Points.RemoveAt(0);
        Canvas.SetLeft(LaserPointerDot, point.X - LaserPointerDot.Width / 2);
        Canvas.SetTop(LaserPointerDot, point.Y - LaserPointerDot.Height / 2);
    }

    private void ClearLaserPointer()
    {
        LaserPointerTrail.Points.Clear();
        LaserPointerTrail.Visibility = Visibility.Collapsed;
        LaserPointerDot.Visibility = Visibility.Collapsed;
    }

    private void TogglePresentationMode()
    {
        if (!_presentationMode)
        {
            _presentationHeaderHeight = EditorView.RowDefinitions[0].Height;
            _presentationToolbarHeight = EditorView.RowDefinitions[1].Height;
            _presentationSidebarWidth = SidebarColumn.Width;
            _presentationPreviousState = WindowState;
            EditorView.RowDefinitions[0].Height = new GridLength(0);
            EditorView.RowDefinitions[1].Height = new GridLength(0);
            SidebarColumn.Width = new GridLength(0);
            WindowState = WindowState.Maximized;
            _presentationMode = true;
            LaserTool.IsChecked = true;
            StatusText.Text = "演示模式 · 激光笔已启用 · Shift+F11 退出";
        }
        else
        {
            EditorView.RowDefinitions[0].Height = _presentationHeaderHeight;
            EditorView.RowDefinitions[1].Height = _presentationToolbarHeight;
            SidebarColumn.Width = _presentationSidebarWidth;
            WindowState = _presentationPreviousState;
            _presentationMode = false;
            PenTool.IsChecked = true;
            ClearLaserPointer();
        }
    }

    private async void JumpSelectedInkToAudio()
    {
        if (_currentPage is null) return;
        var strokeId = InkSurface.GetSelectedStrokes().Select(WpfInkAdapter.GetStrokeId).FirstOrDefault();
        var location = StudyAssistService.FindAudioForStroke(_currentPage, strokeId);
        if (location is null)
        {
            MessageBox.Show(this, "选中的笔迹没有关联录音时间点。只有录音期间书写的笔迹才能反向跳转。", "没有关联录音", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await PlayDesktopRecordingAsync(location.Recording, location.Cue.OffsetMilliseconds);
    }
}
