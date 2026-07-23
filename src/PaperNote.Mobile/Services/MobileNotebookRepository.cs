using PaperNote.Core.Models;
using PaperNote.Core.Services;

namespace PaperNote.Mobile.Services;

public sealed class MobileNotebookRepository
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private string? _currentPassword;

    public MobileNotebookRepository()
    {
        var root = FileSystem.AppDataDirectory;
        Storage = new NotebookStorageService(
            Path.Combine(root, "Notebooks"),
            Path.Combine(root, "Backups"));
        TemplateLibrary = new PaperTemplateLibraryService(Path.Combine(root, "paper-templates.json"));
        MaterialLibrary = new SelectionMaterialLibraryService(Path.Combine(root, "selection-materials.json"));
    }

    public NotebookStorageService Storage { get; }
    public PaperTemplateLibraryService TemplateLibrary { get; }
    public SelectionMaterialLibraryService MaterialLibrary { get; }
    public IReadOnlyList<StoredNotebook> Notebooks { get; private set; } = [];
    public StoredNotebook? Current { get; private set; }
    public IReadOnlyList<NotebookRecoveryResult> LastRecoveryResults { get; private set; } = [];
    public IReadOnlyList<NotebookRecoveryCandidate> RecoveryCandidates { get; private set; } = [];
    public bool IsCurrentEncrypted => !string.IsNullOrEmpty(_currentPassword);

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
        _currentPassword = null;
        Current = stored;
        await RefreshAsync(cancellationToken: cancellationToken);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return stored;
    }

    public async Task OpenAsync(StoredNotebook stored, string? password = null, CancellationToken cancellationToken = default)
    {
        var document = stored.IsEncrypted
            ? await Storage.LoadEncryptedAsync(stored.FilePath, password ?? throw new NotebookPasswordRequiredException(stored.FilePath), cancellationToken)
            : await Storage.LoadAsync(stored.FilePath, cancellationToken);
        document.LastOpenedAt = DateTimeOffset.Now;
        _currentPassword = stored.IsEncrypted ? password : null;
        Current = new StoredNotebook
        {
            FilePath = stored.FilePath,
            Document = document,
            PageCount = document.Pages.Count,
            IsEncrypted = stored.IsEncrypted
        };
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
            if (IsCurrentEncrypted)
                await Storage.SaveEncryptedAsync(current.Document, current.FilePath, _currentPassword!, cancellationToken);
            else
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

    public async Task<StoredNotebook> ImportNotebookAsync(FileResult file, string? password = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Storage.NotebooksDirectory);
        var importedPath = Path.Combine(Storage.NotebooksDirectory, $"import-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.papernote");
        await using (var source = await file.OpenReadAsync())
        await using (var target = File.Create(importedPath))
            await source.CopyToAsync(target, cancellationToken);

        try
        {
            var encrypted = await Storage.IsEncryptedAsync(importedPath, cancellationToken);
            var document = encrypted
                ? await Storage.LoadEncryptedAsync(importedPath, password ?? throw new NotebookPasswordRequiredException(importedPath), cancellationToken)
                : await Storage.LoadAsync(importedPath, cancellationToken);
            var duplicateId = (await Storage.ListAsync(cancellationToken))
                .Any(item => !string.Equals(item.FilePath, importedPath, StringComparison.OrdinalIgnoreCase) && item.Document.Id == document.Id);
            if (duplicateId)
            {
                document.Id = Guid.NewGuid();
                if (encrypted) await Storage.SaveEncryptedAsync(document, importedPath, password!, cancellationToken);
                else await Storage.SaveAsync(document, importedPath, cancellationToken);
            }
            _currentPassword = encrypted ? password : null;
            Current = new StoredNotebook
            {
                FilePath = importedPath,
                Document = document,
                PageCount = document.Pages.Count,
                IsEncrypted = encrypted
            };
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

    public async Task EnableEncryptionAsync(string password, CancellationToken cancellationToken = default)
    {
        var current = Current ?? throw new InvalidOperationException("尚未打开笔记本。");
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            await Storage.SaveEncryptedAsync(current.Document, current.FilePath, password, cancellationToken);
            _currentPassword = password;
            Current = new StoredNotebook
            {
                FilePath = current.FilePath,
                Document = current.Document,
                PageCount = current.Document.Pages.Count,
                IsEncrypted = true
            };
        }
        finally
        {
            _saveGate.Release();
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ChangeEncryptionPasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var current = Current ?? throw new InvalidOperationException("尚未打开笔记本。");
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            _ = await Storage.LoadEncryptedAsync(current.FilePath, currentPassword, cancellationToken);
            await Storage.SaveEncryptedAsync(current.Document, current.FilePath, newPassword, cancellationToken);
            _currentPassword = newPassword;
        }
        finally
        {
            _saveGate.Release();
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisableEncryptionAsync(string password, CancellationToken cancellationToken = default)
    {
        var current = Current ?? throw new InvalidOperationException("尚未打开笔记本。");
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            _ = await Storage.LoadEncryptedAsync(current.FilePath, password, cancellationToken);
            await Storage.RemoveEncryptionAsync(current.Document, current.FilePath, cancellationToken);
            _currentPassword = null;
            Current = new StoredNotebook
            {
                FilePath = current.FilePath,
                Document = current.Document,
                PageCount = current.Document.Pages.Count,
                IsEncrypted = false
            };
        }
        finally
        {
            _saveGate.Release();
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task MoveCurrentToTrashAsync(CancellationToken cancellationToken = default)
    {
        if (Current is null) return;
        if (IsCurrentEncrypted)
            await Storage.MoveEncryptedToTrashAsync(Current.FilePath, _currentPassword!, cancellationToken);
        else
            await Storage.MoveToTrashAsync(Current.FilePath, cancellationToken);
        Current = null;
        _currentPassword = null;
        await RefreshAsync(cancellationToken: cancellationToken);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

}
