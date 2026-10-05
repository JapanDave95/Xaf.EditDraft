// Client-side input journal, M1b-D: the three defects Codex found in the M1b-C review (diffreview a1 of run
// 2026-10-04-journal-m1b-c-32c9f1), owner ruling 2026-10-04 "Commit M1b-C now; one bounded pass for C1 (pageshow
// re-check), C2 (just-written key is evictable), C3 (state per field), Codex re-check". Collaborator run
// 2026-10-04-journal-m1b-d-2d52b6.
//
// R1-R5 are Codex's reproductions (as Claude re-ran them in review-a1-verify.js), turned into tests that assert the
// repaired outcome; they were run red against the M1b-C module (HEAD 865f8b10) before any change. The P-tests come from
// Codex's requirement-only `tests` call of this run (expectations P1..Pn, stated before any code was shown to it).
//
// M1b-F (owner ruling 2026-10-04, "Remove the self-repair entirely; keep G2 + G4; accept the C1 window as the residual";
// run 2026-10-04-journal-m1b-f-45a751): the C1 self-repair tests R1-R3, P1-P9 (P3b included) and P29 (its ownership
// audit asserted a re-check write-back) were deleted with the self-repair. R4/R5 and P10-P23 (C2, C3) stay.
'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const h = require('../helpers/harness');

const PREFIX = 'XafEditDraft.j1|';
const HB = 'XafEditDraft.hb1|';

function keyOf(ns, load, m, g, comp) { return PREFIX + [ns, load, h.CTX, m, String(g || 1)].join('|') + (comp ? '|c' : ''); }
function entryText(o) {
    const e = { at: o.at, f: 1, seq: o.seq || 1, ns: o.ns || 'ns1', load: o.load, p: 'p', t: 't', o: null, ctx: h.CTX, w: 'v',
                m: o.m || 'S', k: 'memo', fmt: null, co: false, bh: null, g: o.g || 1, val: o.val === undefined ? 'x' : o.val, tr: false };
    if (o.comp) e.comp = true;
    return JSON.stringify(e);
}
/** Puts one entry of page load o.load straight into storage (as that page load would have written it). */
function seed(w, o) {
    const k = keyOf(o.ns || 'ns1', o.load, o.m || 'S', o.g, o.comp);
    w.storage.map.set(k, entryText(o));
    return k;
}
function ownedBy(load) {
    return (k) => {
        if (k === HB + load || k === 'XafEditDraft.rt1|' + load) return true;
        if (k.indexOf(PREFIX) !== 0) return false;
        return k.slice(PREFIX.length).split('|')[1] === load;
    };
}
function snapshot(w, pred) { const out = {}; for (const [k, v] of w.storage.map) if (pred(k)) out[k] = v; return out; }
function refuse(w, key, ops) { w.storage.failKeys.push({ prefix: key, ops: ops || ['setItem'], name: 'QuotaExceededError' }); }
function allow(w) { w.storage.failKeys = []; }
function setsOf(w, key, from) { return w.storage.log.slice(from || 0).filter(x => x.op === 'set' && x.key === key).length; }

/**
 * One origin's Web Locks, shared by the tabs that get it. hold: new requests wait for grantAll(); beforeResolve: runs
 * once after a query took its snapshot and before the caller sees it (a writer resuming meanwhile).
 */
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

function typed(t, d, v) { const ed = h.addEditor(t, { d, value: '' }); ed.field.focus(); h.type(ed, v); return ed; }

// ------------------------------------------------------------------------------------------------ Codex's reproductions

test('R4 (C2) a late retry of the oldest capture at a full budget evicts itself; the 60 newer entries stay', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
    const x = h.addEditor(a, { d: { m: 'X' }, value: '' });
    x.field.focus();
    w.storage.failKeys = [{ prefix: x.key(), ops: ['setItem'], name: 'QuotaExceededError' }];
    h.type(x, 'x');                                             // captured first, refused
    const newer = [];
    for (let i = 0; i < 60; i++) { w.clock.now += 1; newer.push(typed(a, { m: 'N' + String(i).padStart(2, '0') }, 'n' + i)); }
    w.storage.failKeys = [];
    x.field.focus(); x.field.blur();                            // focusout retries X
    const own = w.storage.entryKeys(true).filter(k => k.split('|')[2] === 'loada');
    assert.equal(own.length, 60);
    assert.equal(x.stored(), undefined, 'the old retry went (it is the oldest by capture time)');
    assert.deepEqual(newer.filter(e => !e.stored()).map(e => e.d.m), [], 'no newer entry was evicted');
    a.close();
});

