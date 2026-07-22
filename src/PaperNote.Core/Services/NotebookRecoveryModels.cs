using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public enum NotebookRecoveryKind
{
    TemporaryDraft,
    CorruptedNotebook
}

public sealed class NotebookRecoveryCandidate
{
    public required string FilePath { get; init; }
    public required NotebookRecoveryKind Kind { get; init; }
    public required DateTimeOffset LastWriteTime { get; init; }
    public required long FileSize { get; init; }
    public required bool IsReadable { get; init; }
    public required string DisplayName { get; init; }
    public string? Error { get; init; }
    public NotebookDocument? Document { get; init; }

    public bool CanPromote => Kind == NotebookRecoveryKind.TemporaryDraft && IsReadable && Document is not null;
}

public sealed class NotebookRecoveryResult
{
    public required string FilePath { get; init; }
    public required NotebookRecoveryKind Kind { get; init; }
    public required bool Recovered { get; init; }
    public required string Message { get; init; }
    public NotebookDocument? Document { get; init; }
}

public sealed class ReadOnlyNotebookRecovery
{
    public required string FilePath { get; init; }
    public required bool IsReadable { get; init; }
    public required string DisplayName { get; init; }
    public NotebookDocument? Document { get; init; }
    public string? Error { get; init; }
    public string RawPreview { get; init; } = string.Empty;
}
