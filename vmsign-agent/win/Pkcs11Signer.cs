using System.Security.Cryptography.X509Certificates;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;
using ISession = Net.Pkcs11Interop.HighLevelAPI.ISession;

namespace VMSignAgent;

/// <summary>
/// PKCS#11 signing path used to BYPASS the bit4id interactive PIN dialog.
///
/// The bit4id "Universal Middleware" CNG provider prompts for the PIN at private-key
/// acquisition and rejects the CNG SmartCardPin property ("operation not supported"),
/// so the CAPI/CNG preset in <see cref="TokenSigner"/> cannot run unattended. PKCS#11
/// supplies the PIN programmatically via C_Login before any crypto op, so no dialog
/// appears.
///
/// Byte contract matches <see cref="TokenSigner.SignDigest"/> exactly:
///   - input  = the pre-computed SHA-256 digest (SHA256(authAttrs)) from SignPdfFile.createHash
///   - RSA    = CKM_RSA_PKCS over DigestInfo(SHA-256 prefix || digest)  (= CAPI RSA.SignHash)
///   - ECDSA  = CKM_ECDSA over the raw digest, P1363 (R||S), DER        (= TokenSigner output)
/// </summary>
public static class Pkcs11Signer
{
    // bit4id Universal Middleware PKCS#11 module (64-bit). Overridable via Mqtt/Token config.
    public const string DefaultModulePath = @"C:\Windows\System32\bit4xpki.dll";

    // PKCS#1 v1.5 DigestInfo prefix for SHA-256 (RFC 8017). C_Sign with CKM_RSA_PKCS adds the
    // 00 01 FF..FF 00 padding; we must supply DigestInfo == this prefix || 32-byte hash.
    private static readonly byte[] Sha256DigestInfoPrefix =
    {
        0x30, 0x31, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01,
        0x65, 0x03, 0x04, 0x02, 0x01, 0x05, 0x00, 0x04, 0x20
    };

    // ── Process-lifetime library handle ─────────────────────────────────────────────
    // The bit4id module (like several vendor PKCS#11 libraries) is not safe to
    // C_Initialize/C_Finalize more than once per process: doing so — e.g. once for a sign
    // operation, then again later for a separate listing call — crashed the whole agent with
    // an unrecoverable AccessViolationException inside C_Finalize. Loading the library once
    // and keeping the handle alive for the process lifetime (never explicitly disposing it)
    // avoids a second Finalize call entirely. Sessions (opened per operation below) remain
    // short-lived and are still properly closed each time; only the library-level handle is
    // cached.
    private static readonly object LibLock = new();
    private static Pkcs11InteropFactories? _sharedFactories;
    private static IPkcs11Library? _sharedLib;
    private static string? _sharedLibModulePath;

    private static Pkcs11InteropFactories SharedFactories => _sharedFactories!;

    private static IPkcs11Library GetSharedLibrary(string? modulePath)
    {
        modulePath = ResolveModulePath(modulePath);
        lock (LibLock)
        {
            if (_sharedLib != null && string.Equals(_sharedLibModulePath, modulePath, StringComparison.OrdinalIgnoreCase))
                return _sharedLib;

            if (!File.Exists(modulePath))
                throw new FileNotFoundException($"PKCS#11 module not found: {modulePath}");

            var factories = new Pkcs11InteropFactories();
            var lib = factories.Pkcs11LibraryFactory.LoadPkcs11Library(factories, modulePath, AppType.MultiThreaded);
            _sharedFactories = factories;
            _sharedLib = lib;
            _sharedLibModulePath = modulePath;
            return lib;
        }
    }

