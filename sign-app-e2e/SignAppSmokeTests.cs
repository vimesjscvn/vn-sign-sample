using FlaUI.Core.AutomationElements;
using NUnit.Framework;

namespace VMSign.AppE2E;

[TestFixture]
[NonParallelizable]
[Platform("Win")]
public sealed class SignAppSmokeTests
{
    [Test]
    public void Initial_pdf_workspace_exposes_the_shared_shell()
    {
        using var app = StartAppWithTreeOnFailure();

        Assert.Multiple(() =>
        {
            Assert.That(app.MainWindow.Title, Is.EqualTo("Vimes SignSDK Showcase Studio"));
            Assert.That(app.RequireByName("Vimes SignSDK Studio"), Is.Not.Null);
            Assert.That(app.RequireByAutomationId("cboMerchant").ControlType.ToString(), Is.EqualTo("ComboBox"));
            Assert.That(app.RequireByName("Tài liệu PDF"), Is.Not.Null);
            Assert.That(app.RequireByAutomationId("btnBrowse").Name, Is.EqualTo("Chọn File"));
            Assert.That(app.RequireByName("Chưa Nạp Tài Liệu PDF"), Is.Not.Null);
            Assert.That(app.RequireByAutomationId("lblPreviewMock").Name, Is.EqualTo("Trang 1 / 1"));
            Assert.That(app.RequireByName("Nhật ký hệ thống (Logs)"), Is.Not.Null);
        });
    }

    [Test]
    public void Initial_session_is_logged_out_log_is_initialized_and_sign_is_disabled()
    {
        using var app = StartAppWithTreeOnFailure();

        var sessionStatus = app.RequireByAutomationId("lblSessionStatus");
        var signButton = app.RequireByAutomationId("btnSign").AsButton();
        var logs = app.RequireByAutomationId("txtLogs").AsTextBox();

        app.WaitUntil(
            () => logs.Text.Contains("Dashboard Initialized", StringComparison.Ordinal),
            "the dashboard initialization log entry");

        Assert.Multiple(() =>
        {
            Assert.That(sessionStatus.Name, Is.EqualTo("Chưa đăng nhập"));
            Assert.That(signButton.Name, Is.EqualTo("KÝ SỐ TÀI LIỆU PDF"));
            Assert.That(signButton.IsEnabled, Is.False,
                "Signing must remain unavailable before a PDF/signature position is ready.");
            Assert.That(logs.IsReadOnly, Is.True);
            Assert.That(logs.Text, Does.Contain("Vimes SignSDK Showcase Studio on Avalonia"));
        });
    }

    [Test]
    public void Session_flyout_exposes_credentials_certificate_and_login_actions()
    {
        using var app = StartAppWithTreeOnFailure();

        app.RequireByAutomationId("btnSession").AsButton().Click();

        Assert.Multiple(() =>
        {
            Assert.That(app.RequireByName("Xác thực & Chứng thư số"), Is.Not.Null);
            Assert.That(app.RequireByAutomationId("txtUserName").IsEnabled, Is.True);
            Assert.That(app.RequireByAutomationId("txtPassword").IsEnabled, Is.True);
            Assert.That(app.RequireByAutomationId("btnLogin").Name, Is.EqualTo("Đăng Nhập"));
            Assert.That(app.RequireByAutomationId("btnSyncCertificates").Name, Is.EqualTo("Tải Chứng Thư"));
            Assert.That(app.RequireByAutomationId("cboCerts").IsEnabled, Is.True);
        });
    }

    [Test]
    public void System_menu_opens_settings_workspace()
    {
        using var app = StartAppWithTreeOnFailure();

        app.RequireByName("Hệ thống ▼").AsButton().Click();
        app.RequireByName("⚙️ Cài đặt hệ thống").Click();

        var saveSettings = app.RequireByAutomationId("btnSaveSettings");
        Assert.Multiple(() =>
        {
            Assert.That(app.RequireByName("Viettel MySign"), Is.Not.Null);
            Assert.That(app.RequireByName("VNPT SmartCA"), Is.Not.Null);
            Assert.That(app.RequireByName("USB Token Agent Settings"), Is.Not.Null);
            Assert.That(app.RequireByAutomationId("txtMySignUrl").IsEnabled, Is.True);
            Assert.That(saveSettings.Name, Is.EqualTo("LƯU CÀI ĐẶT"));
        });
    }

    [Test]
    public void Loading_a_pdf_then_drawing_a_placement_enables_the_sign_action()
    {
        var pdfPath = TemporaryPdf.CreateTwoPageDocument();
        try
        {
            using var app = StartAppWithTreeOnFailure();
            var signButton = app.RequireByAutomationId("btnSign").AsButton();

            Assert.That(signButton.IsEnabled, Is.False);

            app.RequireByAutomationId("btnBrowse").AsButton().Click();
            WindowsUiDriver.SelectPdfFromOpenDialog(app.ProcessId, pdfPath);

            var previewStatus = app.RequireByAutomationId("lblPreviewMock");
            app.WaitUntil(
                () => previewStatus.Name == "Trang 1 / 2",
                "the two-page PDF preview to load");

            Assert.That(signButton.IsEnabled, Is.False,
                "Loading a document alone must not invent a signature placement.");

            var pdfPreview = app.RequireByAutomationId("imgPdfPage");
            WindowsUiDriver.DragWithin(pdfPreview.BoundingRectangle, app.ProcessId);

            var confirmPlacement = app.RequireByAutomationId("btnConfirmPlace").AsButton();
            Assert.That(confirmPlacement.IsEnabled, Is.True);
            confirmPlacement.Click();

            app.WaitUntil(
                () => signButton.IsEnabled,
                "the PDF sign action to become enabled after placement");

            Assert.Multiple(() =>
            {
                Assert.That(previewStatus.Name, Is.EqualTo("Trang 1 / 2"));
                Assert.That(signButton.IsEnabled, Is.True);
                Assert.That(app.RequireByAutomationId("txtLogs").AsTextBox().Text,
                    Does.Contain("Canvas Box Selection Captured"));
            });
        }
        finally
        {
            try
            {
                File.Delete(pdfPath);
            }
            catch (IOException error)
            {
                TestContext.Error.WriteLine($"Could not remove temporary PDF '{pdfPath}': {error.Message}");
            }
        }
    }

    private static VMSignUiSession StartAppWithTreeOnFailure()
    {
        try
        {
            return VMSignUiSession.Start();
        }
        catch (Exception error)
        {
            TestContext.Error.WriteLine(error);
            throw;
        }
    }
}
