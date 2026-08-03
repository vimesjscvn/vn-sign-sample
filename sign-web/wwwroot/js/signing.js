/**
 * Two-stage PDF signing with Fabric.js canvas.
 * - PDF rendered as background image (from server Docnet/pdfium)
 * - AcroForm fields rendered as clickable Fabric.Rect objects
 * - User can draw custom signature boxes
 * - All coordinates in PDF points (accurate, no DOM offset issues)
 */
'use strict';

const Signing = {
    canvas: null,
    placements: {},       // fieldId → true
    signedFields: {},     // fieldId → true
    drawnBoxes: [],       // Plain PDF-coordinate placements; rendered per page as locked overlays
    fields: [],           // SignatureFieldInfo[] from server
    filePath: null,
    currentPage: 1,
    totalPages: 1,
    pdfW: 595,
    pdfH: 842,
    drawMode: false,
    canvasScale: 1,       // Scale factor: canvas pixels / PDF points
    signatureImageBase64: null,

    get placedCount() {
        return Object.keys(this.placements).length + this.drawnBoxes.length;
    },
    get canSign() {
        return this.placedCount > 0;
    },

    isSigned: false,
    isUploading: false,
    isSigning: false,
    _documentRevision: 0,
    _renderRevision: 0,

    init() {
        this.bindFileInput();
        this.bindSignButton();
        this.bindPageNav();
        this.bindSignatureImageInput();
        this.bindDownloadButton();
        this.bindAutoAcroToggle();
        this.updateUI();
    },

    bindFileInput() {
        const input = document.getElementById('pdfFileInput');
        if (input) input.addEventListener('change', async (e) => {
            const file = e.target.files[0];
            if (file) await this.uploadFile(file);
        });
    },

    bindAutoAcroToggle() {
        const toggle = document.getElementById('toggleAutoAcro');
        const track = document.getElementById('autoAcroTrack');
        if (toggle && track) {
            toggle.addEventListener('click', () => {
                track.classList.toggle('active');
                const isOn = track.classList.contains('active');
                App.log('info', `Tự tạo ô ký: ${isOn ? 'BẬT' : 'TẮT'}`);
            });
        }
    },

    // === Signature Image ===
    bindSignatureImageInput() {
        const input = document.getElementById('sigImageInput');
        if (input) input.addEventListener('change', (e) => {
            const file = e.target.files[0];
            if (file) this.loadSignatureImage(file);
        });
    },

    loadSignatureImage(file) {
        if (!file.type.startsWith('image/')) {
            Toast.warning('Tệp không hợp lệ', 'Vui lòng chọn một tệp hình ảnh.');
            return;
        }
        const reader = new FileReader();
        reader.onload = () => {
            this.signatureImageBase64 = reader.result; // data:image/...;base64,....
            const preview = document.getElementById('sigImagePreview');
            if (preview) {
                preview.innerHTML = `<img src="${this.signatureImageBase64}" style="width:100%;height:100%;object-fit:contain" />`;
                preview.classList.add('filled');
            }
            App.log('ok', `Đã chọn ảnh chữ ký: ${file.name}`);
        };
        reader.onerror = () => Toast.error('Lỗi đọc tệp', 'Không thể đọc tệp ảnh đã chọn.');
        reader.readAsDataURL(file);
    },

    // BCY-only: sign algorithm (ECDSA/RSA) — mirrors sign-app's
    // cboSignAlgorithm.IsVisible ? cboSignAlgorithm.SelectedItem?.ToString() : null.
    resolveSignAlgorithm() {
        if (App.merchantId !== 'BCY') return null;
        return document.getElementById('signAlgorithm')?.value || null;
    },

    // Strips the "data:image/...;base64," prefix — server expects raw base64, same as the PDF payload.
    stripDataUrlPrefix(dataUrl) {
        if (!dataUrl) return '';
        const comma = dataUrl.indexOf(',');
        return comma >= 0 ? dataUrl.slice(comma + 1) : dataUrl;
    },

    clearSignatureImage() {
        this.signatureImageBase64 = null;
        const input = document.getElementById('sigImageInput');
        if (input) input.value = '';
        const preview = document.getElementById('sigImagePreview');
        if (preview) {
            preview.innerHTML = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7"><rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>';
            preview.classList.remove('filled');
        }
    },

    // === Credential Picker ===
    // Resolves which credential to sign with. Returns '' if there's 0-or-1 certs (server
    // falls back to its session default), the chosen credentialId if the user picks one,
    // or null if the user cancelled the picker (caller must abort the sign attempt).
    _credentialPickerResolve: null,
    _pickerChoice: null,

    async resolveCredentialId() {
        const certs = (typeof Session !== 'undefined' && Session.certificates) || [];
        if (certs.length <= 1) {
            return (typeof Session !== 'undefined' && Session.selectedCredentialId) || '';
        }
        return new Promise((resolve) => {
            this._credentialPickerResolve = resolve;
            this.showCredentialPicker(certs);
        });
    },

    showCredentialPicker(certs) {
        this._pickerChoice = Session.selectedCredentialId || certs[0]?.credentialId || null;
        this.renderCredentialPickerList(certs);
        const modal = document.getElementById('credentialPickerModal');
        if (modal) modal.style.display = 'flex';
    },

    renderCredentialPickerList(certs) {
        const list = document.getElementById('credentialPickerList');
        if (!list) return;
        list.innerHTML = certs.map(c => {
            const cn = (typeof Session !== 'undefined' && Session.extractCN(c.subjectDN)) || c.subjectDN || c.credentialId;
            const isSel = c.credentialId === this._pickerChoice;
            return `
                <div onclick="Signing.selectCredentialInPicker('${c.credentialId}')"
                     style="display:flex;align-items:center;gap:10px;padding:10px 12px;border:1px solid ${isSel ? '#cfe0fb' : '#e7eaef'};border-radius:10px;cursor:pointer;background:${isSel ? '#eef4fe' : '#fff'}">
                    <span style="width:16px;height:16px;border-radius:50%;border:2px solid ${isSel ? 'var(--accent)' : '#cdd3dc'};display:flex;align-items:center;justify-content:center;flex:none">
                        ${isSel ? '<span style="width:8px;height:8px;border-radius:50%;background:var(--accent)"></span>' : ''}
                    </span>
                    <div style="min-width:0;flex:1">
                        <div style="font-size:13px;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${cn}</div>
                        <div style="font-size:11px;color:var(--muted);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${c.credentialId}</div>
                    </div>
                </div>`;
        }).join('');
    },

    selectCredentialInPicker(credentialId) {
        this._pickerChoice = credentialId;
        this.renderCredentialPickerList(Session.certificates || []);
    },

    confirmCredentialPicker() {
        const modal = document.getElementById('credentialPickerModal');
        if (modal) modal.style.display = 'none';
        const chosen = this._pickerChoice;
        Session.selectedCredentialId = chosen;
        App.log('ok', `Đã chọn chứng thư ký: ${Session.extractCN(Session.certificates.find(c => c.credentialId === chosen)?.subjectDN) || chosen}`);
        if (this._credentialPickerResolve) {
            this._credentialPickerResolve(chosen);
            this._credentialPickerResolve = null;
        }
    },

    cancelCredentialPicker() {
        const modal = document.getElementById('credentialPickerModal');
        if (modal) modal.style.display = 'none';
        if (this._credentialPickerResolve) {
            this._credentialPickerResolve(null);
            this._credentialPickerResolve = null;
        }
    },

    bindSignButton() {
        const btn = document.getElementById('btnSignPdf');
        if (btn) btn.addEventListener('click', () => this.attemptSign());
    },

    bindDownloadButton() {
        const btn = document.getElementById('downloadBtn');
        if (btn) {
            btn.addEventListener('click', () => {
                if (this.filePath) {
                    window.location.href = `/Pdf/Download?path=${encodeURIComponent(this.filePath)}`;
                }
            });
        }
    },

    bindPageNav() {
        const prev = document.getElementById('btnPrevPage');
        const next = document.getElementById('btnNextPage');
        if (prev) prev.addEventListener('click', () => this.prevPage());
        if (next) next.addEventListener('click', () => this.nextPage());
    },

    // === File Upload ===
    async uploadFile(file) {
        if (this.isUploading) return;
        this.isUploading = true;

        App.log('info', `Uploading: ${file.name}...`);

        const overlay = document.getElementById('loadingOverlay');
        if (overlay) overlay.style.display = 'flex';

        // Check auto-create acro field toggle state
        const autoAcroTrack = document.getElementById('autoAcroTrack');
        const autoCreateAcro = autoAcroTrack ? autoAcroTrack.classList.contains('active') : true;

        const formData = new FormData();
        formData.append('file', file);
        formData.append('autoCreateAcro', autoCreateAcro.toString());

        try {
            const res = await fetch('/Signing/UploadPdf', { method: 'POST', body: formData });
            const data = await res.json();

            this.filePath = data.filePath;
            this.fields = data.fields || [];
            this.placements = {};
            this.signedFields = {};
            this.drawnBoxes = [];
            this.isSigned = false;

            // §4.1: Auto-place if single empty field
            const emptyFields = this.fields.filter(f => !f.isSigned);
            if (emptyFields.length === 1) {
                this.placements[emptyFields[0].id] = true;
            }

            // Hide drop zone, show canvas
            document.getElementById('dropZone').style.display = 'none';
            document.getElementById('canvasContainer').style.display = 'block';

            // Render page
            this.currentPage = 1;
            await this.renderPage();

            // Update file display
            const display = document.getElementById('pdfFileDisplay');
            if (display) {
                display.classList.add('file-input__display--loaded');
                display.innerHTML = `
                    <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#d5473e" stroke-width="1.8"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
                    <span class="file-input__name">${file.name}</span>`;
            }

            this.updateUI();
            App.log('ok', `Loaded ${file.name} — ${this.fields.length} form fields detected.`);
            
            // Trigger visual alignment in the background asynchronously (only if auto-create is on)
            if (autoCreateAcro) this.triggerBackgroundVisualAlignment();
        } catch (err) {
            App.log('error', `Upload failed: ${err.message}`);
            Toast.error('Lỗi tải tệp', err.message);
        } finally {
            if (overlay) overlay.style.display = 'none';
            this.isUploading = false;
            
            // Clear input value so selecting the same file again fires change event
            const input = document.getElementById('pdfFileInput');
            if (input) input.value = '';
        }
    },

    async triggerBackgroundVisualAlignment() {
        if (!this.filePath) return;
        App.log('info', 'AI is scanning and aligning signature areas in the background...');
        
        const header = document.getElementById('placementHint');
        const textEl = header ? header.querySelector('.hint-banner__text') : null;
        const originalText = textEl ? textEl.textContent : '';
        if (textEl) {
            textEl.innerHTML = '<span style="display:inline-flex;align-items:center;gap:6px"><span class="spinner" style="width:14px;height:14px;border-width:2px;vertical-align:middle"></span> Đang chạy AI căn chỉnh vị trí ký tự động ở nền...</span>';
        }

        try {
            const res = await fetch('/Signing/VerifyAndAlignLayoutVisual', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ filePath: this.filePath })
            });
            if (!res.ok) throw new Error('Visual grounding failed');
            
            const data = await res.json();
            this.fields = data.fields || [];
            
            // Redraw fields on the current page to apply alignment adjustments visually
            this.renderFieldOverlays();
            this.updateUI();
            
            App.log('ok', 'AI layout scanning completed. Signature columns aligned!');
            Toast.success('Layout hoàn tất', 'Đã tự động căn chỉnh vị trí ký bằng AI.');
        } catch (err) {
            App.log('warn', `Visual alignment skipped/failed: ${err.message}`);
        } finally {
            if (textEl) {
                textEl.textContent = originalText || 'Bấm vào ô chữ ký có sẵn trên tài liệu, hoặc bật "Vẽ ô ký" để chọn vị trí ký.';
            }
        }
    },

    // === PDF Page Rendering ===
    async renderPage() {
        if (!this.filePath) return;

        try {
            const url = `/Pdf/RenderPage?path=${encodeURIComponent(this.filePath)}&page=${this.currentPage}`;
            const res = await fetch(url);
            if (!res.ok) throw new Error('Render failed');

            const pageW = parseInt(res.headers.get('X-Page-Width')) || 595;
            const pageH = parseInt(res.headers.get('X-Page-Height')) || 842;
            const pageCount = parseInt(res.headers.get('X-Page-Count')) || 1;

            this.pdfW = pageW;
            this.pdfH = pageH;
            this.totalPages = pageCount;

            const blob = await res.blob();
            const imgUrl = URL.createObjectURL(blob);

            // Calculate canvas size to fit container
            const container = document.getElementById('canvasContainer');
            const maxWidth = container.clientWidth || 580;
            this.canvasScale = maxWidth / pageW;
            const canvasW = Math.round(pageW * this.canvasScale);
            const canvasH = Math.round(pageH * this.canvasScale);

            // Initialize or resize Fabric canvas
            if (!this.canvas) {
                this.canvas = new fabric.Canvas('pdfCanvas', {
                    width: canvasW,
                    height: canvasH,
                    selection: false,
                    skipTargetFind: false,
                    fireRightClick: false,
                    stopContextMenu: true,
                });
                // Use Fabric's built-in drawing mode for custom boxes
                this.canvas.on('object:added', () => this.updateUI());
                this.canvas.on('mouse:down', (opt) => this.onCanvasMouseDown(opt));
                this.canvas.on('mouse:move', (opt) => this.onCanvasMouseMove(opt));
                this.canvas.on('mouse:up', (opt) => this.onCanvasMouseUp(opt));
            } else {
                this.canvas.setDimensions({ width: canvasW, height: canvasH });
                this.canvas.clear();
            }

            // Set PDF page as background
            fabric.Image.fromURL(imgUrl, (img) => {
                img.scaleToWidth(canvasW);
                this.canvas.setBackgroundImage(img, this.canvas.renderAll.bind(this.canvas));
            });

            // Render field overlays
            this.renderFieldOverlays();

            // Update page counter
            const curEl = document.getElementById('currentPage');
            const totalEl = document.getElementById('totalPages');
            if (curEl) curEl.textContent = this.currentPage;
            if (totalEl) totalEl.textContent = this.totalPages;

        } catch (err) {
            App.log('error', `Page render failed: ${err.message}`);
        }
    },

    prevPage() {
        if (this.currentPage > 1) { this.currentPage--; this.renderPage(); }
    },
    nextPage() {
        if (this.currentPage < this.totalPages) { this.currentPage++; this.renderPage(); }
    },

    // Scrolls the preview viewport so a just-signed location is actually visible —
    // after a sign, the canvas re-renders in place but the scroll position doesn't
    // follow, so newly-added content outside the current scroll can go unnoticed.
    scrollPreviewToPoint(pdfX, pdfY, pdfW, pdfH) {
        const viewport = document.getElementById('pdfViewport');
        const canvasEl = document.getElementById('pdfCanvas');
        if (!viewport || !canvasEl) return;

        const scale = this.canvasScale;
        const targetX = (pdfX + (pdfW || 0) / 2) * scale;
        const targetY = (pdfY + (pdfH || 0) / 2) * scale;

        const canvasRect = canvasEl.getBoundingClientRect();
        const viewportRect = viewport.getBoundingClientRect();

        // Canvas's position within the viewport's scrollable content, independent of
        // the current scroll offset (works regardless of the flex-centering layout).
        const canvasOffsetX = canvasRect.left - viewportRect.left + viewport.scrollLeft;
        const canvasOffsetY = canvasRect.top - viewportRect.top + viewport.scrollTop;

        viewport.scrollTo({
            left: canvasOffsetX + targetX - viewport.clientWidth / 2,
            top: canvasOffsetY + targetY - viewport.clientHeight / 2,
            behavior: 'smooth'
        });
    },

    // === Field Overlays (Fabric.Rect objects) ===
    renderFieldOverlays() {
        if (!this.canvas) return;

        // Remove old field rects (keep drawn boxes)
        const objects = this.canvas.getObjects().filter(o => o._fieldId);
        objects.forEach(o => this.canvas.remove(o));

        const scale = this.canvasScale;

        this.fields.forEach(field => {
            if (field.page && field.page !== this.currentPage) return;

            const isSigned = field.isSigned || this.signedFields[field.id];
            const isPlaced = this.placements[field.id];

            const color = isSigned ? '#0f9d6e' : (isPlaced ? '#1e5fd4' : '#d9891f');
            const bgColor = isSigned ? 'rgba(15,157,110,0.12)' : (isPlaced ? 'rgba(30,95,212,0.12)' : 'rgba(217,137,31,0.1)');

            // field.x, field.y are in screen coordinates (Y from top)
            const rect = new fabric.Rect({
                left: field.x * scale,
                top: field.y * scale,
                width: field.width * scale,
                height: field.height * scale,
                fill: bgColor,
                stroke: color,
                strokeWidth: 2,
                rx: 4, ry: 4,
                selectable: false,
                evented: !isSigned,
                hoverCursor: isSigned ? 'default' : 'pointer',
            });
            rect._fieldId = field.id;
            rect._isSigned = isSigned;
            this.canvas.add(rect);

            // Add label
            const label = new fabric.Text(
                isSigned ? `✅ ${field.name}` : (isPlaced ? `● ${field.name}` : `○ ${field.name}`),
                {
                    left: field.x * scale + 6,
                    top: field.y * scale + (field.height * scale / 2) - 6,
                    fontSize: 11,
                    fontFamily: 'IBM Plex Sans',
                    fontWeight: '600',
                    fill: color,
                    selectable: false,
                    evented: false,
                }
            );
            label._fieldId = field.id;
            label._isLabel = true;
            this.canvas.add(label);
        });

        this.canvas.renderAll();
    },

    togglePlacement(fieldId) {
        const field = this.fields.find(f => f.id === fieldId);
        if (this.signedFields[fieldId] || (field && field.isSigned)) return;
        if (this.placements[fieldId]) {
            delete this.placements[fieldId];
        } else {
            this.placements[fieldId] = true;
        }
        this.renderFieldOverlays();
        this.updateUI();
    },

    // === Canvas Mouse Events (for drawing + field clicks) ===
    _isDrawing: false,
    _drawStartPoint: null,
    _drawRect: null,

    onCanvasMouseDown(opt) {
        const target = opt.target;

        // If clicked on a field rect → show placement/signing dialog
        if (target && target._fieldId && !target._isSigned && !target._isLabel) {
            const field = this.fields.find(f => f.id === target._fieldId);
            if (!field) return;

            // Store pending placement data
            this._pendingRect = target;
            this._pendingCoords = {
                page: field.page || this.currentPage,
                x: Math.round(field.x),
                y: Math.round(field.y),
                width: Math.round(field.width),
                height: Math.round(field.height),
                fieldId: field.id
            };

            // Dynamically set label of the place button depending on whether it's already placed
            const placeBtn = document.getElementById('btnConfirmPlace');
            if (placeBtn) {
                const labelSpan = placeBtn.querySelector('span');
                if (labelSpan) {
                    const isAlreadyPlaced = this.placements[field.id];
                    labelSpan.innerHTML = isAlreadyPlaced
                        ? `Bỏ chọn ô chữ ký <span style="color:var(--muted);font-weight:400">(hủy đặt)</span>`
                        : `Đặt ô chữ ký <span style="color:var(--muted);font-weight:400">(ký sau)</span>`;
                }
            }

            // Show placement action dialog
            const coordsEl = document.getElementById('placementCoords');
            if (coordsEl) coordsEl.textContent = `Trang ${this._pendingCoords.page}, X=${this._pendingCoords.x}, Y=${this._pendingCoords.y}, W=${this._pendingCoords.width}, H=${this._pendingCoords.height}`;
            document.getElementById('placementDialog').style.display = 'flex';
            return;
        }

        // If clicked on a drawn box label → ignore (let Fabric handle selection)
        if (target && (target._isDrawnBox || target._isDrawnLabel)) return;

        // Start drawing (always allow drawing on empty canvas area)
        // When not in explicit draw mode, still allow if clicking on empty space
        if (!this.drawMode && target) return; // clicked on an object, not drawing

        const pointer = this.canvas.getPointer(opt.e);
        this._isDrawing = true;
        this._drawStartPoint = { x: pointer.x, y: pointer.y };

        this._drawRect = new fabric.Rect({
            left: pointer.x,
            top: pointer.y,
            width: 0,
            height: 0,
            fill: 'rgba(30,95,212,0.08)',
            stroke: '#1e5fd4',
            strokeWidth: 2,
            strokeDashArray: [5, 3],
            rx: 4, ry: 4,
            selectable: false,
            evented: false,
        });
        this.canvas.add(this._drawRect);
    },

    onCanvasMouseMove(opt) {
        if (!this._isDrawing || !this._drawRect || !this._drawStartPoint) return;

        const pointer = this.canvas.getPointer(opt.e);
        const left = Math.min(this._drawStartPoint.x, pointer.x);
        const top = Math.min(this._drawStartPoint.y, pointer.y);
        const width = Math.abs(pointer.x - this._drawStartPoint.x);
        const height = Math.abs(pointer.y - this._drawStartPoint.y);

        this._drawRect.set({ left, top, width, height });
        this.canvas.renderAll();
    },

    onCanvasMouseUp(opt) {
        if (!this._isDrawing || !this._drawStartPoint) return;
        this._isDrawing = false;

        const pointer = this.canvas.getPointer(opt.e);
        const left = Math.min(this._drawStartPoint.x, pointer.x);
        const top = Math.min(this._drawStartPoint.y, pointer.y);
        const w = Math.abs(pointer.x - this._drawStartPoint.x);
        const h = Math.abs(pointer.y - this._drawStartPoint.y);

        console.log('[Signing] Draw complete:', { left, top, w, h });

        // Remove the temporary drawing rect
        if (this._drawRect) {
            this.canvas.remove(this._drawRect);
            this._drawRect = null;
        }

        // Minimum size check
        if (w < 20 || h < 15) {
            this.canvas.renderAll();
            return;
        }

        // Create final rect
        const finalRect = new fabric.Rect({
            left, top, width: w, height: h,
            fill: 'rgba(30,95,212,0.08)',
            stroke: '#1e5fd4',
            strokeWidth: 2,
            strokeDashArray: [5, 3],
            rx: 4, ry: 4,
            selectable: false,
            evented: false,
        });
        this.canvas.add(finalRect);
        this.canvas.renderAll();

        // Convert canvas pixels → PDF points
        const scale = this.canvasScale;
        const pdfX = left / scale;
        const pdfY = top / scale;
        const pdfW = w / scale;
        const pdfH = h / scale;

        // Store pending placement data
        this._pendingRect = finalRect;
        this._pendingCoords = {
            page: this.currentPage,
            x: Math.round(pdfX),
            y: Math.round(pdfY),
            width: Math.round(pdfW),
            height: Math.round(pdfH)
        };

        // Show placement action dialog
        const placeBtn = document.getElementById('btnConfirmPlace');
        if (placeBtn) {
            const labelSpan = placeBtn.querySelector('span');
            if (labelSpan) {
                labelSpan.innerHTML = `Đặt ô chữ ký <span style="color:var(--muted);font-weight:400">(ký sau)</span>`;
            }
        }
        const coordsEl = document.getElementById('placementCoords');
        if (coordsEl) coordsEl.textContent = `Trang ${this._pendingCoords.page}, X=${this._pendingCoords.x}, Y=${this._pendingCoords.y}, W=${this._pendingCoords.width}, H=${this._pendingCoords.height}`;
        document.getElementById('placementDialog').style.display = 'flex';
    },

    // === Dialog Actions ===
    confirmPlace() {
        // "Đặt ô" — keep the box as placed (sign later with batch)
        const rect = this._pendingRect;
        const coords = this._pendingCoords;
        if (!rect || !coords) return;

        if (rect._fieldId) {
            // It's an AcroField. We toggle it as placed
            if (this.placements[rect._fieldId]) {
                delete this.placements[rect._fieldId];
                App.log('info', `Bỏ đặt ô ký: ${rect._fieldId}`);
            } else {
                this.placements[rect._fieldId] = true;
                App.log('ok', `Đặt ô ký: ${rect._fieldId}`);
            }
            this.renderFieldOverlays();
        } else {
            rect.set({
                strokeDashArray: null,
                fill: 'rgba(30,95,212,0.12)',
                selectable: true,
                evented: true,
                hasControls: true,
                hasBorders: true,
                cornerColor: '#1e5fd4',
                borderColor: '#1e5fd4',
            });
            rect._isDrawnBox = true;
            rect._pdfCoords = coords;

            // Add label
            const label = new fabric.Text('● Ô ký vẽ tay', {
                left: rect.left + 6,
                top: rect.top + (rect.height / 2) - 6,
                fontSize: 11,
                fontFamily: 'IBM Plex Sans',
                fontWeight: '600',
                fill: '#1e5fd4',
                selectable: false,
                evented: false,
            });
            label._isDrawnLabel = true;
            this.canvas.add(label);

            this.drawnBoxes.push(rect);
            this.canvas.renderAll();
        }

        this._closePlacementDialog();
        this.updateUI();
    },

    async confirmSignNow() {
        // "Ký ngay" — sign immediately at this position
        if (!App.isLoggedIn) {
            // Not logged in → keep the box as placed, open login flyout
            this.confirmPlace(); // places the box (blue, pending)
            Toast.info('Cần đăng nhập', 'Đã đặt ô chữ ký. Vui lòng đăng nhập để ký.');
            Session.toggleFlyout(); // open login flyout
            return;
        }

        const coords = this._pendingCoords;
        const rect = this._pendingRect;
        if (!coords || !rect) return;

        this._closePlacementDialog();

        const credentialId = await this.resolveCredentialId();
        if (credentialId === null) {
            if (rect && !rect._fieldId) {
                this.canvas.remove(rect);
                this.canvas.renderAll();
            }
            App.log('warn', 'Đã hủy chọn chứng thư ký.');
            return;
        }

        App.log('info', `Ký ngay tại: X=${coords.x}, Y=${coords.y}...`);

        try {
            // Show signing progress modal with guidelines
            const currentMerchantId = Session.loggedInMerchantId || App.merchantId;
            this.showSigningProgress(true, currentMerchantId);

            const res = await fetch('/Signing/SignPdf', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    filePath: this.filePath || '',
                    merchantId: App.merchantId,
                    credentialId,
                    signAlgorithm: this.resolveSignAlgorithm(),
                    signerName: document.getElementById('signerName')?.value || '',
                    signerTitle: document.getElementById('signerTitle')?.value || '',
                    signerPosition: document.getElementById('signerPosition')?.value || '',
                    note: document.getElementById('signNote')?.value || '',
                    showTimestamp: true,
                    signatureImageBase64: this.stripDataUrlPrefix(this.signatureImageBase64),
                    signatureType: parseInt(document.getElementById('signatureType')?.value || '0', 10),
                    displayNameMode: parseInt(document.getElementById('displayMode')?.value || '1', 10),
                    placements: [{
                        fieldId: coords.fieldId || null,
                        page: coords.page,
                        x: coords.x,
                        y: coords.y,
                        width: coords.width,
                        height: coords.height,
                        isDrawn: coords.fieldId ? false : true
                    }]
                })
            });

            const result = await res.json();
            this.showSigningProgress(false);

            if (result.success) {
                this.isSigned = true;
                if (coords.fieldId) {
                    this.signedFields[coords.fieldId] = true;
                    delete this.placements[coords.fieldId];
                }
                // Point at the newly-signed file and re-render the page from it, so the
                // canvas shows the REAL signature (name/image/timestamp) baked into the PDF
                // instead of a generic checkmark placeholder over the old unsigned page.
                if (result.outputPath) {
                    this.filePath = result.outputPath;
                    await this.renderPage();
                    this.scrollPreviewToPoint(coords.x, coords.y, coords.width, coords.height);
                } else {
                    this.canvas.renderAll();
                }

                Toast.success('Ký số thành công', `Đã ký tại vị trí X=${coords.x}, Y=${coords.y}.`);
            } else {
                // Failed — remove the rect if not AcroField
                if (rect && !rect._fieldId) {
                    this.canvas.remove(rect);
                    this.canvas.renderAll();
                }
                App.handleSignFailure(result);
            }
        } catch (err) {
            this.showSigningProgress(false);
            if (rect && !rect._fieldId) {
                this.canvas.remove(rect);
                this.canvas.renderAll();
            }
            Toast.error('Lỗi kết nối', err.message);
            App.log('error', `Lỗi kết nối khi ký: ${err.message}`);
        }

        this._pendingRect = null;
        this._pendingCoords = null;
    },

    cancelPlacement() {
        // "Hủy bỏ" — remove the drawn rect ONLY if it is not an AcroField
        if (this._pendingRect && !this._pendingRect._fieldId) {
            this.canvas.remove(this._pendingRect);
            this.canvas.renderAll();
        }
        this._closePlacementDialog();
    },

    _closePlacementDialog() {
        document.getElementById('placementDialog').style.display = 'none';
        this._pendingRect = null;
        this._pendingCoords = null;
        this._drawRect = null;
    },

    // === Draw Mode ===
    enableDrawMode(enable) {
        this.drawMode = enable;
        if (this.canvas) {
            this.canvas.defaultCursor = enable ? 'crosshair' : 'default';
            this.canvas.hoverCursor = enable ? 'crosshair' : 'pointer';
            this.canvas.renderAll();
        }
        console.log('[Signing] Draw mode:', enable);
        if (enable) {
            App.log('info', 'Chế độ vẽ ô ký: BẬT. Kéo chuột trên tài liệu để vẽ.');
        } else {
            App.log('dim', 'Chế độ vẽ ô ký: TẮT.');
        }
    },

    // === UI Update ===
    updateUI() {
        const btn = document.getElementById('btnSignPdf');
        const hint = document.getElementById('placementHint');
        const downloadBtn = document.getElementById('downloadBtn');

        if (downloadBtn) {
            downloadBtn.disabled = !this.isSigned || !this.filePath;
        }

        if (btn) {
            btn.disabled = !this.canSign;
            const label = btn.querySelector('span');
            if (label) {
                label.textContent = this.canSign
                    ? `KÝ SỐ TÀI LIỆU PDF (${this.placedCount} vị trí)`
                    : 'KÝ SỐ TÀI LIỆU PDF';
            }
        }

        if (hint) {
            const signedCount = Object.keys(this.signedFields).length;
            if (this.canSign) {
                hint.className = 'hint-banner hint-banner--active';
                hint.querySelector('span').textContent = `Đã chọn ${this.placedCount} vị trí ký. Bấm nút bên dưới để thực hiện ký số.`;
            } else if (signedCount > 0) {
                hint.className = 'hint-banner';
                hint.querySelector('span').textContent = `Đã ký ${signedCount} vị trí. Chọn thêm ô hoặc vẽ ô mới để ký tiếp.`;
            } else {
                hint.className = 'hint-banner';
                hint.querySelector('span').textContent = 'Bấm vào ô chữ ký có sẵn trên tài liệu, hoặc bật "Vẽ ô ký" để chọn vị trí ký.';
            }
        }
    },

    // === Sign ===
    async attemptSign() {
        if (!App.isLoggedIn) {
            Toast.info('Cần đăng nhập', 'Vui lòng đăng nhập phiên ký trước khi thực hiện ký số.');
            return;
        }
        if (!this.canSign) return;

        // Collect placements from fields
        const placements = Object.keys(this.placements).map(id => {
            const field = this.fields.find(f => f.id === id);
            return {
                fieldId: id,
                page: field?.page || 1,
                x: field?.x || 0,
                y: field?.y || 0,
                width: field?.width || 150,
                height: field?.height || 60,
                isDrawn: false
            };
        });

        // Collect drawn boxes
        this.drawnBoxes.forEach(rect => {
            if (rect._pdfCoords) {
                placements.push({
                    fieldId: null,
                    page: rect._pdfCoords.page,
                    x: rect._pdfCoords.x,
                    y: rect._pdfCoords.y,
                    width: rect._pdfCoords.width,
                    height: rect._pdfCoords.height,
                    isDrawn: true
                });
            }
        });

        const credentialId = await this.resolveCredentialId();
        if (credentialId === null) {
            App.log('warn', 'Đã hủy chọn chứng thư ký.');
            return;
        }

        App.log('info', `Signing ${placements.length} placement(s)...`);

        try {
            // Show signing progress modal with guidelines
            const currentMerchantId = Session.loggedInMerchantId || App.merchantId;
            this.showSigningProgress(true, currentMerchantId);

            const res = await fetch('/Signing/SignPdf', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    filePath: this.filePath || '',
                    merchantId: App.merchantId,
                    credentialId,
                    signAlgorithm: this.resolveSignAlgorithm(),
                    signerName: document.getElementById('signerName')?.value || '',
                    signerTitle: document.getElementById('signerTitle')?.value || '',
                    signerPosition: document.getElementById('signerPosition')?.value || '',
                    note: document.getElementById('signNote')?.value || '',
                    showTimestamp: true,
                    signatureImageBase64: this.stripDataUrlPrefix(this.signatureImageBase64),
                    signatureType: parseInt(document.getElementById('signatureType')?.value || '0', 10),
                    displayNameMode: parseInt(document.getElementById('displayMode')?.value || '1', 10),
                    placements
                })
            });

            const result = await res.json();
            this.showSigningProgress(false);

            if (result.success) {
                Object.keys(this.placements).forEach(id => { this.signedFields[id] = true; });
                this.placements = {};
                this.drawnBoxes = [];
                this.isSigned = true;

                // Point at the newly-signed file and re-render, so the canvas shows the
                // REAL signature(s) baked into the PDF instead of the old unsigned page.
                if (result.outputPath) {
                    this.filePath = result.outputPath;
                    await this.renderPage();
                    const first = placements[0];
                    if (first) this.scrollPreviewToPoint(first.x, first.y, first.width, first.height);
                } else {
                    this.renderFieldOverlays();
                    this.canvas.getObjects().filter(o => o._isDrawnBox || o._isDrawnLabel).forEach(o => this.canvas.remove(o));
                    this.canvas.renderAll();
                }

                this.updateUI();
                Toast.success('Ký số thành công', `Đã ký ${result.signedCount} vị trí trên tài liệu PDF.`);
            } else {
                App.handleSignFailure(result);
            }
        } catch (err) {
            this.showSigningProgress(false);
            Toast.error('Lỗi kết nối', err.message);
            App.log('error', `Lỗi kết nối khi ký: ${err.message}`);
        }
    },

    getMobileAppName(merchantId) {
        if (!merchantId) return 'ứng dụng xác thực ký số';
        const id = merchantId.toUpperCase();
        if (id.includes('VIETTEL') || id.includes('MYSIGN')) return 'Viettel MySign';
        if (id.includes('SMARTCA') || id.includes('VNPT')) return 'VNPT SmartCA';
        if (id.includes('VGCA')) return 'VGCA Sign Service';
        if (id.includes('FPT')) return 'FPT SmartCA';
        if (id.includes('MISA')) return 'MISA eSign';
        if (id.includes('CA2')) return 'SmartSign (CA2)';
        if (id.includes('BKAV')) return 'BkavCA';
        return `ứng dụng ký số (${merchantId})`;
    },

    showSigningProgress(show, merchantId) {
        const modal = document.getElementById('signingProgressDialog');
        if (!modal) return;
        if (show) {
            const appName = this.getMobileAppName(merchantId);
            const appNameEl = document.getElementById('progressMobileAppName');
            if (appNameEl) {
                appNameEl.textContent = appName;
            }
            modal.style.display = 'flex';
        } else {
            modal.style.display = 'none';
        }
    }
};

document.addEventListener('DOMContentLoaded', () => Signing.init());
