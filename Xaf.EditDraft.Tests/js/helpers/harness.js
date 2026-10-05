// Test harness for the Xaf.EditDraft client-side input journal (milestone M1). It mirrors the host application's jsdom
// harness pattern: jsdom pages, the REAL module under test, everything it touches observable.
//
// Under test is Xaf.EditDraft.Blazor/wwwroot/edit-draft-journal.js, imported unchanged through a data: URL (an ES module
// imported from these CommonJS tests). JOURNAL_JS=<path> runs the same tests against another copy (mutation checks).
//
// Each "tab" is its own jsdom window with its own journal (createJournal) over ONE shared storage, as two tabs of one
// browser share localStorage. A write in one tab queues a 'storage' event for every OTHER tab; the test delivers them
// with tab.deliver(), so races between a write and the other tab's reaction can be staged. Timers come from one fake
// clock (clock.advance); microtasks are real, so MutationObserver ordering is jsdom's (the observer callback sees the old
// input value, a microtask queued from it sees the new one, as measured in Chrome in M0 G2).
'use strict';

const { JSDOM, VirtualConsole } = require('jsdom');
const fs = require('fs');
const path = require('path');

const MODULE_PATH = process.env.JOURNAL_JS
    || path.join(__dirname, '..', '..', '..', 'Xaf.EditDraft.Blazor', 'wwwroot', 'edit-draft-journal.js');
let loaded = null;

function loadModule() {
    if (!loaded) {
        const src = fs.readFileSync(MODULE_PATH, 'utf8');
        loaded = import('data:text/javascript;base64,' + Buffer.from(src, 'utf8').toString('base64'));
    }
    return loaded;
}

async function flush() {
    for (let i = 0; i < 6; i++) await new Promise(r => setImmediate(r));
}

function named(name) {
    const e = new Error(name);
    e.name = name;
    return e;
}

class Clock {
    constructor(start) {
        this.now = start || Date.UTC(2026, 9, 4, 9, 0, 0);
        this.timers = [];
        this.id = 0;
        this.setTimeout = (fn, ms) => { const id = ++this.id; this.timers.push({ id, at: this.now + (ms || 0), fn }); return id; };
    }
    async advance(ms) {
        const end = this.now + (ms || 0);
        for (; ;) {
            this.timers.sort((a, b) => a.at - b.at || a.id - b.id);
            const t = this.timers[0];
            if (!t || t.at > end) break;
            this.timers.shift();
            this.now = t.at;
            t.fn();
            await flush();
        }
        this.now = end;
        await flush();
    }
}

