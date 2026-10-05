// Client-side input journal, milestone M1 — the 16 scenarios of the M0 rules harness
// (journal-rules-check-v2.mjs of the M0 gate run 2026-10-03-edit-draft-journal-m0-455e4d; not in this repository), restated by Codex as
// expectations T43-T58 (collaborator run 2026-10-04-edit-draft-journal-m1-96623a, `tests` a1) and run here against the
// M1 module with real DOM events in jsdom (the M0 harness used a hand-made mock DOM against the spike).
// Two M1 differences from the spike, both from the M0 write-up: no animation-frame read (a composition has ONE later read,
// so S4b/S4c count one drop per stale session), and the install report covers the library's own entries only (S13: the
// host's chart journal is not read by the library).
'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const h = require('../helpers/harness');

async function setup(value, d) {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { d, value });
    ed.field.focus();
    return { w, t, ed };
}

test('T43 S1 C4: focus in and out without an edit writes nothing', async () => {
    const { w, t, ed } = await setup('A');
    h.keydown(ed, 'Tab');
    ed.field.blur();
    await w.clock.advance(1000);
    assert.deepEqual(w.storage.entryKeys(true), []);
    t.close();
});

test('T44 S2 C4: an edit and its reversal A->B->A are both recorded', async () => {
    const { t, ed } = await setup('A');
    h.type(ed, 'B');
    assert.equal(ed.stored().val, 'B');
    h.type(ed, 'A', 'Backspace');
    assert.equal(ed.stored().val, 'A');
    t.close();
});

test('T45 S3 C2: blur and a mutation during composition never reach the entry; the commit does and clears the composing copy', async () => {
    const { t, ed } = await setup('x', { k: 'masked', f: 'LLL' });
    h.composition(ed, 'compositionstart');
    h.input(ed, 'xた', true);
    await h.server(ed, 'xた');
    ed.field.blur();
    assert.equal(ed.stored(), undefined, 'entry written during composition');
    assert.equal(ed.composing().val, 'xた');
    ed.field.focus();
    h.composition(ed, 'compositionend', '体');
    await h.server(ed, 'x体');
    assert.equal(ed.stored().val, 'x体');
    assert.equal(ed.composing(), undefined, 'composing copy kept');
    t.close();
});

test('T46 S4 C2: a delayed read of an earlier composition is dropped once a new one started', async () => {
    const { w, t, ed } = await setup('a');
    h.composition(ed, 'compositionstart'); ed.field.value = 'a一'; h.composition(ed, 'compositionend', '一');
    h.composition(ed, 'compositionstart'); h.input(ed, 'a一に', true);
    await w.clock.advance(0);
    assert.equal(ed.stored().val, 'a一');
    t.close();
});

test('T47 S5 C3: a server response after blur is recorded while the user action is pending', async () => {
    const { t, ed } = await setup('12:00', { k: 'time', f: 'HH:mm' });
    h.maskedKey(ed, '1');
    ed.field.blur();
    await h.server(ed, '01:00');
    assert.equal(ed.stored().val, '01:00');
    t.close();
});

test('T48 S6 C5: a refused write does not block the retry of the same value', async () => {
    const { w, t, ed } = await setup('A');
    w.storage.failSets = 1;
    h.type(ed, 'AZ');
    assert.equal(ed.stored(), undefined);
    ed.field.blur();
    assert.equal(ed.stored().val, 'AZ');
    t.close();
});

test('T49 S7 C4: a server re-read after the descriptor changes (record change / save) is not an edit', async () => {
    const { w, t, ed } = await setup('08:00', { k: 'time', f: 'HH:mm' });
    h.maskedKey(ed, '0');
    await h.server(ed, '00:00');
    const before = ed.stored().val;
    ed.setDescriptor({ ctx: 'c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2', o: '22222222-2222-3333-4444-555555555555', g: 2 });
    await h.server(ed, '17:30');
    assert.equal(before, '00:00');
    assert.equal(w.storage.entryKeys(true).filter(k => k.includes('|c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2|')).length, 0, 'server value journaled as an edit');
    t.close();
});

test('T50 S4b R5: the stale delayed read is DROPPED (not merely redirected) while a newer composition is open', async () => {
    const { w, t, ed } = await setup('a');
    h.composition(ed, 'compositionstart'); ed.field.value = 'a一'; h.composition(ed, 'compositionend', '一');
    h.composition(ed, 'compositionstart'); h.input(ed, 'a一に', true);
    const dropped = t.journal.report().stats.staleReadsDropped;
    const compBefore = JSON.stringify(ed.composing());
    await w.clock.advance(0);
    assert.equal(t.journal.report().stats.staleReadsDropped - dropped, 1, 'the old session\'s later read must be dropped');
    assert.equal(JSON.stringify(ed.composing()), compBefore, 'incomplete key changed by the stale read');
    assert.equal(ed.stored().val, 'a一');
    t.close();
});

