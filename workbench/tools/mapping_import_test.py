"""Run the actual importer against a temporary pack, never production mappings."""
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT.parent / 'PortraitMaterialPolish/artifacts/mapping-audit-20261108'


def main():
    wb = json.loads((OUT / 'workbench-current.json').read_text(encoding='utf-8'))
    wb['paths'] = json.loads((ROOT.parent / 'PortraitRuntime/artifacts/update-build26468176/workbench-data.json').read_text(encoding='utf-8'))['paths']
    route = json.loads((OUT / 'mapping-audit.json').read_text(encoding='utf-8'))['sceneRoutes']['Mentor']['mentor/afterhours']
    ch = next(c for c in wb['characters'] if c['key'] == 'Mentor')
    scene = next(e for e in ch['events'] if e['id'] == 'mentor/afterhours')
    scene['runtimeFactories'] = route['factories']
    author = json.loads(Path('D:/Downloads/立绘映射_2026-11-06.json').read_text(encoding='utf-8-sig'))['characters']['Mentor']
    pack = json.loads((ROOT / 'packs/core/pack.json').read_text(encoding='utf-8'))
    with tempfile.TemporaryDirectory(prefix='portrait-map-import-') as directory:
        target = Path(directory)
        fixture = target / 'beauty'
        (fixture / 'tools').mkdir(parents=True)
        core = fixture / 'packs/core'
        core.mkdir(parents=True)
        script = fixture / 'tools/import_workbench.py'
        shutil.copy2(ROOT / 'tools/import_workbench.py', script)
        manifest = core / 'pack.json'
        manifest.write_text(json.dumps(pack, ensure_ascii=False), encoding='utf-8')
        for kind in ('enters', 'exits'):
            for portrait in author['events']['mentor/afterhours'].get(kind, {}).values():
                rel = portrait.removeprefix('release/') if portrait.startswith('release/') else wb['paths'][portrait]
                asset = core / 'portraits/story' / rel
                asset.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(ROOT / 'packs/core/portraits/story' / rel, asset)
        exported = {'version': 3, 'characters': {'Mentor': {
            'scenes': [{'id': scene['id'], 'event': scene['id'], 'sprite': scene.get('sprite'), 'lines': {}}],
            'events': {scene['id']: author['events'][scene['id']]}
        }}}
        export_path, wb_path = target/'export.json', target/'workbench.json'
        export_path.write_text(json.dumps(exported,ensure_ascii=False),encoding='utf-8')
        wb_path.write_text(json.dumps(wb,ensure_ascii=False),encoding='utf-8')
        result = subprocess.run([sys.executable,str(script),str(export_path),str(wb_path),str(target/'originals'),str(target/'release'),str(ROOT.parent/'PortraitRuntime/catalogs'),str(target/'report.json'),'--apply'],capture_output=True,text=True)
        assert result.returncode == 0, result.stdout + result.stderr
        final = json.loads(manifest.read_text(encoding='utf-8'))
        assert all(final['characters'][ck]['story'] == value['story'] for ck,value in pack['characters'].items())
        assert all(final['characters'][ck]['variants'].get(k) == v for ck,value in pack['characters'].items() for k,v in value['variants'].items())
        report = json.loads((target/'report.json').read_text(encoding='utf-8'))
        assert not report['unbound_slots'], report
    print('PASS partial import preserves all previous scoped mappings; source-proven legacy scene binds both author slots')


if __name__ == '__main__':
    main()
