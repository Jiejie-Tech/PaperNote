using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private string? PromptForNotebookPassword(string title, string message, bool confirmPassword)
    {
        string? result = null;
        var dialog = new Window
        {
            Title = title,
            Owner = IsLoaded ? this : null,
            Width = 430,
            Height = confirmPassword ? 330 : 270,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = Brushes.White
        };

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(36, 43, 57)) };
        var hint = new TextBlock { Text = message, Margin = new Thickness(0, 8, 0, 16), TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(91, 99, 113)) };
        Grid.SetRow(hint, 1);
        var passwordLabel = new TextBlock { Text = "密码（至少 8 个字符）", Margin = new Thickness(0, 0, 0, 5) };
        Grid.SetRow(passwordLabel, 2);
        var passwordBox = new PasswordBox { Height = 34, Padding = new Thickness(8, 5, 8, 5) };
        AutomationProperties.SetAutomationId(passwordBox, "NotebookPasswordBox");
        AutomationProperties.SetName(passwordBox, "笔记本密码");
        Grid.SetRow(passwordBox, 3);

        PasswordBox? confirmationBox = null;
        if (confirmPassword)
        {
            var confirmationPanel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            confirmationPanel.Children.Add(new TextBlock { Text = "再次输入密码", Margin = new Thickness(0, 0, 0, 5) });
            confirmationBox = new PasswordBox { Height = 34, Padding = new Thickness(8, 5, 8, 5) };
            AutomationProperties.SetAutomationId(confirmationBox, "NotebookPasswordConfirmationBox");
            AutomationProperties.SetName(confirmationBox, "确认笔记本密码");
            confirmationPanel.Children.Add(confirmationBox);
            Grid.SetRow(confirmationPanel, 4);
            root.Children.Add(confirmationPanel);
        }

        var errorText = new TextBlock { Foreground = Brushes.Firebrick, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(errorText, 5);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        Grid.SetRow(buttons, 6);
        var cancelButton = new Button { Content = "取消", MinWidth = 82, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        AutomationProperties.SetAutomationId(cancelButton, "NotebookPasswordCancelButton");
        var confirmButton = new Button { Content = "确认", MinWidth = 90, Padding = new Thickness(14, 7, 14, 7), IsDefault = true };
        AutomationProperties.SetAutomationId(confirmButton, "NotebookPasswordConfirmButton");
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(confirmButton);

        root.Children.Add(heading);
        root.Children.Add(hint);
        root.Children.Add(passwordLabel);
        root.Children.Add(passwordBox);
        root.Children.Add(errorText);
        root.Children.Add(buttons);
        dialog.Content = root;

        confirmButton.Click += (_, _) =>
        {
            if (passwordBox.Password.Length < 8)
            {
                errorText.Text = "密码至少需要 8 个字符。";
                passwordBox.Focus();
                return;
            }
            if (confirmationBox is not null && !string.Equals(passwordBox.Password, confirmationBox.Password, StringComparison.Ordinal))
            {
                errorText.Text = "两次输入的密码不一致。";
                confirmationBox.Focus();
                return;
            }
            result = passwordBox.Password;
            dialog.DialogResult = true;
        };
        dialog.ContentRendered += (_, _) => passwordBox.Focus();
        dialog.ShowDialog();
        return result;
    }

    private string? GetSessionOrPromptPassword(string filePath, string title, string message)
    {
        if (string.Equals(_currentNotebookPath, filePath, StringComparison.OrdinalIgnoreCase) && _currentNotebookPassword is not null)
            return _currentNotebookPassword;
        return PromptForNotebookPassword(title, message, confirmPassword: false);
    }

    private void UpdateNotebookProtectionButton()
    {
        if (NotebookProtectionButton is null) return;
        NotebookProtectionButton.Content = _currentNotebookPassword is null ? "密码保护" : "🔒 已保护";
        NotebookProtectionButton.ToolTip = _currentNotebookPassword is null
            ? "为当前笔记本启用本地密码保护"
            : "更改密码或关闭当前笔记本的本地保护";
    }

    private async void NotebookProtection_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNotebook is null || string.IsNullOrWhiteSpace(_currentNotebookPath)) return;
        try
        {
            if (_isDirty) await SaveNotebookAsync();
            if (_currentNotebookPassword is null)
            {
                if (MessageBox.Show(this,
                        "启用后，笔记本正文、页面、笔迹、PDF 内容和录音信息都会在本机加密。\n\nPaperNote 不保存密码，也无法找回密码。确定继续吗？",
                        "启用密码保护", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                var password = PromptForNotebookPassword("设置笔记本密码", "密码只在当前程序运行期间保留，不会写入设置或文件。", confirmPassword: true);
                if (password is null) return;
                CaptureCurrentPage();
                await _notebookStorage.SaveEncryptedAsync(_currentNotebook, _currentNotebookPath, password);
                _currentNotebookPassword = password;
                _isDirty = false;
                SaveStateText.Text = "已加密保存";
                StatusText.Text = "已启用本地密码保护";
            }
            else
            {
                var choice = MessageBox.Show(this,
                    "选择“是”更改密码；选择“否”关闭密码保护；选择“取消”保持不变。",
                    "管理密码保护", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Cancel) return;
                var currentPassword = PromptForNotebookPassword("验证当前密码", "请输入当前密码以继续。", confirmPassword: false);
                if (currentPassword is null) return;
                _ = await _notebookStorage.LoadEncryptedAsync(_currentNotebookPath, currentPassword);
                if (choice == MessageBoxResult.Yes)
                {
                    var newPassword = PromptForNotebookPassword("设置新密码", "新密码至少 8 个字符，并需要输入两次确认。", confirmPassword: true);
                    if (newPassword is null) return;
                    CaptureCurrentPage();
                    await _notebookStorage.SaveEncryptedAsync(_currentNotebook, _currentNotebookPath, newPassword);
                    _currentNotebookPassword = newPassword;
                    SaveStateText.Text = "已加密保存";
                    StatusText.Text = "密码已更改";
                }
                else
                {
                    if (MessageBox.Show(this,
                            "关闭后，能访问本机文件的人可以直接读取笔记内容。确定改回普通文件吗？",
                            "关闭密码保护", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                    CaptureCurrentPage();
                    await _notebookStorage.RemoveEncryptionAsync(_currentNotebook, _currentNotebookPath);
                    _currentNotebookPassword = null;
                    SaveStateText.Text = "已保存";
                    StatusText.Text = "已关闭密码保护，笔记本已改回普通本地文件";
                }
            }

            UpdateNotebookProtectionButton();
            await RefreshLibraryAsync();
        }
        catch (Exception exception)
        {
            SaveStateText.Text = "保护操作失败";
            MessageBox.Show(this, $"密码保护操作失败。\n\n{exception.Message}", "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
