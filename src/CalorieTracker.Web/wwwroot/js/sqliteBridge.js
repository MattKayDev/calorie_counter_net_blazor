(function () {
    const IDB_NAME = 'CalorieTrackerSqliteDB';
    const IDB_STORE = 'files';
    const IDB_KEY = 'calorietracker.sqlite';

    let SQL = null;
    let db = null;
    let initPromise = null;

    function openIdb() {
        return new Promise((resolve, reject) => {
            const req = indexedDB.open(IDB_NAME, 1);
            req.onupgradeneeded = () => {
                const idb = req.result;
                if (!idb.objectStoreNames.contains(IDB_STORE)) {
                    idb.createObjectStore(IDB_STORE);
                }
            };
            req.onsuccess = () => resolve(req.result);
            req.onerror = () => reject(req.error);
        });
    }

    async function loadFromIdb() {
        const idb = await openIdb();
        return new Promise((resolve, reject) => {
            const tx = idb.transaction(IDB_STORE, 'readonly');
            const req = tx.objectStore(IDB_STORE).get(IDB_KEY);
            req.onsuccess = () => resolve(req.result || null);
            req.onerror = () => reject(req.error);
        });
    }

    async function saveToIdb(uint8Array) {
        const idb = await openIdb();
        return new Promise((resolve, reject) => {
            const tx = idb.transaction(IDB_STORE, 'readwrite');
            tx.objectStore(IDB_STORE).put(uint8Array, IDB_KEY);
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    }

    function normalizeParams(params) {
        if (!params || params.length === 0) return [];
        return params.map(p => (p === undefined ? null : p));
    }

    function uint8ToBase64(bytes) {
        let binary = '';
        const chunkSize = 0x8000;
        for (let i = 0; i < bytes.length; i += chunkSize) {
            const chunk = bytes.subarray(i, i + chunkSize);
            binary += String.fromCharCode.apply(null, chunk);
        }
        return btoa(binary);
    }

    function base64ToUint8(base64) {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    }

    window.sqliteBridge = {
        init: function () {
            if (initPromise) return initPromise;
            initPromise = (async () => {
                if (!SQL) {
                    SQL = await window.initSqlJs({
                        locateFile: file => `lib/sqljs/${file}`
                    });
                }
                const savedBytes = await loadFromIdb();
                if (savedBytes && savedBytes.length > 0) {
                    db = new SQL.Database(new Uint8Array(savedBytes));
                } else {
                    db = new SQL.Database();
                    await saveToIdb(db.export());
                }
            })();
            return initPromise;
        },

        execute: async function (sql, params) {
            await this.init();
            const stmt = db.prepare(sql);
            try {
                const bound = normalizeParams(params);
                if (bound.length > 0) stmt.bind(bound);
                stmt.step();
            } finally {
                stmt.free();
            }
            await saveToIdb(db.export());
        },

        query: async function (sql, params) {
            await this.init();
            const stmt = db.prepare(sql);
            const rows = [];
            try {
                const bound = normalizeParams(params);
                if (bound.length > 0) stmt.bind(bound);
                while (stmt.step()) {
                    rows.push(stmt.getAsObject());
                }
            } finally {
                stmt.free();
            }
            return rows;
        },

        exportBase64: async function () {
            await this.init();
            return uint8ToBase64(db.export());
        },

        importBase64: async function (base64) {
            await this.init();
            const bytes = base64ToUint8(base64);
            const nextDb = new SQL.Database(bytes);
            if (db) db.close();
            db = nextDb;
            await saveToIdb(db.export());
        }
    };
})();
