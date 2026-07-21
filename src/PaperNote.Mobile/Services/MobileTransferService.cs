using PaperNote.Core.Services;

namespace PaperNote.Mobile.Services;

public sealed class MobileTransferService(MobileNotebookRepository repository)
{
    private readonly LibraryBackupPackageService _packages = new();

    public async Task<FileResult?> PickNotebookAsync() => await FilePicker.Default.PickAsync(new PickOptions
    {
        PickerTitle = "导入 PaperNote 笔记本",
        FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] = ["application/json", "application/octet-stream", "text/plain"]
        })
    });

    public async Task<FileResult?> PickPdfAsync() => await FilePicker.Default.PickAsync(new PickOptions
    {
        PickerTitle = "导入 PDF",
        FileTypes = FilePickerFileType.Pdf
    });

    public async Task ShareNotebookAsync()
    {
        var current = repository.Current ?? throw new InvalidOperationException("尚未打开笔记本。");
        await repository.SaveCurrentAsync();
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = $"导出 {current.Document.Title}",
            File = new ShareFile(current.FilePath, "application/octet-stream")
        });
    }

    public async Task<string> ExportLibraryPackageAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, $"PaperNote-Backup-{DateTime.Now:yyyyMMdd-HHmmss}.pnbak");
        await _packages.ExportAsync(path, repository.Storage.NotebooksDirectory, repository.Storage.BackupsDirectory, cancellationToken);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "导出 PaperNote 资料库备份",
            File = new ShareFile(path, "application/zip")
        });
        return path;
    }

    public async Task<LibraryImportResult?> ImportLibraryPackageAsync(CancellationToken cancellationToken = default)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "恢复资料库备份" });
        if (file is null) return null;
        var cachePath = Path.Combine(FileSystem.CacheDirectory, $"restore-{Guid.NewGuid():N}.pnbak");
        await using (var source = await file.OpenReadAsync())
        await using (var target = File.Create(cachePath))
            await source.CopyToAsync(target, cancellationToken);
        try
        {
            var result = await _packages.ImportAsync(cachePath, repository.Storage.NotebooksDirectory, repository.Storage.BackupsDirectory, cancellationToken);
            await repository.RefreshAsync(cancellationToken: cancellationToken);
            return result;
        }
        finally
        {
            if (File.Exists(cachePath)) File.Delete(cachePath);
        }
    }
}
