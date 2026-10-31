/* 真机交互验证：用 Chrome DevTools Protocol 在真实页面里点按钮，断言映射真的写进去了。
   node tools/uitest.js  —— 会启动无头 Chrome，结束时截图 _shot.png */
'use strict';
const { spawn } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const CHROME = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const PORT = 9333;
const PAGE_URL = 'file:///' + ROOT.replace(/\\/g, '/') + '/index.html';
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'wb-uitest-'));

const TEST = `(async () => {
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  const WB = window.__WB__;
  const steps = [];
  const check = (name, cond, extra) => steps.push({ name, ok: !!cond, extra });
  if (!WB) return { fatal: 'no __WB__' };

  localStorage.removeItem('dsh-portrait-map-v1');
  WB.map = {};
  await sleep(80);

  // 0. 分支树
  const evEl = document.querySelector('.event');
  const gEls = evEl.querySelectorAll('.tgroup');
  check('分支树渲染', gEls.length >= 2, gEls.length);
  check('主对话在最前', gEls[0].classList.contains('main'), gEls[0].className);
  const condEl = evEl.querySelector('.cond');
  check('分支显示判定条件', !!condEl, condEl && condEl.textContent.trim());
  check('分支有颜色区分', !!evEl.querySelector('.tgroup.branch'));

  // 0b. 可被捕的卡要有两个退场槽
  const allEv = Array.from(document.querySelectorAll('.event'));
  const multiExit = allEv.find(e => e.querySelectorAll('.slot[data-slot="exit"]').length > 1);
  check('退场按分支分成多套', !!multiExit, allEv.length);
  check('退场变体有标签', !!document.querySelector('.slot[data-slot="exit"] + .elabel') ||
        !!document.querySelector('[data-slot="exit"]'));
  let noDef = 0;
  for (const c of WB.D.characters) for (const ev of c.events) for (const l of ev.lines) {
    if (l.spk === 'char' && !l.def) noDef++;
  }
  check('每句都有默认图', noDef === 0, noDef);

  // 1. 选一句 → 点立绘
  const chip = document.querySelector('.chip:not(.ghost)');
  const ev = chip.dataset.ev, lk = chip.dataset.lk;
  chip.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
  window.dispatchEvent(new MouseEvent('mouseup'));
  await sleep(40);
  const por = document.querySelector('.por');
  const pid = por.dataset.id;
  por.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  await sleep(40);
  const ck = Object.keys(WB.map)[0];
  check('单句框选', document.querySelectorAll('.chip.sel').length === 1);
  check('点立绘写入映射', (WB.map[ck].lines || {})[lk] === pid, (WB.map[ck].lines || {})[lk]);
  check('格子变成已标注', !!document.querySelector('.chip.assigned'));

  // 2. 拖拽同组多选（重新查询，前面上色已重绘过 DOM）
  const chips = Array.from(document.querySelectorAll('.chip:not(.ghost)')).filter(c => c.dataset.ev === ev);
  const g0 = chips[0].dataset.g;
  const same = chips.filter(c => c.dataset.g === g0);
  same[0].dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
  for (const c of same.slice(1, 3)) c.dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
  window.dispatchEvent(new MouseEvent('mouseup'));
  await sleep(40);
  check('拖拽可多选', document.querySelectorAll('.chip.sel').length >= 2,
        document.querySelectorAll('.chip.sel').length);

  // 3. 入场槽
  const slot = document.querySelector('.slot[data-slot="enter"]');
  const evSlot = slot.dataset.ev;
  slot.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
  await sleep(30);
  const por2 = document.querySelectorAll('.por')[1];
  const pid2 = por2.dataset.id;
  por2.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  await sleep(40);
  check('入场槽写入', ((WB.map[ck].events || {})[evSlot] || {}).enters?.[0]?.id === pid2,
        JSON.stringify(((WB.map[ck].events || {})[evSlot] || {}).enters));
  check('入场槽显示图', !!document.querySelector('.slot[data-slot="enter"] img'));

  // 3b. 加一个入场变体（开场条件不同 → 入场图不同）
  window.prompt = () => '第二次来';
  document.querySelector('[data-add-variant="enter"]').dispatchEvent(new MouseEvent('click', { bubbles: true }));
  await sleep(80);
  const evCard = Array.from(document.querySelectorAll('.event'))
    .find(el => el.querySelector('.slot[data-ev="' + evSlot + '"]'));
  const eSlots = evCard.querySelectorAll('.slot[data-slot="enter"]');
  check('可加入场变体', eSlots.length === 2, eSlots.length);
  eSlots[1].dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
  await sleep(40);
  const porV = document.querySelectorAll('.por')[2];
  const pidV = porV.dataset.id;
  porV.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  await sleep(60);
  const ents = (((WB.map[ck].events || {})[evSlot] || {}).enters) || [];
  check('两个入场变体互不影响',
        ents.length === 2 && ents[0].id === pid2 && ents[1].id === pidV,
        JSON.stringify(ents));
  check('变体标签可导出',
        Object.keys((WB.logic.buildExport(WB.D, WB.map, [ck]).characters[ck].events[evSlot] || {}).enters || {}).length === 2);

  // 4. Delete 清除
  const assignedChip = document.querySelector('.chip.assigned');
  const delKey = assignedChip.dataset.lk;
  assignedChip.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
  window.dispatchEvent(new MouseEvent('mouseup'));
  await sleep(30);
  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Delete', bubbles: true }));
  await sleep(40);
  check('Delete 清除标注', !(WB.map[ck].lines || {})[delKey], (WB.map[ck].lines || {})[delKey]);

  // 5. 导出
  const out = WB.logic.buildExport(WB.D, WB.map, [ck]);
  check('导出含入场', !!out.characters[ck].events[evSlot], out.characters[ck].events[evSlot]);

  // 6. 换角色
  const other = Array.from(document.querySelectorAll('.char')).find(c => c.dataset.ck !== ck);
  other.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  await sleep(80);
  check('切换角色', document.querySelector('.char.active').dataset.ck === other.dataset.ck);
  check('托盘非空', document.querySelectorAll('.por').length > 0);

  // 7. 拖放立绘到格子
  const ckNow = document.querySelector('.char.active').dataset.ck;
  const dt = new DataTransfer();
  const dragSrc = document.querySelector('.por');
  const dragTarget = document.querySelector('.chip:not(.ghost)');
  const dragLk = dragTarget.dataset.lk;
  dragSrc.dispatchEvent(new DragEvent('dragstart', { bubbles: true, dataTransfer: dt }));
  dragTarget.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: dt }));
  const highlighted = dragTarget.classList.contains('drop');
  dragTarget.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt }));
  document.dispatchEvent(new DragEvent('dragend', { bubbles: true, dataTransfer: dt }));
  await sleep(40);
  check('拖拽时目标格高亮', highlighted);
  check('拖放写入映射', (WB.map[ckNow].lines || {})[dragLk] === dragSrc.dataset.id,
        (WB.map[ckNow].lines || {})[dragLk]);

  // 8. 布局
  check('格子宽度不被压缩', Math.round(document.querySelector('.chip').getBoundingClientRect().width) >= 100);
  const imgStyle = getComputedStyle(document.querySelector('.por img'));
  check('图片禁止原生拖拽', imgStyle.webkitUserDrag === 'none' || imgStyle.getPropertyValue('-webkit-user-drag') === 'none');

  // 9. 双击 = 整场套用
  const firstEv = document.querySelector('.event');
  const evId2 = firstEv.querySelector('.slot').dataset.ev;
  const charObj = WB.D.characters.find(c => c.key === ckNow);
  const evObj = charObj.events.find(e => e.id === evId2);
  const porId2 = document.querySelectorAll('.por')[2].dataset.id;
  firstEv.querySelector('.chip:not(.ghost)').dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
  window.dispatchEvent(new MouseEvent('mouseup'));
  await sleep(40);
  document.querySelectorAll('.por')[2].dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true }));
  await sleep(80);
  check('双击=整场套用',
        evObj.lines.filter(l => l.spk === 'char').every(l => (WB.map[ckNow].lines || {})[l.key] === porId2));

  // 10. 右键 = 整角色
  const porId3 = document.querySelectorAll('.por')[3].dataset.id;
  document.querySelectorAll('.por')[3].dispatchEvent(
    new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 200, clientY: 200 }));
  await sleep(40);
  check('右键弹出批量菜单', !document.getElementById('ctx').hidden);
  document.querySelector('#ctx button[data-act="char"]').dispatchEvent(new MouseEvent('click', { bubbles: true }));
  await sleep(100);
  check('右键=整角色套用',
        charObj.events.every(e => e.lines.filter(l => l.spk === 'char')
          .every(l => (WB.map[ckNow].lines || {})[l.key] === porId3)));

  // 11. 撤销
  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, bubbles: true }));
  await sleep(80);
  check('Ctrl+Z 撤销批量',
        !charObj.events.every(e => e.lines.filter(l => l.spk === 'char')
          .every(l => (WB.map[ckNow].lines || {})[l.key] === porId3)));

  // 13. 布置演示状态（不写 localStorage）
  const c0 = WB.D.characters.find(c => c.events.length);
  const story = c0.portraits.filter(p => p.kind === 'story');
  const base = c0.portraits.find(p => p.kind === 'base');
  const lines = {};
  let n = 0;
  c0.events.forEach(ev => ev.lines.forEach(l => {
    if (l.spk === 'char') lines[l.key] = story[Math.floor(n++ / 2) % story.length].id;
  }));
  const events = {};
  c0.events.slice(0, 2).forEach(ev => { events[ev.id] = { enter: story[0].id, exit: base ? base.id : null }; });
  WB.map = {};
  WB.map = { [c0.key]: { lines: lines, events: events } };
  document.querySelector('.char').dispatchEvent(new MouseEvent('click', { bubbles: true }));
  await sleep(120);

  return { steps };
})()`;

