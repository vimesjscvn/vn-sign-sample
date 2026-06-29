using System.Security.Cryptography.X509Certificates;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;
using ISession = Net.Pkcs11Interop.HighLevelAPI.ISession;

namespace UsbTokenAgent;

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
///   • input  = the pre-computed SHA-256 digest (SHA256(authAttrs)) from SignPdfFile.createHash
///   • RSA    → CKM_RSA_PKCS over DigestInfo(SHA-256 prefix || digest)  (= CAPI RSA.SignHash)
///   • ECDSA  → CKM_ECDSA over the raw digest → P1363 (R||S) → DER       (= TokenSigner output)
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

    /// <summary>
    /// Signs <paramref name="digest"/> on the token via PKCS#11, authenticating with
    /// <paramref name="pin"/> (no interactive dialog). The signing key is selected by matching
    /// the X.509 cert (<paramref name="cert"/>) on the token, then signing with its private key.
    /// </summary>
    public static SignResult SignDigest(X509Certificate2 cert, byte[] digest, string pin, string? modulePath = null)
    {
        modulePath ??= DefaultModulePath;
        if (!File.Exists(modulePath))
            throw new FileNotFoundException($"PKCS#11 module not found: {modulePath}");
        if (string.IsNullOrEmpty(pin))
            throw new ArgumentException("PKCS#11 signing requires a PIN", nameof(pin));

        var factories = new Pkcs11InteropFactories();
        using var lib = factories.Pkcs11LibraryFactory.LoadPkcs11Library(
            factories, modulePath, AppType.MultiThreaded);

        // Pick the first slot that has a token present.
        var slots = lib.GetSlotList(SlotsType.WithTokenPresent);
        if (slots.Count == 0) throw new InvalidOperationException("No PKCS#11 token present.");

        foreach (var slot in slots)
        {
            using var session = slot.OpenSession(SessionType.ReadOnly);
            try { session.Login(CKU.CKU_USER, pin); }
            catch (Pkcs11Exception ex) when (ex.RV == CKR.CKR_USER_ALREADY_LOGGED_IN) { /* token already authenticated — fine */ }
            try
            {
                var priv = FindPrivateKeyForCert(session, cert);
                if (priv == null) continue;

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

    private static CKK GetKeyType(ISession session, IObjectHandle key)
    {
        var attrs = session.GetAttributeValue(key, new List<CKA> { CKA.CKA_KEY_TYPE });
        return (CKK)attrs[0].GetValueAsUlong();
    }
}
