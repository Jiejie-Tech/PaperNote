using Microsoft.Extensions.Logging;
using PaperNote.Mobile.Controls;
using PaperNote.Mobile.Pages;
using PaperNote.Mobile.Platforms.Android;
using PaperNote.Mobile.Services;

namespace PaperNote.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureMauiHandlers(handlers => handlers.AddHandler<InkCanvasView, InkCanvasViewHandler>())
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<MobileNotebookRepository>();
        builder.Services.AddSingleton<MobileTransferService>();
        builder.Services.AddSingleton<AndroidPdfService>();
        builder.Services.AddSingleton<LibraryPage>();
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<EditorPage>();
        builder.Services.AddTransient<SearchPage>();
        builder.Services.AddTransient<BackupPage>();
        builder.Services.AddTransient<SettingsPage>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
