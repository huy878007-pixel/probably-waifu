using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LaunchGiftPortraits.Content;

/// <summary>
/// Presentation policy owned by this content Mod: which asset to show for the current client and
/// dialogue line, and when to hand the renderer back. The core runtime only announces lifecycle
/// nodes and applies whatever this Mod resolves, so the policy can change without touching it.
/// </summary>
static class PortraitPolicy
{
    static string identity, factory, spriteKey, visitId, mood, spriteAlias, nativeAlias;
    static int reaction;
    static bool visitLocked;
    static string appliedPack, appliedPath, appliedCharacter, lastDialogueKey, lastSceneLine;
    public static string LastPath { get; private set; }
    public static string LastStage { get; private set; } = "";

    /// <summary>
    /// Put the current portrait back. The game rewrites the renderer during fades, shadow phases and
    /// client pooling; 1.2.5 compensated with a periodic repair scan, this is the same idea driven by
    /// the core's "is it still mine" check instead of a blind timer.
    /// </summary>
    public static void Reassert()
    {
        if (appliedPack == null || appliedPath == null) return;
        if (!CustomerBeautyPolicy.ShouldReplace(appliedPack, appliedCharacter)) { UseVanilla(); return; }
        CoreBridge.Apply(JsonConvert.SerializeObject(new { pack = appliedPack, path = appliedPath }));
    }

    public static void RefreshPreferences()
    {
        var key = lastDialogueKey;
        var stage = LastStage;
        // Restore releases the core's renderer binding, so rebind through its public catch-up API.
        CoreBridge.Sync();
        Show(key, stage);
    }

    public static void OnEvent(string name, string payload)
    {
        if (visitLocked && (name.StartsWith("trade.", StringComparison.Ordinal) || name == "client.reported")) { Reassert(); return; }
        switch (name)
        {
            case "client.arrested":
            case "client.threatened":
                var danger = Parse(payload)?["context"] as JObject;
                if (!visitLocked && visitId != null && Text(danger, "visitId") == visitId)
                { mood = "惊恐"; Show(null, "danger"); }
                break;
            case PortraitApiNames.Arrival:
            case PortraitApiNames.Outside: Arrival(Parse(payload)); break;
            case "client.reported":
                var reported = Parse(payload);
                if (visitId != null && Text(reported, "visitId") == visitId)
                {
                    reaction = Math.Max(reaction, 2);
                    mood = "不开心";
                    Show(null, "reported");
                }
                break;
            case "trade.bargain":
            case "trade.exposure": React(name, Parse(payload)); break;
            case PortraitApiNames.TradeSale: Sale(Parse(payload)); break;
            case "trade.counter-batch": WholesaleJoy(Parse(payload)); break;
            case PortraitApiNames.Dialogue: Dialogue(Parse(payload)); break;
            case PortraitApiNames.DialogueEnd: Ended(Parse(payload)); break;
            case PortraitApiNames.Leaving:
                // Authored exit mappings take priority; without one a story character keeps its
                // current state. Ordinary customers still use their farewell/reaction variants.
                if (!visitLocked && mood != "不开心" && mood != "愤怒" && mood != "惊恐") mood = "告别";
                lastDialogueKey = null;
                Show(null, "exit");
                break;
            case PortraitApiNames.Enabled: SpritePresenter.Bind(); break;
            case PortraitApiNames.ContentChanged:
            case PortraitApiNames.Scene:
                SpritePresenter.Clear();
                SpecialTargets.Refresh();
                Show(null, "base");
                break;
            case PortraitApiNames.Disabled:
                Forget();
                SpritePresenter.Unbind();
                SpecialTargets.Restore();
                break;
            case PortraitApiNames.BeforeArrival:
            case PortraitApiNames.Left: Forget(); break;
        }
    }

