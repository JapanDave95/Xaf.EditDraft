// Client-side input journal, M1b-C: per-tab clears, per-load budget, capture time, O1-A foreign cleanup, logoff (owner
// rulings 2026-10-04; docs/edit-draft-client-journal-per-tab-clears-2026-10-04.md). Collaborator run
// 2026-10-04-journal-m1b-c-32c9f1. The expectations come from Codex's requirement-only `tests` call of this run
// (N1-N38, stated before any code was shown to it); each test names the N-id it covers. Written before the run against
// the HEAD 9dcf84ab module (red-before) and the candidate (green-after); results in the M1b-C write-up.
//
// Choices where Codex named two readings (recorded in the write-up, owner may re-rule):
//   - physical expiry at exactly 60 minutes: "60 minutes old or older" (the rule T72 pins and Core IsExpired uses);
//   - a lock request still waiting for its grant counts as a live writer (stricter than "held");
//   - logoff result: { ok, removed, own, others } (+ failed, error); it cancels this page load's pending work too;
//   - retire of another load's key returns a Promise when Web Locks exist (the lock query is asynchronous).
'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const h = require('../helpers/harness');

const PREFIX = 'XafEditDraft.j1|';
const HB = 'XafEditDraft.hb1|';
const LEGACY = 'XafEditDraft.clr1|';
const LOCK = 'XafEditDraft.w|';
const MIN = 60000;

/**
 * One origin's Web Locks, shared by the tabs that get it. hold: new requests wait for grantAll(); reject: query() fails;
 * beforeResolve: runs once after a query took its snapshot and before the caller sees it (a writer resuming meanwhile).
 */
function lockManager() {
    const m = {
        held: new Set(), pending: [], requests: [], hold: false, reject: false, beforeResolve: null, queries: 0,
        request(name, cb) {
            m.requests.push(name);
            return new Promise((resolve) => {
                const grant = () => Promise.resolve().then(() => { m.held.add(name); resolve(cb({ name })); });
                if (m.hold) m.pending.push({ name, grant }); else grant();
            });
        },
        query() {
            m.queries++;
            if (m.reject) return Promise.reject(h.named('InvalidStateError'));
            const snap = { held: Array.from(m.held).map(name => ({ name })), pending: m.pending.map(p => ({ name: p.name })) };
            return Promise.resolve().then(() => { const f = m.beforeResolve; m.beforeResolve = null; if (f) f(); return snap; });
        },
        grantAll() { for (const p of m.pending.splice(0)) p.grant(); },
        release(name) { m.held.delete(name); }
    };
    return m;
}

function keyOf(ns, load, m, g, comp) { return PREFIX + [ns, load, h.CTX, m, String(g || 1)].join('|') + (comp ? '|c' : ''); }

function entryText(o) {
    const e = { at: o.at, f: 1, seq: o.seq || 1, ns: o.ns || 'ns1', load: o.jsonLoad || o.load, p: 'p', t: 't', o: null, ctx: h.CTX, w: 'v',
                m: o.m || 'S', k: 'memo', fmt: null, co: false, bh: null, g: o.g || 1, val: o.val === undefined ? 'x' : o.val, tr: false };
    if (o.comp) e.comp = true;
    return JSON.stringify(e);
}

/** Puts one entry of page load o.load straight into storage (as a page load that wrote it earlier would have). */
function seed(w, o) {
    const k = keyOf(o.ns || 'ns1', o.load, o.m || 'S', o.g, o.comp);
    w.storage.map.set(k, entryText(o));
    return k;
}

function open(w, name, loadId, locks, limits) {
    return h.openTab(w, { name, loadId, locks: locks === undefined ? null : locks, limits });
}

/** A key of page load `load`, decided independently of the module: entry/copy load segment, heartbeat, round trip. */
function ownedBy(load) {
    return (k) => {
        if (k === HB + load || k === 'XafEditDraft.rt1|' + load) return true;
        if (k.indexOf(PREFIX) !== 0) return false;
        return k.slice(PREFIX.length).split('|')[1] === load;
    };
}

function snapshot(w, pred) {
    const out = {};
    for (const [k, v] of w.storage.map) if (pred(k)) out[k] = v;
    return out;
}

function opsBy(w, tab, from) { return w.storage.log.slice(from || 0).filter(x => x.tab === tab); }
function refuse(w, key) { w.storage.failKeys.push({ prefix: key, ops: ['setItem'], name: 'QuotaExceededError' }); }
function allow(w) { w.storage.failKeys = []; }

function typed(t, d, value) {
    const ed = h.addEditor(t, { d, value: '' });
    ed.field.focus();
    h.type(ed, value);
    return ed;
}

function pageshow(tab, persisted) {
    let e;
    try { e = new tab.win.PageTransitionEvent('pageshow', { persisted }); }
    catch (x) { e = new tab.win.Event('pageshow'); Object.defineProperty(e, 'persisted', { value: persisted }); }
    tab.win.dispatchEvent(e);
}

/** Wraps the shared storage's per-call check (runs before every storage call, from any tab). */
function onCheck(w, fn) {
    const orig = w.storage.check.bind(w.storage);
    w.storage.check = (op, k) => { fn(op, k); return orig(op, k); };
    return () => { w.storage.check = orig; };
}

// ------------------------------------------------------------------------------------------------ identity and liveness

test('N1 one identity per page load: start() again does not duplicate capture; a new page load has its own id and keys and reports the earlier one value-free', async () => {
    const w = h.world();
    const a = await open(w, 'A', 'loada');
    assert.equal((await a.journal.start()).load, 'loada');
    const ed = h.addEditor(a, { value: '' });
    ed.field.focus();
    const from = w.storage.log.length;
    h.type(ed, 'first load text');
    assert.equal(w.storage.log.slice(from).filter(x => x.op === 'set' && x.key === ed.key()).length, 1, 'one write per change');
    const a2 = await open(w, 'A2', 'loada2');
    const e2 = typed(a2, { m: 'Other' }, 'y');
    assert.ok(ownedBy('loada2')(e2.key()));
    assert.equal(a2.platform.survivorCount, 1);
    assert.ok(!JSON.stringify(a2.platform).includes('first load text'));
    a.close(); a2.close();
});

test('N2 a page load holds its Web Lock and its own heartbeat; its clear keeps the lock; page hide removes only its own heartbeat', async () => {
    const w = h.world();
    const locks = lockManager();
    const a = await open(w, 'A', 'loada', locks);
    const b = await open(w, 'B', 'loadb', locks);
    assert.ok(locks.held.has(LOCK + 'loada'));
    assert.equal(a.journal.report().lockHeld, true);
    assert.equal(w.storage.map.get(HB + 'loada'), String(w.clock.now));
    typed(a, {}, 'x');
    a.journal.clear('ns1');
    assert.ok(locks.held.has(LOCK + 'loada'), 'a clear does not release the lock');
    assert.equal(a.journal.loadId, 'loada');
    a.win.dispatchEvent(new a.win.Event('pagehide'));
    assert.ok(!w.storage.map.has(HB + 'loada'));
    assert.ok(w.storage.map.has(HB + 'loadb'), 'never another load\'s heartbeat');
    a.close(); b.close();
});

test('N2b a page restored from the back/forward cache writes its heartbeat at once and requests its lock again when a query shows it is not held', async () => {
    const w = h.world();
    const locks = lockManager();
    const a = await open(w, 'A', 'loada', locks);
    a.win.dispatchEvent(new a.win.Event('pagehide'));
    locks.release(LOCK + 'loada');                     // released while the page was cached
    w.clock.now += 5000;
    pageshow(a, true);
    assert.equal(w.storage.map.get(HB + 'loada'), String(w.clock.now), 'heartbeat written synchronously on page show');
    await h.flush();
    assert.ok(locks.held.has(LOCK + 'loada'), 'lock requested again');
    const n = locks.requests.length;
    pageshow(a, false);
    await h.flush();
    assert.equal(locks.requests.length, n, 'a show that is not a restore changes nothing');
    a.close();
});

