using System.IO;
using System.Text.Json;
using PaperNote.Core.Models;
using PaperNote.Core.Ink;

namespace PaperNote.Core.Services;

public sealed partial class NotebookStorageService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private readonly NotebookBackupService _backupService;
    private readonly NotebookEncryptionService _encryptionService = new();

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
                var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
                if (NotebookEncryptionService.IsEncrypted(bytes))
                {
                    var metadata = _encryptionService.TryReadMetadata(bytes, out var parsed)
                        ? parsed
                        : CreateFallbackEncryptionMetadata(filePath);
                    notebooks.Add(new StoredNotebook
                    {
                        FilePath = filePath,
                        Document = metadata.CreateLockedPlaceholder(),
                        PageCount = metadata.PageCount,
                        IsEncrypted = true
                    });
                    continue;
                }

                var document = DeserializeDocument(bytes);
                notebooks.Add(new StoredNotebook
                {
                    FilePath = filePath,
                    Document = document,
                    PageCount = document.Pages.Count
                });
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
        return new StoredNotebook { FilePath = filePath, Document = document, PageCount = document.Pages.Count };
    }

    public async Task<NotebookDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        EnsurePathIsInNotebookDirectory(filePath, allowMissing: false);
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        if (NotebookEncryptionService.IsEncrypted(bytes))
            throw new NotebookPasswordRequiredException(filePath);
        return DeserializeDocument(bytes);
    }

    public async Task<NotebookDocument> LoadEncryptedAsync(string filePath, string password, CancellationToken cancellationToken = default)
    {
        EnsurePathIsInNotebookDirectory(filePath, allowMissing: false);
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        if (!NotebookEncryptionService.IsEncrypted(bytes)) return DeserializeDocument(bytes);
        var document = _encryptionService.Decrypt(bytes, password);
        NormalizeDocument(document);
        return document;
    }

    public async Task<bool> IsEncryptedAsync(string filePath, CancellationToken cancellationToken = default)
    {
        EnsurePathIsInNotebookDirectory(filePath, allowMissing: false);
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        return NotebookEncryptionService.IsEncrypted(bytes);
    }

    public Task SaveAsync(NotebookDocument document, string filePath, CancellationToken cancellationToken = default)
        => SaveCoreAsync(document, filePath, password: null, encrypt: false, allowReplaceEncrypted: false, cancellationToken);

    public Task SaveEncryptedAsync(NotebookDocument document, string filePath, string password, CancellationToken cancellationToken = default)
        => SaveCoreAsync(document, filePath, password, encrypt: true, allowReplaceEncrypted: true, cancellationToken);

    public Task RemoveEncryptionAsync(NotebookDocument document, string filePath, CancellationToken cancellationToken = default)
        => SaveCoreAsync(document, filePath, password: null, encrypt: false, allowReplaceEncrypted: true, cancellationToken);

    private async Task SaveCoreAsync(
        NotebookDocument document,
        string filePath,
        string? password,
        bool encrypt,
        bool allowReplaceEncrypted,
        CancellationToken cancellationToken)
    {
        EnsurePathIsInNotebookDirectory(filePath, allowMissing: true);
        if (!encrypt && !allowReplaceEncrypted && File.Exists(filePath))
        {
            var currentBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            if (NotebookEncryptionService.IsEncrypted(currentBytes))
                throw new NotebookPasswordRequiredException(filePath);
        }

        NormalizeDocument(document);
        document.FormatVersion = Math.Max(document.FormatVersion, 19);
        document.ModifiedAt = DateTimeOffset.Now;
        var bytes = encrypt
            ? _encryptionService.Encrypt(document, password ?? throw new ArgumentNullException(nameof(password)))
            : JsonSerializer.SerializeToUtf8Bytes(document, _jsonOptions);
        var directory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("无法确定笔记本保存目录。");

        Directory.CreateDirectory(directory);
        StorageCapacityService.EnsureCanWrite(filePath, bytes.LongLength);
        await _backupService.CreateAutomaticBackupAsync(filePath, cancellationToken);
        var temporaryPath = filePath + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            var persisted = await File.ReadAllBytesAsync(temporaryPath, cancellationToken);
            var verified = encrypt
                ? _encryptionService.Decrypt(persisted, password!)
                : DeserializeDocument(persisted);
            if (verified.Id != document.Id || verified.Pages is null)
                throw new InvalidDataException("临时保存文件验证失败：文档标识或页面数据不完整。");
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private NotebookDocument DeserializeDocument(ReadOnlySpan<byte> bytes)
    {
        var document = JsonSerializer.Deserialize<NotebookDocument>(bytes, _jsonOptions)
            ?? throw new InvalidDataException("笔记本文件内容为空。");
        NormalizeDocument(document);
        return document;
    }

    private static NotebookEncryptionMetadata CreateFallbackEncryptionMetadata(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var id = Guid.Empty;
        var separator = fileName.LastIndexOf('-');
        if (separator >= 0 && Guid.TryParseExact(fileName[(separator + 1)..], "N", out var parsed)) id = parsed;
        var title = separator > 0 ? fileName[..separator] : fileName;
        return new NotebookEncryptionMetadata
        {
            Id = id == Guid.Empty ? Guid.NewGuid() : id,
            Title = string.IsNullOrWhiteSpace(title) ? "加密笔记本" : title,
            ModifiedAt = File.GetLastWriteTimeUtc(filePath),
            PageCount = 1
        };
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
        await _backupService.RestoreAsync(filePath, backupPath, cancellationToken, (bytes, validationToken) =>
        {
            if (NotebookEncryptionService.IsEncrypted(bytes))
                throw new InvalidDataException("所选历史版本已加密，请输入该版本的密码后恢复。");
            _ = DeserializeDocument(bytes);
            return Task.FromResult(bytes);
        });
        return await LoadAsync(filePath, cancellationToken);
    }

    public Task<NotebookDocument> RestoreEncryptedBackupAsync(
        string filePath,
        string backupPath,
        string password,
        CancellationToken cancellationToken = default)
        => RestoreProtectedBackupAsync(filePath, backupPath, password, password, cancellationToken);

    public async Task<NotebookDocument> RestoreProtectedBackupAsync(
        string filePath,
        string backupPath,
        string backupPassword,
        string outputPassword,
        CancellationToken cancellationToken = default)
    {
        await _backupService.RestoreAsync(filePath, backupPath, cancellationToken, (bytes, validationToken) =>
        {
            var document = NotebookEncryptionService.IsEncrypted(bytes)
                ? _encryptionService.Decrypt(bytes, backupPassword)
                : DeserializeDocument(bytes);
            NormalizeDocument(document);
            return Task.FromResult(_encryptionService.Encrypt(document, outputPassword));
        });
        return await LoadEncryptedAsync(filePath, outputPassword, cancellationToken);
    }

    public async Task MoveToTrashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var document = await LoadAsync(filePath, cancellationToken);
        document.IsInTrash = true;
        document.TrashedAt = DateTimeOffset.Now;
        await SaveAsync(document, filePath, cancellationToken);
    }

    public async Task MoveEncryptedToTrashAsync(string filePath, string password, CancellationToken cancellationToken = default)
    {
        var document = await LoadEncryptedAsync(filePath, password, cancellationToken);
        document.IsInTrash = true;
        document.TrashedAt = DateTimeOffset.Now;
        await SaveEncryptedAsync(document, filePath, password, cancellationToken);
    }

    public async Task RestoreAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var document = await LoadAsync(filePath, cancellationToken);
        document.IsInTrash = false;
        document.TrashedAt = null;
        await SaveAsync(document, filePath, cancellationToken);
    }

    public async Task RestoreEncryptedAsync(string filePath, string password, CancellationToken cancellationToken = default)
    {
        var document = await LoadEncryptedAsync(filePath, password, cancellationToken);
        document.IsInTrash = false;
        document.TrashedAt = null;
        await SaveEncryptedAsync(document, filePath, password, cancellationToken);
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
        document.Tags = NormalizeTags(document.Tags);
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
        document.Pages = document.Pages.Where(page => page is not null).ToList();
        var usedPageIds = new HashSet<Guid>();
        foreach (var page in document.Pages)
        {
            if (page.Id == Guid.Empty || !usedPageIds.Add(page.Id))
            {
                page.Id = Guid.NewGuid();
                usedPageIds.Add(page.Id);
            }
        }
        foreach (var page in document.Pages)
        {
            page.Title = NormalizePageTitle(page.Title);
            page.OutlineLevel = Math.Clamp(page.OutlineLevel, 0, 3);
            page.PaperTemplate = NormalizePaperTemplate(page.PaperTemplate);
            page.PaperColor = string.IsNullOrWhiteSpace(page.PaperColor) ? PaperPageDefaults.Color : page.PaperColor;
            page.InkData ??= string.Empty;
            page.Ink ??= new PaperInkDocument();
            PaperInkSerializer.Normalize(page.Ink);
            page.BackgroundImageData ??= string.Empty;
            page.BackgroundSourceType = NormalizeBackgroundSourceType(page.BackgroundSourceType, page.BackgroundImageData);
            page.BackgroundSourceName = page.BackgroundSourceType is "PDF" or "Image" && !string.IsNullOrWhiteSpace(page.BackgroundSourceName) ? page.BackgroundSourceName.Trim() : string.Empty;
            page.BackgroundSourceFingerprint = page.BackgroundSourceType == "PDF" && !string.IsNullOrWhiteSpace(page.BackgroundSourceFingerprint) ? page.BackgroundSourceFingerprint.Trim()[..Math.Min(page.BackgroundSourceFingerprint.Trim().Length, 128)] : string.Empty;
            page.BackgroundPageNumber = page.BackgroundSourceType == "PDF" ? Math.Max(1, page.BackgroundPageNumber) : 0;
            page.BackgroundRotation = NormalizeRightAngleRotation(page.BackgroundRotation);
            page.BackgroundCropLeft = NormalizeCrop(page.BackgroundCropLeft);
            page.BackgroundCropTop = NormalizeCrop(page.BackgroundCropTop);
            page.BackgroundCropRight = NormalizeCrop(page.BackgroundCropRight);
            page.BackgroundCropBottom = NormalizeCrop(page.BackgroundCropBottom);
            page.Tags = NormalizeTags(page.Tags);
            page.OcrText = NormalizeIndexedText(page.OcrText);
            page.OcrBlocks ??= [];
            page.OcrBlocks = page.OcrBlocks.Where(block => block is not null).Take(4000).ToList();
            var usedOcrBlockIds = new HashSet<Guid>();
            foreach (var block in page.OcrBlocks)
            {
                if (block.Id == Guid.Empty || !usedOcrBlockIds.Add(block.Id))
                {
                    block.Id = Guid.NewGuid();
                    usedOcrBlockIds.Add(block.Id);
                }
                block.Text = NormalizeLimitedText(block.Text, 2000);
                block.Confidence = NormalizeUnit(block.Confidence);
                block.X = Math.Max(0, double.IsFinite(block.X) ? block.X : 0);
                block.Y = Math.Max(0, double.IsFinite(block.Y) ? block.Y : 0);
                block.Width = Math.Max(0, double.IsFinite(block.Width) ? block.Width : 0);
                block.Height = Math.Max(0, double.IsFinite(block.Height) ? block.Height : 0);
            }
            page.OcrAverageConfidence = page.OcrBlocks.Count == 0
                ? NormalizeUnit(page.OcrAverageConfidence)
                : page.OcrBlocks.Average(block => block.Confidence);
            if (page.OcrUpdatedAt is { } ocrUpdatedAt && ocrUpdatedAt < page.CreatedAt) page.OcrUpdatedAt = page.CreatedAt;
            if (string.IsNullOrWhiteSpace(page.OcrText)) page.OcrNeedsReview = false;
            page.RecognizedText = NormalizeIndexedText(page.RecognizedText);
            page.PdfText = page.BackgroundSourceType == "PDF" ? NormalizeIndexedText(page.PdfText) : string.Empty;
            page.PdfTextBlocks ??= [];
            page.PdfTextBlocks = page.BackgroundSourceType == "PDF"
                ? page.PdfTextBlocks.Where(block => block is not null).Take(20_000).ToList()
                : [];
            var usedPdfTextBlockIds = new HashSet<Guid>();
            foreach (var block in page.PdfTextBlocks)
            {
                if (block.Id == Guid.Empty || !usedPdfTextBlockIds.Add(block.Id))
                {
                    block.Id = Guid.NewGuid();
                    usedPdfTextBlockIds.Add(block.Id);
                }
                block.Text = NormalizeLimitedText(block.Text, 500);
                block.X = NormalizeUnit(block.X);
                block.Y = NormalizeUnit(block.Y);
                block.Width = Math.Clamp(NormalizeUnit(block.Width), 0, 1 - block.X);
                block.Height = Math.Clamp(NormalizeUnit(block.Height), 0, 1 - block.Y);
                block.ReadingOrder = Math.Max(0, block.ReadingOrder);
            }
            page.PdfTextBlocks = page.PdfTextBlocks.OrderBy(block => block.ReadingOrder).ToList();
            page.PdfPixelsPerMillimeter = double.IsFinite(page.PdfPixelsPerMillimeter)
                ? Math.Clamp(page.PdfPixelsPerMillimeter, 0.01, 10_000)
                : 4;
            page.PdfLinks ??= [];
            page.PdfLinks = page.BackgroundSourceType == "PDF"
                ? page.PdfLinks.Where(link => link is not null).Take(2000).ToList()
                : [];
            var usedPdfLinkIds = new HashSet<Guid>();
            foreach (var link in page.PdfLinks)
            {
                if (link.Id == Guid.Empty || !usedPdfLinkIds.Add(link.Id))
                {
                    link.Id = Guid.NewGuid();
                    usedPdfLinkIds.Add(link.Id);
                }
                link.X = NormalizeUnit(link.X);
                link.Y = NormalizeUnit(link.Y);
                link.Width = Math.Clamp(NormalizeUnit(link.Width), 0, 1 - link.X);
                link.Height = Math.Clamp(NormalizeUnit(link.Height), 0, 1 - link.Y);
                link.TargetSourcePageNumber = Math.Max(0, link.TargetSourcePageNumber);
                link.Label = NormalizeLimitedText(link.Label, 160);
            }
            page.Comments ??= [];
            page.Comments = page.Comments.Where(comment => comment is not null).Take(2000).ToList();
            var usedCommentIds = new HashSet<Guid>();
            foreach (var comment in page.Comments)
            {
                if (comment.Id == Guid.Empty || !usedCommentIds.Add(comment.Id))
                {
                    comment.Id = Guid.NewGuid();
                    usedCommentIds.Add(comment.Id);
                }
                comment.Text = NormalizeLimitedText(comment.Text, 2000);
                comment.Color = string.IsNullOrWhiteSpace(comment.Color) ? "#F0B429" : comment.Color.Trim();
                comment.X = double.IsFinite(comment.X) ? Math.Clamp(comment.X, 0, 840) : 24;
                comment.Y = double.IsFinite(comment.Y) ? Math.Clamp(comment.Y, 0, 1188) : 24;
                if (comment.ModifiedAt < comment.CreatedAt) comment.ModifiedAt = comment.CreatedAt;
            }
            page.Layers ??= [];
            var usedLayerIds = new HashSet<Guid>();
            page.Layers = page.Layers.Where(layer => layer is not null).Select(layer =>
            {
                var id = layer.Id;
                if (id == Guid.Empty || !usedLayerIds.Add(id))
                {
                    id = Guid.NewGuid();
                    usedLayerIds.Add(id);
                }
                var name = string.IsNullOrWhiteSpace(layer.Name) ? "图层" : layer.Name.Trim();
                return new PageLayer
                {
                    Id = id,
                    Name = name[..Math.Min(name.Length, 40)],
                    IsVisible = layer.IsVisible,
                    IsLocked = layer.IsLocked,
                    Opacity = double.IsFinite(layer.Opacity) ? Math.Clamp(layer.Opacity, .1, 1) : 1
                };
            }).ToList();
            page.AudioRecordings ??= [];
            page.AudioRecordings = page.AudioRecordings.Where(recording => recording is not null).ToList();
            var usedRecordingIds = new HashSet<Guid>();
            foreach (var recording in page.AudioRecordings)
            {
                if (recording.Id == Guid.Empty || !usedRecordingIds.Add(recording.Id))
                {
                    recording.Id = Guid.NewGuid();
                    usedRecordingIds.Add(recording.Id);
                }
                recording.LocalFilePath = (recording.LocalFilePath ?? string.Empty).Trim();
                var displayName = string.IsNullOrWhiteSpace(recording.DisplayName) ? "本地录音" : recording.DisplayName.Trim();
                recording.DisplayName = displayName[..Math.Min(displayName.Length, 80)];
                recording.DurationMilliseconds = Math.Max(0, recording.DurationMilliseconds);
                recording.FileSize = Math.Max(0, recording.FileSize);
                recording.SampleRate = Math.Clamp(recording.SampleRate <= 0 ? 48000 : recording.SampleRate, 8000, 192000);
                recording.BitRate = Math.Clamp(recording.BitRate <= 0 ? 128000 : recording.BitRate, 16000, 512000);
                recording.InputDeviceName = NormalizeLimitedText(recording.InputDeviceName, 160);
                recording.TrimStartMilliseconds = Math.Clamp(recording.TrimStartMilliseconds, 0, recording.DurationMilliseconds);
                recording.TrimEndMilliseconds = recording.TrimEndMilliseconds <= 0
                    ? recording.DurationMilliseconds
                    : Math.Clamp(recording.TrimEndMilliseconds, recording.TrimStartMilliseconds, recording.DurationMilliseconds);
                recording.MimeType = string.IsNullOrWhiteSpace(recording.MimeType) ? "audio/mp4" : recording.MimeType.Trim();
                recording.WaveformPeaks ??= [];
                recording.WaveformPeaks = recording.WaveformPeaks
                    .Where(float.IsFinite)
                    .Select(value => Math.Clamp(value, 0f, 1f))
                    .Take(AudioWaveformService.MaximumStoredPeakCount)
                    .ToList();
                recording.Cues ??= [];
                recording.Cues = recording.Cues.Where(cue => cue is not null).ToList();
                var usedCueIds = new HashSet<Guid>();
                foreach (var cue in recording.Cues)
                {
                    if (cue.Id == Guid.Empty || !usedCueIds.Add(cue.Id))
                    {
                        cue.Id = Guid.NewGuid();
                        usedCueIds.Add(cue.Id);
                    }
                    cue.OffsetMilliseconds = Math.Max(0, cue.OffsetMilliseconds);
                    if (recording.DurationMilliseconds > 0) cue.OffsetMilliseconds = Math.Min(cue.OffsetMilliseconds, recording.DurationMilliseconds);
                    var label = (cue.Label ?? string.Empty).Trim();
                    cue.Label = label[..Math.Min(label.Length, 80)];
                }
                recording.Cues = recording.Cues.OrderBy(cue => cue.OffsetMilliseconds).ToList();
            }
            if (page.Layers.Count == 0) page.Layers.Add(new PageLayer());
            page.ActiveLayerId = page.Layers.Any(layer => layer.Id == page.ActiveLayerId) ? page.ActiveLayerId : page.Layers[0].Id;
            foreach (var stroke in page.Ink.Strokes)
            {
                if (stroke.LayerId is Guid layerId && page.Layers.All(layer => layer.Id != layerId)) stroke.LayerId = page.ActiveLayerId;
            }
            (page.BackgroundCropLeft, page.BackgroundCropRight) = NormalizeCropPair(page.BackgroundCropLeft, page.BackgroundCropRight);
            (page.BackgroundCropTop, page.BackgroundCropBottom) = NormalizeCropPair(page.BackgroundCropTop, page.BackgroundCropBottom);
            if (string.IsNullOrWhiteSpace(page.BackgroundImageData))
            {
                page.BackgroundRotation = 0;
                page.BackgroundCropLeft = page.BackgroundCropTop = page.BackgroundCropRight = page.BackgroundCropBottom = 0;
            }
            page.Objects ??= [];
            page.Objects = page.Objects.Where(pageObject => pageObject is not null).ToList();
            var usedObjectIds = new HashSet<Guid>();
            foreach (var pageObject in page.Objects)
            {
                if (pageObject.Id == Guid.Empty || !usedObjectIds.Add(pageObject.Id))
                {
                    pageObject.Id = Guid.NewGuid();
                    usedObjectIds.Add(pageObject.Id);
                }
                NormalizePageObject(pageObject);
                if (pageObject.LayerId is Guid layerId && page.Layers.All(layer => layer.Id != layerId)) pageObject.LayerId = page.ActiveLayerId;
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
            foreach (var link in page.PdfLinks)
            {
                if (link.TargetPageId.HasValue && !validPageIds.Contains(link.TargetPageId.Value)) link.TargetPageId = null;
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

        validPageIds = document.Pages.Select(page => page.Id).ToHashSet();
        document.OutlineEntries ??= [];
        document.OutlineEntries = document.OutlineEntries.Where(entry => entry is not null).Take(5000).ToList();
        var usedOutlineIds = new HashSet<Guid>();
        foreach (var entry in document.OutlineEntries)
        {
            if (entry.Id == Guid.Empty || !usedOutlineIds.Add(entry.Id))
            {
                entry.Id = Guid.NewGuid();
                usedOutlineIds.Add(entry.Id);
            }
            entry.Title = NormalizeLimitedText(entry.Title, 200);
            entry.Level = Math.Clamp(entry.Level, 1, 6);
            entry.SourceFingerprint = NormalizeLimitedText(entry.SourceFingerprint, 128);
            entry.SourcePageNumber = Math.Max(0, entry.SourcePageNumber);
            if (entry.TargetPageId.HasValue && !validPageIds.Contains(entry.TargetPageId.Value)) entry.TargetPageId = null;
        }
        document.OutlineEntries.RemoveAll(entry => string.IsNullOrWhiteSpace(entry.Title) || !entry.TargetPageId.HasValue);
        PdfDocumentContentService.ResolveInternalLinks(document);
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

    private static List<string> NormalizeTags(IEnumerable<string>? tags)
    {
        return (tags ?? []).Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim()).Where(tag => tag.Length <= 30)
            .Distinct(StringComparer.CurrentCultureIgnoreCase).Take(30).ToList();
    }

    public static string NormalizePageTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var cleaned = title.Trim();
        return cleaned[..Math.Min(cleaned.Length, 60)];
    }

    private static string NormalizeIndexedText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalized = text.Trim();
        return normalized[..Math.Min(normalized.Length, PdfDocumentContentService.MaximumExtractedCharactersPerPage)];
    }

    private static string NormalizeLimitedText(string? text, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalized = text.Trim();
        return normalized[..Math.Min(normalized.Length, maximumLength)];
    }

    private static double NormalizeUnit(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

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
        pageObject.AltText = NormalizeLimitedText(pageObject.AltText, 500);
        pageObject.FormulaLatex = NormalizeLimitedText(pageObject.FormulaLatex, 4000);
        pageObject.FormFieldName = NormalizeLimitedText(pageObject.FormFieldName, 120);
        pageObject.FormFieldKind = pageObject.FormFieldKind is "Text" or "Checkbox" or "Signature" ? pageObject.FormFieldKind : string.Empty;
        pageObject.FormFieldValue = NormalizeLimitedText(pageObject.FormFieldValue, 4000);
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





