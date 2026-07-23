using PaperNote.Core.Ink;
using RapidOcrNet;
using SkiaSharp;

namespace PaperNote.Core.Services;

public sealed record OfflineRecognitionBlock(string Text, double Confidence, double X, double Y, double Width, double Height);

public sealed record OfflineRecognitionResult(string Text, IReadOnlyList<OfflineRecognitionBlock> Blocks, TimeSpan Duration)
{
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

/// <summary>Runs multilingual OCR and ink-to-text recognition entirely on the local device.</summary>
public sealed class OfflineRecognitionService : IDisposable
{
    public const string DetectorFileName = "PP-OCRv6_det_tiny.onnx";
    public const string ClassifierFileName = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx";
    public const string RecognizerFileName = "PP-OCRv6_rec_tiny.onnx";
    public const string DictionaryFileName = "ppocrv6_tiny_dict.txt";
    public static readonly string[] RequiredModelFiles = [DetectorFileName, ClassifierFileName, RecognizerFileName, DictionaryFileName];

    private readonly string _modelDirectory;
    private readonly object _gate = new();
    private RapidOcr? _engine;
    private bool _disposed;

    public OfflineRecognitionService(string modelDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        _modelDirectory = Path.GetFullPath(modelDirectory);
    }

    public string ModelDirectory => _modelDirectory;
    public bool IsAvailable => RequiredModelFiles.All(file => File.Exists(Path.Combine(_modelDirectory, file)));
    public IReadOnlyList<string> MissingModelFiles => RequiredModelFiles.Where(file => !File.Exists(Path.Combine(_modelDirectory, file))).ToArray();

    public Task<OfflineRecognitionResult> RecognizeImageAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        return Task.Run(() => RecognizeImage(imageBytes, cancellationToken), cancellationToken);
    }

    public OfflineRecognitionResult RecognizeImage(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (imageBytes.Length == 0) throw new ArgumentException("Image data cannot be empty.", nameof(imageBytes));
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAvailable) throw new FileNotFoundException($"Offline OCR model is incomplete: {string.Join(", ", MissingModelFiles)}");

        using var bitmap = SKBitmap.Decode(imageBytes) ?? throw new InvalidDataException("The image cannot be decoded.");
        using var prepared = ResizeForRecognition(bitmap, 2400);
        var started = DateTimeOffset.UtcNow;
        OcrResult raw;
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            raw = GetOrCreateEngine().Detect(prepared, RapidOcrOptions.PPOCRv6 with { ReturnWordBox = true });
        }

        var blocks = raw.TextBlocks
            .Where(block => !string.IsNullOrWhiteSpace(block.Text))
            .Select(block =>
            {
                var left = block.BoxPoints.Min(point => point.X);
                var top = block.BoxPoints.Min(point => point.Y);
                var right = block.BoxPoints.Max(point => point.X);
                var bottom = block.BoxPoints.Max(point => point.Y);
                var confidence = block.CharScores is { Length: > 0 } ? block.CharScores.Average(score => (double)score) : 0d;
                return new OfflineRecognitionBlock(block.Text.Trim(), confidence, left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
            })
            .ToArray();
        var text = NormalizeRecognizedText(string.Join(Environment.NewLine, blocks.Select(block => block.Text)));
        return new OfflineRecognitionResult(text, blocks, DateTimeOffset.UtcNow - started);
    }

    public Task<OfflineRecognitionResult> RecognizeInkAsync(IEnumerable<PaperInkStroke> strokes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        var snapshot = strokes.Select(stroke => stroke.Clone()).ToArray();
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var png = RasterizeInk(snapshot);
            return RecognizeImage(png, cancellationToken);
        }, cancellationToken);
    }

    public static byte[] RasterizeInk(IEnumerable<PaperInkStroke> strokes, int maximumDimension = 1800)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        var visible = strokes.Where(stroke => stroke.Points.Count > 0 && stroke.Opacity > 0).ToArray();
        if (visible.Length == 0) throw new InvalidOperationException("No ink is available for recognition.");
        var minX = visible.Min(stroke => stroke.Points.Min(point => point.X) - stroke.Width);
        var minY = visible.Min(stroke => stroke.Points.Min(point => point.Y) - stroke.Width);
        var maxX = visible.Max(stroke => stroke.Points.Max(point => point.X) + stroke.Width);
        var maxY = visible.Max(stroke => stroke.Points.Max(point => point.Y) + stroke.Width);
        const float padding = 48;
        var sourceWidth = Math.Max(1, maxX - minX + padding * 2);
        var sourceHeight = Math.Max(1, maxY - minY + padding * 2);
        var scale = Math.Min(3d, Math.Min(maximumDimension / sourceWidth, maximumDimension / sourceHeight));
        scale = Math.Max(.5d, scale);
        var width = Math.Max(64, (int)Math.Ceiling(sourceWidth * scale));
        var height = Math.Max(64, (int)Math.Ceiling(sourceHeight * scale));
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.Scale((float)scale);
        canvas.Translate((float)(-minX + padding), (float)(-minY + padding));
        using var paint = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Style = SKPaintStyle.Stroke, Color = SKColors.Black };
        foreach (var stroke in visible)
        {
            paint.StrokeWidth = (float)Math.Max(1.4, stroke.Width);
            paint.Color = SKColors.Black.WithAlpha((byte)Math.Clamp((int)Math.Round(stroke.Opacity * 255), 24, 255));
            if (stroke.Points.Count == 1)
            {
                canvas.DrawCircle((float)stroke.Points[0].X, (float)stroke.Points[0].Y, paint.StrokeWidth / 2, paint);
                continue;
            }
            using var path = new SKPath();
            path.MoveTo((float)stroke.Points[0].X, (float)stroke.Points[0].Y);
            for (var index = 1; index < stroke.Points.Count; index++) path.LineTo((float)stroke.Points[index].X, (float)stroke.Points[index].Y);
            canvas.DrawPath(path, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static string NormalizeRecognizedText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine, lines).Trim();
    }

    private RapidOcr GetOrCreateEngine()
    {
        if (_engine is not null) return _engine;
        var models = RapidOcrModelSet.PPOCRv6Tiny with
        {
            DetModelPath = Path.Combine(_modelDirectory, DetectorFileName),
            ClsModelPath = Path.Combine(_modelDirectory, ClassifierFileName),
            RecModelPath = Path.Combine(_modelDirectory, RecognizerFileName),
            KeysPath = Path.Combine(_modelDirectory, DictionaryFileName)
        };
        _engine = new RapidOcr();
        _engine.InitModels(models, Math.Clamp(Environment.ProcessorCount / 2, 1, 4));
        return _engine;
    }

    private static SKBitmap ResizeForRecognition(SKBitmap source, int maximumDimension)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= maximumDimension) return source.Copy();
        var scale = maximumDimension / (double)longest;
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul);
        return source.Resize(info, SKSamplingOptions.Default) ?? source.Copy();
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_gate)
        {
            _engine?.Dispose();
            _engine = null;
            _disposed = true;
        }
    }
}
