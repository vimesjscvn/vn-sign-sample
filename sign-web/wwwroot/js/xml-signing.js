/**
 * XML signing — upload, analyze (HOC_BA/TONG_KET/LY_LICH detection), sign.
 * Mirrors sign-app's Ký XML tab (MainWindow.axaml.cs: btnAnalyzeXml_Click / btnSignXml_Click).
 */
'use strict';

const XmlSigning = {
    filePath: null,
    fileName: null,
    lastOutputPath: null,
    isUploading: false,
    isSigning: false,
    _parentXPathOptions: [], // [{ xPath, label, referenceId }] from the last analysis

    init() {
        this.bindFileInput();
        this.bindAnalyzeButton();
        this.bindSignButton();
        this.bindParentXPathSync();
        this.bindDownloadButton();
        this.updateUI();
    },

    bindFileInput() {
        const input = document.getElementById('xmlFileInput');
        if (input) input.addEventListener('change', async (e) => {
            const file = e.target.files[0];
            if (file) await this.uploadFile(file);
        });
    },

    bindAnalyzeButton() {
        const btn = document.getElementById('btnAnalyzeXml');
        if (btn) btn.addEventListener('click', () => this.analyze());
    },

    bindSignButton() {
        const btn = document.getElementById('btnSignXml');
        if (btn) btn.addEventListener('click', () => this.sign());
    },

    bindDownloadButton() {
        const btn = document.getElementById('downloadXmlBtn');
        if (btn) btn.addEventListener('click', () => {
            if (this.lastOutputPath) window.location.href = `/Signing/Download?path=${encodeURIComponent(this.lastOutputPath)}`;
        });
    },

    // Syncs the ReferenceId dropdown when the user manually changes ParentXPath —
    // same behavior as sign-app's cboXmlParentXPath_SelectionChanged.
    bindParentXPathSync() {
        const select = document.getElementById('xmlParentXPath');
        if (select) select.addEventListener('change', () => {
            const opt = this._parentXPathOptions.find(o => o.xPath === select.value);
            if (opt && opt.referenceId) {
                const refSelect = document.getElementById('xmlReferenceId');
                if (refSelect && [...refSelect.options].some(o => o.value === opt.referenceId)) {
                    refSelect.value = opt.referenceId;
                }
            }
        });
    },

    async uploadFile(file) {
        if (this.isUploading) return;
        this.isUploading = true;
        try {
            const formData = new FormData();
            formData.append('file', file);
            const res = await fetch('/Signing/UploadXml', { method: 'POST', body: formData });
            const data = await res.json();
            if (!res.ok || data.error) {
                Toast.error('Tải tệp thất bại', data.error || 'Không thể tải tệp XML.');
                return;
            }
            this.filePath = data.filePath;
            this.fileName = data.fileName;
            this.lastOutputPath = null;
            this._resetAnalysisOptions();

            const display = document.getElementById('xmlFileDisplay');
            if (display) {
                display.classList.add('file-input__display--loaded');
                display.innerHTML = `
                    <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#d5473e" stroke-width="1.8"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
                    <span class="file-input__name">${this.fileName}</span>`;
            }
            App.log('ok', `Đã tải tệp XML: ${this.fileName}`);
        } catch (err) {
            Toast.error('Lỗi kết nối', err.message);
        } finally {
            this.isUploading = false;
            const input = document.getElementById('xmlFileInput');
            if (input) input.value = '';
            this.updateUI();
        }
    },

    async analyze() {
        if (!this.filePath) {
            Toast.warning('Chưa chọn tệp', 'Vui lòng chọn một tệp XML trước khi phân tích.');
            return;
        }
        try {
            const res = await fetch('/Signing/AnalyzeXml', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ filePath: this.filePath })
            });
            const result = await res.json();

            (result.logs || []).forEach(entry => App.log(entry.level, entry.message));

            if (!result.success) {
                Toast.error('Phân tích thất bại', result.errorMessage || 'Không thể phân tích tệp XML.');
                return;
            }

            this._parentXPathOptions = result.parentXPaths || [];
            this._populateSelect('xmlSignTag', result.signTags, result.defaultSignTag);
            this._populateSelect('xmlParentXPath', this._parentXPathOptions.map(o => o.xPath),
                result.defaultParentXPath, this._parentXPathOptions);
            this._populateSelect('xmlReferenceId', result.referenceIds, result.defaultReferenceId);

            Toast.success('Phân tích hoàn tất', `Loại tài liệu: ${result.documentType}.`);
        } catch (err) {
            Toast.error('Lỗi kết nối', err.message);
        } finally {
            this.updateUI();
        }
    },

    _resetAnalysisOptions() {
        this._parentXPathOptions = [];
        this._populateSelect('xmlSignTag', ['']);
        this._populateSelect('xmlParentXPath', ['']);
        this._populateSelect('xmlReferenceId', ['']);
    },

    // options: plain string values. labelSource (optional): [{xPath, label}] for a friendlier label than the raw value.
    _populateSelect(elementId, values, selectedValue, labelSource) {
        const select = document.getElementById(elementId);
        if (!select) return;
        const items = values && values.length > 0 ? values : [''];
        select.innerHTML = items.map(v => {
            const labelObj = labelSource && labelSource.find(o => o.xPath === v);
            const label = labelObj ? labelObj.label : (v || '(Trống)');
            return `<option value="${this._escapeHtml(v)}">${this._escapeHtml(label)}</option>`;
        }).join('');
        if (selectedValue !== undefined && selectedValue !== null && items.includes(selectedValue)) {
            select.value = selectedValue;
        }
    },

    _escapeHtml(s) {
        return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    },

    async sign() {
        if (!App.isLoggedIn) {
            Toast.info('Cần đăng nhập', 'Vui lòng đăng nhập phiên ký trước khi thực hiện ký số.');
            return;
        }
        if (!this.filePath) {
            Toast.warning('Chưa chọn tệp', 'Vui lòng chọn một tệp XML trước khi ký.');
            return;
        }
        if (this.isSigning) return;

        const credentialId = await Signing.resolveCredentialId();
        if (credentialId === null) {
            App.log('warn', 'Đã hủy chọn chứng thư ký.');
            return;
        }

        this.isSigning = true;
        this.updateUI();
        try {
            const currentMerchantId = Session.loggedInMerchantId || App.merchantId;
            Signing.showSigningProgress(true, currentMerchantId);

            const signTag = document.getElementById('xmlSignTag')?.value || '';
            const parentXPath = document.getElementById('xmlParentXPath')?.value || '';
            const referenceUri = document.getElementById('xmlReferenceId')?.value || '';
            const signatureName = document.getElementById('xmlSignatureName')?.value || '';
            const outputDirectory = document.getElementById('xmlOutputDir')?.value || '';

            const res = await fetch('/Signing/SignXml', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    filePath: this.filePath,
                    outputDirectory: outputDirectory || null,
                    merchantId: App.merchantId,
                    credentialId,
                    signatureName: signatureName || null,
                    signTag: signTag || null,
                    parentXPath: parentXPath || null,
                    referenceUri: referenceUri || null
                })
            });
            const result = await res.json();
            Signing.showSigningProgress(false);

            if (result.success) {
                this.lastOutputPath = result.outputPath;
                App.log('ok', `Ký XML thành công. Kết quả: ${result.outputPath}`);
                Toast.success('Ký số thành công', `Đã ký tài liệu XML: ${this.fileName}`);
            } else {
                App.handleSignFailure(result);
            }
        } catch (err) {
            Signing.showSigningProgress(false);
            Toast.error('Lỗi kết nối', err.message);
            App.log('error', `Lỗi kết nối khi ký XML: ${err.message}`);
        } finally {
            this.isSigning = false;
            this.updateUI();
        }
    },

    updateUI() {
        const signBtn = document.getElementById('btnSignXml');
        if (signBtn) signBtn.disabled = !this.filePath || this.isSigning;

        const analyzeBtn = document.getElementById('btnAnalyzeXml');
        if (analyzeBtn) analyzeBtn.disabled = !this.filePath;

        const downloadBtn = document.getElementById('downloadXmlBtn');
        if (downloadBtn) downloadBtn.disabled = !this.lastOutputPath;
    }
};

document.addEventListener('DOMContentLoaded', () => XmlSigning.init());
