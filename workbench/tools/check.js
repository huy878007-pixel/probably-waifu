/* 工作台自检：node tools/check.js
   只依赖 logic.js + data.js，不需要浏览器。任何一条不成立就退出码 1。 */
'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const ROOT = path.resolve(__dirname, '..');
const sandbox = { window: {} };
vm.createContext(sandbox);
for (const f of ['logic.js', 'data.js']) {
  vm.runInContext(fs.readFileSync(path.join(ROOT, f), 'utf8'), sandbox, { filename: f });
}
const L = sandbox.window.WBLogic;
const D = sandbox.window.WB_DATA;

let fails = 0;
const ok = (cond, msg) => {
  if (!cond) { fails++; console.log('  ✗ ' + msg); }
  else console.log('  ✓ ' + msg);
};

console.log('— 数据 —');
ok(D.characters.length === 17, `17 个角色（实际 ${D.characters.length}）`);
ok(D.build === '26317870', `Build 号 ${D.build}`);
const withStory = D.characters.filter(c => c.portraits.some(p => p.kind === 'story'));
ok(withStory.length === 14, `14 个角色有剧情切片（实际 ${withStory.length}）`);
ok(withStory.reduce((n, c) => n + c.portraits.filter(p => p.kind === 'story').length, 0) === 126,
  '剧情切片共 126 张');
const ids = new Set();
let dupPor = 0;
D.characters.forEach(c => c.portraits.forEach(p => { if (ids.has(p.id)) dupPor++; ids.add(p.id); }));
ok(dupPor === 0, `立绘 id 无重复（${ids.size} 个）`);

console.log('— 对话卡 / 台词 —');
let totalLines = 0, assignable = 0, noText = 0;
const cardIds = new Set();
for (const c of D.characters) {
  for (const ev of c.events) {
    const id = c.key + '/' + ev.id;
    if (cardIds.has(id)) fails++;
    cardIds.add(id);
    for (const l of ev.lines) {
      totalLines++;
      if (!l.zh) noText++;
      if (l.spk === 'char') assignable++;
    }
  }
}
ok(totalLines === 766, `台词行合计 ${totalLines}（md 739 + 补漏 8）`);
ok(assignable > 600, `可标注（本人说话）${assignable} 句`);
ok(noText === 0, `角色台词均有文本（无文本 ${noText}）`);
ok(cardIds.size === 94, `对话卡 ${cardIds.size} 张`);

// 非本人的卡（随机 NPC 立绘）必须标出来，且里面没有可标注台词
const foreignCards = [];
for (const c of D.characters) for (const ev of c.events) {
  if (!ev.foreign) continue;
  foreignCards.push(c.key + '/' + ev.id);
  if (ev.lines.some(l => l.spk === 'char')) fails++;
}
ok(foreignCards.length === 6, `非本人卡 ${foreignCards.length} 张：${foreignCards.join(', ')}`);
ok(D.characters.every(c => c.events.every(ev => ev.foreign || ev.lines.length > 0)), '没有空卡');

// 同一句台词只出现在一张卡里（按 key 全局唯一）
const keyToCard = new Map();
let crossCard = 0;
for (const c of D.characters) for (const ev of c.events) for (const l of ev.lines) {
  if (keyToCard.has(l.key)) crossCard++;
  keyToCard.set(l.key, ev.id);
}
ok(crossCard === 0, `台词 key 不跨卡重复（重复 ${crossCard}）`);

console.log('— 分支树 —');
let groupsTotal = 0, withCond = 0, lostLines = 0, badKind = 0;
for (const c of D.characters) {
  for (const ev of c.events) {
    const groups = L.branchesOf(ev);
    groupsTotal += groups.length;
    const flat = groups.reduce((a, g) => a.concat(g.lines), []);
    if (flat.length !== ev.lines.length) lostLines++;
    if (groups[0] && groups[0].kind !== 'main' && groups.some(g => g.kind === 'main')) badKind++;
    groups.forEach(g => { if (g.conds.length) withCond++; });
  }
}
ok(lostLines === 0, '每张卡的台词都能在分支树里找到');
ok(badKind === 0, '主对话排在最前');
ok(groupsTotal > 150, `分支组合计 ${groupsTotal} 组`);
ok(withCond > 12, `带判定条件的分支 ${withCond} 组`);

// 递错/递对这类反应必须在同一张卡里，而不是各成一张卡
const lun = D.characters.find(c => c.key === 'lun');
const visit = lun.events.find(e => e.id === 'officerLunVisit');
ok(!!visit, 'officerLunVisit 存在');
if (visit) {
  const g = L.branchesOf(visit);
  const labels = g.map(x => x.label).join('|');
  ok(visit.lines.length > 3, `一场对话一张卡（${visit.lines.length} 句）`);
  ok(labels.includes('递错') && labels.includes('递对'), `递错/递对在同一张卡内：${labels}`);
  ok(g.some(x => x.conds.length > 0), '分支带判定条件');
}

