using System.IO;
using PaperNote.Desktop.Models;
using SkiaSharp;

namespace PaperNote.Desktop.Services;

public sealed record PdfExportOptions
{
    public string Quality { get; init; } = "Standard";
    public string PageSize { get; init; } = "A4";

    public PdfExportOptions Normalize()
    {
        var quality = Quality is "Draft" or "High" ? Quality : "Standard";
        var pageSize = PageSize is "A5" or "Letter" ? PageSize : "A4";
        return this with { Quality = quality, PageSize = pageSize };
    }
}

public readonly record struct PdfExportPageSpec(float Width, float Height, int PixelWidth, int PixelHeight);

public static class PdfExportService
{
    private const int MaxPages = 1000;

    public static Task ExportAsync(
        string filePath,
        IReadOnlyList<NotebookPage> pages,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ExportAsync(filePath, pages, new PdfExportOptions(), progress, cancellationToken);
    }

    public static Task ExportAsync(
        string filePath,
        IReadOnlyList<NotebookPage> pages,
        PdfExportOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("导出路径不能为空。", nameof(filePath));
        if (pages.Count == 0) throw new ArgumentException("至少需要选择一页。", nameof(pages));
        if (pages.Count > MaxPages) throw new InvalidOperationException($"一次最多导出 {MaxPages} 页。");
        ArgumentNullException.ThrowIfNull(options);
        var normalizedOptions = options.Normalize();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                ExportCore(filePath, pages, normalizedOptions, progress, cancellationToken);
                completion.SetResult();
            }
            catch (OperationCanceledException)
            {
                completion.SetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "PaperNote PDF Export"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public static PdfExportPageSpec GetPageSpec(PdfExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = options.Normalize();
        var (width, height) = normalized.PageSize switch
        {
            "A5" => (420f, 595f),
            "Letter" => (612f, 792f),
            _ => (595f, 842f)
        };
        var (pixelWidth, pixelHeight) = normalized.Quality switch
        {
            "Draft" => (630, 891),
            "High" => (1680, 2376),
            _ => (840, 1188)
        };
        return new PdfExportPageSpec(width, height, pixelWidth, pixelHeight);
    }

    private static void ExportCore(
        string filePath,
        IReadOnlyList<NotebookPage> pages,
        PdfExportOptions options,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("无法确定 PDF 导出目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = fullPath + ".tmp";
        var spec = GetPageSpec(options);

        try
        {
            using (var stream = File.Create(temporaryPath))
            using (var document = SKDocument.CreatePdf(stream))
            {
                for (var index = 0; index < pages.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var png = PageThumbnailService.CreatePagePng(pages[index], spec.PixelWidth, spec.PixelHeight);
                    using var image = SKImage.FromEncodedData(png) ?? throw new InvalidDataException($"第 {index + 1} 页无法渲染。");
                    var canvas = document.BeginPage(spec.Width, spec.Height);
                    canvas.Clear(SKColors.White);
                    var scale = Math.Min(spec.Width / image.Width, spec.Height / image.Height);
                    var drawWidth = image.Width * scale;
                    var drawHeight = image.Height * scale;
                    var left = (spec.Width - drawWidth) / 2;
                    var top = (spec.Height - drawHeight) / 2;
                    canvas.DrawImage(image, new SKRect(left, top, left + drawWidth, top + drawHeight));
                    document.EndPage();
                    progress?.Report(index + 1);
                }
                document.Close();
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
