using Microsoft.AspNetCore.Mvc;
using VMSign.Shared.Services;
using VMSign.Web.Services;

namespace VMSign.Web.Controllers;

/// <summary>
/// Handles PDF and XML signing operations.
/// </summary>
public class SigningController : Controller
{
    private readonly WebSigningService _signingService;
    private readonly FileUploadService _fileService;

    public SigningController(WebSigningService signingService, FileUploadService fileService)
    {
        _signingService = signingService;
        _fileService = fileService;
    }

    /// <summary>
    /// Upload a PDF and detect form fields.
    /// Returns field coordinates for client-side overlay rendering.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> UploadPdf(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        var savedPath = await _fileService.SaveUploadedFileAsync(file);
        var fields = _signingService.DetectFormFields(savedPath);

        return Json(new
        {
            filePath = savedPath,
            fileName = file.FileName,
            fields
        });
    }

    /// <summary>
    /// Execute PDF signing with placed fields.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SignPdf([FromBody] PdfSigningRequest request)
    {
        var session = GetSessionState();
        var guard = new SigningGuard();
        var guardResult = guard.CanProceed(session, request.MerchantId);

        if (guardResult.Status != VMSign.Shared.Models.GuardStatus.Allowed)
        {
            return Json(new { success = false, guardStatus = guardResult.Status.ToString(), message = guardResult.Message });
        }

        // Inject session credentials
        var finalRequest = new PdfSigningRequest
        {
            FilePath = request.FilePath,
            OutputDirectory = request.OutputDirectory,
            MerchantId = request.MerchantId,
            // Explicit choice from the client (credential picker) wins over the session's
            // auto-selected default — a login can have multiple registered certificates.
            CredentialId = !string.IsNullOrEmpty(request.CredentialId) ? request.CredentialId : session.SelectedCredentialId,
            BearerToken = session.BearerToken,
            UserName = session.UserName,
            SignerName = request.SignerName,
            SignerTitle = request.SignerTitle,
            Note = request.Note,
            ShowTimestamp = request.ShowTimestamp,
            SignatureImageBase64 = request.SignatureImageBase64,
            Placements = request.Placements,
            SignAlgorithm = request.SignAlgorithm
        };

        var result = await _signingService.SignPdfAsync(finalRequest);
        return Json(result);
    }

    /// <summary>
    /// Upload XML and sign.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SignXml([FromBody] XmlSigningRequest request)
    {
        var session = GetSessionState();
        var guard = new SigningGuard();
        var guardResult = guard.CanProceed(session, request.MerchantId);

        if (guardResult.Status != VMSign.Shared.Models.GuardStatus.Allowed)
        {
            return Json(new { success = false, guardStatus = guardResult.Status.ToString(), message = guardResult.Message });
        }

        var result = await _signingService.SignXmlAsync(request);
        return Json(result);
    }

    /// <summary>
    /// Download a signed file.
    /// </summary>
    [HttpGet]
    public IActionResult Download(string path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            return NotFound();

        var fileName = Path.GetFileName(path);
        var contentType = path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "application/octet-stream";

        return PhysicalFile(path, contentType, fileName);
    }

    private SessionState GetSessionState()
    {
        return new SessionState
        {
            IsLoggedIn = HttpContext.Session.GetString("IsLoggedIn") == "true",
            UserName = HttpContext.Session.GetString("UserName"),
            BearerToken = HttpContext.Session.GetString("BearerToken"),
            ActiveMerchantId = HttpContext.Session.GetString("MerchantId"),
            SelectedCredentialId = HttpContext.Session.GetString("CredentialId"),
            IsConfigured = true // TODO: check actual config
        };
    }
}