test('T51 S4c R5: the stale read is dropped even after the newer composition has finished', async () => {
    const { w, t, ed } = await setup('a');
    h.composition(ed, 'compositionstart'); ed.field.value = 'a一'; h.composition(ed, 'compositionend', '一');
    h.composition(ed, 'compositionstart'); ed.field.value = 'a一二'; h.composition(ed, 'compositionend', '二');
    const dropped = t.journal.report().stats.staleReadsDropped;
    await w.clock.advance(0);
    assert.equal(t.journal.report().stats.staleReadsDropped - dropped, 1);
    assert.equal(ed.stored().val, 'a一二');
    t.close();
});

test('T52 S8 R1: a descriptor change during composition does not open the entry', async () => {
    const { t, ed } = await setup('q');
    h.composition(ed, 'compositionstart'); h.input(ed, 'qp1', true);
    ed.setDescriptor({ g: 2 });
    await h.flush();
    h.input(ed, 'qp2', true);
    ed.field.blur();
    assert.equal(ed.stored(), undefined, 'incomplete text reached the entry');
    t.close();
});

test('T53 S9 R2: a reply 5 s after the edit attempt (after blur) is still recorded', async () => {
    const { w, t, ed } = await setup('12:00', { k: 'time', f: 'HH:mm' });
    h.maskedKey(ed, '1');
    ed.field.blur();
    await w.clock.advance(5000);
    await h.server(ed, '01:00');
    assert.equal(ed.stored().val, '01:00');
    t.close();
});

test('T54 S10 R3: a composition cancelled back to the stored value leaves no incomplete copy', async () => {
    const { t, ed } = await setup('');
    h.type(ed, 'A');
    h.composition(ed, 'compositionstart'); h.input(ed, 'Apartial', true);
    assert.equal(ed.composing().val, 'Apartial');
    ed.field.value = 'A';
    h.composition(ed, 'compositionend', '');
    assert.equal(ed.stored().val, 'A');
    assert.equal(ed.composing(), undefined, 'stale incomplete copy kept');
    t.close();
});

test('T55 S11 R4: a printable key on an unchanged (read-only) field makes no entry', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: 'A', readOnly: true });
    ed.field.focus();
    h.keydown(ed, 'x');
    ed.field.blur();
    assert.equal(ed.stored(), undefined);
    const unchanged = h.addEditor(t, { d: { m: 'Unchanged' }, value: 'A' });
    unchanged.field.focus();
    h.keydown(unchanged, 'x');
    unchanged.field.blur();
    assert.equal(unchanged.stored(), undefined);
    t.close();
});

test('T56 S11b R4: a rejected masked character (no value change) makes no entry', async () => {
    const { t, ed } = await setup('12:00', { k: 'time', f: 'HH:mm' });
    h.maskedKey(ed, '9');
    await h.server(ed, '12:00');
    ed.field.blur();
    assert.equal(ed.stored(), undefined);
    t.close();
});

test('T57 S12 R7: a storage enumeration failure is reported by start(), not thrown', async () => {
    const w = h.world();
    w.storage.failOps = { ops: ['length'], name: 'SecurityError' };
    const t = await h.openTab(w);
    assert.equal(t.platform.storageError, 'SecurityError');
    t.close();
});

test('T58 S13 R6: start() reports earlier loads\' entries with value-free tail classes and no text', async () => {
    const w = h.world();
    const a = await h.openTab(w, { name: 'A' });
    const main = h.addEditor(a, { d: { m: 'T13' }, value: '' });
    main.field.focus();
    h.type(main, '本日の体温');
    const comp = h.addEditor(a, { d: { m: 'T13c' }, value: '' });
    comp.field.focus();
    h.composition(comp, 'compositionstart'); h.input(comp, '本日のねつ', true);
    a.close();
    const b = await h.openTab(w, { name: 'B' });
    const s = b.platform.survivors;
    assert.equal(b.platform.survivorCount, 2);
    assert.equal(s.find(x => x.m === 'T13' && !x.comp).tail, 'kanji');
    assert.equal(s.find(x => x.m === 'T13c' && x.comp).tail, 'hiragana');
    const text = JSON.stringify(b.platform);
    assert.ok(!text.includes('体温') && !text.includes('ねつ'), 'report must not carry text');
    b.close();
});
