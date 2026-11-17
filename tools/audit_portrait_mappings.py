"""Structural mapping audit. Never invents semantic portrait choices or edits the pack."""
import hashlib
import json
import re
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT.parent / 'PortraitMaterialPolish/artifacts/mapping-audit-20261108'


def read(p):
    return json.loads(p.read_text(encoding='utf-8-sig'))


def methods(path):
    text = path.read_text(encoding='utf-8-sig')
    # Top-level decompiled methods; callback/local-function bodies remain inside their owner.
    starts = list(re.finditer(r'^\t(?:public|private|internal|protected) (?:static )?[^\n=;]+?\b(\w+)\([^\n]*\)\s*\n\t\{', text, re.M))
    result = {}
    for i, m in enumerate(starts):
        body = text[m.start():starts[i + 1].start() if i + 1 < len(starts) else len(text)]
        keys = re.findall(r'GetLocalizedDialogue\(\s*"([^"]+)"', body)
        keys += re.findall(r'GetLocalizedOrDefault\(\s*"Dialogue"\s*,\s*"([^"]+)"', body)
        speaker_aliases = {var: ('"CLIENT"' if value.endswith('.displayName') else 'name:' + namekey if namekey else value)
            for var, value, namekey in re.findall(r'(?:string|var)\s+(\w+)\s*=\s*([\w.]+\.displayName|LocHelper.GetLocalizedClientName\("([^"]+)"\))', body)}
        speakers = {k: speaker_aliases.get(sp, sp) for sp, k in re.findall(r'SetText\(\s*("[^"]+"|[\w.]+)\s*,\s*LocHelper\.GetLocalizedDialogue\("([^"]+)"', body)}
        result[m[1]] = {'body': body, 'keys': set(keys), 'line': text[:m.start()].count('\n') + 1,
                       'identifier': re.findall(r'\bidentifier\s*=\s*"([^"]+)"', body),
                       'sprites': re.findall(r'\bspriteName\s*=\s*"([^"]+)"', body), 'speaker': speakers}
    return result


