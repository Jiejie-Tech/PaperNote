namespace PaperNote.Core.Services;

public static class AudioAttachmentService
{
    public const string DirectoryName = "Audio";

    public static string GetAudioRoot(string notebooksDirectory)
    {
        var notebooksRoot = Path.GetFullPath(notebooksDirectory);
        var libraryRoot = Path.GetDirectoryName(notebooksRoot) ?? notebooksRoot;
        return Path.Combine(libraryRoot, DirectoryName);
    }

    public static string CreateRelativePath(Guid notebookId, Guid pageId, Guid recordingId, string extension)
    {
        extension = NormalizeExtension(extension);
        return Path.Combine(DirectoryName, notebookId.ToString("N"), pageId.ToString("N"), recordingId.ToString("N") + extension)
            .Replace('\\', '/');
    }

    public static string ResolvePath(string notebooksDirectory, string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return string.Empty;
        if (Path.IsPathRooted(storedPath)) return Path.GetFullPath(storedPath);

        var notebooksRoot = Path.GetFullPath(notebooksDirectory);
        var libraryRoot = Path.GetDirectoryName(notebooksRoot) ?? notebooksRoot;
        var normalized = storedPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(libraryRoot, normalized));
        var safeRoot = Path.GetFullPath(libraryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("录音附件路径超出了 PaperNote 资料库。");
        return candidate;
    }

    public static string ToStoredPath(string notebooksDirectory, string absolutePath)
    {
        var notebooksRoot = Path.GetFullPath(notebooksDirectory);
        var libraryRoot = Path.GetDirectoryName(notebooksRoot) ?? notebooksRoot;
        var fullPath = Path.GetFullPath(absolutePath);
        var safeRoot = Path.GetFullPath(libraryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("录音附件必须保存在 PaperNote 资料库中。");
        return Path.GetRelativePath(libraryRoot, fullPath).Replace('\\', '/');
    }

    public static string PrepareRecordingPath(string notebooksDirectory, Guid notebookId, Guid pageId, Guid recordingId, string extension)
    {
        var storedPath = CreateRelativePath(notebookId, pageId, recordingId, extension);
        var path = ResolvePath(notebooksDirectory, storedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    public static bool TryDelete(string notebooksDirectory, string storedPath)
    {
        try
        {
            var path = ResolvePath(notebooksDirectory, storedPath);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return ".m4a";
        extension = extension.Trim();
        if (!extension.StartsWith('.')) extension = "." + extension;
        return extension.ToLowerInvariant() switch
        {
            ".m4a" or ".mp4" or ".aac" or ".wav" => extension.ToLowerInvariant(),
            _ => ".m4a"
        };
    }
}
