using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace VMSignAgent;

/// <summary>
/// Core USB token / Windows CAPI operations shared by every transport
/// (HTTP endpoints, UDP discovery, MQTT responder). The private key never
/// leaves the token; Windows CryptoAPI delegates signing to the CSP driver.
/// </summary>
public static class TokenSigner
{
    /// <summary>
    /// Finds a signing certificate in CurrentUser\My.
    /// Priority: exact serial → CN/email subject match → (only if no identifier) first cert.
    /// Never blindly falls back when an identifier was supplied (wrong-key safety).
    /// </summary>
    public static X509Certificate2? FindCert(string? serial, string? userName)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var all = store.Certificates.Cast<X509Certificate2>().Where(c => c.HasPrivateKey).ToList();

        if (!string.IsNullOrWhiteSpace(serial))
        {
            var bySerial = all.FirstOrDefault(c =>
                string.Equals(c.SerialNumber, serial, StringComparison.OrdinalIgnoreCase));
            if (bySerial != null) return bySerial;
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            var lower = userName.ToLowerInvariant();
            var byName = all.FirstOrDefault(c =>
                c.Subject.ToLowerInvariant().Contains("cn=" + lower) ||
                c.Subject.ToLowerInvariant().Contains("e=" + lower));
            if (byName != null) return byName;
        }

        if (string.IsNullOrWhiteSpace(serial) && string.IsNullOrWhiteSpace(userName))
            return all.FirstOrDefault();

