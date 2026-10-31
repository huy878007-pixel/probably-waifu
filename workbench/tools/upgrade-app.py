from pathlib import Path
r=Path(__file__).resolve().parent.parent
if 'id="dialogSearch"' in (r/'index.html').read_text(encoding='utf-8-sig'):
    raise SystemExit('Already upgraded. This is a one-time migration, do not run again.')
def change(name, old, new):
    p=r/name; text=p.read_text(encoding='utf-8-sig')
    if old not in text: raise RuntimeError(f'Missing target {name}: {old[:100]}')
    p.write_text(text.replace(old,new),encoding='utf-8')

change('index.html','<div class="progress" id="progress"></div>','<div class="progress" id="progress"></div>\n  <span id="saveStatus" class="save-status" role="status" aria-live="polite">正在读取</span>')
change('index.html','<button id="btnRestore"','<button id="btnBackup" class="btn">备份手工标注</button>\n    <button id="btnRestore"')
change('index.html','<div class="stage-tools">','<div class="stage-tools">\n        <button id="btnUndo" class="btn tiny" title="Ctrl+Z">撤销</button>\n        <button id="btnRedo" class="btn tiny" title="Ctrl+Shift+Z / Ctrl+Y">重做</button>')
change('index.html','<div id="events"></div>','<div class="searchbar"><input id="dialogSearch" type="search" aria-label="搜索当前角色台词、钩子或事件" placeholder="搜索台词、钩子、事件或条件…"><span id="resultCount"></span><button id="resetFilters" class="btn tiny">清除筛选</button></div>\n    <div id="events"></div>')
change('index.html','<div id="trayBody"','<section id="selectionInfo" class="selection-info" aria-label="选中台词详情"></section>\n    <div id="trayBody"')
change('index.html','<script src="data.js"></script>','<script src="data.js"></script>\n<script src="release-data.js"></script>')
change('index.html','批量（本场/本角色/全部角色）','批量（本场/本角色）')
change('index.html','id="trayFilter" placeholder','id="trayFilter" aria-label="搜索立绘名称" placeholder')

change('logic.js',"  function linesOf(map, ck) { return (map && map[ck] && map[ck].lines) || {}; }",'''  function linesOf(map, ck) { return (map && map[ck] && map[ck].lines) || {}; }
  // 场景覆盖优先于旧全局标注；显式 null 表示只在此场景回到默认图。
  function lineValue(map, ck, evId, key) {
    var node = map && map[ck] || {}, scoped = (node.contexts || {})[evId] || {};
    return Object.prototype.hasOwnProperty.call(scoped, key) ? scoped[key] : (node.lines || {})[key];
  }
  function scopedLine(map, ck, evId, key) {
    return Object.prototype.hasOwnProperty.call(((map[ck] || {}).contexts || {})[evId] || {}, key);
  }''')
change('logic.js','if (lines[l.key]) mine++;','if (lineValue(map, char.key, ev.id, l.key)) mine++;')
change('logic.js','var v = lines[l.key] || l.def;','var v = lineValue(map, char.key, ev.id, l.key) || l.def;')
change('logic.js','var evs = {};\n      (c.events', 'var evs = {};\n      (c.events') if False else None
change('logic.js',"out.characters[ck] = { sprites: c.sprites || [], lines: lines, events: evs };",'''out.characters[ck] = { sprites: c.sprites || [], lines: lines, events: evs };
        if (m.contexts) { out.characters[ck].contexts = JSON.parse(JSON.stringify(m.contexts)); out.version = 3; }
        out.characters[ck].scenes = (c.events || []).filter(function (ev) { return !ev.foreign; }).map(function (ev) {
          var resolved = {};
          ev.lines.forEach(function (l) { if (l.spk === 'char') resolved[l.key] = lineValue(map, ck, ev.id, l.key) || l.def || null; });
          return { id: ev.id, event: ev.sourceEvent || ev.id, sprite: ev.sprite, stage: ev.stage,
            condition: ev.routeCondition || '', lines: resolved };
        });''')
