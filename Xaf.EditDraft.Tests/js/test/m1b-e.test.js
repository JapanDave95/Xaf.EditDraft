// Client-side input journal, M1b-E: the bounded pass after M1b-D. Owner rulings 2026-10-04 (labels verbatim): "Gate the
// repair on the tab's own heartbeat still existing"; "Yes, run M1b-E as described" (the gate, DR1, DR2, the C# PlanEviction
// twin; P21 deferred to M2). Collaborator run 2026-10-04-journal-m1b-e-74b9e0.
//
// E-tests are the reproductions of the M1b-D write-up (section 4 E-11 logoff, section 5 DR1), run red against HEAD
// 191a7f59 before any change. The Q-tests come from Codex's requirement-only `tests` call of this run.
//
// M1b-F (owner ruling 2026-10-04, "Remove the self-repair entirely; keep G2 + G4; accept the C1 window as the residual";
// run 2026-10-04-journal-m1b-f-45a751): the heartbeat-gate tests (Q2, Q3, Q6, Q7/Q8, Q9, Q10, Q11) and the DR2 tests (E3,
// E3b) were deleted with the self-repair; E1 and E1b (logoff) now hold because nothing writes a removed entry back.
'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const h = require('../helpers/harness');

const HB = 'XafEditDraft.hb1|';
const LOCK = 'XafEditDraft.w|';

/** One origin's Web Locks (same model as m1b-d.test.js): hold delays grants; release drops a cached page's lock. */
function lockManager() {
    const m = {
        held: new Set(), pending: [], requests: [], hold: false, reject: false, beforeResolve: null,
        request(name, cb) {
            m.requests.push(name);
            return new Promise((resolve) => {
                const grant = () => Promise.resolve().then(() => { m.held.add(name); resolve(cb({ name })); });
                if (m.hold) m.pending.push({ name, grant }); else grant();
            });
        },
        query() {
            if (m.reject) return Promise.reject(h.named('InvalidStateError'));
            const snap = { held: Array.from(m.held).map(name => ({ name })), pending: m.pending.map(p => ({ name: p.name })) };
            return Promise.resolve().then(() => { const f = m.beforeResolve; m.beforeResolve = null; if (f) f(); return snap; });
        },
        grantAll() { for (const p of m.pending.splice(0)) p.grant(); },
        release(name) { m.held.delete(name); }
    };
    return m;
}

function pageshow(tab, persisted) {
    let e;
    try { e = new tab.win.PageTransitionEvent('pageshow', { persisted }); }
    catch (x) { e = new tab.win.Event('pageshow'); Object.defineProperty(e, 'persisted', { value: persisted }); }
    tab.win.dispatchEvent(e);
}
function pagehide(tab) { tab.win.dispatchEvent(new tab.win.Event('pagehide')); }
function typed(t, d, v) { const ed = h.addEditor(t, { d, value: '' }); ed.field.focus(); h.type(ed, v); return ed; }
function setsOf(w, key, from) { return w.storage.log.slice(from || 0).filter(x => x.op === 'set' && x.key === key).length; }

// ------------------------------------------------------------------------------------------------ reproductions

test('E1 (E-11 logoff) B in the back/forward cache during A\'s logoff, restored with no new input (no Web Locks): there is no self-repair, so B\'s entry stays absent; B writes a fresh heartbeat; new input is journaled', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
    const b = await h.openTab(w, { name: 'B', loadId: 'loadb' });
    const eb = typed(b, {}, 'typed in B');
    pagehide(b);
    assert.deepEqual(a.journal.logoff('ns1'), { ok: true, removed: 1, own: 0, others: 1 });
    assert.equal(eb.stored(), undefined);
    pageshow(b, true);
    await h.flush();
    assert.equal(eb.stored(), undefined, 'B\'s entry stays absent after the restore');
    assert.ok(w.storage.map.has(HB + 'loadb'), 'B wrote a fresh heartbeat');
    w.clock.now += 10;
    const t2 = w.clock.now;
    h.type(eb, 'typed in B, new');
    assert.deepEqual([eb.stored().val, eb.stored().at], ['typed in B, new', t2], 'new input is journaled');
    a.close(); b.close();
});

