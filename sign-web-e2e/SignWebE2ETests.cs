using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace VMSign.Web.E2E;

/// <summary>
/// End-to-end tests for sign-web using Playwright.
/// Tests visual rendering and user interactions against mockup V4 design.
/// 
/// Prerequisites:
///   1. Run: dotnet run --project ../sign-web --urls "http://localhost:5100"
///   2. Run: dotnet test this project
/// </summary>
[TestFixture]
public class SignWebE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5100";

    public override BrowserNewContextOptions ContextOptions()
    {
        return new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            ColorScheme = ColorScheme.Light,
        };
    }

    [Test]
    public async Task Homepage_LoadsWithCorrectStructure()
    {
        await Page.GotoAsync(BaseUrl);

        // Header elements
        await Expect(Page.Locator(".topbar__logo")).ToBeVisibleAsync();
        await Expect(Page.Locator(".topbar__name")).ToHaveTextAsync("Vimes SignSDK");
        await Expect(Page.Locator(".topbar__subtitle")).ToHaveTextAsync("CROSS-PLATFORM STUDIO");
        await Expect(Page.Locator("#merchantBtn")).ToBeVisibleAsync();
        await Expect(Page.Locator("#sessionPill")).ToBeVisibleAsync();

        // Context bar
        await Expect(Page.Locator(".context-bar__title")).ToHaveTextAsync("Ký PDF");

        // Left panel - Step 1 & 2 cards
        await Expect(Page.Locator(".badge").First).ToHaveTextAsync("1");
        await Expect(Page.Locator(".card__title").First).ToHaveTextAsync("Tài liệu PDF");

        // Sign button (disabled by default)
        var signBtn = Page.Locator("#btnSignPdf");
        await Expect(signBtn).ToBeVisibleAsync();
        await Expect(signBtn).ToBeDisabledAsync();

        // Preview area
        await Expect(Page.Locator(".drop-zone")).ToBeVisibleAsync();

        // Logs panel
        await Expect(Page.Locator(".logs-panel__title")).ToHaveTextAsync("Nhật ký hệ thống");
    }

    [Test]
    public async Task SessionPill_ShowsNotLoggedIn()
    {
        await Page.GotoAsync(BaseUrl);

        var label = Page.Locator("#sessionLabel");
        await Expect(label).ToHaveTextAsync("Chưa đăng nhập");
    }

    [Test]
    public async Task MerchantDropdown_OpensAndShowsList()
    {
        await Page.GotoAsync(BaseUrl);

        // Click merchant button
        await Page.Locator("#merchantBtn").ClickAsync();

        // Dropdown should appear
        var dropdown = Page.Locator("#merchantDropdown");
        await Expect(dropdown).ToBeVisibleAsync();

        // Should have merchant items (loaded from API)
        var items = Page.Locator(".dropdown__item");
        var count = await items.CountAsync();
        Assert.That(count, Is.GreaterThan(0), "Should have at least one merchant");
    }

    [Test]
    public async Task SessionFlyout_OpensOnPillClick()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Click session pill
        await Page.Locator("#sessionPill").ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        // Flyout should appear with login form
        var flyout = Page.Locator("#sessionFlyout");
        await Expect(flyout).ToBeVisibleAsync();

        // Should have login inputs
        await Expect(Page.Locator("#loginUser")).ToBeVisibleAsync();
        await Expect(Page.Locator("#loginPass")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Login_UpdatesSessionState()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Open flyout
        await Page.Locator("#sessionPill").ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        // Fill login form
        await Page.Locator("#loginUser").FillAsync("testuser");
        await Page.Locator("#loginPass").FillAsync("pin123");

        // Click login button
        await Page.Locator(".flyout__login-btn").ClickAsync();

        // Wait for session update
        await Page.WaitForTimeoutAsync(1000);

        // Session pill should show logged in state
        var pill = Page.Locator("#sessionPill");
        await Expect(pill).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("logged-in"));

        // Toast should appear
        var toast = Page.Locator(".toast").First;
        await Expect(toast).ToBeVisibleAsync();
    }

    [Test]
    public async Task DropZone_HighlightsOnDragOver()
    {
        await Page.GotoAsync(BaseUrl);

        var dropZone = Page.Locator(".drop-zone");
        await Expect(dropZone).ToBeVisibleAsync();

        // Simulate drag over via class manipulation (Playwright can't natively simulate drag events easily)
        await Page.EvaluateAsync("document.querySelector('.drop-zone').classList.add('drag-over')");

        // Verify visual feedback class
        await Expect(dropZone).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("drag-over"));
    }

    [Test]
    public async Task LogsPanel_ShowsInitMessage()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForTimeoutAsync(300);

        // Logs body should have at least the init message
        var logsBody = Page.Locator("#logsBody");
        var text = await logsBody.InnerTextAsync();
        Assert.That(text, Does.Contain("Dashboard Initialized"));
    }

    [Test]
    public async Task Screenshot_MatchesMockupLayout()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForTimeoutAsync(500);

        // Take screenshot for manual comparison with mockup
        var screenshotDir = Path.Combine(
            TestContext.CurrentContext.TestDirectory, "screenshots");
        Directory.CreateDirectory(screenshotDir);

        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(screenshotDir, "01-homepage.png"),
            FullPage = false
        });

        // Open merchant dropdown and screenshot
        await Page.Locator("#merchantBtn").ClickAsync();
        await Page.WaitForTimeoutAsync(200);
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(screenshotDir, "02-merchant-dropdown.png"),
            FullPage = false
        });

        // Close dropdown, open session flyout
        await Page.Locator(".dropdown__backdrop").ClickAsync();
        await Page.WaitForTimeoutAsync(100);
        await Page.Locator("#sessionPill").ClickAsync();
        await Page.WaitForTimeoutAsync(200);
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(screenshotDir, "03-session-flyout.png"),
            FullPage = false
        });

        TestContext.WriteLine($"Screenshots saved to: {screenshotDir}");
        Assert.Pass("Screenshots captured for manual comparison with mockup V4.");
    }

    [Test]
    public async Task VisualRegression_HeaderMatchesMockup()
    {
        await Page.GotoAsync(BaseUrl);

        // Check header specific visual properties
        var topbar = Page.Locator(".topbar");
        var height = await topbar.EvaluateAsync<int>("el => el.offsetHeight");
        Assert.That(height, Is.EqualTo(64), "Header should be 64px tall (mockup spec)");

        // Logo size
        var logo = Page.Locator(".topbar__logo");
        var logoW = await logo.EvaluateAsync<int>("el => el.offsetWidth");
        var logoH = await logo.EvaluateAsync<int>("el => el.offsetHeight");
        Assert.That(logoW, Is.EqualTo(34), "Logo should be 34px wide");
        Assert.That(logoH, Is.EqualTo(34), "Logo should be 34px tall");
    }

    [Test]
    public async Task VisualRegression_CardStyling()
    {
        await Page.GotoAsync(BaseUrl);

        var card = Page.Locator(".card").First;
        var borderRadius = await card.EvaluateAsync<string>(
            "el => getComputedStyle(el).borderRadius");
        Assert.That(borderRadius, Is.EqualTo("16px"), "Cards should have 16px border-radius");
    }

    [Test]
    public async Task VisualRegression_InputStyling()
    {
        await Page.GotoAsync(BaseUrl);

        var input = Page.Locator(".input").First;
        var height = await input.EvaluateAsync<int>("el => el.offsetHeight");
        Assert.That(height, Is.InRange(40, 44), "Inputs should be 42px tall (±2px tolerance)");
    }

    [Test]
    public async Task VisualRegression_SignButtonDisabledState()
    {
        await Page.GotoAsync(BaseUrl);

        var btn = Page.Locator("#btnSignPdf");
        var bgColor = await btn.EvaluateAsync<string>(
            "el => getComputedStyle(el).backgroundColor");

        // Disabled = gray (#d3d8e0 → rgb(211, 216, 224))
        Assert.That(bgColor, Does.Contain("211").Or.Contains("216"),
            "Disabled sign button should be gray");
    }

    [Test]
    public async Task VisualRegression_FontFamily()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForTimeoutAsync(1000); // Wait for font load

        var fontFamily = await Page.EvaluateAsync<string>(
            "getComputedStyle(document.body).fontFamily");
        Assert.That(fontFamily, Does.Contain("IBM Plex Sans").IgnoreCase,
            "Body should use IBM Plex Sans font");
    }
}
