// Client-side input journal, milestone M1 — capture rules of the browser module.
// Expectations come from the REQUIREMENT, stated by Codex before any M1 code existed (collaborator run
// 2026-10-04-edit-draft-journal-m1-96623a, `tests` call a1, requirement-only directory). Each test names the expectation
// id it covers (T1 ... T25). The module under test is the real edit-draft-journal.js (see helpers/harness.js).
// M1b-C (2026-10-04, per-load budget, O1-A expiry): T22 and T25 were DELETED, not revised
// (docs/edit-draft-client-journal-per-tab-clears-2026-10-04.md section 4.10); their replacements are in m1b-c.test.js.
'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const h = require('../helpers/harness');

const PREFIX = 'XafEditDraft.j1|';

test('T1 only a valid data-editdraft descriptor, found with closest(), makes an editor journaled; bad descriptors never throw', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    // an input outside any attributed root
    const plain = t.doc.createElement('textarea');
    t.doc.body.appendChild(plain);
    plain.focus();
    plain.value = 'outside';
    plain.dispatchEvent(new t.win.InputEvent('input', { bubbles: true }));
    // unusable descriptors
    const bad = ['{', JSON.stringify(h.descriptor({ v: 2 })), JSON.stringify(h.descriptor({ ns: '' })), JSON.stringify(h.descriptor({ ns: 'a|b' })),
                 JSON.stringify(h.descriptor({ k: 'rich' })), JSON.stringify(h.descriptor({ g: 0 })), JSON.stringify(Object.assign(h.descriptor(), { co: 'no' })), '[]', ''];
    for (const raw of bad) {
        const ed = h.addEditor(t, { raw, d: { m: 'Bad' + bad.indexOf(raw) } });
        ed.field.focus();
        assert.doesNotThrow(() => h.type(ed, 'x'));
        ed.field.blur();
    }
    assert.deepEqual(w.storage.entryKeys(true), [], 'no entry for an unattributed or badly attributed editor');
    assert.ok(t.journal.report().stats.invalidDescriptors >= bad.length - 1, 'invalid descriptors are counted');
    // a valid one, the event dispatched on the field inside the root (resolved with closest)
    const good = h.addEditor(t);
    good.field.focus();
    h.type(good, 'a');
    assert.equal(good.stored().val, 'a');
    t.close();
});

test('T2 every change of one (owner token, load, context, member, generation) rewrites that identity\'s own key, which carries the descriptor', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { d: { rc: undefined } });
    ed.field.focus();
    h.type(ed, 'a'); h.type(ed, 'ab'); h.type(ed, 'abc');
    const keys = w.storage.entryKeys();
    assert.deepEqual(keys, [PREFIX + ['ns1', t.journal.loadId, h.CTX, 'Description', '1'].join('|')]);
    const e = ed.stored();
    assert.equal(e.val, 'abc');
    for (const [k, v] of Object.entries({ ns: 'ns1', p: 'test:ToDo', t: 'ToDo', o: h.OID, ctx: h.CTX, w: 'ToDo_DetailView', m: 'Description', k: 'memo', co: false, bh: 'abcdef0123456789', g: 1, load: t.journal.loadId, f: 1 }))
        assert.deepEqual(e[k], v, k);
    assert.equal(typeof e.at, 'number');
    assert.equal(typeof e.seq, 'number');
    assert.equal(e.tr, false);
    // GUARD: 'CareCrew_InputJournal' is a real application's own localStorage key outside the library's prefix; the
    // library must never touch it.
    assert.ok(!w.storage.map.has('CareCrew_InputJournal'), 'no shared aggregate item');
    t.close();
});

test('T3 entries that differ in owner, page load, context, member or generation never share a key', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const variants = [{}, { ns: 'ns2' }, { ctx: 'ffffffffffffffffffffffffffffffff' }, { m: 'Other' }, { g: 2 }];
    const eds = variants.map(d => h.addEditor(t, { d }));
    eds.forEach((ed, i) => { ed.field.focus(); h.type(ed, 'v' + i); });
    assert.equal(new Set(eds.map(e => e.key())).size, variants.length);
    eds.forEach((ed, i) => assert.equal(ed.stored().val, 'v' + i));
    eds[0].field.focus(); h.type(eds[0], 'v0x');
    eds.slice(1).forEach((ed, i) => assert.equal(ed.stored().val, 'v' + (i + 1), 'changing one keeps the others'));
    const t2 = await h.openTab(w);
    assert.notEqual(t2.journal.loadId, t.journal.loadId, 'a reloaded page is another writer');
    t.close(); t2.close();
});

