// Client-side input journal, milestone M1b cluster A: refused-write retry and clear ordering (requirement R-A1..R-A6).
// Collaborator run 2026-10-04-edit-draft-journal-m1b-a-89eeeb. Part 1 (D-ids) turns the reproductions of Codex's M1
// diffreview a2 (D3-D6, D10) into tests; they were run on the M1 module (HEAD 63622641) first and recorded red there.
// Part 2 (X-ids) comes from Codex's requirement-only `tests` call of this run (expectations stated before any code).
// M1b-C (2026-10-04, per-tab clears): the 28 tests that asserted the withdrawn cross-tab clear marker were DELETED, not
// revised (docs/edit-draft-client-journal-per-tab-clears-2026-10-04.md section 4.10); the 13 below are kept unchanged.
'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const h = require('../helpers/harness');

async function twoTabs(opts) {
    opts = opts || {};
    const w = h.world();
    if (opts.silentUnchanged) w.storage.silentUnchanged = true;
    const a = await h.openTab(w, { name: 'A', locks: opts.locks ? opts.locks() : null });
    const b = await h.openTab(w, { name: 'B', locks: opts.locks ? opts.locks() : null });
    return { w, a, b };
}

/** Web Locks whose grants the test holds until grantAll() (KB fix-552: test with held grants). */
function heldLocks() {
    const queue = [];
    return {
        queue,
        request(name, cb) { return new Promise(resolve => queue.push(() => resolve(cb({ name })))); },
        query() { return Promise.resolve({ held: [], pending: [] }); },
        grantAll() { for (const g of queue.splice(0)) g(); }
    };
}

/** Every value successfully stored under key from now on (setItem calls that got past the failure checks). */
function watchValues(w, key) {
    const seen = [];
    const prev = w.storage.beforeSet;
    w.storage.beforeSet = (k, v, tab) => {
        if (prev) prev(k, v, tab);
        if (k === key) seen.push(JSON.parse(v).val);
    };
    return seen;
}

/** Refuses setItem on one key only (marker and other keys keep working). */
function refuse(w, key) { w.storage.failKeys.push({ prefix: key, ops: ['setItem'], name: 'QuotaExceededError' }); }
function allow(w) { w.storage.failKeys = []; }

// ------------------------------------------------------------------------------------------------ part 1: a2 reproductions

test('D3a a retry writes the CURRENT value: A stored, B refused, back to A leaves A stored and nothing pending', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus();
    h.type(ed, 'A');
    w.storage.failSets = 1;
    h.type(ed, 'AB');                                   // refused
    h.type(ed, 'A', 'Backspace');                       // the user goes back to A before any retry
    assert.equal(t.journal.report().pendingWrites, 0, 'the refused B is no longer wanted');
    await w.clock.advance(5000);
    assert.equal(ed.field.value, 'A');
    assert.equal(ed.stored().val, 'A', 'stored = displayed');
    t.close();
});

test('D3a\' the emptied field is the intent: a refused empty value followed by a return to the stored text never stores the empty value', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: 'base' });
    ed.field.focus();
    h.type(ed, 'base1');
    w.storage.failSets = 1;
    h.type(ed, '', 'Backspace');                        // the user empties the field: refused
    h.type(ed, 'base1');                                // and types the stored text again
    await w.clock.advance(5000);
    assert.equal(ed.stored().val, 'base1');
    t.close();
});

test('D3b (X12) an older refused committed value, retried while a newer composition is open, neither writes the entry nor removes the copy', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus();
    w.storage.failSets = 1;
    h.type(ed, 'X');                                    // refused committed value
    h.composition(ed, 'compositionstart');
    h.input(ed, 'Xた', true);                           // the newer composition's incomplete copy is stored
    assert.equal(ed.composing().val, 'Xた');
    await w.clock.advance(1000);                        // the old retry runs while the composition is open
    assert.equal(ed.composing() && ed.composing().val, 'Xた', 'the newer incomplete copy is kept');
    assert.equal(ed.stored(), undefined, 'no entry write while the composition is open (M0 section 5 item 2)');
    ed.field.value = 'X体';
    h.composition(ed, 'compositionend', '体');          // the commit
    await w.clock.advance(5000);
    assert.equal(ed.stored().val, 'X体', 'the commit is stored, not the older refused value');
    assert.equal(ed.composing(), undefined, 'and the copy of that composition goes with it');
    t.close();
});

test('D10 a refused first write followed by the same value before the retry timer is stored at once (kills "de-duplication advances before the write succeeded")', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus();
    w.storage.failSets = 1;
    h.type(ed, 'new');                                  // refused
    w.clock.now += 100;
    h.input(ed, 'new', false);                          // the same value again at T+100, storage works now, no timer has run
    assert.equal(ed.stored() && ed.stored().val, 'new', 'stored by the second event, not by the timer');
    assert.equal(t.journal.report().pendingWrites, 0);
    t.close();
});

