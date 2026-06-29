using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vimes.SignSDK;
using Vimes.SignSDK.ViewModels;
using Signature.Domain.API;
using Core.Common.Common;
using Core.Config.Settings;

namespace VimesSignSample;

public partial class MainWindow : Window
{
    private readonly ISignSDKClient _signClient;
    private readonly ILogger<MainWindow> _logger;
    private string? _bearerToken;
    private string? _activeUserName;
    private System.Diagnostics.Process? _agentProcess;

    private readonly AppSettings _appSettings;
    private SignDocumentRequest _advancedRequest = new();
    
    // Custom signature image variable
    private string? _customSignatureImageBase64;
    
    // Canvas Mouse selection variables
    private bool _isDrawing = false;
    private Point _startPoint;
    private Rect _selectionRect;

    // Selected coordinates
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
    private WriteableBitmap? _renderedPageImage;
    private int _activePageNum = 1;
    private int _totalPdfPages = 1;

    // Actual PDF page dimensions in PDF points (defaults to A4 until a file is loaded).
    private float _pageW = 595f;
    private float _pageH = 842f;

    // HOC_BA: maps each XPath dropdown entry to its recommended ReferenceId.
    private readonly Dictionary<string, string> _xpathRefMap = new();

    public MainWindow()
    {
        // Parameterless constructor for designer preview
        InitializeComponent();
        _signClient = null!;
        _logger = null!;
        _appSettings = null!;
    }

    public MainWindow(ISignSDKClient signClient, ILogger<MainWindow> logger, AppSettings appSettings)
    {
        InitializeComponent();
        _signClient = signClient;
        _logger = logger;
        _appSettings = appSettings;

        InitializeApp();
    }

    private void InitializeApp()
    {
        // Populate merchants
        var merchants = _signClient.GetRegisteredMerchants();
        cboMerchant.ItemsSource = merchants;
        if (merchants.Count > 0)
        {
            cboMerchant.SelectedIndex = 0;
        }

        cboMerchant.SelectionChanged += cboMerchant_SelectionChanged;

        // Populate Signature Type combobox
        cboSignatureType.ItemsSource = new List<ComboboxItem<SignatureType>>
        {
            new("Mặc định (DEFAULT)", SignatureType.DEFAULT),
            new("Ký chính (PRIMARY)", SignatureType.PRIMARY),
            new("Ký phụ (SECONDARY)", SignatureType.SECONDARY),
            new("Ký thay (ON_BEHALFT_OF)", SignatureType.ON_BEHALFT_OF),
            new("Ký & Ghi chú (NOTE_SIG)", SignatureType.NOTE_AND_SIGNATURE),
            new("Chỉ ghi chú (NOTE)", SignatureType.NOTE)
        };
        cboSignatureType.SelectedIndex = 0;

        // Populate Display Name Mode combobox
        cboDisplayNameMode.ItemsSource = new List<ComboboxItem<DisplayNameMode>>
        {
            new("Người ký & Ảnh chữ ký", DisplayNameMode.SignerWithImage),
            new("Người ký, Phê duyệt & Ảnh", DisplayNameMode.SignerAndAuthorizerWithImage),
            new("Chỉ ảnh chữ ký", DisplayNameMode.Image)
        };
        cboDisplayNameMode.SelectedIndex = 0;

        // Populate Sign Algorithm combobox (shown only for BCY)
        cboSignAlgorithm.ItemsSource = new List<string> { "ECDSA", "RSA" };
        cboSignAlgorithm.SelectedIndex = 0;

        // Load initial settings UI
        LoadSettingsToUi();

        // Initialize advanced request defaults
        _advancedRequest = new SignDocumentRequest 
        { 
            Page = 1, X = 100, Y = 100, Width = 150, Height = 150, 
            SignerName = "Sample User",
            FileName = "sample.pdf"
        };

        // Populate default XML options
        cboXmlSignTag.ItemsSource = new List<string> { "", "CHUKYDONVI", "GVBM", "GVCN", "CBQL", "KY_PHAT_HANH" };
        cboXmlSignTag.SelectedIndex = 0;

        cboXmlParentXPath.ItemsSource = new List<string> { "" };
        cboXmlParentXPath.SelectedIndex = 0;

        cboXmlReferenceId.ItemsSource = new List<string> { "" };
        cboXmlReferenceId.SelectedIndex = 0;

        // Sync inputs
        txtNoteX.TextChanged += (s, e) => { if (int.TryParse(txtNoteX.Text, out int x)) { _noteX = x; UpdatePlacementRects(); } else { _noteX = null; } };
        txtNoteY.TextChanged += (s, e) => { if (int.TryParse(txtNoteY.Text, out int y)) { _noteY = y; UpdatePlacementRects(); } else { _noteY = null; } };

        // Sync credentials and certs bidirectionally
        txtUserName.TextChanged += (s, e) => { if (txtUserNameXml.Text != txtUserName.Text) txtUserNameXml.Text = txtUserName.Text; };
        txtUserNameXml.TextChanged += (s, e) => { if (txtUserName.Text != txtUserNameXml.Text) txtUserName.Text = txtUserNameXml.Text; };
        txtPassword.TextChanged += (s, e) => { if (txtPasswordXml.Text != txtPassword.Text) txtPasswordXml.Text = txtPassword.Text; };
        txtPasswordXml.TextChanged += (s, e) => { if (txtPassword.Text != txtPasswordXml.Text) txtPassword.Text = txtPasswordXml.Text; };

        cboCerts.SelectionChanged += (s, e) => { if (cboCertsXml.SelectedIndex != cboCerts.SelectedIndex) cboCertsXml.SelectedIndex = cboCerts.SelectedIndex; };
        cboCertsXml.SelectionChanged += (s, e) => { if (cboCerts.SelectedIndex != cboCertsXml.SelectedIndex) cboCerts.SelectedIndex = cboCertsXml.SelectedIndex; };

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            winMenu.IsVisible = false;
        }

