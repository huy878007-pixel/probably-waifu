# -*- coding: utf-8 -*-
"""生成工作台数据 data.js（v4：一场对话一张卡 + 卡内分支树）。

- 台词全量来自角色台词 md（739 句）
- 真实 key 从反编译源码反查（含 dialog_x 与 dialog_x_2 混用编号）
- 卡片按客户端事件 id 聚合：递错/递对/违禁/成交都是同一张卡里的分支
- 台词按 key 全局唯一：同一句台词出现在两场对话里也只存一份

只读。用法: python extract.py
"""
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_ROOT = os.path.dirname(HERE)
DESKTOP = os.path.dirname(OUT_ROOT)

CS = r"D:\Temp\ps-decompile-26317870\Assembly-CSharp.decompiled.cs"
LOC = os.path.join(DESKTOP, "深空当铺人物美化", "提取工具与映射", "游戏本地化文本.json")
TALK = os.path.join(DESKTOP, "深空娘化_角色台词")
MAT = os.path.join(DESKTOP, "深空娘化_角色立绘与剧情精灵图_2026-10-31")
BUILD = "26317870"

CHARACTERS = [
    dict(key="lun",      folder="01_伦警官",     name="伦警官",     sprites=["officer_lun"]),
    dict(key="jackson",  folder="02_杰克逊博士",  name="杰克逊博士", sprites=["inventor", "dr._jackson"]),
    dict(key="landlord", folder="03_房东",       name="房东",       sprites=["landlord", "the_landlord"]),
    dict(key="Georgio",  folder="04_乔治奥",     name="乔治奥",     sprites=["george"]),
    dict(key="eleanor",  folder="05_埃莉诺",     name="埃莉诺",     sprites=["eleanor1", "eleanor2", "eleanor3", "eleanor_scav", "eleanor_rev"]),
    dict(key="Mentor",   folder="06_导师",       name="导师",       sprites=["mentor", "the_mentor"]),
    dict(key="Serpico",  folder="07_塞尔皮科",    name="塞尔皮科",   sprites=["serpico", "serpico_scav", "serpico_rev"]),
    dict(key="wild",     folder="08_王尔德",     name="王尔德",     sprites=["joe", "joe_wild", "wild"]),
    dict(key="lou",      folder="09_卢",         name="卢",         sprites=["wanted5", "lou_sec"]),
    dict(key="odin",     folder="10_奥丁",       name="奥丁",       sprites=["odin", "wanted4"]),
    dict(key="butcher",  folder="11_屠夫",       name="屠夫",       sprites=["wanted2"]),
    dict(key="li",       folder="12_李北文",      name="李北文",     sprites=["wanted6"]),
    dict(key="juan",     folder="13_胡安",       name="胡安",       sprites=["wanted7"]),
    dict(key="leroy",    folder="14_勒鲁瓦",      name="勒鲁瓦",     sprites=["wanted9"]),
    dict(key="bailey",   folder="15_詹姆斯贝利_未做剧情精灵图", name="詹姆斯·贝利", sprites=["wanted1"]),
    dict(key="arthur",   folder="16_亚瑟拉弗勒_未做剧情精灵图", name="亚瑟·拉弗勒", sprites=["wanted3"]),
    dict(key="john",     folder="17_约翰史密斯_未做剧情精灵图", name="约翰·史密斯", sprites=["wanted8"]),
]

MD_FILE_TO_CHAR = {
    "01": "lun", "02": "lou", "03": "jackson", "04": "landlord", "05": "Georgio",
    "06": "eleanor", "07": "Mentor", "08": "Serpico", "09": "wild", "10": "bailey",
    "11": "butcher", "12": "arthur", "13": "odin", "14": "li", "15": "juan",
    "16": "john", "17": "leroy",
}

# 原版立绘名 → 这属于哪套阶段立绘（决定这张卡该用哪套图）
SPRITE_STAGE = {
    "officer_lun": "常规",
    "inventor": "常规", "dr._jackson": "常规",
    "landlord": "常规", "the_landlord": "常规",
    "george": "常规",
    "mentor": "常规", "the_mentor": "常规",
    "joe": "常规", "joe_wild": "常规", "wild": "常规",
    "eleanor1": "第一阶段 · 维持体面",
    "eleanor2": "第二阶段 · 跌落与振作",
    "eleanor3": "第三阶段 · 真相与选择",
    "eleanor_scav": "拾荒阶段",
    "eleanor_rev": "革命军阶段",
    "serpico": "制服阶段",
    "serpico_scav": "拾荒阶段",
    "serpico_rev": "结局阶段",
    "wanted5": "普通装",
    "lou_sec": "执法者伪装",
    "odin": "常规", "wanted4": "常规",
    "wanted2": "常规", "wanted6": "常规", "wanted7": "常规", "wanted9": "常规",
    "wanted1": "常规", "wanted3": "常规", "wanted8": "常规",
}