change('logic.js','var dst = out[ck] = { lines: {}, events: {} };','var dst = out[ck] = { lines: {}, events: {} };\n      if (src.contexts) dst.contexts = JSON.parse(JSON.stringify(src.contexts));')
change('logic.js','branchesOf: branchesOf, linesOf: linesOf,','lineValue: lineValue, scopedLine: scopedLine, prepareImport: prepareImport,\n    branchesOf: branchesOf, linesOf: linesOf,')
change('logic.js','  root.WBLogic = {', '''  // 全部校验通过后才返回新快照，导入失败不允许留下半批修改。
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

  root.WBLogic = {''')

change('app.js',"  var undoStack = [];","  var undoStack = [], redoStack = [];\n  var query = '';\n  var storageBlocked = false, dirty = false;")
change('app.js','    undoStack.push(JSON.stringify(map));','    redoStack = [];\n    undoStack.push(JSON.stringify(map));')
change('app.js',"catch (e) { map = {}; return; }", "catch (e) { map = {}; storageBlocked = true; return; }")
change('app.js',"    if (!raw) return;\n    rawBackup = raw;", "    if (!raw) return;\n    if (!map || typeof map !== 'object' || Array.isArray(map)) { map = {}; storageBlocked = true; return; }\n    rawBackup = raw;")
start=(r/'app.js').read_text(encoding='utf-8'); a=start.index('  var saveTimer = null;'); b=start.index('\n  function charNode',a)
new='''  var saveTimer = null;
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
'''
(r/'app.js').write_text(start[:a]+new+start[b:],encoding='utf-8')
change('app.js',"  function charNode(ck) { return map[ck] || (map[ck] = { lines: {}, events: {} }); }", "  function charNode(ck) { var n = map[ck] || (map[ck] = {}); n.lines = n.lines || {}; n.events = n.events || {}; return n; }")
change('app.js',"  function getLine(lk) { return (map[cur] && map[cur].lines && map[cur].lines[lk]) || null; }", "  function getLine(lk, evId) { return L.lineValue(map, cur, evId || focusEventId(), lk) || null; }")
change('app.js','  function setLine(lk, id) {\n    var node = charNode(cur);\n    node.lines[lk] = id;', '''  function setLine(lk, id, evId) {
    var node = charNode(cur);
    if ((byKey[cur].sharedKeys || {})[lk]) {
      evId = evId || focusEventId();
      if (!evId) throw Error('复用台词缺少场景');
      node.contexts = node.contexts || {}; node.contexts[evId] = node.contexts[evId] || {};
      node.contexts[evId][lk] = id;
    } else if (id) node.lines[lk] = id; else delete node.lines[lk];''')
change('app.js',"    $('progress').textContent = '共 ' + t + ' 句 · 你已标 ' + m + ' 句（其余 ' + (t - m) + ' 句是默认图）';", "    $('progress').textContent = '共 ' + t + ' 个场景台词 · 手工 ' + m + ' · 默认 ' + (d-m) + ' · 缺图 ' + (t-d);")
change('app.js',"      var el = document.createElement('div');\n      el.className = 'char'", "      var el = document.createElement('button');\n      el.type = 'button'; el.setAttribute('aria-pressed', String(c.key === cur));\n      el.className = 'char'")
change('app.js','var mine = getLine(rec.key);','var mine = getLine(rec.key, ev.id);')
change('app.js',"    if (!mine && rec.def) cls.push('auto');", "    if (!mine && rec.def) cls.push('auto');\n    if (L.scopedLine(map, cur, ev.id, rec.key)) cls.push('scoped');")
change('app.js',"      '<span class=\"no\">' + (rec.i + 1) + '</span>' + body +", "      '<span class=\"no\">' + (rec.i + 1) + '</span>' + ((byKey[cur].sharedKeys || {})[rec.key] ? '<span class=\"shared-mark\" title=\"复用台词 · 按场景独立标注\">复用</span>' : '') + body +")
change('app.js','var done = assignable.filter(function (l) { return getLine(l.key); }).length;', 'var done = assignable.filter(function (l) { return getLine(l.key, ev.id); }).length;')
change('app.js',"!getLine(l.key) && l.def", "!getLine(l.key, ev.id) && l.def")
change('app.js','      var groups = L.branchesOf(ev);', '''      if (query && [ev.id, ev.sourceEvent, ev.title, ev.stage, ev.routeCondition].concat(ev.lines.map(function(l) { return [l.key,l.zh,l.en,l.label,l.condHuman,l.cond].join(' '); })).join(' ').toLowerCase().indexOf(query) < 0) return;
      var groups = L.branchesOf(ev);''')
