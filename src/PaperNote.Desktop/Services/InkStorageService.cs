using System.IO;
using System.Windows.Ink;

namespace PaperNote.Desktop.Services;

public sealed class InkStorageService
{
    public InkStorageService()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        NotesDirectory = Path.Combine(documents, "PaperNote", "QuickNotes");
        AutosavePath = Path.Combine(NotesDirectory, "quick-note.isf");
    }

    public string NotesDirectory { get; }
    public string AutosavePath { get; }

    public async Task SaveAsync(StrokeCollection strokes, string? destinationPath = null, CancellationToken cancellationToken = default)
    {
        var bytes = InkHistoryService.Serialize(strokes);
        await SaveBytesAsync(bytes, destinationPath ?? AutosavePath, cancellationToken);
    }

    public async Task SaveBytesAsync(byte[] bytes, string destinationPath, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("无法确定笔记保存目录。");

        Directory.CreateDirectory(directory);
        var temporaryPath = destinationPath + ".tmp";

        await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);

        // 先完整写入临时文件，再替换正式文件，降低异常退出时损坏笔记的风险。
        File.Move(temporaryPath, destinationPath, overwrite: true);
    }

    public async Task<StrokeCollection> LoadAsync(string? sourcePath = null, CancellationToken cancellationToken = default)
    {
        var path = sourcePath ?? AutosavePath;
        if (!File.Exists(path))
        {
            return new StrokeCollection();
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return InkHistoryService.Deserialize(bytes);
    }
}

