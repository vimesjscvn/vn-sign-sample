using Microsoft.Extensions.Logging;
using Vimes.SignSDK;
using Vimes.SignSDK.ViewModels;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Config.Settings;
using Signature.Domain.API;
using Core.Common.Common;

namespace WinFormsSample;

public partial class MainForm : Form
{
    private readonly ISignSDKClient _signClient;
    private readonly ILogger<MainForm> _logger;
    private string? _bearerToken;

    private readonly AppSettings _appSettings;
    private SignDocumentRequest _advancedRequest = new();
    private XmlMultiSignRequest _xmlRequest = new();
    
    // Custom signature image variable
    private string? _customSignatureImageBase64;
    
    // Canvas Mouse selection variables
    private bool _isDrawing = false;
    private Point _startPoint;
    private Rectangle _selectionRect = new Rectangle(100, 100, 150, 150);

    // Parsed coordinate points
    private int _sigX = 100;
    private int _sigY = 100;
    private int _sigW = 150;
    private int _sigH = 150;
    private int? _noteX = null;
    private int? _noteY = null;
    private int? _noteW = null;
    private int? _noteH = null;
    private bool _isDrawingNote = false;

    // Rendered PDF Page cache
    private Bitmap? _renderedPageImage;
    private int _activePageNum = 1;
    private int _totalPdfPages = 1;

    // Actual PDF page dimensions in PDF points (defaults to A4 until a file is loaded).
    private float _pageW = 595f;
    private float _pageH = 842f;

    public MainForm(ISignSDKClient signClient, ILogger<MainForm> logger, AppSettings appSettings)
    {
        _signClient = signClient;
        _logger = logger;
        _appSettings = appSettings;
        InitializeComponent();
        txtNoteX.TextChanged += (s, e) => { if (int.TryParse(txtNoteX.Text, out int x)) { _noteX = x; panelSigPlacementMock.Invalidate(); } else { _noteX = null; } };
        txtNoteY.TextChanged += (s, e) => { if (int.TryParse(txtNoteY.Text, out int y)) { _noteY = y; panelSigPlacementMock.Invalidate(); } else { _noteY = null; } };
        
        // Populate merchants
        var merchants = _signClient.GetRegisteredMerchants();
        cboMerchant.Items.AddRange(merchants.ToArray());
        if (cboMerchant.Items.Count > 0) cboMerchant.SelectedIndex = 0;

        // Populate Signature Type combobox with beautiful Vietnamese translations
        cboSignatureType.Items.AddRange(new object[] 
        {
            new ComboboxItem<SignatureType>("Mặc định (DEFAULT)", SignatureType.DEFAULT),
            new ComboboxItem<SignatureType>("Ký chính (PRIMARY)", SignatureType.PRIMARY),
            new ComboboxItem<SignatureType>("Ký phụ (SECONDARY)", SignatureType.SECONDARY),
            new ComboboxItem<SignatureType>("Ký thay (ON_BEHALFT_OF)", SignatureType.ON_BEHALFT_OF),
            new ComboboxItem<SignatureType>("Ký & Ghi chú (NOTE_SIG)", SignatureType.NOTE_AND_SIGNATURE),
            new ComboboxItem<SignatureType>("Chỉ ghi chú (NOTE)", SignatureType.NOTE)
        });
        cboSignatureType.SelectedIndex = 0;

        // Populate Display Name Mode combobox with beautiful Vietnamese translations
        cboDisplayNameMode.Items.AddRange(new object[] 
        {
            new ComboboxItem<DisplayNameMode>("Người ký & Ảnh chữ ký", DisplayNameMode.SignerWithImage),
            new ComboboxItem<DisplayNameMode>("Người ký, Phê duyệt & Ảnh", DisplayNameMode.SignerAndAuthorizerWithImage),
            new ComboboxItem<DisplayNameMode>("Chỉ ảnh chữ ký", DisplayNameMode.Image)
        });
        cboDisplayNameMode.SelectedIndex = 0;

        // Populate Sign Algorithm combobox (shown only for BCY)
        cboSignAlgorithm.Items.AddRange(new object[] { "ECDSA", "RSA" });
        cboSignAlgorithm.SelectedIndex = 0;

        // Bind settings to property grid based on initially selected merchant
        if (cboMerchant.SelectedItem != null)
        {
            cboMerchant_SelectedIndexChanged(cboMerchant, EventArgs.Empty);
        }
        else
        {
            pgSettings.SelectedObject = _appSettings;
        }

        // Initialize advanced request defaults
        _advancedRequest = new SignDocumentRequest 
        { 
            Page = 1, X = 100, Y = 100, Width = 150, Height = 150, 
            SignerName = "Sample User",
            FileName = "sample.pdf"
        };
        pgAdvancedRequest.SelectedObject = _advancedRequest;
        
        // Synchronize manual PropertyGrid edits back to visual mock canvas
        pgAdvancedRequest.PropertyValueChanged += (s, e) =>
        {
            if (_advancedRequest != null)
            {
                _sigX = (int)(_advancedRequest.X ?? 0);
                _sigY = (int)(_advancedRequest.Y ?? 0);
                _sigW = (int)(_advancedRequest.Width ?? 0);
                _sigH = (int)(_advancedRequest.Height ?? 0);
                panelSigPlacementMock.Invalidate();
            }
        };
        
        // Initialize XML request
        _xmlRequest = new XmlMultiSignRequest
        {
            FileDatas = new List<XmlFileDataItem> { new XmlFileDataItem { FileName = "sample.xml", XmlData = "PEV4YW1wbGU+RGF0YTwvRXhhbXBsZT4=" } }
        };
        // Configure grids styling
        SetupBatchFilesGridColumns();

        // Bidirectional synchronization for Username
        txtUserName.TextChanged += (s, e) => { if (txtUserNameXml.Text != txtUserName.Text) txtUserNameXml.Text = txtUserName.Text; };
        txtUserNameXml.TextChanged += (s, e) => { if (txtUserName.Text != txtUserNameXml.Text) txtUserName.Text = txtUserNameXml.Text; };

        // Bidirectional synchronization for Password
        txtPassword.TextChanged += (s, e) => { if (txtPasswordXml.Text != txtPassword.Text) txtPasswordXml.Text = txtPassword.Text; };
        txtPasswordXml.TextChanged += (s, e) => { if (txtPassword.Text != txtPasswordXml.Text) txtPassword.Text = txtPasswordXml.Text; };

        // Bidirectional synchronization for Certificates Combobox index selection
        cboCerts.SelectedIndexChanged += (s, e) => { if (cboCertsXml.SelectedIndex != cboCerts.SelectedIndex) cboCertsXml.SelectedIndex = cboCerts.SelectedIndex; };
        cboCertsXml.SelectedIndexChanged += (s, e) => { if (cboCerts.SelectedIndex != cboCertsXml.SelectedIndex) cboCerts.SelectedIndex = cboCertsXml.SelectedIndex; };

        // XML tab: tooltips
        toolTipXml.SetToolTip(txtXmlSignTag,
            "Tên thẻ XML chứa chữ ký (SignTag).\r\n" +
            "Ví dụ:\r\n" +
            "  CHUKYDONVI   → thẻ tùy chọn cho đơn vị\r\n" +
            "  KY_PHAT_HANH → slot phát hành HOC BA\r\n" +
            "  THONG_TIN    → dùng cho Lý Lịch");
        toolTipXml.SetToolTip(txtXmlReferenceId,
            "Giá trị Id=\"...\" của phần tử cần ký (Reference URI).\r\n" +
            "Để trống → ký toàn bộ tài liệu (URI=\"\").\r\n" +
            "Điền giá trị → ký theo phần tử cụ thể (URI=\"#id\").\r\n" +
            "Ví dụ:\r\n" +
            "  lyLich  → <THONG_TIN Id=\"lyLich\">\r\n" +
            "  data    → <DU_LIEU_HOC_BA Id=\"data\">");
        toolTipXml.SetToolTip(txtXmlSignatureName,
            "Tên định danh của chữ ký trong file XML.\r\n" +
            "Thường để hệ thống tự tạo (UUID). Không bắt buộc.");
        toolTipXml.AutoPopDelay = 10000;
        toolTipXml.InitialDelay = 400;

        // XML tab: help panel content
        rtbXmlHelp.SelectionFont = new System.Drawing.Font("Segoe UI Semibold", 8.5f, System.Drawing.FontStyle.Bold);
        rtbXmlHelp.SelectionColor = System.Drawing.Color.FromArgb(30, 64, 175);
        rtbXmlHelp.AppendText("Hướng dẫn điền thông tin\r\n");
        rtbXmlHelp.SelectionFont = new System.Drawing.Font("Segoe UI", 8.5f);
        rtbXmlHelp.SelectionColor = System.Drawing.Color.FromArgb(51, 65, 85);
        rtbXmlHelp.AppendText(
            "Thẻ Ký: tên thẻ XML chứa <Signature> (hover để xem ví dụ).\r\n" +
            "Reference ID: Id=\"...\" của phần tử cần ký; để trống = ký cả file.\r\n\r\n");
        rtbXmlHelp.SelectionFont = new System.Drawing.Font("Segoe UI Semibold", 8.5f, System.Drawing.FontStyle.Bold);
        rtbXmlHelp.SelectionColor = System.Drawing.Color.FromArgb(30, 64, 175);
        rtbXmlHelp.AppendText("Ví dụ nhanh:\r\n");
        rtbXmlHelp.SelectionFont = new System.Drawing.Font("Courier New", 8f);
        rtbXmlHelp.SelectionColor = System.Drawing.Color.FromArgb(51, 65, 85);
        rtbXmlHelp.AppendText(
            "  HOC BA – phát hành : SignTag=KY_PHAT_HANH   RefID=(trống)\r\n" +
            "  HOC BA – đơn vị    : SignTag=CHUKYDONVI     RefID=(trống)\r\n" +
            "  Lý Lịch            : SignTag=THONG_TIN       RefID=lyLich\r\n" +
            "  BHXH GIAMDINHHS    : SignTag=CHUKYDONVI     RefID=(trống)");

        Log("Dashboard Initialized. Welcome to Vimes SignSDK Showcase Studio!", Color.FromArgb(56, 189, 248));

        // Dynamically adjust splitter distances when form is shown maximized
        this.Load += (s, e) =>
        {
            try
            {
                // splitContainerDirect: left panel 45%, right panel 55% (PDF preview gets more space)
                if (splitContainerDirect.Width > 100)
                    splitContainerDirect.SplitterDistance = (int)(splitContainerDirect.Width * 0.45);

                // splitContainerDirectLeft: credentials+sig image 52%, config 48%
                if (splitContainerDirectLeft.Height > 100)
                    splitContainerDirectLeft.SplitterDistance = (int)(splitContainerDirectLeft.Height * 0.52);

                // splitContainerXml: left panel 45%, right panel 55%
                if (splitContainerXml.Width > 100)
                    splitContainerXml.SplitterDistance = (int)(splitContainerXml.Width * 0.45);

                // splitContainerXmlLeft: credentials 190, configuration remainder
                if (splitContainerXmlLeft.Height > 190)
                    splitContainerXmlLeft.SplitterDistance = 190;
            }
            catch { /* Ignore layout issues during init */ }
        };
    }