change('app.js',"        '<div class=\"tree\">' + tree", "        (ev.routeCondition ? '<div class=\"route-note\">本路线：' + esc(ev.routeCondition) + ' · 游戏事件：' + esc(ev.sourceEvent || ev.id) + ' · 服装：' + esc(ev.sprite) + '</div>' : '') +\n        '<div class=\"branch-legend\">下方并列分组是可选路径或回调，不代表每次对话都会依次触发。</div>' +\n        '<div class=\"tree\">' + tree")
change('app.js',"      box.appendChild(el);\n    });\n  }\n\n  function renderTray", "      box.appendChild(el);\n    });\n    $('resultCount').textContent = box.children.length + ' / ' + c.events.length + ' 场景';\n    if (!box.children.length) box.innerHTML = '<div class=\"empty-tip\">没有符合条件的场景。可清除搜索与筛选后继续。</div>';\n  }\n\n  function renderTray")
change('app.js',"          return '<div class=\"por\" draggable=\"true\"", "          return '<button type=\"button\" class=\"por\" draggable=\"true\"")
change('app.js',"(used[p.id] ? '<span class=\"use\">' + used[p.id] + '</span>' : '') + '</div>';", "(used[p.id] ? '<span class=\"use\">' + used[p.id] + '</span>' : '') + '</button>';")
change('app.js','  function refresh() { renderChars(); renderStageFilter(); renderStage(); renderTray(); renderProgress(); }', '''  function refresh() {
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
      (records.length > 1 ? '<p>已选 ' + records.length + ' 句；点立绘将应用到所选台词。</p>' : '') +
      (line && (line.condHuman || line.cond || ev.routeCondition) ? '<p>条件：' + esc(line.condHuman || line.cond || ev.routeCondition) + '</p>' : '') +
      (inherited ? '<p class="warning">沿用旧版全局标注；此台词还在其他场景出现。重新选图只修改本场景。</p>' : '') +
      (sel.slot ? '<p class="warning">变体名称用于标注；自定义条件名称不会自动生成游戏判定逻辑。</p>' : '');
  }''')
