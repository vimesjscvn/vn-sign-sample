using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using MQTTnet.Protocol;
using Newtonsoft.Json;

namespace VMSignAgent;

/// <summary>
/// MQTT transport for cross-subnet / NAT deployments (MQTTnet 3.x).
/// The agent connects outbound to a central broker, so it works from behind NAT.
///
/// Topics:
///   usbagent/{agentId}/status   - retained presence + Last-Will
///   usbagent/{agentId}/sign/req - app to agent
///   usbagent/{agentId}/sign/res - agent to app
/// </summary>
public sealed class MqttSigningResponder
{
    private readonly string _brokerHost;
    private readonly int _brokerPort;
    private readonly string _username;
    private readonly string? _password;
    private readonly MqttTlsConfig _tls;
    private readonly string _agentId;
    private readonly int _httpPort;
    private readonly string? _tokenPin;
    private readonly string? _phoneNumber;
    private readonly string? _selectedCertificateSerial;
    private readonly string? _pkcs11ModulePath;
    private readonly Action<string?>? _onSignSuccess;

    // How often the token is re-read to keep the retained presence honest — see
    // RefreshPresenceLoopAsync. Nothing is published unless the certificate list changed.
    private static readonly TimeSpan PresenceRefreshInterval = TimeSpan.FromSeconds(15);
    private readonly SemaphoreSlim _presenceGate = new(1, 1);
    private string? _publishedCerts;

    private string StatusTopic  => $"usbagent/{_agentId}/status";
    private string SignReqTopic => $"usbagent/{_agentId}/sign/req";
    private string SignResTopic => $"usbagent/{_agentId}/sign/res";
    private string AuthReqTopic => $"usbagent/{_agentId}/auth/req";
    private string AuthResTopic => $"usbagent/{_agentId}/auth/res";

    public MqttSigningResponder(string brokerHost, int brokerPort, string? username, string? password,
        bool useTls, string agentId, int httpPort, MqttTlsConfig? tls = null, string? tokenPin = null, string? phoneNumber = null, string? selectedCertificateSerial = null, string? pkcs11ModulePath = null, Action<string?>? onSignSuccess = null)
    {
        _brokerHost = brokerHost;
        _brokerPort = brokerPort;
        _agentId    = agentId;
        _username   = string.IsNullOrWhiteSpace(username) ? string.Empty : username!;
        _password   = password;
        _tls        = tls ?? new MqttTlsConfig { UseTls = useTls };
        _httpPort   = httpPort;
        _tokenPin   = tokenPin;
        _phoneNumber = phoneNumber;
        _selectedCertificateSerial = selectedCertificateSerial;
        _pkcs11ModulePath = pkcs11ModulePath;
        _onSignSuccess = onSignSuccess;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var factory = new MqttFactory();
        using var client = factory.CreateMqttClient();

        client.UseApplicationMessageReceivedHandler(e =>
            _ = Task.Run(() => OnMqttMessageAsync(client, e)));

        client.UseConnectedHandler(async _ =>
        {
            Console.WriteLine($"[MQTT] Connected to {_brokerHost}:{_brokerPort} as '{_agentId}'");
            await PublishPresenceAsync(client, online: true, CancellationToken.None);
            await client.SubscribeAsync(
                new MqttTopicFilterBuilder()
                    .WithTopic(SignReqTopic)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());
            await client.SubscribeAsync(
                new MqttTopicFilterBuilder()
                    .WithTopic(AuthReqTopic)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());
            Console.WriteLine($"[MQTT] Subscribed to {SignReqTopic}");
            Console.WriteLine($"[MQTT] Subscribed to {AuthReqTopic}");
        });

        client.UseDisconnectedHandler(async _ =>
        {
            Console.WriteLine("[MQTT] Disconnected; reconnecting in 5s...");
            await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
        });

        var options = BuildOptions();

        // Dispatched through Task.Run for the same reason as the message handler above: the
        // agent is hosted by a WinForms message loop, and the loop below blocks on the token.
        var presenceRefresh = Task.Run(() => RefreshPresenceLoopAsync(client, ct));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!client.IsConnected)
                    await client.ConnectAsync(options, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.WriteLine($"[MQTT] Connect failed: {ex.Message}"); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
            catch (OperationCanceledException) { break; }
        }

        // Let the refresh loop finish before the offline notice, so it cannot overwrite it.
        try { await presenceRefresh; } catch { /* best effort */ }