# 分支名 → 人话
LABELS = {
    "accept": "成交", "wrong_item": "递错物品", "right_item": "递对物品",
    "contraband": "递违禁品", "arrest": "被捕", "arrest_client": "被捕·本人",
    "arrest_sec": "治安人员介入", "choice_accept": "玩家选择·接受",
    "choice_decline": "玩家选择·拒绝", "decline": "拒绝", "drugs": "查获麻醉品",
    "clear": "盘查通过", "no": "否定分支", "bm": "黑市路线", "rev": "革命军路线",
    "sec": "治安部路线", "ask": "发问", "free": "母亲获释", "free_gift": "送出礼物",
    "free_bye": "告别", "rev_gift": "交出礼物", "rev_gun": "索要武器", "rev_bye": "告别",
    "blackinjector_thanks": "谢过黑药", "blackinjector_no": "否认上瘾",
    "blackinjector_no2": "否认上瘾（续）", "black_injector_thanks": "谢过黑药",
    "blackinjector_no_2": "否认上瘾（续）", "thanks": "道谢", "farewell": "告别",
    "offer": "推销报价", "back": "回头补充", "done": "办完", "intro_done": "开场办完",
    "rent_paid": "已交租", "no_grace": "不再宽限", "buyout": "买断",
    "vending": "自动售货", "vending_yes": "同意售货", "vending_last": "最后一次",
    "warning": "警告", "plea": "求饶", "tools": "工具交易", "supply": "供货",
    "supply_leave": "供货·离开", "supply_none": "供货·没有", "supply_enough": "供货·足够",
    "finale": "终局", "to_go": "打包带走", "to": "打包带走",
    "scav": "拾荒", "scav_enough": "拾荒·够了", "scav_all_done": "拾荒·全做完",
    "warned": "被警告", "warned_leave": "被警告·离开", "no_family": "没有家人",
    "even": "凑够货款", "cash": "现金", "short": "钱不够", "wait": "等一下",
    "progress": "进度", "remaining": "还剩", "enough": "够了", "cash_all": "全部现金",
    "promise": "承诺", "accept_small": "小额放贷", "accept_large": "大额放贷",
    "payback": "还钱", "payback_done": "还完", "luck_intro": "开运开场",
    "luck_sale": "开运销售", "nuclear_waste": "核废料", "backpack": "背包",
    "assistance": "帮忙", "serpico_letter": "塞尔皮科的信", "box_collect": "箱子·取走",
    "box_taken": "箱子·已拿", "box_peeked": "箱子·偷看", "box_untouched": "箱子·没动",
    "box_see_you": "箱子·被发现", "display_case": "展柜", "display_case_reminder": "展柜提醒",
    "end_bm": "黑市结局", "end_rev": "革命军结局", "end_sec": "治安部结局",
    "intro2": "续", "intro_accept": "开场成交", "module_accept": "模块成交",
    "storage": "仓库", "module": "模块", "greet": "招呼", "vandal": "破坏",
    "return": "回头客", "choice_return": "选择·归还", "choice_leak": "选择·泄露",
    "leak": "选择·泄露", "demand": "索款", "demand_bomb": "炸弹威胁",
    "box_drop": "箱子·放下", "box_collect": "箱子·收取",
    "disguise": "伪装", "disguise_dismiss": "伪装·被赶走",
    "intro_player": "玩家发问", "player": "玩家台词", "ask": "发问",
}

# 这些段落名不是独立的一场对话，并回主卡当分支
BRANCH_BASES = {
    "accept", "choice", "player", "decline", "thanks", "return", "leak",
    "arrest", "no", "clear", "drugs", "wrong", "right", "contraband",
    "refuse", "player_decl", "player_mach", "player_water", "player_pay",
    "player_nopay", "player_buyout", "player_choice",
}

IDENT_RE = re.compile(r'\.identifier\s*=\s*"([^"]+)"')
# 对象初始化器写法：new StoreClient { identifier = "x", spriteName = "y" }
IDENT_OBJ_RE = re.compile(r'(?<![\w.])identifier\s*=\s*"([^"]+)"')
SPRITE_RE = re.compile(r'\.spriteName\s*=\s*(?:"([^"]*)"|(\w[\w.]*)\(\))')
SPRITE_OBJ_RE = re.compile(r'(?<![\w.])spriteName\s*=\s*(?:"([^"]*)"|(\w[\w.]*)\(\))')
KEY_PATTERNS = [
    re.compile(r'GetLocalizedDialogue\(\s*"([^"]+)"'),
    re.compile(r'GetLocalizedDialoguePhone\(\s*"([^"]+)"'),
    re.compile(r'GetLocalizedOrDefault\(\s*"[^"]*"\s*,\s*"([^"]+)"'),
]
METHOD_RE = re.compile(r'^\t(?:public|private|protected|internal)[^\n]*?\b(\w+)\s*\([^)]*\)\s*$')
RETURN_LITERAL_RE = re.compile(r'^\s*return\s+"([^"]*)"\s*;\s*$')
SECTION_RE = re.compile(r'^###\s+(.+?)（(\d+)）\s*$')
ZH_RE = re.compile(r'^- (.*)$')
EN_RE = re.compile(r'^\s+- \*(.*)\*$')


# ---------------- 基础解析 ----------------
def read_lines(path):
    with open(path, encoding="utf-8-sig") as fh:
        return fh.read().split("\n")


def find_methods(lines):
    out, i, n = [], 0, len(lines)
    while i < n - 1:
        m = METHOD_RE.match(lines[i])
        if m and lines[i + 1].strip() == "{":
            j, depth = i + 1, 0
            while j < n:
                depth += lines[j].count("{") - lines[j].count("}")
                if depth == 0:
                    break
                j += 1
            out.append((m.group(1), i, min(j + 1, n)))
            i = j + 1
        else:
            i += 1
    return out


