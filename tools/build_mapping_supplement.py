"""Expose source-proven missing lines in the editor without assigning new artwork."""
import csv
import hashlib
import html
import json
import re
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT.parent / 'PortraitMaterialPolish/artifacts/mapping-audit-20261108'


def main():
    report = json.loads((OUT / 'mapping-audit.json').read_text(encoding='utf-8'))
    wb = json.loads((OUT / 'workbench-current.json').read_text(encoding='utf-8'))
    loc = json.loads((ROOT.parent / 'PortraitRuntime/catalogs/localization.zh.json').read_text(encoding='utf-8-sig'))
    texts = {x['key']: x['text'] for x in loc if x['table'] == 'Dialogue'}
    names = {c['key']: c['name'] for c in wb['characters']}
    pack = json.loads((ROOT / 'packs/core/pack.json').read_text(encoding='utf-8'))
    build = report['gameEvidence']['build']
    if hashlib.sha256(Path('D:/SteamLibrary/steamapps/common/Probably Stolen/Probably Stolen_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest() != report['gameEvidence']['assemblySha256']:
        raise ValueError('Source evidence is stale; re-audit current game first')
    grouped = defaultdict(list)
    rows = []
    for row in report['sourceKeysNotInWorkbench']:
        if row['alreadyInWorkbench'] or row['kind'] == 'already-shown-as-other-speaker':
            continue
        row = {**row, 'text': texts.get(row['key'], '当前缓存没有中文；需要按源码/游戏补录'), 'textSource': 'cached-localization-2026-11-01' if row['key'] in texts else 'missing'}
        rows.append(row)
        if row['kind'] == 'character':
            grouped[(row['character'], row['factory'])].append(row)
    additions = []
    for (ck, factory), entries in grouped.items():
        info = report['factoryInfo'][factory]
        # Retain source order rather than alphabetic numbering (1,10,2).
        source = (OUT / info['source']).read_text(encoding='utf-8-sig')
        entries.sort(key=lambda r: source.find('"' + r['key'] + '"'))
        sprite = info['sprites'][0] if len(set(info['sprites'])) == 1 else None
        lines = []
        for r in entries:
            path = pack['characters'][ck]['story'].get(r['key'])
            image_id = next((p for p, rel in wb.get('releasePaths', {}).items() if 'portraits/story/' + rel == path), None)
            lines.append({'key': r['key'], 'zh': r['text'], 'en': '', 'spk': 'char', 'main': True,
                          'def': image_id, 'label': '', 'alsoIn': [], 'condHuman': '源码确认角色说话；回调触发条件需结合原函数核对'})
        additions.append({'character': ck, 'event': {'id': 'source|' + factory, 'sourceEvent': '@' + factory,
            'title': names[ck] + ' · 源码补漏 · ' + factory.split('.')[-1], 'stage': '源码补漏 · 待人工核对',
            'sprite': sprite, 'foreign': False, 'noId': False, 'canArrest': False, 'lines': lines,
            'defEnter': None, 'defExits': [], 'runtimeFactories': [factory],
            'routeCondition': '此卡收集当前源码中的遗漏引用，包含回调，不代表这些台词连续发生；中文来自旧缓存，可能缺失。'}})
    payload = {'build': build, 'assemblySha256': report['gameEvidence']['assemblySha256'],
               'routes': report['sceneRoutes'], 'additions': additions, 'newSourceLines': sum(len(x['event']['lines']) for x in additions),
               'pendingSpeakerRows': sum(x['kind'] == 'speaker-unconfirmed' for x in rows), 'baseMappings': report['explicitBaseMappings']}
    script = 'window.WB_SOURCE = ' + json.dumps(payload, ensure_ascii=False) + ';\n' + """
(function(S,D){
  D.characters.forEach(function(c){
    c.events.forEach(function(e){var route=(S.routes[c.key]||{})[e.id]; if(route){e.sourceRouteEvidence=route;if(route.unique)e.runtimeFactories=route.factories;}});
    S.additions.filter(function(a){return a.character===c.key;}).forEach(function(a){if(!c.events.some(function(e){return e.id===a.event.id;}))c.events.push(a.event);});
    var refs={};c.events.forEach(function(e){if(!e.foreign)e.lines.forEach(function(l){if(l.spk==='char')(refs[l.key]||(refs[l.key]=[])).push(e.id);});});
    c.sharedKeys=c.sharedKeys||{};Object.keys(refs).forEach(function(k){if(refs[k].length>1)c.sharedKeys[k]=refs[k];});
  });
})(window.WB_SOURCE,window.WB_DATA);
"""
    (ROOT / 'workbench/source-data.js').write_text(script, encoding='utf-8')
    with (OUT / '源码补漏待核对.csv').open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=['character','factory','key','speaker','kind','hasGlobalMapping','alreadyInWorkbench','source','line','text','textSource'])
        writer.writeheader();writer.writerows(rows)
    summary = f"旧台词清单内缺图 {report['summary']['lineGaps']} 项；旧导入槽位未绑定 {report['summary']['slotGaps']} 项；当前源码补充 {payload['newSourceLines']} 个场景台词引用。"
    h = ['<!doctype html><meta charset="utf-8"><title>立绘映射复核</title><style>body{font:15px/1.7 system-ui;background:#17202b;color:#eee;padding:28px}table{border-collapse:collapse;width:100%}td,th{border:1px solid #475465;padding:8px;text-align:left;vertical-align:top}code{overflow-wrap:anywhere}a{color:#9bd7ff}</style>',
         '<h1>立绘映射复核 · Build '+html.escape(build)+'</h1><p>'+html.escape(summary)+'</p>',
         '<p>源码确定由角色说出的遗漏项已补到工作台「源码补漏」分组，尚未分配新图；说话人未确认项只列在此表。旧缓存中文不能代替新版游戏文本。</p>',
         '<p><a href="file:///D:/AI/probably-waifu/workbench/index.html">打开立绘工作台</a></p><table><tr><th>角色 / 工厂</th><th>台词与键</th><th>当前状态</th><th>源码证据</th></tr>']
    for r in rows:
        h.append('<tr><td>'+html.escape(names[r['character']]+' / '+r['factory'])+'</td><td>'+html.escape(r['text'])+'<br><code>'+html.escape(r['key'])+'</code></td><td>'+html.escape(('已有全局映射' if r['hasGlobalMapping'] else '缺少全局专属映射')+'；'+r['kind'])+'</td><td>'+html.escape(r['source']+':'+str(r['line']))+'</td></tr>')
    h.append('</table><h2>仍待确认的旧入退场标注</h2><p>这些旧场景无法唯一对应到实际工厂，或其台词当前未在关联源码中找到。未强行套用图片。</p><table><tr><th>角色</th><th>旧场景</th><th>槽位</th><th>候选来源</th></tr>')
    for r in report['missingAuthorSlots']:
        h.append('<tr><td>'+html.escape(r['name'])+'</td><td>'+html.escape(r['scene'])+'</td><td>'+html.escape(r['slot'])+'</td><td>'+html.escape(' / '.join(r['candidates']) or '尚未找到唯一来源')+'</td></tr>')
    h.append('</table><h2>已有映射但明确指向基础图</h2><p>以下不等于错误，也不自动更换。请由作者确认是否应使用差分；保留上一张的修复不会覆盖这些明确映射。</p><table><tr><th>角色 / 场景</th><th>台词与键</th><th>图片</th></tr>')
    for r in report['explicitBaseMappings']:
        h.append('<tr><td>'+html.escape(r['name']+' / '+r['scene'])+'</td><td>'+html.escape(r['text'] or '')+'<br><code>'+html.escape(r['key'])+'</code></td><td>'+html.escape(r['path'])+'</td></tr>')
    h.append('</table>')
    (OUT / '映射缺项复核.html').write_text('\n'.join(h),encoding='utf-8')
    print(json.dumps({'addedScenes':len(additions),'addedLineOccurrences':payload['newSourceLines'],'pendingSpeakerOccurrences':payload['pendingSpeakerRows'],'rows':len(rows)},ensure_ascii=False))


if __name__ == '__main__':
    main()
