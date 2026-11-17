"""Build the V3 beta from the approved lossless HJP collection, preserving authored story maps."""
raise SystemExit("Disabled experimental builder: it rewrites HJP blocks. Use an original-byte-preserving HJP build instead.")
import io
import json
import shutil
import sys
from pathlib import Path
import numpy as np
from PIL import Image

sys.path.insert(0, r'D:\Document\bim\portrait-pack')
from hjp import Hjp, write_hjp, L_COLOR, L_ALPHA, C_PNG

ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path(r'D:\Probably Stolen\成品素材_无损')
COLLECTION = SOURCE.parent / '02_美术素材' / '立绘大合集_按角色_2026-11-15'
OUT = ROOT / 'artifacts' / 'v3-content'
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))
def write(p, d):
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(d, ensure_ascii=False, indent=2), encoding='utf-8')

index = read(COLLECTION / '总索引.json')
ids = read(SOURCE / 'expr_ids.snapshot.json')
manifests = {p.parent.name: read(p) for p in (ROOT / 'packs').glob('*/pack.json')}
layout = read(ROOT / 'layout_profiles.json')
mapped = {}
report = []
for role in index['roles_detail']:
    rid = role['id']
    pack_id = next((k for k, d in manifests.items() if rid in d['characters']), 'visitors-v2')
    manifest = manifests[pack_id]
    character = manifest['characters'].setdefault(rid, {'identifiers': [rid], 'variants': {}, 'story': {}})
    container = 'hjp/' + rid + '.base.hjp'
    dest = OUT / 'packs' / pack_id / container
    dest.parent.mkdir(parents=True, exist_ok=True)
    source = Hjp(SOURCE / (rid + '.base.hjp'))
    by_name = {e['name']: e for e in source.meta['expressions']}
    paths = {}
    blocks = []
    for file in role['files']:
        stem = Path(file['file']).stem
        name = '基础原图' if stem == '00_基础原图' else stem.removeprefix('差分_')
        expr = by_name[name]['id']
        color, _ = source.image(expr, L_COLOR)
        alpha = source.silhouette(expr)
        h, w = alpha.shape
        for layer, arr, mode, channels in [(L_COLOR, color, 'RGB', 3), (L_ALPHA, alpha, 'L', 1)]:
            buffer = io.BytesIO()
            Image.fromarray(arr, mode).save(buffer, format='PNG', compress_level=6)
            blocks.append(dict(expr=expr, layer=layer, slot=0, data=buffer.getvalue(), codec=C_PNG, channels=channels, shift=0, w=w, h=h))
        target = container + '#' + str(expr)
        paths[name] = target
        mapped[str(Path(file['source']).resolve()).lower()] = target
        mask = Image.fromarray(alpha).point(lambda a: 255 if a >= 26 else 0)
        bounds = mask.getbbox()
        if not bounds: raise ValueError('Empty portrait ' + rid + '/' + name)
        layout['Assets'][pack_id + '|' + target] = dict(Width=w, Height=h, CenterX=(bounds[0]+bounds[2])/2,
            BottomY=h-bounds[3], FamilyWidth=bounds[2]-bounds[0], FamilyHeight=bounds[3]-bounds[1])
    if not dest.exists(): write_hjp(str(dest), source.meta, blocks, [])
    original = ROOT / 'packs' / pack_id
    for field in ('variants', 'story'):
        for key, value in list(character.get(field, {}).items()):
            replacement = mapped.get(str((original / value).resolve()).lower())
            if replacement: character[field][key] = replacement
    character['base'] = paths['基础原图']
    if pack_id == 'visitors-v2':
        for mood, name in {'开心_原图':'基础原图', '平静':'平静', '好货':'满意', '满意':'满意', '欢喜':'欣喜', '不开心':'皱眉', '愤怒':'生气', '惊恐':'惊恐', '渴求':'渴求', '虚弱':'虚弱'}.items():
            character['variants']['mood:'+mood] = paths.get(name, paths['基础原图'])
        special = SOURCE.parent/'02_美术素材'/'特殊人物_台词动作差分_2026-11-15'
        record = special/'security'/rid/'台词对应与生成记录.json' if 'Sec' in rid else special/rid/'台词对应与生成记录.json'
        if record.exists():
            for row in read(record):
                scene = row.get('scene','')
                target = paths.get('特殊剧情_'+scene)
                if not target: continue
                keys = row.get('keys') or row.get('dialogue_keys') or ([row['key']] if row.get('key') else [])
                for key in keys: character.setdefault('story',{})[key] = target
            if 'Sec' in rid:
                character['variants']['security:entry'] = paths['特殊剧情_01_到场听取情况']
    report.append(dict(character=rid, pack=pack_id, expressions=len(paths), bytes=dest.stat().st_size))
    print(rid, len(paths), flush=True)

# Copy PNG-only special resources, item icons, and metadata still referenced by existing mappings.
for pack_id, manifest in manifests.items():
    destination = OUT/'packs'/pack_id
    for f in (ROOT/'packs'/pack_id).rglob('*'):
        if f.is_file() and f.name != 'pack.json':
            target=destination/f.relative_to(ROOT/'packs'/pack_id)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(f,target)
    write(destination/'pack.json',manifest)
write(OUT/'layout_profiles.json',layout)
write(OUT/'build-report.json',report)
print('V3 content prepared',len(report),sum(r['expressions'] for r in report))