    #region Formatting & Logging Helpers
    private void Log(string message, Color? color = null)
    {
        if (txtLogs.InvokeRequired)
        {
            txtLogs.Invoke(new Action(() => Log(message, color)));
            return;
        }
        Color c = color ?? Color.FromArgb(148, 163, 184); // Slate grey default
        txtLogs.SelectionStart = txtLogs.TextLength;
        txtLogs.SelectionLength = 0;
        txtLogs.SelectionColor = c;
        txtLogs.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        txtLogs.SelectionColor = txtLogs.ForeColor;
        txtLogs.ScrollToCaret();
        _logger.LogInformation(message);
    }

    private void LogSuccess(string message) => Log(message, Color.FromArgb(16, 185, 129));
    private void LogWarning(string message) => Log(message, Color.FromArgb(245, 158, 11));
    private void LogError(string message) => Log(message, Color.FromArgb(239, 68, 68));
    private void LogSystem(string message) => Log(message, Color.FromArgb(56, 189, 248));
    #endregion

    #region Left Navigation Selection Panel
    private void btnNav_Click(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;

        if (btn == btnNavDirect) tabControl.SelectedIndex = 0;
        else if (btn == btnNavXml) tabControl.SelectedIndex = 2;
        else if (btn == btnNavSettings) tabControl.SelectedIndex = 3;

        // Reset and highlight active buttons
        var navButtons = new[] { btnNavDirect, btnNavXml, btnNavSettings };
        foreach (var nav in navButtons)
        {
            if (nav == btn)
            {
                nav.BackColor = Color.FromArgb(30, 41, 59); // highlighted slate background
                nav.ForeColor = Color.White;
            }
            else
            {
                nav.BackColor = Color.Transparent;
                nav.ForeColor = Color.FromArgb(148, 163, 184); // muted text color
            }
        }

        LogSystem($"Switched context view to: {btn.Text.Substring(3)}");
    }
    #endregion

    #region DataGridView Setups


    private void SetupBatchFilesGridColumns()
    {
        dgvBatchFiles.Columns.Clear();
        dgvBatchFiles.AutoGenerateColumns = false;

        dgvBatchFiles.Columns.Add(new DataGridViewCheckBoxColumn 
        { 
            Name = "colSelect", 
            HeaderText = "Sign?", 
            Width = 60,
            ReadOnly = false
        });
        dgvBatchFiles.Columns.Add(new DataGridViewTextBoxColumn 
        { 
            Name = "colFileName", 
            HeaderText = "File Name", 
            Width = 180,
            ReadOnly = true
        });
        dgvBatchFiles.Columns.Add(new DataGridViewTextBoxColumn 
        { 
            Name = "colSize", 
            HeaderText = "Size", 
            Width = 90,
            ReadOnly = true
        });
        dgvBatchFiles.Columns.Add(new DataGridViewTextBoxColumn 
        { 
            Name = "colStatus", 
            HeaderText = "Status / Outcome", 
            Width = 200,
            ReadOnly = true
        });
    }
    #endregion

    #region Tab 1: Identity & Credentials Flow
    private async void btnLogin_Click(object sender, EventArgs e)
    {
        try
        {
            string userName = txtUserName.Text;
            string password = txtPassword.Text;
            string merchantId = cboMerchant.SelectedItem?.ToString() ?? "";
            
            // Check for Local/USB CA details

            LogSystem($"Attempting authentication with [{merchantId}] as {userName}...");
            btnLogin.Enabled = false;
            btnLoginXml.Enabled = false;

            var result = await _signClient.LoginAsync(userName, password, merchantId, "", "");

            if (result.Success)
            {
                _bearerToken = result.BearerToken;
                LogSuccess("Authentication Successful. Session established.");
                
                // Color status bar indicators
                lblSessionStatus.Text = $"Active Session: {userName} ({merchantId})";
                lblSessionStatus.ForeColor = Color.FromArgb(16, 185, 129);
                panelStatusDot.BackColor = Color.FromArgb(16, 185, 129);

                LogSystem("Retrieving merchant registration certificates...");
                var certs = await _signClient.GetCertificatesAsync(userName, _bearerToken ?? "", merchantId: merchantId);
                
                PopulateCertificatesControls(certs, userName, merchantId);
            }
            else
            {
                LogError($"Authentication Failed: {result.ErrorMessage}");
                lblSessionStatus.Text = "Status: Authentication Failed";
                lblSessionStatus.ForeColor = Color.FromArgb(239, 68, 68);
                panelStatusDot.BackColor = Color.FromArgb(239, 68, 68);
            }
        }
        catch (Exception ex)
        {
            LogError($"Runtime Authentication Error: {ex.Message}");
        }
        finally
        {
            btnLogin.Enabled = true;
            btnLoginXml.Enabled = true;
        }
    }