// ------------------------------------------------------------------------------------------------ part 2: Codex X-expectations
// From Codex's requirement-only list (tests a1 of this run: ordering rules O1-O9, expectations X1-X44). Expectations
// already covered by part 1 or by the M1 suite are not repeated: X4 first run = D4a, X5 core = D4b, X6 = D4c, X8 = D6,
// X10 = D3a, X12 = D3b, X15 = D5a, X26 = D10; X27, X29-X34, X36, X39, X40 = the M1 suite (unchanged).

test('X3 emptying the field is an ordered edit: an older refused value never comes back; only that field is affected', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const sibling = h.addEditor(t, { d: { m: 'Sibling' }, value: '' });
    sibling.field.focus(); h.type(sibling, 'sibling text');
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus();
    h.type(ed, 'A');
    w.clock.now += 10;
    refuse(w, ed.key());
    h.type(ed, 'AB');                                   // refused
    allow(w);
    const seen = watchValues(w, ed.key());
    w.clock.now += 10;
    h.input(ed, '', false, 'deleteContentBackward');   // the user empties the field
    await w.clock.advance(5000);
    ed.field.blur();
    t.win.dispatchEvent(new t.win.Event('pagehide'));
    assert.equal(ed.stored().val, '', 'the clear is recorded as an empty value');
    assert.ok(!seen.includes('AB'), 'the older refused value is never written');
    assert.equal(sibling.stored().val, 'sibling text', 'a sibling field is not cleared');
    t.close();
});

test('X11 refused B, C and D in turn: the retry stores D and never B or C', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus();
    h.type(ed, 'A');
    refuse(w, ed.key());
    const seen = watchValues(w, ed.key());
    w.clock.now += 10; h.type(ed, 'AB');
    w.clock.now += 10; h.type(ed, 'ABC');
    w.clock.now += 10; h.type(ed, 'ABCD');
    allow(w);
    await w.clock.advance(5000);
    assert.equal(ed.stored().val, 'ABCD');
    assert.deepEqual(seen, ['ABCD']);
    t.close();
});

test('X13 retries are bounded: the first attempt plus five timer retries, then only focusout or the next edit retries; a success on the last retry ends them', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus();
    refuse(w, ed.key());
    h.type(ed, 'x');
    for (let i = 0; i < 7; i++) await w.clock.advance(1000);
    let r = t.journal.report();
    assert.equal(r.stats.writeFailures, 6, 'first attempt + 5 retries');
    assert.equal(r.stats.retries, 5);
    assert.equal(r.pendingWrites, 1, 'still wanted: kept pending, no more timers');
    assert.equal(w.clock.timers.length, 0);
    ed.field.blur();                                    // focusout tries again, without renewing the allowance
    assert.ok(t.journal.report().stats.writeFailures > 6);
    assert.equal(w.clock.timers.length, 0, 'no new timer after the allowance is used up');
    allow(w);
    ed.field.focus(); ed.field.blur();
    assert.equal(ed.stored().val, 'x', 'written once storage accepts it');
    // second run: the fifth timer retry succeeds
    const w2 = h.world();
    const t2 = await h.openTab(w2);
    const e2 = h.addEditor(t2, { value: '' });
    e2.field.focus();
    w2.storage.failSets = 5;                            // the first attempt and four retries are refused
    h.type(e2, 'y');
    for (let i = 0; i < 7; i++) await w2.clock.advance(1000);
    r = t2.journal.report();
    assert.equal(e2.stored().val, 'y');
    assert.equal(r.stats.retries, 5);
    assert.equal(r.pendingWrites, 0);
    assert.equal(w2.clock.timers.length, 0, 'no retry after the success');
    t.close(); t2.close();
});

test('X14 a descriptor change never moves a refused value to the new key; the old key\'s own intent may finish under the old key (decision); the new baseline is no edit', async () => {
    for (const change of ['generation', 'record']) {
        const w = h.world();
        const t = await h.openTab(w);
        const ed = h.addEditor(t, { value: '' });
        ed.field.focus();
        const oldKey = ed.key();
        refuse(w, oldKey);
        h.type(ed, 'B');                                // refused under d0
        allow(w);
        ed.setDescriptor(change === 'generation' ? { g: 2 } : { ctx: 'ffffffffffffffffffffffffffffffff', o: '99999999-2222-3333-4444-555555555555', g: 1 });
        await h.flush();
        const seenNew = watchValues(w, ed.key());
        await w.clock.advance(5000);
        ed.field.blur(); ed.field.focus();
        assert.deepEqual(seenNew, [], change + ': nothing under the new key from the old intent or the new baseline');
        assert.equal(w.storage.entry(oldKey).val, 'B', change + ': the d0 intent finished under the d0 key');
        h.type(ed, 'BC');
        assert.equal(ed.stored().val, 'BC', change + ': a new edit is captured under the new key');
        t.close();
    }
    // a composition open across the change: no entry write under either key until it ends
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus();
    const oldKey = ed.key();
    refuse(w, oldKey);
    h.type(ed, 'B');
    allow(w);
    h.composition(ed, 'compositionstart');
    h.input(ed, 'Bた', true);
    ed.setDescriptor({ g: 2 });
    await h.flush();
    h.input(ed, 'Bたい', true);
    await w.clock.advance(5000);
    assert.equal(w.storage.entry(oldKey), undefined, 'no d0 entry write while composing');
    assert.equal(ed.stored(), undefined, 'no d1 entry write while composing');
    t.close();
});