test('R5 (C3) two fields share one key: clear resets both field states; the first field\'s blur, delayed reads and retries write nothing', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
    const f1 = h.addEditor(a, { value: '' });                   // two fields, the same descriptor (same key)
    const f2 = h.addEditor(a, { value: '' });
    f1.field.focus(); h.type(f1, 'one');
    f2.field.focus(); h.type(f2, 'two');
    f1.field.focus();
    const r = a.journal.clear('ns1');
    assert.deepEqual(r, { ok: true, removed: 1 });
    assert.equal(f1.stored(), undefined, 'absent after the clear');
    f1.field.blur();
    await w.clock.advance(5000);
    assert.equal(f1.stored(), undefined, 'still absent after the blur and the delayed reads');
    f2.field.focus(); f2.field.blur();
    await w.clock.advance(5000);
    assert.equal(f2.stored(), undefined, 'the second field writes nothing either');
    a.close();
});

// ------------------------------------------------------------------------------------------------ C2: the key just written competes (P10-P16)

/** The own keys that must remain after the write: everything but the oldest (at, key) beyond the count limit. */
function expectedSurvivors(items, limit) {
    const sorted = items.slice().sort((x, y) => (x.at - y.at) || (x.key < y.key ? -1 : (x.key > y.key ? 1 : 0)));
    return sorted.slice(Math.max(0, sorted.length - limit)).map(x => x.key).sort();
}

test('P10 (C2) count boundaries: with 59, 60 or 61 own keys before a late retry X is written, the newest 60 by capture time then key remain; X is evicted when it falls among the oldest, never exempt; another load\'s keys and heartbeats stay', async () => {
    for (const before of [59, 60, 61]) {
        for (const pos of ['oldest', 'middle', 'newest']) {
            const w = h.world();
            const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
            const x = h.addEditor(a, { d: { m: 'X' }, value: '' });
            x.field.focus();
            refuse(w, x.key());
            const tX = w.clock.now;
            h.type(x, 'x');                                 // captured at tX, refused
            w.clock.now += 1000;
            const items = [];
            for (let i = 0; i < before; i++) {
                const older = pos === 'newest' || (pos === 'middle' && i < before / 2);
                const at = older ? tX - 500 + i : tX + 1 + i;
                items.push({ key: seed(w, { load: 'loada', m: 'S' + String(i).padStart(2, '0'), at }), at });
            }
            seed(w, { load: 'loadb', m: 'B1', at: tX - 900 });
            seed(w, { load: 'loadb', m: 'B2', at: tX - 899 });
            w.storage.map.set(HB + 'loadb', String(tX - 900));
            const bBytes = snapshot(w, ownedBy('loadb'));
            allow(w);
            x.field.focus(); x.field.blur();                // the retry writes X
            items.push({ key: x.key(), at: tX });
            const own = w.storage.entryKeys(true).filter(ownedBy('loada')).sort();
            const label = before + ' before, X ' + pos;
            assert.deepEqual(own, expectedSurvivors(items, 60), label);
            assert.equal(!!x.stored(), own.includes(x.key()), label + ': X present only when among the newest 60');
            if (pos === 'oldest' && before >= 60) assert.equal(x.stored(), undefined, label + ': X evicted itself');
            if (pos === 'newest') assert.equal(x.stored().at, tX, label + ': X kept with its capture time');
            assert.deepEqual(snapshot(w, ownedBy('loadb')), bBytes, label + ': another load\'s keys untouched');
            a.close();
        }
    }
});

