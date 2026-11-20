(function () {
    let scanner = null;
    let isRunning = false;

    window.barcodeBridge = {
        start: async function (elementId, dotNetRef) {
            await this.stop();
            if (typeof Html5Qrcode === 'undefined') {
                throw new Error('Barcode scanner library not loaded.');
            }
            scanner = new Html5Qrcode(elementId);
            isRunning = true;
            try {
                await scanner.start(
                    { facingMode: 'environment' },
                    {
                        fps: 10,
                        qrbox: { width: 250, height: 150 },
                        aspectRatio: 1.5
                    },
                    async (decodedText) => {
                        if (!isRunning) return;
                        isRunning = false;
                        await window.barcodeBridge.stop();
                        await dotNetRef.invokeMethodAsync('OnBarcodeScanned', decodedText);
                    },
                    () => { /* ignore per-frame scan misses */ }
                );
            } catch (err) {
                isRunning = false;
                throw err;
            }
        },

        stop: async function () {
            isRunning = false;
            if (scanner) {
                try {
                    if (scanner.isScanning) {
                        await scanner.stop();
                    }
                    scanner.clear();
                } catch {
                    // ignore cleanup errors
                }
                scanner = null;
            }
        }
    };

    window.fileDownloadBridge = {
        downloadBase64: function (fileName, contentType, base64) {
            const link = document.createElement('a');
            link.download = fileName;
            link.href = `data:${contentType};base64,${base64}`;
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
        }
    };

    window.themeBridge = {
        apply: function (mode) {
            const theme = mode === 'dark' ? 'dark' : 'light';
            document.documentElement.setAttribute('data-theme', theme);
            try { localStorage.setItem('ct-theme', theme); } catch { }
            const meta = document.querySelector('meta[name="theme-color"]');
            if (meta) meta.setAttribute('content', theme === 'dark' ? '#0f172a' : '#16a34a');
        }
    };

    // Apply cached theme immediately on script load to avoid flash
    try {
        const saved = localStorage.getItem('ct-theme');
        if (saved === 'dark') {
            window.themeBridge.apply('dark');
        }
    } catch { }
})();
