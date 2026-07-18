using Core.Config.Settings;
using Newtonsoft.Json;
using SignSDK;
using Signature.Domain.API;
using VMSign.Shared.Models;
using VMSign.Shared.Services;
using VMSign.Web.Controllers;

namespace VMSign.Web.Services;

/// <summary>
/// Delegates to ISignSDKClient — same signing engine as sign-app (desktop).
/// </summary>
public class WebSigningService
{
    private readonly ISignSDKClient _signClient;
    private readonly ILogger<WebSigningService> _logger;
    private readonly AppSettings _appSettings;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public WebSigningService(
        ISignSDKClient signClient,
        ILogger<WebSigningService> logger,
        AppSettings appSettings,
        IHttpContextAccessor httpContextAccessor)
    {
        _signClient = signClient;
        _logger = logger;
        _appSettings = appSettings;
        _httpContextAccessor = httpContextAccessor;
    }

    public IReadOnlyList<string> GetRegisteredMerchants()
    {
        return _signClient.GetRegisteredMerchants();
    }

    /// <summary>
    /// Applies any session-saved "Cài Đặt SDK" override for this merchant onto the
    /// process-wide AppSettings singleton before the SDK reads it.
    /// NOTE: AppSettings is shared across ALL concurrent users/requests — this mutates
    /// global state, not a per-request value. Acceptable for this single-developer
    /// sample; a multi-user deployment would need the SDK itself to accept per-call
    /// credential overrides instead.
    /// </summary>
    private void ApplySessionSettingsOverride(string merchantId)
    {
        var session = _httpContextAccessor.HttpContext?.Session;
        var json = session?.GetString($"MerchantSettings:{merchantId}");
        if (string.IsNullOrEmpty(json)) return;

        var o = JsonConvert.DeserializeObject<MerchantSettingsOverride>(json);
        if (o == null) return;

        switch (merchantId.ToUpperInvariant())
        {
            case "VIETTEL":
                if (!string.IsNullOrWhiteSpace(o.BaseUrl)) _appSettings.MySignSetting.BaseUrl = o.BaseUrl;
                if (!string.IsNullOrWhiteSpace(o.ProfileId)) _appSettings.MySignSetting.ProfileId = o.ProfileId;
                if (!string.IsNullOrWhiteSpace(o.ClientId)) _appSettings.MySignSetting.ClientId = o.ClientId;
                if (!string.IsNullOrWhiteSpace(o.ClientSecret)) _appSettings.MySignSetting.ClientSecret = o.ClientSecret;
                break;
            case "VNPT":
                if (!string.IsNullOrWhiteSpace(o.BaseUrl)) _appSettings.SmartCASetting.BaseUrl = o.BaseUrl;
                if (!string.IsNullOrWhiteSpace(o.ProfileId)) _appSettings.SmartCASetting.ProfileId = o.ProfileId;
                if (!string.IsNullOrWhiteSpace(o.ClientId)) _appSettings.SmartCASetting.ClientId = o.ClientId;
                if (!string.IsNullOrWhiteSpace(o.ClientSecret)) _appSettings.SmartCASetting.ClientSecret = o.ClientSecret;
                break;
            // Other merchants (BCY, USB, SIM, InTrust, CMC) use a different settings
            // shape and aren't wired to this generic URL/ProfileId/ClientId/Secret form.
        }
    }

