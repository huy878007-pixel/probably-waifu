using System;
using HarmonyLib;
using MelonLoader;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LaunchGiftPortraits;

// 显示设置：F10 打开机械风格窗口（CustomUIMachineryStyle），原生滑条 + 透明点击拦截
internal static class DisplaySettings
{
    private const string Tag = "[娘化设置]";
    private const string WindowId = "waifu_settings";

    internal static float BubbleX, BubbleY, NameX, NameY;
    internal static float PortraitScale = 1f, PortraitX, PortraitY;
    internal static KeyCode Hotkey = KeyCode.F10;

    private static bool loaded;
    private static CustomUIWindow window;
    private static CustomUIBuilder builder;
    private static GameObject blockerRoot;

    private static string Folder => System.IO.Path.GetDirectoryName(typeof(DisplaySettings).Assembly.Location);
    private static string ConfigPath => System.IO.Path.Combine(Folder ?? ".", "ui.json");

    private static bool bubbleCaptured;
    private static Transform bubbleT, nameT;
    private static Vector3 bubbleBase, nameBase;

    private static SpriteRenderer portrait;
    private static Vector3 portraitBasePos, portraitBaseScale;
    private static bool portraitDirty;
    private static bool sliderProbeLogged;

    internal static bool IsOpen => window != null && window.IsAlive && window.IsVisible;

