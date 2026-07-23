using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

/// <summary>使用本地 AES-GCM 和 PBKDF2 对整个笔记本进行加密。</summary>
public sealed class NotebookEncryptionService
{
    private static readonly byte[] MagicV1 = "PNENC1"u8.ToArray();
    private static readonly byte[] MagicV2 = "PNENC2"u8.ToArray();
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int MetadataLengthField = 4;
    private const int MaximumMetadataLength = 64 * 1024;
    private const int Iterations = 210_000;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public byte[] Encrypt(NotebookDocument document, string password)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidatePassword(password);

        var plain = JsonSerializer.SerializeToUtf8Bytes(document, _jsonOptions);
        var metadata = NotebookEncryptionMetadata.FromDocument(document);
        var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, _jsonOptions);
        if (metadataBytes.Length > MaximumMetadataLength)
            throw new InvalidDataException("加密笔记本元数据过大。");

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagLength];
        var authenticatedHeaderLength = MagicV2.Length + MetadataLengthField + metadataBytes.Length;
        var envelope = new byte[authenticatedHeaderLength + SaltLength + NonceLength + TagLength + cipher.Length];

        try
        {
            MagicV2.CopyTo(envelope, 0);
            BinaryPrimitives.WriteInt32LittleEndian(envelope.AsSpan(MagicV2.Length, MetadataLengthField), metadataBytes.Length);
            metadataBytes.CopyTo(envelope, MagicV2.Length + MetadataLengthField);
            salt.CopyTo(envelope, authenticatedHeaderLength);
            nonce.CopyTo(envelope, authenticatedHeaderLength + SaltLength);

            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, plain, cipher, tag, envelope.AsSpan(0, authenticatedHeaderLength));
            tag.CopyTo(envelope, authenticatedHeaderLength + SaltLength + NonceLength);
            cipher.CopyTo(envelope, authenticatedHeaderLength + SaltLength + NonceLength + TagLength);
            return envelope;
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
        if (envelope.StartsWith(MagicV2)) return DecryptV2(envelope, password);
        if (envelope.StartsWith(MagicV1)) return DecryptV1(envelope, password);
        throw new InvalidDataException("不是有效的 PaperNote 加密数据。");
    }

    public bool TryReadMetadata(ReadOnlySpan<byte> envelope, out NotebookEncryptionMetadata metadata)
    {
        metadata = new NotebookEncryptionMetadata();
        try
        {
            if (!TryGetV2Layout(envelope, out var metadataLength, out _, out _)) return false;
            var metadataOffset = MagicV2.Length + MetadataLengthField;
            metadata = JsonSerializer.Deserialize<NotebookEncryptionMetadata>(envelope.Slice(metadataOffset, metadataLength), _jsonOptions)
                ?? new NotebookEncryptionMetadata();
            metadata.Title = string.IsNullOrWhiteSpace(metadata.Title) ? "加密笔记本" : metadata.Title.Trim();
            metadata.FolderName ??= string.Empty;
            metadata.CoverStyle ??= NotebookDefaults.CoverStyle;
            metadata.PageCount = Math.Clamp(metadata.PageCount, 1, 100_000);
            return metadata.Id != Guid.Empty;
        }
        catch
        {
            metadata = new NotebookEncryptionMetadata();
            return false;
        }
    }

    public static bool IsEncrypted(ReadOnlySpan<byte> content)
        => content.StartsWith(MagicV1) || content.StartsWith(MagicV2);

    private NotebookDocument DecryptV2(ReadOnlySpan<byte> envelope, string password)
    {
        if (!TryGetV2Layout(envelope, out _, out var authenticatedHeaderLength, out var cipherOffset))
            throw new InvalidDataException("加密笔记本头部无效或内容不完整。");

        var salt = envelope.Slice(authenticatedHeaderLength, SaltLength);
        var nonce = envelope.Slice(authenticatedHeaderLength + SaltLength, NonceLength);
        var tag = envelope.Slice(authenticatedHeaderLength + SaltLength + NonceLength, TagLength);
        var cipher = envelope[cipherOffset..];
        return DecryptPayload(cipher, salt, nonce, tag, envelope[..authenticatedHeaderLength], password);
    }

    private NotebookDocument DecryptV1(ReadOnlySpan<byte> envelope, string password)
    {
        var headerLength = MagicV1.Length + SaltLength + NonceLength + TagLength;
        if (envelope.Length <= headerLength)
            throw new InvalidDataException("加密笔记本内容不完整。");

        var salt = envelope.Slice(MagicV1.Length, SaltLength);
        var nonce = envelope.Slice(MagicV1.Length + SaltLength, NonceLength);
        var tag = envelope.Slice(MagicV1.Length + SaltLength + NonceLength, TagLength);
        var cipher = envelope[headerLength..];
        return DecryptPayload(cipher, salt, nonce, tag, MagicV1, password);
    }

    private NotebookDocument DecryptPayload(
        ReadOnlySpan<byte> cipher,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> tag,
        ReadOnlySpan<byte> associatedData,
        string password)
    {
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, cipher, tag, plain, associatedData);
            return JsonSerializer.Deserialize<NotebookDocument>(plain, _jsonOptions)
                ?? throw new InvalidDataException("解密后的笔记本内容为空。");
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

    private static bool TryGetV2Layout(
        ReadOnlySpan<byte> envelope,
        out int metadataLength,
        out int authenticatedHeaderLength,
        out int cipherOffset)
    {
        metadataLength = 0;
        authenticatedHeaderLength = 0;
        cipherOffset = 0;
        var fixedHeaderLength = MagicV2.Length + MetadataLengthField;
        if (envelope.Length <= fixedHeaderLength + SaltLength + NonceLength + TagLength || !envelope.StartsWith(MagicV2))
            return false;

        metadataLength = BinaryPrimitives.ReadInt32LittleEndian(envelope.Slice(MagicV2.Length, MetadataLengthField));
        if (metadataLength <= 0 || metadataLength > MaximumMetadataLength) return false;
        authenticatedHeaderLength = fixedHeaderLength + metadataLength;
        cipherOffset = authenticatedHeaderLength + SaltLength + NonceLength + TagLength;
        return cipherOffset < envelope.Length;
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ArgumentException("密码至少需要 8 个字符。", nameof(password));
    }
}

