using System;
using System.Configuration;
using System.Drawing;
using System.Windows.Forms;

namespace VMSignAgent;

/// <summary>
/// Settings form for VMSignAgent. Allows editing app.config values via UI.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly bool _requireEndUser;
    private TextBox txtEndUserPhoneNumber = null!;
    private TextBox txtTokenPin = null!;
    private ComboBox cboCertificates = null!;
    private CheckBox chkShowSignSuccessToast = null!;
    private TextBox txtPort = null!;
    private TextBox txtDiscoveryPort = null!;
    private TextBox txtMqttHost = null!;
    private TextBox txtMqttPort = null!;
    private TextBox txtMqttUsername = null!;
    private TextBox txtMqttPassword = null!;
    private TextBox txtMqttAgentId = null!;
    private CheckBox chkMqttUseTls = null!;
    private Panel tlsPanel = null!;
    private Button btnSave = null!;
    private Button btnCancel = null!;
    private int tlsPanelVisibleHeight;
    private int buttonBaseY;
    private TextBox txtMqttCaCertPath = null!;
    private TextBox txtMqttClientPfxPath = null!;
    private TextBox txtMqttClientPfxPassword = null!;
    private CheckBox chkMqttAllowUntrusted = null!;

    public SettingsForm(bool requireEndUser = false)
    {
        _requireEndUser = requireEndUser;
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        Text = _requireEndUser ? "VMSignAgent - First Setup" : "VMSignAgent - Settings";
        Size = new Size(560, 720);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.White;

        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16) };
        Controls.Add(panel);

        int y = 10;

        AddSectionHeader(panel, ref y, "End-user Authentication");
        txtEndUserPhoneNumber = AddField(panel, ref y, "Phone Number:", "");
        txtTokenPin = AddField(panel, ref y, "USB Token PIN:", "", true);

        var lblCertificate = new Label
        {
            Text = "Certificate:",
            Location = new Point(10, y + 4),
            Size = new Size(110, 18),
            ForeColor = Color.FromArgb(100, 116, 139),
            Font = new Font("Segoe UI", 8.5f),
        };
        panel.Controls.Add(lblCertificate);

        cboCertificates = new ComboBox
        {
            Location = new Point(120, y),
            Size = new Size(322, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        panel.Controls.Add(cboCertificates);

        var btnReloadCerts = new Button
        {
            Text = "↻",
            Size = new Size(30, 24),
            Location = new Point(450, y - 1),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        btnReloadCerts.Click += BtnLoadCerts_Click;
        panel.Controls.Add(btnReloadCerts);
        new ToolTip().SetToolTip(btnReloadCerts, "Reload certificates");
        y += 30;

        chkShowSignSuccessToast = new CheckBox
        {
            Text = "Show sign success notification",
            Location = new Point(120, y),
            AutoSize = true,
        };
        panel.Controls.Add(chkShowSignSuccessToast);
        y += 28;

        if (_requireEndUser)
        {
            var hint = new Label
            {
                Text = "Phone number and PIN are required before the agent can publish usable certificates.",
                Location = new Point(10, y),
                Size = new Size(500, 34),
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.5f),
            };
            panel.Controls.Add(hint);
            y += 40;
        }

        y += 10;

        AddSectionHeader(panel, ref y, "HTTP Service");
        txtPort = AddField(panel, ref y, "Port:", "9999");
        txtDiscoveryPort = AddField(panel, ref y, "Discovery Port:", "9998");

        y += 10;

        AddSectionHeader(panel, ref y, "MQTT (Cross-Subnet Signing)");
        txtMqttHost = AddField(panel, ref y, "Broker Host:", "");
        txtMqttPort = AddField(panel, ref y, "Broker Port:", "1883");
        txtMqttUsername = AddField(panel, ref y, "Username:", "");
        txtMqttPassword = AddField(panel, ref y, "Password:", "", true);
        txtMqttAgentId = AddField(panel, ref y, "Agent ID:", "");

        y += 5;
        chkMqttUseTls = new CheckBox { Text = "Use TLS", Location = new Point(120, y), AutoSize = true };
        chkMqttUseTls.CheckedChanged += (_, __) => UpdateTlsFieldsVisibility();
        panel.Controls.Add(chkMqttUseTls);
        y += 25;

        tlsPanel = new Panel
        {
            Location = new Point(0, y),
            Size = new Size(500, 118),
        };
        panel.Controls.Add(tlsPanel);

        int tlsY = 0;
        txtMqttCaCertPath = AddField(tlsPanel, ref tlsY, "CA Cert Path:", "");
        txtMqttClientPfxPath = AddField(tlsPanel, ref tlsY, "Client PFX Path:", "");
        txtMqttClientPfxPassword = AddField(tlsPanel, ref tlsY, "Client PFX Password:", "", true);

        chkMqttAllowUntrusted = new CheckBox
        {
            Text = "Allow untrusted cert (DEV only)",
            Location = new Point(120, y),
            AutoSize = true,
            ForeColor = Color.OrangeRed,
        };
        tlsPanel.Controls.Add(chkMqttAllowUntrusted);
        tlsY += 40;
        tlsPanel.Height = tlsY;
        tlsPanelVisibleHeight = tlsPanel.Height;
        y += tlsPanel.Height;

        buttonBaseY = y;
        btnSave = new Button
        {
            Text = "Save && Restart",
            Size = new Size(150, 35),
            Location = new Point(120, y),
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        btnSave.Click += BtnSave_Click;
        panel.Controls.Add(btnSave);

        btnCancel = new Button
        {
            Text = "Cancel",
            Size = new Size(100, 35),
            Location = new Point(285, y),
            FlatStyle = FlatStyle.Flat,
        };
        btnCancel.Click += (_, __) => Close();
        panel.Controls.Add(btnCancel);
    }

    private void BtnLoadCerts_Click(object? sender, EventArgs e)
    {
        LoadCertificatesIntoComboBox(Cfg("Token:SelectedCertificateSerial", ""), showMessage: true);
    }

    private void LoadCertificatesIntoComboBox(string selectedSerial, bool showMessage)
    {
        try
        {
            var certs = TokenSigner.ListCerts();
            cboCertificates.Items.Clear();

            if (certs.Count == 0)
            {
                if (showMessage)
                {
                    MessageBox.Show(
                        "No certificates found in Windows Personal store.\n\nMake sure USB Token is plugged in and driver is installed.",
                        "Certificates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                return;
            }

            foreach (var cert in certs)
            {
                var item = new CertificateComboItem(cert.Serial, $"{cert.SubjectDN} ({cert.Serial})");
                cboCertificates.Items.Add(item);
                if (string.Equals(cert.Serial, selectedSerial, StringComparison.OrdinalIgnoreCase))
                {
                    cboCertificates.SelectedItem = item;
                }
            }

            if (cboCertificates.SelectedIndex < 0 && cboCertificates.Items.Count > 0)
            {
                cboCertificates.SelectedIndex = 0;
            }

            if (showMessage)
            {
                MessageBox.Show($"Loaded {certs.Count} certificate(s).", "Certificates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load certificates:\n{ex.Message}", "Certificates", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadSettings()
    {
        txtEndUserPhoneNumber.Text = Cfg("EndUser:PhoneNumber", "");
        txtTokenPin.Text = Cfg("Token:Pin", "");
        LoadCertificatesIntoComboBox(Cfg("Token:SelectedCertificateSerial", ""), showMessage: false);
        chkShowSignSuccessToast.Checked = Cfg("Ui:ShowSignSuccessToast", "true").Equals("true", StringComparison.OrdinalIgnoreCase);
        txtPort.Text = Cfg("Port", "9999");
        txtDiscoveryPort.Text = Cfg("DiscoveryPort", "9998");
        txtMqttHost.Text = Cfg("Mqtt:BrokerHost", "");
        txtMqttPort.Text = Cfg("Mqtt:BrokerPort", "1883");
        txtMqttUsername.Text = Cfg("Mqtt:Username", "");
        txtMqttPassword.Text = Cfg("Mqtt:Password", "");
        txtMqttAgentId.Text = Cfg("Mqtt:AgentId", "");
        chkMqttUseTls.Checked = Cfg("Mqtt:UseTls", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        txtMqttCaCertPath.Text = Cfg("Mqtt:CaCertPath", "");
        txtMqttClientPfxPath.Text = Cfg("Mqtt:ClientPfxPath", "");
        txtMqttClientPfxPassword.Text = Cfg("Mqtt:ClientPfxPassword", "");
        chkMqttAllowUntrusted.Checked = Cfg("Mqtt:AllowUntrusted", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        UpdateTlsFieldsVisibility();
    }

    private void UpdateTlsFieldsVisibility()
    {
        if (tlsPanel == null || btnSave == null || btnCancel == null)
        {
            return;
        }

        tlsPanel.Visible = chkMqttUseTls.Checked;
        var hiddenOffset = chkMqttUseTls.Checked ? 0 : tlsPanelVisibleHeight;
        btnSave.Top = buttonBaseY - hiddenOffset;
        btnCancel.Top = buttonBaseY - hiddenOffset;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        try
        {
            if (!ValidateEndUserSettings())
            {
                return;
            }

            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = config.AppSettings.Settings;

            SetCfg(settings, "EndUser:PhoneNumber", txtEndUserPhoneNumber.Text.Trim());
            SetCfg(settings, "Token:Pin", txtTokenPin.Text);
            SetCfg(settings, "Token:SelectedCertificateSerial", (cboCertificates.SelectedItem as CertificateComboItem)?.Serial ?? string.Empty);
            SetCfg(settings, "Ui:ShowSignSuccessToast", chkShowSignSuccessToast.Checked ? "true" : "false");
            SetCfg(settings, "Port", txtPort.Text);
            SetCfg(settings, "DiscoveryPort", txtDiscoveryPort.Text);
            SetCfg(settings, "Mqtt:BrokerHost", txtMqttHost.Text);
            SetCfg(settings, "Mqtt:BrokerPort", txtMqttPort.Text);
            SetCfg(settings, "Mqtt:Username", txtMqttUsername.Text);
            SetCfg(settings, "Mqtt:Password", txtMqttPassword.Text);
            SetCfg(settings, "Mqtt:AgentId", txtMqttAgentId.Text);
            SetCfg(settings, "Mqtt:UseTls", chkMqttUseTls.Checked ? "true" : "false");
            SetCfg(settings, "Mqtt:CaCertPath", txtMqttCaCertPath.Text);
            SetCfg(settings, "Mqtt:ClientPfxPath", txtMqttClientPfxPath.Text);
            SetCfg(settings, "Mqtt:ClientPfxPassword", txtMqttClientPfxPassword.Text);
            SetCfg(settings, "Mqtt:AllowUntrusted", chkMqttAllowUntrusted.Checked ? "true" : "false");

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");

            var result = MessageBox.Show(
                "Settings saved successfully.\n\nRestart agent to apply changes?",
                "Settings Saved", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
            {
                Application.Restart();
                Environment.Exit(0);
            }
            else
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save settings:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool ValidateEndUserSettings()
    {
        var mqttEnabled = !string.IsNullOrWhiteSpace(txtMqttHost.Text);
        if (!_requireEndUser && !mqttEnabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(txtEndUserPhoneNumber.Text))
        {
            MessageBox.Show("Phone number is required.", "Missing Phone Number", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtEndUserPhoneNumber.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(txtTokenPin.Text))
        {
            MessageBox.Show("USB Token PIN is required.", "Missing PIN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtTokenPin.Focus();
            return false;
        }

        return true;
    }

    private static string Cfg(string key, string defaultValue)
    {
        var v = ConfigurationManager.AppSettings[key];
        return string.IsNullOrEmpty(v) ? defaultValue : v;
    }

    private static void SetCfg(KeyValueConfigurationCollection settings, string key, string value)
    {
        if (settings[key] != null)
        {
            settings[key].Value = value;
        }
        else
        {
            settings.Add(key, value);
        }
    }

    private void AddSectionHeader(Panel panel, ref int y, string text)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(10, y),
            Size = new Size(500, 22),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 41, 59),
        };
        panel.Controls.Add(lbl);
        y += 24;

        var sep = new Label { Location = new Point(10, y), Size = new Size(500, 1), BackColor = Color.FromArgb(226, 232, 240) };
        panel.Controls.Add(sep);
        y += 6;
    }

    private TextBox AddField(Panel panel, ref int y, string label, string defaultValue, bool isPassword = false)
    {
        var lbl = new Label
        {
            Text = label,
            Location = new Point(10, y + 3),
            Size = new Size(110, 18),
            ForeColor = Color.FromArgb(100, 116, 139),
            Font = new Font("Segoe UI", 8.5f),
        };
        panel.Controls.Add(lbl);

        var txt = new TextBox
        {
            Location = new Point(120, y),
            Size = new Size(360, 23),
            Text = defaultValue,
            BorderStyle = BorderStyle.FixedSingle,
        };
        if (isPassword) txt.UseSystemPasswordChar = true;
        panel.Controls.Add(txt);

        y += 28;
        return txt;
    }

    private sealed class CertificateComboItem
    {
        public CertificateComboItem(string serial, string displayText)
        {
            Serial = serial;
            DisplayText = displayText;
        }

        public string Serial { get; }
        private string DisplayText { get; }

        public override string ToString() => DisplayText;
    }
}
