using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LaunchGiftPortraits.Content;

/// <summary>The art pack supplies its silhouettes and the measured native window aperture.</summary>
static class PortraitLayoutPolicy
{
    static JObject profile;
    static bool loaded;
    static string configured;
    static DateTime retryAt;

    static float PresetFill(string size) => size == "large" ? .95f
        : (size == "small" ? .5f : 2f / 3f) * (230f / 185f) * 1.05f;

    public static void Reset() { configured = null; loaded = false; profile = null; retryAt = default; }

    public static void Update()
    {
        if (!CoreBridge.Bound || BeautyContentMod.Settings == null) return;
        if (!loaded)
        {
            loaded = true;
            var root = Path.GetDirectoryName(typeof(BeautyContentMod).Assembly.Location) ?? ".";
            var path = Path.Combine(root, "layout_profiles.json");
            if (File.Exists(path))
            {
                try { profile = JObject.Parse(File.ReadAllText(path)); }
                catch (Exception e) { BeautyContentMod.Warn("Layout profile unavailable: " + e.Message); }
            }
        }
        if (profile == null) return;
        var settings = BeautyContentMod.Settings;
        string stamp = settings.PortraitSize + "/" + settings.PortraitScale + "/" + settings.PortraitX + "/" + settings.PortraitY;
        if (stamp == configured) return;
        if (DateTime.UtcNow < retryAt) return;
        // Small adopts the former medium, medium the former large. Large leaves 5% headroom
        // in the measured opening; the core still applies the width limit and bottom anchor.
        float fill = PresetFill(settings.PortraitSize);
        profile["Fill"] = fill;
        profile["Scale"] = settings.PortraitScale;
        // New presets reset legacy fine offsets. Existing custom configurations remain readable.
        profile["OffsetX"] = settings.PortraitX;
        profile["OffsetY"] = settings.PortraitY;
        if (CoreBridge.ConfigureLayout(profile.ToString(Formatting.None))) configured = stamp;
        else retryAt = DateTime.UtcNow.AddSeconds(1);
    }
}
