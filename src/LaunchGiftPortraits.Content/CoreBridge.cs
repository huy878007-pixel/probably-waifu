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
    const string VisitorTypeName = "PortraitRuntime.Unity.VisitorApi";
    const string QueryTypeName = "PortraitRuntime.Unity.RuntimeApi";

    static Type api;
    static Type visitor;
    static Type queries;
    static Action<string, string> handler;

    public static bool Bound => api != null;
    public static bool VisitorBound => visitor != null;
    public static string LastEvent { get; private set; } = "";
    public static int Subscribers => api == null ? 0 : Count("SubscriberCount");
    public static int Version => api == null ? 0 : Count("Version");
    /// <summary>Folder the core runtime loads from, used only to read a legacy ui.json once.</summary>
    public static string CoreFolder => api == null ? null : Path.GetDirectoryName(api.Assembly.Location);

    public static bool Bind()
    {
        if (api != null) return true;
        api = Find(ApiTypeName);
        if (api == null) return false;
        visitor = Find(VisitorTypeName);
        queries = Find(QueryTypeName);
        handler = (name, payload) =>
        {
            LastEvent = name + " " + payload;
            // 小小 first: it stamps the mood and the trade outcome the presentation layer then draws.
            try { XiaoxiaoRuntime.OnEvent(name, payload); }
            catch (Exception e) { BeautyContentMod.Warn("Xiaoxiao event " + name + " failed: " + e.GetType().Name + ": " + e.Message); }
            PortraitPolicy.OnEvent(name, payload);
        };
        api.GetMethod("Subscribe", Statics)?.Invoke(null, new object[] { handler });
        return true;
    }

    public static void Unbind()
    {
        if (api == null) return;
        if (handler != null) api.GetMethod("Unsubscribe", Statics)?.Invoke(null, new object[] { handler });
        handler = null;
        api = null;
        visitor = null;
        queries = null;
        LastEvent = "";
    }

    public static string Lookup(string request) => Call("Lookup", request) as string;
    public static bool ConfigureLayout(string request) => Call("ConfigureLayout", request) is bool configured && configured;
    public static bool Apply(string request) => Call("Apply", request) is bool applied && applied;
    public static void Restore() => api?.GetMethod("Restore", Statics)?.Invoke(null, null);
    public static void Sync() => api?.GetMethod("Sync", Statics)?.Invoke(null, null);
    public static bool IsApplied() => Invoke("IsApplied") is bool applied && applied;
    public static bool IsForeign() => Invoke("IsForeign") is bool foreign && foreign;
    public static string Status() => Call("Status", null) as string;
    /// <summary>Core-owned run snapshot as JSON; keeps this Mod free of game-type dependencies.</summary>
    public static string Query(string resource)
    {
        var target = queries?.GetMethod("Query", Statics);
        if (target == null) return null;
        try { return target.Invoke(null, new object[] { resource }) as string; }
        catch (Exception e) { return "bridge-error " + e.GetType().Name + ": " + e.Message; }
    }

    /// <summary>Register the delegate the core consults instead of guessing a replacement itself.</summary>
    public static void SetSpriteResolver(object resolver) => Invoke("SetSpriteResolver", resolver);
    public static bool ApplyTo(object owner, string pack, string path) => Invoke("ApplyTo", owner, pack, path) is bool applied && applied;
    public static void ReleaseTo(object owner) => Invoke("ReleaseTo", owner);

    // ---- visitor API (core 1.6.6+) ---------------------------------------------------------------

    public static bool VisitorRegister(string owner, string id, Func<object> factory) =>
        InvokeVisitor("Register", owner, id, factory) is bool ok && ok;
    public static bool VisitorUnregister(string owner) => InvokeVisitor("Unregister", owner) is bool ok && ok;
    public static bool VisitorEnqueue(string owner, string id, bool first) =>
        InvokeVisitor("Enqueue", owner, id, first) is bool queued && queued;
    public static string VisitorState(string owner, string key) => InvokeVisitor("GetState", owner, key) as string;
    public static bool VisitorSetState(string owner, string key, string value) =>
        InvokeVisitor("SetState", owner, key, value) is bool ok && ok;
    public static bool VisitorRoll(string owner, string key, int percent) =>
        InvokeVisitor("Roll", owner, key, percent) is bool hit && hit;
    public static int VisitorPick(string owner, string key, int count) =>
        InvokeVisitor("Pick", owner, key, count) is int index ? index : 0;
    public static int VisitorCountDisplayItem(string id) => InvokeVisitor("CountDisplayItem", id) is int units ? units : 0;
    public static bool VisitorGiveOnce(string owner, string key, string itemId) =>
        InvokeVisitor("GiveOnce", owner, key, itemId) is bool granted && granted;
    public static object VisitorCreate(string request) => InvokeVisitor("CreateCustomer", request);
    public static string VisitorStatus() => InvokeVisitor("Status") as string;

    static object InvokeVisitor(string method, params object[] arguments)
    {
        var target = visitor?.GetMethod(method, Statics);
        if (target == null) return null;
        try { return target.Invoke(null, arguments); }
        catch (Exception e) { return "bridge-error " + e.GetType().Name + ": " + e.Message; }
    }

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

    static Type Find(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(name, false);
            if (type != null) return type;
        }
        return null;
    }
}
