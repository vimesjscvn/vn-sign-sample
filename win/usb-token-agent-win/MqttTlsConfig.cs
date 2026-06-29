using System.Collections.Generic;
using System.IO;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using MQTTnet.Client.Options;

namespace UsbTokenAgent;

/// <summary>
/// TLS settings for connecting to the MQTT broker (MQTTnet 3.x).
/// </summary>
public sealed class MqttTlsConfig
{
    public bool UseTls { get; init; }
    public string? CaCertPath { get; init; }
    public string? ClientPfxPath { get; init; }
    public string? ClientPfxPassword { get; init; }
    public bool AllowUntrusted { get; init; }

    public void Apply(MqttClientOptionsBuilder builder)
    {
        if (!UseTls) return;

        X509Certificate2? caCert = null;
        if (!string.IsNullOrWhiteSpace(CaCertPath) && File.Exists(CaCertPath))
            caCert = new X509Certificate2(CaCertPath);

        var clientCerts = new List<X509Certificate2>();
        if (!string.IsNullOrWhiteSpace(ClientPfxPath) && File.Exists(ClientPfxPath))
            clientCerts.Add(new X509Certificate2(ClientPfxPath, ClientPfxPassword ?? string.Empty));

        var tlsParams = new MqttClientOptionsBuilderTlsParameters
        {
            UseTls                         = true,
            SslProtocol                    = SslProtocols.Tls12,
            AllowUntrustedCertificates     = AllowUntrusted,
            IgnoreCertificateChainErrors   = AllowUntrusted,
            IgnoreCertificateRevocationErrors = AllowUntrusted,
            Certificates                   = clientCerts,
        };

        if (AllowUntrusted)
        {
            tlsParams.CertificateValidationHandler = _ => true;
        }
        else if (caCert != null)
        {
            var pinnedCa = caCert;
            tlsParams.CertificateValidationHandler = ctx =>
            {
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode      = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags   = X509VerificationFlags.AllowUnknownCertificateAuthority;
                chain.ChainPolicy.ExtraStore.Add(pinnedCa);
                if (!chain.Build(new X509Certificate2(ctx.Certificate))) return false;
                // Confirm the chain roots at the pinned CA.
                var root = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
                return root.Thumbprint == pinnedCa.Thumbprint;
            };
        }

        builder.WithTls(tlsParams);
    }
}