test('P11 (C2) equal capture times are ordered by key (ordinal), also for the key just written, whatever the insertion order', async () => {
    for (const xMember of ['0x', 'mm']) {                   // '0x' sorts before 'aa' (X goes), 'mm' after it (aa goes)
        for (const reversed of [false, true]) {
            const w = h.world();
            const a = await h.openTab(w, { name: 'A', loadId: 'loada', limits: { entries: 3 } });
            const x = h.addEditor(a, { d: { m: xMember }, value: '' });
            x.field.focus();
            refuse(w, x.key());
            const T = w.clock.now;
            h.type(x, 'x');
            w.clock.now += 1000;
            const seeds = [{ m: 'aa', at: T }, { m: 'zz', at: T }, { m: 'qq', at: T + 5 }];
            const keys = {};
            for (const s of (reversed ? seeds.slice().reverse() : seeds)) keys[s.m] = seed(w, { load: 'loada', m: s.m, at: s.at });
            allow(w);
            x.field.focus(); x.field.blur();
            const label = 'X ' + xMember + (reversed ? ', reversed' : '');
            if (xMember === '0x') {
                assert.equal(x.stored(), undefined, label + ': X has the smallest key at the oldest time and goes');
                assert.ok(w.storage.map.has(keys.aa));
            } else {
                assert.ok(!w.storage.map.has(keys.aa), label + ': aa goes');
                assert.equal(x.stored().at, T);
            }
            assert.ok(w.storage.map.has(keys.zz) && w.storage.map.has(keys.qq), label);
            a.close();
        }
    }
});

test('P12 (C2) a sole written entry larger than the whole serialized budget evicts itself', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada', limits: { serializedChars: 300 } });
    const ed = typed(a, { m: 'Big' }, 'y'.repeat(400));
    assert.equal(ed.stored(), undefined);
    assert.equal(a.journal.report().stats.evicted, 1);
    assert.equal(a.journal.report().stats.writes, 1);
    a.close();
});

test('P13 (C2) one budget over this load\'s namespaces and incomplete copies: an old retry in another namespace evicts itself; another load\'s entries, copies and heartbeats stay byte for byte', async () => {
    for (const withLocks of [false, true]) {
        const w = h.world();
        const locks = withLocks ? lockManager() : null;
        const a = await h.openTab(w, { name: 'A', loadId: 'loada', locks, limits: { entries: 4 } });
        const x = h.addEditor(a, { d: { ns: 'ns2', m: 'X' }, value: '' });
        x.field.focus();
        refuse(w, x.key());
        const tX = w.clock.now;
        h.type(x, 'x');
        w.clock.now += 1000;
        const own = [seed(w, { ns: 'ns1', load: 'loada', m: 'S1', at: tX + 10 }), seed(w, { ns: 'ns2', load: 'loada', m: 'S2', at: tX + 20 }),
                     seed(w, { ns: 'ns2', load: 'loada', m: 'C', at: tX + 30, comp: true })];
        const n = typed(a, { m: 'N' }, 'n');                // 4 own keys now (S1, S2, the copy C, N)
        seed(w, { ns: 'ns1', load: 'loadb', m: 'B1', at: tX - 5000 });
        seed(w, { ns: 'ns2', load: 'loadb', m: 'B2', at: tX - 5000, comp: true });
        w.storage.map.set(HB + 'loadb', String(tX - 5000));
        const bBytes = snapshot(w, ownedBy('loadb'));
        const from = w.storage.log.length;
        allow(w);
        x.field.focus(); x.field.blur();
        const label = withLocks ? 'locks' : 'no locks';
        assert.equal(x.stored(), undefined, label + ': the old retry evicted itself');
        assert.ok(own.every(k => w.storage.map.has(k)) && n.stored(), label + ': the newer own keys stay (the copy counts but is newer)');
        assert.deepEqual(snapshot(w, ownedBy('loadb')), bBytes, label);
        assert.equal(w.storage.log.slice(from).filter(e => !ownedBy('loada')(e.key)).length, 0, label + ': no operation on another load\'s keys');
        a.close();
    }
});

