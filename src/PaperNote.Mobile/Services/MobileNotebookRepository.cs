using PaperNote.Core.Models;
using PaperNote.Core.Services;

namespace PaperNote.Mobile.Services;

public sealed class MobileNotebookRepository
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public MobileNotebookRepository()
    {
        var root = FileSystem.AppDataDirectory;
        Storage = new NotebookStorageService(
            Path.Combine(root, "Notebooks"),
            Path.Combine(root, "Backups"));
        TemplateLibrary = new PaperTemplateLibraryService(Path.Combine(root, "paper-templates.json"));
    }

    public NotebookStorageService Storage { get; }
    public PaperTemplateLibraryService TemplateLibrary { get; }
    public IReadOnlyList<StoredNotebook> Notebooks { get; private set; } = [];
    public StoredNotebook? Current { get; private set; }
    public IReadOnlyList<NotebookRecoveryResult> LastRecoveryResults { get; private set; } = [];
    public IReadOnlyList<NotebookRecoveryCandidate> RecoveryCandidates { get; private set; } = [];

    public event EventHandler? LibraryChanged;
    public event EventHandler? CurrentChanged;

    public async Task<IReadOnlyList<StoredNotebook>> RefreshAsync(string? query = null, bool includeTrash = false, CancellationToken cancellationToken = default)
    {
        LastRecoveryResults = await Storage.RecoverTemporaryDraftsAsync(cancellationToken);
        RecoveryCandidates = await Storage.InspectRecoveryAsync(cancellationToken);
        var all = await Storage.ListAsync(cancellationToken);
        Notebooks = all
            .Where(item => includeTrash ? item.Document.IsInTrash : !item.Document.IsInTrash)
            .Where(item => NotebookContentService.TryMatch(item.Document, query, out _))
            .ToArray();
        LibraryChanged?.Invoke(this, EventArgs.Empty);
        return Notebooks;
    }

    public async Task<StoredNotebook> CreateAsync(string? title = null, CancellationToken cancellationToken = default)
    {
        var document = NotebookDocument.Create(string.IsNullOrWhiteSpace(title) ? $"新笔记 {DateTime.Now:MM-dd}" : title.Trim());
        var stored = await Storage.CreateAsync(document, cancellationToken);
        Current = stored;
        await RefreshAsync(cancellationToken: cancellationToken);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return stored;
    }

    public async Task OpenAsync(StoredNotebook stored, CancellationToken cancellationToken = default)
    {
        var document = await Storage.LoadAsync(stored.FilePath, cancellationToken);
        document.LastOpenedAt = DateTimeOffset.Now;
        Current = new StoredNotebook { FilePath = stored.FilePath, Document = document };
        await SaveCurrentAsync(cancellationToken);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveCurrentAsync(CancellationToken cancellationToken = default)
    {
        var current = Current;
        if (current is null) return;

        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            await Storage.SaveAsync(current.Document, current.FilePath, cancellationToken);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public NotebookPage GetCurrentPage()
    {
        var notebook = Current?.Document ?? throw new InvalidOperationException("尚未打开笔记本。");
        var page = notebook.Pages.FirstOrDefault(item => item.Id == notebook.CurrentPageId) ?? notebook.Pages.First();
        notebook.CurrentPageId = page.Id;
        return page;
    }

    public NotebookPage AddPage(string template = "Dotted", string color = "#FFFFFF", int? afterIndex = null)
    {
        var notebook = Current?.Document ?? throw new InvalidOperationException("尚未打开笔记本。");
        var page = new NotebookPage { PaperTemplate = template, PaperColor = color };
        var currentIndex = notebook.Pages.FindIndex(item => item.Id == notebook.CurrentPageId);
        var index = afterIndex ?? Math.Clamp(currentIndex + 1, 0, notebook.Pages.Count);
        notebook.Pages.Insert(Math.Clamp(index, 0, notebook.Pages.Count), page);
        notebook.CurrentPageId = page.Id;
        return page;
    }

    public NotebookPage DuplicatePage(Guid pageId)
    {
        var notebook = Current?.Document ?? throw new InvalidOperationException("尚未打开笔记本。");
        var index = notebook.Pages.FindIndex(item => item.Id == pageId);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(pageId));
        var copy = notebook.Pages[index].Clone();
        notebook.Pages.Insert(index + 1, copy);
        notebook.CurrentPageId = copy.Id;
        return copy;
    }

    public bool DeletePage(Guid pageId)
    {
        var notebook = Current?.Document;
        if (notebook is null || notebook.Pages.Count <= 1) return false;
        var index = notebook.Pages.FindIndex(item => item.Id == pageId);
        if (index < 0) return false;
        var deleted = PageBatchService.Delete(notebook, [pageId]);
        if (deleted) notebook.CurrentPageId = notebook.Pages[Math.Clamp(index, 0, notebook.Pages.Count - 1)].Id;
        return deleted;
    }

    public async Task<StoredNotebook> ImportNotebookAsync(FileResult file, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Storage.NotebooksDirectory);
        var importedPath = Path.Combine(Storage.NotebooksDirectory, $"import-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.papernote");
        await using (var source = await file.OpenReadAsync())
        await using (var target = File.Create(importedPath))
            await source.CopyToAsync(target, cancellationToken);

        try
        {
            var document = await Storage.LoadAsync(importedPath, cancellationToken);
            var duplicateId = (await Storage.ListAsync(cancellationToken))
                .Any(item => !string.Equals(item.FilePath, importedPath, StringComparison.OrdinalIgnoreCase) && item.Document.Id == document.Id);
            if (duplicateId)
            {
                document.Id = Guid.NewGuid();
                await Storage.SaveAsync(document, importedPath, cancellationToken);
            }
            Current = new StoredNotebook { FilePath = importedPath, Document = document };
            await RefreshAsync(cancellationToken: cancellationToken);
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return Current;
        }
        catch
        {
            if (File.Exists(importedPath)) File.Delete(importedPath);
            throw;
        }
    }

    public async Task MoveCurrentToTrashAsync(CancellationToken cancellationToken = default)
    {
        if (Current is null) return;
        await Storage.MoveToTrashAsync(Current.FilePath, cancellationToken);
        Current = null;
        await RefreshAsync(cancellationToken: cancellationToken);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}
