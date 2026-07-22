using PaperNote.Core.Ink;
using PaperNote.Core.Models;
using PaperNote.Core.Services;

namespace PaperNote.Mobile.Controls;

public enum InkCanvasTool
{
    Pen,
    Highlighter,
    Eraser,
    Pan,
    Select
}

public enum InkEraserMode
{
    Partial,
    Stroke
}

public sealed class InkCanvasView : View
{
    public static readonly BindableProperty DocumentProperty = BindableProperty.Create(
        nameof(Document), typeof(PaperInkDocument), typeof(InkCanvasView), new PaperInkDocument(), propertyChanged: InvalidateNative);
    public static readonly BindableProperty PageProperty = BindableProperty.Create(
        nameof(Page), typeof(NotebookPage), typeof(InkCanvasView), null, propertyChanged: InvalidateNative);
    public static readonly BindableProperty ToolProperty = BindableProperty.Create(
        nameof(Tool), typeof(InkCanvasTool), typeof(InkCanvasView), InkCanvasTool.Pen, propertyChanged: InvalidateNative);
    public static readonly BindableProperty InkColorProperty = BindableProperty.Create(
        nameof(InkColor), typeof(string), typeof(InkCanvasView), "#1D2530", propertyChanged: InvalidateNative);
    public static readonly BindableProperty InkWidthProperty = BindableProperty.Create(
        nameof(InkWidth), typeof(double), typeof(InkCanvasView), 3.2d, propertyChanged: InvalidateNative);
    public static readonly BindableProperty FingerDrawingEnabledProperty = BindableProperty.Create(
        nameof(FingerDrawingEnabled), typeof(bool), typeof(InkCanvasView), false, propertyChanged: InvalidateNative);
    public static readonly BindableProperty InkOpacityProperty = BindableProperty.Create(
        nameof(InkOpacity), typeof(double), typeof(InkCanvasView), 1d, propertyChanged: InvalidateNative);
    public static readonly BindableProperty EraserModeProperty = BindableProperty.Create(
        nameof(EraserMode), typeof(InkEraserMode), typeof(InkCanvasView), InkEraserMode.Partial, propertyChanged: InvalidateNative);
    public static readonly BindableProperty SmoothingEnabledProperty = BindableProperty.Create(
        nameof(SmoothingEnabled), typeof(bool), typeof(InkCanvasView), true, propertyChanged: InvalidateNative);
    public static readonly BindableProperty SelectionFilterProperty = BindableProperty.Create(
        nameof(SelectionFilter), typeof(PageSelectionFilter), typeof(InkCanvasView), PageSelectionFilter.All, propertyChanged: InvalidateNative);

