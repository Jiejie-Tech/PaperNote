using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PaperNote.Desktop.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private const double PdfCropPreviewWidth = 420;
    private const double PdfCropPreviewHeight = 594;
    private bool _isUpdatingPdfCropEditor;
    private double _pendingCropLeft;
    private double _pendingCropTop;
    private double _pendingCropRight;
    private double _pendingCropBottom;

    private void OpenCustomPdfCrop_Click(object sender, RoutedEventArgs e) => OpenCustomPdfCropEditor();

    private bool OpenCustomPdfCropEditor()
    {
        if (!CanEditCurrentPdfPage())
        {
            StatusText.Text = _isReadOnly ? "只读模式下不能调整 PDF 裁剪" : "当前页不是 PDF 页面";
            return false;
        }

        _pendingCropLeft = _currentPage!.BackgroundCropLeft;
        _pendingCropTop = _currentPage.BackgroundCropTop;
        _pendingCropRight = _currentPage.BackgroundCropRight;
        _pendingCropBottom = _currentPage.BackgroundCropBottom;
        PdfCropPreviewImage.Source = PageThumbnailService.DecodeImageData(_currentPage.BackgroundImageData);
        PdfCropEditorOverlay.Visibility = Visibility.Visible;
        SyncPdfCropSliders();
        UpdatePdfCropVisuals();
        StatusText.Text = "自定义 PDF 裁剪 · 拖动边框或调整四边比例";
        return true;
    }

    private void ClosePdfCropEditor_Click(object sender, RoutedEventArgs e) => ClosePdfCropEditor();

    private void ClosePdfCropEditor()
    {
        PdfCropEditorOverlay.Visibility = Visibility.Collapsed;
        PdfCropPreviewImage.Source = null;
        InkSurface.Focus();
    }

    private void ApplyPdfCropEditor_Click(object sender, RoutedEventArgs e)
    {
        if (SetCurrentPdfCrop(_pendingCropLeft, _pendingCropTop, _pendingCropRight, _pendingCropBottom))
        {
            ClosePdfCropEditor();
        }
    }

    private void PdfCropSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingPdfCropEditor || PdfCropEditorOverlay.Visibility != Visibility.Visible) return;
        _pendingCropLeft = PdfCropLeftSlider.Value / 100d;
        _pendingCropTop = PdfCropTopSlider.Value / 100d;
        _pendingCropRight = PdfCropRightSlider.Value / 100d;
        _pendingCropBottom = PdfCropBottomSlider.Value / 100d;
        NormalizePendingCrop();
        SyncPdfCropSliders();
        UpdatePdfCropVisuals();
    }

    private void PdfCropThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: string edge }) return;
        var delta = edge switch
        {
            "Left" or "Right" => e.HorizontalChange / PdfCropPreviewWidth,
            _ => e.VerticalChange / PdfCropPreviewHeight
        };
        if (edge is "Right" or "Bottom") delta = -delta;
        SetPendingPdfCropEdge(edge, GetPendingPdfCropEdge(edge) + delta);
    }

    private double GetPendingPdfCropEdge(string edge) => edge switch
    {
        "Left" => _pendingCropLeft,
        "Top" => _pendingCropTop,
        "Right" => _pendingCropRight,
        "Bottom" => _pendingCropBottom,
        _ => 0
    };

    private bool SetPendingPdfCropEdge(string edge, double amount)
    {
        amount = Math.Clamp(amount, 0, 0.45);
        switch (edge)
        {
            case "Left": _pendingCropLeft = Math.Min(amount, 0.9 - _pendingCropRight); break;
            case "Top": _pendingCropTop = Math.Min(amount, 0.9 - _pendingCropBottom); break;
            case "Right": _pendingCropRight = Math.Min(amount, 0.9 - _pendingCropLeft); break;
            case "Bottom": _pendingCropBottom = Math.Min(amount, 0.9 - _pendingCropTop); break;
            default: return false;
        }
        NormalizePendingCrop();
        SyncPdfCropSliders();
        UpdatePdfCropVisuals();
        return true;
    }

    private void PdfCropPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value } || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)) return;
        amount = Math.Clamp(amount, 0, 0.2);
        _pendingCropLeft = _pendingCropTop = _pendingCropRight = _pendingCropBottom = amount;
        SyncPdfCropSliders();
        UpdatePdfCropVisuals();
    }

    private void NormalizePendingCrop()
    {
        _pendingCropLeft = Math.Clamp(_pendingCropLeft, 0, 0.45);
        _pendingCropTop = Math.Clamp(_pendingCropTop, 0, 0.45);
        _pendingCropRight = Math.Clamp(_pendingCropRight, 0, Math.Min(0.45, 0.9 - _pendingCropLeft));
        _pendingCropBottom = Math.Clamp(_pendingCropBottom, 0, Math.Min(0.45, 0.9 - _pendingCropTop));
    }

    private void SyncPdfCropSliders()
    {
        _isUpdatingPdfCropEditor = true;
        try
        {
            PdfCropLeftSlider.Value = _pendingCropLeft * 100;
            PdfCropTopSlider.Value = _pendingCropTop * 100;
            PdfCropRightSlider.Value = _pendingCropRight * 100;
            PdfCropBottomSlider.Value = _pendingCropBottom * 100;
        }
        finally { _isUpdatingPdfCropEditor = false; }
    }

    private void UpdatePdfCropVisuals()
    {
        if (PdfCropFrame is null) return;
        var left = _pendingCropLeft * PdfCropPreviewWidth;
        var top = _pendingCropTop * PdfCropPreviewHeight;
        var right = (1 - _pendingCropRight) * PdfCropPreviewWidth;
        var bottom = (1 - _pendingCropBottom) * PdfCropPreviewHeight;
        var width = Math.Max(1, right - left);
        var height = Math.Max(1, bottom - top);

        Canvas.SetLeft(PdfCropFrame, left);
        Canvas.SetTop(PdfCropFrame, top);
        PdfCropFrame.Width = width;
        PdfCropFrame.Height = height;

        PdfCropShadeLeft.Width = left;
        PdfCropShadeLeft.Height = PdfCropPreviewHeight;
        PdfCropShadeTop.Width = width;
        PdfCropShadeTop.Height = top;
        Canvas.SetLeft(PdfCropShadeTop, left);
        PdfCropShadeRight.Width = Math.Max(0, PdfCropPreviewWidth - right);
        PdfCropShadeRight.Height = PdfCropPreviewHeight;
        Canvas.SetLeft(PdfCropShadeRight, right);
        PdfCropShadeBottom.Width = width;
        PdfCropShadeBottom.Height = Math.Max(0, PdfCropPreviewHeight - bottom);
        Canvas.SetLeft(PdfCropShadeBottom, left);
        Canvas.SetTop(PdfCropShadeBottom, bottom);

        PositionCropThumb(PdfCropLeftThumb, left - 7, top + height / 2 - 34);
        PositionCropThumb(PdfCropRightThumb, right - 7, top + height / 2 - 34);
        PositionCropThumb(PdfCropTopThumb, left + width / 2 - 34, top - 7);
        PositionCropThumb(PdfCropBottomThumb, left + width / 2 - 34, bottom - 7);

        PdfCropLeftValue.Text = $"{_pendingCropLeft:P0}";
        PdfCropTopValue.Text = $"{_pendingCropTop:P0}";
        PdfCropRightValue.Text = $"{_pendingCropRight:P0}";
        PdfCropBottomValue.Text = $"{_pendingCropBottom:P0}";
        PdfCropRetainedText.Text = $"保留区域：{Math.Max(0, 1 - _pendingCropLeft - _pendingCropRight):P0} × {Math.Max(0, 1 - _pendingCropTop - _pendingCropBottom):P0}";
    }

    private static void PositionCropThumb(Thumb thumb, double left, double top)
    {
        Canvas.SetLeft(thumb, left);
        Canvas.SetTop(thumb, top);
    }
}
