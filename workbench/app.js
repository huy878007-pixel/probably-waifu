/* 深空娘化 · 立绘工作台（界面层）
   纯逻辑在 logic.js，数据在 data.js。
   映射：map[角色] = { lines: {台词key: 立绘id}, events: {事件id: {enter, exit}} } */
(function () {
  'use strict';

  var D = window.WB_DATA || { characters: [] };
  var L = window.WBLogic;
  var STORE = 'dsh-portrait-map-v1';
  var thumb = L.thumb;

  var byKey = {};
  D.characters.forEach(function (c) { byKey[c.key] = c; });

  var map = {};
  var cur = null;
  var sel = null;        // { ev, lks: [] } 或 { ev, slot }
  var anchor = null;
  var drag = null;       // { ev, g, i0, i1 }
  var onlyUnas = false;
  var onlyDef = false;
  var trayFilter = '';
  var stageFilter = '';
  var focusEv = null;
  var undoStack = [], redoStack = [];
  var query = '';
  var reviewOnly = false;
  var storageBlocked = false, dirty = false;

  function stageHue(s) {
    var h = 0;
    for (var i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) % 360;
    return h;
  }
  function stageLabel(ev) {
    if (ev.foreign) return '非本人';
    return ev.stage || '无固定立绘';
  }

  function pushUndo() {
    redoStack = [];
    undoStack.push(JSON.stringify(map));
    if (undoStack.length > 80) undoStack.shift();
  }

  var $ = function (id) { return document.getElementById(id); };
  var esc = function (s) {
    return String(s == null ? '' : s).replace(/[&<>"]/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c];
    });
  };

  /* ---------- 存取 ---------- */
  var rawBackup = null;
  function load() {
    var raw = null;
    try { raw = localStorage.getItem(STORE); } catch (e) { map = {}; storageBlocked = true; return; }
    try { map = JSON.parse(raw || '{}') || {}; } catch (e) { map = {}; storageBlocked = true; return; }
    if (!raw) return;
    if (!map || typeof map !== 'object' || Array.isArray(map)) { map = {}; storageBlocked = true; return; }
    rawBackup = raw;
    // 只在内存里做兼容迁移，绝不写回存储：你的原数据不会被程序改写
    try {
      var r = L.migrateMap(D, map);
      if (r.moved) {
        map = r.map;
        setTimeout(function () { flash('按新结构兼容显示（' + r.moved + ' 处）。你的存储文件没被改写。'); }, 400);
      }
    } catch (e) { /* 迁移失败就按原样用 */ }
  }
  var saveTimer = null;
  function saveState(text, error) { $('saveStatus').textContent = text; $('saveStatus').classList.toggle('error', !!error); }
  function flushSave() {
    clearTimeout(saveTimer);
    if (!dirty) return true;
    if (storageBlocked) { saveState('原存储异常 · 请先导出备份', true); return false; }
    try {
      // 首次改动前自动保留本次打开时的原始数据；不会在读取时写入。
      if (rawBackup) { localStorage.setItem(STORE + '-backup', rawBackup); rawBackup = null; }
      localStorage.setItem(STORE, JSON.stringify(map)); dirty = false;
      saveState('已保存 ' + new Date().toLocaleTimeString('zh-CN', {hour12:false}), false); return true;
    } catch (e) { saveState('保存失败 · 请导出备份', true); return false; }
  }
  function save() { dirty = true; saveState('正在保存…', false); clearTimeout(saveTimer); saveTimer = setTimeout(flushSave, 200); }
  window.addEventListener('pagehide', flushSave);
  document.addEventListener('visibilitychange', function() { if(document.hidden) flushSave(); });
  window.addEventListener('beforeunload', function(e) { if(dirty && !flushSave()) { e.preventDefault(); e.returnValue = ''; } });

  function charNode(ck) { var n = map[ck] || (map[ck] = {}); n.lines = n.lines || {}; n.events = n.events || {}; return n; }
  function getLine(lk, evId) { return L.lineValue(map, cur, evId || focusEventId(), lk) || null; }
  function setLine(lk, id, evId) {
    var node = charNode(cur);
    if ((byKey[cur].sharedKeys || {})[lk]) {
      evId = evId || focusEventId();
      if (!evId) throw Error('复用台词缺少场景');
      node.contexts = node.contexts || {}; node.contexts[evId] = node.contexts[evId] || {};
      node.contexts[evId][lk] = id;
    } else if (id) node.lines[lk] = id; else delete node.lines[lk];
    if (node.auto) delete node.auto[lk];
  }

  /* 渲染只看不写；只有在用户真正赋值时才把变体写进存储 */
  function ensureEntries(evId) {
    var node = charNode(cur);
    node.events[evId] = node.events[evId] || {};
    var v = node.events[evId];
    if (!Array.isArray(v.enters) || !v.enters.length) {
      var ev = curEvent(evId);
      v.enters = JSON.parse(JSON.stringify(L.entryList(map, cur, ev)));
      delete v.enter;
    }
    return v.enters;
  }
  function setEntry(evId, idx, id) {
    var list = ensureEntries(evId);
    if (list[idx]) list[idx].id = id;
  }
  function ensureExits(evId) {
    var node = charNode(cur);
    node.events[evId] = node.events[evId] || {};
    var v = node.events[evId];
    if (!Array.isArray(v.exits) || !v.exits.length) {
      var ev = curEvent(evId);
      v.exits = JSON.parse(JSON.stringify(L.exitList(map, cur, ev)));
      delete v.exit; delete v.exitArrest;
    }
    return v.exits;
  }
  function setExit(evId, idx, id) {
    var list = ensureExits(evId);
    if (list[idx]) list[idx].id = id;
  }

  function curEvent(id) {
    var c = byKey[cur];
    if (!c) return null;
    return c.events.filter(function (e) { return e.id === id; })[0] || null;
  }
  function assignableOf(ev) {
    return (ev.lines || []).filter(function (l) { return l.spk === 'char'; });
  }

  /* ---------- 渲染 ---------- */
  function renderProgress() {
    var t = 0, d = 0, m = 0;
    D.characters.forEach(function (c) {
      var s = L.charStats(c, map); t += s.total; d += s.done; m += s.mine;
    });
    $('progress').textContent = '共 ' + t + ' 个场景台词 · 手工 ' + m + ' · 默认 ' + (d-m) + ' · 缺图 ' + (t-d);
  }

  function renderChars() {
    var box = $('charList');
    box.innerHTML = '';
    D.characters.forEach(function (c) {
      var s = L.charStats(c, map);
      var el = document.createElement('button');
      el.type = 'button'; el.setAttribute('aria-pressed', String(c.key === cur));
      el.className = 'char' + (c.key === cur ? ' active' : '') + (s.total ? '' : ' empty');
      el.dataset.ck = c.key;
      el.innerHTML = '<span>' + esc(c.name) + '</span><span class="cnt">' +
        (s.total ? s.mine + '/' + s.total : '—') + '</span>';
      box.appendChild(el);
    });
  }

  function phText(rec) {
    if (rec.spk === 'char') return '未标注';
    if (rec.spk === 'player') return '玩家';
    return (rec.key || '').indexOf('sec') >= 0 ? '治安人员' : '他人';
  }

  function chipHTML(ev, gname, rec) {
    var mine = getLine(rec.key, ev.id);
    var assigned = mine || rec.def || null;
    var cls = ['chip'];
    if (rec.spk !== 'char') cls.push('ghost', rec.spk);
    if (assigned) cls.push('assigned');
    if (!mine && rec.def) cls.push('auto');
    if (L.scopedLine(map, cur, ev.id, rec.key)) cls.push('scoped');
    if (!rec.zh) cls.push('noted');
    var on = false;
    if (drag && drag.ev === ev.id && drag.g === gname) {
      var a = Math.min(drag.i0, drag.i1), b = Math.max(drag.i0, drag.i1);
      on = rec.i >= a && rec.i <= b;
    } else if (sel && !sel.slot && sel.ev === ev.id && sel.lks.indexOf(rec.key) >= 0) {
      on = true;
    }
    if (on) cls.push('sel');

    var body = assigned
      ? '<img loading="lazy" src="' + thumb(assigned, 't') + '" alt="">'
      : '<div class="ph">' + phText(rec) + '</div>';

    return '<button class="' + cls.join(' ') + '" data-ev="' + esc(ev.id) + '" data-g="' + gname +
      '" data-i="' + rec.i + '" data-lk="' + esc(rec.key) + '" title="' + esc(rec.zh || rec.key) + '">' +
      '<span class="no">' + (rec.i + 1) + '</span>' + ((byKey[cur].sharedKeys || {})[rec.key] ? '<span class="shared-mark" title="复用台词 · 按场景独立标注">复用</span>' : '') + body +
      '<span class="txt">' + esc(rec.zh || '（取不到文本）') + '</span></button>';
  }

  function slotHTML(ev, which, label) {
    var id = null;
    var isSel = sel && sel.slot === which && sel.ev === ev.id;
    return '';
  }

  function variantSlotsHTML(ev, kind) {
    var list = kind === 'enter' ? L.entryList(map, cur, ev) : L.exitList(map, cur, ev);
    var out = list.map(function (v, i) {
      var isSel = sel && sel.slot === kind && sel.ev === ev.id && sel.idx === i;
      return '<div class="eslot">' +
        '<div class="slot' + (v.id ? ' filled' : '') + (isSel ? ' sel' : '') + (v.id && !v.own ? ' auto' : '') +
        '" data-ev="' + esc(ev.id) + '" data-slot="' + kind + '" data-idx="' + i + '">' +
        (v.id ? '<img src="' + thumb(v.id, 't') + '" alt=""><span class="x" data-clear="1" title="清除">×</span>'
          : '入场') +
        (list.length > 1 ? '<span class="del" data-del-variant="' + kind + '" title="删掉这个变体">−</span>' : '') +
        '</div>' +
        '<div class="elabel" data-rename="1" data-kind="' + kind + '" data-ev="' + esc(ev.id) + '" data-idx="' + i +
        '" title="点击改名（写成条件）">' + esc(v.label || (kind === 'enter' ? '入场' : '退场')) + '</div>' +
        '</div>';
    }).join('');
    return out + '<button class="addentry" data-add-variant="' + kind + '" data-ev="' + esc(ev.id) +
      '" title="再加一个">＋</button>';
  }

  function entrySlotsHTML(ev) { return variantSlotsHTML(ev, 'enter'); }
  function exitSlotsHTML(ev) { return variantSlotsHTML(ev, 'exit'); }

  function renderStageFilter() {
    var box = $('stageFilter');
    var c = byKey[cur];
    if (!c) { box.innerHTML = ''; return; }
    var stages = [];
    c.events.forEach(function (ev) {
      var s = ev.stage || '电话 / 无固定立绘';
      if (stages.indexOf(s) < 0) stages.push(s);
    });
    box.innerHTML = '<option value="">全部阶段</option>' + stages.map(function (s) {
      return '<option value="' + esc(s) + '">' + esc(s) + '</option>';
    }).join('');
    if (stages.indexOf(stageFilter) < 0) stageFilter = '';
    box.value = stageFilter;
  }

  function renderStage() {
    var c = byKey[cur];
    $('charTitle').textContent = c ? (c.name + ' · ' + c.events.length + ' 个剧情场景') : '选择角色';
    var box = $('events');
    box.innerHTML = '';
    if (!c) { box.innerHTML = '<div class="empty-tip">左侧选一个角色开始</div>'; return; }
    if (!c.events.length) { box.innerHTML = '<div class="empty-tip">这个角色在当前 Build 里没有独立对话事件</div>'; return; }

    c.events.forEach(function (ev) {
      if (stageFilter && (ev.stage || '无固定立绘') !== stageFilter) return;
      if (query && [ev.id, ev.sourceEvent, ev.title, ev.stage, ev.routeCondition].concat(ev.lines.map(function(l) { return [l.key,l.zh,l.en,l.label,l.condHuman,l.cond].join(' '); })).join(' ').toLowerCase().indexOf(query) < 0) return;
      var groups = L.branchesOf(ev);
      var assignable = assignableOf(ev);
      var done = assignable.filter(function (l) { return getLine(l.key, ev.id); }).length;
      if (onlyUnas && done === assignable.length) return;
      if (onlyDef && !ev.lines.some(function (l) { return l.spk === 'char' && !getLine(l.key, ev.id) && l.def; })) return;
      if (reviewOnly && !ev.lines.some(function(l) { var own = getLine(l.key,ev.id); return l.spk === 'char' && own && !L.samePortrait(D,own,l.def); })) return;

      var tree = groups.map(function (g, gi) {
        var cnt = g.lines.filter(function (l) { return l.spk === 'char'; }).length;
        var conds = (g.conds || []).map(function (c) { return esc(c); }).join(' / ');
        return '<div class="tgroup ' + g.kind + '">' +
          '<div class="tlabel"><span class="ico"></span>' + esc(g.label) +
          (cnt ? ' <button class="btn tiny branch-select" data-select-group="' + gi + '" data-ev="' + esc(ev.id) + '">选择本分支</button>' : '') +
          (cnt ? ' <span class="cnt">' + cnt + ' 句</span>' : '') +
          (conds ? '<span class="cond" title="触发条件">条件：' + conds + '</span>' : '') + '</div>' +
          '<div class="row-body">' + g.lines.map(function (r) { return chipHTML(ev, gi, r); }).join('') + '</div></div>';
      }).join('');

      var exitsHTML = exitSlotsHTML(ev);

      var el = document.createElement('div');
      el.className = 'event' + (ev.foreign ? ' foreign' : '');
      el.innerHTML =
        '<div class="event-head">' +
          '<span class="event-id">' + esc(ev.title || ev.id) + '</span>' +
          '<span class="badge-stage' + (ev.stage || ev.foreign ? '' : ' none') + '"' +
            ' style="--h:' + (ev.foreign ? 0 : stageHue(stageLabel(ev))) + '">' +
            esc(stageLabel(ev)) + '</span>' +
          '<span class="event-meta">' +
            (ev.foreign ? '<b class="warn">非本人</b> · ' + ev.lines.length + ' 句 · 随机 NPC 立绘'
              : assignable.length + ' 句 · ' + Math.max(0, groups.length - 1) + ' 个分支' +
                (ev.canArrest ? ' · 可被捕' : '') +
                (ev.sprite ? ' · 原版图 ' + esc(ev.sprite) : '')) + '</span>' +
          '<span class="event-prog">' + (ev.foreign ? '—' : done + '/' + assignable.length) + '</span>' +
        '</div>' +
        (ev.foreign
          ? '<div class="foreign-note">这段是<b>别人</b>在说话（游戏里用的是随机 NPC 立绘），换这个角色的立绘会穿帮，只作上下文参考。</div>'
          : '<div class="row"><div class="row-label">入场</div><div class="row-body">' + entrySlotsHTML(ev) + '</div></div>') +
        (ev.routeCondition ? '<div class="route-note">本路线：' + esc(ev.routeCondition) + ' · 游戏事件：' + esc(ev.sourceEvent || ev.id) + ' · 服装：' + esc(ev.sprite) + '</div>' : '') +
        '<div class="branch-legend">下方并列分组是可选路径或回调，不代表每次对话都会依次触发。</div>' +
        '<div class="tree">' + tree + '</div>' +
        (ev.foreign ? '' : '<div class="row end"><div class="row-label">退场</div><div class="row-body">' + exitsHTML + '</div></div>');
      box.appendChild(el);
    });
    $('resultCount').textContent = box.children.length + ' / ' + c.events.length + ' 场景';
    if (!box.children.length) box.innerHTML = '<div class="empty-tip">没有符合条件的场景。可清除搜索与筛选后继续。</div>';
  }

  function renderTray() {
    var body = $('trayBody');
    body.innerHTML = '';
    var c = byKey[cur];
    if (!c) { body.innerHTML = '<div class="empty-tip">—</div>'; return; }
    var used = L.usedCounts(c, map);

    var groups = [];
    var base = c.portraits.filter(function (p) { return p.kind === 'base'; });
    var released = c.portraits.filter(function(p) { return p.release; });
    if (released.length) groups.push(['发布版 · 语义复核（当前默认）', released]);
    if (base.length) groups.push(['基础立绘（初版）', base]);
    var sheets = {};
    c.portraits.filter(function (p) { return p.kind === 'story' && !p.release; }).forEach(function (p) {
      var k = p.sheet || '剧情立绘';
      (sheets[k] = sheets[k] || []).push(p);
    });
    Object.keys(sheets).forEach(function (k) { groups.push([k, sheets[k]]); });

    var f = trayFilter.trim().toLowerCase();
    var html = '';
    groups.forEach(function (pair) {
      var vis = f ? pair[1].filter(function (p) { return (p.label + p.sheet).toLowerCase().indexOf(f) >= 0; }) : pair[1];
      if (!vis.length) return;
      html += '<div class="tray-group"><h4>' + esc(pair[0]) + '</h4><div class="tray-grid">' +
        vis.map(function (p) {
          return '<button type="button" class="por" draggable="true" data-id="' + esc(p.id) + '" title="' + esc(p.label) + '">' +
            '<img loading="lazy" src="' + thumb(p.id, 'p') + '" alt="">' +
            '<span class="lab">' + esc(p.label) + '</span>' +
            (used[p.id] ? '<span class="use">' + used[p.id] + '</span>' : '') + '</button>';
        }).join('') + '</div></div>';
    });
    body.innerHTML = html || '<div class="empty-tip">没有立绘</div>';
  }

  function refresh() {
    var y = document.querySelector('.stage').scrollTop, ty = $('trayBody').scrollTop;
    renderChars(); renderStageFilter(); renderStage(); renderTray(); renderProgress(); renderSelection();
    document.querySelector('.stage').scrollTop = y; $('trayBody').scrollTop = ty;
    $('btnUndo').disabled = !undoStack.length; $('btnRedo').disabled = !redoStack.length;
  }
  function renderSelection() {
    var box = $('selectionInfo');
    if (!sel) { box.innerHTML = '<b>选中详情</b><p>选择一句台词，核对场景、触发条件和当前配图。带“复用”标记的台词会按场景单独保存。</p>'; return; }
    var ev = curEvent(sel.ev); if (!ev) { box.innerHTML = ''; return; }
    var records = ev.lines.filter(function(l) { return !sel.slot && sel.lks.indexOf(l.key) >= 0; });
    var line = records[0], id = line ? (getLine(line.key, ev.id) || line.def) : null;
    if(sel.slot) { var list = sel.slot === 'enter' ? L.entryList(map,cur,ev) : L.exitList(map,cur,ev); id = (list[sel.idx || 0] || {}).id; }
    var inherited = line && (byKey[cur].sharedKeys || {})[line.key] && getLine(line.key,ev.id) && !L.scopedLine(map,cur,ev.id,line.key);
    var p = byKey[cur].portraits.filter(function(p) { return p.id === id; })[0];
    box.innerHTML = '<b>' + esc(ev.title || ev.id) + '</b><div class="detail-meta">' + esc(ev.stage || '无固定阶段') + ' · ' + esc(ev.sprite || '无固定服装') + '</div>' +
      (id ? '<img class="detail-image" src="' + esc(thumb(id,'p')) + '" alt="当前场景配图">' : '') +
      '<p>' + esc(line ? line.zh : sel.slot === 'enter' ? '入场变体' : '退场变体') + '</p>' +
      '<code>' + esc(line ? line.key : ev.id) + '</code>' +
      '<div class="detail-meta">' + esc(p ? p.label : '未配图') + ' · ' + (line && getLine(line.key,ev.id) ? '手工指定' : '默认配图') + '</div>' +
      (line && getLine(line.key,ev.id) && !L.samePortrait(D,getLine(line.key,ev.id),line.def) ? '<div class="reference"><span>复核默认图（供对照）</span><img src="' + esc(thumb(line.def,'p')) + '" alt="复核默认配图"><small>不同不代表错误，请核对服装与当前剧情。</small></div>' : '') +
      (records.length > 1 ? '<p>已选 ' + records.length + ' 句；点立绘将应用到所选台词。</p>' : '') +
      (line && (line.condHuman || line.cond || ev.routeCondition) ? '<p>条件：' + esc(line.condHuman || line.cond || ev.routeCondition) + '</p>' : '') +
      (inherited ? '<p class="warning">沿用旧版全局标注；此台词还在其他场景出现。重新选图只修改本场景。</p>' : '') +
      (sel.slot ? '<p class="warning">变体名称用于标注；自定义条件名称不会自动生成游戏判定逻辑。</p>' : '');
  }

  /* ---------- 预览 ---------- */
  function showPreview(id, title, text) {
    var box = $('preview');
    if (id) {
      var p = (byKey[cur].portraits || []).filter(function (x) { return x.id === id; })[0];
      $('previewImg').src = thumb(id, 'p');
      $('previewImg').style.display = '';
      $('previewName').textContent = p ? (p.sheet ? p.sheet + ' / ' + p.label : p.label) : id;
      box.hidden = false;
    } else {
      $('previewImg').style.display = 'none';
      $('previewName').textContent = title || '';
      box.hidden = !(title || text);
    }
    $('previewText').textContent = text || '';
  }
  function hidePreview() { $('preview').hidden = true; }

  /* ---------- 选中态 ---------- */
  function paintSelection() {
    renderSelection();
    var a = drag ? Math.min(drag.i0, drag.i1) : -1;
    var b = drag ? Math.max(drag.i0, drag.i1) : -1;
    stage.querySelectorAll('.chip').forEach(function (c) {
      var on = false;
      if (drag && c.dataset.ev === drag.ev && c.dataset.g === drag.g) {
        var i = Number(c.dataset.i);
        on = i >= a && i <= b;
      } else if (sel && !sel.slot && sel.ev === c.dataset.ev && sel.lks.indexOf(c.dataset.lk) >= 0) {
        on = true;
      }
      c.classList.toggle('sel', on && !c.classList.contains('ghost'));
    });
    stage.querySelectorAll('.slot').forEach(function (s) {
      var on = !!(sel && sel.slot === s.dataset.slot && sel.ev === s.dataset.ev);
      if (on) on = (Number(s.dataset.idx || 0) === (sel.idx || 0));
      s.classList.toggle('sel', on);
    });
  }

  function groupLines(ev, gindex) {
    var g = L.branchesOf(ev)[gindex];
    return g ? g.lines : [];
  }

  function selectChip(chip, ev, extend) {
    var g = Number(chip.dataset.g), i = Number(chip.dataset.i);
    var grp = groupLines(ev, g);
    if (extend && anchor && anchor.ev === ev.id && anchor.g === g) {
      var a = Math.min(anchor.i, i), b = Math.max(anchor.i, i);
      sel = { ev: ev.id, lks: grp.filter(function (r) { return r.i >= a && r.i <= b && r.spk === 'char'; }).map(function (r) { return r.key; }) };
    } else {
      sel = { ev: ev.id, lks: grp.filter(function (r) { return r.i === i && r.spk === 'char'; }).map(function (r) { return r.key; }) };
      anchor = { ev: ev.id, g: g, i: i };
    }
    paintSelection();
  }

  /* ---------- 上色 / 清除 ---------- */
  function applyPortrait(id) {
    if (!sel) return;
    pushUndo();
    if (sel.slot === 'enter') setEntry(sel.ev, sel.idx || 0, id);
    else if (sel.slot === 'exit') setExit(sel.ev, sel.idx || 0, id);
    else sel.lks.forEach(function (lk) { setLine(lk, id, sel.ev); });
    save(); refresh();
  }

  function clearAssign() {
    if (!sel) return;
    pushUndo();
    if (sel.slot === 'enter') setEntry(sel.ev, sel.idx || 0, null);
    else if (sel.slot === 'exit') setExit(sel.ev, sel.idx || 0, null);
    else sel.lks.forEach(function (lk) {
      setLine(lk, null, sel.ev);
      if (charNode(cur).auto) delete charNode(cur).auto[lk];
    });
    save(); refresh();
  }

  /* ---------- 批量 ---------- */
  function fillEvent(ev, id) {
    var n = 0;
    assignableOf(ev).forEach(function (l) { setLine(l.key, id, ev.id); n++; });
    return n;
  }
  function focusEventId() { return (sel && sel.ev) || focusEv; }

  function applyToEvent(id) {
    var ev = curEvent(focusEventId());
    if (!ev) return;
    pushUndo();
    var n = fillEvent(ev, id);
    save(); refresh();
    flash('已把这张图套到「' + ev.id + '」全部 ' + n + ' 句');
  }
  function applyToChar(id) {
    var c = byKey[cur];
    if (!c) return;
    pushUndo();
    var n = 0;
    c.events.forEach(function (ev) { n += fillEvent(ev, id); });
    save(); refresh();
    flash('已把这张图套到「' + c.name + '」全部 ' + n + ' 句');
  }
  /* ---------- 交互 ---------- */
  $('charList').addEventListener('click', function (e) {
    var el = e.target.closest('.char');
    if (!el) return;
    cur = el.dataset.ck; sel = null; anchor = null; focusEv = null; stageFilter = '';
    document.querySelector('.stage').scrollTop = 0;
    refresh();
  });

  var stage = $('events');

  stage.addEventListener('mousedown', function (e) {
    if (e.button !== 0) return;
    var slot = e.target.closest('.slot');
    var chip = e.target.closest('.chip');
    if (slot) {
      if (e.target.dataset.clear || e.target.dataset.delVariant) return;
      sel = { ev: slot.dataset.ev, slot: slot.dataset.slot, idx: Number(slot.dataset.idx || 0) };
      anchor = null; focusEv = slot.dataset.ev;
      paintSelection();
      return;
    }
    if (!chip || chip.classList.contains('ghost')) return;
    var ev = curEvent(chip.dataset.ev);
    if (!ev) return;
    drag = { ev: ev.id, g: Number(chip.dataset.g), i0: Number(chip.dataset.i), i1: Number(chip.dataset.i) };
    focusEv = ev.id;
    selectChip(chip, ev, e.shiftKey);
    e.preventDefault();
  });

  stage.addEventListener('mouseover', function (e) {
    var chip = e.target.closest('.chip');
    if (!chip) return;
    if (drag) {
      if (chip.dataset.ev !== drag.ev || Number(chip.dataset.g) !== drag.g) return;
      drag.i1 = Number(chip.dataset.i);
      var ev = curEvent(drag.ev);
      var grp = groupLines(ev, drag.g);
      var a = Math.min(drag.i0, drag.i1), b = Math.max(drag.i0, drag.i1);
      sel = { ev: drag.ev, lks: grp.filter(function (r) { return r.i >= a && r.i <= b && r.spk === 'char'; }).map(function (r) { return r.key; }) };
      paintSelection();
    }
    var ev2 = curEvent(chip.dataset.ev);
    var line = ev2 && (ev2.lines || []).filter(function (l) { return l.key === chip.dataset.lk; })[0];
    showPreview(getLine(chip.dataset.lk, chip.dataset.ev) || (line && line.def), line ? line.key : '', line ? line.zh : '');
  });

  window.addEventListener('mouseup', function () {
    if (!drag) return;
    drag = null;
    refresh();
  });

  stage.addEventListener('click', function (e) {
    var groupButton = e.target.closest('[data-select-group]');
    if (groupButton) {
      var groupEvent = curEvent(groupButton.dataset.ev);
      sel = {ev: groupEvent.id, lks: groupLines(groupEvent, Number(groupButton.dataset.selectGroup)).filter(function(l) {return l.spk === 'char';}).map(function(l) {return l.key;})};
      focusEv = groupEvent.id; anchor = null; paintSelection(); return;
    }
    // 加一个入场/退场变体
    var addBtn = e.target.closest('[data-add-variant]');
    if (addBtn) {
      if (!cur) return;
      var kind = addBtn.dataset.addVariant, evId = addBtn.dataset.ev;
      var label = prompt(kind === 'enter' ? '这个入场的条件（第一次来 / 再次上门…）' : '这个退场的条件（走完哪个分支 / 被捕…）',
        kind === 'enter' ? '新入场' : '新退场');
      if (label === null) return;
      label = label.trim() || (kind === 'enter' ? '入场' : '退场');
      var existingVariants = kind === 'enter' ? L.entryList(map,cur,curEvent(evId)) : L.exitList(map,cur,curEvent(evId));
      if (existingVariants.some(function(v) { return v.label === label; })) return flash('条件名称不能重复，否则导出时会互相覆盖');
      pushUndo();
      var list = kind === 'enter' ? ensureEntries(evId) : ensureExits(evId);
      list.push({ label: label.trim() || (kind === 'enter' ? '入场' : '退场'), id: null });
      save(); refresh();
      sel = { ev: evId, slot: kind, idx: list.length - 1 };
      paintSelection();
      return;
    }
    // 删掉变体
    var delBtn = e.target.closest('[data-del-variant]');
    if (delBtn) {
      var boxd = delBtn.closest('.slot');
      var kd = delBtn.dataset.delVariant;
      pushUndo();
      var ld = kd === 'enter' ? ensureEntries(boxd.dataset.ev) : ensureExits(boxd.dataset.ev);
      if (ld.length <= 1) { undoStack.pop(); return; }
      ld.splice(Number(boxd.dataset.idx), 1);
      sel = null; save(); refresh(); return;
    }
    // 双击改别名
    if (e.target.dataset.rename) {
      var lb = e.target, k = lb.dataset.kind;
      var name = prompt('改名（写成条件）', lb.textContent);
      if (name === null) return;
      name = name.trim() || '名称';
      var currentList = k === 'enter' ? L.entryList(map,cur,curEvent(lb.dataset.ev)) : L.exitList(map,cur,curEvent(lb.dataset.ev));
      if (currentList.some(function(v,i) { return i !== Number(lb.dataset.idx) && v.label === name; })) return flash('条件名称不能重复，未修改');
      pushUndo();
      var lst = k === 'enter' ? ensureEntries(lb.dataset.ev) : ensureExits(lb.dataset.ev);
      if (lst[Number(lb.dataset.idx)]) lst[Number(lb.dataset.idx)].label = name.trim() || '名称';
      save(); refresh(); return;
    }
    var slot = e.target.closest('.slot');
    if (!slot) return;
    if (e.target.dataset.clear) {
      pushUndo();
      if (slot.dataset.slot === 'enter') setEntry(slot.dataset.ev, Number(slot.dataset.idx || 0), null);
      else setExit(slot.dataset.ev, Number(slot.dataset.idx || 0), null);
      save(); refresh();
    }
  });

  stage.addEventListener('mouseleave', hidePreview);

  /* ---------- 托盘 ---------- */
  $('trayBody').addEventListener('click', function (e) {
    var por = e.target.closest('.por');
    if (!por) return;
    if (e.detail > 1) return;
    if (!sel) { flash('先框选台词，或者直接把立绘拖到格子上'); showPreview(por.dataset.id, '', ''); return; }
    applyPortrait(por.dataset.id);
  });
  $('trayBody').addEventListener('dblclick', function (e) {
    var por = e.target.closest('.por');
    if (!por) return;
    e.preventDefault();
    applyToEvent(por.dataset.id);
  });
  $('trayBody').addEventListener('contextmenu', function (e) {
    var por = e.target.closest('.por');
    if (!por) return;
    e.preventDefault();
    openCtx(e.clientX, e.clientY, por.dataset.id);
  });
  $('trayBody').addEventListener('mouseover', function (e) {
    var por = e.target.closest('.por');
    if (por) showPreview(por.dataset.id, '', '');
  });
  $('trayBody').addEventListener('mouseleave', hidePreview);

  /* ---------- 右键批量菜单 ---------- */
  var ctxPorId = null;

  function countOf(scope, c, ev) {
    if (scope === 'ev') return ev ? assignableOf(ev).length : 0;
    if (scope === 'char') return c.events.reduce(function (n, x) { return n + assignableOf(x).length; }, 0);
    return D.characters.reduce(function (n, cc) {
      return n + cc.events.reduce(function (m, x) { return m + assignableOf(x).length; }, 0);
    }, 0);
  }

  function openCtx(x, y, id) {
    var c = byKey[cur];
    if (!c) return;
    ctxPorId = id;
    var ev = curEvent(focusEventId());
    $('ctx').innerHTML =
      '<button data-act="ev">套用到本场全部 <span class="cnt">' + countOf('ev', c, ev) + ' 句</span></button>' +
      '<button data-act="char">套用到「' + esc(c.name) + '」全部 <span class="cnt">' + countOf('char', c, ev) + ' 句</span></button>' +
      '<hr><button data-act="enter">设为本场入场</button>' +
      '<button data-act="exit">设为本场退场</button>' +
      '<hr><button data-act="clear" class="warn">清空本场台词</button>';
    var box = $('ctx');
    box.hidden = false;
    box.style.left = x + 'px';
    box.style.top = y + 'px';
    var r = box.getBoundingClientRect();
    box.style.left = Math.max(4, Math.min(x, innerWidth - r.width - 8)) + 'px';
    box.style.top = Math.max(4, Math.min(y, innerHeight - r.height - 8)) + 'px';
  }
  function closeCtx() { $('ctx').hidden = true; }

  $('ctx').addEventListener('click', function (e) {
    var b = e.target.closest('button');
    if (!b) return;
    var act = b.dataset.act, id = ctxPorId;
    var c = byKey[cur];
    var ev = curEvent(focusEventId());
    closeCtx();
    if (!id || !c) return;
    if (!ev && act !== 'char') return flash('先选中要修改的剧情场景');
    if (act === 'ev') applyToEvent(id);
    else if (act === 'char') applyToChar(id);
    else if (act === 'enter' && ev) { pushUndo(); setEntry(ev.id, 0, id); save(); refresh(); }
    else if (act === 'exit' && ev) { pushUndo(); setExit(ev.id, 0, id); save(); refresh(); }
    else if (act === 'clear' && ev) {
      pushUndo();
      assignableOf(ev).forEach(function (l) { setLine(l.key, null, ev.id); });
      save(); refresh();
    }
  });

  document.addEventListener('mousedown', function (e) {
    if (!e.target.closest('#ctx')) closeCtx();
  }, true);
  window.addEventListener('blur', closeCtx);
  $('events').addEventListener('scroll', closeCtx);

  /* ---------- 从托盘拖到台词 ---------- */
  var dragId = null;

  document.addEventListener('dragstart', function (e) {
    if (e.target.tagName === 'INPUT') return;
    var por = e.target.closest('.por');
    if (!por) { e.preventDefault(); return; }
    dragId = por.dataset.id;
    e.dataTransfer.setData('text/plain', dragId);
    e.dataTransfer.effectAllowed = 'copy';
  });
  document.addEventListener('dragend', function () {
    dragId = null;
    stage.querySelectorAll('.drop').forEach(function (n) { n.classList.remove('drop'); });
  });

  stage.addEventListener('dragover', function (e) {
    var t = e.target.closest('.chip, .slot, .event-head');
    if (!t || t.classList.contains('ghost')) return;
    e.preventDefault();
    if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy';
    if (!t.classList.contains('drop')) t.classList.add('drop');
  });
  stage.addEventListener('dragleave', function (e) {
    var t = e.target.closest('.chip, .slot, .event-head');
    if (t) t.classList.remove('drop');
  });
  stage.addEventListener('drop', function (e) {
    var t = e.target.closest('.chip, .slot, .event-head');
    if (!t) return;
    e.preventDefault();
    t.classList.remove('drop');
    var id = dragId || (e.dataTransfer && e.dataTransfer.getData('text/plain'));
    if (!byKey[cur].portraits.some(function(p) { return p.id === id; })) return flash('此图片不属于当前角色，未修改');
    if (!id) return;
    if (t.classList.contains('event-head')) {
      var head = t.closest('.event');
      var slotEl = head && head.querySelector('.slot');
      if (slotEl) { focusEv = slotEl.dataset.ev; applyToEvent(id); }
      return;
    }
    if (t.classList.contains('slot')) {
      focusEv = t.dataset.ev;
      pushUndo();
      if (t.dataset.slot === 'enter') setEntry(t.dataset.ev, Number(t.dataset.idx || 0), id);
      else setExit(t.dataset.ev, Number(t.dataset.idx || 0), id);
      save(); refresh(); return;
    }
    if (t.classList.contains('ghost')) return;
    focusEv = t.dataset.ev;
    if (sel && !sel.slot && sel.ev === t.dataset.ev && sel.lks.indexOf(t.dataset.lk) >= 0) applyPortrait(id);
    else { pushUndo(); setLine(t.dataset.lk, id, t.dataset.ev); save(); refresh(); }
  });

  /* ---------- 其它控件 ---------- */
  $('trayFilter').addEventListener('input', function (e) { trayFilter = e.target.value; renderTray(); });
  $('onlyUnas').addEventListener('change', function (e) { onlyUnas = e.target.checked; filterChanged(); });
  $('stageFilter').addEventListener('change', function (e) { stageFilter = e.target.value; filterChanged(); });
  $('onlyDef').addEventListener('change', function (e) { onlyDef = e.target.checked; filterChanged(); });

  $('btnClearChar').addEventListener('click', function () {
    if (!cur) return;
    if (!confirm('清空「' + byKey[cur].name + '」的全部标注？')) return;
    pushUndo();
    delete map[cur];
    sel = null; save(); refresh();
  });

  function historyStep(redo) {
    var from = redo ? redoStack : undoStack, to = redo ? undoStack : redoStack;
    if (!from.length) return;
    to.push(JSON.stringify(map)); map = JSON.parse(from.pop()); sel = null; save(); refresh(); flash(redo ? '已重做' : '已撤销');
  }
  $('btnUndo').onclick = function() { historyStep(false); };
  $('btnRedo').onclick = function() { historyStep(true); };
  function filterChanged() { sel = null; anchor = null; focusEv = null; renderStage(); renderSelection(); hidePreview(); }
  $('reviewOnly').onchange = function(e) { reviewOnly = e.target.checked; filterChanged(); };
  $('dialogSearch').addEventListener('input', function(e) { query = e.target.value.trim().toLowerCase(); filterChanged(); });
  $('resetFilters').onclick = function() { query = stageFilter = ''; reviewOnly = onlyUnas = onlyDef = false; $('reviewOnly').checked = false; $('dialogSearch').value = ''; $('onlyUnas').checked = $('onlyDef').checked = false; refresh(); };
  stage.addEventListener('keydown', function(e) {
    if ((e.key === 'Enter' || e.key === ' ') && e.target.matches('.chip:not(.ghost)')) {
      e.preventDefault(); var ev = curEvent(e.target.dataset.ev); focusEv = ev.id; selectChip(e.target,ev,e.shiftKey);
    }
  });
  document.addEventListener('keydown', function(e) {
    var typing = document.activeElement && (document.activeElement.matches('input,textarea,select') || document.activeElement.isContentEditable);
    if ((e.ctrlKey || e.metaKey) && !typing) {
      var key = e.key.toLowerCase();
      if (key === 'z' || key === 'y') { e.preventDefault(); historyStep(key === 'y' || e.shiftKey); return; }
      if (key === 'f') { e.preventDefault(); $('dialogSearch').focus(); return; }
      if (key === 's') { e.preventDefault(); flushSave(); return; }
    }
    if (e.key === 'Escape' && !typing) { sel = null; hidePreview(); closeCtx(); refresh(); }
    if ((e.key === 'Delete' || e.key === 'Backspace') && sel && !typing) { e.preventDefault(); clearAssign(); }
  });

  /* ---------- 导出 / 导入 ---------- */
  function download(obj, name) {
    var blob = new Blob([JSON.stringify(obj, null, 1)], { type: 'application/json' });
    var a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = name;
    a.click();
    setTimeout(function () { URL.revokeObjectURL(a.href); }, 2000);
  }
  var stamp = function () { return new Date().toISOString().slice(0, 10); };

  $('btnBackup').onclick = function() { download({format:'dsh-workbench-backup',version:1,build:D.build,created:new Date().toISOString(),map:map}, '手工标注备份_' + stamp() + '.json'); };
  $('btnExport').addEventListener('click', function () {
    download(L.buildExport(D, map, null), '立绘映射_' + stamp() + '.json');
  });
  $('btnRestore').addEventListener('click', function () {
    var raw = null;
    try { raw = localStorage.getItem(STORE + '-backup'); } catch (e) { }
    if (!raw) return flash('暂无自动备份；首次修改前会保留本次打开时的已有存储，也可手动导出备份');
    if (!confirm('用备份覆盖当前标注？当前内容会先另存到 -before-restore。')) return;
    try {
      localStorage.setItem(STORE + '-before-restore', JSON.stringify(map));
      localStorage.setItem(STORE, raw);
    } catch (e) { return flash('浏览器不允许写入'); }
    clearTimeout(saveTimer); dirty = false; location.reload();
  });
  $('btnExportChar').addEventListener('click', function () {
    if (!cur) return flash('先选角色');
    download(L.buildExport(D, map, [cur]), '立绘映射_' + byKey[cur].name + '_' + stamp() + '.json');
  });
  $('btnCopy').addEventListener('click', function () {
    var txt = JSON.stringify(L.buildExport(D, map, null));
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(txt).then(function () { flash('已复制 ' + txt.length + ' 字符'); },
        function () { flash('浏览器不允许剪贴板，请用导出'); });
    } else { flash('浏览器不支持剪贴板，请用导出'); }
  });
  $('fileImport').addEventListener('change', function (e) {
    var f = e.target.files[0];
    if (!f) return;
    e.target.value = '';
    if (f.size > 20 * 1024 * 1024) return flash('文件超过 20 MB，请核对是不是映射 JSON');
    var fr = new FileReader();
    fr.onload = function () {
      try {
        var j = JSON.parse(fr.result), result = L.prepareImport(D, map, j);
        if (!confirm('校验通过：合并 ' + result.count + ' 句标注、' + result.slots + ' 个入退场槽位。文件中同名项将覆盖，其他标注保留。是否导入？')) return;
        if (storageBlocked) throw Error('原存储异常，当前禁止覆盖；先导出备份再处理存储');
        // 导入前落地备份；写入失败则不进行合并。
        localStorage.setItem(STORE + '-before-import', JSON.stringify(map));
        pushUndo(); map = result.map; sel = null; save(); refresh();
        flash('已合并 ' + result.count + ' 句；可撤销或从备份恢复');
      } catch (err) { flash('导入未执行：' + err.message); }
      e.target.value = '';
    };
    fr.onerror = function() { flash('文件读取失败，未修改标注'); e.target.value = ''; };
    fr.readAsText(f, 'utf-8');
  });

  var flashTimer = null;
  var HINT = '框选台词 → 点立绘上色；或直接把立绘拖到格子上。<b>双击立绘</b>=整场套用，<b>右键立绘</b>=批量，Ctrl+Z 撤销。';
  function flash(msg) {
    $('hint').textContent = msg;
    clearTimeout(flashTimer);
    flashTimer = setTimeout(function () { $('hint').innerHTML = HINT; }, 2400);
  }

  /* ---------- 启动 ---------- */
  load();
  saveState(storageBlocked ? '读取异常 · 原数据已保护' : '已读取 · 本地自动保存', storageBlocked);
  $('buildTag').textContent = '2.0 · Build ' + (D.build || '?');
  var first = D.characters.filter(function (c) { return c.events.length; })[0] || D.characters[0];
  cur = first ? first.key : null;
  refresh();

  window.__WB__ = { D: D, logic: L, flushSave: flushSave, get map() { return map; }, set map(v) { map = v; refresh(); } };
})();