    public async Task<LoginResult> LoginAsync(string userName, string password, string merchantId)
    {
        _logger.LogInformation("Login: {User} / {Merchant}", userName, merchantId);
        try
        {
            ApplySessionSettingsOverride(merchantId);
            var result = await _signClient.LoginAsync(userName, password, merchantId);
            return new LoginResult
            {
                Success = result.Success,
                UserName = result.UserName ?? userName,
                BearerToken = result.BearerToken,
                ErrorMessage = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed");
            return new LoginResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<List<BaseCertificateInfo>> GetCertificatesAsync(
        string userName, string bearerToken, string merchantId)
    {
        return await _signClient.GetCertificatesAsync(userName, bearerToken, merchantId: merchantId);
    }

    public List<SignatureFieldInfo> DetectFormFields(string pdfPath)
    {
        _logger.LogInformation("Detecting fields: {Path}", pdfPath);
        var fields = new List<SignatureFieldInfo>();
        try
        {
            using var reader = new iText.Kernel.Pdf.PdfReader(pdfPath);
            using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);
            var acroForm = iText.Forms.PdfAcroForm.GetAcroForm(pdfDoc, false);
            if (acroForm == null) return fields;

            foreach (var kvp in acroForm.GetFormFields())
            {
                if (kvp.Value is not iText.Forms.Fields.PdfSignatureFormField sigField) continue;
                var widgets = sigField.GetWidgets();
                if (widgets == null || widgets.Count == 0) continue;

                foreach (var widget in widgets)
                {
                    var page = widget.GetPage();
                    if (page == null) continue;
                    var rect = widget.GetRectangle()?.ToRectangle();
                    if (rect == null || rect.GetWidth() < 1) continue;

                    fields.Add(new SignatureFieldInfo
                    {
                        Id = kvp.Key,
                        Name = kvp.Key,
                        Page = pdfDoc.GetPageNumber(page),
                        X = rect.GetLeft(),
                        Y = page.GetPageSize().GetHeight() - rect.GetTop(),
                        Width = rect.GetWidth(),
                        Height = rect.GetHeight(),
                        IsSigned = sigField.GetValue() != null
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Field detection failed");
        }
        return fields;
    }

    public async Task<SigningResult> SignPdfAsync(PdfSigningRequest request)
    {
        _logger.LogInformation("SignPdf: {Path}, {Count} placements", request.FilePath, request.Placements.Count);

        try
        {
            var fileData = await File.ReadAllBytesAsync(request.FilePath);
            var base64Data = Convert.ToBase64String(fileData);
            int signed = 0;
            string? lastSignedUrl = null;

            foreach (var placement in request.Placements)
            {
                var sdkRequest = new SignDocumentRequest
                {
                    UserName = request.UserName,
                    CredentialID = request.CredentialId,
                    MerchantId = request.MerchantId,
                    FileName = Path.GetFileName(request.FilePath),
                    FileData = base64Data,
                    SignerName = request.SignerName,
                    SignerTitle = request.SignerTitle,
                    SignatureImage = request.SignatureImageBase64,
                    Page = placement.Page,
                    X = placement.X,
                    Y = placement.Y,
                    Width = placement.Width,
                    Height = placement.Height,
                };

                var result = await _signClient.SignDocumentAsync(sdkRequest);

                if (result.Success)
                {
                    signed++;
                    lastSignedUrl = result.SignedFileUrl;
                }
                else
                {
                    _logger.LogWarning("Sign placement failed: {Msg}", result.ErrorMessage);
                    return new SigningResult { Success = false, ErrorMessage = result.ErrorMessage, SignedCount = signed };
                }
            }

            return new SigningResult
            {
                Success = true,
                SignedCount = signed,
                OutputPath = lastSignedUrl ?? request.FilePath
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignPdf failed");
            return new SigningResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<SigningResult> SignXmlAsync(XmlSigningRequest request)
    {
        _logger.LogInformation("SignXml: {Path}", request.FilePath);
        try
        {
            var fileData = await File.ReadAllBytesAsync(request.FilePath);
            var sdkRequest = new SignDocumentRequest
            {
                UserName = request.UserName,
                CredentialID = request.CredentialId,
                MerchantId = request.MerchantId,
                FileName = Path.GetFileName(request.FilePath),
                FileData = Convert.ToBase64String(fileData),
            };

            var result = await _signClient.SignDocumentAsync(sdkRequest);
            if (result.Success)
            {
                return new SigningResult { Success = true, SignedCount = 1, OutputPath = result.SignedFileUrl };
            }

            return new SigningResult { Success = false, ErrorMessage = result.ErrorMessage };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignXml failed");
            return new SigningResult { Success = false, ErrorMessage = ex.Message };
        }
    }
}

public class LoginResult
{
    public bool Success { get; set; }
    public string? UserName { get; set; }
    public string? BearerToken { get; set; }
    public string? ErrorMessage { get; set; }
}
