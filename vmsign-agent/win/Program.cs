using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VMSignAgent;

// ── VMSignAgent ────────────────────────────────────────────────────────────
// Local HTTP service (net4.6.1) that exposes Windows CAPI / USB token signing
// as a hash-then-sign API. Targets .NET Framework 4.6.1 so no runtime install
// is needed on Windows 10/11 workstations (framework is pre-installed).
//
// HTTP endpoints (HttpListener, no ASP.NET Core):
//   POST /login     — find cert in Windows Personal store by serial / CN
//   POST /certs     — list all certs with private keys
//   POST /signHash  — sign a pre-computed SHA-256 digest
//
// LAN discovery: UDP broadcast responder on DiscoveryPort (9998).
// Cross-subnet:  optional MQTT responder (set Mqtt:BrokerHost in app.config).
//
// Configuration: app.config <appSettings> — see the file for all keys.
// ─────────────────────────────────────────────────────────────────────────────

static string Cfg(string key) => ConfigurationManager.AppSettings[key] ?? string.Empty;
static string? Opt(string key) { var v = Cfg(key); return string.IsNullOrWhiteSpace(v) ? null : v; }
static bool    CfgBool(string key) => Cfg(key).Equals("true", StringComparison.OrdinalIgnoreCase);

int port          = int.TryParse(Cfg("Port"),          out int _p)  ? _p  : 9999;
int discoveryPort = int.TryParse(Cfg("DiscoveryPort"), out int _dp) ? _dp : 9998;
string? tokenPin  = Opt("Token:Pin");
string? pkcs11Mod = Opt("Token:Pkcs11Module");
string? mqttHost  = Opt("Mqtt:BrokerHost");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var listener = new HttpListener();
listener.Prefixes.Add($"http://127.0.0.1:{port}/");
try { listener.Start(); }
catch (Exception ex)
{
    Console.Error.WriteLine($"[USB Agent] Cannot start HTTP listener on port {port}: {ex.Message}");
    Console.Error.WriteLine("  Tip: run once as admin, or register the URL with:");
    Console.Error.WriteLine($"  netsh http add urlacl url=http://+:{port}/ user={Environment.UserName}");
    return;
}
Console.WriteLine($"[USB Agent] HTTP  http://localhost:{port}/");

var tasks = new List<Task>
{
    HttpLoop(listener, tokenPin, pkcs11Mod, cts.Token),
    UdpDiscovery(discoveryPort, port, cts.Token),
};

if (mqttHost != null)
{
    int mqttPort = int.TryParse(Cfg("Mqtt:BrokerPort"), out int _mp) ? _mp : 1883;
    var tls = new MqttTlsConfig
    {
        UseTls            = CfgBool("Mqtt:UseTls"),
        CaCertPath        = Opt("Mqtt:CaCertPath"),
        ClientPfxPath     = Opt("Mqtt:ClientPfxPath"),
        ClientPfxPassword = Opt("Mqtt:ClientPfxPassword"),
        AllowUntrusted    = CfgBool("Mqtt:AllowUntrusted"),
    };
    var mqtt = new MqttSigningResponder(
        mqttHost, mqttPort,
        Opt("Mqtt:Username"), Opt("Mqtt:Password"),
        tls.UseTls,
        Opt("Mqtt:AgentId") ?? Dns.GetHostName(),
        port, tls, tokenPin);
    tasks.Add(mqtt.RunAsync(cts.Token));
    Console.WriteLine($"[USB Agent] MQTT  {mqttHost}:{mqttPort}");
}

await Task.WhenAll(tasks);
listener.Stop();

// ── HTTP loop ─────────────────────────────────────────────────────────────────
static async Task HttpLoop(HttpListener listener, string? tokenPin, string? pkcs11Mod, CancellationToken ct)
{
    ct.Register(() => { try { listener.Stop(); } catch { } });
    while (listener.IsListening)
    {
        HttpListenerContext ctx;
        try   { ctx = await listener.GetContextAsync(); }
        catch { break; }
        _ = Task.Run(() => HandleHttp(ctx, tokenPin, pkcs11Mod));
    }
}

static async Task HandleHttp(HttpListenerContext ctx, string? tokenPin, string? pkcs11Mod)
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
            var r    = JsonConvert.DeserializeObject<LoginReq>(body) ?? new LoginReq();
            var cert = TokenSigner.FindCert(r.Serial, r.UserName);
            result = cert == null
                ? (object)Err("Certificate not found in Windows Personal store")
                : TokenSigner.ToInfo(cert);
            break;
        }
        case "/certs":
            result = new { Success = true, Certs = TokenSigner.ListCerts() };
            break;
        case "/signhash":
        {
            var r = JsonConvert.DeserializeObject<SignHashReq>(body) ?? new SignHashReq();
            if (string.IsNullOrWhiteSpace(r.HashBase64)) { result = Err("hashBase64 is required"); break; }
            var cert = TokenSigner.FindCert(r.Serial, null);
            if (cert == null) { result = Err("Certificate not found in Windows Personal store"); break; }
            byte[] digest;
            try   { digest = Convert.FromBase64String(r.HashBase64!); }
            catch { result = Err("hashBase64 is not valid base64"); break; }
            var pin = !string.IsNullOrEmpty(r.Pin) ? r.Pin : tokenPin;
            try
            {
                var sr2 = !string.IsNullOrEmpty(pin)
                    ? Pkcs11Signer.SignDigest(cert, digest, pin!, pkcs11Mod)
                    : TokenSigner.SignDigest(cert, digest, null);
                result = new
                {
                    Success           = true,
                    SignatureBase64   = Convert.ToBase64String(sr2.Signature),
                    CertificateBase64 = Convert.ToBase64String(sr2.CertRawData),
                    sr2.Algorithm,
                };
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

static object Err(string msg) => new { Success = false, Error = msg };

// ── UDP discovery responder ────────────────────────────────────────────────────
static async Task UdpDiscovery(int discoveryPort, int httpPort, CancellationToken ct)
{
    using var udp = new UdpClient();
    udp.EnableBroadcast = true;
    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    try { udp.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort)); }
    catch (Exception ex)
    {
        Console.WriteLine($"[USB Agent] UDP discovery disabled: {ex.Message}");
        return;
    }
    Console.WriteLine($"[USB Agent] UDP   discovery port {discoveryPort}");
    using var reg = ct.Register(() => { try { udp.Close(); } catch { } });
    while (!ct.IsCancellationRequested)
    {
        UdpReceiveResult result;
        try   { result = await udp.ReceiveAsync(); }
        catch { break; }
        var msg = Encoding.UTF8.GetString(result.Buffer).Trim();
        if (!msg.StartsWith("VIMES-USB-DISCOVER", StringComparison.OrdinalIgnoreCase)) continue;
        try
        {
            var reply = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(TokenSigner.BuildDiscoveryReply(httpPort)));
            await udp.SendAsync(reply, reply.Length, result.RemoteEndPoint);
        }
        catch (Exception ex) { Console.WriteLine($"[USB Agent] UDP reply error: {ex.Message}"); }
    }
}

// ── HTTP request models ───────────────────────────────────────────────────────
class LoginReq   { public string? Serial { get; set; } public string? UserName { get; set; } }
class SignHashReq { public string? HashBase64 { get; set; } public string? Serial { get; set; } public string? Pin { get; set; } }
