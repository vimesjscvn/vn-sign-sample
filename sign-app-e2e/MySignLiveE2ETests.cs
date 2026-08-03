using System.Drawing;
using System.Drawing.Imaging;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using iText.Kernel.Pdf;
using iText.Signatures;
using NUnit.Framework;

namespace VMSign.AppE2E;

[TestFixture]
[NonParallelizable]
[Platform("Win")]
public sealed class MySignLiveE2ETests
{
    private const string UsernameVariable = "VMSIGN_MYSIGN_USERNAME";
    private const string PasswordVariable = "VMSIGN_MYSIGN_PASSWORD";
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan SigningTimeout = TimeSpan.FromSeconds(180);
    private static readonly string[] SensitiveAutomationIds =
    {
        "btnSession",
        "txtUserName",
        "txtPassword",
        "cboCerts",
        "lstFilePath",
        "txtPdfOutputDir",
        "txtSignerName",
        "txtLogs",
    };

    [Test]
    [Category("Live")]
    public void Credentialed_MySign_flow_signs_and_verifies_a_generated_pdf()
    {
        var username = Environment.GetEnvironmentVariable(UsernameVariable);
        var password = Environment.GetEnvironmentVariable(PasswordVariable);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Assert.Ignore(
                $"Set {UsernameVariable} and {PasswordVariable} to opt in to the live MySign E2E test.");
            return;
        }

        var runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var runDirectory = Path.Combine(
            Path.GetTempPath(),
            $"vmsign-mysign-live-{runId}");
        var inputDirectory = Path.Combine(runDirectory, "input");
        var outputDirectory = Path.Combine(runDirectory, "output");
        Directory.CreateDirectory(outputDirectory);