let ws;
const cleanup = () => {
  try { ws && ws.close(); } catch (e) {}
  chrome.kill();
};

const chrome = spawn(CHROME, [
  '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run',
  '--no-default-browser-check', '--window-size=1600,1000',
  '--remote-debugging-port=' + PORT, '--user-data-dir=' + profile, PAGE_URL
], { stdio: 'ignore' });

const sleep = ms => new Promise(r => setTimeout(r, ms));

async function findTarget() {
  for (let i = 0; i < 80; i++) {
    try {
      const list = await (await fetch('http://127.0.0.1:' + PORT + '/json/list')).json();
      const t = list.find(x => x.type === 'page' && x.url.indexOf('index.html') >= 0);
      if (t) return t;
    } catch (e) { }
    await sleep(250);
  }
  throw new Error('DevTools 没起来');
}

function rpc(socket) {
  let seq = 0;
  const pending = new Map();
  socket.addEventListener('message', ev => {
    const m = JSON.parse(typeof ev.data === 'string' ? ev.data : Buffer.from(ev.data).toString('utf8'));
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
  });
  return (method, params) => new Promise(res => {
    const id = ++seq;
    pending.set(id, res);
    socket.send(JSON.stringify({ id, method, params: params || {} }));
  });
}

(async () => {
  let code = 1;
  try {
    const target = await findTarget();
    ws = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((res, rej) => {
      ws.addEventListener('open', res);
      ws.addEventListener('error', rej);
    });
    const send = rpc(ws);
    await send('Runtime.enable');
    await send('Page.enable');

    const r = await send('Runtime.evaluate', { expression: TEST, awaitPromise: true, returnByValue: true });
    const val = r.result && r.result.result && r.result.result.value;
    if (!val) { console.log('页面没有返回结果:', JSON.stringify(r.result && r.result.exceptionDetails).slice(0,600)); }
    else if (!val.steps) { console.log('返回异常:', JSON.stringify(val).slice(0,300), JSON.stringify(r.result.exceptionDetails||'').slice(0,400)); }
    else if (val.fatal) { console.log('致命:', val.fatal); }
    else {
      let bad = 0;
      val.steps.forEach(s => {
        console.log((s.ok ? '  ✓ ' : '  ✗ ') + s.name + (s.ok ? '' : '  实际=' + JSON.stringify(s.extra)));
        if (!s.ok) bad++;
      });
      code = bad ? 1 : 0;
      console.log(bad ? `失败 ${bad} 项` : '交互全部通过');
    }

    const shot = await send('Page.captureScreenshot', { format: 'png' });
    if (shot.result && shot.result.data) {
      fs.writeFileSync(path.join(ROOT, '_shot.png'), Buffer.from(shot.result.data, 'base64'));
      console.log('截图: _shot.png');
    }
  } catch (e) {
    console.log('出错:', e.message);
  } finally {
    cleanup();
    process.exit(code);
  }
})();
