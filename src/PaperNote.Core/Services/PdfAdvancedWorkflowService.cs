using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public sealed record PdfFormValidationResult(bool IsValid, IReadOnlyList<string> MissingRequiredFields);

/// <summary>Offline PDF overlay helpers for fillable fields, signatures and calibrated measurements.</summary>
public static class PdfAdvancedWorkflowService
{
    public static IReadOnlyList<PageObject> GetFormFields(NotebookPage page) =>
        page.Objects.Where(item => !string.IsNullOrWhiteSpace(item.FormFieldKind)).ToArray();

    public static PageObject AddTextField(NotebookPage page, string name, double x, double y, double width = 260, double height = 64, bool required = false)
    {
        ArgumentNullException.ThrowIfNull(page);
        var field = CreateField("Text", name, x, y, width, height, required);
        field.Kind = "Text";
        field.Text = string.IsNullOrWhiteSpace(name) ? "填写内容" : $"{name}：";
        field.FillColor = "#E6FFFFFF";
        page.Objects.Add(field);
        page.ModifiedAt = DateTimeOffset.Now;
        return field;
    }

    public static PageObject AddCheckbox(NotebookPage page, string name, double x, double y, bool required = false)
    {
        ArgumentNullException.ThrowIfNull(page);
        var field = CreateField("Checkbox", name, x, y, 54, 54, required);
        field.Kind = "Text";
        field.Text = "☐";
        field.FontSize = 34;
        field.AltText = $"复选框 {field.FormFieldName}";
        page.Objects.Add(field);
        page.ModifiedAt = DateTimeOffset.Now;
        return field;
    }

    public static PageObject AddSignature(NotebookPage page, string name, string imageData, double x, double y, double width = 260, double height = 100)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (string.IsNullOrWhiteSpace(imageData)) throw new ArgumentException("签名图片不能为空。", nameof(imageData));
        try { _ = Convert.FromBase64String(imageData); }
        catch (FormatException) { throw new ArgumentException("签名图片数据无效。", nameof(imageData)); }
        var field = CreateField("Signature", name, x, y, width, height, false);
        field.Kind = "Image";
        field.ImageData = imageData;
        field.FormFieldValue = "signed";
        field.AltText = $"本地签名 {field.FormFieldName}";
        field.IsLocked = true;
        page.Objects.Add(field);
        page.ModifiedAt = DateTimeOffset.Now;
        return field;
    }

    public static bool SetFieldValue(PageObject field, string? value, bool? isChecked = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (string.IsNullOrWhiteSpace(field.FormFieldKind)) return false;
        if (field.FormFieldKind == "Checkbox")
        {
            field.FormFieldChecked = isChecked ?? !field.FormFieldChecked;
            field.FormFieldValue = field.FormFieldChecked ? "true" : "false";
            field.Text = field.FormFieldChecked ? "☑" : "☐";
        }
        else
        {
            field.FormFieldValue = (value ?? string.Empty).Trim();
            if (field.FormFieldKind == "Text") field.Text = string.IsNullOrWhiteSpace(field.FormFieldName)
                ? field.FormFieldValue
                : $"{field.FormFieldName}：{field.FormFieldValue}";
        }
        return true;
    }

    public static PdfFormValidationResult Validate(NotebookPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var missing = GetFormFields(page)
            .Where(item => item.FormFieldRequired && (item.FormFieldKind == "Checkbox" ? !item.FormFieldChecked : string.IsNullOrWhiteSpace(item.FormFieldValue)))
            .Select(item => string.IsNullOrWhiteSpace(item.FormFieldName) ? "未命名字段" : item.FormFieldName)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return new PdfFormValidationResult(missing.Length == 0, missing);
    }

    public static double CalibrateMeasurement(NotebookPage page, double pixelDistance, double knownDistanceMillimeters)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!double.IsFinite(pixelDistance) || pixelDistance <= 0) throw new ArgumentOutOfRangeException(nameof(pixelDistance));
        if (!double.IsFinite(knownDistanceMillimeters) || knownDistanceMillimeters <= 0) throw new ArgumentOutOfRangeException(nameof(knownDistanceMillimeters));
        page.PdfPixelsPerMillimeter = Math.Clamp(pixelDistance / knownDistanceMillimeters, 0.01, 10_000);
        page.ModifiedAt = DateTimeOffset.Now;
        return page.PdfPixelsPerMillimeter;
    }

    public static GeometryMeasurement? MeasureSelection(NotebookPage page, IEnumerable<Guid> strokeIds, IEnumerable<Guid> objectIds)
    {
        var raw = GeometryMeasurementService.MeasureSelection(page, strokeIds, objectIds);
        if (raw is null) return null;
        var scale = GeometryMeasurementService.PixelsPerMillimeter / Math.Clamp(page.PdfPixelsPerMillimeter, 0.01, 10_000);
        return raw with
        {
            WidthMillimeters = raw.WidthMillimeters * scale,
            HeightMillimeters = raw.HeightMillimeters * scale,
            DirectDistanceMillimeters = raw.DirectDistanceMillimeters * scale,
            PathLengthMillimeters = raw.PathLengthMillimeters * scale,
            PerimeterMillimeters = raw.PerimeterMillimeters * scale,
            AreaSquareMillimeters = raw.AreaSquareMillimeters * scale * scale
        };
    }

    private static PageObject CreateField(string kind, string name, double x, double y, double width, double height, bool required) => new()
    {
        X = Math.Clamp(x, 0, 810),
        Y = Math.Clamp(y, 0, 1158),
        Width = Math.Clamp(width, 30, 840),
        Height = Math.Clamp(height, 30, 1188),
        StrokeColor = "#3157D5",
        FillColor = "#14FFFFFF",
        StrokeThickness = 2,
        FormFieldKind = kind,
        FormFieldName = NormalizeName(name),
        FormFieldRequired = required,
        AltText = $"PDF {kind} 字段 {NormalizeName(name)}"
    };

    private static string NormalizeName(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "未命名字段" : value.Trim();
        return text[..Math.Min(text.Length, 120)];
    }
}
