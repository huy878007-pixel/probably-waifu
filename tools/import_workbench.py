"""Convert the author's workbench export into native-keyed portrait assets."""
import argparse
import json
from collections import defaultdict
from pathlib import Path

from PIL import Image


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser()
    for name in ("export", "workbench", "originals", "release", "catalogs", "report"):
        parser.add_argument(name, type=Path)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1] / "packs/core"
    manifest = root / "pack.json"
    pack = read(manifest)
    exported = read(args.export)
    if exported["version"] not in (2, 3):
        raise ValueError("Unsupported workbench export")
    workbook = read(args.workbench)
    by_character = {c["key"]: c for c in workbook["characters"]}
    known = set(read(args.catalogs / "dialogue_keys.json"))
    factories = read(args.catalogs / "client_factories.json")
    additions, unresolved = {}, []
    stats = defaultdict(int)

    def asset(identifier, character):
        if not identifier:
            return None
        parts = identifier.split("/")
        if any(not p or p in (".", "..") or "\\" in p or ":" in p for p in parts):
            raise ValueError(f"Invalid portrait path: {identifier}")
        folder = parts[1] if parts[0] == "release" and len(parts) > 1 else parts[0]
        if folder != workbook_character["folder"]:
            raise ValueError(f"{character}: portrait belongs to another character: {identifier}")
        if identifier not in ids and not (args.originals / identifier).is_file():
            raise ValueError(f"{character}: unknown portrait {identifier}")
        mapped = workbook["paths"].get(identifier)
        if identifier.startswith("release/"):
            mapped = identifier.removeprefix("release/")
        elif len(parts) == 4:
            # A legacy cell's sheet is part of its identity. Never match on the caption alone.
            mapped = f"{parts[0]}/{parts[2]}__{parts[3]}"
        if mapped:
            target = "portraits/story/" + mapped
            if (root / target).is_file():
                return target
            if (args.release / mapped).is_file():
                additions[target] = (args.release / mapped, None)
                return target
        source = (args.originals / identifier).resolve()
        if not source.is_relative_to(args.originals.resolve()) or not source.is_file():
            raise FileNotFoundError(f"{character}: {identifier}")
        target = f"portraits/story/{parts[0]}/workbench__{'__'.join(parts[2:])}"
        additions[target] = (source, root / pack["characters"][character]["base"])
        return target

    for character, changes in exported["characters"].items():
        definition = pack["characters"][character]
        workbook_character = by_character[character]
        ids = {p["id"] for p in workbook_character["portraits"]}
        events = {e["id"]: e for e in workbook_character["events"]}
        native_ids = set(definition["identifiers"])
        for method in factories:
            if method["type"] + "." + method["method"] in definition["factories"]:
                native_ids.update(method["identifier_literals"])
        native_ids = {n.lower(): n for n in native_ids if n and n != "NO_ID"}
        factory_events = {}
        for method in factories:
            qualified = method["type"] + "." + method["method"]
            if qualified in definition["factories"]:
                factory_events[method["method"].removeprefix("Create").lower()] = "@" + qualified
        variants = definition["variants"]
        generated_scopes = {}
        choices = defaultdict(list)

        def scoped(event, sprite, suffix, path):
            if not path:
                return
            key = f"workbench|{character}|{event}|{sprite or ''}|{suffix}"
            if key in generated_scopes and generated_scopes[key] != path:
                raise ValueError(f"Conflicting selection: {key}")
            generated_scopes[key] = path
            variants[key] = path
            stats["scoped_" + suffix.split(":")[0]] += 1

        for scene in changes["scenes"]:
            reference = events[scene["id"]]
            event = reference.get("sourceEvent") or reference["id"]
            sprite = reference.get("sprite")
            if (scene["event"], scene.get("sprite")) != (event, sprite):
                raise ValueError(f"Scene metadata disagrees: {scene['id']}")
            native_event = native_ids.get(event.lower()) or factory_events.get(event.lower().replace("_", ""))
            if not native_event and event.startswith('@') and event[1:] in definition['factories']:
                native_event = event
            if not native_event and len(reference.get('runtimeFactories', [])) == 1 and reference['runtimeFactories'][0] in definition['factories']:
                native_event = '@' + reference['runtimeFactories'][0]
            line_keys = {line["key"] for line in reference["lines"] if line["spk"] == "char"}
            for key, identifier in scene["lines"].items():
                if key not in known or key not in line_keys:
                    raise ValueError(f"{character}/{scene['id']}: invalid dialogue key {key}")
                choices[key].append((native_event, sprite, asset(identifier, character)))
                stats["scene_lines"] += 1
            slots = changes["events"].get(scene["id"], {})
            for label, identifier in slots.get("enters", {}).items():
                path = asset(identifier, character)
                if native_event and label == "默认入场":
                    scoped(native_event, sprite, "entry", path)
                else:
                    unresolved.append([character, scene["id"], label])
            for label, identifier in slots.get("exits", {}).items():
                path = asset(identifier, character)
                if native_event and label in ("自然退场", "本路线自然退场", "被捕带走"):
                    scoped(native_event, sprite, "arrest" if label == "被捕带走" else "exit", path)
                elif native_event and label.startswith("走完「") and label.endswith("」后"):
                    branch = label[3:-2]
                    group = [
                        line["key"] for line in reference["lines"]
                        if line["spk"] == "char" and not line.get("main")
                        and ((line.get("label") or line.get("branch")) == branch
                             or (branch == "成交" and (line.get("ctx") == branch or branch in (line.get("label") or ""))))
                    ]
                    if group:
                        scoped(native_event, sprite, "exit:" + group[-1], path)
                    else:
                        unresolved.append([character, scene["id"], label])
                else:
                    unresolved.append([character, scene["id"], label])

        for key, options in choices.items():
            paths = {path for _, _, path in options}
            if len(paths) == 1:
                path = paths.pop()
                if path:
                    stats["changed_global"] += definition["story"].get(key) != path
                    definition["story"][key] = path
                else:
                    definition["story"].pop(key, None)
            else:
                definition["story"].pop(key, None)
                for event, sprite, path in set(options):
                    if not event:
                        raise ValueError(f"Cannot identify native route for {character}/{key}")
                    scoped(event, sprite, "line:" + key, path)

    if args.apply:
        for target, (source, base) in additions.items():
            path = root / target
            path.parent.mkdir(parents=True, exist_ok=True)
            with Image.open(source) as original:
                art = original.convert("RGBA")
            if base and art.size != tuple(pack.get("portraitCanvas", (1152, 844))):
                width, height = pack.get("portraitCanvas", (1152, 844))
                with Image.open(base) as image:
                    bounds = image.convert("RGBA").getchannel("A").getbbox()
                art = art.crop(art.getchannel("A").getbbox())
                scale = min(width / art.width, (bounds[3] - bounds[1]) / art.height)
                art = art.resize((round(art.width * scale), round(art.height * scale)), Image.Resampling.LANCZOS)
                canvas = Image.new("RGBA", (width, height))
                x = min(width - art.width, max(0, round((bounds[0] + bounds[2] - art.width) / 2)))
                canvas.paste(art, (x, height - art.height))
                art = canvas
            art.save(path)
        write(manifest, pack)
    write(args.report, {"characters": len(exported["characters"]), **stats,
                        "asset_additions": list(additions), "unbound_slots": unresolved})


if __name__ == "__main__":
    main()
