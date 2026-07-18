using VMSign.Shared.Models;

namespace VMSign.Shared.Services;

/// <summary>
/// Orchestrates the signing workflow including guards, placement, and execution.
/// </summary>
public interface ISigningOrchestrator
{
    /// <summary>Check login + config guards before signing.</summary>
    GuardResult ValidateBeforeSign(SessionState session, string merchantId);

    /// <summary>Execute signing for all placed fields.</summary>
    Task<SigningResult> SignPdfAsync(PdfSigningRequest request);

    /// <summary>Execute XML signing.</summary>
    Task<SigningResult> SignXmlAsync(XmlSigningRequest request);

    /// <summary>Execute batch signing for a folder.</summary>
    Task<SigningResult> SignBatchAsync(
        BatchSigningRequest request,
        IProgress<BatchFileStatus>? progress = null,
        CancellationToken cancellationToken = default);
}
