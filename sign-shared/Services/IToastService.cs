namespace VMSign.Shared.Services;

/// <summary>
/// Platform-agnostic toast notification service.
/// Desktop and Web implement this differently.
/// </summary>
public interface IToastService
{
    void ShowSuccess(string title, string message);
    void ShowWarning(string title, string message);
    void ShowError(string title, string message);
    void ShowInfo(string title, string message);
}
