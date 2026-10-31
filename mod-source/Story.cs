using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using MelonLoader;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace StoryPortraits;

// 按台词换立绘：只读 StoryMap.json + Story/*.png，不碰存档、经济、物品。
internal static class StoryMod
{
    private static readonly Dictionary<string, string> Lines = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> TextKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> CharOf = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> SpriteChar = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Sprite> Cache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Miss = new(StringComparer.Ordinal);
    private static string Root;
    private static SpriteRenderer bound;     // 我们改过的那个渲染器
    private static Sprite vanilla;           // 它原本的图，离开时还回去
    private static Sprite appliedSprite;     // 我们换上去的那张（用来判断现在还该不该还原）
    private static int appliedCount;

    internal static void Boot()
    {
        if (Root != null) return;
        Root = Path.GetDirectoryName(typeof(StoryMod).Assembly.Location);
        string path = Path.Combine(Root, "StoryMap.json");
        if (!File.Exists(path))
        {
            MelonLogger.Warning("没有 StoryMap.json，只启用基础立绘：" + path);
            return;
        }
        var doc = JObject.Parse(File.ReadAllText(path));
        if (doc["lines"] is JObject lines)
            foreach (var kv in lines) Lines[(string)kv.Key] = (string)kv.Value;
        if (doc["charOf"] is JObject chars)
            foreach (var kv in chars) CharOf[(string)kv.Key] = (string)kv.Value;
        if (doc["spriteOf"] is JObject owners)
            foreach (var kv in owners)
                if (kv.Value is JArray arr)
                    foreach (var s in arr)
                    {
                        string name = (s?.ToString() ?? "").Trim();
                        if (name.Length > 0) SpriteChar[name] = (string)kv.Key;
                    }
        MelonLogger.Msg($"剧情立绘已载入：{Lines.Count} 句台词，{SpriteChar.Count} 个角色代号");
    }

    internal static void NoteText(string key, string text)
    {
        if (string.IsNullOrEmpty(text) || key == null || !Lines.ContainsKey(key)) return;
        TextKey[text] = key;
        string flat = Flat(text);
        if (flat.Length > 0) TextKey[flat] = key;
        string norm = Norm(text);
        if (norm.Length > 3) TextKey[norm] = key;
    }

    private static string Flat(string s) => Regex.Replace(s ?? "", "<[^>]+>", "").Trim();
    private static string Norm(string s) => Regex.Replace(s ?? "", "[^\u4e00-\u9fff]", "");

    // 当前店里这位是不是这句台词的主人：不是就不换，避免串到别的客人身上
    private static bool OwnerMatches(string charKey)
    {
        try
        {
            var blueprint = PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint();
            string name = (blueprint?.spriteName ?? "").Trim();
            if (name.Length == 0) return true;                       // 认不出就放行
            if (!SpriteChar.TryGetValue(name, out string owner)) return true;
            return owner == charKey;
        }
        catch
        {
            return true;
        }
    }

    internal static void Apply(Dialogue dialogue)
    {
        if (dialogue == null || Lines.Count == 0) return;
        string key = FindKey(dialogue);
        if (key == null || !Lines.TryGetValue(key, out string rel)) return;
        if (CharOf.TryGetValue(key, out string charKey) && !OwnerMatches(charKey)) return;

        var renderer = StoreClientMono.Instance != null ? StoreClientMono.Instance.image : null;
        if (renderer == null) return;
        if (bound != renderer)
        {
            bound = renderer;
            vanilla = renderer.sprite;                               // 记下原图，离开时还回去
        }
        Sprite sprite = Build(rel, vanilla ?? renderer.sprite);
        if (sprite == null || sprite == renderer.sprite) return;
        renderer.sprite = sprite;
        appliedSprite = sprite;
        if (appliedCount++ % 25 == 0) MelonLogger.Msg($"已换图（{appliedCount}）：{key}");
    }