    /// <summary>
    /// Signs <paramref name="digest"/> on the token via PKCS#11, authenticating with
    /// <paramref name="pin"/> (no interactive dialog). The signing key is selected by matching
    /// the X.509 cert (<paramref name="cert"/>) on the token, then signing with its private key.
    /// </summary>
    public static SignResult SignDigest(X509Certificate2 cert, byte[] digest, string pin, string? modulePath = null)
    {
        if (string.IsNullOrEmpty(pin))
            throw new ArgumentException("PKCS#11 signing requires a PIN", nameof(pin));

        var lib = GetSharedLibrary(modulePath);
        var factories = SharedFactories;

        // Pick the first slot that has a token present.
        var slots = lib.GetSlotList(SlotsType.WithTokenPresent);
        if (slots.Count == 0) throw new InvalidOperationException("No PKCS#11 token present.");

        foreach (var slot in slots)
        {
            using var session = slot.OpenSession(SessionType.ReadOnly);
            try { session.Login(CKU.CKU_USER, pin); }
            catch (Pkcs11Exception ex) when (ex.RV == CKR.CKR_USER_ALREADY_LOGGED_IN) { /* token already authenticated; fine */ }
            try
            {
                var priv = FindPrivateKeyForCert(session, cert);
                if (priv == null) continue;

                // Guard against silently signing with the wrong on-token key: if the cert's
                // CKA_ID couldn't be matched, FindPrivateKeyForCert falls back to "the only
                // private key on the token" — which is WRONG the moment a second, unrelated
                // key/cert has ever been provisioned there (e.g. leftover from earlier testing).
                // A mismatched key produces a signature that looks successful here but always
                // fails downstream CMS/XML-DSig verification, so fail loud now instead.
                EnsureKeyMatchesCertificate(session, priv, cert);

                var keyType = GetKeyType(session, priv);
                if (keyType == CKK.CKK_EC)
                {
                    var mech = factories.MechanismFactory.Create(CKM.CKM_ECDSA);
                    byte[] p1363 = session.Sign(mech, priv, digest);
                    byte[] der = TokenSigner.EcdsaP1363ToDer(p1363);
                    return new SignResult(der, cert.RawData, "ECDSA");
                }
                else // RSA
                {
                    byte[] digestInfo = new byte[Sha256DigestInfoPrefix.Length + digest.Length];
                    Buffer.BlockCopy(Sha256DigestInfoPrefix, 0, digestInfo, 0, Sha256DigestInfoPrefix.Length);
                    Buffer.BlockCopy(digest, 0, digestInfo, Sha256DigestInfoPrefix.Length, digest.Length);
                    var mech = factories.MechanismFactory.Create(CKM.CKM_RSA_PKCS);
                    byte[] sig = session.Sign(mech, priv, digestInfo);
                    return new SignResult(sig, cert.RawData, "RSA");
                }
            }
            finally
            {
                try { session.Logout(); } catch { }
            }
        }

        throw new InvalidOperationException(
            $"No PKCS#11 private key found matching certificate serial {cert.SerialNumber}.");
    }

    /// <summary>
    /// Lists X.509 certificates that have a usable signing key present on any connected
    /// PKCS#11 token, read directly from the token (no Windows Certificate Store involved).
    ///
    /// Windows only projects a smart card's certificates into CurrentUser\My via its own
    /// Certificate Propagation service, which fires on card-insertion events and can lag or
    /// simply not run (e.g. service not elevated, missed PnP event). The vendor middleware
    /// (bit4id) and this PKCS#11 module see the token's certificates immediately regardless —
    /// this method lets certificate *listing* be as robust as the *signing* path already is
    /// (<see cref="SignDigest"/> already talks to the token directly, bypassing Windows CAPI).
    ///
    /// A token also carries CA/root certs (its own issuer chain) as plain CKO_CERTIFICATE
    /// objects with no corresponding private key — those aren't something the user could ever
    /// sign with, so they're excluded via Basic Constraints (see inline comment below),
    /// mirroring the Windows-store side's `HasPrivateKey` filter in intent.
    ///
    /// Reading CKO_CERTIFICATE objects does not require C_Login: certificates are public data
    /// on essentially all PKCS#11 tokens, so no PIN is needed just to enumerate them.
    /// Best-effort: any failure (module missing, no token, unreadable object) yields an empty
    /// list rather than throwing, since callers merge this with the Windows-store result.
    /// </summary>
    public static List<CertInfo> ListCerts(string? modulePath = null)
    {
        var results = new List<CertInfo>();
        try
        {
            var lib = GetSharedLibrary(modulePath);

            foreach (var slot in lib.GetSlotList(SlotsType.WithTokenPresent))
            {
                using var session = slot.OpenSession(SessionType.ReadOnly);

                var certTemplate = new List<IObjectAttribute>
                {
                    session.Factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE),
                };
                foreach (var co in session.FindAllObjects(certTemplate))
                {
                    var attrs = session.GetAttributeValue(co, new List<CKA> { CKA.CKA_VALUE });
                    var raw = attrs[0].GetValueAsByteArray();
                    if (raw == null || raw.Length == 0) continue;

                    try
                    {
                        using var cert = new X509Certificate2(raw);

                        // Private-key objects are CKA_PRIVATE and only enumerable after C_Login,
                        // which isn't available at listing time — so a CKA_ID correlation to
                        // CKO_PRIVATE_KEY isn't possible here. Filter out CA/root certs (which a
                        // token also carries as plain CKO_CERTIFICATE objects, its own issuer
                        // chain) via Basic Constraints instead: RFC 5280 requires CA certs to set
                        // cA=TRUE, so anything without that extension is treated as end-entity.
                        var basicConstraints = cert.Extensions
                            .OfType<X509BasicConstraintsExtension>()
                            .FirstOrDefault();
                        if (basicConstraints?.CertificateAuthority == true)
                            continue;

                        results.Add(new CertInfo(
                            Serial: cert.SerialNumber,
                            SubjectDN: cert.Subject,
                            IssuerDN: cert.Issuer,
                            ValidFrom: cert.NotBefore.ToString("O"),
                            ValidTo: cert.NotAfter.ToString("O"),
                            Thumbprint: cert.Thumbprint,
                            Certificate: Convert.ToBase64String(cert.RawData),
                            Algorithm: cert.GetECDsaPublicKey() != null ? "ECDSA" : "RSA"));
                    }
                    catch { /* CKA_VALUE wasn't a parseable X.509 DER cert; skip it */ }
                }
            }
        }
        catch
        {
            // Module missing/unloadable, no token, etc. — return whatever was found so far.
        }

