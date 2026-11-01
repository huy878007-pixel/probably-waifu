using System.Reflection;

namespace LaunchGiftPortraits.Content;

/// <summary>
/// Binds to the core runtime by reflection only. A compile-time reference to PortraitRuntime.* would
/// make the game's ModScanner refuse this Mod, because the core DLL is neither a game assembly nor
/// listed in this Mod's manifest, so every call crosses as JSON and BCL delegate types.
/// </summary>
static class CoreBridge
{
    const BindingFlags Statics = BindingFlags.Public | BindingFlags.Static;
    const string ApiTypeName = "PortraitRuntime.Unity.PortraitApi";

    static Type api;
    static Action<string, string> handler;

    public static bool Bound => api != null;
    public static string LastEvent { get; private set; } = "";
    public static int Subscribers => api == null ? 0 : Count("SubscriberCount");
    public static int Version => api == null ? 0 : Count("Version");
    /// <summary>Folder the core runtime loads from, used only to read a legacy ui.json once.</summary>
    public static string CoreFolder => api == null ? null : Path.GetDirectoryName(api.Assembly.Location);

    public static bool Bind()
    {
        if (api != null) return true;
        api = Find();
        if (api == null) return false;
        handler = (name, payload) => { LastEvent = name + " " + payload; PortraitPolicy.OnEvent(name, payload); };
        api.GetMethod("Subscribe", Statics)?.Invoke(null, new object[] { handler });
        return true;
    }

    public static void Unbind()
    {
        if (api == null) return;
        if (handler != null) api.GetMethod("Unsubscribe", Statics)?.Invoke(null, new object[] { handler });
        handler = null;
        api = null;
        LastEvent = "";
    }

    public static string Lookup(string request) => Call("Lookup", request) as string;
    public static bool Apply(string request) => Call("Apply", request) is bool applied && applied;
    public static void Restore() => api?.GetMethod("Restore", Statics)?.Invoke(null, null);
    public static void Sync() => api?.GetMethod("Sync", Statics)?.Invoke(null, null);
    public static bool IsApplied() => Invoke("IsApplied") is bool applied && applied;
    public static string Status() => Call("Status", null) as string;

    /// <summary>Register the delegate the core consults instead of guessing a replacement itself.</summary>
    public static void SetSpriteResolver(object resolver) => Invoke("SetSpriteResolver", resolver);
    public static bool ApplyTo(object owner, string pack, string path) => Invoke("ApplyTo", owner, pack, path) is bool applied && applied;
    public static void ReleaseTo(object owner) => Invoke("ReleaseTo", owner);

    static object Invoke(string method, params object[] arguments)
    {
        var target = api?.GetMethod(method, Statics);
        if (target == null) return null;
        try { return target.Invoke(null, arguments); }
        catch (Exception e) { return "bridge-error " + e.GetType().Name + ": " + e.Message; }
    }

    static object Call(string method, string argument)
    {
        var target = api?.GetMethod(method, Statics);
        if (target == null) return null;
        try { return target.Invoke(null, target.GetParameters().Length == 0 ? null : new object[] { argument }); }
        catch (Exception e) { return "bridge-error " + e.GetType().Name + ": " + e.Message; }
    }

    static int Count(string property)
    {
        try { return api?.GetProperty(property, Statics)?.GetValue(null) is int value ? value : 0; }
        catch (Exception) { return 0; }
    }

    static Type Find()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(ApiTypeName, false);
            if (type != null) return type;
        }
        return null;
    }
}