test('P14 (C2) a rewrite of an existing key counts once and evicts nothing at the count limit; a late retry that grows an old key past the serialized budget evicts that key itself (its older stored value goes with it)', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada', limits: { entries: 3 } });
    const k = typed(a, { m: 'K' }, 'k');
    w.clock.now += 10; typed(a, { m: 'O1' }, 'o1');
    w.clock.now += 10; typed(a, { m: 'O2' }, 'o2');
    w.clock.now += 10;
    k.field.focus(); h.type(k, 'k2');
    assert.equal(w.storage.entryKeys(true).filter(ownedBy('loada')).length, 3);
    assert.equal(k.stored().val, 'k2');
    assert.equal(a.journal.report().stats.evicted, 0, 'a rewrite evicts nothing');
    const w2 = h.world();
    const b = await h.openTab(w2, { name: 'B', loadId: 'loadb', limits: { serializedChars: 1400 } });
    const kb = typed(b, { m: 'K' }, 'small');
    w2.clock.now += 10;
    refuse(w2, kb.key());
    h.type(kb, 'z'.repeat(500));                            // captured now, refused
    w2.clock.now += 10; const o1 = typed(b, { m: 'O1' }, 'o1');
    w2.clock.now += 10; const o2 = typed(b, { m: 'O2' }, 'o2');
    assert.equal(kb.stored().val, 'small');
    assert.ok(o1.stored() && o2.stored(), 'three small entries fit');
    allow(w2);
    await w2.clock.advance(1000);                           // the retry writes the big value, then the budget runs
    assert.equal(kb.stored(), undefined, 'the oldest key (the rewritten one) went');
    assert.ok(o1.stored() && o2.stored());
    a.close(); b.close();
});

test('P15 (C2) the old retry that evicted itself was a successful write: writes +1, evicted +1, no new failure, nothing pending; neither an unchanged blur nor a persisted page show writes it again', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada', limits: { entries: 3 } });
    const x = h.addEditor(a, { d: { m: 'X' }, value: '' });
    x.field.focus();
    refuse(w, x.key());
    h.type(x, 'x');
    for (const m of ['N1', 'N2', 'N3']) { w.clock.now += 1; typed(a, { m }, m); }
    const s0 = Object.assign({}, a.journal.report().stats);
    allow(w);
    const from = w.storage.log.length;
    x.field.focus(); x.field.blur();
    const s1 = a.journal.report().stats;
    assert.equal(x.stored(), undefined);
    assert.equal(setsOf(w, x.key(), from), 1, 'one write of X');
    assert.deepEqual([s1.writes - s0.writes, s1.evicted - s0.evicted, s1.writeFailures - s0.writeFailures], [1, 1, 0]);
    assert.equal(a.journal.report().pendingWrites, 0);
    x.field.focus(); x.field.blur();
    pageshow(a, true);
    await w.clock.advance(5000);
    assert.equal(setsOf(w, x.key(), from), 1, 'not written again');
    a.close();
});

test('P16 (C2, D-5 unchanged) when the self-eviction removal is refused, the key stays stored over budget, the failure is reported, and the removal is not retried by a timer or a blur', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada', limits: { entries: 2 } });
    const x = h.addEditor(a, { d: { m: 'X' }, value: '' });
    x.field.focus();
    refuse(w, x.key());
    h.type(x, 'x');
    for (const m of ['N1', 'N2']) { w.clock.now += 1; typed(a, { m }, m); }
    allow(w);
    refuse(w, x.key(), ['removeItem']);
    const errors = a.journal.report().stats.storageErrors;
    x.field.focus(); x.field.blur();
    assert.equal(x.stored().val, 'x', 'the refused removal leaves X stored');
    assert.equal(w.storage.entryKeys(true).filter(ownedBy('loada')).length, 3);
    assert.equal(a.journal.report().stats.storageErrors, errors + 1);
    allow(w);
    const from = w.storage.log.length;
    await w.clock.advance(10000);
    x.field.focus(); x.field.blur();
    assert.equal(w.storage.log.slice(from).filter(e => e.key === x.key()).length, 0, 'no removal or write retried');
    a.close();
});

// ------------------------------------------------------------------------------------------------ C3: every field state of a key (P17-P23)

test('P17 (C3) three fields share one key, typed and focused in another order: clear removes the one entry and resets all three; no blur writes pre-clear text', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
    const f = [h.addEditor(a, { value: '' }), h.addEditor(a, { value: '' }), h.addEditor(a, { value: '' })];
    for (const [i, v] of [[2, 'three'], [0, 'one'], [1, 'two']]) { f[i].field.focus(); h.type(f[i], v); }
    f[1].field.focus();
    assert.equal(f[0].key(), f[2].key());
    assert.deepEqual(a.journal.clear('ns1'), { ok: true, removed: 1 });
    const from = w.storage.log.length;
    for (const e of [f[1], f[0], f[2]]) { e.field.focus(); e.field.blur(); }
    await w.clock.advance(5000);
    assert.equal(f[0].stored(), undefined);
    assert.equal(setsOf(w, f[0].key(), from), 0);
    a.close();
});