// ------------------------------------------------------------------------------------------------ ownership audit

test('N3 mutation audit: capture, a retry, an emptied field, eviction, list and clear(ns1) in one page load write and remove only that load\'s keys (no Web Locks; every other load alive)', async () => {
    for (const withLocks of [false, true]) {
        const w = h.world();
        const now = w.clock.now;
        const locks = withLocks ? lockManager() : null;
        seed(w, { load: 'loadb', m: 'B1', at: now - 1000 });
        seed(w, { load: 'loadb', m: 'B2', at: now - 2 * 60 * MIN });                 // expired
        seed(w, { load: 'loadb', m: 'B3', at: now - 1000, comp: true });
        seed(w, { ns: 'ns2', load: 'loadb', m: 'B4', at: now - 1000 });
        seed(w, { load: 'loadz', m: 'Z1', at: now - 2 * 60 * MIN });                 // a closed page load, expired
        w.storage.map.set(HB + 'loadz', String(now - 2 * 60 * MIN));
        seed(w, { load: 'loadaa', m: 'AA', at: now - 2 * 60 * MIN });                // a load id that starts with A's
        w.storage.map.set(LEGACY + 'ns1', String(now - 2 * 60 * MIN) + '|loadb|1');
        w.storage.map.set(LEGACY + 'ns2', 'garbage');
        // GUARD: 'CareCrew_InputJournal' is a real application's own localStorage key outside the library's prefix; the
        // library must never touch it.
        w.storage.map.set('CareCrew_InputJournal', '{"other":"journal"}');
        w.storage.map.set('XafEditDraft.j0|ns1|loada|' + h.CTX + '|Old|1', '{"at":1,"val":"keep"}');
        w.storage.map.set('XafEditDraft.j2|ns1|loada|' + h.CTX + '|New|1', '{"at":1,"val":"keep"}');
        const ns10 = seed(w, { ns: 'ns10', load: 'loada', m: 'Prefix', at: now + 10 * MIN });   // A's own key, namespace "ns10", newest
        if (withLocks) for (const l of ['loadb', 'loadz', 'loadaa']) locks.held.add(LOCK + l);   // every other load alive
        const others = snapshot(w, k => !ownedBy('loada')(k));
        const a = await open(w, 'A', 'loada', locks, { entries: 4 });
        const e1 = typed(a, { m: 'One' }, 'one');
        refuse(w, e1.key());
        h.type(e1, 'one more');
        allow(w);
        await w.clock.advance(1000);                    // the retry
        h.type(e1, '', 'Backspace');                    // emptied
        for (const m of ['Two', 'Three', 'Four', 'Five']) { typed(a, { m }, m); w.clock.now += 1; }   // past A's budget of 4
        await a.journal.list('ns1');
        a.journal.clear('ns1');
        await w.clock.advance(5000);
        const label = withLocks ? 'with locks' : 'no locks';
        for (const [k, v] of Object.entries(others)) assert.equal(w.storage.map.get(k), v, label + ': untouched ' + k);
        assert.deepEqual(opsBy(w, 'A').filter(x => !ownedBy('loada')(x.key)), [], label + ': A wrote or removed only its own keys');
        assert.ok(w.storage.map.has(ns10), label + ': clear(ns1) does not reach namespace ns10');
        assert.ok(a.journal.report().stats.evicted > 0, label + ': eviction ran');
    }
});

// ------------------------------------------------------------------------------------------------ per-tab clears

test('N4 independent clears: one tab\'s clear(ns1) removes only its own ns1 entry and copy (removed:2), cancels only its own pending ns1 work and writes no marker; the other tab\'s keys stay byte for byte and its pending write lands with its own capture time', async () => {
    for (const [clearer, other] of [['A', 'B'], ['B', 'A']]) {
        const w = h.world();
        const t = { A: await open(w, 'A', 'loada'), B: await open(w, 'B', 'loadb') };
        const ed = {};
        for (const n of ['A', 'B']) {
            const main = typed(t[n], { m: 'Main' }, n + ' text');
            const comp = h.addEditor(t[n], { d: { m: 'Comp' }, value: '' });
            comp.field.focus();
            h.composition(comp, 'compositionstart');
            h.input(comp, n + 'た', true);             // incomplete copy only
            w.clock.now += 10;
            const pend = h.addEditor(t[n], { d: { m: 'Pend' }, value: '' });
            pend.field.focus();
            refuse(w, pend.key());
            h.type(pend, n + ' pending');
            ed[n] = { main, comp, pend, pendAt: w.clock.now };
            w.clock.now += 10;
        }
        const keep = typed(t[clearer], { ns: 'ns2', m: 'Keep' }, 'kept ns2');
        const keep2 = h.addEditor(t[clearer], { d: { ns: 'ns2', m: 'Keep2' }, value: '' });
        keep2.field.focus();
        refuse(w, keep2.key());
        h.type(keep2, 'pending ns2');
        const otherLoad = t[other].journal.loadId;
        const before = snapshot(w, k => k.indexOf(PREFIX) === 0 && ownedBy(otherLoad)(k));
        assert.equal(Object.keys(before).length, 2);
        w.clock.now += 10;
        assert.deepEqual(t[clearer].journal.clear('ns1'), { ok: true, removed: 2 }, clearer + ' clears');
        assert.equal(ed[clearer].main.stored(), undefined);
        assert.equal(ed[clearer].comp.composing(), undefined);
        for (const [k, v] of Object.entries(before)) assert.equal(w.storage.map.get(k), v, 'the other tab\'s key is untouched: ' + k);
        assert.ok(!Array.from(w.storage.map.keys()).some(k => k.indexOf(LEGACY) === 0), 'no marker');
        w.clock.now += 10;
        allow(w);
        await t.A.deliver(); await t.B.deliver();
        await w.clock.advance(5000);
        ed[clearer].pend.field.blur(); ed[other].pend.field.blur();
        assert.equal(ed[clearer].pend.stored(), undefined, 'the clearing tab\'s pending ns1 write never lands');
        assert.equal(ed[other].pend.stored().val, other + ' pending', 'the other tab\'s pending write lands');
        assert.equal(ed[other].pend.stored().at, ed[other].pendAt, 'with its own capture time');
        assert.equal(keep.stored().val, 'kept ns2');
        assert.equal(keep2.stored().val, 'pending ns2', 'the clearing tab\'s pending ns2 work is not cancelled');
        t.A.close(); t.B.close();
    }
});

