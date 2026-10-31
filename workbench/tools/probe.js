/* 用真实输入事件复现拖拽：node tools/probe.js */
'use strict';
const { spawn } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const CHROME = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const PORT = 9334;
const PAGE_URL = 'file:///' + ROOT.replace(/\\/g, '/') + '/index.html';
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'wb-probe-'));

const chrome = spawn(CHROME, ['--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run',
  '--window-size=1600,1000', '--remote-debugging-port=' + PORT, '--user-data-dir=' + profile, PAGE_URL],
  { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function findTarget() {
  for (let i = 0; i < 80; i++) {
    try {
      const l = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
      const t = l.find(x => x.type === 'page' && x.url.includes('index.html'));
      if (t) return t;
    } catch (e) {}
    await sleep(250);
  }
  throw new Error('no devtools');
}

function rpc(ws) {
  let seq = 0; const pending = new Map();
  ws.addEventListener('message', ev => {
    const m = JSON.parse(typeof ev.data === 'string' ? ev.data : Buffer.from(ev.data).toString('utf8'));
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
    if (m.method === 'Runtime.exceptionThrown') {
      console.log('!! 页面异常:', JSON.stringify(m.params.exceptionDetails.text || m.params.exceptionDetails));
    }
  });
  return (method, params) => new Promise(res => {
    const id = ++seq; pending.set(id, res);
    ws.send(JSON.stringify({ id, method, params: params || {} }));
  });
}

(async () => {
  try {
    const t = await findTarget();
    const ws = new WebSocket(t.webSocketDebuggerUrl);
    await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
    const send = rpc(ws);
    await send('Runtime.enable');

    const ev = async expr => {
      const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
      if (r.result && r.result.exceptionDetails) return { err: r.result.exceptionDetails.text };
      return r.result.result.value;
    };

    // 三个格子的中心坐标
    const rects = await ev(`(() => {
      const cs = Array.from(document.querySelectorAll('.chip:not(.ghost)')).slice(0, 4);
      return cs.map(c => { const r = c.getBoundingClientRect();
        return { x: Math.round(r.x + r.width/2), y: Math.round(r.y + r.height/2), g: c.dataset.g, i: c.dataset.i }; });
    })()`);
    console.log('格子坐标:', JSON.stringify(rects));
    if (!rects || !rects.length) { console.log('没有格子'); return; }

    const click = async (type, pt, buttons) => send('Input.dispatchMouseEvent',
      { type, x: pt.x, y: pt.y, button: 'left', buttons, clickCount: 1 });

    await click('mousePressed', rects[0], 1);
    await sleep(60);
    console.log('按下后 .sel 数:', await ev(`document.querySelectorAll('.chip.sel').length`));

    // 逐个移动到后面的格子
    for (let k = 1; k < rects.length; k++) {
      await click('mouseMoved', rects[k], 1);
      await sleep(60);
      console.log(`移到第 ${k} 格后 .sel 数:`, await ev(`document.querySelectorAll('.chip.sel').length`));
      console.log('   drag 状态:', await ev(`window.__WB__ && (()=>{try{return JSON.stringify({d:!!window.__DBG_DRAG})}catch(e){return 'n/a'}})()`));
    }
    await click('mouseReleased', rects[rects.length - 1], 0);
    await sleep(60);
    console.log('松开后 .sel 数:', await ev(`document.querySelectorAll('.chip.sel').length`));
  } catch (e) {
    console.log('出错:', e.message);
  } finally {
    chrome.kill();
    process.exit(0);
  }
})();
