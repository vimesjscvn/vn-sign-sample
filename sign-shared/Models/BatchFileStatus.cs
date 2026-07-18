namespace VMSign.Shared.Models;

/// <summary>
/// Tracks the signing status of a single file in a batch operation.
/// </summary>
public class BatchFileStatus
{
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
    public BatchFileState State { get; set; } = BatchFileState.Pending;
    public string? ErrorMessage { get; set; }
}

public enum BatchFileState
{
    Pending,
    Signing,
    Done,
    Error
}
