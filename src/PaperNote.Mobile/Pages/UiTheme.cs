namespace PaperNote.Mobile.Pages;

internal static class UiTheme
{
    public static readonly Color Accent = Color.FromArgb("#3157D5");
    public static readonly Color AccentSoft = Color.FromArgb("#E8EDFF");
    public static readonly Color Surface = Color.FromArgb("#FFFFFF");
    public static readonly Color Background = Color.FromArgb("#F4F6FB");
    public static readonly Color Text = Color.FromArgb("#172033");
    public static readonly Color Muted = Color.FromArgb("#687187");
    public static readonly Color Border = Color.FromArgb("#DCE1EC");

    public static Button Button(string text, EventHandler? clicked = null, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 44,
            Padding = new Thickness(16, 8),
            CornerRadius = 13,
            BackgroundColor = primary ? Accent : Surface,
            TextColor = primary ? Colors.White : Text,
            BorderColor = primary ? Accent : Border,
            BorderWidth = 1
        };
        if (clicked is not null) button.Clicked += clicked;
        return button;
    }

    public static Border Card(View content) => new()
    {
        Content = content,
        BackgroundColor = Surface,
        Stroke = Border,
        StrokeThickness = 1,
        Padding = 16,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 }
    };
}
