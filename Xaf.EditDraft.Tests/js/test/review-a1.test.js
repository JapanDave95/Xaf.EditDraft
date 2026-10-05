// Client-side input journal, milestone M1 — schedules from Codex's diffreview a1 (collaborator run
// 2026-10-04-edit-draft-journal-m1-96623a). Each test is the "decisive check" Codex named for one defect (C1-C10), run
// against the real module. Written AFTER the review and run once on the unfixed module (expected fail-before, recorded as
// the sensitivity check), then on the fixed module. The fixes are NOT cross-reviewed (two-pass limit).
// M1b-C (2026-10-04, per-tab clears): C4 (cross-tab clear) and C6 (heartbeat-only foreign retire without Web Locks) were
// DELETED, not revised (docs/edit-draft-client-journal-per-tab-clears-2026-10-04.md section 4.10).
'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const h = require('../helpers/harness');

test('C1 two masked attempts answered by two separate replies after blur: the second reply is the stored value; nothing unresolved', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { d: { m: 'Start', k: 'time', f: 'HH:mm' }, value: '12:00' });
    ed.field.focus();
    h.maskedKey(ed, '0');
    h.maskedKey(ed, '8');
    ed.field.blur();
    await h.server(ed, '00:00');
    await h.server(ed, '08:00');
    assert.equal(ed.stored().val, '08:00');
    await w.clock.advance(60000);
    assert.equal(t.journal.report().stats.unresolved, 0);
    t.close();
});

test('C2 masked composition: blur or page hide between compositionend and the mask reply never promotes the unprocessed text', async () => {
    for (const between of ['blur', 'pagehide', 'input']) {
        const w = h.world();
        const t = await h.openTab(w);
        const ed = h.addEditor(t, { d: { m: 'Start', k: 'time', f: 'HH:mm' }, value: '09:00' });
        ed.field.focus();
        h.composition(ed, 'compositionstart');
        h.input(ed, '1230', true);
        h.composition(ed, 'compositionend', '1230');
        if (between === 'blur') ed.field.blur();
        else if (between === 'pagehide') t.win.dispatchEvent(new t.win.Event('pagehide'));
        else h.input(ed, '1230', false);
        assert.equal(ed.stored(), undefined, between + ': no entry before the mask reply');
        assert.equal(ed.composing().val, '1230', between + ': the incomplete copy stays');
        await h.server(ed, '12:30');
        assert.equal(ed.stored().val, '12:30', between);
        assert.equal(ed.composing(), undefined, between);
        t.close();
    }
});

test('C3 a value the server sets after the user acted is never journaled by blur, page hide or a later focused re-render', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const typed = h.addEditor(t, { d: { m: 'Typed' }, value: '' });
    typed.field.focus();
    h.type(typed, 'typed');
    await h.server(typed, 'server');
    typed.field.blur();
    assert.equal(typed.stored().val, 'typed', 'blur after a server value');
    const rejected = h.addEditor(t, { d: { m: 'Rejected' }, value: 'A' });
    rejected.field.focus();
    h.keydown(rejected, 'x');                        // an attempt that changed nothing
    await h.server(rejected, 'from the server');
    t.win.dispatchEvent(new t.win.Event('pagehide'));
    rejected.field.blur();
    assert.equal(rejected.stored(), undefined, 'a rejected attempt does not authorize a later server value');
    const masked = h.addEditor(t, { d: { m: 'Masked', k: 'time', f: 'HH:mm' }, value: '09:00' });
    masked.field.focus();
    h.maskedKey(masked, '1');
    await h.server(masked, '10:00');
    assert.equal(masked.stored().val, '10:00');
    await h.server(masked, '11:00');                // a server update with no attempt waiting, field still focused
    masked.field.blur();
    assert.equal(masked.stored().val, '10:00', 'the later server update is not an edit');
    t.close();
});

test('C5 a refused committed write keeps the incomplete composition copy (also for the next page load)', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus();
    h.composition(ed, 'compositionstart');
    h.input(ed, 'たいおん', true);
    assert.equal(ed.composing().val, 'たいおん');
    w.storage.failSets = 10;                         // every write refused from here
    ed.field.value = '体温';
    h.composition(ed, 'compositionend', '体温');
    assert.ok(w.storage.entryKeys(true).length >= 1, 'the earlier recoverable copy is still stored');
    assert.equal(ed.composing().val, 'たいおん');
    t.close();
    w.storage.failSets = 0;
    const next = await h.openTab(w);
    assert.equal(next.platform.survivors.filter(s => s.comp).length, 1);
    next.close();
});

test('C7 a partial storage failure is reported as a failure by list, clear and retire', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus(); h.type(ed, 'x');
    const e = ed.stored();
    w.storage.failOps = { ops: ['getItem'], name: 'SecurityError' };
    const list = await t.journal.list('ns1');
    assert.equal(list.ok, false, 'list with unreadable entries is not a successful empty list');
    assert.equal(t.journal.retire([ed.key()], [{ seq: e.seq, val: 'x' }]).retired.length, 0, 'cannot read: not retired');
    w.storage.failOps = { ops: ['removeItem'], name: 'SecurityError' };
    const c = t.journal.clear('ns1');
    assert.equal(c.ok, false, 'a clear that removed nothing is not ok');
    assert.equal(ed.stored().val, 'x');
    w.storage.failOps = null;
    t.close();
});

test('C8 value() returns nothing for an entry 60 minutes old, even when it was listed before', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus(); h.type(ed, 'x');
    const listed = (await t.journal.list('ns1')).entries[0];
    w.clock.now += 60 * 60000 - 1;
    assert.equal(new TextDecoder().decode(t.journal.value(listed.key, listed.seq)), 'x');
    w.clock.now += 1;
    assert.equal(t.journal.value(listed.key, listed.seq), null);
    t.close();
});

test('C9 a delayed read scheduled under one descriptor is dropped after the descriptor changed, even when the new one has an action', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: 'a' });
    ed.field.focus();
    h.composition(ed, 'compositionstart'); ed.field.value = 'a一'; h.composition(ed, 'compositionend', '一');   // later read queued (g1)
    ed.setDescriptor({ g: 2 });
    await h.flush();
    h.type(ed, 'g2 typed');
    ed.field.value = 'changed without an input event';
    const dropped = t.journal.report().stats.staleReadsDropped;
    await w.clock.advance(0);
    assert.equal(ed.stored().val, 'g2 typed');
    assert.equal(t.journal.report().stats.staleReadsDropped - dropped, 1);
    t.close();
});

test('C10 a first input event with no earlier focus or key event is journaled at once', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: 'base' });
    ed.root.setAttribute('field-text', 'base');
    h.input(ed, 'autofilled', false, 'insertReplacementText');
    assert.equal(ed.stored() && ed.stored().val, 'autofilled');
    t.close();
});

test('T9b page hide while composing (editor still focused) writes only the incomplete copy', async () => {
    const w = h.world();
    const t = await h.openTab(w);
    const ed = h.addEditor(t, { value: '' });
    ed.field.focus();
    h.type(ed, '本');
    h.composition(ed, 'compositionstart');
    h.input(ed, '本た', true);
    t.win.dispatchEvent(new t.win.Event('pagehide'));
    assert.equal(ed.stored().val, '本');
    assert.equal(ed.composing().val, '本た');
    t.close();
});
