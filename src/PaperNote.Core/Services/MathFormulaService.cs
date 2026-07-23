using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public static partial class MathFormulaService
{
    public static string ToLatex(string? recognizedText)
    {
        if (string.IsNullOrWhiteSpace(recognizedText)) return string.Empty;
        var text = recognizedText.Trim()
            .Replace('×', '*').Replace('÷', '/').Replace('−', '-')
            .Replace("≤", @"\le ", StringComparison.Ordinal)
            .Replace("≥", @"\ge ", StringComparison.Ordinal)
            .Replace("≠", @"\ne ", StringComparison.Ordinal)
            .Replace("≈", @"\approx ", StringComparison.Ordinal)
            .Replace("π", @"\pi ", StringComparison.Ordinal)
            .Replace("√", @"\sqrt", StringComparison.Ordinal);
        text = FractionRegex().Replace(text, match => $@"\frac{{{match.Groups[1].Value}}}{{{match.Groups[2].Value}}}");
        text = PowerRegex().Replace(text, match => $"{match.Groups[1].Value}^{{{match.Groups[2].Value}}}");
        text = SubscriptRegex().Replace(text, match => $"{match.Groups[1].Value}_{{{match.Groups[2].Value}}}");
        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    public static bool IsBalanced(string? latex)
    {
        var depth = 0;
        foreach (var character in latex ?? string.Empty)
        {
            if (character == '{') depth++;
            else if (character == '}' && --depth < 0) return false;
        }
        return depth == 0;
    }

    public static PageObject CreateFormulaObject(string latex, double x = 170, double y = 220, double width = 500, double height = 110)
    {
        latex = (latex ?? string.Empty).Trim();
        if (latex.Length == 0) throw new ArgumentException("公式不能为空。", nameof(latex));
        if (!IsBalanced(latex)) throw new FormatException("LaTeX 花括号不匹配。");
        return new PageObject
        {
            Kind = "Text", X = x, Y = y, Width = width, Height = height,
            Text = ToReadablePreview(latex), FormulaLatex = latex,
            AltText = $"数学公式：{latex}", FontSize = 26,
            StrokeColor = "#172033", FillColor = "#0A3157D5"
        };
    }

    public static string ExportSvg(string latex, int width = 1200, int height = 240)
    {
        latex = (latex ?? string.Empty).Trim();
        if (latex.Length == 0 || !IsBalanced(latex)) throw new FormatException("LaTeX 公式无效。");
        width = Math.Clamp(width, 200, 4000); height = Math.Clamp(height, 80, 2000);
        var escaped = SecurityElement.Escape(latex) ?? string.Empty;
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\"><rect width=\"100%\" height=\"100%\" fill=\"white\"/><text x=\"32\" y=\"{height / 2}\" dominant-baseline=\"middle\" font-family=\"Cambria Math, STIX Two Math, serif\" font-size=\"48\" fill=\"#172033\">{escaped}</text></svg>";
    }

    private static string ToReadablePreview(string latex) => latex
        .Replace(@"\frac", "分数", StringComparison.Ordinal)
        .Replace(@"\sqrt", "√", StringComparison.Ordinal)
        .Replace(@"\pi", "π", StringComparison.Ordinal)
        .Replace(@"\le", "≤", StringComparison.Ordinal)
        .Replace(@"\ge", "≥", StringComparison.Ordinal);

    [GeneratedRegex(@"(?<![\\\w])(\d+(?:\.\d+)?)\s*/\s*(\d+(?:\.\d+)?)(?!\w)")]
    private static partial Regex FractionRegex();
    [GeneratedRegex(@"([A-Za-z0-9})])\^([A-Za-z0-9+-]+)")]
    private static partial Regex PowerRegex();
    [GeneratedRegex(@"([A-Za-z])_([A-Za-z0-9]+)")]
    private static partial Regex SubscriptRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
