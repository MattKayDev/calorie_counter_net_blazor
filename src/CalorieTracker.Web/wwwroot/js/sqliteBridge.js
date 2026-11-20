(function () {
    const IDB_NAME = 'CalorieTrackerSqliteDB';
    const IDB_STORE = 'files';
    const LEGACY_KEY = 'calorietracker.sqlite';

    let SQL = null;
    const dbs = {};
    const initPromises = {};
    const saveTimers = {};
    const dirtyDbs = new Set();

    async function flushSave(name) {
        if (saveTimers[name]) {
            clearTimeout(saveTimers[name]);
            delete saveTimers[name];
        }
        dirtyDbs.delete(name);
        if (!dbs[name]) return;
        await saveToIdb(storageKey(name), dbs[name].export());
    }

    function scheduleSave(name) {
        dirtyDbs.add(name);
        if (saveTimers[name]) {
            clearTimeout(saveTimers[name]);
        }
        saveTimers[name] = setTimeout(() => {
            flushSave(name).catch(() => { });
        }, 120);
    }

    function flushAllDirty() {
        for (const name of Array.from(dirtyDbs)) {
            flushSave(name).catch(() => { });
        }
    }

    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') {
            flushAllDirty();
        }
    });
    window.addEventListener('pagehide', flushAllDirty);

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

    async function loadFromIdb(key) {
        const idb = await openIdb();
        return new Promise((resolve, reject) => {
            const tx = idb.transaction(IDB_STORE, 'readonly');
            const req = tx.objectStore(IDB_STORE).get(key);
            req.onsuccess = () => resolve(req.result || null);
            req.onerror = () => reject(req.error);
        });
    }

    async function saveToIdb(key, uint8Array) {
        const idb = await openIdb();
        return new Promise((resolve, reject) => {
            const tx = idb.transaction(IDB_STORE, 'readwrite');
            tx.objectStore(IDB_STORE).put(uint8Array, key);
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    }

    function storageKey(dbName) {
        return `${dbName || 'personal'}.sqlite`;
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
        init: function (dbName) {
            const name = dbName || 'personal';
            if (initPromises[name]) return initPromises[name];

            initPromises[name] = (async () => {
                if (!SQL) {
                    SQL = await window.initSqlJs({
                        locateFile: file => `lib/sqljs/${file}`
                    });
                }

                const key = storageKey(name);
                let savedBytes = await loadFromIdb(key);

                // Seamless upgrade: if opening 'personal' for the first time and legacy single-DB exists, load it.
                if ((!savedBytes || savedBytes.length === 0) && name === 'personal') {
                    const legacyBytes = await loadFromIdb(LEGACY_KEY);
                    if (legacyBytes && legacyBytes.length > 0) {
                        savedBytes = legacyBytes;
                        await saveToIdb(key, new Uint8Array(legacyBytes));
                    }
                }

                if (savedBytes && savedBytes.length > 0) {
                    dbs[name] = new SQL.Database(new Uint8Array(savedBytes));
                } else {
                    dbs[name] = new SQL.Database();
                    await saveToIdb(key, dbs[name].export());
                }
            })();

            return initPromises[name];
        },

        execute: async function (dbName, sql, params) {
            const name = dbName || 'personal';
            await this.init(name);
            const db = dbs[name];
            const stmt = db.prepare(sql);
            try {
                const bound = normalizeParams(params);
                if (bound.length > 0) stmt.bind(bound);
                stmt.step();
            } finally {
                stmt.free();
            }
            scheduleSave(name);
        },

        query: async function (dbName, sql, params) {
            const name = dbName || 'personal';
            await this.init(name);
            const db = dbs[name];
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

        exportBase64: async function (dbName) {
            const name = dbName || 'personal';
            await this.init(name);
            return uint8ToBase64(dbs[name].export());
        },

        importBase64: async function (dbName, base64) {
            const name = dbName || 'personal';
            await this.init(name);
            const bytes = base64ToUint8(base64);
            const nextDb = new SQL.Database(bytes);
            if (dbs[name]) dbs[name].close();
            dbs[name] = nextDb;
            await flushSave(name);
        }
    };
})();
