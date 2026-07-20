using System.IO;
using System.IO.Compression;
using System.Text.Json;
using PaperNote.Desktop.Models;

namespace PaperNote.Desktop.Services;

public sealed class LibraryBackupManifest
{
    public int FormatVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public int NotebookCount { get; set; }
    public int BackupCount { get; set; }
}

public sealed class LibraryImportResult
{
    public int ImportedNotebooks { get; init; }
    public int SkippedNotebooks { get; init; }
    public int ImportedBackups { get; init; }
}

public sealed class LibraryBackupPackageService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public async Task<LibraryBackupManifest> ExportAsync(string packagePath, string notebooksDirectory, string backupsDirectory, CancellationToken cancellationToken = default)
    {
        packagePath = Path.GetFullPath(packagePath);
        notebooksDirectory = Path.GetFullPath(notebooksDirectory);
        backupsDirectory = Path.GetFullPath(backupsDirectory);
        var packageDirectory = Path.GetDirectoryName(packagePath) ?? throw new InvalidOperationException("无法确定备份包目录。");
        Directory.CreateDirectory(packageDirectory);
        var notebookFiles = Directory.Exists(notebooksDirectory) ? Directory.EnumerateFiles(notebooksDirectory, "*.papernote", SearchOption.TopDirectoryOnly).ToArray() : [];
        var backupFiles = Directory.Exists(backupsDirectory) ? Directory.EnumerateFiles(backupsDirectory, "*.papernote", SearchOption.AllDirectories).ToArray() : [];
        var manifest = new LibraryBackupManifest { NotebookCount = notebookFiles.Length, BackupCount = backupFiles.Length };
        var temporaryPath = packagePath + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                    await using (var manifestStream = manifestEntry.Open())
                        await JsonSerializer.SerializeAsync(manifestStream, manifest, _jsonOptions, cancellationToken);
                    foreach (var file in notebookFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await AddFileAsync(archive, file, $"notebooks/{Path.GetFileName(file)}", cancellationToken);
                    }
                    foreach (var file in backupFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(backupsDirectory, file).Replace('\\', '/');
                        await AddFileAsync(archive, file, $"backups/{relative}", cancellationToken);
                    }
                }
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, packagePath, overwrite: true);
            return manifest;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public async Task<LibraryImportResult> ImportAsync(string packagePath, string notebooksDirectory, string backupsDirectory, CancellationToken cancellationToken = default)
    {
        packagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(packagePath)) throw new FileNotFoundException("找不到所选资料库备份包。", packagePath);
        notebooksDirectory = Path.GetFullPath(notebooksDirectory);
        backupsDirectory = Path.GetFullPath(backupsDirectory);
        Directory.CreateDirectory(notebooksDirectory);
        Directory.CreateDirectory(backupsDirectory);

        using var archive = ZipFile.OpenRead(packagePath);
        var notebookEntries = archive.Entries.Where(entry => entry.FullName.StartsWith("notebooks/", StringComparison.OrdinalIgnoreCase) && entry.FullName.EndsWith(".papernote", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry.Name)).ToArray();
        if (notebookEntries.Length == 0) throw new InvalidDataException("备份包中没有可恢复的笔记本。");

        var validated = new List<(ZipArchiveEntry Entry, byte[] Bytes)>();
        foreach (var entry in notebookEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await ReadEntryBytesAsync(entry, cancellationToken);
            ValidateNotebookBytes(bytes, $"备份包中的 {entry.Name}");
            validated.Add((entry, bytes));
        }

        var validatedBackups = new List<(string SourceStem, string BackupName, byte[] Bytes)>();
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith("backups/", StringComparison.OrdinalIgnoreCase) && entry.FullName.EndsWith(".papernote", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry.Name)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = entry.FullName["backups/".Length..].Replace('\\', '/');
            var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;
            var bytes = await ReadEntryBytesAsync(entry, cancellationToken);
            ValidateNotebookBytes(bytes, $"历史版本 {entry.Name}");
            validatedBackups.Add((parts[0], Path.GetFileName(parts[1]), bytes));
        }

        var imported = 0;
        var skipped = 0;
        var importedBackups = 0;
        var stemMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in validated)
        {
            var safeName = Path.GetFileName(item.Entry.Name);
            var sourceStem = Path.GetFileNameWithoutExtension(safeName);
            var destination = Path.Combine(notebooksDirectory, safeName);
            if (File.Exists(destination))
            {
                var existing = await File.ReadAllBytesAsync(destination, cancellationToken);
                if (existing.AsSpan().SequenceEqual(item.Bytes))
                {
                    skipped++;
                    stemMap[sourceStem] = Path.GetFileNameWithoutExtension(destination);
                    continue;
                }
                destination = Path.Combine(notebooksDirectory, $"{sourceStem}-imported-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.papernote");
            }
            await WriteAtomicAsync(destination, item.Bytes, cancellationToken);
            imported++;
            stemMap[sourceStem] = Path.GetFileNameWithoutExtension(destination);
        }

        foreach (var item in validatedBackups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!stemMap.TryGetValue(item.SourceStem, out var destinationStem)) continue;
            var destinationDirectory = Path.Combine(backupsDirectory, destinationStem);
            Directory.CreateDirectory(destinationDirectory);
            var destination = Path.Combine(destinationDirectory, item.BackupName);
            if (File.Exists(destination))
            {
                var existing = await File.ReadAllBytesAsync(destination, cancellationToken);
                if (existing.AsSpan().SequenceEqual(item.Bytes)) continue;
                destination = Path.Combine(destinationDirectory, $"imported-{Guid.NewGuid():N}-{item.BackupName}");
            }
            await WriteAtomicAsync(destination, item.Bytes, cancellationToken);
            importedBackups++;
        }

        return new LibraryImportResult { ImportedNotebooks = imported, SkippedNotebooks = skipped, ImportedBackups = importedBackups };
    }

    private void ValidateNotebookBytes(byte[] bytes, string label)
    {
        try
        {
            var document = JsonSerializer.Deserialize<NotebookDocument>(bytes, _jsonOptions) ?? throw new InvalidDataException($"{label}内容为空。");
            if (document.Pages is null) throw new InvalidDataException($"{label}缺少页面数据。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{label}不是有效的 PaperNote 笔记。", exception);
        }
    }

    private static async Task AddFileAsync(ZipArchive archive, string sourcePath, string entryName, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var output = entry.Open();
        await input.CopyToAsync(output, cancellationToken);
    }

    private static async Task<byte[]> ReadEntryBytesAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Length > 100 * 1024 * 1024) throw new InvalidDataException($"备份包中的 {entry.Name} 超过 100 MB 限制。");
        await using var input = entry.Open();
        using var output = new MemoryStream();
        await input.CopyToAsync(output, cancellationToken);
        return output.ToArray();
    }

    private static async Task WriteAtomicAsync(string destination, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporaryPath = destination + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, destination);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