test('E1b (E-11 logoff with Web Locks) B\'s lock was dropped while cached; restored with no new input; its lock is requested and granted again and B\'s entry stays absent', async () => {
    const w = h.world();
    const locks = lockManager();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada', locks });
    const b = await h.openTab(w, { name: 'B', loadId: 'loadb', locks });
    const eb = typed(b, {}, 'typed in B');
    pagehide(b);
    locks.release(LOCK + 'loadb');
    a.journal.logoff('ns1');
    pageshow(b, true);
    await h.flush();
    assert.ok(locks.held.has(LOCK + 'loadb'), 'B holds its lock again');
    assert.equal(eb.stored(), undefined, 'B\'s entry stays absent after a logoff');
    a.close(); b.close();
});

test('E2 (G2, DR1) a late retry of the oldest capture at a full budget whose post-write scan read of the written key fails once still evicts itself; the 60 newer entries stay', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
    const x = h.addEditor(a, { d: { m: 'X' }, value: '' });
    x.field.focus();
    w.storage.failKeys = [{ prefix: x.key(), ops: ['setItem'], name: 'QuotaExceededError' }];
    h.type(x, 'x');                                             // captured first, refused
    const newer = [];
    for (let i = 0; i < 60; i++) { w.clock.now += 1; newer.push(typed(a, { m: 'N' + String(i).padStart(2, '0') }, 'n' + i)); }
    w.storage.failKeys = [];
    let armed = false, failed = 0;
    const orig = w.storage.check.bind(w.storage);
    w.storage.check = (op, k) => {
        if (op === 'setItem' && k === x.key()) armed = true;                                                   // after X's write ...
        if (armed && op === 'getItem' && k === x.key()) { armed = false; failed++; throw h.named('SecurityError'); }   // ... its scan read fails once
        return orig(op, k);
    };
    x.field.focus(); x.field.blur();                            // focusout retries X
    w.storage.check = orig;
    assert.equal(failed, 1, 'the schedule ran: one read of X failed');
    const own = w.storage.entryKeys(true).filter(k => k.split('|')[2] === 'loada');
    assert.equal(own.length, 60);
    assert.equal(x.stored(), undefined, 'the old retry went (it is the oldest by capture time)');
    assert.deepEqual(newer.filter(e => !e.stored()).map(e => e.d.m), [], 'no newer entry was evicted');
    a.close();
});

// ------------------------------------------------------------------------------------------------ logoff (Codex tests a1 Q4)

test('Q4 (logoff) logoff without Web Locks, the removal event withheld or delivered before the restore: the entry stays absent, no write of the entry or its copy, a fresh heartbeat; delivery only changes removedElsewhere', async () => {
    for (const delivered of [false, true]) {
        const w = h.world();
        const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
        const b = await h.openTab(w, { name: 'B', loadId: 'loadb' });
        const eb = typed(b, {}, 'typed in B');
        pagehide(b);
        assert.ok(!w.storage.map.has(HB + 'loadb'), 'page hide removed B\'s heartbeat');
        a.journal.logoff('ns1');
        const removed = b.journal.report().stats.removedElsewhere;
        if (delivered) await b.deliver();
        w.clock.now += 500;
        const from = w.storage.log.length;
        pageshow(b, true);
        await h.flush();
        const label = delivered ? 'event delivered' : 'event withheld';
        assert.equal(eb.stored(), undefined, label);
        assert.equal(setsOf(w, eb.key(), from) + setsOf(w, eb.composingKey(), from), 0, label);
        assert.equal(w.storage.map.get(HB + 'loadb'), String(w.clock.now), label + ': fresh heartbeat');
        assert.deepEqual(w.storage.log.slice(from).filter(x => x.tab === 'B').map(x => x.key), [HB + 'loadb'], label + ': B wrote only its heartbeat');
        assert.equal(b.journal.report().stats.removedElsewhere, removed + (delivered ? 1 : 0), label);
        a.close(); b.close();
    }
});

// ------------------------------------------------------------------------------------------------ G2: the written key competes even when its scan read fails (Q12-Q17)