def sprite_helpers(lines):
    table = {}
    for name, s, e in find_methods(lines):
        lits = [RETURN_LITERAL_RE.match(l).group(1) for l in lines[s:e] if RETURN_LITERAL_RE.match(l)]
        if len(lits) == 1:
            table[name] = lits[0]
    return table


def md_files():
    if not os.path.isdir(TALK):
        return []
    return [(os.path.join(TALK, fn), MD_FILE_TO_CHAR[fn[:2]])
            for fn in sorted(os.listdir(TALK))
            if fn.endswith(".md") and fn[:2] in MD_FILE_TO_CHAR]


def parse_md_file(path):
    lines = read_lines(path)
    order, secs = [], {}
    cur_token, cur_sec, cur = None, None, None
    for line in lines:
        m = SECTION_RE.match(line)
        if m:
            head = m.group(1).strip()
            tok, sec = (head.split(" / ", 1) if " / " in head else (head, ""))
            cur_token, cur_sec = tok.strip(), sec.strip()
            if cur_token not in secs:
                secs[cur_token] = {}
                order.append(cur_token)
            cur = secs[cur_token].setdefault(cur_sec, [])
            continue
        if cur_token is None:
            continue
        em = EN_RE.match(line)
        if em:
            if cur:
                cur[-1]["en"] = em.group(1)
            continue
        zm = ZH_RE.match(line)
        if zm and not line.startswith("  "):
            cur.append({"zh": zm.group(1), "en": ""})
    return [(t, secs[t]) for t in order]


def parse_talk():
    out = {}
    for path, _ck in md_files():
        for token, secs in parse_md_file(path):
            out.setdefault(token, {})
            for sec, rows in secs.items():
                out[token].setdefault(sec, rows)
    return out


def speaker_of(key):
    if "arrest_sec" in key or key.endswith("_sec"):
        return "npc"
    if "choice" in key:
        return "player"
    return "char"


# ---------------- 源码扫描 ----------------
def context_of(lines, start, line_no):
    """主对话 / 成交 / 反应:字段"""
    if "acceptDealDialogue" in lines[line_no]:
        return "成交"
    for k in range(line_no, start, -1):
        cur = lines[k]
        lm = re.search(r'storeClient\.(\w+)\s*=\s*\(.*=>', cur)
        if lm:
            return "主对话" if lm.group(1) == "mainDialogue" else "反应:" + lm.group(1)
        if "storeClient.mainDialogue" in cur:
            return "主对话"
        nm = re.search(r'storeClient\.(\w+)\s*=\s*new Dialogue', cur)
        if nm:
            return "其它:" + nm.group(1)
    return "主对话"


# 条件人话化：先匹配代码特征
COND_MAP = [
    (r'!\s*storeClient\.IsClientBuyingThisItem', "递的不是他要的类型"),
    (r'storeClient\.IsClientBuyingThisItem', "递的是他要的类型"),
    (r'ContrabandHelper\.IsContraband', "物品属于违禁品"),
    (r'IsGameItemType\("NARCOTIC"\)', "物品是麻醉品"),
    (r'IsGameItemType\("MEDICAL"\)', "物品是医疗品"),
    (r'IsGameItemType\("FOOD"\)', "物品是食物"),
    (r'arrest' , "被捕流程"),
]
IF_RE = re.compile(r'^if\s*\((.*)\)\s*$')
ELSE_IF_RE = re.compile(r'^else\s+if\s*\((.*)\)\s*$')


def humanize_cond(cond):
    if not cond:
        return ""
    if cond == "else":
        return "其余情况"
    for pat, label in COND_MAP:
        if re.search(pat, cond):
            return label
    return cond


def enclosing_cond(lines, depths, start, idx):
    """找包住这一行最内层的 if 条件（-1 层缩进处最近的那个 if/else）。"""
    target = depths[idx] - 1
    for j in range(idx - 1, start - 1, -1):
        if depths[j] != target:
            continue
        s = lines[j].strip()
        m = IF_RE.match(s)
        if m:
            return m.group(1).strip()
        m = ELSE_IF_RE.match(s)
        if m:
            return "else if " + m.group(1).strip()
        if s == "else":
            return "else"
    return ""


SETTEXT_RE = re.compile(r'SetText\(\s*([^,]+?)\s*,')


def settext_arg(lines, idx, pos):
    """找这一句台词对应的 SetText 第一参数（说话人）。同一行优先，否则往回找 6 行。"""
    m = None
    for mm in SETTEXT_RE.finditer(lines[idx][:pos]):
        m = mm
    if m:
        return m.group(1).strip()
    for j in range(idx - 1, max(-1, idx - 7), -1):
        last = None
        for mm in SETTEXT_RE.finditer(lines[j]):
            last = mm
        if last:
            return last.group(1).strip()
    return ""