test('X18 a clear with one removal refused: ok:false, removed counts the real removals, the surviving bytes are not overwritten by pending pre-clear work', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const k1 = h.addEditor(t, { d: { m: 'K1' }, value: '' });
    const k2 = h.addEditor(t, { d: { m: 'K2' }, value: '' });
    k1.field.focus(); h.type(k1, 'one');
    k2.field.focus(); h.type(k2, 'two');
    refuse(w, k1.key()); refuse(w, k2.key());
    k1.field.focus(); h.type(k1, 'one more');
    k2.field.focus(); h.type(k2, 'two more');
    const k2bytes = w.storage.map.get(k2.key());
    w.storage.failKeys.push({ prefix: k2.key(), ops: ['removeItem'], name: 'SecurityError' });
    const r = t.journal.clear('ns1');
    assert.equal(r.ok, false);
    assert.equal(r.removed, 1);
    assert.equal(r.error, 'SecurityError');
    assert.equal(k1.stored(), undefined);
    await w.clock.advance(5000);
    allow(w);
    await w.clock.advance(5000);
    k2.field.blur();
    assert.equal(k1.stored(), undefined);
    assert.equal(w.storage.map.get(k2.key()), k2bytes, 'not overwritten');
    t.close();
});

test('X28 de-duplication moves only after a successful write: the repeated value after a refusal is written, the next repeat is not', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus();
    h.type(ed, 'A');
    refuse(w, ed.key());
    h.type(ed, 'AB');
    allow(w);
    w.clock.now += 100;
    h.input(ed, 'AB', false);
    const first = ed.stored();
    assert.equal(first.val, 'AB');
    h.input(ed, 'AB', false);
    assert.equal(ed.stored().seq, first.seq, 'the same stored value is not written again');
    t.close();
});

test('X35 a pending retry writes the user\'s value, never a server-only value set meanwhile; focusout and page hide skip it; a new input resumes', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus();
    h.type(ed, 'typed');
    refuse(w, ed.key());
    h.type(ed, 'typed2');
    allow(w);
    const seen = watchValues(w, ed.key());
    await h.server(ed, 'from the server');             // field-text + value, no attempt waiting
    await w.clock.advance(5000);
    ed.field.blur();
    t.win.dispatchEvent(new t.win.Event('pagehide'));
    assert.equal(ed.stored().val, 'typed2');
    assert.ok(!seen.includes('from the server'));
    ed.field.focus();
    h.type(ed, 'from the server, edited');
    assert.equal(ed.stored().val, 'from the server, edited');
    t.close();
});

test('X41 two writers with held events and an unchanged-value write that raises no event keep each other\'s entries and latest values', async () => {
    for (const locks of [null, heldLocks]) {
        const { w, a, b } = await twoTabs({ locks, silentUnchanged: true });
        const ea = h.addEditor(a, { d: { m: 'KA' }, value: '' });
        const eb = h.addEditor(b, { d: { m: 'KB' }, value: '' });
        ea.field.focus(); h.type(ea, 'a1');
        eb.field.focus(); h.type(eb, 'b1');
        h.type(ea, 'a12');
        refuse(w, eb.key()); h.type(eb, 'b12'); allow(w);
        const same = w.storage.map.get(ea.key());
        w.storage.view({ name: 'C' }).setItem(ea.key(), same);   // identical serialized value: no event in a browser
        await w.clock.advance(5000);
        await a.deliver(); await b.deliver();
        await w.clock.advance(5000);
        assert.equal(ea.stored().val, 'a12');
        assert.equal(eb.stored().val, 'b12');
        a.close(); b.close();
    }
});

test('X42 a late removal event of an old value does not remove the newer value or the newer refused intent', async () => {
    for (const refused of [false, true]) {
        const { w, a, b } = await twoTabs();
        const eb = h.addEditor(b, { value: '' });
        eb.field.focus();
        h.type(eb, 'old');
        w.storage.view({ name: 'A' }).removeItem(eb.key());      // removed elsewhere (as an eviction would); event held
        if (refused) refuse(w, eb.key());
        h.type(eb, 'old C');
        allow(w);
        await b.deliver();                              // the old removal arrives now
        await w.clock.advance(5000);
        assert.equal(eb.stored() && eb.stored().val, 'old C', refused ? 'the refused newer value is retried' : 'the stored newer value stays');
        a.close(); b.close();
    }
});
