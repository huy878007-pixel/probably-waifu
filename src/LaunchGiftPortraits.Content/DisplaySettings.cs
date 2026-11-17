using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;
using UObj = UnityEngine.Object;

namespace LaunchGiftPortraits.Content;

/// <summary>
/// Presentation preferences owned by this content Mod: portrait offset and scale, bubble and name
/// placement, crispness, and the native F10 window. Saved next to this DLL so the core runtime keeps
/// no beauty state at all.
/// </summary>
public sealed class DisplaySettings
{
    const string WindowId = "launchgift_settings";

    [JsonProperty("crisp")] public bool Crisp { get; set; } = true;
    [JsonProperty("ordinaryCustomerBeauty")] public bool OrdinaryCustomerBeauty { get; set; } = true;
    /// <summary>跨存档的用户配置：初次设置引导还没完成（或从未跑过）。</summary>
    [JsonProperty("guidePending")] public bool GuidePending { get; set; } = true;
    /// <summary>独立于引导完成状态：玩家允许小小日常来采购才进入排程。</summary>
    [JsonProperty("xiaoxiaoVisitsEnabled")] public bool XiaoxiaoVisitsEnabled { get; set; }
    // 默认值即用户确认的外观：大立绘、对话框下移、不翻转。已有 ui.json 里的数值优先。
    [JsonProperty("portraitSize")] public string PortraitSize { get; set; } = "large";
    [JsonProperty("portraitScale")] public float PortraitScale { get; set; } = 1;
    [JsonProperty("portraitX")] public float PortraitX { get; set; }
    [JsonProperty("portraitY")] public float PortraitY { get; set; }
    [JsonProperty("bubbleX")] public float BubbleX { get; set; } = 104;
    [JsonProperty("bubbleY")] public float BubbleY { get; set; } = -182;
    [JsonProperty("bubbleFlipY")] public bool BubbleFlipY { get; set; }
    [JsonProperty("nameX")] public float NameX { get; set; } = 104;
    [JsonProperty("nameY")] public float NameY { get; set; } = -182;
    [JsonProperty("hotkey")] public string Hotkey { get; set; } = "F10";

    [JsonIgnore] public bool HasPortraitAdjustment => PortraitX != 0 || PortraitY != 0 || PortraitScale != 1;
    [JsonIgnore] public KeyCode HotkeyCode => Enum.TryParse(Hotkey, true, out KeyCode value) ? value : KeyCode.F10;
    [JsonIgnore] public bool IsOpen => window != null && window.IsAlive && window.IsVisible;

    string path;
    GameObject blocker;
    CustomUIWindow window;
    CustomUIBuilder builder;
    Transform bubble, name;
    Image bubbleBackground, nameBackground;
    BubbleFrameFlip bubbleFlip, nameFlip;
    readonly BubbleTextFlip bubbleTextFlip = new(), nameTextFlip = new();
    readonly BubblePlacement placement = new();
    object camera;
    bool savedUpscale, cameraOwned;

