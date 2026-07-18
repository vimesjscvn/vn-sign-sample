using VMSign.Shared.Models;

namespace VMSign.Shared.Services;

/// <summary>
/// Validates preconditions before signing: login state and config state.
/// Implements V4 login guard + config guard logic.
/// </summary>
public class SigningGuard
{
    /// <summary>
    /// Check if the user can proceed to sign.
    /// </summary>
    public GuardResult CanProceed(SessionState session, string merchantId)
    {
        // Guard 1: Must be logged in
        if (!session.IsLoggedIn)
        {
            return GuardResult.NeedsLogin();
        }

        // Guard 2: Merchant must be configured
        if (!session.IsConfigured)
        {
            var info = MerchantRegistry.GetDisplayInfo(merchantId);
            return GuardResult.NeedsConfig(
                $"Merchant {info.Name} chưa có thông tin kết nối hợp lệ tới nhà cung cấp chữ ký số.");
        }

        return GuardResult.Allowed();
    }
}