    private async void btnSyncCertificates_Click(object sender, EventArgs e)
    {
        try
        {
            string userName = txtUserName.Text;
            string merchantId = cboMerchant.SelectedItem?.ToString() ?? "";

            LogWarning($"Bypassing caches. Requesting direct certificate registration retrieval from {merchantId} server...");
            btnSyncCertificates.Enabled = false;
            btnSyncCertificatesXml.Enabled = false;

            var certs = await _signClient.DownloadCertificatesAsync(userName, merchantId);
            PopulateCertificatesControls(certs, userName, merchantId);
            LogSuccess("Direct bypass certificate refresh completed.");
        }
        catch (Exception ex)
        {
            LogError($"Bypass Certificates Sync Error: {ex.Message}");
        }
        finally
        {
            btnSyncCertificates.Enabled = true;
            btnSyncCertificatesXml.Enabled = true;
        }
    }

    private void btnLoginXml_Click(object sender, EventArgs e)
    {
        btnLogin_Click(btnLogin, e);
    }

    private void btnSyncCertificatesXml_Click(object sender, EventArgs e)
    {
        btnSyncCertificates_Click(btnSyncCertificates, e);
    }

    private void PopulateCertificatesControls(List<BaseCertificateInfo>? certs, string userName, string merchantId)
    {
        cboCerts.Items.Clear();
        cboCertsXml.Items.Clear();

        if (certs != null && certs.Count > 0)
        {
            foreach (var cert in certs)
            {
                cboCerts.Items.Add(cert.credentialID);
                cboCertsXml.Items.Add(cert.credentialID);
            }
            
            cboCerts.SelectedIndex = 0;
            cboCertsXml.SelectedIndex = 0;
            LogSuccess($"Parsed and loaded {certs.Count} verified certificates into local registry.");

            // Sync with Advanced options
            if (_advancedRequest != null)
            {
                _advancedRequest.UserName = userName;
                _advancedRequest.MerchantId = merchantId;
                _advancedRequest.CredentialID = certs[0].credentialID;
                pgAdvancedRequest.Refresh();
                LogSystem("[Auto-Sync] Propagated credentials to Advanced PropertyGrid configuration.");
            }
        }
        else
        {
            LogWarning("Authentication returned zero active certificates or scopes.");
        }
    }




    #endregion

