import argparse
import csv
import hashlib
import json
from PIL import Image, ImageOps
from collections import defaultdict
from pathlib import Path
from visitor_story import apply_story_moods

parser = argparse.ArgumentParser()
parser.add_argument("delivery", type=Path)
args = parser.parse_args()
delivery = args.delivery
sources = delivery.parent
original = sources / "普通客人_原版立绘_177张_2026-11-03"
with (original / "立绘索引.csv").open(encoding="utf-8-sig") as stream:
    native = {row["key"]: row for row in csv.DictReader(stream)}
pack = Path(__file__).resolve().parents[2] / "packs" / "visitors-v2"
def digest(path):
    return hashlib.sha256(path.read_bytes()).digest()

evidence = defaultdict(set)
for folder in sources.glob("普通客人娘化_*"):
    for metadata in folder.glob("*.json"):
        rows = json.loads(metadata.read_text(encoding="utf-8-sig"))
        if not isinstance(rows, list):
            continue
        for row in rows:
            if not isinstance(row, dict) or not isinstance(row.get("refs"), list):
                continue
            keys = {Path(ref).stem for ref in row["refs"] if str(original) in ref and Path(ref).stem in native}
            art = folder / (str(row.get("name", "")) + ".png")
            if len(keys) == 1 and art.is_file():
                evidence[digest(art)].update(keys)

# Explicit crosswalk for batches whose generation records lack per-image references.
batches = [
    (29,"femaleUpper",[10,11,6,7,8,9,1,2]),
    (37,"femaleScav",[14]), (38,"femaleUpper",[3,4,5]),
    (41,"femaleScav",[10,11,12,13,8,9,2,3,4,5,6,7]),
    (53,"femaleLower",range(10,15)), (58,"femaleScav",[1]),
    (59,"femaleLower",[4,5,6,7,8,9,1,2,3]),
    (68,"femaleSecOff",[1,2]), (70,"femaleMed",[2]), (71,"femaleSec",[1,2]),
    (73,"maleChef",[1,2,3]), (76,"maleRev",[3,4]), (78,"maleEng",[3,4,5,6]),
    (82,"maleRev",[1,2]), (84,"maleBM",[1]), (85,"maleEng",[1,2]),
    (87,"maleMerc",range(1,5)), (91,"maleLower",range(1,12)),
    (102,"maleScav",range(1,13)), (114,"maleUpper",range(1,6)), (119,"maleScav",[13]),
    (120,"maleUpper",range(6,10)), (124,"maleSecCorpo",[1,2]),
    (126,"maleFarmer",range(1,5)), (130,"maleSci",[1,2]), (132,"maleBM",range(2,7)),
    (137,"femaleMed",[1]), (138,"femaleSec",[3,4,5]), (141,"maleSec",range(1,7)),
    (147,"maleMed",[1,2]), (149,"maleSecOff",[1,2,3]),
]
crosswalk = {start + offset: prefix + str(n) for start,prefix,indices in batches
             for offset,n in enumerate(indices)}
entries = json.loads((delivery / "立绘索引.json").read_text(encoding="utf-8-sig"))
characters, unresolved = {}, []
for entry in entries:
    group, files = entry["group"], entry["files"]
    key = entry["game_id"]
    if group == "01_普通客人":
        candidates = evidence[digest(delivery / files["开心_原图"]["path"])]
        fallback = crosswalk.get(int(entry["key"].split("_", 1)[0]))
        if fallback and candidates and candidates != {fallback}:
            raise ValueError(f"Conflicting evidence: {entry['key']}")
        if not candidates and fallback:
            candidates = {fallback}
        if len(candidates) != 1:
            unresolved.append(entry["key"])
            continue
        key = next(iter(candidates))
    if key in characters:
        raise ValueError(f"Duplicate visitor identity: {key}")
    variants = {}
    for mood, asset in files.items():
        relative = f"portraits/{key}/{mood}.png"
        target = pack / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        with Image.open(delivery / asset["path"]) as source:
            image = ImageOps.contain(source.convert("RGBA"), (1152, 844), Image.Resampling.LANCZOS)
            canvas = Image.new("RGBA", (1152, 844))
            canvas.paste(image, ((1152 - image.width) // 2, 844 - image.height))
            canvas.save(target)
        variants["mood:" + mood] = relative
    identifiers = [key]
    if group in ("01_普通客人", "02_改造体"):
        prefix = "aug" if group == "02_改造体" else "ordinary"
        identifiers.extend(dict.fromkeys("visitor:" + prefix + ":" + name for name in (native[key]["originalSprite"], key)))
    characters[key] = {"identifiers": identifiers,
                       "base": variants.get("mood:开心_原图", variants.get("mood:敌对")),
                       "variants": variants}
for key, names in {'shadyMerchant': ['scalper', 'loan_shark', 'loan_shark2', 'loan_shark_collect'], 'admin': ['appraisalBoardPromotion', 'appraisalBoardIntro', 'appraisalBoardIntro2'], 'merchant': ['supplier', 'supplierIntro', 'waterSeller'], 'sawyer': ['janitorIntroduction', 'janitorUEIntroduction', 'janitorUEIntroductionFence', 'sawyerButcherOffer'], 'courrier': ['courrierRedImp', 'courrierMining', 'courrierRedImpFree', 'courrierMiningFree'], 'cartel_handler': ['cartelIntro', 'cartelSecSnitched', 'cartelHandlerCheckin', 'cartelHandlerWeekly']}.items():
    characters[key]["identifiers"].extend(names)
if unresolved or len(characters) != len(entries):
    raise ValueError(f"Incomplete visitor mapping: {unresolved}")
# These two exclusive characters were added after the source delivery.
for key, ids in {"waffle_enjoyer": ["foodieTourist", "foodieTouristReturning"],
                 "case_enjoyer": ["caseEnjoyer", "caseEnjoyerReturning"]}.items():
    variants = {"mood:" + mood: f"portraits/{key}/{mood}.png"
                for mood in ("开心_原图", "平静", "不开心", "愤怒", "欢喜")}
    if all((pack / path).is_file() for path in variants.values()):
        characters[key] = {"identifiers": ids + ["visitor:ordinary:" + key],
                           "base": variants["mood:开心_原图"], "variants": variants}
identifiers = [identity for character in characters.values() for identity in character["identifiers"]]
if len(set(identifiers)) != len(identifiers):
    raise ValueError("Duplicate visitor alias")
apply_story_moods(characters)
manifest = {"format": "portrait-pack/1", "id": "visitors-v2", "name": "深空娘化 · 客人差分",
            "priority": 90, "characters": characters}
(pack / "pack.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"bound": len(characters), "unresolved": unresolved}, ensure_ascii=False))


