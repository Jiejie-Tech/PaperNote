using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PaperNote.Core.Services;

public sealed class PaperNoteResourcePackManifest
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "PaperNote 本地资源包";
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public int MaterialCount { get; set; }
    public int TemplateCount { get; set; }
    public string PayloadSha256 { get; set; } = string.Empty;
}

public sealed record ResourcePackImportResult(
    PaperNoteResourcePackManifest Manifest,
    IReadOnlyList<SelectionMaterial> Materials,
    IReadOnlyList<SharedPaperTemplate> Templates,
    int SkippedDuplicates);

public static class LocalResourcePackService
{
    private const int MaximumPayloadBytes = 100 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static async Task ExportAsync(
        string path,
        string name,
        IEnumerable<SelectionMaterial>? materials,
        IEnumerable<SharedPaperTemplate>? templates,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var materialList = SelectionMaterialLibraryService.Normalize(materials).ToList();
        var templateList = PaperTemplateLibraryService.Normalize(templates).ToList();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new ResourcePackPayload { Materials = materialList, Templates = templateList }, Json);
        if (payload.Length > MaximumPayloadBytes) throw new InvalidDataException("资源包内容超过 100 MB 限制。");
        var manifest = new PaperNoteResourcePackManifest
        {
            Name = Limit(name, 80, "PaperNote 本地资源包"),
            Description = Limit(description, 500, string.Empty),
            MaterialCount = materialList.Count,
            TemplateCount = templateList.Count,
            PayloadSha256 = Convert.ToHexString(SHA256.HashData(payload))
        };
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("无法确定资源包目录。"));
        var temporary = fullPath + ".tmp";
        try
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            await using (var file = File.Create(temporary))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                await WriteEntryAsync(archive, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, Json), cancellationToken);
                await WriteEntryAsync(archive, "resources.json", payload, cancellationToken);
            }
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task<ResourcePackImportResult> ImportAsync(
        string path,
        IEnumerable<SelectionMaterial>? existingMaterials = null,
        IEnumerable<SharedPaperTemplate>? existingTemplates = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到资源包。", path);
        using var archive = ZipFile.OpenRead(path);
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("资源包缺少 manifest.json。");
        var payloadEntry = archive.GetEntry("resources.json") ?? throw new InvalidDataException("资源包缺少 resources.json。");
        if (manifestEntry.Length > 1_000_000 || payloadEntry.Length > MaximumPayloadBytes) throw new InvalidDataException("资源包超过安全大小限制。");
        var manifest = await ReadJsonAsync<PaperNoteResourcePackManifest>(manifestEntry, cancellationToken) ?? throw new InvalidDataException("资源包清单无效。");
        if (manifest.FormatVersion != 1) throw new NotSupportedException($"不支持资源包格式 {manifest.FormatVersion}。");
        var payloadBytes = await ReadBytesAsync(payloadEntry, cancellationToken);
        var actualHash = Convert.ToHexString(SHA256.HashData(payloadBytes));
        if (!string.Equals(actualHash, manifest.PayloadSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("资源包校验失败，文件可能已损坏。 ");
        ResourcePackPayload payload;
        try { payload = JsonSerializer.Deserialize<ResourcePackPayload>(payloadBytes, Json) ?? new ResourcePackPayload(); }
        catch (JsonException exception) { throw new InvalidDataException("资源包内容不是有效的 JSON。", exception); }
        var materials = SelectionMaterialLibraryService.Normalize(payload.Materials).ToList();
        var templates = PaperTemplateLibraryService.Normalize(payload.Templates).ToList();
        if (manifest.MaterialCount != materials.Count || manifest.TemplateCount != templates.Count)
            throw new InvalidDataException("资源包清单数量与实际内容不一致。");
        manifest.Name = Limit(manifest.Name, 80, "PaperNote 本地资源包");
        manifest.Description = Limit(manifest.Description, 500, string.Empty);

        var materialKeys = (existingMaterials ?? []).Select(MaterialKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var templateKeys = (existingTemplates ?? []).Select(TemplateKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;
        var uniqueMaterials = new List<SelectionMaterial>();
        foreach (var item in materials)
        {
            if (materialKeys.Add(MaterialKey(item))) uniqueMaterials.Add(item);
            else skipped++;
        }
        var uniqueTemplates = new List<SharedPaperTemplate>();
        foreach (var item in templates)
        {
            if (templateKeys.Add(TemplateKey(item))) uniqueTemplates.Add(item);
            else skipped++;
        }
        materials = uniqueMaterials;
        templates = uniqueTemplates;
        foreach (var item in materials) item.Id = Guid.NewGuid();
        foreach (var item in templates) item.Id = Guid.NewGuid();
        return new ResourcePackImportResult(manifest, materials, templates, skipped);
    }

    public static IEnumerable<SelectionMaterial> SortMaterials(IEnumerable<SelectionMaterial> materials, string? query = null, string? category = null)
    {
        query = query?.Trim(); category = category?.Trim();
        return materials.Where(item => string.IsNullOrWhiteSpace(category) || string.Equals(item.Category, category, StringComparison.CurrentCultureIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(query) || item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || item.Keywords.Any(word => word.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .OrderByDescending(item => item.IsFavorite).ThenBy(item => item.Category).ThenByDescending(item => item.UpdatedAt).ThenBy(item => item.Name);
    }

    private static string MaterialKey(SelectionMaterial item) => $"{item.Name}|{item.Width:0.###}|{item.Height:0.###}|{item.Strokes.Count}|{item.Objects.Count}";
    private static string TemplateKey(SharedPaperTemplate item) => $"{item.Name}|{item.PaperTemplate}|{item.PaperColor}|{item.BackgroundImageData.Length}";
    private static string Limit(string? value, int maximum, string fallback) { var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim(); return text[..Math.Min(text.Length, maximum)]; }
    private static async Task WriteEntryAsync(ZipArchive archive, string name, byte[] data, CancellationToken token) { var entry = archive.CreateEntry(name, CompressionLevel.Optimal); await using var stream = entry.Open(); await stream.WriteAsync(data, token); }
    private static async Task<T?> ReadJsonAsync<T>(ZipArchiveEntry entry, CancellationToken token) { await using var stream = entry.Open(); return await JsonSerializer.DeserializeAsync<T>(stream, Json, token); }
    private static async Task<byte[]> ReadBytesAsync(ZipArchiveEntry entry, CancellationToken token) { await using var stream = entry.Open(); using var memory = new MemoryStream(); await stream.CopyToAsync(memory, token); return memory.ToArray(); }

    private sealed class ResourcePackPayload
    {
        public List<SelectionMaterial> Materials { get; set; } = [];
        public List<SharedPaperTemplate> Templates { get; set; } = [];
    }
}