test('N5 simultaneous clears: B clears while A\'s clear is between its enumeration and its removals; each removes only its own two keys; held events, in either order, change nothing', async () => {
    for (const order of ['in order', 'reversed']) {
        const w = h.world();
        const a = await open(w, 'A', 'loada');
        const b = await open(w, 'B', 'loadb');
        const eds = [typed(a, { m: 'A1' }, 'a1'), typed(a, { m: 'A2' }, 'a2'), typed(b, { m: 'B1' }, 'b1'), typed(b, { m: 'B2' }, 'b2')];
        const pa = h.addEditor(a, { d: { m: 'AP' }, value: '' }); pa.field.focus(); refuse(w, pa.key()); h.type(pa, 'ap');
        const pb = h.addEditor(b, { d: { m: 'BP' }, value: '' }); pb.field.focus(); refuse(w, pb.key()); h.type(pb, 'bp');
        const ka = typed(a, { ns: 'ns2', m: 'K' }, 'ka');
        const kb = typed(b, { ns: 'ns2', m: 'K' }, 'kb');
        let rb = null;
        const restore = onCheck(w, (op) => { if (op === 'removeItem' && rb === null) { rb = 'running'; rb = b.journal.clear('ns1'); } });
        const ra = a.journal.clear('ns1');
        restore();
        assert.deepEqual(ra, { ok: true, removed: 2 }, order + ': A');
        assert.deepEqual(rb, { ok: true, removed: 2 }, order + ': B');
        for (const e of eds) assert.equal(e.stored(), undefined, order + ': ' + e.d.m);
        allow(w);
        if (order === 'reversed') { a.pendingEvents.reverse(); b.pendingEvents.reverse(); }
        await a.deliver(); await b.deliver();
        await w.clock.advance(5000);
        assert.equal(pa.stored(), undefined, order + ': A\'s pending work stays cancelled');
        assert.equal(pb.stored(), undefined, order + ': B\'s pending work stays cancelled');
        assert.equal(ka.stored().val, 'ka');
        assert.equal(kb.stored().val, 'kb');
        a.close(); b.close();
    }
});

test('N6 an empty clear and a repeated clear return {ok:true, removed:0}, still cancel pending work, and a later edit is journaled', async () => {
    const w = h.world();
    const a = await open(w, 'A', 'loada');
    const p = h.addEditor(a, { d: { m: 'P' }, value: '' });
    p.field.focus();
    refuse(w, p.key());
    h.type(p, 'pending');
    assert.deepEqual(a.journal.clear('ns1'), { ok: true, removed: 0 });
    assert.deepEqual(a.journal.clear('ns1'), { ok: true, removed: 0 });
    assert.equal(a.journal.report().pendingWrites, 0);
    allow(w);
    await w.clock.advance(5000);
    p.field.blur();
    assert.equal(p.stored(), undefined);
    p.field.focus();
    h.type(p, 'pending, then new');
    assert.equal(p.stored().val, 'pending, then new');
    a.close();
});

test('N7 an enumeration failure (at the first key, at a later key, or with no storage) gives {ok:false, removed:0, error}; the entries stay; pending work is still cancelled and nothing is retried', async () => {
    for (const how of ['first', 'later']) {
        const w = h.world();
        const a = await open(w, 'A', 'loada');
        const e1 = typed(a, { m: 'E1' }, 'one');
        const e2 = typed(a, { m: 'E2' }, 'two');
        const p = h.addEditor(a, { d: { m: 'P' }, value: '' }); p.field.focus(); refuse(w, p.key()); h.type(p, 'pending');
        let calls = 0;
        const restore = onCheck(w, (op) => { if (op === 'key' && (how === 'first' || ++calls > 1)) throw h.named('SecurityError'); });
        const r = a.journal.clear('ns1');
        restore();
        assert.deepEqual(r, { ok: false, removed: 0, error: 'SecurityError' }, how);
        assert.equal(e1.stored().val, 'one');
        assert.equal(e2.stored().val, 'two');
        allow(w);
        await w.clock.advance(5000);
        p.field.blur();
        assert.equal(p.stored(), undefined, how + ': cancelled');
        assert.equal(a.journal.report().pendingWrites, 0);
        a.close();
    }
    const w2 = h.world();
    const t2 = await open(w2, 'A', 'loada');
    const n = await h.openTab(w2, { name: 'N', loadId: 'loadn', storage: null });
    assert.deepEqual(n.journal.clear('ns1'), { ok: false, removed: 0, error: 'StorageUnavailable' });
    t2.close(); n.close();
});

test('N8 a refused removal during clear: {ok:false, removed:2, failed:1, error}; the refused key keeps its exact bytes, cancelled work does not overwrite it, it is not retried; a second clear removes it', async () => {
    const w = h.world();
    const a = await open(w, 'A', 'loada');
    const k = [typed(a, { m: 'K1' }, 'one'), typed(a, { m: 'K2' }, 'two'), typed(a, { m: 'K3' }, 'three')];
    for (const e of k) { refuse(w, e.key()); e.field.focus(); h.type(e, e.field.value + ' more'); }
    const bytes = w.storage.map.get(k[2].key());
    let attempts = 0;
    const restore = onCheck(w, (op, key) => { if (op === 'removeItem' && key === k[2].key()) { attempts++; throw h.named('SecurityError'); } });
    assert.deepEqual(a.journal.clear('ns1'), { ok: false, removed: 2, failed: 1, error: 'SecurityError' });
    allow(w);
    await w.clock.advance(5000);
    await a.deliver();
    k[2].field.blur();
    assert.equal(w.storage.map.get(k[2].key()), bytes, 'exact pre-clear bytes');
    assert.equal(attempts, 1, 'no automatic retry of the removal');
    restore();
    assert.deepEqual(a.journal.clear('ns1'), { ok: true, removed: 1 });
    assert.equal(k[2].stored(), undefined);
    a.close();
});

test('N9 (M1b-C3) after this page load\'s own clear: retyping exactly the text shown writes nothing; V -> W -> V writes W, then V with the later capture time', async () => {
    const w = h.world();
    const a = await open(w, 'A', 'loada');
    const ed = typed(a, {}, 'V');
    assert.deepEqual(a.journal.clear('ns1'), { ok: true, removed: 1 });
    w.clock.now += 10;
    const from = w.storage.log.length;
    h.input(ed, 'V', false);
    assert.equal(w.storage.log.slice(from).filter(x => x.op === 'set' && x.key === ed.key()).length, 0, 'the shown text again: no write');
    w.clock.now += 10;
    h.type(ed, 'VW');
    assert.equal(ed.stored().val, 'VW');
    w.clock.now += 10;
    const tBack = w.clock.now;
    h.type(ed, 'V', 'Backspace');
    assert.equal(ed.stored().val, 'V', 'the return to V is written (no stale pre-clear de-duplication)');
    assert.equal(ed.stored().at, tBack);
    a.close();
});

test('N10 an emptied field is an entry with val "" and the capture time of the action, also when its first write was refused; value() hands out zero bytes', async () => {
    const w = h.world();
    const a = await open(w, 'A', 'loada');
    const ed = typed(a, {}, 'abcd');
    w.clock.now += 100;
    const t1 = w.clock.now;
    h.type(ed, '', 'Backspace');
    assert.equal(ed.stored().val, '');
    assert.equal(ed.stored().at, t1);
    const bytes = a.journal.value(ed.key(), ed.stored().seq);
    assert.ok(bytes !== null && bytes.length === 0, 'zero bytes, not null');
    const r = typed(a, { m: 'Refused' }, 'x');
    w.clock.now += 100;
    const t2 = w.clock.now;
    refuse(w, r.key());
    h.type(r, '', 'Backspace');
    allow(w);
    await w.clock.advance(1000);
    assert.equal(r.stored().val, '');
    assert.equal(r.stored().at, t2, 'the retry keeps the capture time');
    a.close();
});

