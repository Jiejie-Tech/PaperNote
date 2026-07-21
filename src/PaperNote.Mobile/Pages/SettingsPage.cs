namespace PaperNote.Mobile.Pages;

public sealed class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        Title = "设置";
        BackgroundColor = UiTheme.Background;
        var finger = new Switch { IsToggled = Preferences.Default.Get("FingerDrawing", true) };
        finger.Toggled += (_, e) => Preferences.Default.Set("FingerDrawing", e.Value);
        var autosave = new Label { Text = "墨迹停止后约 0.7 秒自动保存", TextColor = UiTheme.Muted, FontSize = 13 };
        var fingerRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        fingerRow.Add(new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = "允许手指书写", FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = UiTheme.Text },
                new Label { Text = "默认开启；关闭时手指用于平移，触控笔仍可书写。编辑器工具栏也可快速切换。", FontSize = 13, TextColor = UiTheme.Muted }
            }
        });
        fingerRow.Add(finger, 1);
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 14,
                Children =
                {
                    new Label { Text = "设置", FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = UiTheme.Text },
                    UiTheme.Card(fingerRow),
                    UiTheme.Card(new VerticalStackLayout
                    {
                        Spacing = 6,
                        Children =
                        {
                            new Label { Text = "本地优先", FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = UiTheme.Text },
                            new Label { Text = "笔记保存在本机应用数据目录，不依赖账号和服务器。请定期使用“备份与恢复”导出资料库。", FontSize = 13, TextColor = UiTheme.Muted },
                            autosave
                        }
                    }),
                    UiTheme.Card(new VerticalStackLayout
                    {
                        Spacing = 5,
                        Children =
                        {
                            new Label { Text = "PaperNote Android", FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = UiTheme.Text },
                            new Label { Text = "版本 1.0.0 · 跨平台 PaperInk 格式", FontSize = 13, TextColor = UiTheme.Muted }
                        }
                    })
                }
            }
        };
    }
}