class SharedStorage {
    constructor() {
        this.map = new Map();
        this.failSets = 0;          // the next N setItem calls throw QuotaExceededError
        this.failOps = null;        // { ops: ['length', 'key', 'getItem', 'setItem', 'removeItem'] | 'all', name }
        // Added for M1b cluster A (all off by default, so the M1 tests run exactly as before):
        this.failKeys = [];         // [{ prefix, ops: [...] | 'all', name }]: only calls on keys starting with prefix throw
        this.capacity = null;       // total characters (keys + values) the store holds; a setItem beyond it throws QuotaExceededError
        this.silentUnchanged = false;   // browsers raise no 'storage' event when setItem stores the value already there
        this.afterGet = null;       // (key, value, tabName) => void: runs inside a getItem, between two storage calls of the module
        this.beforeSet = null;      // (key, value, tabName) => void: runs inside a setItem, before the value is stored
        this.afterSet = null;       // (key, value, tabName) => void: runs inside a setItem, after the value is stored
        this.readFilter = null;     // (tabName, key, value) => value: what one tab's getItem returns (a stale view)
        this.tabs = [];
        this.log = [];
    }
    check(op, key) {
        const f = this.failOps;
        if (f && (f.ops === 'all' || f.ops.indexOf(op) >= 0)) throw named(f.name || 'SecurityError');
        if (key != null) {
            for (const fk of this.failKeys) {
                if (String(key).indexOf(fk.prefix) === 0 && (fk.ops === 'all' || fk.ops.indexOf(op) >= 0)) throw named(fk.name || 'SecurityError');
            }
        }
    }
    size(exceptKey) {
        let n = 0;
        for (const [k, v] of this.map) if (k !== exceptKey) n += k.length + v.length;
        return n;
    }
    emit(fromTab, key, oldValue, newValue) {
        for (const t of this.tabs) if (t !== fromTab) t.pendingEvents.push({ key, oldValue, newValue });
    }
    view(tab) {
        const s = this;
        const tabName = tab && tab.name;
        return {
            get length() { s.check('length'); return s.map.size; },
            key(i) { s.check('key'); const k = Array.from(s.map.keys())[i]; return k === undefined ? null : k; },
            getItem(k) {
                s.check('getItem', k);
                let v = s.map.has(k) ? s.map.get(k) : null;
                if (s.readFilter) v = s.readFilter(tabName, k, v);
                if (s.afterGet) s.afterGet(k, v, tabName);
                return v;
            },
            setItem(k, v) {
                s.check('setItem', k);
                if (s.failSets > 0) { s.failSets--; throw named('QuotaExceededError'); }
                if (s.beforeSet) s.beforeSet(k, String(v), tabName);
                if (s.capacity != null && s.size(k) + k.length + String(v).length > s.capacity) throw named('QuotaExceededError');
                const old = s.map.has(k) ? s.map.get(k) : null;
                s.map.set(k, String(v));
                s.log.push({ op: 'set', key: k, tab: tabName });
                if (!(s.silentUnchanged && old === String(v))) s.emit(tab, k, old, String(v));
                if (s.afterSet) s.afterSet(k, String(v), tabName);
            },
            removeItem(k) {
                s.check('removeItem', k);
                if (!s.map.has(k)) return;
                const old = s.map.get(k);
                s.map.delete(k);
                s.log.push({ op: 'remove', key: k, tab: tab && tab.name });
                s.emit(tab, k, old, null);
            }
        };
    }
    /** Journal entry keys (not heartbeats, markers or composing copies unless asked). */
    entryKeys(withComposing) {
        return Array.from(this.map.keys()).filter(k => k.indexOf('XafEditDraft.j1|') === 0 && (withComposing || !k.endsWith('|c')));
    }
    entry(key) {
        const v = this.map.get(key);
        return v === undefined ? undefined : JSON.parse(v);
    }
}

function world(start) {
    const clock = new Clock(start);
    const storage = new SharedStorage();
    return { clock, storage, tabs: storage.tabs };
}

const CTX = '0123456789abcdef0123456789abcdef';
const OID = '11111111-2222-3333-4444-555555555555';

function descriptor(patch) {
    return Object.assign({ v: 1, ns: 'ns1', p: 'test:ToDo', t: 'ToDo', o: OID, ctx: CTX, w: 'ToDo_DetailView', m: 'Description', k: 'memo', co: false, bh: 'abcdef0123456789', g: 1 }, patch || {});
}

/**
 * Opens a tab. opts: { name, storage (override; null = no storage), locks, loadId, limits, window patch }.
 * The journal is started (listeners installed); tab.platform holds the start() report.
 */
async function openTab(w, opts) {
    opts = opts || {};
    const dom = new JSDOM('<!DOCTYPE html><html><body></body></html>', {
        url: 'http://192.0.2.10:5003/', pretendToBeVisual: true, virtualConsole: new VirtualConsole()
    });
    const win = dom.window;
    if (opts.patchWindow) opts.patchWindow(win);
    const mod = await loadModule();
    const tab = { name: opts.name || 'tab' + (w.tabs.length + 1), win, doc: win.document, pendingEvents: [], mod, w };
    w.tabs.push(tab);
    const env = {
        window: win, document: win.document,
        storage: opts.storage !== undefined ? opts.storage : w.storage.view(tab),
        now: () => w.clock.now, setTimeout: w.clock.setTimeout, setInterval: () => 0,
        locks: opts.locks !== undefined ? opts.locks : null,
        loadId: opts.loadId
    };
    if (opts.limits) env.limits = opts.limits;
    tab.journal = mod.createJournal(env);
    tab.platform = await tab.journal.start();
    await flush();
    tab.deliver = async () => {
        const events = tab.pendingEvents.splice(0);
        for (const e of events) win.dispatchEvent(new win.StorageEvent('storage', { key: e.key, oldValue: e.oldValue, newValue: e.newValue }));
        await flush();
    };
    tab.close = () => { const i = w.tabs.indexOf(tab); if (i >= 0) w.tabs.splice(i, 1); win.close(); };
    return tab;
}

