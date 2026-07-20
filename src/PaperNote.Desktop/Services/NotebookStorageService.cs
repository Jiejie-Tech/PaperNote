using System.IO;
using System.Text.Json;
using PaperNote.Desktop.Models;

namespace PaperNote.Desktop.Services;

public sealed class NotebookStorageService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private readonly NotebookBackupService _backupService;

    public NotebookStorageService(string? notebooksDirectory = null, string? backupsDirectory = null)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        NotebooksDirectory = Path.GetFullPath(notebooksDirectory ?? Path.Combine(documents, "PaperNote", "Notebooks"));
        BackupsDirectory = Path.GetFullPath(backupsDirectory ?? Path.Combine(Path.GetDirectoryName(NotebooksDirectory)!, "Backups"));
        _backupService = new NotebookBackupService(NotebooksDirectory, BackupsDirectory, _jsonOptions);
    }

    public string NotebooksDirectory { get; }
    public string BackupsDirectory { get; }

    public async Task<IReadOnlyList<StoredNotebook>> ListAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(NotebooksDirectory);
        var notebooks = new List<StoredNotebook>();

        foreach (var filePath in Directory.EnumerateFiles(NotebooksDirectory, "*.papernote", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var document = await LoadAsync(filePath, cancellationToken);
                notebooks.Add(new StoredNotebook { FilePath = filePath, Document = document });
            }
            catch
            {
                // 单个损坏文件不应阻止整个书架打开；文件仍保留，便于后续恢复。
            }
        }

        return notebooks
            .OrderBy(item => item.Document.IsInTrash)
            .ThenByDescending(item => item.Document.IsInTrash ? item.Document.TrashedAt : item.Document.ModifiedAt)
            .ToArray();
    }

    public async Task<StoredNotebook> CreateAsync(NotebookDocument document, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(NotebooksDirectory);
        NormalizeDocument(document);
        var safeTitle = MakeSafeFileName(document.Title);
        var filePath = Path.Combine(NotebooksDirectory, $"{safeTitle}-{document.Id:N}.papernote");
        await SaveAsync(document, filePath, cancellationToken);
        return new StoredNotebook { FilePath = filePath, Document = document };
    }

    public async Task<NotebookDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        EnsurePathIsInNotebookDirectory(filePath, allowMissing: false);
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var document = JsonSerializer.Deserialize<NotebookDocument>(json, _jsonOptions)
            ?? throw new InvalidDataException("笔记本文件内容为空。");

        NormalizeDocument(document);
        return document;
    }

    public async Task SaveAsync(NotebookDocument document, string filePath, CancellationToken cancellationToken = default)
    {
        EnsurePathIsInNotebookDirectory(filePath, allowMissing: true);
        NormalizeDocument(document);
        document.FormatVersion = Math.Max(document.FormatVersion, 13);
        document.ModifiedAt = DateTimeOffset.Now;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, _jsonOptions);
        var directory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("无法确定笔记本保存目录。");

        Directory.CreateDirectory(directory);
        await _backupService.CreateAutomaticBackupAsync(filePath, cancellationToken);
        var temporaryPath = filePath + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public Task<IReadOnlyList<NotebookBackupInfo>> ListBackupsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return _backupService.ListAsync(filePath, cancellationToken);
    }

    public Task<NotebookBackupInfo> CreateBackupAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return _backupService.CreateManualBackupAsync(filePath, cancellationToken);
    }

    public async Task<NotebookDocument> RestoreBackupAsync(string filePath, string backupPath, CancellationToken cancellationToken = default)
    {
        await _backupService.RestoreAsync(filePath, backupPath, cancellationToken);
        return await LoadAsync(filePath, cancellationToken);
    }

    public async Task MoveToTrashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var document = await LoadAsync(filePath, cancellationToken);
        document.IsInTrash = true;
        document.TrashedAt = DateTimeOffset.Now;
        await SaveAsync(document, filePath, cancellationToken);
    }

    public async Task RestoreAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var document = await LoadAsync(filePath, cancellationToken);
        document.IsInTrash = false;
        document.TrashedAt = null;
        await SaveAsync(document, filePath, cancellationToken);
    }

    public void PermanentlyDelete(string filePath)
    {
        EnsurePathIsInNotebookDirectory(filePath, allowMissing: true);
        if (File.Exists(filePath)) File.Delete(filePath);
        var temporaryPath = filePath + ".tmp";
        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        var restoreTemporaryPath = filePath + ".restore.tmp";
        if (File.Exists(restoreTemporaryPath)) File.Delete(restoreTemporaryPath);
        _backupService.DeleteAll(filePath);
    }

    public static string NormalizeCoverStyle(string? coverStyle)
    {
        return coverStyle is "Blue" or "Purple" or "Green" or "Beige" or "Red"
            ? coverStyle
            : NotebookDefaults.CoverStyle;
    }

    public static string NormalizeFolderName(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return NotebookDefaults.FolderName;
        var cleaned = folderName.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars()) cleaned = cleaned.Replace(invalid, '_');
        return cleaned[..Math.Min(cleaned.Length, 30)];
    }

    private static void NormalizeDocument(NotebookDocument document)
    {
        document.Title = string.IsNullOrWhiteSpace(document.Title) ? "未命名笔记本" : document.Title.Trim();
        document.FolderName = NormalizeFolderName(document.FolderName);
        document.CoverStyle = NormalizeCoverStyle(document.CoverStyle);
        if (!document.IsInTrash) document.TrashedAt = null;
        if (document.LastOpenedAt is { } openedAt && openedAt < document.CreatedAt) document.LastOpenedAt = document.CreatedAt;

        document.PaperPresets ??= [];
        document.PaperPresets = document.PaperPresets
            .Where(preset => preset is not null)
            .Select(preset => new PaperPreset
            {
                Id = preset.Id == Guid.Empty ? Guid.NewGuid() : preset.Id,
                PaperTemplate = NormalizePaperTemplate(preset.PaperTemplate),
                PaperColor = string.IsNullOrWhiteSpace(preset.PaperColor) ? PaperPageDefaults.Color : preset.PaperColor
            })
            .DistinctBy(preset => $"{preset.PaperTemplate}|{preset.PaperColor}", StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        document.Pages ??= [];
        foreach (var page in document.Pages)
        {
            page.Title = NormalizePageTitle(page.Title);
            page.OutlineLevel = Math.Clamp(page.OutlineLevel, 0, 3);
            page.PaperTemplate = NormalizePaperTemplate(page.PaperTemplate);
            page.PaperColor = string.IsNullOrWhiteSpace(page.PaperColor) ? PaperPageDefaults.Color : page.PaperColor;
            page.InkData ??= string.Empty;
            page.BackgroundImageData ??= string.Empty;
            page.BackgroundSourceType = NormalizeBackgroundSourceType(page.BackgroundSourceType, page.BackgroundImageData);
            page.BackgroundSourceName = page.BackgroundSourceType is "PDF" or "Image" && !string.IsNullOrWhiteSpace(page.BackgroundSourceName) ? page.BackgroundSourceName.Trim() : string.Empty;
            page.BackgroundPageNumber = page.BackgroundSourceType == "PDF" ? Math.Max(1, page.BackgroundPageNumber) : 0;
            page.BackgroundRotation = NormalizeRightAngleRotation(page.BackgroundRotation);
            page.BackgroundCropLeft = NormalizeCrop(page.BackgroundCropLeft);
            page.BackgroundCropTop = NormalizeCrop(page.BackgroundCropTop);
            page.BackgroundCropRight = NormalizeCrop(page.BackgroundCropRight);
            page.BackgroundCropBottom = NormalizeCrop(page.BackgroundCropBottom);
            (page.BackgroundCropLeft, page.BackgroundCropRight) = NormalizeCropPair(page.BackgroundCropLeft, page.BackgroundCropRight);
            (page.BackgroundCropTop, page.BackgroundCropBottom) = NormalizeCropPair(page.BackgroundCropTop, page.BackgroundCropBottom);
            if (string.IsNullOrWhiteSpace(page.BackgroundImageData))
            {
                page.BackgroundRotation = 0;
                page.BackgroundCropLeft = page.BackgroundCropTop = page.BackgroundCropRight = page.BackgroundCropBottom = 0;
            }
            page.Objects ??= [];
            foreach (var pageObject in page.Objects)
            {
                NormalizePageObject(pageObject);
            }

            var validGroups = page.Objects
                .Where(pageObject => pageObject.GroupId.HasValue)
                .GroupBy(pageObject => pageObject.GroupId!.Value)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet();
            foreach (var pageObject in page.Objects)
            {
                if (pageObject.GroupId.HasValue && !validGroups.Contains(pageObject.GroupId.Value)) pageObject.GroupId = null;
            }
        }

        var validPageIds = document.Pages.Select(page => page.Id).ToHashSet();
        foreach (var page in document.Pages)
        {
            foreach (var pageObject in page.Objects)
            {
                if (pageObject.LinkTargetPageId.HasValue && !validPageIds.Contains(pageObject.LinkTargetPageId.Value)) pageObject.LinkTargetPageId = null;
            }
        }

        if (document.Pages.Count == 0)
        {
            var page = new NotebookPage();
            document.Pages.Add(page);
            document.CurrentPageId = page.Id;
        }
        else if (document.Pages.All(page => page.Id != document.CurrentPageId))
        {
            document.CurrentPageId = document.Pages[0].Id;
        }
    }

    private void EnsurePathIsInNotebookDirectory(string filePath, bool allowMissing)
    {
        var root = Path.GetFullPath(NotebooksDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(filePath);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(fullPath), ".papernote", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("笔记本文件不在 PaperNote 数据目录中。");
        }

        if (!allowMissing && !File.Exists(fullPath)) throw new FileNotFoundException("找不到笔记本文件。", fullPath);
    }

    public static string NormalizePageTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var cleaned = title.Trim();
        return cleaned[..Math.Min(cleaned.Length, 60)];
    }

    private static void NormalizePageObject(PageObject pageObject)
    {
        pageObject.Kind = pageObject.Kind is "Text" or "Image" or "Shape" ? pageObject.Kind : PageObjectDefaults.Kind;
        pageObject.ShapeKind = pageObject.ShapeKind is "Rectangle" or "RoundedRectangle" or "Ellipse" or "Triangle" or "Diamond" or "Arrow" or "Line" ? pageObject.ShapeKind : PageObjectDefaults.ShapeKind;
        pageObject.X = double.IsFinite(pageObject.X) ? Math.Clamp(pageObject.X, 0, 810) : 180;
        pageObject.Y = double.IsFinite(pageObject.Y) ? Math.Clamp(pageObject.Y, 0, 1158) : 180;
        pageObject.Width = double.IsFinite(pageObject.Width) ? Math.Clamp(pageObject.Width, 30, 840) : 280;
        pageObject.Height = double.IsFinite(pageObject.Height) ? Math.Clamp(pageObject.Height, 30, 1188) : 140;
        pageObject.X = Math.Min(pageObject.X, Math.Max(0, 840 - pageObject.Width));
        pageObject.Y = Math.Min(pageObject.Y, Math.Max(0, 1188 - pageObject.Height));
        pageObject.Text ??= string.Empty;
        pageObject.ImageData ??= string.Empty;
        pageObject.StrokeColor = string.IsNullOrWhiteSpace(pageObject.StrokeColor) ? PageObjectDefaults.StrokeColor : pageObject.StrokeColor;
        pageObject.FillColor = string.IsNullOrWhiteSpace(pageObject.FillColor) ? PageObjectDefaults.FillColor : pageObject.FillColor;
        pageObject.StrokeThickness = double.IsFinite(pageObject.StrokeThickness) ? Math.Clamp(pageObject.StrokeThickness, 1, 20) : 3;
        pageObject.FontSize = double.IsFinite(pageObject.FontSize) ? Math.Clamp(pageObject.FontSize, 10, 96) : PageObjectDefaults.FontSize;
        pageObject.Opacity = double.IsFinite(pageObject.Opacity) ? Math.Clamp(pageObject.Opacity, 0.1, 1) : PageObjectDefaults.Opacity;
        pageObject.Rotation = NormalizeRotation(pageObject.Rotation);
    }

    private static int NormalizeRightAngleRotation(int rotation)
    {
        var normalized = rotation % 360;
        if (normalized < 0) normalized += 360;
        return ((int)Math.Round(normalized / 90d) * 90) % 360;
    }

    private static double NormalizeCrop(double crop)
    {
        return double.IsFinite(crop) ? Math.Clamp(crop, 0, 0.45) : 0;
    }

    private static (double Leading, double Trailing) NormalizeCropPair(double leading, double trailing)
    {
        var total = leading + trailing;
        if (total <= 0.9) return (leading, trailing);
        var scale = 0.9 / total;
        return (leading * scale, trailing * scale);
    }

    private static double NormalizeRotation(double rotation)
    {
        if (!double.IsFinite(rotation)) return 0;
        var normalized = rotation % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static string NormalizeBackgroundSourceType(string? sourceType, string? imageData)
    {
        if (string.IsNullOrWhiteSpace(imageData)) return string.Empty;
        if (string.Equals(sourceType, "PDF", StringComparison.OrdinalIgnoreCase)) return "PDF";
        return string.Equals(sourceType, "Image", StringComparison.OrdinalIgnoreCase) ? "Image" : string.Empty;
    }
    private static string NormalizePaperTemplate(string? paperTemplate)
    {
        return paperTemplate is "Blank" or "Dotted" or "Lined" or "Grid"
            ? paperTemplate
            : PaperPageDefaults.Template;
    }

    public static string MakeSafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(title.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "未命名笔记本" : safe[..Math.Min(safe.Length, 40)];
    }
}





