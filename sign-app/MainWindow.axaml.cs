using Avalonia;
using Avalonia.Controls;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Vimes.SignSDK;
using Vimes.SignSDK.Helpers;
using Vimes.SignSDK.ViewModels;
using Signature.Domain.API;
using Core.Common.Common;
using Core.Config.Settings;
using VMSign.Shared.Services;
using VMSign.Shared.Models;

namespace VMSign;

public partial class MainWindow : Window
{
    private readonly ISignSDKClient _signClient;
    private readonly ILogger<MainWindow> _logger;
    private string? _bearerToken;
    private string? _activeUserName;
    private string? _activeMerchantId;
    private bool _isLoggedIn;
    private bool _isRestoringMerchantSelection;
    private System.Diagnostics.Process? _agentProcess;

    private readonly AppSettings _appSettings;
    private readonly IConfiguration _configuration;
    private SignDocumentRequest _advancedRequest = new();
    
    // Custom signature image variable
    private string? _customSignatureImageBase64;
    
    // Canvas Mouse selection variables
    private bool _isDrawing = false;
    private Point _startPoint;
    private Rect _selectionRect;

    // Selected coordinates
    private int _sigX;
    private int _sigY;
    private int _sigW;
    private int _sigH;
    private string? _selectedSignatureFieldId;
    private string? _signaturePlacementFilePath;
    private int? _signaturePlacementPage;
    private int? _noteX = null;
    private int? _noteY = null;
    private int? _noteW = null;
    private int? _noteH = null;
    private string? _notePlacementFilePath;
    private int? _notePlacementPage;
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

    // Credential picker (multi-certificate accounts)
    private TaskCompletionSource<string?>? _credentialPickerTcs;
    private string? _credentialPickerChoice;
    private static readonly HttpClient _testConnectionHttp = new() { Timeout = TimeSpan.FromSeconds(5) };

    // Session flyout: subject DNs of the active session's registered certificates, keyed by credentialID
    private readonly Dictionary<string, string?> _certSubjectsByCredentialId = new();

    // Multi-placement signing state (mirrors sign-web's Signing.placements / drawnBoxes):
    // AcroFields the user has marked "placed" (pending, not yet signed) this document.
    private readonly HashSet<string> _placedFieldIds = new(StringComparer.Ordinal);
    // AcroFields already signed this document (belt-and-suspenders alongside the PDF's own isSigned flag).
    private readonly HashSet<string> _signedFieldIds = new(StringComparer.Ordinal);
    // Hand-drawn (non-AcroField) placements queued for the next sign.
    private sealed class DrawnPlacement { public int Page; public int X, Y, W, H; }
    private readonly List<DrawnPlacement> _drawnPlacements = new();
    // Rects of all AcroFields detected on the currently rendered page, keyed by field name.
    private readonly Dictionary<string, (int page, int x, int y, int w, int h)> _detectedFieldRects = new();
    // Live button references for detected fields so placement toggles can restyle in place
    // without re-parsing the PDF.
    private readonly Dictionary<string, Button> _fieldButtons = new();
    // Overlay borders rendered for queued drawn placements.
    private readonly List<Border> _drawnPlacementOverlays = new();

    private sealed class PendingPlacement { public string? FieldId; public int Page; public int X, Y, W, H; }

    public MainWindow()
    {
        // Parameterless constructor for designer preview
        InitializeComponent();
        _signClient = null!;
        _logger = null!;
        _appSettings = null!;
        _configuration = null!;
    }

    public MainWindow(ISignSDKClient signClient, ILogger<MainWindow> logger, AppSettings appSettings, IConfiguration configuration)
    {
        InitializeComponent();
        _signClient = signClient;
        _logger = logger;
        _appSettings = appSettings;
        _configuration = configuration;

        InitializeApp();
    }

    private void InitializeApp()
    {
        // Populate merchants
        var merchants = _signClient.GetRegisteredMerchants();
        cboMerchant.ItemsSource = merchants;
        cboMerchant.SelectionChanged += cboMerchant_SelectionChanged;
        if (merchants.Count > 0)
        {
            var configuredMerchant = _appSettings.InternalSetting?.DefaultMerchantId;
            cboMerchant.SelectedItem = merchants.FirstOrDefault(
                merchant => string.Equals(
                    merchant,
                    configuredMerchant,
                    StringComparison.OrdinalIgnoreCase))
                ?? merchants[0];
        }
        RefreshMerchantHeaderDisplay();
        RenderMerchantDropdownList();
        RenderLoginMerchantGrid();

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

        if (_appSettings?.InternalSetting != null)
        {
            chkAutoCreateAcroField.IsChecked = _appSettings.InternalSetting.AutoCreateAcroField == 1;
        }

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

        cboCerts.SelectionChanged += (s, e) =>
        {
            if (cboCertsXml.SelectedIndex != cboCerts.SelectedIndex)
                cboCertsXml.SelectedIndex = cboCerts.SelectedIndex;
            UpdateSigningActionStates();
            RefreshSessionInfoRows();
        };
        cboCertsXml.SelectionChanged += (s, e) =>
        {
            if (cboCerts.SelectedIndex != cboCertsXml.SelectedIndex)
                cboCerts.SelectedIndex = cboCertsXml.SelectedIndex;
            UpdateSigningActionStates();
        };

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            winMenu.IsVisible = false;
        }

        // Register drag-and-drop event handlers
        this.AddHandler(DragDrop.DragOverEvent, Canvas_DragOver);
        this.AddHandler(DragDrop.DropEvent, Canvas_Drop);

        // Escape closes header overlays/dialogs. Tunnel so it fires even while a TextBox
        // inside an overlay holds focus.
        this.AddHandler(KeyDownEvent, MainWindow_KeyDown, RoutingStrategies.Tunnel);

        ApplyLoggedOutUi();
        UpdateSigningActionStates();
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

    #region Toast Notifications
    private void ToastSuccess(string title, string message) => ShowToast("success", title, message);
    private void ToastWarning(string title, string message) => ShowToast("warning", title, message);
    private void ToastError(string title, string message) => ShowToast("error", title, message);
    private void ToastInfo(string title, string message) => ShowToast("info", title, message);

    /// <summary>
    /// Ephemeral top-right notification card, matching sign-web's Toast system
    /// (auto-dismiss ~4.2s). Also mirrors the message into the system log panel,
    /// same pairing as web's Toast.show → App.log.
    /// </summary>
    private void ShowToast(string type, string title, string message)
    {
        string accentHex = type switch
        {
            "success" => "#0F9D6E",
            "error" => "#CC4238",
            "warning" => "#D9891F",
            _ => "#1E5FD4"
        };

        Dispatcher.UIThread.Post(() =>
        {
            var textStack = new StackPanel { Spacing = 2 };
            textStack.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                FontSize = 13.5,
                Foreground = new Avalonia.Media.SolidColorBrush(ColorFromHex("#16233A")),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
            textStack.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 12,
                Foreground = new Avalonia.Media.SolidColorBrush(ColorFromHex("#5B6472")),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });

            var closeBtn = new Button
            {
                Content = "✕",
                Background = Avalonia.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new Avalonia.Media.SolidColorBrush(ColorFromHex("#8A93A1")),
                Padding = new Thickness(4),
                MinHeight = 0,
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
            };

            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto") };
            Grid.SetColumn(textStack, 0);
            Grid.SetColumn(closeBtn, 1);
            content.Children.Add(textStack);
            content.Children.Add(closeBtn);

            var card = new Border
            {
                Background = Avalonia.Media.Brushes.White,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12),
                BorderThickness = new Thickness(4, 0, 0, 0),
                BorderBrush = new Avalonia.Media.SolidColorBrush(ColorFromHex(accentHex)),
                BoxShadow = Avalonia.Media.BoxShadows.Parse("0 8 24 0 #33000000"),
                Child = content
            };

