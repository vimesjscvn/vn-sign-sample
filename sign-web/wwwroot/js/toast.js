/**
 * Toast notification system.
 * Auto-dismiss after 4.2s (matching V4 mockup behavior).
 */
'use strict';

const Toast = {
    show(type, title, message) {
        const container = document.getElementById('toastContainer');
        if (!container) return;

        const toast = document.createElement('div');
        toast.className = `toast toast--${type}`;
        toast.innerHTML = `
            <div style="display:flex;flex-direction:column;gap:2px;flex:1">
                <strong style="font-size:13.5px;color:#16233a">${title}</strong>
                <span style="font-size:12px;color:#5b6472">${message}</span>
            </div>
            <button onclick="this.parentElement.remove()" style="border:none;background:none;cursor:pointer;color:#8a93a1;font-size:18px">&times;</button>
        `;

        container.appendChild(toast);

        // Auto-dismiss after 4.2s
        setTimeout(() => {
            if (toast.parentElement) {
                toast.style.opacity = '0';
                toast.style.transform = 'translateY(-10px)';
                toast.style.transition = 'opacity .3s, transform .3s';
                setTimeout(() => toast.remove(), 300);
            }
        }, 4200);

        App.log(type === 'error' ? 'error' : (type === 'warning' ? 'warn' : 'ok'), `${title}: ${message}`);
    },

    success(title, msg) { this.show('success', title, msg); },
    error(title, msg) { this.show('error', title, msg); },
    warning(title, msg) { this.show('warning', title, msg); },
    info(title, msg) { this.show('info', title, msg); }
};
