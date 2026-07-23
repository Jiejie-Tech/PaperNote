using System.IO;
using System.Text.Json;
using PaperNote.Core.Ink;
using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public sealed class SelectionMaterial
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "个人素材";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string Category { get; set; } = "未分类";
    public List<string> Keywords { get; set; } = [];
    public bool IsFavorite { get; set; }
    public string ThumbnailData { get; set; } = string.Empty;
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 1;
    public List<PaperInkStroke> Strokes { get; set; } = [];
    public List<PageObject> Objects { get; set; } = [];
}

public sealed record SelectionMaterialPlacement(
    IReadOnlyList<PaperInkStroke> Strokes,
    IReadOnlyList<PageObject> Objects);

public sealed class SelectionMaterialLibraryService
{
    public const int MaximumMaterials = 48;
    public const int MaximumStrokesPerMaterial = 512;
    public const int MaximumObjectsPerMaterial = 256;
    public const int MaximumPointsPerMaterial = 100_000;

    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SelectionMaterialLibraryService(string? filePath = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _filePath = Path.GetFullPath(filePath ?? Path.Combine(local, "PaperNote", "selection-materials.json"));
    }

    public async Task<IReadOnlyList<SelectionMaterial>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath)) return [];
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
            var materials = JsonSerializer.Deserialize<List<SelectionMaterial>>(json, _jsonOptions) ?? [];
            return Normalize(materials);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public async Task SaveAsync(IEnumerable<SelectionMaterial> materials, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(materials).ToArray();
        var directory = Path.GetDirectoryName(_filePath) ?? throw new InvalidOperationException("无法确定个人素材目录。");
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

    public static SelectionMaterial? Create(
        string? name,
        NotebookPage page,
        IEnumerable<Guid> strokeIds,
        IEnumerable<Guid> objectIds)
    {
        var snapshot = SelectionExportService.Create(page, strokeIds, objectIds, padding: 0);
        if (snapshot is null || !snapshot.HasContent) return null;

        var strokes = snapshot.Page.Ink.Strokes.Take(MaximumStrokesPerMaterial).Select(stroke =>
        {
            var clone = stroke.Clone();
            clone.Id = Guid.NewGuid();
            clone.LayerId = null;
            foreach (var point in clone.Points)
            {
                point.X -= snapshot.X;
                point.Y -= snapshot.Y;
            }
            return clone;
        }).ToList();
        TrimPoints(strokes);

        var objects = snapshot.Page.Objects.Take(MaximumObjectsPerMaterial).Select(item =>
        {
            var clone = item.Clone();
            clone.X -= snapshot.X;
            clone.Y -= snapshot.Y;
            clone.LayerId = null;
            clone.LinkTargetPageId = null;
            clone.IsLocked = false;
            return clone;
        }).ToList();

        return new SelectionMaterial
        {
            Name = NormalizeName(name),
            Width = Math.Max(1, snapshot.Width),
            Height = Math.Max(1, snapshot.Height),
            Strokes = strokes,
            Objects = objects
        };
    }

    public static SelectionMaterialPlacement Instantiate(
        SelectionMaterial material,
        double x = 170,
        double y = 260,
        double maximumWidth = 500,
        double maximumHeight = 520,
        Guid? layerId = null)
    {
        ArgumentNullException.ThrowIfNull(material);
        var width = IsPositiveFinite(material.Width) ? material.Width : 1;
        var height = IsPositiveFinite(material.Height) ? material.Height : 1;
        maximumWidth = IsPositiveFinite(maximumWidth) ? maximumWidth : 500;
        maximumHeight = IsPositiveFinite(maximumHeight) ? maximumHeight : 520;
        var scale = Math.Min(1, Math.Min(maximumWidth / width, maximumHeight / height));
        if (!IsPositiveFinite(scale)) scale = 1;

        var strokes = material.Strokes.Take(MaximumStrokesPerMaterial).Select(stroke =>
        {
            var clone = stroke.Clone();
            clone.Id = Guid.NewGuid();
            clone.LayerId = layerId;
            clone.Width = Math.Max(.5, clone.Width * scale);
            foreach (var point in clone.Points)
            {
                point.X = x + point.X * scale;
                point.Y = y + point.Y * scale;
            }
            return clone;
        }).ToList();
        TrimPoints(strokes);

        var groupMap = new Dictionary<Guid, Guid>();
        var objects = material.Objects.Take(MaximumObjectsPerMaterial).Select(item =>
        {
            var clone = item.Clone();
            clone.X = x + clone.X * scale;
            clone.Y = y + clone.Y * scale;
            clone.Width = Math.Max(1, clone.Width * scale);
            clone.Height = Math.Max(1, clone.Height * scale);
            clone.StrokeThickness = Math.Max(.5, clone.StrokeThickness * scale);
            clone.FontSize = Math.Max(8, clone.FontSize * scale);
            clone.LayerId = layerId;
            clone.LinkTargetPageId = null;
            clone.IsLocked = false;
            if (clone.GroupId is Guid oldGroup)
            {
                if (!groupMap.TryGetValue(oldGroup, out var newGroup))
                {
                    newGroup = Guid.NewGuid();
                    groupMap[oldGroup] = newGroup;
                }
                clone.GroupId = newGroup;
            }
            return clone;
        }).ToList();

        return new SelectionMaterialPlacement(strokes, objects);
    }

    public static IReadOnlyList<SelectionMaterial> Normalize(IEnumerable<SelectionMaterial>? materials)
    {
        var result = new List<SelectionMaterial>();
        foreach (var material in materials ?? [])
        {
            material.Id = material.Id == Guid.Empty || result.Any(item => item.Id == material.Id) ? Guid.NewGuid() : material.Id;
            material.Name = NormalizeName(material.Name);
            material.Category = string.IsNullOrWhiteSpace(material.Category) ? "未分类" : material.Category.Trim()[..Math.Min(material.Category.Trim().Length, 40)];
            material.Keywords = (material.Keywords ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()[..Math.Min(value.Trim().Length, 30)]).Distinct(StringComparer.CurrentCultureIgnoreCase).Take(20).ToList();
            if (material.UpdatedAt < material.CreatedAt) material.UpdatedAt = material.CreatedAt;
            material.ThumbnailData ??= string.Empty;
            if (!string.IsNullOrWhiteSpace(material.ThumbnailData)) try { _ = Convert.FromBase64String(material.ThumbnailData); } catch (FormatException) { material.ThumbnailData = string.Empty; }
            material.Width = IsPositiveFinite(material.Width) ? material.Width : 1;
            material.Height = IsPositiveFinite(material.Height) ? material.Height : 1;
            material.Strokes = (material.Strokes ?? [])
                .Where(IsValidStroke)
                .Take(MaximumStrokesPerMaterial)
                .ToList();
            TrimPoints(material.Strokes);
            material.Objects = (material.Objects ?? [])
                .Where(IsValidObject)
                .Take(MaximumObjectsPerMaterial)
                .ToList();
            if (material.Strokes.Count == 0 && material.Objects.Count == 0) continue;
            result.Add(material);
            if (result.Count >= MaximumMaterials) break;
        }
        return result;
    }

    private static string NormalizeName(string? name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? $"个人素材 {DateTime.Now:MM-dd HHmm}" : name.Trim();
        return value[..Math.Min(value.Length, 60)];
    }

    private static void TrimPoints(List<PaperInkStroke> strokes)
    {
        var remaining = MaximumPointsPerMaterial;
        for (var index = 0; index < strokes.Count; index++)
        {
            var stroke = strokes[index];
            if (remaining <= 0)
            {
                strokes.RemoveRange(index, strokes.Count - index);
                break;
            }
            if (stroke.Points.Count > remaining) stroke.Points = stroke.Points.Take(remaining).ToList();
            remaining -= stroke.Points.Count;
        }
        strokes.RemoveAll(stroke => stroke.Points.Count == 0);
    }

    private static bool IsValidStroke(PaperInkStroke stroke)
    {
        if (stroke is null || stroke.Points.Count == 0 || !IsPositiveFinite(stroke.Width)) return false;
        stroke.Id = stroke.Id == Guid.Empty ? Guid.NewGuid() : stroke.Id;
        stroke.LayerId = null;
        stroke.Opacity = double.IsFinite(stroke.Opacity) ? Math.Clamp(stroke.Opacity, 0, 1) : 1;
        stroke.Points = stroke.Points.Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y)).ToList();
        return stroke.Points.Count > 0;
    }

    private static bool IsValidObject(PageObject item)
    {
        if (item is null || !double.IsFinite(item.X) || !double.IsFinite(item.Y) || !IsPositiveFinite(item.Width) || !IsPositiveFinite(item.Height)) return false;
        item.Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id;
        item.LayerId = null;
        item.LinkTargetPageId = null;
        item.IsLocked = false;
        item.Opacity = double.IsFinite(item.Opacity) ? Math.Clamp(item.Opacity, 0, 1) : 1;
        return true;
    }

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;
}
