using System.Text;
using System.Text.Json;
using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public sealed partial class NotebookStorageService
{
    private const long MaximumRecoveryDocumentBytes = 100L * 1024 * 1024;

    public async Task<IReadOnlyList<NotebookRecoveryCandidate>> InspectRecoveryAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(NotebooksDirectory);
        var candidates = new List<NotebookRecoveryCandidate>();
        var paths = Directory.EnumerateFiles(NotebooksDirectory, "*.papernote.tmp", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(NotebooksDirectory, "*.papernote", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isTemporary = path.EndsWith(".papernote.tmp", StringComparison.OrdinalIgnoreCase);
            var kind = isTemporary ? NotebookRecoveryKind.TemporaryDraft : NotebookRecoveryKind.CorruptedNotebook;
            NotebookDocument? document = null;
            string? error = null;
            try
            {
                document = await DeserializeRecoveryDocumentAsync(path, cancellationToken);
                if (!isTemporary && document is not null) continue;
                if (document is null) error = "文件内容无法完整读取。";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                error = exception.Message;
            }

            var info = new FileInfo(path);
            candidates.Add(new NotebookRecoveryCandidate
            {
                FilePath = path,
                Kind = kind,
                LastWriteTime = info.LastWriteTime,
                FileSize = info.Exists ? info.Length : 0,
                IsReadable = document is not null,
                DisplayName = Path.GetFileNameWithoutExtension(isTemporary ? Path.GetFileNameWithoutExtension(path) : path),
                Error = error,
                Document = document
            });
        }

        return candidates.OrderByDescending(item => item.LastWriteTime).ToArray();
    }

    public async Task<IReadOnlyList<NotebookRecoveryResult>> RecoverTemporaryDraftsAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(NotebooksDirectory);
        var results = new List<NotebookRecoveryResult>();
        foreach (var temporaryPath in Directory.EnumerateFiles(NotebooksDirectory, "*.papernote.tmp", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = temporaryPath[..^4];
            NotebookDocument? document = null;
            try
            {
                document = await DeserializeRecoveryDocumentAsync(temporaryPath, cancellationToken);
                if (document is null)
                {
                    results.Add(FailedRecovery(temporaryPath, "临时草稿内容不完整，已保留供只读抢救。"));
                    continue;
                }

                var temporaryInfo = new FileInfo(temporaryPath);
                var targetInfo = new FileInfo(targetPath);
                if (targetInfo.Exists && targetInfo.LastWriteTimeUtc >= temporaryInfo.LastWriteTimeUtc)
                {
                    results.Add(new NotebookRecoveryResult
                    {
                        FilePath = temporaryPath,
                        Kind = NotebookRecoveryKind.TemporaryDraft,
                        Recovered = false,
                        Message = "正式文件更新，不覆盖当前笔记。",
                        Document = document
                    });
                    continue;
                }

                if (targetInfo.Exists)
                {
                    try { await _backupService.CreateManualBackupAsync(targetPath, cancellationToken); }
                    catch (JsonException) { /* 正式文件已损坏时，临时草稿仍可作为抢救来源。 */ }
                    catch (InvalidDataException) { /* 同上。 */ }
                }

                File.Move(temporaryPath, targetPath, overwrite: true);
                results.Add(new NotebookRecoveryResult
                {
                    FilePath = targetPath,
                    Kind = NotebookRecoveryKind.TemporaryDraft,
                    Recovered = true,
                    Message = "已从临时草稿恢复最新内容。",
                    Document = document
                });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                results.Add(FailedRecovery(temporaryPath, exception.Message));
            }
        }

        return results;
    }

    public async Task<ReadOnlyNotebookRecovery> ReadForRecoveryAsync(string filePath, CancellationToken cancellationToken = default)
    {
        EnsureRecoveryPath(filePath);
        var fullPath = Path.GetFullPath(filePath);
        var displayName = Path.GetFileNameWithoutExtension(fullPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(fullPath)
            : fullPath);
        byte[] bytes;
        try
        {
            var info = new FileInfo(fullPath);
            if (info.Length > MaximumRecoveryDocumentBytes)
                return new ReadOnlyNotebookRecovery { FilePath = fullPath, DisplayName = displayName, IsReadable = false, Error = "文件过大，不能直接在抢救视图中读取。" };
            bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ReadOnlyNotebookRecovery { FilePath = fullPath, DisplayName = displayName, IsReadable = false, Error = exception.Message };
        }

        try
        {
            var document = JsonSerializer.Deserialize<NotebookDocument>(bytes, _jsonOptions);
            if (document is not null)
            {
                NormalizeDocument(document);
                return new ReadOnlyNotebookRecovery { FilePath = fullPath, DisplayName = displayName, IsReadable = true, Document = document, RawPreview = MakePreview(bytes) };
            }
        }
        catch (JsonException) { }

        try
        {
            using var json = JsonDocument.Parse(bytes);
            var salvaged = SalvageDocument(json.RootElement);
            if (salvaged is not null)
            {
                NormalizeDocument(salvaged);
                return new ReadOnlyNotebookRecovery
                {
                    FilePath = fullPath,
                    DisplayName = displayName,
                    IsReadable = true,
                    Document = salvaged,
                    Error = "文件部分损坏，当前内容仅供只读抢救；请另存为新笔记本。",
                    RawPreview = MakePreview(bytes)
                };
            }
        }
        catch (JsonException) { }

        return new ReadOnlyNotebookRecovery
        {
            FilePath = fullPath,
            DisplayName = displayName,
            IsReadable = false,
            Error = "文件不是完整的 JSON 文档，无法自动恢复结构化内容。",
            RawPreview = MakePreview(bytes)
        };
    }

    /// <summary>Creates a normal notebook from readable recovery content without overwriting the source file.</summary>
    public async Task<StoredNotebook> SaveRecoveryCopyAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var recovery = await ReadForRecoveryAsync(filePath, cancellationToken);
        if (!recovery.IsReadable || recovery.Document is null)
            throw new InvalidDataException(recovery.Error ?? "The recovery file does not contain a readable notebook.");

        var document = recovery.Document;
        document.Id = Guid.NewGuid();
        document.Title = $"{(string.IsNullOrWhiteSpace(document.Title) ? recovery.DisplayName : document.Title)} (抢救副本)";
        document.CreatedAt = DateTimeOffset.Now;
        document.ModifiedAt = DateTimeOffset.Now;
        document.LastOpenedAt = null;
        document.IsInTrash = false;
        document.TrashedAt = null;
        NormalizeDocument(document);
        return await CreateAsync(document, cancellationToken);
    }

    private async Task<NotebookDocument?> DeserializeRecoveryDocumentAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaximumRecoveryDocumentBytes) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<NotebookDocument>(stream, _jsonOptions, cancellationToken);
        if (document is null) return null;
        NormalizeDocument(document);
        return document;
    }

    private static NotebookRecoveryResult FailedRecovery(string path, string message)
        => new() { FilePath = path, Kind = NotebookRecoveryKind.TemporaryDraft, Recovered = false, Message = message };

    private void EnsureRecoveryPath(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var root = Path.GetFullPath(NotebooksDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !(fullPath.EndsWith(".papernote", StringComparison.OrdinalIgnoreCase) || fullPath.EndsWith(".papernote.tmp", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("恢复文件不在 PaperNote 数据目录中。");
        if (!File.Exists(fullPath)) throw new FileNotFoundException("找不到待恢复文件。", fullPath);
    }

    private static NotebookDocument? SalvageDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        var document = new NotebookDocument();
        if (root.TryGetProperty("FormatVersion", out var format) && format.TryGetInt32(out var formatVersion)) document.FormatVersion = formatVersion;
        if (root.TryGetProperty("Id", out var id) && id.TryGetGuid(out var documentId)) document.Id = documentId;
        if (root.TryGetProperty("Title", out var title) && title.ValueKind == JsonValueKind.String) document.Title = title.GetString() ?? document.Title;
        if (root.TryGetProperty("CreatedAt", out var created) && created.TryGetDateTimeOffset(out var createdAt)) document.CreatedAt = createdAt;
        if (root.TryGetProperty("ModifiedAt", out var modified) && modified.TryGetDateTimeOffset(out var modifiedAt)) document.ModifiedAt = modifiedAt;
        if (root.TryGetProperty("CurrentPageId", out var currentPage) && currentPage.TryGetGuid(out var currentPageId)) document.CurrentPageId = currentPageId;
        if (root.TryGetProperty("Pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
        {
            foreach (var pageElement in pages.EnumerateArray())
            {
                try
                {
                    var page = pageElement.Deserialize<NotebookPage>();
                    if (page is not null) document.Pages.Add(page);
                }
                catch (JsonException) { }
            }
        }
        if (document.Pages.Count == 0 && string.IsNullOrWhiteSpace(document.Title)) return null;
        return document;
    }

    private static string MakePreview(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).Replace("\0", string.Empty, StringComparison.Ordinal);
        return text.Length <= 4000 ? text : text[..4000];
    }
}
