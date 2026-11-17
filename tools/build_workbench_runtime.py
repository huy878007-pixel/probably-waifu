"""Read-only snapshot of current package mappings for the human review workbench."""
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    source = ROOT / 'packs/core/pack.json'
    raw = source.read_bytes()
    pack = json.loads(raw.decode('utf-8-sig'))
    snapshot = {
        'source': 'packs/core/pack.json',
        'sha256': hashlib.sha256(raw).hexdigest(),
        'generated': datetime.now(timezone.utc).isoformat(),
        'gameBuild': pack.get('gameBuild'),
        'characters': pack['characters'],
    }
    (ROOT / 'workbench/runtime-data.js').write_text(
        'window.WB_RUNTIME = ' + json.dumps(snapshot, ensure_ascii=False) + ';\n', encoding='utf-8')
    print(json.dumps({'characters': len(pack['characters']), 'sha256': snapshot['sha256']}))


if __name__ == '__main__':
    main()