test('T4 a non-composing edit is in storage before the input handler returns (no timer, frame or acknowledgement)', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    for (const k of ['text', 'memo', 'combo']) {
        const ed = h.addEditor(t, { d: { m: 'M_' + k, k } });
        ed.field.focus();
        h.keydown(ed, 'q');
        ed.field.value = 'q';
        ed.field.dispatchEvent(new t.win.InputEvent('input', { bubbles: true }));
        assert.equal(w.storage.entry(ed.key()).val, 'q', k + ': written synchronously');
        h.keydown(ed, 'r');
        ed.field.value = 'qr';
        ed.field.dispatchEvent(new t.win.InputEvent('input', { bubbles: true }));
        assert.equal(w.storage.entry(ed.key()).val, 'qr', k + ': each change separately');
    }
    assert.equal(w.clock.timers.filter(x => x.at <= w.clock.now).length, 0, 'nothing waits on a timer');
    // a reload right after: the new page load sees the entry
    const t2 = await h.openTab(w);
    assert.ok(t2.platform.survivorCount >= 3);
    t.close(); t2.close();
});

test('T5 an entry needs an edit action AND a changed value; either alone makes none', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const actions = {
        beforeinput: (ed) => h.beforeinput(ed, 'x'),
        paste: (ed) => ed.field.dispatchEvent(new t.win.Event('paste', { bubbles: true })),
        cut: (ed) => ed.field.dispatchEvent(new t.win.Event('cut', { bubbles: true })),
        printable: (ed) => h.keydown(ed, 'x'),
        backspace: (ed) => h.keydown(ed, 'Backspace'),
        del: (ed) => h.keydown(ed, 'Delete'),
        ime: (ed) => h.keydown(ed, 'Process', { keyCode: 229 })
    };
    for (const [name, act] of Object.entries(actions)) {
        const ed = h.addEditor(t, { d: { m: 'A_' + name }, value: 'base' });
        ed.field.focus();
        act(ed);
        ed.field.value = 'changed';
        ed.field.blur();                             // the final read at focusout
        assert.equal(ed.stored() && ed.stored().val, 'changed', name + ' + change → entry');
    }
    // change without an action (a value set by script, then focusout)
    const silent = h.addEditor(t, { d: { m: 'Silent' }, value: 'base' });
    silent.field.focus();
    silent.field.value = 'set by script';
    silent.field.blur();
    assert.equal(silent.stored(), undefined, 'change without an action');
    // action without a change
    const still = h.addEditor(t, { d: { m: 'Still' }, value: 'base' });
    still.field.focus();
    h.keydown(still, 'x');
    still.field.blur();
    assert.equal(still.stored(), undefined, 'action without a change');
    // masked: ArrowUp and wheel are edit actions; the server's reply is read
    for (const how of ['ArrowUp', 'wheel']) {
        const m = h.addEditor(t, { d: { m: 'Masked_' + how, k: 'time', f: 'HH:mm' }, value: '07:15' });
        m.field.focus();
        if (how === 'wheel') m.field.dispatchEvent(new t.win.WheelEvent('wheel', { bubbles: true, deltaY: -1 }));
        else h.keydown(m, 'ArrowUp');
        await h.server(m, '07:16');
        assert.equal(m.stored().val, '07:16', how);
    }
    t.close();
});