            closeBtn.Click += (s, e) => toastHost.Children.Remove(card);
            toastHost.Children.Insert(0, card);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4200) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                toastHost.Children.Remove(card);
            };
            timer.Start();
        });

        string logColorHex = type switch
        {
            "success" => "#10B981",
            "error" => "#EF4444",
            "warning" => "#F59E0B",
            _ => "#38BDF8"
        };
        Log($"{title}: {message}", ColorFromHex(logColorHex));
    }
    #endregion

    #region Signing Progress Modal
    private static string GetMobileAppName(string? merchantId)
    {
        if (string.IsNullOrWhiteSpace(merchantId)) return "ứng dụng xác thực ký số";
        var id = merchantId.ToUpperInvariant();
        if (id.Contains("VIETTEL") || id.Contains("MYSIGN")) return "Viettel MySign";
        if (id.Contains("SMARTCA") || id.Contains("VNPT")) return "VNPT SmartCA";
        if (id.Contains("VGCA") || id.Contains("BCY")) return "VGCA Sign Service";
        if (id.Contains("FPT")) return "FPT SmartCA";
        if (id.Contains("MISA")) return "MISA eSign";
        if (id.Contains("CA2")) return "SmartSign (CA2)";
        if (id.Contains("BKAV")) return "BkavCA";
        return $"ứng dụng ký số ({merchantId})";
    }

    private void ShowSigningProgress(bool show, string? merchantId = null)
    {
        if (show)
        {
            lblProgressAppLine.Text = $"2. Mở ứng dụng xác thực ký số (ví dụ: {GetMobileAppName(merchantId)}).";
        }
        panelSigningProgress.IsVisible = show;
    }
    #endregion

    #region Config Guard Modal
    /// <summary>
    /// Best-effort "is this merchant usable" check, mirroring sign-web's SigningGuard
    /// config-guard (VMSign.Shared.Services.SigningGuard). Local/USB merchants need no
    /// remote credentials so they're always considered configured.
    /// </summary>
    private bool IsMerchantConfigured(string merchantId)
    {
        var info = MerchantRegistry.GetDisplayInfo(merchantId);
        if (info.CertMode != MerchantCertMode.Remote) return true;
        if (_appSettings == null) return true;

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

    private void ShowConfigGuard(string merchantId)
    {
        var info = MerchantRegistry.GetDisplayInfo(merchantId);
        lblConfigGuardMsg.Text = $"Merchant {info.Name} chưa có thông tin kết nối hợp lệ tới nhà cung cấp chữ ký số.";
        panelConfigGuard.IsVisible = true;
    }

    private void btnConfigGuardClose_Click(object? sender, RoutedEventArgs e) => panelConfigGuard.IsVisible = false;
    private void ConfigGuardBackground_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == panelConfigGuard) panelConfigGuard.IsVisible = false;
    }
    private void btnConfigGuardGoto_Click(object? sender, RoutedEventArgs e)
    {
        panelConfigGuard.IsVisible = false;
        SwitchTab(3, "Cài Đặt SDK");
    }
    #endregion

    #region Credential Picker Modal
    /// <summary>
    /// Resolves which credential to sign with — mirrors sign-web's Signing.resolveCredentialId.
    /// Returns the sole/selected credential immediately when there's 0-or-1 certs; otherwise
    /// shows a picker modal and awaits the user's choice (null = user cancelled).
    /// </summary>
    private Task<string?> ResolveCredentialIdAsync()
    {
        var certs = (cboCerts.ItemsSource as IEnumerable<ComboboxItem<string>>)?.ToList() ?? new List<ComboboxItem<string>>();
        if (certs.Count <= 1)
        {
            var selected = cboCerts.SelectedItem as ComboboxItem<string>;
            return Task.FromResult(selected?.Value);
        }

        _credentialPickerChoice = (cboCerts.SelectedItem as ComboboxItem<string>)?.Value ?? certs[0].Value;
        RenderCredentialPickerList(certs);
        panelCredentialPicker.IsVisible = true;

        _credentialPickerTcs = new TaskCompletionSource<string?>();
        return _credentialPickerTcs.Task;
    }

    private void RenderCredentialPickerList(List<ComboboxItem<string>> certs)
    {
        var items = new List<Control>();
        foreach (var cert in certs)
        {
            bool isSelected = cert.Value == _credentialPickerChoice;
            var row = new Border
            {
                Padding = new Thickness(12, 10),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new Avalonia.Media.SolidColorBrush(ColorFromHex(isSelected ? "#CFE0FB" : "#E7EAEF")),
                Background = new Avalonia.Media.SolidColorBrush(ColorFromHex(isSelected ? "#EEF4FE" : "#FFFFFF")),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Tag = cert.Value
            };
            var dot = new Ellipse
            {
                Width = 16,
                Height = 16,
                Stroke = new Avalonia.Media.SolidColorBrush(ColorFromHex(isSelected ? "#1E5FD4" : "#CDD3DC")),
                StrokeThickness = 2,
                Fill = isSelected ? new Avalonia.Media.SolidColorBrush(ColorFromHex("#1E5FD4")) : Avalonia.Media.Brushes.Transparent,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            var label = new TextBlock { Text = cert.Text, FontSize = 13, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var stack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10 };
            stack.Children.Add(dot);
            stack.Children.Add(label);
            row.Child = stack;
            row.PointerPressed += (s, e) =>
            {
                _credentialPickerChoice = cert.Value;
                RenderCredentialPickerList(certs);
            };
            items.Add(row);
        }
        lstCredentialPicker.Children.Clear();
        foreach (var item in items) lstCredentialPicker.Children.Add(item);
    }

    private void btnCredentialPickerConfirm_Click(object? sender, RoutedEventArgs e)
    {
        panelCredentialPicker.IsVisible = false;
        var chosen = _credentialPickerChoice;
        var tcs = _credentialPickerTcs;
        _credentialPickerTcs = null;
        tcs?.SetResult(chosen);
    }

    private void btnCredentialPickerCancel_Click(object? sender, RoutedEventArgs e)
    {
        panelCredentialPicker.IsVisible = false;
        var tcs = _credentialPickerTcs;
        _credentialPickerTcs = null;
        tcs?.SetResult(null);
    }
    #endregion

    #region CA Authentication Flow
    private static bool UsesLocalIdentity(string merchantId) =>
        merchantId.Equals("LOCAL", StringComparison.OrdinalIgnoreCase)
        || merchantId.Equals("USB", StringComparison.OrdinalIgnoreCase)
        || merchantId.Equals("SELF", StringComparison.OrdinalIgnoreCase);

    private bool HasActiveSessionForCurrentMerchant()
    {
        var selectedMerchant = cboMerchant.SelectedItem?.ToString();
        return _isLoggedIn
            && !string.IsNullOrWhiteSpace(_activeMerchantId)
            && string.Equals(
                selectedMerchant,
                _activeMerchantId,
                StringComparison.OrdinalIgnoreCase);
    }

    private void ClearCertificateControls()
    {
        cboCerts.ItemsSource = null;
        cboCerts.SelectedIndex = -1;
        if (cboCertsXml != null)
        {
            cboCertsXml.ItemsSource = null;
            cboCertsXml.SelectedIndex = -1;
        }
    }

    private void ApplyLoggedOutUi()
    {
        _isLoggedIn = false;
        cboMerchant.IsEnabled = true;

        var merchantId = cboMerchant.SelectedItem?.ToString() ?? "";
        txtUserName.IsEnabled = !UsesLocalIdentity(merchantId);
        txtPassword.IsEnabled = true;
        btnLogin.IsVisible = true;
        btnLogin.IsEnabled = true;
        btnSyncCertificates.IsEnabled = false;
        btnLogout.IsVisible = false;

        lblSessionStatus.Text = "Chưa đăng nhập";
        lblSessionStatus.Foreground = Avalonia.Media.Brushes.Red;
        panelStatusDot.Fill = Avalonia.Media.Brushes.Red;
        btnSession.BorderBrush = Avalonia.Media.Brushes.Red;
        btnSession.Background = new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.Parse("#11EF4444"));

        panelSessionLoggedInHeader.IsVisible = false;
        panelSessionInfoRows.IsVisible = false;
        panelSessionLoggedOutHeader.IsVisible = true;
        panelSessionLoginForm.IsVisible = true;
        btnMerchantSelector.Opacity = 1.0;
        btnMerchantSelector.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
    }

    private void ApplyLoggedInUi()
    {
        _isLoggedIn = true;
        cboMerchant.IsEnabled = false;
        txtUserName.IsEnabled = false;
        txtPassword.IsEnabled = false;
        btnLogin.IsVisible = false;
        btnLogin.IsEnabled = false;
        btnSyncCertificates.IsEnabled = true;
        btnLogout.IsVisible = true;

        lblSessionStatus.Text = $"Active Session: {_activeUserName} ({_activeMerchantId})";
        lblSessionStatus.Foreground = Avalonia.Media.Brushes.Green;
        panelStatusDot.Fill = Avalonia.Media.Brushes.Green;
        btnSession.BorderBrush = Avalonia.Media.Brushes.Green;
        btnSession.Background = new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.Parse("#1139D98A"));

        panelSessionLoggedOutHeader.IsVisible = false;
        panelSessionLoginForm.IsVisible = false;
        panelSessionLoggedInHeader.IsVisible = true;
        panelSessionInfoRows.IsVisible = true;
        btnMerchantSelector.Opacity = 0.6;
        btnMerchantSelector.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.No);

        var displayName = ExtractCN(GetActiveCertSubjectDN()) ?? _activeUserName ?? "User";
        lblSessionDisplayName.Text = displayName;
        lblSessionAvatarInitial.Text = displayName.Length > 0 ? displayName.Substring(0, 1).ToUpperInvariant() : "U";
        RefreshSessionInfoRows();
    }

    private string? GetActiveCertSubjectDN()
    {
        var selected = cboCerts.SelectedItem as ComboboxItem<string>;
        if (selected == null) return null;
        return _certSubjectsByCredentialId.TryGetValue(selected.Value, out var subj) ? subj : null;
    }

    private static string? ExtractCN(string? subjectDN)
    {
        if (string.IsNullOrWhiteSpace(subjectDN)) return null;
        int i = subjectDN.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        int start = i + 3;
        int end = subjectDN.IndexOf(',', start);
        var cn = (end > start ? subjectDN.Substring(start, end - start) : subjectDN.Substring(start)).Trim();
        return string.IsNullOrEmpty(cn) ? null : cn;
    }

    private void RefreshSessionInfoRows()
    {
        if (!_isLoggedIn) return;

        var subjectDN = GetActiveCertSubjectDN();
        var cn = ExtractCN(subjectDN);
        var displayName = cn ?? _activeUserName ?? "User";

        lblSessionDisplayName.Text = displayName;
        lblSessionAvatarInitial.Text = displayName.Length > 0 ? displayName.Substring(0, 1).ToUpperInvariant() : "U";
        lblInfoRowAccount.Text = _activeUserName ?? "—";
        lblInfoRowMerchant.Text = MerchantRegistry.GetDisplayInfo(_activeMerchantId ?? "").Name;
        lblInfoRowCert.Text = cn ?? subjectDN ?? "Chưa có chứng thư";
    }

    #region Merchant Header Selector (card-list dropdown, mirrors sign-web)
    private void RefreshMerchantHeaderDisplay()
    {
        var merchantId = cboMerchant.SelectedItem?.ToString() ?? "";
        var info = MerchantRegistry.GetDisplayInfo(merchantId);
        lblMerchantTag.Text = info.Tag;
        lblMerchantName.Text = info.Name;
    }

    private void RenderMerchantDropdownList()
    {
        var merchants = (cboMerchant.ItemsSource as IEnumerable<string>)?.ToList() ?? new List<string>();
        var currentMerchant = cboMerchant.SelectedItem?.ToString();

        merchantDropdownList.Children.Clear();
        foreach (var merchantId in merchants)
        {
            var info = MerchantRegistry.GetDisplayInfo(merchantId);
            bool isActive = string.Equals(merchantId, currentMerchant, StringComparison.OrdinalIgnoreCase);

            var row = new Border
            {
                Padding = new Thickness(10, 9),
                CornerRadius = new CornerRadius(9),
                Background = isActive
                    ? new Avalonia.Media.SolidColorBrush(ColorFromHex("#EEF4FE"))
                    : Avalonia.Media.Brushes.Transparent,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            var tagBadge = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = new Avalonia.Media.SolidColorBrush(ColorFromHex("#1A1E5FD4")),
                Child = new TextBlock { Text = info.Tag, FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 12, Foreground = new Avalonia.Media.SolidColorBrush(ColorFromHex("#1E5FD4")), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
            };
            var textStack = new StackPanel { Spacing = 1, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock { Text = info.Name, FontSize = 13, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = new Avalonia.Media.SolidColorBrush(ColorFromHex("#1A2230")) });
            textStack.Children.Add(new TextBlock { Text = info.Description, FontSize = 11, Foreground = new Avalonia.Media.SolidColorBrush(ColorFromHex("#8A93A1")) });

            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto") };
            Grid.SetColumn(tagBadge, 0);
            Grid.SetColumn(textStack, 1);
            textStack.Margin = new Thickness(10, 0, 0, 0);
            content.Children.Add(tagBadge);
            content.Children.Add(textStack);
            if (isActive)
            {
                var check = new TextBlock { Text = "✓", Foreground = new Avalonia.Media.SolidColorBrush(ColorFromHex("#1E5FD4")), FontWeight = Avalonia.Media.FontWeight.Bold, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                Grid.SetColumn(check, 2);
                content.Children.Add(check);
            }
            row.Child = content;

            row.PointerPressed += (s, e) => SelectMerchantFromDropdown(merchantId);
            merchantDropdownList.Children.Add(row);
        }
    }

    private void RenderLoginMerchantGrid()
    {
        var merchants = (cboMerchant.ItemsSource as IEnumerable<string>)?.ToList() ?? new List<string>();
        var currentMerchant = cboMerchant.SelectedItem?.ToString();

        panelLoginMerchantGrid.Children.Clear();
        foreach (var merchantId in merchants)
        {
            var info = MerchantRegistry.GetDisplayInfo(merchantId);
            bool isSelected = string.Equals(merchantId, currentMerchant, StringComparison.OrdinalIgnoreCase);

            var chip = new Border
            {
                Padding = new Thickness(8, 5),
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1.5),
                BorderBrush = new Avalonia.Media.SolidColorBrush(ColorFromHex(isSelected ? "#1E5FD4" : "#CDD3DC")),
                Background = isSelected ? new Avalonia.Media.SolidColorBrush(ColorFromHex("#0A1E5FD4")) : Avalonia.Media.Brushes.White,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = new TextBlock { Text = info.Name, FontSize = 11.5, FontWeight = Avalonia.Media.FontWeight.Medium, Foreground = new Avalonia.Media.SolidColorBrush(ColorFromHex(isSelected ? "#1E5FD4" : "#334155")) }
            };
            chip.PointerPressed += (s, e) => SelectMerchantFromDropdown(merchantId);
            panelLoginMerchantGrid.Children.Add(chip);
        }
    }

    private void SelectMerchantFromDropdown(string merchantId)
    {
        if (_isLoggedIn)
        {
            CloseMerchantDropdown();
            ToastInfo("Merchant đã khóa", "Đăng xuất để chọn merchant khác — merchant hiện tại gắn với phiên đăng nhập.");
            return;
        }
        cboMerchant.SelectedItem = merchantId;
        CloseMerchantDropdown();
    }
    #endregion

    #region Header Overlays (in-window replacements for Flyout popups)
    /// <summary>
    /// Anchors a top-aligned overlay panel just below <paramref name="anchor"/>, mirroring
    /// where a Flyout would have opened. Measured from the live control bounds rather than
    /// hard-coded offsets so it stays correct across DPI and header layout changes.
    /// </summary>
    private void PositionOverlayUnder(Control anchor, Control panel, bool alignRight)
    {
        var origin = anchor.TranslatePoint(new Point(0, anchor.Bounds.Height), this);
        if (origin is null) return;

        double top = origin.Value.Y + 6;
        panel.Margin = alignRight
            ? new Thickness(0, top, Math.Max(0, Bounds.Width - (origin.Value.X + anchor.Bounds.Width)), 0)
            : new Thickness(origin.Value.X, top, 0, 0);
    }

    private void OpenMerchantDropdown()
    {
        RenderMerchantDropdownList();
        PositionOverlayUnder(btnMerchantSelector, merchantDropdownPanel, alignRight: false);
        panelMerchantDropdown.IsVisible = true;
    }

    private void CloseMerchantDropdown() => panelMerchantDropdown.IsVisible = false;

    private void btnMerchantSelector_Click(object? sender, RoutedEventArgs e)
    {
        if (_isLoggedIn)
        {
            ToastInfo("Merchant đã khóa", "Đăng xuất để chọn merchant khác — merchant hiện tại gắn với phiên đăng nhập.");
            return;
        }

        if (panelMerchantDropdown.IsVisible) CloseMerchantDropdown();
        else OpenMerchantDropdown();
    }

    private void MerchantDropdownBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e) => CloseMerchantDropdown();

    private void btnSession_Click(object? sender, RoutedEventArgs e)
    {
        if (panelSessionFlyout.IsVisible) CloseSessionFlyout();
        else OpenSessionFlyout(focusCredentials: !_isLoggedIn);
    }

    private void SessionFlyoutBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e) => CloseSessionFlyout();

    private void CloseSessionFlyout() => panelSessionFlyout.IsVisible = false;

    /// <summary>Closes whichever header overlay is open; Escape is the expected dismissal key.</summary>
    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        if (panelMerchantDropdown.IsVisible) CloseMerchantDropdown();
        if (panelSessionFlyout.IsVisible) CloseSessionFlyout();
        if (panelPlacementDialog.IsVisible) panelPlacementDialog.IsVisible = false;
        if (panelConfigGuard.IsVisible) panelConfigGuard.IsVisible = false;
    }
    #endregion

    private void ResetSessionState(bool clearCredentials)
    {
        _bearerToken = null;
        _activeUserName = null;
        _activeMerchantId = null;
        ClearCertificateControls();

        if (clearCredentials)
        {
            txtUserName.Text = "";
            txtPassword.Text = "";
        }

        ApplyLoggedOutUi();
        UpdateSigningActionStates();
    }

    private void OpenSessionFlyout(bool focusCredentials)
    {
        PositionOverlayUnder(btnSession, sessionFlyoutPanel, alignRight: true);
        panelSessionFlyout.IsVisible = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (focusCredentials)
                {
                    if (txtUserName.IsEnabled)
                        txtUserName.Focus();
                    else
                        txtPassword.Focus();
                }
                else
                {
                    btnSyncCertificates.Focus();
                }
            },
            DispatcherPriority.Input);
    }

    private bool EnsureSigningSession()
    {
        if (!HasActiveSessionForCurrentMerchant())
        {
            LogWarning("Vui lòng đăng nhập đúng merchant trước khi ký. Vùng ký hiện tại vẫn được giữ lại.");
            OpenSessionFlyout(focusCredentials: true);
            return false;
        }

        if (cboCerts.SelectedItem is not ComboboxItem<string>)
        {
            LogWarning("Phiên đăng nhập chưa có chứng thư được chọn. Vui lòng tải chứng thư trước khi ký.");
            OpenSessionFlyout(focusCredentials: false);
            return false;
        }

        return true;
    }

    private async void btnLogin_Click(object? sender, RoutedEventArgs e)
    {
        string merchantId = cboMerchant.SelectedItem?.ToString() ?? "";
        bool isLocalOrUsb = UsesLocalIdentity(merchantId);
        string userName = isLocalOrUsb ? "" : (txtUserName.Text ?? "").Trim();
        string password = isLocalOrUsb ? "" : (txtPassword.Text ?? "");

        if (string.IsNullOrWhiteSpace(merchantId))
        {
            LogError("Vui lòng chọn merchant trước khi đăng nhập.");
            return;
        }

        if (!isLocalOrUsb && string.IsNullOrWhiteSpace(userName))
        {
            ToastWarning("Thiếu thông tin", "Vui lòng nhập tài khoản trước khi đăng nhập.");
            OpenSessionFlyout(focusCredentials: true);
            return;
        }

        // A new login attempt must never inherit a token or certificate from a
        // previous account/merchant.
        ResetSessionState(clearCredentials: false);
        cboMerchant.IsEnabled = false;
        btnLogin.IsEnabled = false;
        if (btnLoginXml != null) btnLoginXml.IsEnabled = false;

        try
        {
            LogSystem($"Attempting authentication with [{merchantId}] as {(isLocalOrUsb ? "local cert" : userName)}...");
            var result = await _signClient.LoginAsync(userName, password, merchantId, "", "");

            if (result.Success)
            {
                var resolvedUserName = string.IsNullOrWhiteSpace(result.UserName)
                    ? userName
                    : result.UserName;
                LogSystem("Retrieving merchant registration certificates...");
                var certs = await _signClient.GetCertificatesAsync(
                    resolvedUserName,
                    result.BearerToken ?? "",
                    merchantId: merchantId);

                _bearerToken = result.BearerToken;
                _activeUserName = resolvedUserName;
                _activeMerchantId = merchantId;
                PopulateCertificatesControls(certs, resolvedUserName, merchantId);
                ApplyLoggedInUi();
                UpdateSigningActionStates();

                LogSuccess("Authentication Successful. Session established.");
                if (isLocalOrUsb && !string.IsNullOrWhiteSpace(_activeUserName))
                    LogSystem($"Resolved token identity (serial): {_activeUserName}");
                ToastSuccess("Đăng nhập thành công", $"Phiên ký đã thiết lập cho {resolvedUserName} ({merchantId}).");

                CloseSessionFlyout();
            }
            else
            {
                ToastError("Đăng nhập thất bại", result.ErrorMessage ?? "Kiểm tra lại thông tin.");
                ResetSessionState(clearCredentials: false);
                OpenSessionFlyout(focusCredentials: true);
            }
        }
        catch (Exception ex)
        {
            ToastError("Lỗi kết nối", ex.Message);
            ResetSessionState(clearCredentials: false);
            OpenSessionFlyout(focusCredentials: true);
        }
        finally
        {
            if (!_isLoggedIn)
            {
                cboMerchant.IsEnabled = true;
                btnLogin.IsEnabled = true;
                if (btnLoginXml != null) btnLoginXml.IsEnabled = true;
            }
        }
    }

    private async void btnSyncCertificates_Click(object? sender, RoutedEventArgs e)
    {
        if (!HasActiveSessionForCurrentMerchant()
            || string.IsNullOrWhiteSpace(_activeUserName)
            || string.IsNullOrWhiteSpace(_activeMerchantId))
        {
            LogWarning("Vui lòng đăng nhập trước khi tải lại chứng thư.");
            OpenSessionFlyout(focusCredentials: true);
            return;
        }

        try
        {
            LogWarning($"Bypassing caches. Requesting direct certificate registration retrieval from {_activeMerchantId} server...");
            btnSyncCertificates.IsEnabled = false;
            if (btnSyncCertificatesXml != null) btnSyncCertificatesXml.IsEnabled = false;

            var certs = await _signClient.DownloadCertificatesAsync(
                _activeUserName,
                _activeMerchantId);
            PopulateCertificatesControls(certs, _activeUserName, _activeMerchantId);
            UpdateSigningActionStates();
            LogSuccess("Direct bypass certificate refresh completed.");
            ToastSuccess("Đã tải chứng thư", "Danh sách chứng thư số đã được làm mới.");
        }
        catch (Exception ex)
        {
            ToastError("Lỗi tải chứng thư", ex.Message);
            ResetSessionState(clearCredentials: false);
            OpenSessionFlyout(focusCredentials: true);
        }
        finally
        {
            if (_isLoggedIn)
            {
                btnSyncCertificates.IsEnabled = true;
                if (btnSyncCertificatesXml != null) btnSyncCertificatesXml.IsEnabled = true;
            }
        }
    }

    private void btnLogout_Click(object? sender, RoutedEventArgs e)
    {
        ResetSessionState(clearCredentials: true);
        CloseSessionFlyout();
        ToastInfo("Đã đăng xuất", "Phiên ký đã kết thúc.");
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
        _certSubjectsByCredentialId.Clear();

        if (certs != null && certs.Count > 0)
        {
            var comboItems = new List<ComboboxItem<string>>();
            var comboItemsXml = new List<ComboboxItem<string>>();
            foreach (var cert in certs)
            {
                comboItems.Add(new ComboboxItem<string>(CertDisplayText(cert), cert.credentialID));
                comboItemsXml.Add(new ComboboxItem<string>(CertDisplayText(cert), cert.credentialID));
                _certSubjectsByCredentialId[cert.credentialID] = cert.subjectDN;
            }

            cboCerts.ItemsSource = comboItems;
            cboCerts.SelectedIndex = 0;

            if (cboCertsXml != null)
            {
                cboCertsXml.ItemsSource = comboItemsXml;
                cboCertsXml.SelectedIndex = 0;
            }

            LogSuccess($"Parsed and loaded {certs.Count} verified certificates into local registry.");
            RefreshSessionInfoRows();
        }
        else
        {
            LogWarning("Authentication returned zero active certificates or scopes.");
        }
    }

    private void cboMerchant_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isRestoringMerchantSelection) return;
        if (cboMerchant.SelectedItem == null) return;
        string selectedMerchant = cboMerchant.SelectedItem.ToString()!;

        if (_isLoggedIn
            && !string.Equals(
                selectedMerchant,
                _activeMerchantId,
                StringComparison.OrdinalIgnoreCase))
        {
            ToastInfo("Merchant đã khóa", "Đăng xuất để chọn merchant khác — merchant hiện tại gắn với phiên đăng nhập.");
            _isRestoringMerchantSelection = true;
            try
            {
                cboMerchant.SelectedItem = _activeMerchantId;
            }
            finally
            {
                _isRestoringMerchantSelection = false;
            }
            return;
        }

        RefreshMerchantHeaderDisplay();
        RenderMerchantDropdownList();
        RenderLoginMerchantGrid();

        bool isBcy = string.Equals(selectedMerchant, "BCY", StringComparison.OrdinalIgnoreCase);
        panelSignAlgorithm.IsVisible = isBcy;
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
        if (!_isLoggedIn)
        {
            txtPassword.Text = "";
            ClearCertificateControls();
        }

        if (selectedMerchant.Equals("USB", StringComparison.OrdinalIgnoreCase))
        {
            EnsureAgentRunning();
        }
        else
        {
            StopAgent();
        }

        UpdateSigningActionStates();
    }
    #endregion

    #region PDF Signing Canvas & Preview
    private string? CurrentPdfPath => lstFilePath?.SelectedItem?.ToString();

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void BindSignaturePlacementToCurrentContext()
    {
        _signaturePlacementFilePath = CurrentPdfPath;
        _signaturePlacementPage = _activePageNum;
    }

    private void BindNotePlacementToCurrentContext()
    {
        _notePlacementFilePath = CurrentPdfPath;
        _notePlacementPage = _activePageNum;
    }

    private bool IsSignaturePlacementForCurrentContext() =>
        _sigW >= 10
        && _sigH >= 10
        && PathsEqual(_signaturePlacementFilePath, CurrentPdfPath)
        && _signaturePlacementPage == _activePageNum;

    private bool IsNotePlacementForCurrentContext() =>
        _noteX.HasValue
        && _noteY.HasValue
        && _noteW.HasValue
        && _noteH.HasValue
        && PathsEqual(_notePlacementFilePath, CurrentPdfPath)
        && _notePlacementPage == _activePageNum;

    private void ClearPlacementState()
    {
        _sigX = 0;
        _sigY = 0;
        _sigW = 0;
        _sigH = 0;
        _selectedSignatureFieldId = null;
        _signaturePlacementFilePath = null;
        _signaturePlacementPage = null;
        _noteX = null;
        _noteY = null;
        _noteW = null;
        _noteH = null;
        _notePlacementFilePath = null;
        _notePlacementPage = null;
        panelPlacementDialog.IsVisible = false;

        // Per-document multi-placement state — a newly loaded/selected document starts clean.
        _placedFieldIds.Clear();
        _signedFieldIds.Clear();
        _drawnPlacements.Clear();
        _detectedFieldRects.Clear();
        _fieldButtons.Clear();
        ClearDrawnPlacementOverlays();
    }

    private void UpdateSigningActionStates()
    {
        UpdateSignButtonState();

        if (btnSignXml != null)
        {
            var xmlFiles = lstXmlFilePath?.ItemsSource as IEnumerable<string>;
            bool hasSingleXml = xmlFiles?.Count(File.Exists) == 1;
            btnSignXml.IsEnabled = hasSingleXml
                && HasActiveSessionForCurrentMerchant()
                && cboCertsXml?.SelectedItem is ComboboxItem<string>;
        }
    }

    private async void btnBrowse_Click(object? sender, RoutedEventArgs e)
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Select PDF Document",
            AllowMultiple = false,
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

        // Match sign-web: loading a document does not implicitly create a signature
        // placement. The user must choose an AcroField or draw a box first.
        ClearPlacementState();
        UpdatePlacementRects();

        string pdfPath = lstFilePath.SelectedItem?.ToString() ?? "";
        if (!string.IsNullOrEmpty(pdfPath) && File.Exists(pdfPath) && chkAutoCreateAcroField.IsChecked == true)
        {
            try
            {
                var labelsJson = _appSettings?.InternalSetting?.SignerLabels;
                if (!string.IsNullOrEmpty(labelsJson))
                {
                    var labels = Newtonsoft.Json.JsonConvert.DeserializeObject<List<TextSearchFieldCreator.SignerLabel>>(labelsJson);
                    if (labels != null && labels.Count > 0)
                    {
                        LogSystem($"[AutoPlace] Scanning and auto-creating signature fields in {Path.GetFileName(pdfPath)}...");
                        var created = TextSearchFieldCreator.CreateFieldsFromLabels(pdfPath, labels);
                        if (created.Count > 0)
                        {
                            LogSuccess($"[AutoPlace] Successfully created {created.Count} signature field(s) in PDF: " +
                                string.Join(", ", created.Select(c => c.FieldName)));
                        }
                        else
                        {
                            LogSystem("[AutoPlace] No new signature fields created (they might already exist).");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogWarning($"[AutoPlace] Failed to auto-create fields: {ex.Message}");
            }
        }

        RenderPdfPage();

        // Fire-and-forget: AI vision alignment runs in the background and re-renders
        // the page if it finds and creates additional fields — mirrors sign-web's
        // triggerBackgroundVisualAlignment() call right after the initial upload render.
        if (!string.IsNullOrEmpty(pdfPath) && File.Exists(pdfPath) && chkAutoCreateAcroField.IsChecked == true)
        {
            _ = AlignFieldsVisuallyAsync(pdfPath);
        }
    }

    private void RenderPdfPage()
    {
        try
        {
            string pdfPath = lstFilePath.SelectedItem?.ToString() ?? "";

            if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
            {
                imgPdfPage.Source = null;
                panelEmptyState.IsVisible = true;
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
                        var rawBytes = pageReader.GetImage(
                            Docnet.Core.Models.RenderFlags.RenderAnnotations
                            | (Docnet.Core.Models.RenderFlags)0x800);  // FPDF_PRINTING: renders form fields including signatures

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
                        panelEmptyState.IsVisible = false;

                        LogSystem($"Rendered actual PDF Page {_activePageNum}/{_totalPdfPages} successfully!");
                    }
                }
            }

            // Detect and show clickable signature form fields
            DetectSignatureFields(pdfPath);
        }
        catch (Exception ex)
        {
            LogWarning($"Note: Render actual PDF content failed (using fallback design layout): {ex.Message}");
            imgPdfPage.Source = null;
            panelEmptyState.IsVisible = true;
        }

        UpdatePlacementRects();
    }

    private void DetectSignatureFields(string pdfPath)
    {
        try
        {
            // Clear previous form field overlays
            icFormFields.ItemsSource = null;
            var children = panelSigPlacementMock.Children
                .OfType<Avalonia.Controls.Button>()
                .Where(b => b.Tag is string s && s == "FormField")
                .ToList();
            foreach (var c in children) panelSigPlacementMock.Children.Remove(c);
            _fieldButtons.Clear();
            _detectedFieldRects.Clear();

            using var reader = new iText.Kernel.Pdf.PdfReader(pdfPath);
            using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);
            var acroForm = iText.Forms.PdfAcroForm.GetAcroForm(pdfDoc, false);
            if (acroForm == null) return;

            var fields = acroForm.GetFormFields();
            int pageNum = _activePageNum;

            foreach (var kvp in fields)
            {
                var field = kvp.Value;
                // Look for signature fields (empty ones that can be signed)
                if (field is not iText.Forms.Fields.PdfSignatureFormField sigField) continue;

                var widgets = sigField.GetWidgets();
                if (widgets == null || widgets.Count == 0) continue;

                foreach (var widget in widgets)
                {
                    var page = widget.GetPage();
                    if (page == null) continue;
                    int fieldPage = pdfDoc.GetPageNumber(page);
                    if (fieldPage != pageNum) continue;

                    var rect = widget.GetRectangle()?.ToRectangle();
                    if (rect == null || rect.GetWidth() < 1 || rect.GetHeight() < 1) continue;

                    // Convert PDF coordinates (bottom-left origin) to canvas (top-left origin)
                    float x = rect.GetLeft();
                    float y = _pageH - rect.GetTop();
                    float w = rect.GetWidth();
                    float h = rect.GetHeight();

                    string fieldName = kvp.Key ?? "Signature";
                    bool isSignedInPdf = sigField.GetValue() != null;

                    _detectedFieldRects[fieldName] = (fieldPage, (int)x, (int)rect.GetBottom(), (int)w, (int)h);

                    var btn = new Avalonia.Controls.Button
                    {
                        Width = w,
                        Height = h,
                        FontSize = Math.Min(11, h * 0.4),
                        Tag = "FormField",
                        Opacity = 0.85,
                        BorderThickness = new Thickness(2),
                        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    };

                    if (!isSignedInPdf)
                    {
                        float capturedX = x, capturedY = rect.GetBottom(), capturedW = w, capturedH = h;
                        btn.Click += (s, e) => OpenPlacementDialogForField(fieldName, pageNum, (int)capturedX, (int)capturedY, (int)capturedW, (int)capturedH);
                    }

                    Avalonia.Controls.Canvas.SetLeft(btn, x);
                    Avalonia.Controls.Canvas.SetTop(btn, y);
                    panelSigPlacementMock.Children.Add(btn);
                    _fieldButtons[fieldName] = btn;
                    ApplyFieldButtonStyle(fieldName, btn, isSignedInPdf);
                }
            }
        }
        catch (Exception ex)
        {
            // Silently ignore — form field detection is optional
            LogWarning($"Form field detection: {ex.Message}");
        }

        RefreshDrawnPlacementOverlays();
    }

    /// <summary>
    /// Colors/labels a field button by its 3-state status — mirrors sign-web's
    /// renderFieldOverlays: signed (green) &gt; placed/pending (blue) &gt; available (orange).
    /// </summary>
    private void ApplyFieldButtonStyle(string fieldName, Button btn, bool isSignedInPdf)
    {
        bool isSigned = isSignedInPdf || _signedFieldIds.Contains(fieldName);
        bool isPlaced = !isSigned && _placedFieldIds.Contains(fieldName);

        if (isSigned)
        {
            btn.Content = $"✅ {fieldName}";
            btn.Background = Avalonia.Media.Brushes.LightGreen;
            btn.BorderBrush = Avalonia.Media.Brushes.Green;
        }
        else if (isPlaced)
        {
            btn.Content = $"● Đã đặt: {fieldName}";
            btn.Background = new Avalonia.Media.SolidColorBrush(ColorFromHex("#1E1E5FD4"));
            btn.BorderBrush = new Avalonia.Media.SolidColorBrush(ColorFromHex("#1E5FD4"));
        }
        else
        {
            btn.Content = $"✍️ Ký tại đây: {fieldName}";
            btn.Background = Avalonia.Media.Brushes.LightYellow;
            btn.BorderBrush = Avalonia.Media.Brushes.Orange;
        }
    }

    private void UpdateFieldButtonVisual(string fieldName)
    {
        if (_fieldButtons.TryGetValue(fieldName, out var btn))
        {
            ApplyFieldButtonStyle(fieldName, btn, isSignedInPdf: false);
        }
    }

    private void ClearDrawnPlacementOverlays()
    {
        foreach (var overlay in _drawnPlacementOverlays) panelSigPlacementMock.Children.Remove(overlay);
        _drawnPlacementOverlays.Clear();
    }

    /// <summary>Renders a persistent blue box for every queued (not-yet-signed) drawn placement.</summary>
    private void RefreshDrawnPlacementOverlays()
    {
        ClearDrawnPlacementOverlays();
        int i = 1;
        foreach (var box in _drawnPlacements.Where(b => b.Page == _activePageNum))
        {
            var overlay = new Border
            {
                Width = box.W,
                Height = box.H,
                Background = new Avalonia.Media.SolidColorBrush(ColorFromHex("#221E5FD4")),
                BorderBrush = new Avalonia.Media.SolidColorBrush(ColorFromHex("#1E5FD4")),
                BorderThickness = new Thickness(2),
                Child = new TextBlock { Text = $"● Ô ký vẽ tay #{i}", Foreground = new Avalonia.Media.SolidColorBrush(ColorFromHex("#1E5FD4")), FontSize = 10, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(5) }
            };
            Avalonia.Controls.Canvas.SetLeft(overlay, box.X);
            Avalonia.Controls.Canvas.SetTop(overlay, _pageH - box.Y - box.H);
            panelSigPlacementMock.Children.Add(overlay);
            _drawnPlacementOverlays.Add(overlay);
            i++;
        }
    }

    private void OpenPlacementDialogForField(string fieldName, int page, int x, int y, int w, int h)
    {
        _selectedSignatureFieldId = fieldName;
        _sigX = x;
        _sigY = y;
        _sigW = w;
        _sigH = h;
        UpdatePlacementRects();
        LogSystem($"Đã chọn vùng ký từ form field '{fieldName}': X={_sigX}, Y={_sigY}, W={_sigW}, H={_sigH}");

        bool alreadyPlaced = _placedFieldIds.Contains(fieldName);
        btnConfirmPlace.Content = alreadyPlaced
            ? "Bỏ chọn ô chữ ký (hủy đặt)"
            : "Đặt ô ký (ký sau)";
        lblPlacementCoords.Text = $"Trang {_activePageNum}, X={_sigX}, Y={_sigY}, W={_sigW}, H={_sigH} (Field: {fieldName})";
        panelPlacementDialog.IsVisible = true;
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

        UpdateSignButtonState();
    }

    private void UpdateSignButtonState()
    {
        if (btnSign == null) return;

        var files = lstFilePath?.ItemsSource as IEnumerable<string>;
        bool hasDocument = files?.Any(File.Exists) == true;
        int placedCount = _placedFieldIds.Count + _drawnPlacements.Count;
        btnSign.IsEnabled = hasDocument && placedCount > 0;
        btnSign.Content = placedCount > 0
            ? $"KÝ SỐ TÀI LIỆU PDF ({placedCount} vị trí)"
            : "KÝ SỐ TÀI LIỆU PDF";
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
            _selectedSignatureFieldId = null;
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

            btnConfirmPlace.Content = "Đặt ô ký (ký sau)";
            lblPlacementCoords.Text = $"Trang {_activePageNum}, X={_sigX}, Y={_sigY}, W={_sigW}, H={_sigH}";
            panelPlacementDialog.IsVisible = true;
        }

        UpdatePlacementRects();
    }

    private async void btnConfirmSignNow_Click(object? sender, RoutedEventArgs e)
    {
        panelPlacementDialog.IsVisible = false;
        await SignImmediateAsync();
    }

    private void btnConfirmPlace_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedSignatureFieldId != null)
        {
            var fieldId = _selectedSignatureFieldId;
            if (!_placedFieldIds.Remove(fieldId))
            {
                _placedFieldIds.Add(fieldId);
                LogSystem($"Đặt ô ký: {fieldId}");
            }
            else
            {
                LogSystem($"Bỏ đặt ô ký: {fieldId}");
            }
            UpdateFieldButtonVisual(fieldId);
        }
        else if (_sigW >= 10 && _sigH >= 10)
        {
            _drawnPlacements.Add(new DrawnPlacement { Page = _activePageNum, X = _sigX, Y = _sigY, W = _sigW, H = _sigH });
            LogSystem($"Đặt ô ký vẽ tay: X={_sigX}, Y={_sigY}, W={_sigW}, H={_sigH}");
            RefreshDrawnPlacementOverlays();
        }

        _sigX = 0;
        _sigY = 0;
        _sigW = 0;
        _sigH = 0;
        _selectedSignatureFieldId = null;
        panelPlacementDialog.IsVisible = false;
        UpdatePlacementRects();
        UpdateSignButtonState();
    }

    private void btnCancelPlacement_Click(object? sender, RoutedEventArgs e)
    {
        _sigX = 0;
        _sigY = 0;
        _sigW = 0;
        _sigH = 0;
        _selectedSignatureFieldId = null;
        panelPlacementDialog.IsVisible = false;
        UpdatePlacementRects();
    }

    private void Background_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        panelPlacementDialog.IsVisible = false;
    }

    private void SwitchTab(int index, string viewName)
    {
        tabMain.SelectedIndex = index;
        LogSystem($"Switched context view to: {viewName}");
    }

    // Drag-and-drop file loading support
    public void Canvas_DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    public void Canvas_Drop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        if (files != null)
        {
            var firstFile = files.FirstOrDefault();
            if (firstFile != null)
            {
                var path = firstFile.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        LogSystem($"Dropped PDF detected: {Path.GetFileName(path)}. Loading...");
                        lstFilePath.ItemsSource = new List<string> { path };
                        lstFilePath.SelectedIndex = 0;
                        _activePageNum = 1;
                        RenderPdfPage();
                        SwitchTab(0, "Ký PDF");
                    }
                    else if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        LogSystem($"Dropped XML detected: {Path.GetFileName(path)}. Loading...");
                        lstXmlFilePath.ItemsSource = new List<string> { path };
                        lstXmlFilePath.SelectedIndex = 0;
                        SwitchTab(1, "Ký XML");
                    }
                    else
                    {
                        LogWarning("Dropped file is not a supported format (.pdf or .xml).");
                    }
                }
            }
        }
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

    private List<PendingPlacement> BuildAccumulatedPlacements()
    {
        var result = new List<PendingPlacement>();
        foreach (var fieldId in _placedFieldIds)
        {
            if (_detectedFieldRects.TryGetValue(fieldId, out var rect))
            {
                result.Add(new PendingPlacement { FieldId = fieldId, Page = rect.page, X = rect.x, Y = rect.y, W = rect.w, H = rect.h });
            }
        }
        foreach (var box in _drawnPlacements)
        {
            result.Add(new PendingPlacement { FieldId = null, Page = box.Page, X = box.X, Y = box.Y, W = box.W, H = box.H });
        }
        return result;
    }

    private string ResolveOutputFolder(string filePath)
    {
        if (!string.IsNullOrEmpty(txtPdfOutputDir.Text)) return txtPdfOutputDir.Text;

        var parentDir = Path.GetDirectoryName(filePath)!;
        return string.Equals(Path.GetFileName(parentDir), "Signed", StringComparison.OrdinalIgnoreCase)
            ? parentDir
            : Path.Combine(parentDir, "Signed");
    }

    private static byte[] ResolveSignedBytes(string? signedFileUrl)
    {
        if (!string.IsNullOrEmpty(signedFileUrl) && File.Exists(signedFileUrl))
            return File.ReadAllBytes(signedFileUrl);

        if (!string.IsNullOrWhiteSpace(signedFileUrl))
        {
            string base64Data = signedFileUrl.Contains(",") ? signedFileUrl.Split(',').Last() : signedFileUrl;
            return Convert.FromBase64String(base64Data);
        }

        throw new InvalidOperationException("MySign returned success without signed PDF data.");
    }

    /// <summary>
    /// Signs one or more placements onto the same PDF, one SDK call per placement,
    /// re-uploading the progressively-signed bytes each time so multiple signatures
    /// stack onto a single output file — mirrors sign-web's WebSigningService.SignPdfAsync.
    /// </summary>
    private async Task<(bool success, int signedCount, string? outputPath, string? error)> SignPlacementsSequentially(
        string filePath, string credentialId, List<PendingPlacement> placements)
    {
        string merchantId = cboMerchant.SelectedItem?.ToString() ?? "";
        string userName = _activeUserName ?? (txtUserName.Text ?? "");

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var base64 = Convert.ToBase64String(fileBytes);

        var outFolder = ResolveOutputFolder(filePath);
        if (!Directory.Exists(outFolder)) Directory.CreateDirectory(outFolder);
        var ts = DateTime.Now.ToString("yyMMddHHmmss");
        var outPath = Path.Combine(outFolder, $"{ts}_{Path.GetFileNameWithoutExtension(filePath)}_signed{Path.GetExtension(filePath)}");

        int signed = 0;
        foreach (var placement in placements)
        {
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
                Page = placement.Page,
                X = placement.X,
                Y = placement.Y,
                Width = placement.W,
                Height = placement.H,
                SignatureId = placement.FieldId,
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
                return (signed > 0, signed, signed > 0 ? outPath : null, "No response from server.");

            if (!result.Success)
            {
                LogError($"Signature Rejected by Remote Gateway: {result.ErrorMessage}");
                return (signed > 0, signed, signed > 0 ? outPath : null, result.ErrorMessage);
            }

            byte[] signedBytes;
            try
            {
                signedBytes = ResolveSignedBytes(result.SignedFileUrl);
            }
            catch (Exception ex)
            {
                return (signed > 0, signed, signed > 0 ? outPath : null, ex.Message);
            }

            await File.WriteAllBytesAsync(outPath, signedBytes);
            base64 = Convert.ToBase64String(signedBytes);
            LogSuccess($"Signature Registered! Server Transaction ID: {result.TransactionId}");
            signed++;

            if (placement.FieldId != null)
            {
                _placedFieldIds.Remove(placement.FieldId);
                _signedFieldIds.Add(placement.FieldId);
            }
        }

        LogSuccess($"Signed output deployed successfully to: {outPath}");
        return (true, signed, outPath, null);
    }

    /// <summary>Points the active file list at the freshly-signed output and re-renders in place.</summary>
    private void ReplaceActiveFileWithSignedOutput(string outputPath)
    {
        _sigX = 0;
        _sigY = 0;
        _sigW = 0;
        _sigH = 0;
        _selectedSignatureFieldId = null;
        _noteW = 0;
        _noteH = 0;

        int currentPage = _activePageNum;

        // Temporarily detach selection changed to prevent it from resetting page index to 1
        lstFilePath.SelectionChanged -= lstFilePath_SelectionChanged;
        lstFilePath.ItemsSource = new List<string> { outputPath };
        lstFilePath.SelectedIndex = 0;
        lstFilePath.SelectionChanged += lstFilePath_SelectionChanged;

        _activePageNum = currentPage;
        RenderPdfPage();
    }

    /// <summary>"Ký ngay vị trí này" — signs only the just-selected/drawn placement immediately,
    /// independent of any other placements already queued for the batch sign button.</summary>
    private async Task SignImmediateAsync()
    {
        if (_sigW < 10 || _sigH < 10) return;

        var filePath = CurrentPdfPath;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            ToastError("Không thể ký", "Không tìm thấy tệp PDF hiện tại.");
            return;
        }

        string merchantId = cboMerchant.SelectedItem?.ToString() ?? "";
        if (!IsMerchantConfigured(merchantId))
        {
            ShowConfigGuard(merchantId);
            return;
        }

        var credentialId = await ResolveCredentialIdAsync();
        if (credentialId == null)
        {
            LogWarning("Đã hủy chọn chứng thư ký.");
            return;
        }

        var placement = new PendingPlacement { FieldId = _selectedSignatureFieldId, Page = _activePageNum, X = _sigX, Y = _sigY, W = _sigW, H = _sigH };

        btnSign.IsEnabled = false;
        ShowSigningProgress(true, merchantId);
        try
        {
            var (success, _, outputPath, error) = await SignPlacementsSequentially(filePath, credentialId, new List<PendingPlacement> { placement });
            if (success && outputPath != null)
            {
                ToastSuccess("Ký số thành công", $"Đã ký tại vị trí X={placement.X}, Y={placement.Y}.");
                ReplaceActiveFileWithSignedOutput(outputPath);
            }
            else
            {
                ToastError("Ký số thất bại", error ?? "Đã xảy ra lỗi không xác định.");
            }
        }
        catch (Exception ex)
        {
            ToastError("Lỗi ký số", ex.Message);
        }
        finally
        {
            ShowSigningProgress(false);
            _sigX = 0;
            _sigY = 0;
            _sigW = 0;
            _sigH = 0;
            _selectedSignatureFieldId = null;
            UpdateSignButtonState();
        }
    }

    private async void btnSign_Click(object? sender, RoutedEventArgs e)
    {
        var files = lstFilePath.ItemsSource as List<string>;
        if (files == null || files.Count == 0)
        {
            LogError("No PDF file selected or specified.");
            UpdateSignButtonState();
            return;
        }

        var placements = BuildAccumulatedPlacements();
        if (placements.Count == 0)
        {
            ToastWarning("Chưa chọn vị trí ký", "Vui lòng chọn AcroField hoặc vẽ một vùng ký trước khi ký số.");
            UpdateSignButtonState();
            return;
        }

        string signMerchantId = cboMerchant.SelectedItem?.ToString() ?? "";
        if (!IsMerchantConfigured(signMerchantId))
        {
            ShowConfigGuard(signMerchantId);
            return;
        }

        var credentialId = await ResolveCredentialIdAsync();
        if (credentialId == null)
        {
            LogWarning("Đã hủy chọn chứng thư ký.");
            return;
        }

        btnSign.IsEnabled = false;
        try
        {
            string? lastSignedPath = null;
            int totalSigned = 0;

            ShowSigningProgress(true, signMerchantId);
            try
            {
                foreach (var filePath in files)
                {
                    LogSystem($"Processing signing for: {Path.GetFileName(filePath)} ({placements.Count} vị trí)...");
                    try
                    {
                        var (success, signedCount, outputPath, error) = await SignPlacementsSequentially(filePath, credentialId, placements);
                        totalSigned += signedCount;
                        if (outputPath != null && File.Exists(outputPath))
                        {
                            lastSignedPath = outputPath;
                        }
                        if (!success)
                        {
                            LogError($"Signature failed for {Path.GetFileName(filePath)}: {error}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Signature Generation Exception: {ex.Message}");
                    }
                }
            }
            finally
            {
                ShowSigningProgress(false);
            }

            if (!string.IsNullOrEmpty(lastSignedPath))
            {
                LogSuccess($"Signature execution finished successfully. {totalSigned} placement(s) signed.");
                ToastSuccess("Ký số thành công", $"Đã ký {totalSigned} vị trí trên tài liệu.");
                _drawnPlacements.Clear();
                ReplaceActiveFileWithSignedOutput(lastSignedPath);
            }
            else
            {
                ToastError("Ký số thất bại", "Không có tệp nào được ký thành công. Xem chi tiết trong Nhật ký hệ thống.");
            }
        }
        finally
        {
            UpdateSignButtonState();
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

    private async void btnBrowseXmlOutput_Click(object? sender, RoutedEventArgs e)
    {
        var options = new FolderPickerOpenOptions
        {
            Title = "Chọn thư mục xuất file XML đã ký"
        };
        var folders = await this.StorageProvider.OpenFolderPickerAsync(options);
        if (folders != null && folders.Count > 0)
        {
            txtXmlOutputDir.Text = folders[0].Path.LocalPath;
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
            var nsmgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");
            
            LogSystem($"=== Phân tích: {Path.GetFileName(filePath)} ===");

            // Detect document type
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
                LogSystem($"Phần tử có Id=\"...\" ({idNodes.Count} tìm thấy) — chọn làm ReferenceId:");
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

                if ((isTongKet || isLyLich || isHocBa) && firstDataId != null)
                {
                    cboXmlReferenceId.ItemsSource = refIds;
                    cboXmlReferenceId.SelectedIndex = refIds.IndexOf(firstDataId);
                }
                else
                {
                    cboXmlReferenceId.ItemsSource = refIds;
                    cboXmlReferenceId.SelectedIndex = firstDataId != null ? refIds.IndexOf(firstDataId) : 0;
                }
            }
            else
            {
                LogWarning("  Không tìm thấy phần tử nào có Id=\"...\" — ReferenceId để trống sẽ ký cả tài liệu.");
                cboXmlReferenceId.ItemsSource = refIds;
                cboXmlReferenceId.SelectedIndex = 0;
            }

            if (isHocBa)
            {
                LogSystem("Loại: HOC_BA — chọn XPath vị trí theo vai trò ký:");

                var xpathGvbm      = "//*[local-name()='DANH_SACH_THONG_TIN_KY']//*[local-name()='GVBM'][not(*)][1]";
                var xpathGvcn      = "//*[local-name()='DANH_SACH_THONG_TIN_KY']/*[local-name()='GVCN']";
                var xpathCbql      = "//*[local-name()='PHAT_HANH_HOC_BA']//*[local-name()='CBQL'][not(*)][1]";
                var xpathKyPhatHanh = "//*[local-name()='PHAT_HANH_HOC_BA']/*[local-name()='KY_PHAT_HANH']";

                parentXPaths.Add(xpathCbql);
                parentXPaths.Add(xpathKyPhatHanh);
                parentXPaths.Add(xpathGvcn);
                parentXPaths.Add(xpathGvbm);

                _xpathRefMap[xpathGvcn]       = "thongtinhocba";
                _xpathRefMap[xpathCbql]       = "data";
                _xpathRefMap[xpathKyPhatHanh] = "data";

                // Build per-GVBM XPath entries
                var gvbmSlotNodes = doc.SelectNodes(
                    "//*[local-name()='DANH_SACH_THONG_TIN_KY']/*[local-name()='DANH_SACH_GVBM']/*[local-name()='GVBM']");
                var gvbmPairList = new List<(string teacherId, string diemId)>();
                if (gvbmSlotNodes?.Count > 0)
                {
                    foreach (System.Xml.XmlNode gvbmNode in gvbmSlotNodes)
                    {
                        var gvbmEl = gvbmNode as System.Xml.XmlElement;
                        if (gvbmEl == null) continue;
                        string teacherId = gvbmEl.GetAttribute("Id");
                        if (string.IsNullOrEmpty(teacherId)) continue;
                        var diemEl = doc.SelectSingleNode(
                            $"//*[local-name()='DIEM_TONG_KET'][@Id][.//*[local-name()='GVBM'][@Id='{teacherId}']]")
                            as System.Xml.XmlElement;
                        if (diemEl == null) continue;
                        string diemId = diemEl.GetAttribute("Id");
                        if (string.IsNullOrEmpty(diemId)) continue;
                        string xpathSlot = $"//*[local-name()='DANH_SACH_THONG_TIN_KY']/*[local-name()='DANH_SACH_GVBM']/*[local-name()='GVBM'][@Id='{teacherId}']";
                        parentXPaths.Add(xpathSlot);
                        _xpathRefMap[xpathSlot] = diemId;
                        gvbmPairList.Add((teacherId, diemId));
                    }
                }

                // Default to CBQL
                cboXmlParentXPath.ItemsSource = parentXPaths;
                cboXmlParentXPath.SelectedItem = xpathCbql;
                cboXmlSignTag.ItemsSource = knownTags;
                cboXmlSignTag.SelectedItem = "CBQL";

                // Set ReferenceId to "data" for CBQL
                cboXmlReferenceId.SelectedItem = "data";

                LogSystem($"  CBQL (cán bộ quản lý)   : {xpathCbql}  → RefID=data  ← mặc định (NEAC-safe)");
                LogSystem($"  KY_PHAT_HANH (phát hành): {xpathKyPhatHanh}  → RefID=data");
                LogSystem($"  GVCN (chủ nhiệm)        : {xpathGvcn}  → RefID=thongtinhocba");
                LogSystem($"  GVBM (giáo viên bộ môn) : {xpathGvbm}  → RefID=Id học sinh (chọn cụ thể bên dưới)");
                if (gvbmPairList.Count > 0)
                {
                    LogSystem($"  GVBM cá nhân ({gvbmPairList.Count} cặp — chọn từ dropdown XPath, RefID tự động cập nhật):");
                    int gi = 1;
                    foreach (var (tId, dId) in gvbmPairList)
                        LogSystem($"    [{gi++}] GVBM Id={tId}  →  RefID={dId}");
                }
                LogWarning("Lưu ý NEAC: Chữ ký phải đặt NGOÀI phần tử được tham chiếu. CBQL nằm ngoài DU_LIEU_HOC_BA (#data) → NEAC xác minh được.");
            }
            else if (isTongKet)
            {
                LogSystem("Loại: BCY (TONG_KET_CA_NAM) → Thẻ Ký để TRỐNG, ReferenceId = Id của TONG_KET_CA_NAM.");
                cboXmlSignTag.ItemsSource = knownTags;
                cboXmlSignTag.SelectedItem = "";
                cboXmlParentXPath.ItemsSource = parentXPaths;
                cboXmlParentXPath.SelectedIndex = 0;
            }
            else if (isLyLich)
            {
                LogSystem("Loại: Lý Lịch (THONG_TIN) → Thẻ Ký để TRỐNG, ReferenceId = Id của THONG_TIN.");
                cboXmlSignTag.ItemsSource = knownTags;
                cboXmlSignTag.SelectedItem = "";
                cboXmlParentXPath.ItemsSource = parentXPaths;
                cboXmlParentXPath.SelectedIndex = 0;
            }
            else
            {
                LogWarning("Không nhận diện được loại tài liệu — tự chọn SignTag và ReferenceId phù hợp.");
                cboXmlSignTag.ItemsSource = knownTags;
                cboXmlSignTag.SelectedIndex = 0;
                cboXmlParentXPath.ItemsSource = parentXPaths;
                cboXmlParentXPath.SelectedIndex = 0;
            }

            // Report existing signatures
            var existingSigs = doc.SelectNodes("//ds:Signature", nsmgr);
            if (existingSigs?.Count > 0)
            {
                LogWarning($"Cảnh báo: tệp đã có {existingSigs.Count} chữ ký — ký lại sẽ THÊM chữ ký mới, không xóa chữ ký cũ.");
                foreach (System.Xml.XmlElement sig in existingSigs)
                {
                    string sigId = sig.GetAttribute("Id");
                    var signerNode = sig.SelectSingleNode(".//ds:X509SubjectName", nsmgr) as System.Xml.XmlElement;
                    string signerInfo = signerNode?.InnerText ?? "(không rõ)";
                    int cnIdx = signerInfo.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
                    if (cnIdx >= 0)
                    {
                        int start = cnIdx + 3;
                        int end = signerInfo.IndexOf(',', start);
                        signerInfo = end > start ? signerInfo.Substring(start, end - start) : signerInfo.Substring(start);
                    }
                    LogWarning($"  Chữ ký [{(string.IsNullOrEmpty(sigId) ? "?" : sigId.Substring(0, Math.Min(8, sigId.Length)))}...] — người ký: {signerInfo}");
                }
            }
            else
            {
                LogSuccess("Chưa có chữ ký — tệp sẵn sàng ký.");
            }
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

        string xmlMerchantId = cboMerchant.SelectedItem?.ToString() ?? "";
        if (!IsMerchantConfigured(xmlMerchantId))
        {
            ShowConfigGuard(xmlMerchantId);
            return;
        }

        try
        {
            btnSignXml.IsEnabled = false;
            ShowSigningProgress(true, xmlMerchantId);

            string userName = _activeUserName ?? (txtUserNameXml.Text ?? "");
            string credentialId = (cboCertsXml.SelectedItem as ComboboxItem<string>)!.Value;
            string merchantId = xmlMerchantId;

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
                    string outFolder;
                    if (!string.IsNullOrWhiteSpace(txtXmlOutputDir.Text) && Directory.Exists(txtXmlOutputDir.Text))
                    {
                        outFolder = txtXmlOutputDir.Text;
                    }
                    else
                    {
                        var parentDir = Path.GetDirectoryName(originalFilePath)!;
                        outFolder = Path.Combine(parentDir, "Signed");
                    }
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
            if (successCount > 0)
                ToastSuccess("Ký XML thành công", $"Đã ký {successCount} tệp XML thành công.");
            else
                ToastError("Ký XML thất bại", "Không có tệp nào được ký thành công. Xem chi tiết trong Nhật ký hệ thống.");
        }
        catch (Exception ex)
        {
            ToastError("Lỗi ký XML", ex.Message);
        }
        finally
        {
            btnSignXml.IsEnabled = true;
            ShowSigningProgress(false);
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

            // SELF merchant always signs with whatever is provisioned as
            // CertificateSetting.DefaultUserName/{serial}.pfx under WebRootPath/certs,
            // regardless of what's passed to LoginAsync's certFileName/certPassword params.
            // So we provision the user-picked PFX there first, matching Program.cs's
            // proven-working --test-e2e flow, instead of stuffing the cert bytes into
            // UserName/CredentialID (which SignDocumentsAsync never reads that way — it
            // always requires a prior LoginAsync session for the given user/merchant).
            const string batchUser = "BatchSignUser";
            var env = Program.Host!.Services.GetRequiredService<Core.Common.Abstractions.ISignEnvironment>();
            string certsFolder = Path.Combine(env.WebRootPath, "certs");
            Directory.CreateDirectory(certsFolder);
            string serial = UtilSigner.ConvertStringToNumber(batchUser);
            string pfxDestPath = Path.Combine(certsFolder, $"{serial}.pfx");
            File.Copy(certPath, pfxDestPath, overwrite: true);

            _appSettings.CertificateSetting ??= new CertificateSetting();
            _appSettings.CertificateSetting.DefaultUserName = batchUser;
            _appSettings.CertificateSetting.DefaultPassword = certPass;

            var loginResult = await _signClient.LoginAsync(batchUser, certPass, "SELF", "", "");
            if (!loginResult.Success)
            {
                LogError($"Đăng nhập Self CA thất bại: {loginResult.ErrorMessage}");
                return;
            }

            var certs = await _signClient.GetCertificatesAsync(loginResult.UserName, loginResult.BearerToken ?? "", "", "SELF");
            var cert = certs?.FirstOrDefault();
            if (cert == null)
            {
                LogError("Không lấy được chứng thư từ file PFX đã chọn.");
                return;
            }

            LogSystem($"Bắt đầu ký hàng loạt {pdfFiles.Length} files bằng Self CA...");
            batchProgressBar.Value = 0;

            int success = 0;
            for (int i = 0; i < pdfFiles.Length; i++)
            {
                string file = pdfFiles[i];
                lblBatchStatus.Text = $"Đang ký: {Path.GetFileName(file)}";

                try
                {
                    var docBytes = await File.ReadAllBytesAsync(file);
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
                        UserName = batchUser,
                        CredentialID = cert.credentialID,
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
                    else
                    {
                        LogError($"Lỗi ký file {Path.GetFileName(file)}: {res?.ErrorMessage ?? "Không nhận được phản hồi từ dịch vụ ký số."}");
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

        // BCY
        txtBcyUrl.Text = _appSettings.TerminalSetting?.BaseUrl ?? "";
        txtBcyRelyingParty.Text = _appSettings.TerminalSetting?.RelyingParty ?? "";
        txtBcySignAlgorithm.Text = _appSettings.TerminalSetting?.SignAlgorithm ?? "ECDSA";

        // CMC
        txtCmcUrl.Text = _appSettings.CmcSetting?.BaseUrl ?? "";
        txtCmcProfileId.Text = _appSettings.CmcSetting?.SigningProfileId ?? "";
        txtCmcKeyAuth.Text = _appSettings.CmcSetting?.KeyAuth ?? "";

        // InTrust
        txtInTrustUrl.Text = _appSettings.InTrustSetting?.BaseUrl ?? "";
        txtInTrustBasicAuth.Text = _appSettings.InTrustSetting?.BasicAuthorization ?? "";

        // SIM (MSSP)
        txtSimApId.Text = _appSettings.MsspSetting?.ApId ?? "";
        txtSimApPassword.Text = _appSettings.MsspSetting?.ApPassword ?? "";
        txtSimMsspId.Text = _appSettings.MsspSetting?.MsspId ?? "";

        // USB
        txtUsbAgentIp.Text = _appSettings.UsbSetting?.UsbAgentIp ?? "127.0.0.1";
        txtUsbAgentPort.Text = _appSettings.UsbSetting?.UsbAgentPort.ToString() ?? "9999";
        txtUsbAgentExePath.Text = _appSettings.UsbSetting?.UsbAgentExePath ?? "";
    }

    /// <summary>
    /// Real HTTP connectivity probe for a merchant's Base URL — mirrors sign-web's
    /// SettingsController.TestConnection (GET, headers-only, report the status code).
    /// </summary>
    private async void TestConnectionClicked(TextBox urlBox, TextBlock statusLabel)
    {
        string url = urlBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            statusLabel.Text = "URL không hợp lệ.";
            statusLabel.Foreground = new Avalonia.Media.SolidColorBrush(ColorFromHex("#CC4238"));
            return;
        }

        statusLabel.Text = "Đang kiểm tra kết nối...";
        statusLabel.Foreground = new Avalonia.Media.SolidColorBrush(ColorFromHex("#64748B"));
        try
        {
            using var response = await _testConnectionHttp.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            var msg = $"Kết nối thành công (HTTP {(int)response.StatusCode}).";
            statusLabel.Text = msg;
            statusLabel.Foreground = new Avalonia.Media.SolidColorBrush(ColorFromHex("#0F9D6E"));
            ToastSuccess("Kết nối thành công", msg);
        }
        catch (Exception ex)
        {
            var msg = $"Không thể kết nối: {ex.Message}";
            statusLabel.Text = msg;
            statusLabel.Foreground = new Avalonia.Media.SolidColorBrush(ColorFromHex("#CC4238"));
            ToastError("Kết nối thất bại", msg);
        }
    }

    private void btnTestMySign_Click(object? sender, RoutedEventArgs e) => TestConnectionClicked(txtMySignUrl, lblMySignTestStatus);
    private void btnTestSmartCA_Click(object? sender, RoutedEventArgs e) => TestConnectionClicked(txtSmartCAUrl, lblSmartCATestStatus);
    private void btnTestBcy_Click(object? sender, RoutedEventArgs e) => TestConnectionClicked(txtBcyUrl, lblBcyTestStatus);
    private void btnTestCmc_Click(object? sender, RoutedEventArgs e) => TestConnectionClicked(txtCmcUrl, lblCmcTestStatus);
    private void btnTestInTrust_Click(object? sender, RoutedEventArgs e) => TestConnectionClicked(txtInTrustUrl, lblInTrustTestStatus);
    private void btnTestSim_Click(object? sender, RoutedEventArgs e) => TestConnectionClicked(txtSimApId, lblSimTestStatus);

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

            if (_appSettings.TerminalSetting == null) _appSettings.TerminalSetting = new();
            _appSettings.TerminalSetting.BaseUrl = txtBcyUrl.Text;
            _appSettings.TerminalSetting.RelyingParty = txtBcyRelyingParty.Text;
            _appSettings.TerminalSetting.SignAlgorithm = txtBcySignAlgorithm.Text;

            if (_appSettings.CmcSetting == null) _appSettings.CmcSetting = new();
            _appSettings.CmcSetting.BaseUrl = txtCmcUrl.Text;
            _appSettings.CmcSetting.SigningProfileId = txtCmcProfileId.Text;
            _appSettings.CmcSetting.KeyAuth = txtCmcKeyAuth.Text;

            if (_appSettings.InTrustSetting == null) _appSettings.InTrustSetting = new();
            _appSettings.InTrustSetting.BaseUrl = txtInTrustUrl.Text;
            _appSettings.InTrustSetting.BasicAuthorization = txtInTrustBasicAuth.Text;

            if (_appSettings.MsspSetting == null) _appSettings.MsspSetting = new();
            _appSettings.MsspSetting.ApId = txtSimApId.Text;
            _appSettings.MsspSetting.ApPassword = txtSimApPassword.Text;
            _appSettings.MsspSetting.MsspId = txtSimMsspId.Text;

            if (_appSettings.UsbSetting == null) _appSettings.UsbSetting = new();
            _appSettings.UsbSetting.UsbAgentIp = txtUsbAgentIp.Text;
            if (int.TryParse(txtUsbAgentPort.Text, out int port)) _appSettings.UsbSetting.UsbAgentPort = port;
            _appSettings.UsbSetting.UsbAgentExePath = txtUsbAgentExePath.Text;

            // Persist to the same higher-precedence user config that Program loads.
            // VMSIGN_CONFIG_DIR lets E2E/portable runs isolate this state safely.
            string configPath = Program.UserConfigFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            if (!File.Exists(configPath)) File.WriteAllText(configPath, "{}");

            var pathsToSave = new List<string> { configPath };

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
            ToastSuccess("Đã lưu cài đặt", "Cấu hình nhà cung cấp ký số đã được lưu.");
        }
        catch (Exception ex)
        {
            ToastError("Lưu thất bại", ex.Message);
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
                agentExe = Path.Combine(appDir, "VMSignAgent");
                if (!File.Exists(agentExe))
                {
                    // Check parent directories or common bundle location
                    agentExe = "/Applications/VMSignAgent.app/Contents/MacOS/VMSignAgent";
                }
            }
            else
            {
                // Windows binary
                agentExe = Path.Combine(appDir, "VMSignAgent.exe");
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
            LogSystem($"[USB Agent] Started VMSignAgent (PID {_agentProcess?.Id}).");
        }
        catch (Exception ex)
        {
            LogError($"[USB Agent] Failed to start VMSignAgent: {ex.Message}");
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

    #region AI Vision Field Alignment (Gemini)
    /// <summary>
    /// Background pass that visually re-scans the rendered page image with Gemini vision
    /// and creates any missing signature fields the text-search pass couldn't find —
    /// mirrors sign-web's WebSigningService.AlignFieldsVisually / GeminiLayoutService.
    /// Opt-in via InternalSetting.VertexAiVerification, same gate as the web sample.
    /// </summary>
    private async Task AlignFieldsVisuallyAsync(string pdfPath)
    {
        try
        {
            if (_appSettings?.InternalSetting?.VertexAiVerification != 1) return;

            var apiKey = _configuration?["GeminiSetting:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("YOUR_GEMINI_API_KEY"))
            {
                LogSystem("[AI Align] Bỏ qua: chưa cấu hình GeminiSetting:ApiKey.");
                return;
            }

            var labelsJson = _appSettings?.InternalSetting?.SignerLabels;
            if (string.IsNullOrEmpty(labelsJson)) return;
            var labels = JsonConvert.DeserializeObject<List<TextSearchFieldCreator.SignerLabel>>(labelsJson);
            if (labels == null || labels.Count == 0) return;

            if (_renderedPageImage == null) return;

            LogSystem("[AI Align] Đang chạy AI căn chỉnh vị trí ký tự động ở nền...");

            var model = _configuration?["GeminiSetting:Model"] ?? "gemini-2.5-flash";
            byte[] imageBytes;
            using (var ms = new MemoryStream())
            {
                // Encode the already-rendered page via Avalonia/Skia (cross-platform,
                // no System.Drawing dependency) instead of re-rendering to BMP.
                _renderedPageImage.Save(ms);
                imageBytes = ms.ToArray();
            }
            if (imageBytes.Length == 0) return;

            var blocks = await DetectSignatureBlocksWithGeminiAsync(imageBytes, apiKey, model);
            if (blocks == null || blocks.Count == 0)
            {
                LogSystem("[AI Align] AI không phát hiện thêm vùng ký nào.");
                return;
            }

            int created = CreateMissingFieldsFromAiBlocks(pdfPath, labels, blocks);
            if (created > 0)
            {
                LogSuccess($"[AI Align] Đã tự động tạo thêm {created} vùng ký từ AI.");
                ToastSuccess("Layout hoàn tất", "Đã tự động căn chỉnh vị trí ký bằng AI.");
                if (PathsEqual(pdfPath, CurrentPdfPath)) RenderPdfPage();
            }
            else
            {
                LogSystem("[AI Align] AI quét xong, không có vùng ký mới cần tạo.");
            }
        }
        catch (Exception ex)
        {
            LogWarning($"[AI Align] Bỏ qua do lỗi: {ex.Message}");
        }
    }

    private sealed class GeminiDetectionResult
    {
        [Newtonsoft.Json.JsonProperty("label")]
        public string Label { get; set; } = "";

        // [ymin, xmin, ymax, xmax], normalized 0–1000 from top-left
        [Newtonsoft.Json.JsonProperty("box_2d")]
        public int[] Box2d { get; set; } = Array.Empty<int>();
    }

    private static async Task<List<GeminiDetectionResult>> DetectSignatureBlocksWithGeminiAsync(byte[] imageBytes, string apiKey, string model)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
        var base64Image = Convert.ToBase64String(imageBytes);

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new
                        {
                            text = "You are an expert document layout analysis engine. Analyze this page from a Vietnamese document.\n" +
                                   "Locate all signature areas (the blank spaces where signatures/stamps are placed, usually below signer titles like 'NGƯỜI LẬP BẢNG', 'KẾ TOÁN', 'GIÁM ĐỐC', etc.).\n" +
                                   "For each signature area, output a JSON object with 'label' (the title/role) and 'box_2d' (bounding box [ymin, xmin, ymax, xmax] normalized on a scale of 0 to 1000 from top-left [0,0]).\n" +
                                   "Respond ONLY with a valid JSON array, do not include markdown blocks or explanation."
                        },
                        new { inlineData = new { mimeType = "image/png", data = base64Image } }
                    }
                }
            },
            generationConfig = new { responseMimeType = "application/json" }
        };

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var jsonPayload = JsonConvert.SerializeObject(payload);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(url, content);
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return new List<GeminiDetectionResult>();

        dynamic? respObj = JsonConvert.DeserializeObject(responseBody);
        string rawText = respObj?.candidates[0].content.parts[0].text ?? "[]";
        rawText = rawText.Trim();
        if (rawText.StartsWith("```json")) rawText = rawText.Substring(7);
        if (rawText.EndsWith("```")) rawText = rawText.Substring(0, rawText.Length - 3);
        rawText = rawText.Trim();

        return JsonConvert.DeserializeObject<List<GeminiDetectionResult>>(rawText) ?? new List<GeminiDetectionResult>();
    }

    /// <summary>
    /// Creates AcroFields for AI-detected blocks that don't already match a text-search-created
    /// field, with the same collision-avoidance nudging as TextSearchFieldCreator.
    /// </summary>
    private int CreateMissingFieldsFromAiBlocks(string pdfPath, List<TextSearchFieldCreator.SignerLabel> labels, List<GeminiDetectionResult> blocks)
    {
        string tmpPath = pdfPath + ".ai.tmp";
        int createdCount = 0;
        try
        {
            using (var reader = new iText.Kernel.Pdf.PdfReader(pdfPath))
            using (var writer = new iText.Kernel.Pdf.PdfWriter(tmpPath))
            using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader, writer))
            {
                var acroForm = iText.Forms.PdfAcroForm.GetAcroForm(pdfDoc, true);
                int lastPage = pdfDoc.GetNumberOfPages();
                var pageObj = pdfDoc.GetPage(lastPage);
                double pageWidth = pageObj.GetPageSize().GetWidth();
                double pageHeight = pageObj.GetPageSize().GetHeight();
                var textRects = TextSearchFieldCreator.ExtractAllTextRects(pageObj);

                foreach (var block in blocks)
                {
                    if (block.Box2d == null || block.Box2d.Length < 4) continue;

                    var matchedLabel = labels.Find(l =>
                        l.Label.ToLower().Contains(block.Label.ToLower())
                        || block.Label.ToLower().Contains(l.Label.ToLower()));

                    string fieldName = matchedLabel?.FieldName ?? $"sig_ai_{Guid.NewGuid().ToString().Substring(0, 4)}";
                    float desiredWidth = matchedLabel?.Width ?? 150f;
                    float desiredHeight = matchedLabel?.Height ?? 60f;

                    if (acroForm.GetField(fieldName) != null) continue;

                    float ymin = block.Box2d[0], xmin = block.Box2d[1], ymax = block.Box2d[2], xmax = block.Box2d[3];
                    float aiCenterX = (float)((xmin + xmax) / 2000f * pageWidth);
                    float aiCenterY = (float)((1000f - (ymin + ymax) / 2f) / 1000f * pageHeight);
                    float fieldX = aiCenterX - (desiredWidth / 2f);
                    float fieldY = aiCenterY - (desiredHeight / 2f);

                    var (nudgedX, nudgedY) = TextSearchFieldCreator.NudgeToAvoidCollision(
                        fieldX, fieldY, desiredWidth, desiredHeight, textRects, (float)pageWidth, (float)pageHeight);

                    var sigField = iText.Forms.Fields.PdfFormField.CreateSignature(pdfDoc,
                        new iText.Kernel.Geom.Rectangle(nudgedX, nudgedY, desiredWidth, desiredHeight));
                    sigField.SetFieldName(fieldName);
                    acroForm.AddField(sigField, pageObj);
                    createdCount++;
                }
            }

            if (createdCount > 0)
            {
                File.Delete(pdfPath);
                File.Move(tmpPath, pdfPath);
            }
            else if (File.Exists(tmpPath))
            {
                File.Delete(tmpPath);
            }
        }
        catch
        {
            if (File.Exists(tmpPath)) { try { File.Delete(tmpPath); } catch { } }
            throw;
        }
        return createdCount;
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
