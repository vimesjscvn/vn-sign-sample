'use strict';

const Session = {
    credentialId: null,
    certSubject: null,
    loggedInMerchantId: null, // the merchant actually authenticated against — NOT App.merchantId,
                              // which the header "Merchant" dropdown can change independently of login
    certificates: [],         // all certificates registered under this login (may be >1)
    selectedCredentialId: null, // which one to actually sign with; defaults to certificates[0]
    selectedMerchant: 'BCY',
    _logoutPromise: null,

    // Pulls the CN= (common name) segment out of a certificate subject DN string,
    // e.g. "C=VN,L=ĐÀ NẴNG,CN=NGUYỄN THIỆN TÂM,UID=..." → "NGUYỄN THIỆN TÂM".
    extractCN(subjectDN) {
        if (!subjectDN) return null;
        const match = subjectDN.match(/CN=([^,]+)/i);
        return match ? match[1].trim() : null;
    },

    init() {
        // Session pill click is handled via inline onclick in _Layout.cshtml
        // No need to bind here — just ensure Session object is available globally
    },

    focusLoginUser() {
        const focus = () => {
            const input = document.getElementById('loginUser');
            if (input && input.offsetParent !== null) input.focus();
        };
        window.requestAnimationFrame(focus);
        window.setTimeout(focus, 0);
    },

    openLoginFlyout() {
        const flyout = document.getElementById('sessionFlyout');
        if (!flyout) return;

        // Opening the login panel is deliberately idempotent. Sign/guard paths may
        // converge here at the same time; a second call must not toggle it closed.
        if (App.isLoggedIn) {
            flyout.style.display = 'block';
            this.renderPanel();
            return;
        }

        this.selectedMerchant = this.selectedMerchant || App.merchantId;
        flyout.style.display = 'block';
        if (!document.getElementById('loginUser')) this.renderPanel();
        this.focusLoginUser();
    },

    toggleFlyout() {
        const flyout = document.getElementById('sessionFlyout');
        if (!flyout) return;
        const visible = flyout.style.display !== 'none' && flyout.style.display !== '';
        if (visible) {
            this.closeFlyout();
            return;
        }
        if (!App.isLoggedIn) {
            this.openLoginFlyout();
            return;
        }
        flyout.style.display = 'block';
        this.renderPanel();
    },

    closeFlyout() {
        const flyout = document.getElementById('sessionFlyout');
        if (flyout) flyout.style.display = 'none';
    },

    renderPanel() {
        const panel = document.getElementById('sessionPanel');
        if (!panel) return;

        if (App.isLoggedIn) {
            const name = document.getElementById('sessionLabel')?.textContent || 'User';
            const cn = this.extractCN(this.certSubject);
            const initial = (cn || name).charAt(0).toUpperCase();

            const merchant = (App.merchants || []).find(m => m.id === this.loggedInMerchantId);
            const merchantName = merchant?.name || this.loggedInMerchantId || '—';

            const infoRow = (label, value) => `
                <div style="display:flex;justify-content:space-between;align-items:baseline;gap:10px;padding:8px 0;border-bottom:1px solid #eef0f3">
                    <span style="font-size:11.5px;color:var(--muted)">${label}</span>
                    <span style="font-size:12.5px;font-weight:600;color:#1a2230;text-align:right;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:190px" title="${value}">${value}</span>
                </div>`;

            panel.innerHTML = `
                <div style="padding:18px 18px 8px;background:linear-gradient(180deg,#12233f,#0e2440);color:#fff">
                    <div style="display:flex;align-items:center;gap:11px">
                        <span style="width:40px;height:40px;border-radius:11px;background:rgba(255,255,255,.14);display:flex;align-items:center;justify-content:center;font-weight:700;font-size:16px">${initial}</span>
                        <div style="line-height:1.3">
                            <div style="font-size:14.5px;font-weight:700">${cn || name}</div>
                            <div style="display:flex;align-items:center;gap:6px;font-size:11px;color:#8fd6b0">
                                <span style="width:7px;height:7px;border-radius:50%;background:#39d98a"></span>Phiên đang hoạt động
                            </div>
                        </div>
                    </div>
                </div>
                <div style="padding:6px 18px 4px">
                    ${infoRow('Tài khoản', name)}
                    ${infoRow('Nhà cung cấp', merchantName)}
                    ${this.certSubject ? infoRow('Chứng thư ký số', cn || this.certSubject) : infoRow('Chứng thư ký số', 'Chưa có chứng thư')}
                    ${this.credentialId ? infoRow('Credential ID', this.credentialId) : ''}
                </div>
                <div style="padding:14px 18px 18px">
                    <button onclick="Session.doLogout()" style="width:100%;height:38px;border:1px solid #f3cfcb;border-radius:10px;background:#fdf1f0;font:600 12.5px 'IBM Plex Sans';color:#cc4238;cursor:pointer;display:flex;align-items:center;justify-content:center;gap:8px">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/></svg>
                        Đăng xuất
                    </button>
                </div>`;
        } else {
            // V5: Login flyout with merchant selector (radio list)
            const merchants = App.merchants || [];
            const merchantRows = merchants.map(m => {
                const isSelected = m.id === this.selectedMerchant;
                const rowBg = isSelected ? 'background:#eef4fe;border-color:#cfe0fb' : 'background:#fff;border-color:#e7eaef';
                const dotStyle = isSelected
                    ? 'width:16px;height:16px;border-radius:50%;border:2px solid var(--accent);display:flex;align-items:center;justify-content:center'
                    : 'width:16px;height:16px;border-radius:50%;border:2px solid #cdd3dc';
                const dotInner = isSelected
                    ? '<span style="width:8px;height:8px;border-radius:50%;background:var(--accent)"></span>'
                    : '';
                return `
                    <div onclick="Session.selectLoginMerchant('${m.id}')" style="display:flex;align-items:center;gap:8px;padding:9px 10px;border:1px solid;border-radius:10px;cursor:pointer;transition:background .1s;min-width:0;${rowBg}">
                        <span style="width:22px;height:22px;border-radius:6px;background:rgba(30,95,212,.1);display:flex;align-items:center;justify-content:center;font:700 10px 'IBM Plex Sans';color:var(--accent);flex:none">${m.tag}</span>
                        <span style="font-size:12.5px;font-weight:600;flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${m.name}</span>
                        <span style="${dotStyle}">${dotInner}</span>
                    </div>`;
            }).join('');

            const isLocalOrUsb = this._isLocalOrUsbMerchant(this.selectedMerchant);
            panel.innerHTML = `
                <div class="flyout__login">
                    <div class="flyout__login-header">
                        <span class="flyout__login-icon">
                            <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>
                        </span>
                        <div>
                            <div class="flyout__login-title">Đăng nhập phiên ký</div>
                            <div class="flyout__login-subtitle">Xác thực một lần cho mọi chức năng</div>
                        </div>
                    </div>
                    <label style="display:block;font-size:12px;font-weight:600;color:#5b6472;margin-bottom:7px">Nhà cung cấp ký số</label>
                    <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-bottom:14px" id="loginMerchantList">
                        ${merchantRows}
                    </div>
                    <label style="display:block;font-size:12px;font-weight:500;color:#5b6472;margin-bottom:6px">Tài khoản</label>
                    <input class="flyout__input" id="loginUser"
                        placeholder="${isLocalOrUsb ? 'Không cần nhập (tự nhận diện qua token/agent)' : 'Nhập tài khoản'}"
                        ${isLocalOrUsb ? 'disabled' : ''} />
                    <label style="display:block;font-size:12px;font-weight:500;color:#5b6472;margin-bottom:6px">Mật khẩu (PIN)</label>
                    <input class="flyout__input" id="loginPass" type="password" placeholder="••••••••" />
                    <button class="flyout__login-btn" onclick="Session.doLogin()">Đăng Nhập &amp; Bắt Đầu Phiên</button>
                </div>`;
        }
    },

    // USB/LOCAL/SELF resolve identity from the connected token/agent — no username needed.
    // Mirrors sign-app's isLocalOrUsb (MainWindow.axaml.cs), which disables + clears txtUserName.
    _isLocalOrUsbMerchant(id) {
        return ['USB', 'LOCAL', 'SELF'].includes((id || '').toUpperCase());
    },

    selectLoginMerchant(id) {
        const isLocalOrUsb = this._isLocalOrUsbMerchant(id);
        const user = isLocalOrUsb ? '' : (document.getElementById('loginUser')?.value || '');
        const pass = document.getElementById('loginPass')?.value || '';
        this.selectedMerchant = id;
        App.syncMerchantHeader(id);
        // Re-render to update radio state
        this.renderPanel();
        const userInput = document.getElementById('loginUser');
        const passInput = document.getElementById('loginPass');
        if (userInput) userInput.value = user;
        if (passInput) passInput.value = pass;
        this.focusLoginUser();
    },

    applySelectedCredential(credentialId, updateSignerName = true) {
        const cert = (this.certificates || []).find(c => c.credentialId === credentialId);
        this.selectedCredentialId = cert?.credentialId || null;
        this.credentialId = cert?.credentialId || null;
        this.certSubject = cert?.subjectDN || null;

        if (updateSignerName) {
            const signerNameInput = document.getElementById('signerName');
            const cn = this.extractCN(this.certSubject);
            if (signerNameInput && cn) signerNameInput.value = cn;
        }
        return cert || null;
    },

    markLoggedOut() {
        App.isLoggedIn = false;
        this.credentialId = null;
        this.certSubject = null;
        this.loggedInMerchantId = null;
        this.certificates = [];
        this.selectedCredentialId = null;
        App.updateSessionUI({ isLoggedIn: false });

        const signerNameInput = document.getElementById('signerName');
        if (signerNameInput) signerNameInput.value = 'Sample User';
    },

    async handleSessionExpired(message) {
        // Clear the browser state synchronously so a concurrent sign click cannot
        // start another request while the best-effort server logout is in flight.
        this.markLoggedOut();
        this.openLoginFlyout();
        Toast.warning('Phiên ký đã hết hạn', message || 'Vui lòng đăng nhập lại để tiếp tục.');
        App.log('warn', message || 'Signing session expired.');
        await this.doLogout({ openLogin: true, showToast: false });
    },

    async doLogin() {
        const user = document.getElementById('loginUser')?.value || '';
        const pass = document.getElementById('loginPass')?.value || '';
        if (!user) { Toast.warning('Thiếu thông tin', 'Vui lòng nhập tài khoản.'); return; }

        const merchantId = this.selectedMerchant || App.merchantId;

        try {
            const res = await fetch('/Session/Login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ userName: user, password: pass, merchantId })
            });
            const data = await res.json();
            if (data.success) {
                App.isLoggedIn = true;
                this.selectedMerchant = merchantId;
                this.loggedInMerchantId = merchantId;
                this.certificates = data.certificates || [];
                const initialCredentialId = data.credentialId || this.certificates[0]?.credentialId || null;
                this.applySelectedCredential(initialCredentialId);
                App.syncMerchantHeader(merchantId);
                App.updateSessionUI({ isLoggedIn: true, userName: data.userName });

                this.closeFlyout();
                Toast.success('Đăng nhập thành công', `Phiên ký đã thiết lập cho ${data.userName} (${merchantId}).`);
                App.log('ok', `Authentication Successful. Session: ${data.userName} via ${merchantId}`);

                if (this.credentialId) {
                    App.log('ok', `Đã chọn chứng thư ký: ${this.certSubject || this.credentialId}`);
                } else if (data.certWarning) {
                    Toast.warning('Chưa có chứng thư', data.certWarning);
                    App.log('warn', data.certWarning);
                } else {
                    Toast.warning('Chưa có chứng thư', 'Tài khoản không có chứng thư ký khả dụng.');
                    App.log('warn', 'Tài khoản không có chứng thư ký khả dụng.');
                }
            } else {
                Toast.error('Đăng nhập thất bại', data.error || 'Kiểm tra lại thông tin.');
                App.log('error', `Authentication Failed: ${data.error}`);
            }
        } catch (err) {
            Toast.error('Lỗi kết nối', err.message);
        }
    },

    async doLogout(options = {}) {
        const { openLogin = false, showToast = true } = options;
        this.markLoggedOut();
        if (openLogin) this.openLoginFlyout();
        else this.closeFlyout();

        if (!this._logoutPromise) {
            this._logoutPromise = fetch('/Session/Logout', { method: 'POST' })
                .catch(err => App.log('warn', `Logout request failed: ${err.message}`))
                .finally(() => { this._logoutPromise = null; });
        }
        await this._logoutPromise;

        if (showToast) {
            Toast.info('Đã đăng xuất', 'Phiên ký đã kết thúc.');
            App.log('warn', 'Session ended. Logged out.');
        }
        if (openLogin) this.openLoginFlyout();
    }
};

document.addEventListener('DOMContentLoaded', () => Session.init());