def scan_code(lines, helpers, depths):
    """key -> {first, events[list], sprite, spriteExpr, ctx, cond, spkArg}"""
    methods = find_methods(lines)

    # 先给每个方法记下它自己的 identifier / spriteName（含对象初始化器写法）
    meta = {}
    for name, s, e in methods:
        body = "\n".join(lines[s:e])
        ids = IDENT_RE.findall(body)
        sm = SPRITE_RE.search(body)
        if "new StoreClient" in body:
            ids = ids + [x for x in IDENT_OBJ_RE.findall(body) if x not in ids]
            if sm is None:
                sm = SPRITE_OBJ_RE.search(body)
        sprite, expr = None, None
        if sm:
            if sm.group(1) is not None:
                sprite, expr = sm.group(1), '"%s"' % sm.group(1)
            else:
                expr = sm.group(2)
                sprite = helpers.get(sm.group(2).split(".")[-1])
        meta[name] = (ids, sprite, expr)

    FACTORY_RE = re.compile(r'(?:[\w.]+\.)?(\w+)\("([A-Za-z0-9_]+)"\)')

    info = {}
    for name, s, e in methods:
        ids, sprite, sprite_expr = meta[name]
        if not ids or sprite is None:
            # 由工厂方法创建：CreateLouTalkClient("louBoxDrop") 这种
            for m in FACTORY_RE.finditer("\n".join(lines[s:e])):
                fac, arg = m.group(1), m.group(2)
                if fac == name or fac not in meta:
                    continue
                f_ids, f_sprite, f_expr = meta[fac]
                if f_sprite is None:
                    continue
                if not ids:
                    ids = [arg]
                if sprite is None:
                    sprite, sprite_expr = f_sprite, f_expr
                break
        for idx in range(s, e):
            hits = []
            for pat in KEY_PATTERNS:
                for m in pat.finditer(lines[idx]):
                    hits.append((m.start(), m.group(1)))
            hits.sort()
            for pos, key in hits:
                rec = info.get(key)
                if rec is None:
                    cond = enclosing_cond(lines, depths, s, idx)
                    rec = info[key] = dict(first=idx, events=[], sprite=None, spriteExpr=None,
                                           ctx=context_of(lines, s, idx),
                                           cond=cond, condHuman=humanize_cond(cond),
                                           spkArg=settext_arg(lines, idx, pos))
                for i in ids:
                    if i not in rec["events"]:
                        rec["events"].append(i)
                if rec["sprite"] is None and sprite:
                    rec["sprite"] = sprite
                if rec["spriteExpr"] is None and sprite_expr:
                    rec["spriteExpr"] = sprite_expr
    return info


def section_keys(code_keys, token, section, n_lines):
    prefix = "dialog_" + token + "_" + (section + "_" if section else "")
    exact = ("dialog_" + token + "_" + section) if section else None
    numbered = sorted([k for k in code_keys if k.startswith(prefix) and k[len(prefix):].isdigit()],
                      key=lambda k: int(k[len(prefix):]))
    idxs = [int(k[len(prefix):]) for k in numbered]
    if exact and exact in code_keys:
        if idxs and idxs[0] == 1:
            return numbered, "code"
        return [exact] + numbered, "code"
    if numbered:
        return numbered, "code"
    if exact:
        return [exact], "guess"
    return [prefix + str(i + 1) for i in range(n_lines)], "guess"


def humanize(section):
    if not section:
        return ""
    if section in LABELS:
        return LABELS[section]
    best = None
    for k in LABELS:
        if section.endswith("_" + k) and (best is None or len(k) > len(best)):
            best = k
    if best:
        return LABELS[best]
    return section.replace("_", " ")


# ---------------- 主流程 ----------------
def dup_groups(portraits, thresh=26.0):
    """同角色同一张原图内，把主体几乎一样的切片聚成一簇，只做标注。
    原画有时把两格画得几乎一样（如乔治奥 01/03），标出来省得在两张一样的图之间选。"""
    try:
        import numpy as np
        from PIL import Image
    except Exception:
        return
    story = [p for p in portraits if p["kind"] == "story"]
    if len(story) < 2:
        return
    sigs = {}
    for p in story:
        path = os.path.join(MAT, p["id"])
        try:
            im = Image.open(path).convert("RGBA")
        except Exception:
            continue
        bb = im.getbbox()
        if bb:
            im = im.crop(bb)
        w = 128
        h = max(1, int(w * im.height / im.width))
        sigs[p["id"]] = np.asarray(im.resize((w, h), Image.LANCZOS), dtype=np.int16)

    parent = {k: k for k in sigs}

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[rb] = ra

    by_sheet = {}
    for p in story:
        if p["id"] in sigs:
            by_sheet.setdefault(p["sheet"], []).append(p["id"])
    for ids in by_sheet.values():
        for i in range(len(ids)):
            for j in range(i + 1, len(ids)):
                a, b = sigs[ids[i]], sigs[ids[j]]
                hh, ww = min(a.shape[0], b.shape[0]), min(a.shape[1], b.shape[1])
                aa, bb = a[:hh, :ww], b[:hh, :ww]
                m = (aa[..., 3] > 128) & (bb[..., 3] > 128)
                if m.sum() < 200:
                    continue
                if float(np.abs(aa[..., :3] - bb[..., :3])[m].mean()) < thresh:
                    union(ids[i], ids[j])

    clusters = {}
    for p in story:
        if p["id"] in sigs:
            clusters.setdefault(find(p["id"]), []).append(p)
    for items in clusters.values():
        if len(items) > 1:
            for p in items:
                p["dup"] = [q["label"] for q in items if q is not p]


