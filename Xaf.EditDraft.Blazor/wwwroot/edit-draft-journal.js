// Xaf.EditDraft client-side input journal, phase 2 milestone M1.
// ES module served by the Razor class library as _content/Xaf.EditDraft.Blazor/edit-draft-journal.js and imported
// through IJSRuntime by EditDraftJournalAttributeControllerBlazor (no host-page script tag).
// Design: docs/edit-draft-client-journal-design-2026-10-03.md (Q1, Q2); binding changes and M1 rules:
// docs/edit-draft-client-journal-m0-2026-10-03.md (section 5 and section 9).
//
// What it does: it listens only inside elements that carry a server-built data-editdraft descriptor and writes each
// recorded change of such an editor at once (synchronously) to that change's own localStorage key. It never sets a value,
// a selection or the caret, never cancels or stops an event, and never renders markup.
//
// Parts an IME or iPad finding (M0 section 7, still open) may change are kept small and replaceable:
//   - storage access: createStorage(store)  (every call returns { ok, ... } and never throws)
//   - composition:    COMPOSITION           (start / update / end handling)
//
// M1b cluster A (refused-write retry, requirement R-A1..R-A4): every write goes through one intent log per journal key
// ("intent log" below). Retries apply the key's CURRENT intent, never a snapshot.
// M1b-C (per-tab clears, docs/edit-draft-client-journal-per-tab-clears-2026-10-04.md; owner rulings 2026-10-04): each page
// load writes, removes and evicts only the keys that carry its own load id; clear(ns) reaches only this page load and
// sends nothing to other tabs. Another page load's keys are removed only by expiry or retire, and only when Web Locks show
// that load's lock is not held and its heartbeat is absent or stale (O1-A); logoff(ns) is the one sweep of every page
// load's entries of a namespace (O2). Conflicts between tabs are left to the server's intake.
// M1b-D (owner ruling 2026-10-04): the key just written is evictable like any other (planEviction); every field state of
// a shared key is kept, so a clear replaces all of them (stateByKey).
// M1b-E / M1b-F (owner rulings 2026-10-04): a key just written that a failed read left out of the post-write scan still
// competes for eviction. There is no self-repair after a back/forward-cache restore ("Remove the self-repair entirely"):
// the page rewrites its heartbeat and requests its lock again, nothing more; a removal by another tab that races the
// restore stays (the C1 window is the accepted residual).

export const FORMAT = 1;
export const PREFIX = 'XafEditDraft.j1|';          // one key per entry; the format version is part of the prefix
export const COMPOSING_SUFFIX = '|c';              // incomplete text of an open composition, kept apart from the entry
export const HEARTBEAT_PREFIX = 'XafEditDraft.hb1|';
export const LOCK_PREFIX = 'XafEditDraft.w|';           // Web Lock name of a page load: this + its load id
const ROUND_TRIP_PREFIX = 'XafEditDraft.rt1|';
export const LIMITS = Object.freeze({
    entries: 60,                 // owner decision 9: 60 entries, per page load (owner ruling 2026-10-04)
    valueChars: 12000,           // owner decision 9: 12,000 characters per value (longer: stored truncated, flagged)
    keepMs: 60 * 60 * 1000,      // owner decision 11: 60 minutes
    serializedChars: 1048576,    // key + serialized entry, summed over the journal keys of one page load (M0 section 5 item 9)
    pendingMs: 60000,            // post-blur window of one masked edit attempt (the JS interop default timeout)
    heartbeatMs: 10000,          // liveness heartbeat of a page load
    retryMs: 1000,               // a refused write is tried again after this delay (and on the next event)
    retryAttempts: 5,
    writerAliveMs: 120000        // another page load whose heartbeat is younger than this is not gone: its keys are never removed
});
export const KINDS = Object.freeze(['text', 'memo', 'combo', 'masked', 'time', 'custom']);

const DESCRIPTOR_ATTRIBUTE = 'data-editdraft';
const EVENT_TYPES = ['focusin', 'focusout', 'keydown', 'beforeinput', 'input', 'compositionstart', 'compositionupdate', 'compositionend', 'paste', 'cut'];
const MUTATION_ATTRIBUTES = ['field-text', 'field-text-version', DESCRIPTOR_ATTRIBUTE];

// ------------------------------------------------------------------------------------------------ pure helpers

/** The key of one entry: prefix + owner token | page load | editing context | member path | generation. */
export function entryKey(ns, load, ctx, member, generation) {
    return PREFIX + [ns, load, ctx, member, String(generation)].join('|');
}

/**
 * The page load a key belongs to, read from the KEY (never from the stored JSON, which any same-origin writer can set):
 * the load segment of an entry key or incomplete copy, or the suffix of a heartbeat key. Null for any other key.
 */
export function loadOfKey(key) {
    if (typeof key !== 'string') return null;
    if (key.indexOf(PREFIX) === 0) {
        const parts = key.slice(PREFIX.length).split('|');
        return parts.length >= 5 && parts[1].length > 0 ? parts[1] : null;
    }
    if (key.indexOf(HEARTBEAT_PREFIX) === 0) {
        const load = key.slice(HEARTBEAT_PREFIX.length);
        return load.length > 0 && load.indexOf('|') < 0 ? load : null;
    }
    return null;
}

/**
 * Parses a data-editdraft descriptor. Returns null for anything that is not a complete version-1 descriptor; such an
 * editor is not journaled. Never throws.
 */
export function parseDescriptor(raw) {
    if (typeof raw !== 'string' || raw.length === 0 || raw.length > 65536) return null;
    let d;
    try { d = JSON.parse(raw); } catch (e) { return null; }
    if (!d || typeof d !== 'object' || Array.isArray(d) || d.v !== FORMAT) return null;
    for (const f of ['ns', 'p', 't', 'ctx', 'w', 'm', 'k']) {
        if (typeof d[f] !== 'string' || d[f].length === 0 || d[f].length > 512) return null;
    }
    for (const f of ['ns', 'ctx', 'm']) if (d[f].indexOf('|') >= 0) return null;   // key parts
    if (KINDS.indexOf(d.k) < 0) return null;
    if (!Number.isInteger(d.g) || d.g < 1) return null;
    if (typeof d.co !== 'boolean') return null;
    if (d.o != null && typeof d.o !== 'string') return null;
    if (d.f != null && typeof d.f !== 'string') return null;
    if (d.bh != null && typeof d.bh !== 'string') return null;
    if (d.rc != null && (typeof d.rc !== 'object' || Array.isArray(d.rc))) return null;
    if (d.cu != null && typeof d.cu !== 'string') return null;
    return {
        v: d.v, ns: d.ns, p: d.p, t: d.t, o: d.o == null ? null : d.o, ctx: d.ctx, w: d.w, m: d.m, k: d.k,
        f: d.f == null ? null : d.f, cu: d.cu == null ? null : d.cu, co: d.co, bh: d.bh == null ? null : d.bh, g: d.g,
        rc: d.rc == null ? null : d.rc
    };
}

