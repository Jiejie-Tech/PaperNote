using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaperNote.Core.Services;

public enum PdfImportJobStatus
{
    Preparing,
    Rendering,
    Cancelled,
    Failed,
    Completed
}

public sealed record PdfImportProgress(
    string Stage,
    int CompletedPages,
    int TotalPages,
    int CurrentPage,
    bool FromCache,
    string Message)
{
    public double Fraction => TotalPages <= 0 ? 0 : Math.Clamp(CompletedPages / (double)TotalPages, 0, 1);
}

public sealed class PdfImportJobManifest
{
    public int FormatVersion { get; set; } = 1;
    public string JobId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string SourceFingerprint { get; set; } = string.Empty;
    public long SourceLength { get; set; }
    public int SourcePageCount { get; set; }
    public int TargetWidth { get; set; }
    public int TargetHeight { get; set; }
    public List<int> RequestedPages { get; set; } = [];
    public Dictionary<int, string> CachedPages { get; set; } = [];
    public PdfImportJobStatus Status { get; set; } = PdfImportJobStatus.Preparing;
    public string LastError { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PdfImportCacheService
{
    public const long DefaultMaximumCacheBytes = 1024L * 1024 * 1024;
    private const string ManifestFileName = "manifest.json";
    private const string SourceFileName = "source.pdf";
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public PdfImportCacheService(string rootDirectory, long maximumCacheBytes = DefaultMaximumCacheBytes)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("缓存目录不能为空。", nameof(rootDirectory));
        if (maximumCacheBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCacheBytes));
        RootDirectory = Path.GetFullPath(rootDirectory);
        MaximumCacheBytes = maximumCacheBytes;
    }

    public string RootDirectory { get; }
    public long MaximumCacheBytes { get; }

    public async Task<PdfImportJobManifest> PrepareAsync(
        string sourcePath,
        string sourceName,
        IReadOnlyList<int> requestedPages,
        int sourcePageCount,
        int targetWidth,
        int targetHeight,
        IProgress<PdfImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(sourcePath, requestedPages, sourcePageCount, targetWidth, targetHeight);
        Directory.CreateDirectory(RootDirectory);
        var sourceInfo = new FileInfo(sourcePath);
        progress?.Report(new PdfImportProgress("准备", 0, requestedPages.Count, 0, false, "正在检查 PDF…"));
        var fingerprint = await ComputeFingerprintAsync(sourcePath, cancellationToken);
        var canonicalPages = requestedPages.Distinct().OrderBy(page => page).ToArray();
        var jobId = ComputeJobId(fingerprint, canonicalPages, targetWidth, targetHeight);
        var jobDirectory = GetJobDirectory(jobId);
        var manifestPath = Path.Combine(jobDirectory, ManifestFileName);
        Directory.CreateDirectory(jobDirectory);

        var manifest = await TryLoadManifestAsync(manifestPath, cancellationToken);
        if (manifest is null || !Matches(manifest, fingerprint, sourceInfo.Length, sourcePageCount, canonicalPages, targetWidth, targetHeight))
        {
            DeleteDirectoryContents(jobDirectory);
            Directory.CreateDirectory(jobDirectory);
            manifest = new PdfImportJobManifest
            {
                JobId = jobId,
                SourceName = string.IsNullOrWhiteSpace(sourceName) ? Path.GetFileName(sourcePath) : sourceName,
                SourceFingerprint = fingerprint,
                SourceLength = sourceInfo.Length,
                SourcePageCount = sourcePageCount,
                TargetWidth = targetWidth,
                TargetHeight = targetHeight,
                RequestedPages = canonicalPages.ToList(),
                Status = PdfImportJobStatus.Preparing
            };
        }

        RemoveMissingCacheEntries(manifest, jobDirectory);
        await PruneAsync(jobId, cancellationToken);
        var cachedSourcePath = Path.Combine(jobDirectory, SourceFileName);
        if (!File.Exists(cachedSourcePath) || new FileInfo(cachedSourcePath).Length != sourceInfo.Length)
        {
            var temporaryPath = cachedSourcePath + ".tmp";
            DeleteTemporaryFile(temporaryPath);
            try
            {
                await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await source.CopyToAsync(target, 1024 * 1024, cancellationToken);
                File.Move(temporaryPath, cachedSourcePath, true);
            }
            catch
            {
                DeleteTemporaryFile(temporaryPath);
                throw;
            }
        }

        manifest.Status = PdfImportJobStatus.Rendering;
        manifest.LastError = string.Empty;
        manifest.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveManifestAsync(manifest, cancellationToken);
        await PruneAsync(jobId, cancellationToken);
        return manifest;
    }