    // 客人离开：只把我们换上去的那张还原成原图。
    // 必须确认渲染器上现在还是"我们的图"——否则会把下一位客人刚设好的立绘盖掉
    // （这就是"普通顾客出现时变成上一个特殊角色"的原因）。
    internal static void Restore()
    {
        try
        {
            if (bound != null && vanilla != null && appliedSprite != null && bound.sprite == appliedSprite)
                bound.sprite = vanilla;
        }
        catch
        {
        }
        vanilla = null;
        appliedSprite = null;
    }

    // 新客人上门：游戏自己会设新图，我们只丢掉记的状态，绝不去写渲染器
    internal static void Forget()
    {
        bound = null;
        vanilla = null;
        appliedSprite = null;
    }

    private static string FindKey(Dialogue dialogue)
    {
        string text = dialogue.plainText;
        if (string.IsNullOrEmpty(text)) return null;
        if (TextKey.TryGetValue(text, out string key)) return key;
        string flat = Flat(text);
        if (flat.Length > 0 && TextKey.TryGetValue(flat, out key)) return key;
        string norm = Norm(text);
        if (norm.Length > 3 && TextKey.TryGetValue(norm, out key)) return key;
        return null;
    }

    private static Sprite Build(string rel, Sprite template)
    {
        if (template == null) return null;
        Rect rect = template.rect;
        if (rect.width <= 0 || rect.height <= 0) return null;
        string cacheKey = rel + "|" + rect.width + "x" + rect.height;
        if (Cache.TryGetValue(cacheKey, out Sprite cached) && cached != null) return cached;

        string path = Path.Combine(Root ?? ".", "Story", rel.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            if (Miss.Add(rel)) MelonLogger.Warning("缺图：" + rel);
            return null;
        }
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
        {
            UObject.Destroy(texture);
            if (Miss.Add(rel)) MelonLogger.Warning("PNG 解码失败：" + rel);
            return null;
        }
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        UObject.DontDestroyOnLoad(texture);

        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(template.pivot.x / rect.width, template.pivot.y / rect.height),
            template.pixelsPerUnit * (texture.width / rect.width));
        if (sprite == null) return null;
        sprite.name = "Story/" + rel;
        UObject.DontDestroyOnLoad(sprite);
        Cache[cacheKey] = sprite;
        return sprite;
    }
}

[HarmonyPatch(typeof(LocHelper), "GetLocalizedDialogue")]
internal static class LocDialogueHook
{
    [HarmonyPostfix]
    private static void Postfix(string key, string __result) => StoryMod.NoteText(key, __result);
}

[HarmonyPatch(typeof(LocHelper), "GetLocalizedDialoguePhone")]
internal static class LocPhoneHook
{
    [HarmonyPostfix]
    private static void Postfix(string key, string __result) => StoryMod.NoteText(key, __result);
}

[HarmonyPatch]
internal static class DisplayTextHook
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (MethodInfo method in typeof(DialogUIManager).GetMethods(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            if (method.Name == "DisplayClientText" && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == typeof(Dialogue))
                yield return method;
    }

    [HarmonyPostfix]
    private static void Postfix(Dialogue dialogue) => StoryMod.Apply(dialogue);
}

// 客人离开 / 下一位上门：把我们改过的图恢复成原版，避免残留在别人身上
[HarmonyPatch(typeof(StoreUIManager), "OnClientStartLeaving")]
internal static class LeavingHook
{
    [HarmonyPostfix]
    private static void Postfix() => StoryMod.Restore();
}

[HarmonyPatch(typeof(StoreUIManager), "OnNextClientArrived")]
internal static class ArrivalResetHook
{
    [HarmonyPostfix]
    private static void Postfix() => StoryMod.Forget();
}

[HarmonyPatch(typeof(PlayerStore), "DismissCurrentClient")]
internal static class DismissHook
{
    [HarmonyPrefix]
    private static void Prefix() => StoryMod.Restore();
}
