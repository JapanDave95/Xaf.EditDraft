// Client-side input journal, milestone M1 — intake API (list / value / retire / clear), coverage, platform line, two tabs,
// and the F6 race classes D1-D6 restated for a journal with ONE KEY PER ENTRY.
// Expectations come from the REQUIREMENT, stated by Codex before any M1 code existed (collaborator run
// 2026-10-04-edit-draft-journal-m1-96623a, `tests` call a1). Each test names the expectation id (T26 ... T42); the F6
// rows are docs/input-journal-two-tabs-f6-2026-10-03.md section 6.5 (D1-D6).
// M1b-C (2026-10-04, per-tab clears, per-load budget): T35b, T37, T40 and T42 were DELETED, not revised
// (docs/edit-draft-client-journal-per-tab-clears-2026-10-04.md section 4.10); their replacements are in m1b-c.test.js.
'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const h = require('../helpers/harness');

const PREFIX = 'XafEditDraft.j1|';

async function typedEntry(t, value, d) {
    const ed = h.addEditor(t, { d, value: '' });
    ed.field.focus();
    h.type(ed, value);
    return ed;
}

function fakeLocks() {
    const held = [];
    return {
        held,
        request(name, cb) { held.push({ name }); return Promise.resolve().then(() => cb({ name })); },
        query() { return Promise.resolve({ held: held.slice(), pending: [] }); }
    };
}

test('T26 retire removes an entry whose stored sequence and value match the echo and nothing newer is pending', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = await typedEntry(t, 'durable');
    const listed = (await t.journal.list('ns1')).entries[0];
    assert.equal(listed.key, ed.key());
    const r = t.journal.retire([listed.key], [{ seq: listed.seq, val: 'durable' }]);
    assert.deepEqual(r.retired, [ed.key()]);
    assert.equal(ed.stored(), undefined);
    ed.field.blur();                                  // the same value again: not written back
    assert.equal(ed.stored(), undefined);
    h.type(ed, 'durable2');                           // a genuinely new value is journaled again
    assert.equal(ed.stored().val, 'durable2');
    t.close();
});

test('T27 a missing, older, different or still-pending echo keeps the entry; repeating a retirement touches nothing else', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = await typedEntry(t, 'v1');
    const seq1 = ed.stored().seq;
    h.type(ed, 'v12');
    const seq2 = ed.stored().seq;
    assert.equal(t.journal.retire([ed.key()], [{ seq: seq1, val: 'v1' }]).kept[0].reason, 'sequence', 'an older snapshot cannot delete the later edit');
    assert.equal(t.journal.retire([ed.key()], [{ seq: seq2, val: 'other' }]).kept[0].reason, 'value');
    assert.equal(t.journal.retire([ed.key()], []).kept[0].reason, 'invalid', 'no echo');
    assert.equal(t.journal.retire([ed.key()], { [ed.key()]: { seq: seq2, val: 'v12' } }).retired.length, 1, 'echo as a map');
    const other = await typedEntry(t, 'keep', { m: 'Other' });
    assert.equal(t.journal.retire([ed.key()], [{ seq: seq2, val: 'v12' }]).kept[0].reason, 'missing', 'repeat: already gone');
    assert.equal(other.stored().val, 'keep');
    // newer typing pending: an open composition
    const comp = await typedEntry(t, 'c', { m: 'Comp' });
    const s = comp.stored();
    h.composition(comp, 'compositionstart');
    h.input(comp, 'cね', true);
    assert.equal(t.journal.retire([comp.key()], [{ seq: s.seq, val: 'c' }]).kept[0].reason, 'pending');
    assert.equal(comp.stored().val, 'c');
    // a generation change is another key: an echo of g1 never touches g2
    const gen = await typedEntry(t, 'g1', { m: 'Gen' });
    const g1 = gen.stored();
    gen.setDescriptor({ g: 2 }); await h.flush();
    h.type(gen, 'g1x');
    t.journal.retire([gen.key()], [{ seq: g1.seq, val: 'g1' }]);
    assert.equal(gen.stored().val, 'g1x');
    // a refused write pending: kept
    const f = await typedEntry(t, 'f', { m: 'Failing' });
    const fs = f.stored();
    w.storage.failSets = 1;
    h.type(f, 'f2');
    assert.equal(t.journal.retire([f.key()], [{ seq: fs.seq, val: 'f' }]).kept[0].reason, 'pending');
    t.close();
});