def story_order(lines):
    """StoreClientUniqueList 的注册顺序表 = 作者排的剧情顺序。"""
    order, idx = {}, 0
    for i in range(len(lines) - 2):
        m = re.match(r'^\s*"([A-Za-z0-9_]+)",\s*$', lines[i])
        if not m:
            continue
        if not re.match(r'^\s*[A-Za-z_][\w.]*\.[A-Za-z_]\w*\s*$', lines[i + 1]):
            continue
        if lines[i + 2].strip() != "},":
            continue
        if m.group(1) not in order:
            order[m.group(1)] = idx
            idx += 1
    return order


# 分支/条件用词 → 想要的表情用词（自动补全用）
INTENT = [
    (["递错", "wrong", "否定", "不对", "不行"], ["否认", "强装", "为难", "尴尬", "拒绝", "摇头", "躲闪"]),
    (["递对", "right", "满意", "认可"], ["得意", "笑", "认可", "收下", "满意", "点头"]),
    (["违禁", "contraband"], ["为难", "警惕", "拒绝", "犹豫"]),
    (["被捕", "arrest", "抓"], ["慌张", "辩解", "紧张", "害怕", "冒汗", "无辜"]),
    (["受伤", "shot", "痛", "救命"], ["受伤", "痛苦", "虚弱", "捂"]),
    (["警告", "威胁", "warn"], ["威胁", "严肃", "警告", "冷"]),
    (["求", "plea", "饶"], ["求饶", "可怜", "慌张", "哀求"]),
    (["谢", "thanks"], ["笑", "温和", "感谢", "点头"]),
    (["成交", "accept"], ["笑", "满意", "点头", "认可"]),
    (["推销", "介绍", "offer"], ["得意", "介绍", "展示", "笑"]),
    (["盘查", "inspect", "检查"], ["严肃", "盘查", "警惕"]),
    (["告别", "bye", "离开"], ["告别", "转身", "平静"]),
]


def score_portrait(label, text):
    lab = re.sub(r'^\d+[_\-\s]*', '', label or "")
    s = 0
    for n in (4, 3, 2):
        for i in range(0, max(0, len(lab) - n + 1)):
            if lab[i:i + n] in text:
                s += n
    for keys, wants in INTENT:
        if any(k in text for k in keys) and any(w in lab for w in wants):
            s += 7
    return s


MANUAL_PATH = os.path.join(HERE, "manual_map.json")


def load_manual():
    if not os.path.exists(MANUAL_PATH):
        return {}
    try:
        raw = json.load(open(MANUAL_PATH, encoding="utf-8-sig"))
    except Exception as ex:
        print("!! 手工表读取失败（本次不会用人工定图）:", ex)
        return {}
    out = {k: v for k, v in raw.items() if not k.startswith("_")}
    print("人工定图: %d 个角色 / %d 句" % (len(out), sum(len(v) for v in out.values())))
    return out


def manual_pick(manual, char_key, line_key, portraits):
    """人工定的图优先：值是 label（可去掉编号前缀）"""
    val = (manual.get(char_key) or {}).get(line_key)
    if not val:
        return None
    v = str(val).strip()
    for p in portraits:
        lab = p.get("label") or ""
        if lab == v or lab.endswith(v) or v in lab:
            return p
    return None


_COLOR_CACHE = {}


def mean_color(rel_id):
    """人物主体平均颜色，用来把基本立绘和该阶段切片对上（缓存）"""
    if rel_id in _COLOR_CACHE:
        return _COLOR_CACHE[rel_id]
    val = None
    try:
        import numpy as np
        from PIL import Image
        im = Image.open(os.path.join(MAT, rel_id)).convert("RGBA")
        bb = im.getbbox()
        if bb:
            im = im.crop(bb)
        im = im.resize((48, 48))
        a = np.asarray(im, dtype=np.float32)
        m = a[..., 3] > 128
        if m.sum() >= 50:
            val = a[..., :3][m].mean(axis=0)
    except Exception:
        val = None
    _COLOR_CACHE[rel_id] = val
    return val


def closest_base(bases, ref_ids):
    """在几张基本立绘里挑颜色最接近参考切片的那张"""
    try:
        import numpy as np
    except Exception:
        return None
    refs = [mean_color(i) for i in ref_ids]
    refs = [r for r in refs if r is not None]
    if not refs or not bases:
        return None
    target = np.mean(np.stack(refs), axis=0)
    best, best_d = None, 1e9
    for p in bases:
        c = mean_color(p["id"])
        if c is None:
            continue
        d = float(np.linalg.norm(c - target))
        if d < best_d:
            best_d, best = d, p
    return best