test('N11 storage events only count: marker writes, entry additions and the removal of this tab\'s entry, delivered repeated and reordered, cause no write or removal here and cancel nothing', async () => {
    const w = h.world();
    const a = await open(w, 'A', 'loada');
    const b = await open(w, 'B', 'loadb');
    const ea = typed(a, { m: 'Kept' }, 'kept');
    const ep = h.addEditor(a, { d: { m: 'Pend' }, value: '' });
    ep.field.focus();
    refuse(w, ep.key());
    const tp = w.clock.now;
    h.type(ep, 'pending');
    const bv = w.storage.view(b);
    bv.setItem(LEGACY + 'ns1', String(w.clock.now) + '|loadb|1');
    bv.setItem(LEGACY + 'ns1', String(w.clock.now + 1) + '|loadb|2');
    bv.setItem(keyOf('ns1', 'loadb', 'BX'), entryText({ load: 'loadb', m: 'BX', at: w.clock.now }));
    bv.removeItem(ea.key());                            // A's entry removed elsewhere
    a.pendingEvents.push(...a.pendingEvents.slice().reverse());
    const from = w.storage.log.length;
    await a.deliver();
    assert.deepEqual(opsBy(w, 'A', from), [], 'no write or removal by A while handling events');
    assert.equal(ea.stored(), undefined, 'the removed entry is not written back');
    assert.ok(a.journal.report().stats.removedElsewhere >= 1);
    assert.equal(a.journal.report().pendingWrites, 1, 'the pending intent is not cancelled');
    allow(w);
    await w.clock.advance(1000);
    assert.equal(ep.stored().val, 'pending');
    assert.equal(ep.stored().at, tp);
    a.close(); b.close();
});

test('N12 legacy clear markers (recent, old, future-dated, malformed) are never read, written or removed, and change nothing: capture, retry, list and clear work as without them', async () => {
    const w = h.world();
    const now = w.clock.now;
    const markers = { [LEGACY + 'ns1']: String(now) + '|loadb|1', [LEGACY + 'ns2']: String(now - 3 * 60 * MIN) + '|loadb|1',
                      [LEGACY + 'ns3']: String(now + 60 * MIN) + '|x|9', [LEGACY + 'ns4']: 'not a marker' };
    for (const [k, v] of Object.entries(markers)) w.storage.map.set(k, v);
    const reads = [];
    w.storage.afterGet = (k, v, tab) => { if (tab === 'A' && k.indexOf(LEGACY) === 0) reads.push(k); };
    const a = await open(w, 'A', 'loada');
    const ed = typed(a, {}, 'one');
    refuse(w, ed.key());
    h.type(ed, 'two');
    allow(w);
    await w.clock.advance(1000);
    assert.equal(ed.stored().val, 'two');
    a.pendingEvents.push({ key: LEGACY + 'ns1', oldValue: markers[LEGACY + 'ns1'], newValue: String(now + 5) + '|loadb|7' });
    await a.deliver();
    h.type(ed, 'three');
    assert.equal(ed.stored().val, 'three', 'a marker event suppresses nothing');
    const l = await a.journal.list('ns1');
    assert.equal(l.ok, true);
    assert.equal(l.entries.length, 1);
    assert.deepEqual(a.journal.clear('ns1'), { ok: true, removed: 1 });
    w.storage.afterGet = null;
    for (const [k, v] of Object.entries(markers)) assert.equal(w.storage.map.get(k), v, k);
    assert.deepEqual(reads, [], 'no marker read');
    assert.deepEqual(opsBy(w, 'A').filter(x => x.key.indexOf(LEGACY) === 0), [], 'no marker write or removal');
    a.close();
});

// ------------------------------------------------------------------------------------------------ per-load budget

test('N13 per-load budget: with 59 own keys the 60th write evicts nothing; the 61st evicts this load\'s oldest key; older keys of another page load are never evicted; rewriting a key adds nothing', async () => {
    const w = h.world();
    const now = w.clock.now;
    const foreign = [];
    for (let i = 0; i < 10; i++) foreign.push(seed(w, { ns: 'seedns', load: 'loadb', m: 'B' + String(i).padStart(2, '0'), at: now - 50 * MIN + i }));
    const own = [];
    for (let i = 0; i < 59; i++) own.push(seed(w, { ns: 'seedns', load: 'loada', m: 'A' + String(i).padStart(2, '0'), at: now - 40 * MIN + i }));
    const a = await open(w, 'A', 'loada');
    const aKeys = () => w.storage.entryKeys(true).filter(ownedBy('loada'));
    typed(a, { m: 'New60' }, 'sixty');
    assert.equal(aKeys().length, 60, '60: nothing evicted');
    assert.ok(own.every(k => w.storage.map.has(k)));
    const e61 = typed(a, { m: 'New61' }, 'sixty-one');
    assert.equal(aKeys().length, 60);
    assert.ok(!w.storage.map.has(own[0]), 'this load\'s oldest went');
    assert.ok(w.storage.map.has(own[1]));
    assert.ok(foreign.every(k => w.storage.map.has(k)), 'another load\'s older keys stay');
    const evicted = a.journal.report().stats.evicted;
    h.type(e61, 'sixty-one again');
    assert.equal(aKeys().length, 60);
    assert.equal(a.journal.report().stats.evicted, evicted, 'a rewrite evicts nothing');
    a.close();
});

test('N14 per-load serialized budget: own key + stored JSON totals of 1,048,575 and 1,048,576 evict nothing; 1,048,577 evicts this load\'s oldest key; another load\'s key is never evicted', async () => {
    const w0 = h.world();
    const t0 = await open(w0, 'A', 'loada');
    const n0 = typed(t0, { m: 'New' }, 'n');
    const newSize = n0.key().length + w0.storage.map.get(n0.key()).length;
    t0.close();
    for (const total of [1048575, 1048576, 1048577]) {
        const w = h.world();
        const now = w.clock.now;
        const foreign = seed(w, { load: 'loadb', m: 'Foreign', at: now - 50 * MIN, val: '\u0001'.repeat(12000) });
        let sum = 0;
        const big = [];
        for (let i = 0; i < 14; i++) {
            const k = seed(w, { load: 'loada', m: 'Big' + String(i).padStart(2, '0'), at: now - 30 * MIN + i, val: '\u0001'.repeat(12000) });
            big.push(k);
            sum += k.length + w.storage.map.get(k).length;
        }
        const padKey = keyOf('ns1', 'loada', 'Pad');
        const base = padKey.length + entryText({ load: 'loada', m: 'Pad', at: now - 40 * MIN, val: '' }).length;
        const rest = total - newSize - sum - base;
        const q = Math.floor(rest / 6);
        const val = '\u0001'.repeat(q) + 'x'.repeat(rest - 6 * q);
        assert.ok(val.length <= 12000);
        seed(w, { load: 'loada', m: 'Pad', at: now - 40 * MIN, val });
        const a = await open(w, 'A', 'loada');
        const n = typed(a, { m: 'New' }, 'n');
        const ownTotal = () => w.storage.entryKeys(true).filter(ownedBy('loada')).reduce((s, k) => s + k.length + w.storage.map.get(k).length, 0);
        assert.ok(w.storage.map.has(foreign), total + ': another load\'s key stays');
        assert.ok(n.stored(), total + ': the new entry is stored');
        assert.ok(big.every(k => w.storage.map.has(k)), total + ': the newer own keys stay');
        if (total <= 1048576) {
            assert.equal(ownTotal(), total, total + ': nothing evicted');
            assert.ok(w.storage.map.has(padKey));
        } else {
            assert.ok(!w.storage.map.has(padKey), total + ': this load\'s oldest key went');
            assert.ok(ownTotal() <= 1048576);
        }
        a.close();
    }
});

