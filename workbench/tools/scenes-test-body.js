(async () => {
  const W = window.__WB__, D = W.D, L = W.logic, steps = [];
  const check = (name, cond, extra) => steps.push({name,ok:!!cond,extra});
  const sleep = ms => new Promise(r=>setTimeout(r,ms));
  const selectChar = ck => document.querySelector('.char[data-ck="'+ck+'"]').click();
  const choose = (ev,key) => {
    const el=Array.from(document.querySelectorAll('.chip')).find(n=>n.dataset.ev===ev&&n.dataset.lk===key);
    if(!el) throw Error('Missing chip '+ev+'/'+key);
    el.dispatchEvent(new MouseEvent('mousedown',{bubbles:true,button:0})); window.dispatchEvent(new MouseEvent('mouseup'));
  };
  const picture = id => Array.from(document.querySelectorAll('.por')).find(n=>n.dataset.id===id).click();
  const initial=localStorage.getItem('dsh-portrait-map-v1');
  await sleep(280);
  check('打开页面不改写既有手工存储',localStorage.getItem('dsh-portrait-map-v1')===initial);
  check('旧全局标注完整载入',Object.keys(W.map.lun.lines).length===1);
  check('发布版素材156张',D.audit.releaseImages===156,D.audit);
  check('17角色都有剧情差分',D.characters.every(c=>c.portraits.some(p=>p.release&&!p.label.startsWith('base'))));
  const lun=D.characters.find(c=>c.key==='lun');
  check('健康巡逻不再默认受伤图',lun.events.find(e=>e.id==='officerLunVisit').lines.filter(l=>l.spk==='char').every(l=>l.def.endsWith('base__lun.png')));
  check('复用台词回到真实事件',lun.events.find(e=>e.id==='officerLunInspection').lines.some(l=>l.key==='dialog_lun_duty_accept'));
  const weekly=D.characters.find(c=>c.key==='john').events.find(e=>e.id==='wanted8_weekly');
  check('约翰每周回访补全本人及治安人员台词',weekly&&weekly.lines.length===4&&weekly.lines.filter(l=>l.spk==='char').length===3);
  const serp=D.characters.find(c=>c.key==='Serpico'), ele=D.characters.find(c=>c.key==='eleanor');
  const routes=serp.events.filter(e=>e.sourceEvent==='serpicoConclusion');
  check('塞尔皮科结局分成三条',routes.length===3);
  check('三路线共用奖励与告别均补全',routes.every(e=>['5','accept','done'].every(s=>e.lines.some(l=>l.key==='dialog_serpico_end_bm_'+s))));
  check('三路线服装分离',new Set(routes.map(e=>e.sprite)).size===3);
  check('治安部路线不混入黑市开场',!routes.find(e=>e.sprite==='serpico').lines.some(l=>l.key==='dialog_serpico_end_bm_1'));
  const eroutes=ele.events.filter(e=>e.sourceEvent==='eleanor9');
  check('埃莉诺结局分成两条',eroutes.length===2);
  check('革命军路线补回共用道谢',eroutes.find(e=>e.sprite==='eleanor_rev').lines.some(l=>l.key==='dialog_eleanor9_thanks'));
  check('革命军共用告别默认绿制服',eroutes.find(e=>e.sprite==='eleanor_rev').lines.filter(l=>/farewell_/.test(l.key)).every(l=>l.def.includes('革命军沉痛告别')));
  selectChar('eleanor');
  const k='dialog_eleanor9_thanks', free='eleanor9::free', rev='eleanor9::rev';
  const p1=ele.portraits.find(p=>p.release&&p.label==='05_团聚释然').id;
  const p2=ele.portraits.find(p=>p.release&&p.label==='06_革命军沉痛告别').id;
  choose(free,k); picture(p1); choose(rev,k); picture(p2);
  check('同一句两条路线独立指定',L.lineValue(W.map,'eleanor',free,k)===p1&&L.lineValue(W.map,'eleanor',rev,k)===p2);
  check('场景修改不污染旧全局标注',!W.map.eleanor.lines[k]&&W.map.lun.lines.dialog_lun_visit_1);
  document.querySelector('#btnUndo').click();
  check('撤销只回退最近路线',L.lineValue(W.map,'eleanor',free,k)===p1&&!L.lineValue(W.map,'eleanor',rev,k));
  document.querySelector('#btnRedo').click();
  check('重做恢复路线覆盖',L.lineValue(W.map,'eleanor',rev,k)===p2);
  const out=L.buildExport(D,W.map,null);
  const scenes=out.characters.eleanor.scenes;
  check('完整导出保留路线区别',out.version===3&&scenes.find(e=>e.id===free).lines[k]===p1&&scenes.find(e=>e.id===rev).lines[k]===p2);
  const imported=L.prepareImport(D,{},out).map;
  check('导出再导入不合并不同路线',L.lineValue(imported,'eleanor',free,k)===p1&&L.lineValue(imported,'eleanor',rev,k)===p2);
  const before=JSON.stringify(W.map);
  for(const [name,doc] of [
    ['拒绝运行时文件',{lines:{a:'bad'}}],
    ['拒绝错角色图片',{characters:{eleanor:{lines:{[k]:lun.portraits[0].id}}}}],
    ['拒绝错误台词归属',{characters:{eleanor:{lines:{dialog_lun_visit_1:p1}}}}],
    ['拒绝跨路线台词',{characters:{eleanor:{contexts:{[free]:{dialog_eleanor9_rev_1:p2}}}}}]
  ]) {let rejected=false;try{L.prepareImport(D,W.map,doc);}catch(e){rejected=true;}check(name,rejected&&JSON.stringify(W.map)===before);}
  const backup=L.prepareImport(D,{}, {format:'dsh-workbench-backup',map:W.map}).map;
  check('手工备份往返不混入默认值',JSON.stringify(backup)===JSON.stringify(W.map));
  choose(rev,k);
  check('详情显示场景与服装',document.querySelector('#selectionInfo').textContent.includes('eleanor_rev'));
  const search=document.querySelector('#dialogSearch');search.value='dialog_eleanor9_rev_2';search.dispatchEvent(new Event('input',{bubbles:true}));
  check('按钩子搜索只留下对应路线',document.querySelectorAll('.event').length===1&&document.querySelector('.event').textContent.includes('母亲遇害'));
  check('筛选后清除隐藏选区',!document.querySelector('.chip.sel'));
  search.value='完全不存在的钩子';search.dispatchEvent(new Event('input',{bubbles:true}));
  check('无结果有恢复提示',document.querySelector('#events').textContent.includes('没有符合条件'));
  document.querySelector('#resetFilters').click();
  const button=document.querySelector('[data-select-group]');button.click();
  check('分支按钮只选择本分支',document.querySelectorAll('.chip.sel').length>0&&new Set(Array.from(document.querySelectorAll('.chip.sel')).map(x=>x.dataset.g)).size===1);
  const por=document.querySelector('.por');por.dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,clientX:200,clientY:200}));
  check('不再允许跨角色套同一张图',!document.querySelector('#ctx [data-act="all"]'));
  const previous=JSON.stringify(W.map), target=document.querySelector('.chip:not(.ghost)'), dt=new DataTransfer();dt.setData('text/plain',lun.portraits[0].id);
  target.dispatchEvent(new DragEvent('drop',{bubbles:true,cancelable:true,dataTransfer:dt}));
  check('拒绝外部或错角色拖入',JSON.stringify(W.map)===previous);
  // 浏览器写入失败必须可见，随后恢复方法重试。
  const native=Storage.prototype.setItem;Storage.prototype.setItem=function(){throw Error('test quota');};
  choose(rev,k);picture(p2); W.flushSave();
  check('保存失败明确可见',document.querySelector('#saveStatus').classList.contains('error'));
  Storage.prototype.setItem=native;W.flushSave();
  check('重试保存成功',!document.querySelector('#saveStatus').classList.contains('error'));
  check('持久化保留双路线覆盖',JSON.parse(localStorage.getItem('dsh-portrait-map-v1')).eleanor.contexts[free][k]===p1);
  sessionStorage.setItem('wb-test-expected',JSON.stringify(W.map));
  document.querySelector('#resetFilters').click();
  const stage=document.querySelector('#stageFilter');stage.value='母亲遇害 · 加入革命军';stage.dispatchEvent(new Event('change',{bubbles:true}));
  choose(rev,k);document.querySelector('#preview').hidden=true;
  return {steps};
})()