change('app.js',"    $('previewText').textContent = (text || '').slice(0, 220);", "    $('previewText').textContent = text || '';")
change('app.js',"  function paintSelection() {", "  function paintSelection() {\n    renderSelection();")
change('app.js','setLine(lk, id);','setLine(lk, id, sel.ev);')
change('app.js','      delete charNode(cur).lines[lk];','      setLine(lk, null, sel.ev);')
change('app.js',"setLine(l.key, id); n++;", "setLine(l.key, id, ev.id); n++;")
# 删除有缺陷的跨角色套图入口和实现。
p=r/'app.js'; t=p.read_text(encoding='utf-8'); a=t.index('  function applyToAll(id) {'); b=t.index('  /* ---------- 交互',a); t=t[:a]+t[b:]; p.write_text(t,encoding='utf-8')
change('app.js',"    showPreview(getLine(chip.dataset.lk),", "    showPreview(getLine(chip.dataset.lk, chip.dataset.ev) || (line && line.def),")
change('app.js',"    refresh();\n  });\n\n  var stage", "    document.querySelector('.stage').scrollTop = 0;\n    refresh();\n  });\n\n  var stage")
change('app.js',"      var ld = kd === 'enter'", "      pushUndo();\n      var ld = kd === 'enter'")
change('app.js',"      if (ld.length <= 1) return;\n      pushUndo();", "      if (ld.length <= 1) { undoStack.pop(); return; }")
change('app.js',"      '<button data-act=\"all\">套用到全部角色 <span class=\"cnt\">' + countOf('all', c, ev) + ' 句</span></button>' +\n", '')
change('app.js',"    else if (act === 'all') {\n      if (!confirm('把这张图套到全部角色共 ' + countOf('all', c, ev) + ' 句？\\n（错了可以 Ctrl+Z 撤销）')) return;\n      applyToAll(id);\n    } else if", "    else if")
change('app.js',"assignableOf(ev).forEach(function (l) { delete charNode(cur).lines[l.key]; });", "assignableOf(ev).forEach(function (l) { setLine(l.key, null, ev.id); });")
change('app.js',"    var ev = curEvent(focusEventId()) || c.events[0];", "    var ev = curEvent(focusEventId());")
change('app.js',"var ev = curEvent(focusEventId()) || (c && c.events[0]);", "var ev = curEvent(focusEventId());")
change('app.js',"    if (!id || !c) return;", "    if (!id || !c) return;\n    if (!ev && act !== 'char') return flash('先选中要修改的剧情场景');")
change('app.js',"    var id = dragId || (e.dataTransfer && e.dataTransfer.getData('text/plain'));", "    var id = dragId || (e.dataTransfer && e.dataTransfer.getData('text/plain'));\n    if (!byKey[cur].portraits.some(function(p) { return p.id === id; })) return flash('此图片不属于当前角色，未修改');")
change('app.js','setLine(t.dataset.lk, id);','setLine(t.dataset.lk, id, t.dataset.ev);')
p=r/'app.js';t=p.read_text(encoding='utf-8');a=t.index("  document.addEventListener('keydown', function (e) {");b=t.index('  /* ---------- 导出',a)
t=t[:a]+'''  function historyStep(redo) {
    var from = redo ? redoStack : undoStack, to = redo ? undoStack : redoStack;
    if (!from.length) return;
    to.push(JSON.stringify(map)); map = JSON.parse(from.pop()); sel = null; save(); refresh(); flash(redo ? '已重做' : '已撤销');
  }
  $('btnUndo').onclick = function() { historyStep(false); };
  $('btnRedo').onclick = function() { historyStep(true); };
  $('dialogSearch').addEventListener('input', function(e) { query = e.target.value.trim().toLowerCase(); renderStage(); });
  $('resetFilters').onclick = function() { query = stageFilter = ''; onlyUnas = onlyDef = false; $('dialogSearch').value = ''; $('onlyUnas').checked = $('onlyDef').checked = false; refresh(); };
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

'''+t[b:];p.write_text(t,encoding='utf-8')
change('app.js',"  $('btnExport').addEventListener('click', function () {", "  $('btnBackup').onclick = function() { download({format:'dsh-workbench-backup',version:1,build:D.build,created:new Date().toISOString(),map:map}, '手工标注备份_' + stamp() + '.json'); };\n  $('btnExport').addEventListener('click', function () {")
p=r/'app.js'; t=p.read_text(encoding='utf-8'); a=t.index('        var j = JSON.parse(fr.result);');b=t.index("      e.target.value = '';",a)
t=t[:a]+'''        var j = JSON.parse(fr.result), result = L.prepareImport(D, map, j);
        if (!confirm('校验通过：合并 ' + result.count + ' 句标注、' + result.slots + ' 个入退场槽位。文件中同名项将覆盖，其他标注保留。是否导入？')) return;
        if (storageBlocked) throw Error('原存储异常，当前禁止覆盖；先导出备份再处理存储');
        // 导入前落地备份；写入失败则不进行合并。
        localStorage.setItem(STORE + '-before-import', JSON.stringify(map));
        pushUndo(); map = result.map; sel = null; save(); refresh();
        flash('已合并 ' + result.count + ' 句；可撤销或从备份恢复');
      } catch (err) { flash('导入未执行：' + err.message); }
'''+t[b:];p.write_text(t,encoding='utf-8')
change('app.js',"    fr.readAsText(f, 'utf-8');", "    fr.onerror = function() { flash('文件读取失败，未修改标注'); e.target.value = ''; };\n    fr.readAsText(f, 'utf-8');")
change('app.js',"    $('hint').innerHTML = msg;", "    $('hint').textContent = msg;")
change('app.js',"'已把这张图套到「' + esc(ev.id)", "'已把这张图套到「' + ev.id")
change('app.js',"'已把这张图套到「' + esc(c.name)", "'已把这张图套到「' + c.name")
change('app.js',"  $('buildTag').textContent = 'Build ' + (D.build || '?');", "  saveState(storageBlocked ? '读取异常 · 原数据已保护' : '已读取 · 本地自动保存', storageBlocked);\n  $('buildTag').textContent = '2.0 · Build ' + (D.build || '?');")
change('app.js',"get map() { return map; }", "flushSave: flushSave, get map() { return map; }")
print('Application upgraded')
