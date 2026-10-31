# -*- coding: utf-8 -*-
"""为工作台生成立绘预览图：assets/p（300宽，托盘/大图）与 assets/t（96宽，时间轴小图）。

只读素材，输出到工作台目录。用法: python thumbs.py
"""
import os
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_ROOT = os.path.dirname(HERE)
DESKTOP = os.path.dirname(OUT_ROOT)
MAT = os.path.join(DESKTOP, "深空娘化_角色立绘与剧情精灵图_2026-10-31")

BIG, SMALL = 300, 96


def main():
    made = 0
    for kind, sub in (("p", BIG), ("t", SMALL)):
        os.makedirs(os.path.join(OUT_ROOT, "assets", kind), exist_ok=True)
    for dirpath, _, files in os.walk(MAT):
        rel_dir = os.path.relpath(dirpath, MAT).replace("\\", "/")
        if "02_已认可基础立绘" not in rel_dir and "05_复核切割单张_v2" not in rel_dir:
            continue
        for fn in files:
            if not fn.lower().endswith(".png"):
                continue
            src = os.path.join(dirpath, fn)
            rel = os.path.relpath(src, MAT).replace("\\", "/")
            tag = rel.replace("/", "__")
            im = Image.open(src).convert("RGBA")
            for kind, width in (("p", BIG), ("t", SMALL)):
                w, h = im.size
                nh = max(1, round(h * width / w))
                im.resize((width, nh), Image.LANCZOS).save(
                    os.path.join(OUT_ROOT, "assets", kind, tag), optimize=True)
            made += 1
    print("生成预览:", made, "张 ×2 尺寸")
    for kind in ("p", "t"):
        d = os.path.join(OUT_ROOT, "assets", kind)
        total = sum(os.path.getsize(os.path.join(d, f)) for f in os.listdir(d))
        print(f"  assets/{kind}: {len(os.listdir(d))} 文件 {total // 1024} KB")


if __name__ == "__main__":
    main()
