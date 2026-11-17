using System;

namespace LaunchGiftPortraits.Content;

/// <summary>
/// Publishes this Mod's 小小 switches into the game's Mod settings window. The native panel only
/// lights its button for Mods that registered ModConfig entries; F10 stays the full editor.
/// </summary>
static class ModSettingsBridge
{
    static ConfigEntry<bool> visits, guide;
    static bool syncing;

    public static void Register()
    {
        if (visits != null || guide != null) return;
        var settings = BeautyContentMod.Settings;
        if (settings == null) return;
        try
        {
            var config = ModConfig.For("player.launchgiftportraits");
            visits = config.Get("xiaoxiaoVisits") as ConfigEntry<bool>
                ?? config.Define("xiaoxiaoVisits", settings.XiaoxiaoVisitsEnabled, "小小",
                    () => "允许小小日常来访",
                    () => "允许小小按每日概率来店里采购。关闭后不会日常来访，开场引导不受影响。");
            visits.OnChanged += value => Apply(() => settings.SetXiaoxiaoVisits(value));

            guide = config.Get("guidePending") as ConfigEntry<bool>
                ?? config.Define("guidePending", settings.GuidePending, "小小",
                    () => "下次来客时显示初次引导",
                    () => "打开后，下一个安全来客会触发小小的开场引导；完成或跳过后自动关闭，F10 里可以重新打开。");
            guide.OnChanged += value => Apply(() => settings.SetGuidePending(value));

            SyncFromSettings();
            BeautyContentMod.Warn("Mod settings registered: xiaoxiao x2");
        }
        catch (Exception e)
        {
            visits = null; guide = null;
            BeautyContentMod.Warn("Mod settings unavailable: " + e.Message);
        }
    }

    /// <summary>Mirror changes made in the F10 window into the game panel and its config file.</summary>
    public static void SyncFromSettings()
    {
        if (syncing) return;
        var settings = BeautyContentMod.Settings;
        if (settings == null) return;
        syncing = true;
        try
        {
            visits?.Set(settings.XiaoxiaoVisitsEnabled);
            guide?.Set(settings.GuidePending);
        }
        finally { syncing = false; }
    }

    static void Apply(Action action)
    {
        if (syncing) return;
        syncing = true;
        try { action(); }
        finally { syncing = false; }
    }
}
