namespace PaperNote.Mobile.Pages;

internal sealed class PasswordPromptPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Entry _password;
    private readonly Entry? _confirmation;
    private readonly Label _validation;
    private bool _closing;

    private PasswordPromptPage(string title, string message, bool confirmPassword)
    {
        Title = title;
        BackgroundColor = UiTheme.Background;
        Shell.SetNavBarIsVisible(this, false);

        _password = new Entry
        {
            Placeholder = "至少 8 个字符",
            IsPassword = true,
            ReturnType = confirmPassword ? ReturnType.Next : ReturnType.Done,
            AutomationId = "NotebookPasswordEntry"
        };
        _confirmation = confirmPassword
            ? new Entry
            {
                Placeholder = "再次输入密码",
                IsPassword = true,
                ReturnType = ReturnType.Done,
                AutomationId = "NotebookPasswordConfirmationEntry"
            }
            : null;
        _validation = new Label { TextColor = Color.FromArgb("#B42318"), FontSize = 13, IsVisible = false };

        var cancel = UiTheme.Button("取消", async (_, _) => await FinishAsync(null));
        cancel.AutomationId = "NotebookPasswordCancelButton";
        var confirm = UiTheme.Button("确定", async (_, _) => await SubmitAsync(), primary: true);
        confirm.AutomationId = "NotebookPasswordConfirmButton";
        var buttons = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }
        };
        buttons.Add(cancel);
        buttons.Add(confirm, 1);

        var form = new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                new Label { Text = title, FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = UiTheme.Text },
                new Label { Text = message, FontSize = 14, TextColor = UiTheme.Muted },
                _password
            }
        };
        if (_confirmation is not null) form.Add(_confirmation);
        form.Add(_validation);
        form.Add(buttons);

        Content = new Grid
        {
            Padding = new Thickness(24),
            Children =
            {
                new Border
                {
                    Content = form,
                    Padding = 22,
                    MaximumWidthRequest = 420,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    BackgroundColor = UiTheme.Surface,
                    Stroke = UiTheme.Border,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 }
                }
            }
        };

        _password.Completed += async (_, _) =>
        {
            if (_confirmation is not null) _confirmation.Focus();
            else await SubmitAsync();
        };
        if (_confirmation is not null) _confirmation.Completed += async (_, _) => await SubmitAsync();
    }

    public static async Task<string?> ShowAsync(INavigation navigation, string title, string message, bool confirmPassword)
    {
        var page = new PasswordPromptPage(title, message, confirmPassword);
        await navigation.PushModalAsync(page);
        return await page._completion.Task;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () => _password.Focus());
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_closing) _completion.TrySetResult(null);
    }

    private async Task SubmitAsync()
    {
        var password = _password.Text ?? string.Empty;
        if (password.Length < 8)
        {
            ShowValidation("密码至少需要 8 个字符。");
            return;
        }
        if (_confirmation is not null && !string.Equals(password, _confirmation.Text, StringComparison.Ordinal))
        {
            ShowValidation("两次输入的密码不一致。");
            _confirmation.Focus();
            return;
        }
        await FinishAsync(password);
    }

    private void ShowValidation(string message)
    {
        _validation.Text = message;
        _validation.IsVisible = true;
    }

    private async Task FinishAsync(string? password)
    {
        if (_closing) return;
        _closing = true;
        _completion.TrySetResult(password);
        await Navigation.PopModalAsync();
    }
}