test('N15 eviction is oldest first by capture time, then key: a value captured early and written late by a retry goes before one captured later; another load\'s older key stays', async () => {
    const w = h.world();
    const now = w.clock.now;
    const foreign = seed(w, { load: 'loadb', m: 'Old', at: now - 30 * MIN });
    const a = await open(w, 'A', 'loada', null, { entries: 3 });
    const x = h.addEditor(a, { d: { m: 'X' }, value: '' });
    x.field.focus();
    refuse(w, x.key());
    const t1 = w.clock.now;
    h.type(x, 'x');                                     // captured at t1, refused
    w.clock.now += 100;
    const y = typed(a, { m: 'Y' }, 'y');                // captured and written at t1 + 100
    w.clock.now += 100;
    allow(w);
    await w.clock.advance(1000);                        // x is written now, by its retry
    assert.equal(x.stored().at, t1);
    w.clock.now += 100;
    typed(a, { m: 'Z' }, 'z');
    w.clock.now += 100;
    typed(a, { m: 'V' }, 'v');                          // 4 keys: the oldest by capture time goes
    assert.equal(x.stored(), undefined, 'x (captured first) went');
    assert.equal(y.stored().val, 'y');
    assert.ok(w.storage.map.has(foreign));
    // equal capture times: the smaller key goes
    const w2 = h.world();
    const n2 = w2.clock.now;
    const tieZ = seed(w2, { load: 'loada', m: 'zz', at: n2 - 1000 });
    const tieA = seed(w2, { load: 'loada', m: 'aa', at: n2 - 1000 });
    const t2 = await open(w2, 'A', 'loada', null, { entries: 2 });
    typed(t2, { m: 'C' }, 'c');
    assert.ok(!w2.storage.map.has(tieA));
    assert.ok(w2.storage.map.has(tieZ));
    a.close(); t2.close();
});

test('N16 one budget per page load across its namespaces and incomplete copies; after a reload the new page load neither counts nor evicts the earlier load\'s keys', async () => {
    const w = h.world();
    const a = await open(w, 'A', 'loada', null, { entries: 4 });
    const y = typed(a, { ns: 'ns2', m: 'Y' }, 'y');
    w.clock.now += 10;
    const x = typed(a, { m: 'X' }, 'x');
    w.clock.now += 10;
    const c = h.addEditor(a, { d: { m: 'C' }, value: '' });
    c.field.focus();
    h.composition(c, 'compositionstart');
    h.input(c, 'cた', true);                            // an incomplete copy takes a slot
    w.clock.now += 10;
    const z = typed(a, { m: 'Z' }, 'z');
    w.clock.now += 10;
    const v = typed(a, { m: 'V' }, 'v');                // fifth key: the oldest (ns2) goes
    assert.equal(y.stored(), undefined, 'the oldest key, in another namespace, went');
    assert.equal(x.stored().val, 'x');
    assert.equal(c.composing().val, 'cた');
    assert.equal(z.stored().val, 'z');
    assert.equal(v.stored().val, 'v');
    const earlier = snapshot(w, k => k.indexOf(PREFIX) === 0 && ownedBy('loada')(k));
    assert.equal(Object.keys(earlier).length, 4);
    a.close();                                          // reload
    const a2 = await open(w, 'A2', 'loada2', null, { entries: 4 });
    for (let i = 0; i < 6; i++) { typed(a2, { m: 'N' + i }, 'n' + i); w.clock.now += 1; }
    assert.equal(w.storage.entryKeys(true).filter(ownedBy('loada2')).length, 4);
    for (const [k, val] of Object.entries(earlier)) assert.equal(w.storage.map.get(k), val, 'the earlier load\'s key stays: ' + k);
    a2.close();
});

test('N17 at its budget and under a full shared quota, a refused new write and a refused replacement evict nothing, not even this load\'s own keys; the old bytes stay; the failure is reported and stays pending', async () => {
    const w = h.world();
    const now = w.clock.now;
    seed(w, { load: 'loadb', m: 'B', at: now - 1000 });
    const a = await open(w, 'A', 'loada', null, { entries: 2 });
    typed(a, { m: 'P' }, 'p');
    w.clock.now += 10;
    const q = typed(a, { m: 'Q' }, 'q');
    w.storage.map.set('HostData', 'x'.repeat(100));
    w.storage.capacity = w.storage.size() + 5;
    const before = snapshot(w, () => true);
    typed(a, { m: 'R' }, 'a new value');
    h.type(q, 'q replaced by a longer value');
    for (let i = 0; i < 7; i++) await w.clock.advance(1000);
    assert.deepEqual(snapshot(w, () => true), before, 'nothing removed or changed');
    assert.ok(a.journal.report().errors.some(e => e.op === 'write' && e.error === 'QuotaExceededError'));
    assert.equal(a.journal.report().pendingWrites, 2);
    a.close();
});

// ------------------------------------------------------------------------------------------------ capture time

test('N18 the stored at is the capture time: a value refused at t1 and written by a retry at t1 + 2000 is stored with at t1; an unchanged re-read does not move it; a new value has its own time', async () => {
    const w = h.world();
    const a = await open(w, 'A', 'loada');
    const ed = h.addEditor(a, { value: '' });
    ed.field.focus();
    const t1 = w.clock.now;
    w.storage.failSets = 2;
    h.type(ed, 'v');                                    // refused
    await w.clock.advance(1000);                        // retry 1: refused
    await w.clock.advance(1000);                        // retry 2: written
    assert.equal(ed.stored().val, 'v');
    assert.equal(ed.stored().at, t1);
    const seq = ed.stored().seq;
    w.clock.now += 5000;
    ed.field.blur(); ed.field.focus();
    assert.equal(ed.stored().seq, seq, 'not written again');
    assert.equal(ed.stored().at, t1);
    w.clock.now += 100;
    const t3 = w.clock.now;
    h.type(ed, 'vw');
    assert.equal(ed.stored().at, t3);
    a.close();
});

test('N21 a refused value of descriptor d0 completes under the d0 key with d0\'s descriptor and its capture time; the d1 value keeps its own key and time', async () => {
    const w = h.world();
    const a = await open(w, 'A', 'loada');
    const ed = h.addEditor(a, { value: '' });
    ed.field.focus();
    const d0Key = ed.key();
    const t0 = w.clock.now;
    refuse(w, d0Key);
    h.type(ed, 'B');
    ed.setDescriptor({ g: 2 });
    await h.flush();
    w.clock.now += 100;
    const t1 = w.clock.now;
    h.type(ed, 'BC');
    allow(w);
    await w.clock.advance(5000);
    const d0 = w.storage.entry(d0Key);
    assert.deepEqual([d0.val, d0.g, d0.at], ['B', 1, t0]);
    const d1 = ed.stored();
    assert.deepEqual([d1.val, d1.g, d1.at], ['BC', 2, t1]);
    a.close();
});

test('X14b (N22, M1b-C7) an older refused d0 value released after a newer d0 composition finished under d1 never removes the newer d0 incomplete copy', async () => {
    const w = h.world();
    const a = await open(w, 'A', 'loada');
    const ed = h.addEditor(a, { value: '' });
    ed.field.focus();
    const d0 = ed.key();
    w.storage.failSets = 1;
    h.type(ed, 'B');                                    // the older d0 value: refused, retry pending
    h.composition(ed, 'compositionstart');
    h.input(ed, 'Bた', true);                           // a newer d0 incomplete copy
    const copyBytes = w.storage.map.get(d0 + '|c');
    assert.ok(copyBytes);
    ed.setDescriptor({ g: 2 });
    await h.flush();
    h.input(ed, 'Bたい', true);                         // the composition goes on under d1
    ed.field.value = 'B体';
    h.composition(ed, 'compositionend', '体');          // and finishes under d1
    await w.clock.advance(0);
    assert.equal(ed.stored().val, 'B体');
    await w.clock.advance(5000);                        // the older d0 retry runs now
    assert.equal(w.storage.entry(d0).val, 'B');
    assert.equal(w.storage.map.get(d0 + '|c'), copyBytes, 'the newer d0 copy survives byte for byte');
    a.close();
});

// ------------------------------------------------------------------------------------------------ list, value, retire

