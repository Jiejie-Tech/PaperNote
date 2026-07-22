using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

/// <summary>使用本地 AES-GCM 和 PBKDF2 对整个笔记本进行加密。</summary>
public sealed class NotebookEncryptionService
{
    private static readonly byte[] Magic = "PNENC1"u8.ToArray();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = false };

    public byte[] Encrypt(NotebookDocument document, string password)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidatePassword(password);
        var plain = JsonSerializer.SerializeToUtf8Bytes(document, _jsonOptions);
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag, Magic);
            using var stream = new MemoryStream();
            stream.Write(Magic); stream.Write(salt); stream.Write(nonce); stream.Write(tag); stream.Write(cipher);
            return stream.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public NotebookDocument Decrypt(ReadOnlySpan<byte> envelope, string password)
    {
        ValidatePassword(password);
        const int headerLength = 6 + 16 + 12 + 16;
        if (envelope.Length <= headerLength || !envelope[..Magic.Length].SequenceEqual(Magic)) throw new InvalidDataException("不是有效的 PaperNote 加密数据。");
        var salt = envelope.Slice(6, 16); var nonce = envelope.Slice(22, 12); var tag = envelope.Slice(34, 16); var cipher = envelope[50..];
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain, Magic);
            return JsonSerializer.Deserialize<NotebookDocument>(plain, _jsonOptions) ?? throw new InvalidDataException("解密后的笔记本内容为空。");
        }
        catch (CryptographicException exception)
        {
            throw new UnauthorizedAccessException("密码错误或笔记本已损坏。", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static bool IsEncrypted(ReadOnlySpan<byte> content) => content.Length >= Magic.Length && content[..Magic.Length].SequenceEqual(Magic);

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8) throw new ArgumentException("密码至少需要 8 个字符。", nameof(password));
    }
}