test('T6 focus, focusout, Tab, a rejected key, read-only typing and a server re-read make no entry, also after delayed work', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const a = h.addEditor(t, { d: { m: 'Focus' }, value: 'A' });
    a.field.focus(); a.field.blur();
    const b = h.addEditor(t, { d: { m: 'Tab' }, value: 'A' });
    b.field.focus(); h.keydown(b, 'Tab'); b.field.blur();
    const c = h.addEditor(t, { d: { m: 'Rejected', k: 'masked', f: '00' }, value: '12' });
    c.field.focus(); h.maskedKey(c, 'z'); await h.server(c, '12'); c.field.blur();
    const d = h.addEditor(t, { d: { m: 'ReadOnly' }, value: 'A', readOnly: true });
    d.field.focus(); h.keydown(d, 'x'); h.beforeinput(d, 'x'); d.field.blur();
    const e = h.addEditor(t, { d: { m: 'Server', k: 'time', f: 'HH:mm' }, value: '08:00' });
    await h.server(e, '17:30');                     // a re-render with no edit attempt
    const f = h.addEditor(t, { d: { m: 'ServerText' }, value: 'old' });
    f.field.focus(); f.field.value = 'from the server'; f.root.setAttribute('field-text', 'from the server'); await h.flush(); f.field.blur();
    await w.clock.advance(120000);
    assert.deepEqual(w.storage.entryKeys(true), []);
    t.close();
});

test('T6b a value the server sets on a focused text editor (field-text, no input event) is not read, even after the user typed', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus();
    h.type(ed, 'typed');
    await h.server(ed, 'set by the server');        // field-text + the value, as a re-render would
    await w.clock.advance(1000);
    assert.equal(ed.stored().val, 'typed', 'text editors are recorded from input events only');
    t.close();
});

test('T7 an emptied field is stored as a clear; A→B→A stores B then A and keeps the entry', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const clear = h.addEditor(t, { d: { m: 'Clear' }, value: 'A' });
    clear.field.focus();
    h.type(clear, '', 'Backspace');
    assert.equal(clear.stored().val, '', 'a clear is a recorded empty value');
    const rev = h.addEditor(t, { d: { m: 'Reverse' }, value: 'A' });
    rev.field.focus();
    h.type(rev, 'B');
    assert.equal(rev.stored().val, 'B');
    h.type(rev, 'A');
    assert.equal(rev.stored().val, 'A', 'the return to the baseline is recorded, not removed');
    rev.field.blur();
    assert.equal(rev.stored().val, 'A');
    t.close();
});

test('T8 a new descriptor (record change, save, generation) starts a new baseline; its server re-read is not an edit', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { d: { m: 'Start', k: 'time', f: 'HH:mm' }, value: '08:00' });
    ed.field.focus();
    h.maskedKey(ed, '0');
    await h.server(ed, '00:00');
    const g1 = ed.key();
    assert.equal(w.storage.entry(g1).val, '00:00');
    ed.setDescriptor({ g: 2 });                      // after a save: same context, next generation
    await h.flush();
    await h.server(ed, '00:00');                     // the save's re-render
    ed.setDescriptor({ ctx: 'ffffffffffffffffffffffffffffffff', o: '99999999-2222-3333-4444-555555555555', g: 1 });   // another record in the same editor
    await h.flush();
    await h.server(ed, '17:30');
    assert.deepEqual(w.storage.entryKeys(), [g1], 'no entry for either re-read');
    assert.equal(w.storage.entry(g1).val, '00:00', 'the old identity keeps its own last value');
    h.maskedKey(ed, '1');
    await h.server(ed, '17:31');
    assert.equal(ed.stored().val, '17:31', 'a later user change uses the new identity');
    t.close();
});

test('T9 while a composition is open no path writes the entry: input, focusout, masked read, delayed read, page hide, descriptor change', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const text = h.addEditor(t, { d: { m: 'Text' }, value: '' });
    text.field.focus();
    h.type(text, '本');
    assert.equal(text.stored().val, '本');
    h.composition(text, 'compositionstart');
    h.input(text, '本た', true);
    text.field.blur();                               // focusout during composition
    t.win.dispatchEvent(new t.win.Event('pagehide'));
    text.setDescriptor({ g: 2 });                    // descriptor change during composition
    await h.flush();
    text.field.focus();
    h.input(text, '本たい', true);
    await w.clock.advance(1000);
    assert.equal(w.storage.entry(PREFIX + ['ns1', t.journal.loadId, h.CTX, 'Text', '1'].join('|')).val, '本', 'g1 entry unchanged');
    assert.equal(text.stored(), undefined, 'no g2 entry while composing');
    const masked = h.addEditor(t, { d: { m: 'Masked', k: 'masked', f: '000' }, value: '' });
    masked.field.focus();
    h.composition(masked, 'compositionstart');
    h.input(masked, '１', true);
    await h.server(masked, '１');                    // a mutation read during composition
    assert.equal(masked.stored(), undefined, 'masked: no entry during composition');
    assert.equal(masked.composing().val, '１', 'the incomplete copy only');
    t.close();
});

