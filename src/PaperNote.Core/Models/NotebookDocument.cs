using PaperNote.Core.Ink;

namespace PaperNote.Core.Models;

public static class PaperPageDefaults
{
    public const string Template = "Dotted";
    public const string Color = "#FFFFFF";
}

public static class NotebookDefaults
{
    public const string CoverStyle = "Blue";
    public const string FolderName = "";
}

public sealed class NotebookDocument
{
    public int FormatVersion { get; set; } = 17;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "未命名笔记本";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastOpenedAt { get; set; }
    public Guid CurrentPageId { get; set; }
    public string FolderName { get; set; } = NotebookDefaults.FolderName;
    public string CoverStyle { get; set; } = NotebookDefaults.CoverStyle;
    public bool IsPinned { get; set; }
    public bool IsFavorite { get; set; }
    public List<string> Tags { get; set; } = [];
    public bool IsInTrash { get; set; }
    public DateTimeOffset? TrashedAt { get; set; }
    public List<NotebookPage> Pages { get; set; } = [];
    public List<PaperPreset> PaperPresets { get; set; } = [];
    public List<DocumentOutlineEntry> OutlineEntries { get; set; } = [];

    public static NotebookDocument Create(string title, string? firstPageInkData = null)
    {
        var page = new NotebookPage
        {
            InkData = firstPageInkData ?? string.Empty
        };

        return new NotebookDocument
        {
            Title = title,
            CurrentPageId = page.Id,
            Pages = [page]
        };
    }
}

public sealed class NotebookPage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public bool IsBookmarked { get; set; }
    public int OutlineLevel { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.Now;
    // WPF ISF data retained for backwards compatibility. New clients use Ink.
    public string InkData { get; set; } = string.Empty;
    public PaperInkDocument Ink { get; set; } = new();
    public string PaperTemplate { get; set; } = PaperPageDefaults.Template;
    public string PaperColor { get; set; } = PaperPageDefaults.Color;
    public string BackgroundImageData { get; set; } = string.Empty;
    public string BackgroundSourceType { get; set; } = string.Empty;
    public string BackgroundSourceName { get; set; } = string.Empty;
    public string BackgroundSourceFingerprint { get; set; } = string.Empty;
    public int BackgroundPageNumber { get; set; }
    public int BackgroundRotation { get; set; }
    public double BackgroundCropLeft { get; set; }
    public double BackgroundCropTop { get; set; }
    public double BackgroundCropRight { get; set; }
    public double BackgroundCropBottom { get; set; }
    public List<PageObject> Objects { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public string OcrText { get; set; } = string.Empty;
    public string RecognizedText { get; set; } = string.Empty;
    public string PdfText { get; set; } = string.Empty;
    public List<PdfPageLink> PdfLinks { get; set; } = [];
    public List<PageComment> Comments { get; set; } = [];
    public List<PageLayer> Layers { get; set; } = [];
    public Guid? ActiveLayerId { get; set; }
    public List<AudioRecording> AudioRecordings { get; set; } = [];

    public NotebookPage Clone(bool preserveIdentity = false)
    {
        return new NotebookPage
        {
            Id = preserveIdentity ? Id : Guid.NewGuid(),
            Title = Title,
            IsBookmarked = IsBookmarked,
            OutlineLevel = OutlineLevel,
            CreatedAt = preserveIdentity ? CreatedAt : DateTimeOffset.Now,
            ModifiedAt = preserveIdentity ? ModifiedAt : DateTimeOffset.Now,
            InkData = InkData,
            Ink = Ink.Clone(),
            PaperTemplate = PaperTemplate,
            PaperColor = PaperColor,
            BackgroundImageData = BackgroundImageData,
            BackgroundSourceType = BackgroundSourceType,
            BackgroundSourceName = BackgroundSourceName,
            BackgroundSourceFingerprint = BackgroundSourceFingerprint,
            BackgroundPageNumber = BackgroundPageNumber,
            BackgroundRotation = BackgroundRotation,
            BackgroundCropLeft = BackgroundCropLeft,
            BackgroundCropTop = BackgroundCropTop,
            BackgroundCropRight = BackgroundCropRight,
            BackgroundCropBottom = BackgroundCropBottom,
            Objects = Objects.Select(item => item.Clone()).ToList(),
            Tags = Tags.ToList(), OcrText = OcrText, RecognizedText = RecognizedText, PdfText = PdfText,
            PdfLinks = PdfLinks.Select(link => link.Clone()).ToList(), Comments = Comments.Select(comment => comment.Clone()).ToList(),
            Layers = Layers.Select(layer => layer.Clone()).ToList(), ActiveLayerId = ActiveLayerId,
            AudioRecordings = AudioRecordings.Select(recording => recording.Clone()).ToList()
        };
    }
}

public sealed class PaperPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PaperTemplate { get; set; } = PaperPageDefaults.Template;
    public string PaperColor { get; set; } = PaperPageDefaults.Color;
}

public sealed class StoredNotebook
{
    public required string FilePath { get; init; }
    public required NotebookDocument Document { get; init; }
}

public static class PageObjectDefaults
{
    public const string Kind = "Text";
    public const string ShapeKind = "Rectangle";
    public const string StrokeColor = "#3978F6";
    public const string FillColor = "#1A3978F6";
    public const double FontSize = 20;
    public const double Opacity = 1;
}

public sealed class PageObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = PageObjectDefaults.Kind;
    public double X { get; set; } = 180;
    public double Y { get; set; } = 180;
    public double Width { get; set; } = 280;
    public double Height { get; set; } = 140;
    public string Text { get; set; } = string.Empty;
    public string ImageData { get; set; } = string.Empty;
    public string ShapeKind { get; set; } = PageObjectDefaults.ShapeKind;
    public string StrokeColor { get; set; } = PageObjectDefaults.StrokeColor;
    public string FillColor { get; set; } = PageObjectDefaults.FillColor;
    public double StrokeThickness { get; set; } = 3;
    public double FontSize { get; set; } = PageObjectDefaults.FontSize;
    public double Opacity { get; set; } = PageObjectDefaults.Opacity;
    public double Rotation { get; set; }
    public bool IsLocked { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? LinkTargetPageId { get; set; }
    public Guid? LayerId { get; set; }
    public bool IsHidden { get; set; }

    public PageObject Clone()
    {
        return new PageObject
        {
            Kind = Kind,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            Text = Text,
            ImageData = ImageData,
            ShapeKind = ShapeKind,
            StrokeColor = StrokeColor,
            FillColor = FillColor,
            StrokeThickness = StrokeThickness,
            FontSize = FontSize,
            Opacity = Opacity,
            Rotation = Rotation,
            IsLocked = IsLocked,
            GroupId = GroupId,
            LinkTargetPageId = LinkTargetPageId,
            LayerId = LayerId,
            IsHidden = IsHidden
        };
    }
}






