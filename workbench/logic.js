/* 纯逻辑：不碰 DOM，可被 Node 直接加载做自检。 */
(function (root) {
  'use strict';

  /* 一张卡 = 一场对话。卡内分树枝：
     main 主对话 → branch 各分支（递错/递对/违禁/被捕…）→ accept 成交 → npc 插话/玩家选项 */
  function branchesOf(ev) {
    var main = [], accept = [], branches = {}, branchOrder = [], npc = [];
    function addCond(bucket, rec) {
      var c = rec.condHuman || rec.cond || '';
      if (c && bucket.conds.indexOf(c) < 0) bucket.conds.push(c);
    }
    (ev.lines || []).forEach(function (l, i) {
      var rec = Object.assign({}, l, { i: i });
      if (l.spk !== 'char') { npc.push(rec); return; }
      if (l.main) { main.push(rec); return; }
      if (l.ctx === '成交' || (l.label || '').indexOf('成交') >= 0) { accept.push(rec); return; }
      var k = l.label || l.branch || '分支';
      if (!branches[k]) { branches[k] = []; branchOrder.push(k); }
      branches[k].push(rec);
    });
    function group(kind, label, lines) {
      var g = { kind: kind, label: label, lines: lines, conds: [] };
      lines.forEach(function (r) { addCond(g, r); });
      return g;
    }
    var out = [];
    if (main.length) out.push(group('main', '主对话', main));
    branchOrder.forEach(function (k) { out.push(group('branch', k, branches[k])); });
    if (accept.length) out.push(group('accept', '成交', accept));
    if (npc.length) out.push(group('npc', '插话 / 选项（非本人）', npc));
    return out;
  }

  /* 映射结构：map[角色key] = { lines: {台词key: 立绘id}, events: {事件id: {enter, exit}} }
     台词按 key 全局唯一，同一句出现在两场对话里也只存一份。 */
  function linesOf(map, ck) { return (map && map[ck] && map[ck].lines) || {}; }
  // 场景覆盖优先于旧全局标注；显式 null 表示只在此场景回到默认图。
  function lineValue(map, ck, evId, key) {
    var node = map && map[ck] || {}, scoped = (node.contexts || {})[evId] || {};
    return Object.prototype.hasOwnProperty.call(scoped, key) ? scoped[key] : (node.lines || {})[key];
  }
  function scopedLine(map, ck, evId, key) {
    return Object.prototype.hasOwnProperty.call(((map[ck] || {}).contexts || {})[evId] || {}, key);
  }
  function samePortrait(D, a, b) {
    var aliases = D.releasePaths || {};
    return a === b || !!(aliases[a] && aliases[b] && aliases[a] === aliases[b]);
  }

  /* 入场/退场变体：只读计算，绝不写 map。用户没填就回默认值（默认值不落盘） */
  function entryList(map, ck, ev) {
    var evs = (map && map[ck] && map[ck].events) || {};
    var v = evs[ev.id] || evs[ev.sourceEvent] || {};
    if (Array.isArray(v.enters) && v.enters.length) return v.enters;
    if (v.enter) return [{ label: "默认入场", id: v.enter }];
    return [{ label: "默认入场", id: ev.defEnter || null }];
  }

  /* 退场也可以有多个变体：不同分支/不同结局，走的时候表情不一样 */
  function exitList(map, ck, ev) {
    var evs = (map && map[ck] && map[ck].events) || {};
    var v = evs[ev.id] || evs[ev.sourceEvent] || {};
    if (Array.isArray(v.exits) && v.exits.length) return v.exits;
    var out = [];
    if (v.exit) out.push({ label: "自然退场", id: v.exit });
    if (v.exitArrest) out.push({ label: "被捕带走", id: v.exitArrest });
    if (out.length) return out;
    return ev.defExits || [{ label: "自然退场", id: ev.defExit || null }];
  }

  function charStats(char, map) {
    var total = 0, mine = 0, defOnly = 0, blank = 0;
    var lines = linesOf(map, char.key);
    (char.events || []).forEach(function (ev) {
      (ev.lines || []).forEach(function (l) {
        if (l.spk !== 'char') return;
        total++;
        if (lineValue(map, char.key, ev.id, l.key)) mine++;                     // 你自己标的
        else if (l.def) defOnly++;                    // 默认图（不算你标）
        else blank++;                                 // 连默认图都没有
      });
    });
    return { total: total, mine: mine, defOnly: defOnly, blank: blank, done: mine + defOnly };
  }

  function usedCounts(char, map) {
    var used = {}, lines = linesOf(map, char.key);
    (char.events || []).forEach(function (ev) {
      (ev.lines || []).forEach(function (l) {
        if (l.spk !== 'char') return;
        var v = lineValue(map, char.key, ev.id, l.key) || l.def;
        if (v) used[v] = (used[v] || 0) + 1;
      });
    });
    (char.events || []).forEach(function (ev) {
      entryList(map, char.key, ev).forEach(function (en, i) {
        var v = (en && en.id) || (i === 0 ? ev.defEnter : null);
        if (v) used[v] = (used[v] || 0) + 1;
      });
      exitList(map, char.key, ev).forEach(function (ex) {
        if (ex && ex.id) used[ex.id] = (used[ex.id] || 0) + 1;
      });
    });
    return used;
  }

  // 导出给 Mod：每角色一份「台词key → 图」+ 每个事件一份入场/退场
  function buildExport(D, map, keys) {
    var out = { version: D.audit ? 3 : 2, build: D.build, characters: {} };
    var byKey = {};
    D.characters.forEach(function (c) { byKey[c.key] = c; });
    (keys && keys.length ? keys : D.characters.map(function (c) { return c.key; })).forEach(function (ck) {
      var c = byKey[ck];
      if (!c) return;
      var m = map[ck] || {};
      var lines = {};
      (c.events || []).forEach(function (ev) {
        (ev.lines || []).forEach(function (l) {
          if (l.spk !== 'char') return;
          var v = ((m.lines || {})[l.key]) || l.def;
          if (v) lines[l.key] = v;
        });
      });
      var evs = {};
      (c.events || []).forEach(function (ev) {
        if (ev.foreign) return;
        var enters = {};
        entryList(map, ck, ev).forEach(function (en, i) {
          var v = (en && en.id) || (i === 0 ? ev.defEnter : null);
          if (v) enters[(en && en.label) || '默认入场'] = v;
        });
        var exits = {};
        exitList(map, ck, ev).forEach(function (ex) {
          if (ex && ex.id) exits[ex.label || '退场'] = ex.id;
        });
        if (Object.keys(enters).length || Object.keys(exits).length) {
          evs[ev.id] = { enters: enters, exits: exits };
        }
      });
      if (Object.keys(lines).length || Object.keys(evs).length) {
        out.characters[ck] = { sprites: c.sprites || [], lines: lines, events: evs };
        if (m.contexts) { out.characters[ck].contexts = JSON.parse(JSON.stringify(m.contexts)); out.version = 3; }
        out.characters[ck].scenes = (c.events || []).filter(function (ev) { return !ev.foreign; }).map(function (ev) {
          var resolved = {};
          ev.lines.forEach(function (l) { if (l.spk === 'char') resolved[l.key] = lineValue(map, ck, ev.id, l.key) || l.def || null; });
          return { id: ev.id, event: ev.sourceEvent || ev.id, sprite: ev.sprite, stage: ev.stage,
            condition: ev.routeCondition || '', lines: resolved };
        });
      }
    });
    return out;
  }

  function thumb(id, size) { return 'assets/' + size + '/' + id.replace(/\//g, '__'); }

  /* 旧数据迁移：老格式是「按事件分组」，现在是「台词全局 + 每事件入场退场」。
     台词按 key 搬，入场退场按事件 id 搬；事件 id 认不出来时用台词 key 投票。 */
  function migrateMap(D, map) {
    var keyOwner = {}, keyToEv = {}, evIds = {}, evById = {};
    D.characters.forEach(function (c) {
      var ids = {}, byId = {};
      (c.events || []).forEach(function (ev) {
        ids[ev.id] = 1;
        byId[ev.id] = ev;
        (ev.lines || []).forEach(function (l) {
          keyOwner[l.key] = c.key;
          if (!keyToEv[l.key]) keyToEv[l.key] = ev.id;
        });
      });
      evIds[c.key] = ids;
      evById[c.key] = byId;
    });

    var out = {}, moved = 0, lostLines = [], lostSlots = [];
    Object.keys(map || {}).forEach(function (ck) {
      var src = map[ck] || {};
      var dst = out[ck] = { lines: {}, events: {} };
      if (src.contexts) dst.contexts = JSON.parse(JSON.stringify(src.contexts));
      if (src.lines && typeof src.lines === 'object' && !Array.isArray(src.lines)) {
        Object.keys(src.lines).forEach(function (k) {
          if (!src.lines[k]) return;
          if (keyOwner[k] && keyOwner[k] !== ck) { lostLines.push(ck + '/' + k); return; }
          dst.lines[k] = src.lines[k];
        });
        Object.keys(src.events || {}).forEach(function (e) {
          var v = src.events[e] || {};
          if (!(evIds[ck] || {})[e]) { dst.events[e] = v; return; }   // 认不出的旧事件原样留着
          var ev = evById[ck][e];
          dst.events[e] = {
            enters: entryList(map, ck, ev),
            exits: (Array.isArray(v.exits) && v.exits.length) ? v.exits : exitList(map, ck, ev)
          };
        });
        return;
      }
      // 旧格式
      Object.keys(src).forEach(function (evId) {
        if (evId === 'lines' || evId === 'events') return;
        var v = src[evId] || {};
        var evLines = v.lines || {};
        Object.keys(evLines).forEach(function (lk) {
          if (!keyOwner[lk]) { lostLines.push(ck + '/' + lk); return; }
          if (dst.lines[lk] == null) { dst.lines[lk] = evLines[lk]; moved++; }
        });
        if (!v.enter && !v.exit && !v.exitArrest) return;
        var target = (evIds[ck] || {})[evId] ? evId : null;
        if (!target) {                       // 老的分段 id（如 lun/visit）→ 用台词投票
          var votes = {};
          Object.keys(evLines).forEach(function (lk) {
            var ev = keyToEv[lk];
            if (ev) votes[ev] = (votes[ev] || 0) + 1;
          });
          target = Object.keys(votes).sort(function (a, b) { return votes[b] - votes[a]; })[0] || null;
        }
        if (!target) { lostSlots.push(ck + '/' + evId); return; }
        dst.events[target] = dst.events[target] || { enters: [], exits: [] };
        if (v.enter) {
          if (!dst.events[target].enters) dst.events[target].enters = [];
          if (!dst.events[target].enters.length) {
            dst.events[target].enters.push({ label: "默认入场", id: v.enter });
            moved++;
          }
        }
        dst.events[target].exits = dst.events[target].exits || [];
        if (v.exit && !dst.events[target].exits.some(function (e) { return e.id === v.exit; })) {
          dst.events[target].exits.push({ label: "自然退场", id: v.exit });
          moved++;
        }
        if (v.exitArrest && !dst.events[target].exits.some(function (e) { return e.id === v.exitArrest; })) {
          dst.events[target].exits.push({ label: "被捕带走", id: v.exitArrest });
          moved++;
        }
      });
    });
    return { map: out, moved: moved, lostLines: lostLines, lostSlots: lostSlots };
  }

  // 全部校验通过后才返回新快照，导入失败不允许留下半批修改。
  function prepareImport(D, current, doc) {
    var plain = function (x) { return x && typeof x === 'object' && !Array.isArray(x); };
    var backup = doc && doc.format === 'dsh-workbench-backup';
    var incoming = backup ? doc.map : doc && doc.characters;
    if (!plain(incoming) || !Object.keys(incoming).length) throw Error('不是工作台导出或手工备份；运行时 StoryMap.json 请勿作为标注导入');
    var out = JSON.parse(JSON.stringify(current)), count = 0, slots = 0;
    Object.keys(incoming).forEach(function (ck) {
      var c = D.characters.filter(function (x) { return x.key === ck; })[0];
      if (!c) throw Error('未知角色：' + ck);
      var src = incoming[ck];
      if (!plain(src)) throw Error('角色内容格式错误：' + ck);
      var ids = new Set(c.portraits.map(function (p) { return p.id; }));
      var keys = new Set(), events = new Set();
      c.events.forEach(function (e) { events.add(e.id); if(e.sourceEvent) events.add(e.sourceEvent); e.lines.forEach(function(l) { if(l.spk === 'char') keys.add(l.key); }); });
      function image(id, nullable) { if (nullable && id === null) return; if (typeof id !== 'string' || !ids.has(id)) throw Error('立绘不属于此角色或不存在：' + ck + ' / ' + id); }
      var dst = out[ck] || (out[ck] = { lines: {}, events: {} });
      dst.lines = dst.lines || {}; dst.events = dst.events || {};
      if (src.lines != null && !plain(src.lines)) throw Error('lines 必须是对象');
      Object.keys(src.lines || {}).forEach(function (k) { if (!keys.has(k)) throw Error('未知或非本人台词：' + k); image(src.lines[k], false); dst.lines[k] = src.lines[k]; count++; });
      if (src.contexts != null && !plain(src.contexts)) throw Error('contexts 必须是对象');
      Object.keys(src.contexts || {}).forEach(function (evId) {
        var ev = c.events.filter(function (e) { return e.id === evId; })[0], value = src.contexts[evId];
        if (!ev || ev.foreign || !plain(value)) throw Error('未知或不可编辑的剧情场景：' + evId);
        dst.contexts = dst.contexts || {}; dst.contexts[evId] = dst.contexts[evId] || {};
        Object.keys(value).forEach(function (k) { if (!ev.lines.some(function (l) { return l.key === k && l.spk === 'char'; })) throw Error('台词不属于此场景：' + k); image(value[k], true); dst.contexts[evId][k] = value[k]; count++; });
      });
      // 完整方案中的 scenes 保存了各路线实际呈现值；不能退回只读全局 lines。
      if (src.scenes != null) {
        if (!Array.isArray(src.scenes)) throw Error('scenes 必须是数组');
        src.scenes.forEach(function(scene) {
          var ev = c.events.filter(function(e) { return e.id === scene.id; })[0];
          if (!ev || ev.foreign || !plain(scene.lines)) throw Error('未知剧情场景：' + scene.id);
          Object.keys(scene.lines).forEach(function(k) {
            if (!ev.lines.some(function(l) { return l.key === k && l.spk === 'char'; })) throw Error('台词不属于此路线：' + k);
            image(scene.lines[k], true);
            if ((c.sharedKeys || {})[k]) {
              dst.contexts = dst.contexts || {}; dst.contexts[ev.id] = dst.contexts[ev.id] || {};
              // 显式 contexts（包括 null 回默认）优先于完整方案的展开值。
              if (!Object.prototype.hasOwnProperty.call((src.contexts || {})[ev.id] || {}, k)) dst.contexts[ev.id][k] = scene.lines[k];
            }
          });
        });
      }
      if (src.events != null && !plain(src.events)) throw Error('events 必须是对象');
      Object.keys(src.events || {}).forEach(function (evId) {
        if (!events.has(evId)) throw Error('未知事件：' + evId);
        var e = src.events[evId]; if (!plain(e)) throw Error('事件内容格式错误');
        var node = dst.events[evId] || (dst.events[evId] = {});
        ['enters','exits'].forEach(function (kind) {
          if (e[kind] == null) return;
          var list = Array.isArray(e[kind]) ? e[kind] : plain(e[kind]) ? Object.keys(e[kind]).map(function(k) { return {label:k,id:e[kind][k]}; }) : null;
          if (!list) throw Error('入退场变体格式错误');
          var labels = new Set();
          list.forEach(function (v) { if (!plain(v) || typeof v.label !== 'string' || !v.label.trim() || labels.has(v.label)) throw Error('变体名称为空或重复'); labels.add(v.label); image(v.id, true); slots++; });
          node[kind] = JSON.parse(JSON.stringify(list));
        });
        ['enter','exit','exitArrest'].forEach(function(k) { if(e[k] != null) {image(e[k], false); node[k] = e[k]; slots++;} });
      });
    });
    if (!count && !slots) throw Error('文件中没有可导入的标注');
    return {map: out, count:count, slots:slots};
  }

  root.WBLogic = {
    lineValue: lineValue, scopedLine: scopedLine, prepareImport: prepareImport, samePortrait: samePortrait,
    branchesOf: branchesOf, linesOf: linesOf, entryList: entryList, exitList: exitList,
    charStats: charStats, usedCounts: usedCounts,
    buildExport: buildExport, thumb: thumb, migrateMap: migrateMap
  };
})(typeof window !== 'undefined' ? window : globalThis);
