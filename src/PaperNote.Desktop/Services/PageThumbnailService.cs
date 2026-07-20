using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaperNote.Desktop.Models;

namespace PaperNote.Desktop.Services;

public static class PageThumbnailService
{
    public const double PageWidth = 840;
    public const double PageHeight = 1188;
    private const int ThumbnailWidth = 150;
    private const int ThumbnailHeight = 212;

    public static ImageSource Create(
        StrokeCollection strokes,
        string? paperTemplate = null,
        string? paperColor = null,
        IReadOnlyList<PageObject>? pageObjects = null,
        string? backgroundImageData = null)
    {
        return RenderBitmap(
            strokes,
            paperTemplate,
            paperColor,
            pageObjects,
            PageBackgroundService.CreateImageSource(backgroundImageData),
            ThumbnailWidth,
            ThumbnailHeight);
    }

    public static ImageSource CreateFromInkData(
        string? inkData,
        string? paperTemplate = null,
        string? paperColor = null,
        IReadOnlyList<PageObject>? pageObjects = null,
        string? backgroundImageData = null)
    {
        return Create(Deserialize(inkData), paperTemplate, paperColor, pageObjects, backgroundImageData);
    }

    public static BitmapSource CreatePageBitmap(NotebookPage page, int pixelWidth = 840, int pixelHeight = 1188)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (pixelWidth is < 64 or > 5000 || pixelHeight is < 64 or > 7000)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "页面输出尺寸超出允许范围。");

        return RenderBitmap(
            Deserialize(page.InkData),
            page.PaperTemplate,
            page.PaperColor,
            page.Objects,
            PageBackgroundService.CreateImageSource(page),
            pixelWidth,
            pixelHeight);
    }

    public static byte[] CreatePagePng(NotebookPage page, int pixelWidth = 840, int pixelHeight = 1188)
    {
        var bitmap = CreatePageBitmap(page, pixelWidth, pixelHeight);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static RenderTargetBitmap RenderBitmap(
        StrokeCollection strokes,
        string? paperTemplate,
        string? paperColor,
        IReadOnlyList<PageObject>? pageObjects,
        ImageSource? backgroundImage,
        int pixelWidth,
        int pixelHeight)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(pixelWidth / PageWidth, pixelHeight / PageHeight));
            var backgroundBrush = new SolidColorBrush(ParsePaperColor(paperColor));
            backgroundBrush.Freeze();
            context.DrawRectangle(backgroundBrush, null, new Rect(0, 0, PageWidth, PageHeight));
            DrawPaperTemplate(context, paperTemplate);
            if (backgroundImage is not null) context.DrawImage(backgroundImage, new Rect(0, 0, PageWidth, PageHeight));
            foreach (var stroke in strokes) stroke.Draw(context);
            DrawPageObjects(context, pageObjects);
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static string Serialize(StrokeCollection strokes)
    {
        return Convert.ToBase64String(InkHistoryService.Serialize(strokes));
    }

    public static StrokeCollection Deserialize(string? inkData)
    {
        if (string.IsNullOrWhiteSpace(inkData)) return new StrokeCollection();
        try
        {
            return InkHistoryService.Deserialize(Convert.FromBase64String(inkData));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException or IOException)
        {
            return new StrokeCollection();
        }
    }

    public static Color ParsePaperColor(string? paperColor)
    {
        try
        {
            return (Color)(ColorConverter.ConvertFromString(
                string.IsNullOrWhiteSpace(paperColor) ? PaperPageDefaults.Color : paperColor) ?? Colors.White);
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException or ArgumentException)
        {
            return Colors.White;
        }
    }

    private static void DrawPageObjects(DrawingContext context, IReadOnlyList<PageObject>? pageObjects)
    {
        if (pageObjects is null) return;
        foreach (var pageObject in pageObjects)
        {
            var rect = new Rect(pageObject.X, pageObject.Y, pageObject.Width, pageObject.Height);
            context.PushOpacity(Math.Clamp(pageObject.Opacity, 0.1, 1));
            var hasRotation = Math.Abs(pageObject.Rotation) > 0.01;
            if (hasRotation)
                context.PushTransform(new RotateTransform(pageObject.Rotation, rect.X + rect.Width / 2, rect.Y + rect.Height / 2));

            switch (pageObject.Kind)
            {
                case "Image":
                    var image = DecodeImageData(pageObject.ImageData);
                    if (image is not null) context.DrawImage(image, rect);
                    break;
                case "Shape":
                    DrawShape(context, pageObject, rect);
                    break;
                default:
                    DrawText(context, pageObject, rect);
                    break;
            }
            if (hasRotation) context.Pop();
            context.Pop();
        }
    }

    private static void DrawText(DrawingContext context, PageObject pageObject, Rect rect)
    {
        var text = string.IsNullOrWhiteSpace(pageObject.Text) ? " " : pageObject.Text;
        var brush = new SolidColorBrush(ParseColor(pageObject.StrokeColor, Colors.Black));
        brush.Freeze();
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI, Microsoft YaHei UI"),
            Math.Clamp(pageObject.FontSize, 10, 96),
            brush,
            1.0)
        {
            MaxTextWidth = Math.Max(20, rect.Width - 18),
            MaxTextHeight = Math.Max(20, rect.Height - 18),
            Trimming = TextTrimming.CharacterEllipsis
        };
        context.DrawText(formatted, new Point(rect.X + 9, rect.Y + 9));
    }

    private static void DrawShape(DrawingContext context, PageObject pageObject, Rect rect)
    {
        var strokeBrush = new SolidColorBrush(ParseColor(pageObject.StrokeColor, Color.FromRgb(57, 120, 246)));
        strokeBrush.Freeze();
        var fillBrush = new SolidColorBrush(ParseColor(pageObject.FillColor, Color.FromArgb(24, 57, 120, 246)));
        fillBrush.Freeze();
        var pen = new Pen(strokeBrush, Math.Clamp(pageObject.StrokeThickness, 1, 20));
        pen.Freeze();
        var inner = new Rect(rect.X + 8, rect.Y + 8, Math.Max(1, rect.Width - 16), Math.Max(1, rect.Height - 16));
        switch (pageObject.ShapeKind)
        {
            case "Ellipse":
                context.DrawEllipse(fillBrush, pen, new Point(inner.X + inner.Width / 2, inner.Y + inner.Height / 2), inner.Width / 2, inner.Height / 2);
                break;
            case "Line":
                context.DrawLine(pen, new Point(inner.Left, inner.Bottom), new Point(inner.Right, inner.Top));
                break;
            case "Triangle":
            case "Diamond":
            case "Arrow":
                context.DrawGeometry(fillBrush, pen, CreateShapeGeometry(pageObject.ShapeKind, inner));
                break;
            case "RoundedRectangle":
                context.DrawRoundedRectangle(fillBrush, pen, inner, 18, 18);
                break;
            default:
                context.DrawRectangle(fillBrush, pen, inner);
                break;
        }
    }

    private static Geometry CreateShapeGeometry(string shapeKind, Rect rect)
    {
        Point Map(double x, double y) => new(rect.X + rect.Width * x, rect.Y + rect.Height * y);
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            switch (shapeKind)
            {
                case "Triangle":
                    stream.BeginFigure(Map(0.5, 0), true, true);
                    stream.PolyLineTo([Map(1, 1), Map(0, 1)], true, true);
                    break;
                case "Diamond":
                    stream.BeginFigure(Map(0.5, 0), true, true);
                    stream.PolyLineTo([Map(1, 0.5), Map(0.5, 1), Map(0, 0.5)], true, true);
                    break;
                default:
                    stream.BeginFigure(Map(0, 0.34), true, true);
                    stream.PolyLineTo([
                        Map(0.62, 0.34), Map(0.62, 0), Map(1, 0.5),
                        Map(0.62, 1), Map(0.62, 0.66), Map(0, 0.66)
                    ], true, true);
                    break;
            }
        }
        geometry.Freeze();
        return geometry;
    }

    public static BitmapImage? DecodeImageData(string? imageData)
    {
        if (string.IsNullOrWhiteSpace(imageData)) return null;
        try
        {
            var bytes = Convert.FromBase64String(imageData);
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static Color ParseColor(string? value, Color fallback)
    {
        try { return (Color)(ColorConverter.ConvertFromString(value) ?? fallback); }
        catch { return fallback; }
    }

    private static void DrawPaperTemplate(DrawingContext context, string? paperTemplate)
    {
        var template = string.IsNullOrWhiteSpace(paperTemplate) ? PaperPageDefaults.Template : paperTemplate;
        var patternBrush = new SolidColorBrush(Color.FromRgb(209, 215, 225));
        patternBrush.Freeze();
        var patternPen = new Pen(patternBrush, 1);
        patternPen.Freeze();

        switch (template)
        {
            case "Blank":
                return;
            case "Lined":
                for (var y = 48d; y < PageHeight; y += 48d) context.DrawLine(patternPen, new Point(0, y), new Point(PageWidth, y));
                return;
            case "Grid":
                for (var x = 48d; x < PageWidth; x += 48d) context.DrawLine(patternPen, new Point(x, 0), new Point(x, PageHeight));
                for (var y = 48d; y < PageHeight; y += 48d) context.DrawLine(patternPen, new Point(0, y), new Point(PageWidth, y));
                return;
            default:
                for (var x = 28d; x < PageWidth; x += 28d)
                for (var y = 28d; y < PageHeight; y += 28d)
                    context.DrawEllipse(patternBrush, null, new Point(x, y), 1.35, 1.35);
                return;
        }
    }
}
