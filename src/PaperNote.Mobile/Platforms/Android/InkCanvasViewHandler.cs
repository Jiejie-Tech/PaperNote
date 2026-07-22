using Microsoft.Maui.Handlers;
using PaperNote.Mobile.Controls;

namespace PaperNote.Mobile.Platforms.Android;

public sealed class InkCanvasViewHandler : ViewHandler<InkCanvasView, NativeInkCanvasView>
{
    public static readonly PropertyMapper<InkCanvasView, InkCanvasViewHandler> Mapper = new(ViewMapper)
    {
        [nameof(InkCanvasView.Document)] = MapState,
        [nameof(InkCanvasView.Page)] = MapState,
        [nameof(InkCanvasView.Tool)] = MapState,
        [nameof(InkCanvasView.InkColor)] = MapState,
        [nameof(InkCanvasView.InkWidth)] = MapState,
        [nameof(InkCanvasView.FingerDrawingEnabled)] = MapState,
        [nameof(InkCanvasView.InkOpacity)] = MapState,
        [nameof(InkCanvasView.EraserMode)] = MapState,
        [nameof(InkCanvasView.SmoothingEnabled)] = MapState
    };

    public InkCanvasViewHandler() : base(Mapper) { }

    public bool CanUndo => PlatformView?.CanUndo == true;
    public bool CanRedo => PlatformView?.CanRedo == true;
    public Guid? SelectedObjectId => PlatformView?.SelectedObjectId;
    public int SelectedObjectCount => PlatformView?.SelectedObjectCount ?? 0;
    public IReadOnlyCollection<Guid> SelectedObjectIds => PlatformView?.SelectedObjectIds ?? Array.Empty<Guid>();

    protected override NativeInkCanvasView CreatePlatformView() => new(Context);

    protected override void ConnectHandler(NativeInkCanvasView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.InkChanged += PlatformView_InkChanged;
        platformView.HistoryChanged += PlatformView_HistoryChanged;
        platformView.SelectionChanged += PlatformView_SelectionChanged;
        RefreshFromVirtualView();
    }

    protected override void DisconnectHandler(NativeInkCanvasView platformView)
    {
        platformView.InkChanged -= PlatformView_InkChanged;
        platformView.HistoryChanged -= PlatformView_HistoryChanged;
        platformView.SelectionChanged -= PlatformView_SelectionChanged;
        platformView.DisposeResources();
        base.DisconnectHandler(platformView);
    }

    public void RefreshFromVirtualView()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.Apply(VirtualView);
    }

    public void Undo() => PlatformView?.Undo();
    public void Redo() => PlatformView?.Redo();
    public void ClearInk() => PlatformView?.ClearInk();
    public void ResetViewport() => PlatformView?.ResetViewport();
    public void SelectObject(Guid? objectId) => PlatformView?.SelectObject(objectId);
    public void DuplicateSelection() => PlatformView?.DuplicateSelection();
    public void DeleteSelection() => PlatformView?.DeleteSelection();
    public void RotateSelection(double degrees) => PlatformView?.RotateSelection(degrees);
    public void BringSelectionToFront() => PlatformView?.BringSelectionToFront();
    public void SendSelectionToBack() => PlatformView?.SendSelectionToBack();
    public void ToggleSelectionLock() => PlatformView?.ToggleSelectionLock();
    public void UpdateSelectedText(string text) => PlatformView?.UpdateSelectedText(text);
    public void UpdateSelectionStyle(string strokeColor, double opacity) => PlatformView?.UpdateSelectionStyle(strokeColor, opacity);
    public void GroupSelection() => PlatformView?.GroupSelection();
    public void UngroupSelection() => PlatformView?.UngroupSelection();

    private void PlatformView_InkChanged(object? sender, EventArgs e) => VirtualView?.NotifyInkChanged();
    private void PlatformView_HistoryChanged(object? sender, EventArgs e) => VirtualView?.NotifyHistoryChanged();
    private void PlatformView_SelectionChanged(object? sender, EventArgs e) => VirtualView?.NotifySelectionChanged();
    private static void MapState(InkCanvasViewHandler handler, InkCanvasView view) => handler.RefreshFromVirtualView();
}