test('T10 an interrupted composition stays an incomplete copy under its own key; the committed entry is unchanged', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: '体温' });
    ed.field.focus();
    h.type(ed, '体温。');
    h.composition(ed, 'compositionstart');
    h.input(ed, '体温。ねつ', true);
    // the page goes away here: no compositionend
    assert.equal(ed.stored().val, '体温。');
    const c = ed.composing();
    assert.equal(c.val, '体温。ねつ');
    assert.equal(c.comp, true, 'marked incomplete');
    assert.notEqual(ed.composingKey(), ed.key());
    t.close();
});

test('T11 text editor: compositionend writes the committed value once; the later read is de-duplicated and the copy is cleaned', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus();
    h.composition(ed, 'compositionstart');
    h.input(ed, 'たいおん', true);
    h.input(ed, '体温', true);
    ed.field.value = '体温';
    h.composition(ed, 'compositionend', '体温');
    const first = ed.stored();
    assert.equal(first.val, '体温');
    assert.equal(ed.composing(), undefined, 'incomplete copy removed');
    await w.clock.advance(0);                         // the later read
    const after = ed.stored();
    assert.equal(after.val, '体温');
    assert.equal(after.seq, first.seq, 'no second write of the same value');
    assert.equal(w.storage.entryKeys(true).length, 1);
    t.close();
});

test('T12 masked editor: compositionend does not promote the old display; the next field-text read does', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { d: { m: 'Start', k: 'time', f: 'HH:mm' }, value: '09:00' });
    ed.field.focus();
    h.composition(ed, 'compositionstart');
    h.input(ed, '１２３０', true);
    h.composition(ed, 'compositionend', '１２３０');
    assert.equal(ed.stored(), undefined, 'nothing promoted at compositionend');
    await h.server(ed, '12:30');
    assert.equal(ed.stored().val, '12:30');
    assert.equal(ed.composing(), undefined, 'incomplete copy removed by the read');
    t.close();
});

test('T13 delayed work of an earlier composition is dropped (counted) after a newer one started or finished; nothing is written by it', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: 'a' });
    ed.field.focus();
    h.composition(ed, 'compositionstart'); ed.field.value = 'a一'; h.composition(ed, 'compositionend', '一');
    h.composition(ed, 'compositionstart'); h.input(ed, 'a一に', true);
    const before = t.journal.report().stats.staleReadsDropped;
    const main = JSON.stringify(ed.stored());
    const comp = JSON.stringify(ed.composing());
    await w.clock.advance(0);
    assert.equal(t.journal.report().stats.staleReadsDropped - before, 1, 'the stale read is dropped');
    assert.equal(JSON.stringify(ed.stored()), main);
    assert.equal(JSON.stringify(ed.composing()), comp);
    // after the newer one finished too
    const ed2 = h.addEditor(t, { d: { m: 'Two' }, value: 'a' });
    ed2.field.focus();
    h.composition(ed2, 'compositionstart'); ed2.field.value = 'a一'; h.composition(ed2, 'compositionend', '一');
    h.composition(ed2, 'compositionstart'); ed2.field.value = 'a一二'; h.composition(ed2, 'compositionend', '二');
    const b2 = t.journal.report().stats.staleReadsDropped;
    await w.clock.advance(0);
    assert.equal(t.journal.report().stats.staleReadsDropped - b2, 1, 'only the older session\'s read is dropped');
    assert.equal(ed2.stored().val, 'a一二');
    t.close();
});