test('T28 two tabs writing different entries interleaved keep both; storage events never make a tab rewrite', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A' });
    const b = await h.openTab(w, { name: 'B' });
    const ea = h.addEditor(a, { value: '' });
    const eb = h.addEditor(b, { value: '' });
    ea.field.focus(); eb.field.focus();
    h.type(ea, 'A1'); h.type(eb, 'B1'); h.type(ea, 'A12'); h.type(eb, 'B12');
    await a.deliver(); await b.deliver();
    const sets = w.storage.log.filter(x => x.op === 'set' && x.key.indexOf(PREFIX) === 0).length;
    await w.clock.advance(30000);
    await a.deliver(); await b.deliver();
    assert.equal(ea.stored().val, 'A12');
    assert.equal(eb.stored().val, 'B12');
    assert.equal(w.storage.log.filter(x => x.op === 'set' && x.key.indexOf(PREFIX) === 0).length, sets, 'no rewrite after the events');
    // with Web Locks too
    const w2 = h.world();
    const c = await h.openTab(w2, { locks: fakeLocks() });
    const d = await h.openTab(w2, { locks: fakeLocks() });
    const ec = h.addEditor(c, { value: '' }); const ed = h.addEditor(d, { value: '' });
    ec.field.focus(); ed.field.focus(); h.type(ec, 'C'); h.type(ed, 'D');
    await c.deliver(); await d.deliver();
    assert.equal(ec.stored().val, 'C'); assert.equal(ed.stored().val, 'D');
    a.close(); b.close(); c.close(); d.close();
});

test('T29 without Web Locks the heartbeat path works; an old heartbeat means unknown, never gone; with locks the lock is reported', async () => {
    const w = h.world();
    const a = await h.openTab(w, { locks: null });
    assert.equal(a.platform.webLocks, false);
    await typedEntry(a, 'x');
    let list = await a.journal.list('ns1');
    assert.equal(list.writers[a.journal.loadId].alive, true, 'fresh heartbeat');
    w.clock.now += 10 * 60000;                       // no heartbeat for ten minutes (setInterval is not run by the harness)
    list = await a.journal.list('ns1');
    assert.equal(list.writers[a.journal.loadId].alive, null, 'unknown, not false');
    assert.ok(list.writers[a.journal.loadId].heartbeatAgeMs >= 10 * 60000);
    const locks = fakeLocks();
    const b = await h.openTab(w, { locks });
    assert.equal(b.platform.webLocks, true);
    await h.flush();
    list = await b.journal.list('ns1');
    assert.equal(list.writers[b.journal.loadId].lock, true);
    assert.ok(locks.held.some(l => l.name === 'XafEditDraft.w|' + b.journal.loadId));
    a.close(); b.close();
});

test('T30 coverage groups attributed editors by view and context, counts invalid descriptors and names roots without a field', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    h.addEditor(t, { d: { m: 'Description' } });
    h.addEditor(t, { d: { m: 'Start', k: 'time', f: 'HH:mm' } });
    h.addEditor(t, { d: { m: 'Description', ctx: 'ffffffffffffffffffffffffffffffff' } });            // an inactive MDI tab of the same view
    h.addEditor(t, { d: { m: 'Name', w: 'ShiftType_DetailView' } });
    h.addEditor(t, { raw: '{broken', d: { m: 'Broken' } });
    const nofield = t.doc.createElement('dxbl-combo-box');
    nofield.setAttribute('data-editdraft', JSON.stringify(h.descriptor({ m: 'ReadOnlyLookup', k: 'combo' })));
    t.doc.body.appendChild(nofield);
    const c = t.journal.coverage();
    assert.equal(c.invalid, 1);
    const g = (w2, ctx) => c.groups.find(x => x.w === w2 && x.ctx === ctx);
    assert.deepEqual(g('ToDo_DetailView', h.CTX).members.map(m => m.m).sort(), ['Description', 'ReadOnlyLookup', 'Start']);
    assert.equal(g('ToDo_DetailView', h.CTX).members.find(m => m.m === 'ReadOnlyLookup').field, false);
    assert.deepEqual(g('ToDo_DetailView', 'ffffffffffffffffffffffffffffffff').members.map(m => m.m), ['Description']);
    assert.deepEqual(g('ShiftType_DetailView', h.CTX).members.map(m => m.m), ['Name']);
    assert.ok(!JSON.stringify(c).includes('"val"'), 'no values');
    t.close();
});