/** Oldest first; equal times are ordered by key, so every tab evicts the same entry (F6 D1). */
export function evictionOrder(a, b) {
    return (a.at - b.at) || (a.key < b.key ? -1 : (a.key > b.key ? 1 : 0));
}

/**
 * Which entries must go once an entry of newSize characters (key + serialized value) is written under newKey: the
 * oldest first (evictionOrder) until the count and the serialized total fit. Items: [{ key, at, size }]. The key just
 * written competes like any other (M1b-D C2): it takes its time from its own item, and when it is the oldest by capture
 * time (then key) it is the one chosen, so an old retry never evicts a newer entry. A newKey that is not among the
 * items has no known time and is never chosen.
 */
export function planEviction(items, newKey, newSize, limits) {
    const lim = limits || LIMITS;
    const listed = items.find(x => x.key === newKey);
    const others = items.filter(x => x.key !== newKey);
    if (listed) others.push({ key: newKey, at: listed.at, size: newSize });
    others.sort(evictionOrder);
    let count = others.length + (listed ? 0 : 1);
    let total = others.reduce((s, x) => s + x.size, 0) + (listed ? 0 : newSize);
    const out = [];
    for (const x of others) {
        if (count <= lim.entries && total <= lim.serializedChars) break;
        out.push(x.key);
        count--;
        total -= x.size;
    }
    return out;
}

/** Expired: 60 minutes old or older (an unreadable time counts as expired). */
export function isExpired(at, now, limits) {
    return typeof at !== 'number' || !isFinite(at) || now - at >= (limits || LIMITS).keepMs;
}

function errorName(e) { return (e && e.name) || String(e); }

/** Value-free class of the last character of a text (for reports that must not carry the text). */
export function tailClass(v) {
    if (typeof v !== 'string' || v.length === 0) return 'empty';
    const c = v.charAt(v.length - 1);
    if (/[一-鿿㐀-䶿]/.test(c)) return 'kanji';
    if (/[぀-ゟ]/.test(c)) return 'hiragana';
    if (/[゠-ヿ]/.test(c)) return 'katakana';
    if (/[0-9０-９]/.test(c)) return 'digit';
    if (/[A-Za-z]/.test(c)) return 'latin';
    return 'other';
}

