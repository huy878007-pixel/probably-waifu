"""Restore only explicit author choices with one proven runtime factory; never guess images."""
import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT.parent / 'PortraitMaterialPolish/artifacts/mapping-audit-20261108'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    path = ROOT / 'packs/core/pack.json'
    raw = path.read_bytes()
    audit = json.loads((OUT / 'mapping-audit.json').read_text(encoding='utf-8'))
    if hashlib.sha256(raw).hexdigest() != audit['sourceSha256']:
        raise ValueError('Pack changed after audit; re-audit first')
    pack = json.loads(raw.decode('utf-8-sig'))
    edits = [r for r in audit['structuralRepairs'] if not r.get('conflict')]
    keys = set()
    for edit in edits:
        ck, key, asset = edit['character'], edit['key'], edit['path']
        if (ck, key) in keys or key in pack['characters'][ck]['variants']:
            raise ValueError('Duplicate or existing mapping: ' + key)
        keys.add((ck, key))
        if not (ROOT / 'packs/core' / asset).is_file():
            raise ValueError('Missing asset: ' + asset)
        pack['characters'][ck]['variants'][key] = asset
    if args.apply:
        backup = OUT / 'pack-before-structural-repairs.json'
        if backup.exists():
            raise ValueError('Backup exists; refusing overwrite')
        backup.write_bytes(raw)
        path.write_text(json.dumps(pack, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        (OUT / 'applied-structural-repairs.json').write_text(json.dumps({'beforeSha256':audit['sourceSha256'],
            'afterSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'edits':edits},ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'applied':args.apply,'newBindings':len(edits),'semanticSelectionsChanged':0}))


if __name__ == '__main__':
    main()
