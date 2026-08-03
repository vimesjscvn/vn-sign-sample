'use strict';

const App = {
    currentMode: 'pdf',
    merchantId: 'BCY',
    isLoggedIn: false,
    pos: 'sig',

    async init() {
        this.bindToggle();
        this.bindMerchant();
        await this.loadMerchants();
        await this.checkSession();
        this.log('info', 'Dashboard Initialized. Welcome to Vimes SignSDK Studio!');
    },

    // === Mode switching ===
    switchMode(mode) {
        this.currentMode = mode;
        const titles = {
            pdf: ['Ký PDF', 'Chọn tệp và cấu hình chữ ký'],
            xml: ['Ký XML', 'Ký số tài liệu XML'],
            batch: ['Ký Hàng Loạt', 'Ký nhiều tệp cùng lúc'],
            settings: ['Cài Đặt SDK', 'Cấu hình nhà cung cấp ký số'],
        };
        const [title, caption] = titles[mode] || titles.pdf;
        const el = (id) => document.getElementById(id);
        if (el('ctxTitle')) el('ctxTitle').textContent = title;
        if (el('ctxCaption')) el('ctxCaption').textContent = caption;

        const panelIds = { pdf: 'pdfPanel', xml: 'xmlPanel', batch: 'batchPanel', settings: 'settingsPanel' };
        const targetId = panelIds[mode];
        if (targetId) {
            Object.values(panelIds).forEach(id => {
                const panel = document.getElementById(id);
                if (panel) panel.style.display = (id === targetId) ? '' : 'none';
            });
            if (mode === 'settings' && typeof SettingsPanel !== 'undefined') SettingsPanel.onShow();
        }

        // The PDF preview/canvas column is meaningless outside PDF mode — hide it so the
        // logs panel isn't left floating next to a stale/empty "no document" drop zone.
        const previewEl = document.querySelector('.preview');
        if (previewEl) previewEl.style.display = (mode === 'pdf') ? '' : 'none';

        this.log('dim', `Switched context view to: ${title}`);
    },

    // === Function nav dropdown (Ký PDF / Ký XML) ===
    toggleFunctionMenu() {
        const dd = document.getElementById('functionDropdown');
        if (!dd) return;
        dd.style.display = (dd.style.display !== 'none') ? 'none' : 'block';
    },
    closeFunctionMenu() {
        const dd = document.getElementById('functionDropdown');
        if (dd) dd.style.display = 'none';
    },
    selectFunction(mode) {
        this.switchMode(mode);
        this.closeFunctionMenu();
    },

    // === Merchant ===
    merchants: [],
    bindMerchant() {
        const btn = document.getElementById('merchantBtn');
        if (btn) btn.addEventListener('click', () => this.toggleMerchantMenu());
    },
    async loadMerchants() {
        try {
            const res = await fetch('/Merchant/List');
            this.merchants = await res.json();
            // Set default to first merchant if available
            if (this.merchants.length > 0) {
                const requestedId = (typeof Session !== 'undefined' && Session.selectedMerchant)
                    || this.merchantId;
                const first = this.merchants.find(m => m.id === requestedId) || this.merchants[0];
                this.syncMerchantHeader(first.id);
            }
        } catch (e) {
            console.warn('Failed to load merchants:', e);
        }
    },
    syncMerchantHeader(id) {
        const merchant = this.merchants.find(m => m.id === id);
        if (!merchant) return false;
        this.merchantId = merchant.id;
        const nameEl = document.getElementById('merchantName');
        if (nameEl) nameEl.textContent = merchant.name;
        if (typeof Session !== 'undefined') Session.selectedMerchant = merchant.id;

        // BCY-only: sign algorithm (ECDSA/RSA) selector — mirrors sign-app's
        // cboSignAlgorithm.IsVisible = isBcy in MainWindow.axaml.cs.
        const algPanel = document.getElementById('panelSignAlgorithm');
        if (algPanel) algPanel.style.display = (merchant.id === 'BCY') ? '' : 'none';

        return true;
    },
    toggleMerchantMenu() {
        if (this.isLoggedIn) {
            Toast.info('Merchant đã khóa', 'Đăng xuất để chọn merchant khác — merchant hiện tại gắn với phiên đăng nhập.');
            return;
        }
        const dd = document.getElementById('merchantDropdown');
        if (!dd) return;
        const visible = dd.style.display !== 'none';
        dd.style.display = visible ? 'none' : 'block';
        if (!visible) this.renderMerchantList();
    },
    closeMerchantMenu() {
        const dd = document.getElementById('merchantDropdown');
        if (dd) dd.style.display = 'none';
    },
    renderMerchantList() {
        const list = document.getElementById('merchantList');
        if (!list) return;
        list.innerHTML = this.merchants.map(m => `
            <div class="dropdown__item ${m.id === this.merchantId ? 'active' : ''}" onclick="App.selectMerchant('${m.id}', '${m.name}')">
                <span style="width:28px;height:28px;border-radius:8px;background:rgba(30,95,212,.1);display:flex;align-items:center;justify-content:center;font:700 12px 'IBM Plex Sans';color:var(--accent)">${m.tag}</span>
                <div style="flex:1">
                    <div style="font-size:13px;font-weight:600">${m.name}</div>
                    <div style="font-size:11px;color:var(--muted)">${m.description}</div>
                </div>
                ${m.id === this.merchantId ? '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="var(--accent)" stroke-width="2.4"><polyline points="20 6 9 17 4 12"/></svg>' : ''}
            </div>
        `).join('');
    },
    selectMerchant(id, name) {
        if (this.isLoggedIn) {
            this.toggleMerchantMenu();
            return;
        }
        this.syncMerchantHeader(id);
        this.closeMerchantMenu();
        const m = this.merchants.find(x => x.id === id);
        Toast.info('Đã chuyển merchant', m?.switchNote || `Đang sử dụng ${name}.`);
        this.log('info', `Merchant switched to: ${name}`);
    },

    // === Position mode ===
    setPos(mode) {
        if (mode === 'note') {
            this.showNotePlacementUnavailable();
            mode = 'sig';
        }
        this.pos = mode;
        document.getElementById('posSig')?.classList.toggle('active', mode === 'sig');
        document.getElementById('posNote')?.classList.toggle('active', mode === 'note');

        // Enable draw mode when "Vẽ ô chữ ký" is selected
        if (typeof Signing !== 'undefined') Signing.enableDrawMode(mode === 'sig');
    },
    showNotePlacementUnavailable() {
        Toast.info(
            'Chưa hỗ trợ ghi chú độc lập',
            'Backend hiện chỉ hỗ trợ vùng chữ ký. Ghi chú vẫn có thể nhập trong thông tin chữ ký.'
        );
    },

    // === Timestamp toggle ===
    bindToggle() {
        const toggle = document.getElementById('toggleTimestamp');
        if (toggle) {
            toggle.addEventListener('click', () => {
                const track = document.getElementById('tsTrack');
                track.classList.toggle('active');
                toggle.setAttribute('aria-checked', track.classList.contains('active') ? 'true' : 'false');
            });
        }
    },

    // === Session ===
    async checkSession() {
        try {
            const res = await fetch('/Session/Status');
            const data = await res.json();
            this.isLoggedIn = data.isLoggedIn;
            if (data.isLoggedIn && typeof Session !== 'undefined') {
                Session.loggedInMerchantId = data.merchantId || null;
                Session.certificates = data.certificates || [];
                Session.selectedMerchant = data.merchantId || Session.selectedMerchant;
                const credentialId = data.credentialId || Session.certificates[0]?.credentialId || null;
                Session.applySelectedCredential(credentialId);
            }
            this.updateSessionUI(data);
        } catch (e) { /* ignore */ }
    },
    updateSessionUI(data) {
        const pill = document.getElementById('sessionPill');
        const label = document.getElementById('sessionLabel');
        const merchantBtn = document.getElementById('merchantBtn');
        if (!pill) return;
        if (data.isLoggedIn) {
            pill.classList.add('logged-in');
            if (label) label.textContent = data.userName || 'Đã đăng nhập';

            // Lock the merchant switcher to whatever merchant this session actually
            // authenticated with — it must not be independently browsable while logged in.
            const loggedInId = (typeof Session !== 'undefined' && Session.loggedInMerchantId) || this.merchantId;
            this.syncMerchantHeader(loggedInId);
            this.closeMerchantMenu();
            if (merchantBtn) merchantBtn.classList.add('locked');
        } else {
            pill.classList.remove('logged-in');
            if (label) label.textContent = 'Chưa đăng nhập';
            if (merchantBtn) merchantBtn.classList.remove('locked');
            if (typeof Session !== 'undefined') this.syncMerchantHeader(Session.selectedMerchant);
        }
    },

    // === Config Guard ===
    showGuide(msg) {
        const modal = document.getElementById('configGuardModal');
        const msgEl = document.getElementById('configGuardMsg');
        if (msgEl) msgEl.textContent = msg;
        if (modal) modal.style.display = 'flex';
    },
    closeGuide() {
        const modal = document.getElementById('configGuardModal');
        if (modal) modal.style.display = 'none';
    },
    gotoSettings() {
        this.closeGuide();
        this.switchMode('settings');
    },

    // Displays a failed sign/guard response the right way depending on why it failed:
    // NeedsConfig → config guard modal, NeedsLogin → login flyout, otherwise a plain error toast.
    // Returns the message shown, so callers can still branch on it (e.g. session-expiry re-login).
    handleSignFailure(result) {
        const detail = result.errorMessage || result.message || 'Đã xảy ra lỗi không xác định.';
        if (result.guardStatus === 'NeedsConfig') {
            this.showGuide(detail);
            this.log('warn', detail);
            return detail;
        }
        if (result.guardStatus === 'NeedsLogin') {
            Toast.info('Cần đăng nhập', detail);
            if (typeof Session !== 'undefined') Session.toggleFlyout();
            this.log('warn', detail);
            return detail;
        }
        Toast.error('Ký số thất bại', detail);
        this.log('error', `Ký số thất bại: ${detail}`);
        if (detail.includes('đăng nhập lại') || detail.includes('hết hạn')) {
            if (typeof Session !== 'undefined') {
                Session.doLogout();
                Session.toggleFlyout();
            }
        }
        return detail;
    },

    // === Logs ===
    log(level, message) {
        const body = document.getElementById('logsBody');
        const countEl = document.getElementById('logCount');
        if (!body) return;
        const time = new Date().toLocaleTimeString('vi-VN', { hour12: false });
        const colors = { info: '#6fb0ff', ok: '#39d98a', warn: '#f5b731', error: '#ff6b6b', dim: '#6c86a8' };
        const line = document.createElement('div');
        line.style.color = colors[level] || colors.info;
        line.textContent = `[${time}] ${message}`;
        body.appendChild(line);
        body.scrollTop = body.scrollHeight;
        if (countEl) countEl.textContent = `${body.children.length} dòng`;
    },
    copyLogs() {
        const body = document.getElementById('logsBody');
        if (body) navigator.clipboard?.writeText(body.innerText);
        Toast.info('Đã sao chép', 'Nhật ký đã được copy vào clipboard.');
    },
    clearLogs() {
        const body = document.getElementById('logsBody');
        if (body) body.innerHTML = '';
        const countEl = document.getElementById('logCount');
        if (countEl) countEl.textContent = '0 dòng';
    }
};

document.addEventListener('DOMContentLoaded', () => App.init());
