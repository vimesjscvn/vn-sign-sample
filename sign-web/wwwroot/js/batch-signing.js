/**
 * Batch signing — SELF/LOCAL merchant only (same scope as sign-app's "Ký Hàng Loạt" tab).
 * Uploads a PFX, then signs every *.pdf in a server-side source folder with one placement each.
 */
'use strict';

const BatchSigning = {
    certFilePath: null,
    certFileName: null,
    isUploading: false,
    isSigning: false,

    init() {
        this.bindCertInput();
        this.bindSignButton();
        this.updateUI();
    },

    bindCertInput() {
        const input = document.getElementById('batchCertInput');
        if (input) input.addEventListener('change', async (e) => {
            const file = e.target.files[0];
            if (file) await this.uploadCert(file);
        });
    },

    bindSignButton() {
        const btn = document.getElementById('btnBatchSign');
        if (btn) btn.addEventListener('click', () => this.sign());
    },

    async uploadCert(file) {
        if (this.isUploading) return;
        this.isUploading = true;
        try {
            const formData = new FormData();
            formData.append('file', file);
            const res = await fetch('/Signing/UploadBatchCert', { method: 'POST', body: formData });
            const data = await res.json();
            if (!res.ok || data.error) {
                Toast.error('Tải tệp thất bại', data.error || 'Không thể tải file chứng thư.');
                return;
            }
            this.certFilePath = data.filePath;
            this.certFileName = data.fileName;

            const display = document.getElementById('batchCertDisplay');
            if (display) {
                display.classList.add('file-input__display--loaded');
                display.innerHTML = `
                    <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#d5473e" stroke-width="1.8"><rect x="3" y="11" width="18" height="10" rx="2"/><circle cx="12" cy="16" r="1.5"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>
                    <span class="file-input__name">${this.certFileName}</span>`;
            }
            App.log('ok', `Đã tải chứng thư: ${this.certFileName}`);
        } catch (err) {
            Toast.error('Lỗi kết nối', err.message);
        } finally {
            this.isUploading = false;
            const input = document.getElementById('batchCertInput');
            if (input) input.value = '';
            this.updateUI();
        }
    },

    async sign() {
        const sourceDir = document.getElementById('batchSourceDir')?.value?.trim() || '';
        const outputDir = document.getElementById('batchOutputDir')?.value?.trim() || '';
        const certPass = document.getElementById('batchCertPass')?.value || '';

        if (!sourceDir) {
            Toast.warning('Thiếu thông tin', 'Vui lòng nhập thư mục nguồn hợp lệ.');
            return;
        }
        if (!this.certFilePath) {
            Toast.warning('Thiếu thông tin', 'Vui lòng chọn file chứng thư Self CA.');
            return;
        }
        if (this.isSigning) return;

        this.isSigning = true;
        this.updateUI();
        this._setStatus('Đang ký hàng loạt...', false);
        this._setProgress(0);
        document.getElementById('batchResults').innerHTML = '';

        try {
            const res = await fetch('/Signing/SignBatch', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    sourceDirectory: sourceDir,
                    outputDirectory: outputDir || `${sourceDir}\\Signed`,
                    merchantId: 'SELF',
                    pfxFilePath: this.certFilePath,
                    pfxPassword: certPass
                })
            });
            const result = await res.json();

            if (!result.success) {
                this._setStatus(result.message || 'Ký hàng loạt thất bại.', true);
                Toast.error('Ký hàng loạt thất bại', result.message || 'Đã xảy ra lỗi.');
                return;
            }

            this._setProgress(100);
            this._renderResults(result.files || []);
            this._setStatus(`Hoàn tất. Thành công ${result.successCount}/${result.total} tệp.`, result.failedCount > 0);
            App.log(result.failedCount > 0 ? 'warn' : 'ok',
                `Ký hàng loạt hoàn tất: ${result.successCount}/${result.total} thành công.`);

            if (result.successCount > 0) {
                Toast.success('Ký hàng loạt hoàn tất', `Đã ký ${result.successCount}/${result.total} tệp.`);
            } else {
                Toast.error('Ký hàng loạt thất bại', 'Không có tệp nào được ký thành công.');
            }
        } catch (err) {
            this._setStatus(`Lỗi kết nối: ${err.message}`, true);
            Toast.error('Lỗi kết nối', err.message);
        } finally {
            this.isSigning = false;
            this.updateUI();
        }
    },

    _renderResults(files) {
        const container = document.getElementById('batchResults');
        if (!container) return;
        container.innerHTML = files.map(f => {
            const ok = f.state === 2; // BatchFileState.Done
            const color = ok ? '#0f9d6e' : '#cc4238';
            const icon = ok ? '✓' : '✕';
            const detail = f.errorMessage ? ` — ${f.errorMessage}` : '';
            return `<div style="font-size:12px;color:${color}"><strong>${icon}</strong> ${f.fileName}${detail}</div>`;
        }).join('');
    },

    _setStatus(text, isError) {
        const el = document.getElementById('batchStatus');
        if (el) {
            el.textContent = text;
            el.style.color = isError ? '#cc4238' : 'var(--muted)';
        }
    },

    _setProgress(pct) {
        const bar = document.getElementById('batchProgressBar');
        if (bar) bar.style.width = `${pct}%`;
    },

    updateUI() {
        const btn = document.getElementById('btnBatchSign');
        if (btn) btn.disabled = !this.certFilePath || this.isSigning;
    }
};

document.addEventListener('DOMContentLoaded', () => BatchSigning.init());