test('N24 list() takes each entry\'s page load from its key, not from the stored JSON', async () => {
    const w = h.world();
    const now = w.clock.now;
    const bKey = seed(w, { load: 'loadb', jsonLoad: 'loada', m: 'ClaimsA', at: now - 1000, val: 'secret b' });
    const aKey = seed(w, { load: 'loada', jsonLoad: 'loadb', m: 'ClaimsB', at: now - 1000, val: 'secret a' });
    const a = await open(w, 'A', 'loada');
    const l = await a.journal.list('ns1');
    assert.equal(l.ok, true);
    const byKey = Object.fromEntries(l.entries.map(e => [e.key, e]));
    assert.deepEqual([byKey[bKey].load, byKey[bKey].self], ['loadb', false]);
    assert.deepEqual([byKey[aKey].load, byKey[aKey].self], ['loada', true]);
    assert.ok(!JSON.stringify(l).includes('secret'));
    a.close();
});

test('N26 value() refuses an entry 60 minutes old or older; an expired entry of another page load is not removed by it', async () => {
    const w = h.world();
    const now = w.clock.now;
    const expired = seed(w, { load: 'loadz', m: 'F', at: now - 60 * MIN, seq: 11, val: 'old' });
    const young = seed(w, { load: 'loadz', m: 'G', at: now - 60 * MIN + 1, seq: 12, val: '本日' });
    const a = await open(w, 'A', 'loada');
    assert.equal(a.journal.value(expired, 11), null);
    assert.ok(w.storage.map.has(expired), 'not removed by value()');
    assert.equal(new TextDecoder().decode(a.journal.value(young, 12)), '本日');
    assert.equal(a.journal.value(young, 13), null, 'another sequence');
    w.clock.now += 1;
    assert.equal(a.journal.value(young, 12), null, 'exactly 60 minutes');
    assert.ok(w.storage.map.has(young));
    a.close();
});

test('N27 retire of this page load\'s entry works without Web Locks; a refused removal is reported as kept (remove-failed), never as retired', async () => {
    const w = h.world();
    const a = await open(w, 'A', 'loada');
    const ed = typed(a, {}, 'v');
    const e = ed.stored();
    w.storage.failKeys = [{ prefix: ed.key(), ops: ['removeItem'], name: 'SecurityError' }];
    const r = a.journal.retire([ed.key()], [{ seq: e.seq, val: 'v' }]);
    assert.deepEqual(r.retired, []);
    assert.equal(r.kept[0].reason, 'remove-failed');
    assert.equal(ed.stored().val, 'v');
    allow(w);
    assert.deepEqual(a.journal.retire([ed.key()], [{ seq: e.seq, val: 'v' }]).retired, [ed.key()]);
    a.close();
});

test('N28 retire of another page load\'s entry (O1-A): removed only when a successful lock query shows its lock neither held nor requested AND its heartbeat is absent or stale; the load is read from the key', async () => {
    const w = h.world();
    const now = w.clock.now;
    const locks = lockManager();
    const mk = (load, m) => seed(w, { load, m, at: now - 1000, seq: 7, val: 'v' });
    const held = mk('loadh', 'Held'); locks.held.add(LOCK + 'loadh');
    const fresh = mk('loadf', 'Fresh'); w.storage.map.set(HB + 'loadf', String(now - 1000));
    const absent = mk('loadx', 'Absent');
    const stale = mk('loads', 'Stale'); w.storage.map.set(HB + 'loads', String(now - 120000));
    const forged = seed(w, { load: 'loadh', jsonLoad: 'loada', m: 'Forged', at: now - 1000, seq: 7, val: 'v' });
    const a = await open(w, 'A', 'loada', locks);
    const echo = [{ seq: 7, val: 'v' }];
    const one = async (k) => a.journal.retire([k], echo);
    let r = await one(held);
    assert.equal(r.kept[0].reason, 'writer-locked'); assert.ok(w.storage.map.has(held));
    r = await one(fresh);
    assert.equal(r.kept[0].reason, 'writer-alive'); assert.ok(w.storage.map.has(fresh));
    r = await one(absent);
    assert.deepEqual(r.retired, [absent]); assert.ok(!w.storage.map.has(absent));
    r = await one(stale);
    assert.deepEqual(r.retired, [stale]);
    r = await one(forged);
    assert.equal(r.kept[0].reason, 'writer-locked', 'a key naming loadh is loadh\'s, whatever its JSON says'); assert.ok(w.storage.map.has(forged));
    const q = mk('loadq', 'Q');
    locks.reject = true;
    r = await one(q);
    locks.reject = false;
    assert.equal(r.kept[0].reason, 'lock-unknown'); assert.ok(w.storage.map.has(q));
    const u = mk('loadu', 'U');
    w.storage.failKeys = [{ prefix: HB + 'loadu', ops: ['getItem'], name: 'SecurityError' }];
    r = await one(u);
    allow(w);
    assert.equal(r.kept[0].reason, 'unknown'); assert.ok(w.storage.map.has(u));
    a.close();
    // no Web Locks: kept, and the answer comes at once
    const w2 = h.world();
    const n2 = w2.clock.now;
    const x2 = seed(w2, { load: 'loadx', m: 'Absent', at: n2 - 1000, seq: 7, val: 'v' });
    const f2 = seed(w2, { load: 'loadz', jsonLoad: 'loada', m: 'Forged', at: n2 - 1000, seq: 7, val: 'v' });
    const a2 = await open(w2, 'A', 'loada', null);
    for (const k of [x2, f2]) {
        const r2 = a2.journal.retire([k], echo);
        assert.equal(typeof r2.then, 'undefined', 'synchronous without Web Locks');
        assert.equal(r2.kept[0].reason, 'no-web-locks');
        assert.ok(w2.storage.map.has(k));
    }
    a2.close();
});

test('N28b expiry of another page load\'s keys (O1-A): never listed; removed only when the lock query shows its lock not held and its heartbeat is absent or stale; with a failed query, no Web Locks or an unreadable heartbeat it stays', async () => {
    for (const mode of ['locks', 'query fails', 'no locks']) {
        const w = h.world();
        const now = w.clock.now;
        const old = now - 61 * MIN;
        const locks = mode === 'no locks' ? null : lockManager();
        const held = seed(w, { load: 'loadh', m: 'Held', at: old });
        if (locks) locks.held.add(LOCK + 'loadh');
        const fresh = seed(w, { load: 'loadf', m: 'Fresh', at: old }); w.storage.map.set(HB + 'loadf', String(now - 1000));
        const absent = seed(w, { load: 'loadx', m: 'Absent', at: old });
        const stale = seed(w, { load: 'loads', m: 'Stale', at: old }); w.storage.map.set(HB + 'loads', String(now - 120000));
        const unread = seed(w, { load: 'loadu', m: 'Unread', at: old });
        if (locks && mode === 'query fails') locks.reject = true;
        w.storage.failKeys = [{ prefix: HB + 'loadu', ops: ['getItem'], name: 'SecurityError' }];
        const a = await open(w, 'A', 'loada', locks);
        const l = await a.journal.list('ns1');
        allow(w);
        assert.deepEqual(l.entries.map(e => e.key), [], mode + ': expired entries are not listed');
        assert.ok(w.storage.map.has(held), mode + ': lock held');
        assert.ok(w.storage.map.has(fresh), mode + ': fresh heartbeat');
        assert.ok(w.storage.map.has(unread), mode + ': unreadable heartbeat');
        const removable = mode === 'locks';
        assert.equal(w.storage.map.has(absent), !removable, mode + ': no lock, no heartbeat');
        assert.equal(w.storage.map.has(stale), !removable, mode + ': no lock, stale heartbeat');
        a.close();
    }
});

