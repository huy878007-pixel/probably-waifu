# -*- coding: utf-8 -*-
"""台词完整性审计：拿三个独立来源对撞 data.js。

1) 台词 md 里所有段落（按 token 统计句数）
2) 反编译源码里所有取词调用（按所在方法归类）
3) data.js 里实际收进去的行

输出：哪些台词在游戏里存在、但工作台里没有。
只读。用法: python audit.py
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from extract import (CS, KEY_PATTERNS, IDENT_RE, SPRITE_RE, find_methods,  # noqa: E402
                     read_lines, parse_talk, sprite_helpers)

ROOT = os.path.dirname(HERE)


def main():
    lines = read_lines(CS)
    helpers = sprite_helpers(lines)
    tokens = parse_talk()
    data = json.load(open(os.path.join(ROOT, "data.js"), encoding="utf-8")
                     .read().split("=", 1)[1].rsplit(";", 1)[0].strip()) if False else None

    # data.js 是 window.WB_DATA = {...};
    raw = open(os.path.join(ROOT, "data.js"), encoding="utf-8").read()
    body = raw.split("=", 1)[1].strip().rstrip(";")
    data = json.loads(body)

    captured = {}          # key -> (char, event)
    for c in data["characters"]:
        for ev in c["events"]:
            for l in ev["lines"]:
                captured[l["key"]] = (c["key"], ev["id"])

    # ---- 源码里所有取词点 ----
    sites = {}             # key -> set(method)
    for name, s, e in find_methods(lines):
        for idx in range(s, e):
            for pat in KEY_PATTERNS:
                for m in pat.finditer(lines[idx]):
                    sites.setdefault(m.group(1), set()).add(name)

    print(f"源码取词点：{len(sites)} 个不同 key")
    print(f"工作台收录：{len(captured)} 个 key\n")

    missing = {k: v for k, v in sites.items() if k not in captured}
    print(f"== 在源码里出现、但工作台没有的 key：{len(missing)} ==")

    # 按方法归类，并判断该方法有没有 identifier / 属于哪个 sprite
    by_method = {}
    method_meta = {}
    for name, s, e in find_methods(lines):
        body_text = "\n".join(lines[s:e])
        ids = IDENT_RE.findall(body_text)
        sm = SPRITE_RE.search(body_text)
        sprite = None
        if sm:
            sprite = sm.group(1) if sm.group(1) is not None else helpers.get(sm.group(2).split(".")[-1])
        method_meta[name] = (ids, sprite)

    for k, meths in missing.items():
        for meth in meths:
            by_method.setdefault(meth, []).append(k)

    rows = []
    for meth, keys in by_method.items():
        ids, sprite = method_meta.get(meth, ([], None))
        rows.append((len(keys), meth, ",".join(ids) or "-", sprite or "-"))
    rows.sort(reverse=True)

    print(f"{'句数':>5}  {'方法':<42} {'identifier':<32} sprite")
    for n, meth, ids, sprite in rows[:25]:
        print(f"{n:>5}  {meth[:42]:<42} {ids[:32]:<32} {sprite}")

    # ---- 这些方法是不是别的角色/随机 NPC ----
    known_sprites = set()
    for c in data["characters"]:
        known_sprites.update(c.get("sprites") or [])
    ours = [r for r in rows if r[3] in known_sprites]
    print(f"\n其中属于这 17 个角色的 sprite 的：{len(ours)} 个方法")
    for n, meth, ids, sprite in ours:
        print(f"  {n:>3} 句  {meth}  id={ids} sprite={sprite}")

    # ---- md 对照 ----
    print("\n== 台词 md 逐 token 对账 ==")
    print(f"{'token':<16}{'md句数':>7}{'工作台':>7}  差")
    tot_md = tot_ws = 0
    gaps = []
    for tok, secs in sorted(tokens.items()):
        md_n = sum(len(v) for v in secs.values())
        ws_n = sum(1 for k in captured if k.startswith("dialog_" + tok + "_"))
        tot_md += md_n
        tot_ws += ws_n
        if md_n != ws_n:
            gaps.append((tok, md_n, ws_n))
    for tok, md_n, ws_n in sorted(gaps, key=lambda x: x[1] - x[2], reverse=True):
        print(f"{tok:<16}{md_n:>7}{ws_n:>7}  {md_n - ws_n:+d}")
    print(f"{'合计':<16}{tot_md:>7}{tot_ws:>7}  {tot_md - tot_ws:+d}")

    # ---- 非 LocHelper 的硬编码台词 ----
    hard = []
    for i, line in enumerate(lines):
        if "SetText(" in line and re.search(r'"[^"]*[\u4e00-\u9fff]', line) and "LocHelper" not in line:
            hard.append((i + 1, line.strip()[:120]))
    print(f"\n== SetText 里直接写死的含中文文本：{len(hard)} 处 ==")
    for ln, text in hard[:10]:
        print(f"  L{ln}: {text}")

    # ---- SetText 总数 vs 已知 key ----
    total_settext = sum(1 for l in lines if ".SetText(" in l)
    print(f"\n源码 SetText 调用行数：{total_settext}（一行多个调用的情况未拆）")


if __name__ == "__main__":
    main()