    public void Load(string file)
    {
        path = file;
        bool migrated = false;
        if (File.Exists(file))
        {
            try
            {
                var text = File.ReadAllText(file);
                JsonConvert.PopulateObject(text, this);
                if (Newtonsoft.Json.Linq.JObject.Parse(text)["portraitSize"] == null)
                {
                    if (!File.Exists(file + ".pre-fit.bak")) File.Copy(file, file + ".pre-fit.bak");
                    PortraitScale = 1; PortraitX = PortraitY = 0;
                    migrated = true;
                }
            }
            catch (JsonException e) { Warn("Invalid ui.json; using available defaults: " + e.Message); }
        }
        // 上方阅读整体上移 20：气泡与姓名同步移动，保持原有的相对关系。只认精确的旧预设数值。
        if (BubbleFlipY && BubbleX == 87 && NameX == 87 && NameY == 98 && (BubbleY == 170 || BubbleY == 190))
        {
            if (!File.Exists(file + ".pre-upper-shift.bak")) { try { File.Copy(file, file + ".pre-upper-shift.bak"); } catch (IOException) { } }
            BubbleY = 190; NameY = 118;
            migrated = true;
            Warn("Upper preset shifted up: bubble 190 / name 118");
        }
        Normalize();
        if (migrated) Save();
    }
    /// <summary>Keep every value inside the range the native window offers, and sanitise NaN.</summary>
    public void Normalize()
    {
        if (PortraitSize != "small" && PortraitSize != "medium" && PortraitSize != "large") PortraitSize = "large";
        PortraitScale = Clamp(PortraitScale, .5f, 2f, 1);
        // The portrait lives in world units (~2 units tall), so tens of units already leave the frame.
        PortraitX = Clamp(PortraitX, -1, 1, 0); PortraitY = Clamp(PortraitY, -1, 1, 0);
        BubbleX = Clamp(BubbleX, -400, 400, 0); BubbleY = Clamp(BubbleY, -400, 400, 0);
        NameX = Clamp(NameX, -400, 400, 0); NameY = Clamp(NameY, -400, 400, 0);
    }
    static float Clamp(float value, float min, float max, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));

    public void Save()
    {
        if (path == null) return;
        try
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            Warn(FormattableString.Invariant($"Display settings saved: bubbleFlipY={BubbleFlipY} bubble=({BubbleX},{BubbleY}) name=({NameX},{NameY})"));
        }
        catch (Exception e) { Warn("Cannot save ui.json: " + e.Message); }
    }
    /// <summary>Write to a new location and remember it, used when migrating the legacy core file.</summary>
    public void Save(string file) { path = file; Save(); }

    public void SetOrdinaryCustomerBeauty(bool enabled)
    {
        if (OrdinaryCustomerBeauty == enabled) return;
        OrdinaryCustomerBeauty = enabled;
        SpritePresenter.Clear();
        PortraitPolicy.RefreshPreferences();
        Warn("Ordinary customer beauty: " + enabled);
    }

    /// <summary>引导完成或主动跳过后才关闭；中途退出保留 true，下次来客还会遇到她。</summary>
    public void SetGuidePending(bool pending)
    {
        if (GuidePending == pending) return;
        GuidePending = pending;
        Save();
        RefreshValues();
        Warn("Xiaoxiao guide pending: " + pending);
        ModSettingsBridge.SyncFromSettings();
    }

    public void SetXiaoxiaoVisits(bool enabled)
    {
        if (XiaoxiaoVisitsEnabled == enabled) return;
        XiaoxiaoVisitsEnabled = enabled;
        Save();
        RefreshValues();
        Warn("Xiaoxiao visits allowed: " + enabled);
        ModSettingsBridge.SyncFromSettings();
    }

    /// <summary>
    /// 三种气泡/姓名框预设。立绘大小与预设无关，保留在 <see cref="PortraitSize"/> 里。
    /// 这三组数值是用户已确认的正式配置，不要自行"校正"；用户可以在 F10 里继续微调。
    /// </summary>
    public void ApplyBubblePreset(string preset)
    {
        placement.Restore();
        switch (preset)
        {
            case "vanilla": BubbleX = BubbleY = NameX = NameY = 0; BubbleFlipY = false; break;
            case "lower": BubbleX = NameX = 104; BubbleY = NameY = -182; BubbleFlipY = false; break;
            case "upper": BubbleX = NameX = 87; BubbleY = 190; NameY = 118; BubbleFlipY = true; break;
            default: Warn("Unknown bubble preset: " + preset); return;
        }
        ApplyBubbleFlip();
        placement.Apply(BubbleX, BubbleY, BubbleFlipY, NameX, NameY);
        RefreshValues();
        Warn("Bubble preset: " + preset);
    }

    public void CaptureBubble(DialogUIManager ui)
    {
        if (ui == null) return;
        var nextBubble = ui.clientBubble != null ? ui.clientBubble.transform : null;
        bubble = nextBubble;
        var nextName = ui.clientNameBubble != null ? ui.clientNameBubble.transform : null;
        name = nextName;
        placement.Bind(ui);
        CaptureFrame(ui.clientBubble != null ? ui.clientBubble.backgroundImage : null, ref bubbleBackground, ref bubbleFlip);
        CaptureFrame(ui.clientNameBubbleImage, ref nameBackground, ref nameFlip);
        bubbleTextFlip.Bind(ui.clientBubble != null && ui.clientBubble.textfield != null ? ui.clientBubble.textfield.rectTransform : null, bubbleBackground != null ? bubbleBackground.rectTransform : null);
        nameTextFlip.Bind(ui.clientNameTMP != null ? ui.clientNameTMP.rectTransform : null, nameBackground != null ? nameBackground.rectTransform : null);
    }

    static void CaptureFrame(Image next, ref Image current, ref BubbleFrameFlip flip)
    {
        if (current == next) return;
        if (flip != null) flip.enabled = false;
        current = next;
        flip = null;
    }

    static void ApplyFrameFlip(Image image, ref BubbleFrameFlip flip, bool enabled)
    {
        if (image == null) return;
        if (enabled && flip == null)
            flip = image.GetComponent<BubbleFrameFlip>() ?? image.gameObject.AddComponent<BubbleFrameFlip>();
        if (flip != null && flip.enabled != enabled) flip.enabled = enabled;
    }

    void ApplyBubbleFlip()
    {
        ApplyFrameFlip(bubbleBackground, ref bubbleFlip, BubbleFlipY);
        ApplyFrameFlip(nameBackground, ref nameFlip, BubbleFlipY);
        bubbleTextFlip.Apply(BubbleFlipY);
        nameTextFlip.Apply(BubbleFlipY);
    }

    /// <summary>Applies preferences to the live portrait. Called every LateUpdate by the Mod's pump.</summary>
    public void Apply(bool replaced)
    {
        PortraitLayoutPolicy.Update();

        placement.Apply(BubbleX, BubbleY, BubbleFlipY, NameX, NameY);
        ApplyBubbleFlip();

        if (EmporiumEntry.Instance == null) { RestoreCamera(); return; }
        var cam = GameAccess.PixelPerfectCamera();
        if (!ReferenceEquals(cam, camera)) { RestoreCamera(); camera = cam; }
        var prop = camera?.GetType().GetProperty("upscaleRT");
        if (prop == null) return;
        if (Crisp && replaced && !cameraOwned) { savedUpscale = (bool)prop.GetValue(camera); if (savedUpscale) prop.SetValue(camera, false); cameraOwned = true; }
        if ((!Crisp || !replaced) && cameraOwned) RestoreCamera();
    }

    void RestoreCamera()
    {
        if (camera is UObj obj && obj == null) { cameraOwned = false; return; }
        if (cameraOwned && camera != null)
        {
            var prop = camera.GetType().GetProperty("upscaleRT");
            if (prop != null && (bool)prop.GetValue(camera) == false) prop.SetValue(camera, savedUpscale);
        }
        cameraOwned = false;
    }

    public void Restore()
    {
        if (blocker != null) UObj.Destroy(blocker);
        blocker = null;
        RestoreCamera();

        placement.Restore();
        if (bubbleFlip != null) bubbleFlip.enabled = false;
        if (nameFlip != null) nameFlip.enabled = false;
        bubbleTextFlip.Restore();
        nameTextFlip.Restore();
    }

    // ---- native F10 window ---------------------------------------------------

    public void Toggle()
    {
        if (IsOpen) ClosePanel(); else OpenPanel();
    }

    /// <summary>Open the game's own Machinery-styled window with native sliders and buttons.</summary>
    public void OpenPanel()
    {
        var manager = CustomUIManager.Instance;
        if (manager == null) { Warn("No CustomUIManager in this scene; F10 window unavailable."); return; }
        var layer = manager.GetLayerRoot("overlay");
        if (layer == null) { Warn("overlay canvas unavailable; F10 window unavailable."); return; }
        // Transparent full-screen blocker so clicks cannot reach the store behind the window.
        if (blocker == null)
        {
            blocker = new GameObject("LaunchGiftPortraits.Blocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)blocker.transform;
            rect.SetParent(layer, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            var image = blocker.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
        }
        builder = manager.CreateWindow(WindowId, "深空娘化 · 显示设置", "overlay", null, CustomUIManager.Style.Machinery)
            .SetCloseOnEscape(true).SetDraggable(false).OnClose(Cleanup);
        builder.AddLabel("F10 开关此窗口 · Esc 关闭", "hint");
        builder.BeginScrollFlexible();
        builder.AddSpace(2f);
        builder.AddHeader("美化范围", null);
        builder.AddToggle("普通顾客娘化", OrdinaryCustomerBeauty, SetOrdinaryCustomerBeauty, "ordinary_beauty");
        builder.AddLabel("关闭后普通客人恢复原版 · 剧情角色保留美化", "ordinary_hint");
        builder.AddSpace(2f);
        builder.AddHeader("小小", null);
        builder.AddToggle("允许小小日常来访", XiaoxiaoVisitsEnabled, SetXiaoxiaoVisits, "xiaoxiao_visits");
        builder.AddLabel("上层区声望 · 每天最多一次 · 摆上海豹团子概率翻倍", "xiaoxiao_hint");
        builder.AddToggle("重新开启小小引导", GuidePending, SetGuidePending, "guide_pending");
        builder.AddLabel("打开后下一次来客优先见到她 · 完成或跳过后自动关闭", "guide_hint");
        builder.AddSpace(2f);
        builder.AddHeader("对话框", null);
        builder.BeginRow(6f, TextAnchor.MiddleCenter);
        Fix(builder.AddButton("原版位置", () => ApplyBubblePreset("vanilla"), "bubble_vanilla").Last, 130f);
        Fix(builder.AddButton("下移位置", () => ApplyBubblePreset("lower"), "bubble_lower").Last, 130f);
        Fix(builder.AddButton("上方阅读", () => ApplyBubblePreset("upper"), "bubble_upper").Last, 130f);
        builder.End();
        builder.AddToggle("气泡外框上下翻转（文字保持正向）", BubbleFlipY, value =>
        {
            BubbleFlipY = value;
            ApplyBubbleFlip();
            Warn("Bubble frame flip: " + value);
            RefreshNameLock();
        }, "bubble_flip");
        SliderRow(builder, "气泡 X", -400f, 400f, BubbleX, "bx", value => BubbleX = value);
        SliderRow(builder, "气泡 Y", -400f, 400f, BubbleY, "by", value => BubbleY = value);
        SliderRow(builder, "姓名框 X", -400f, 400f, NameX, "nx", value => NameX = value);
        SliderRow(builder, "姓名框 Y", -400f, 400f, NameY, "ny", value => NameY = value);
        builder.AddLabel("上方阅读：姓名框绑定左下角，X/Y 暂停使用", "name_locked");
        builder.AddSpace(2f);
        builder.AddToggle("高清立绘（关闭像素放大）", Crisp, value => Crisp = value, "crisp");
        builder.AddSpace(2f);
        builder.AddHeader("角色立绘", null);
        builder.AddLabel("等比例适配 · 下沿贴齐 · 保留原生动画", "fit_hint");
        builder.BeginRow(6f, TextAnchor.MiddleCenter);
        Fix(builder.AddButton("小", () => SelectSize("small"), "size_small").Last, 130f);
        Fix(builder.AddButton("中", () => SelectSize("medium"), "size_medium").Last, 130f);
        Fix(builder.AddButton("大", () => SelectSize("large"), "size_large").Last, 130f);
        builder.End();
        builder.AddLabel(SizeLabel(), "size_current");
        builder.AddHeader("自由微调", null);
        builder.AddLabel("以所选档位为基础 · 1.00 为标准倍率 · 正 X 向右 / 正 Y 向上", "fine_hint");
        SliderRow(builder, "倍率", .5f, 2f, PortraitScale, "ps", value => PortraitScale = value);
        SliderRow(builder, "水平 X", -1f, 1f, PortraitX, "px", value => PortraitX = value);
        SliderRow(builder, "垂直 Y", -1f, 1f, PortraitY, "py", value => PortraitY = value);
        builder.AddButton("重置微调", () => SelectSize(PortraitSize), "reset_fine");
        builder.End(); // scroll content; keep the save/close buttons outside it
        builder.AddSpace(2f);
        builder.BeginRow(6f, TextAnchor.MiddleCenter);
        Fix(builder.AddButton("保存", Save, "save").Last, 96f);
        Fix(builder.AddButton("重置", ResetAll, "reset").Last, 96f);
        Fix(builder.AddButton("关闭", ClosePanel, "close").Last, 96f);
        builder.End();
        window = builder.Show();
        RefreshNameLock();
        try { window.SetSize(560f, 560f); window.Center(); }
        catch (Exception e) { Warn("Window sizing failed: " + e.Message); }
        try
        {
            // The native header drag is unreliable for mod windows; drive the frame ourselves.
            var frame = window.frame;
            if (frame != null && frame.dragHandle != null) frame.dragHandle.enabled = false;
            foreach (var handler in window.root.GetComponentsInChildren<UIDragHandler>(true)) handler.enabled = false;
            var header = window.root.transform.Find("header");
            if (header != null) header.gameObject.AddComponent<PanelDrag>().target = (RectTransform)window.root.transform;
        }
        catch (Exception e) { Warn("Header drag unavailable: " + e.Message); }
    }

    string SizeLabel() => "当前：" + (PortraitSize == "small" ? "小" : PortraitSize == "large" ? "大" : "中");

    public void SelectSize(string size)
    {
        PortraitSize = size;
        PortraitScale = 1;
        PortraitX = PortraitY = 0;
        PortraitLayoutPolicy.Update();
        RefreshValues();
        Save();
    }

    public void ClosePanel()
    {
        if (window != null && window.IsAlive) window.Close();
        Cleanup();
    }

    void Cleanup()
    {
        window = null;
        builder = null;
        if (blocker != null) { UObj.Destroy(blocker); blocker = null; }
    }

    /// <summary>Pull the window back when it is dragged out of reach. Called every frame by the pump.</summary>
    public void Tick()
    {
        if (window == null || !window.IsAlive) { if (blocker != null && !IsOpen) Cleanup(); return; }
        var rect = window.Rect;
        if (rect == null) return;
        if (Math.Abs(rect.anchoredPosition.x) > Screen.width * .6f || Math.Abs(rect.anchoredPosition.y) > Screen.height * .6f) window.Center();
    }

    void ResetAll()
    {
        Crisp = true;
        SetOrdinaryCustomerBeauty(true);
        PortraitSize = "medium";
        PortraitScale = 1;
        PortraitX = PortraitY = BubbleX = BubbleY = NameX = NameY = 0;
        BubbleFlipY = false;

        RefreshValues();
        Save();
    }

    void RefreshNameLock()
    {
        if (window == null || !window.IsAlive) return;
        foreach (var id in new[] { "nx", "ny", "i_nx", "i_ny" })
            window.Get(id)?.SetInteractable(!BubbleFlipY);
        window.Get("name_locked")?.SetVisible(BubbleFlipY);
    }

    void RefreshValues()
    {
        RefreshNameLock();
        window?.Get("size_current")?.SetText(SizeLabel());
        window?.Get("bubble_flip")?.SetBool(BubbleFlipY);
        window?.Get("ordinary_beauty")?.SetBool(OrdinaryCustomerBeauty);
        window?.Get("crisp")?.SetBool(Crisp);
        window?.Get("xiaoxiao_visits")?.SetBool(XiaoxiaoVisitsEnabled);
        window?.Get("guide_pending")?.SetBool(GuidePending);
        foreach (var (id, value) in new[] { ("bx", BubbleX), ("by", BubbleY),  ("ps", PortraitScale), ("px", PortraitX), ("py", PortraitY) })
        {
            if (window == null || !window.IsAlive) return;
            window.Get("i_" + id)?.SetString(Fmt(id, value));
            window.Get("v_" + id)?.SetText(Fmt(id, value));
            window.Get(id)?.SetFloat(value);   // keep the handle in sync after a reset
        }
    }

    void SliderRow(CustomUIBuilder target, string label, float min, float max, float value, string id, Action<float> onChanged)
    {
        target.BeginRow(6f, TextAnchor.MiddleLeft);
        target.AddLabel(label, null);
        Fix(target.Last, 64f);
        // A wide slider cannot give fine steps with the mouse, so the number next to it is editable.
        target.AddInput("", Fmt(id, value), text =>
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var typed))
            {
                Refresh(id);
                return;
            }
            var snapped = Snap(id, Mathf.Clamp(typed, min, max));
            onChanged(snapped);
            var slider = window != null && window.IsAlive ? window.Get(id) : null;
            if (slider != null) slider.SetFloat(snapped);
            Refresh(id);
        }, "i_" + id);
        Fix(target.Last, 72f);
        target.AddSlider(min, max, value, changed =>
        {
            var snapped = Snap(id, changed);
            onChanged(snapped);
            Refresh(id);
        }, false, id);
        Fix(target.Last, 240f);
        target.End();
    }

    float Current(string id) => id switch
    {
        "bx" => BubbleX, "by" => BubbleY, "nx" => NameX, "ny" => NameY,
        "ps" => PortraitScale, "px" => PortraitX, "py" => PortraitY, _ => 0f,
    };

    void Refresh(string id)
    {
        if (window == null || !window.IsAlive) return;
        var text = Fmt(id, Current(id));
        window.Get("i_" + id)?.SetString(text);
        window.Get("v_" + id)?.SetText(text);
    }

    /// <summary>Scale needs decimals; a whole-number format turned 1.05 into 1 and 1.5 into 2.</summary>
    static string Fmt(string id, float value) =>
        value.ToString(id is "ps" or "px" or "py" ? "0.00" : "0");

    /// <summary>Keep the displayed value and fine slider steps consistent.</summary>
    static float Snap(string id, float value) => id switch
    {
        "ps" => (float)Math.Round(value, 2),
        "px" or "py" => (float)Math.Round(value, 2),
        _ => (float)Math.Round(value),
    };

    static void Fix(CustomUIElement element, float width)
    {
        if (element == null || element.root == null) return;
        var layout = element.root.GetComponent<LayoutElement>() ?? element.root.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.flexibleWidth = 0f;
    }

    static void Warn(string text) { try { Debug.Log("[LaunchGiftPortraits] " + text); } catch (Exception) { } }
}

/// <summary>Reads the game's pixel-perfect camera without a compile-time Harmony reference.</summary>
static class GameAccess
{
    public static object PixelPerfectCamera()
    {
        var field = typeof(EmporiumEntry).GetField("pixelPerfectCamera", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return field?.GetValue(EmporiumEntry.Instance);
    }
}








