using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Net.Pkcs11Interop.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VMSignAgent;

/// <summary>
/// Test Sign: proves, one link at a time, that a document can be signed with this token
/// the way the hospital software signs it.
///
///   1. Token   - sign a random digest locally and verify it against the certificate.
///   2. Server  - log in to the signing API (mid=USB).
///   3. MQTT    - credentials/list: the server finds this agent over MQTT and checks phone+PIN.
///   4. Sign    - sign/multi with a generated PDF; the server sends the hash back to this agent
///                over MQTT, embeds the signature and returns the signed file.
///
/// Each step stops the run on failure and says which link broke, because "USB signing does
/// not work" can mean a wrong PIN, a broker the server cannot reach, or a phone number that
/// matches no agent - and from the hospital software those all look alike.
///
/// Step 1 runs first on purpose: a wrong PIN costs one of the token's few retries before it
/// locks, and the server would spend another one in step 4. Stopping at step 1 spends one.
/// </summary>
internal sealed class SignTestRunner
{
    private const string Merchant = "USB";
    private const string SignaturePath = "/api/v1/Signature";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private readonly Action<string> _log;

    public SignTestRunner(Action<string> log)
    {
        _log = log;
    }

    /// <param name="mqttConnected">The running agent's broker link, as the tray last saw it.</param>
    /// <returns>Path of the signed PDF, or null when a step failed.</returns>
    public async Task<string?> RunAsync(bool? mqttConnected, CancellationToken ct)
    {
        // Settings as saved. The running agent read them at startup, so its MQTT side matches
        // unless they were changed without a restart - step 3 then fails on phone/PIN.
        // The PIN is used verbatim: Settings stores it untrimmed and MQTT auth compares ordinally.
        var phone = AgentConfig.Get("EndUser:PhoneNumber").Trim();
        var pin = AgentConfig.Get("Token:Pin");
        var selectedSerial = AgentConfig.Get("Token:SelectedCertificateSerial").Trim();
        var pkcs11Module = AgentConfig.Get("Token:Pkcs11Module").Trim();
        var apiUrl = NormalizeApiUrl(AgentConfig.Get("SignApi:BaseUrl"));
        var mqttHost = AgentConfig.Get("Mqtt:BrokerHost").Trim();

        try
        {
            var cert = await TestTokenAsync(pin, selectedSerial, pkcs11Module);

            _log("");
            _log($"2/4  Signing server  {apiUrl ?? "(not set)"}");
            if (apiUrl == null)
                throw new SignTestException("no valid API URL in Settings",
                    "Open Settings and fill in Signing Server > API URL, e.g. http://10.0.0.5:8081");
            if (string.IsNullOrEmpty(mqttHost))
                throw new SignTestException("MQTT is not configured in this agent",
                    "The server reaches USB tokens only over MQTT. Fill in the MQTT section of Settings.");
            if (string.IsNullOrEmpty(phone))
                throw new SignTestException("no phone number in Settings",
                    "The server finds this agent by its phone number. Fill in End-user > Phone Number.");

            var identity = new
            {
                user_name = phone,
                password = pin,
                ip = LocalIp(),
                mid = Merchant,
            };

            await PostAsync(apiUrl, "/login", identity, ct);
            _log("     OK   login accepted");

            _log("");
            _log("3/4  Server -> MQTT broker -> this agent");
            if (mqttConnected == false)
                _log($"     WARN this agent is not connected to its broker ({mqttHost}); the server will not find it");
            var credentialId = await FindCredentialAsync(apiUrl, identity, cert, ct);

            _log("");
            _log("4/4  Sign a test PDF");
            return await SignPdfAsync(apiUrl, identity, credentialId, phone, cert, ct);
        }
        catch (SignTestException ex)
        {
            _log("     FAIL " + ex.Message);
            if (!string.IsNullOrEmpty(ex.Hint))
                _log("     ->   " + ex.Hint);
            return null;
        }
        catch (OperationCanceledException)
        {
            _log("     Cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            _log("     FAIL " + ex.GetBaseException().Message);
            return null;
        }
    }

    // ── Step 1 ──────────────────────────────────────────────────────────────────────────

    private async Task<X509Certificate2> TestTokenAsync(string pin, string selectedSerial, string pkcs11Module)
    {
        _log("1/4  USB token");
        if (string.IsNullOrEmpty(pin))
            throw new SignTestException("no PIN in Settings",
                "The server always sends a PIN with a USB sign request. Fill in End-user > USB Token PIN.");

        var cert = await Task.Run(() => TokenSigner.FindCert(NullIfEmpty(selectedSerial), null));
        if (cert == null)
            throw new SignTestException(string.IsNullOrEmpty(selectedSerial)
                    ? "no certificate found"
                    : $"selected certificate {selectedSerial} not found",
                "Plug in the token, then pick the certificate again in Settings.");

        _log($"     Certificate: {cert.GetNameInfo(X509NameType.SimpleName, false)}  serial {cert.SerialNumber}");
        _log($"     Valid {cert.NotBefore:yyyy-MM-dd} to {cert.NotAfter:yyyy-MM-dd}");
        if (DateTime.Now > cert.NotAfter)
            _log("     WARN the certificate has expired; the server may still sign, but the signature will not validate");
        else if (DateTime.Now < cert.NotBefore)
            _log("     WARN the certificate is not valid yet");

        var digest = new byte[32];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(digest);

        SignResult result;
        var watch = Stopwatch.StartNew();
        try
        {
            result = await Task.Run(() => TokenSigner.SignDigestPreferred(cert, digest, pin, NullIfEmpty(pkcs11Module)));
        }
        catch (Pkcs11Exception ex) when (ex.RV == CKR.CKR_PIN_INCORRECT)
        {
            throw new SignTestException("the token rejected the PIN",
                "The token locks after a few wrong PINs. Correct the PIN in Settings before trying again.");
        }
        catch (Pkcs11Exception ex) when (ex.RV == CKR.CKR_PIN_LOCKED)
        {
            throw new SignTestException("the token PIN is locked",
                "Unlock it with the token vendor's tool (PUK), or ask the CA.");
        }
        catch (Exception ex)
        {
            throw new SignTestException("signing on the token failed: " + ex.GetBaseException().Message,
                "Check the token is plugged in and its driver/middleware is installed.");
        }
        watch.Stop();

        if (!Verify(cert, digest, result))
            throw new SignTestException("the token signed, but the signature does not match the certificate",
                "The token holds a different key from this certificate. Pick the right certificate in Settings.");

        _log($"     OK   signed and verified a test digest ({result.Algorithm}, {watch.Elapsed.TotalSeconds:0.0}s)");
        return cert;
    }

    private static bool Verify(X509Certificate2 cert, byte[] digest, SignResult result)
    {
        var rsa = cert.GetRSAPublicKey();
        if (rsa != null)
            return rsa.VerifyHash(digest, result.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var ecdsa = cert.GetECDsaPublicKey();
        if (ecdsa != null)
            return ecdsa.VerifyHash(digest, EcdsaDerToP1363(result.Signature, (ecdsa.KeySize + 7) / 8));

        return false;
    }

    // ── Steps 3 and 4 ───────────────────────────────────────────────────────────────────

    private async Task<string> FindCredentialAsync(string apiUrl, object identity, X509Certificate2 cert, CancellationToken ct)
    {
        var json = await PostAsync(apiUrl, "/credentials/list", identity, ct);
        var creds = json["result"] as JArray ?? new JArray();
        _log($"     OK   the server found this agent: {creds.Count} certificate(s)");

        var mine = creds.OfType<JObject>().FirstOrDefault(c =>
            string.Equals((string?)c["serial_number"], cert.SerialNumber, StringComparison.OrdinalIgnoreCase));
        if (mine == null)
            throw new SignTestException(
                $"the server's list does not contain certificate {cert.SerialNumber}",
                "Another agent may be publishing the same phone number, or this agent has not been restarted " +
                "since its certificate was changed. Restart the agent and try again.");

        return (string?)mine["credential_id"] ?? cert.SerialNumber;
    }

    private async Task<string> SignPdfAsync(string apiUrl, object identity, string credentialId, string phone,
        X509Certificate2 cert, CancellationToken ct)
    {
        var stamp = DateTime.Now;
        var testId = $"VMSIGN-TEST-{stamp:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6)}";
        var pdf = SamplePdf.Build("VMSignAgent - Test signature", new[]
        {
            "This page was generated by VMSignAgent to test USB token signing.",
            $"Computer:    {Environment.MachineName}",
            $"Phone:       {phone}",
            $"Certificate: {cert.GetNameInfo(X509NameType.SimpleName, false)}",
            $"Serial:      {cert.SerialNumber}",
            $"Created:     {stamp:yyyy-MM-dd HH:mm:ss}",
            $"Test ID:     {testId}",
            "",
            "The signature box below was added by the signing server.",
        });

        var request = JObject.FromObject(identity);
        request["trans_id"] = testId;
        request["credential_id"] = credentialId;
        request["computer_name"] = Environment.MachineName;
        request["mac"] = LocalMac();
        request["os"] = "Windows";
        request["data_type"] = 1; // BASE64: the signed file comes back in raw_data
        request["local_sign"] = false;
        // No signature_id: it names a signature record in the HIS, and with one the server goes on
        // to mark that record signed - which fails for a test, and fails the whole file with it.
        request["file_datas"] = new JArray(JObject.FromObject(new
        {
            page_sign = 1,
            file_name = testId + ".pdf",
            signature_name = "vmsign_test",
            store_uid = testId,
            store_data = false,
            display_name_mode = 2,
            // Drawn by the server with its own Unicode font, so the diacritics stay.
            name_signer = cert.GetNameInfo(X509NameType.SimpleName, false),
            title_signer = "VMSignAgent test",
            is_show_signature_time = true,
            // The server takes point_x/point_y as the CENTRE of the box: this is [72,420]-[312,510].
            point_x = 192,
            point_y = 465,
            width = 240,
            height = 90,
            pdf_data = Convert.ToBase64String(pdf),
        }));

        var watch = Stopwatch.StartNew();
        var json = await PostAsync(apiUrl, "/sign/multi", request, ct);
        watch.Stop();

        var file = (json["result"] as JArray)?.OfType<JObject>().FirstOrDefault();
        if (file == null)
            throw new SignTestException("the server answered without a file result", null);

        // SignatureStatus.Done = 2. Read as text so a server that serialises enums by name also works.
        var status = (string?)file["status"];
        var message = (string?)file["message"];
        if (status != "2" && !string.Equals(status, "Done", StringComparison.OrdinalIgnoreCase))
            throw new SignTestException($"the server did not sign the file: {message}", Hint(message));

        var rawData = (string?)file["raw_data"];
        if (string.IsNullOrEmpty(rawData))
            throw new SignTestException("the server reported success but returned no file", null);

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VMSignAgent");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"test-signed-{stamp:yyyyMMdd-HHmmss}.pdf");
        File.WriteAllBytes(path, Convert.FromBase64String(rawData));

        _log($"     OK   signed by the server in {watch.Elapsed.TotalSeconds:0.0}s");
        _log($"     Saved: {path}");
        _log("");
        _log("All steps passed. USB signing works end to end on this computer.");
        return path;
    }

    // ── HTTP ────────────────────────────────────────────────────────────────────────────

    private static async Task<JObject> PostAsync(string apiUrl, string action, object body, CancellationToken ct)
    {
        var url = apiUrl + SignaturePath + action;
        using var content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await Http.PostAsync(url, content, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SignTestException($"no answer from {url} within {Http.Timeout.TotalSeconds:0}s",
                "The server may be waiting on the MQTT broker or on this agent. Check the agent's MQTT status.");
        }
        catch (HttpRequestException ex)
        {
            throw new SignTestException($"cannot reach {url}: {ex.GetBaseException().Message}",
                "Check Settings > Signing Server > API URL and that this computer can reach the server.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            JObject json;
            try { json = JObject.Parse(text); }
            catch
            {
                throw new SignTestException(
                    $"{url} answered HTTP {(int)response.StatusCode}, but not with the signing API's JSON",
                    "Check Settings > Signing Server > API URL points at the signing API.");
            }

            if ((bool?)json["success"] != true)
            {
                var message = (string?)json["message"];

                // sign/multi: the envelope only counts the failed files; each file says why.
                var fileMessage = (json["result"] as JArray)?.OfType<JObject>()
                    .Select(f => (string?)f["message"])
                    .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
                if (!string.IsNullOrWhiteSpace(fileMessage) && fileMessage != message)
                    message = string.IsNullOrWhiteSpace(message) ? fileMessage : $"{message} {fileMessage}";

                throw new SignTestException(string.IsNullOrWhiteSpace(message)
                    ? $"the server refused the request (HTTP {(int)response.StatusCode})"
                    : $"server: {message}", Hint(message));
            }

            return json;
        }
    }

    /// <summary>Turns the server's USB error messages into the thing to go and fix.</summary>
    private static string? Hint(string? message)
    {
        if (string.IsNullOrEmpty(message)) return null;
        bool Has(string s) => message!.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;

        if (Has("Error while connecting with host") || Has("Connection refused"))
            return "The SERVER cannot connect to its MQTT broker. On the server, check the broker is running and " +
                   "USB_MQTT_BROKER_HOST/PORT in .env (then recreate the API container).";
        if (Has("BadUserNameOrPassword") || Has("NotAuthorized"))
            return "The broker refused the SERVER's MQTT username/password (USB_MQTT_USERNAME/PASSWORD on the server).";
        if (Has("nào đang online"))
            return "The server reached the broker but sees no agent at all. This agent and the server must use the " +
                   "same broker host and port.";
        if (Has("cho số điện thoại này"))
            return "The server sees agents, but none with this phone number. The phone number in Settings must be " +
                   "the one typed when signing; restart the agent after changing it.";
        if (Has("PIN USB Token không hợp lệ"))
            return "This agent rejected the phone/PIN. If Settings were changed, restart the agent so it uses them.";
        if (Has("Không tìm thấy thông tin CTS"))
            return "The server's USB merchant is not in MQTT mode (USB_MQTT_BROKER_HOST is empty on the server).";
        if (Has("Timeout") || Has("timed out"))
            return "The server waited for this agent and gave up. Check the agent's MQTT status and that the " +
                   "token is plugged in.";
        return null;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Accepts the bare server ("http://host:2606") or the URL the hospital software is
    /// configured with ("http://host:2606/api/v1/Signature"), and returns the bare server.
    /// </summary>
    public static string? NormalizeApiUrl(string? value)
    {
        var url = (value ?? string.Empty).Trim().TrimEnd('/');
        if (url.Length == 0) return null;
        if (url.EndsWith(SignaturePath, StringComparison.OrdinalIgnoreCase))
            url = url.Substring(0, url.Length - SignaturePath.Length);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? url
            : null;
    }

    private static NetworkInterface? PrimaryInterface() =>
        NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
            n.OperationalStatus == OperationalStatus.Up &&
            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
            n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
            n.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork));

    private static string LocalIp() =>
        PrimaryInterface()?.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString()
        ?? IPAddress.Loopback.ToString();

    private static string LocalMac()
    {
        var bytes = PrimaryInterface()?.GetPhysicalAddress().GetAddressBytes() ?? Array.Empty<byte>();
        return string.Join("-", bytes.Select(b => b.ToString("X2")));
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>DER SEQUENCE{INTEGER r, INTEGER s} to the fixed-width R||S that ECDsa.VerifyHash takes.</summary>
    private static byte[] EcdsaDerToP1363(byte[] der, int fieldSize)
    {
        var pos = 0;
        if (der[pos++] != 0x30) throw new CryptographicException("ECDSA signature is not a DER sequence");
        ReadLength(der, ref pos);
        var result = new byte[fieldSize * 2];
        for (var part = 0; part < 2; part++)
        {
            if (der[pos++] != 0x02) throw new CryptographicException("ECDSA signature is missing an integer");
            var len = ReadLength(der, ref pos);
            var start = pos;
            while (len > fieldSize && der[start] == 0) { start++; len--; } // sign padding
            if (len > fieldSize) throw new CryptographicException("ECDSA integer is longer than the curve");
            Buffer.BlockCopy(der, start, result, part * fieldSize + fieldSize - len, len);
            pos = start + len;
        }
        return result;
    }

    private static int ReadLength(byte[] der, ref int pos)
    {
        int first = der[pos++];
        if (first < 0x80) return first;
        var length = 0;
        for (var i = 0; i < (first & 0x7F); i++) length = (length << 8) | der[pos++];
        return length;
    }
}

internal sealed class SignTestException : Exception
{
    public SignTestException(string message, string? hint) : base(message)
    {
        Hint = hint;
    }

    public string? Hint { get; }
}