    public string GetSourcePath(PdfImportJobManifest manifest)
        => Path.Combine(GetJobDirectory(ValidateJobId(manifest.JobId)), SourceFileName);

    public IReadOnlyList<int> GetMissingPages(PdfImportJobManifest manifest)
    {
        var jobDirectory = GetJobDirectory(ValidateJobId(manifest.JobId));
        RemoveMissingCacheEntries(manifest, jobDirectory);
        return manifest.RequestedPages.Where(page => !manifest.CachedPages.ContainsKey(page)).ToArray();
    }

    public bool TryReadPage(PdfImportJobManifest manifest, int pageNumber, out byte[] bytes)
    {
        bytes = [];
        if (!manifest.CachedPages.TryGetValue(pageNumber, out var relativePath)) return false;
        var path = ResolveCacheFile(manifest.JobId, relativePath);
        if (!File.Exists(path))
        {
            manifest.CachedPages.Remove(pageNumber);
            return false;
        }
        bytes = File.ReadAllBytes(path);
        return bytes.Length > 0;
    }

    public async Task SavePageAsync(PdfImportJobManifest manifest, int pageNumber, ReadOnlyMemory<byte> bytes, string extension = ".jpg", CancellationToken cancellationToken = default)
    {
        if (!manifest.RequestedPages.Contains(pageNumber)) throw new ArgumentOutOfRangeException(nameof(pageNumber));
        if (bytes.IsEmpty) throw new ArgumentException("页面缓存不能为空。", nameof(bytes));
        extension = extension.StartsWith('.') ? extension : "." + extension;
        if (extension.Length > 8 || extension.Any(character => !char.IsLetterOrDigit(character) && character != '.'))
            throw new ArgumentException("缓存扩展名无效。", nameof(extension));

        var fileName = $"page-{pageNumber:D6}{extension.ToLowerInvariant()}";
        var path = ResolveCacheFile(manifest.JobId, fileName);
        var temporaryPath = path + ".tmp";
        DeleteTemporaryFile(temporaryPath);
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes.ToArray(), cancellationToken);
            File.Move(temporaryPath, path, true);
        }
        catch
        {
            DeleteTemporaryFile(temporaryPath);
            throw;
        }
        manifest.CachedPages[pageNumber] = fileName;
        manifest.Status = PdfImportJobStatus.Rendering;
        manifest.LastError = string.Empty;
        manifest.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveManifestAsync(manifest, cancellationToken);
    }

    public Task MarkCompletedAsync(PdfImportJobManifest manifest, CancellationToken cancellationToken = default)
        => SetStatusAsync(manifest, PdfImportJobStatus.Completed, string.Empty, cancellationToken);

    public Task MarkCancelledAsync(PdfImportJobManifest manifest, CancellationToken cancellationToken = default)
        => SetStatusAsync(manifest, PdfImportJobStatus.Cancelled, "用户取消；已完成页面将在下次导入时继续使用。", cancellationToken);

    public Task MarkFailedAsync(PdfImportJobManifest manifest, Exception exception, CancellationToken cancellationToken = default)
        => SetStatusAsync(manifest, PdfImportJobStatus.Failed, exception.Message, cancellationToken);

    public async Task PruneAsync(string? preserveJobId = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(RootDirectory)) return;
        var directories = new DirectoryInfo(RootDirectory).EnumerateDirectories()
            .Where(directory => !string.Equals(directory.Name, preserveJobId, StringComparison.OrdinalIgnoreCase))
            .Select(directory => new
            {
                Directory = directory,
                LastWrite = directory.LastWriteTimeUtc,
                Size = SafeEnumerateFiles(directory.FullName).Sum(file => file.Length)
            })
            .OrderByDescending(item => item.LastWrite)
            .ToList();
        var preservedSize = string.IsNullOrWhiteSpace(preserveJobId) ? 0 : DirectorySize(GetJobDirectory(preserveJobId));
        var total = preservedSize + directories.Sum(item => item.Size);
        foreach (var item in directories.OrderBy(item => item.LastWrite))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (total <= MaximumCacheBytes && item.LastWrite >= DateTime.UtcNow.AddDays(-30)) break;
            try
            {
                item.Directory.Delete(true);
                total -= item.Size;
            }
            catch { }
            await Task.Yield();
        }
    }

    public async Task<PdfImportJobManifest?> LoadAsync(string jobId, CancellationToken cancellationToken = default)
        => await TryLoadManifestAsync(Path.Combine(GetJobDirectory(ValidateJobId(jobId)), ManifestFileName), cancellationToken);

    private async Task SetStatusAsync(PdfImportJobManifest manifest, PdfImportJobStatus status, string error, CancellationToken cancellationToken)
    {
        manifest.Status = status;
        manifest.LastError = error;
        manifest.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveManifestAsync(manifest, cancellationToken);
    }

    private async Task SaveManifestAsync(PdfImportJobManifest manifest, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetJobDirectory(ValidateJobId(manifest.JobId)), ManifestFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, _jsonOptions);
        DeleteTemporaryFile(temporaryPath);
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, path, true);
        }
        catch
        {
            DeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    private async Task<PdfImportJobManifest?> TryLoadManifestAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<PdfImportJobManifest>(stream, _jsonOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string GetJobDirectory(string jobId) => Path.Combine(RootDirectory, ValidateJobId(jobId));

    private string ResolveCacheFile(string jobId, string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new InvalidDataException("PDF 缓存路径无效。");
        var root = GetJobDirectory(ValidateJobId(jobId));
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("PDF 缓存路径越界。");
        return path;
    }

    private static string ValidateJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId) || jobId.Length != 32 || jobId.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("PDF 导入任务标识无效。");
        return jobId.ToLowerInvariant();
    }

    private static async Task<string> ComputeFingerprintAsync(string sourcePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string ComputeJobId(string fingerprint, IReadOnlyList<int> pages, int width, int height)
    {
        var payload = $"{fingerprint}|{width}x{height}|{string.Join(',', pages)}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
    }

    private static bool Matches(PdfImportJobManifest manifest, string fingerprint, long length, int sourcePageCount, IReadOnlyList<int> pages, int width, int height)
        => manifest.FormatVersion == 1 &&
           manifest.SourceFingerprint == fingerprint &&
           manifest.SourceLength == length &&
           manifest.SourcePageCount == sourcePageCount &&
           manifest.TargetWidth == width &&
           manifest.TargetHeight == height &&
           manifest.RequestedPages.SequenceEqual(pages);

    private static void ValidateRequest(string sourcePath, IReadOnlyList<int> pages, int sourcePageCount, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("请选择 PDF 文件。", nameof(sourcePath));
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("找不到要导入的 PDF 文件。", sourcePath);
        if (pages.Count == 0) throw new ArgumentException("请至少选择一页 PDF。", nameof(pages));
        if (pages.Count > PdfPageRangeService.MaximumImportPageCount) throw new InvalidDataException($"一次最多导入 {PdfPageRangeService.MaximumImportPageCount} 页。");
        if (sourcePageCount <= 0 || pages.Any(page => page < 1 || page > sourcePageCount)) throw new InvalidDataException("PDF 页码范围无效。");
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
    }

    private static void RemoveMissingCacheEntries(PdfImportJobManifest manifest, string jobDirectory)
    {
        foreach (var item in manifest.CachedPages.ToArray())
        {
            var path = Path.GetFullPath(Path.Combine(jobDirectory, item.Value));
            var prefix = jobDirectory.EndsWith(Path.DirectorySeparatorChar) ? jobDirectory : jobDirectory + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || new FileInfo(path).Length == 0)
                manifest.CachedPages.Remove(item.Key);
        }
    }

    private static IEnumerable<FileInfo> SafeEnumerateFiles(string directory)
    {
        try { return new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories).ToArray(); }
        catch { return []; }
    }

    private static long DirectorySize(string directory) => Directory.Exists(directory) ? SafeEnumerateFiles(directory).Sum(file => file.Length) : 0;

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void DeleteDirectoryContents(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
        foreach (var child in Directory.EnumerateDirectories(directory)) Directory.Delete(child, true);
    }
}
