# 深空娘化 · 素材、工具与源码

《深空当铺：可能是偷的 / Probably Stolen》人物美化 Mod「深空娘化：可能有点好看」的公开仓库。

Mod 工坊页：https://steamcommunity.com/sharedfiles/filedetails/?id=3828971372

## 目录

| 目录 | 内容 |
|---|---|
| `demo_*.png` / `base/` | Mod 展示图：实机演示、17 位角色的默认立绘（多阶段并排） |
| `workbench/` | 台词立绘工作台（纯前端，双击 `index.html` 打开），用来给每句台词配图 |
| `mod-source/` | Mod 源码：`Port.cs`（基础立绘）、`Story.cs`（剧情差分）、`Settings.cs`（F10 显示设置）、`StoryMap.json`（台词 → 立绘映射） |

## 工作台怎么用

1. 打开 `workbench/index.html`
2. 左侧选角色 → 中间选对话卡 → 右侧点图
3. 顶栏「导出映射 JSON」，交给打包脚本即可

## 说明：源码是怎么来的

Mod 源码是在**反编译分析游戏 `Assembly-CSharp.dll`**（用于确认方法签名、精灵名与调用链）之后编写的自制代码，没有复制游戏原始实现。游戏本体、原始美术与音频资源的版权归原作者 / 发行商所有；本仓库只包含自制修改代码、自制立绘与配置数据。使用本仓库代码需自行拥有正版游戏。

## 许可

- 立绘与图像素材：**CC BY-NC 4.0**（署名—非商业性使用）
- 工具与源码：**AGPL-3.0**（GNU Affero General Public License v3.0，见 `LICENSE`）

立绘为 AI 生成后手工修整；未覆盖的角色保持游戏原版。