/** The time of a stored entry, read from its first property without parsing the whole value; null when unreadable. */
function storedAt(text) {
    if (typeof text !== 'string') return null;
    const m = /^\{"at":(\d+)/.exec(text);
    if (m) return Number(m[1]);
    try { const x = JSON.parse(text); return typeof x.at === 'number' ? x.at : null; } catch (e) { return null; }
}

function parseEntry(text) {
    try { const x = JSON.parse(text); return x && typeof x === 'object' && x.f === FORMAT ? x : null; } catch (e) { return null; }
}

// ------------------------------------------------------------------------------------------------ storage access

/** Wraps a Storage-like object. Every call reports success or the error name; none throws (M0 section 5 item 12). */
export function createStorage(store) {
    const missing = { name: 'StorageUnavailable' };
    return {
        keys() {
            try {
                if (!store) throw missing;
                const out = [];
                const n = store.length;
                for (let i = 0; i < n; i++) { const k = store.key(i); if (k != null) out.push(k); }
                return { ok: true, keys: out };
            } catch (e) { return { ok: false, error: errorName(e) }; }
        },
        get(key) {
            try { if (!store) throw missing; return { ok: true, value: store.getItem(key) }; }
            catch (e) { return { ok: false, error: errorName(e) }; }
        },
        set(key, value) {
            try { if (!store) throw missing; store.setItem(key, value); return { ok: true }; }
            catch (e) { return { ok: false, error: errorName(e) }; }
        },
        remove(key) {
            try { if (!store) throw missing; store.removeItem(key); return { ok: true }; }
            catch (e) { return { ok: false, error: errorName(e) }; }
        }
    };
}

function localStorageOf(win) {
    try { return win.localStorage; } catch (e) { return null; }
}

function makeLoadId(now) {
    return now.toString(36) + Math.random().toString(36).slice(2, 8);
}

// ------------------------------------------------------------------------------------------------ the journal

/**
 * Creates one journal bound to a window. env: { window, document, storage, now, setTimeout, setInterval, queueMicrotask,
 * MutationObserver, locks, loadId, limits }. Only `window` is required; the rest default to it.
 */
export function createJournal(env) {
    const win = env.window;
    const doc = env.document || win.document;
    const limits = Object.freeze(Object.assign({}, LIMITS, env.limits || {}));
    const now = env.now || (() => Date.now());
    const later = env.setTimeout || ((fn, ms) => win.setTimeout(fn, ms));
    const every = env.setInterval || ((fn, ms) => win.setInterval(fn, ms));
    const micro = env.queueMicrotask || (win.queueMicrotask ? (fn) => win.queueMicrotask(fn) : (fn) => Promise.resolve().then(fn));
    const Observer = env.MutationObserver || win.MutationObserver;
    const locks = env.locks !== undefined ? env.locks : (win.navigator && win.navigator.locks && win.navigator.locks.request ? win.navigator.locks : null);
    const store = createStorage(env.storage !== undefined ? env.storage : localStorageOf(win));
    const loadId = env.loadId || makeLoadId(now());

    const states = new WeakMap();            // field element -> per-field state (one per descriptor; a clear replaces it)
    const stateByKey = new Map();            // entry key -> Set of the field states that write it, one per field, oldest
                                             // first (several fields may share a key: a clear replaces every one, M1b-D C3);
                                             // the last is the latest (retire and the composition hold ask it)
    const entries = new Map();               // journal key -> its intent log in this page load (see "intent log")
    const descriptorCache = new Map();       // raw attribute text -> parsed descriptor (or null)
    const stats = { writes: 0, writeFailures: 0, retries: 0, evicted: 0, expired: 0, unresolved: 0, invalidDescriptors: 0,
                    removedElsewhere: 0, skippedNoAction: 0, skippedUnchanged: 0, skippedSame: 0,
                    skippedServerSet: 0, droppedAfterClear: 0, staleReadsDropped: 0, storageErrors: 0,
                    droppedExpired: 0, deferredComposing: 0 };
    const errors = [];                       // value-free failure records, bounded
    const refTo = typeof WeakRef === 'function' ? (el) => new WeakRef(el) : (el) => ({ deref: () => el });
    let seq = 0;                             // sequence of written entries (retire matches it)
    let ord = 0;                             // page-wide order of intents, field states and clears
    let started = false;
    let observer = null;

    function fail(op, error, member) {
        stats.storageErrors++;
        note(op, error, member);
    }
    function note(op, error, member) {
        errors.push({ op, error, m: member || null, at: now() });
        if (errors.length > 50) errors.splice(0, errors.length - 50);
    }

    function descriptorOf(root) {
        const raw = root && root.getAttribute ? root.getAttribute(DESCRIPTOR_ATTRIBUTE) : null;
        if (raw == null) return null;
        if (descriptorCache.has(raw)) return descriptorCache.get(raw);
        const d = parseDescriptor(raw);
        if (!d) stats.invalidDescriptors++;
        if (descriptorCache.size > 500) descriptorCache.clear();
        descriptorCache.set(raw, d);
        return d;
    }
    function rootOf(el) {
        return el && el.closest ? el.closest('[' + DESCRIPTOR_ATTRIBUTE + ']') : null;
    }
    function fieldOf(root, target) {
        if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA') && rootOf(target) === root) return target;
        if (!root || !root.querySelectorAll) return null;
        const list = root.querySelectorAll('input:not([type=hidden]), textarea');
        for (const el of list) if (el.getAttribute('aria-hidden') !== 'true' && el.tabIndex !== -1) return el;
        return list.length ? list[0] : null;
    }
    function isMasked(root, d) {
        return (root.hasAttribute && root.hasAttribute('is-mask-defined')) || d.k === 'masked' || d.k === 'time';
    }
    function isReadOnly(field) {
        return !!(field.readOnly || field.disabled);
    }
    function valueOf(field) {
        const v = field.value;
        return v == null ? '' : String(v);
    }
    function descKey(d) { return d.ns + '|' + d.ctx + '|' + d.m + '|' + d.g; }

    /**
     * The state of a field for the CURRENT descriptor. A changed descriptor (record change, save, generation bump) starts
     * a fresh state whose baseline is the value shown now (or `initialBaseline` when given); this page load's own clear of
     * the namespace replaces the state at once (refreshStates). An open composition belongs to the element and is carried
     * over (M0 R1). markedVal / valueAt: the last recorded value and its capture time (when the user action made it); a
     * re-read of that same value (focusout, page hide, a later read) keeps its capture time, so a re-read never makes an
     * old value look newer (stored as the entry's `at`).
     */
    function stateFor(field, d, initialBaseline) {
        const s = states.get(field);
        if (s && s.descKey === descKey(d)) return s;
        return newState(field, d, initialBaseline !== undefined ? initialBaseline : valueOf(field), s);
    }
    function newState(field, d, baseline, prev) {
        const s = { descKey: descKey(d), d, ns: d.ns, m: d.m, mainKey: entryKey(d.ns, loadId, d.ctx, d.m, d.g),
                    ord: ++ord, markedVal: baseline, valueAt: now(), fieldRef: refTo(field),
                    composing: prev ? prev.composing : false, session: prev ? prev.session : 0,
                    acted: false, changed: false, serverSet: false, awaitMask: null,
                    baseline, ops: [], opSeq: prev ? prev.opSeq : 0 };
        states.set(field, s);
        let set = stateByKey.get(s.mainKey);
        if (!set) { set = new Set(); stateByKey.set(s.mainKey, set); }
        for (const o of set) { const f = o.fieldRef.deref(); if (!f || f === field) set.delete(o); }   // one state per field
        set.add(s);
        return s;
    }
    /** The latest field state that writes key (the last one created), or null. */
    function latestState(key) {
        let last = null;
        const set = stateByKey.get(key);
        if (set) for (const s of set) last = s;
        return last;
    }

    /**
     * This page load's clear starts a fresh state for every field of the namespace now, also for every field that shares
     * a key with another: baseline = the text shown now, no action, no pending attempt (delayed reads of the old state
     * are dropped by their own state check).
     */
    function refreshStates(ns) {
        for (const set of Array.from(stateByKey.values())) {
            for (const s of Array.from(set)) {
                if (s.ns !== ns) continue;
                const field = s.fieldRef.deref();
                if (!field || states.get(field) !== s) continue;
                newState(field, s.d, valueOf(field), s);
            }
        }
    }

    function markAction(field, d, initialBaseline) {
        if (isReadOnly(field)) return null;
        const s = stateFor(field, d, initialBaseline);
        s.acted = true;
        return s;
    }

    /**
     * An edit attempt whose result the server applies (masked typing, arrows, wheel, a button inside the editor): it waits
     * for its server reply, also after blur, until ONE field-text reply answers it (oldest first) or pendingMs passes. One
     * keystroke is one attempt: the beforeinput that follows its own keydown joins that keydown's operation.
     */
    function openOperation(root, field, d, type) {
        const s = markAction(field, d);
        if (!s) return null;
        const last = s.ops.length ? s.ops[s.ops.length - 1] : null;
        if (type === 'beforeinput' && last && last.type === 'keydown' && !last.answered && !last.paired) { last.paired = true; return last; }
        const op = { id: ++s.opSeq, at: now(), answered: false, type: type || null, paired: false };
        s.ops.push(op);
        later(() => {
            const i = s.ops.indexOf(op);
            if (i >= 0) s.ops.splice(i, 1);
            if (s.awaitMask === op) s.awaitMask = null;   // the incomplete copy stays; nothing is promoted without the reply
            if (!op.answered) { stats.unresolved++; errors.push({ op: 'capture-unresolved', error: 'no response', m: d.m, at: now() }); }
        }, limits.pendingMs);
        return op;
    }
    function oldestOpen(s) { return s.ops.find(op => !op.answered) || null; }
    function hasOpenOperation(s) { return !!oldestOpen(s); }

    // -------------------------------------------------------------------------------------------- storage helpers

    function removeKey(key, why) {
        const r = store.remove(key);
        if (!r.ok) { fail('remove', r.error); return false; }
        if (why === 'evicted') stats.evicted++;
        if (why === 'expired') stats.expired++;
        return true;
    }

    /** The time of a heartbeat value ("<time>"); NaN when unreadable. */
    function stampTime(text) {
        const m = /^(\d+)/.exec(typeof text === 'string' ? text : '');
        return m ? Number(m[1]) : NaN;
    }

    function isOwnKey(key) { return loadOfKey(key) === loadId; }

    /**
     * Reads the journal keys and heartbeats. This page load's own expired keys are removed here. Another page load's
     * expired keys are only collected (`stale`): they are not listed, not handed out and not counted, and are removed
     * later only through removeStaleForeign (O1-A). Keys of other prefixes (the host's own data, other formats, legacy
     * clear markers of earlier builds) are never read or touched. Returns { items: [{ key, at, size, own }], stale, partial }
     * for the live journal keys, or null when storage cannot be enumerated. partial = a key could not be read.
     */
    function scan() {
        const ks = store.keys();
        if (!ks.ok) { fail('enumerate', ks.error); return null; }
        const items = [];
        const stale = [];
        let partial = false;
        const t = now();
        for (const k of ks.keys) {
            const journalKey = k.indexOf(PREFIX) === 0;
            const heartbeatKey = k.indexOf(HEARTBEAT_PREFIX) === 0;
            if (!journalKey && !heartbeatKey) continue;
            const own = isOwnKey(k);
            if (heartbeatKey && own) continue;                 // this page load's own heartbeat is rewritten while it runs
            const g = store.get(k);
            if (!g.ok) { fail('read', g.error); partial = true; continue; }
            if (g.value == null) continue;
            const at = heartbeatKey ? stampTime(g.value) : storedAt(g.value);
            if (isExpired(at, t, limits)) {
                if (own) removeKey(k, 'expired');
                else stale.push(k);
                continue;
            }
            if (journalKey) items.push({ key: k, at, size: k.length + g.value.length, own });
        }
        return { items, stale, partial };
    }

    /**
     * Held or requested Web Lock names of this origin, as the set of page loads they name; null when Web Locks or
     * locks.query() are missing or the query fails (then no other page load's key may be removed).
     */
    async function lockedLoads() {
        if (!locks || typeof locks.query !== 'function') return null;
        try {
            const q = await locks.query();
            const out = new Set();
            for (const l of [].concat((q && q.held) || [], (q && q.pending) || [])) {
                if (l && typeof l.name === 'string' && l.name.indexOf(LOCK_PREFIX) === 0) out.add(l.name.slice(LOCK_PREFIX.length));
            }
            return out;
        } catch (e) {
            note('locks', errorName(e), null);
            return null;
        }
    }

    /**
     * O1-A: another page load counts as gone only when its lock is neither held nor requested (`locked`, from a successful
     * locks.query()) AND its heartbeat is absent or older than writerAliveMs. An unreadable heartbeat is not "gone".
     */
    function writerGone(load, locked) {
        if (!locked || !load || load === loadId || locked.has(load)) return false;
        const g = store.get(HEARTBEAT_PREFIX + load);
        if (!g.ok) { fail('read', g.error); return false; }
        if (g.value == null) return true;
        const at = stampTime(g.value);
        return !isFinite(at) || now() - at >= limits.writerAliveMs;
    }

    /**
     * Removes other page loads' expired keys (collected by scan) whose writer is gone (O1-A). Without Web Locks, or when
     * the query fails, nothing is removed: the keys stay, ineligible. Each key is read again just before its removal, so
     * a value written since the scan (it is no longer expired) stays.
     */
    async function removeStaleForeign(stale) {
        if (!stale || stale.length === 0) return 0;
        const locked = await lockedLoads();
        if (!locked) return 0;
        let removed = 0;
        for (const k of stale) {
            if (!writerGone(loadOfKey(k), locked)) continue;
            const g = store.get(k);
            if (!g.ok) { fail('read', g.error); continue; }
            if (g.value == null) continue;
            const at = k.indexOf(HEARTBEAT_PREFIX) === 0 ? stampTime(g.value) : storedAt(g.value);
            if (!isExpired(at, now(), limits)) continue;
            if (removeKey(k, 'expired')) removed++;
        }
        return removed;
    }

    /** At most max UTF-16 units, never ending inside a surrogate pair. */
    function truncate(text, max) {
        if (text.length <= max) return text;
        let cut = max;
        const c = text.charCodeAt(cut - 1);
        if (c >= 0xD800 && c <= 0xDBFF) cut--;
        return text.slice(0, cut);
    }

    // -------------------------------------------------------------------------------------------- intent log (M1b cluster A)
    //
    // Ordering model (requirement R-A1..R-A4; Codex M1 diffreview a2 D3; KB fix-552 prevention rule):
    //  Each journal key this page load writes (entry or incomplete copy) has one record with a per-entry sequence n. Every
    //  recorded value, every removal of an incomplete copy and every clear of this page load is an INTENT with the next n
    //  and a page-wide order number. Only the latest intent (cur) is ever applied: a retry writes what the user means at
    //  retry time, never the value that was refused (D3a), with the capture time of that value, and removes an incomplete
    //  copy only when the copy is older than the value it writes (D3b). Only this page load's own keys are ever written.

    function entryOf(key, ns, comp) {
        let E = entries.get(key);
        if (!E) {
            E = { key, ns, comp: !!comp, n: 0, applied: 0, cur: null, written: null, attempts: 0, timer: false };
            entries.set(key, E);
        }
        return E;
    }
    function isPending(E) { return !!E.cur && E.applied < E.cur.n; }
    /**
     * The field that writes mainKey has an open composition, or a masked composition waiting for its mask reply (read
     * from the field's CURRENT state: the composition belongs to the element, also across a descriptor change).
     */
    function incompleteNow(mainKey) {
        const s = latestState(mainKey);
        const field = s ? s.fieldRef.deref() : null;
        const cur = field ? states.get(field) : null;
        return !!cur && (cur.composing || (cur.awaitMask !== null && !cur.awaitMask.answered));
    }
    function pendingCount() { let n = 0; for (const E of entries.values()) if (isPending(E)) n++; return n; }

    /**
     * A new intent for one key: the value the user means now (or the incomplete copy's text), with `madeAt`, the capture
     * time of the user action that produced it; applied at once.
     */
    function intend(key, d, value, composing, madeAt) {
        const E = entryOf(key, d.ns, composing);
        const cur = E.cur;
        if (cur && isPending(E) && cur.kind === 'value' && cur.val === value && cur.d === d) {
            apply(E);                                   // the same pending intent read again: tried again, not renewed
            return E;
        }
        E.cur = { n: ++E.n, ord: ++ord, kind: 'value', d, val: value, comp: !!composing, at: madeAt };
        E.attempts = 0;
        apply(E);
        return E;
    }

    /** Brings storage to the key's CURRENT intent (immediately, on a retry, on focusout). True when nothing is left to do. */
    function apply(E) {
        const it = E.cur;
        if (!it || E.applied >= it.n) return true;
        if (it.kind === 'remove') return applyRemove(E, it);
        if (it.kind !== 'value') { E.applied = it.n; return true; }
        if (now() - it.at >= limits.keepMs) { E.applied = it.n; stats.droppedExpired++; return false; }   // older than the retention
        if (!E.comp && incompleteNow(E.key)) { stats.deferredComposing++; return false; }   // no entry write while composing (M0 section 5 item 2); the commit supersedes it
        if (E.written && E.written.val === it.val) {
            // Already stored by this page load: nothing to write (and a superseded refused value is gone with it).
            E.applied = it.n;
            stats.skippedSame++;
            if (!E.comp) cleanCopy(E.key, it.ord);
            return true;
        }
        const d = it.d;
        const val = truncate(it.val, limits.valueChars);
        // `at` is the capture time of the value (when the user action made it), not the write time: a retry or a re-read
        // never makes it newer (ordering between tabs at intake, and the 60-minute retention, count from the user action).
        const entry = { at: it.at, f: FORMAT, seq: ++seq, ns: d.ns, load: loadId, p: d.p, t: d.t, o: d.o, ctx: d.ctx, w: d.w,
                        m: d.m, k: d.k, fmt: d.f, cu: d.cu, co: d.co, bh: d.bh, g: d.g, val, tr: val.length !== it.val.length };
        if (d.rc) entry.rc = d.rc;
        if (it.comp) entry.comp = true;
        const text = JSON.stringify(entry);
        const r = store.set(E.key, text);
        if (!r.ok) {
            // A refused write evicts nothing: earlier entries stay as they are; it is reported and the key's current intent
            // is tried again later (timer, focusout, the next event).
            stats.writeFailures++;
            fail('write', r.error, d.m);
            scheduleRetry(E);
            return false;
        }
        stats.writes++;
        E.written = { val: it.val, seq: entry.seq, ord: it.ord };   // de-duplication moves only after a write succeeded (M0 C5)
        E.applied = it.n;
        E.attempts = 0;
        // Budget after the write, per page load: the oldest keys of this page load (all its namespaces and incomplete
        // copies), the one just written included (M1b-D C2), go until its count and serialized total fit. A key that
        // evicted itself stays recorded as written (not written again by a re-read). Another page load's keys are never
        // evicted.
        const sc = scan();
        if (sc) {
            const own = sc.items.filter(x => x.own);
            // A read that failed in the scan leaves the key just written out of it: it still competes, with its capture
            // time, so a read failure never brings back its exemption (M1b-E G2, DR1).
            if (!own.some(x => x.key === E.key)) own.push({ key: E.key, at: it.at, size: E.key.length + text.length, own: true });
            for (const k of planEviction(own, E.key, E.key.length + text.length, limits)) removeKey(k, 'evicted');
        }
        if (!E.comp) cleanCopy(E.key, it.ord);
        return true;
    }

    function applyRemove(E, it) {
        const r = store.remove(E.key);
        if (!r.ok) { fail('remove', r.error); scheduleRetry(E); return false; }
        E.written = null;
        E.applied = it.n;
        E.attempts = 0;
        return true;
    }

    /** At most limits.retryAttempts timer retries per intent (after the first attempt); focusout and the next event also retry. */
    function scheduleRetry(E) {
        if (E.timer || E.attempts >= limits.retryAttempts) return;
        E.attempts++;
        E.timer = true;
        later(() => {
            E.timer = false;
            if (!isPending(E)) return;
            stats.retries++;
            apply(E);
        }, limits.retryMs);
    }
    function retryFailed() {
        for (const E of Array.from(entries.values())) {
            if (!isPending(E)) continue;
            stats.retries++;
            apply(E);
        }
    }

    /**
     * Removes the incomplete copy of mainKey, unless that copy belongs to a composition newer than the intent at upToOrd
     * (an older retry never removes a newer composition's copy: D3b). Any pending write of an older copy is superseded.
     */
    function cleanCopy(mainKey, upToOrd) {
        const ck = mainKey + COMPOSING_SUFFIX;
        const Ec = entries.get(ck);
        if (Ec && Ec.cur && Ec.cur.kind === 'value' && Ec.cur.ord > upToOrd) return;
        const g = store.get(ck);
        const stored = !g.ok || g.value != null;
        if (!Ec) { if (stored) removeKey(ck, 'composing-cleaned'); return; }
        if (Ec.cur && Ec.cur.kind !== 'value' && !isPending(Ec) && !stored) return;
        Ec.cur = { n: ++Ec.n, ord: ++ord, kind: 'remove' };
        Ec.attempts = 0;
        if (!stored) { Ec.applied = Ec.cur.n; Ec.written = null; return; }
        apply(Ec);
    }

    /** True when v is what this page load last wrote under key and nothing newer is pending (not written again). */
    function unchangedSinceWrite(key, v) {
        const E = entries.get(key);
        return !!E && !!E.written && E.written.val === v && !isPending(E);
    }

    // -------------------------------------------------------------------------------------------- clears

    /**
     * This page load's clear (or logoff) supersedes its own intents of the namespace: every pending write or retry of it
     * is cancelled and nothing of it is treated as stored any more. Nothing is sent to other tabs.
     */
    function supersede(ns) {
        for (const E of entries.values()) {
            if (E.ns !== ns) continue;
            if (E.cur && E.cur.kind !== 'clear') {
                if (isPending(E)) stats.droppedAfterClear++;
                E.cur = { n: ++E.n, ord: ++ord, kind: 'clear' };
                E.applied = E.cur.n;
            }
            E.written = null;
        }
    }

    /**
     * One pass over the keys that start with `prefix`: each is removed once; a refused removal is counted and reported,
     * never retried. Returns { removed, own, failed, error, enumError } (own = removed keys of this page load).
     */
    function sweepPrefix(prefix) {
        const out = { removed: 0, own: 0, failed: 0, error: null, enumError: null };
        const ks = store.keys();
        if (!ks.ok) { fail('enumerate', ks.error); out.enumError = ks.error; return out; }
        for (const k of ks.keys) {
            if (k.indexOf(prefix) !== 0) continue;
            const r = store.remove(k);
            if (r.ok) { out.removed++; if (isOwnKey(k)) out.own++; }
            else { out.failed++; out.error = r.error; fail('remove', r.error); }
        }
        return out;
    }

    // -------------------------------------------------------------------------------------------- recording

    /**
     * Records the field's current value. The single write path of every trigger (input, focusout, composition end,
     * masked read, page hide):
     *   - while a composition is open, or a masked composition waits for its mask reply, only the incomplete copy is
     *     written (never the entry);
     *   - an entry needs a user action in this field for the current descriptor AND a value that differs from the
     *     baseline at least once (a later return to the baseline, A->B->A, is still recorded);
     *   - a value the server set after the user's last input (serverSet) is never written by focusout or page hide;
     *   - the incomplete copy is removed only once the entry is written or nothing needs writing (M0 R3; Codex a1 C5);
     *   - the same value as the last successful write is not written again (so nothing removed elsewhere comes back);
     *   - every write is an intent of the key's intent log (ordering, retries and clears: see "intent log").
     */
    function record(root, field, reason, composingHint) {
        if (!root || !field) return;
        const d = descriptorOf(root);
        if (!d) return;
        const s = stateFor(field, d);
        if (composingHint === true) s.composing = true;
        if (!s.acted) { stats.skippedNoAction++; return; }
        if (s.serverSet && reason !== 'input' && reason !== 'mutation') { stats.skippedServerSet++; return; }
        const v = valueOf(field);
        // The capture time of this value: a changed value is made now; the same value again keeps the time it was first
        // recorded with (a re-read, a retry or a key that changed nothing never makes it newer).
        const fresh = v !== s.markedVal;
        const madeAt = fresh ? now() : s.valueAt;
        s.markedVal = v;
        s.valueAt = madeAt;
        const incomplete = s.composing || (s.awaitMask !== null && !s.awaitMask.answered);
        if (incomplete) {
            const ck = s.mainKey + COMPOSING_SUFFIX;
            if (unchangedSinceWrite(ck, v)) { stats.skippedSame++; return; }
            intend(ck, d, v, true, madeAt);
            return;
        }
        if (s.baseline === null || v !== s.baseline) s.changed = true;
        if (!s.changed) { cleanCopy(s.mainKey, Infinity); stats.skippedUnchanged++; return; }
        if (unchangedSinceWrite(s.mainKey, v)) { cleanCopy(s.mainKey, Infinity); stats.skippedSame++; return; }
        intend(s.mainKey, d, v, false, madeAt);   // the incomplete copy goes once this value is written (apply)
    }

    // -------------------------------------------------------------------------------------------- composition (replaceable)

    const COMPOSITION = {
        start(root, field, d) {
            const s = markAction(field, d);
            if (!s) return;
            s.composing = true;
            s.session++;
        },
        update(root, field, d) {
            const s = markAction(field, d);
            if (s) s.composing = true;
        },
        end(root, field, d) {
            const s = markAction(field, d);
            if (!s) return;
            s.composing = false;
            s.serverSet = false;
            // Masked editors: the server applies the composed text through the mask; until that reply every path keeps
            // writing the incomplete copy only (Codex a1 C2), and the reply's field-text read records it (M0 section 9 rule 5).
            if (isMasked(root, d)) { s.awaitMask = openOperation(root, field, d, 'compositionend'); return; }
            const session = s.session;
            record(root, field, 'compositionend');
            // Text editors: one more read after the component's update. Dropped when the field's state is no longer this
            // one (descriptor change, clear) or a newer composition has started or is open (a stale read is dropped, never
            // redirected: M0 R5; Codex a1 C9).
            later(() => {
                const s2 = states.get(field);
                if (s2 !== s || s2.session !== session || s2.composing) { stats.staleReadsDropped++; return; }
                record(root, field, 'compositionend+later');
            }, 0);
        }
    };

    // -------------------------------------------------------------------------------------------- events

    function isEditKey(e, masked) {
        if (e.type !== 'keydown') return false;
        if (e.key && e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) return true;
        if (e.key === 'Backspace' || e.key === 'Delete' || e.key === 'Process' || e.keyCode === 229) return true;
        return masked && (e.key === 'ArrowUp' || e.key === 'ArrowDown');
    }

    function onEvent(e) {
        const root = rootOf(e.target);
        if (!root) return;
        const d = descriptorOf(root);
        if (!d) return;
        const field = fieldOf(root, e.target);
        if (!field) return;
        const masked = isMasked(root, d);
        switch (e.type) {
            case 'focusin':
                stateFor(field, d);                      // baseline at focus, before any action
                return;
            case 'focusout':
                record(root, field, 'focusout');
                retryFailed();
                return;
            case 'compositionstart': COMPOSITION.start(root, field, d); return;
            case 'compositionupdate': COMPOSITION.update(root, field, d); return;
            case 'compositionend': COMPOSITION.end(root, field, d); return;
            case 'input': {
                // A first input with no earlier state (autofill, no focus event) compares with the editor's server text
                // (field-text) when there is one, otherwise it counts as a change (Codex a1 C10).
                const ft = root.getAttribute ? root.getAttribute('field-text') : null;
                const s = markAction(field, d, ft != null ? ft : null);
                if (!s) return;
                s.serverSet = false;
                if (e.isComposing) s.composing = true;   // the session is restored from the event itself (M0 R1)
                record(root, field, 'input', e.isComposing === true);
                return;
            }
            case 'pointerdown':
                // A button inside the editor (clear, spin): its result comes back from the server as field-text.
                if (e.target !== field) openOperation(root, field, d, 'pointerdown');
                return;
            default:
                if (e.type === 'beforeinput' || e.type === 'paste' || e.type === 'cut' || isEditKey(e, masked) || (e.type === 'wheel' && masked)) {
                    if (masked) openOperation(root, field, d, e.type); else markAction(field, d);
                }
        }
    }

    function onMutations(list) {
        const answered = new Set();                    // one reply per editor per callback answers one attempt
        for (const mu of list) {
            if (mu.type !== 'attributes') continue;
            const root = mu.attributeName === DESCRIPTOR_ATTRIBUTE ? mu.target : rootOf(mu.target);
            if (!root) continue;
            const d = descriptorOf(root);
            if (!d) continue;
            const field = fieldOf(root, null);
            if (!field) continue;
            if (mu.attributeName === DESCRIPTOR_ATTRIBUTE) { stateFor(field, d); continue; }   // new descriptor: fresh baseline now
            if (answered.has(root)) continue;
            answered.add(root);
            const s = stateFor(field, d);
            const op = oldestOpen(s);
            if (!op) {
                // A server value with no attempt of this field waiting is not an edit, on any editor (Codex a1 C3); text
                // editors are recorded from input events, so for them only an attempt's reply (a button) is read here.
                s.serverSet = true;
                continue;
            }
            op.answered = true;
            if (s.awaitMask === op) s.awaitMask = null;
            // Read in a microtask queued from the observer callback: the editor applies field-text to the input in its own
            // update microtask, queued before this one (M0 G2). No animation frame (paused in hidden tabs).
            micro(() => {
                if (states.get(field) !== s) { stats.staleReadsDropped++; return; }   // descriptor changed or cleared since
                s.serverSet = false;
                record(root, field, 'mutation');
            });
        }
    }

    function onLifecycle() {
        const a = doc.activeElement;
        const root = rootOf(a);
        if (root) record(root, fieldOf(root, a), 'pagehide');
    }

    /**
     * Storage events of other tabs only count removals: they never rewrite or remove an entry, never cancel an intent and
     * never change a field state here (per-tab clears). Any other key (a legacy clear marker included) has no effect.
     */
    function onStorage(e) {
        if (!e) return;
        if (e.key == null) { stats.removedElsewhere++; return; }        // storage.clear() elsewhere: nothing is rewritten
        if (e.key.indexOf(PREFIX) === 0 && e.newValue == null) stats.removedElsewhere++;   // evicted / retired / swept elsewhere: not rewritten
    }

    // -------------------------------------------------------------------------------------------- liveness

    let lockHeld = false;
    function heartbeat() {
        const r = store.set(HEARTBEAT_PREFIX + loadId, String(now()));
        if (!r.ok) fail('heartbeat', r.error);
    }
    /** The page load's Web Lock, held for its lifetime (the callback never resolves). Optional (M0 section 5 item 7). */
    function requestLock() {
        if (!locks || typeof locks.request !== 'function') return;
        try {
            const p = locks.request(LOCK_PREFIX + loadId, () => { lockHeld = true; return new Promise(() => { }); });
            if (p && typeof p.catch === 'function') p.catch(() => { lockHeld = false; });
        } catch (e) { /* optional */ }
    }
    function startLiveness() {
        heartbeat();
        every(heartbeat, limits.heartbeatMs);
        requestLock();
    }
    /**
     * A page restored from the back/forward cache: page hide removed its heartbeat and the browser may have released its
     * lock. The heartbeat is written again at once, and the lock is requested again when a query shows it is not held.
     */
    async function resumeLiveness() {
        heartbeat();
        const locked = await lockedLoads();
        if (locked && !locked.has(loadId)) { lockHeld = false; requestLock(); }
    }

    async function writers() {
        const out = {};
        const ks = store.keys();
        if (ks.ok) {
            for (const k of ks.keys) {
                if (k.indexOf(HEARTBEAT_PREFIX) !== 0) continue;
                const g = store.get(k);
                const at = g.ok ? Number(g.value) : NaN;
                // alive: true for a fresh heartbeat, otherwise null (unknown): an old heartbeat alone never proves a writer is gone.
                out[k.slice(HEARTBEAT_PREFIX.length)] = { heartbeatAgeMs: isFinite(at) ? now() - at : null, alive: isFinite(at) && now() - at < 3 * limits.heartbeatMs ? true : null, lock: null };
            }
        }
        if (locks && locks.query) {
            try {
                const q = await locks.query();
                for (const l of (q && q.held) || []) {
                    if (!l.name || l.name.indexOf(LOCK_PREFIX) !== 0) continue;
                    const id = l.name.slice(LOCK_PREFIX.length);
                    out[id] = Object.assign({ heartbeatAgeMs: null, alive: true }, out[id] || {}, { lock: true, alive: true });
                }
            } catch (e) { /* optional */ }
        }
        return out;
    }

    // -------------------------------------------------------------------------------------------- public API

    async function startJournal() {
        if (!started) {
            started = true;
            for (const t of EVENT_TYPES) doc.addEventListener(t, onEvent, true);
            doc.addEventListener('wheel', onEvent, { capture: true, passive: true });
            doc.addEventListener('pointerdown', onEvent, { capture: true, passive: true });
            if (win.addEventListener) {
                win.addEventListener('storage', onStorage);
                win.addEventListener('pagehide', () => { onLifecycle(); store.remove(HEARTBEAT_PREFIX + loadId); });
                win.addEventListener('pageshow', (e) => { if (e && e.persisted) resumeLiveness(); });
            }
            doc.addEventListener('visibilitychange', () => { if (doc.visibilityState === 'hidden') onLifecycle(); });
            if (Observer) {
                observer = new Observer(onMutations);
                observer.observe(doc.documentElement || doc.body, { subtree: true, attributes: true, attributeFilter: MUTATION_ATTRIBUTES });
            }
            startLiveness();
        }
        const sc = scan();
        if (sc) await removeStaleForeign(sc.stale);
        // Entries that survived from earlier page loads (reload / close / crash), described without their text: member,
        // incomplete flag, age, length and the class of the last character (M0 R6: tells 体温 from ねつ for the IME check).
        const survivors = [];
        if (sc) {
            for (const x of sc.items) {
                if (x.own) continue;
                const g = store.get(x.key);
                const e = g.ok && g.value != null ? parseEntry(g.value) : null;
                survivors.push(e ? { m: e.m, comp: !!e.comp, ageSec: Math.round((now() - e.at) / 1000), len: typeof e.val === 'string' ? e.val.length : 0, tail: tailClass(e.val) }
                                 : { m: null, comp: false, ageSec: null, len: null, tail: 'unreadable' });
            }
        }
        let roundTrip;
        const rk = ROUND_TRIP_PREFIX + loadId;
        const w = store.set(rk, 'x'.repeat(64));
        if (!w.ok) roundTrip = w.error;
        else { const g = store.get(rk); roundTrip = g.ok && g.value === 'x'.repeat(64) ? 'ok' : (g.ok ? 'mismatch' : g.error); store.remove(rk); }
        let estimate = null;
        try {
            if (win.navigator && win.navigator.storage && win.navigator.storage.estimate) {
                const e = await win.navigator.storage.estimate();
                estimate = { usage: e.usage == null ? null : e.usage, quota: e.quota == null ? null : e.quota };
            }
        } catch (e) { estimate = { error: errorName(e) }; }
        const nav = win.navigator || {};
        return {
            load: loadId, format: FORMAT, isSecureContext: !!win.isSecureContext, webLocks: !!locks,
            storageEstimate: estimate, broadcastChannel: typeof win.BroadcastChannel === 'function',
            survivorCount: survivors.length, survivors,
            storageError: sc && !sc.partial ? null : (errors.length ? errors[errors.length - 1].error : 'unknown'), roundTrip,
            ua: nav.userAgent || null
        };
    }

    /**
     * Metadata of this namespace's unexpired entries (no values) and the page loads that hold them; the page load of an
     * entry is read from its key (S6). ok=false if any key was unreadable.
     */
    async function list(ns) {
        if (typeof ns !== 'string' || ns.length === 0) return { ok: false, error: 'namespace', entries: [], writers: {} };
        const sc = scan();
        if (!sc) return { ok: false, error: errors.length ? errors[errors.length - 1].error : 'unknown', entries: [], writers: {} };
        let partial = sc.partial;
        const entries = [];
        for (const x of sc.items) {
            if (x.key.indexOf(PREFIX + ns + '|') !== 0) continue;
            const g = store.get(x.key);
            if (!g.ok) { fail('read', g.error); partial = true; continue; }
            if (g.value == null) continue;
            const e = parseEntry(g.value);
            if (!e) continue;
            const load = loadOfKey(x.key);
            entries.push({ key: x.key, load, self: load === loadId, p: e.p, t: e.t, o: e.o, ctx: e.ctx, w: e.w, m: e.m,
                           k: e.k, fmt: e.fmt, cu: e.cu || null, co: e.co, bh: e.bh, g: e.g, rc: e.rc || null, at: e.at, seq: e.seq,
                           len: typeof e.val === 'string' ? e.val.length : 0, tr: !!e.tr, comp: !!e.comp });
        }
        entries.sort(evictionOrder);
        await removeStaleForeign(sc.stale);
        if (partial) return { ok: false, partial: true, error: errors.length ? errors[errors.length - 1].error : 'unknown', entries, writers: await writers() };
        return { ok: true, entries, writers: await writers() };
    }

    /**
     * The value of one entry as UTF-8 bytes (returned to an IJSStreamReference), only while its sequence is `seqWanted`
     * and it is younger than the retention; otherwise null (read failure included: the caller lists again). An expired
     * entry of this page load is removed; another page load's is only refused (removed only by removeStaleForeign).
     */
    function value(key, seqWanted) {
        if (typeof key !== 'string' || key.indexOf(PREFIX) !== 0) return null;
        const g = store.get(key);
        if (!g.ok) { fail('read', g.error); return null; }
        if (g.value == null) return null;
        const e = parseEntry(g.value);
        if (!e || (seqWanted != null && e.seq !== seqWanted)) return null;
        if (isExpired(e.at, now(), limits)) { if (isOwnKey(key)) removeKey(key, 'expired'); return null; }
        return new TextEncoder().encode(typeof e.val === 'string' ? e.val : '');
    }

    /** Pending work of this key here: a refused write, an incomplete copy, an open composition or an unanswered attempt. */
    function pendingFor(key) {
        const E = entries.get(key);
        if (E && isPending(E)) return 'pending';
        const g = store.get(key + COMPOSING_SUFFIX);
        if (!g.ok) return 'unknown';
        if (g.value != null) return 'pending';
        const s = latestState(key);
        if (s && (s.composing || hasOpenOperation(s) || (s.awaitMask && !s.awaitMask.answered))) return 'pending';
        return null;
    }

    /**
     * Retires entries after the server echoed them from a durable write. echo: an array parallel to keys of
     * { seq, val }, or an object keyed by key. The page load of each key is read from the KEY (S6). An entry is removed
     * only when its stored sequence AND value match the echo and:
     *   - this page load's entry: nothing newer of it is pending here;
     *   - another page load's entry (O1-A): a successful locks.query() shows that load's lock neither held nor requested,
     *     and its heartbeat is absent or stale. Without Web Locks, or when the query fails, it is kept.
     * Otherwise it is kept, with the reason. Returns { retired, kept }; when another page load's key is named and Web
     * Locks exist, the result comes as a Promise (the lock query is asynchronous), so callers await it.
     */
    function retire(keys, echo) {
        const list = Array.isArray(keys) ? keys : [];
        const foreign = list.some(k => typeof k === 'string' && k.indexOf(PREFIX) === 0 && !isOwnKey(k));
        if (foreign && locks && typeof locks.query === 'function') {
            return lockedLoads().then(locked => retireNow(list, echo, locked, locked ? null : 'lock-unknown'));
        }
        return retireNow(list, echo, null, 'no-web-locks');
    }
    function retireNow(keys, echo, locked, noLockReason) {
        const retired = [];
        const kept = [];
        for (let i = 0; i < keys.length; i++) {
            const key = keys[i];
            const want = Array.isArray(echo) ? echo[i] : (echo ? echo[key] : null);
            const load = loadOfKey(key);
            if (typeof key !== 'string' || key.indexOf(PREFIX) !== 0 || key.endsWith(COMPOSING_SUFFIX) || !want || !load) { kept.push({ key, reason: 'invalid' }); continue; }
            const own = load === loadId;
            if (!own) {
                // Liveness first, the entry last: the final read before the removal is the entry itself.
                if (!locked) { kept.push({ key, reason: noLockReason }); continue; }
                if (locked.has(load)) { kept.push({ key, reason: 'writer-locked' }); continue; }
                const hb = store.get(HEARTBEAT_PREFIX + load);
                if (!hb.ok) { fail('read', hb.error); kept.push({ key, reason: 'unknown' }); continue; }
                const hbAt = hb.value == null ? NaN : stampTime(hb.value);
                if (hb.value != null && isFinite(hbAt) && now() - hbAt < limits.writerAliveMs) { kept.push({ key, reason: 'writer-alive' }); continue; }
            }
            const g = store.get(key);
            if (!g.ok) { kept.push({ key, reason: g.error }); continue; }
            if (g.value == null) { kept.push({ key, reason: 'missing' }); continue; }
            const e = parseEntry(g.value);
            if (!e) { kept.push({ key, reason: 'unreadable' }); continue; }
            if (e.seq !== want.seq) { kept.push({ key, reason: 'sequence' }); continue; }
            const sameValue = e.val === want.val
                || (e.tr === true && typeof want.val === 'string' && want.val.length > limits.valueChars && truncate(want.val, limits.valueChars) === e.val);
            if (!sameValue) { kept.push({ key, reason: 'value' }); continue; }
            if (own) {
                const pending = pendingFor(key);
                if (pending) { kept.push({ key, reason: pending }); continue; }
            }
            if (removeKey(key, 'retired')) retired.push(key); else kept.push({ key, reason: 'remove-failed' });
        }
        return { retired, kept };
    }

    function validNamespace(ns) { return typeof ns === 'string' && ns.length > 0 && ns.indexOf('|') < 0; }

    /**
     * This page load's clear of one namespace (discard): its pending work of the namespace is cancelled, its field states
     * start again from the text shown, and its own entries and incomplete copies of the namespace are removed in ONE pass.
     * It writes no marker and sends nothing to other tabs; other page loads' entries stay. A refused removal is reported
     * and not retried (the caller may call clear again). Results: { ok: true, removed }; enumeration failure
     * { ok: false, removed: 0, error }; refused removals { ok: false, removed, failed, error }.
     */
    function clear(ns) {
        if (!validNamespace(ns)) return { ok: false, removed: 0, error: 'namespace' };
        supersede(ns);
        refreshStates(ns);
        const r = sweepPrefix(PREFIX + ns + '|' + loadId + '|');
        if (r.enumError) return { ok: false, removed: 0, error: r.enumError };
        if (r.failed === 0) return { ok: true, removed: r.removed };
        return { ok: false, removed: r.removed, failed: r.failed, error: r.error };
    }

    /**
     * Logoff (O2): one sweep of every entry and incomplete copy of the owner namespace, of every page load in this
     * browser, once, with no notice to other tabs. This page load's pending work of the namespace is cancelled first.
     * A refused removal is reported and not retried; a tab still open may write again afterwards (that entry stays until
     * it expires). Results: { ok, removed, own, others } (+ failed, error); enumeration failure
     * { ok: false, removed: 0, own: 0, others: 0, error }.
     */
    function logoff(ns) {
        if (!validNamespace(ns)) return { ok: false, removed: 0, own: 0, others: 0, error: 'namespace' };
        supersede(ns);
        refreshStates(ns);
        const r = sweepPrefix(PREFIX + ns + '|');
        if (r.enumError) return { ok: false, removed: 0, own: 0, others: 0, error: r.enumError };
        const out = { ok: r.failed === 0, removed: r.removed, own: r.own, others: r.removed - r.own };
        if (r.failed > 0) { out.failed = r.failed; out.error = r.error; }
        return out;
    }

    /** Attributed editors in the DOM, grouped by view and editing context (inactive MDI tabs keep their editors). */
    function coverage() {
        const groups = new Map();
        let invalid = 0;
        for (const root of doc.querySelectorAll('[' + DESCRIPTOR_ATTRIBUTE + ']')) {
            const d = parseDescriptor(root.getAttribute(DESCRIPTOR_ATTRIBUTE));
            if (!d) { invalid++; continue; }
            const id = d.w + '|' + d.ctx;
            if (!groups.has(id)) groups.set(id, { w: d.w, ctx: d.ctx, members: [] });
            const field = fieldOf(root, null);
            groups.get(id).members.push({ m: d.m, k: d.k, co: d.co, g: d.g, field: !!field, tag: (root.tagName || '').toLowerCase() });
        }
        return { groups: Array.from(groups.values()), invalid };
    }

    function report() {
        return { load: loadId, stats: Object.assign({}, stats), errors: errors.slice(), lockHeld, pendingWrites: pendingCount() };
    }

    return { start: startJournal, list, value, retire, clear, logoff, coverage, report, loadId };
}

// ------------------------------------------------------------------------------------------------ module entry points

let shared = null;
function journal() {
    if (!shared) shared = createJournal({ window: globalThis });
    return shared;
}

/** Installs the listeners once per page and returns the value-free platform line. Idempotent. */
export function start() { return journal().start(); }
export function list(ns) { return journal().list(ns); }
export function value(key, seq) { return journal().value(key, seq); }
export function retire(keys, echo) { return journal().retire(keys, echo); }
export function clear(ns) { return journal().clear(ns); }
export function logoff(ns) { return journal().logoff(ns); }
export function coverage() { return journal().coverage(); }
export function report() { return journal().report(); }