        return null;
    }

    /// <summary>Lists every certificate in CurrentUser\My that has a usable private key.</summary>
    public static List<CertInfo> ListCerts()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates.Cast<X509Certificate2>()
            .Where(c => c.HasPrivateKey)
            .Select(ToInfo)
            .ToList();
    }

    public static CertInfo ToInfo(X509Certificate2 c) => new(
        Serial: c.SerialNumber,
        SubjectDN: c.Subject,
        IssuerDN: c.Issuer,
        ValidFrom: c.NotBefore.ToString("O"),
        ValidTo: c.NotAfter.ToString("O"),
        Thumbprint: c.Thumbprint,
        Certificate: Convert.ToBase64String(c.RawData),
        Algorithm: c.GetECDsaPublicKey() != null ? "ECDSA" : "RSA");

    /// <summary>
    /// Signs a PRE-COMPUTED SHA-256 digest directly (no re-hash) via Windows CAPI.
    /// Matches SignPdfFile.createHash output contract: RSA-PKCS1 / ECDSA over the digest.
    /// ECDSA signatures are converted from CNG P1363 to DER for iText7/PKCS7.
    ///
    /// <paramref name="pin"/> (optional): when supplied, the token PIN is set on the CNG
    /// key handle before signing so the CSP does NOT show an interactive PIN dialog —
    /// required for unattended/headless agents. When null/empty, the token prompts as usual.
    /// </summary>
    public static SignResult SignDigest(X509Certificate2 cert, byte[] digest, string? pin = null)
    {
        var ecKey = cert.GetECDsaPrivateKey();
        if (ecKey != null)
        {
            TrySetPin(ecKey, pin);
            byte[] der = EcdsaP1363ToDer(ecKey.SignHash(digest));
            return new SignResult(der, cert.RawData, "ECDSA");
        }

        var rsaKey = cert.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("No RSA or ECDSA private key found");
        TrySetPin(rsaKey, pin);
        byte[] sig = rsaKey.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return new SignResult(sig, cert.RawData, "RSA");
    }

    /// <summary>
    /// Supplies the token PIN so the next private-key operation does not pop an interactive
    /// PIN dialog. Handles both modern CNG/KSP tokens (NCRYPT "SmartCardPin") and legacy CSP
    /// tokens (CryptSetProvParam PP_SIGNATURE_PIN / PP_KEYEXCHANGE_PIN). No-op for software
    /// certs. Best-effort: failures are swallowed so a key that can't take a PIN never breaks
    /// signing (it simply prompts as usual on a real token).
    /// </summary>
    private static void TrySetPin(AsymmetricAlgorithm key, string? pin)
    {
        if (string.IsNullOrEmpty(pin) || key == null) return;

        // Modern CNG / KSP minidriver tokens.
        CngKey? cngKey = key switch
        {
            RSACng r => r.Key,
            ECDsaCng e => e.Key,
            _ => null
        };
        if (cngKey != null) { TrySetCngPin(cngKey, pin!); return; }

        // Legacy CSP tokens (RSACryptoServiceProvider).
        if (key is RSACryptoServiceProvider csp) { TrySetCspPin(csp, pin!); return; }
    }

    // CNG: NCRYPT_PIN_PROPERTY expects a null-terminated Unicode string.
    private static void TrySetCngPin(CngKey cngKey, string pin)
    {
        try
        {
            const string NCRYPT_PIN_PROPERTY = "SmartCardPin";
            byte[] pinBytes = Encoding.Unicode.GetBytes(pin + "\0");
            cngKey.SetProperty(new CngProperty(NCRYPT_PIN_PROPERTY, pinBytes, CngPropertyOptions.None));
        }
        catch
        {
            // KSP that rejects SmartCardPin (e.g. bit4id) — fall through; caller uses PKCS#11 path instead.
        }
    }

    // Legacy CSP: acquire the key container silently and stash the PIN so the subsequent
    // sign on the same container does not prompt. PP_*_PIN expects an ANSI string.
    // NOTE: PIN caching is CSP-specific; most smart-card CSPs cache the verified PIN at the
    // container/session level once set, so this carries to the signing handle.
    private static void TrySetCspPin(RSACryptoServiceProvider csp, string pin)
    {
        IntPtr hProv = IntPtr.Zero;
        try
        {
            var info = csp.CspKeyContainerInfo;
            uint flags = CRYPT_SILENT | (info.MachineKeyStore ? CRYPT_MACHINE_KEYSET : 0u);
            if (!CryptAcquireContext(out hProv, info.KeyContainerName, info.ProviderName,
                    (uint)info.ProviderType, flags))
                return;

            byte[] pinBytes = Encoding.ASCII.GetBytes(pin + "\0");
            CryptSetProvParam(hProv, PP_SIGNATURE_PIN, pinBytes, 0);
            CryptSetProvParam(hProv, PP_KEYEXCHANGE_PIN, pinBytes, 0);
        }
        catch
        {
            // Software CSP / unsupported — fall through to normal prompt.
        }
        finally
        {
            if (hProv != IntPtr.Zero) CryptReleaseContext(hProv, 0);
        }
    }

    // ── Legacy CSP P/Invoke (advapi32) ───────────────────────────────────────────
    private const uint PP_KEYEXCHANGE_PIN = 32;   // 0x20
    private const uint PP_SIGNATURE_PIN   = 33;   // 0x21
    private const uint CRYPT_SILENT       = 0x40;
    private const uint CRYPT_MACHINE_KEYSET = 0x20;

    [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool CryptAcquireContext(out IntPtr hProv, string? pszContainer,
        string? pszProvider, uint dwProvType, uint dwFlags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CryptSetProvParam(IntPtr hProv, uint dwParam, byte[] pbData, uint dwFlags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CryptReleaseContext(IntPtr hProv, uint dwFlags);

    /// <summary>Presence/discovery payload for this machine: host + certs available.</summary>
    public static DiscoveryReply BuildDiscoveryReply(int httpPort) => new(
        Service: "vmsign-agent",
        Host: Dns.GetHostName(),
        HttpPort: httpPort,
        Certs: SafeListCertsAsDiscovery());

    private static List<DiscoveryCert> SafeListCertsAsDiscovery()
    {
        try
        {
            return ListCerts()
                .Select(c => new DiscoveryCert(c.Serial, c.SubjectDN, c.Algorithm))
                .ToList();
        }
        catch { return new List<DiscoveryCert>(); }
    }

    // ── ECDSA P1363 (R||S) → DER SEQUENCE{INTEGER r, INTEGER s} ──────────────────
    public static byte[] EcdsaP1363ToDer(byte[] p1363)
    {
        int half = p1363.Length / 2;
        byte[] r = MinimalBigInt(p1363.Take(half).ToArray());
        byte[] s = MinimalBigInt(p1363.Skip(half).ToArray());
        byte[] body = DerInt(r).Concat(DerInt(s)).ToArray();
        return new byte[] { 0x30, (byte)body.Length }.Concat(body).ToArray();
    }

    private static byte[] MinimalBigInt(byte[] val)
    {
        int start = 0;
        while (start < val.Length - 1 && val[start] == 0) start++;
        val = val.Skip(start).ToArray();
        return (val[0] & 0x80) != 0 ? new byte[] { 0x00 }.Concat(val).ToArray() : val;
    }

    private static byte[] DerInt(byte[] val) =>
        new byte[] { 0x02, (byte)val.Length }.Concat(val).ToArray();
}

// ── Shared DTOs ─────────────────────────────────────────────────────────────────
public record SignResult(byte[] Signature, byte[] CertRawData, string Algorithm);

public record CertInfo(
    string Serial, string SubjectDN, string IssuerDN,
    string ValidFrom, string ValidTo,
    string Thumbprint, string Certificate, string Algorithm)
{
    public bool Success => true;
}

public record DiscoveryReply(string Service, string Host, int HttpPort, List<DiscoveryCert> Certs);
public record DiscoveryCert(string Serial, string Subject, string Algorithm);