def main():
    lines = read_lines(CS)
    depths, d = [], 0
    for line in lines:
        depths.append(d)
        d += line.count("{") - line.count("}")
    helpers = sprite_helpers(lines)
    code = scan_code(lines, helpers, depths)
    code_keys = set(code.keys())
    story = story_order(lines)
    print(f"源码取词点: {len(code)} 个 key，其中有判定条件 "
          f"{sum(1 for v in code.values() if v.get('cond'))} 个；剧情顺序表 {len(story)} 条")

    loc = {}
    if os.path.exists(LOC):
        loc = {r["key"]: r for r in json.load(open(LOC, encoding="utf-8"))}

    per_char = {c["key"]: [] for c in CHARACTERS}
    for path, ck in md_files():
        for token, secs in parse_md_file(path):
            per_char[ck].append((token, secs))

    all_keys = set()
    guessed, dropped = [], []
    report = []

    for c in CHARACTERS:
        records = []                       # 全部台词行（未分卡）
        for token, secs in per_char[c["key"]]:
            for sec, rows in secs.items():
                keys, how = section_keys(code_keys, token, sec, len(rows))
                for i in range(max(len(rows), len(keys))):
                    key = keys[i] if i < len(keys) else "dialog_" + token + "_" + (sec + "_" if sec else "") + str(i + 1)
                    if key in all_keys:
                        dropped.append(key)
                        continue
                    all_keys.add(key)
                    if i < len(rows):
                        zh, en = rows[i]["zh"], rows[i]["en"]
                    else:
                        r = loc.get(key) or {"zh": "", "en": ""}
                        zh, en = r.get("zh", ""), r.get("en", "")
                    if how == "guess":
                        guessed.append(key)
                    ci = code.get(key) or {}
                    records.append(dict(key=key, zh=zh, en=en, spk=speaker_of(key),
                                        section=sec, secNorm=re.sub(r'\d+$', '', sec or ""),
                                        token=token,
                                        events=list(ci.get("events") or []),
                                        sprite=ci.get("sprite"),
                                        spriteExpr=ci.get("spriteExpr"),
                                        spkArg=ci.get("spkArg", ""),
                                        ctx=ci.get("ctx", "主对话"),
                                        cond=ci.get("cond", ""),
                                        condHuman=ci.get("condHuman", ""),
                                        order=ci.get("first", 10 ** 9)))

        # 分卡：有客户端事件就按第一个事件归卡，没有就按 token
        cards = {}
        for r in records:
            if r["events"]:
                cid = r["events"][0]
            else:
                # 没有客户端事件（电话/地图等）：按段落首词再分一次，避免几十句日常挤一卡
                base = re.sub(r'\d+$', '', (r["section"] or "").split("_")[0])
                cid = r["token"] + ("/" + base if base else "")
            card = cards.get(cid)
            if card is None:
                card = cards[cid] = dict(id=cid, lines=[], sprite=None, spriteExpr=None,
                                         order=10 ** 9, noId=not r["events"], base=base if not r["events"] else None)
            card["lines"].append(r)
            card["order"] = min(card["order"], r["order"])
            if card["spriteExpr"] is None and r.get("spriteExpr"):
                card["spriteExpr"] = r["spriteExpr"]
            if card["sprite"] is None and r.get("sprite"):
                card["sprite"] = r["sprite"]

        own_sprites = set(c["sprites"])

        # 选择/玩家/被捕/道谢 这类不是独立流程，并回本 token 的主卡
        for token in {r["token"] for r in records}:
            root = cards.get(token)
            if not root:
                continue
            for cid in list(cards):
                if cid == token or not cid.startswith(token + "/"):
                    continue
                sub = cards[cid]
                if sub.get("base") in BRANCH_BASES:
                    root["lines"].extend(sub["lines"])
                    root["order"] = min(root["order"], sub["order"])
                    if root["spriteExpr"] is None:
                        root["spriteExpr"] = sub["spriteExpr"]
                    if root["sprite"] is None:
                        root["sprite"] = sub["sprite"]
                    del cards[cid]

        events = []
        for cid, card in cards.items():
            card["lines"].sort(key=lambda x: x["order"])
            # 这张卡到底是不是本人在说话
            expr = card["spriteExpr"]
            if expr and "GetRandom" in expr:
                foreign = True                     # 通用随机 NPC 立绘
            elif card["sprite"] and card["sprite"] not in own_sprites:
                foreign = True                     # 明确是别人的立绘（如 maleBM5）
            else:
                foreign = False                    # 本人，或是无立绘的电话/地图对话
            can_arrest = any("arrest" in (r["section"] or "") for r in card["lines"])

            # 先定每句是谁在说
            for r in card["lines"]:
                key = r["key"]
                a = r.get("spkArg") or ""
                if "arrest_sec" in key or key.endswith("_sec"):
                    spk = "npc"
                elif a == '"SEC"':
                    spk = "npc"
                elif a == '"PLAYER"' or "choice" in key:
                    spk = "player"
                elif foreign:
                    spk = "npc"
                else:
                    spk = "char"
                r["spk"] = spk

            # 主对话段 = 本人台词最多的那段，并列取名字最短的
            sec_cnt = {}
            for r in card["lines"]:
                if r["spk"] == "char":
                    sec_cnt[r["secNorm"]] = sec_cnt.get(r["secNorm"], 0) + 1
            main_sec = None
            if sec_cnt.get(""):
                main_sec = ""                      # 有无后缀的主段，就是它
            elif sec_cnt and max(sec_cnt.values()) > 0:
                top = max(sec_cnt.values())
                main_sec = min([s for s, n in sec_cnt.items() if n == top], key=len)

            for r in card["lines"]:
                ctx = str(r["ctx"])
                r["main"] = (r["secNorm"] == main_sec) and not ctx.startswith("反应") and ctx != "成交"
                r["alsoIn"] = [e for e in r["events"] if e != cid]
                label = humanize(r["secNorm"])
                if not r["main"] and not label:
                    label = "交易反应" if ctx.startswith("反应") else (ctx if ctx else "分支")
                r["label"] = "" if r["main"] else label
            events.append(dict(id=cid, title=cid, sprite=card["sprite"],
                               stage=("" if (foreign or not card["sprite"])
                                      else SPRITE_STAGE.get(card["sprite"], card["sprite"])),
                               noId=card["noId"], order=card["order"],
                               canArrest=can_arrest, foreign=foreign,
                               lines=card["lines"]))
        # 剧情顺序：注册表里的按表顺序；表里没有的卡，按代码位置插进相邻两张之间
        table_cards = sorted((e["order"], story[e["id"]]) for e in events if e["id"] in story)
        for e in events:
            if e["id"] in story:
                e["_key"] = float(story[e["id"]])
            else:
                prev = [s for (o, s) in table_cards if o <= e["order"]]
                nxt = [s for (o, s) in table_cards if o > e["order"]]
                if prev and nxt:
                    e["_key"] = (max(prev) + min(nxt)) / 2.0
                elif prev:
                    e["_key"] = max(prev) + 0.5
                elif nxt:
                    e["_key"] = min(nxt) - 0.5
                else:
                    e["_key"] = 0.0
        LASTA = ("conclusion", "finale", "outro", "payback", "arrest")

        def is_last(e):
            low = e["id"].lower()
            return any(k in low for k in LASTA)

        events.sort(key=lambda e: (1 if is_last(e) else 0, e["_key"], e["order"], e["id"]))
        for e in events:
            e.pop("order")
            e.pop("_key", None)
            for r in e["lines"]:
                r.pop("order")
                r.pop("events", None)
                r.pop("sprite", None)
                r.pop("spriteExpr", None)
                r.pop("spkArg", None)
                r.pop("token", None)
                r.pop("section", None)
                r.pop("ctx", None)
                if r["main"] or not r.get("cond"):
                    r.pop("cond", None)
                    r.pop("condHuman", None)

        # 兜底补漏：游戏里有、但 md 漏掉的对话（人工登记在 extra_events.json）
        extra_path = os.path.join(HERE, "extra_events.json")
        if os.path.exists(extra_path):
            try:
                extra = json.load(open(extra_path, encoding="utf-8-sig"))
            except Exception as ex:
                print("!! extra_events.json 读取失败:", ex)
                extra = {}
            for ev in extra.get(c["key"], []):
                if any(e["id"] == ev["id"] for e in events):
                    continue
                rows = []
                for i, l in enumerate(ev["lines"]):
                    rows.append(dict(key=l["key"], zh=l["zh"], en=l.get("en", ""), spk="char",
                                     main=(i == 0), label=""))
                events.append(dict(id=ev["id"], sprite=ev.get("sprite", ""), stage=ev.get("stage", ""),
                                   canArrest=ev.get("canArrest", False), foreign=False, lines=rows))
                print("补充事件:", c["key"], ev["id"], len(rows), "句")

        report.append(dict(key=c["key"], name=c["name"], folder=c["folder"], sprites=c["sprites"],
                           portraits=dedupe_portraits(scan_portraits(c["folder"])), events=events))

    # ---- 给每一句定一个默认图：人工表优先，其余按规则兜底 ----
    manual = load_manual()
    filled = 0
    manual_hit = 0
    for c in report:
        story = [p for p in c["portraits"] if p["kind"] == "story"]
        base = [p for p in c["portraits"] if p["kind"] == "base"]

        def sheet_match(pool, parts):
            return [p for p in pool if any(k in (p.get("sheet") or "") for k in parts)]

        def label_match(pool, parts):
            return [p for p in pool if any(k in (p.get("label") or "") for k in parts)]

        last_parts = []
        char_rot = 0                       # 打分打平时轮着用，别全塞同一张
        for ev in c["events"]:
            if ev["foreign"]:
                continue
            parts = []
            for x in (ev.get("stage") or "").split("·"):
                x = x.strip()
                if not x:
                    continue
                parts.append(x)
                if x.endswith("阶段") and len(x) > 2:
                    parts.append(x[:-2])
            if parts:
                if "伪装" not in (ev.get("stage") or ""):
                    last_parts = parts      # 伪装只是个别场次，别让后面的电话卡沿用
            else:
                parts = last_parts          # 电话/地图这类没阶段，沿用前一张卡的阶段
            # 先认原张名字，再认单格名字；都不中才退回全部
            cands = (sheet_match(story, parts) or sheet_match(base, parts)
                     or label_match(story, parts) or label_match(base, parts)
                     or story or base)
            # 某些阶段没有专属切片，借用同衣服的其它套（只影响候选，不动素材）
            SHEET_BORROW = {"eleanor": {"拾荒阶段": "修正服装"}}
            for st_kw, sh_kw in SHEET_BORROW.get(c["key"], {}).items():
                if st_kw in (ev.get("stage") or ""):
                    borrowed = [p for p in story if sh_kw in (p.get("sheet") or "")]
                    if borrowed:
                        cands = borrowed
                    break
            if not cands:
                continue
            first = None
            for l in ev["lines"]:
                if l["spk"] != "char":
                    continue
                text = (l.get("zh") or "") + (l.get("label") or "") + (l.get("condHuman") or "") + (l.get("cond") or "")
                pick = manual_pick(manual, c["key"], l["key"], c["portraits"])
                if pick is not None:
                    manual_hit += 1
                    l["def"] = pick["id"]
                    filled += 1
                    if first is None:
                        first = l["def"]
                    continue
                best, best_s = None, -1
                for p in cands:
                    sc = score_portrait(p["label"], text)
                    if sc > best_s:
                        best_s, best = sc, p
                if best_s <= 0:
                    best = cands[char_rot % len(cands)]
                    char_rot += 1
                else:
                    char_rot = (cands.index(best) + 1) % len(cands)
                l["def"] = best["id"]
                filled += 1
                if first is None:
                    first = l["def"]
            if first:
                # 进场/退场默认用「该阶段的基本立绘」，不是切片
                # 手工指定的阶段→基础立绘（新图换过服装、名字又对不上时用）
                BASE_FIX = {"eleanor": {"第三阶段 · 真相与选择": "埃莉诺_第四阶段_拾荒者"}}
                base_pick = None
                for fix_label, fix_base in BASE_FIX.get(c["key"], {}).items():
                    if ev.get("stage") == fix_label:
                        base_pick = next((p for p in base if fix_base in (p.get("label") or "")), None)
                        break
                if base_pick is None and parts:
                    base_pick = next((p for p in base if any(k in (p.get("label") or "") for k in parts)), None)
                if base_pick is None:
                    refs = [l["def"] for l in ev["lines"] if l["spk"] == "char" and l.get("def")]
                    base_pick = closest_base(base, refs)      # 名字对不上就按颜色配
                if base_pick is None and base:
                    base_pick = base[0]
                entry_pic = base_pick["id"] if base_pick else first
                ev["defEnter"] = entry_pic
                labels = []
                for l in ev["lines"]:
                    if l["spk"] == "char" and not l["main"] and l.get("label") and l["label"] not in labels:
                        labels.append(l["label"])
                defex = [{"label": "自然退场", "id": entry_pic}]
                for bl in labels:
                    defex.append({"label": "走完「" + bl + "」后", "id": entry_pic})
                if ev["canArrest"]:
                    defex.append({"label": "被捕带走", "id": entry_pic})
                ev["defExits"] = defex

    # 同一角色同一阶段，进场默认图统一成多数票（防止个别卡颜色匹配跑偏）
    for c in report:
        votes = {}
        for ev in c["events"]:
            if ev.get("defEnter") and ev.get("stage"):
                votes.setdefault(ev["stage"], []).append(ev["defEnter"])
        chosen = {}
        for st, ids in votes.items():
            chosen[st] = max(set(ids), key=ids.count)
        for ev in c["events"]:
            st = ev.get("stage")
            if st in chosen and ev.get("defEnter"):
                ev["defEnter"] = chosen[st]
                for ex in ev.get("defExits") or []:
                    ex["id"] = chosen[st]

    # 没有独立立绘的剧情对话（导师到访/教程、王尔德规矩、卢的后续留言）：
    # 标注成「沿用前一阶段的服装」，而不是编一个不存在的电话剧情
    for c in report:
        last = ""
        for ev in c["events"]:
            if ev["foreign"]:
                continue
            if ev.get("stage"):
                last = ev["stage"]
            else:
                ev["stage"] = (last + "（沿用）") if last else "无固定立绘"

    total = sum(len(e["lines"]) for c in report for e in c["events"])
    no_zh = [r["key"] for c in report for e in c["events"] for r in e["lines"] if not r["zh"]]
    print(f"角色 {len(report)} / 对话卡 {sum(len(c['events']) for c in report)} / 台词 {total} / 无文本 {len(no_zh)}")
    print(f"猜 key {len(guessed)} / 跳过重复 {len(dropped)} / 默认图 {filled} 句")
    if no_zh:
        print("无文本:", no_zh[:6])
    for c in report:
        n = sum(len(e["lines"]) for e in c["events"])
        print(f"  {c['name']:<9} 卡{len(c['events']):>3} 台词{n:>4} 立绘{len(c['portraits']):>3}")

    out_js = os.path.join(OUT_ROOT, "data.js")
    with open(out_js, "w", encoding="utf-8") as fh:
        fh.write("// 自动生成，勿手改。Build %s\n" % BUILD)
        fh.write("window.WB_DATA = ")
        json.dump(dict(build=BUILD, characters=report), fh, ensure_ascii=False, separators=(",", ":"))
        fh.write(";\n")
    print("写出:", out_js, os.path.getsize(out_js) // 1024, "KB")


def dedupe_portraits(ps):
    """同 label 的立绘只留一张：优先名字带「修正」/v2 的那套（新修好的），旧的仍留在磁盘上"""
    def score(p):
        s = (p.get("sheet") or "").lower()
        return 1 if ("修正" in s or "v2" in s) else 0
    best = {}
    for p in ps:
        k = p.get("label") or ""
        if k not in best or score(p) > score(best[k]):
            best[k] = p
    return [p for p in ps if best.get(p.get("label")) is p]


def scan_portraits(folder):
    out, base = [], os.path.join(MAT, folder)
    for kind, sub in (("base", "02_已认可基础立绘"), ("story", "05_复核切割单张_v2")):
        root = os.path.join(base, sub)
        if not os.path.isdir(root):
            continue
        for dirpath, _, files in os.walk(root):
            for fn in sorted(files):
                if not fn.lower().endswith(".png"):
                    continue
                full = os.path.join(dirpath, fn)
                in_sheet = os.path.abspath(dirpath) != os.path.abspath(root)
                out.append(dict(
                    id=os.path.relpath(full, MAT).replace("\\", "/"),
                    label=os.path.splitext(fn)[0],
                    sheet=os.path.basename(dirpath) if in_sheet else "",
                    kind=kind))
    return out


if __name__ == "__main__":
    main()