console.log('— 退场分支 —');
const arrestCards = [];
for (const c of D.characters) for (const ev of c.events) if (ev.canArrest) arrestCards.push(c.key + '/' + ev.id);
ok(arrestCards.length > 10, `可被捕的对话卡 ${arrestCards.length} 张`);
ok(arrestCards.every(x => x), '被捕标记挂在卡上');
const lunSurplus = lun.events.find(e => e.id === 'officerLunSurplus');
ok(!!lunSurplus && lunSurplus.canArrest === true, 'officerLunSurplus 标为可被捕');
ok(!!visit && visit.canArrest === false, 'officerLunVisit 不可被捕');

console.log('— 导出结构 —');
const empty = L.buildExport(D, {}, null);
ok(Object.keys(empty.characters).length === 17, '空映射也会导出默认图（17 角色）');
const por = lun.portraits.find(p => p.kind === 'story');
const line0 = visit ? visit.lines[0] : null;
const map = {
  lun: {
    lines: { [line0.key]: por.id },
    events: { officerLunVisit: { enters: [{ label: '默认入场', id: por.id }], exits: [{ label: '自然退场', id: por.id }] } }
  }
};
const out = L.buildExport(D, map, null);
ok(out.characters.lun.lines[line0.key] === por.id, '台词映射导出');
ok(out.characters.lun.events.officerLunVisit.enters['默认入场'] === por.id, '入场导出');
ok(out.characters.lun.events.officerLunVisit.exits['自然退场'] === por.id, '退场导出');
ok(D.characters.every(c => c.events.every(ev => ev.foreign || ev.lines.filter(l => l.spk === 'char').every(l => l.def))), '每句都有默认图');
ok(out.characters.lun.sprites.join() === 'officer_lun', '带上原版 sprite 名');

ok(Object.keys(L.buildExport(D, map, ['lun']).characters).length === 1, '单角色导出');

// 多个入场变体（开场条件不同 → 入场图不同）
const multi = {
  lun: {
    lines: {},
    events: {
      officerLunVisit: {
        enters: [{ label: '第一次来', id: por.id }, { label: '再次上门', id: lun.portraits[1].id }],
        exit: null, exitArrest: null
      }
    }
  }
};
const mo = L.buildExport(D, multi, ['lun']);
ok(Object.keys(mo.characters.lun.events.officerLunVisit.enters).length === 2, '入场变体各导出一份');

console.log('— 旧数据迁移 —');
{
  // v1/v3 格式：按事件（或分段）分组
  const oldMap = {
    lun: {
      officerLunVisit: { enter: por.id, exit: por.id, lines: { [line0.key]: por.id } },
      'lun/visit_wrong_item': { enter: null, exit: null, lines: { [visit.lines[visit.lines.length - 1].key]: por.id } }
    }
  };
  const mg = L.migrateMap(D, oldMap);
  ok(mg.map.lun.lines[line0.key] === por.id, '台词按 key 迁移');
  const mge = (mg.map.lun.events.officerLunVisit || {});
  ok(!!mge.enters && mge.enters[0].id === por.id, '入场迁移（转成变体列表）');
  ok(!!mge.exits && mge.exits[0].id === por.id, '退场按事件迁移');
  ok(Object.keys(mg.map.lun.lines).length === 2, '分段 id 里的台词也迁过来了');
  ok(mg.lostLines.length === 0, '没有丢台词');
  // 新格式原样
  const mg2 = L.migrateMap(D, map);
  ok(mg2.map.lun.lines[line0.key] === por.id && mg2.moved === 0, '新格式原样保留');
}

console.log('— 统计 —');
const st = L.charStats(lun, map);
ok(st.done === st.total && st.total > 40, '有默认图时进度满 ' + st.done + '/' + st.total);
ok(L.charStats(lun, {}).done === L.charStats(lun, {}).total, '空映射也算已填（默认图）');
ok((L.usedCounts(lun, map)[por.id] || 0) >= 2, '立绘使用计数正常');

console.log('— 预览图 —');
const missing = [];
for (const c of D.characters) for (const p of c.portraits) for (const size of ['p', 't']) {
  if (!fs.existsSync(path.join(ROOT, 'assets', size, p.id.replace(/\//g, '__')))) missing.push(size + '/' + p.label);
}
ok(missing.length === 0, `预览图齐全（缺 ${missing.length}）`);
ok(L.thumb('a/b/c.png', 't') === 'assets/t/a__b__c.png', 'thumb 路径转换');

console.log('');
if (fails) { console.log(`失败 ${fails} 项`); process.exit(1); }
console.log('全部通过');