test('N29 freshness thresholds: a heartbeat 119,999 ms old keeps another load\'s entry from retire, one 120,000 ms old does not; the writer report calls a heartbeat under 30 s alive and an older one unknown', async () => {
    const w = h.world();
    const now = w.clock.now;
    const locks = lockManager();
    const p = seed(w, { load: 'loadp', m: 'P', at: now - 1000, seq: 3, val: 'v' }); w.storage.map.set(HB + 'loadp', String(now - 119999));
    const q = seed(w, { load: 'loadq', m: 'Q', at: now - 1000, seq: 3, val: 'v' }); w.storage.map.set(HB + 'loadq', String(now - 120000));
    const r = seed(w, { load: 'loadr', m: 'R', at: now - 1000, seq: 3, val: 'v' }); w.storage.map.set(HB + 'loadr', String(now - 29999));
    const s = seed(w, { load: 'loads', m: 'S', at: now - 1000, seq: 3, val: 'v' }); w.storage.map.set(HB + 'loads', String(now - 30001));
    const a = await open(w, 'A', 'loada', locks);
    const echo = [{ seq: 3, val: 'v' }];
    const l = await a.journal.list('ns1');
    assert.equal(l.writers.loadr.alive, true);
    assert.equal(l.writers.loads.alive, null);
    assert.equal((await a.journal.retire([p], echo)).kept[0].reason, 'writer-alive');
    assert.deepEqual((await a.journal.retire([q], echo)).retired, [q]);
    assert.equal((await a.journal.retire([s], echo)).kept[0].reason, 'writer-alive', 'unknown in the report is not stale for retire');
    assert.ok(w.storage.map.has(r));
    a.close();
});

test('N30 expiry edge, reading "60 minutes old or older": own keys 3,599,999 ms old stay and 3,600,000 ms old go; another load\'s key at 3,600,000 ms goes only when its writer is gone', async () => {
    const w = h.world();
    const now = w.clock.now;
    const locks = lockManager();
    const young = seed(w, { load: 'loada', m: 'Young', at: now - 3599999 });
    const edge = seed(w, { load: 'loada', m: 'Edge', at: now - 3600000 });
    const gone = seed(w, { load: 'loadx', m: 'Gone', at: now - 3600000 });
    const alive = seed(w, { load: 'loadh', m: 'Alive', at: now - 3600000 });
    locks.held.add(LOCK + 'loadh');
    const a = await open(w, 'A', 'loada', locks);
    assert.ok(w.storage.map.has(young));
    assert.ok(!w.storage.map.has(edge));
    assert.ok(!w.storage.map.has(gone));
    assert.ok(w.storage.map.has(alive));
    a.close();
});

test('N31 a lock request still waiting for its grant counts as a live writer: its entry is kept while the request waits and after the grant; lockHeld is false until granted', async () => {
    const w = h.world();
    const locks = lockManager();
    locks.hold = true;
    const b = await open(w, 'B', 'loadb', locks);
    assert.equal(b.journal.report().lockHeld, false);
    const eb = typed(b, {}, 'b');
    const e = eb.stored();
    w.storage.map.delete(HB + 'loadb');
    locks.hold = false;
    const a = await open(w, 'A', 'loada', locks);
    const echo = [{ seq: e.seq, val: 'b' }];
    assert.equal((await a.journal.retire([eb.key()], echo)).kept[0].reason, 'writer-locked');
    assert.ok(eb.stored());
    locks.grantAll();
    await h.flush();
    assert.equal(b.journal.report().lockHeld, true);
    assert.equal((await a.journal.retire([eb.key()], echo)).kept[0].reason, 'writer-locked');
    a.close(); b.close();
});

test('N32 a writer that resumes from the back/forward cache while another tab checks it is not treated as gone: its page show writes the heartbeat before the check reads it (retire and expiry)', async () => {
    const w = h.world();
    const locks = lockManager();
    const a = await open(w, 'A', 'loada', locks);
    const b = await open(w, 'B', 'loadb', locks);
    const eb = typed(b, {}, 'b');
    const e = eb.stored();
    b.win.dispatchEvent(new b.win.Event('pagehide'));   // cached: heartbeat removed ...
    locks.release(LOCK + 'loadb');                     // ... and its lock released
    locks.beforeResolve = () => pageshow(b, true);     // B resumes after A's query took its snapshot
    const r = await a.journal.retire([eb.key()], [{ seq: e.seq, val: 'b' }]);
    assert.equal(r.kept[0].reason, 'writer-alive');
    assert.ok(eb.stored());
    await h.flush();
    assert.ok(locks.held.has(LOCK + 'loadb'), 'B holds its lock again');
    // expiry: the entry is old now; B goes into the cache again and resumes during A's expiry pass
    w.clock.now += 61 * MIN;
    b.win.dispatchEvent(new b.win.Event('pagehide'));
    locks.release(LOCK + 'loadb');
    w.storage.map.set(HB + 'loadb', String(w.clock.now - 61 * MIN));
    locks.beforeResolve = () => pageshow(b, true);
    await a.journal.list('ns1');
    assert.ok(w.storage.map.has(eb.key()), 'expiry leaves the resumed writer\'s entry');
    a.close(); b.close();
});

test('N33 (C1) a replacement stored while retire reads the writer\'s heartbeat survives: the entry is read after the liveness checks', async () => {
    for (const variant of ['new value', 'same value, new sequence']) {
        const w = h.world();
        const now = w.clock.now;
        const locks = lockManager();
        const k = seed(w, { load: 'loadb', m: 'R', at: now - 1000, seq: 11, val: 'V1' });
        const a = await open(w, 'A', 'loada', locks);
        const replacement = entryText({ load: 'loadb', m: 'R', at: now, seq: 12, val: variant === 'new value' ? 'V2' : 'V1' });
        let done = false;
        w.storage.afterGet = (key, v, tab) => {
            if (!done && tab === 'A' && key === HB + 'loadb') { done = true; w.storage.view({ name: 'B' }).setItem(k, replacement); }
        };
        const r = await a.journal.retire([k], [{ seq: 11, val: 'V1' }]);
        w.storage.afterGet = null;
        assert.equal(done, true, variant + ': the replacement ran during the heartbeat read');
        assert.equal(w.storage.map.get(k), replacement, variant);
        assert.deepEqual(r.retired, [], variant);
        assert.equal(r.kept[0].reason, 'sequence', variant);
        a.close();
    }
});

test('N34 (C2) a fresh value written over an expired entry after the expiry scan read it survives: the key is read again before its removal', async () => {
    for (const variant of ['new value', 'same value']) {
        const w = h.world();
        const now = w.clock.now;
        const locks = lockManager();
        const k = seed(w, { load: 'loadb', m: 'E', at: now - 61 * MIN, seq: 1, val: 'old' });
        const fresh = entryText({ load: 'loadb', m: 'E', at: now, seq: 2, val: variant === 'new value' ? 'new' : 'old' });
        let reads = 0;
        w.storage.afterGet = (key, v, tab) => { if (tab === 'A' && key === k && ++reads === 1) w.storage.view({ name: 'B' }).setItem(k, fresh); };
        const a = await open(w, 'A', 'loada', locks);
        w.storage.afterGet = null;
        assert.ok(reads >= 1);
        assert.equal(w.storage.map.get(k), fresh, variant);
        a.close();
    }
});

test('N34b residual: a fresh value written between the expiry pass\'s last read and its removal is removed (no compare-and-swap in localStorage; the remover cannot close this window). There is no self-repair (M1b-F), so the value stays lost', { todo: 'no self-repair; remover-side window accepted (owner ruling 2026-10-04 M1b-F: "Remove the self-repair entirely; keep G2 + G4; accept the C1 window as the residual")' }, async () => {
    const w = h.world();
    const now = w.clock.now;
    const locks = lockManager();
    const k = seed(w, { load: 'loadb', m: 'E', at: now - 61 * MIN, seq: 1, val: 'old' });
    const fresh = entryText({ load: 'loadb', m: 'E', at: now, seq: 2, val: 'new' });
    let reads = 0;
    w.storage.afterGet = (key, v, tab) => { if (tab === 'A' && key === k && ++reads === 2) w.storage.view({ name: 'B' }).setItem(k, fresh); };
    const a = await open(w, 'A', 'loada', locks);
    w.storage.afterGet = null;
    assert.equal(w.storage.map.get(k), fresh);
    a.close();
});

