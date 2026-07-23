using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public sealed class LibraryBackupFileManifest
{
    public string Path { get; set; } = string.Empty;
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class LibraryBackupManifest
{
    public int FormatVersion { get; set; } = 3;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public int NotebookCount { get; set; }
    public int BackupCount { get; set; }
    public int AudioCount { get; set; }
    public List<LibraryBackupFileManifest> Files { get; set; } = [];
}

public sealed class LibraryImportResult
{
    public int ImportedNotebooks { get; init; }
    public int SkippedNotebooks { get; init; }
    public int ImportedBackups { get; init; }
    public int ImportedAudioAttachments { get; init; }
    public int SkippedAudioAttachments { get; init; }
}

public sealed class LibraryBackupPackageService
{
    private const int CurrentFormatVersion = 3;
    private const int MinimumSupportedFormatVersion = 2;
    private const long MaximumDocumentEntryBytes = 100L * 1024 * 1024;
    private const long MaximumAudioEntryBytes = 2L * 1024 * 1024 * 1024;
    private const long MaximumTotalUncompressedBytes = 4L * 1024 * 1024 * 1024;
    private const int MaximumEntryCount = 100_000;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public async Task<LibraryBackupManifest> ExportAsync(
        string packagePath,
        string notebooksDirectory,
        string backupsDirectory,
        CancellationToken cancellationToken = default)
    {
        packagePath = Path.GetFullPath(packagePath);
        notebooksDirectory = Path.GetFullPath(notebooksDirectory);
        backupsDirectory = Path.GetFullPath(backupsDirectory);
        var audioRoot = AudioAttachmentService.GetAudioRoot(notebooksDirectory);
        var packageDirectory = Path.GetDirectoryName(packagePath) ?? throw new InvalidOperationException("无法确定备份包目录。");
        Directory.CreateDirectory(packageDirectory);

        var notebookFiles = EnumerateFiles(notebooksDirectory, "*.papernote", SearchOption.TopDirectoryOnly);
        var backupFiles = EnumerateFiles(backupsDirectory, "*.papernote", SearchOption.AllDirectories);
        var audioFiles = Directory.Exists(audioRoot)
            ? EnumerateFiles(audioRoot, "*", SearchOption.AllDirectories)
            : [];

        var manifest = new LibraryBackupManifest
        {
            FormatVersion = CurrentFormatVersion,
            NotebookCount = notebookFiles.Length,
            BackupCount = backupFiles.Length,
            AudioCount = audioFiles.Length
        };

        foreach (var file in notebookFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            manifest.Files.Add(await CreateManifestEntryAsync(
                file,
                $"notebooks/{Path.GetFileName(file)}",
                cancellationToken));
        }

        foreach (var file in backupFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeArchivePath(Path.GetRelativePath(backupsDirectory, file));
            manifest.Files.Add(await CreateManifestEntryAsync(file, $"backups/{relative}", cancellationToken));
        }

        foreach (var file in audioFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeArchivePath(Path.GetRelativePath(audioRoot, file));
            manifest.Files.Add(await CreateManifestEntryAsync(file, $"audio/{relative}", cancellationToken));
        }

        ValidateManifest(manifest);
        var temporaryPath = packagePath + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                    await using (var manifestStream = manifestEntry.Open())
                    {
                        await JsonSerializer.SerializeAsync(manifestStream, manifest, _jsonOptions, cancellationToken);
                    }

                    foreach (var file in notebookFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await AddFileAsync(archive, file, $"notebooks/{Path.GetFileName(file)}", cancellationToken);
                    }

                    foreach (var file in backupFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relative = NormalizeArchivePath(Path.GetRelativePath(backupsDirectory, file));
                        await AddFileAsync(archive, file, $"backups/{relative}", cancellationToken);
                    }

                    foreach (var file in audioFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relative = NormalizeArchivePath(Path.GetRelativePath(audioRoot, file));
                        await AddFileAsync(archive, file, $"audio/{relative}", cancellationToken);
                    }
                }

                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, packagePath, overwrite: true);
            return manifest;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    public async Task<LibraryImportResult> ImportAsync(
        string packagePath,
        string notebooksDirectory,
        string backupsDirectory,
        CancellationToken cancellationToken = default)
    {
        packagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(packagePath)) throw new FileNotFoundException("找不到资料库备份包。", packagePath);
        notebooksDirectory = Path.GetFullPath(notebooksDirectory);
        backupsDirectory = Path.GetFullPath(backupsDirectory);
        var audioRoot = AudioAttachmentService.GetAudioRoot(notebooksDirectory);
        Directory.CreateDirectory(notebooksDirectory);
        Directory.CreateDirectory(backupsDirectory);

        var stagingRoot = Path.Combine(Path.GetDirectoryName(notebooksDirectory) ?? notebooksDirectory, $".papernote-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            ValidateArchiveShape(archive);
            var manifest = await ReadManifestAsync(archive, cancellationToken);
            var expectedFiles = BuildExpectedFileMap(manifest);
            ValidateManifestEntriesMatchArchive(archive, expectedFiles);
            var totalUncompressedBytes = archive.Entries.Sum(entry => Math.Max(0L, entry.Length));
            if (totalUncompressedBytes > MaximumTotalUncompressedBytes)
                throw new InvalidDataException("备份包解压后的总大小超过安全限制。");

            var notebookEntries = archive.Entries
                .Where(entry => IsFileEntryInDirectory(entry, "notebooks") && entry.Name.EndsWith(".papernote", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (notebookEntries.Length == 0) throw new InvalidDataException("备份包中没有可恢复的笔记本。");

            var validatedNotebooks = new List<ValidatedDocument>();
            foreach (var entry in notebookEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = await ReadEntryBytesAsync(entry, MaximumDocumentEntryBytes, cancellationToken);
                VerifyEntryIntegrity(entry.FullName, bytes, expectedFiles);
                ValidateNotebookBytes(bytes, $"笔记本 {entry.Name}");
                validatedNotebooks.Add(new ValidatedDocument(entry.Name, bytes));
            }

            var validatedBackups = new List<ValidatedBackup>();
            foreach (var entry in archive.Entries.Where(entry => IsFileEntryInDirectory(entry, "backups") && entry.Name.EndsWith(".papernote", StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = GetArchiveRelativePath(entry.FullName, "backups");
                var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
                    throw new InvalidDataException($"备份条目路径无效：{entry.FullName}");
                var bytes = await ReadEntryBytesAsync(entry, MaximumDocumentEntryBytes, cancellationToken);
                VerifyEntryIntegrity(entry.FullName, bytes, expectedFiles);
                ValidateNotebookBytes(bytes, $"历史版本 {entry.Name}");
                validatedBackups.Add(new ValidatedBackup(parts[0], Path.GetFileName(parts[1]), bytes));
            }

            var validatedAudio = new List<ValidatedAudio>();
            foreach (var entry in archive.Entries.Where(entry => IsFileEntryInDirectory(entry, "audio")))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = GetArchiveRelativePath(entry.FullName, "audio");
                EnsureSafeRelativePath(relative, entry.FullName);
                var staged = Path.Combine(stagingRoot, "audio", relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                await CopyAndVerifyEntryAsync(entry, staged, expectedFiles, cancellationToken);
                validatedAudio.Add(new ValidatedAudio(relative, staged));
            }

            var imported = 0;
            var skipped = 0;
            var importedBackups = 0;
            var importedAudio = 0;
            var skippedAudio = 0;
            var stemMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in validatedNotebooks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var safeName = Path.GetFileName(item.Name);
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

            foreach (var item in validatedAudio)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = ResolveSafeLibraryPath(audioRoot, item.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(destination))
                {
                    if (await FilesEqualAsync(destination, item.StagedPath, cancellationToken))
                    {
                        skippedAudio++;
                        continue;
                    }
                    destination = Path.Combine(Path.GetDirectoryName(destination)!, $"imported-{Guid.NewGuid():N}-{Path.GetFileName(destination)}");
                }
                await MoveFileAtomicAsync(item.StagedPath, destination, cancellationToken);
                importedAudio++;
            }

            return new LibraryImportResult
            {
                ImportedNotebooks = imported,
                SkippedNotebooks = skipped,
                ImportedBackups = importedBackups,
                ImportedAudioAttachments = importedAudio,
                SkippedAudioAttachments = skippedAudio
            };
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    private async Task<LibraryBackupManifest> ReadManifestAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("备份包缺少 manifest.json。");
        await using var manifestStream = manifestEntry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<LibraryBackupManifest>(manifestStream, _jsonOptions, cancellationToken)
            ?? throw new InvalidDataException("备份清单内容为空。");
        if (manifest.FormatVersion < MinimumSupportedFormatVersion || manifest.FormatVersion > CurrentFormatVersion)
            throw new InvalidDataException($"不支持的备份包格式版本：{manifest.FormatVersion}。");
        ValidateManifest(manifest);
        return manifest;
    }

    private static Dictionary<string, LibraryBackupFileManifest> BuildExpectedFileMap(LibraryBackupManifest manifest)
    {
        return manifest.Files
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .ToDictionary(item => NormalizeArchivePath(item.Path), StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateManifestEntriesMatchArchive(ZipArchive archive, IReadOnlyDictionary<string, LibraryBackupFileManifest> expectedFiles)
    {
        var actual = archive.Entries
            .Where(entry => !entry.FullName.EndsWith('/') && !string.IsNullOrWhiteSpace(entry.Name))
            .Select(entry => NormalizeArchivePath(entry.FullName))
            .Where(path => !string.Equals(path, "manifest.json", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actual.SetEquals(expectedFiles.Keys))
        {
            var missing = expectedFiles.Keys.Except(actual, StringComparer.OrdinalIgnoreCase).Take(3);
            var unexpected = actual.Except(expectedFiles.Keys, StringComparer.OrdinalIgnoreCase).Take(3);
            throw new InvalidDataException($"备份清单与压缩包内容不一致。缺少：{string.Join(", ", missing)}；多余：{string.Join(", ", unexpected)}");
        }
    }

    private static void ValidateManifest(LibraryBackupManifest manifest)
    {
        if (manifest.Files.Count > MaximumEntryCount) throw new InvalidDataException("备份清单中的文件数量超过安全限制。");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.Files)
        {
            var path = NormalizeArchivePath(item.Path);
            EnsureSafeArchivePath(path, "manifest.json");
            if (!paths.Add(path)) throw new InvalidDataException($"备份清单包含重复路径：{path}");
            if (item.Length < 0 || item.Length > MaximumTotalUncompressedBytes) throw new InvalidDataException($"备份清单中的文件大小无效：{path}");
            if (string.IsNullOrWhiteSpace(item.Sha256) || item.Sha256.Length != 64 || !item.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException($"备份清单中的校验值无效：{path}");
        }
    }

    private static void ValidateArchiveShape(ZipArchive archive)
    {
        if (archive.Entries.Count > MaximumEntryCount) throw new InvalidDataException("备份包中的文件数量超过安全限制。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var normalized = NormalizeArchivePath(entry.FullName);
            if (!names.Add(normalized)) throw new InvalidDataException($"备份包包含重复路径：{normalized}");
            if (entry.FullName.EndsWith('/') || string.IsNullOrWhiteSpace(entry.Name)) continue;
            EnsureSafeArchivePath(normalized, entry.FullName);
            if (entry.Length < 0 || entry.Length > MaximumTotalUncompressedBytes)
                throw new InvalidDataException($"备份条目大小无效：{entry.FullName}");
        }
    }

    private static async Task<LibraryBackupFileManifest> CreateManifestEntryAsync(string sourcePath, string archivePath, CancellationToken cancellationToken)
    {
        return new LibraryBackupFileManifest
        {
            Path = NormalizeArchivePath(archivePath),
            Length = new FileInfo(sourcePath).Length,
            Sha256 = await DocumentIntegrityService.ComputeFileSha256Async(sourcePath, cancellationToken)
        };
    }

    private static bool IsFileEntryInDirectory(ZipArchiveEntry entry, string directory)
    {
        var prefix = directory.TrimEnd('/') + "/";
        return !entry.FullName.EndsWith('/') && entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry.Name);
    }

    private static string GetArchiveRelativePath(string archivePath, string directory)
    {
        var normalized = NormalizeArchivePath(archivePath);
        var prefix = directory.TrimEnd('/') + "/";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"备份条目目录无效：{archivePath}");
        return normalized[prefix.Length..];
    }

    private static string NormalizeArchivePath(string path) => path.Replace('\\', '/');

    private static void EnsureSafeArchivePath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0') || Path.IsPathRooted(path) || path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException($"{label} 包含无效的相对路径：{path}");
    }

    private static void EnsureSafeRelativePath(string path, string label)
    {
        EnsureSafeArchivePath(path, label);
        if (path.StartsWith("/", StringComparison.Ordinal) || path.Contains(':')) throw new InvalidDataException($"{label} 包含绝对路径：{path}");
    }

    private static string ResolveSafeLibraryPath(string audioRoot, string relativePath)
    {
        EnsureSafeRelativePath(relativePath, relativePath);
        var root = Path.GetFullPath(audioRoot);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"目标路径越界：{relativePath}");
        return candidate;
    }

    private static void VerifyEntryIntegrity(string path, byte[] bytes, IReadOnlyDictionary<string, LibraryBackupFileManifest> expectedFiles)
    {
        var normalized = NormalizeArchivePath(path);
        if (!expectedFiles.TryGetValue(normalized, out var expected))
            throw new InvalidDataException($"备份清单缺少文件：{normalized}");
        if (expected.Length != bytes.LongLength || !DocumentIntegrityService.Verify(bytes, expected.Sha256))
            throw new InvalidDataException($"文件校验失败：{normalized}");
    }

    private void ValidateNotebookBytes(byte[] bytes, string label)
    {
        if (NotebookEncryptionService.IsEncrypted(bytes))
        {
            if (bytes.Length <= 6 + 16 + 12 + 16)
                throw new InvalidDataException($"{label} 的加密内容不完整。");
            return;
        }

        try
        {
            var document = JsonSerializer.Deserialize<NotebookDocument>(bytes, _jsonOptions) ?? throw new InvalidDataException($"{label} 内容为空。");
            if (document.Pages is null) throw new InvalidDataException($"{label} 缺少页面数据。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{label} 不是有效的 PaperNote 文档。", exception);
        }
    }

    private static async Task AddFileAsync(ZipArchive archive, string sourcePath, string entryName, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(NormalizeArchivePath(entryName), CompressionLevel.Optimal);
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var output = entry.Open();
        await input.CopyToAsync(output, cancellationToken);
    }

    private static async Task<byte[]> ReadEntryBytesAsync(ZipArchiveEntry entry, long maximumBytes, CancellationToken cancellationToken)
    {
        if (entry.Length > maximumBytes) throw new InvalidDataException($"备份条目 {entry.Name} 超过大小限制。");
        await using var input = entry.Open();
        using var output = new MemoryStream(capacity: checked((int)Math.Min(entry.Length, int.MaxValue)));
        await input.CopyToAsync(output, cancellationToken);
        return output.ToArray();
    }

    private static async Task CopyAndVerifyEntryAsync(ZipArchiveEntry entry, string stagedPath, IReadOnlyDictionary<string, LibraryBackupFileManifest> expectedFiles, CancellationToken cancellationToken)
    {
        await using var input = entry.Open();
        await using var output = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            length += read;
            if (length > MaximumAudioEntryBytes) throw new InvalidDataException($"音频附件过大：{entry.Name}");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hash.AppendData(buffer, 0, read);
        }
        await output.FlushAsync(cancellationToken);
        var normalized = NormalizeArchivePath(entry.FullName);
        if (!expectedFiles.TryGetValue(normalized, out var expected) || expected.Length != length || !Convert.ToHexString(hash.GetHashAndReset()).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"音频附件校验失败：{normalized}");
    }

    private static async Task<bool> FilesEqualAsync(string left, string right, CancellationToken cancellationToken)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length) return false;
        await using var leftStream = new FileStream(left, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var rightStream = new FileStream(right, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var leftBuffer = new byte[81920];
        var rightBuffer = new byte[81920];
        while (true)
        {
            var leftRead = await leftStream.ReadAsync(leftBuffer.AsMemory(), cancellationToken);
            var rightRead = await rightStream.ReadAsync(rightBuffer.AsMemory(), cancellationToken);
            if (leftRead != rightRead) return false;
            if (leftRead == 0) return true;
            if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead))) return false;
        }
    }

    private static async Task WriteAtomicAsync(string destination, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporaryPath = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static async Task MoveFileAtomicAsync(string source, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var temporaryPath = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.Move(source, temporaryPath);
            File.Move(temporaryPath, destination, overwrite: true);
            await Task.CompletedTask;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static string[] EnumerateFiles(string root, string pattern, SearchOption option) =>
        Directory.Exists(root) ? Directory.EnumerateFiles(root, pattern, option).Where(path => !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)).ToArray() : [];

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private sealed record ValidatedDocument(string Name, byte[] Bytes);
    private sealed record ValidatedBackup(string SourceStem, string BackupName, byte[] Bytes);
    private sealed record ValidatedAudio(string RelativePath, string StagedPath);
}
