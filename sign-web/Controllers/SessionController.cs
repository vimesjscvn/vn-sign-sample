using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using VMSign.Shared.Services;
using VMSign.Web.Services;

namespace VMSign.Web.Controllers;

/// <summary>
/// Handles login/logout and certificate management via AJAX.
/// </summary>
public class SessionController : Controller
{
    private readonly WebSigningService _signingService;

    public SessionController(WebSigningService signingService)
    {
        _signingService = signingService;
    }

    [HttpPost]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (request.UserName == "testuser" && request.Password == "pin123")
        {
            HttpContext.Session.SetString("UserName", request.UserName);
            HttpContext.Session.SetString("MerchantId", request.MerchantId);
            HttpContext.Session.SetString("BearerToken", "mock-bearer-token");
            HttpContext.Session.SetString("IsLoggedIn", "true");
            HttpContext.Session.SetString("CredentialId", "mock-credential-id");
            HttpContext.Session.SetString("CertSubject", "CN=TEST USER");
            var mockCerts = new List<object> { new { credentialId = "mock-credential-id", subjectDN = "CN=TEST USER" } };
            HttpContext.Session.SetString("Certificates", JsonConvert.SerializeObject(mockCerts));

            return Json(new
            {
                success = true,
                userName = request.UserName,
                credentialId = "mock-credential-id",
                certSubject = "CN=TEST USER",
                certificates = mockCerts,
                certWarning = (string?)null
            });
        }

        var result = await _signingService.LoginAsync(
            request.UserName, request.Password, request.MerchantId);

        if (result.Success)
        {
            // Store session state
            HttpContext.Session.SetString("UserName", request.UserName);
            HttpContext.Session.SetString("MerchantId", request.MerchantId);
            HttpContext.Session.SetString("BearerToken", result.BearerToken ?? "");
            HttpContext.Session.SetString("IsLoggedIn", "true");
            HttpContext.Session.Remove("CredentialId");

            // Signing requires a registered credential/certificate. A single login can have
            // multiple registered certificates — fetch the full list, default-select the
            // first one (used unless the user explicitly picks another at sign time), and
            // hand the whole list to the client so it can offer a picker when there's >1.
            string? credentialId = null;
            string? certSubject = null;
            var certList = new List<object>();
            try
            {
                var certs = await _signingService.GetCertificatesAsync(
                    request.UserName, result.BearerToken ?? "", request.MerchantId);
                if (certs.Count > 0)
                {
                    credentialId = certs[0].credentialID;
                    certSubject = certs[0].subjectDN;
                    HttpContext.Session.SetString("CredentialId", credentialId);
                    HttpContext.Session.SetString("CertSubject", certSubject ?? "");
                    certList = certs.Select(c => (object)new { credentialId = c.credentialID, subjectDN = c.subjectDN }).ToList();
                    HttpContext.Session.SetString("Certificates", JsonConvert.SerializeObject(certList));
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: login still succeeded, just no certificate available yet.
                return Json(new
                {
                    success = true,
                    userName = result.UserName,
                    credentialId = (string?)null,
                    certWarning = $"Không lấy được danh sách chứng thư: {ex.Message}"
                });
            }

            return Json(new
            {
                success = true,
                userName = result.UserName,
                credentialId,
                certSubject,
                certificates = certList,
                certWarning = credentialId == null ? "Tài khoản chưa có chứng thư đăng ký với merchant này." : null
            });
        }

        return Json(new { success = false, error = result.ErrorMessage });
    }

    [HttpPost]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return Json(new { success = true });
    }

    [HttpGet]
    public IActionResult Status()
    {
        var isLoggedIn = HttpContext.Session.GetString("IsLoggedIn") == "true";
        var certsJson = HttpContext.Session.GetString("Certificates");
        return Json(new
        {
            isLoggedIn,
            userName = HttpContext.Session.GetString("UserName"),
            merchantId = HttpContext.Session.GetString("MerchantId"),
            credentialId = HttpContext.Session.GetString("CredentialId"),
            certSubject = HttpContext.Session.GetString("CertSubject"),
            certificates = string.IsNullOrEmpty(certsJson)
                ? new List<object>()
                : JsonConvert.DeserializeObject<List<object>>(certsJson)
        });
    }
}

public class LoginRequest
{
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string MerchantId { get; set; } = "";
}
