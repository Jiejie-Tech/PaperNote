using System.Security.Cryptography;

namespace PaperNote.Core.Services;

public static class DocumentIntegrityService
{
    public static string ComputeSha256(ReadOnlySpan<byte> content) => Convert.ToHexStringLower(SHA256.HashData(content));

    public static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    public static bool Verify(ReadOnlySpan<byte> content, string? expectedHash)
        => !string.IsNullOrWhiteSpace(expectedHash) && CryptographicOperations.FixedTimeEquals(EncodingBytes(ComputeSha256(content)), EncodingBytes(expectedHash.Trim().ToLowerInvariant()));

    private static byte[] EncodingBytes(string value) => System.Text.Encoding.ASCII.GetBytes(value);
}
