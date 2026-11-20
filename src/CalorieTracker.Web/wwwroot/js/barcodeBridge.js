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

    const ACCENT_META_COLORS = {
        'royal-blue': '#2563eb',
        'green': '#16a34a',
        'purple': '#7c3aed',
        'orange': '#ea580c',
        'red': '#dc2626',
        'teal': '#0d9488',
        'rose': '#db2777',
        'indigo': '#4f46e5'
    };

    window.themeBridge = {
        apply: function (mode, accent) {
            const theme = mode === 'dark' ? 'dark' : 'light';
            const scheme = ACCENT_META_COLORS[accent] ? accent : 'royal-blue';
            document.documentElement.setAttribute('data-theme', theme);
            document.documentElement.setAttribute('data-accent', scheme);
            try {
                localStorage.setItem('ct-theme', theme);
                localStorage.setItem('ct-accent', scheme);
            } catch { }
            const meta = document.querySelector('meta[name="theme-color"]');
            if (meta) meta.setAttribute('content', theme === 'dark' ? '#0f172a' : ACCENT_META_COLORS[scheme]);
        }
    };

    // Apply cached theme & accent immediately on script load to avoid flash
    try {
        const savedTheme = localStorage.getItem('ct-theme') || 'light';
        const savedAccent = localStorage.getItem('ct-accent') || 'royal-blue';
        window.themeBridge.apply(savedTheme, savedAccent);
    } catch { }

    window.pwaUpdateBridge = {
        checkForUpdate: async function () {
            if (!('serviceWorker' in navigator)) {
                return { updateAvailable: false, isSupported: false, message: 'Service workers are not supported in this browser.' };
            }

            try {
                const reg = await navigator.serviceWorker.getRegistration();
                if (!reg) {
                    return { updateAvailable: false, isSupported: false, message: 'Offline service worker is not active yet.' };
                }

                // If an update is already waiting, report immediately
                if (reg.waiting) {
                    return { updateAvailable: true, isSupported: true, message: 'A new version is downloaded and ready to install!' };
                }

                await reg.update();

                if (reg.waiting) {
                    return { updateAvailable: true, isSupported: true, message: 'A new version is downloaded and ready to install!' };
                }

                if (reg.installing) {
                    const worker = reg.installing;
                    const becameInstalled = await new Promise((resolve) => {
                        const timer = setTimeout(() => resolve(false), 8000);
                        worker.addEventListener('statechange', () => {
                            if (worker.state === 'installed' && navigator.serviceWorker.controller) {
                                clearTimeout(timer);
                                resolve(true);
                            } else if (worker.state === 'redundant') {
                                clearTimeout(timer);
                                resolve(false);
                            }
                        });
                    });

                    if (becameInstalled || reg.waiting) {
                        return { updateAvailable: true, isSupported: true, message: 'A new version is downloaded and ready to install!' };
                    }
                }

                return { updateAvailable: false, isSupported: true, message: 'You are up to date with the latest version.' };
            } catch (err) {
                return { updateAvailable: false, isSupported: true, message: 'Could not check for updates right now (offline or network error).' };
            }
        },

        applyUpdate: async function () {
            if (!('serviceWorker' in navigator)) {
                window.location.reload();
                return;
            }

            const reg = await navigator.serviceWorker.getRegistration();
            if (reg && reg.waiting) {
                let reloaded = false;
                navigator.serviceWorker.addEventListener('controllerchange', () => {
                    if (!reloaded) {
                        reloaded = true;
                        window.location.reload();
                    }
                });
                reg.waiting.postMessage({ type: 'SKIP_WAITING' });
                setTimeout(() => {
                    if (!reloaded) {
                        reloaded = true;
                        window.location.reload();
                    }
                }, 1000);
            } else {
                window.location.reload();
            }
        }
    };
})();
