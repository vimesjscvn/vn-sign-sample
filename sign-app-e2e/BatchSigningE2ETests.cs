using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FlaUI.Core.AutomationElements;
using NUnit.Framework;

namespace VMSign.AppE2E;

[TestFixture]
[NonParallelizable]
[Platform("Win")]
public sealed class BatchSigningE2ETests
{
    private static readonly TimeSpan SigningTimeout = TimeSpan.FromSeconds(60);

    [Test]
    public void Batch_signing_with_a_self_signed_pfx_signs_every_pdf_in_the_source_folder()
    {
        var runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var runDirectory = Path.Combine(Path.GetTempPath(), $"vmsign-batch-e2e-{runId}");
        var sourceDirectory = Path.Combine(runDirectory, "source");
        var outputDirectory = Path.Combine(runDirectory, "output");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(outputDirectory);

        TemporaryPdf.CreateE2ETestOnlyDocument(sourceDirectory);
        TemporaryPdf.CreateE2ETestOnlyDocument(sourceDirectory);
        var pfxPath = CreateSelfSignedPfx(runDirectory, out var pfxPassword);

        VMSignUiSession? app = null;
        try
        {
            app = VMSignUiSession.Start();

            app.RequireByName("Chức năng ▼").AsButton().Click();
            app.RequireByName("📁 Ký hàng loạt").Click();
            app.RequireByAutomationId("txtBatchFolder").AsTextBox().Text = sourceDirectory;
            app.RequireByAutomationId("txtBatchOutput").AsTextBox().Text = outputDirectory;
            app.RequireByAutomationId("txtBatchCertPath").AsTextBox().Text = pfxPath;
            app.RequireByAutomationId("txtBatchCertPass").AsTextBox().Text = pfxPassword;

            var status = app.RequireByAutomationId("lblBatchStatus");
            app.RequireByAutomationId("btnBatchSign").AsButton().Click();
            app.WaitUntil(
                () => status.Name.Contains("thành công", StringComparison.OrdinalIgnoreCase)
                      || status.Name.Contains("thất bại", StringComparison.OrdinalIgnoreCase)
                      || status.Name.Contains("lỗi", StringComparison.OrdinalIgnoreCase),
                "batch signing to finish",
                SigningTimeout);

            Assert.That(status.Name, Is.EqualTo("Ký thành công."),
                $"Batch signing did not report success. Status: '{status.Name}'");

            var signedFiles = Directory.GetFiles(outputDirectory, "*.pdf");
            Assert.That(signedFiles, Has.Length.EqualTo(2),
                "Every PDF in the source folder should have a signed counterpart in the output folder.");
            foreach (var file in signedFiles)
            {
                Assert.That(new FileInfo(file).Length, Is.GreaterThan(0), $"{file} should not be empty.");
            }
        }
        finally
        {
            app?.Dispose();
            try
            {
                Directory.Delete(runDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a lingering temp folder is not a test failure.
            }
        }
    }

    private static string CreateSelfSignedPfx(string directory, out string password)
    {
        password = "batchpass123";
        var pfxPath = Path.Combine(directory, "batch-test-cert.pfx");
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=BatchSignE2ETest", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(
            DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(365));
        File.WriteAllBytes(pfxPath, certificate.Export(X509ContentType.Pfx, password));
        return pfxPath;
    }
}
