/**
 * Drag & drop file support with visual feedback (V4 mockup spec).
 */
'use strict';

const DragDrop = {
    init() {
        const dropZone = document.getElementById('dropZone');
        if (!dropZone) return;

        ['dragenter', 'dragover'].forEach(evt => {
            dropZone.addEventListener(evt, (e) => {
                e.preventDefault();
                dropZone.classList.add('drag-over');
            });
        });

        ['dragleave', 'drop'].forEach(evt => {
            dropZone.addEventListener(evt, (e) => {
                e.preventDefault();
                dropZone.classList.remove('drag-over');
            });
        });

        dropZone.addEventListener('drop', (e) => {
            const files = e.dataTransfer?.files;
            if (!files || files.length === 0) return;

            const file = files[0];
            if (file.name.endsWith('.pdf')) {
                App.log('info', `Dropped PDF: ${file.name}. Loading...`);
                Signing.uploadFile(file);
            } else if (file.name.endsWith('.xml')) {
                App.log('info', `Dropped XML: ${file.name}.`);
                // TODO: switch to XML mode and load
            } else {
                Toast.warning('Định dạng không hỗ trợ', 'Chỉ chấp nhận file .pdf hoặc .xml');
            }
        });
    }
};

document.addEventListener('DOMContentLoaded', () => DragDrop.init());
