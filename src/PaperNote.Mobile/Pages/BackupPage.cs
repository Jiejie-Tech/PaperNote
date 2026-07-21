using PaperNote.Mobile.Services;

namespace PaperNote.Mobile.Pages;

public sealed class BackupPage : ContentPage
{
    private readonly MobileNotebookRepository _repository;
    private readonly MobileTransferService _transfer;
    private readonly Label _status;

    public BackupPage(MobileNotebookRepository repository, MobileTransferService transfer)
    {
        _repository = repository;
        _transfer = transfer;
        Title = "备份与恢复";
        BackgroundColor = UiTheme.Background;
        _status = new Label { Text = "备份文件可保存到网盘、聊天或其他设备。", FontSize = 14, TextColor = UiTheme.Muted };
        var export = UiTheme.Button("导出整个资料库备份", Export_Clicked, primary: true);
        var import = UiTheme.Button("恢复资料库备份", Import_Clicked);
        var current = UiTheme.Button("为当前笔记本创建快照", Snapshot_Clicked);
        var restore = UiTheme.Button("恢复当前笔记本快照", RestoreSnapshot_Clicked);
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 14,
                Children =
                {
                    new Label { Text = "备份与恢复", FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = UiTheme.Text },
                    _status,
                    UiTheme.Card(new VerticalStackLayout { Spacing = 10, Children = { new Label { Text = "整个资料库", FontSize = 19, FontAttributes = FontAttributes.Bold, TextColor = UiTheme.Text }, export, import } }),
                    UiTheme.Card(new VerticalStackLayout { Spacing = 10, Children = { new Label { Text = "当前笔记本", FontSize = 19, FontAttributes = FontAttributes.Bold, TextColor = UiTheme.Text }, current, restore } })
                }
            }
        };
    }

    private async void Export_Clicked(object? sender, EventArgs e)
    {
        try { await _transfer.ExportLibraryPackageAsync(); _status.Text = "资料库备份已生成并打开分享面板。"; }
        catch (Exception exception) { await DisplayAlertAsync("备份失败", exception.Message, "知道了"); }
    }

    private async void Import_Clicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await _transfer.ImportLibraryPackageAsync();
            if (result is not null) _status.Text = $"已恢复 {result.ImportedNotebooks} 本笔记，跳过 {result.SkippedNotebooks} 本重复笔记。";
        }
        catch (Exception exception) { await DisplayAlertAsync("恢复失败", exception.Message, "知道了"); }
    }

    private async void Snapshot_Clicked(object? sender, EventArgs e)
    {
        if (_repository.Current is null) { await DisplayAlertAsync("没有当前笔记本", "请先打开一个笔记本。", "知道了"); return; }
        try
        {
            await _repository.SaveCurrentAsync();
            var info = await _repository.Storage.CreateBackupAsync(_repository.Current.FilePath);
            _status.Text = $"已创建快照：{info.DisplayText}";
        }
        catch (Exception exception) { await DisplayAlertAsync("创建失败", exception.Message, "知道了"); }
    }

    private async void RestoreSnapshot_Clicked(object? sender, EventArgs e)
    {
        if (_repository.Current is null) { await DisplayAlertAsync("没有当前笔记本", "请先打开一个笔记本。", "知道了"); return; }
        try
        {
            var backups = await _repository.Storage.ListBackupsAsync(_repository.Current.FilePath);
            if (backups.Count == 0) { await DisplayAlertAsync("没有快照", "当前笔记本还没有可恢复的快照。", "知道了"); return; }
            var labels = backups.Select(item => item.DisplayText).ToArray();
            var choice = await DisplayActionSheetAsync("选择快照", "取消", null, labels);
            var index = Array.IndexOf(labels, choice);
            if (index < 0) return;
            if (!await DisplayAlertAsync("恢复快照", "当前内容会先自动备份，然后替换为所选快照。", "恢复", "取消")) return;
            var document = await _repository.Storage.RestoreBackupAsync(_repository.Current.FilePath, backups[index].FilePath);
            await _repository.OpenAsync(new PaperNote.Core.Models.StoredNotebook { FilePath = _repository.Current.FilePath, Document = document });
            _status.Text = "快照已恢复。";
        }
        catch (Exception exception) { await DisplayAlertAsync("恢复失败", exception.Message, "知道了"); }
    }
}
