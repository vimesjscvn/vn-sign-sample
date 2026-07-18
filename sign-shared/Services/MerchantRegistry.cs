using VMSign.Shared.Models;

namespace VMSign.Shared.Services;

/// <summary>
/// Provides display metadata for known merchants.
/// The actual list of available merchants comes from ISignSDKClient.GetRegisteredMerchants().
/// This only maps IDs → display info (name, description, icon tag).
/// </summary>
public static class MerchantRegistry
{
    private static readonly Dictionary<string, MerchantInfo> _metadata = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BCY"] = new() { Id = "BCY", Name = "BCY", Description = "BCY", Tag = "B", SwitchNote = "Ký số qua HSM Ban Cơ yếu Chính phủ.", CertMode = MerchantCertMode.Remote },
        ["VIETTEL"] = new() { Id = "VIETTEL", Name = "Viettel", Description = "Viettel", Tag = "V", SwitchNote = "Ký số qua Viettel MySign.", CertMode = MerchantCertMode.Remote },
        ["VNPT"] = new() { Id = "VNPT", Name = "SmartCA", Description = "SmartCA", Tag = "N", SwitchNote = "Ký số qua VNPT SmartCA.", CertMode = MerchantCertMode.Remote },
        ["USB"] = new() { Id = "USB", Name = "USB", Description = "USB", Tag = "U", SwitchNote = "Tự động khởi động USB Token Agent.", CertMode = MerchantCertMode.UsbToken },
        ["LOCAL"] = new() { Id = "LOCAL", Name = "LOCAL", Description = "LOCAL", Tag = "L", SwitchNote = "Ký bằng tệp chứng thư PFX/P12 trên máy.", CertMode = MerchantCertMode.LocalPfx },
        ["SELF"] = new() { Id = "SELF", Name = "SELF", Description = "SELF", Tag = "S", SwitchNote = "Ký bằng chứng thư tự cấp.", CertMode = MerchantCertMode.LocalPfx },
        ["CMC"] = new() { Id = "CMC", Name = "CMC", Description = "CMC", Tag = "C", SwitchNote = "Ký số qua CMC CA.", CertMode = MerchantCertMode.Remote },
        ["INTRUST"] = new() { Id = "INTRUST", Name = "InTrust", Description = "InTrust", Tag = "I", SwitchNote = "Ký số qua InTrust.", CertMode = MerchantCertMode.Remote },
        ["SIM"] = new() { Id = "SIM", Name = "SIM", Description = "SIM", Tag = "M", SwitchNote = "Ký số qua SIM PKI.", CertMode = MerchantCertMode.Remote },
    };

    /// <summary>
    /// Get display metadata for a merchant ID. Returns a fallback if unknown.
    /// </summary>
    public static MerchantInfo GetDisplayInfo(string merchantId)
    {
        if (_metadata.TryGetValue(merchantId, out var info))
            return info;

        // Fallback for unknown merchants registered in SDK but not in our metadata
        return new MerchantInfo
        {
            Id = merchantId,
            Name = merchantId,
            Description = "Nhà cung cấp ký số",
            Tag = merchantId.Length > 0 ? merchantId[..1] : "?",
            SwitchNote = $"Đã chuyển sang {merchantId}.",
            CertMode = MerchantCertMode.Remote
        };
    }

    /// <summary>
    /// Map a list of registered merchant IDs (from SDK) to display info.
    /// </summary>
    public static List<MerchantInfo> GetDisplayInfoForIds(IEnumerable<string> merchantIds)
    {
        return merchantIds.Select(GetDisplayInfo).ToList();
    }
}