    #region Tab 2: Quick PDF Sign & Coordinate Canvas
    private void btnBrowse_Click(object sender, EventArgs e)
    {
        using (OpenFileDialog ofd = new OpenFileDialog())
        {
            ofd.Filter = "PDF Documents (*.pdf)|*.pdf";
            ofd.Multiselect = true;
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                lstFilePath.Items.Clear();
                lstFilePath.Items.AddRange(ofd.FileNames);
                if (lstFilePath.Items.Count > 0)
                    lstFilePath.SelectedIndex = 0;

                txtAdvancedFilePath.Text = ofd.FileNames[0]; // auto-sync first file
                
                if (ofd.FileNames.Length > 1)
                {
                    LogSystem($"Target PDFs Loaded: {ofd.FileNames.Length} files.");
                }
                else
                {
                    LogSystem($"Target PDF Loaded: {Path.GetFileName(ofd.FileName)} ({new FileInfo(ofd.FileName).Length / 1024} KB)");
                }
                _activePageNum = 1;
                RenderPdfPage();
            }
        }
    }

    private void lstFilePath_SelectedIndexChanged(object sender, EventArgs e)
    {
        _activePageNum = 1;
        RenderPdfPage();
    }

    private async void btnSign_Click(object sender, EventArgs e)
    {
        btnSign.Enabled = false;
        try
        {
            string[] filePaths = lstFilePath.Items.Cast<string>().ToArray();

            if (filePaths.Length == 0)
            {
                LogError("No PDF file selected or specified.");
                return;
            }

            string? lastSignedPath = null;
            int successCount = 0;

            foreach (var filePath in filePaths)
            {
                LogSystem($"Processing signing for: {Path.GetFileName(filePath)}");
                string? signedPath = await SignSingleFile(filePath, isQuickSign: true);
                if (!string.IsNullOrEmpty(signedPath) && File.Exists(signedPath))
                {
                    successCount++;
                    lastSignedPath = signedPath;
                }
            }

            if (successCount > 0 && !string.IsNullOrEmpty(lastSignedPath))
            {
                LogSuccess($"Signature execution finished successfully for {successCount} file(s).");
                
                // Clear selection box so it doesn't overlap the new signature
                _sigW = 0;
                _sigH = 0;
                _noteW = 0;
                _noteH = 0;
                _selectionRect = Rectangle.Empty;
                
                // Update file path textboxes to reflect the signed file paths
                lstFilePath.Items.Clear();
                if (filePaths.Length > 1)
                {
                    var signedPaths = filePaths.Select(fp => {
                        var parentDir = Path.GetDirectoryName(fp)!;
                        var outFolder = string.Equals(Path.GetFileName(parentDir), "Signed", StringComparison.OrdinalIgnoreCase)
                            ? parentDir
                            : Path.Combine(parentDir, "Signed");
                        return Path.Combine(outFolder, Path.GetFileName(fp));
                    }).ToArray();
                    
                    lstFilePath.Items.AddRange(signedPaths);
                    lstFilePath.SelectedIndex = 0;
                    txtAdvancedFilePath.Text = lastSignedPath;
                }
                else
                {
                    lstFilePath.Items.Add(lastSignedPath!);
                    lstFilePath.SelectedIndex = 0;
                    txtAdvancedFilePath.Text = lastSignedPath;
                }
                
                // Render and load the last signed PDF file in the interactive viewer
                RenderPdfPage();
            }
        }
        finally
        {
            btnSign.Enabled = true;
        }
    }

    private async Task<string?> SignSingleFile(string filePath, bool isQuickSign)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                LogError("Invalid operation: Target PDF file is missing.");
                return null;
            }

            if (cboCerts.SelectedItem == null)
            {
                LogError("Invalid operation: No certificate selected in the registry.");
                return null;
            }

            string credentialId = cboCerts.SelectedItem.ToString()!;
            string merchantId = cboMerchant.SelectedItem?.ToString() ?? "";
            string userName = txtUserName.Text;

            LogSystem($"Constructing signature request for {Path.GetFileName(filePath)}...");
            
            var fileBytes = await File.ReadAllBytesAsync(filePath);
            var base64 = Convert.ToBase64String(fileBytes);

            var request = new SignDocumentRequest
            {
                FileName = Path.GetFileName(filePath),
                FileData = base64,
                SignerName = txtSignerName.Text,
                SignerTitle = txtSignerTitle.Text,
                Note = txtNote.Text,
                SignatureType = ((ComboboxItem<SignatureType>)cboSignatureType.SelectedItem).Value,
                DisplayNameMode = ((ComboboxItem<DisplayNameMode>)cboDisplayNameMode.SelectedItem).Value,
                IsShowSignatureTime = chkShowSignatureTime.Checked,
                SignerPosition = "Visual Placement",
                Page = _activePageNum,
                X = _sigX,
                Y = _sigY,
                Width = _sigW,
                Height = _sigH,
                NotePointX = _noteX,
                NotePointY = _noteY,
                SignatureImage = _customSignatureImageBase64,
                SignAlgorithm = cboSignAlgorithm.Visible ? cboSignAlgorithm.SelectedItem?.ToString() : null
            };

            var batchRequest = new SignDocumentsRequest
            {
                UserName = userName,
                CredentialID = credentialId,
                MerchantId = merchantId,
                Documents = new List<SignDocumentRequest> { request }
            };

            LogSystem($"[BEFORE SIGN] Request → Page={request.Page}, X={request.X}, Y={request.Y}, W={request.Width}, H={request.Height} (PDF lower-left origin)");
            LogSystem("Invoking SignSDK client signing workflow...");
            
            var results = await _signClient.SignDocumentsAsync(batchRequest);
            var result = results?.FirstOrDefault();
            if (result == null)
            {
                LogError("Signature failed: No response from server.");
                return null;
            }

            if (result.Success)
            {
                LogSuccess($"Signature Registered! Server Transaction ID: {result.TransactionId}");
                
                var parentDir = Path.GetDirectoryName(filePath)!;
                var outFolder = string.Equals(Path.GetFileName(parentDir), "Signed", StringComparison.OrdinalIgnoreCase)
                    ? parentDir
                    : Path.Combine(parentDir, "Signed");
                if (!Directory.Exists(outFolder)) Directory.CreateDirectory(outFolder);
                
                var outPath = Path.Combine(outFolder, Path.GetFileName(filePath));
                byte[] signedBytes;
                
                if (!string.IsNullOrEmpty(result.SignedFileUrl) && File.Exists(result.SignedFileUrl))
                {
                    signedBytes = await File.ReadAllBytesAsync(result.SignedFileUrl);
                }
                else
                {
                    string base64Data = result.SignedFileUrl.Contains(",") ? result.SignedFileUrl.Split(',').Last() : result.SignedFileUrl;
                    signedBytes = Convert.FromBase64String(base64Data);
                }
                
                await File.WriteAllBytesAsync(outPath, signedBytes);
                LogSuccess($"Signed output deployed successfully to: {outPath}");
                LogActualSignaturePosition(outPath, request);
                return outPath;
            }
            else
            {
                LogError($"Signature Rejected by Remote Gateway: {result.ErrorMessage}");
                if (!string.IsNullOrEmpty(result.ErrorCode))
                {
                    LogError($"Programmatic Error Code: {result.ErrorCode}");
                }
                return null;
            }
        }
        catch (Exception ex)
        {
            LogError($"Signature Generation Exception: {ex.Message}");
            return null;
        }
    }

    private void LogActualSignaturePosition(string signedPdfPath, SignDocumentRequest request)
    {
        try
        {
            using var reader = new iText.Kernel.Pdf.PdfReader(signedPdfPath);
            using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);
            var sigUtil = new iText.Signatures.SignatureUtil(pdfDoc);
            var sigNames = sigUtil.GetSignatureNames();
            if (sigNames == null || sigNames.Count == 0)
            {
                LogWarning("[AFTER SIGN] No signature widgets found in signed PDF.");
                return;
            }
            var latest = sigNames[sigNames.Count - 1];
            var acro = iText.Forms.PdfAcroForm.GetAcroForm(pdfDoc, false);
            var field = acro?.GetField(latest);
            var widgets = field?.GetWidgets();
            if (widgets == null || widgets.Count == 0)
            {
                LogWarning($"[AFTER SIGN] Signature '{latest}' has no widget rectangle (invisible signature).");
                return;
            }
            iText.Kernel.Geom.Rectangle first = null;
            foreach (var w in widgets)
            {
                var r = w.GetRectangle().ToRectangle();
                int page = pdfDoc.GetPageNumber(w.GetPage());
                if (first == null) first = r;
                LogSystem($"[AFTER SIGN] Placement → Page={page}, X={r.GetLeft():0.##}, Y={r.GetBottom():0.##}, W={r.GetWidth():0.##}, H={r.GetHeight():0.##} (field '{latest}')");
            }
            float dx = (float)(first.GetLeft() - (request.X ?? 0));
            float dy = (float)(first.GetBottom() - (request.Y ?? 0));
            float dw = (float)(first.GetWidth() - (request.Width ?? 0));
            float dh = (float)(first.GetHeight() - (request.Height ?? 0));
            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5 && Math.Abs(dw) < 0.5 && Math.Abs(dh) < 0.5)
                LogSuccess("[DELTA] Placement matches request exactly (within 0.5 pt).");
            else
                LogWarning($"[DELTA] ΔX={dx:+0.##;-0.##;0}, ΔY={dy:+0.##;-0.##;0}, ΔW={dw:+0.##;-0.##;0}, ΔH={dh:+0.##;-0.##;0}");
        }
        catch (Exception ex)
        {
            LogWarning($"[AFTER SIGN] Could not read signature positions: {ex.Message}");
        }
    }

    private void btnBrowseSigImage_Click(object sender, EventArgs e)
    {
        using (OpenFileDialog ofd = new OpenFileDialog())
        {
            ofd.Filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                pbSigImage.Image = Image.FromFile(ofd.FileName);
                var imgBytes = File.ReadAllBytes(ofd.FileName);
                _customSignatureImageBase64 = Convert.ToBase64String(imgBytes);
                LogSystem($"Imported custom signature image: {Path.GetFileName(ofd.FileName)} ({imgBytes.Length} bytes)");
                panelSigPlacementMock.Invalidate(); // trigger mock repaint
            }
        }
    }

    private void btnClearSigImage_Click(object sender, EventArgs e)
    {
        pbSigImage.Image = null;
        _customSignatureImageBase64 = null;
        LogSystem("Reset to default merchant/SDK system signature visual representation.");
        panelSigPlacementMock.Invalidate();
    }



    private Rectangle GetMockPaperRect()
    {
        int canvasW = panelSigPlacementMock.Width;
        int canvasH = panelSigPlacementMock.Height;

        // Use 15px padding
        int padding = 15;
        int maxW = canvasW - (padding * 2);
        int maxH = canvasH - (padding * 2);

        if (maxW <= 0 || maxH <= 0) return Rectangle.Empty;

        float targetRatio = _pageW / _pageH;
        int paperW, paperH;

        if (maxW / targetRatio <= maxH)
        {
            paperW = maxW;
            paperH = (int)(maxW / targetRatio);
        }
        else
        {
            paperH = maxH;
            paperW = (int)(maxH * targetRatio);
        }

        int paperX = (canvasW - paperW) / 2;
        int paperY = (canvasH - paperH) / 2;

        return new Rectangle(paperX, paperY, paperW, paperH);
    }

    private void panelSigPlacementMock_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        var paperRect = GetMockPaperRect();
        if (paperRect.IsEmpty) return;

        // Restrict start coordinates to be inside the paper sheet bounds
        int startX = Math.Max(paperRect.X, Math.Min(e.X, paperRect.Right));
        int startY = Math.Max(paperRect.Y, Math.Min(e.Y, paperRect.Bottom));

        _isDrawing = true;
        _startPoint = new Point(startX, startY);
        _isDrawingNote = rbPositionNote.Checked;
        _selectionRect = new Rectangle(startX, startY, 0, 0);
        panelSigPlacementMock.Invalidate();
    }

    private void panelSigPlacementMock_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing) return;

        var paperRect = GetMockPaperRect();
        if (paperRect.IsEmpty) return;

        // Constrain current coordinate inside the paper bounds
        int currX = Math.Max(paperRect.X, Math.Min(e.X, paperRect.Right));
        int currY = Math.Max(paperRect.Y, Math.Min(e.Y, paperRect.Bottom));

        int x = Math.Min(_startPoint.X, currX);
        int y = Math.Min(_startPoint.Y, currY);
        int w = Math.Abs(_startPoint.X - currX);
        int h = Math.Abs(_startPoint.Y - currY);

        _selectionRect = new Rectangle(x, y, w, h);

        // Convert selection to PDF points in real-time
        float relativeX = _selectionRect.X - paperRect.X;
        float relativeY = _selectionRect.Y - paperRect.Y;
        
        int pageW = (int)_pageW;
        int pageH = (int)_pageH;

                int pdfX = (int)((relativeX / paperRect.Width) * _pageW);
        int pdfW = (int)((_selectionRect.Width / (float)paperRect.Width) * _pageW);
        int pdfH = (int)((_selectionRect.Height / (float)paperRect.Height) * _pageH);
        int pdfY = pageH - (int)((relativeY / paperRect.Height) * _pageH) - pdfH;

        if (pdfX < 0) pdfX = 0;
        if (pdfW < 0) pdfW = 0;
        if (pdfX + pdfW > pageW) pdfW = pageW - pdfX;

        if (pdfH < 0) pdfH = 0;
        if (pdfY < 0) pdfY = 0;
        if (pdfY + pdfH > pageH)
        {
            pdfY = pageH - pdfH;
            if (pdfY < 0)
            {
                pdfY = 0;
                pdfH = pageH;
            }
        }

        if (_isDrawingNote) {
            _noteX = pdfX;
            _noteY = pdfY;
            _noteW = pdfW;
            _noteH = pdfH;
        } else {
            _sigX = pdfX;
            _sigY = pdfY;
            _sigW = pdfW;
            _sigH = pdfH;
        }
        panelSigPlacementMock.Invalidate();
    }

    private void panelSigPlacementMock_MouseUp(object sender, MouseEventArgs e)
    {
        if (!_isDrawing) return;
        _isDrawing = false;
        
        if (_isDrawingNote)
        {
            if (_noteW < 10) _noteW = 100;
            if (_noteH < 10) _noteH = 24;
            txtNoteX.Text = _noteX.ToString();
            txtNoteY.Text = _noteY.ToString();
            LogSystem($"Note Box Selection Captured: X:{_noteX}, Y:{_noteY}, W:{_noteW}, H:{_noteH} (PDF points)");
        }
        else
        {
            // Ensure final bounds check and validation
            if (_sigW < 10) _sigW = 100;
            if (_sigH < 10) _sigH = 100;
            
            // Update both simple variables and the property grid object
            _advancedRequest.X = _sigX;
            _advancedRequest.Y = _sigY;
            _advancedRequest.Width = _sigW;
            _advancedRequest.Height = _sigH;
            pgAdvancedRequest.Refresh();

            LogSystem($"Canvas Box Selection Captured: X:{_sigX}, Y:{_sigY}, W:{_sigW}, H:{_sigH} (PDF points)");
        }

        panelSigPlacementMock.Invalidate();
    }

    private void panelSigPlacementMock_Paint(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Clear canvas with beautiful light workspace background
        g.Clear(Color.FromArgb(241, 245, 249));

        var paperRect = GetMockPaperRect();
        if (paperRect.IsEmpty) return;

        // 1. Draw Paper Shadow (slightly offset grey rect)        // 1. Draw Paper Shadow (slightly offset grey rect)
        var shadowRect = new Rectangle(paperRect.X + 3, paperRect.Y + 3, paperRect.Width, paperRect.Height);
        var shadowBrush = new SolidBrush(Color.FromArgb(200, 226, 232, 240));
        g.FillRectangle(shadowBrush, shadowRect);

        // 2. Draw Paper Base
        g.FillRectangle(Brushes.White, paperRect);

        if (_renderedPageImage != null)
        {
            g.DrawImage(_renderedPageImage, paperRect);
        }
        else
        {
            // 3. Draw Beautiful High-Fidelity Mock PDF lines (fallback)
            var linePen = new Pen(Color.FromArgb(241, 245, 249), 2);
            var darkLinePen = new Pen(Color.FromArgb(226, 232, 240), 3);
            
            // Draw mock header title
            g.DrawLine(darkLinePen, paperRect.X + 20, paperRect.Y + 20, paperRect.X + 80, paperRect.Y + 20);
            
            // Draw mock body text blocks
            int currentY = paperRect.Y + 40;
            int endY = paperRect.Bottom - 20;
            while (currentY < endY)
            {
                g.DrawLine(linePen, paperRect.X + 20, currentY, paperRect.Right - 20, currentY);
                currentY += 12;
            }
        }

        // Draw page outline border
        g.DrawRectangle(new Pen(Color.FromArgb(148, 163, 184), 1), paperRect);

        // 4. Map and Draw Signature Box
        if (_sigW > 0 && _sigH > 0)
        {
            float pxX = paperRect.X + (_sigX / _pageW) * paperRect.Width;
            float pxY = paperRect.Y + ((_pageH - _sigY - _sigH) / _pageH) * paperRect.Height;
            float pxW = (_sigW / _pageW) * paperRect.Width;
            float pxH = (_sigH / _pageH) * paperRect.Height;

            var placementRect = new RectangleF(pxX, pxY, pxW, pxH);
            if (placementRect.Width < 5) placementRect.Width = 5;
            if (placementRect.Height < 5) placementRect.Height = 5;

            var fillBrush = new SolidBrush(Color.FromArgb(40, 43, 87, 154)); 
            g.FillRectangle(fillBrush, placementRect);

            var borderPen = new Pen(Color.FromArgb(43, 87, 154), 1.5f);
            borderPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
            g.DrawRectangle(borderPen, Rectangle.Round(placementRect));

            var font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);
            var textBrush = new SolidBrush(Color.FromArgb(43, 87, 154));
            
            if (pbSigImage.Image != null)
            {
                g.DrawImage(pbSigImage.Image, placementRect);
            }
            else
            {
                string label = "Signature Field";
                var size = g.MeasureString(label, font);
                if (placementRect.Width >= size.Width && placementRect.Height >= size.Height)
                    g.DrawString(label, font, textBrush, placementRect.X + (placementRect.Width - size.Width) / 2, placementRect.Y + (placementRect.Height - size.Height) / 2);
                else
                    g.DrawString("✍️", font, textBrush, placementRect.X + 2, placementRect.Y + 2);
            }
        }

        // 5. Map and Draw Note Box
        if (_noteW > 0 && _noteH > 0)
        {
            float pxX = paperRect.X + (_noteX.Value / _pageW) * paperRect.Width;
            float pxY = paperRect.Y + ((_pageH - _noteY.Value - _noteH.Value) / _pageH) * paperRect.Height;
            float pxW = (_noteW.Value / _pageW) * paperRect.Width;
            float pxH = (_noteH.Value / _pageH) * paperRect.Height;

            var placementRect = new RectangleF(pxX, pxY, pxW, pxH);
            if (placementRect.Width < 5) placementRect.Width = 5;
            if (placementRect.Height < 5) placementRect.Height = 5;

            var fillBrush = new SolidBrush(Color.FromArgb(40, 245, 158, 11)); // Amber/Orange
            g.FillRectangle(fillBrush, placementRect);

            var borderPen = new Pen(Color.FromArgb(245, 158, 11), 1.5f);
            borderPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
            g.DrawRectangle(borderPen, Rectangle.Round(placementRect));

            var font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);
            var textBrush = new SolidBrush(Color.FromArgb(180, 83, 9));
            
            string label = "Note Field";
            var size = g.MeasureString(label, font);
            if (placementRect.Width >= size.Width && placementRect.Height >= size.Height)
                g.DrawString(label, font, textBrush, placementRect.X + (placementRect.Width - size.Width) / 2, placementRect.Y + (placementRect.Height - size.Height) / 2);
            else
                g.DrawString("📝", font, textBrush, placementRect.X + 2, placementRect.Y + 2);
        }

        // 7. Draw coordinates indicator flag
        var flagFont = new Font("Segoe UI", 7F);
        var flagBrush = new SolidBrush(Color.FromArgb(15, 23, 42));
        g.DrawString($"Sig: {_sigX}, {_sigY} ({_sigW}x{_sigH}) | Note: {_noteX}, {_noteY} ({_noteW}x{_noteH})", flagFont, flagBrush, paperRect.X + 5, paperRect.Bottom - 16);
    }
    private void btnPrevPage_Click(object sender, EventArgs e)
    {
        if (_activePageNum > 1)
        {
            _activePageNum--;
            RenderPdfPage();
        }
    }

    private void btnNextPage_Click(object sender, EventArgs e)
    {
        if (_activePageNum < _totalPdfPages)
        {
            _activePageNum++;
            RenderPdfPage();
        }
    }

    private void RenderPdfPage()
    {
        try
        {
            string pdfPath = lstFilePath.SelectedItem?.ToString() ?? "";

            if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
            {
                _renderedPageImage = null;
                panelSigPlacementMock.Invalidate();
                return;
            }

            int pageIndex = _activePageNum - 1;
            if (pageIndex < 0) pageIndex = 0;

            using (var library = Docnet.Core.DocLib.Instance)
            {
                // Render at native PDF dimensions (1.0 = 1 PDF point per pixel) so the
                // canvas matches the exact coordinate system the signer will write into.
                using (var docReader = library.GetDocReader(pdfPath, new Docnet.Core.Models.PageDimensions(1.0d)))
                {
                    _totalPdfPages = docReader.GetPageCount();
                    if (_activePageNum > _totalPdfPages)
                    {
                        _activePageNum = _totalPdfPages;
                        pageIndex = _activePageNum - 1;
                    }

                    lblPreviewMock.Text = $"Page {_activePageNum} of {_totalPdfPages}";

                    using (var pageReader = docReader.GetPageReader(pageIndex))
                    {
                        var width = pageReader.GetPageWidth();
                        var height = pageReader.GetPageHeight();
                        _pageW = width;
                        _pageH = height;
                        var rawBytes = pageReader.GetImage(Docnet.Core.Models.RenderFlags.RenderAnnotations); // Bgra32 byte array with visual signatures rendered!

                        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        var bmpData = bitmap.LockBits(
                            new Rectangle(0, 0, width, height),
                            System.Drawing.Imaging.ImageLockMode.WriteOnly,
                            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                        System.Runtime.InteropServices.Marshal.Copy(rawBytes, 0, bmpData.Scan0, rawBytes.Length);
                        bitmap.UnlockBits(bmpData);

                        // Cache previous image so we can cleanly dispose it
                        var prevImage = _renderedPageImage;
                        _renderedPageImage = bitmap;
                        prevImage?.Dispose();

                        LogSystem($"Rendered actual PDF Page {_activePageNum}/{_totalPdfPages} successfully!");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogWarning($"Note: Render actual PDF content skipped/failed (using fallback design layout): {ex.Message}");
            _renderedPageImage = null;
        }

        panelSigPlacementMock.Invalidate();
    }
    #endregion

    #region Tab 3: Advanced PDF signing (PropertyGrid integration)
    private void btnBrowseAdvanced_Click(object sender, EventArgs e)
    {
        using (OpenFileDialog ofd = new OpenFileDialog())
        {
            ofd.Filter = "PDF Documents (*.pdf)|*.pdf";
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                txtAdvancedFilePath.Text = ofd.FileName;
                LogSystem($"Advanced PDF Target Set: {Path.GetFileName(ofd.FileName)}");
            }
        }
    }

    private async void btnSignAdvanced_Click(object sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(txtAdvancedFilePath.Text) || !File.Exists(txtAdvancedFilePath.Text))
            {
                LogError("Invalid Operation: Please select a valid target PDF file.");
                return;
            }

            if (cboCerts.SelectedItem == null)
            {
                LogError("Invalid Operation: Select a certificate from the registry first.");
                return;
            }

            btnSignAdvanced.Enabled = false;
            
            _advancedRequest.FileName = Path.GetFileName(txtAdvancedFilePath.Text);
            
            var fileBytes = await File.ReadAllBytesAsync(txtAdvancedFilePath.Text);
            _advancedRequest.FileData = Convert.ToBase64String(fileBytes);

            var batchRequest = new SignDocumentsRequest
            {
                UserName = txtUserName.Text,
                CredentialID = cboCerts.SelectedItem.ToString()!,
                MerchantId = cboMerchant.SelectedItem?.ToString() ?? "",
                Documents = new List<SignDocumentRequest> { _advancedRequest }
            };

            LogSystem($"Initiating Advanced Property-Driven Signing for {_advancedRequest.FileName}...");
            var results = await _signClient.SignDocumentsAsync(batchRequest);
            var result = results?.FirstOrDefault();
            
            if (result == null)
            {
                LogError("Signature failed: No response from server.");
                return;
            }

            if (result.Success)
            {
                LogSuccess($"Advanced Signature Completed! Transaction: {result.TransactionId}");
                var outPath = Path.Combine(Path.GetDirectoryName(txtAdvancedFilePath.Text)!, "Signed_Advanced_" + _advancedRequest.FileName);
                byte[] signedBytes;
                
                if (!string.IsNullOrEmpty(result.SignedFileUrl) && File.Exists(result.SignedFileUrl))
                {
                    signedBytes = await File.ReadAllBytesAsync(result.SignedFileUrl);
                }
                else
                {
                    string base64Data = result.SignedFileUrl.Contains(",") ? result.SignedFileUrl.Split(',').Last() : result.SignedFileUrl;
                    signedBytes = Convert.FromBase64String(base64Data);
                }
                
                await File.WriteAllBytesAsync(outPath, signedBytes);
                LogSuccess($"Advanced output deployed successfully: {outPath}");
            }
            else
            {
                LogError($"Advanced Signature Rejected: {result.ErrorMessage}");
                if (!string.IsNullOrEmpty(result.ErrorCode))
                {
                    LogError($"Programmatic Gateway Code: {result.ErrorCode}");
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Advanced Signing Engine Runtime Error: {ex.Message}");
        }
        finally
        {
            btnSignAdvanced.Enabled = true;
        }
    }
    #endregion

    #region Tab 4: XML Signing Preview (Mock showcase)
    private string _selectedXmlFilePath = "";
    private string _xmlFileContent = "";

    private void btnBrowseXml_Click(object sender, EventArgs e)
    {
        using (OpenFileDialog ofd = new OpenFileDialog())
        {
            ofd.Filter = "XML Files (*.xml)|*.xml";
            ofd.Multiselect = true;
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                lstXmlFilePath.Items.Clear();
                lstXmlFilePath.Items.AddRange(ofd.FileNames);
                if (lstXmlFilePath.Items.Count > 0)
                {
                    lstXmlFilePath.SelectedIndex = 0;
                }

                if (ofd.FileNames.Length > 1)
                {
                    LogSystem($"Loaded XML Files: {ofd.FileNames.Length} files.");
                }
                else if (ofd.FileNames.Length == 1)
                {
                    LogSystem($"Loaded XML File: {Path.GetFileName(ofd.FileName)} ({new FileInfo(ofd.FileName).Length / 1024.0:F2} KB)");
                }
            }
        }
    }

    private void lstXmlFilePath_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (lstXmlFilePath.SelectedItem != null)
        {
            string filePath = lstXmlFilePath.SelectedItem.ToString()!;
            if (File.Exists(filePath))
            {
                try
                {
                    _selectedXmlFilePath = filePath;
                    _xmlFileContent = File.ReadAllText(filePath);
                    rtbXmlPreview.Text = _xmlFileContent;

                    // Parse some basic info to populate text fields as defaults
                    txtXmlSignatureName.Text = "Signature_" + Guid.NewGuid().ToString().Substring(0, 8);
                }
                catch (Exception ex)
                {
                    LogError($"Failed to read XML file: {ex.Message}");
                }
            }
        }
    }

    private async void btnSignXml_Click(object sender, EventArgs e)
    {
        if (lstXmlFilePath.Items.Count == 0)
        {
            LogError("Vui lòng chọn tệp XML cần ký trước.");
            return;
        }

        if (cboCertsXml.SelectedItem == null)
        {
            LogError("Vui lòng đăng nhập và chọn chứng thư số trước khi ký.");
            return;
        }

        try
        {
            btnSignXml.Enabled = false;

            string userName = txtUserNameXml.Text;
            string credentialId = cboCertsXml.SelectedItem.ToString()!;
            string merchantId = cboMerchant.SelectedItem?.ToString() ?? "";

            string signTag = txtXmlSignTag.Text;
            string? referenceId = string.IsNullOrWhiteSpace(txtXmlReferenceId.Text) ? null : txtXmlReferenceId.Text.Trim();

            var fileDatas = new List<XmlFileDataItem>();
            foreach (var item in lstXmlFilePath.Items)
            {
                string filePath = item.ToString()!;
                if (File.Exists(filePath))
                {
                    string fileContent = await File.ReadAllTextAsync(filePath);
                    string signatureName = "Signature_" + Guid.NewGuid().ToString().Substring(0, 8);
                    fileDatas.Add(new XmlFileDataItem
                    {
                        FileName = Path.GetFileName(filePath),
                        XmlData = Convert.ToBase64String(Encoding.UTF8.GetBytes(fileContent)),
                        SignTag = signTag,
                        SignatureName = signatureName,
                        ReferenceId = referenceId
                    });
                }
            }

            LogSystem($"Packaging {fileDatas.Count} XML file(s) for signing...");
            LogSystem($"Parameters -> SignTag: '{signTag}', ReferenceId: '{referenceId ?? "(whole doc)"}'");
            LogSystem("Invoking SignSDK client XML signing workflow...");

            var request = new XmlMultiSignRequest
            {
                UserName = userName,
                CredentialID = credentialId,
                MID = merchantId,
                FileDatas = fileDatas,
                SignAlgorithm = null  // SDK auto-detects ECDSA vs RSA from the certificate
            };

            var results = await _signClient.SignXmlDocumentsAsync(request);
            if (results == null || results.Count == 0)
            {
                LogError("Signature failed: No response from XML signing engine.");
                return;
            }

            int successCount = 0;
            string? lastSignedXml = null;
            string? lastSignedPath = null;

            foreach (var result in results)
            {
                // Find matching original file path from ListBox
                string? originalFilePath = null;
                foreach (var item in lstXmlFilePath.Items)
                {
                    string path = item.ToString()!;
                    if (Path.GetFileName(path) == result.FileName)
                    {
                        originalFilePath = path;
                        break;
                    }
                }

                if (originalFilePath == null)
                {
                    // Fallback to first if there's only 1
                    if (lstXmlFilePath.Items.Count == 1)
                    {
                        originalFilePath = lstXmlFilePath.Items[0].ToString();
                    }
                }

                if (result.Success && originalFilePath != null)
                {
                    successCount++;
                    byte[] signedBytes = Convert.FromBase64String(result.SignedXmlBase64);
                    string signedXml = Encoding.UTF8.GetString(signedBytes);

                    string directory = Path.GetDirectoryName(originalFilePath) ?? "";
                    string baseName = Path.GetFileNameWithoutExtension(originalFilePath);
                    string signedPath = Path.Combine(directory, $"{baseName}_signed.xml");

                    await File.WriteAllBytesAsync(signedPath, signedBytes);
                    
                    lastSignedXml = signedXml;
                    lastSignedPath = signedPath;

                    LogSuccess($"Ký XML thành công! File: {result.FileName} -> {Path.GetFileName(signedPath)}");
                }
                else
                {
                    LogError($"Ký XML thất bại cho file: {result.FileName}. Lỗi: {result.ErrorMessage}");
                }
            }

            if (successCount > 0 && lastSignedXml != null && lastSignedPath != null)
            {
                rtbXmlPreview.Text = lastSignedXml;
                if (successCount == lstXmlFilePath.Items.Count)
                {
                    LogSuccess($"Đã ký thành công toàn bộ {successCount} tệp XML!");
                }
                else
                {
                    LogWarning($"Đã ký thành công {successCount}/{lstXmlFilePath.Items.Count} tệp XML.");
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Lỗi trong quá trình ký XML: {ex.Message}");
        }
        finally
        {
            btnSignXml.Enabled = true;
        }
    }
    #endregion

    #region Tab 5: Native Batch PDF Sign Integration (High Performance)
    private void btnBrowseBatch_Click(object sender, EventArgs e)
    {
        using (FolderBrowserDialog fbd = new FolderBrowserDialog())
        {
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                txtBatchFolder.Text = fbd.SelectedPath;
                LogSystem($"Selected Batch Source Directory: {fbd.SelectedPath}");
                LoadBatchDirectoryFiles();
            }
        }
    }

    private void btnBrowseBatchCert_Click(object sender, EventArgs e)
    {
        using (OpenFileDialog ofd = new OpenFileDialog())
        {
            ofd.Filter = "Certificate Files (*.p12;*.pfx)|*.p12;*.pfx";
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                txtBatchCertPath.Text = ofd.FileName;
                LogSystem($"Batch certificate path set: {Path.GetFileName(ofd.FileName)}");
            }
        }
    }

    private void btnBrowseBatchOutput_Click(object sender, EventArgs e)
    {
        using (FolderBrowserDialog fbd = new FolderBrowserDialog())
        {
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                txtBatchOutput.Text = fbd.SelectedPath;
                LogSystem($"Batch output deploy folder: {fbd.SelectedPath}");
            }
        }
    }

    private void LoadBatchDirectoryFiles()
    {
        dgvBatchFiles.Rows.Clear();
        string path = txtBatchFolder.Text;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        var files = Directory.GetFiles(path, "*.pdf");
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            // Default check = true, filename, size, status = Pending
            dgvBatchFiles.Rows.Add(true, Path.GetFileName(file), $"{info.Length / 1024} KB", "Ready", file);
        }

        LogSystem($"Identified {files.Length} target PDFs inside the folder structure.");
    }

    private void btnSelectAllBatch_Click(object sender, EventArgs e)
    {
        foreach (DataGridViewRow row in dgvBatchFiles.Rows)
        {
            row.Cells["colSelect"].Value = true;
        }
    }

    private void btnClearAllBatch_Click(object sender, EventArgs e)
    {
        foreach (DataGridViewRow row in dgvBatchFiles.Rows)
        {
            row.Cells["colSelect"].Value = false;
        }
    }

    private async void btnBatchSign_Click(object sender, EventArgs e)
    {
        try
        {
            if (dgvBatchFiles.Rows.Count == 0)
            {
                LogError("No target documents identified for batch processing.");
                return;
            }

            var selectedRows = dgvBatchFiles.Rows.Cast<DataGridViewRow>()
                .Where(r => r.Cells["colSelect"].Value != null && (bool)r.Cells["colSelect"].Value == true)
                .ToList();

            if (selectedRows.Count == 0)
            {
                LogError("No documents selected. Check at least one document in the grid.");
                return;
            }

            if (cboCerts.SelectedItem == null)
            {
                LogError("Please discover certificates and select one in the active registry.");
                return;
            }

            btnBatchSign.Enabled = false;
            progressBar.Value = 0;
            progressBar.Maximum = selectedRows.Count;
            lblBatchStatus.Text = $"Processing 0 / {selectedRows.Count}...";

            string credentialId = cboCerts.SelectedItem.ToString()!;
            string merchantId = cboMerchant.SelectedItem?.ToString() ?? "";
            string userName = txtUserName.Text;

            // Retrieve batch configurations
            string outputDir = txtBatchOutput.Text;
            if (string.IsNullOrEmpty(outputDir))
            {
                outputDir = Path.Combine(txtBatchFolder.Text, "Batch_Signed");
                txtBatchOutput.Text = outputDir;
                LogWarning($"No explicit output directory defined. Defaulting to subdirectory: {outputDir}");
            }

            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            LogSystem($"Constructing high-level native batch request for {selectedRows.Count} documents...");
            
            // Build the native batch envelope request
            var batchRequest = new SignDocumentsRequest
            {
                UserName = userName,
                CredentialID = credentialId,
                MerchantId = merchantId,
                OutputFolder = outputDir,
                UserSecret = txtBatchUserSecret.Text,
                CertFileName = txtBatchCertPath.Text,
                CertPassword = txtBatchCertPass.Text,
                HospitalName = "General Hospital", // SCA Identity parameter mapping
                CompanyName = "Vimes Systems", // Message caption mapping
                Documents = new List<SignDocumentRequest>()
            };

            // Package each checked file into the native request
            var rowFileMap = new Dictionary<string, DataGridViewRow>();
            foreach (var row in selectedRows)
            {
                string filename = row.Cells["colFileName"].Value.ToString()!;
                string fullPath = row.Cells[4].Value.ToString()!; // absolute path column
                
                var fileBytes = await File.ReadAllBytesAsync(fullPath);
                var base64 = Convert.ToBase64String(fileBytes);

                var docRequest = new SignDocumentRequest
                {
                    FileName = filename,
                    FileData = base64,
                    SignerName = txtSignerName.Text,
                    SignerTitle = txtSignerTitle.Text,
                    SignerPosition = "Batch Placement",
                    Page = 1,
                    X = _sigX,
                    Y = _sigY,
                    Width = _sigW,
                    Height = _sigH,
                    SignatureImage = _customSignatureImageBase64
                };

                batchRequest.Documents.Add(docRequest);
                rowFileMap[filename] = row;
                row.Cells["colStatus"].Value = "Preparing...";
            }

            LogSystem("Dispatching native multi-file signing call to the SDK Core...");
            
            // Execute native batch API in background thread to prevent UI freezing
            var results = await Task.Run(async () => await _signClient.SignDocumentsAsync(batchRequest));

            LogSystem("Native multi-file signing completed. Analyzing individual outcomes...");

            int successCount = 0;
            foreach (var res in results)
            {
                if (rowFileMap.TryGetValue(res.FileName, out var targetRow))
                {
                    if (res.Success)
                    {
                        successCount++;
                        targetRow.Cells["colStatus"].Value = "SIGNED (Emerald)";
                        targetRow.DefaultCellStyle.ForeColor = Color.FromArgb(16, 185, 129);
                        LogSuccess($"[Batch Item Success] File: {res.FileName} | Tx: {res.TransactionId}");
                    }
                    else
                    {
                        targetRow.Cells["colStatus"].Value = $"Error: {res.ErrorMessage}";
                        targetRow.DefaultCellStyle.ForeColor = Color.FromArgb(239, 68, 68);
                        LogError($"[Batch Item Failed] File: {res.FileName} | Msg: {res.ErrorMessage}");
                        if (!string.IsNullOrEmpty(res.ErrorCode))
                        {
                            LogError($"[Batch Item Failed] Code: {res.ErrorCode}");
                        }
                    }
                }
                progressBar.Value = Math.Min(progressBar.Value + 1, progressBar.Maximum);
                lblBatchStatus.Text = $"Processed {progressBar.Value} / {selectedRows.Count}";
            }

            LogSuccess($"Batch signing execution finished. Successful operations: {successCount} / {selectedRows.Count}");
            lblBatchStatus.Text = $"Completed! Success: {successCount}/{selectedRows.Count}";
        }
        catch (Exception ex)
        {
            LogError($"Native Batch Processing Runtime Failure: {ex.Message}");
        }
        finally
        {
            btnBatchSign.Enabled = true;
        }
    }
    #endregion

    #region Tab 6: Settings Preservation
    private void btnSaveSettings_Click(object sender, EventArgs e)
    {
        try
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            
            if (!File.Exists(configPath))
            {
                configPath = "appsettings.json";
            }

            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                var jObj = Newtonsoft.Json.Linq.JObject.Parse(json);
                
                var settingsJson = Newtonsoft.Json.JsonConvert.SerializeObject(_appSettings);
                var settingsObj = Newtonsoft.Json.Linq.JObject.Parse(settingsJson);
                
                var appSettingsWrapper = new Newtonsoft.Json.Linq.JObject();
                appSettingsWrapper["AppSettings"] = settingsObj;
                
                jObj.Merge(appSettingsWrapper, new Newtonsoft.Json.Linq.JsonMergeSettings
                {
                    MergeArrayHandling = Newtonsoft.Json.Linq.MergeArrayHandling.Union,
                    MergeNullValueHandling = Newtonsoft.Json.Linq.MergeNullValueHandling.Merge
                });
                
                string finalJson = jObj.ToString(Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(configPath, finalJson);
                LogSuccess($"Active settings successfully saved to: {configPath}");

                // Save back to project original source configuration file
                try
                {
                    string sourcePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\appsettings.json"));
                    if (File.Exists(sourcePath))
                    {
                        File.WriteAllText(sourcePath, finalJson);
                        LogSystem($"Updated project original source copy config: {sourcePath}");
                    }
                }
                catch (Exception srcEx)
                {
                    LogWarning($"Could not update original project source copy config: {srcEx.Message}");
                }

                MessageBox.Show($"Settings configurations synchronized successfully to:{Environment.NewLine}{configPath}", 
                    "Settings Preserved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                var settingsJson = Newtonsoft.Json.JsonConvert.SerializeObject(_appSettings, Newtonsoft.Json.Formatting.Indented);
                var appSettingsWrapper = new Newtonsoft.Json.Linq.JObject();
                appSettingsWrapper["AppSettings"] = Newtonsoft.Json.Linq.JObject.Parse(settingsJson);
                File.WriteAllText(configPath, appSettingsWrapper.ToString(Newtonsoft.Json.Formatting.Indented));
                LogSuccess("Created brand new configurations file structures.");
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to synchronize and save settings: {ex.Message}");
        }
    }
    #endregion

    #region Console Logging Cleansers
    private void btnClearLogs_Click(object sender, EventArgs e)
    {
        txtLogs.Clear();
        LogSystem("Console logs cleared.");
    }

    private void btnCopyLogs_Click(object sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(txtLogs.Text))
        {
            Clipboard.SetText(txtLogs.Text);
            LogSystem("Console output copied to system clipboard buffers.");
        }
    }
    #endregion

    #region Combobox SelectedIndex Changed Events & Syncs
    private void cboMerchant_SelectedIndexChanged(object? sender, EventArgs e)
    {
        string selectedMerchant = cboMerchant.SelectedItem?.ToString() ?? "";
        if (string.IsNullOrEmpty(selectedMerchant)) return;

        LogSystem($"Merchant Switched: Active gateway shifted to: [{selectedMerchant}]");

        if (_advancedRequest != null)
        {
            _advancedRequest.MerchantId = selectedMerchant;
            pgAdvancedRequest.Refresh();
        }

        if (_xmlRequest != null)
        {
            _xmlRequest.MID = selectedMerchant;
        }

        // Adaptive Credentials fields based on selected merchant
        bool isLocalOrUsb = selectedMerchant.Equals("LOCAL", StringComparison.OrdinalIgnoreCase) || 
                            selectedMerchant.Equals("USB", StringComparison.OrdinalIgnoreCase) ||
                            selectedMerchant.Equals("SELF", StringComparison.OrdinalIgnoreCase);

        if (isLocalOrUsb)
        {
            txtUserName.Text = "N/A (Local CA)";
            txtPassword.Text = "";
            txtUserName.Enabled = false;
            txtPassword.Enabled = false;
            txtUserNameXml.Text = "N/A (Local CA)";
            txtPasswordXml.Text = "";
            txtUserNameXml.Enabled = false;
            txtPasswordXml.Enabled = false;
            // Enable Local/USB CA fields
            // Sync with Batch Local CA fields
            txtBatchCertPath.Text = "";
            txtBatchCertPass.Text = "";

            LogWarning($"[{selectedMerchant} Selected] Local keys enabled. Select your certificate and provide passwords.");
        }
        else
        {
            txtUserName.Text = "";
            txtPassword.Text = "";
            txtUserName.Enabled = true;
            txtPassword.Enabled = true;
            txtUserNameXml.Text = "";
            txtPasswordXml.Text = "";
            txtUserNameXml.Enabled = true;
            txtPasswordXml.Enabled = true;

            // Disable Local/USB CA fields

            LogSystem($"[{selectedMerchant} Selected] Cloud CA enabled. Username & Password input required.");
        }

        // Dynamically bind settings property grid to the active merchant configuration
        object? selectedSetting = null;
        switch (selectedMerchant.ToUpperInvariant())
        {
            case "VIETTEL":
            case "MYSIGN":
                selectedSetting = _appSettings.MySignSetting;
                break;
            case "BCY":
                selectedSetting = _appSettings.TerminalSetting;
                break;
            case "VNPT":
            case "VNPT_NEW":
            case "SMARTCA":
                selectedSetting = _appSettings.SmartCASetting;
                break;
            case "SOFTDREAM":
                selectedSetting = _appSettings.SoftdreamSetting;
                break;
            case "LOCAL":
            case "SELF":
                selectedSetting = _appSettings.CertificateSetting;
                break;
            case "SIM":
                selectedSetting = _appSettings.MsspSetting;
                break;
            case "INTRUST":
                selectedSetting = _appSettings.InTrustSetting;
                break;
            case "CMC":
                selectedSetting = _appSettings.CmcSetting;
                break;
            case "USB":
                selectedSetting = _appSettings.UsbSetting;
                break;
            default:
                selectedSetting = _appSettings; // Fallback to all settings
                break;
        }

        pgSettings.SelectedObject = selectedSetting;
        pgSettings.Refresh();

        bool isBcy = string.Equals(selectedMerchant, "BCY", StringComparison.OrdinalIgnoreCase);
        lblSignAlgorithm.Visible = isBcy;
        cboSignAlgorithm.Visible = isBcy;
    }

    private void cboCerts_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (cboCerts.SelectedItem != null && _advancedRequest != null)
        {
            _advancedRequest.CredentialID = cboCerts.SelectedItem.ToString();
            pgAdvancedRequest.Refresh();
            LogSystem($"[Sync] Advanced Request Credential ID updated to: {_advancedRequest.CredentialID}");
        }
    }
    #endregion

    private class ComboboxItem<T>
    {
        public string Text { get; set; }
        public T Value { get; set; }

        public ComboboxItem(string text, T value)
        {
            Text = text;
            Value = value;
        }

        public override string ToString()
        {
            return Text;
        }
    }
}






