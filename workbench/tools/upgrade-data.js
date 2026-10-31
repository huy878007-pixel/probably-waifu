// 从已复核发布包生成独立数据层；不改原始 data.js、素材或浏览器标注。
'use strict';
const fs = require('fs'), path = require('path'), vm = require('vm');
const root = path.resolve(__dirname, '..');
const release = path.join(root, '..', '深空娘化_剧情立绘Mod', 'Mod发布');
const mapping = JSON.parse(fs.readFileSync(path.join(release, 'StoryMap.json'), 'utf8'));
const s = { window: {} }; vm.runInNewContext(fs.readFileSync(path.join(root, 'data.js'), 'utf8'), s);
const D = s.window.WB_DATA;
const paths = [];
for (const c of D.characters) {
  const dir = fs.readdirSync(path.join(release, 'Story')).find(n => n.slice(0, 2) === c.folder.slice(0, 2));
  if (!dir) throw Error('缺少角色目录 ' + c.key);
  for (const file of fs.readdirSync(path.join(release, 'Story', dir)).filter(n => n.endsWith('.png'))) {
    paths.push({ ck: c.key, rel: dir + '/' + file, label: file.replace(/\.png$/, '').replace(/^.*__/, '') });
  }
}
const loc = JSON.parse(fs.readFileSync(path.join(root, '..', '深空当铺人物美化', '提取工具与映射', '游戏本地化文本.json'), 'utf8'));
const payload = { mapping, portraits: paths, weekly: loc.filter(r => ['dialog_spacer_food_1','dialog_spacer_food_2'].includes(r.key)) };
const transform = function (P) {
  var D = window.WB_DATA;
  D.releaseMap = P.mapping;
  D.releasePaths = {};
  D.audit = { version: '2.0', sharedCopies: 0, routes: 5, releaseImages: P.portraits.length };
  var idOf = function (rel) { return rel ? 'release/' + rel : null; };
  D.characters.forEach(function (c) {
    P.portraits.filter(function (p) { return p.ck === c.key; }).forEach(function (p) {
      var id = idOf(p.rel); D.releasePaths[id] = p.rel;
      c.portraits.push({ id: id, label: p.label, sheet: '发布版 · 语义复核', kind: 'story', release: true });
    });
    // 保留旧 ID 的发布路径对应关系，已有手工标注仍能被识别。
    c.portraits.filter(function (p) { return !p.release; }).forEach(function (p) {
      var tail = p.id.split('/').pop();
      var candidates = P.portraits.filter(function (r) { return r.ck === c.key && r.rel.endsWith('__' + tail); });
      if (p.kind === 'base') {
        var wanted = null;
        if (c.key === 'eleanor') {
          if (p.label.indexOf('第一阶段') >= 0) wanted = 'eleanor1';
          else if (p.label.indexOf('第二阶段') >= 0) wanted = 'eleanor2';
          else if (p.label.indexOf('第三阶段') >= 0) wanted = 'eleanor3';
          else if (p.label.indexOf('第四阶段') >= 0) wanted = 'eleanor_scav';
          else if (p.label.indexOf('第五阶段') >= 0) wanted = 'eleanor_rev';
        } else if (c.key === 'Serpico') wanted = p.label.indexOf('拾荒者') >= 0 ? 'serpico_scav' : 'Serpico';
        else if (c.key === 'lou') wanted = p.label.indexOf('伪装') >= 0 ? 'lou_sec' : 'wanted5';
        candidates = P.portraits.filter(function(r) { return r.ck === c.key && /\/base__/.test(r.rel) && (!wanted || r.rel.endsWith('base__' + wanted + '.png')); });
      }
      if (candidates.length === 1) D.releasePaths[p.id] = candidates[0].rel;
    });
    var evs = {};
    c.events.forEach(function (e) { evs[e.id] = e; });
    if (c.key === 'john') {
      // CreateWanted8Weekly（反编译源码 120449 起）复用通用买食物台词，旧提取器漏整卡。
      var weekly = { id:'wanted8_weekly', title:'每周回访 · 购买食物', sprite:'wanted8', stage:'常规', canArrest:true, foreign:false,
        evidence:'Assembly-CSharp.decompiled.cs:120449-120476', lines:P.weekly.map(function(r) {
          return {key:r.key, zh:r.zh, en:r.en, spk:'char', secNorm:'weekly_food',main:true,label:'',alsoIn:[],def:'release/17_约翰史密斯_未做剧情精灵图/base__wanted8.png'};
        }), defEnter:'release/17_约翰史密斯_未做剧情精灵图/base__wanted8.png' };
      c.events.push(weekly); evs[weekly.id] = weekly;
    }
    // 提取器去重后留下的 alsoIn 是源码真实引用，补回原事件，保留来源标记。
    c.events.slice().forEach(function (e) { e.lines.slice().forEach(function (l) {
      (l.alsoIn || []).forEach(function (target) {
        var dst = evs[target];
        if (!dst || dst.lines.some(function (r) { return r.key === l.key; })) return;
        var copy = JSON.parse(JSON.stringify(l)); copy.alsoIn = []; copy.sharedFrom = e.id;
        dst.lines.push(copy); D.audit.sharedCopies++;
      });
    }); });
    var expanded = [];
    c.events.forEach(function (e) {
      var specs = null;
      if (e.id === 'serpicoConclusion') specs = [
        ['bm', '黑市结局 · 离站', 'serpico_scav', 'choice 不是 1 或 3', 'end_bm'],
        ['rev', '革命军结局', 'serpico_rev', 'choice == 1', 'end_rev'],
        ['sec', '治安部结局 · 重返制服', 'serpico', 'choice == 3', 'end_sec']
      ];
      if (e.id === 'eleanor9') specs = [
        ['free', '母亲获释 · 拾荒生活', 'eleanor_scav', 'eleanorDriveChoice != 2', 'free'],
        ['rev', '母亲遇害 · 加入革命军', 'eleanor_rev', 'eleanorDriveChoice == 2', 'rev']
      ];
      if (!specs) { expanded.push(e); return; }
      specs.forEach(function (spec) {
        var v = JSON.parse(JSON.stringify(e));
        v.sourceEvent = e.id; v.id = e.id + '::' + spec[0]; v.title = spec[1];
        v.stage = spec[1]; v.sprite = spec[2]; v.routeCondition = spec[3];
        if (e.id === 'serpicoConclusion') {
          v.lines = e.lines.filter(function (l) { return l.secNorm === spec[4] || /dialog_serpico_end_bm_(5|accept|done)$/.test(l.key); });
          v.lines = JSON.parse(JSON.stringify(v.lines));
          v.lines.forEach(function (l) {
            l.main = !/_(accept|done)$/.test(l.key); l.label = l.main ? '' : (/_accept$/.test(l.key) ? '领取奖励' : '告别');
            l.cond = l.condHuman = spec[3];
          });
        } else {
          var prefix = 'dialog_eleanor9_';
          var suffixes = spec[0] === 'free'
            ? ['free_1','free_2','free_3','thanks','free_gift','free_gift_2','free_bye','farewell_1','farewell_2','farewell_3']
            : ['rev_1','rev_2','rev_3','thanks','rev_gift','rev_gun','rev_bye','farewell_1','farewell_2','farewell_3'];
          v.lines = suffixes.map(function (k) { return JSON.parse(JSON.stringify(e.lines.filter(function (l) { return l.key === prefix + k; })[0])); });
          v.lines.forEach(function (l, i) {
            l.main = spec[0] === 'free' || i < 6;
            l.label = l.main ? '' : '交易后 / 尝试离开时告别'; l.cond = l.condHuman = spec[3];
          });
        }
        expanded.push(v);
      });
    });
    c.events = expanded;
    c.events.forEach(function (e) {
      var source = e.sourceEvent || e.id, base = P.mapping.events[source];
      e.lines.forEach(function (l) {
        if (l.spk !== 'char') return;
        var stageMap = P.mapping.spriteLines && P.mapping.spriteLines[e.sprite];
        l.def = idOf(stageMap && stageMap[l.key] || P.mapping.lines[l.key]) || l.def;
      });
      if (base) {
        e.defEnter = idOf(Object.values(base.enters || {})[0]) || e.defEnter;
        e.defExits = Object.keys(base.exits || {}).map(function (label) { return { label: label, id: idOf(base.exits[label]) }; });
      }
      if (e.sourceEvent) {
        // 结局卡的槽位也按本路线重建，避免沿用另一套服装。
        e.defEnter = (e.lines[0] || {}).def || e.defEnter;
        e.defExits = [{ label: '本路线自然退场', id: (e.lines[e.lines.length - 1] || {}).def || e.defEnter }];
      }
    });
    var usage = {};
    c.events.forEach(function (e) { e.lines.forEach(function (l) { if (l.spk === 'char') (usage[l.key] = usage[l.key] || []).push(e.id); }); });
    c.sharedKeys = {};
    Object.keys(usage).forEach(function (k) { if (usage[k].length > 1) c.sharedKeys[k] = usage[k]; });
  });
};
fs.writeFileSync(path.join(root, 'release-data.js'), '// 由 tools/upgrade-data.js 生成；发布版默认配图与源码路线补充层。\n(' + transform.toString() + ')(' + JSON.stringify(payload) + ');\n');
fs.writeFileSync(path.join(root, 'tools', 'release-images.json'), JSON.stringify(paths, null, 2));
console.log('发布版素材', paths.length, '已生成 release-data.js；原始 data.js 未改动');