test('T31 the platform line reports secure context, Web Locks, storage.estimate and the user agent, and missing APIs do not stop start()', async () => {
    const w = h.world();
    const t = await h.openTab(w, {
        locks: fakeLocks(),
        patchWindow: (win) => { Object.defineProperty(win.navigator, 'storage', { value: { estimate: () => Promise.resolve({ usage: 10, quota: 5242880 }) }, configurable: true }); }
    });
    for (const k of ['load', 'format', 'isSecureContext', 'webLocks', 'storageEstimate', 'broadcastChannel', 'survivorCount', 'survivors', 'storageError', 'roundTrip', 'ua'])
        assert.ok(k in t.platform, k);
    assert.equal(t.platform.webLocks, true);
    assert.deepEqual(t.platform.storageEstimate, { usage: 10, quota: 5242880 });
    assert.equal(t.platform.roundTrip, 'ok');
    assert.equal(typeof t.platform.ua, 'string');
    const t2 = await h.openTab(w, { locks: null, patchWindow: (win) => { Object.defineProperty(win.navigator, 'storage', { value: undefined, configurable: true }); } });
    assert.equal(t2.platform.webLocks, false);
    assert.equal(t2.platform.storageEstimate, null);
    t.close(); t2.close();
});

test('T32 list(ns) returns only that namespace\'s unexpired entries, as metadata without values; it retires nothing; an unreadable store is not an empty success', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    await typedEntry(t, 'secret one', { m: 'A' });
    await typedEntry(t, 'secret two', { m: 'B', ns: 'ns2' });
    const list = await t.journal.list('ns1');
    assert.equal(list.ok, true);
    assert.deepEqual(list.entries.map(e => e.m), ['A']);
    const e = list.entries[0];
    for (const k of ['key', 'load', 'self', 'p', 't', 'o', 'ctx', 'w', 'm', 'k', 'fmt', 'co', 'bh', 'g', 'at', 'seq', 'len', 'tr', 'comp']) assert.ok(k in e, k);
    assert.equal(e.len, 'secret one'.length);
    assert.equal(e.self, true);
    assert.ok(!JSON.stringify(list).includes('secret'), 'no value in the listing');
    assert.equal(w.storage.entryKeys().length, 2, 'listing removes nothing');
    assert.equal((await t.journal.list('')).ok, false);
    w.storage.failOps = { ops: ['length', 'key'], name: 'SecurityError' };
    const broken = await t.journal.list('ns1');
    assert.equal(broken.ok, false);
    assert.deepEqual(broken.entries, []);
    t.close();
});

test('T33 value() returns the exact text as UTF-8 bytes in a Uint8Array (Japanese, quotes, control characters, lines)', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const text = '本日の体温は "36.5"\\ 度。\n二行目\t\u0001end';
    const ed = await typedEntry(t, text);
    const bytes = t.journal.value(ed.key(), ed.stored().seq);
    assert.ok(bytes instanceof Uint8Array || Object.prototype.toString.call(bytes) === '[object Uint8Array]');
    assert.equal(new TextDecoder().decode(bytes), text);
    assert.equal(t.journal.value('not-a-journal-key', 1), null);
    t.close();
});

test('T34 a value is handed out only for the sequence that was listed (frozen); an edit during transfer makes the old read return nothing', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = await typedEntry(t, 'first');
    const listed = (await t.journal.list('ns1')).entries[0];
    h.type(ed, 'first, then more');
    assert.equal(t.journal.value(listed.key, listed.seq), null, 'the listed snapshot changed: re-list');
    assert.equal(t.journal.retire([listed.key], [{ seq: listed.seq, val: 'first' }]).kept[0].reason, 'sequence', 'a lost or late acknowledgement of the old snapshot deletes nothing');
    const now = (await t.journal.list('ns1')).entries[0];
    assert.equal(new TextDecoder().decode(t.journal.value(now.key, now.seq)), 'first, then more');
    t.close();
});

