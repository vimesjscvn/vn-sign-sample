using Avalonia;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Vimes.SignSDK;
using Vimes.SignSDK.ViewModels;
using Vimes.SignSDK.Merchants.MySign;
using Vimes.SignSDK.Merchants.SmartCA;
using Vimes.SignSDK.Merchants.USB;
using Vimes.SignSDK.Merchants.Self;
using Vimes.SignSDK.Merchants.BCY;
using Vimes.SignSDK.Merchants.SIM;
using Vimes.SignSDK.Merchants.InTrust;
using Vimes.SignSDK.Merchants.CMC;
using Core.Config.Settings;
using Vimes.SignSDK.Helpers;

namespace VimesSignSample;

public static class Program
{
    public static IHost? Host { get; private set; }

    private static readonly string[] _redirectedAssemblies =
    [
        "Vimes.SignSDK.Abstractions",
        "Vimes.SignSDK.Merchant.Base",
        "Vimes.Core",
        "Vimes.Signature.Domain",
    ];

    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, resolveArgs) =>
        {
            var requested = new AssemblyName(resolveArgs.Name);
            if (Array.Exists(_redirectedAssemblies,
                    n => string.Equals(n, requested.Name, StringComparison.OrdinalIgnoreCase)))
            {
                var loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(
                        a.GetName().Name, requested.Name,
                        StringComparison.OrdinalIgnoreCase));
                return loaded;
            }
            return null;
        };

        // User config directory: ~/.config/vimes-sign/
        var userConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "vimes-sign");
        var userConfigFile = Path.Combine(userConfigDir, "appsettings.json");

        // Auto-copy bundled appsettings.json on first run if user config doesn't exist
        if (!File.Exists(userConfigFile))
        {
            Directory.CreateDirectory(userConfigDir);
            var bundledConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (File.Exists(bundledConfig))
            {
                File.Copy(bundledConfig, userConfigFile);
            }
        }

        var builder = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile(userConfigFile, optional: true, reloadOnChange: true);

        var configuration = builder.Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config", "vimes-sign", "logs", "vimes_sample-.txt"),
                rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config =>
            {
                config.AddConfiguration(configuration);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSignSDK(context.Configuration);
                
                services.AddSignSDKMySign();
                services.AddSignSDKSmartCA();
                services.AddSignSDKUSB();
                services.AddSignSDKSelf();
                services.AddSignSDKBCY();
                services.AddSignSDKSIM();
                services.AddSignSDKInTrust();
                services.AddSignSDKCMC();

                services.AddTransient<MainWindow>();
                
                services.AddSingleton<Core.Common.Abstractions.ISignEnvironment>(new DesktopSignEnvironment());
            })
            .UseSerilog()
            .Build();

        if (args.Contains("--test-e2e"))
        {
            RunE2ETestAsync().GetAwaiter().GetResult();
        }
        else
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }

    private static async Task RunE2ETestAsync()
    {
        Console.WriteLine("[E2E Test] Initializing Headless E2E verification test...");
        
        string pdfPath = Path.Combine(Directory.GetCurrentDirectory(), "test_doc.pdf");
        string certFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "certs");
        string signedPath = Path.Combine(Directory.GetCurrentDirectory(), "test_doc_signed.pdf");

        // Ensure directories exist
        Directory.CreateDirectory(certFolder);

        // Calculate expected certificate file path based on default user name
        string defaultUser = "TestUser";
        string defaultPass = "password123";
        string serial = UtilSigner.ConvertStringToNumber(defaultUser);
        string certPath = Path.Combine(certFolder, $"{serial}.pfx");

        try
        {
            // 1. Generate PDF document using iText 7
            Console.WriteLine("[E2E Test] Generating temporary PDF document...");
            using (var writer = new iText.Kernel.Pdf.PdfWriter(pdfPath))
            using (var pdf = new iText.Kernel.Pdf.PdfDocument(writer))
            {
                pdf.AddNewPage();
            }
            Console.WriteLine($"[E2E Test] PDF generated at: {pdfPath}");

            // 2. Generate self-signed certificate using System.Security.Cryptography
            Console.WriteLine($"[E2E Test] Generating self-signed PFX certificate (Serial: {serial})...");
            using (var rsa = RSA.Create(2048))
            {
                var req = new CertificateRequest($"CN={defaultUser}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(365));
                var pfxBytes = cert.Export(X509ContentType.Pfx, defaultPass);
                await File.WriteAllBytesAsync(certPath, pfxBytes);
            }
            Console.WriteLine($"[E2E Test] PFX certificate generated at: {certPath}");

            // 3. Resolve SignSDK Client
            var signClient = Host!.Services.GetRequiredService<ISignSDKClient>();

            // Configure AppSettings Certificate/Mssp details first so DI services resolve it correctly
            var appSettings = Host!.Services.GetRequiredService<AppSettings>();
            if (appSettings.CertificateSetting == null)
            {
                appSettings.CertificateSetting = new CertificateSetting();
            }
            appSettings.CertificateSetting.DefaultUserName = defaultUser;
            appSettings.CertificateSetting.DefaultPassword = defaultPass;

            // Login
            Console.WriteLine("[E2E Test] Authenticating with SELF merchant...");
            var loginResult = await signClient.LoginAsync(defaultUser, defaultPass, "SELF", "", "");
            if (!loginResult.Success)
            {
                throw new Exception($"Login failed: {loginResult.ErrorMessage}");
            }
            Console.WriteLine($"[E2E Test] Authentication successful. Session: {loginResult.UserName}");

            // Retrieve certificates to get credentialID
            Console.WriteLine("[E2E Test] Retrieving certificate credentials...");
            var certs = await signClient.GetCertificatesAsync(loginResult.UserName, loginResult.BearerToken ?? "", "", "SELF");
            var targetCert = certs?.FirstOrDefault();
            if (targetCert == null)
            {
                throw new Exception("No certificates returned from the SELF local store.");
            }
            Console.WriteLine($"[E2E Test] Loaded certificate: {targetCert.subjectDN} (Credential ID: {targetCert.credentialID})");

            // 4. Construct sign request
            var docBytes = await File.ReadAllBytesAsync(pdfPath);
            var docRequest = new SignDocumentRequest
            {
                FileName = Path.GetFileName(pdfPath),
                FileData = Convert.ToBase64String(docBytes),
                SignerName = "Vimes E2E Tester",
                Page = 1,
                X = 100,
                Y = 100,
                Width = 150,
                Height = 150
            };

            var batchRequest = new SignDocumentsRequest
            {
                UserName = loginResult.UserName,
                CredentialID = targetCert.credentialID,
                MerchantId = "SELF",
                CertFileName = certPath,
                CertPassword = defaultPass,
                Documents = new List<SignDocumentRequest> { docRequest }
            };

            // 5. Call SDK to sign
            Console.WriteLine("[E2E Test] Calling Vimes SignSDK sign client...");
            var results = await signClient.SignDocumentsAsync(batchRequest);
            var result = results?.FirstOrDefault();

            if (result == null)
            {
                throw new Exception("Sign SDK returned null or empty result array.");
            }

            if (!result.Success)
            {
                throw new Exception($"Sign SDK error: {result.ErrorMessage}");
            }

            // 6. Save signed file
            Console.WriteLine($"[E2E Test] Signature generated! Transaction: {result.TransactionId}");
            byte[] signedBytes;
            if (result.SignedFileUrl.Contains(","))
            {
                signedBytes = Convert.FromBase64String(result.SignedFileUrl.Split(',').Last());
            }
            else
            {
                signedBytes = Convert.FromBase64String(result.SignedFileUrl);
            }
            await File.WriteAllBytesAsync(signedPath, signedBytes);
            Console.WriteLine($"[E2E Test] Signed PDF saved to: {signedPath}");

            // 7. Verify signature in signed PDF
            Console.WriteLine("[E2E Test] Verifying signature structure in the output PDF...");
            using (var reader = new iText.Kernel.Pdf.PdfReader(signedPath))
            using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader))
            {
                var sigUtil = new iText.Signatures.SignatureUtil(pdfDoc);
                var names = sigUtil.GetSignatureNames();
                if (names == null || names.Count == 0)
                {
                    throw new Exception("Verification failed: No signatures found in signed PDF.");
                }

                Console.WriteLine($"[E2E Test] Success! Found signatures in output: {string.Join(", ", names)}");
            }

            Console.WriteLine("[E2E Test] ===============================================");
            Console.WriteLine("[E2E Test] E2E HEADLESS TEST COMPLETED SUCCESSFULLY!");
            Console.WriteLine("[E2E Test] ===============================================");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[E2E Test] ERROR: Headless E2E test failed! Details: {ex.ToString()}");
            Console.ResetColor();
            Environment.Exit(1);
        }
        finally
        {
            // Clean up files
            try { if (File.Exists(pdfPath)) File.Delete(pdfPath); } catch {}
            try { if (File.Exists(certPath)) File.Delete(certPath); } catch {}
            try { if (File.Exists(signedPath)) File.Delete(signedPath); } catch {}
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

public class DesktopSignEnvironment : Core.Common.Abstractions.ISignEnvironment
{
    private static string GetDataDirectory()
    {
        // Use ~/.config/vimes-sign/ as writable data directory on macOS
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataDir = Path.Combine(home, ".config", "vimes-sign");
        Directory.CreateDirectory(dataDir);
        return dataDir;
    }

    public string WebRootPath { get; set; } = Path.Combine(GetDataDirectory(), "wwwroot");
    public string ContentRootPath { get; set; } = GetDataDirectory();
    public string EnvironmentName { get; set; } = "Development";
}
