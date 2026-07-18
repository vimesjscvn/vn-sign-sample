namespace VMSign.Shared.Models;

/// <summary>
/// Result of a signing operation (single or batch).
/// </summary>
public class SigningResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int SignedCount { get; init; }
    public int FailedCount { get; init; }
    public string? OutputPath { get; init; }
}