        try
        {
            await PublishPresenceAsync(client, online: false, CancellationToken.None);
            await client.DisconnectAsync();
        }
        catch { /* best effort */ }
    }

    private IMqttClientOptions BuildOptions()
    {
        var offline = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
            new MqttPresence("vmsign-agent", _agentId, Dns.GetHostName(), _httpPort, false,
                _phoneNumber, new List<PresenceCert>(), DateTimeOffset.UtcNow)));

        var builder = new MqttClientOptionsBuilder()
            .WithClientId($"usbagent-{_agentId}-{Guid.NewGuid():N}")
            .WithTcpServer(_brokerHost, _brokerPort)
            .WithCleanSession(true);

        if (!string.IsNullOrWhiteSpace(_username))
        {
            builder.WithCredentials(_username, _password ?? string.Empty);
        }

        builder.WithWillMessage(new MqttApplicationMessageBuilder()
                .WithTopic(StatusTopic)
                .WithPayload(offline)
                .WithRetainFlag(true)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build());

        _tls.Apply(builder);
        return builder.Build();
    }

    /// <param name="onlyIfChanged">
    /// Skip the publish when the certificate list is identical to the one already on the broker.
    /// Used by the refresh loop; the connect and shutdown paths always publish.
    /// </param>
    private async Task PublishPresenceAsync(IMqttClient client, bool online, CancellationToken ct, bool onlyIfChanged = false)
    {
        await _presenceGate.WaitAsync(ct);
        try
        {
            var certs = online
                ? TokenSigner.ListCerts(_selectedCertificateSerial)
                    .Select(c => new PresenceCert(c.Serial, c.SubjectDN, c.Algorithm, c.Certificate))
                    .ToList()
                : new List<PresenceCert>();

            // Compared over the certificates alone — the presence timestamp changes every call.
            // Ordered by serial because neither the Windows store nor PKCS#11 promises an order.
            var fingerprint = online
                ? JsonConvert.SerializeObject(certs.OrderBy(c => c.Serial, StringComparer.OrdinalIgnoreCase))
                : null;
            if (onlyIfChanged && fingerprint == _publishedCerts) return;

            var payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
                new MqttPresence("vmsign-agent", _agentId, Dns.GetHostName(),
                    _httpPort, online, _phoneNumber, certs, DateTimeOffset.UtcNow)));
            await client.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(StatusTopic)
                .WithPayload(payload)
                .WithRetainFlag(true)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build(), ct);

            // Recorded only once the publish went out, so a failed one is retried next tick.
            _publishedCerts = fingerprint;
            if (onlyIfChanged)
                Console.WriteLine($"[MQTT] Presence updated: {certs.Count} certificate(s)");
        }
        finally
        {
            _presenceGate.Release();
        }
    }

    /// <summary>
    /// Keeps the retained presence message in step with the token that is actually plugged in.
    ///
    /// Presence is published once on connect and is retained, so whatever was sent then stays on
    /// the broker indefinitely. If the token was not readable at that moment — plugged in later,
    /// or Windows' Certificate Propagation service had not caught up yet — an empty certificate
    /// list sticks until the agent is restarted, and the app has no way to recover: the only
    /// topics are sign/req|res, auth/req|res and +/status, so presence is its sole source of
    /// certificates. Unplugging the token is the mirror problem, a list still advertised as
    /// available. Re-published only on an actual change, so an idle agent stays silent.
    /// </summary>
    private async Task RefreshPresenceLoopAsync(IMqttClient client, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(PresenceRefreshInterval, ct); }
            catch (OperationCanceledException) { break; }

            if (!client.IsConnected) continue;

            try { await PublishPresenceAsync(client, online: true, ct, onlyIfChanged: true); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.WriteLine($"[MQTT] Presence refresh failed: {ex.Message}"); }
        }
    }

    private Task OnMqttMessageAsync(IMqttClient client, MqttApplicationMessageReceivedEventArgs e)
    {
        if (e.ApplicationMessage.Topic == SignReqTopic)
        {
            return OnSignRequestAsync(client, e);
        }

        if (e.ApplicationMessage.Topic == AuthReqTopic)
        {
            return OnAuthRequestAsync(client, e);
        }

        return Task.CompletedTask;
    }

    private async Task OnAuthRequestAsync(IMqttClient client, MqttApplicationMessageReceivedEventArgs e)
    {
        MqttAuthRequest? req = null;
        try
        {
            var json = Encoding.UTF8.GetString(e.ApplicationMessage.Payload ?? Array.Empty<byte>());
            req = JsonConvert.DeserializeObject<MqttAuthRequest>(json);
        }
        catch (Exception ex) { Console.WriteLine($"[MQTT] Auth request not parseable: {ex.Message}"); }

        var response = HandleAuth(req);
        await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(AuthResTopic)
            .WithPayload(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build());
        Console.WriteLine($"[MQTT] Auth response sent (correlationId={response.CorrelationId}, success={response.Success})");
    }

    private async Task OnSignRequestAsync(IMqttClient client, MqttApplicationMessageReceivedEventArgs e)
    {
        if (e.ApplicationMessage.Topic != SignReqTopic) return;

        MqttSignRequest? req = null;
        try
        {
            var json = Encoding.UTF8.GetString(e.ApplicationMessage.Payload ?? Array.Empty<byte>());
            req = JsonConvert.DeserializeObject<MqttSignRequest>(json);
        }
        catch (Exception ex) { Console.WriteLine($"[MQTT] Sign request not parseable: {ex.Message}"); }

        var response = HandleSign(req);
        await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(SignResTopic)
            .WithPayload(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build());
        Console.WriteLine($"[MQTT] Sign response sent (correlationId={response.CorrelationId}, success={response.Success})");
    }

    private MqttSignResponse HandleSign(MqttSignRequest? req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.HashBase64))
            return MqttSignResponse.Fail(req?.CorrelationId, "hashBase64 is required");

        var serialToUse = string.IsNullOrWhiteSpace(_selectedCertificateSerial) ? req.Serial : _selectedCertificateSerial;
        var cert = TokenSigner.FindCert(serialToUse, null);
        if (cert == null)
            return MqttSignResponse.Fail(req.CorrelationId, "Certificate not found in Windows Personal store");

        byte[] digest;
        try   { digest = Convert.FromBase64String(req.HashBase64); }
        catch { return MqttSignResponse.Fail(req.CorrelationId, "hashBase64 is not valid base64"); }

        // No fallback to the agent's locally-cached PIN here: this handler answers requests
        // arriving over the network (MQTT), so the caller must prove it knows the PIN on every
        // request. The cached PIN is only for the loopback-bound local HTTP path (Program.cs),
        // which is already physically trusted.
        if (string.IsNullOrEmpty(req.Pin))
            return MqttSignResponse.Fail(req.CorrelationId, "pin is required");

        try
        {
            var r = TokenSigner.SignDigestPreferred(cert, digest, req.Pin, _pkcs11ModulePath);
            _onSignSuccess?.Invoke(cert.SerialNumber);
            return new MqttSignResponse(req.CorrelationId, true,
                Convert.ToBase64String(r.Signature),
                Convert.ToBase64String(r.CertRawData),
                r.Algorithm, null);
        }
        catch (Exception ex) { return MqttSignResponse.Fail(req.CorrelationId, ex.Message); }
    }

    private MqttAuthResponse HandleAuth(MqttAuthRequest? req)
    {
        if (req == null)
        {
            return MqttAuthResponse.Fail(null, "auth request is required");
        }

        if (string.IsNullOrWhiteSpace(_phoneNumber) || string.IsNullOrWhiteSpace(_tokenPin))
        {
            return MqttAuthResponse.Fail(req.CorrelationId, "agent phone number or PIN is not configured");
        }

        var phoneMatches = string.Equals(
            NormalizePhone(req.PhoneNumber),
            NormalizePhone(_phoneNumber),
            StringComparison.OrdinalIgnoreCase);
        var pinMatches = string.Equals(req.Pin ?? string.Empty, _tokenPin, StringComparison.Ordinal);

        return phoneMatches && pinMatches
            ? new MqttAuthResponse(req.CorrelationId, true, null)
            : MqttAuthResponse.Fail(req.CorrelationId, "invalid phone number or PIN");
    }

    private static string NormalizePhone(string? phone) =>
        (phone ?? string.Empty).Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
}

// MQTT message contracts
public record MqttPresence(
    string Service, string AgentId, string Host, int HttpPort, bool Online,
    string? PhoneNumber, List<PresenceCert> Certs, DateTimeOffset Ts);

public record PresenceCert(string Serial, string Subject, string Algorithm, string Certificate);

public record MqttSignRequest(string? CorrelationId, string? HashBase64, string? Serial, string? Pin = null);

public record MqttSignResponse(
    string? CorrelationId, bool Success,
    string? SignatureBase64, string? CertificateBase64, string? Algorithm, string? Error)
{
    public static MqttSignResponse Fail(string? id, string error) =>
        new(id, false, null, null, null, error);
}

public record MqttAuthRequest(string? CorrelationId, string? PhoneNumber, string? Pin);

public record MqttAuthResponse(string? CorrelationId, bool Success, string? Error)
{
    public static MqttAuthResponse Fail(string? id, string error) =>
        new(id, false, error);
}
