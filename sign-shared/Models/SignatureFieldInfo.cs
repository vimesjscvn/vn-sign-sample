namespace VMSign.Shared.Models;

/// <summary>
/// Represents a detected signature form field in a PDF document.
/// </summary>
public class SignatureFieldInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Page { get; init; }

    // PDF point coordinates (origin = bottom-left)
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }

    /// <summary>Whether this field already contains a signature.</summary>
    public bool IsSigned { get; init; }
}