/** An attributed editor: a DX-like root element carrying data-editdraft and one input/textarea inside it. */
function addEditor(tab, opts) {
    opts = opts || {};
    const d = descriptor(opts.d);
    const doc = tab.doc;
    const masked = opts.masked !== undefined ? opts.masked : (d.k === 'masked' || d.k === 'time');
    const tag = masked ? 'dxbl-masked-input' : (d.k === 'memo' ? 'dxbl-memo-editor' : 'dxbl-input-editor');
    const root = doc.createElement(tag);
    if (opts.raw !== undefined) root.setAttribute('data-editdraft', opts.raw);
    else root.setAttribute('data-editdraft', JSON.stringify(d));
    if (masked) root.setAttribute('is-mask-defined', '');
    const field = doc.createElement(d.k === 'memo' ? 'textarea' : 'input');
    field.value = opts.value !== undefined ? opts.value : '';
    if (opts.readOnly) field.readOnly = true;
    root.appendChild(field);
    (opts.parent || doc.body).appendChild(root);
    const ed = {
        tab, root, field, d,
        key() { return tab.mod.entryKey(ed.d.ns, tab.journal.loadId, ed.d.ctx, ed.d.m, ed.d.g); },
        composingKey() { return ed.key() + tab.mod.COMPOSING_SUFFIX; },
        stored() { return tab.w.storage.entry(ed.key()); },
        composing() { return tab.w.storage.entry(ed.composingKey()); },
        setDescriptor(patch) { ed.d = Object.assign({}, ed.d, patch); root.setAttribute('data-editdraft', JSON.stringify(ed.d)); }
    };
    return ed;
}

// ---- browser-like user actions (the module only listens; these dispatch what a browser would) ----
function keydown(ed, key, extra) {
    ed.field.dispatchEvent(new ed.tab.win.KeyboardEvent('keydown', Object.assign({ key, bubbles: true, cancelable: true }, extra || {})));
}
function beforeinput(ed, data, inputType) {
    ed.field.dispatchEvent(new ed.tab.win.InputEvent('beforeinput', { data, inputType: inputType || 'insertText', bubbles: true, cancelable: true }));
}
function input(ed, value, isComposing, inputType) {
    ed.field.value = value;
    ed.field.dispatchEvent(new ed.tab.win.InputEvent('input', { inputType: inputType || 'insertText', isComposing: !!isComposing, bubbles: true }));
}
/** A typed character in a text editor: keydown, beforeinput, the value changes, input. */
function type(ed, newValue, key) {
    const k = key || (newValue.length ? newValue.charAt(newValue.length - 1) : 'Backspace');
    keydown(ed, k);
    beforeinput(ed, k.length === 1 ? k : null, k.length === 1 ? 'insertText' : 'deleteContentBackward');
    input(ed, newValue, false, k.length === 1 ? 'insertText' : 'deleteContentBackward');
}
function composition(ed, type, data) {
    ed.field.dispatchEvent(new ed.tab.win.CompositionEvent(type, { data: data || '', bubbles: true }));
}
/** DX masked editors: the server-applied text arrives as the root's field-text; lit applies it to the input in its update microtask. */
async function server(ed, text) {
    ed.root.setAttribute('field-text', text);
    ed.root.setAttribute('field-text-version', String(Number(ed.root.getAttribute('field-text-version') || '0') + 1));
    ed.tab.win.queueMicrotask(() => { ed.field.value = text; });
    await flush();
}
/** A masked edit attempt: keydown + beforeinput (cancelled by the editor in the browser); no input event follows. */
function maskedKey(ed, key) {
    keydown(ed, key);
    if (key.length === 1) beforeinput(ed, key, 'insertText');
}

module.exports = {
    loadModule, flush, world, openTab, addEditor, descriptor, CTX, OID,
    keydown, beforeinput, input, type, composition, server, maskedKey, named
};