        var screenshotDirectory = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "screenshots",
            "mysign-live",
            runId);
        Directory.CreateDirectory(screenshotDirectory);

        var inputPdf = TemporaryPdf.CreateE2ETestOnlyDocument(inputDirectory);
        var beforeScreenshot = Path.Combine(screenshotDirectory, "01-before-sign.png");
        var finalScreenshotCaptured = false;
        VMSignUiSession? app = null;

        try
        {
            app = VMSignUiSession.Start();
            WindowsUiDriver.PlaceMainWindowForScreenshots(app.ProcessId);
            LogInAndWaitForCertificate(app, username, password);
            LoadPdfAndPlaceSignature(app, inputPdf, outputDirectory);

            CaptureAndAttach(app, beforeScreenshot, "sign-app immediately before live MySign signing");

            var logs = app.RequireByAutomationId("txtLogs").AsTextBox();
            app.RequireByAutomationId("btnSign").AsButton().Invoke();
            app.WaitUntil(
                () => logs.Text.Contains(
                    "Invoking SignSDK client signing workflow...",
                    StringComparison.Ordinal),
                "the sign-app signing workflow to start");
            app.WaitUntil(
                () => HasSigningSucceeded(logs.Text) || HasSigningFailed(logs.Text),
                "the live MySign signing operation to finish",
                SigningTimeout);

            var succeeded = HasSigningSucceeded(logs.Text);
            var afterScreenshot = Path.Combine(
                screenshotDirectory,
                succeeded ? "02-after-success.png" : "02-after-failure.png");
            app.RequireByAutomationId("lstFilePath").Focus();
            CaptureAndAttach(
                app,
                afterScreenshot,
                succeeded
                    ? "sign-app after successful live MySign signing"
                    : "sign-app after failed live MySign signing");
            finalScreenshotCaptured = true;

            Assert.That(
                succeeded,
                Is.True,
                "The live MySign request was not successful. See the attached failure screenshot.");

            var signedPdf = WaitForSignedPdf(app, outputDirectory);
            VerifyPdfSignature(signedPdf);
        }
        catch
        {
            if (app is not null && !finalScreenshotCaptured)
            {
                CaptureFailureWithoutMaskingOriginalError(app, screenshotDirectory);
            }

            throw;
        }
        finally
        {
            app?.Dispose();
            DeleteRunDirectory(runDirectory);
        }
    }

    private static void LogInAndWaitForCertificate(
        VMSignUiSession app,
        string username,
        string password)
    {
        app.RequireByAutomationId("btnSession").AsButton().Click();
        app.RequireByAutomationId("txtUserName").AsTextBox().Text = username;
        app.RequireByAutomationId("txtPassword").AsTextBox().Text = password;
        app.RequireByAutomationId("btnLogin").AsButton().Click();

        var sessionStatus = app.RequireByAutomationId("lblSessionStatus");
        var logs = app.RequireByAutomationId("txtLogs").AsTextBox();
        app.WaitUntil(
            () => sessionStatus.Name.Contains("Active Session:", StringComparison.Ordinal)
                  || HasAuthenticationFailed(logs.Text),
            "MySign authentication to finish",
            LoginTimeout);

        if (!sessionStatus.Name.Contains("Active Session:", StringComparison.Ordinal)
            || !sessionStatus.Name.Contains("(VIETTEL)", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(
                "MySign authentication did not establish a VIETTEL session. "
                + "Credential values were intentionally omitted.");
        }

        app.WaitUntil(
            () => logs.Text.Contains("Parsed and loaded ", StringComparison.Ordinal)
                  && logs.Text.Contains(
                      " verified certificates into local registry.",
                      StringComparison.Ordinal),
            "at least one MySign certificate to be loaded",
            LoginTimeout);

        // Close the credential flyout before returning to the PDF workspace.
        app.MainWindow.Focus();
        Keyboard.Press(VirtualKeyShort.ESCAPE);
    }

    private static void LoadPdfAndPlaceSignature(
        VMSignUiSession app,
        string inputPdf,
        string outputDirectory)
    {
        app.RequireByAutomationId("txtPdfOutputDir").AsTextBox().Text = outputDirectory;

        var autoCreate = app.RequireByAutomationId("chkAutoCreateAcroField").AsCheckBox();
        if (autoCreate.IsChecked == true)
        {
            autoCreate.Click();
        }

        app.RequireByAutomationId("btnBrowse").AsButton().Click();
        WindowsUiDriver.SelectPdfFromOpenDialog(app.ProcessId, inputPdf);

        var previewStatus = app.RequireByAutomationId("lblPreviewMock");
        app.WaitUntil(
            () => previewStatus.Name == "Trang 1 / 1",
            "the generated E2E TEST ONLY PDF preview to load");

        var pdfPreview = app.RequireByAutomationId("imgPdfPage");
        WindowsUiDriver.DragWithin(pdfPreview.BoundingRectangle, app.ProcessId);
        app.RequireByAutomationId("btnConfirmPlace").AsButton().Click();
        app.WaitUntil(
            () => app.RequireByAutomationId("btnSign").AsButton().IsEnabled,
            "the live PDF sign action to become enabled");
    }

    private static string WaitForSignedPdf(VMSignUiSession app, string outputDirectory)
    {
        string? signedPdf = null;
        app.WaitUntil(
            () =>
            {
                signedPdf = Directory.EnumerateFiles(outputDirectory, "*_signed.pdf")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                return signedPdf is not null && new FileInfo(signedPdf).Length > 0;
            },
            "the signed PDF output file",
            TimeSpan.FromSeconds(15));

        return signedPdf!;
    }

    private static void VerifyPdfSignature(string signedPdf)
    {
        using var reader = new PdfReader(signedPdf);
        using var pdf = new PdfDocument(reader);
        var signatureUtility = new SignatureUtil(pdf);
        var signatureNames = signatureUtility.GetSignatureNames();

        Assert.That(signatureNames, Is.Not.Empty, "The output PDF has no embedded signature.");
        foreach (var signatureName in signatureNames)
        {
            var signature = signatureUtility.ReadSignatureData(signatureName);
            Assert.That(
                signature.VerifySignatureIntegrityAndAuthenticity(),
                Is.True,
                $"Signature field '{signatureName}' failed the iText integrity check.");
        }
    }

    private static bool HasAuthenticationFailed(string logs) =>
        logs.Contains("Authentication Failed:", StringComparison.Ordinal)
        || logs.Contains("Runtime Authentication Error:", StringComparison.Ordinal);

    private static bool HasSigningSucceeded(string logs) =>
        logs.Contains("Signature execution finished successfully", StringComparison.Ordinal);

    private static bool HasSigningFailed(string logs) =>
        logs.Contains("Signature Rejected by Remote Gateway:", StringComparison.Ordinal)
        || logs.Contains("Signature Generation Exception:", StringComparison.Ordinal)
        || logs.Contains("Signature failed:", StringComparison.Ordinal)
        || logs.Contains("Invalid operation:", StringComparison.Ordinal);

    private static void CaptureAndAttach(
        VMSignUiSession app,
        string path,
        string description)
    {
        app.MainWindow.Focus();
        Thread.Sleep(250);

        using var capture = Capture.Element(app.MainWindow);
        using var bitmap = (Bitmap)capture.Bitmap.Clone();
        var dpiScale = WindowsUiDriver.GetMainWindowDpiScale(app.ProcessId);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            foreach (var automationId in SensitiveAutomationIds)
            {
                RedactElement(
                    graphics,
                    bitmap.Size,
                    app.MainWindow.BoundingRectangle,
                    dpiScale,
                    app.FindByAutomationId(automationId));
            }
        }

        bitmap.Save(path, ImageFormat.Png);
        TestContext.AddTestAttachment(path, description);
    }

    private static void RedactElement(
        Graphics graphics,
        Size imageSize,
        Rectangle windowBounds,
        float dpiScale,
        AutomationElement? element)
    {
        if (element is null)
        {
            return;
        }

        Rectangle elementBounds;
        try
        {
            elementBounds = element.BoundingRectangle;
        }
        catch
        {
            return;
        }

        var redactionBounds = Rectangle.FromLTRB(
            (int)Math.Floor((elementBounds.Left - windowBounds.Left) * dpiScale),
            (int)Math.Floor((elementBounds.Top - windowBounds.Top) * dpiScale),
            (int)Math.Ceiling((elementBounds.Right - windowBounds.Left) * dpiScale),
            (int)Math.Ceiling((elementBounds.Bottom - windowBounds.Top) * dpiScale));
        redactionBounds.Inflate(8, 8);
        redactionBounds.Intersect(new Rectangle(Point.Empty, imageSize));
        if (redactionBounds.Width > 0 && redactionBounds.Height > 0)
        {
            graphics.FillRectangle(Brushes.DarkSlateGray, redactionBounds);
        }
    }

    private static void CaptureFailureWithoutMaskingOriginalError(
        VMSignUiSession app,
        string screenshotDirectory)
    {
        try
        {
            var path = Path.Combine(screenshotDirectory, "02-after-failure.png");
            CaptureAndAttach(app, path, "sign-app after failed live MySign E2E flow");
        }
        catch (Exception screenshotError)
        {
            TestContext.Error.WriteLine(
                $"Could not capture the failure screenshot: {screenshotError.GetType().Name}");
        }
    }

    private static void DeleteRunDirectory(string runDirectory)
    {
        try
        {
            if (Directory.Exists(runDirectory))
            {
                Directory.Delete(runDirectory, recursive: true);
            }
        }
        catch (IOException cleanupError)
        {
            TestContext.Error.WriteLine(
                $"Could not remove the isolated live-test directory: {cleanupError.GetType().Name}");
        }
        catch (UnauthorizedAccessException cleanupError)
        {
            TestContext.Error.WriteLine(
                $"Could not remove the isolated live-test directory: {cleanupError.GetType().Name}");
        }
    }
}
