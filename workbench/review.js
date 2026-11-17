/* 人工审阅：候选预览与确认分离；审阅记录独立于游戏配图方案。 */
(function () {
  'use strict';
  var W = window.__WB__, D = W.D, L = W.logic, R = window.WB_RUNTIME;
  var STORE = 'dsh-portrait-review-v1', reviews = {}, blocked = false, rawBroken = null;
  var c, ev, items = [], index = 0, candidate = null, bg = 'checker', filter = '';
  var issues = ['台词不匹配','差分不一致','裁切边缘','结束回退'];
  var $ = function (id) { return document.getElementById(id); };
  var esc = function (s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function(x) { return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[x]; }); };
  try { var raw = localStorage.getItem(STORE); reviews = JSON.parse(raw || '{}'); if (!reviews || typeof reviews !== 'object' || Array.isArray(reviews)) throw Error('记录格式错误'); }
  catch (e) { reviews = {}; blocked = true; rawBroken = typeof raw === 'string' ? raw : null; }

  var dialog = document.createElement('dialog'); dialog.id = 'humanReview';
  dialog.setAttribute('aria-labelledby','reviewTitle');
  dialog.innerHTML = '<header class="audit-head"><h2 id="reviewTitle">人工逐项审阅</h2><select id="auditCharacter" aria-label="审阅角色"></select><select id="auditScene" aria-label="审阅场景"></select><button class="btn" id="auditExport">导出审阅记录</button><label class="btn" for="auditImport">导入审阅记录</label><input id="auditImport" type="file" accept=".json" hidden><button class="btn" id="auditClose">关闭</button></header>' +
    '<div class="audit-toolbar"><button class="btn" id="auditPrev">上一项</button><select id="auditItem" aria-label="审阅条目"></select><button class="btn" id="auditNext">下一项</button><span id="auditPosition"></span><label>预览底色 <select id="auditBackground"><option value="checker">棋盘格</option><option value="dark">深色</option><option value="light">浅色</option><option value="green">绿色</option></select></label></div>' +
    '<div class="audit-grid"><section class="audit-context"><h3>原台词与场景</h3><div id="auditContext"></div><hr><label>核对结果<select id="auditStatus"><option value="pending">待核对</option><option value="confirmed">已核对</option><option value="fix">待修复</option></select></label><fieldset id="auditIssues"><legend>问题标记</legend>' + issues.map(function(x,i) {return '<label><input type="checkbox" value="'+i+'"> '+x+'</label>';}).join('') + '</fieldset><label>备注<textarea id="auditNote" rows="4" placeholder="例如：手套消失、左侧裁断，或这一句应更严肃"></textarea></label><p id="auditSave" role="status" aria-live="polite"></p></section>' +
    '<section class="audit-images"><div id="auditComparison"></div><p id="auditPath" class="audit-path"></p><p id="auditMessage" role="status" aria-live="polite"></p><button class="btn primary" id="auditApply">确认只修改当前项</button><button class="btn" id="auditFull">打开候选图片</button></section>' +
    '<section class="audit-candidates"><h3>候选立绘</h3><input type="search" id="auditSearch" aria-label="筛选候选立绘" placeholder="搜索名称或图组"><div id="auditGallery"></div></section></div>';
  document.body.appendChild(dialog);
  var gapButton=document.createElement('button');gapButton.className='btn';gapButton.id='auditNextGap';gapButton.textContent='下一个映射待核项';
  dialog.querySelector('.audit-toolbar').appendChild(gapButton);

  function key() { var t=items[index]; return JSON.stringify([c.key,ev.id,t.kind,t.key || t.label,t.idx || 0]); }
  function record() { return reviews[key()] || {}; }
  function message(text) { $('auditMessage').textContent = text; }
  function saveRecord() {
    if (blocked) { $('auditSave').textContent = '原记录读取异常，已禁止覆盖；请先导出备份。'; return false; }
    var previous = record(), t=items[index];
    var value = { character:c.key, scene:ev.id, sourceEvent:ev.sourceEvent || ev.id, sprite:ev.sprite,
      kind:t.kind, key:t.key || null, label:t.label, index:t.idx, text:t.line ? t.line.zh : '',
      status:$('auditStatus').value, issues:Array.from($('auditIssues').querySelectorAll('input:checked')).map(function(n) {return issues[+n.value];}),
      note:$('auditNote').value, expectedPortrait:previous.expectedPortrait || null, assignment:previous.assignment || null, updated:new Date().toISOString() };
    try { var next=Object.assign({},reviews); next[key()]=value; localStorage.setItem(STORE,JSON.stringify(next)); reviews=next; $('auditSave').textContent='审阅记录已保存'; return true; }
    catch(e) { $('auditSave').textContent='保存失败，请导出记录后再离开'; reviews[key()]=value; return false; }
  }
  function targets() {
    items=[];
    if (!ev || ev.foreign) return;
    L.entryList(W.map,c.key,ev).forEach(function(x,i) {items.push({kind:'enter',idx:i,label:'入场 · '+x.label,def:x.id});});
    (ev.lines || []).forEach(function(l) { if(l.spk==='char') items.push({kind:'line',key:l.key,line:l,label:l.zh || l.key,def:l.def}); });
    items.push({kind:'end',label:'对话结束后的停留图（问题记录）'});
    L.exitList(W.map,c.key,ev).forEach(function(x,i) {items.push({kind:'exit',idx:i,label:'退场 · '+x.label,def:x.id});});
  }
  function sceneNames(def) {
    var event=ev.sourceEvent || ev.id, list=(def.identifiers || []).filter(function(x) {return x.toLowerCase()===event.toLowerCase();});
    (def.factories || []).forEach(function(x) { if(x.split('.').pop().replace(/^Create/,'').toLowerCase()===event.replace(/_/g,'').toLowerCase()) list.push('@'+x); });
    (ev.runtimeFactories || []).forEach(function(f) {if((def.factories || []).includes(f)) list.push('@'+f);});
    return Array.from(new Set(list));
  }
  function runtime(t) {
    var def=R && R.characters[c.key]; if(!def) return {path:null,via:'未提供运行包快照'};
    if(t.kind==='end') return {path:null,via:'应延续上一张立绘；此项记录你的期望，不写入台词映射。尚未映射的新台词不代表恢复默认图'};
    var names=sceneNames(def), slots=t.kind==='line' ? ['line:'+t.key] : t.kind==='enter' ? ['entry'] :
      /被捕/.test(t.label) ? ['arrest'] : /自然退场|默认退场/.test(t.label) ? ['exit'] : [];
    // 与内容包相同的结构查找；无法识别的自定义条件不推测。
    for(var i=0;i<slots.length;i++) for(var j=0;j<names.length;j++) {
      var sprites=[ev.sprite || '', ''];
      for(var k=0;k<sprites.length;k++) { var name='workbench|'+c.key+'|'+names[j]+'|'+sprites[k]+'|'+slots[i];
        if((def.variants || {})[name]) return {path:def.variants[name],via:name}; }
    }
    if(t.kind==='line' && (def.story || {})[t.key]) return {path:def.story[t.key],via:'story:'+t.key};
    if(t.kind==='exit' && !slots.length) return {path:null,via:'自定义退场条件未静态解析，请按游戏分支实测'};
    return {path:(def.variants || {})[ev.sprite] || def.base,via:'无专属映射，回到角色默认图（静态预览，不包含游戏即时状态）'};
  }
  function idForPath(path) { return c.portraits.find(function(p) { return 'portraits/story/'+(D.releasePaths || {})[p.id]===path; }); }
  function src(id) { return L.thumb(id,'p'); }
  function originalSrc(id) {
    var rel=(D.releasePaths || {})[id];
    return rel ? '../packs/core/portraits/story/'+rel : src(id);
  }
  function figure(title,url,caption) { return '<figure><figcaption>'+esc(title)+'</figcaption><div class="audit-canvas '+bg+'">'+(url ? '<img src="'+esc(url)+'" alt="'+esc(title)+'">' : '<span>无明确配图</span>')+'</div><small>'+esc(caption)+'</small></figure>'; }
  function activeId() {
    var t=items[index], r=runtime(t), runtimeImage=idForPath(r.path);
    if(t.kind==='line') return L.lineValue(W.map,c.key,ev.id,t.key) || (runtimeImage && runtimeImage.id) || t.def;
    if(t.kind==='end') return record().expectedPortrait;
    var stored=((W.map[c.key] || {}).events || {})[ev.id] || ((W.map[c.key] || {}).events || {})[ev.sourceEvent];
    return stored ? t.def : (runtimeImage && runtimeImage.id) || t.def;
  }
  function pictures() {
    var t=items[index], r=runtime(t), own=activeId();
    $('auditComparison').innerHTML=figure('当前本地运行包',r.path ? '../packs/core/'+r.path : null,r.via)+
      figure('工作方案 · 当前项',own ? originalSrc(own) : null,own || '未指定')+
      figure('候选图 · 尚未确认',candidate ? originalSrc(candidate) : null,candidate || '选择右侧立绘进行对照');
    $('auditPath').textContent='来源：'+(R ? R.source+' · SHA256 '+R.sha256.slice(0,12)+' · 快照 '+R.generated : '无快照');
    $('auditApply').textContent=t.kind==='end' ? '记录结束后的期望图' : '确认只修改当前项';
    $('auditApply').disabled=!candidate || blocked; $('auditFull').disabled=!candidate;
    dialog.querySelectorAll('.audit-canvas img').forEach(function(img) { img.onerror=function() { this.replaceWith(document.createTextNode('图片未找到：'+this.getAttribute('src'))); }; });
  }
  function gallery() {
    $('auditGallery').innerHTML=c.portraits.filter(function(p) {return (p.label+' '+p.sheet).toLowerCase().includes(filter.toLowerCase());}).map(function(p) {
      return '<button class="audit-portrait'+(candidate===p.id?' chosen':'')+'" data-portrait="'+esc(p.id)+'" aria-pressed="'+(candidate===p.id)+'"><img loading="lazy" src="'+esc(src(p.id))+'" alt=""><span>'+esc(p.label)+'</span></button>';
    }).join('');
  }
  function render() {
    var t=items[index]; if(!t) return;
    candidate=null;
    $('auditItem').innerHTML=items.map(function(x,i) {return '<option value="'+i+'">'+esc(x.label.length>62 ? x.label.slice(0,62)+'…' : x.label)+'</option>';}).join('');
    $('auditItem').value=String(index); $('auditPrev').disabled=index===0; $('auditNext').disabled=index===items.length-1;
    $('auditPosition').textContent=(index+1)+' / '+items.length;
    var group=t.line && L.branchesOf(ev).find(function(g) {return g.lines.some(function(l) {return l.key===t.key;});});
    var groupLines=group ? group.lines : [], pos=groupLines.findIndex(function(l) {return l.key===t.key;});
    $('auditContext').innerHTML='<b>'+esc(c.name)+' · '+esc(ev.stage || '无固定阶段')+'</b><p class="audit-path">场景：'+esc(ev.id)+'<br>原版精灵：'+esc(ev.sprite || '无')+'</p>'+
      (t.line ? '<p>分组：'+esc(group ? group.label : '原始清单')+'</p><small>相邻项只表示同组清单顺序，分支不一定连续发生。</small>'+groupLines.slice(Math.max(0,pos-1),pos+2).map(function(l) {return '<blockquote class="'+(l.key===t.key?'current':'')+'">'+esc(l.zh || l.key)+'</blockquote>';}).join('')+'<code>'+esc(t.key)+'</code><p>'+esc(t.line.en || '')+'</p><p>条件：'+esc(t.line.condHuman || t.line.cond || ev.routeCondition || '未提供')+'</p>' : '<p>'+esc(t.label)+'</p>')+
      (t.kind==='exit' ? '<p>退场条件沿用原有槽位。自定义名称不自动生成游戏判定。</p>' : '')+
      '<p class="warning">这是已有解包台词清单及源码补漏引用；清单结束不代表应切回默认立绘。</p>'+
      (t.key && window.WB_SOURCE && WB_SOURCE.baseMappings.some(function(r){return r.character===c.key && r.scene===ev.id && r.key===t.key;}) ? '<p class="warning">此句当前有明确映射，但指向基础图。请确认是否应改用差分，不会自动替你更换。</p>' : '')+
      (ev.sourceRouteEvidence && !ev.sourceRouteEvidence.unique ? '<p class="warning">旧场景与实际事件尚未唯一对应；请勿把旧入退场标注当作已生效。</p>' : '');
    var r=record(); $('auditStatus').value=r.status || 'pending'; $('auditNote').value=r.note || '';
    $('auditIssues').querySelectorAll('input').forEach(function(n) {n.checked=(r.issues || []).includes(issues[+n.value]);});
    $('auditSave').textContent=blocked ? '审阅存储异常，原数据已保护；可导出备份。' : r.updated ? '上次保存 '+new Date(r.updated).toLocaleString('zh-CN') : '尚未记录';
    message('候选图只预览，点击确认后才修改当前项。'); pictures(); gallery();
  }
  function scenes(preferred) {
    var list=c.events.filter(function(x) {return !x.foreign;});
    $('auditScene').innerHTML=list.map(function(x) {return '<option value="'+esc(x.id)+'">'+esc((x.stage || '')+' · '+(x.title || x.id))+'</option>';}).join('');
    ev=list.find(function(x) {return x.id===preferred;}) || list[0];
    if(!ev) { message('此角色没有可审阅的独立剧情场景'); return; }
    $('auditScene').value=ev.id; targets(); index=0; render();
  }
  $('btnReview').onclick=function() {
    var current=W.reviewSelection();
    c=D.characters.find(function(x) {return x.key===current.character && x.events.some(function(e) {return !e.foreign;});}) || D.characters.find(function(x) {return x.events.some(function(e) {return !e.foreign;});});
    if(!c) return;
    $('auditCharacter').innerHTML=D.characters.filter(function(x) {return x.events.some(function(e) {return !e.foreign;});}).map(function(x) {return '<option value="'+esc(x.key)+'">'+esc(x.name)+'</option>';}).join('');
    $('auditCharacter').value=c.key; window.WBReviewOpen=true; $('preview').hidden=true;
    scenes(current.selection && current.selection.ev);
    if(current.selection) { var s=current.selection, at=items.findIndex(function(t) {return s.slot ? t.kind===s.slot && t.idx===(s.idx || 0) : t.key===(s.lks || [])[0];}); if(at>=0) {index=at;render();} }
    dialog.showModal();
  };
  $('auditCharacter').onchange=function() { c=D.characters.find(function(x) {return x.key===$('auditCharacter').value;}); filter='';$('auditSearch').value='';scenes(); };
  $('auditScene').onchange=function() { scenes($('auditScene').value); };
  $('auditItem').onchange=function() {index=+$('auditItem').value;render();};
  $('auditPrev').onclick=function() {if(index>0) {index--;render();}};
  $('auditNext').onclick=function() {if(index<items.length-1) {index++;render();}};
  gapButton.onclick=function() {
    var S=window.WB_SOURCE;if(!S)return message('没有源码核对清单');
    var list=S.baseMappings.map(function(r){return {ck:r.character,scene:r.scene,key:r.key};});
    S.additions.forEach(function(a){a.event.lines.forEach(function(l){list.push({ck:a.character,scene:a.event.id,key:l.key});});});
    list=list.filter(function(r){var saved=reviews[JSON.stringify([r.ck,r.scene,'line',r.key,0])];return !saved || saved.status!=='confirmed';});
    if(!list.length)return message('当前清单无待核项；说话人未确认和旧场景未绑定项仍请查看缺项清单。');
    var at=list.findIndex(function(r){return r.ck===c.key && r.scene===ev.id && r.key===items[index].key;});
    var next=list[(at+1)%list.length];c=D.characters.find(function(x){return x.key===next.ck;});$('auditCharacter').value=c.key;
    scenes(next.scene);index=items.findIndex(function(t){return t.key===next.key;});if(index<0)index=0;render();
    message('待核项 '+((at+1)%list.length+1)+' / '+list.length+'。基础图不一定有错，请人工判断。');
  };
  $('auditClose').onclick=function() {dialog.close();}; dialog.addEventListener('close',function() {window.WBReviewOpen=false;});
  dialog.addEventListener('keydown',function(e) { if(e.key==='Escape') e.stopPropagation(); });
  $('auditBackground').onchange=function() {bg=this.value;pictures();};
  $('auditSearch').oninput=function() {filter=this.value;gallery();};
  $('auditGallery').onclick=function(e) {var b=e.target.closest('[data-portrait]'); if(!b) return;candidate=b.dataset.portrait;pictures();gallery();message('候选图尚未写入配图方案。');};
  $('auditStatus').onchange=saveRecord; $('auditIssues').onchange=saveRecord; $('auditNote').oninput=saveRecord;
  $('auditApply').onclick=function() {
    if(!candidate) return;
    var t=items[index];
    if(t.kind==='end') { if(!saveRecord()) return; reviews[key()].expectedPortrait=candidate; try {localStorage.setItem(STORE,JSON.stringify(reviews));message('期望图已记录；尚未改变游戏结束判定。');} catch(e) {message('期望图保存失败，请导出记录');} return; }
    // Keep the existing editor on this character; only this exact scene/line/slot is assigned.
    if(W.reviewSelection().character!==c.key) $('charList').querySelector('[data-ck="'+c.key+'"]').click();
    if(W.assignReviewed(c.key,ev.id,t.kind==='line'?{key:t.key}:{slot:t.kind,idx:t.idx},candidate)) {
      if(saveRecord()) {reviews[key()].assignment=candidate;try {localStorage.setItem(STORE,JSON.stringify(reviews));} catch(e) {message('配图已保存，但修改记录保存失败；请先导出');return;}}
      var keep=candidate;targets();candidate=keep;pictures();message('已保存当前项，未跳句；完成后请导出审阅记录，内含本轮明确确认的修改。');
    } else message('未能持久保存，请先导出配图方案并检查本地存储。');
  };
  $('auditFull').onclick=function() {if(candidate) window.open(originalSrc(candidate),'_blank','noopener');};
  function download(value,name) {var url=URL.createObjectURL(new Blob([JSON.stringify(value,null,2)],{type:'application/json'})),a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(function() {URL.revokeObjectURL(url);},2000);}
  $('auditExport').onclick=function() {
    var edits=[],inactive=[];
    Object.keys(reviews).forEach(function(k) {
      var r=reviews[k];if(!r.assignment || r.kind==='end')return;
      var ch=D.characters.find(function(x){return x.key===r.character;}), scene=ch && ch.events.find(function(x){return x.id===r.scene;});
      if(!scene || !ch.portraits.some(function(p){return p.id===r.assignment;})) {inactive.push(k);return;}
      var current=r.kind==='line' ? L.lineValue(W.map,r.character,r.scene,r.key) :
        ((r.kind==='enter' ? L.entryList(W.map,r.character,scene) : L.exitList(W.map,r.character,scene))[r.index] || {}).id;
      if(current!==r.assignment) {inactive.push(k);return;}
      edits.push({character:r.character,scene:r.scene,event:r.sourceEvent,sprite:r.sprite,kind:r.kind,key:r.key,index:r.index,label:r.label,portrait:r.assignment});
    });
    download({format:'dsh-human-review',version:1,created:new Date().toISOString(),source:R ? {path:R.source,sha256:R.sha256,generated:R.generated}:null,
      records:reviews,explicitEdits:edits,inactiveEditRecords:inactive,manualMap:W.map,unreadableOriginal:rawBroken},'立绘审阅记录_'+new Date().toISOString().slice(0,10)+'.json');
  };
  $('auditImport').onchange=function() {
    var f=this.files[0];this.value='';if(!f)return;if(f.size>20*1024*1024) {message('文件超过 20 MB');return;}
    var reader=new FileReader();reader.onload=function() {try {
      var j=JSON.parse(reader.result);if(blocked)throw Error('原存储异常，请先导出备份');
      if(j.format!=='dsh-human-review'||j.version!==1||!j.records||typeof j.records!=='object'||Array.isArray(j.records))throw Error('不是审阅记录文件');
      Object.keys(j.records).forEach(function(k) {
        var r=j.records[k], parts=JSON.parse(k), ch=D.characters.find(function(x){return x.key===r.character;}), scene=ch && ch.events.find(function(x){return x.id===r.scene;});
        if(!Array.isArray(parts)||parts.length!==5||!scene||!['pending','confirmed','fix'].includes(r.status)||!Array.isArray(r.issues)||r.issues.some(function(x){return !issues.includes(x);})||typeof r.note!=='string'||parts[0]!==r.character||parts[1]!==r.scene||!['line','enter','exit','end'].includes(r.kind)||parts[2]!==r.kind)throw Error('记录格式或场景不匹配');
        if(parts[3] !== (r.key || r.label) || parts[4] !== (r.index || 0))throw Error('记录标识不匹配');
        if(r.kind==='line' && !scene.lines.some(function(l){return l.key===r.key && l.spk==='char';}))throw Error('台词不属于该场景');
        if((r.kind==='enter'||r.kind==='exit') && (!Number.isInteger(r.index)||r.index<0))throw Error('槽位索引无效');
        if((r.expectedPortrait && !ch.portraits.some(function(p){return p.id===r.expectedPortrait;})) || (r.assignment && !ch.portraits.some(function(p){return p.id===r.assignment;})))throw Error('立绘不属于该角色');
      });
      if(!confirm('合并 '+Object.keys(j.records).length+' 项审阅记录？同名记录覆盖，台词配图不会改变。'))return;
      var next=Object.assign({},reviews,j.records);localStorage.setItem(STORE+'-before-import',JSON.stringify(reviews));localStorage.setItem(STORE,JSON.stringify(next));reviews=next;render();message('审阅记录已合并，配图方案未修改。');
    }catch(e){message('导入未执行：'+e.message);}};reader.onerror=function(){message('文件读取失败');};reader.readAsText(f,'utf-8');
  };
})();
