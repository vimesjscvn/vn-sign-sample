using VMSign.Shared.Models;

namespace VMSign.Shared.Services;

/// <summary>
/// Request model for PDF signing — contains all placed fields and config.
/// </summary>
public class PdfSigningRequest
{
    public required string FilePath { get; init; }
    public string? OutputDirectory { get; init; }
    public required string MerchantId { get; init; }
    public required string CredentialId { get; init; }
    public string? BearerToken { get; init; }
    public string? UserName { get; init; }

    // Signature metadata
    public string? SignerName { get; init; }
    public string? SignerTitle { get; init; }
    public string? Note { get; init; }
    public bool ShowTimestamp { get; init; } = true;
    public string? SignatureImageBase64 { get; init; }

    // Placements to sign (two-stage committed fields)
    public required List<FieldPlacement> Placements { get; init; }

    public string? SignAlgorithm { get; init; }
}

public class FieldPlacement
{
    public string? FieldId { get; init; }
    public int Page { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public bool IsDrawn { get; init; }
}