    internal static void Load()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            if (!System.IO.File.Exists(ConfigPath)) return;
            var o = JObject.Parse(System.IO.File.ReadAllText(ConfigPath));
            BubbleX = (float?)o["bubbleX"] ?? 0f;
            BubbleY = (float?)o["bubbleY"] ?? 0f;
            NameX = (float?)o["nameX"] ?? 0f;
            NameY = (float?)o["nameY"] ?? 0f;
            PortraitScale = (float?)o["portraitScale"] ?? 1f;
            PortraitX = (float?)o["portraitX"] ?? 0f;
            PortraitY = (float?)o["portraitY"] ?? 0f;
            var hk = (string)o["hotkey"];
            if (!string.IsNullOrEmpty(hk) && Enum.TryParse(hk, true, out KeyCode parsed)) Hotkey = parsed;
            MelonLogger.Msg($"{Tag} 已读取 {ConfigPath}");
        }
        catch (Exception e) { MelonLogger.Warning($"{Tag} 读取配置失败：{e.Message}"); }
    }

    internal static void Save()
    {
        try
        {
            var o = new JObject
            {
                ["bubbleX"] = BubbleX, ["bubbleY"] = BubbleY,
                ["nameX"] = NameX, ["nameY"] = NameY,
                ["portraitScale"] = PortraitScale, ["portraitX"] = PortraitX, ["portraitY"] = PortraitY,
                ["hotkey"] = Hotkey.ToString(),
            };
            System.IO.File.WriteAllText(ConfigPath, o.ToString());
            MelonLogger.Msg($"{Tag} 已保存：{ConfigPath}");
        }
        catch (Exception e) { MelonLogger.Warning($"{Tag} 保存失败：{e.Message}"); }
    }

    internal static void Tick()
    {
        Load();
        if (Input.GetKeyDown(Hotkey)) { if (IsOpen) ClosePanel(); else OpenPanel(); }
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) ClosePanel();
        if (blockerRoot != null && !IsOpen) { MelonLogger.Msg($"{Tag} 检测到窗口已消失（Tick）"); Cleanup(); }
        if (IsOpen)
        {
            var r = window.Rect;
            if (r != null && (Mathf.Abs(r.anchoredPosition.x) > Screen.width * 0.6f || Mathf.Abs(r.anchoredPosition.y) > Screen.height * 0.6f))
            {
                MelonLogger.Msg($"{Tag} 窗口跑出屏幕 {r.anchoredPosition}，拉回中间");
                window.Center();
            }
        }
        if (IsOpen && Time.frameCount % 60 == 0)
        {
            var r = window.Rect;
            MelonLogger.Msg($"{Tag} 窗口位置 {(r != null ? r.anchoredPosition.ToString() : "-")} 可见={window.IsVisible} 存活={window.IsAlive}");
        }
        ApplyBubble();
        ApplyPortrait();
    }

    internal static void RebindPortrait() => portrait = null;

    // ---------------------------------------------------------------- 气泡与名字条
    internal static void CaptureBubble(DialogUIManager ui)
    {
        if (ui == null) return;
        if (ui.clientBubble != null) bubbleT = ui.clientBubble.transform;
        if (ui.clientNameBubble != null) nameT = ui.clientNameBubble.transform;
        if (bubbleT != null) bubbleBase = bubbleT.localPosition;
        if (nameT != null) nameBase = nameT.localPosition;
        bubbleCaptured = true;
    }

    internal static void ApplyBubble()
    {
        if (!bubbleCaptured) return;
        if (bubbleT != null) bubbleT.localPosition = bubbleBase + new Vector3(BubbleX, BubbleY, 0f);
        if (nameT != null) nameT.localPosition = nameBase + new Vector3(NameX, NameY, 0f);
    }

    // ---------------------------------------------------------------- 立绘
    private static void ApplyPortrait()
    {
        var renderer = StoreClientMono.Instance != null ? StoreClientMono.Instance.image : null;
        if (renderer == null) { portrait = null; return; }
        if (renderer != portrait)
        {
            portrait = renderer;
            portraitBasePos = renderer.transform.localPosition;
            portraitBaseScale = renderer.transform.localScale;
            portraitDirty = true;
        }
        if (!portraitDirty) return;
        portraitDirty = false;
        portrait.transform.localPosition = portraitBasePos + new Vector3(PortraitX, PortraitY, 0f);
        portrait.transform.localScale = portraitBaseScale * PortraitScale;
    }

    private static void TouchPortrait() => portraitDirty = true;

    internal static void ResetAll()
    {
        BubbleX = 0f; BubbleY = 0f; NameX = 0f; NameY = 0f;
        PortraitScale = 1f; PortraitX = 0f; PortraitY = 0f;
        TouchPortrait();
        ApplyBubble();
        RefreshValues();
    }

    // ---------------------------------------------------------------- 面板
    private static void OpenPanel()
    {
        var manager = CustomUIManager.Instance;
        if (manager == null) { MelonLogger.Warning($"{Tag} 当前场景没有 CustomUIManager"); return; }

        var layer = manager.GetLayerRoot("overlay");
        if (layer == null) { MelonLogger.Warning($"{Tag} 拿不到 overlay 画布"); return; }

        if (blockerRoot == null)
        {
            blockerRoot = new GameObject("waifu_blocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)blockerRoot.transform;
            rect.SetParent(layer, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = blockerRoot.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
        }

        builder = manager.CreateWindow(WindowId, "深空娘化 · 显示设置", "overlay", null, CustomUIManager.Style.Machinery)
                       .SetCloseOnEscape(true)
                       .SetDraggable(false)   // 根节点拖动会和滑条抢事件，改成只拖标题栏
                       .OnClose(Cleanup);

        var b = builder;
        b.AddLabel($"分辨率 {Screen.width}×{Screen.height}   热键 {Hotkey}", "res");
        b.AddSpace(2f);
        b.AddHeader("对话框");
        SliderRow(b, "气泡 X", -600f, 600f, BubbleX, "bx", v => { BubbleX = v; ApplyBubble(); });
        SliderRow(b, "气泡 Y", -600f, 600f, BubbleY, "by", v => { BubbleY = v; ApplyBubble(); });
        SliderRow(b, "姓名框 X", -600f, 600f, NameX, "nx", v => { NameX = v; ApplyBubble(); });
        SliderRow(b, "姓名框 Y", -600f, 600f, NameY, "ny", v => { NameY = v; ApplyBubble(); });
        b.AddSpace(2f);
        b.AddHeader("角色立绘");
        SliderRow(b, "缩放", 0.6f, 1.6f, PortraitScale, "ps", v => { PortraitScale = v; TouchPortrait(); });
        SliderRow(b, "立绘 X", -300f, 300f, PortraitX, "px", v => { PortraitX = v; TouchPortrait(); });
        SliderRow(b, "立绘 Y", -300f, 300f, PortraitY, "py", v => { PortraitY = v; TouchPortrait(); });
        b.AddSpace(2f);

        b.BeginRow(6f, TextAnchor.MiddleCenter);
        b.AddButton("保存", Save, "save"); Fix(b.Last, 96f);
        b.AddButton("重置", ResetAll, "reset"); Fix(b.Last, 96f);
        b.AddButton("关闭", ClosePanel, "close"); Fix(b.Last, 96f);
        b.End();

        window = b.Show();
        try { window.SetSize(460f, 520f); window.Center(); } catch { }
        try
        {
            var frame = window.frame;
            if (frame != null && frame.dragHandle != null) frame.dragHandle.enabled = false;
            // 壳里所有拖动组件都停用：它们挂在根节点上，还带边缘停靠，会误伤滑条
            int killed = 0;
            foreach (var drag in window.root.GetComponentsInChildren<UIDragHandler>(true))
                if (drag.enabled) { drag.enabled = false; killed++; }
            var header = window.root.transform.Find("header");
            if (header != null) header.gameObject.AddComponent<PanelDrag>().target = (RectTransform)window.root.transform;
            MelonLogger.Msg($"{Tag} 标题栏拖动 header={(header != null)}，停用拖动组件 {killed} 个");
        }
        catch (Exception e) { MelonLogger.Warning($"{Tag} 标题栏拖动挂载失败：{e.Message}"); }
        sliderProbeLogged = false;
        MelonLogger.Msg($"{Tag} 面板已打开（Machinery 460×520）");
    }

    private static void ClosePanel()
    {
        if (window != null && window.IsAlive) window.Close();
        Cleanup();
    }

    // 只做清理：窗口可能已经被右上角 X 关掉
    private static void Cleanup()
    {
        try
        {
            var r = window != null ? window.Rect : null;
            MelonLogger.Msg($"{Tag} Cleanup alive={(window != null && window.IsAlive)} visible={(window != null && window.IsVisible)} pos={(r != null ? r.anchoredPosition.ToString() : "-")}");
            var stack = (Environment.StackTrace ?? string.Empty).Split('\n');
            MelonLogger.Msg($"{Tag} 栈：{(stack.Length > 3 ? stack[3].Trim() : "-")}");
        }
        catch { }
        window = null;
        builder = null;
        if (blockerRoot != null) { UnityEngine.Object.Destroy(blockerRoot); blockerRoot = null; }
    }

    // 一行：名字 | 数值 | 滑条
    private static void SliderRow(CustomUIBuilder b, string label, float min, float max, float value, string id, Action<float> onChanged)
    {
        b.BeginRow(6f, TextAnchor.MiddleLeft);
        b.AddLabel(label); Fix(b.Last, 64f);
        b.AddLabel(Fmt(value), "v_" + id); Fix(b.Last, 48f);
        b.AddSlider(min, max, value, v =>
        {
            if (!sliderProbeLogged)
            {
                sliderProbeLogged = true;
                var r = window != null ? window.Rect : null;
                MelonLogger.Msg($"{Tag} 滑条事件 {id}={v} 窗口存活={(window != null && window.IsAlive)} 可见={(window != null && window.IsVisible)} 位置={(r != null ? r.anchoredPosition.ToString() : "-")}");
            }
            onChanged(v);
            GetById("v_" + id)?.SetText(Fmt(v));
        }, false, id);
        Fix(b.Last, 250f);
        b.End();
    }

    private static CustomUIElement GetById(string id)
    {
        // 不要用 builder.GetWindow()：它的实现里会 window.Hide()，一拖滑条窗口就没了
        if (window == null || !window.IsAlive) return null;
        return window.Get(id);
    }

    private static string Fmt(float v) => v.ToString("0");

    private static void RefreshValues()
    {
        var pairs = new (string id, float v)[]
        {
            ("bx", BubbleX), ("by", BubbleY), ("nx", NameX), ("ny", NameY),
            ("ps", PortraitScale), ("px", PortraitX), ("py", PortraitY),
        };
        foreach (var (id, v) in pairs) GetById("v_" + id)?.SetText(Fmt(v));
    }

    private static void Fix(CustomUIElement element, float width)
    {
        if (element == null || element.root == null) return;
        var layout = element.root.GetComponent<LayoutElement>() ?? element.root.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.flexibleWidth = 0f;
    }
}

// 气泡基准在 Start / 模式切换时重新捕获，偏移由 DisplaySettings 统一施加
[HarmonyPatch(typeof(DialogUIManager))]
internal static class BubbleHooks
{
    [HarmonyPostfix, HarmonyPatch("Start")]
    private static void StartPostfix(DialogUIManager __instance)
    {
        DisplaySettings.CaptureBubble(__instance);
        DisplaySettings.ApplyBubble();
        var cb = __instance.clientBubble;
        if (cb != null)
            MelonLogger.Msg($"[娘化设置] 气泡坐标 {cb.transform.localPosition} 画布 {Screen.width}×{Screen.height}");
    }

    [HarmonyPostfix, HarmonyPatch("SetNormalMode")]
    private static void NormalPostfix(DialogUIManager __instance)
    {
        DisplaySettings.CaptureBubble(__instance);
        DisplaySettings.ApplyBubble();
    }

    [HarmonyPostfix, HarmonyPatch("SetShootingMode")]
    private static void ShootingPostfix(DialogUIManager __instance)
    {
        DisplaySettings.CaptureBubble(__instance);
        DisplaySettings.ApplyBubble();
    }
}
// 只拖标题栏，避免和滑条抢拖动事件
internal sealed class PanelDrag : MonoBehaviour, UnityEngine.EventSystems.IDragHandler, UnityEngine.EventSystems.IPointerDownHandler
{
    internal RectTransform target;

    public void OnPointerDown(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (target != null) target.SetAsLastSibling();
    }

    public void OnDrag(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (target == null) return;
        var canvas = target.GetComponentInParent<Canvas>();
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        if (scale <= 0f) scale = 1f;
        target.anchoredPosition += eventData.delta / scale;
    }
}
