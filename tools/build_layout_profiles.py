"""Measure every character's alpha silhouette and keep expression families at one scale."""
import json
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[1]
assets = {}
families = []
for manifest in sorted((root / "packs").glob("*/pack.json")):
    data = json.loads(manifest.read_text(encoding="utf-8-sig"))
    if data.get("enabled") is False:
        continue
    for character, value in data.get("characters", {}).items():
        paths = set(value.get("variants", {}).values()) | set(value.get("story", {}).values())
        if value.get("base"):
            paths.add(value["base"])
        measured = {}
        for relative in sorted(paths):
            with Image.open(manifest.parent / relative) as image:
                alpha = image.convert("RGBA").getchannel("A").point(lambda value: 255 if value >= 26 else 0)
                bounds = alpha.getbbox()
                if not bounds:
                    raise ValueError(f"Empty portrait: {relative}")
                measured[relative] = (image.size, bounds)
        if not measured:
            continue
        if len({size for size, _ in measured.values()}) != 1:
            raise ValueError(f"Normalize family canvas first: {character}")
        left = min(bounds[0] for _, bounds in measured.values())
        right = max(bounds[2] for _, bounds in measured.values())
        height = max(bounds[3] - bounds[1] for _, bounds in measured.values())
        for relative, (size, bounds) in measured.items():
            entry = dict(Width=size[0], Height=size[1], CenterX=(left + right) / 2,
                         BottomY=size[1] - bounds[3], FamilyWidth=right-left, FamilyHeight=height)
            key = data["id"] + "|" + relative
            if key in assets and assets[key] != entry:
                raise ValueError(f"Shared portrait has conflicting families: {key}")
            assets[key] = entry
        families.append(dict(pack=data["id"], character=character, paths=len(measured)))
profile = dict(Frame="--Physical Store--/shutterFrame",
               Aperture=[338/960, 329/540, 285/960, 185/540],
               Origin=[(.00394094+4.8)/9.6, (1.582904+2.7)/5.4],
               Fill=.5, Assets=assets)
(root / "layout_profiles.json").write_text(json.dumps(profile, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(dict(families=len(families), assets=len(assets)), ensure_ascii=False))