test('P18 (C3) queued pre-clear work of every shared field (refused writes, an exhausted retry allowance, a delayed composition read) writes nothing after the clear, also after retries, blurs and a persisted page show', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
    const f = [h.addEditor(a, { value: '' }), h.addEditor(a, { value: '' }), h.addEditor(a, { value: '' })];
    const K = f[0].key();
    refuse(w, K);
    f[0].field.focus(); h.type(f[0], 'one');
    await w.clock.advance(6000);                            // f0's intent: retry allowance used up
    f[1].field.focus(); h.type(f[1], 'two');
    f[2].field.focus();
    h.composition(f[2], 'compositionstart');
    h.input(f[2], 'さん', true);
    h.input(f[2], '三', true);
    h.composition(f[2], 'compositionend', '三');              // its delayed read is queued
    const from = w.storage.log.length;
    a.journal.clear('ns1');
    allow(w);
    await w.clock.advance(10000);
    for (const e of f) { e.field.focus(); h.keydown(e, 'Shift'); e.field.blur(); }
    pageshow(a, true);
    await w.clock.advance(5000);
    assert.equal(setsOf(w, K, from) + setsOf(w, K + '|c', from), 0, 'no write of the key or its copy after the clear');
    assert.ok(!w.storage.map.has(K) && !w.storage.map.has(K + '|c'));
    assert.equal(a.journal.report().pendingWrites, 0);
    a.close();
});

test('P19 (C3) a clear with shared fields and a refused removal of the copy: one pass, {ok:false, removed:1, failed:1}; the copy keeps its bytes and is not retried; another load\'s key and another namespace stay', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
    const f1 = h.addEditor(a, { value: '' }), f2 = h.addEditor(a, { value: '' });
    f1.field.focus(); h.type(f1, 'one');
    f2.field.focus();
    h.composition(f2, 'compositionstart');
    h.input(f2, 'に', true);                                // the shared key's incomplete copy
    const other = typed(a, { ns: 'ns2', m: 'Other' }, 'other');
    const bKey = seed(w, { load: 'loadb', m: 'B', at: w.clock.now - 1000 });
    const copyBytes = w.storage.map.get(f1.composingKey()), otherBytes = w.storage.map.get(other.key()), bBytes = w.storage.map.get(bKey);
    assert.ok(copyBytes);
    let attempts = 0;
    const orig = w.storage.check.bind(w.storage);
    w.storage.check = (op, key) => { if (op === 'removeItem' && key === f1.composingKey()) { attempts++; throw h.named('SecurityError'); } return orig(op, key); };
    assert.deepEqual(a.journal.clear('ns1'), { ok: false, removed: 1, failed: 1, error: 'SecurityError' });
    f1.field.focus(); f1.field.blur();
    await w.clock.advance(10000);
    w.storage.check = orig;
    assert.equal(w.storage.map.get(f1.composingKey()), copyBytes);
    assert.equal(attempts, 1);
    assert.equal(f1.stored(), undefined);
    assert.equal(w.storage.map.get(other.key()), otherBytes);
    assert.equal(w.storage.map.get(bKey), bBytes);
    a.close();
});

test('P20 (C3) after the clear every shared field journals a new edit under the same key with its own capture time; a quiet sibling\'s blur does not overwrite the newest value', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
    const f1 = h.addEditor(a, { value: '' }), f2 = h.addEditor(a, { value: '' });
    f1.field.focus(); h.type(f1, 'one');
    f2.field.focus(); h.type(f2, 'two');
    a.journal.clear('ns1');
    w.clock.now += 10;
    const t2 = w.clock.now;
    h.type(f2, 'two new');
    assert.deepEqual([f2.stored().val, f2.stored().at], ['two new', t2]);
    f1.field.focus(); f1.field.blur();
    assert.equal(f2.stored().val, 'two new', 'the quiet sibling writes nothing');
    w.clock.now += 10;
    const t1 = w.clock.now;
    f1.field.focus(); h.type(f1, 'one new');
    assert.deepEqual([f1.stored().val, f1.stored().at], ['one new', t1]);
    a.close();
});