        return results;
    }

    /// <summary>
    /// Finds the private-key object whose CKA_ID matches the on-token certificate equal to
    /// <paramref name="cert"/>. Falls back to a lone signing key when only one is present.
    /// </summary>
    private static IObjectHandle? FindPrivateKeyForCert(ISession session, X509Certificate2 cert)
    {
        // 1. Match the certificate object by raw value to read its CKA_ID.
        byte[]? certId = null;
        var certTemplate = new List<IObjectAttribute>
        {
            session.Factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE),
        };
        foreach (var co in session.FindAllObjects(certTemplate))
        {
            var attrs = session.GetAttributeValue(co, new List<CKA> { CKA.CKA_VALUE, CKA.CKA_ID });
            var val = attrs[0].GetValueAsByteArray();
            if (val != null && val.Length == cert.RawData.Length && val.SequenceEqual(cert.RawData))
            {
                certId = attrs[1].GetValueAsByteArray();
                break;
            }
        }

        // 2. Private key with that CKA_ID.
        if (certId is { Length: > 0 })
        {
            var keyTemplate = new List<IObjectAttribute>
            {
                session.Factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY),
                session.Factories.ObjectAttributeFactory.Create(CKA.CKA_ID, certId),
            };
            var keys = session.FindAllObjects(keyTemplate);
            if (keys.Count > 0) return keys[0];
        }

        // 3. Fallback: single private key on the token.
        var allKeys = session.FindAllObjects(new List<IObjectAttribute>
        {
            session.Factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY),
        });
        return allKeys.Count == 1 ? allKeys[0] : null;
    }

    public static string ResolveModulePath(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return Environment.ExpandEnvironmentVariables(configuredPath.Trim());

        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bit4xpki.dll");
        if (File.Exists(localPath))
            return localPath;

        return DefaultModulePath;
    }

    /// <summary>
    /// Verifies the on-token private key object actually pairs with <paramref name="cert"/>'s
    /// public key before it gets used to sign anything. For RSA this compares CKA_MODULUS
    /// (not sensitive, always readable) against the certificate's RSA modulus byte-for-byte.
    /// </summary>
    private static void EnsureKeyMatchesCertificate(ISession session, IObjectHandle priv, X509Certificate2 cert)
    {
        var keyType = GetKeyType(session, priv);
        if (keyType != CKK.CKK_RSA) return; // EC key/point comparison not implemented; RSA is the reproduced case.

        var rsaPub = cert.GetRSAPublicKey();
        if (rsaPub == null) return;

        var certModulus = rsaPub.ExportParameters(false).Modulus ?? Array.Empty<byte>();
        var attrs = session.GetAttributeValue(priv, new List<CKA> { CKA.CKA_MODULUS });
        var keyModulus = attrs[0].GetValueAsByteArray() ?? Array.Empty<byte>();

        // Both are big-endian unsigned integers but may differ by a leading zero byte
        // (two's-complement sign padding) — trim before comparing.
        static byte[] TrimLeadingZeros(byte[] b)
        {
            int i = 0;
            while (i < b.Length - 1 && b[i] == 0) i++;
            var result = new byte[b.Length - i];
            Buffer.BlockCopy(b, i, result, 0, result.Length);
            return result;
        }

        if (!TrimLeadingZeros(certModulus).SequenceEqual(TrimLeadingZeros(keyModulus)))
        {
            throw new InvalidOperationException(
                $"PKCS#11 key mismatch: the on-token private key (modulus {keyModulus.Length * 8} bits) does not " +
                $"match certificate '{cert.Subject}' (modulus {certModulus.Length * 8} bits). " +
                "This certificate's private key is not actually provisioned on this PKCS#11 token — " +
                "signing with the wrong key would produce a signature that fails downstream verification.");
        }
    }

    private static CKK GetKeyType(ISession session, IObjectHandle key)
    {
        var attrs = session.GetAttributeValue(key, new List<CKA> { CKA.CKA_KEY_TYPE });
        return (CKK)attrs[0].GetValueAsUlong();
    }
}