        Log("Dashboard Initialized. Welcome to Vimes SignSDK Showcase Studio on Avalonia!", ColorFromHex("#38BDF8"));
    }

    #region Formatting & Logging Helpers
    private static Avalonia.Media.Color ColorFromHex(string hex)
    {
        return Avalonia.Media.Color.Parse(hex);
    }

    private void Log(string message, Avalonia.Media.Color? color = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var text = txtLogs.Text ?? "";
            var timestamp = $"[{DateTime.Now:HH:mm:ss}] ";
            txtLogs.Text = text + timestamp + message + Environment.NewLine;
            
            // Scroll to end
            txtLogs.CaretIndex = txtLogs.Text.Length;
        });

        _logger?.LogInformation(message);
    }

    private void LogSuccess(string message) => Log(message, ColorFromHex("#10B981"));
    private void LogWarning(string message) => Log(message, ColorFromHex("#F59E0B"));
    private void LogError(string message) => Log(message, ColorFromHex("#EF4444"));
    private void LogSystem(string message) => Log(message, ColorFromHex("#38BDF8"));
    #endregion

    #region CA Authentication Flow
    private async void btnLogin_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string merchantId = cboMerchant.SelectedItem?.ToString() ?? "";
            bool isLocalOrUsb = merchantId.Equals("LOCAL", StringComparison.OrdinalIgnoreCase)
                             || merchantId.Equals("USB",   StringComparison.OrdinalIgnoreCase)
                             || merchantId.Equals("SELF",  StringComparison.OrdinalIgnoreCase);

            string userName = isLocalOrUsb ? "" : (txtUserName.Text ?? "");
            string password = isLocalOrUsb ? "" : (txtPassword.Text ?? "");

            LogSystem($"Attempting authentication with [{merchantId}] as {(isLocalOrUsb ? "local cert" : userName)}...");
            btnLogin.IsEnabled = false;
            if (btnLoginXml != null) btnLoginXml.IsEnabled = false;

            var result = await _signClient.LoginAsync(userName, password, merchantId, "", "");

            if (result.Success)
            {
                _bearerToken = result.BearerToken;
                _activeUserName = string.IsNullOrWhiteSpace(result.UserName) ? userName : result.UserName;
                LogSuccess("Authentication Successful. Session established.");
                if (isLocalOrUsb && !string.IsNullOrWhiteSpace(_activeUserName))
                    LogSystem($"Resolved token identity (serial): {_activeUserName}");

                lblSessionStatus.Text = $"Active Session: {_activeUserName} ({merchantId})";
                lblSessionStatus.Foreground = Avalonia.Media.Brushes.Green;
                panelStatusDot.Fill = Avalonia.Media.Brushes.Green;

                LogSystem("Retrieving merchant registration certificates...");
                var certs = await _signClient.GetCertificatesAsync(_activeUserName, _bearerToken ?? "", merchantId: merchantId);

                PopulateCertificatesControls(certs, _activeUserName, merchantId);
            }
            else
            {
                LogError($"Authentication Failed: {result.ErrorMessage}");
                lblSessionStatus.Text = "Status: Authentication Failed";
                lblSessionStatus.Foreground = Avalonia.Media.Brushes.Red;
                panelStatusDot.Fill = Avalonia.Media.Brushes.Red;
            }
        }
        catch (Exception ex)
        {
            LogError($"Runtime Authentication Error: {ex.Message}");
        }
        finally
        {
            btnLogin.IsEnabled = true;
            if (btnLoginXml != null) btnLoginXml.IsEnabled = true;
        }
    }

    private async void btnSyncCertificates_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string userName = txtUserName.Text ?? "";
            string merchantId = cboMerchant.SelectedItem?.ToString() ?? "";

            LogWarning($"Bypassing caches. Requesting direct certificate registration retrieval from {merchantId} server...");
            btnSyncCertificates.IsEnabled = false;
            if (btnSyncCertificatesXml != null) btnSyncCertificatesXml.IsEnabled = false;

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
            btnSyncCertificates.IsEnabled = true;
            if (btnSyncCertificatesXml != null) btnSyncCertificatesXml.IsEnabled = true;
        }
    }

    private void btnLoginXml_Click(object? sender, RoutedEventArgs e)
    {
        btnLogin_Click(btnLogin, e);
    }

    private void btnSyncCertificatesXml_Click(object? sender, RoutedEventArgs e)
    {
        btnSyncCertificates_Click(btnSyncCertificates, e);
    }

    private static string CertDisplayText(BaseCertificateInfo cert)
    {
        string subject = cert.subjectDN ?? "";
        string cn = subject;
        int i = subject.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
        {
            int start = i + 3;
            int end = subject.IndexOf(',', start);
            cn = (end > start ? subject.Substring(start, end - start) : subject.Substring(start)).Trim();
        }
        var serial = cert.serialNumber ?? cert.credentialID;
        return string.IsNullOrWhiteSpace(cn) ? serial : $"{cn} — {serial}";
    }

    private void PopulateCertificatesControls(List<BaseCertificateInfo>? certs, string userName, string merchantId)
    {
        cboCerts.ItemsSource = null;
        if (cboCertsXml != null) cboCertsXml.ItemsSource = null;

        if (certs != null && certs.Count > 0)
        {
            var comboItems = new List<ComboboxItem<string>>();
            var comboItemsXml = new List<ComboboxItem<string>>();
            foreach (var cert in certs)
            {
                comboItems.Add(new ComboboxItem<string>(CertDisplayText(cert), cert.credentialID));
                comboItemsXml.Add(new ComboboxItem<string>(CertDisplayText(cert), cert.credentialID));
            }

            cboCerts.ItemsSource = comboItems;
            cboCerts.SelectedIndex = 0;

            if (cboCertsXml != null)
            {
                cboCertsXml.ItemsSource = comboItemsXml;
                cboCertsXml.SelectedIndex = 0;
            }

            LogSuccess($"Parsed and loaded {certs.Count} verified certificates into local registry.");
        }
        else
        {
            LogWarning("Authentication returned zero active certificates or scopes.");
        }
    }

    private void cboMerchant_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (cboMerchant.SelectedItem == null) return;
        string selectedMerchant = cboMerchant.SelectedItem.ToString()!;
        
        bool isBcy = string.Equals(selectedMerchant, "BCY", StringComparison.OrdinalIgnoreCase);
        lblSignAlgorithm.IsVisible = isBcy;
        cboSignAlgorithm.IsVisible = isBcy;

        bool isLocalOrUsb = selectedMerchant.Equals("USB", StringComparison.OrdinalIgnoreCase)
                         || selectedMerchant.Equals("LOCAL", StringComparison.OrdinalIgnoreCase)
                         || selectedMerchant.Equals("SELF", StringComparison.OrdinalIgnoreCase);

        // Disable username field for local/USB merchants (password/PIN still needed for USB)
        txtUserName.IsEnabled = !isLocalOrUsb;
        if (isLocalOrUsb)
        {
            txtUserName.Text = "";
        }

        if (selectedMerchant.Equals("USB", StringComparison.OrdinalIgnoreCase))
        {
            EnsureAgentRunning();
        }
        else
        {
            StopAgent();
        }
    }
    #endregion

    #region PDF Signing Canvas & Preview
    private async void btnBrowse_Click(object? sender, RoutedEventArgs e)
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Select PDF Documents",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("PDF Documents") { Patterns = new[] { "*.pdf" } } }
        };

        var files = await this.StorageProvider.OpenFilePickerAsync(options);
        if (files != null && files.Count > 0)
        {
            lstFilePath.ItemsSource = files.Select(f => f.Path.LocalPath).ToList();
            lstFilePath.SelectedIndex = 0;
            _activePageNum = 1;
            RenderPdfPage();
        }
    }

    private async void btnBrowsePdfOutputDir_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await this.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Chọn thư mục đầu ra PDF ký số" });
        if (folders != null && folders.Count > 0)
        {
            txtPdfOutputDir.Text = folders[0].Path.LocalPath;
            LogSystem($"Thư mục đầu ra PDF đã chọn: {txtPdfOutputDir.Text}");
        }
    }

    private void lstFilePath_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _activePageNum = 1;
        RenderPdfPage();
    }

    private void RenderPdfPage()
    {
        try
        {
            string pdfPath = lstFilePath.SelectedItem?.ToString() ?? "";

            if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
            {
                imgPdfPage.Source = null;
                return;
            }

            int pageIndex = _activePageNum - 1;
            if (pageIndex < 0) pageIndex = 0;

            using (var library = Docnet.Core.DocLib.Instance)
            {
                using (var docReader = library.GetDocReader(pdfPath, new Docnet.Core.Models.PageDimensions(1.0d)))
                {
                    _totalPdfPages = docReader.GetPageCount();
                    if (_activePageNum > _totalPdfPages)
                    {
                        _activePageNum = _totalPdfPages;
                        pageIndex = _activePageNum - 1;
                    }

                    lblPreviewMock.Text = $"Trang {_activePageNum} / {_totalPdfPages}";

                    using (var pageReader = docReader.GetPageReader(pageIndex))
                    {
                        var width = pageReader.GetPageWidth();
                        var height = pageReader.GetPageHeight();
                        _pageW = width;
                        _pageH = height;
                        var rawBytes = pageReader.GetImage(Docnet.Core.Models.RenderFlags.RenderAnnotations);

                        // Set canvas sizes dynamically to match PDF coordinates
                        panelSigPlacementMock.Width = width;
                        panelSigPlacementMock.Height = height;
                        imgPdfPage.Width = width;
                        imgPdfPage.Height = height;

                        var bitmap = new WriteableBitmap(
                            new PixelSize(width, height),
                            new Vector(96, 96),
                            Avalonia.Platform.PixelFormat.Bgra8888,
                            Avalonia.Platform.AlphaFormat.Premul);

                        using (var locked = bitmap.Lock())
                        {
                            System.Runtime.InteropServices.Marshal.Copy(rawBytes, 0, locked.Address, rawBytes.Length);
                        }

                        imgPdfPage.Source = bitmap;
                        _renderedPageImage = bitmap;

                        LogSystem($"Rendered actual PDF Page {_activePageNum}/{_totalPdfPages} successfully!");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogWarning($"Note: Render actual PDF content failed (using fallback design layout): {ex.Message}");
            imgPdfPage.Source = null;
        }

        UpdatePlacementRects();
    }

    private void UpdatePlacementRects()
    {
        // Draw the rectangles based on pdf points
        // In Avalonia Canvas:
        // rectSig: Width=_sigW, Height=_sigH
        // Left=_sigX, Top=_pageH - _sigY - _sigH
        rectSig.Width = _sigW;
        rectSig.Height = _sigH;
        Canvas.SetLeft(rectSig, _sigX);
        Canvas.SetTop(rectSig, _pageH - _sigY - _sigH);
        rectSig.IsVisible = _sigW > 0 && _sigH > 0;

        txtSigX.Text = _sigX.ToString();
        txtSigY.Text = _sigY.ToString();
        txtSigW.Text = _sigW.ToString();
        txtSigH.Text = _sigH.ToString();

        if (_noteX.HasValue && _noteY.HasValue && _noteW.HasValue && _noteH.HasValue)
        {
            rectNote.Width = _noteW.Value;
            rectNote.Height = _noteH.Value;
            Canvas.SetLeft(rectNote, _noteX.Value);
            Canvas.SetTop(rectNote, _pageH - _noteY.Value - _noteH.Value);
            rectNote.IsVisible = true;

            txtNoteX.Text = _noteX.ToString();
            txtNoteY.Text = _noteY.ToString();
            txtNoteW.Text = _noteW.ToString();
            txtNoteH.Text = _noteH.ToString();
        }
        else
        {
            rectNote.IsVisible = false;
            txtNoteW.Text = "";
            txtNoteH.Text = "";
        }
    }

    private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(panelSigPlacementMock);
        if (!properties.Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(panelSigPlacementMock);
        _isDrawing = true;
        _startPoint = pos;
        _isDrawingNote = rbPositionNote.IsChecked == true;

        _selectionRect = new Rect(pos, pos);
    }

    private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDrawing) return;

        var pos = e.GetPosition(panelSigPlacementMock);
        
        // Constrain pointer to canvas bounds
        double x = Math.Max(0, Math.Min(pos.X, _pageW));
        double y = Math.Max(0, Math.Min(pos.Y, _pageH));

        var rectX = Math.Min(_startPoint.X, x);
        var rectY = Math.Min(_startPoint.Y, y);
        var rectW = Math.Abs(_startPoint.X - x);
        var rectH = Math.Abs(_startPoint.Y - y);

        _selectionRect = new Rect(rectX, rectY, rectW, rectH);

        // Convert selection to PDF points in real-time
        int pdfX = (int)rectX;
        int pdfW = (int)rectW;
        int pdfH = (int)rectH;
        int pdfY = (int)_pageH - (int)rectY - pdfH;

        if (_isDrawingNote)
        {
            _noteX = pdfX;
            _noteY = pdfY;
            _noteW = pdfW;
            _noteH = pdfH;
        }
        else
        {
            _sigX = pdfX;
            _sigY = pdfY;
            _sigW = pdfW;
            _sigH = pdfH;
        }

        UpdatePlacementRects();
    }

    private void Canvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDrawing) return;
        _isDrawing = false;

        if (_isDrawingNote)
        {
            if (_noteW < 10) _noteW = 100;
            if (_noteH < 10) _noteH = 24;
            LogSystem($"Note Box Selection Captured: X:{_noteX}, Y:{_noteY}, W:{_noteW}, H:{_noteH} (PDF points)");
        }
        else
        {
            if (_sigW < 10) _sigW = 100;
            if (_sigH < 10) _sigH = 100;
            LogSystem($"Canvas Box Selection Captured: X:{_sigX}, Y:{_sigY}, W:{_sigW}, H:{_sigH} (PDF points)");
        }

        UpdatePlacementRects();
    }

    private void SwitchTab(int index, string viewName)
    {
        tabMain.SelectedIndex = index;
        LogSystem($"Switched context view to: {viewName}");
    }

    // In-window Menu handlers (Windows/Linux)
    private void MenuPdfSign_Click(object? sender, RoutedEventArgs e) => SwitchTab(0, "Ký PDF");
    private void MenuXmlSign_Click(object? sender, RoutedEventArgs e) => SwitchTab(1, "Ký XML");
    private void MenuBatchSign_Click(object? sender, RoutedEventArgs e) => SwitchTab(2, "Ký Hàng Loạt");
    private void MenuSettings_Click(object? sender, RoutedEventArgs e) => SwitchTab(3, "Cài Đặt SDK");
    private void MenuExit_Click(object? sender, RoutedEventArgs e) => this.Close();
    private void MenuHelpDoc_Click(object? sender, RoutedEventArgs e) => LogSystem("Mở tài liệu hướng dẫn sử dụng...");
    private void MenuAbout_Click(object? sender, RoutedEventArgs e) => LogSystem("VIMES SignSDK Showcase Studio v2.1.0 (Avalonia)");

    // MacOS System Native Menu handlers
    private void NativeMenuPdfSign_Click(object? sender, EventArgs e) => SwitchTab(0, "Ký PDF");
    private void NativeMenuXmlSign_Click(object? sender, EventArgs e) => SwitchTab(1, "Ký XML");
    private void NativeMenuBatchSign_Click(object? sender, EventArgs e) => SwitchTab(2, "Ký Hàng Loạt");
    private void NativeMenuSettings_Click(object? sender, EventArgs e) => SwitchTab(3, "Cài Đặt SDK");
    private void NativeMenuExit_Click(object? sender, EventArgs e) => this.Close();
    private void NativeMenuHelpDoc_Click(object? sender, EventArgs e) => LogSystem("Mở tài liệu hướng dẫn sử dụng...");
    private void NativeMenuAbout_Click(object? sender, EventArgs e) => LogSystem("VIMES SignSDK Showcase Studio v2.1.0 (Avalonia)");

    private void btnPrevPage_Click(object? sender, RoutedEventArgs e)
    {
        if (_activePageNum > 1)
        {
            _activePageNum--;
            RenderPdfPage();
        }
    }

    private void btnNextPage_Click(object? sender, RoutedEventArgs e)
    {
        if (_activePageNum < _totalPdfPages)
        {
            _activePageNum++;
            RenderPdfPage();
        }
    }

    private async void btnSign_Click(object? sender, RoutedEventArgs e)
    {
        btnSign.IsEnabled = false;
        try
        {
            var files = lstFilePath.ItemsSource as List<string>;
            if (files == null || files.Count == 0)
            {
                LogError("No PDF file selected or specified.");
                return;
            }

            string? lastSignedPath = null;
            int successCount = 0;
            var allSignedPaths = new List<string>();

            foreach (var filePath in files)
            {
                LogSystem($"Processing signing for: {Path.GetFileName(filePath)}");
                string? signedPath = await SignSingleFile(filePath);
                if (!string.IsNullOrEmpty(signedPath) && File.Exists(signedPath))
                {
                    successCount++;
                    lastSignedPath = signedPath;
                    allSignedPaths.Add(signedPath);
                }
            }

            if (successCount > 0 && !string.IsNullOrEmpty(lastSignedPath))
            {
                LogSuccess($"Signature execution finished successfully for {successCount} file(s).");
                _sigW = 0;
                _sigH = 0;
                _noteW = 0;
                _noteH = 0;

                int currentPage = _activePageNum;

                // Temporarily detach selection changed to prevent it from resetting page index to 1
                lstFilePath.SelectionChanged -= lstFilePath_SelectionChanged;

                lstFilePath.ItemsSource = allSignedPaths;
                lstFilePath.SelectedIndex = 0;

                lstFilePath.SelectionChanged += lstFilePath_SelectionChanged;

                _activePageNum = currentPage;
                RenderPdfPage();
            }
        }
        finally
        {
            btnSign.IsEnabled = true;
        }
    }

    private async Task<string?> SignSingleFile(string filePath)
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

            var selectedCert = cboCerts.SelectedItem as ComboboxItem<string>;
            string credentialId = selectedCert!.Value;
            string merchantId = cboMerchant.SelectedItem?.ToString() ?? "";
            string userName = _activeUserName ?? (txtUserName.Text ?? "");

            LogSystem($"Constructing signature request for {Path.GetFileName(filePath)}...");
            
            var fileBytes = await File.ReadAllBytesAsync(filePath);
            var base64 = Convert.ToBase64String(fileBytes);

            var request = new SignDocumentRequest
            {
                FileName = Path.GetFileName(filePath),
                FileData = base64,
                SignerName = txtSignerName.Text ?? "",
                SignerTitle = txtSignerTitle.Text ?? "",
                Note = txtNote.Text ?? "",
                SignatureType = (cboSignatureType.SelectedItem as ComboboxItem<SignatureType>)?.Value ?? SignatureType.DEFAULT,
                DisplayNameMode = (cboDisplayNameMode.SelectedItem as ComboboxItem<DisplayNameMode>)?.Value ?? DisplayNameMode.SignerWithImage,
                IsShowSignatureTime = chkShowSignatureTime.IsChecked == true,
                SignerPosition = "Visual Placement",
                Page = _activePageNum,
                X = _sigX,
                Y = _sigY,
                Width = _sigW,
                Height = _sigH,
                NotePointX = _noteX,
                NotePointY = _noteY,
                SignatureImage = _customSignatureImageBase64,
                SignAlgorithm = cboSignAlgorithm.IsVisible ? cboSignAlgorithm.SelectedItem?.ToString() : null
            };

            var batchRequest = new SignDocumentsRequest
            {
                UserName = userName,
                CredentialID = credentialId,
                MerchantId = merchantId,
                Pin = txtPassword.Text ?? "",
                Documents = new List<SignDocumentRequest> { request }
            };

            LogSystem($"[BEFORE SIGN] Request → Page={request.Page}, X={request.X}, Y={request.Y}, W={request.Width}, H={request.Height}");
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
                
                string outFolder;
                if (!string.IsNullOrEmpty(txtPdfOutputDir.Text))
                {
                    outFolder = txtPdfOutputDir.Text;
                }
                else
                {
                    var parentDir = Path.GetDirectoryName(filePath)!;
                    outFolder = string.Equals(Path.GetFileName(parentDir), "Signed", StringComparison.OrdinalIgnoreCase)
                        ? parentDir
                        : Path.Combine(parentDir, "Signed");
                }
                if (!Directory.Exists(outFolder)) Directory.CreateDirectory(outFolder);
                
                var ts = DateTime.Now.ToString("yyMMddHHmmss");
                var outPath = Path.Combine(outFolder, $"{ts}_{Path.GetFileNameWithoutExtension(filePath)}_signed{Path.GetExtension(filePath)}");
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
                return outPath;
            }
            else
            {
                LogError($"Signature Rejected by Remote Gateway: {result.ErrorMessage}");
                return null;
            }
        }
        catch (Exception ex)
        {
            LogError($"Signature Generation Exception: {ex.Message}");
            return null;
        }
    }

    private async void btnBrowseSigImage_Click(object? sender, RoutedEventArgs e)
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Select Signature Image",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Image Files") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } } }
        };

        var files = await this.StorageProvider.OpenFilePickerAsync(options);
        if (files != null && files.Count > 0)
        {
            var filePath = files[0].Path.LocalPath;
            var stream = await files[0].OpenReadAsync();
            pbSigImage.Source = new Bitmap(stream);
            
            var imgBytes = await File.ReadAllBytesAsync(filePath);
            _customSignatureImageBase64 = Convert.ToBase64String(imgBytes);
            LogSystem($"Imported custom signature image: {Path.GetFileName(filePath)} ({imgBytes.Length} bytes)");
        }
    }

    private void btnClearSigImage_Click(object? sender, RoutedEventArgs e)
    {
        pbSigImage.Source = null;
        _customSignatureImageBase64 = null;
        LogSystem("Reset to default merchant/SDK system signature visual representation.");
    }
    #endregion

    #region XML Signing Tab
    private async void btnBrowseXml_Click(object? sender, RoutedEventArgs e)
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Select XML Documents",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("XML Documents") { Patterns = new[] { "*.xml" } } }
        };

        var files = await this.StorageProvider.OpenFilePickerAsync(options);
        if (files != null && files.Count > 0)
        {
            lstXmlFilePath.ItemsSource = files.Select(f => f.Path.LocalPath).ToList();
            lstXmlFilePath.SelectedIndex = 0;
        }
    }

    private void btnAnalyzeXml_Click(object? sender, RoutedEventArgs e)
    {
        if (lstXmlFilePath.SelectedItem == null)
        {
            LogWarning("Chọn một tệp XML trong danh sách trước khi phân tích.");
            return;
        }

        string filePath = lstXmlFilePath.SelectedItem.ToString()!;
        if (!File.Exists(filePath)) { LogError($"Tệp không tồn tại: {filePath}"); return; }

        try
        {
            var doc = new System.Xml.XmlDocument();
            doc.Load(filePath);
            
            LogSystem($"=== Phân tích: {Path.GetFileName(filePath)} ===");

            bool isHocBa = doc.SelectSingleNode("//*[local-name()='DANH_SACH_THONG_TIN_KY']") != null;
            bool isTongKet = doc.SelectSingleNode("//*[local-name()='TONG_KET_CA_NAM']") != null;
            bool isLyLich = doc.SelectSingleNode("//*[local-name()='THONG_TIN'][@Id='lyLich']") != null
                            || doc.SelectSingleNode("//*[local-name()='THONG_TIN' and @Id]") != null;

            var idNodes = doc.SelectNodes("//*[@Id]");
            var seenIds = new HashSet<string>();
            var seenTags = new HashSet<string>();
            string? firstDataId = null;

            var refIds = new List<string> { "" };
            var knownTags = new List<string> { "", "CHUKYDONVI", "GVBM", "GVCN", "CBQL", "KY_PHAT_HANH" };
            var parentXPaths = new List<string> { "" };
            _xpathRefMap.Clear();

            if (idNodes?.Count > 0)
            {
                LogSystem($"Phần tử có Id=\"...\" ({idNodes.Count} tìm thấy):");
                foreach (System.Xml.XmlElement el in idNodes)
                {
                    string id = el.GetAttribute("Id");
                    if (el.LocalName == "Signature") continue;
                    LogSystem($"  <{el.LocalName} Id=\"{id}\">");

                    if (seenIds.Add(id)) refIds.Add(id);
                    if (seenTags.Add(el.LocalName) && !knownTags.Contains(el.LocalName))
                        knownTags.Add(el.LocalName);

                    if (firstDataId == null) firstDataId = id;
                }

                cboXmlReferenceId.ItemsSource = refIds;
                cboXmlReferenceId.SelectedIndex = firstDataId != null ? refIds.IndexOf(firstDataId) : 0;
            }
            else
            {
                LogWarning("  Không tìm thấy phần tử nào có Id=\"...\"");
                cboXmlReferenceId.ItemsSource = refIds;
                cboXmlReferenceId.SelectedIndex = 0;
            }

            if (isHocBa)
            {
                LogSystem("Loại: HOC_BA");
                var xpathCbql = "//*[local-name()='PHAT_HANH_HOC_BA']//*[local-name()='CBQL'][not(*)][1]";
                var xpathKyPhatHanh = "//*[local-name()='PHAT_HANH_HOC_BA']/*[local-name()='KY_PHAT_HANH']";
                parentXPaths.Add(xpathCbql);
                parentXPaths.Add(xpathKyPhatHanh);
                
                _xpathRefMap[xpathCbql] = "";
                _xpathRefMap[xpathKyPhatHanh] = "";
            }

            cboXmlSignTag.ItemsSource = knownTags;
            cboXmlSignTag.SelectedIndex = 0;

            cboXmlParentXPath.ItemsSource = parentXPaths;
            cboXmlParentXPath.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            LogError($"Lỗi phân tích XML: {ex.Message}");
        }
    }

    private void cboXmlParentXPath_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (cboXmlParentXPath.SelectedItem is string xpath && _xpathRefMap.TryGetValue(xpath, out var refId))
        {
            var items = cboXmlReferenceId.ItemsSource as List<string>;
            if (items != null)
            {
                int index = items.IndexOf(refId);
                if (index >= 0) cboXmlReferenceId.SelectedIndex = index;
            }
        }
    }

    private async void btnSignXml_Click(object? sender, RoutedEventArgs e)
    {
        var files = lstXmlFilePath.ItemsSource as List<string>;
        if (files == null || files.Count == 0)
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
            btnSignXml.IsEnabled = false;

            string userName = _activeUserName ?? (txtUserNameXml.Text ?? "");
            string credentialId = (cboCertsXml.SelectedItem as ComboboxItem<string>)!.Value;
            string merchantId = cboMerchant.SelectedItem?.ToString() ?? "";

            string signTag = cboXmlSignTag.SelectedItem?.ToString() ?? "";
            string? referenceId = string.IsNullOrWhiteSpace(cboXmlReferenceId.SelectedItem?.ToString()) ? null : cboXmlReferenceId.SelectedItem.ToString();
            string? parentXPath = string.IsNullOrWhiteSpace(cboXmlParentXPath.SelectedItem?.ToString()) ? null : cboXmlParentXPath.SelectedItem.ToString();

            var fileDatas = new List<XmlFileDataItem>();
            foreach (var filePath in files)
            {
                if (File.Exists(filePath))
                {
                    string fileContent = await File.ReadAllTextAsync(filePath);
                    string signatureName = string.IsNullOrWhiteSpace(txtXmlSignatureName.Text) 
                        ? "Signature_" + Guid.NewGuid().ToString().Substring(0, 8) 
                        : txtXmlSignatureName.Text;

                    fileDatas.Add(new XmlFileDataItem
                    {
                        FileName = Path.GetFileName(filePath),
                        XmlData = Convert.ToBase64String(Encoding.UTF8.GetBytes(fileContent)),
                        SignTag = signTag,
                        SignatureName = signatureName,
                        ReferenceId = referenceId,
                        ParentXPath = parentXPath
                    });
                }
            }

            LogSystem($"Packaging {fileDatas.Count} XML file(s) for signing...");
            var request = new XmlMultiSignRequest
            {
                UserName = userName,
                CredentialID = credentialId,
                MID = merchantId,
                FileDatas = fileDatas,
                Pin = txtPassword.Text ?? "",
                SignAlgorithm = null
            };

            var results = await _signClient.SignXmlDocumentsAsync(request);
            if (results == null || results.Count == 0)
            {
                LogError("Signature failed: No response from XML signing engine.");
                return;
            }

            int successCount = 0;
            foreach (var result in results)
            {
                string? originalFilePath = files.FirstOrDefault(f => Path.GetFileName(f) == result.FileName);
                if (result.Success && originalFilePath != null)
                {
                    successCount++;
                    var parentDir = Path.GetDirectoryName(originalFilePath)!;
                    var outFolder = Path.Combine(parentDir, "Signed");
                    if (!Directory.Exists(outFolder)) Directory.CreateDirectory(outFolder);
                    
                    var outPath = Path.Combine(outFolder, $"{DateTime.Now:yyMMddHHmmss}_{result.FileName}");
                    byte[] signedBytes = Convert.FromBase64String(result.SignedXmlBase64);

                    await File.WriteAllBytesAsync(outPath, signedBytes);
                    LogSuccess($"XML signed output saved successfully to: {outPath}");
                }
                else
                {
                    LogError($"Failed to sign XML {result.FileName}: {result.ErrorMessage}");
                }
            }

            LogSuccess($"Signed {successCount} XML documents successfully.");
        }
        catch (Exception ex)
        {
            LogError($"Lỗi ký XML: {ex.Message}");
        }
        finally
        {
            btnSignXml.IsEnabled = true;
        }
    }
    #endregion

    #region Batch PDF Signing Tab
    private async void btnBrowseBatch_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await this.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Chọn thư mục nguồn PDF" });
        if (folders != null && folders.Count > 0)
        {
            txtBatchFolder.Text = folders[0].Path.LocalPath;
            LogSystem($"Thư mục nguồn: {txtBatchFolder.Text}");
        }
    }

    private async void btnBrowseBatchOutput_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await this.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Chọn thư mục đầu ra" });
        if (folders != null && folders.Count > 0)
        {
            txtBatchOutput.Text = folders[0].Path.LocalPath;
            LogSystem($"Thư mục đầu ra: {txtBatchOutput.Text}");
        }
    }

    private async void btnBrowseBatchCert_Click(object? sender, RoutedEventArgs e)
    {
        var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Chọn chứng thư số cục bộ (.p12/.pfx)",
            FileTypeFilter = new[] { new FilePickerFileType("Certificates") { Patterns = new[] { "*.pfx", "*.p12" } } }
        });
        if (files != null && files.Count > 0)
        {
            txtBatchCertPath.Text = files[0].Path.LocalPath;
        }
    }

    private void btnSelectAllBatch_Click(object? sender, RoutedEventArgs e)
    {
        LogSystem("Chọn tất cả file trong thư mục.");
    }

    private void btnClearAllBatch_Click(object? sender, RoutedEventArgs e)
    {
        LogSystem("Bỏ chọn tất cả file.");
    }

    private async void btnBatchSign_Click(object? sender, RoutedEventArgs e)
    {
        // Batch signing using local/self cert details
        string folder = txtBatchFolder.Text ?? "";
        string outFolder = txtBatchOutput.Text ?? "";
        string certPath = txtBatchCertPath.Text ?? "";
        string certPass = txtBatchCertPass.Text ?? "";

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            LogError("Vui lòng chọn thư mục nguồn hợp lệ.");
            return;
        }

        if (string.IsNullOrEmpty(certPath) || !File.Exists(certPath))
        {
            LogError("Vui lòng chọn file chứng thư Self CA.");
            return;
        }

        btnBatchSign.IsEnabled = false;
        try
        {
            var pdfFiles = Directory.GetFiles(folder, "*.pdf");
            if (pdfFiles.Length == 0)
            {
                LogWarning("Không tìm thấy file PDF nào.");
                return;
            }

            if (string.IsNullOrEmpty(outFolder))
            {
                outFolder = Path.Combine(folder, "Signed");
            }
            if (!Directory.Exists(outFolder)) Directory.CreateDirectory(outFolder);

            LogSystem($"Bắt đầu ký hàng loạt {pdfFiles.Length} files bằng Self CA...");
            batchProgressBar.Value = 0;

            int success = 0;
            for (int i = 0; i < pdfFiles.Length; i++)
            {
                string file = pdfFiles[i];
                lblBatchStatus.Text = $"Đang ký: {Path.GetFileName(file)}";
                
                try
                {
                    // For Self CA we package it with the cert data in local config and sign it
                    var certBytes = await File.ReadAllBytesAsync(certPath);
                    var docBytes = await File.ReadAllBytesAsync(file);

                    // Re-use signClient's local SignCertificatedDocument method if exposed, or make normal call
                    // Actually, self signing usually proceeds by creating a SignDocumentsRequest and setting credentialID to the PFX content / config.
                    // Let's call client sign workflow
                    var docRequest = new SignDocumentRequest
                    {
                        FileName = Path.GetFileName(file),
                        FileData = Convert.ToBase64String(docBytes),
                        SignerName = txtSignerName.Text ?? "Self Signed",
                        Page = 1,
                        X = 100,
                        Y = 100,
                        Width = 150,
                        Height = 150
                    };

                    var batchRequest = new SignDocumentsRequest
                    {
                        UserName = Convert.ToBase64String(certBytes), // SDK can accept base64 cert or load it from config
                        CredentialID = certPass, // password
                        MerchantId = "SELF",
                        Documents = new List<SignDocumentRequest> { docRequest }
                    };

                    var results = await _signClient.SignDocumentsAsync(batchRequest);
                    var res = results?.FirstOrDefault();
                    if (res != null && res.Success)
                    {
                        var outPath = Path.Combine(outFolder, Path.GetFileName(file));
                        byte[] signedBytes;
                        if (res.SignedFileUrl.Contains(","))
                        {
                            signedBytes = Convert.FromBase64String(res.SignedFileUrl.Split(',').Last());
                        }
                        else
                        {
                            signedBytes = Convert.FromBase64String(res.SignedFileUrl);
                        }

                        await File.WriteAllBytesAsync(outPath, signedBytes);
                        success++;
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Lỗi ký file {Path.GetFileName(file)}: {ex.Message}");
                }

                batchProgressBar.Value = (double)(i + 1) / pdfFiles.Length * 100;
            }

            LogSuccess($"Ký hoàn tất. Thành công {success}/{pdfFiles.Length} tệp.");
            lblBatchStatus.Text = "Ký thành công.";
        }
        finally
        {
            btnBatchSign.IsEnabled = true;
        }
    }
    #endregion

    #region Settings Tab & Preservation
    private void LoadSettingsToUi()
    {
        if (_appSettings == null) return;

        // MySign
        txtMySignUrl.Text = _appSettings.MySignSetting?.BaseUrl ?? "https://remotesigning.viettel.vn";
        txtMySignProfile.Text = _appSettings.MySignSetting?.ProfileId ?? "";
        txtMySignClientId.Text = _appSettings.MySignSetting?.ClientId ?? "";
        txtMySignSecret.Text = _appSettings.MySignSetting?.ClientSecret ?? "";

        // SmartCA
        txtSmartCAUrl.Text = _appSettings.SmartCASetting?.BaseUrl ?? "https://gwsca.vnpt.vn";
        txtSmartCAProfile.Text = _appSettings.SmartCASetting?.ProfileId ?? "";
        txtSmartCAClientId.Text = _appSettings.SmartCASetting?.ClientId ?? "";
        txtSmartCASecret.Text = _appSettings.SmartCASetting?.ClientSecret ?? "";

        // USB
        txtUsbAgentIp.Text = _appSettings.UsbSetting?.UsbAgentIp ?? "127.0.0.1";
        txtUsbAgentPort.Text = _appSettings.UsbSetting?.UsbAgentPort.ToString() ?? "9999";
        txtUsbAgentExePath.Text = _appSettings.UsbSetting?.UsbAgentExePath ?? "";
    }

    private void btnSaveSettings_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Copy UI values back to _appSettings
            if (_appSettings.MySignSetting == null) _appSettings.MySignSetting = new();
            _appSettings.MySignSetting.BaseUrl = txtMySignUrl.Text;
            _appSettings.MySignSetting.ProfileId = txtMySignProfile.Text;
            _appSettings.MySignSetting.ClientId = txtMySignClientId.Text;
            _appSettings.MySignSetting.ClientSecret = txtMySignSecret.Text;

            if (_appSettings.SmartCASetting == null) _appSettings.SmartCASetting = new();
            _appSettings.SmartCASetting.BaseUrl = txtSmartCAUrl.Text;
            _appSettings.SmartCASetting.ProfileId = txtSmartCAProfile.Text;
            _appSettings.SmartCASetting.ClientId = txtSmartCAClientId.Text;
            _appSettings.SmartCASetting.ClientSecret = txtSmartCASecret.Text;

            if (_appSettings.UsbSetting == null) _appSettings.UsbSetting = new();
            _appSettings.UsbSetting.UsbAgentIp = txtUsbAgentIp.Text;
            if (int.TryParse(txtUsbAgentPort.Text, out int port)) _appSettings.UsbSetting.UsbAgentPort = port;
            _appSettings.UsbSetting.UsbAgentExePath = txtUsbAgentExePath.Text;

            // Merge into appsettings.json
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (!File.Exists(configPath)) configPath = "appsettings.json";

            var pathsToSave = new List<string> { configPath };

            // Also check project root directory if running in Dev/Debug environment (bin/Debug/net8.0)
            try
            {
                var dir = AppDomain.CurrentDomain.BaseDirectory;
                var parent = Directory.GetParent(dir)?.Parent?.Parent;
                if (parent != null)
                {
                    var projSettings = Path.Combine(parent.FullName, "appsettings.json");
                    if (File.Exists(projSettings) && Path.GetFullPath(projSettings) != Path.GetFullPath(configPath))
                    {
                        pathsToSave.Add(projSettings);
                    }
                }
            }
            catch {}

            foreach (var path in pathsToSave)
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var jObj = Newtonsoft.Json.Linq.JObject.Parse(json);
                    
                    var settingsJson = JsonConvert.SerializeObject(_appSettings);
                    var settingsObj = Newtonsoft.Json.Linq.JObject.Parse(settingsJson);
                    
                    var appSettingsWrapper = new Newtonsoft.Json.Linq.JObject();
                    appSettingsWrapper["AppSettings"] = settingsObj;
                    
                    jObj.Merge(appSettingsWrapper, new Newtonsoft.Json.Linq.JsonMergeSettings
                    {
                        MergeArrayHandling = Newtonsoft.Json.Linq.MergeArrayHandling.Union,
                        MergeNullValueHandling = Newtonsoft.Json.Linq.MergeNullValueHandling.Merge
                    });
                    
                    string finalJson = jObj.ToString(Formatting.Indented);
                    File.WriteAllText(path, finalJson);
                    LogSuccess($"Active settings successfully saved to: {path}");
                }
                else
                {
                    LogError($"Cannot find appsettings.json file to write settings: {path}");
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to save settings: {ex.Message}");
        }
    }
    #endregion

    #region USB Agent Process Management
    private void EnsureAgentRunning()
    {
        if (_agentProcess is { HasExited: false }) return;

        string agentExe = _appSettings?.UsbSetting?.UsbAgentExePath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(agentExe))
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // Cross-platform binary selection
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                // Mac OS binary lookups
                agentExe = Path.Combine(appDir, "UsbTokenAgent");
                if (!File.Exists(agentExe))
                {
                    // Check parent directories or common bundle location
                    agentExe = "/Applications/UsbTokenAgent.app/Contents/MacOS/UsbTokenAgent";
                }
            }
            else
            {
                // Windows binary
                agentExe = Path.Combine(appDir, "UsbTokenAgent.exe");
            }
        }

        if (!File.Exists(agentExe))
        {
            LogWarning($"[USB Agent] Agent binary not found at '{agentExe}'. " +
                       "Start it manually or set the path in Settings.");
            return;
        }

        try
        {
            _agentProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = agentExe,
                WorkingDirectory = Path.GetDirectoryName(agentExe)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
            });
            LogSystem($"[USB Agent] Started UsbTokenAgent (PID {_agentProcess?.Id}).");
        }
        catch (Exception ex)
        {
            LogError($"[USB Agent] Failed to start UsbTokenAgent: {ex.Message}");
        }
    }

    private void StopAgent()
    {
        if (_agentProcess is null || _agentProcess.HasExited) return;
        try
        {
            _agentProcess.Kill(entireProcessTree: true);
            _agentProcess.Dispose();
            LogSystem("[USB Agent] Stopped.");
        }
        catch { }
        finally { _agentProcess = null; }
    }

    protected override void OnClosed(EventArgs e)
    {
        StopAgent();
        base.OnClosed(e);
    }
    #endregion

    #region UI event redirections for Logs
    private void btnClearLogs_Click(object? sender, RoutedEventArgs e)
    {
        txtLogs.Text = "";
    }

    private async void btnCopyLogs_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(txtLogs.Text ?? "");
            LogSystem("Logs copied to clipboard.");
        }
    }
    #endregion

    private class ComboboxItem<T>
    {
        public string Text { get; }
        public T Value { get; }
        public ComboboxItem(string text, T value) { Text = text; Value = value; }
        public override string ToString() => Text;
    }
}