test('P21a (C3) retire of a shared key answers at once and removes the one entry; when only the retired value\'s field acted, no blur of any field and no persisted page show writes it back; a later edit is journaled', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
    const f = [h.addEditor(a, { value: '' }), h.addEditor(a, { value: '' }), h.addEditor(a, { value: '' })];
    f[0].field.focus();                                     // a state for the key, no action
    f[1].field.focus(); h.type(f[1], 'two');
    f[1].field.blur();
    const e = f[1].stored();
    const r = a.journal.retire([f[1].key()], [{ seq: e.seq, val: 'two' }]);
    assert.ok(!(r instanceof Promise), 'synchronous');
    assert.deepEqual(r, { retired: [f[1].key()], kept: [] });
    const from = w.storage.log.length;
    for (const x of f) { x.field.focus(); x.field.blur(); }
    pageshow(a, true);
    await w.clock.advance(5000);
    assert.equal(setsOf(w, f[0].key(), from), 0);
    f[2].field.focus(); h.type(f[2], 'three');
    assert.equal(f[2].stored().val, 'three');
    a.close();
});

test('P21 (C3) retire of a shared key answers at once and removes the one entry; no blur of any field and no persisted page show writes the retired value back; a later edit is journaled', { todo: 'ESCALATED to the owner (M1b-D write-up): red on the candidate and on HEAD 865f8b10 alike. Two shared fields that both acted re-record their own text on blur and overwrite each other, so after the retire the sibling writes "one" and the retired "two" follows. Outside C3 ("nothing else about key identity changes"); classified faulty-for-scope / ambiguous (Codex P21 named the dependency)' }, async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
    const f = [h.addEditor(a, { value: '' }), h.addEditor(a, { value: '' }), h.addEditor(a, { value: '' })];
    f[0].field.focus(); h.type(f[0], 'one');
    f[1].field.focus(); h.type(f[1], 'two');
    f[1].field.blur();
    const e = f[1].stored();
    const r = a.journal.retire([f[1].key()], [{ seq: e.seq, val: 'two' }]);
    assert.ok(!(r instanceof Promise), 'synchronous');
    assert.deepEqual(r, { retired: [f[1].key()], kept: [] });
    for (const x of f) { x.field.focus(); x.field.blur(); }
    pageshow(a, true);
    await w.clock.advance(5000);
    assert.equal(f[0].stored(), undefined);
    f[2].field.focus(); h.type(f[2], 'three');
    assert.equal(f[2].stored().val, 'three');
    a.close();
});

test('P22 (C3) pending work of the shared key from any of its fields keeps retire from removing the stored entry; the retry then writes that value with its capture time', async () => {
    for (const src of [0, 1, 2]) {
        const w = h.world();
        const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
        const f = [h.addEditor(a, { value: '' }), h.addEditor(a, { value: '' }), h.addEditor(a, { value: '' })];
        f[0].field.focus(); h.type(f[0], 'v0');
        for (const x of f) x.field.focus();                 // every field has a state for the key
        const e0 = f[0].stored();
        refuse(w, f[0].key());
        w.clock.now += 10;
        const t1 = w.clock.now;
        f[src].field.focus(); h.type(f[src], 'v1 from ' + src);
        const r = a.journal.retire([f[0].key()], [{ seq: e0.seq, val: 'v0' }]);
        assert.deepEqual(r, { retired: [], kept: [{ key: f[0].key(), reason: 'pending' }] }, 'field ' + src);
        assert.equal(f[0].stored().val, 'v0');
        allow(w);
        await w.clock.advance(1000);
        assert.deepEqual([f[0].stored().val, f[0].stored().at], ['v1 from ' + src, t1]);
        a.close();
    }
});

test('P23 (C3) a shared field removed from the page: the clear still cancels its pending pre-clear work; the remaining field journals a new edit', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A', loadId: 'loada' });
    const f1 = h.addEditor(a, { value: '' }), f2 = h.addEditor(a, { value: '' });
    f2.field.focus(); h.type(f2, 'two');
    refuse(w, f1.key());
    f1.field.focus(); h.type(f1, 'one');                    // refused, pending
    f1.root.remove();
    assert.deepEqual(a.journal.clear('ns1'), { ok: true, removed: 1 });
    allow(w);
    await w.clock.advance(10000);
    assert.equal(f1.stored(), undefined);
    f2.field.focus(); h.type(f2, 'two new');
    assert.equal(f2.stored().val, 'two new');
    a.close();
});