    static void Arrival(JObject context)
    {
        if (context == null) return;
        var nextVisit = Text(context, "visitId");
        if (nextVisit != visitId || identity != Text(context, "identity") || mood == null)
        {
            appliedPack = appliedPath = appliedCharacter = null;
            LastPath = null;
            LastStage = "";
            lastDialogueKey = null;
            lastSceneLine = null;
            visitId = nextVisit;
            reaction = 0;
            visitLocked = false;
            var source = Text(context, "factory");
            bool officialSecurity = source?.StartsWith("StoreClientListSecurity.", StringComparison.Ordinal) == true;
            mood = officialSecurity ? "不开心" : Text(context, "factionReputationTier") == "low" ? "平静" : "开心_原图";
            var locked = Text(context, "visitExpressionLock");
            if (locked == "渴求" || locked == "虚弱") { mood = locked; visitLocked = true; }
        }
        identity = Text(context, "identity");
        factory = Text(context, "factory");
        spriteKey = Text(context, "spriteKey");
        var augmented = context.Value<bool?>("isAugmented");
        var prefix = !augmented.HasValue ? null : augmented.Value ? "visitor:aug:" : "visitor:ordinary:";
        spriteAlias = spriteKey == null || prefix == null ? null : prefix + spriteKey;
        var vanilla = Text(context, "vanilla");
        nativeAlias = vanilla == null || prefix == null ? null : prefix + vanilla;
        // Arrival is also a catch-up notification during UI rebinding and settings refresh.
        // Only the first arrival of this visit selects entry art.
        if (appliedPath != null) { Reassert(); return; }
        Show(null, "base");
    }

    static void Dialogue(JObject context)
    {
        if (context == null) return;
        if (Text(context, "identity") != identity ||
            (Text(context, "visitId") != null && Text(context, "visitId") != visitId)) Arrival(context);
        // A content-authored line carries its own pack; the pack decides the art, and an explicit
        // portrait beats the mood. Native lines have no pack and use the story-differential map.
        var pack = Text(context, "pack");
        if (pack != null)
        {
            lastDialogueKey = null;
            var portrait = Text(context, "portrait");
            if (portrait != null) ShowAsset(pack, portrait, "line");
            else Show(null, "line");
            return;
        }
        var key = Text(context, "key");
        if (key == null) return;                              // untracked line: leave the portrait alone
        spriteKey = Text(context, "spriteKey") ?? spriteKey;
        lastSceneLine = key;
        lastDialogueKey = key;
        if (visitLocked && appliedPath != null) { Reassert(); return; }
        Show(key, "story");
    }

    /// <summary>Closing a dialogue does not change the visitor's physical/emotional state.</summary>
    static void Ended(JObject payload)
    {
        if (Text(payload, "source") != "store" || mood == null) return;
        Reassert();
    }

    static void React(string name, JObject payload)
    {
        var context = payload?["context"] as JObject;
        var details = payload?["details"] as JObject;
        if (visitId == null || Text(context, "visitId") != visitId || details == null) return;
        int next = 0;
        if (name == "trade.bargain")
        {
            var kind = Text(details, "kind");
            if (kind == "discount" && details.Value<bool?>("applied") == true) next = 1;
            if (kind == "markup") next = 2;
        }
        else if (details.Value<int?>("newlyDetectedUnits") > 0)
        {
            // Preview threshold. The core reports detected units per visit.
            next = details.Value<long?>("detectedUnitsThisVisit") >= 3 ? 3 : 2;
        }
        if (next <= reaction) return;
        reaction = next;
        mood = next == 3 ? "愤怒" : next == 2 ? "不开心" : "欢喜";
        Show(null, "reaction");
    }

    /// <summary>A committed sale can be either "spent what they came for" or "found what they wanted".</summary>
    static void Sale(JObject payload)
    {
        var context = payload?["context"] as JObject;
        var details = payload?["details"] as JObject;
        if (visitId == null || Text(context, "visitId") != visitId || details == null ||
            details.Value<bool?>("committed") != true || details.Value<long?>("paid") is not > 0) return;
        if (details.Value<bool?>("nearBudget") == true || details.Value<bool?>("budgetCapped") == true) { Joy("budget-joy"); return; }
        if (details.Value<bool?>("matchesDemand") == true) GoodFind();
    }

