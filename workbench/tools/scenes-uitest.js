/* 真机交互验证：用 Chrome DevTools Protocol 在真实页面里点按钮，断言映射真的写进去了。
   node tools/uitest.js  —— 会启动无头 Chrome，结束时截图 _shot.png */
'use strict';
const { spawn } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const CHROME = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const PORT = 9334;
const PAGE_URL = 'file:///' + ROOT.replace(/\\/g, '/') + '/index.html';
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'wb-uitest-'));

const TEST = fs.readFileSync(path.join(__dirname,'scenes-test-body.js'),'utf8');

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

    await send('Runtime.evaluate', {expression: `localStorage.setItem('dsh-portrait-map-v1',JSON.stringify({lun:{lines:{dialog_lun_visit_1:window.__WB__.D.characters[0].portraits[0].id},events:{}}}));`});
    await send('Page.reload'); await sleep(450);
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

    await send('Page.reload'); await sleep(500);
    const reload = await send('Runtime.evaluate',{returnByValue:true,expression:"JSON.stringify(window.__WB__.map)===sessionStorage.getItem('wb-test-expected')"});
    if(reload.result.result.value) console.log('  ✓ 重新打开仍保留全部场景覆盖'); else {console.log('  ✗ 重新打开数据变化');code=1;}
    await send('Runtime.evaluate',{expression:`document.querySelector('.char[data-ck="eleanor"]').click();let s=document.querySelector('#stageFilter');s.value='母亲遇害 · 加入革命军';s.dispatchEvent(new Event('change',{bubbles:true}));let c=Array.from(document.querySelectorAll('.chip')).find(x=>x.dataset.lk==='dialog_eleanor9_thanks');c.dispatchEvent(new MouseEvent('mousedown',{bubbles:true,button:0}));window.dispatchEvent(new MouseEvent('mouseup'));document.querySelector('#preview').hidden=true;`});
    for (const width of [1100, 390]) {
      await send('Emulation.setDeviceMetricsOverride',{width,height:844,deviceScaleFactor:1,mobile:false});
      await sleep(80);
      const layout = await send('Runtime.evaluate',{returnByValue:true,expression:'({page:document.documentElement.scrollWidth,viewport:innerWidth,stage:document.querySelector(".stage").clientWidth})'});
      const v=layout.result.result.value;
      if(v.page<=v.viewport+1&&v.stage>200) console.log('  ✓ '+width+'px布局无整页横向溢出');
      else {console.log('  ✗ '+width+'px布局 '+JSON.stringify(v));code=1;}
    }
    await send('Emulation.clearDeviceMetricsOverride');
    const shot = await send('Page.captureScreenshot', { format: 'png' });
    if (shot.result && shot.result.data) {
      fs.writeFileSync(path.join(ROOT, '_shot-scenes.png'), Buffer.from(shot.result.data, 'base64'));
      console.log('截图: _shot-scenes.png');
    }
  } catch (e) {
    console.log('出错:', e.message);
  } finally {
    cleanup();
    process.exit(code);
  }
})();
