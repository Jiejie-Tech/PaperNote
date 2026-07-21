using PaperNote.Core.Ink;
using PaperNote.Core.Models;

namespace PaperNote.Mobile.Controls;

public enum InkCanvasTool
{
    Pen,
    Highlighter,
    Eraser,
    Pan
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

    public event EventHandler? InkChanged;
    public event EventHandler? HistoryChanged;

    public bool CanUndo => Handler is Platforms.Android.InkCanvasViewHandler handler && handler.CanUndo;
    public bool CanRedo => Handler is Platforms.Android.InkCanvasViewHandler handler && handler.CanRedo;

    public void Undo() => (Handler as Platforms.Android.InkCanvasViewHandler)?.Undo();
    public void Redo() => (Handler as Platforms.Android.InkCanvasViewHandler)?.Redo();
    public void Clear() => (Handler as Platforms.Android.InkCanvasViewHandler)?.ClearInk();
    public void ResetViewport() => (Handler as Platforms.Android.InkCanvasViewHandler)?.ResetViewport();

    internal void NotifyInkChanged() => InkChanged?.Invoke(this, EventArgs.Empty);
    internal void NotifyHistoryChanged() => HistoryChanged?.Invoke(this, EventArgs.Empty);

    private static void InvalidateNative(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is InkCanvasView view)
            (view.Handler as Platforms.Android.InkCanvasViewHandler)?.RefreshFromVirtualView();
    }
}