    /// <summary>发现好货：只在没有更重要的反应时切换，且只有本包为它准备了立绘。</summary>
    static void GoodFind()
    {
        if (appliedPack != XiaoxiaoRuntime.Pack || reaction != 0 || mood == "不开心" || mood == "愤怒") return;
        mood = "好货";
        Show(null, "good-find");
    }

    static void WholesaleJoy(JObject payload)
    {
        var context = payload?["context"] as JObject;
        var details = payload?["details"] as JObject;
        if (visitId == null || Text(context, "visitId") != visitId || details == null ||
            Text(details, "direction") != "buy" || details.Value<bool?>("committed") != true ||
            details.Value<long?>("paid") is not > 0 || details.Value<bool?>("allPurchased") != true) return;
        var offered = details.Value<int?>("offeredItems");
        if (offered is not > 0 || details.Value<int?>("purchasedItems") != offered) return;
        Joy("wholesale-joy");
    }

    static void Joy(string stage)
    {
        if (reaction != 0 || mood == "不开心" || mood == "愤怒") return;
        reaction = 1;
        mood = "欢喜";
        Show(null, stage);
    }

    static void Show(string dialogueKey, string stage)
    {
        if (identity == null && factory == null) return;
        var request = JsonConvert.SerializeObject(new { identity, factory, spriteKey, dialogueKey });
        var answer = Parse(CoreBridge.Lookup(request));
        var resolvedIdentity = identity;
        if (answer?.Value<bool?>("ok") != true)
        {
            // The game can deliberately dress an augmented client in a regular sprite. Prefer the
            // actual native sprite key over an inferred augmented alias so that its disguise stays
            // intact, and dedicated aug sprites still select their own authored augmented artwork.
            foreach (var alias in new[] { spriteKey, spriteAlias, nativeAlias }.Distinct())
            {
                if (alias == null) continue;
                var candidate = Parse(CoreBridge.Lookup(JsonConvert.SerializeObject(new { identity = alias, spriteKey, dialogueKey })));
                if (candidate?.Value<bool?>("ok") != true || Text(candidate, "pack") != "visitors-v2") continue;
                resolvedIdentity = alias;
                answer = candidate;
                break;
            }
        }
        if (answer?.Value<bool?>("ok") != true) return;
        if (visitLocked && appliedPath != null) { Reassert(); return; }
        if (!CustomerBeautyPolicy.ShouldReplace(Text(answer, "pack"), Text(answer, "character")))
        { UseVanilla(); return; }
        if (Text(answer, "pack") == "core" && ShowScenePortrait(Text(answer, "character"), dialogueKey, stage)) return;
        if (Text(answer, "pack") == "visitors-v2" && Text(answer, "via") != "story" && stage != "base" && appliedPath != null &&
            appliedCharacter == Text(answer, "character") && new[] { "cartel_handler", "courrier", "sawyer" }.Contains(appliedCharacter))
        { Reassert(); return; }
        // A missing line mapping is absence of a new instruction, not an instruction to reset.
        // Keep the last explicit portrait (including injured/changed clothing) for this visit.
        // Exact story/scene/exit mappings above still win; new visits clear the retained state.
        if (Text(answer, "pack") == "core" && Text(answer, "via") != "story" &&
            appliedPack == "core" && appliedPath != null && appliedCharacter == Text(answer, "character") && stage != "base")
        { Reassert(); return; }
        // Every pack is asked for its mood variant, but a story-differential line keeps its own art.
        // A pack without the variant falls back to its base image.
        if (stage == "base" && factory?.StartsWith("StoreClientListSecurity.", StringComparison.Ordinal) == true)
        {
            var entry = Parse(CoreBridge.Lookup(JsonConvert.SerializeObject(new { identity = resolvedIdentity, factory, spriteKey = "security:entry" })));
            if (entry?.Value<bool?>("ok") == true) answer = entry;
        }
        else if (mood != null && Text(answer, "via") != "story")
            answer = Parse(CoreBridge.Lookup(JsonConvert.SerializeObject(new { identity = resolvedIdentity, factory, spriteKey = "mood:" + mood, dialogueKey }))) ?? answer;
        var pack = Text(answer, "pack");
        var path = Text(answer, "path");
        if (pack == null || path == null) return;
        ShowAsset(pack, path, stage, Text(answer, "character"));
    }

