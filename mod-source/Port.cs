using System.Reflection;
using HarmonyLib;


using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using UObject = UnityEngine.Object;



using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
namespace LaunchGiftPortraits;

public sealed class PortraitMod : MelonMod
{
    private static readonly Dictionary<string, string> Assets = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Texture2D> Textures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Sprite> Sprites = new();
    private static readonly HashSet<string> Failures = new();
    private static readonly HashSet<string> Reported = new();
    private static int scanFrames;
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["officer_lun"] = "lun", ["dr._jackson"] = "jackson", ["inventor"] = "jackson",
        ["odin"] = "wanted4", ["george"] = "Georgio", ["joe"] = "wild", ["joe_wild"] = "wild", ["the_landlord"] = "landlord", ["the_mentor"] = "Mentor",
    };

    public override void OnInitializeMelon()
    {
        foreach (string resource in typeof(PortraitMod).Assembly.GetManifestResourceNames())
            if (resource.StartsWith("Portraits/") && resource.EndsWith(".png"))
                Assets[Path.GetFileNameWithoutExtension(resource)] = resource;
        if (Assets.Count != 34)
            throw new InvalidDataException($"Expected 34 portrait/scene/board resources, found {Assets.Count}");
        MelonLogger.Msg("Launch Gift 1.2.0: 17 characters, 23 portraits + 1 shop scene + wanted board + 9 UI headshots. Unlisted sprites stay vanilla. No save writes.");
        StoryPortraits.StoryMod.Boot();
        scanFrames = 120;
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName) => scanFrames = 120;
    public override void OnLateUpdate()
    {
        DisplaySettings.Tick();
        if (scanFrames > 0 && --scanFrames % 15 == 0)
            RepairVisibleSprites();
    }

    internal static void ScheduleScan() => scanFrames = 120;

    private static string? Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string clean = Path.GetFileNameWithoutExtension(name.Replace('\\', '/'));
        if (Assets.ContainsKey(clean)) return clean;
        return Aliases.TryGetValue(clean, out var alias) ? alias : null;
    }

    internal static Sprite? Replace(Sprite? original, string? hint = null)
    {
        // Preserve geometry and leave missing/unknown original sprites untouched.
        if (original == null || original.name.StartsWith("LaunchGift/")) return original;
        string? key = Resolve(original.name) ?? Resolve(hint);
        if (key == null) return original;
        Rect rect = original.rect;
        Vector2 pivot = original.pivot;
        if (rect.width <= 0 || rect.height <= 0) return original;
        string cacheKey = FormattableString.Invariant($"{key}|{rect.width}|{rect.height}|{pivot.x}|{pivot.y}|{original.pixelsPerUnit}");
        if (Sprites.TryGetValue(cacheKey, out var cached) && cached != null) return cached;
        if (Failures.Contains(key)) return original;
        try
        {
            if (!Textures.TryGetValue(key, out var texture) || texture == null)
            {
                using var stream = typeof(PortraitMod).Assembly.GetManifestResourceStream(Assets[key])
                    ?? throw new InvalidDataException($"Missing embedded PNG: {key}");
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes.ToArray()))
                {
                    UObject.Destroy(texture);
                    throw new InvalidDataException($"PNG decode failed: {key}");
                }
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                UObject.DontDestroyOnLoad(texture);
                Textures[key] = texture;
            }
            float scaleX = texture.width / rect.width;
            float scaleY = texture.height / rect.height;
            if (Math.Abs(scaleX - scaleY) > .01f)
                throw new InvalidDataException($"Aspect ratio mismatch for {key}: native {rect.width}x{rect.height}");
            var replacement = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(pivot.x / rect.width, pivot.y / rect.height), original.pixelsPerUnit * scaleX);
            if (replacement == null) throw new InvalidDataException($"Sprite creation failed: {key}");
            replacement.name = "LaunchGift/" + key;
            UObject.DontDestroyOnLoad(replacement);
            Sprites[cacheKey] = replacement;
            if (Reported.Add(key)) MelonLogger.Msg($"Replaced {key}; native bounds and pivot preserved.");
            return replacement;
        }
        catch (Exception ex)
        {
            Failures.Add(key);
            MelonLogger.Error($"{key}: {ex.Message}; using vanilla sprite.");
            return original;
        }
    }

    internal static void RepairVisibleSprites()
    {
        // 遍历全场景修"场景图 / 通缉令 / UI 头像"，但**排除客人自己那个渲染器**。
        // 排除它是因为客人的待机动画帧名字会撞上资产名（如 landlord），
        // 一旦被按名替换，整段动画就变成别的角色。客人立绘走 RepairClient()。
        try
        {
            var clientA = StoreClientMono.Instance != null ? StoreClientMono.Instance.image : null;
            var clientB = StoreUIManager.Instance != null ? StoreUIManager.Instance.clientGameObject?.image : null;

            foreach (var renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
            {
                if (renderer == null || renderer == clientA || renderer == clientB) continue;
                var before = renderer.sprite;
                var after = Replace(before);
                if (after != before) renderer.sprite = after;
            }
            foreach (var image in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (image == null) continue;
                var before = image.overrideSprite;
                var after = Replace(before);
                if (after != before) image.overrideSprite = after;
                before = image.sprite;
                after = Replace(before);
                if (after != before) image.sprite = after;
            }
        }
        catch (Exception ex)
        {
            if (Failures.Add("scene-scan")) MelonLogger.Warning($"Scene repair: {ex.Message}");
        }
    }

    internal static void RepairClient(StoreUIManager? ui = null)
    {
        try
        {
            var renderer = ui != null ? ui.clientGameObject?.image : StoreClientMono.Instance?.image;
            if (renderer == null) return;
            string? hint = PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint()?.spriteName;
            var before = renderer.sprite;
            var after = Replace(before, hint);
            if (after != before) renderer.sprite = after;
        }
        catch (Exception ex)
        {
            if (Failures.Add("client-repair")) MelonLogger.Warning($"Client portrait: {ex.Message}");
        }
    }
}

// Patch only the game's sprite lookup entry points, not inventory, economy or saves.
[HarmonyPatch]
internal static class LookupHooks
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var targets = new (Type Type, string Name)[]
        {
            (typeof(SpriteDict), "GetSprite"), (typeof(RenderHandler), "LoadFromAtlas"),
            (typeof(RenderHandler), "LoadFromFile"), (typeof(DialogBubbleList), "GetSprite"),
            (typeof(CustomUIManager), "GetSprite"), (typeof(CustomUIManager), "GetModSprite"),
        };
        foreach (var target in targets)
        {
            var methods = target.Type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Where(m => m.Name == target.Name && m.ReturnType == typeof(Sprite)).ToArray();
            if (methods.Length == 0) MelonLogger.Warning($"Missing lookup hook: {target.Type.Name}.{target.Name}");
            foreach (var method in methods) yield return method;
        }
    }

    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(ref Sprite __result)
    {
        var replacement = PortraitMod.Replace(__result);
        if (replacement != null) __result = replacement;
    }
}

[HarmonyPatch(typeof(StoreUIManager), "OnNextClientArrived")]
internal static class ArrivalHook
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(StoreUIManager __instance)
    {
        DisplaySettings.RebindPortrait();
        PortraitMod.RepairClient(__instance);
    }
}

[HarmonyPatch(typeof(MapUIManager), "ActivateClientOutsideStore")]
internal static class OutsideHook
{
    [HarmonyPrefix]
    private static void Prefix() => PortraitMod.RepairVisibleSprites();
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        PortraitMod.RepairClient();
        PortraitMod.ScheduleScan();
    }
}
