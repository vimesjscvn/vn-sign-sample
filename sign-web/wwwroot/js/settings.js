'use strict';

/**
 * Cài Đặt SDK panel — one card per merchant (MySign, SmartCA, BCY, CMC, InTrust, SIM, USB),
 * matching sign-app's Settings tab field-for-field. Uses browser LocalStorage for client-side
 * persistence (per-browser, survives server restart) and synchronizes back to the server-side
 * HTTP Session on app boot.
 */
const SettingsPanel = {
    // merchantId -> field id map. BaseUrl doubles as SIM's "AP ID (URL)" field, matching sign-app.
    _merchants: {
        VIETTEL: { baseUrl: 'mysignUrl', profileId: 'mysignProfile', clientId: 'mysignClientId', clientSecret: 'mysignSecret' },
        VNPT: { baseUrl: 'smartcaUrl', profileId: 'smartcaProfile', clientId: 'smartcaClientId', clientSecret: 'smartcaSecret' },
        BCY: { baseUrl: 'bcyUrl', relyingParty: 'bcyRelyingParty', signAlgorithm: 'bcySignAlgorithm' },
        CMC: { baseUrl: 'cmcUrl', signingProfileId: 'cmcProfileId', keyAuth: 'cmcKeyAuth' },
        INTRUST: { baseUrl: 'intrustUrl', basicAuthorization: 'intrustBasicAuth' },
        SIM: { baseUrl: 'simApId', apPassword: 'simApPassword', msspId: 'simMsspId' },
        USB: { usbAgentIp: 'usbAgentIp', usbAgentPort: 'usbAgentPort', usbAgentExePath: 'usbAgentExePath' }
    },

    init() {
        document.querySelectorAll('#settingsPanel [data-test-url]').forEach(btn => {
            btn.addEventListener('click', () => this.testConnection(btn.dataset.testUrl, btn.dataset.testStatus));
        });

        const saveBtn = document.getElementById('btnSaveSettings');
        if (saveBtn) saveBtn.addEventListener('click', () => this.saveSettings());

        // Sync local storage overrides to server session on startup
        this.syncAllLocalStorageOverridesToServer();
    },

    onShow() {
        Object.keys(this._merchants).forEach(merchantId => this.loadMerchantDefaults(merchantId));
    },

    async syncAllLocalStorageOverridesToServer() {
        const prefix = 'VMSign:MerchantSettings:';
        for (let i = 0; i < localStorage.length; i++) {
            const key = localStorage.key(i);
            if (key && key.startsWith(prefix)) {
                try {
                    const settingsStr = localStorage.getItem(key);
                    if (settingsStr) {
                        const settings = JSON.parse(settingsStr);
                        // Sync to current server session
                        await fetch('/Settings/Save', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify(settings)
                        });
                    }
                } catch (e) {
                    console.warn('Failed to sync settings override to server for ' + key, e);
                }
            }
        }
    },

    async loadMerchantDefaults(merchantId) {
        const fields = this._merchants[merchantId];
        if (!fields) return;
        try {
            // Check LocalStorage first for persistence
            const localJson = localStorage.getItem(`VMSign:MerchantSettings:${merchantId}`);
            let data = null;
            if (localJson) {
                try { data = JSON.parse(localJson); } catch (e) {}
            }

            // Fallback to server session if not in LocalStorage
            if (!data) {
                const res = await fetch(`/Settings/Get?merchantId=${encodeURIComponent(merchantId)}`);
                data = await res.json();
            }

            const setVal = (id, v) => { const el = document.getElementById(id); if (el && v) el.value = v; };
            Object.entries(fields).forEach(([dataKey, elementId]) => setVal(elementId, data[dataKey]));
        } catch (err) {
            console.warn(`Failed to load settings for ${merchantId}:`, err);
        }
    },

    async testConnection(urlFieldId, statusFieldId) {
        const url = document.getElementById(urlFieldId)?.value?.trim() || '';
        const statusEl = document.getElementById(statusFieldId);
        const setStatus = (text, color) => { if (statusEl) { statusEl.textContent = text; statusEl.style.color = color; } };

        if (!url) {
            setStatus('Vui lòng nhập URL trước khi kiểm tra.', '#cc4238');
            return;
        }

        setStatus('Đang kiểm tra kết nối...', 'var(--muted)');
        try {
            const res = await fetch('/Settings/TestConnection', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ baseUrl: url })
            });
            const data = await res.json();
            setStatus(data.message, data.success ? '#0f9d6e' : '#cc4238');
            if (data.success) {
                Toast.success('Kết nối thành công', data.message);
                App.log('ok', `Kiểm tra kết nối OK: ${url}`);
            } else {
                Toast.error('Kết nối thất bại', data.message);
                App.log('error', `Kiểm tra kết nối thất bại: ${data.message}`);
            }
        } catch (err) {
            setStatus(err.message, '#cc4238');
            Toast.error('Lỗi kết nối', err.message);
        }
    },

    _readMerchantBody(merchantId) {
        const fields = this._merchants[merchantId];
        const getVal = id => document.getElementById(id)?.value || '';
        const body = { merchantId };
        Object.entries(fields).forEach(([dataKey, elementId]) => {
            const raw = getVal(elementId);
            body[dataKey] = dataKey === 'usbAgentPort'
                ? (raw ? parseInt(raw, 10) : null)
                : raw;
        });
        return body;
    },

    async saveSettings() {
        const merchantIds = Object.keys(this._merchants);
        let allOk = true;

        for (const merchantId of merchantIds) {
            const body = this._readMerchantBody(merchantId);
            try {
                const res = await fetch('/Settings/Save', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(body)
                });
                const data = await res.json();
                if (data.success) {
                    localStorage.setItem(`VMSign:MerchantSettings:${merchantId}`, JSON.stringify(body));
                } else {
                    allOk = false;
                    App.log('error', `Lưu cài đặt ${merchantId} thất bại: ${data.message || ''}`);
                }
            } catch (err) {
                allOk = false;
                App.log('error', `Lỗi khi lưu cài đặt ${merchantId}: ${err.message}`);
            }
        }

        if (allOk) {
            Toast.success('Đã lưu cài đặt', 'Cấu hình nhà cung cấp ký số đã được lưu cho trình duyệt này.');
            App.log('ok', 'Đã lưu cài đặt cho tất cả merchant (localStorage).');
        } else {
            Toast.error('Lưu chưa hoàn tất', 'Một số merchant lưu thất bại — xem Nhật ký hệ thống.');
        }
    }
};

document.addEventListener('DOMContentLoaded', () => SettingsPanel.init());
