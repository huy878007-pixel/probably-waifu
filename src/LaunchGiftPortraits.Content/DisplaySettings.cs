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
    [JsonProperty("portraitScale")] public float PortraitScale { get; set; } = 1;
    [JsonProperty("portraitX")] public float PortraitX { get; set; }
    [JsonProperty("portraitY")] public float PortraitY { get; set; }
    [JsonProperty("bubbleX")] public float BubbleX { get; set; }
    [JsonProperty("bubbleY")] public float BubbleY { get; set; }
    [JsonProperty("nameX")] public float NameX { get; set; }
    [JsonProperty("nameY")] public float NameY { get; set; }
    [JsonProperty("hotkey")] public string Hotkey { get; set; } = "F10";

    [JsonIgnore] public bool HasPortraitAdjustment => PortraitX != 0 || PortraitY != 0 || PortraitScale != 1;
    [JsonIgnore] public KeyCode HotkeyCode => Enum.TryParse(Hotkey, true, out KeyCode value) ? value : KeyCode.F10;
    [JsonIgnore] public bool IsOpen => window != null && window.IsAlive && window.IsVisible;

    string path;
    GameObject blocker;
    CustomUIWindow window;
    CustomUIBuilder builder;
    Transform portrait, bubble, name;
    Vector3 portraitBase, portraitSize, bubbleBase, nameBase;
    object camera;
    bool savedUpscale, cameraOwned, portraitOwned;

    public void Load(string file)
    {
        path = file;
        if (File.Exists(file))
        {
            try { JsonConvert.PopulateObject(File.ReadAllText(file), this); }
            catch (JsonException e) { Warn("Invalid ui.json; using available defaults: " + e.Message); }
        }
        Normalize();
    }
    /// <summary>Keep every value inside the range the native window offers, and sanitise NaN.</summary>
    public void Normalize()
    {
        PortraitScale = Clamp(PortraitScale, .6f, 1.6f, 1);
        PortraitX = Clamp(PortraitX, -300, 300, 0); PortraitY = Clamp(PortraitY, -300, 300, 0);
        BubbleX = Clamp(BubbleX, -600, 600, 0); BubbleY = Clamp(BubbleY, -600, 600, 0);
        NameX = Clamp(NameX, -600, 600, 0); NameY = Clamp(NameY, -600, 600, 0);
    }
    static float Clamp(float value, float min, float max, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));

    public void Save()
    {
        if (path == null) return;
        try { File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented)); }
        catch (Exception e) { Warn("Cannot save ui.json: " + e.Message); }
    }
    /// <summary>Write to a new location and remember it, used when migrating the legacy core file.</summary>
    public void Save(string file) { path = file; Save(); }

    public void CaptureBubble(DialogUIManager ui)
    {
        if (ui == null) return;
        if (bubble != ui.clientBubble?.transform) { bubble = ui.clientBubble?.transform; if (bubble != null) bubbleBase = bubble.localPosition; }
        if (name != ui.clientNameBubble?.transform) { name = ui.clientNameBubble?.transform; if (name != null) nameBase = name.localPosition; }
    }

    /// <summary>Applies preferences to the live portrait. Called every LateUpdate by the Mod's pump.</summary>
    public void Apply(bool replaced)
    {
        var image2 = StoreClientMono.Instance?.image;
        var target = image2 != null ? image2.transform : null;
        if (target != portrait) { ResetPortrait(); portrait = target; }
        bool moving = StoreClientMono.Instance != null && StoreClientMono.Instance.IsMoving();
        if (portrait != null && replaced && HasPortraitAdjustment && !moving && image2.gameObject.activeInHierarchy)
        {
            if (!portraitOwned) { portraitBase = portrait.localPosition; portraitSize = portrait.localScale; }
            portrait.localPosition = portraitBase + new Vector3(PortraitX, PortraitY);
            portrait.localScale = portraitSize * PortraitScale;
            portraitOwned = true;
        }
        else if (portraitOwned) ResetPortrait();

        if (bubble != null && (BubbleX != 0 || BubbleY != 0)) bubble.localPosition = bubbleBase + new Vector3(BubbleX, BubbleY);
        if (name != null && (NameX != 0 || NameY != 0)) name.localPosition = nameBase + new Vector3(NameX, NameY);

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

    public void ResetPortrait()
    {
        if (portraitOwned && portrait != null) { portrait.localPosition = portraitBase; portrait.localScale = portraitSize; }
        portraitOwned = false;
        if (portrait != null && portrait != StoreClientMono.Instance?.image?.transform) portrait = null;
    }

    public void Restore()
    {
        if (blocker != null) UObj.Destroy(blocker);
        blocker = null;
        RestoreCamera();
        ResetPortrait();
        if (bubble != null) bubble.localPosition = bubbleBase;
        if (name != null) name.localPosition = nameBase;
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
        builder.AddSpace(2f);
        builder.AddHeader("对话框", null);
        SliderRow(builder, "气泡 X", -600f, 600f, BubbleX, "bx", value => BubbleX = value);
        SliderRow(builder, "气泡 Y", -600f, 600f, BubbleY, "by", value => BubbleY = value);
        SliderRow(builder, "姓名框 X", -600f, 600f, NameX, "nx", value => NameX = value);
        SliderRow(builder, "姓名框 Y", -600f, 600f, NameY, "ny", value => NameY = value);
        builder.AddSpace(2f);
        builder.AddToggle("高清立绘（关闭像素放大）", Crisp, value => Crisp = value, "crisp");
        builder.AddSpace(2f);
        builder.AddHeader("角色立绘", null);
        SliderRow(builder, "缩放", .6f, 1.6f, PortraitScale, "ps", value => PortraitScale = value);
        SliderRow(builder, "立绘 X", -300f, 300f, PortraitX, "px", value => PortraitX = value);
        SliderRow(builder, "立绘 Y", -300f, 300f, PortraitY, "py", value => PortraitY = value);
        builder.AddSpace(2f);
        builder.BeginRow(6f, TextAnchor.MiddleCenter);
        Fix(builder.AddButton("保存", Save, "save").Last, 96f);
        Fix(builder.AddButton("重置", ResetAll, "reset").Last, 96f);
        Fix(builder.AddButton("关闭", ClosePanel, "close").Last, 96f);
        builder.End();
        window = builder.Show();
        try { window.SetSize(460f, 520f); window.Center(); }
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
        PortraitScale = 1;
        PortraitX = PortraitY = BubbleX = BubbleY = NameX = NameY = 0;
        ResetPortrait();
        RefreshValues();
        Save();
    }

    void RefreshValues()
    {
        foreach (var (id, value) in new[] { ("bx", BubbleX), ("by", BubbleY), ("nx", NameX), ("ny", NameY), ("ps", PortraitScale), ("px", PortraitX), ("py", PortraitY) })
        {
            if (window == null || !window.IsAlive) return;
            window.Get("v_" + id)?.SetText(Fmt(value));
        }
    }

    void SliderRow(CustomUIBuilder target, string label, float min, float max, float value, string id, Action<float> onChanged)
    {
        target.BeginRow(6f, TextAnchor.MiddleLeft);
        target.AddLabel(label, null);
        Fix(target.Last, 64f);
        target.AddLabel(Fmt(value), "v_" + id);
        Fix(target.Last, 48f);
        target.AddSlider(min, max, value, changed =>
        {
            onChanged(changed);
            if (window != null && window.IsAlive) window.Get("v_" + id)?.SetText(Fmt(changed));
        }, false, id);
        Fix(target.Last, 250f);
        target.End();
    }

    static string Fmt(float value) => value.ToString("0");

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
