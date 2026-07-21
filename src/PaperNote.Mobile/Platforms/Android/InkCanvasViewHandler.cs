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
        [nameof(InkCanvasView.FingerDrawingEnabled)] = MapState
    };

    public InkCanvasViewHandler() : base(Mapper) { }

    public bool CanUndo => PlatformView?.CanUndo == true;
    public bool CanRedo => PlatformView?.CanRedo == true;

    protected override NativeInkCanvasView CreatePlatformView() => new(Context);

    protected override void ConnectHandler(NativeInkCanvasView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.InkChanged += PlatformView_InkChanged;
        platformView.HistoryChanged += PlatformView_HistoryChanged;
        RefreshFromVirtualView();
    }

    protected override void DisconnectHandler(NativeInkCanvasView platformView)
    {
        platformView.InkChanged -= PlatformView_InkChanged;
        platformView.HistoryChanged -= PlatformView_HistoryChanged;
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

    private void PlatformView_InkChanged(object? sender, EventArgs e) => VirtualView?.NotifyInkChanged();
    private void PlatformView_HistoryChanged(object? sender, EventArgs e) => VirtualView?.NotifyHistoryChanged();
    private static void MapState(InkCanvasViewHandler handler, InkCanvasView view) => handler.RefreshFromVirtualView();
}
