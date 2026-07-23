namespace PaperNote.Core.Services;

public sealed class InsufficientStorageException : IOException
{
    public long RequiredBytes { get; }
    public long AvailableBytes { get; }

    public InsufficientStorageException(long requiredBytes, long availableBytes)
        : base($"本地空间不足：保存至少需要 {Format(requiredBytes)}，当前可用 {Format(availableBytes)}。请清理空间后重试，原笔记不会被覆盖。")
    {
        RequiredBytes = requiredBytes;
        AvailableBytes = availableBytes;
    }

    private static string Format(long bytes) => $"{bytes / 1024d / 1024d:0.0} MB";
}

public static class StorageCapacityService
{
    public const long DefaultSafetyReserveBytes = 96L * 1024 * 1024;

    public static void EnsureCanWrite(string targetPath, long payloadBytes, long safetyReserveBytes = DefaultSafetyReserveBytes)
    {
        if (payloadBytes < 0) throw new ArgumentOutOfRangeException(nameof(payloadBytes));
        var fullPath = Path.GetFullPath(targetPath);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root)) return;
        try
        {
            var drive = new DriveInfo(root);
            if (!drive.IsReady) return;
            var existingBytes = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
            var required = CalculateRequiredBytes(payloadBytes, existingBytes, safetyReserveBytes);
            if (drive.AvailableFreeSpace < required) throw new InsufficientStorageException(required, drive.AvailableFreeSpace);
        }
        catch (ArgumentException) { }
    }

    public static long CalculateRequiredBytes(long payloadBytes, long existingBytes = 0, long safetyReserveBytes = DefaultSafetyReserveBytes)
    {
        payloadBytes = Math.Max(0, payloadBytes);
        existingBytes = Math.Max(0, existingBytes);
        safetyReserveBytes = Math.Max(0, safetyReserveBytes);
        // Temporary write + verification + backup can coexist briefly. Existing bytes may be reused after atomic replacement.
        return checked(payloadBytes * 2 + Math.Min(existingBytes, payloadBytes) + safetyReserveBytes);
    }

    public static bool HasEnoughSpace(long availableBytes, long payloadBytes, long existingBytes = 0, long safetyReserveBytes = DefaultSafetyReserveBytes)
        => Math.Max(0, availableBytes) >= CalculateRequiredBytes(payloadBytes, existingBytes, safetyReserveBytes);
}
