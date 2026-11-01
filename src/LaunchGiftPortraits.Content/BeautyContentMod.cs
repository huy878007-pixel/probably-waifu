using UnityEngine;
using UObj = UnityEngine.Object;

namespace LaunchGiftPortraits.Content;

/// <summary>
/// Content-only Mod: this package owns artwork, mapping, presentation policy and settings;
/// the core runtime owns loading, lifecycle notification and the renderer mechanism.
/// No compile-time reference to the core: see <see cref="CoreBridge"/>.
/// </summary>
public sealed class BeautyContentMod : IMod
{
    static DisplaySettings settings;
    GameObject host;
    ModManifest manifest;

    public static DisplaySettings Settings => settings;

    // Native ModLoader owns enable state. The core discovers this Mod's opt-in marker.
    public void Init(ModManifest manifest) { this.manifest = manifest; }

    public void OnEnable()
    {
        // The loader does not honour Prerequisites when ordering: this Mod can be enabled before the
        // core assembly exists. The pump below keeps retrying, so a late bind is normal, not fatal.
        TryBind();
        try
        {
            host = new GameObject("LaunchGiftPortraits.Lifecycle");
            UObj.DontDestroyOnLoad(host);
            host.AddComponent<BeautyPump>();
        }
        catch (Exception e)
        {
            host = null;
            Warn("Presentation host unavailable; behaviour only. " + e.GetType().Name);
        }
        Warn("Enabled; " + Diagnostic());
    }

    /// <summary>Bind once the core exists. Safe and cheap to call repeatedly.</summary>
    public static bool TryBind()
    {
        if (CoreBridge.Bound) return true;
        if (!CoreBridge.Bind()) return false;
        settings ??= LoadSettings();
        SpritePresenter.Bind();
        SpecialTargets.Refresh();
        try { CoreBridge.Sync(); } catch (Exception e) { Warn("Catch-up failed: " + e.Message); }
        Warn("Bound to the core runtime; " + Diagnostic());
        return true;
    }

    public void OnDisable()
    {
        SpecialTargets.Restore();
        SpritePresenter.Unbind();
        if (host != null) { UObj.Destroy(host); host = null; }
        settings?.Restore();
        CoreBridge.Restore();
        CoreBridge.Unbind();
        settings = null;
    }

    /// <summary>
    /// ui.json lives next to this DLL. The legacy core build kept the same values in the core folder;
    /// that file is read once, copied aside, and never overwritten.
    /// </summary>
    static DisplaySettings LoadSettings()
    {
        var folder = Path.GetDirectoryName(typeof(BeautyContentMod).Assembly.Location) ?? ".";
        var own = Path.Combine(folder, "ui.json");
        var legacy = CoreBridge.CoreFolder == null ? null : Path.Combine(CoreBridge.CoreFolder, "ui.json");
        var loaded = new DisplaySettings();
        if (File.Exists(own) || legacy == null || !File.Exists(legacy)) { loaded.Load(own); return loaded; }
        try
        {
            loaded.Load(legacy);
            var backup = legacy + ".core-legacy.bak";
            if (!File.Exists(backup)) File.Copy(legacy, backup, false);
            loaded.Save(own);
            Warn("Migrated presentation settings from the core build into " + own);
        }
        catch (Exception e)
        {
            Warn("Legacy ui.json migration failed, keeping defaults: " + e.Message);
            loaded.Load(own);
        }
        return loaded;
    }

    /// <summary>Diagnostics for the runtime log and the offline contract test.</summary>
    public static string Diagnostic() => "bound=" + CoreBridge.Bound + " version=" + CoreBridge.Version
        + " subscribers=" + CoreBridge.Subscribers + " pump=" + (BeautyPump.Instance != null) + " last=" + CoreBridge.LastEvent;
    public static bool BridgeReady => CoreBridge.Bound;
    public static string LastEvent => CoreBridge.LastEvent;

    internal static void Warn(string text) { try { Debug.Log("[LaunchGiftPortraits] " + text); } catch (Exception) { } }
}