test('T35 clear(ns) removes that namespace (entries and incomplete copies), keeps others, is repeatable, and drops work scheduled before it', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const a = await typedEntry(t, 'a', { m: 'A' });
    h.composition(a, 'compositionstart'); h.input(a, 'aた', true);
    const b = await typedEntry(t, 'b', { m: 'B', ns: 'ns2' });
    // work scheduled before the clear: a text composition's later read and a masked read
    const late = await typedEntry(t, 'x', { m: 'Late' });
    h.composition(late, 'compositionstart'); late.field.value = 'x一'; h.composition(late, 'compositionend');
    const m = h.addEditor(t, { d: { m: 'Masked', k: 'time', f: 'HH:mm' }, value: '01:00' });
    m.field.focus(); h.maskedKey(m, '2');
    m.root.setAttribute('field-text', '02:00');      // the observer callback queues its read now ...
    const r = t.journal.clear('ns1');                // ... and the clear runs before that read
    t.win.queueMicrotask(() => { m.field.value = '02:00'; });
    await w.clock.advance(0);
    assert.equal(r.ok, true);
    assert.ok(w.storage.entryKeys(true).every(k => k.indexOf(PREFIX + 'ns1|') !== 0), 'nothing of ns1 left or written back');
    assert.equal(b.stored().val, 'b', 'ns2 kept');
    assert.deepEqual(t.journal.clear('ns1'), { ok: true, removed: 0 }, 'repeatable');
    late.field.blur(); a.field.blur(); m.field.blur();
    assert.ok(w.storage.entryKeys(true).every(k => k.indexOf(PREFIX + 'ns1|') !== 0), 'focusout after the clear writes nothing back');
    w.storage.failOps = { ops: ['length'], name: 'SecurityError' };
    assert.deepEqual(t.journal.clear('ns1'), { ok: false, removed: 0, error: 'SecurityError' });
    t.close();
});

test('T36 a never-saved record\'s first edit stores the reconstruction raws with the entry; an untouched form stores nothing; they survive midnight', async () => {
    const w = h.world(Date.UTC(2026, 9, 4, 14, 50, 0));          // 23:50 in Japan
    const t = await h.openTab(w);
    const rc = { Owner: '7c9e6679-7425-40de-944b-e07fc1f90ae7', Day: '2026-10-04T00:00:00.0000000', Start: '2026-10-04T09:00:00.0000000', End: '2026-10-04T18:00:00.0000000' };
    const untouched = h.addEditor(t, { d: { m: 'Note', rc }, value: '' });
    untouched.field.focus(); untouched.field.blur();
    assert.equal(untouched.stored(), undefined);
    const ed = h.addEditor(t, { d: { m: 'Reason', rc }, value: '' });
    ed.field.focus();
    h.type(ed, '会議');
    assert.deepEqual(ed.stored().rc, rc);
    await w.clock.advance(20 * 60000);                // 00:10 the next day, within the 60 minutes
    const list = await t.journal.list('ns1');
    assert.deepEqual(list.entries.find(e => e.m === 'Reason').rc, rc);
    t.close();
});

// ------------------------------------------------------------------------------------------------ F6 race classes

test('T38 F6 D2: work holding a pre-discard value (later read, retry) never recreates a discarded entry', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = await typedEntry(t, 'v');
    h.composition(ed, 'compositionstart'); ed.field.value = 'v一'; h.composition(ed, 'compositionend');   // later read queued
    const f = await typedEntry(t, 'r', { m: 'Retry' });
    w.storage.failSets = 1;
    h.type(f, 'r2');                                  // refused: a retry is scheduled
    t.journal.clear('ns1');
    await w.clock.advance(5000);
    assert.deepEqual(w.storage.entryKeys(true).filter(k => k.indexOf(PREFIX + 'ns1|') === 0), []);
    t.close();
});

test('T39 F6 D3: an entry removed just before a discard is not restored by stale work; a NEW edit after the discard is journaled', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A' });
    const ed = await typedEntry(a, 'pre-discard');
    h.composition(ed, 'compositionstart'); ed.field.value = 'pre-discard一'; h.composition(ed, 'compositionend');
    w.storage.map.delete(ed.key());                   // removed elsewhere (another tab's eviction) just before the discard
    a.journal.clear('ns1');
    await w.clock.advance(0);
    ed.field.blur();
    assert.equal(ed.stored(), undefined, 'not restored');
    ed.field.focus();
    h.type(ed, 'after the discard');
    assert.equal(ed.stored().val, 'after the discard', 'decision of this build: a genuinely new edit after a discard is kept');
    a.close();
});

test('T41 F6 D5: after a discard or a removal, a read failure is reported and lists nothing (no fallback copy)', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    await typedEntry(t, 'earlier-load text');
    assert.equal((await t.journal.list('ns1')).entries.length, 1);
    t.journal.clear('ns1');
    w.storage.failOps = { ops: ['length', 'key', 'getItem'], name: 'SecurityError' };
    const r = await t.journal.list('ns1');
    assert.equal(r.ok, false);
    assert.equal(r.error, 'SecurityError');
    assert.deepEqual(r.entries, []);
    t.close();
});
