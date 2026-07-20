using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PaperNote.Desktop.Models;

namespace PaperNote.Desktop.Services;

public sealed class NotebookBackupInfo
{
    public required string FilePath { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string Title { get; init; }
    public required int PageCount { get; init; }
    public required string Kind { get; init; }

    public string DisplayText => $"{CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {PageCount} 页 · {Kind}";
}

internal sealed class NotebookBackupService
{
    private const int MaximumBackupsPerNotebook = 30;
    private static readonly TimeSpan AutomaticBackupInterval = TimeSpan.FromMinutes(2);
    private readonly string _notebooksDirectory;
    private readonly string _backupsDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public NotebookBackupService(string notebooksDirectory, string backupsDirectory, JsonSerializerOptions jsonOptions)
    {
        _notebooksDirectory = Path.GetFullPath(notebooksDirectory);
        _backupsDirectory = Path.GetFullPath(backupsDirectory);
        _jsonOptions = jsonOptions;
    }

    public async Task CreateAutomaticBackupAsync(string notebookPath, CancellationToken cancellationToken)
    {
        EnsureNotebookPath(notebookPath, allowMissing: true);
        if (!File.Exists(notebookPath)) return;

        var backupDirectory = GetNotebookBackupDirectory(notebookPath);
        var latestAutomatic = Directory.Exists(backupDirectory)
            ? Directory.EnumerateFiles(backupDirectory, "*-auto-*.papernote", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (latestAutomatic is not null && DateTime.UtcNow - latestAutomatic.LastWriteTimeUtc < AutomaticBackupInterval) return;

        var bytes = await File.ReadAllBytesAsync(notebookPath, cancellationToken);
        await CreateBackupFromBytesAsync(notebookPath, bytes, "auto", deduplicate: true, cancellationToken);
    }

    public async Task<NotebookBackupInfo> CreateManualBackupAsync(string notebookPath, CancellationToken cancellationToken)
    {
        EnsureNotebookPath(notebookPath, allowMissing: false);
        var bytes = await File.ReadAllBytesAsync(notebookPath, cancellationToken);
        var backupPath = await CreateBackupFromBytesAsync(notebookPath, bytes, "manual", deduplicate: false, cancellationToken)
            ?? throw new InvalidOperationException("无法创建当前版本备份。");
        return await ReadBackupInfoAsync(backupPath, cancellationToken)
            ?? throw new InvalidDataException("刚创建的版本备份无法读取。");
    }

    public async Task<IReadOnlyList<NotebookBackupInfo>> ListAsync(string notebookPath, CancellationToken cancellationToken)
    {
        EnsureNotebookPath(notebookPath, allowMissing: true);
        var directory = GetNotebookBackupDirectory(notebookPath);
        if (!Directory.Exists(directory)) return [];

        var result = new List<NotebookBackupInfo>();
        foreach (var backupPath in Directory.EnumerateFiles(directory, "*.papernote", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = await ReadBackupInfoAsync(backupPath, cancellationToken);
            if (info is not null) result.Add(info);
        }
        return result.OrderByDescending(item => item.CreatedAt).ToArray();
    }

    public async Task RestoreAsync(string notebookPath, string backupPath, CancellationToken cancellationToken)
    {
        EnsureNotebookPath(notebookPath, allowMissing: false);
        EnsureBackupPath(notebookPath, backupPath);

        var backupBytes = await File.ReadAllBytesAsync(backupPath, cancellationToken);
        _ = JsonSerializer.Deserialize<NotebookDocument>(backupBytes, _jsonOptions)
            ?? throw new InvalidDataException("历史版本内容为空。");

        var currentBytes = await File.ReadAllBytesAsync(notebookPath, cancellationToken);
        await CreateBackupFromBytesAsync(notebookPath, currentBytes, "before-restore", deduplicate: false, cancellationToken);

        var temporaryPath = notebookPath + ".restore.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, backupBytes, cancellationToken);
            File.Move(temporaryPath, notebookPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public void DeleteAll(string notebookPath)
    {
        EnsureNotebookPath(notebookPath, allowMissing: true);
        var directory = GetNotebookBackupDirectory(notebookPath);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private async Task<string?> CreateBackupFromBytesAsync(string notebookPath, byte[] bytes, string kind, bool deduplicate, CancellationToken cancellationToken)
    {
        if (bytes.Length == 0) return null;
        _ = JsonSerializer.Deserialize<NotebookDocument>(bytes, _jsonOptions)
            ?? throw new InvalidDataException("无法备份空白笔记本文件。");

        var directory = GetNotebookBackupDirectory(notebookPath);
        Directory.CreateDirectory(directory);
        if (deduplicate)
        {
            var latest = Directory.EnumerateFiles(directory, "*.papernote", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (latest is not null)
            {
                var latestBytes = await File.ReadAllBytesAsync(latest, cancellationToken);
                if (SHA256.HashData(latestBytes).AsSpan().SequenceEqual(SHA256.HashData(bytes))) return null;
            }
        }

        var timestamp = DateTimeOffset.Now;
        var fileName = $"{timestamp:yyyyMMdd-HHmmss-fff}-{kind}-{Guid.NewGuid():N}.papernote";
        var backupPath = Path.Combine(directory, fileName);
        var temporaryPath = backupPath + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, backupPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }

        TrimBackups(directory);
        return backupPath;
    }

    private async Task<NotebookBackupInfo?> ReadBackupInfoAsync(string backupPath, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(backupPath, cancellationToken);
            var document = JsonSerializer.Deserialize<NotebookDocument>(bytes, _jsonOptions);
            if (document is null) return null;
            var fileName = Path.GetFileName(backupPath);
            var kind = fileName.Contains("-before-restore-", StringComparison.OrdinalIgnoreCase)
                ? "恢复前保护点"
                : fileName.Contains("-manual-", StringComparison.OrdinalIgnoreCase) ? "手动版本" : "自动备份";
            return new NotebookBackupInfo
            {
                FilePath = backupPath,
                CreatedAt = File.GetLastWriteTime(backupPath),
                Title = string.IsNullOrWhiteSpace(document.Title) ? "未命名笔记本" : document.Title,
                PageCount = document.Pages?.Count ?? 0,
                Kind = kind
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void TrimBackups(string directory)
    {
        foreach (var stale in Directory.EnumerateFiles(directory, "*.papernote", SearchOption.TopDirectoryOnly)
                     .Select(path => new FileInfo(path))
                     .OrderByDescending(info => info.LastWriteTimeUtc)
                     .Skip(MaximumBackupsPerNotebook))
        {
            stale.Delete();
        }
    }

    private string GetNotebookBackupDirectory(string notebookPath)
    {
        var stem = Path.GetFileNameWithoutExtension(notebookPath);
        return Path.Combine(_backupsDirectory, stem);
    }

    private void EnsureNotebookPath(string notebookPath, bool allowMissing)
    {
        var root = _notebooksDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(notebookPath);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(fullPath), ".papernote", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("笔记本文件不在 PaperNote 数据目录中。");
        }
        if (!allowMissing && !File.Exists(fullPath)) throw new FileNotFoundException("找不到笔记本文件。", fullPath);
    }

    private void EnsureBackupPath(string notebookPath, string backupPath)
    {
        var root = Path.GetFullPath(GetNotebookBackupDirectory(notebookPath)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(backupPath);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(fullPath), ".papernote", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath))
        {
            throw new InvalidOperationException("所选历史版本不属于当前笔记本。");
        }
    }
}
