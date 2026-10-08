using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace VMSignAgent;

/// <summary>
/// Window for Test Sign (tray menu). Runs <see cref="SignTestRunner"/> as soon as it opens
/// and shows each step as it finishes.
/// </summary>
public sealed class SignTestForm : Form
{
    private readonly Func<bool?> _mqttConnected;
    private TextBox txtLog = null!;
    private Button btnRun = null!;
    private Button btnOpenPdf = null!;
    private string? _signedPdfPath;

    /// <param name="mqttConnected">Reads the running agent's broker link at the moment a run starts.</param>
    public SignTestForm(Func<bool?> mqttConnected)
    {
        _mqttConnected = mqttConnected;
        InitializeComponent();
        Shown += (_, __) => RunTest();
    }

    private void InitializeComponent()
    {
        Text = "VMSignAgent - Test Sign PDF";
        Size = new Size(680, 500);
        MinimumSize = new Size(520, 360);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.White;

        var intro = new Label
        {
            Text = "Signs a sample PDF through the signing server, the same way the hospital software does, " +
                   "using the phone number, PIN, certificate and server saved in Settings.",
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(12, 10, 12, 0),
            ForeColor = Color.FromArgb(71, 85, 105),
        };

        txtLog = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9f),
            BackColor = Color.FromArgb(248, 250, 252),
            BorderStyle = BorderStyle.FixedSingle,
        };
        var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 4) };
        logHost.Controls.Add(txtLog);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 8, 8, 8),
        };

        var btnClose = new Button { Text = "Close", Size = new Size(100, 32), FlatStyle = FlatStyle.Flat };
        btnClose.Click += (_, __) => Close();

        btnOpenPdf = new Button
        {
            Text = "Open signed PDF",
            Size = new Size(140, 32),
            FlatStyle = FlatStyle.Flat,
            Enabled = false,
        };
        btnOpenPdf.Click += (_, __) => OpenSignedPdf();

        btnRun = new Button
        {
            Text = "Run again",
            Size = new Size(110, 32),
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        btnRun.Click += (_, __) => RunTest();

        buttons.Controls.Add(btnClose);
        buttons.Controls.Add(btnOpenPdf);
        buttons.Controls.Add(btnRun);

        // Fill must be added first so it takes what Top and Bottom leave.
        Controls.Add(logHost);
        Controls.Add(buttons);
        Controls.Add(intro);
        CancelButton = btnClose;
    }

    private async void RunTest()
    {
        btnRun.Enabled = false;
        btnOpenPdf.Enabled = false;
        _signedPdfPath = null;
        txtLog.Clear();
        UseWaitCursor = true;
        try
        {
            AgentConfig.Reload();
            var runner = new SignTestRunner(AppendLog);
            _signedPdfPath = await runner.RunAsync(_mqttConnected(), CancellationToken.None);
        }
        finally
        {
            UseWaitCursor = false;
            if (!IsDisposed)
            {
                btnRun.Enabled = true;
                btnOpenPdf.Enabled = _signedPdfPath != null;
            }
        }

        if (_signedPdfPath != null && !IsDisposed)
            OpenSignedPdf();
    }

    private void AppendLog(string line)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), line);
            return;
        }
        txtLog.AppendText(line + Environment.NewLine);
    }

    private void OpenSignedPdf()
    {
        if (_signedPdfPath == null) return;
        try
        {
            Process.Start(_signedPdfPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot open the PDF:\n{ex.Message}\n\n{_signedPdfPath}", "Test Sign",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