    static bool ShowScenePortrait(string character, string dialogueKey, string stage)
    {
        string[] slots;
        if (dialogueKey != null) slots = new[] { "line:" + dialogueKey };
        else if (stage == "base") slots = new[] { "entry" };
        else if (stage == "exit")
        {
            bool arrested = PlayerStore.Instance?.isClientBeingArrested == true;
            slots = arrested ? new[] { "arrest" } : lastSceneLine == null
                ? new[] { "exit" } : new[] { "exit:" + lastSceneLine, "exit" };
        }
        else return false;

        foreach (var slot in slots)
        foreach (var scene in new[] { identity, factory == null ? null : "@" + factory })
        {
            if (scene == null) continue;
            foreach (var sprite in new[] { spriteKey ?? "", "" }.Distinct())
            {
                var key = $"workbench|{character}|{scene}|{sprite}|{slot}";
                var match = Parse(CoreBridge.Lookup(JsonConvert.SerializeObject(new { sprite = key })));
                if (match?.Value<bool?>("ok") != true || Text(match, "pack") != "core" ||
                    Text(match, "character") != character) continue;
                ShowAsset("core", Text(match, "path"), stage, character);
                return true;
            }
        }
        return false;
    }

    /// <summary>Apply one concrete pack asset, used by the mood path and by content-authored lines.</summary>
    static void ShowAsset(string pack, string path, string stage, string character = null)
    {
        if (!CustomerBeautyPolicy.ShouldReplace(pack, character)) { UseVanilla(); return; }
        if (!CoreBridge.Apply(JsonConvert.SerializeObject(new { pack, path }))) return;
        appliedPack = pack;
        appliedPath = path;
        appliedCharacter = character;
        LastPath = path;
        LastStage = stage;
    }

    static void UseVanilla()
    {
        CoreBridge.Restore();
        appliedPack = appliedPath = appliedCharacter = null;
        LastPath = null;
        LastStage = "vanilla";
    }

    static void Forget() { identity = null; factory = null; spriteKey = null; visitId = null; mood = null; spriteAlias = null; nativeAlias = null; reaction = 0; appliedPack = null; appliedPath = null; appliedCharacter = null; lastDialogueKey = null; lastSceneLine = null; LastPath = null; LastStage = ""; }

    static JObject Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JObject.Parse(json); } catch (JsonException) { return null; }
    }
    static string Text(JObject source, string name)
    {
        var token = source?[name];
        return token == null || token.Type == JTokenType.Null ? null : token.ToString();
    }
}

/// <summary>Lifecycle names published by the core API; duplicated here because this Mod cannot reference it.</summary>
static class PortraitApiNames
{
    public const string Arrival = "client.arrival";
    public const string BeforeArrival = "client.before-arrival";
    public const string Leaving = "client.leaving";
    public const string Left = "client.left";
    public const string Dialogue = "dialogue.display";
    public const string DialogueEnd = "dialogue.end";
    public const string Outside = "client.outside";
    public const string Enabled = "runtime.enabled";
    public const string Disabled = "runtime.disabled";
    public const string ContentChanged = "content.changed";
    public const string Scene = "scene.loaded";
    public const string TradeSale = "trade.sale";
}
