using PaperNote.Core.Ink;

namespace PaperNote.Core.Services;

/// <summary>
/// Lightweight grid index used to avoid scanning every stroke while rendering or erasing large pages.
/// The index returns conservative candidates in the document's original drawing order.
/// </summary>
public sealed class InkSpatialIndex
{
    private const int MaximumCellsPerStroke = 4096;
    private readonly double _cellSize;
    private readonly Dictionary<CellKey, HashSet<PaperInkStroke>> _cells = [];
    private readonly Dictionary<PaperInkStroke, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PaperInkStroke, int> _order = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<PaperInkStroke> _globalStrokes = new(ReferenceEqualityComparer.Instance);

    public InkSpatialIndex(double cellSize = 128)
    {
        if (!double.IsFinite(cellSize) || cellSize < 16) throw new ArgumentOutOfRangeException(nameof(cellSize));
        _cellSize = cellSize;
    }

    public int Count => _entries.Count;

    public void Rebuild(PaperInkDocument? document)
    {
        _cells.Clear();
        _entries.Clear();
        _order.Clear();
        _globalStrokes.Clear();
        if (document?.Strokes is not { Count: > 0 }) return;

        for (var index = 0; index < document.Strokes.Count; index++)
        {
            var stroke = document.Strokes[index];
            _order[stroke] = index;
            AddEntry(stroke);
        }
    }

    /// <summary>Updates only changed stroke bounds while refreshing inexpensive drawing-order metadata.</summary>
    public void Update(
        PaperInkDocument document,
        IEnumerable<PaperInkStroke>? removedStrokes,
        IEnumerable<PaperInkStroke>? addedStrokes)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (removedStrokes is not null)
            foreach (var stroke in removedStrokes) RemoveEntry(stroke);
        if (addedStrokes is not null)
            foreach (var stroke in addedStrokes) AddEntry(stroke);

        _order.Clear();
        for (var index = 0; index < document.Strokes.Count; index++) _order[document.Strokes[index]] = index;
    }

    public IReadOnlyList<PaperInkStroke> Query(double x, double y, double width, double height)
    {
        if (!TryNormalizeRectangle(x, y, width, height, out var bounds) || _entries.Count == 0) return [];
        var candidates = CollectCandidates(bounds);
        return candidates
            .Where(stroke => _entries.TryGetValue(stroke, out var entry) && entry.Bounds.Intersects(bounds))
            .OrderBy(stroke => _order.GetValueOrDefault(stroke, int.MaxValue))
            .ToArray();
    }

    public IReadOnlyList<PaperInkStroke> QueryCircle(double x, double y, double radius)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(radius) || radius <= 0) return [];
        var bounds = new InkBounds(x - radius, y - radius, x + radius, y + radius);
        var radiusSquared = radius * radius;
        var candidates = CollectCandidates(bounds);
        return candidates
            .Where(stroke => _entries.TryGetValue(stroke, out var entry) && entry.Bounds.DistanceSquaredTo(x, y) <= radiusSquared)
            .OrderBy(stroke => _order.GetValueOrDefault(stroke, int.MaxValue))
            .ToArray();
    }

    private HashSet<PaperInkStroke> CollectCandidates(InkBounds bounds)
    {
        var result = new HashSet<PaperInkStroke>(_globalStrokes, ReferenceEqualityComparer.Instance);
        if (!TryGetCellRange(bounds, out var minX, out var minY, out var maxX, out var maxY)) return result;
        for (var cellY = minY; cellY <= maxY; cellY++)
        for (var cellX = minX; cellX <= maxX; cellX++)
            if (_cells.TryGetValue(new CellKey(cellX, cellY), out var strokes)) result.UnionWith(strokes);
        return result;
    }

    private void AddEntry(PaperInkStroke stroke)
    {
        if (_entries.ContainsKey(stroke) || !TryGetStrokeBounds(stroke, out var bounds)) return;
        if (!TryGetCellRange(bounds, out var minX, out var minY, out var maxX, out var maxY)) return;
        var width = (long)maxX - minX + 1;
        var height = (long)maxY - minY + 1;
        if (width <= 0 || height <= 0 || width > MaximumCellsPerStroke || height > MaximumCellsPerStroke || width * height > MaximumCellsPerStroke)
        {
            _entries[stroke] = new Entry(bounds, []);
            _globalStrokes.Add(stroke);
            return;
        }

        var occupiedCells = new List<CellKey>((int)(width * height));
        for (var cellY = minY; cellY <= maxY; cellY++)
        for (var cellX = minX; cellX <= maxX; cellX++)
        {
            var key = new CellKey(cellX, cellY);
            if (!_cells.TryGetValue(key, out var strokes)) _cells[key] = strokes = new HashSet<PaperInkStroke>(ReferenceEqualityComparer.Instance);
            strokes.Add(stroke);
            occupiedCells.Add(key);
        }
        _entries[stroke] = new Entry(bounds, occupiedCells);
    }

    private void RemoveEntry(PaperInkStroke stroke)
    {
        if (!_entries.Remove(stroke, out var entry)) return;
        _globalStrokes.Remove(stroke);
        _order.Remove(stroke);
        foreach (var key in entry.Cells)
        {
            if (!_cells.TryGetValue(key, out var strokes)) continue;
            strokes.Remove(stroke);
            if (strokes.Count == 0) _cells.Remove(key);
        }
    }

    private bool TryGetCellRange(InkBounds bounds, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = minY = maxX = maxY = 0;
        return TryFloorToInt(bounds.Left / _cellSize, out minX) &&
               TryFloorToInt(bounds.Top / _cellSize, out minY) &&
               TryFloorToInt(bounds.Right / _cellSize, out maxX) &&
               TryFloorToInt(bounds.Bottom / _cellSize, out maxY);
    }

    private static bool TryGetStrokeBounds(PaperInkStroke stroke, out InkBounds bounds)
    {
        bounds = default;
        if (stroke.Points.Count == 0) return false;
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        var hasPoint = false;
        foreach (var point in stroke.Points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) continue;
            hasPoint = true;
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }
        if (!hasPoint) return false;
        var padding = double.IsFinite(stroke.Width) ? Math.Clamp(Math.Abs(stroke.Width) / 2, 0, 1024) : 0;
        bounds = new InkBounds(minX - padding, minY - padding, maxX + padding, maxY + padding);
        return true;
    }

    private static bool TryNormalizeRectangle(double x, double y, double width, double height, out InkBounds bounds)
    {
        bounds = default;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height)) return false;
        var x2 = x + width;
        var y2 = y + height;
        if (!double.IsFinite(x2) || !double.IsFinite(y2)) return false;
        bounds = new InkBounds(Math.Min(x, x2), Math.Min(y, y2), Math.Max(x, x2), Math.Max(y, y2));
        return true;
    }

    private static bool TryFloorToInt(double value, out int result)
    {
        result = 0;
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue) return false;
        result = (int)Math.Floor(value);
        return true;
    }

    private readonly record struct CellKey(int X, int Y);
    private sealed record Entry(InkBounds Bounds, IReadOnlyList<CellKey> Cells);

    private readonly record struct InkBounds(double Left, double Top, double Right, double Bottom)
    {
        public bool Intersects(InkBounds other)
            => Left <= other.Right && Right >= other.Left && Top <= other.Bottom && Bottom >= other.Top;

        public double DistanceSquaredTo(double x, double y)
        {
            var dx = x < Left ? Left - x : x > Right ? x - Right : 0;
            var dy = y < Top ? Top - y : y > Bottom ? y - Bottom : 0;
            return dx * dx + dy * dy;
        }
    }
}