/** Fails exactly the next getItem(key) after a setItem(key) (the post-write scan read of the key just written). */
function failScanReadOf(w, key) {
    let armed = false;
    const state = { failed: 0 };
    const orig = w.storage.check.bind(w.storage);
    w.storage.check = (op, k) => {
        if (op === 'setItem' && k === key) armed = true;
        if (armed && op === 'getItem' && k === key) { armed = false; state.failed++; throw h.named('SecurityError'); }
        return orig(op, k);
    };
    state.restore = () => { w.storage.check = orig; };
    return state;
}

test('Q12 (G2) DR1 in full: the self-evicting retry counts writes +1 and evicted +1, removes no newer key, and is not written again by an unchanged blur or a persisted page show; the same schedule without the read failure ends the same', async () => {
    const results = [];
    for (const failRead of [true, false]) {
        const w = h.world();
        const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
        const x = h.addEditor(a, { d: { m: 'X' }, value: '' });
        x.field.focus();
        w.storage.failKeys = [{ prefix: x.key(), ops: ['setItem'], name: 'QuotaExceededError' }];
        h.type(x, 'x');
        const newer = [];
        for (let i = 0; i < 60; i++) { w.clock.now += 1; newer.push(typed(a, { m: 'N' + String(i).padStart(2, '0') }, 'n' + i)); }
        w.storage.failKeys = [];
        const bytes = newer.map(e => w.storage.map.get(e.key()));
        const s0 = Object.assign({}, a.journal.report().stats);
        const fr = failRead ? failScanReadOf(w, x.key()) : null;
        const from = w.storage.log.length;
        x.field.focus(); x.field.blur();
        if (fr) { fr.restore(); assert.equal(fr.failed, 1); }
        const s1 = a.journal.report().stats;
        const label = failRead ? 'read fails' : 'read succeeds';
        assert.deepEqual([s1.writes - s0.writes, s1.evicted - s0.evicted, s1.writeFailures - s0.writeFailures], [1, 1, 0], label);
        assert.equal(x.stored(), undefined, label);
        assert.deepEqual(newer.map(e => w.storage.map.get(e.key())), bytes, label + ': the newer entries byte for byte');
        assert.equal(w.storage.log.slice(from).filter(e => e.op === 'remove' && e.key !== x.key()).length, 0, label + ': no other removal');
        x.field.focus(); x.field.blur();
        pageshow(a, true);
        await w.clock.advance(5000);
        assert.equal(setsOf(w, x.key(), from), 1, label + ': not written again');
        results.push(w.storage.entryKeys(true).filter(k => k.split('|')[2] === 'loada').sort());
    }
    assert.deepEqual(results[0], results[1], 'the same keys remain with and without the read failure');
});

test('Q13 (G2) a failed read of ANOTHER key in the post-write scan is not compensated: the written key is listed once, nothing is evicted (61 keys stay)', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
    const x = h.addEditor(a, { d: { m: 'X' }, value: '' });
    x.field.focus();
    w.storage.failKeys = [{ prefix: x.key(), ops: ['setItem'], name: 'QuotaExceededError' }];
    h.type(x, 'x');
    const newer = [];
    for (let i = 0; i < 60; i++) { w.clock.now += 1; newer.push(typed(a, { m: 'N' + String(i).padStart(2, '0') }, 'n' + i)); }
    w.storage.failKeys = [];
    const n59 = newer[59].key();
    let armed = false, failed = 0;
    const orig = w.storage.check.bind(w.storage);
    w.storage.check = (op, k) => {
        if (op === 'setItem' && k === x.key()) armed = true;
        if (armed && op === 'getItem' && k === n59) { armed = false; failed++; throw h.named('SecurityError'); }
        return orig(op, k);
    };
    x.field.focus(); x.field.blur();
    w.storage.check = orig;
    assert.equal(failed, 1);
    assert.equal(w.storage.entryKeys(true).filter(k => k.split('|')[2] === 'loada').length, 61);
    assert.equal(x.stored().val, 'x');
    assert.ok(newer.every(e => e.stored()));
    a.close();
});

