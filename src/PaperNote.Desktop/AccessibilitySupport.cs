using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PaperNote.Desktop;

internal static partial class AccessibilitySupport
{
    private static readonly Dictionary<string, string> FriendlyTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Notebook"] = "笔记本", ["Library"] = "资料库", ["Editor"] = "编辑器", ["Page"] = "页面",
        ["Title"] = "标题", ["Search"] = "搜索", ["Filter"] = "筛选", ["Folder"] = "文件夹",
        ["Button"] = "按钮", ["Box"] = "输入框", ["Combo"] = "选择框", ["List"] = "列表",
        ["Tool"] = "工具", ["Ink"] = "墨迹", ["Pen"] = "钢笔", ["Width"] = "粗细",
        ["Color"] = "颜色", ["Zoom"] = "缩放", ["Status"] = "状态", ["Save"] = "保存",
        ["Close"] = "关闭", ["Open"] = "打开", ["Current"] = "当前", ["Jump"] = "跳转",
        ["Outline"] = "大纲", ["Pdf"] = "PDF", ["Audio"] = "录音", ["Text"] = "文字"
    };

    public static void Apply(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Visit(root, new HashSet<DependencyObject>());
        if (SystemParameters.HighContrast) ApplyHighContrast(root, new HashSet<DependencyObject>());
    }

    private static void Visit(DependencyObject element, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element)) return;
        if (element is FrameworkElement framework)
        {
            var name = AutomationProperties.GetName(framework);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = InferName(framework);
                if (!string.IsNullOrWhiteSpace(name)) AutomationProperties.SetName(framework, name);
            }

            var toolTip = framework.ToolTip?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(toolTip) && string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(framework)))
                AutomationProperties.SetHelpText(framework, toolTip);

            if (framework is ButtonBase or TextBoxBase or ComboBox or ListBoxItem or Slider)
                KeyboardNavigation.SetTabNavigation(framework, KeyboardNavigationMode.Local);
        }

        foreach (var child in EnumerateChildren(element)) Visit(child, visited);
    }

    private static string InferName(FrameworkElement element)
    {
        var content = element switch
        {
            ContentControl { Content: string text } => text,
            HeaderedContentControl { Header: string header } => header,
            HeaderedItemsControl { Header: string header } => header,
            TextBlock textBlock => textBlock.Text,
            _ => string.Empty
        };
        content = NormalizeVisibleText(content);
        if (!string.IsNullOrWhiteSpace(content)) return content;
        return HumanizeName(element.Name, element.GetType().Name);
    }

    private static string NormalizeVisibleText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Replace("_", string.Empty, StringComparison.Ordinal).Replace("✓", "，已选中", StringComparison.Ordinal);
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text.Length <= 120 ? text : text[..120];
    }

    private static string HumanizeName(string? name, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(name) ? fallback : name;
        source = CamelCaseRegex().Replace(source, " $1");
        foreach (var pair in FriendlyTerms) source = Regex.Replace(source, $"\\b{Regex.Escape(pair.Key)}\\b", pair.Value, RegexOptions.IgnoreCase);
        source = WhitespaceRegex().Replace(source, " ").Trim();
        return source;
    }

    private static void ApplyHighContrast(DependencyObject element, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element)) return;
        if (element is Control control && element is not InkCanvas)
        {
            control.Foreground = SystemColors.WindowTextBrush;
            switch (control)
            {
                case ButtonBase or TextBoxBase or ComboBox or ListBoxItem or MenuItem:
                    control.Background = SystemColors.WindowBrush;
                    control.BorderBrush = SystemColors.WindowTextBrush;
                    break;
            }
        }
        else if (element is TextBlock textBlock) textBlock.Foreground = SystemColors.WindowTextBrush;
        else if (element is Border border && !IsCanvasContainer(border))
        {
            border.Background = SystemColors.WindowBrush;
            border.BorderBrush = SystemColors.WindowTextBrush;
        }

        foreach (var child in EnumerateChildren(element)) ApplyHighContrast(child, visited);
    }

    private static bool IsCanvasContainer(FrameworkElement element)
    {
        var name = element.Name ?? string.Empty;
        return name.Contains("Ink", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Paper", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Canvas", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Thumbnail", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<DependencyObject> EnumerateChildren(DependencyObject parent)
    {
        var count = 0;
        try { count = VisualTreeHelper.GetChildrenCount(parent); } catch (InvalidOperationException) { }
        for (var i = 0; i < count; i++) yield return VisualTreeHelper.GetChild(parent, i);

        if (count != 0) yield break;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) yield return child;
    }

    [GeneratedRegex("([A-Z][a-z0-9]+)")]
    private static partial Regex CamelCaseRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}
