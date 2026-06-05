namespace WinFormsSample;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        System.Windows.Forms.DataGridViewCellStyle dgvHeaderStyle = new System.Windows.Forms.DataGridViewCellStyle();
        System.Windows.Forms.DataGridViewCellStyle dgvRowStyle = new System.Windows.Forms.DataGridViewCellStyle();
        
        this.txtLogs = new System.Windows.Forms.RichTextBox();
        this.panelSidebar = new System.Windows.Forms.Panel();
        this.lblSidebarTitle = new System.Windows.Forms.Label();
        this.btnNavDirect = new System.Windows.Forms.Button();

        this.btnNavXml = new System.Windows.Forms.Button();
        this.btnNavSettings = new System.Windows.Forms.Button();
        
        this.panelHeader = new System.Windows.Forms.Panel();
        this.lblMerchant = new System.Windows.Forms.Label();
        this.cboMerchant = new System.Windows.Forms.ComboBox();
        this.lblSessionStatus = new System.Windows.Forms.Label();
        this.panelStatusDot = new System.Windows.Forms.Panel();
        
        this.tabControl = new System.Windows.Forms.TabControl();
        
        // Tab 1: Identity & Credentials
        this.splitContainerIdentity = new System.Windows.Forms.SplitContainer();
        this.gbCredentials = new System.Windows.Forms.GroupBox();
        this.lblUser = new System.Windows.Forms.Label();
        this.txtUserName = new System.Windows.Forms.TextBox();
        this.lblPass = new System.Windows.Forms.Label();
        this.txtPassword = new System.Windows.Forms.TextBox();

        this.btnLogin = new System.Windows.Forms.Button();
        this.btnSyncCertificates = new System.Windows.Forms.Button();
        this.lblActiveCertLabel = new System.Windows.Forms.Label();
        this.cboCerts = new System.Windows.Forms.ComboBox();

        // Tab 2: Quick PDF Sign
        this.tabDirect = new System.Windows.Forms.TabPage();
        this.splitContainerDirect = new System.Windows.Forms.SplitContainer();
        this.splitContainerDirectLeft = new System.Windows.Forms.SplitContainer();
        this.gbDirectConfig = new System.Windows.Forms.GroupBox();
        this.lblFile = new System.Windows.Forms.Label();
        this.lstFilePath = new System.Windows.Forms.ListBox();
        this.btnBrowse = new System.Windows.Forms.Button();
        this.lblSignerName = new System.Windows.Forms.Label();
        this.txtSignerName = new System.Windows.Forms.TextBox();
        this.lblSignerTitle = new System.Windows.Forms.Label();
        this.txtSignerTitle = new System.Windows.Forms.TextBox();
        this.lblSignatureType = new System.Windows.Forms.Label();
        this.cboSignatureType = new System.Windows.Forms.ComboBox();
        this.lblDisplayNameMode = new System.Windows.Forms.Label();
        this.cboDisplayNameMode = new System.Windows.Forms.ComboBox();
        this.lblNote = new System.Windows.Forms.Label();
        this.txtNote = new System.Windows.Forms.TextBox();
        this.rbPositionSignature = new System.Windows.Forms.RadioButton();
        this.rbPositionNote = new System.Windows.Forms.RadioButton();
        this.lblNoteX = new System.Windows.Forms.Label();
        this.txtNoteX = new System.Windows.Forms.TextBox();
        this.lblNoteY = new System.Windows.Forms.Label();
        this.txtNoteY = new System.Windows.Forms.TextBox();
        this.chkShowSignatureTime = new System.Windows.Forms.CheckBox();
        this.btnSign = new System.Windows.Forms.Button();
        this.gbDirectPreview = new System.Windows.Forms.GroupBox();
        this.lblSigImage = new System.Windows.Forms.Label();
        this.pbSigImage = new System.Windows.Forms.PictureBox();
        this.btnBrowseSigImage = new System.Windows.Forms.Button();
        this.btnClearSigImage = new System.Windows.Forms.Button();
        this.lblPreviewMock = new System.Windows.Forms.Label();
        this.panelSigPlacementMock = new System.Windows.Forms.Panel();
        this.btnPrevPage = new System.Windows.Forms.Button();
        this.btnNextPage = new System.Windows.Forms.Button();

        // Tab 3: Advanced PDF (Feature Explorer)
        this.tabAdvanced = new System.Windows.Forms.TabPage();
        this.splitContainerAdvanced = new System.Windows.Forms.SplitContainer();
        this.gbAdvancedSign = new System.Windows.Forms.GroupBox();
        this.lblAdvancedFile = new System.Windows.Forms.Label();
        this.txtAdvancedFilePath = new System.Windows.Forms.TextBox();
        this.btnBrowseAdvanced = new System.Windows.Forms.Button();
        this.btnSignAdvanced = new System.Windows.Forms.Button();
        this.pgAdvancedRequest = new System.Windows.Forms.PropertyGrid();

        // Tab 4: XML Signing Showcase
        this.tabXml = new System.Windows.Forms.TabPage();
        this.splitContainerXml = new System.Windows.Forms.SplitContainer();
        this.gbXmlAction = new System.Windows.Forms.GroupBox();
        this.btnSignXml = new System.Windows.Forms.Button();
        this.pgXmlRequest = new System.Windows.Forms.PropertyGrid();

        // Tab 5: Batch PDF Sign
        this.tabBatch = new System.Windows.Forms.TabPage();
        this.splitContainerBatch = new System.Windows.Forms.SplitContainer();
        this.gbBatchConfig = new System.Windows.Forms.GroupBox();
        this.lblBatchFolder = new System.Windows.Forms.Label();
        this.txtBatchFolder = new System.Windows.Forms.TextBox();
        this.btnBrowseBatch = new System.Windows.Forms.Button();
        this.lblBatchUserSecret = new System.Windows.Forms.Label();
        this.txtBatchUserSecret = new System.Windows.Forms.TextBox();
        this.lblBatchCertPath = new System.Windows.Forms.Label();
        this.txtBatchCertPath = new System.Windows.Forms.TextBox();
        this.btnBrowseBatchCert = new System.Windows.Forms.Button();
        this.lblBatchCertPass = new System.Windows.Forms.Label();
        this.txtBatchCertPass = new System.Windows.Forms.TextBox();
        this.lblBatchOutput = new System.Windows.Forms.Label();
        this.txtBatchOutput = new System.Windows.Forms.TextBox();
        this.btnBrowseBatchOutput = new System.Windows.Forms.Button();
        this.btnBatchSign = new System.Windows.Forms.Button();
        this.progressBar = new System.Windows.Forms.ProgressBar();
        this.lblBatchStatus = new System.Windows.Forms.Label();
        this.gbBatchFilesList = new System.Windows.Forms.GroupBox();
        this.dgvBatchFiles = new System.Windows.Forms.DataGridView();
        this.btnSelectAllBatch = new System.Windows.Forms.Button();
        this.btnClearAllBatch = new System.Windows.Forms.Button();

        // Tab 6: SDK Settings Explorer
        this.tabSettings = new System.Windows.Forms.TabPage();
        this.pgSettings = new System.Windows.Forms.PropertyGrid();
        this.panelSettingsBottom = new System.Windows.Forms.Panel();
        this.btnSaveSettings = new System.Windows.Forms.Button();

        this.panelLogsTitle = new System.Windows.Forms.Panel();
        this.lblLogsTitle = new System.Windows.Forms.Label();
        this.btnClearLogs = new System.Windows.Forms.Button();
        this.btnCopyLogs = new System.Windows.Forms.Button();

        this.panelSidebar.SuspendLayout();
        this.panelHeader.SuspendLayout();
        this.tabControl.SuspendLayout();
        this.gbCredentials.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.splitContainerDirectLeft)).BeginInit();
        this.splitContainerDirectLeft.Panel1.SuspendLayout();
        this.splitContainerDirectLeft.Panel2.SuspendLayout();
        this.splitContainerDirectLeft.SuspendLayout();
        this.tabDirect.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.splitContainerDirect)).BeginInit();
        this.splitContainerDirect.Panel1.SuspendLayout();
        this.splitContainerDirect.Panel2.SuspendLayout();
        this.splitContainerDirect.SuspendLayout();
        this.gbDirectConfig.SuspendLayout();
        this.gbDirectPreview.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.pbSigImage)).BeginInit();
        this.tabAdvanced.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.splitContainerAdvanced)).BeginInit();
        this.splitContainerAdvanced.Panel1.SuspendLayout();
        this.splitContainerAdvanced.Panel2.SuspendLayout();
        this.splitContainerAdvanced.SuspendLayout();
        this.gbAdvancedSign.SuspendLayout();
        this.tabXml.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.splitContainerXml)).BeginInit();
        this.splitContainerXml.Panel1.SuspendLayout();
        this.splitContainerXml.Panel2.SuspendLayout();
        this.splitContainerXml.SuspendLayout();
        this.gbXmlAction.SuspendLayout();
        this.tabBatch.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.splitContainerBatch)).BeginInit();
        this.splitContainerBatch.Panel1.SuspendLayout();
        this.splitContainerBatch.Panel2.SuspendLayout();
        this.splitContainerBatch.SuspendLayout();
        this.gbBatchConfig.SuspendLayout();
        this.gbBatchFilesList.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.dgvBatchFiles)).BeginInit();
        this.tabSettings.SuspendLayout();
        this.panelSettingsBottom.SuspendLayout();
        this.panelLogsTitle.SuspendLayout();
        this.SuspendLayout();

        // 
        // panelSidebar
        // 
        this.panelSidebar.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
        this.panelSidebar.Controls.Add(this.lblSidebarTitle);
        this.panelSidebar.Controls.Add(this.btnNavDirect);

        this.panelSidebar.Controls.Add(this.btnNavXml);
        this.panelSidebar.Controls.Add(this.btnNavSettings);
        this.panelSidebar.Dock = System.Windows.Forms.DockStyle.Left;
        this.panelSidebar.Location = new System.Drawing.Point(0, 0);
        this.panelSidebar.Name = "panelSidebar";
        this.panelSidebar.Size = new System.Drawing.Size(220, 680);
        this.panelSidebar.TabIndex = 0;
        // 
        // lblSidebarTitle
        // 
        this.lblSidebarTitle.Font = new System.Drawing.Font("Segoe UI Semibold", 14F, System.Drawing.FontStyle.Bold);
        this.lblSidebarTitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(56)))), ((int)(((byte)(189)))), ((int)(((byte)(248)))));
        this.lblSidebarTitle.Location = new System.Drawing.Point(15, 20);
        this.lblSidebarTitle.Name = "lblSidebarTitle";
        this.lblSidebarTitle.Size = new System.Drawing.Size(190, 45);
        this.lblSidebarTitle.Text = "VIMES SignSDK";
        this.lblSidebarTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        // 
        // btnNavDirect
        // 
        this.btnNavDirect.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(41)))), ((int)(((byte)(59)))));
        this.btnNavDirect.FlatAppearance.BorderSize = 0;
        this.btnNavDirect.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnNavDirect.Font = new System.Drawing.Font("Segoe UI Semibold", 10F);
        this.btnNavDirect.ForeColor = System.Drawing.Color.White;
        this.btnNavDirect.Location = new System.Drawing.Point(10, 80);
        this.btnNavDirect.Name = "btnNavDirect";
        this.btnNavDirect.Size = new System.Drawing.Size(200, 45);
        this.btnNavDirect.TabIndex = 1;
        this.btnNavDirect.Text = "🎨 Ký PDF Trực Quan";
        this.btnNavDirect.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        this.btnNavDirect.UseVisualStyleBackColor = true;
        this.btnNavDirect.Click += new System.EventHandler(this.btnNav_Click);
        // 

        // 
        // btnNavXml
        // 
        this.btnNavXml.FlatAppearance.BorderSize = 0;
        this.btnNavXml.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnNavXml.Font = new System.Drawing.Font("Segoe UI Semibold", 10F);
        this.btnNavXml.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(148)))), ((int)(((byte)(163)))), ((int)(((byte)(184)))));
        this.btnNavXml.Location = new System.Drawing.Point(10, 190);
        this.btnNavXml.Name = "btnNavXml";
        this.btnNavXml.Size = new System.Drawing.Size(200, 45);
        this.btnNavXml.TabIndex = 3;
        this.btnNavXml.Text = "💠 Ký XML";
        this.btnNavXml.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        this.btnNavXml.UseVisualStyleBackColor = true;
        this.btnNavXml.Visible = false;
        this.btnNavXml.Click += new System.EventHandler(this.btnNav_Click);
        // 
        // btnNavSettings
        // 
        this.btnNavSettings.FlatAppearance.BorderSize = 0;
        this.btnNavSettings.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnNavSettings.Font = new System.Drawing.Font("Segoe UI Semibold", 10F);
        this.btnNavSettings.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(148)))), ((int)(((byte)(163)))), ((int)(((byte)(184)))));
        this.btnNavSettings.Location = new System.Drawing.Point(10, 135);
        this.btnNavSettings.Name = "btnNavSettings";
        this.btnNavSettings.Size = new System.Drawing.Size(200, 45);
        this.btnNavSettings.TabIndex = 4;
        this.btnNavSettings.Text = "⚙️ Cài Đặt SDK";
        this.btnNavSettings.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        this.btnNavSettings.UseVisualStyleBackColor = true;
        this.btnNavSettings.Click += new System.EventHandler(this.btnNav_Click);
        // 
        // panelHeader
        // 
        this.panelHeader.BackColor = System.Drawing.Color.White;
        this.panelHeader.Controls.Add(this.lblMerchant);
        this.panelHeader.Controls.Add(this.cboMerchant);
        this.panelHeader.Controls.Add(this.lblSessionStatus);
        this.panelHeader.Controls.Add(this.panelStatusDot);
        this.panelHeader.Dock = System.Windows.Forms.DockStyle.Top;
        this.panelHeader.Location = new System.Drawing.Point(220, 0);
        this.panelHeader.Name = "panelHeader";
        this.panelHeader.Size = new System.Drawing.Size(830, 60);
        this.panelHeader.TabIndex = 1;
        // 
        // lblMerchant
        // 
        this.lblMerchant.AutoSize = true;
        this.lblMerchant.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold);
        this.lblMerchant.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(41)))), ((int)(((byte)(59)))));
        this.lblMerchant.Location = new System.Drawing.Point(20, 20);
        this.lblMerchant.Name = "lblMerchant";
        this.lblMerchant.Size = new System.Drawing.Size(147, 23);
        this.lblMerchant.Text = "Nhà Cung Cấp:";
        // 
        // cboMerchant
        // 
        this.cboMerchant.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.cboMerchant.Font = new System.Drawing.Font("Segoe UI", 9.75F);
        this.cboMerchant.FormattingEnabled = true;
        this.cboMerchant.Location = new System.Drawing.Point(170, 16);
        this.cboMerchant.Name = "cboMerchant";
        this.cboMerchant.Size = new System.Drawing.Size(220, 29);
        this.cboMerchant.TabIndex = 1;
        this.cboMerchant.SelectedIndexChanged += new System.EventHandler(this.cboMerchant_SelectedIndexChanged);
        // 
        // lblSessionStatus
        // 
        this.lblSessionStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
        this.lblSessionStatus.Font = new System.Drawing.Font("Segoe UI Semibold", 9.5F);
        this.lblSessionStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(116)))), ((int)(((byte)(139)))));
        this.lblSessionStatus.Location = new System.Drawing.Point(540, 20);
        this.lblSessionStatus.Name = "lblSessionStatus";
        this.lblSessionStatus.Size = new System.Drawing.Size(240, 23);
        this.lblSessionStatus.Text = "Trạng Thái: Chưa Kết Nối";
        this.lblSessionStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
        // 
        // panelStatusDot
        // 
        this.panelStatusDot.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
        this.panelStatusDot.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(239)))), ((int)(((byte)(68)))), ((int)(((byte)(68)))));
        this.panelStatusDot.Location = new System.Drawing.Point(790, 24);
        this.panelStatusDot.Name = "panelStatusDot";
        this.panelStatusDot.Size = new System.Drawing.Size(12, 12);
        this.panelStatusDot.TabIndex = 3;
        // 
        // 
        // tabControl
        // 
        this.tabControl.Controls.Add(this.tabDirect);
        this.tabControl.Controls.Add(this.tabBatch);
        this.tabControl.Controls.Add(this.tabXml);
        this.tabControl.Controls.Add(this.tabSettings);
        this.tabControl.Dock = System.Windows.Forms.DockStyle.Fill;
        this.tabControl.ItemSize = new System.Drawing.Size(0, 1);
        this.tabControl.Location = new System.Drawing.Point(220, 60);
        this.tabControl.Name = "tabControl";
        this.tabControl.SelectedIndex = 0;
        this.tabControl.Size = new System.Drawing.Size(830, 500);
        this.tabControl.SizeMode = System.Windows.Forms.TabSizeMode.Fixed;
        this.tabControl.TabIndex = 2;
        // 
        // splitContainerIdentity
        // 
        this.splitContainerIdentity.Dock = System.Windows.Forms.DockStyle.Fill;
        this.splitContainerIdentity.Location = new System.Drawing.Point(5, 5);
        this.splitContainerIdentity.Name = "splitContainerIdentity";
        this.splitContainerIdentity.Orientation = System.Windows.Forms.Orientation.Horizontal;
        // 
        // splitContainerIdentity.Panel1
        // 
        // 
        // gbCredentials
        // 
        this.gbCredentials.BackColor = System.Drawing.Color.White;
        this.gbCredentials.Controls.Add(this.lblUser);
        this.gbCredentials.Controls.Add(this.txtUserName);
        this.gbCredentials.Controls.Add(this.lblPass);
        this.gbCredentials.Controls.Add(this.txtPassword);

        this.gbCredentials.Controls.Add(this.btnLogin);
        this.gbCredentials.Controls.Add(this.lblActiveCertLabel);
        this.gbCredentials.Controls.Add(this.cboCerts);
        this.gbCredentials.Controls.Add(this.btnSyncCertificates);
        this.gbCredentials.Controls.Add(this.lblSigImage);
        this.gbCredentials.Controls.Add(this.pbSigImage);
        this.gbCredentials.Controls.Add(this.btnBrowseSigImage);
        this.gbCredentials.Controls.Add(this.btnClearSigImage);
        this.gbCredentials.Dock = System.Windows.Forms.DockStyle.Fill;
        this.gbCredentials.Font = new System.Drawing.Font("Segoe UI Semibold", 9.5F, System.Drawing.FontStyle.Bold);
        this.gbCredentials.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
        this.gbCredentials.Location = new System.Drawing.Point(0, 0);
        this.gbCredentials.Name = "gbCredentials";
        this.gbCredentials.Size = new System.Drawing.Size(500, 250);
        this.gbCredentials.TabIndex = 0;
        this.gbCredentials.TabStop = false;
        this.gbCredentials.Text = "Xác Thực Nhà Cung Cấp && Chứng Thư Số";
        // 
        // lblUser
        // 
        this.lblUser.AutoSize = true;
        this.lblUser.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F, System.Drawing.FontStyle.Bold);
        this.lblUser.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblUser.Location = new System.Drawing.Point(15, 25);
        this.lblUser.Name = "lblUser";
        this.lblUser.Size = new System.Drawing.Size(220, 17);
        this.lblUser.Text = "Tên Đăng Nhập / Số Điện Thoại:";
        // 
        // txtUserName
        // 
        this.txtUserName.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.txtUserName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left))));
        this.txtUserName.Location = new System.Drawing.Point(15, 45);
        this.txtUserName.Name = "txtUserName";
        this.txtUserName.Size = new System.Drawing.Size(220, 27);
        this.txtUserName.TabIndex = 1;
        this.txtUserName.Text = "";
        // 
        // lblPass
        // 
        this.lblPass.AutoSize = true;
        this.lblPass.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F, System.Drawing.FontStyle.Bold);
        this.lblPass.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblPass.Location = new System.Drawing.Point(255, 25);
        this.lblPass.Name = "lblPass";
        this.lblPass.Size = new System.Drawing.Size(109, 17);
        this.lblPass.Text = "Mật Khẩu / Mã PIN:";
        // 
        // txtPassword
        // 
        this.txtPassword.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right))));
        this.txtPassword.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.txtPassword.Location = new System.Drawing.Point(255, 45);
        this.txtPassword.Name = "txtPassword";
        this.txtPassword.PasswordChar = '*';
        this.txtPassword.Size = new System.Drawing.Size(220, 27);
        this.txtPassword.TabIndex = 2;
        this.txtPassword.Text = "";

        // 
        // btnLogin
        // 
        this.btnLogin.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(43)))), ((int)(((byte)(87)))), ((int)(((byte)(154)))));
        this.btnLogin.FlatAppearance.BorderSize = 0;
        this.btnLogin.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnLogin.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold);
        this.btnLogin.ForeColor = System.Drawing.Color.White;
        this.btnLogin.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right))));
        this.btnLogin.Location = new System.Drawing.Point(15, 135);
        this.btnLogin.Name = "btnLogin";
        this.btnLogin.Size = new System.Drawing.Size(460, 34);
        this.btnLogin.TabIndex = 6;
        this.btnLogin.Text = "🔑 Đăng Nhập && Tìm Chứng Thư";
        this.btnLogin.UseVisualStyleBackColor = false;
        this.btnLogin.Click += new System.EventHandler(this.btnLogin_Click);
        // 
        // lblActiveCertLabel
        // 
        this.lblActiveCertLabel.AutoSize = true;
        this.lblActiveCertLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F, System.Drawing.FontStyle.Bold);
        this.lblActiveCertLabel.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblActiveCertLabel.Location = new System.Drawing.Point(15, 178);
        this.lblActiveCertLabel.Name = "lblActiveCertLabel";
        this.lblActiveCertLabel.Size = new System.Drawing.Size(175, 17);
        this.lblActiveCertLabel.Text = "Chứng Thư Số Được Chọn:";
        // 
        // cboCerts
        // 
        this.cboCerts.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.cboCerts.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.cboCerts.FormattingEnabled = true;
        this.cboCerts.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right))));
        this.cboCerts.Location = new System.Drawing.Point(15, 198);
        this.cboCerts.Name = "cboCerts";
        this.cboCerts.Size = new System.Drawing.Size(300, 27);
        this.cboCerts.TabIndex = 7;
        // 
        // btnSyncCertificates
        // 
        this.btnSyncCertificates.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
        this.btnSyncCertificates.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(16)))), ((int)(((byte)(185)))), ((int)(((byte)(129)))));
        this.btnSyncCertificates.FlatAppearance.BorderSize = 0;
        this.btnSyncCertificates.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnSyncCertificates.Font = new System.Drawing.Font("Segoe UI Semibold", 8F, System.Drawing.FontStyle.Bold);
        this.btnSyncCertificates.ForeColor = System.Drawing.Color.White;
        this.btnSyncCertificates.Location = new System.Drawing.Point(325, 197);
        this.btnSyncCertificates.Name = "btnSyncCertificates";
        this.btnSyncCertificates.Size = new System.Drawing.Size(150, 29);
        this.btnSyncCertificates.TabIndex = 8;
        this.btnSyncCertificates.Text = "🔄 Làm Mới Chứng Thư";
        this.btnSyncCertificates.UseVisualStyleBackColor = false;
        this.btnSyncCertificates.Click += new System.EventHandler(this.btnSyncCertificates_Click);
        // 


        // 
        // tabDirect
        // 
        this.tabDirect.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(250)))), ((int)(((byte)(252)))));
        this.tabDirect.Controls.Add(this.splitContainerDirect);
        this.tabDirect.Location = new System.Drawing.Point(4, 5);
        this.tabDirect.Name = "tabDirect";
        this.tabDirect.Padding = new System.Windows.Forms.Padding(15);
        this.tabDirect.Size = new System.Drawing.Size(822, 491);
        this.tabDirect.TabIndex = 0;
        this.tabDirect.Text = "🎨 Ký Trực Quan";
        // 
        // splitContainerDirect
        // 
        this.splitContainerDirect.Dock = System.Windows.Forms.DockStyle.Fill;
        this.splitContainerDirect.Location = new System.Drawing.Point(15, 15);
        this.splitContainerDirect.Name = "splitContainerDirect";
        // 
        // 
        // splitContainerDirect.Panel1
        // 
        this.splitContainerDirect.Panel1.Controls.Add(this.splitContainerDirectLeft);
        // 
        // splitContainerDirect.Panel2
        // 
        this.splitContainerDirect.Panel2.Controls.Add(this.gbDirectPreview);
        this.splitContainerDirect.Size = new System.Drawing.Size(950, 560);
        this.splitContainerDirect.SplitterDistance = 500;
        this.splitContainerDirect.TabIndex = 0;
        // 
        // 
        // splitContainerDirectLeft
        // 
        this.splitContainerDirectLeft.Dock = System.Windows.Forms.DockStyle.Fill;
        this.splitContainerDirectLeft.Location = new System.Drawing.Point(0, 0);
        this.splitContainerDirectLeft.Name = "splitContainerDirectLeft";
        this.splitContainerDirectLeft.Orientation = System.Windows.Forms.Orientation.Horizontal;
        // 
        // splitContainerDirectLeft.Panel1
        // 
        this.splitContainerDirectLeft.Panel1.Controls.Add(this.gbCredentials);
        // 
        // splitContainerDirectLeft.Panel2
        // 
        this.splitContainerDirectLeft.Panel2.Controls.Add(this.gbDirectConfig);
        this.splitContainerDirectLeft.Size = new System.Drawing.Size(500, 560);
        this.splitContainerDirectLeft.SplitterDistance = 250;
        this.splitContainerDirectLeft.TabIndex = 0;
        // 
        // gbDirectConfig
        // 
        this.gbDirectConfig.BackColor = System.Drawing.Color.White;
        this.gbDirectConfig.Controls.Add(this.lblFile);
        this.gbDirectConfig.Controls.Add(this.lstFilePath);
        this.gbDirectConfig.Controls.Add(this.btnBrowse);
        this.gbDirectConfig.Controls.Add(this.lblSignerName);
        this.gbDirectConfig.Controls.Add(this.txtSignerName);
        this.gbDirectConfig.Controls.Add(this.lblSignerTitle);
        this.gbDirectConfig.Controls.Add(this.txtSignerTitle);
        this.gbDirectConfig.Controls.Add(this.lblSignatureType);
        this.gbDirectConfig.Controls.Add(this.cboSignatureType);
        this.gbDirectConfig.Controls.Add(this.lblDisplayNameMode);
        this.gbDirectConfig.Controls.Add(this.cboDisplayNameMode);
        this.gbDirectConfig.Controls.Add(this.lblNote);
        this.gbDirectConfig.Controls.Add(this.txtNote);
        this.gbDirectConfig.Controls.Add(this.lblNoteX);
        this.gbDirectConfig.Controls.Add(this.txtNoteX);
        this.gbDirectConfig.Controls.Add(this.lblNoteY);
        this.gbDirectConfig.Controls.Add(this.txtNoteY);
        this.gbDirectConfig.Controls.Add(this.chkShowSignatureTime);
        this.gbDirectConfig.Controls.Add(this.btnSign);
        this.gbDirectConfig.Dock = System.Windows.Forms.DockStyle.Fill;
        this.gbDirectConfig.Font = new System.Drawing.Font("Segoe UI Semibold", 9.5F, System.Drawing.FontStyle.Bold);
        this.gbDirectConfig.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
        this.gbDirectConfig.Location = new System.Drawing.Point(0, 0);
        this.gbDirectConfig.Name = "gbDirectConfig";
        this.gbDirectConfig.Size = new System.Drawing.Size(500, 320);
        this.gbDirectConfig.TabIndex = 0;
        this.gbDirectConfig.TabStop = false;
        this.gbDirectConfig.Text = "Cấu Hình Ký Số Đơn Tệp";
        // 
        // lblFile
        // 
        this.lblFile.AutoSize = true;
        this.lblFile.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F, System.Drawing.FontStyle.Bold);
        this.lblFile.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblFile.Location = new System.Drawing.Point(15, 20);
        this.lblFile.Name = "lblFile";
        this.lblFile.Size = new System.Drawing.Size(102, 17);
        this.lblFile.Text = "Tệp PDF Cần Ký *:";
        // 
        // lstFilePath
        // 
        this.lstFilePath.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lstFilePath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right))));
        this.lstFilePath.Location = new System.Drawing.Point(15, 38);
        this.lstFilePath.Name = "lstFilePath";
        this.lstFilePath.Size = new System.Drawing.Size(365, 72);
        this.lstFilePath.TabIndex = 1;
        this.lstFilePath.IntegralHeight = false;
        this.lstFilePath.SelectedIndexChanged += new System.EventHandler(this.lstFilePath_SelectedIndexChanged);
        // 
        // btnBrowse
        // 
        this.btnBrowse.Font = new System.Drawing.Font("Segoe UI", 8F);
        this.btnBrowse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
        this.btnBrowse.Location = new System.Drawing.Point(395, 37);
        this.btnBrowse.Name = "btnBrowse";
        this.btnBrowse.Size = new System.Drawing.Size(90, 29);
        this.btnBrowse.TabIndex = 2;
        this.btnBrowse.Text = "Duyệt Tệp...";
        this.btnBrowse.UseVisualStyleBackColor = true;
        this.btnBrowse.Click += new System.EventHandler(this.btnBrowse_Click);
        // 
        // lblSignerName
        // 
        this.lblSignerName.AutoSize = true;
        this.lblSignerName.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F, System.Drawing.FontStyle.Bold);
        this.lblSignerName.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblSignerName.Location = new System.Drawing.Point(15, 115);
        this.lblSignerName.Name = "lblSignerName";
        this.lblSignerName.Size = new System.Drawing.Size(91, 17);
        this.lblSignerName.Text = "Tên Người Ký:";
        // 
        // txtSignerName
        // 
        this.txtSignerName.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.txtSignerName.Location = new System.Drawing.Point(15, 133);
        this.txtSignerName.Name = "txtSignerName";
        this.txtSignerName.Size = new System.Drawing.Size(220, 27);
        this.txtSignerName.TabIndex = 3;
        this.txtSignerName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left))));
        this.txtSignerName.Text = "Người Ký Mẫu";
        // 
        // lblSignerTitle
        // 
        this.lblSignerTitle.AutoSize = true;
        this.lblSignerTitle.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F, System.Drawing.FontStyle.Bold);
        this.lblSignerTitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblSignerTitle.Location = new System.Drawing.Point(255, 115);
        this.lblSignerTitle.Name = "lblSignerTitle";
        this.lblSignerTitle.Size = new System.Drawing.Size(81, 17);
        this.lblSignerTitle.Text = "Chức Danh Người Ký:";
        // 
        // txtSignerTitle
        // 
        this.txtSignerTitle.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.txtSignerTitle.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right))));
        this.txtSignerTitle.Location = new System.Drawing.Point(255, 133);
        this.txtSignerTitle.Name = "txtSignerTitle";
        this.txtSignerTitle.Size = new System.Drawing.Size(220, 27);
        this.txtSignerTitle.TabIndex = 4;
        this.txtSignerTitle.Text = "Trưởng Phòng";
        // 
        // lblSignatureType
        // 
        this.lblSignatureType.AutoSize = true;
        this.lblSignatureType.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F, System.Drawing.FontStyle.Bold);
        this.lblSignatureType.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblSignatureType.Location = new System.Drawing.Point(15, 165);
        this.lblSignatureType.Name = "lblSignatureType";
        this.lblSignatureType.Size = new System.Drawing.Size(100, 17);
        this.lblSignatureType.Text = "Loại Chữ Ký:";
        // 
        // cboSignatureType
        // 
        this.cboSignatureType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.cboSignatureType.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.cboSignatureType.Location = new System.Drawing.Point(15, 183);
        this.cboSignatureType.Name = "cboSignatureType";
        this.cboSignatureType.Size = new System.Drawing.Size(220, 27);
        this.cboSignatureType.TabIndex = 5;
        // 
        // lblDisplayNameMode
        // 
        this.lblDisplayNameMode.AutoSize = true;
        this.lblDisplayNameMode.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F, System.Drawing.FontStyle.Bold);
        this.lblDisplayNameMode.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblDisplayNameMode.Location = new System.Drawing.Point(255, 165);
        this.lblDisplayNameMode.Name = "lblDisplayNameMode";
        this.lblDisplayNameMode.Size = new System.Drawing.Size(95, 17);
        this.lblDisplayNameMode.Text = "Chế Độ Hiển Thị:";
        // 
        // cboDisplayNameMode
        // 
        this.cboDisplayNameMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.cboDisplayNameMode.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.cboDisplayNameMode.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right))));
        this.cboDisplayNameMode.Location = new System.Drawing.Point(255, 183);
        this.cboDisplayNameMode.Name = "cboDisplayNameMode";
        this.cboDisplayNameMode.Size = new System.Drawing.Size(220, 27);
        this.cboDisplayNameMode.TabIndex = 6;
        this.cboDisplayNameMode.DropDownWidth = 350;
        // 
        // lblNote
        // 
        this.lblNote.AutoSize = true;
        this.lblNote.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F, System.Drawing.FontStyle.Bold);
        this.lblNote.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblNote.Location = new System.Drawing.Point(15, 215);
        this.lblNote.Name = "lblNote";
        this.lblNote.Size = new System.Drawing.Size(56, 17);
        this.lblNote.Text = "Ghi chú:";
        // 
        // txtNote
        // 
        this.txtNote.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.txtNote.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right))));
        this.txtNote.Location = new System.Drawing.Point(15, 233);
        this.txtNote.Name = "txtNote";
        this.txtNote.Size = new System.Drawing.Size(300, 27);
        this.txtNote.TabIndex = 7;
        // lblNoteX
        this.lblNoteX.AutoSize = true;
        this.lblNoteX.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
        
        this.lblNoteX.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F, System.Drawing.FontStyle.Bold);
        this.lblNoteX.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblNoteX.Location = new System.Drawing.Point(325, 215);
        this.lblNoteX.Name = "lblNoteX";
        this.lblNoteX.Size = new System.Drawing.Size(65, 17);
        this.lblNoteX.Text = "Tọa độ X:";
        // txtNoteX
        this.txtNoteX.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.txtNoteX.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
        
        this.txtNoteX.Location = new System.Drawing.Point(325, 233);
        this.txtNoteX.Name = "txtNoteX";
        this.txtNoteX.Size = new System.Drawing.Size(65, 27);
        // lblNoteY
        this.lblNoteY.AutoSize = true;
        this.lblNoteY.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
        
        this.lblNoteY.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F, System.Drawing.FontStyle.Bold);
        this.lblNoteY.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblNoteY.Location = new System.Drawing.Point(400, 215);
        this.lblNoteY.Name = "lblNoteY";
        this.lblNoteY.Size = new System.Drawing.Size(65, 17);
        this.lblNoteY.Text = "Tọa độ Y:";
        // txtNoteY
        this.txtNoteY.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.txtNoteY.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
        
        this.txtNoteY.Location = new System.Drawing.Point(400, 233);
        this.txtNoteY.Name = "txtNoteY";
        this.txtNoteY.Size = new System.Drawing.Size(65, 27);
        // rbPositionSignature
        this.rbPositionSignature.AutoSize = true;
        this.rbPositionSignature.Checked = true;
        this.rbPositionSignature.Location = new System.Drawing.Point(120, 18);
        this.rbPositionSignature.Name = "rbPositionSignature";
        this.rbPositionSignature.Size = new System.Drawing.Size(120, 21);
        this.rbPositionSignature.TabStop = true;
        this.rbPositionSignature.Text = "Vị trí Chữ Ký";
        this.rbPositionSignature.UseVisualStyleBackColor = true;
        // rbPositionNote
        this.rbPositionNote.AutoSize = true;
        this.rbPositionNote.Location = new System.Drawing.Point(230, 18);
        this.rbPositionNote.Name = "rbPositionNote";
        this.rbPositionNote.Size = new System.Drawing.Size(120, 21);
        this.rbPositionNote.Text = "Vị trí Ghi Chú";
        this.rbPositionNote.UseVisualStyleBackColor = true;
        // 
        // chkShowSignatureTime
        // 
        this.chkShowSignatureTime.AutoSize = true;
        this.chkShowSignatureTime.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F, System.Drawing.FontStyle.Bold);
        this.chkShowSignatureTime.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.chkShowSignatureTime.Location = new System.Drawing.Point(15, 270);
        this.chkShowSignatureTime.Name = "chkShowSignatureTime";
        this.chkShowSignatureTime.Size = new System.Drawing.Size(400, 21);
        this.chkShowSignatureTime.TabIndex = 8;
        this.chkShowSignatureTime.Text = "Hiển Thị Nhãn Thời Gian Trên Chữ Ký";
        this.chkShowSignatureTime.UseVisualStyleBackColor = true;
        // 
        // btnSign
        // 
        this.btnSign.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(43)))), ((int)(((byte)(87)))), ((int)(((byte)(154)))));
        this.btnSign.FlatAppearance.BorderSize = 0;
        this.btnSign.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnSign.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold);
        this.btnSign.ForeColor = System.Drawing.Color.White;
        this.btnSign.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right))));
        this.btnSign.Location = new System.Drawing.Point(15, 305);
        this.btnSign.Name = "btnSign";
        this.btnSign.Size = new System.Drawing.Size(460, 40);
        this.btnSign.TabIndex = 9;
        this.btnSign.Text = "⚡ KÝ PDF";
        this.btnSign.UseVisualStyleBackColor = false;
        this.btnSign.Click += new System.EventHandler(this.btnSign_Click);
        // 
        // gbDirectPreview
        // 
        this.gbDirectPreview.BackColor = System.Drawing.Color.White;
        this.gbDirectPreview.Controls.Add(this.lblPreviewMock);
        this.gbDirectPreview.Controls.Add(this.rbPositionSignature);
        this.gbDirectPreview.Controls.Add(this.rbPositionNote);
        this.gbDirectPreview.Controls.Add(this.btnPrevPage);
        this.gbDirectPreview.Controls.Add(this.btnNextPage);
        this.gbDirectPreview.Controls.Add(this.panelSigPlacementMock);
        this.gbDirectPreview.Dock = System.Windows.Forms.DockStyle.Fill;
        this.gbDirectPreview.Font = new System.Drawing.Font("Segoe UI Semibold", 9.5F, System.Drawing.FontStyle.Bold);
        this.gbDirectPreview.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
        this.gbDirectPreview.Location = new System.Drawing.Point(0, 0);
        this.gbDirectPreview.Name = "gbDirectPreview";
        this.gbDirectPreview.Size = new System.Drawing.Size(378, 461);
        this.gbDirectPreview.TabIndex = 0;
        this.gbDirectPreview.TabStop = false;
        this.gbDirectPreview.Text = "Xem Trước Văn Bản PDF && Vị Trí Ký";
        // 
        // lblSigImage
        // 
        this.lblSigImage.AutoSize = true;
        this.lblSigImage.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F, System.Drawing.FontStyle.Bold);
        this.lblSigImage.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblSigImage.Location = new System.Drawing.Point(15, 235);
        this.lblSigImage.Name = "lblSigImage";
        this.lblSigImage.Size = new System.Drawing.Size(121, 17);
        this.lblSigImage.Text = "Ảnh Chữ Ký (Tùy Chọn):";
        // 
        // pbSigImage
        // 
        this.pbSigImage.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(241)))), ((int)(((byte)(245)))), ((int)(((byte)(249)))));
        this.pbSigImage.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        this.pbSigImage.Location = new System.Drawing.Point(15, 255);
        this.pbSigImage.Name = "pbSigImage";
        this.pbSigImage.Size = new System.Drawing.Size(100, 60);
        this.pbSigImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
        this.pbSigImage.TabIndex = 9;
        this.pbSigImage.TabStop = false;
        // 
        // btnBrowseSigImage
        // 
        this.btnBrowseSigImage.Font = new System.Drawing.Font("Segoe UI", 8F);
        this.btnBrowseSigImage.Location = new System.Drawing.Point(125, 255);
        this.btnBrowseSigImage.Name = "btnBrowseSigImage";
        this.btnBrowseSigImage.Size = new System.Drawing.Size(120, 27);
        this.btnBrowseSigImage.TabIndex = 10;
        this.btnBrowseSigImage.Text = "Chọn Ảnh...";
        this.btnBrowseSigImage.UseVisualStyleBackColor = true;
        this.btnBrowseSigImage.Click += new System.EventHandler(this.btnBrowseSigImage_Click);
        // 
        // btnClearSigImage
        // 
        this.btnClearSigImage.Font = new System.Drawing.Font("Segoe UI", 8F);
        this.btnClearSigImage.Location = new System.Drawing.Point(125, 287);
        this.btnClearSigImage.Name = "btnClearSigImage";
        this.btnClearSigImage.Size = new System.Drawing.Size(120, 27);
        this.btnClearSigImage.TabIndex = 11;
        this.btnClearSigImage.Text = "Xóa Ảnh";
        this.btnClearSigImage.UseVisualStyleBackColor = true;
        this.btnClearSigImage.Click += new System.EventHandler(this.btnClearSigImage_Click);
        // 
        // lblPreviewMock
        // 
        this.lblPreviewMock.AutoSize = true;
        this.lblPreviewMock.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblPreviewMock.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblPreviewMock.Location = new System.Drawing.Point(15, 25);
        this.lblPreviewMock.Name = "lblPreviewMock";
        this.lblPreviewMock.Size = new System.Drawing.Size(175, 20);
        this.lblPreviewMock.Text = "Trang Văn Bản:";
        // 
        // btnPrevPage
        // 
        this.btnPrevPage.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
        this.btnPrevPage.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnPrevPage.FlatAppearance.BorderSize = 0;
        this.btnPrevPage.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(241)))), ((int)(((byte)(245)))), ((int)(((byte)(249)))));
        this.btnPrevPage.Font = new System.Drawing.Font("Segoe UI", 7.5F, System.Drawing.FontStyle.Bold);
        this.btnPrevPage.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(41)))), ((int)(((byte)(59)))));
        this.btnPrevPage.Location = new System.Drawing.Point(280, 22);
        this.btnPrevPage.Name = "btnPrevPage";
        this.btnPrevPage.Size = new System.Drawing.Size(40, 24);
        this.btnPrevPage.TabIndex = 4;
        this.btnPrevPage.Text = "◀";
        this.btnPrevPage.UseVisualStyleBackColor = false;
        this.btnPrevPage.Click += new System.EventHandler(this.btnPrevPage_Click);
        // 
        // btnNextPage
        // 
        this.btnNextPage.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
        this.btnNextPage.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnNextPage.FlatAppearance.BorderSize = 0;
        this.btnNextPage.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(241)))), ((int)(((byte)(245)))), ((int)(((byte)(249)))));
        this.btnNextPage.Font = new System.Drawing.Font("Segoe UI", 7.5F, System.Drawing.FontStyle.Bold);
        this.btnNextPage.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(41)))), ((int)(((byte)(59)))));
        this.btnNextPage.Location = new System.Drawing.Point(325, 22);
        this.btnNextPage.Name = "btnNextPage";
        this.btnNextPage.Size = new System.Drawing.Size(40, 24);
        this.btnNextPage.TabIndex = 5;
        this.btnNextPage.Text = "▶";
        this.btnNextPage.UseVisualStyleBackColor = false;
        this.btnNextPage.Click += new System.EventHandler(this.btnNextPage_Click);
        // 
        // panelSigPlacementMock
        // 
        this.panelSigPlacementMock.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
        this.panelSigPlacementMock.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(251)))), ((int)(((byte)(251)))), ((int)(((byte)(251)))));
        this.panelSigPlacementMock.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        this.panelSigPlacementMock.Location = new System.Drawing.Point(15, 52);
        this.panelSigPlacementMock.Name = "panelSigPlacementMock";
        this.panelSigPlacementMock.Size = new System.Drawing.Size(345, 400);
        this.panelSigPlacementMock.TabIndex = 5;
        this.panelSigPlacementMock.Paint += new System.Windows.Forms.PaintEventHandler(this.panelSigPlacementMock_Paint);
        this.panelSigPlacementMock.MouseDown += new System.Windows.Forms.MouseEventHandler(this.panelSigPlacementMock_MouseDown);
        this.panelSigPlacementMock.MouseMove += new System.Windows.Forms.MouseEventHandler(this.panelSigPlacementMock_MouseMove);
        this.panelSigPlacementMock.MouseUp += new System.Windows.Forms.MouseEventHandler(this.panelSigPlacementMock_MouseUp);
        // 
        // tabAdvanced
        // 
        this.tabAdvanced.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(250)))), ((int)(((byte)(252)))));
        this.tabAdvanced.Controls.Add(this.splitContainerAdvanced);
        this.tabAdvanced.Location = new System.Drawing.Point(4, 5);
        this.tabAdvanced.Name = "tabAdvanced";
        this.tabAdvanced.Padding = new System.Windows.Forms.Padding(15);
        this.tabAdvanced.Size = new System.Drawing.Size(822, 431);
        this.tabAdvanced.TabIndex = 2;
        this.tabAdvanced.Text = "Advanced PDF";
        // 
        // splitContainerAdvanced
        // 
        this.splitContainerAdvanced.Dock = System.Windows.Forms.DockStyle.Fill;
        this.splitContainerAdvanced.Location = new System.Drawing.Point(15, 15);
        this.splitContainerAdvanced.Name = "splitContainerAdvanced";
        // 
        // splitContainerAdvanced.Panel1
        // 
        this.splitContainerAdvanced.Panel1.Controls.Add(this.gbAdvancedSign);
        // 
        // splitContainerAdvanced.Panel2
        // 
        this.splitContainerAdvanced.Panel2.Controls.Add(this.pgAdvancedRequest);
        this.splitContainerAdvanced.Size = new System.Drawing.Size(792, 401);
        this.splitContainerAdvanced.SplitterDistance = 330;
        this.splitContainerAdvanced.TabIndex = 0;
        // 
        // gbAdvancedSign
        // 
        this.gbAdvancedSign.BackColor = System.Drawing.Color.White;
        this.gbAdvancedSign.Controls.Add(this.lblAdvancedFile);
        this.gbAdvancedSign.Controls.Add(this.txtAdvancedFilePath);
        this.gbAdvancedSign.Controls.Add(this.btnBrowseAdvanced);
        this.gbAdvancedSign.Controls.Add(this.btnSignAdvanced);
        this.gbAdvancedSign.Dock = System.Windows.Forms.DockStyle.Fill;
        this.gbAdvancedSign.Font = new System.Drawing.Font("Segoe UI Semibold", 9.5F, System.Drawing.FontStyle.Bold);
        this.gbAdvancedSign.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
        this.gbAdvancedSign.Location = new System.Drawing.Point(0, 0);
        this.gbAdvancedSign.Name = "gbAdvancedSign";
        this.gbAdvancedSign.Size = new System.Drawing.Size(330, 401);
        this.gbAdvancedSign.TabIndex = 0;
        this.gbAdvancedSign.TabStop = false;
        this.gbAdvancedSign.Text = "Advanced PDF Feature Testbed";
        // 
        // lblAdvancedFile
        // 
        this.lblAdvancedFile.AutoSize = true;
        this.lblAdvancedFile.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblAdvancedFile.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblAdvancedFile.Location = new System.Drawing.Point(20, 35);
        this.lblAdvancedFile.Name = "lblAdvancedFile";
        this.lblAdvancedFile.Size = new System.Drawing.Size(123, 20);
        this.lblAdvancedFile.Text = "Target PDF File *:";
        // 
        // txtAdvancedFilePath
        // 
        this.txtAdvancedFilePath.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        this.txtAdvancedFilePath.Location = new System.Drawing.Point(20, 58);
        this.txtAdvancedFilePath.Name = "txtAdvancedFilePath";
        this.txtAdvancedFilePath.Size = new System.Drawing.Size(200, 29);
        this.txtAdvancedFilePath.TabIndex = 1;
        // 
        // btnBrowseAdvanced
        // 
        this.btnBrowseAdvanced.Font = new System.Drawing.Font("Segoe UI", 8.5F);
        this.btnBrowseAdvanced.Location = new System.Drawing.Point(230, 57);
        this.btnBrowseAdvanced.Name = "btnBrowseAdvanced";
        this.btnBrowseAdvanced.Size = new System.Drawing.Size(80, 30);
        this.btnBrowseAdvanced.TabIndex = 2;
        this.btnBrowseAdvanced.Text = "Browse...";
        this.btnBrowseAdvanced.UseVisualStyleBackColor = true;
        this.btnBrowseAdvanced.Click += new System.EventHandler(this.btnBrowseAdvanced_Click);
        // 
        // btnSignAdvanced
        // 
        this.btnSignAdvanced.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
        this.btnSignAdvanced.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(41)))), ((int)(((byte)(59)))));
        this.btnSignAdvanced.FlatAppearance.BorderSize = 0;
        this.btnSignAdvanced.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnSignAdvanced.ForeColor = System.Drawing.Color.White;
        this.btnSignAdvanced.Location = new System.Drawing.Point(20, 320);
        this.btnSignAdvanced.Name = "btnSignAdvanced";
        this.btnSignAdvanced.Size = new System.Drawing.Size(290, 45);
        this.btnSignAdvanced.TabIndex = 3;
        this.btnSignAdvanced.Text = "🚀 SIGN WITH PROPERTY OBJECT";
        this.btnSignAdvanced.UseVisualStyleBackColor = false;
        this.btnSignAdvanced.Click += new System.EventHandler(this.btnSignAdvanced_Click);
        // 
        // pgAdvancedRequest
        // 
        this.pgAdvancedRequest.Dock = System.Windows.Forms.DockStyle.Fill;
        this.pgAdvancedRequest.Location = new System.Drawing.Point(0, 0);
        this.pgAdvancedRequest.Name = "pgAdvancedRequest";
        this.pgAdvancedRequest.Size = new System.Drawing.Size(458, 401);
        this.pgAdvancedRequest.TabIndex = 0;
        // 
           this.tabBatch.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(250)))), ((int)(((byte)(252)))));
        this.tabBatch.Controls.Add(this.splitContainerBatch);
        this.tabBatch.Location = new System.Drawing.Point(4, 5);
        this.tabBatch.Name = "tabBatch";
        this.tabBatch.Padding = new System.Windows.Forms.Padding(15);
        this.tabBatch.Size = new System.Drawing.Size(822, 431);
        this.tabBatch.TabIndex = 3;
        this.tabBatch.Text = "Ký Hàng Loạt";
        // 
        // splitContainerBatch
        // 
        this.splitContainerBatch.Dock = System.Windows.Forms.DockStyle.Fill;
        this.splitContainerBatch.Location = new System.Drawing.Point(15, 15);
        this.splitContainerBatch.Name = "splitContainerBatch";
        // 
        // splitContainerBatch.Panel1
        // 
        this.splitContainerBatch.Panel1.Controls.Add(this.gbBatchConfig);
        // 
        // splitContainerBatch.Panel2
        // 
        this.splitContainerBatch.Panel2.Controls.Add(this.gbBatchFilesList);
        this.splitContainerBatch.Size = new System.Drawing.Size(792, 401);
        this.splitContainerBatch.SplitterDistance = 330;
        this.splitContainerBatch.TabIndex = 0;
        // 
        // gbBatchConfig
        // 
        this.gbBatchConfig.BackColor = System.Drawing.Color.White;
        this.gbBatchConfig.Controls.Add(this.lblBatchFolder);
        this.gbBatchConfig.Controls.Add(this.txtBatchFolder);
        this.gbBatchConfig.Controls.Add(this.btnBrowseBatch);
        this.gbBatchConfig.Controls.Add(this.lblBatchUserSecret);
        this.gbBatchConfig.Controls.Add(this.txtBatchUserSecret);
        this.gbBatchConfig.Controls.Add(this.lblBatchCertPath);
        this.gbBatchConfig.Controls.Add(this.txtBatchCertPath);
        this.gbBatchConfig.Controls.Add(this.btnBrowseBatchCert);
        this.gbBatchConfig.Controls.Add(this.lblBatchCertPass);
        this.gbBatchConfig.Controls.Add(this.txtBatchCertPass);
        this.gbBatchConfig.Controls.Add(this.lblBatchOutput);
        this.gbBatchConfig.Controls.Add(this.txtBatchOutput);
        this.gbBatchConfig.Controls.Add(this.btnBrowseBatchOutput);
        this.gbBatchConfig.Controls.Add(this.btnBatchSign);
        this.gbBatchConfig.Controls.Add(this.progressBar);
        this.gbBatchConfig.Controls.Add(this.lblBatchStatus);
        this.gbBatchConfig.Dock = System.Windows.Forms.DockStyle.Fill;
        this.gbBatchConfig.Font = new System.Drawing.Font("Segoe UI Semibold", 9.5F, System.Drawing.FontStyle.Bold);
        this.gbBatchConfig.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
        this.gbBatchConfig.Location = new System.Drawing.Point(0, 0);
        this.gbBatchConfig.Name = "gbBatchConfig";
        this.gbBatchConfig.Size = new System.Drawing.Size(330, 401);
        this.gbBatchConfig.TabIndex = 0;
        this.gbBatchConfig.TabStop = false;
        this.gbBatchConfig.Text = "Cấu Hình Ký Hàng Loạt";
        // 
        // lblBatchFolder
        // 
        this.lblBatchFolder.AutoSize = true;
        this.lblBatchFolder.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblBatchFolder.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblBatchFolder.Location = new System.Drawing.Point(15, 20);
        this.lblBatchFolder.Name = "lblBatchFolder";
        this.lblBatchFolder.Size = new System.Drawing.Size(126, 20);
        this.lblBatchFolder.Text = "Thư Mục Nguồn PDF:";
        // 
        // txtBatchFolder
        // 
        this.txtBatchFolder.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        this.txtBatchFolder.Location = new System.Drawing.Point(15, 43);
        this.txtBatchFolder.Name = "txtBatchFolder";
        this.txtBatchFolder.Size = new System.Drawing.Size(205, 29);
        this.txtBatchFolder.TabIndex = 1;
        // 
        // btnBrowseBatch
        // 
        this.btnBrowseBatch.Font = new System.Drawing.Font("Segoe UI", 8.5F);
        this.btnBrowseBatch.Location = new System.Drawing.Point(230, 42);
        this.btnBrowseBatch.Name = "btnBrowseBatch";
        this.btnBrowseBatch.Size = new System.Drawing.Size(80, 30);
        this.btnBrowseBatch.TabIndex = 2;
        this.btnBrowseBatch.Text = "Duyệt...";
        this.btnBrowseBatch.UseVisualStyleBackColor = true;
        this.btnBrowseBatch.Click += new System.EventHandler(this.btnBrowseBatch_Click);
        // 
        // lblBatchUserSecret
        // 
        this.lblBatchUserSecret.AutoSize = true;
        this.lblBatchUserSecret.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblBatchUserSecret.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblBatchUserSecret.Location = new System.Drawing.Point(15, 80);
        this.lblBatchUserSecret.Name = "lblBatchUserSecret";
        this.lblBatchUserSecret.Size = new System.Drawing.Size(217, 20);
        this.lblBatchUserSecret.Text = "Mã Bảo Mật (SmartCA OTP):";
        // 
        // txtBatchUserSecret
        // 
        this.txtBatchUserSecret.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        this.txtBatchUserSecret.Location = new System.Drawing.Point(15, 100);
        this.txtBatchUserSecret.Name = "txtBatchUserSecret";
        this.txtBatchUserSecret.Size = new System.Drawing.Size(295, 29);
        this.txtBatchUserSecret.TabIndex = 3;
        // 
        // lblBatchCertPath
        // 
        this.lblBatchCertPath.AutoSize = true;
        this.lblBatchCertPath.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblBatchCertPath.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblBatchCertPath.Location = new System.Drawing.Point(15, 137);
        this.lblBatchCertPath.Name = "lblBatchCertPath";
        this.lblBatchCertPath.Size = new System.Drawing.Size(184, 20);
        this.lblBatchCertPath.Text = "Tệp Chứng Thư (Local/USB):";
        // 
        // txtBatchCertPath
        // 
        this.txtBatchCertPath.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        this.txtBatchCertPath.Location = new System.Drawing.Point(15, 157);
        this.txtBatchCertPath.Name = "txtBatchCertPath";
        this.txtBatchCertPath.Size = new System.Drawing.Size(205, 29);
        this.txtBatchCertPath.TabIndex = 4;
        // 
        // btnBrowseBatchCert
        // 
        this.btnBrowseBatchCert.Font = new System.Drawing.Font("Segoe UI", 8.5F);
        this.btnBrowseBatchCert.Location = new System.Drawing.Point(230, 156);
        this.btnBrowseBatchCert.Name = "btnBrowseBatchCert";
        this.btnBrowseBatchCert.Size = new System.Drawing.Size(80, 30);
        this.btnBrowseBatchCert.TabIndex = 5;
        this.btnBrowseBatchCert.Text = "Duyệt...";
        this.btnBrowseBatchCert.UseVisualStyleBackColor = true;
        this.btnBrowseBatchCert.Click += new System.EventHandler(this.btnBrowseBatchCert_Click);
        // 
        // lblBatchCertPass
        // 
        this.lblBatchCertPass.AutoSize = true;
        this.lblBatchCertPass.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblBatchCertPass.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblBatchCertPass.Location = new System.Drawing.Point(15, 194);
        this.lblBatchCertPass.Name = "lblBatchCertPass";
        this.lblBatchCertPass.Size = new System.Drawing.Size(150, 20);
        this.lblBatchCertPass.Text = "Mật Khẩu Chứng Thư:";
        // 
        // txtBatchCertPass
        // 
        this.txtBatchCertPass.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        this.txtBatchCertPass.Location = new System.Drawing.Point(15, 214);
        this.txtBatchCertPass.Name = "txtBatchCertPass";
        this.txtBatchCertPass.PasswordChar = '*';
        this.txtBatchCertPass.Size = new System.Drawing.Size(295, 29);
        this.txtBatchCertPass.TabIndex = 6;
        // 
        // lblBatchOutput
        // 
        this.lblBatchOutput.AutoSize = true;
        this.lblBatchOutput.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblBatchOutput.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
        this.lblBatchOutput.Location = new System.Drawing.Point(15, 251);
        this.lblBatchOutput.Name = "lblBatchOutput";
        this.lblBatchOutput.Size = new System.Drawing.Size(213, 20);
        this.lblBatchOutput.Text = "Thư Mục Đầu Ra:";
        // 
        // txtBatchOutput
        // 
        this.txtBatchOutput.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        this.txtBatchOutput.Location = new System.Drawing.Point(15, 271);
        this.txtBatchOutput.Name = "txtBatchOutput";
        this.txtBatchOutput.Size = new System.Drawing.Size(205, 29);
        this.txtBatchOutput.TabIndex = 7;
        // 
        // btnBrowseBatchOutput
        // 
        this.btnBrowseBatchOutput.Font = new System.Drawing.Font("Segoe UI", 8.5F);
        this.btnBrowseBatchOutput.Location = new System.Drawing.Point(230, 270);
        this.btnBrowseBatchOutput.Name = "btnBrowseBatchOutput";
        this.btnBrowseBatchOutput.Size = new System.Drawing.Size(80, 30);
        this.btnBrowseBatchOutput.TabIndex = 8;
        this.btnBrowseBatchOutput.Text = "Duyệt...";
        this.btnBrowseBatchOutput.UseVisualStyleBackColor = true;
        this.btnBrowseBatchOutput.Click += new System.EventHandler(this.btnBrowseBatchOutput_Click);
        // 
        // btnBatchSign
        // 
        this.btnBatchSign.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(16)))), ((int)(((byte)(185)))), ((int)(((byte)(129)))));
        this.btnBatchSign.FlatAppearance.BorderSize = 0;
        this.btnBatchSign.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnBatchSign.ForeColor = System.Drawing.Color.White;
        this.btnBatchSign.Location = new System.Drawing.Point(15, 312);
        this.btnBatchSign.Name = "btnBatchSign";
        this.btnBatchSign.Size = new System.Drawing.Size(295, 42);
        this.btnBatchSign.TabIndex = 9;
        this.btnBatchSign.Text = "📚 BẮT ĐẦU KÝ HÀNG LOẠT";
        this.btnBatchSign.UseVisualStyleBackColor = false;
        this.btnBatchSign.Click += new System.EventHandler(this.btnBatchSign_Click);
        // 
        // progressBar
        // 
        this.progressBar.Location = new System.Drawing.Point(15, 360);
        this.progressBar.Name = "progressBar";
        this.progressBar.Size = new System.Drawing.Size(295, 12);
        this.progressBar.TabIndex = 10;
        // 
        // lblBatchStatus
        // 
        this.lblBatchStatus.Font = new System.Drawing.Font("Segoe UI", 8F);
        this.lblBatchStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(116)))), ((int)(((byte)(139)))));
        this.lblBatchStatus.Location = new System.Drawing.Point(15, 375);
        this.lblBatchStatus.Name = "lblBatchStatus";
        this.lblBatchStatus.Size = new System.Drawing.Size(295, 18);
        this.lblBatchStatus.Text = "Trạng Thái: Đang Chờ";
        this.lblBatchStatus.TextAlign = System.Drawing.ContentAlignment.TopCenter;
        // 
        // gbBatchFilesList
        // 
        this.gbBatchFilesList.BackColor = System.Drawing.Color.White;
        this.gbBatchFilesList.Controls.Add(this.dgvBatchFiles);
        this.gbBatchFilesList.Controls.Add(this.btnSelectAllBatch);
        this.gbBatchFilesList.Controls.Add(this.btnClearAllBatch);
        this.gbBatchFilesList.Dock = System.Windows.Forms.DockStyle.Fill;
        this.gbBatchFilesList.Font = new System.Drawing.Font("Segoe UI Semibold", 9.5F, System.Drawing.FontStyle.Bold);
        this.gbBatchFilesList.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
        this.gbBatchFilesList.Location = new System.Drawing.Point(0, 0);
        this.gbBatchFilesList.Name = "gbBatchFilesList";
        this.gbBatchFilesList.Size = new System.Drawing.Size(458, 401);
        this.gbBatchFilesList.TabIndex = 0;
        this.gbBatchFilesList.TabStop = false;
        this.gbBatchFilesList.Text = "Danh Sách Tệp Chờ Ký Hàng Loạt";
        // 
        // dgvBatchFiles
        // 
        this.dgvBatchFiles.AllowUserToAddRows = false;
        this.dgvBatchFiles.AllowUserToDeleteRows = false;
        this.dgvBatchFiles.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
        this.dgvBatchFiles.BackgroundColor = System.Drawing.Color.White;
        this.dgvBatchFiles.BorderStyle = System.Windows.Forms.BorderStyle.None;
        this.dgvBatchFiles.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.SingleHorizontal;
        this.dgvBatchFiles.ColumnHeadersDefaultCellStyle = dgvHeaderStyle;
        this.dgvBatchFiles.ColumnHeadersHeight = 28;
        this.dgvBatchFiles.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        this.dgvBatchFiles.DefaultCellStyle = dgvRowStyle;
        this.dgvBatchFiles.EnableHeadersVisualStyles = false;
        this.dgvBatchFiles.GridColor = System.Drawing.Color.FromArgb(((int)(((byte)(226)))), ((int)(((byte)(232)))), ((int)(((byte)(240)))));
        this.dgvBatchFiles.Location = new System.Drawing.Point(15, 30);
        this.dgvBatchFiles.MultiSelect = false;
        this.dgvBatchFiles.Name = "dgvBatchFiles";
        this.dgvBatchFiles.RowHeadersVisible = false;
        this.dgvBatchFiles.RowHeadersWidth = 51;
        this.dgvBatchFiles.RowTemplate.Height = 35;
        this.dgvBatchFiles.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
        this.dgvBatchFiles.Size = new System.Drawing.Size(428, 312);
        this.dgvBatchFiles.TabIndex = 0;
        // 
        // btnSelectAllBatch
        // 
        this.btnSelectAllBatch.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
        this.btnSelectAllBatch.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F);
        this.btnSelectAllBatch.Location = new System.Drawing.Point(15, 355);
        this.btnSelectAllBatch.Name = "btnSelectAllBatch";
        this.btnSelectAllBatch.Size = new System.Drawing.Size(120, 30);
        this.btnSelectAllBatch.TabIndex = 1;
        this.btnSelectAllBatch.Text = "Chọn Tất Cả";
        this.btnSelectAllBatch.UseVisualStyleBackColor = true;
        this.btnSelectAllBatch.Click += new System.EventHandler(this.btnSelectAllBatch_Click);
        // 
        // btnClearAllBatch
        // 
        this.btnClearAllBatch.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
        this.btnClearAllBatch.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F);
        this.btnClearAllBatch.Location = new System.Drawing.Point(145, 355);
        this.btnClearAllBatch.Name = "btnClearAllBatch";
        this.btnClearAllBatch.Size = new System.Drawing.Size(120, 30);
        this.btnClearAllBatch.TabIndex = 2;
        this.btnClearAllBatch.Text = "Bỏ Chọn Tất Cả";
        this.btnClearAllBatch.UseVisualStyleBackColor = true;
        this.btnClearAllBatch.Click += new System.EventHandler(this.btnClearAllBatch_Click);
        // 
        // tabXml
        // 
        this.tabXml.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(250)))), ((int)(((byte)(252)))));
        this.tabXml.Controls.Add(this.splitContainerXml);
        this.tabXml.Location = new System.Drawing.Point(4, 5);
        this.tabXml.Name = "tabXml";
        this.tabXml.Size = new System.Drawing.Size(822, 431);
        this.tabXml.TabIndex = 4;
        this.tabXml.Text = "XML Sign";
        // 
        // splitContainerXml
        // 
        this.splitContainerXml.Dock = System.Windows.Forms.DockStyle.Fill;
        this.splitContainerXml.Location = new System.Drawing.Point(0, 0);
        this.splitContainerXml.Name = "splitContainerXml";
        // 
        // splitContainerXml.Panel1
        // 
        this.splitContainerXml.Panel1.Controls.Add(this.gbXmlAction);
        this.splitContainerXml.Panel1.Padding = new System.Windows.Forms.Padding(15);
        // 
        // splitContainerXml.Panel2
        // 
        this.splitContainerXml.Panel2.Controls.Add(this.pgXmlRequest);
        this.splitContainerXml.Size = new System.Drawing.Size(822, 431);
        this.splitContainerXml.SplitterDistance = 330;
        this.splitContainerXml.TabIndex = 0;
        // 
        // gbXmlAction
        // 
        this.gbXmlAction.BackColor = System.Drawing.Color.White;
        this.gbXmlAction.Controls.Add(this.btnSignXml);
        this.gbXmlAction.Dock = System.Windows.Forms.DockStyle.Fill;
        this.gbXmlAction.Font = new System.Drawing.Font("Segoe UI Semibold", 9.5F, System.Drawing.FontStyle.Bold);
        this.gbXmlAction.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
        this.gbXmlAction.Location = new System.Drawing.Point(15, 15);
        this.gbXmlAction.Name = "gbXmlAction";
        this.gbXmlAction.Size = new System.Drawing.Size(300, 401);
        this.gbXmlAction.TabIndex = 0;
        this.gbXmlAction.TabStop = false;
        this.gbXmlAction.Text = "XML Signing (Mock Preview)";
        // 
        // btnSignXml
        // 
        this.btnSignXml.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
        this.btnSignXml.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(41)))), ((int)(((byte)(59)))));
        this.btnSignXml.FlatAppearance.BorderSize = 0;
        this.btnSignXml.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnSignXml.ForeColor = System.Drawing.Color.White;
        this.btnSignXml.Location = new System.Drawing.Point(20, 330);
        this.btnSignXml.Name = "btnSignXml";
        this.btnSignXml.Size = new System.Drawing.Size(260, 45);
        this.btnSignXml.TabIndex = 1;
        this.btnSignXml.Text = "💠 SIGN XML DATA";
        this.btnSignXml.UseVisualStyleBackColor = false;
        this.btnSignXml.Click += new System.EventHandler(this.btnSignXml_Click);
        // 
        // pgXmlRequest
        // 
        this.pgXmlRequest.Dock = System.Windows.Forms.DockStyle.Fill;
        this.pgXmlRequest.Location = new System.Drawing.Point(0, 0);
        this.pgXmlRequest.Name = "pgXmlRequest";
        this.pgXmlRequest.Size = new System.Drawing.Size(488, 431);
        this.pgXmlRequest.TabIndex = 0;
        // 
        // tabSettings
        // 
        this.tabSettings.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(250)))), ((int)(((byte)(252)))));
        this.tabSettings.Controls.Add(this.pgSettings);
        this.tabSettings.Controls.Add(this.panelSettingsBottom);
        this.tabSettings.Location = new System.Drawing.Point(4, 5);
        this.tabSettings.Name = "tabSettings";
        this.tabSettings.Size = new System.Drawing.Size(822, 431);
        this.tabSettings.TabIndex = 5;
        this.tabSettings.Text = "Cài Đặt";
        // 
        // pgSettings
        // 
        this.pgSettings.Dock = System.Windows.Forms.DockStyle.Fill;
        this.pgSettings.Location = new System.Drawing.Point(0, 0);
        this.pgSettings.Name = "pgSettings";
        this.pgSettings.Size = new System.Drawing.Size(822, 381);
        this.pgSettings.TabIndex = 0;
        // 
        // panelSettingsBottom
        // 
        this.panelSettingsBottom.Controls.Add(this.btnSaveSettings);
        this.panelSettingsBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
        this.panelSettingsBottom.Location = new System.Drawing.Point(0, 381);
        this.panelSettingsBottom.Name = "panelSettingsBottom";
        this.panelSettingsBottom.Size = new System.Drawing.Size(822, 50);
        this.panelSettingsBottom.TabIndex = 1;
        // 
        // btnSaveSettings
        // 
        this.btnSaveSettings.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(16)))), ((int)(((byte)(185)))), ((int)(((byte)(129)))));
        this.btnSaveSettings.Dock = System.Windows.Forms.DockStyle.Fill;
        this.btnSaveSettings.FlatAppearance.BorderSize = 0;
        this.btnSaveSettings.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnSaveSettings.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold);
        this.btnSaveSettings.ForeColor = System.Drawing.Color.White;
        this.btnSaveSettings.Location = new System.Drawing.Point(0, 0);
        this.btnSaveSettings.Name = "btnSaveSettings";
        this.btnSaveSettings.Size = new System.Drawing.Size(822, 50);
        this.btnSaveSettings.TabIndex = 0;
        this.btnSaveSettings.Text = "💾 LƯU CẤU HÌNH NHÀ CUNG CẤP VÀO APPSETTINGS.JSON";
        this.btnSaveSettings.UseVisualStyleBackColor = false;
        this.btnSaveSettings.Click += new System.EventHandler(this.btnSaveSettings_Click);
        // 
        // panelLogsTitle
        // 
        this.panelLogsTitle.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(41)))), ((int)(((byte)(59)))));
        this.panelLogsTitle.Controls.Add(this.lblLogsTitle);
        this.panelLogsTitle.Controls.Add(this.btnClearLogs);
        this.panelLogsTitle.Controls.Add(this.btnCopyLogs);
        this.panelLogsTitle.Dock = System.Windows.Forms.DockStyle.Bottom;
        this.panelLogsTitle.Location = new System.Drawing.Point(220, 500);
        this.panelLogsTitle.Name = "panelLogsTitle";
        this.panelLogsTitle.Size = new System.Drawing.Size(830, 30);
        this.panelLogsTitle.TabIndex = 3;
        // 
        // lblLogsTitle
        // 
        this.lblLogsTitle.AutoSize = true;
        this.lblLogsTitle.Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F, System.Drawing.FontStyle.Bold);
        this.lblLogsTitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(148)))), ((int)(((byte)(163)))), ((int)(((byte)(184)))));
        this.lblLogsTitle.Location = new System.Drawing.Point(15, 6);
        this.lblLogsTitle.Name = "lblLogsTitle";
        this.lblLogsTitle.Size = new System.Drawing.Size(161, 20);
        this.lblLogsTitle.Text = "Nhật Ký Chẩn Đoán SDK";
        // 
        // btnClearLogs
        // 
        this.btnClearLogs.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
        this.btnClearLogs.BackColor = System.Drawing.Color.Transparent;
        this.btnClearLogs.FlatAppearance.BorderSize = 0;
        this.btnClearLogs.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnClearLogs.Font = new System.Drawing.Font("Segoe UI", 8F);
        this.btnClearLogs.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(113)))), ((int)(((byte)(113)))));
        this.btnClearLogs.Location = new System.Drawing.Point(615, 2);
        this.btnClearLogs.Name = "btnClearLogs";
        this.btnClearLogs.Size = new System.Drawing.Size(100, 25);
        this.btnClearLogs.TabIndex = 1;
        this.btnClearLogs.Text = "Xóa Nhật Ký";
        this.btnClearLogs.UseVisualStyleBackColor = false;
        this.btnClearLogs.Click += new System.EventHandler(this.btnClearLogs_Click);
        // 
        // btnCopyLogs
        // 
        this.btnCopyLogs.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
        this.btnCopyLogs.BackColor = System.Drawing.Color.Transparent;
        this.btnCopyLogs.FlatAppearance.BorderSize = 0;
        this.btnCopyLogs.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnCopyLogs.Font = new System.Drawing.Font("Segoe UI", 8F);
        this.btnCopyLogs.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(56)))), ((int)(((byte)(189)))), ((int)(((byte)(248)))));
        this.btnCopyLogs.Location = new System.Drawing.Point(725, 2);
        this.btnCopyLogs.Name = "btnCopyLogs";
        this.btnCopyLogs.Size = new System.Drawing.Size(90, 25);
        this.btnCopyLogs.TabIndex = 2;
        this.btnCopyLogs.Text = "Sao Chép";
        this.btnCopyLogs.UseVisualStyleBackColor = false;
        this.btnCopyLogs.Click += new System.EventHandler(this.btnCopyLogs_Click);
        // 
        // txtLogs
        // 
        this.txtLogs.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
        this.txtLogs.BorderStyle = System.Windows.Forms.BorderStyle.None;
        this.txtLogs.Dock = System.Windows.Forms.DockStyle.Bottom;
        this.txtLogs.Font = new System.Drawing.Font("Consolas", 9.5F);
        this.txtLogs.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(148)))), ((int)(((byte)(163)))), ((int)(((byte)(184)))));
        this.txtLogs.Location = new System.Drawing.Point(220, 530);
        this.txtLogs.Name = "txtLogs";
        this.txtLogs.ReadOnly = true;
        this.txtLogs.Size = new System.Drawing.Size(830, 150);
        this.txtLogs.TabIndex = 4;
        this.txtLogs.Text = "";
        // 
        // MainForm
        // 
        this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(1200, 840);
        this.Controls.Add(this.tabControl);
        this.Controls.Add(this.panelLogsTitle);
        this.Controls.Add(this.txtLogs);
        this.Controls.Add(this.panelHeader);
        this.Controls.Add(this.panelSidebar);
        this.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.Name = "MainForm";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        this.Text = "Vimes SignSDK - Công Cụ Trình Diễn Ký Số Trực Quan";
        this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
        this.panelSidebar.ResumeLayout(false);
        this.panelHeader.ResumeLayout(false);
        this.panelHeader.PerformLayout();
        this.tabControl.ResumeLayout(false);
        this.splitContainerDirectLeft.Panel1.ResumeLayout(false);
        this.splitContainerDirectLeft.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.splitContainerDirectLeft)).EndInit();
        this.splitContainerDirectLeft.ResumeLayout(false);
        this.gbCredentials.ResumeLayout(false);
        this.gbCredentials.PerformLayout();
        this.tabDirect.ResumeLayout(false);
        this.splitContainerDirect.Panel1.ResumeLayout(false);
        this.splitContainerDirect.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.splitContainerDirect)).EndInit();
        this.splitContainerDirect.ResumeLayout(false);
        this.gbDirectConfig.ResumeLayout(false);
        this.gbDirectConfig.PerformLayout();
        this.gbDirectPreview.ResumeLayout(false);
        this.gbDirectPreview.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)(this.pbSigImage)).EndInit();
        // Redundant visual trick
        this.tabAdvanced.ResumeLayout(false);
        this.splitContainerAdvanced.Panel1.ResumeLayout(false);
        this.splitContainerAdvanced.Panel1.PerformLayout();
        this.splitContainerAdvanced.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.splitContainerAdvanced)).EndInit();
        this.splitContainerAdvanced.ResumeLayout(false);
        this.gbAdvancedSign.ResumeLayout(false);
        this.gbAdvancedSign.PerformLayout();
        this.tabXml.ResumeLayout(false);
        this.splitContainerXml.Panel1.ResumeLayout(false);
        this.splitContainerXml.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.splitContainerXml)).EndInit();
        this.splitContainerXml.ResumeLayout(false);
        this.gbXmlAction.ResumeLayout(false);
        this.tabBatch.ResumeLayout(false);
        this.splitContainerBatch.Panel1.ResumeLayout(false);
        this.splitContainerBatch.Panel1.PerformLayout();
        this.splitContainerBatch.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.splitContainerBatch)).EndInit();
        this.splitContainerBatch.ResumeLayout(false);
        this.gbBatchConfig.ResumeLayout(false);
        this.gbBatchConfig.PerformLayout();
        this.gbBatchFilesList.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.dgvBatchFiles)).EndInit();
        this.tabSettings.ResumeLayout(false);
        this.panelSettingsBottom.ResumeLayout(false);
        this.panelLogsTitle.ResumeLayout(false);
        this.panelLogsTitle.PerformLayout();
        this.ResumeLayout(false);

    }

    private System.Windows.Forms.RichTextBox txtLogs;
    private System.Windows.Forms.Panel panelSidebar;
    private System.Windows.Forms.Label lblSidebarTitle;
    private System.Windows.Forms.Button btnNavDirect;

    private System.Windows.Forms.Button btnNavXml;
    private System.Windows.Forms.Button btnNavSettings;
    
    private System.Windows.Forms.Panel panelHeader;
    private System.Windows.Forms.Label lblMerchant;
    private System.Windows.Forms.ComboBox cboMerchant;
    private System.Windows.Forms.Label lblSessionStatus;
    private System.Windows.Forms.Panel panelStatusDot;

    private System.Windows.Forms.TabControl tabControl;
    
    // Tab 1: Identity & Credentials
    private System.Windows.Forms.SplitContainer splitContainerIdentity;
    private System.Windows.Forms.GroupBox gbCredentials;
    private System.Windows.Forms.Label lblUser;
    private System.Windows.Forms.TextBox txtUserName;
    private System.Windows.Forms.Label lblPass;
    private System.Windows.Forms.TextBox txtPassword;

    private System.Windows.Forms.Button btnLogin;
    private System.Windows.Forms.Button btnSyncCertificates;
    private System.Windows.Forms.Label lblActiveCertLabel;
    private System.Windows.Forms.ComboBox cboCerts;

    // Tab 2: Quick PDF Sign
    private System.Windows.Forms.TabPage tabDirect;
    private System.Windows.Forms.SplitContainer splitContainerDirect;
    private System.Windows.Forms.SplitContainer splitContainerDirectLeft;
    private System.Windows.Forms.GroupBox gbDirectConfig;
    private System.Windows.Forms.Label lblFile;
    private System.Windows.Forms.ListBox lstFilePath;
    private System.Windows.Forms.Button btnBrowse;
    private System.Windows.Forms.Label lblSignerName;
    private System.Windows.Forms.TextBox txtSignerName;
    private System.Windows.Forms.Label lblSignerTitle;
    private System.Windows.Forms.TextBox txtSignerTitle;
    private System.Windows.Forms.Label lblSignatureType;
    private System.Windows.Forms.ComboBox cboSignatureType;
    private System.Windows.Forms.Label lblDisplayNameMode;
    private System.Windows.Forms.ComboBox cboDisplayNameMode;
    private System.Windows.Forms.Label lblNote;
    private System.Windows.Forms.TextBox txtNote;
    private System.Windows.Forms.RadioButton rbPositionSignature;
    private System.Windows.Forms.RadioButton rbPositionNote;
    private System.Windows.Forms.Label lblNoteX;
    private System.Windows.Forms.TextBox txtNoteX;
    private System.Windows.Forms.Label lblNoteY;
    private System.Windows.Forms.TextBox txtNoteY;
    private System.Windows.Forms.CheckBox chkShowSignatureTime;
    private System.Windows.Forms.Button btnSign;
    private System.Windows.Forms.GroupBox gbDirectPreview;
    private System.Windows.Forms.Label lblSigImage;
    private System.Windows.Forms.PictureBox pbSigImage;
    private System.Windows.Forms.Button btnBrowseSigImage;
    private System.Windows.Forms.Button btnClearSigImage;
    private System.Windows.Forms.Label lblPreviewMock;
    private System.Windows.Forms.Panel panelSigPlacementMock;
    private System.Windows.Forms.Button btnPrevPage;
    private System.Windows.Forms.Button btnNextPage;

    // Tab 3: Advanced PDF
    private System.Windows.Forms.TabPage tabAdvanced;
    private System.Windows.Forms.SplitContainer splitContainerAdvanced;
    private System.Windows.Forms.GroupBox gbAdvancedSign;
    private System.Windows.Forms.Label lblAdvancedFile;
    private System.Windows.Forms.TextBox txtAdvancedFilePath;
    private System.Windows.Forms.Button btnBrowseAdvanced;
    private System.Windows.Forms.Button btnSignAdvanced;
    private System.Windows.Forms.PropertyGrid pgAdvancedRequest;

    // Tab 4: Batch PDF Sign
    private System.Windows.Forms.TabPage tabBatch;
    private System.Windows.Forms.SplitContainer splitContainerBatch;
    private System.Windows.Forms.GroupBox gbBatchConfig;
    private System.Windows.Forms.Label lblBatchFolder;
    private System.Windows.Forms.TextBox txtBatchFolder;
    private System.Windows.Forms.Button btnBrowseBatch;
    private System.Windows.Forms.Label lblBatchUserSecret;
    private System.Windows.Forms.TextBox txtBatchUserSecret;
    private System.Windows.Forms.Label lblBatchCertPath;
    private System.Windows.Forms.TextBox txtBatchCertPath;
    private System.Windows.Forms.Button btnBrowseBatchCert;
    private System.Windows.Forms.Label lblBatchCertPass;
    private System.Windows.Forms.TextBox txtBatchCertPass;
    private System.Windows.Forms.Label lblBatchOutput;
    private System.Windows.Forms.TextBox txtBatchOutput;
    private System.Windows.Forms.Button btnBrowseBatchOutput;
    private System.Windows.Forms.Button btnBatchSign;
    private System.Windows.Forms.ProgressBar progressBar;
    private System.Windows.Forms.Label lblBatchStatus;
    private System.Windows.Forms.GroupBox gbBatchFilesList;
    private System.Windows.Forms.DataGridView dgvBatchFiles;
    private System.Windows.Forms.Button btnSelectAllBatch;
    private System.Windows.Forms.Button btnClearAllBatch;

    // Tab 5: XML Sign
    private System.Windows.Forms.TabPage tabXml;
    private System.Windows.Forms.SplitContainer splitContainerXml;
    private System.Windows.Forms.GroupBox gbXmlAction;
    private System.Windows.Forms.Button btnSignXml;
    private System.Windows.Forms.PropertyGrid pgXmlRequest;

    // Tab 6: Settings
    private System.Windows.Forms.TabPage tabSettings;
    private System.Windows.Forms.PropertyGrid pgSettings;
    private System.Windows.Forms.Panel panelSettingsBottom;
    private System.Windows.Forms.Button btnSaveSettings;

    private System.Windows.Forms.Panel panelLogsTitle;
    private System.Windows.Forms.Label lblLogsTitle;
    private System.Windows.Forms.Button btnClearLogs;
    private System.Windows.Forms.Button btnCopyLogs;
}

