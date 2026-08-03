namespace VMSign.Shared.Services;

/// <summary>
/// Request model for XML signing.
/// </summary>
public class XmlSigningRequest
{
    public required string FilePath { get; init; }
    public string? OutputDirectory { get; init; }
    public required string MerchantId { get; init; }
    public required string CredentialId { get; init; }
    public string? BearerToken { get; init; }
    public string? UserName { get; init; }

    /// <summary>Hardware-token PIN (USB merchant) — forwarded to the USB Token Agent
    /// so it can sign via PKCS#11 without the token's interactive PIN dialog.</summary>
    public string? Pin { get; init; }

    public string? SignatureName { get; init; }
    public string? SignTag { get; init; }
    public string? ParentXPath { get; init; }
    public string? ReferenceUri { get; init; }
}