    public PaperInkDocument Document
    {
        get => (PaperInkDocument)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public NotebookPage? Page
    {
        get => (NotebookPage?)GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public InkCanvasTool Tool
    {
        get => (InkCanvasTool)GetValue(ToolProperty);
        set => SetValue(ToolProperty, value);
    }

    public string InkColor
    {
        get => (string)GetValue(InkColorProperty);
        set => SetValue(InkColorProperty, value);
    }

    public double InkWidth
    {
        get => (double)GetValue(InkWidthProperty);
        set => SetValue(InkWidthProperty, value);
    }

    public bool FingerDrawingEnabled
    {
        get => (bool)GetValue(FingerDrawingEnabledProperty);
        set => SetValue(FingerDrawingEnabledProperty, value);
    }

    public double InkOpacity
    {
        get => (double)GetValue(InkOpacityProperty);
        set => SetValue(InkOpacityProperty, Math.Clamp(value, .1, 1));
    }

    public InkEraserMode EraserMode
    {
        get => (InkEraserMode)GetValue(EraserModeProperty);
        set => SetValue(EraserModeProperty, value);
    }

    public bool SmoothingEnabled
    {
        get => (bool)GetValue(SmoothingEnabledProperty);
        set => SetValue(SmoothingEnabledProperty, value);
    }

    public PageSelectionFilter SelectionFilter
    {
        get => (PageSelectionFilter)GetValue(SelectionFilterProperty);
        set => SetValue(SelectionFilterProperty, value);
    }

    public event EventHandler? InkChanged;
    public event EventHandler? HistoryChanged;
    public event EventHandler? SelectionChanged;

    public bool CanUndo => Handler is Platforms.Android.InkCanvasViewHandler handler && handler.CanUndo;
    public bool CanRedo => Handler is Platforms.Android.InkCanvasViewHandler handler && handler.CanRedo;
    public Guid? SelectedObjectId => (Handler as Platforms.Android.InkCanvasViewHandler)?.SelectedObjectId;
    public int SelectedObjectCount => (Handler as Platforms.Android.InkCanvasViewHandler)?.SelectedObjectCount ?? 0;
    public int SelectedStrokeCount => (Handler as Platforms.Android.InkCanvasViewHandler)?.SelectedStrokeCount ?? 0;
    public int SelectedContentCount => SelectedObjectCount + SelectedStrokeCount;
    public IReadOnlyCollection<Guid> SelectedObjectIds => (Handler as Platforms.Android.InkCanvasViewHandler)?.SelectedObjectIds ?? Array.Empty<Guid>();
    public IReadOnlyCollection<Guid> SelectedStrokeIds => (Handler as Platforms.Android.InkCanvasViewHandler)?.SelectedStrokeIds ?? Array.Empty<Guid>();

    public void Undo() => (Handler as Platforms.Android.InkCanvasViewHandler)?.Undo();
    public void Redo() => (Handler as Platforms.Android.InkCanvasViewHandler)?.Redo();
    public void Clear() => (Handler as Platforms.Android.InkCanvasViewHandler)?.ClearInk();
    public void ResetViewport() => (Handler as Platforms.Android.InkCanvasViewHandler)?.ResetViewport();
    public void SelectObject(Guid? objectId) => (Handler as Platforms.Android.InkCanvasViewHandler)?.SelectObject(objectId);
    public void DuplicateSelection() => (Handler as Platforms.Android.InkCanvasViewHandler)?.DuplicateSelection();
    public void DeleteSelection() => (Handler as Platforms.Android.InkCanvasViewHandler)?.DeleteSelection();
    public void RotateSelection(double degrees) => (Handler as Platforms.Android.InkCanvasViewHandler)?.RotateSelection(degrees);
    public void BringSelectionToFront() => (Handler as Platforms.Android.InkCanvasViewHandler)?.BringSelectionToFront();
    public void SendSelectionToBack() => (Handler as Platforms.Android.InkCanvasViewHandler)?.SendSelectionToBack();
    public void ToggleSelectionLock() => (Handler as Platforms.Android.InkCanvasViewHandler)?.ToggleSelectionLock();
    public void UpdateSelectedText(string text) => (Handler as Platforms.Android.InkCanvasViewHandler)?.UpdateSelectedText(text);
    public void UpdateSelectionStyle(string? strokeColor = null, double? opacity = null, double? inkWidth = null, PaperInkTool? inkTool = null)
        => (Handler as Platforms.Android.InkCanvasViewHandler)?.UpdateSelectionStyle(strokeColor, opacity, inkWidth, inkTool);
    public void ClearSelection() => (Handler as Platforms.Android.InkCanvasViewHandler)?.ClearSelection();
    public void GroupSelection() => (Handler as Platforms.Android.InkCanvasViewHandler)?.GroupSelection();
    public void UngroupSelection() => (Handler as Platforms.Android.InkCanvasViewHandler)?.UngroupSelection();

    internal void NotifyInkChanged() => InkChanged?.Invoke(this, EventArgs.Empty);
    internal void NotifyHistoryChanged() => HistoryChanged?.Invoke(this, EventArgs.Empty);
    internal void NotifySelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);

    private static void InvalidateNative(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is InkCanvasView view)
            (view.Handler as Platforms.Android.InkCanvasViewHandler)?.RefreshFromVirtualView();
    }
}
