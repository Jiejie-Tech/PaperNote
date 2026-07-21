using System.Globalization;
using System.IO;
using PDFtoImage;
using SkiaSharp;

namespace PaperNote.Desktop.Services;

public sealed record ImportedPdfPage(int PageNumber, string ImageData);

public static class PdfImportService
{
    public const long MaximumFileSizeBytes = 200L * 1024 * 1024;
    public const int MaximumPageCount = 300;

    public static int GetPageCount(string filePath)
    {
        var pdfBytes = ReadAndValidatePdf(filePath);
        try
        {
            var pageCount = Conversion.GetPageCount(pdfBytes, password: null);
            if (pageCount <= 0) throw new InvalidDataException("PDF 中没有可导入的页面。");
            return pageCount;
        }
        catch (Exception exception) when (exception is not ArgumentException and not FileNotFoundException and not InvalidDataException)
        {
            throw new InvalidDataException("无法读取这个 PDF，文件可能已损坏、受密码保护或格式不受支持。", exception);
        }
    }

    public static IReadOnlyList<int> ParsePageSelection(string? selection, int pageCount)
    {
        if (pageCount <= 0) throw new ArgumentOutOfRangeException(nameof(pageCount));
        var normalized = (selection ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Equals("全部", StringComparison.OrdinalIgnoreCase) || normalized.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (pageCount > MaximumPageCount) throw new InvalidDataException($"一次最多导入 {MaximumPageCount} 页，请输入页码范围。");
            return Enumerable.Range(1, pageCount).ToArray();
        }

        normalized = normalized.Replace('，', ',').Replace('；', ',').Replace(';', ',');
        var pages = new SortedSet<int>();
        foreach (var rawPart in normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart.Replace(" ", string.Empty);
            var dashIndex = part.IndexOf('-');
            if (dashIndex < 0)
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var pageNumber))
                    throw new InvalidDataException($"无法识别页码“{rawPart}”。");
                AddPage(pageNumber);
                continue;
            }

            if (part.IndexOf('-', dashIndex + 1) >= 0 ||
                !int.TryParse(part[..dashIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
                !int.TryParse(part[(dashIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var end) ||
                start > end)
            {
                throw new InvalidDataException($"无法识别页码范围“{rawPart}”。");
            }
            for (var pageNumber = start; pageNumber <= end; pageNumber++) AddPage(pageNumber);
        }

        if (pages.Count == 0) throw new InvalidDataException("请至少选择一页 PDF。");
        return pages.ToArray();

        void AddPage(int pageNumber)
        {
            if (pageNumber < 1 || pageNumber > pageCount) throw new InvalidDataException($"页码 {pageNumber} 超出范围，当前 PDF 共 {pageCount} 页。");
            pages.Add(pageNumber);
            if (pages.Count > MaximumPageCount) throw new InvalidDataException($"一次最多导入 {MaximumPageCount} 页。");
        }
    }

    public static IReadOnlyList<ImportedPdfPage> Import(
        string filePath,
        int targetWidth = 840,
        int targetHeight = 1188)
    {
        var pageCount = GetPageCount(filePath);
        if (pageCount > MaximumPageCount) throw new InvalidDataException($"这个 PDF 共 {pageCount} 页，一次最多导入 {MaximumPageCount} 页，请选择页码范围。");
        return Import(filePath, Enumerable.Range(1, pageCount).ToArray(), targetWidth, targetHeight);
    }

    public static IReadOnlyList<ImportedPdfPage> Import(
        string filePath,
        IReadOnlyList<int> pageNumbers,
        int targetWidth = 840,
        int targetHeight = 1188)
    {
        if (targetWidth <= 0 || targetHeight <= 0) throw new ArgumentOutOfRangeException(nameof(targetWidth), "页面尺寸必须大于零。");
        if (pageNumbers.Count == 0) throw new ArgumentException("请至少选择一页 PDF。", nameof(pageNumbers));
        if (pageNumbers.Count > MaximumPageCount) throw new InvalidDataException($"一次最多导入 {MaximumPageCount} 页 PDF。");

        var pdfBytes = ReadAndValidatePdf(filePath);
        try
        {
            var pageCount = Conversion.GetPageCount(pdfBytes, password: null);
            var requestedPages = pageNumbers.Distinct().OrderBy(page => page).ToArray();
            if (requestedPages.Any(page => page < 1 || page > pageCount)) throw new InvalidDataException($"导入页码必须在 1 至 {pageCount} 之间。");

            var options = new RenderOptions
            {
                Width = targetWidth,
                Height = null,
                WithAspectRatio = true,
                WithAnnotations = true,
                WithFormFill = true,
                BackgroundColor = SKColors.White
            };

            var result = new List<ImportedPdfPage>(requestedPages.Length);
            var renderedPages = Conversion.ToImages(pdfBytes, requestedPages.Select(page => page - 1), password: null, options);
            using var enumerator = renderedPages.GetEnumerator();
            foreach (var pageNumber in requestedPages)
            {
                if (!enumerator.MoveNext()) throw new InvalidDataException("PDF 页面渲染不完整，请重新导入。");
                using var renderedPage = enumerator.Current;
                using var fitted = FitToPage(renderedPage, targetWidth, targetHeight);
                using var encoded = fitted.Encode(SKEncodedImageFormat.Png, 100);
                result.Add(new ImportedPdfPage(pageNumber, Convert.ToBase64String(encoded.ToArray())));
            }

            if (enumerator.MoveNext()) enumerator.Current.Dispose();
            return result;
        }
        catch (Exception exception) when (exception is not ArgumentException and not FileNotFoundException and not InvalidDataException)
        {
            throw new InvalidDataException("无法读取这个 PDF，文件可能已损坏、受密码保护或格式不受支持。", exception);
        }
    }

    private static byte[] ReadAndValidatePdf(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("请选择 PDF 文件。", nameof(filePath));
        if (!File.Exists(filePath)) throw new FileNotFoundException("找不到要导入的 PDF 文件。", filePath);
        if (!string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("只能导入 PDF 文件。");
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MaximumFileSizeBytes) throw new InvalidDataException("PDF 文件不能超过 200 MB。");
        return File.ReadAllBytes(filePath);
    }

    private static SKBitmap FitToPage(SKBitmap source, int targetWidth, int targetHeight)
    {
        var output = new SKBitmap(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.White);
        var scale = Math.Min(targetWidth / (double)Math.Max(1, source.Width), targetHeight / (double)Math.Max(1, source.Height));
        var width = (float)Math.Max(1, source.Width * scale);
        var height = (float)Math.Max(1, source.Height * scale);
        var left = (targetWidth - width) / 2f;
        var top = (targetHeight - height) / 2f;
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawBitmap(source, new SKRect(left, top, left + width, top + height), paint);
        canvas.Flush();
        return output;
    }
}