test('T14 every committed path removes the incomplete copy, also when the committed value equals the stored one', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const paths = {
        input: async (ed) => { h.composition(ed, 'compositionstart'); h.input(ed, 'Ax', true); ed.field.value = 'A'; h.composition(ed, 'compositionend'); },
        focusout: async (ed) => { h.composition(ed, 'compositionstart'); h.input(ed, 'Ax', true); ed.field.value = 'A'; h.composition(ed, 'compositionupdate');
                                  // composition cancelled by the browser without compositionend, then focus leaves
                                  ed.field.dispatchEvent(new t.win.CompositionEvent('compositionend', { data: '', bubbles: true })); ed.field.blur(); },
        delayed: async (ed) => { h.composition(ed, 'compositionstart'); h.input(ed, 'Ax', true); ed.field.value = 'A'; h.composition(ed, 'compositionend'); await w.clock.advance(0); }
    };
    for (const [name, run] of Object.entries(paths)) {
        const ed = h.addEditor(t, { d: { m: 'P_' + name }, value: '' });
        ed.field.focus();
        h.type(ed, 'A');
        await run(ed);
        assert.equal(ed.stored().val, 'A', name);
        assert.equal(ed.composing(), undefined, name + ': incomplete copy removed');
    }
    const m = h.addEditor(t, { d: { m: 'P_mutation', k: 'masked', f: '000' }, value: '' });
    m.field.focus();
    h.maskedKey(m, '1'); await h.server(m, '1');
    h.composition(m, 'compositionstart'); h.input(m, '1２', true);
    h.composition(m, 'compositionend');
    await h.server(m, '1');                          // the server keeps the value: equal to the stored entry
    assert.equal(m.stored().val, '1');
    assert.equal(m.composing(), undefined, 'mutation path removes the copy');
    t.close();
});

test('T15 masked editors are read in the observer\'s microtask, with no animation frame available; an unrelated mutation is not an edit', async () => {
    const w = h.world();
    const t = await h.openTab(w, { patchWindow: (win) => { win.requestAnimationFrame = undefined; } });
    const ed = h.addEditor(t, { d: { m: 'Start', k: 'time', f: 'HH:mm' }, value: '00:57' });
    ed.field.focus();
    h.maskedKey(ed, '1');
    ed.root.setAttribute('field-text', '17:57');
    let seenInCallback = null;
    new t.win.MutationObserver(() => { seenInCallback = ed.field.value; }).observe(ed.root, { attributes: true, attributeFilter: ['field-text-version'] });
    t.win.queueMicrotask(() => { ed.field.value = '17:57'; });
    await h.flush();
    assert.equal(ed.stored().val, '17:57', 'the updated display, not the value seen inside the callback');
    ed.field.blur();
    const other = h.addEditor(t, { d: { m: 'Other', k: 'time', f: 'HH:mm' }, value: '01:00' });
    other.root.setAttribute('sync-selection-start', '3');
    await h.server(other, '02:00');
    assert.equal(other.stored(), undefined);
    t.close();
});

test('T16 masked edits need no input event; a rejected key with an unchanged display makes no entry', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { d: { m: 'Start', k: 'time', f: 'HH:mm' }, value: '00:58' });
    ed.field.focus();
    const seen = [];
    for (const [key, shown] of [['0', '00:58'], ['7', '07:58'], ['1', '07:01'], ['5', '07:15']]) {
        h.maskedKey(ed, key);
        await h.server(ed, shown);
        seen.push(ed.stored() ? ed.stored().val : null);
    }
    assert.deepEqual(seen, [null, '07:58', '07:01', '07:15'], 'the first digit left the display unchanged: no entry yet');
    const rejected = h.addEditor(t, { d: { m: 'End', k: 'time', f: 'HH:mm' }, value: '12:00' });
    rejected.field.focus();
    h.maskedKey(rejected, '9');
    await h.server(rejected, '12:00');
    rejected.field.blur();
    assert.equal(rejected.stored(), undefined);
    t.close();
});

test('T17 a reply after blur belongs to its pending operation (5 s later still read); a descriptor change cancels the old operation', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { d: { m: 'Start', k: 'time', f: 'HH:mm' }, value: '12:00' });
    ed.field.focus();
    h.maskedKey(ed, '1');
    ed.field.blur();
    await w.clock.advance(5000);
    await h.server(ed, '01:00');
    assert.equal(ed.stored().val, '01:00');
    // two overlapping attempts, answered in order
    const two = h.addEditor(t, { d: { m: 'Two', k: 'time', f: 'HH:mm' }, value: '12:00' });
    two.field.focus();
    h.maskedKey(two, '0'); h.maskedKey(two, '8');
    two.field.blur();
    await h.server(two, '08:00');
    assert.equal(two.stored().val, '08:00');
    // a descriptor change between the attempt and the reply
    const moved = h.addEditor(t, { d: { m: 'Moved', k: 'time', f: 'HH:mm' }, value: '12:00' });
    moved.field.focus();
    h.maskedKey(moved, '3');
    moved.field.blur();
    moved.setDescriptor({ ctx: 'ffffffffffffffffffffffffffffffff', g: 1 });
    await h.flush();
    await h.server(moved, '03:00');
    assert.equal(moved.stored(), undefined, 'not attributed to the new descriptor');
    t.close();
});

