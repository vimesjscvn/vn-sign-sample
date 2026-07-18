namespace VMSign.Shared.Models;

/// <summary>
/// Describes a signing merchant/provider for display in the merchant dropdown.
/// </summary>
public class MerchantInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Tag { get; init; }
    public string? SwitchNote { get; init; }

    /// <summary>
    /// Whether this merchant requires a local PFX file (LOCAL/SELF),
    /// a USB PIN (USB), or uses remote signing (BCY/VIETTEL/VNPT).
    /// </summary>
    public MerchantCertMode CertMode { get; init; }
}

public enum MerchantCertMode
{
    LocalPfx,
    UsbToken,
    Remote
}
