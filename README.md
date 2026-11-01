# 深空娘化 · 素材、工具与源码

《深空当铺：可能是偷的 / Probably Stolen》人物美化 Mod「深空娘化：可能有点好看」的公开仓库。

Mod 工坊页：https://steamcommunity.com/sharedfiles/filedetails/?id=3828971372

## 这是什么

当前版本 **2.1.7**，是一个**内容 Mod**（`player.launchgiftportraits`）：它带素材、映射和显示策略，运行时能力由另一个仓库 **深空内容核心库** 提供：

- 运行时：https://github.com/huy878007-pixel/PortraitRuntime
- 本仓库：素材 + 内容包 + 美化 Mod 的 C# 代码

两者通过 JSON 桥接通信，本仓库**不引用**运行时的程序集——游戏的原生扫描会拒绝这种跨 Mod 引用。

## 目录

| 目录 | 内容 |
|---|---|
| `packs/core/` | 内容包本体：`pack.json`（17 位角色、669 条剧情配图映射、11 项特殊资源）+ 174 张立绘 |
| `src/LaunchGiftPortraits.Content/` | 美化 Mod 的 C# 代码：取图策略、入场／离场切换、F10 原生设置窗口 |
| `special_targets.json` | 特殊美术（通缉令、通缉头像、博士场景）的场景对象定位表 |
| `base/` / `demo_*.png` | 展示用：17 位角色默认立绘、实机演示截图 |
| `workbench/` | 台词立绘工作台（纯前端，双击 `index.html` 打开） |
| `mod-source/` | **旧版 1.2.5 实现**，仅供对照；当前实现在 `src/` |

## 构建

需要 .NET SDK 10 与本地游戏程序集。`Directory.Build.props` 里的 `GameManaged` 指向游戏目录，可用环境变量覆盖。

```powershell
dotnet build src/LaunchGiftPortraits.Content -c Release
```

产物是 `LaunchGiftPortraits.Content.dll`。打包成可安装的 Mod 需要与运行时仓库配合：把本仓库克隆到运行时的 `beauty/` 目录，再在运行时里跑 `python tools/build_pack.py`。

## 工作台怎么用

1. 打开 `workbench/index.html`
2. 左侧选角色 → 中间选对话卡 → 右侧点图
3. 顶栏「导出映射 JSON」，交给打包脚本即可

## 说明：源码是怎么来的

Mod 源码与映射数据是在**反编译分析官方游戏 `Assembly-CSharp.dll`**（用于确认方法签名、精灵名与调用链）之后编写的自制代码，没有复制游戏原始实现。游戏本体、原始美术与音频资源的版权归原作者 / 发行商所有；本仓库只包含自制修改代码、自制立绘与配置数据。使用本仓库代码需自行拥有正版游戏。

细则见 `NOTICE.md`。

## 许可

- 立绘与图像素材（`packs/core/portraits/`、`packs/core/special/`）：**CC BY-NC 4.0**（署名—非商业性使用），见 `LICENSE-ART`
- 工具、映射与源码：**AGPL-3.0**（GNU Affero General Public License v3.0，见 `LICENSE`）

立绘为 AI 生成后手工修整；未覆盖的角色保持游戏原版。
