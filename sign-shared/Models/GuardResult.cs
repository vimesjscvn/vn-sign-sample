namespace VMSign.Shared.Models;

/// <summary>
/// Result of pre-sign validation checks (login guard, config guard).
/// </summary>
public class GuardResult
{
    public GuardStatus Status { get; init; }
    public string? Message { get; init; }

    public static GuardResult Allowed() => new() { Status = GuardStatus.Allowed };
    public static GuardResult NeedsLogin(string msg = "Vui lòng đăng nhập phiên ký trước khi thực hiện ký số.")
        => new() { Status = GuardStatus.NeedsLogin, Message = msg };
    public static GuardResult NeedsConfig(string msg = "Merchant chưa được cấu hình.")
        => new() { Status = GuardStatus.NeedsConfig, Message = msg };
}

public enum GuardStatus
{
    Allowed,
    NeedsLogin,
    NeedsConfig
}
