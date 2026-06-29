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
///   usbagent/{agentId}/status   — retained presence + Last-Will
///   usbagent/{agentId}/sign/req — app → agent
///   usbagent/{agentId}/sign/res — agent → app
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

    private string StatusTopic  => $"usbagent/{_agentId}/status";
    private string SignReqTopic => $"usbagent/{_agentId}/sign/req";
    private string SignResTopic => $"usbagent/{_agentId}/sign/res";

    public MqttSigningResponder(string brokerHost, int brokerPort, string? username, string? password,
        bool useTls, string agentId, int httpPort, MqttTlsConfig? tls = null, string? tokenPin = null)
    {
        _brokerHost = brokerHost;
        _brokerPort = brokerPort;
        _agentId    = agentId;
        _username   = string.IsNullOrWhiteSpace(username) ? agentId : username!;
        _password   = password;
        _tls        = tls ?? new MqttTlsConfig { UseTls = useTls };
        _httpPort   = httpPort;
        _tokenPin   = tokenPin;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var factory = new MqttFactory();
        using var client = factory.CreateMqttClient();

        client.UseApplicationMessageReceivedHandler(e =>
            _ = Task.Run(() => OnSignRequestAsync(client, e)));

        client.UseConnectedHandler(async _ =>
        {
            Console.WriteLine($"[MQTT] Connected to {_brokerHost}:{_brokerPort} as '{_agentId}'");
            await PublishPresenceAsync(client, online: true, CancellationToken.None);
            await client.SubscribeAsync(
                new MqttTopicFilterBuilder()
                    .WithTopic(SignReqTopic)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());
            Console.WriteLine($"[MQTT] Subscribed to {SignReqTopic}");
        });

        client.UseDisconnectedHandler(async _ =>
        {
            Console.WriteLine("[MQTT] Disconnected; reconnecting in 5s...");
            await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
        });

        var options = BuildOptions();

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
                new List<PresenceCert>(), DateTimeOffset.UtcNow)));

        var builder = new MqttClientOptionsBuilder()
            .WithClientId($"usbagent-{_agentId}-{Guid.NewGuid():N}")
            .WithTcpServer(_brokerHost, _brokerPort)
            .WithCleanSession(true)
            .WithCredentials(_username, _password ?? string.Empty)
            .WithWillMessage(new MqttApplicationMessageBuilder()
                .WithTopic(StatusTopic)
                .WithPayload(offline)
                .WithRetainFlag(true)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build());

        _tls.Apply(builder);
        return builder.Build();
    }

    private async Task PublishPresenceAsync(IMqttClient client, bool online, CancellationToken ct)
    {
        var certs = online
            ? TokenSigner.ListCerts()
                .Select(c => new PresenceCert(c.Serial, c.SubjectDN, c.Algorithm, c.Certificate))
                .ToList()
            : new List<PresenceCert>();
        var payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
            new MqttPresence("vmsign-agent", _agentId, Dns.GetHostName(),
                _httpPort, online, certs, DateTimeOffset.UtcNow)));
        await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(StatusTopic)
            .WithPayload(payload)
            .WithRetainFlag(true)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build(), ct);
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

        var cert = TokenSigner.FindCert(req.Serial, null);
        if (cert == null)
            return MqttSignResponse.Fail(req.CorrelationId, "Certificate not found in Windows Personal store");

        byte[] digest;
        try   { digest = Convert.FromBase64String(req.HashBase64); }
        catch { return MqttSignResponse.Fail(req.CorrelationId, "hashBase64 is not valid base64"); }

        try
        {
            var pin = !string.IsNullOrEmpty(req.Pin) ? req.Pin : _tokenPin;
            var r   = !string.IsNullOrEmpty(pin)
                ? Pkcs11Signer.SignDigest(cert, digest, pin!)
                : TokenSigner.SignDigest(cert, digest, null);
            return new MqttSignResponse(req.CorrelationId, true,
                Convert.ToBase64String(r.Signature),
                Convert.ToBase64String(r.CertRawData),
                r.Algorithm, null);
        }
        catch (Exception ex) { return MqttSignResponse.Fail(req.CorrelationId, ex.Message); }
    }
}

// ── MQTT message contracts ─────────────────────────────────────────────────────
public record MqttPresence(
    string Service, string AgentId, string Host, int HttpPort, bool Online,
    List<PresenceCert> Certs, DateTimeOffset Ts);

public record PresenceCert(string Serial, string Subject, string Algorithm, string Certificate);

public record MqttSignRequest(string? CorrelationId, string? HashBase64, string? Serial, string? Pin = null);

public record MqttSignResponse(
    string? CorrelationId, bool Success,
    string? SignatureBase64, string? CertificateBase64, string? Algorithm, string? Error)
{
    public static MqttSignResponse Fail(string? id, string error) =>
        new(id, false, null, null, null, error);
}