test('T18 an attempt with no reply inside the window is reported capture-unresolved; a timely reply is not', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { d: { m: 'Start', k: 'time', f: 'HH:mm' }, value: '12:00' });
    ed.field.focus();
    h.maskedKey(ed, '1');
    ed.field.blur();
    await w.clock.advance(59999);
    assert.equal(t.journal.report().stats.unresolved, 0, 'still inside the window');
    await w.clock.advance(1);
    const r = t.journal.report();
    assert.equal(r.stats.unresolved, 1);
    assert.ok(r.errors.some(e => e.op === 'capture-unresolved' && e.m === 'Start'));
    assert.equal(ed.stored(), undefined, 'no value is invented');
    await h.server(ed, '01:00');                     // a reply after the window, field not focused
    assert.equal(ed.stored(), undefined, 'a reply after the window is not read');
    const ok = h.addEditor(t, { d: { m: 'End', k: 'time', f: 'HH:mm' }, value: '12:00' });
    ok.field.focus();
    h.maskedKey(ok, '2');
    await h.server(ok, '02:00');
    await w.clock.advance(60000);
    assert.equal(t.journal.report().stats.unresolved, 1, 'answered in time: not unresolved');
    t.close();
});

test('T19 the journal never cancels or stops an event and never touches the value, selection or caret', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: 'abc' });
    const seen = [];
    t.win.addEventListener('keydown', (e) => seen.push(['keydown', e.defaultPrevented]));
    t.win.addEventListener('beforeinput', (e) => seen.push(['beforeinput', e.defaultPrevented]));
    t.win.addEventListener('input', (e) => seen.push(['input', e.defaultPrevented]));
    ed.field.focus();
    ed.field.setSelectionRange(1, 2);
    h.keydown(ed, 'x');
    h.beforeinput(ed, 'x');
    ed.field.dispatchEvent(new t.win.InputEvent('input', { bubbles: true }));
    assert.deepEqual(seen, [['keydown', false], ['beforeinput', false], ['input', false]], 'events reach the window, not cancelled');
    assert.equal(ed.field.value, 'abc');
    assert.deepEqual([ed.field.selectionStart, ed.field.selectionEnd], [1, 2]);
    t.close();
});

test('T20 a refused write keeps earlier entries, is reported, does not move de-duplication and is written by a later attempt', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: 'A' });
    const other = h.addEditor(t, { d: { m: 'Other' }, value: '' });
    other.field.focus(); h.type(other, 'kept'); other.field.blur();
    ed.field.focus();
    h.type(ed, 'AB');
    w.storage.failSets = 1;
    h.type(ed, 'ABC');
    assert.equal(ed.stored().val, 'AB', 'the earlier value survives the refusal');
    assert.equal(other.stored().val, 'kept', 'other entries survive');
    assert.equal(t.journal.report().stats.writeFailures, 1);
    assert.ok(t.journal.report().errors.some(e => e.op === 'write' && e.error === 'QuotaExceededError'));
    ed.field.blur();                                 // the same value again: not suppressed by de-duplication
    assert.equal(ed.stored().val, 'ABC');
    // the retry timer writes it when no later event comes
    const ed2 = h.addEditor(t, { d: { m: 'Timer' }, value: '' });
    ed2.field.focus();
    w.storage.failSets = 1;
    h.type(ed2, 'x');
    assert.equal(ed2.stored(), undefined);
    await w.clock.advance(1000);
    assert.equal(ed2.stored().val, 'x');
    t.close();
});

test('T20b at the 60-entry cap a refused write evicts nothing', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    seed(w, 60, w.clock.now - 1000);
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus();
    w.storage.failSets = 1;
    h.type(ed, 'x');
    assert.equal(w.storage.entryKeys(true).length, 60, 'all 60 earlier entries are still there');
    t.close();
});