// ------------------------------------------------------------------------------------------------ logoff (O2)

test('N35 logoff(ns) removes every page load\'s entries and copies of that namespace once, with no notice, whatever the lock state; other namespaces, markers, host keys and heartbeats stay; a later write of another tab is not swept again', async () => {
    for (const lockMode of ['held', 'query fails', 'none']) {
        const w = h.world();
        const locks = lockMode === 'none' ? null : lockManager();
        const a = await open(w, 'A', 'loada', locks);
        const b = await open(w, 'B', 'loadb', locks);
        if (lockMode === 'query fails') locks.reject = true;
        const eds = [];
        for (const t of [a, b]) {
            eds.push(typed(t, { m: 'Main' }, t.name + ' text'));
            const c = h.addEditor(t, { d: { m: 'Comp' }, value: '' });
            c.field.focus();
            h.composition(c, 'compositionstart');
            h.input(c, t.name + 'た', true);
            eds.push(c);
        }
        const k2 = [typed(a, { ns: 'ns2', m: 'K' }, 'a2'), typed(b, { ns: 'ns2', m: 'K' }, 'b2')];
        const ap = h.addEditor(a, { d: { m: 'AP' }, value: '' }); ap.field.focus(); refuse(w, ap.key()); h.type(ap, 'a pending');
        const bp = h.addEditor(b, { d: { m: 'BP' }, value: '' }); bp.field.focus(); refuse(w, bp.key());
        const tp = w.clock.now;
        h.type(bp, 'b pending');
        w.storage.map.set(LEGACY + 'ns1', 'old marker');
        w.storage.map.set('HostData', 'host');
        const from = w.storage.log.length;
        const r = a.journal.logoff('ns1');
        assert.deepEqual(r, { ok: true, removed: 4, own: 2, others: 2 }, lockMode);
        assert.ok(!w.storage.entryKeys(true).some(k => k.indexOf(PREFIX + 'ns1|') === 0), lockMode + ': nothing of ns1 left');
        assert.ok(k2.every(e => e.stored()), lockMode + ': ns2 stays');
        assert.equal(w.storage.map.get(LEGACY + 'ns1'), 'old marker');
        assert.equal(w.storage.map.get('HostData'), 'host');
        assert.ok(w.storage.map.has(HB + 'loada') && w.storage.map.has(HB + 'loadb'), lockMode + ': heartbeats stay');
        assert.deepEqual(opsBy(w, 'A', from).filter(x => x.op === 'set'), [], lockMode + ': no notice written');
        allow(w);
        await a.deliver(); await b.deliver();
        await w.clock.advance(5000);
        assert.equal(ap.stored(), undefined, lockMode + ': A\'s own pending ns1 work is cancelled');
        assert.equal(bp.stored().val, 'b pending', lockMode + ': B\'s pending work is not cancelled');
        assert.equal(bp.stored().at, tp);
        await w.clock.advance(60000);
        assert.ok(bp.stored(), lockMode + ': no recurring sweep');
        a.close(); b.close();
    }
});

test('N36 logoff reports a refused removal ({ok:false, removed:3, failed:1}) and an enumeration failure ({ok:false, removed:0}) and never retries by itself; a second logoff is a new sweep', async () => {
    const w = h.world();
    const a = await open(w, 'A', 'loada');
    const b = await open(w, 'B', 'loadb');
    typed(a, { m: 'A1' }, 'a1'); typed(a, { m: 'A2' }, 'a2');
    const b1 = typed(b, { m: 'B1' }, 'b1'); typed(b, { m: 'B2' }, 'b2');
    const bytes = w.storage.map.get(b1.key());
    let attempts = 0;
    const restore = onCheck(w, (op, k) => { if (op === 'removeItem' && k === b1.key()) { attempts++; throw h.named('SecurityError'); } });
    assert.deepEqual(a.journal.logoff('ns1'), { ok: false, removed: 3, own: 2, others: 1, failed: 1, error: 'SecurityError' });
    await w.clock.advance(60000);
    assert.equal(attempts, 1, 'no automatic retry');
    assert.equal(w.storage.map.get(b1.key()), bytes);
    restore();
    w.storage.failOps = { ops: ['length'], name: 'SecurityError' };
    assert.deepEqual(a.journal.logoff('ns1'), { ok: false, removed: 0, own: 0, others: 0, error: 'SecurityError' });
    w.storage.failOps = null;
    const b3 = typed(b, { m: 'B3' }, 'b3');
    assert.deepEqual(a.journal.logoff('ns1'), { ok: true, removed: 2, own: 0, others: 2 }, 'one sweep per call: the refused key and B\'s new entry');
    assert.equal(b3.stored(), undefined);
    assert.deepEqual(a.journal.logoff('ns1|x'), { ok: false, removed: 0, own: 0, others: 0, error: 'namespace' }, 'a separator in the namespace is refused');
    a.close(); b.close();
});

// ------------------------------------------------------------------------------------------------ reports and inventory

test('N37 reports stay bounded and value-free: 80 refused writes and a failing lock query put at most 50 errors and no draft text into report(), list() or retire(); lockHeld is false without Web Locks', async () => {
    const w = h.world();
    const locks = lockManager();
    const a = await open(w, 'A', 'loada', locks);
    locks.reject = true;
    const ed = h.addEditor(a, { value: '' });
    ed.field.focus();
    refuse(w, ed.key());
    for (let i = 0; i < 80; i++) h.type(ed, 'secret ' + i);
    const f = seed(w, { load: 'loadx', m: 'F', at: w.clock.now - 1000, seq: 1, val: 'secret foreign' });
    const rr = await a.journal.retire([f], [{ seq: 1, val: 'secret foreign' }]);
    assert.equal(rr.kept[0].reason, 'lock-unknown');
    const l = await a.journal.list('ns1');
    const r = a.journal.report();
    assert.ok(r.errors.length <= 50, 'bounded');
    assert.ok(r.errors.some(e => e.op === 'locks'), 'the failed lock query is reported');
    for (const x of [r, l, rr]) assert.ok(!JSON.stringify(x).includes('secret'), 'no draft text');
    assert.equal(r.lockHeld, true);
    a.journal.clear('ns1');
    assert.equal(a.journal.report().pendingWrites, 0);
    const n = await open(w, 'N', 'loadn', null);
    assert.equal(n.journal.report().lockHeld, false);
    a.close(); n.close();
});

test('N38 (X40 restated) the API is start, list, value, retire, clear, logoff, coverage and report; no clear-marker export remains', async () => {
    const mod = await h.loadModule();
    for (const f of ['start', 'list', 'value', 'retire', 'clear', 'logoff', 'coverage', 'report', 'createJournal', 'entryKey', 'loadOfKey',
                     'parseDescriptor', 'planEviction', 'evictionOrder', 'isExpired', 'tailClass', 'createStorage']) assert.equal(typeof mod[f], 'function', f);
    assert.ok(!('CLEAR_MARKER_PREFIX' in mod));
    const w = h.world();
    const t = await open(w, 'A', 'loada');
    assert.deepEqual(Object.keys(t.journal).sort(), ['clear', 'coverage', 'list', 'loadId', 'logoff', 'report', 'retire', 'start', 'value']);
    t.close();
});
