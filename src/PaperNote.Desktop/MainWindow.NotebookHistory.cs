using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PaperNote.Desktop.Services;
using PaperNote.Core.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private async void NotebookHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNotebook is null || string.IsNullOrWhiteSpace(_currentNotebookPath)) return;
        try
        {
            if (_isDirty) await SaveNotebookAsync();
            var notebookPath = _currentNotebookPath;
            NotebookBackupInfo? selectedForRestore = null;

            var dialog = new Window
            {
                Title = "历史版本",
                Owner = this,
                Width = 610,
                Height = 470,
                MinWidth = 520,
                MinHeight = 380,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brushes.White,
                ShowInTaskbar = false
            };
            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var heading = new TextBlock { Text = "历史版本与误改恢复", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(36, 43, 57)) };
            var hint = new TextBlock
            {
                Text = "程序会定期保留旧版本，最多保存 30 份。恢复前也会自动保护当前内容。",
                Margin = new Thickness(0, 8, 0, 14),
                Foreground = new SolidColorBrush(Color.FromRgb(99, 107, 122)),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(hint, 1);

            var list = new ListBox { DisplayMemberPath = nameof(NotebookBackupInfo.DisplayText), BorderBrush = new SolidColorBrush(Color.FromRgb(218, 222, 230)), BorderThickness = new Thickness(1) };
            Grid.SetRow(list, 2);
            var emptyText = new TextBlock { Text = "还没有历史版本。可以先创建一个当前版本。", Foreground = new SolidColorBrush(Color.FromRgb(119, 126, 140)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            Grid.SetRow(emptyText, 2);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            Grid.SetRow(buttons, 3);
            var createButton = new Button { Content = "创建当前版本", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
            var restoreButton = new Button { Content = "恢复所选版本", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
            var closeButton = new Button { Content = "关闭", Padding = new Thickness(18, 7, 18, 7), IsCancel = true };
            buttons.Children.Add(createButton);
            buttons.Children.Add(restoreButton);
            buttons.Children.Add(closeButton);

            root.Children.Add(heading);
            root.Children.Add(hint);
            root.Children.Add(list);
            root.Children.Add(emptyText);
            root.Children.Add(buttons);
            dialog.Content = root;

            async Task RefreshBackupsAsync()
            {
                var backups = await _notebookStorage.ListBackupsAsync(notebookPath);
                list.ItemsSource = backups;
                emptyText.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (backups.Count > 0) list.SelectedIndex = 0;
            }

            list.SelectionChanged += (_, _) => restoreButton.IsEnabled = list.SelectedItem is NotebookBackupInfo;
            createButton.Click += async (_, _) =>
            {
                createButton.IsEnabled = false;
                try
                {
                    await _notebookStorage.CreateBackupAsync(notebookPath);
                    await RefreshBackupsAsync();
                    StatusText.Text = "已创建当前历史版本";
                }
                catch (Exception exception)
                {
                    MessageBox.Show(dialog, $"无法创建历史版本。\n\n{exception.Message}", "创建失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { createButton.IsEnabled = true; }
            };
            restoreButton.Click += (_, _) =>
            {
                if (list.SelectedItem is not NotebookBackupInfo backup) return;
                var result = MessageBox.Show(dialog, $"确定恢复到 {backup.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} 的版本吗？\n\n恢复前会先保护当前内容。", "恢复历史版本", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
                selectedForRestore = backup;
                dialog.DialogResult = true;
            };
            closeButton.Click += (_, _) => dialog.Close();

            await RefreshBackupsAsync();
            dialog.ShowDialog();
            if (selectedForRestore is null) return;

            SaveStateText.Text = "正在恢复…";
            await _notebookStorage.RestoreBackupAsync(notebookPath, selectedForRestore.FilePath);
            _isDirty = false;
            await OpenNotebookAsync(notebookPath);
            await RefreshLibraryAsync();
            SaveStateText.Text = "已保存";
            StatusText.Text = $"已恢复 {selectedForRestore.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} 的历史版本";
        }
        catch (Exception exception)
        {
            SaveStateText.Text = "恢复失败";
            MessageBox.Show(this, $"无法打开或恢复历史版本。\n\n{exception.Message}", "历史版本失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
