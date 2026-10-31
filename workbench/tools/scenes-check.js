'use strict';
const fs=require('fs'), path=require('path'),vm=require('vm'),assert=require('assert');
const root=path.resolve(__dirname,'..'),s={window:{}};vm.createContext(s);
for(const name of ['data.js','release-data.js','logic.js'])vm.runInContext(fs.readFileSync(path.join(root,name),'utf8'),s);
const D=s.window.WB_DATA,L=s.window.WBLogic;let checks=0;
const check=(name,cond)=>{assert.ok(cond,name);checks++;console.log('✓ '+name);};
const unique=new Set();let count=0,missing=[],badDefault=[],badScope=[],missingShared=[];
for(const c of D.characters){
 const ids=new Set(c.portraits.map(p=>p.id)),eventIds=new Set(c.events.map(e=>e.id));
 check(c.name+'：所有图片ID唯一',ids.size===c.portraits.length);
 for(const p of c.portraits)for(const size of ['p','t'])if(!fs.existsSync(path.join(root,L.thumb(p.id,size))))missing.push(p.id);
 for(const e of c.events){
  for(const l of e.lines){
   if(l.spk==='char'){unique.add(l.key);count++;if(!ids.has(l.def))badDefault.push(e.id+'/'+l.key);}
   for(const target of l.alsoIn||[])if(eventIds.has(target)&&!c.events.find(x=>x.id===target).lines.some(x=>x.key===l.key))missingShared.push(target+'/'+l.key);
  }
  for(const list of [L.entryList({},c.key,e),L.exitList({},c.key,e)])for(const item of list)if(item.id&&!ids.has(item.id))badScope.push(e.id+'/'+item.id);
 }
}
check('所有旧图及发布版缩略图齐全',!missing.length);
check('所有本人默认图属于当前角色',!badDefault.length);
check('所有入退场默认图属于当前角色',!badScope.length);
check('共享引用已补回目标场景',!missingShared.length);
check('98场景',D.characters.reduce((n,c)=>n+c.events.length,0)===98);
check('698场景台词 / 671个不同本人key',count===698&&unique.size===671);
check('156张发布版图片',D.characters.reduce((n,c)=>n+c.portraits.filter(p=>p.release).length,0)===156);
const lun=D.characters.find(c=>c.key==='lun');
check('同图不同ID不会误判成图像差异',L.samePortrait(D,lun.portraits.find(p=>p.kind==='base').id,lun.events[0].lines[0].def));
const c=D.characters.find(c=>c.key==='eleanor'),k='dialog_eleanor9_thanks',p=c.portraits[0].id;
const map={eleanor:{lines:{[k]:p},events:{eleanor9:{enter:p}},contexts:{'eleanor9::rev':{[k]:null}}}};
const before=JSON.stringify(map),rev=c.events.find(e=>e.id==='eleanor9::rev');
check('旧全局手工标注仍可继承',L.lineValue(map,'eleanor','eleanor9::free',k)===p);
check('清除仅阻断当前场景继承',L.lineValue(map,'eleanor',rev.id,k)===null);
check('旧事件入场在拆分后仍可读取',L.entryList(map,'eleanor',rev)[0].id===p);
const out=L.buildExport(D,map,null);
check('导出包含不同服装及条件',out.version===3&&out.characters.eleanor.scenes.some(e=>e.sprite==='eleanor_rev'&&e.condition));
check('导出保留显式null覆盖',out.characters.eleanor.contexts[rev.id][k]===null);
const imported=L.prepareImport(D,{},JSON.parse(JSON.stringify(out))).map;
check('导出导入保持本场景回默认',L.lineValue(imported,'eleanor',rev.id,k)===null);
const migrated=L.migrateMap(D,map).map;
check('兼容迁移保留场景覆盖',migrated.eleanor.contexts[rev.id][k]===null);
check('统计/导出/迁移不改输入',JSON.stringify(map)===before);
let rejected=false;try{L.prepareImport(D,map,{characters:{eleanor:{events:{eleanor9:{enters:[{label:'a',id:p},{label:'a',id:p}]}}}}});}catch(e){rejected=true;}
check('重复条件名整批拒绝且输入不变',rejected&&JSON.stringify(map)===before);
console.log('通过 '+checks+' 项场景静态检查');