public sealed class NotebookEncryptionMetadata
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "加密笔记本";
    public DateTimeOffset ModifiedAt { get; set; }
    public DateTimeOffset? LastOpenedAt { get; set; }
    public string FolderName { get; set; } = string.Empty;
    public string CoverStyle { get; set; } = NotebookDefaults.CoverStyle;
    public bool IsPinned { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsInTrash { get; set; }
    public DateTimeOffset? TrashedAt { get; set; }
    public int PageCount { get; set; } = 1;

    public static NotebookEncryptionMetadata FromDocument(NotebookDocument document) => new()
    {
        Id = document.Id,
        Title = document.Title,
        ModifiedAt = document.ModifiedAt,
        LastOpenedAt = document.LastOpenedAt,
        FolderName = document.FolderName,
        CoverStyle = document.CoverStyle,
        IsPinned = document.IsPinned,
        IsFavorite = document.IsFavorite,
        IsInTrash = document.IsInTrash,
        TrashedAt = document.TrashedAt,
        PageCount = Math.Max(1, document.Pages.Count)
    };

    public NotebookDocument CreateLockedPlaceholder()
    {
        var document = NotebookDocument.Create(Title);
        document.Id = Id == Guid.Empty ? Guid.NewGuid() : Id;
        document.ModifiedAt = ModifiedAt == default ? DateTimeOffset.MinValue : ModifiedAt;
        document.LastOpenedAt = LastOpenedAt;
        document.FolderName = FolderName;
        document.CoverStyle = CoverStyle;
        document.IsPinned = IsPinned;
        document.IsFavorite = IsFavorite;
        document.IsInTrash = IsInTrash;
        document.TrashedAt = TrashedAt;
        return document;
    }
}

public sealed class NotebookPasswordRequiredException : UnauthorizedAccessException
{
    public NotebookPasswordRequiredException(string? filePath = null)
        : base("这个笔记本已启用本地密码保护，请输入密码后打开。")
    {
        FilePath = filePath;
    }

    public string? FilePath { get; }
}
