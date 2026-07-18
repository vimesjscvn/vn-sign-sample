namespace VMSign.Shared.Services;

/// <summary>
/// Request model for batch signing.
/// </summary>
public class BatchSigningRequest
{
    public required string SourceDirectory { get; init; }
    public required string OutputDirectory { get; init; }
    public required string MerchantId { get; init; }

    // For LOCAL/SELF merchant
    public string? PfxFilePath { get; init; }
    public string? PfxPassword { get; init; }

    // For USB merchant
    public string? UsbPin { get; init; }

    // For remote merchants (BCY/VIETTEL/VNPT)
    public string? CredentialId { get; init; }
    public string? BearerToken { get; init; }
    public string? UserName { get; init; }
}
