using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using VMSignAgent;

// VMSignAgent
// Local HTTP service (net4.6.1) that exposes Windows CAPI / USB token signing
// as a hash-then-sign API with System Tray icon (like Unikey).
//
// HTTP endpoints (HttpListener):
//   POST /login     - find cert in Windows Personal store by serial / CN
//   POST /certs     - list all certs with private keys
//   POST /signHash  - sign a pre-computed SHA-256 digest
//
// LAN discovery: UDP broadcast responder on DiscoveryPort (9998).
// Cross-subnet:  optional MQTT responder (set Mqtt:BrokerHost in app.config).

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

var trayApp = new TrayApplication();
Application.Run(trayApp);

// System Tray Application
class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _tasks = new();
    private HttpListener? _listener;
    private string _status = "Starting...";
    private int _signCount = 0;

    public TrayApplication()
    {
        // Create tray icon
        _trayIcon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "VMSignAgent",
            Visible = true,
            ContextMenuStrip = CreateMenu(),
        };
        _trayIcon.DoubleClick += (_, __) => ShowStatus();

        if (!PromptForEndUserSettingsIfNeeded())
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            ExitThread();
            return;
        }

        // Start services on a background thread
        Task.Run(() => StartServices());
    }

    private ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Status", null, (_, __) => ShowStatus());
        menu.Items.Add("Certificates", null, (_, __) => ShowCerts());
        menu.Items.Add("Settings", null, (_, __) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, __) => ExitApp());
        return menu;
    }

    private void ShowStatus()
    {
        var selectedSerial = ConfigurationManager.AppSettings["Token:SelectedCertificateSerial"];
        var certs = TokenSigner.ListCerts(selectedSerial);
        var msg = $"VMSignAgent\n" +
                  $"---------------------\n" +
                  $"Status: {_status}\n" +
                  $"Certificates: {certs.Count}\n" +
                  $"Signs completed: {_signCount}\n";
        MessageBox.Show(msg, "VMSignAgent", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowCerts()
    {
        var selectedSerial = ConfigurationManager.AppSettings["Token:SelectedCertificateSerial"];
        var certs = TokenSigner.ListCerts(selectedSerial);
        if (certs.Count == 0)
        {
            var message = string.IsNullOrWhiteSpace(selectedSerial)
                ? "No certificates found in Windows Personal store.\n\nMake sure USB Token is plugged in and driver is installed."
                : "The selected certificate was not found in Windows Personal store.\n\nOpen Settings and load certificates again.";
            MessageBox.Show(message,
                "Certificates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"Found {certs.Count} certificate(s):\n");
        foreach (var c in certs)
        {
            sb.AppendLine($"- {c.SubjectDN}");
            sb.AppendLine($"  Serial: {c.Serial}");
            sb.AppendLine($"  Algorithm: {c.Algorithm}");
            sb.AppendLine();
        }
        MessageBox.Show(sb.ToString(), "Certificates", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowSettings()
    {
        var form = new SettingsForm();
        form.ShowDialog();
    }

    private bool PromptForEndUserSettingsIfNeeded()
    {
        string Cfg(string key) => ConfigurationManager.AppSettings[key] ?? string.Empty;
        var mqttEnabled = !string.IsNullOrWhiteSpace(Cfg("Mqtt:BrokerHost"));
        var missingEndUser = string.IsNullOrWhiteSpace(Cfg("EndUser:PhoneNumber")) ||
            string.IsNullOrWhiteSpace(Cfg("Token:Pin"));

        if (!mqttEnabled || !missingEndUser)
        {
            return true;
        }

        MessageBox.Show(
            "Please enter the end-user phone number and USB Token PIN before using VMSignAgent MQTT signing.",
            "VMSignAgent First Setup",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        using var form = new SettingsForm(requireEndUser: true);
        form.ShowDialog();
        ConfigurationManager.RefreshSection("appSettings");

        missingEndUser = string.IsNullOrWhiteSpace(Cfg("EndUser:PhoneNumber")) ||
            string.IsNullOrWhiteSpace(Cfg("Token:Pin"));
        if (!missingEndUser)
        {
            return true;
        }

        MessageBox.Show(
            "VMSignAgent cannot start MQTT signing until phone number and PIN are configured.",
            "VMSignAgent First Setup",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        return false;
    }

    private void ExitApp()
    {
        _cts.Cancel();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        try { _listener?.Stop(); } catch { }
        Application.Exit();
    }

    private async Task StartServices()
    {
        try
        {
            string Cfg(string key) => ConfigurationManager.AppSettings[key] ?? string.Empty;
            string? Opt(string key) { var v = Cfg(key); return string.IsNullOrWhiteSpace(v) ? null : v; }
            bool CfgBool(string key) => Cfg(key).Equals("true", StringComparison.OrdinalIgnoreCase);

            int port = int.TryParse(Cfg("Port"), out int _p) ? _p : 9999;
            int discoveryPort = int.TryParse(Cfg("DiscoveryPort"), out int _dp) ? _dp : 9998;
            string? endUserPhoneNumber = Opt("EndUser:PhoneNumber");
            string? tokenPin = Opt("Token:Pin");
            string? selectedCertificateSerial = Opt("Token:SelectedCertificateSerial");
            string? pkcs11Mod = Opt("Token:Pkcs11Module");
            string? mqttHost = Opt("Mqtt:BrokerHost");

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try { _listener.Start(); }
            catch (Exception ex)
            {
                UpdateStatus($"HTTP Error: {ex.Message}");
                ShowBalloon("Cannot start", $"Port {port}: {ex.Message}", ToolTipIcon.Error);
                return;
            }

            _tasks.Add(HttpLoop(_listener, tokenPin, pkcs11Mod, selectedCertificateSerial, _cts.Token));
            _tasks.Add(UdpDiscovery(discoveryPort, port, selectedCertificateSerial, _cts.Token));

            var statusParts = new List<string> { $"HTTP :{port}" };

            if (mqttHost != null)
            {
                int mqttPort = int.TryParse(Cfg("Mqtt:BrokerPort"), out int _mp) ? _mp : 1883;
                var tls = new MqttTlsConfig
                {
                    UseTls = CfgBool("Mqtt:UseTls"),
                    CaCertPath = Opt("Mqtt:CaCertPath"),
                    ClientPfxPath = Opt("Mqtt:ClientPfxPath"),
                    ClientPfxPassword = Opt("Mqtt:ClientPfxPassword"),
                    AllowUntrusted = CfgBool("Mqtt:AllowUntrusted"),
                };
                var mqtt = new MqttSigningResponder(
                    mqttHost, mqttPort,
                    Opt("Mqtt:Username"), Opt("Mqtt:Password"),
                    tls.UseTls,
                    Opt("Mqtt:AgentId") ?? Dns.GetHostName(),
                    port, tls, tokenPin, endUserPhoneNumber, selectedCertificateSerial, pkcs11Mod, NotifySignSuccess);
                _tasks.Add(mqtt.RunAsync(_cts.Token));
                statusParts.Add($"MQTT {mqttHost}:{mqttPort}");
            }

            var certs = TokenSigner.ListCerts(selectedCertificateSerial);
            UpdateStatus($"Running - {string.Join(", ", statusParts)}");
            ShowBalloon("VMSignAgent", $"Running on port {port}\n{certs.Count} certificate(s) found", ToolTipIcon.Info);

            await Task.WhenAll(_tasks);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Error: {ex.Message}");
        }
    }

    private void UpdateStatus(string status)
    {
        _status = status;
        if (_trayIcon != null)
        {
            try
            {
                _trayIcon.Text = $"VMSignAgent: {(status.Length > 48 ? status.Substring(0, 48) + "..." : status)}";
            }
            catch { }
        }
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        try { _trayIcon.ShowBalloonTip(3000, title, text, icon); }
        catch { }
    }

    public void IncrementSignCount() => Interlocked.Increment(ref _signCount);

    private void NotifySignSuccess(string? serial)
    {
        IncrementSignCount();
        if (!CfgBool("Ui:ShowSignSuccessToast", defaultValue: true))
        {
            return;
        }

        var message = string.IsNullOrWhiteSpace(serial)
            ? "Document signed successfully."
            : $"Document signed successfully.\nCertificate: {serial}";
        ShowBalloon("VMSignAgent", message, ToolTipIcon.Info);
    }

    private static bool CfgBool(string key, bool defaultValue)
    {
        var value = ConfigurationManager.AppSettings[key];
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    // HTTP loop
    private async Task HttpLoop(HttpListener listener, string? tokenPin, string? pkcs11Mod, string? selectedCertificateSerial, CancellationToken ct)
    {
        ct.Register(() => { try { listener.Stop(); } catch { } });
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => HandleHttp(ctx, tokenPin, pkcs11Mod, selectedCertificateSerial));
        }
    }

    private async Task HandleHttp(HttpListenerContext ctx, string? tokenPin, string? pkcs11Mod, string? selectedCertificateSerial)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        res.ContentType = "application/json";
        string body;
        using (var sr = new StreamReader(req.InputStream, Encoding.UTF8))
            body = await sr.ReadToEndAsync();

        object result;
        switch (req.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant())
        {
            case "/login":
            {
                var r = JsonConvert.DeserializeObject<LoginReq>(body) ?? new LoginReq();
                var cert = TokenSigner.FindCert(r.Serial, r.UserName);
                result = cert == null
                    ? (object)Err("Certificate not found in Windows Personal store")
                    : TokenSigner.ToInfo(cert);
                break;
            }
            case "/certs":
                result = new { Success = true, Certs = TokenSigner.ListCerts(selectedCertificateSerial) };
                break;
            case "/signhash":
            {
                var r = JsonConvert.DeserializeObject<SignHashReq>(body) ?? new SignHashReq();
                if (string.IsNullOrWhiteSpace(r.HashBase64)) { result = Err("hashBase64 is required"); break; }
                var serialToUse = string.IsNullOrWhiteSpace(selectedCertificateSerial) ? r.Serial : selectedCertificateSerial;
                var cert = TokenSigner.FindCert(serialToUse, null);
                if (cert == null) { result = Err("Certificate not found in Windows Personal store"); break; }
                byte[] digest;
                try { digest = Convert.FromBase64String(r.HashBase64!); }
                catch { result = Err("hashBase64 is not valid base64"); break; }
                var pin = !string.IsNullOrEmpty(r.Pin) ? r.Pin : tokenPin;
                try
                {
                    var sr2 = TokenSigner.SignDigestPreferred(cert, digest, pin, pkcs11Mod);
                    result = new
                    {
                        Success = true,
                        SignatureBase64 = Convert.ToBase64String(sr2.Signature),
                        CertificateBase64 = Convert.ToBase64String(sr2.CertRawData),
                        sr2.Algorithm,
                    };
                    NotifySignSuccess(cert.SerialNumber);
                }
                catch (Exception ex) { result = Err($"sign failed: {ex.Message}"); break; }
                break;
            }
            default:
                res.StatusCode = 404;
                result = Err("Not found");
                break;
        }

        var json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result));
        res.ContentLength64 = json.Length;
        try
        {
            await res.OutputStream.WriteAsync(json, 0, json.Length);
            res.Close();
        }
        catch { /* client disconnected */ }
    }

    private static object Err(string msg) => new { Success = false, Error = msg };

    // UDP discovery responder
    private static async Task UdpDiscovery(int discoveryPort, int httpPort, string? selectedCertificateSerial, CancellationToken ct)
    {
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        try { udp.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort)); }
        catch { return; }
        using var reg = ct.Register(() => { try { udp.Close(); } catch { } });
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await udp.ReceiveAsync(); }
            catch { break; }
            var msg = Encoding.UTF8.GetString(result.Buffer).Trim();
            if (!msg.StartsWith("VIMES-USB-DISCOVER", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var reply = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(TokenSigner.BuildDiscoveryReply(httpPort, selectedCertificateSerial)));
                await udp.SendAsync(reply, reply.Length, result.RemoteEndPoint);
            }
            catch { }
        }
    }

    // Create a simple icon programmatically
    private static Icon CreateIcon()
    {
        // Try to load from file first
        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var iconPath = Path.Combine(Path.GetDirectoryName(exePath) ?? ".", "usb-agent.ico");
        if (File.Exists(iconPath))
            return new Icon(iconPath);

        // Generate a simple USB-key icon programmatically
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            // Key body (green rectangle)
            g.FillRectangle(Brushes.ForestGreen, 2, 4, 12, 8);
            // Key teeth
            g.FillRectangle(Brushes.White, 4, 6, 2, 4);
            g.FillRectangle(Brushes.White, 8, 6, 2, 4);
            // USB connector
            g.FillRectangle(Brushes.Gray, 12, 5, 3, 6);
            // Border
            g.DrawRectangle(Pens.DarkGreen, 2, 4, 11, 7);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}

// HTTP request models
class LoginReq { public string? Serial { get; set; } public string? UserName { get; set; } }
class SignHashReq { public string? HashBase64 { get; set; } public string? Serial { get; set; } public string? Pin { get; set; } }