def main():
    evidence = read(OUT / 'source-evidence.json')
    assembly = Path('D:/SteamLibrary/steamapps/common/Probably Stolen/Probably Stolen_Data/Managed/Assembly-CSharp.dll')
    if hashlib.sha256(assembly.read_bytes()).hexdigest() != evidence['assemblySha256']:
        raise ValueError('Game assembly changed; decompile and record new evidence before auditing')
    for filename, digest in evidence['files'].items():
        if hashlib.sha256((OUT / filename).read_bytes()).hexdigest() != digest:
            raise ValueError('Decompiled source changed: ' + filename)
    pack = read(ROOT / 'packs/core/pack.json')
    wb = read(OUT / 'workbench-current.json')
    author = read(Path('D:/Downloads/立绘映射_2026-11-06.json'))
    tables = {p.name.removesuffix('.decompiled.cs'): methods(p) for p in OUT.glob('*.decompiled.cs')}
    registered = {f for d in pack['characters'].values() for f in d['factories']}

    def expand(cls, name, visited=None):
        visited = set() if visited is None else visited
        if (cls, name) in visited or name not in tables.get(cls, {}):
            return set(), {}
        visited.add((cls, name))
        method = tables[cls][name]
        keys, speakers = set(method['keys']), dict(method['speaker'])
        qualified = re.findall(r'\b([A-Za-z_]\w*)\.([A-Za-z_]\w*)\(', method['body'])
        for target, child in [(cls, c) for c in re.findall(r'(?<![\w.])([A-Za-z_]\w*)\(', method['body'])] + qualified:
            if (target, child) == (cls, name) or target not in tables:
                continue
            if target + '.' + child in registered and not re.search(r'(?:=|return)\s*' + re.escape(child) + r'\(', method['body']):
                continue
            child_keys, child_speakers = expand(target, child, visited)
            keys.update(child_keys)
            speakers.update(child_speakers)
        return keys, speakers

    all_factory_info = {}
    for ck, definition in pack['characters'].items():
        for factory in definition['factories']:
            cls, name = factory.split('.')
            method = tables.get(cls, {}).get(name)
            keys, speakers = expand(cls, name)
            all_factory_info[factory] = {'character': ck, 'keys': sorted(keys), 'speakers': speakers,
                'exists': method is not None, 'identifier': method['identifier'] if method else [],
                'sprites': method['sprites'] if method else [],
                'source': cls + '.decompiled.cs', 'line': method['line'] if method else None}

    line_gaps, slot_gaps, routes, source_missing, decisions, assets_missing, base_lines = [], [], {}, [], [], [], []
    seen_factory_keys = set()
    for ch in wb['characters']:
        ck, definition = ch['key'], pack['characters'][ch['key']]
        routes[ck] = {}
        for ev in ch['events']:
            if ev.get('foreign'):
                continue
            event = ev.get('sourceEvent') or ev['id']
            keys = {l['key'] for l in ev['lines'] if l['spk'] == 'char'}
            candidates = []
            for factory in definition['factories']:
                info = all_factory_info[factory]
                # Exact runtime identifier or full literal-key containment, never name semantics.
                if event in info['identifier'] or (keys and keys <= set(info['keys'])):
                    candidates.append(factory)
            if len(candidates) != 1:
                candidates = [f for f in candidates if event in all_factory_info[f]['identifier']] or candidates
            routes[ck][ev['id']] = {'factories': candidates, 'unique': len(candidates) == 1}
            names = [i for i in definition['identifiers'] if i.lower() == event.lower()]
            if len(candidates) == 1:
                names += all_factory_info[candidates[0]]['identifier'] + ['@' + candidates[0]]
            names = list(dict.fromkeys(names))
            sprites = list(dict.fromkeys([ev.get('sprite') or '', '']))

            def scope(slot):
                for name in names:
                    for sprite in sprites:
                        k = f'workbench|{ck}|{name}|{sprite}|{slot}'
                        if k in definition['variants']:
                            return definition['variants'][k], k
                return None, None

            for line in ev['lines']:
                if line['spk'] != 'char':
                    continue
                path, via = scope('line:' + line['key'])
                path = path or definition['story'].get(line['key'])
                if not path:
                    line_gaps.append({'character': ck, 'name': ch['name'], 'scene': ev['id'], 'key': line['key'], 'text': line.get('zh'), 'candidates': candidates})
                elif path == definition['base'] or path.startswith('portraits/base/') or Path(path).name.startswith('base__'):
                    base_lines.append({'character': ck, 'name': ch['name'], 'scene': ev['id'], 'key': line['key'], 'text': line.get('zh'), 'path': path})
                for f in candidates:
                    seen_factory_keys.add((f, line['key']))
            if not names:
                source_missing.append({'character': ck, 'scene': ev['id'], 'factories': candidates})
            # Compare already-confirmed author choices with the effective runtime slot.
            choices = author.get('characters', {}).get(ck, {}).get('events', {}).get(ev['id'], {})
            for kind, slots in [('enters', choices.get('enters', {})), ('exits', choices.get('exits', {}))]:
                for label, portrait in slots.items():
                    suffix = 'entry' if kind == 'enters' and label == '默认入场' else 'arrest' if label == '被捕带走' else 'exit' if label in ('自然退场', '本路线自然退场') else None
                    if suffix is None and label.startswith('走完「') and label.endswith('」后'):
                        branch = label[3:-2]
                        group = [l['key'] for l in ev['lines'] if l['spk'] == 'char' and not l.get('main') and
                                 ((l.get('label') or l.get('branch')) == branch or (branch == '成交' and (l.get('ctx') == branch or branch in (l.get('label') or ''))))]
                        if group:
                            suffix = 'exit:' + group[-1]
                    actual, via = scope(suffix) if suffix else (None, None)
                    rel = wb.get('releasePaths', {}).get(portrait)
                    if portrait and portrait.startswith('release/'):
                        rel = portrait.removeprefix('release/')
                    expected = 'portraits/story/' + rel if rel else None
                    if not actual:
                        gap = {'character': ck, 'name': ch['name'], 'scene': ev['id'], 'slot': label,
                               'suffix': suffix, 'authorPortrait': portrait, 'expectedPath': expected, 'candidates': candidates}
                        slot_gaps.append(gap)
                        if suffix and expected and len(candidates) == 1 and (ROOT / 'packs/core' / expected).is_file():
                            factory = candidates[0]
                            key = f'workbench|{ck}|@{factory}|{ev.get("sprite") or ""}|{suffix}'
                            decisions.append({'character': ck, 'scene': ev['id'], 'key': key, 'path': expected,
                                'evidence': {'source': all_factory_info[factory]['source'], 'line': all_factory_info[factory]['line'], 'authorSlot': label}})
                    elif expected and actual != expected:
                        decisions.append({'character': ck, 'scene': ev['id'], 'conflict': True, 'key': via, 'current': actual, 'expected': expected})

    unseen = []
    known_context = {ch['key']: {l['key'] for e in ch['events'] for l in e['lines'] if e.get('foreign') or l['spk'] != 'char'} for ch in wb['characters']}
    known_any = {ch['key']: {l['key'] for e in ch['events'] for l in e['lines']} for ch in wb['characters']}
    for factory, info in all_factory_info.items():
        ck = info['character']
        for key in info['keys']:
            if (factory, key) in seen_factory_keys:
                continue
            sp = info['speakers'].get(key, '')
            kind = 'player-or-security' if sp in ('"PLAYER"', '"SEC"') else 'character' if (sp == '"CLIENT"' or sp.endswith('.displayName')) else 'speaker-unconfirmed'
            if key in known_context[ck]:
                kind = 'already-shown-as-other-speaker'
            unseen.append({'character': ck, 'factory': factory, 'key': key, 'speaker': sp, 'kind': kind,
                           'hasGlobalMapping': key in pack['characters'][ck]['story'], 'alreadyInWorkbench': key in known_any[ck], 'source': info['source'], 'line': info['line']})
    for ck, definition in pack['characters'].items():
        for path in set([definition['base']] + list(definition['story'].values()) + list(definition['variants'].values())):
            if not (ROOT / 'packs/core' / path).is_file():
                assets_missing.append({'character': ck, 'path': path})
    manifest = ROOT / 'packs/core/pack.json'
    result = {'sourceSha256': hashlib.sha256(manifest.read_bytes()).hexdigest(), 'workbenchBuild': wb['build'], 'gameEvidence': evidence,
        'sceneRoutes': routes, 'factoryInfo': all_factory_info, 'missingExplicitLines': line_gaps,
        'missingAuthorSlots': slot_gaps, 'unresolvedSceneContexts': source_missing, 'sourceKeysNotInWorkbench': unseen,
        'missingAssets': assets_missing, 'structuralRepairs': decisions, 'explicitBaseMappings': base_lines,
        'summary': {'lineGaps': len(line_gaps), 'slotGaps': len(slot_gaps), 'unresolvedScenes': len(source_missing),
                    'sourceKeysNotInWorkbench': dict(Counter(r['kind'] for r in unseen)), 'missingAssets': len(assets_missing),
                    'structuralRepairs': sum(not d.get('conflict') for d in decisions), 'conflicts': sum(bool(d.get('conflict')) for d in decisions), 'explicitBaseLineMappings': len(base_lines)}}
    (OUT / 'mapping-audit.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(result['summary'], ensure_ascii=False))
    print('line gaps by character:', dict(Counter(r['character'] for r in line_gaps)))
    print('slot gaps by character:', dict(Counter(r['character'] for r in slot_gaps)))


if __name__ == '__main__':
    main()
