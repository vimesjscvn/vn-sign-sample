using System.Text;
using System.Xml;
using Core.Config.Settings;
using Core.Common.Common;
using Newtonsoft.Json;
using VMSign.Web.Models;

#if USE_SDK_SOURCE
using SignSDK;
using Signature.API.ViewModels;
#else
using Vimes.SignSDK;
using Vimes.SignSDK.ViewModels;
#endif

using Signature.Domain.API;
using VMSign.Shared.Models;
using VMSign.Shared.Services;
using VMSign.Web.Controllers;
using Docnet.Core;
using Docnet.Core.Models;

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
    private readonly GeminiLayoutService _geminiService;
    private readonly LocalVisionLayoutService _localVisionService;

    public WebSigningService(
        ISignSDKClient signClient,
        ILogger<WebSigningService> logger,
        AppSettings appSettings,
        IHttpContextAccessor httpContextAccessor,
        GeminiLayoutService geminiService,
        LocalVisionLayoutService localVisionService)
    {
        _signClient = signClient;
        _logger = logger;
        _appSettings = appSettings;
        _httpContextAccessor = httpContextAccessor;
        _geminiService = geminiService;
        _localVisionService = localVisionService;
    }

    public IReadOnlyList<string> GetRegisteredMerchants()
    {
        return _signClient.GetRegisteredMerchants();
    }

    /// <summary>
    /// Best-effort "is this merchant usable" check, mirrored in sign-app's
    /// IsMerchantConfigured (MainWindow.axaml.cs) so both front-ends gate signing
    /// the same way. Local/USB merchants need no remote credentials so they're
    /// always considered configured.
    /// </summary>
    public bool IsMerchantConfigured(string merchantId)
    {
        var info = MerchantRegistry.GetDisplayInfo(merchantId);
        if (info.CertMode != MerchantCertMode.Remote) return true;

        return merchantId.ToUpperInvariant() switch
        {
            "VIETTEL" => !string.IsNullOrWhiteSpace(_appSettings.MySignSetting?.ClientId)
                         && !string.IsNullOrWhiteSpace(_appSettings.MySignSetting?.ClientSecret),
            "VNPT" => !string.IsNullOrWhiteSpace(_appSettings.SmartCASetting?.ClientId)
                      && !string.IsNullOrWhiteSpace(_appSettings.SmartCASetting?.ClientSecret),
            "BCY" => !string.IsNullOrWhiteSpace(_appSettings.TerminalSetting?.BaseUrl),
            "CMC" => !string.IsNullOrWhiteSpace(_appSettings.CmcSetting?.BaseUrl),
            "INTRUST" => !string.IsNullOrWhiteSpace(_appSettings.InTrustSetting?.BaseUrl),
            "SIM" => !string.IsNullOrWhiteSpace(_appSettings.MsspSetting?.ApId),
            _ => true
        };
    }

    /// <summary>
    /// Detects HOC_BA / TONG_KET / LY_LICH document shape and pre-fills SignTag/ParentXPath/
    /// ReferenceId signing options — ported from sign-app's btnAnalyzeXml_Click (MainWindow.axaml.cs)
    /// so both front-ends offer the same NEAC-safe defaults.
    /// </summary>
    public XmlAnalysisResult AnalyzeXmlDocument(string filePath)
    {
        var result = new XmlAnalysisResult();
        if (!File.Exists(filePath))
        {
            result.Success = false;
            result.ErrorMessage = $"Tệp không tồn tại: {filePath}";
            return result;
        }

        void Log(string level, string message) => result.Logs.Add(new XmlAnalysisLogEntry { Level = level, Message = message });

        try
        {
            var doc = new XmlDocument();
            doc.Load(filePath);
            var nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");

            Log("info", $"=== Phân tích: {Path.GetFileName(filePath)} ===");

            bool isHocBa = doc.SelectSingleNode("//*[local-name()='DANH_SACH_THONG_TIN_KY']") != null;
            bool isTongKet = doc.SelectSingleNode("//*[local-name()='TONG_KET_CA_NAM']") != null;
            bool isLyLich = doc.SelectSingleNode("//*[local-name()='THONG_TIN'][@Id='lyLich']") != null
                            || doc.SelectSingleNode("//*[local-name()='THONG_TIN' and @Id]") != null;

            result.DocumentType = isHocBa ? "HOC_BA" : isTongKet ? "TONG_KET" : isLyLich ? "LY_LICH" : "UNKNOWN";

            var idNodes = doc.SelectNodes("//*[@Id]");
            var seenIds = new HashSet<string>();
            var seenTags = new HashSet<string>();
            string? firstDataId = null;

            var refIds = new List<string> { "" };
            var knownTags = new List<string> { "", "CHUKYDONVI", "GVBM", "GVCN", "CBQL", "KY_PHAT_HANH" };
            var parentXPaths = new List<XmlParentXPathOption> { new() { XPath = "", Label = "(Không đặt — ký toàn tài liệu)" } };

            if (idNodes?.Count > 0)
            {
                Log("info", $"Phần tử có Id=\"...\" ({idNodes.Count} tìm thấy) — chọn làm ReferenceId:");
                foreach (XmlElement el in idNodes)
                {
                    string id = el.GetAttribute("Id");
                    if (el.LocalName == "Signature") continue;
                    Log("info", $"  <{el.LocalName} Id=\"{id}\">");

                    if (seenIds.Add(id)) refIds.Add(id);
                    if (seenTags.Add(el.LocalName) && !knownTags.Contains(el.LocalName))
                        knownTags.Add(el.LocalName);

                    if (firstDataId == null) firstDataId = id;
                }

                result.ReferenceIds = refIds;
                result.DefaultReferenceId = firstDataId ?? "";
            }
            else
            {
                Log("warn", "  Không tìm thấy phần tử nào có Id=\"...\" — ReferenceId để trống sẽ ký cả tài liệu.");
                result.ReferenceIds = refIds;
                result.DefaultReferenceId = "";
            }

            if (isHocBa)
            {
                Log("info", "Loại: HOC_BA — chọn XPath vị trí theo vai trò ký:");

                var xpathGvbm = "//*[local-name()='DANH_SACH_THONG_TIN_KY']//*[local-name()='GVBM'][not(*)][1]";
                var xpathGvcn = "//*[local-name()='DANH_SACH_THONG_TIN_KY']/*[local-name()='GVCN']";
                var xpathCbql = "//*[local-name()='PHAT_HANH_HOC_BA']//*[local-name()='CBQL'][not(*)][1]";
                var xpathKyPhatHanh = "//*[local-name()='PHAT_HANH_HOC_BA']/*[local-name()='KY_PHAT_HANH']";

                parentXPaths.Add(new() { XPath = xpathCbql, Label = "CBQL (cán bộ quản lý)", ReferenceId = "data" });
                parentXPaths.Add(new() { XPath = xpathKyPhatHanh, Label = "KY_PHAT_HANH (phát hành)", ReferenceId = "data" });
                parentXPaths.Add(new() { XPath = xpathGvcn, Label = "GVCN (giáo viên chủ nhiệm)", ReferenceId = "thongtinhocba" });
                parentXPaths.Add(new() { XPath = xpathGvbm, Label = "GVBM (giáo viên bộ môn — vị trí đầu tiên)" });

                var gvbmSlotNodes = doc.SelectNodes(
                    "//*[local-name()='DANH_SACH_THONG_TIN_KY']/*[local-name()='DANH_SACH_GVBM']/*[local-name()='GVBM']");
                var gvbmPairList = new List<(string teacherId, string diemId)>();
                if (gvbmSlotNodes?.Count > 0)
                {
                    foreach (XmlNode gvbmNode in gvbmSlotNodes)
                    {
                        if (gvbmNode is not XmlElement gvbmEl) continue;
                        string teacherId = gvbmEl.GetAttribute("Id");
                        if (string.IsNullOrEmpty(teacherId)) continue;
                        var diemEl = doc.SelectSingleNode(
                            $"//*[local-name()='DIEM_TONG_KET'][@Id][.//*[local-name()='GVBM'][@Id='{teacherId}']]") as XmlElement;
                        if (diemEl == null) continue;
                        string diemId = diemEl.GetAttribute("Id");
                        if (string.IsNullOrEmpty(diemId)) continue;
                        string xpathSlot = $"//*[local-name()='DANH_SACH_THONG_TIN_KY']/*[local-name()='DANH_SACH_GVBM']/*[local-name()='GVBM'][@Id='{teacherId}']";
                        parentXPaths.Add(new() { XPath = xpathSlot, Label = $"GVBM Id={teacherId} → RefID={diemId}", ReferenceId = diemId });
                        gvbmPairList.Add((teacherId, diemId));
                    }
                }

                result.ParentXPaths = parentXPaths;
                result.DefaultParentXPath = xpathCbql;
                result.SignTags = knownTags;
                result.DefaultSignTag = "CBQL";
                result.DefaultReferenceId = "data";

                Log("info", $"  CBQL (cán bộ quản lý)   : {xpathCbql}  → RefID=data  ← mặc định (NEAC-safe)");
                Log("info", $"  KY_PHAT_HANH (phát hành): {xpathKyPhatHanh}  → RefID=data");
                Log("info", $"  GVCN (chủ nhiệm)        : {xpathGvcn}  → RefID=thongtinhocba");
                Log("info", $"  GVBM (giáo viên bộ môn) : {xpathGvbm}  → RefID=Id học sinh (chọn cụ thể bên dưới)");
                if (gvbmPairList.Count > 0)
                {
                    Log("info", $"  GVBM cá nhân ({gvbmPairList.Count} cặp — chọn từ dropdown XPath, RefID tự động cập nhật):");
                    int gi = 1;
                    foreach (var (tId, dId) in gvbmPairList)
                        Log("info", $"    [{gi++}] GVBM Id={tId}  →  RefID={dId}");
                }
                Log("warn", "Lưu ý NEAC: Chữ ký phải đặt NGOÀI phần tử được tham chiếu. CBQL nằm ngoài DU_LIEU_HOC_BA (#data) → NEAC xác minh được.");
            }
            else if (isTongKet)
            {
                Log("info", "Loại: BCY (TONG_KET_CA_NAM) → Thẻ Ký để TRỐNG, ReferenceId = Id của TONG_KET_CA_NAM.");
                result.SignTags = knownTags;
                result.DefaultSignTag = "";
                result.ParentXPaths = parentXPaths;
                result.DefaultParentXPath = "";
            }
            else if (isLyLich)
            {
                Log("info", "Loại: Lý Lịch (THONG_TIN) → Thẻ Ký để TRỐNG, ReferenceId = Id của THONG_TIN.");
                result.SignTags = knownTags;
                result.DefaultSignTag = "";
                result.ParentXPaths = parentXPaths;
                result.DefaultParentXPath = "";
            }
            else
            {
                Log("warn", "Không nhận diện được loại tài liệu — tự chọn SignTag và ReferenceId phù hợp.");
                result.SignTags = knownTags;
                result.DefaultSignTag = knownTags.Count > 0 ? knownTags[0] : "";
                result.ParentXPaths = parentXPaths;
                result.DefaultParentXPath = "";
            }

            var existingSigs = doc.SelectNodes("//ds:Signature", nsmgr);
            result.ExistingSignatureCount = existingSigs?.Count ?? 0;
            if (existingSigs?.Count > 0)
            {
                Log("warn", $"Cảnh báo: tệp đã có {existingSigs.Count} chữ ký — ký lại sẽ THÊM chữ ký mới, không xóa chữ ký cũ.");
                foreach (XmlElement sig in existingSigs)
                {
                    string sigId = sig.GetAttribute("Id");
                    var signerNode = sig.SelectSingleNode(".//ds:X509SubjectName", nsmgr) as XmlElement;
                    string signerInfo = signerNode?.InnerText ?? "(không rõ)";
                    int cnIdx = signerInfo.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
                    if (cnIdx >= 0)
                    {
                        int start = cnIdx + 3;
                        int end = signerInfo.IndexOf(',', start);
                        signerInfo = end > start ? signerInfo.Substring(start, end - start) : signerInfo.Substring(start);
                    }
                    Log("warn", $"  Chữ ký [{(string.IsNullOrEmpty(sigId) ? "?" : sigId.Substring(0, Math.Min(8, sigId.Length)))}...] — người ký: {signerInfo}");
                }
            }
            else
            {
                Log("ok", "Chưa có chữ ký — tệp sẵn sàng ký.");
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AnalyzeXmlDocument failed");
            result.Success = false;
            result.ErrorMessage = $"Lỗi phân tích XML: {ex.Message}";
        }

        return result;
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
            case "BCY":
                if (!string.IsNullOrWhiteSpace(o.BaseUrl)) _appSettings.TerminalSetting.BaseUrl = o.BaseUrl;
                if (!string.IsNullOrWhiteSpace(o.RelyingParty)) _appSettings.TerminalSetting.RelyingParty = o.RelyingParty;
                if (!string.IsNullOrWhiteSpace(o.SignAlgorithm)) _appSettings.TerminalSetting.SignAlgorithm = o.SignAlgorithm;
                break;
            case "CMC":
                if (!string.IsNullOrWhiteSpace(o.BaseUrl)) _appSettings.CmcSetting.BaseUrl = o.BaseUrl;
                if (!string.IsNullOrWhiteSpace(o.SigningProfileId)) _appSettings.CmcSetting.SigningProfileId = o.SigningProfileId;
                if (!string.IsNullOrWhiteSpace(o.KeyAuth)) _appSettings.CmcSetting.KeyAuth = o.KeyAuth;
                break;
            case "INTRUST":
                if (!string.IsNullOrWhiteSpace(o.BaseUrl)) _appSettings.InTrustSetting.BaseUrl = o.BaseUrl;
                if (!string.IsNullOrWhiteSpace(o.BasicAuthorization)) _appSettings.InTrustSetting.BasicAuthorization = o.BasicAuthorization;
                break;
            case "SIM":
                // Mirrors sign-app: the "AP ID (URL)" field maps to MsspSetting.ApId, which
                // doubles as the MSSP endpoint URL for this merchant (not a separate BaseUrl).
                if (!string.IsNullOrWhiteSpace(o.BaseUrl)) _appSettings.MsspSetting.ApId = o.BaseUrl;
                if (!string.IsNullOrWhiteSpace(o.ApPassword)) _appSettings.MsspSetting.ApPassword = o.ApPassword;
                if (!string.IsNullOrWhiteSpace(o.MsspId)) _appSettings.MsspSetting.MsspId = o.MsspId;
                break;
            case "USB":
                if (!string.IsNullOrWhiteSpace(o.UsbAgentIp)) _appSettings.UsbSetting.UsbAgentIp = o.UsbAgentIp;
                if (o.UsbAgentPort.HasValue) _appSettings.UsbSetting.UsbAgentPort = o.UsbAgentPort.Value;
                if (!string.IsNullOrWhiteSpace(o.UsbAgentExePath)) _appSettings.UsbSetting.UsbAgentExePath = o.UsbAgentExePath;
                break;
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

    private byte[] RenderPdfPageToBmp(string pdfPath, int page)
    {
        using var library = DocLib.Instance;
        using var docReader = library.GetDocReader(pdfPath, new PageDimensions(1.0d));
        using var pageReader = docReader.GetPageReader(page - 1);
        var width = pageReader.GetPageWidth();
        var height = pageReader.GetPageHeight();
        var rawBytes = pageReader.GetImage(RenderFlags.RenderAnnotations | (RenderFlags)0x800);
        
        // BMP file header (14 bytes) + DIB header (40 bytes) = 54 bytes total
        int rowSize = width * 4;
        int imageSize = rowSize * height;
        int fileSize = 54 + imageSize;
        var bmp = new byte[fileSize];
        bmp[0] = 0x42; bmp[1] = 0x4D;
        BitConverter.GetBytes(fileSize).CopyTo(bmp, 2);
        BitConverter.GetBytes(54).CopyTo(bmp, 10);
        BitConverter.GetBytes(40).CopyTo(bmp, 14);
        BitConverter.GetBytes(width).CopyTo(bmp, 18);
        BitConverter.GetBytes(-height).CopyTo(bmp, 22);
        BitConverter.GetBytes((short)1).CopyTo(bmp, 26);
        BitConverter.GetBytes((short)32).CopyTo(bmp, 28);
        BitConverter.GetBytes(0).CopyTo(bmp, 30);
        BitConverter.GetBytes(imageSize).CopyTo(bmp, 34);
        Buffer.BlockCopy(rawBytes, 0, bmp, 54, imageSize);
        return bmp;
    }

    public List<TextSearchFieldCreator.CreatedFieldResult> AutoCreateSignatureFields(string pdfPath, bool runVisualGrounding = false)
    {
        var results = new List<TextSearchFieldCreator.CreatedFieldResult>();
        try
        {
            var autoCreateMode = _appSettings.InternalSetting?.AutoCreateAcroField ?? 0;
            if (autoCreateMode != (int)AutoCreateAcroFieldMode.On)
            {
                _logger.LogInformation("AutoCreateAcroField is disabled");
                return results;
            }

            var labelsJson = _appSettings.InternalSetting?.SignerLabels;
            if (string.IsNullOrEmpty(labelsJson))
            {
                _logger.LogWarning("SignerLabels configuration is empty");
                return results;
            }

            var labels = JsonConvert.DeserializeObject<List<TextSearchFieldCreator.SignerLabel>>(labelsJson);
            if (labels == null || labels.Count == 0)
            {
                _logger.LogWarning("SignerLabels could not be parsed or contains no items");
                return results;
            }

            // 1. Run algorithmic text search and field creation first (ultra-fast)
            results = TextSearchFieldCreator.CreateFieldsFromLabels(pdfPath, labels);

            // 2. Hybrid verification/adjustment with visual models if requested
            if (runVisualGrounding)
            {
                results = AlignFieldsVisually(pdfPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-create signature fields");
        }
        return results;
    }

    public List<TextSearchFieldCreator.CreatedFieldResult> AlignFieldsVisually(string pdfPath)
    {
        var results = new List<TextSearchFieldCreator.CreatedFieldResult>();
        try
        {
            var labelsJson = _appSettings.InternalSetting?.SignerLabels;
            if (string.IsNullOrEmpty(labelsJson)) return results;

            var labels = JsonConvert.DeserializeObject<List<TextSearchFieldCreator.SignerLabel>>(labelsJson);
            if (labels == null || labels.Count == 0) return results;

            // Detect existing fields (already placed by text search with collision avoidance)
            var existingFields = DetectFormFields(pdfPath);
            foreach (var f in existingFields)
            {
                results.Add(new TextSearchFieldCreator.CreatedFieldResult
                {
                    FieldName = f.Name,
                    Page = f.Page,
                    X = f.X,
                    Y = f.Y,
                    Width = f.Width,
                    Height = f.Height
                });
            }

            var verifyMode = _appSettings.InternalSetting?.VertexAiVerification ?? 0;
            bool useGemini = (verifyMode == 1);
            bool useLocalVision = _localVisionService.IsEnabled;

            if (useGemini || useLocalVision)
            {
                _logger.LogInformation("Visual AI scan started (Gemini: {useGemini}, Local: {useLocalVision}). Looking for missed labels...", useGemini, useLocalVision);

                int lastPage = 1;
                double pageWidth = 0;
                double pageHeight = 0;
                List<TextSearchFieldCreator.TextRect> textRects;

                using (var reader = new iText.Kernel.Pdf.PdfReader(pdfPath))
                using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader))
                {
                    lastPage = pdfDoc.GetNumberOfPages();
                    var pageObj = pdfDoc.GetPage(lastPage);
                    pageWidth = pageObj.GetPageSize().GetWidth();
                    pageHeight = pageObj.GetPageSize().GetHeight();
                    // Extract text rects for collision checking on AI-created fields
                    textRects = TextSearchFieldCreator.ExtractAllTextRects(pageObj);
                }

                byte[] bmpBytes = RenderPdfPageToBmp(pdfPath, lastPage);
                var aiBlocks = new List<LocalVisionLayoutService.DetectionResult>();
                if (useLocalVision)
                {
                    aiBlocks = _localVisionService.DetectSignatureBlocksAsync(bmpBytes).GetAwaiter().GetResult();
                }
                else if (useGemini)
                {
                    var geminiBlocks = _geminiService.DetectSignatureBlocksAsync(bmpBytes).GetAwaiter().GetResult();
                    foreach (var gb in geminiBlocks)
                    {
                        aiBlocks.Add(new LocalVisionLayoutService.DetectionResult
                        {
                            Label = gb.Label,
                            Box2d = gb.Box2d
                        });
                    }
                }

                if (aiBlocks != null && aiBlocks.Count > 0)
                {
                    _logger.LogInformation("AI detected {Count} signature blocks visually.", aiBlocks.Count);

                    // Only create fields for labels that are MISSING (not already placed by text search)
                    string tmpPath = pdfPath + ".tmp";
                    bool anyCreated = false;

                    using (var reader = new iText.Kernel.Pdf.PdfReader(pdfPath))
                    using (var writer = new iText.Kernel.Pdf.PdfWriter(tmpPath))
                    using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader, writer))
                    {
                        var acroForm = iText.Forms.PdfAcroForm.GetAcroForm(pdfDoc, true);
                        var pageObj = pdfDoc.GetPage(lastPage);

                        // Find the average Y of existing text-matched fields on this page for row alignment
                        var existingYValues = results
                            .Where(r => r.Page == lastPage && !r.WasSkipped)
                            .Select(r => (float)pageHeight - r.Y - r.Height)
                            .ToList();
                        
                        // Group existing fields into rows by proximity
                        var rowYGroups = new List<List<float>>();
                        foreach (var y in existingYValues)
                        {
                            bool placed = false;
                            foreach (var group in rowYGroups)
                            {
                                if (Math.Abs(y - group.Average()) < 50f)
                                {
                                    group.Add(y);
                                    placed = true;
                                    break;
                                }
                            }
                            if (!placed) rowYGroups.Add(new List<float> { y });
                        }

                        foreach (var block in aiBlocks)
                        {
                            var matchedLabel = labels.Find(l =>
                                l.Label.ToLower().Contains(block.Label.ToLower()) ||
                                block.Label.ToLower().Contains(l.Label.ToLower()));

                            string fieldName = matchedLabel?.FieldName ?? $"sig_ai_{Guid.NewGuid().ToString().Substring(0, 4)}";
                            float desiredWidth = matchedLabel?.Width ?? 150f;
                            float desiredHeight = matchedLabel?.Height ?? 60f;

                            // Skip if field already exists
                            if (acroForm.GetField(fieldName) != null)
                            {
                                _logger.LogInformation("Field '{FieldName}' already exists, preserving text-search position", fieldName);
                                continue;
                            }

                            // Calculate initial position from AI visual coordinates
                            float xmin = block.Box2d[1];
                            float xmax = block.Box2d[3];
                            float ymin = block.Box2d[0];
                            float ymax = block.Box2d[2];
                            float aiCenterX = (float)((xmin + xmax) / 2000f * pageWidth);
                            float aiCenterY = (float)((1000f - (ymin + ymax) / 2f) / 1000f * pageHeight);

                            float fieldX = aiCenterX - (desiredWidth / 2f);

                            // Try to align Y with the nearest existing row
                            float fieldY = aiCenterY - (desiredHeight / 2f);
                            foreach (var group in rowYGroups)
                            {
                                float rowAvgY = group.Average();
                                if (Math.Abs(fieldY - rowAvgY) < 60f)
                                {
                                    fieldY = rowAvgY;
                                    _logger.LogInformation("Aligned new field '{FieldName}' Y to existing row at {RowY}", fieldName, rowAvgY);
                                    break;
                                }
                            }

                            // Run collision-aware nudging
                            var (nudgedX, nudgedY) = TextSearchFieldCreator.NudgeToAvoidCollision(
                                fieldX, fieldY, desiredWidth, desiredHeight,
                                textRects, (float)pageWidth, (float)pageHeight);

                            var sigField = iText.Forms.Fields.PdfFormField.CreateSignature(pdfDoc,
                                new iText.Kernel.Geom.Rectangle(nudgedX, nudgedY, desiredWidth, desiredHeight));
                            sigField.SetFieldName(fieldName);
                            acroForm.AddField(sigField, pageObj);
                            anyCreated = true;

                            _logger.LogInformation("Auto-created missing field '{FieldName}' at collision-free position X={X}, Y={Y}", 
                                fieldName, nudgedX, nudgedY);

                            results.Add(new TextSearchFieldCreator.CreatedFieldResult
                            {
                                FieldName = fieldName,
                                Page = lastPage,
                                X = nudgedX,
                                Y = (float)pageHeight - (nudgedY + desiredHeight),
                                Width = desiredWidth,
                                Height = desiredHeight
                            });
                        }
                    }

                    if (anyCreated)
                    {
                        File.Delete(pdfPath);
                        File.Move(tmpPath, pdfPath);
                    }
                    else
                    {
                        if (File.Exists(tmpPath)) File.Delete(tmpPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed during visual alignment");
        }
        return results;
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
        ApplySessionSettingsOverride(request.MerchantId);

        try
        {
            var fileData = await File.ReadAllBytesAsync(request.FilePath);
            var base64Data = Convert.ToBase64String(fileData);
            int signed = 0;
            string? lastSignedUrl = null;
            var outputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory)
                ? Path.Combine(Path.GetDirectoryName(request.FilePath)!, "Signed")
                : request.OutputDirectory;
            Directory.CreateDirectory(outputDirectory);
            var normalizedOutputPath = Path.Combine(
                outputDirectory,
                $"{DateTime.Now:yyMMddHHmmss}_{Path.GetFileNameWithoutExtension(request.FilePath)}_signed.pdf");

            // Load the document once to get page heights
            var pageHeights = new Dictionary<int, float>();
            try
            {
                using var pdfReader = new iText.Kernel.Pdf.PdfReader(request.FilePath);
                using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(pdfReader);
                int pageCount = pdfDoc.GetNumberOfPages();
                for (int i = 1; i <= pageCount; i++)
                {
                    var page = pdfDoc.GetPage(i);
                    if (page != null)
                    {
                        pageHeights[i] = page.GetPageSize().GetHeight();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read PDF page heights for coordinate conversion.");
            }

            foreach (var placement in request.Placements)
            {
                float pageHeight = pageHeights.TryGetValue(placement.Page, out var h) ? h : 842f;
                // Convert screen Y (top-left) to PDF Y (bottom-left)
                float convertedY = pageHeight - placement.Y - placement.Height;
                float finalX = placement.X;
                float finalY = convertedY;

                // Only compensate if this is a custom drawn position (no pre-existing fieldId)
                if (string.IsNullOrEmpty(placement.FieldId))
                {
                    // SignPdfAsynchronous shifts the coordinates by -Width/2 and -Height/2.
                    // We add them here to compensate so the signature lands exactly on the drawn box bounds.
                    finalX = placement.X + (placement.Width / 2f);
                    finalY = convertedY + (placement.Height / 2f);
                }

                var sdkRequest = new SignDocumentRequest
                {
                    UserName = request.UserName,
                    CredentialID = request.CredentialId,
                    MerchantId = request.MerchantId,
                    BearerToken = request.BearerToken,
                    FileName = Path.GetFileName(request.FilePath),
                    FileData = base64Data,
                    SignerName = request.SignerName,
                    SignerTitle = request.SignerTitle,
                    SignerPosition = request.SignerPosition,
                    Note = request.Note,
                    IsShowSignatureTime = request.ShowTimestamp,
                    SignatureImage = request.SignatureImageBase64,
                    Page = placement.Page,
                    X = finalX,
                    Y = finalY,
                    Width = placement.Width,
                    Height = placement.Height,
                    SignatureId = placement.FieldId,
#if USE_SDK_SOURCE
                    SignatureType = request.SignatureType,
                    DisplayNameMode = request.DisplayNameMode
#else
                    SignatureType = (SignatureType)(request.SignatureType ?? 0),
                    DisplayNameMode = (DisplayNameMode)(request.DisplayNameMode ?? 0)
#endif
                };

                var batchRequest = new SignDocumentsRequest
                {
                    UserName = request.UserName,
                    CredentialID = request.CredentialId,
                    MerchantId = request.MerchantId,
                    BearerToken = request.BearerToken,
                    Pin = request.Pin,
                    Documents = new List<SignDocumentRequest> { sdkRequest }
                };

                var results = await _signClient.SignDocumentsAsync(batchRequest);
                var result = results?.FirstOrDefault();

                if (result == null)
                {
                    return new SigningResult
                    {
                        Success = false,
                        ErrorMessage = "Không nhận được phản hồi từ dịch vụ ký số.",
                        SignedCount = signed
                    };
                }

                if (result.Success)
                {
                    signed++;
                    lastSignedUrl = result.SignedFileUrl;

                    byte[] signedBytes;
                    if (!string.IsNullOrEmpty(lastSignedUrl) && File.Exists(lastSignedUrl))
                    {
                        signedBytes = await File.ReadAllBytesAsync(lastSignedUrl);
                    }
                    else if (!string.IsNullOrWhiteSpace(lastSignedUrl))
                    {
                        var base64Payload = lastSignedUrl.Contains(',')
                            ? lastSignedUrl[(lastSignedUrl.LastIndexOf(',') + 1)..]
                            : lastSignedUrl;
                        signedBytes = Convert.FromBase64String(base64Payload);
                    }
                    else
                    {
                        return new SigningResult
                        {
                            Success = false,
                            ErrorMessage = "MySign returned success without signed PDF data.",
                            SignedCount = signed - 1
                        };
                    }

                    // Normalize SDK file/base64 responses into one stable local PDF.
                    // Browser preview/download and subsequent placement stacking can
                    // then use the same path regardless of merchant response shape.
                    await File.WriteAllBytesAsync(normalizedOutputPath, signedBytes);
                    lastSignedUrl = normalizedOutputPath;
                    base64Data = Convert.ToBase64String(signedBytes);
                }
                else
                {
                    _logger.LogWarning("Sign placement failed: {Msg}", result.ErrorMessage);
                    return new SigningResult { Success = false, ErrorMessage = MapErrorMessage(result.ErrorMessage), SignedCount = signed };
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
        ApplySessionSettingsOverride(request.MerchantId);
        try
        {
            var fileContent = await File.ReadAllTextAsync(request.FilePath);
            var signatureName = string.IsNullOrWhiteSpace(request.SignatureName)
                ? "Signature_" + Guid.NewGuid().ToString().Substring(0, 8)
                : request.SignatureName;

            var xmlRequest = new XmlMultiSignRequest
            {
                UserName = request.UserName,
                CredentialID = request.CredentialId,
                MID = request.MerchantId,
                BearerToken = request.BearerToken,
                Pin = request.Pin,
                FileDatas = new List<XmlFileDataItem>
                {
                    new XmlFileDataItem
                    {
                        FileName = Path.GetFileName(request.FilePath),
                        XmlData = Convert.ToBase64String(Encoding.UTF8.GetBytes(fileContent)),
                        SignatureName = signatureName,
                        SignTag = string.IsNullOrWhiteSpace(request.SignTag) ? "CHUKYDONVI" : request.SignTag,
                        ReferenceId = request.ReferenceUri,
                        ParentXPath = request.ParentXPath
                    }
                }
            };

            var results = await _signClient.SignXmlDocumentsAsync(xmlRequest);
            var result = results?.FirstOrDefault();

            if (result == null)
            {
                return new SigningResult { Success = false, ErrorMessage = "Không nhận được phản hồi từ dịch vụ ký XML." };
            }

            if (!result.Success)
            {
                return new SigningResult { Success = false, ErrorMessage = MapErrorMessage(result.ErrorMessage) };
            }

            var signedBytes = Convert.FromBase64String(result.SignedXmlBase64);
            var outputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory)
                ? Path.Combine(Path.GetDirectoryName(request.FilePath)!, "Signed")
                : request.OutputDirectory;
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(
                outputDirectory,
                $"{DateTime.Now:yyMMddHHmmss}_{Path.GetFileName(request.FilePath)}");
            await File.WriteAllBytesAsync(outputPath, signedBytes);

            return new SigningResult { Success = true, SignedCount = 1, OutputPath = outputPath };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignXml failed");
            return new SigningResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private string MapErrorMessage(string? error)
    {
        if (string.IsNullOrEmpty(error)) return "Đã xảy ra lỗi không xác định.";
        if (error.Contains("Certificate not found") || error.Contains("Token không hợp lệ") || error.Contains("58038"))
        {
            return "Phiên làm việc với nhà cung cấp chữ ký số (Token) đã hết hạn hoặc không hợp lệ. Vui lòng đăng xuất và đăng nhập lại.";
        }
        return error;
    }
}

public class LoginResult
{
    public bool Success { get; set; }
    public string? UserName { get; set; }
    public string? BearerToken { get; set; }
    public string? ErrorMessage { get; set; }
}
