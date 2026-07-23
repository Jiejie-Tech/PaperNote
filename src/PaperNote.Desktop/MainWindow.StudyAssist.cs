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
