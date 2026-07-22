using System.Globalization;

namespace PaperNote.Core.Services;

public static class PdfPageRangeService
{
    public const int MaximumImportPageCount = 500;

    public static IReadOnlyList<int> Parse(string? selection, int pageCount, int maximumCount = MaximumImportPageCount)
    {
        if (pageCount <= 0) throw new ArgumentOutOfRangeException(nameof(pageCount));
        if (maximumCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));

        var normalized = (selection ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Equals("全部", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (pageCount > maximumCount)
                throw new InvalidDataException($"一次最多导入 {maximumCount} 页，请输入页码范围。");
            return Enumerable.Range(1, pageCount).ToArray();
        }

        normalized = normalized
            .Replace('，', ',')
            .Replace('；', ',')
            .Replace(';', ',')
            .Replace('—', '-')
            .Replace('–', '-');

        var pages = new SortedSet<int>();
        foreach (var rawPart in normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart.Replace(" ", string.Empty);
            var dashIndex = part.IndexOf('-');
            if (dashIndex < 0)
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var pageNumber))
                    throw new InvalidDataException($"无法识别页码“{rawPart}”。");
                AddPage(pageNumber);
                continue;
            }

            if (part.IndexOf('-', dashIndex + 1) >= 0 ||
                !int.TryParse(part[..dashIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
                !int.TryParse(part[(dashIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var end) ||
                start > end)
            {
                throw new InvalidDataException($"无法识别页码范围“{rawPart}”。");
            }

            for (var pageNumber = start; pageNumber <= end; pageNumber++) AddPage(pageNumber);
        }

        if (pages.Count == 0) throw new InvalidDataException("请至少选择一页 PDF。");
        return pages.ToArray();

        void AddPage(int pageNumber)
        {
            if (pageNumber < 1 || pageNumber > pageCount)
                throw new InvalidDataException($"页码 {pageNumber} 超出范围，当前 PDF 共 {pageCount} 页。");
            pages.Add(pageNumber);
            if (pages.Count > maximumCount)
                throw new InvalidDataException($"一次最多导入 {maximumCount} 页。");
        }
    }

    public static string DefaultSelection(int pageCount, int maximumCount = MaximumImportPageCount)
        => pageCount <= maximumCount ? $"1-{pageCount}" : $"1-{maximumCount}";
}
