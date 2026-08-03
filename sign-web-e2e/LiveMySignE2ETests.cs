using System.Text.Json;
using System.Text.RegularExpressions;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Signatures;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace VMSign.Web.E2E;

/// <summary>
/// Opt-in E2E coverage for the real Viettel MySign service.
///
/// This test is intentionally skipped unless both VMSIGN_MYSIGN_USERNAME and
/// VMSIGN_MYSIGN_PASSWORD are present. The values are read only to fill the
/// password-protected login form and are never written to test output.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("LiveMySign")]
public class LiveMySignE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5100";
    private const float LiveSigningTimeoutMs = 180_000;

    private string? _fixturePdfPath;
    private string? _downloadedPdfPath;
    private string? _afterScreenshotPath;
    private bool _afterScreenshotCaptured;

    public override BrowserNewContextOptions ContextOptions()
    {
        return new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 850 },
            ColorScheme = ColorScheme.Light,
            AcceptDownloads = true
        };
    }

    [Test]
    public async Task LoginUploadPlaceSignAndVerify()
    {
        var userName = Environment.GetEnvironmentVariable("VMSIGN_MYSIGN_USERNAME");
        var password = Environment.GetEnvironmentVariable("VMSIGN_MYSIGN_PASSWORD");

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            Assert.Ignore(
                "Live MySign E2E is opt-in. Set VMSIGN_MYSIGN_USERNAME and " +
                "VMSIGN_MYSIGN_PASSWORD to run it.");
        }

        var screenshotDirectory = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "screenshots",
            "live-mysign");
        Directory.CreateDirectory(screenshotDirectory);

        var beforeScreenshotPath = Path.Combine(
            screenshotDirectory, "sign-web-before-sign.png");
        var successScreenshotPath = Path.Combine(
            screenshotDirectory, "sign-web-after-success.png");
        var failureScreenshotPath = Path.Combine(
            screenshotDirectory, "sign-web-after-failure.png");

        DeleteIfPresent(beforeScreenshotPath);
        DeleteIfPresent(successScreenshotPath);
        DeleteIfPresent(failureScreenshotPath);

        var runId = Guid.NewGuid().ToString("N");
        _fixturePdfPath = Path.Combine(
            Path.GetTempPath(), $"vmsign-mysign-live-e2e-{runId}.pdf");
        _downloadedPdfPath = Path.Combine(
            Path.GetTempPath(), $"vmsign-mysign-live-e2e-signed-{runId}.pdf");
        CreateTestOnlyPdf(_fixturePdfPath);

        try
        {
            await Page.GotoAsync(
                BaseUrl,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 30_000
                });

            await LoginToViettelAsync(userName!, password!);
            await Page.Locator("#signerName").FillAsync("Sample User");
            await Page.Locator("#signerPosition").FillAsync("Visual Placement");
            await UploadAndPlaceSignatureAsync(_fixturePdfPath);

            await CaptureMaskedScreenshotAsync(beforeScreenshotPath);
            TestContext.AddTestAttachment(
                beforeScreenshotPath, "sign-web immediately before the live signing request");

            var signResponseTask = Page.WaitForResponseAsync(
                response =>
                    response.Url.Contains("/Signing/SignPdf", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(response.Request.Method, "POST", StringComparison.OrdinalIgnoreCase),
                new PageWaitForResponseOptions { Timeout = LiveSigningTimeoutMs });

            await Page.Locator("#btnSignPdf").ClickAsync();
            await ConfirmCredentialPickerIfShownAsync(signResponseTask);

            var signResponse = await signResponseTask;
            Assert.That(
                signResponse.Ok,
                Is.True,
                $"The MySign request returned HTTP {signResponse.Status}.");

            var signResult = await ReadSignResultAsync(signResponse);
            _afterScreenshotPath = signResult.Success
                ? successScreenshotPath
                : failureScreenshotPath;

            if (signResult.Success)
            {
                await Expect(Page.Locator("#signingProgressDialog"))
                    .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 30_000 });
                await Expect(Page.Locator("#downloadBtn"))
                    .ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 30_000 });
                await Expect(Page.Locator(".toast--success").Last)
                    .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
            }
            else
            {
                await Expect(Page.Locator("#signingProgressDialog"))
                    .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 30_000 });
                await Expect(Page.Locator(".toast--error").Last)
                    .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
            }

            await CaptureMaskedScreenshotAsync(_afterScreenshotPath);
            _afterScreenshotCaptured = true;
            TestContext.AddTestAttachment(
                _afterScreenshotPath,
                signResult.Success
                    ? "sign-web after successful live MySign signing"
                    : "sign-web after failed live MySign signing");

            Assert.That(
                signResult.Success,
                Is.True,
                "MySign returned success=false. See sign-web-after-failure.png.");

            var signedPdfPath = await GetSignedPdfAsync(signResult.OutputPath);
            VerifySignedPdf(signedPdfPath);
        }
        finally
        {
            if (!_afterScreenshotCaptured && Page is { IsClosed: false })
            {
                _afterScreenshotPath ??= failureScreenshotPath;
                try
                {
                    await CaptureMaskedScreenshotAsync(_afterScreenshotPath);
                    _afterScreenshotCaptured = true;
                    TestContext.AddTestAttachment(
                        _afterScreenshotPath,
                        "sign-web state when the live MySign E2E stopped");
                }
                catch
                {
                    // Do not hide the original test failure if the browser is already unavailable.
                }
            }

            DeleteIfPresent(_fixturePdfPath);
            DeleteIfPresent(_downloadedPdfPath);
        }
    }

    private async Task LoginToViettelAsync(string userName, string password)
    {
        await Page.Locator("#sessionPill").ClickAsync();
        await Expect(Page.Locator("#sessionFlyout"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        var viettelMerchant = Page.Locator(
            "#loginMerchantList > div[onclick*=\"'VIETTEL'\"]");
        await Expect(viettelMerchant).ToHaveCountAsync(1);
        await viettelMerchant.ClickAsync();

        // Selecting a merchant re-renders the flyout, so fill credentials afterwards.
        await Page.Locator("#loginUser").FillAsync(userName);
        await Page.Locator("#loginPass").FillAsync(password);

        var loginResponseTask = Page.WaitForResponseAsync(
            response =>
                response.Url.Contains("/Session/Login", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(response.Request.Method, "POST", StringComparison.OrdinalIgnoreCase),
            new PageWaitForResponseOptions { Timeout = 60_000 });

        await Page.Locator(".flyout__login-btn").ClickAsync();
        var loginResponse = await loginResponseTask;

        Assert.That(
            loginResponse.Ok,
            Is.True,
            $"The MySign login request returned HTTP {loginResponse.Status}.");

        using var loginDocument = JsonDocument.Parse(await loginResponse.TextAsync());
        var root = loginDocument.RootElement;
        var loginSucceeded =
            root.TryGetProperty("success", out var successElement) &&
            successElement.ValueKind == JsonValueKind.True;

        Assert.That(
            loginSucceeded,
            Is.True,
            "MySign login failed. Credentials and the response body are intentionally not logged.");

        var certificateCount =
            root.TryGetProperty("certificates", out var certificatesElement) &&
            certificatesElement.ValueKind == JsonValueKind.Array
                ? certificatesElement.GetArrayLength()
                : 0;
        var hasDefaultCredential =
            root.TryGetProperty("credentialId", out var credentialElement) &&
            credentialElement.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(credentialElement.GetString());

        Assert.That(
            certificateCount > 0 && hasDefaultCredential,
            Is.True,
            "MySign login succeeded, but no registered signing certificate was returned.");

        await Expect(Page.Locator("#sessionPill"))
            .ToHaveClassAsync(
                new Regex("logged-in"),
                new LocatorAssertionsToHaveClassOptions { Timeout = 30_000 });
    }

    private async Task UploadAndPlaceSignatureAsync(string pdfPath)
    {
        var autoAcroTrack = Page.Locator("#autoAcroTrack");
        if (await autoAcroTrack.EvaluateAsync<bool>(
                "element => element.classList.contains('active')"))
        {
            await Page.Locator("#toggleAutoAcro").ClickAsync();
        }

        await Page.Locator("#pdfFileInput").SetInputFilesAsync(pdfPath);
        await Expect(Page.Locator("#canvasContainer"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Expect(Page.Locator("#totalPages")).ToHaveTextAsync("1");

        var canvas = Page.Locator(".upper-canvas");
        await Expect(canvas)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        var bounds = await canvas.BoundingBoxAsync();
        Assert.That(bounds, Is.Not.Null, "The PDF canvas did not expose drawable bounds.");

        var startX = bounds!.X + (bounds.Width * 0.25f);
        var startY = bounds.Y + (bounds.Height * 0.30f);
        var endX = bounds.X + (bounds.Width * 0.55f);
        var endY = bounds.Y + (bounds.Height * 0.45f);

        await Page.Mouse.MoveAsync(startX, startY);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(
            endX,
            endY,
            new MouseMoveOptions { Steps = 12 });
        await Page.Mouse.UpAsync();

        await Expect(Page.Locator("#placementDialog"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Page.Locator("#btnConfirmPlace").ClickAsync();
        await Expect(Page.Locator("#btnSignPdf"))
            .ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#signBtnLabel")).ToContainTextAsync("1");
    }

    private async Task ConfirmCredentialPickerIfShownAsync(Task<IResponse> signResponseTask)
    {
        var picker = Page.Locator("#credentialPickerModal");

        for (var attempt = 0; attempt < 50 && !signResponseTask.IsCompleted; attempt++)
        {
            if (await picker.IsVisibleAsync())
            {
                Assert.That(
                    await Page.Locator("#credentialPickerList > div").CountAsync(),
                    Is.GreaterThan(0),
                    "The credential picker opened without a certificate.");
                await Page.Locator(
                        "#credentialPickerModal button",
                        new PageLocatorOptions { HasText = "Xác nhận" })
                    .ClickAsync();
                return;
            }

            await Page.WaitForTimeoutAsync(100);
        }
    }

    private async Task<string> GetSignedPdfAsync(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath))
        {
            return outputPath;
        }

        var download = await Page.RunAndWaitForDownloadAsync(
            () => Page.Locator("#downloadBtn").ClickAsync(),
            new PageRunAndWaitForDownloadOptions { Timeout = 30_000 });
        await download.SaveAsAsync(_downloadedPdfPath!);
        return _downloadedPdfPath!;
    }

    private async Task CaptureMaskedScreenshotAsync(string path)
    {
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            FullPage = false,
            Animations = ScreenshotAnimations.Disabled,
            Caret = ScreenshotCaret.Hide,
            Mask =
            [
                Page.Locator("#sessionPill"),
                Page.Locator("#sessionFlyout"),
                Page.Locator("#loginUser"),
                Page.Locator("#loginPass"),
                Page.Locator("#credentialPickerModal"),
                Page.Locator("#signerName"),
                Page.Locator("#logsBody"),
                Page.Locator(".toast")
            ],
            MaskColor = "#334155"
        });
    }

    private static async Task<SignResponse> ReadSignResultAsync(IResponse response)
    {
        using var document = JsonDocument.Parse(await response.TextAsync());
        var root = document.RootElement;

        var success =
            root.TryGetProperty("success", out var successElement) &&
            successElement.ValueKind == JsonValueKind.True;
        var outputPath =
            root.TryGetProperty("outputPath", out var outputPathElement) &&
            outputPathElement.ValueKind == JsonValueKind.String
                ? outputPathElement.GetString()
                : null;

        return new SignResponse(success, outputPath);
    }

    private static void CreateTestOnlyPdf(string path)
    {
        using var writer = new PdfWriter(path);
        using var document = new PdfDocument(writer);
        var page = document.AddNewPage();
        var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
        var canvas = new PdfCanvas(page);

        canvas.BeginText()
            .SetFontAndSize(font, 32)
            .MoveText(155, 700)
            .ShowText("E2E TEST ONLY")
            .EndText();
    }

    private static void VerifySignedPdf(string path)
    {
        Assert.That(File.Exists(path), Is.True, "The signed PDF is not accessible.");
        Assert.That(new FileInfo(path).Length, Is.GreaterThan(100), "The signed PDF is empty.");

        using var reader = new PdfReader(path);
        using var document = new PdfDocument(reader);
        var signatureUtil = new SignatureUtil(document);
        var signatureNames = signatureUtil.GetSignatureNames();

        Assert.That(
            signatureNames,
            Is.Not.Empty,
            "iText did not find a digital signature in the signed PDF.");

        var latestSignature = signatureUtil.ReadSignatureData(signatureNames[^1]);
        Assert.That(
            latestSignature.VerifySignatureIntegrityAndAuthenticity(),
            Is.True,
            "iText found the signature, but its integrity/authenticity check failed.");
    }

    private static void DeleteIfPresent(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record SignResponse(bool Success, string? OutputPath);
}
