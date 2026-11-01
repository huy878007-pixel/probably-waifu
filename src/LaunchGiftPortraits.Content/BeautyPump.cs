using UnityEngine;

namespace LaunchGiftPortraits.Content;

/// <summary>
/// Drives this Mod's own presentation layer: the F10 hotkey, per-frame offsets and the settings
/// window. It also retries the core handshake, because the native loader can enable this Mod before
/// the core assembly is loaded.
/// </summary>
public sealed class BeautyPump : MonoBehaviour
{
    public static BeautyPump Instance { get; private set; }
    int nextBind;

    void Awake() => Instance = this;

    void Update()
    {
        if (!Ready()) return;
        var settings = BeautyContentMod.Settings;
        if (settings == null) return;
        if (Input.GetKeyDown(settings.HotkeyCode)) settings.Toggle();
        settings.Tick();
    }

    void LateUpdate()
    {
        if (!Ready()) return;
        var settings = BeautyContentMod.Settings;
        if (settings == null) return;
        if (PortraitPolicy.LastPath != null && !CoreBridge.IsApplied()) PortraitPolicy.Reassert();
        settings.CaptureBubble(DialogUIManager.Instance);
        settings.Apply(PortraitPolicy.LastPath != null);
    }

    /// <summary>Bind to the core, retrying a few times a second until it exists.</summary>
    bool Ready()
    {
        if (CoreBridge.Bound) return true;
        if (Time.frameCount < nextBind) return false;
        nextBind = Time.frameCount + 30;
        return BeautyContentMod.TryBind();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        BeautyContentMod.Settings?.Restore();
    }
}
