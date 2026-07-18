namespace VMSign.Shared.Services;

/// <summary>
/// Represents the current user signing session state.
/// </summary>
public class SessionState
{
    public bool IsLoggedIn { get; set; }
    public string? UserName { get; set; }
    public string? BearerToken { get; set; }
    public string? ActiveMerchantId { get; set; }
    public string? SelectedCredentialId { get; set; }
    public string? CertificateDisplayName { get; set; }
    public DateTime? CertificateExpiry { get; set; }

    public bool IsConfigured { get; set; }
}
