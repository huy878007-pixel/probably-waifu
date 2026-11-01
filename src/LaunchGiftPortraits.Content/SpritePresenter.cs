using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LaunchGiftPortraits.Content;

/// <summary>
/// Answers the core's sprite lookups for character and scene artwork. The core only asks; deciding
/// to replace a native sprite is this package's job, using data registered by its own content packs.
/// Answers are memoised because the core consults the resolver on its render path.
/// </summary>
static class SpritePresenter
{
    static readonly Dictionary<string, string> sprites = new(StringComparer.Ordinal);

    public static void Bind() => CoreBridge.SetSpriteResolver((Func<string, string>)Sprite);

    public static void Unbind()
    {
        CoreBridge.SetSpriteResolver(null);
        Clear();
    }

    public static void Clear() => sprites.Clear();

    static string Sprite(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (sprites.TryGetValue(key, out var cached)) return cached.Length == 0 ? null : cached;
        var answer = Ask(key);
        sprites[key] = answer ?? "";
        return answer;
    }

    static string Ask(string key)
    {
        var reply = CoreBridge.Lookup(JsonConvert.SerializeObject(new { sprite = key }));
        if (string.IsNullOrEmpty(reply)) return null;
        try
        {
            var answer = JObject.Parse(reply);
            if (answer.Value<bool?>("ok") != true) return null;
            var pack = Text(answer, "pack");
            var path = Text(answer, "path");
            return pack != null && path != null ? pack + "|" + path : null;
        }
        catch (JsonException) { return null; }
    }

    static string Text(JObject source, string name)
    {
        var token = source?[name];
        return token == null || token.Type == JTokenType.Null ? null : token.ToString();
    }
}
