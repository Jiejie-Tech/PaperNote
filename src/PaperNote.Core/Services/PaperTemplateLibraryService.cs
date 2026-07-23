using System.IO;
using System.Text.Json;
using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public sealed class SharedPaperTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "自定义纸张";
    public string PaperTemplate { get; set; } = PaperPageDefaults.Template;
    public string PaperColor { get; set; } = PaperPageDefaults.Color;
    public string BackgroundImageData { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string Category { get; set; } = "未分类";
    public List<string> Keywords { get; set; } = [];
    public bool IsFavorite { get; set; }
}

public sealed class PaperTemplateLibraryService
{
    public const int MaximumTemplates = 24;
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public PaperTemplateLibraryService(string? filePath = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _filePath = Path.GetFullPath(filePath ?? Path.Combine(local, "PaperNote", "paper-templates.json"));
    }

    public async Task<IReadOnlyList<SharedPaperTemplate>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath)) return [];
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
            var templates = JsonSerializer.Deserialize<List<SharedPaperTemplate>>(json, _jsonOptions) ?? [];
            return Normalize(templates);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public async Task SaveAsync(IEnumerable<SharedPaperTemplate> templates, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(templates).ToArray();
        var directory = Path.GetDirectoryName(_filePath) ?? throw new InvalidOperationException("无法确定纸张模板目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(normalized, _jsonOptions);
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static IReadOnlyList<SharedPaperTemplate> Normalize(IEnumerable<SharedPaperTemplate>? templates)
    {
        var result = new List<SharedPaperTemplate>();
        foreach (var template in templates ?? [])
        {
            template.Id = template.Id == Guid.Empty ? Guid.NewGuid() : template.Id;
            template.Name = string.IsNullOrWhiteSpace(template.Name) ? "自定义纸张" : template.Name.Trim()[..Math.Min(template.Name.Trim().Length, 60)];
            template.PaperTemplate = template.PaperTemplate is "Blank" or "Dotted" or "Lined" or "Grid" ? template.PaperTemplate : PaperPageDefaults.Template;
            template.PaperColor = string.IsNullOrWhiteSpace(template.PaperColor) ? PaperPageDefaults.Color : template.PaperColor.Trim();
            template.BackgroundImageData ??= string.Empty;
            if (!string.IsNullOrWhiteSpace(template.BackgroundImageData))
            {
                try { _ = Convert.FromBase64String(template.BackgroundImageData); }
                catch (FormatException) { template.BackgroundImageData = string.Empty; }
            }
            template.SourceName = string.IsNullOrWhiteSpace(template.SourceName) ? string.Empty : Path.GetFileName(template.SourceName.Trim());
            template.Category = string.IsNullOrWhiteSpace(template.Category) ? "未分类" : template.Category.Trim()[..Math.Min(template.Category.Trim().Length, 40)];
            template.Keywords = (template.Keywords ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()[..Math.Min(value.Trim().Length, 30)]).Distinct(StringComparer.CurrentCultureIgnoreCase).Take(20).ToList();
            if (template.UpdatedAt < template.CreatedAt) template.UpdatedAt = template.CreatedAt;
            if (result.Any(item => item.Id == template.Id)) template.Id = Guid.NewGuid();
            result.Add(template);
            if (result.Count >= MaximumTemplates) break;
        }
        return result;
    }
}
