using System.IO;
using System.Text.Json;

namespace PaperNote.Desktop.Services;

public sealed class WorkspaceNotebookTab
{
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public sealed class WorkspaceState
{
    public List<WorkspaceNotebookTab> Tabs { get; set; } = [];
    public string ActiveNotebookPath { get; set; } = string.Empty;
    public string LibrarySort { get; set; } = "Modified";
}

public sealed class WorkspaceStateService
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public WorkspaceStateService(string? filePath = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _filePath = Path.GetFullPath(filePath ?? Path.Combine(local, "PaperNote", "workspace-state.json"));
    }

    public async Task<WorkspaceState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath)) return new WorkspaceState();
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
            var state = JsonSerializer.Deserialize<WorkspaceState>(json, _jsonOptions) ?? new WorkspaceState();
            return Normalize(state);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new WorkspaceState();
        }
    }

    public async Task SaveAsync(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            state = Normalize(state);
            var directory = Path.GetDirectoryName(_filePath) ?? throw new InvalidOperationException("无法确定工作区设置目录。");
            Directory.CreateDirectory(directory);
            var temporaryPath = _filePath + ".tmp";
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(state, _jsonOptions);
                await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
                File.Move(temporaryPath, _filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
        finally { _saveGate.Release(); }
    }

    public static WorkspaceState Normalize(WorkspaceState state)
    {
        state.Tabs ??= [];
        state.Tabs = state.Tabs
            .Where(tab => !string.IsNullOrWhiteSpace(tab.FilePath))
            .GroupBy(tab => Path.GetFullPath(tab.FilePath), StringComparer.OrdinalIgnoreCase)
            .Select(group => new WorkspaceNotebookTab
            {
                FilePath = group.Key,
                Title = string.IsNullOrWhiteSpace(group.Last().Title) ? Path.GetFileNameWithoutExtension(group.Key) : group.Last().Title.Trim()
            })
            .TakeLast(8)
            .ToList();
        state.ActiveNotebookPath = string.IsNullOrWhiteSpace(state.ActiveNotebookPath) ? string.Empty : Path.GetFullPath(state.ActiveNotebookPath);
        state.LibrarySort = state.LibrarySort is "Opened" or "Title" ? state.LibrarySort : "Modified";
        return state;
    }
}