test('Q14 (G2) with its scan read failing, the written key competes by capture time then key: oldest -> it goes; newest -> the oldest other goes; equal time, its key first -> it goes; equal time, the other key first -> the other goes', async () => {
    const cases = [
        { name: 'oldest', xm: 'X', seeds: [['S1', 1], ['S2', 2], ['S3', 3]], gone: 'X' },
        { name: 'newest', xm: 'X', seeds: [['S1', -3], ['S2', -2], ['S3', -1]], gone: 'S1' },
        { name: 'tie, its key first', xm: '0x', seeds: [['aa', 0], ['zz', 5], ['qq', 6]], gone: '0x' },
        { name: 'tie, the other key first', xm: 'mm', seeds: [['aa', 0], ['zz', 5], ['qq', 6]], gone: 'aa' }
    ];
    for (const c of cases) {
        const w = h.world();
        const a = await h.openTab(w, { name: 'A', loadId: 'loada', limits: { entries: 3 } });
        const x = h.addEditor(a, { d: { m: c.xm }, value: '' });
        x.field.focus();
        w.storage.failKeys = [{ prefix: x.key(), ops: ['setItem'], name: 'QuotaExceededError' }];
        const T = w.clock.now;
        h.type(x, 'x');
        w.clock.now += 1000;
        const keys = {};
        for (const [m, dt] of c.seeds) {
            keys[m] = 'XafEditDraft.j1|ns1|loada|' + h.CTX + '|' + m + '|1';
            w.storage.map.set(keys[m], JSON.stringify({ at: T + dt, f: 1, seq: 100, ns: 'ns1', load: 'loada', m, g: 1, val: m }));
        }
        keys[c.xm] = x.key();
        w.storage.failKeys = [];
        const fr = failScanReadOf(w, x.key());
        x.field.focus(); x.field.blur();
        fr.restore();
        assert.equal(fr.failed, 1, c.name);
        const gone = Object.keys(keys).filter(m => !w.storage.map.has(keys[m]));
        assert.deepEqual(gone, [c.gone], c.name);
        if (c.gone !== c.xm) assert.equal(x.stored().at, T, c.name + ': kept with its capture time');
        a.close();
    }
});

test('Q16 (G2) a sole entry larger than the serialized budget whose scan read fails evicts itself (writes +1, evicted +1) and is not written again', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada', limits: { serializedChars: 300 } });
    const ed = h.addEditor(a, { d: { m: 'Big' }, value: '' });
    ed.field.focus();
    const fr = failScanReadOf(w, ed.key());
    const from = w.storage.log.length;
    h.type(ed, 'y'.repeat(400));
    fr.restore();
    assert.equal(fr.failed, 1);
    assert.equal(ed.stored(), undefined);
    assert.deepEqual([a.journal.report().stats.writes, a.journal.report().stats.evicted], [1, 1]);
    ed.field.blur();
    pageshow(a, true);
    await w.clock.advance(5000);
    assert.equal(setsOf(w, ed.key(), from), 1, 'one write only');
    a.close();
});

test('Q17 (G2, D-5 unchanged) the written key left out of its scan is chosen, and its removal is refused: it stays, the failure is reported, nothing else is removed and nothing is retried', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada', limits: { entries: 2 } });
    const x = h.addEditor(a, { d: { m: 'X' }, value: '' });
    x.field.focus();
    w.storage.failKeys = [{ prefix: x.key(), ops: ['setItem'], name: 'QuotaExceededError' }];
    h.type(x, 'x');
    const n = [];
    for (const m of ['N1', 'N2']) { w.clock.now += 1; n.push(typed(a, { m }, m)); }
    w.storage.failKeys = [{ prefix: x.key(), ops: ['removeItem'], name: 'SecurityError' }];
    const errors = a.journal.report().stats.storageErrors;
    const fr = failScanReadOf(w, x.key());
    const from = w.storage.log.length;
    x.field.focus(); x.field.blur();
    fr.restore();
    assert.equal(x.stored().val, 'x', 'the refused removal leaves X stored');
    assert.ok(n.every(e => e.stored()), 'no other key removed');
    assert.equal(a.journal.report().stats.storageErrors, errors + 2, 'the scan read failure and the refused removal are counted');
    w.storage.failKeys = [];
    await w.clock.advance(10000);
    x.field.focus(); x.field.blur();
    assert.equal(w.storage.log.slice(from).filter(e => e.op === 'remove').length, 0, 'no removal retried');
    a.close();
});