test('T21 storage failures are reported by every operation and never thrown', async () => {
    const w = h.world();
    w.storage.failOps = { ops: 'all', name: 'SecurityError' };
    let t;
    await assert.doesNotReject(async () => { t = await h.openTab(w); });
    assert.equal(t.platform.storageError, 'SecurityError');
    assert.equal(t.platform.roundTrip, 'SecurityError');
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus();
    assert.doesNotThrow(() => { h.type(ed, 'x'); ed.field.blur(); });
    const list = await t.journal.list('ns1');
    assert.equal(list.ok, false);
    assert.equal(list.error, 'SecurityError');
    assert.equal(t.journal.clear('ns1').ok, false);
    assert.equal(t.journal.retire([ed.key()], [{ seq: 1, val: 'x' }]).kept[0].reason, 'SecurityError');
    assert.equal(t.journal.value(ed.key(), 1), null);
    assert.ok(t.journal.report().stats.storageErrors > 0);
    // no storage object at all
    const w2 = h.world();
    const t2 = await h.openTab(w2, { storage: null });
    assert.equal(t2.platform.storageError, 'StorageUnavailable');
    t.close(); t2.close();
});

/** Seeds n journal entries of another page load directly into storage, times start, start+1, ... */
function seed(w, n, start, opts) {
    opts = opts || {};
    const keys = [];
    for (let i = 0; i < n; i++) {
        const key = PREFIX + ['seedns', opts.load || 'seedload', h.CTX, 'S' + String(i).padStart(3, '0'), '1'].join('|');
        const at = opts.sameTime ? start : start + i;
        w.storage.map.set(key, JSON.stringify({ at, f: 1, seq: i + 1, ns: 'seedns', load: opts.load || 'seedload', p: 'p', t: 't', o: null, ctx: h.CTX, w: 'v', m: 'S' + i, k: 'memo', fmt: null, co: false, bh: null, g: 1, val: opts.val || 'x', tr: false }));
        keys.push(key);
    }
    return keys;
}

test('T23 values up to 12,000 characters are exact; longer ones are stored truncated and flagged, never split inside a surrogate pair', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    for (const n of [11999, 12000, 12001]) {
        const ed = h.addEditor(t, { d: { m: 'L' + n }, value: '' });
        ed.field.focus();
        h.type(ed, 'あ'.repeat(n));
        const e = ed.stored();
        assert.equal(e.val.length, Math.min(n, 12000), String(n));
        assert.equal(e.tr, n > 12000, String(n));
    }
    const pair = h.addEditor(t, { d: { m: 'Pair' }, value: '' });
    pair.field.focus();
    h.type(pair, 'a'.repeat(11999) + '𠮷x');          // the pair straddles 12,000
    const e = pair.stored();
    assert.equal(e.tr, true);
    assert.equal(e.val.length, 11999, 'the high surrogate is not kept alone');
    t.close();
});

test('T24 the size budget counts the serialized entry (quotes and control characters), not the raw text', async () => {
    const w = h.world();
    const t = await h.openTab(w, { limits: { serializedChars: 3000 } });
    const raw = h.addEditor(t, { d: { m: 'Plain' }, value: '' });
    raw.field.focus(); h.type(raw, 'a'.repeat(1000));
    w.clock.now += 10;
    const quoted = h.addEditor(t, { d: { m: 'Quoted' }, value: '' });
    quoted.field.focus(); h.type(quoted, '"'.repeat(1000));   // 1,000 characters, about 2,000 serialized
    assert.ok(!w.storage.map.has(raw.key()), 'the older entry was evicted although the raw text totals only 2,000');
    assert.ok(w.storage.map.has(quoted.key()));
    const size = quoted.key().length + w.storage.map.get(quoted.key()).length;
    assert.ok(size > 2000 && size <= 3000);
    const ctl = h.addEditor(t, { d: { m: 'Control' }, value: '' });
    ctl.field.focus(); h.type(ctl, '\u0001'.repeat(10));
    assert.ok(w.storage.map.get(ctl.key()).includes('\\u0001'), 'control characters serialize as six characters each');
    t.close();
});
