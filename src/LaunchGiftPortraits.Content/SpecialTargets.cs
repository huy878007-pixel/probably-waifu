using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LaunchGiftPortraits.Content;

/// <summary>
/// Replaces the special full-size artwork the game draws from its own scene objects: the wanted
/// poster, the nine wanted portraits and the Dr. Jackson scene. The target list ships next to this
/// DLL; the core runtime has no scene knowledge and performs no replacement of its own.
/// </summary>
static class SpecialTargets
{
    sealed class Target
    {
        public int Scene { get; set; }
        public string Root { get; set; }
        public int[] Children { get; set; }
        public string Name { get; set; }
        public string Key { get; set; }
        public string Component { get; set; }
    }

    sealed class Binding
    {
        public UnityEngine.Object Owner;
        public Sprite Before;
        public Sprite Applied;
    }

    static readonly List<Binding> bindings = new();
    static Target[] targets;

    public static void Refresh()
    {
        Restore();
        try { Apply(SceneManager.GetActiveScene()); }
        catch (Exception e) { Warn("Scene scan unavailable: " + e.GetType().Name); }
    }

    public static void Apply(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        foreach (var target in Load().Where(x => x.Scene == scene.buildIndex)) Bind(scene, target);
    }

    public static void Restore()
    {
        foreach (var binding in bindings)
        {
            if (binding.Owner is SpriteRenderer renderer)
            {
                if (renderer != null && renderer.sprite == binding.Applied) renderer.sprite = binding.Before;
            }
            else if (binding.Owner is Image image && image != null && image.sprite == binding.Applied) image.sprite = binding.Before;
            CoreBridge.ReleaseTo(binding.Owner);
        }
        bindings.Clear();
    }

    static void Bind(Scene scene, Target target)
    {
        var roots = scene.GetRootGameObjects().Where(x => x.name == target.Root).ToArray();
        if (roots.Length != 1) { Warn("Special root not unique: " + target.Root); return; }
        var transform = roots[0].transform;
        foreach (var index in target.Children ?? Array.Empty<int>())
        {
            if (index >= transform.childCount) return;
            transform = transform.GetChild(index);
        }
        if (transform.name != target.Name) { Warn("Special target changed: " + target.Name); return; }
        var renderer = target.Component == "SpriteRenderer" ? transform.GetComponent<SpriteRenderer>() : null;
        var image = target.Component == "Image" ? transform.GetComponent<Image>() : null;
        var before = renderer != null ? renderer.sprite : image?.sprite;
        if (before == null || before.name != target.Key) return;
        var owner = renderer != null ? (UnityEngine.Object)renderer : image;
        var answer = Answer(target.Key);
        if (answer == null) return;
        var bar = answer.IndexOf('|');
        if (bar <= 0) return;
        if (!CoreBridge.ApplyTo(owner, answer.Substring(0, bar), answer.Substring(bar + 1))) return;
        var applied = renderer != null ? renderer.sprite : image?.sprite;
        if (applied != null && applied != before) bindings.Add(new Binding { Owner = owner, Before = before, Applied = applied });
    }

    static string Answer(string key)
    {
        var reply = CoreBridge.Lookup(JsonConvert.SerializeObject(new { sprite = key }));
        if (string.IsNullOrEmpty(reply)) return null;
        try
        {
            var answer = Newtonsoft.Json.Linq.JObject.Parse(reply);
            if (answer.Value<bool?>("ok") != true) return null;
            var pack = answer["pack"]?.ToString();
            var path = answer["path"]?.ToString();
            return pack != null && path != null ? pack + "|" + path : null;
        }
        catch (JsonException) { return null; }
    }

    static Target[] Load()
    {
        if (targets != null) return targets;
        try
        {
            var folder = Path.GetDirectoryName(typeof(SpecialTargets).Assembly.Location) ?? ".";
            var file = Path.Combine(folder, "special_targets.json");
            targets = File.Exists(file) ? JsonConvert.DeserializeObject<Target[]>(File.ReadAllText(file)) ?? Array.Empty<Target>() : Array.Empty<Target>();
        }
        catch (Exception e)
        {
            Warn("special_targets.json unreadable: " + e.Message);
            targets = Array.Empty<Target>();
        }
        return targets;
    }

    static void Warn(string text) { try { Debug.Log("[LaunchGiftPortraits] " + text); } catch (Exception) { } }
}
