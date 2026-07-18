using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace VMSign.Web.Controllers;

/// <summary>
/// Backs the "Cài Đặt SDK" panel.
/// Save/Get persist to HttpContext.Session (per-browser, in-memory, lost on server
/// restart or after the session's idle timeout — see Program.cs). WebSigningService
/// reads these back and applies them onto the process-wide AppSettings singleton
/// just before login, so saved values actually take effect without a server restart.
/// </summary>
public class SettingsController : Controller
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    [HttpPost]
    public async Task<IActionResult> TestConnection([FromBody] TestConnectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BaseUrl) || !Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out var uri))
            return Json(new { success = false, message = "URL không hợp lệ." });

        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            return Json(new { success = true, message = $"Kết nối thành công (HTTP {(int)response.StatusCode})." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Không thể kết nối: {ex.Message}" });
        }
    }

    [HttpPost]
    public IActionResult Save([FromBody] SaveSettingsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MerchantId))
            return Json(new { success = false, message = "Thiếu merchant." });

        var o = new MerchantSettingsOverride
        {
            BaseUrl = request.BaseUrl,
            ProfileId = request.ProfileId,
            ClientId = request.ClientId,
            ClientSecret = request.ClientSecret
        };
        HttpContext.Session.SetString($"MerchantSettings:{request.MerchantId}", JsonConvert.SerializeObject(o));
        return Json(new { success = true });
    }

    [HttpGet]
    public IActionResult Get(string merchantId)
    {
        var json = HttpContext.Session.GetString($"MerchantSettings:{merchantId}");
        var o = string.IsNullOrEmpty(json)
            ? new MerchantSettingsOverride()
            : JsonConvert.DeserializeObject<MerchantSettingsOverride>(json) ?? new MerchantSettingsOverride();
        return Json(o);
    }
}

public class TestConnectionRequest
{
    public string BaseUrl { get; set; } = "";
}

public class SaveSettingsRequest
{
    public string MerchantId { get; set; } = "";
    public string? BaseUrl { get; set; }
    public string? ProfileId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

public class MerchantSettingsOverride
{
    public string? BaseUrl { get; set; }
    public string? ProfileId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}
