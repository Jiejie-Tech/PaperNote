namespace PaperNote.Mobile.Models;

public sealed class NotebookCard
{
    public required PaperNote.Core.Models.StoredNotebook Stored { get; init; }
    public string Title => Stored.Document.Title;
    public string Detail => $"{Stored.Document.Pages.Count} 页 · {Stored.Document.ModifiedAt.LocalDateTime:MM-dd HH:mm}";
    public string Folder => string.IsNullOrWhiteSpace(Stored.Document.FolderName) ? "未分类" : Stored.Document.FolderName;
    public string CoverColor => Stored.Document.CoverStyle switch
    {
        "Purple" => "#7657D9",
        "Green" => "#2D9D78",
        "Orange" => "#E28B35",
        "Red" => "#D85B5B",
        "Gray" => "#667085",
        _ => "#3157D5"
    };
}

public sealed class PageCard
{
    public required PaperNote.Core.Models.NotebookPage Page { get; init; }
    public required int Number { get; init; }
    public string Title => string.IsNullOrWhiteSpace(Page.Title) ? $"第 {Number} 页" : Page.Title;
    public string Detail => $"{Page.Ink.Strokes.Count} 条笔迹";
}
