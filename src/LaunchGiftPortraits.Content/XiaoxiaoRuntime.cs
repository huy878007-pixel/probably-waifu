using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LaunchGiftPortraits.Content;

/// <summary>
/// 小小：初次设置引导、日常采购来访与海豹团子赠礼。
/// 原生队列、对话、逐存档状态与物品发放由核心 VisitorApi 提供；名字、台词、立绘、概率都属于本包，
/// 所以核心那侧完全不知道她的存在。
/// </summary>
static class XiaoxiaoRuntime
{
    public const string Owner = "player.launchgiftportraits";
    public const string Pack = "xiaoxiao";
    public const string GuideId = "xiaoxiao.guide";
    public const string BuyerId = "xiaoxiao.buyer";
    public const string GiftItem = "xiaoxiao.seal-dumpling";
    public static int VisitChance(bool displayedGift) => displayedGift ? 24 : 12;
    public const int SupplyChance = 12;

    const string GuideQueued = "guide/queued";
    const string VisitQueued = "visit/queued";
    const string PaidDay = "visit/day";
    const string PaidCount = "visit/count";
    const string GiftState = "gift/state";
    const string GiftFails = "gift/fails";
    const string GiftThanks = "gift/thanks";

    /// <summary>
    /// 用户确认口径：前 6 次有效交易来访没有赠礼；第 7 次起 25%，每次都送不出去就再加 10 个百分点，
    /// 封顶 100%，只送一次。一次来访有真实成交只算一次，拆单不累加。
    /// </summary>
    public static int GiftChance(int paidVisits, int failures) =>
        paidVisits <= 6 ? 0 : Math.Min(100, 25 + 10 * Math.Max(0, failures));

    /// <summary>当天的需求类别与台词，按实际需求买货（原生标签）。</summary>
    static readonly (string[] Tags, string Line)[] Needs =
    {
        (new[] { "FOOD", "WATER" }, "今天想补些食物和饮用水，有合适的吗？"),
        (new[] { "FOOD" }, "今天想补些食物，有什么推荐的吗？"),
        (new[] { "WATER" }, "今天主要是来买水的，有合适的水吗？"),
        (new[] { "MATERIAL" }, "我需要补些材料，你这里有存货吗？"),
        (new[] { "TOOL" }, "今天来挑几件工具，要实用、耐用的！"),
        (new[] { "MEDICAL" }, "我需要补些医疗用品，你这里还有吗？"),
        (new[] { "FOOD", "MATERIAL" }, "食物和材料都想补一点，帮我看看？"),
        (new[] { "HOUSEHOLD_GOOD" }, "今天想添些日用品，有合适的吗？"),
        (new[] { "SEED" }, "想试着种点东西，店里有种子吗？"),
        (new[] { "CHEMICAL_SUPPLIES" }, "今天要补些化学用品，有合适的存货吗？"),
        (new[] { "LUXURY_ITEM" }, "今天想挑件精致的小东西，有什么好货？"),
        (new[] { "ARMOR" }, "想补一件防护装备，帮我看看？"),
        (new[] { "WEAPON", "AMMUNITION" }, "今天来看看武器和弹药，实用的就好。"),
        (new[] { "MEDICAL", "TOOL" }, "医疗用品和工具都想备一点，店主有推荐的吗？"),
    };

    static readonly string[][] Supplies =
    {
        new[] { "scrap_metal", "scrap_metal" },
        new[] { "water_ration", "water_ration" },
        new[] { "bandage_item", "topical_bandage_item" },
        new[] { "screwdriver", "scrap_metal" },
    };

    static readonly string[] Entries =
    {
        "店主，我来采购啦！",
        "今天有什么新到的东西吗？",
        "又来打扰啦，今天有几样东西想买。",
    };

    // ---- 绑定 -------------------------------------------------------------------------------

    public static bool Bind()
    {
        if (!CoreBridge.Bound || !CoreBridge.VisitorBound) return false;
        bool guide = CoreBridge.VisitorRegister(Owner, GuideId, () => Factory(true));
        bool buyer = CoreBridge.VisitorRegister(Owner, BuyerId, () => Factory(false));
        return guide && buyer;
    }

    public static string Diagnostic() => "visitor=" + CoreBridge.VisitorBound
        + " guidePending=" + (BeautyContentMod.Settings?.GuidePending == true)
        + " visits=" + (BeautyContentMod.Settings?.XiaoxiaoVisitsEnabled == true)
        + " paid=" + Get(PaidCount) + " gift=" + Get(GiftState);

    // ---- 事件派发 ---------------------------------------------------------------------------

    public static void OnEvent(string name, string payload)
    {
        switch (name)
        {
            case "runtime.enabled": Bind(); break;
            case "visitor.queue-window": QueueWindow(Parse(payload)); break;
            case "visitor.action": HandleAction(Parse(payload)); break;
            case PortraitApiNames.TradeSale: Sale(Parse(payload)); break;
            case "trade.purchase":
            case "trade.counter-batch":
                var purchase = Parse(payload);
                if (name == "trade.counter-batch" && (Text(purchase?["details"] as JObject, "direction") != "buy"
                    || purchase?["details"]?.Value<bool?>("committed") != true)) break;
                if (purchase?["details"] is JObject details && details.Value<long?>("paid") > 0)
                {
                    details["committed"] = true;
                    Sale(purchase);
                }
                break;
        }
    }

    /// <summary>每次原生换客前一刻决定要不要插队，只有这里会安排小小。</summary>
    static void QueueWindow(JObject context)
    {
        var settings = BeautyContentMod.Settings;
        if (settings == null || context?.Value<bool?>("ready") != true) return;
        string day = Day().ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (settings.GuidePending)
        {
            if (Get(GuideQueued) == day) return;
            if (CoreBridge.VisitorEnqueue(Owner, GuideId, true)) Set(GuideQueued, day);
            return;
        }
        if (!settings.XiaoxiaoVisitsEnabled || Get(VisitQueued) == day) return;

        // 抽中但上次没放下的礼物，先补发；放得下才算送达，放不下留到下次。
        if (Get(GiftState) == "won") GrantGift();

        // 摆进原生橱窗才翻倍：12% → 24%，放普通库存或仓库不算。
        int chance = VisitChance(CoreBridge.VisitorCountDisplayItem(GiftItem) > 0);
        if (!CoreBridge.VisitorRoll(Owner, "visit/day/" + day, chance)) return;
        if (!CoreBridge.VisitorEnqueue(Owner, BuyerId, true)) return;
        Set(VisitQueued, day);
        if (Get(GiftThanks) == "yes") Set(GiftThanks, "");
    }

    /// <summary>引导里的按钮动作。美化配置始终留在本包，不塞进核心。</summary>
    static void HandleAction(JObject payload)
    {
        var settings = BeautyContentMod.Settings;
        string action = Text(payload, "action");
        if (settings == null || action == null) return;
        switch (action)
        {
            case "set:ordinary:on": settings.SetOrdinaryCustomerBeauty(true); break;
            case "set:ordinary:off": settings.SetOrdinaryCustomerBeauty(false); break;
            case "size:small": settings.SelectSize("small"); break;
            case "size:medium": settings.SelectSize("medium"); break;
            case "size:large": settings.SelectSize("large"); break;
            case "bubble:vanilla": settings.ApplyBubblePreset("vanilla"); break;
            case "bubble:lower": settings.ApplyBubblePreset("lower"); break;
            case "bubble:upper": settings.ApplyBubblePreset("upper"); break;
            case "act:save": settings.Save(); break;
            case "guide:skip":
            case "guide:done": settings.SetGuidePending(false); break;
            case "invite:yes": settings.SetXiaoxiaoVisits(true); break;
            case "invite:no": settings.SetXiaoxiaoVisits(false); break;
            default: BeautyContentMod.Warn("Unknown xiaoxiao action: " + action); break;
        }
    }

    /// <summary>真实成交才计数：一次来访只结算一次，读档同一天不重复。</summary>
    static void Sale(JObject payload)
    {
        var context = payload?["context"] as JObject;
        var details = payload?["details"] as JObject;
        if (details == null || Text(context, "identity") != BuyerId) return;
        if (details.Value<bool?>("committed") != true || details.Value<long?>("paid") is not > 0) return;
        string day = Day().ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Get(PaidDay) == day) return;

        int paid = Count(PaidCount) + 1;
        Set(PaidDay, day);
        Set(PaidCount, paid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Get(GiftState) != "") return;                      // 已送出，或已抽中等着放下
        int failures = Count(GiftFails);
        int chance = GiftChance(paid, failures);
        if (chance <= 0) return;
        if (CoreBridge.VisitorRoll(Owner, "gift/day/" + day, chance)) GrantGift();
        else Set(GiftFails, (failures + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    static void GrantGift()
    {
        if (!CoreBridge.VisitorGiveOnce(Owner, "seal", GiftItem)) return;   // 库存放不下：保留领取资格
        Set(GiftState, "given");
        Set(GiftThanks, "yes");
    }

    // ---- 原生访客构建 -----------------------------------------------------------------------

    static object Factory(bool guide)
    {
        try { return Create(guide); }
        catch (Exception e)
        {
            BeautyContentMod.Warn("Xiaoxiao visitor build failed: " + e.GetType().Name + ": " + e.Message);
            return null;
        }
    }

    static object Create(bool guide)
    {
        var settings = BeautyContentMod.Settings;
        if (settings == null) return null;
        var request = new Dictionary<string, object>
        {
            ["id"] = guide ? GuideId : BuyerId,
            ["owner"] = Owner,
            ["pack"] = Pack,
            ["name"] = "小小",
            ["sprite"] = "femaleUpper1",
            ["portrait"] = Art("05-entry-calm"),
            ["faction"] = "FACTION_UPPER_LEVEL",
        };
        if (guide)
        {
            request["guide"] = true;
            request["dialogue"] = GuideDialogue();
        }
        else
        {
            // 来访许可在 QueueWindow 里判断；工厂本身必须永远能构造出客户端，
            // 因为游戏也会用它读名字、重建队列。
            request["budgetMultiplier"] = 2;
            var need = Needs[(CoreBridge.VisitorPick(Owner, "need", Needs.Length) + Day()) % Needs.Length];
            request["tags"] = need.Tags;
            bool supplies = CoreBridge.VisitorPick(Owner, "supply/day/" + Day(), 100) < SupplyChance;
            if (supplies)
            {
                request["intent"] = "sellbuy";
                request["offers"] = Supplies[CoreBridge.VisitorPick(Owner, "supply-items/day/" + Day(), Supplies.Length)];
            }
            request["dialogue"] = BuyerDialogue(need.Line, supplies);
            request["done"] = FarewellDialogue();
        }
        return CoreBridge.VisitorCreate(JsonConvert.SerializeObject(request));
    }

    // ---- 对话内容 ---------------------------------------------------------------------------

    static string Art(string stem) => "portraits/xiaoxiao/" + stem + ".png";

    static Dictionary<string, object> Node(string text, string portrait = null, Dictionary<string, object>[] options = null,
        string next = null, bool dismiss = false, string action = null, string endAction = null)
    {
        var node = new Dictionary<string, object> { ["text"] = text };
        if (portrait != null) node["portrait"] = portrait;
        if (next != null) node["next"] = next;
        if (dismiss) node["dismiss"] = true;
        if (action != null) node["action"] = action;
        if (endAction != null) node["endAction"] = endAction;
        if (options != null) node["choices"] = options;
        return node;
    }

    static Dictionary<string, object> Option(string text, string next, string action = null)
    {
        var option = new Dictionary<string, object> { ["text"] = text, ["next"] = next };
        if (action != null) option["action"] = action;
        return option;
    }

    static Dictionary<string, object> Graph(string start, Dictionary<string, object> nodes) =>
        new() { ["start"] = start, ["nodes"] = nodes };

    /// <summary>
    /// 首次引导：显示设置三步 + 来访许可询问，全部按钮即时生效。
    /// 原生对话只有 3 个选项气泡，所以每个节点最多 3 个选项；立绘大小先选再确认，气泡只给上中下三个位置。
    /// </summary>
    static Dictionary<string, object> GuideDialogue()
    {
        var nodes = new Dictionary<string, object>
        {
            ["welcome"] = Node("你好，店主！欢迎使用「深空娘化，可能有点好看」模组。我是模组引导员小小！",
                Art("01-welcome"), next: "introduction"),
            ["introduction"] = Node("接下来，我会帮助你完成这个模组的基础设置。我们一步一步来，把显示效果调到你看着舒服的样子就好。",
                Art("02-explain"), new[]
                {
                    Option("好，开始吧", "ordinary"),
                    Option("先用当前设置，跳过引导", "skip-help", "guide:skip"),
                }),
            ["skip-help"] = Node("没问题，那就保留现在的设置。以后想调整，按 F10 就能打开设置界面。",
                Art("03-confirm"), next: "invite-intro"),
            ["ordinary"] = Node("第一项，我们先确定普通顾客的外观。除了剧情人物，你希望平时来店里的普通客人，也使用娘化立绘吗？",
                Art("02-explain"), next: "ordinary-options"),
            ["ordinary-options"] = Node("这个选项只影响普通顾客。我和剧情人物的立绘都会保留，不受它影响。",
                Art("02-explain"), new[]
                {
                    Option("普通顾客也一起娘化", "ordinary-on", "set:ordinary:on"),
                    Option("普通顾客保留原版", "ordinary-off", "set:ordinary:off"),
                }),
            ["ordinary-on"] = Node("好，那就让日常来往的客人们也换个新模样！", Art("03-confirm"), next: "size-menu"),
            ["ordinary-off"] = Node("明白，普通客人就保留原版的样子。", Art("03-confirm"), next: "size-menu"),
            ["size-menu"] = Node("接下来是第二项：立绘大小。就拿我来试试看吧！",
                Art("02-explain"), next: "size-options"),
            ["size-options"] = Node("这里有小、中、大三档。你可以切换着看看，选一个看得清楚、又不觉得挡视线的大小。其他角色也会按照这一档进行适配。",
                Art("02-explain"), new[]
                {
                    Option("小", "size-ok", "size:small"),
                    Option("中", "size-ok", "size:medium"),
                    Option("大", "size-ok", "size:large"),
                }),
            ["size-ok"] = Node("现在这样的大小怎么样？看着舒服的话，我们就继续；想再换一档也没关系。",
                Art("03-confirm"), new[]
                {
                    Option("这个大小就好", "bubble"),
                    Option("再试试其他大小", "size-menu"),
                }),
            ["bubble"] = Node("大小选好了，第三项是对话气泡的位置。",
                Art("02-explain"), next: "bubble-options"),
            ["bubble-options"] = Node("你可以保留游戏原来的位置，也可以把气泡往下挪一些，或者换成上方阅读。先切换看看，哪一种更顺眼？",
                Art("02-explain"), new[]
                {
                    Option("原版位置", "bubble-done", "bubble:vanilla"),
                    Option("下移位置", "bubble-done", "bubble:lower"),
                    Option("上方阅读", "bubble-upper-hint", "bubble:upper"),
                }),
            ["bubble-upper-hint"] = Node("这种布局下，姓名框会跟着对话框移动，不需要再单独摆放。",
                Art("02-explain"), next: "bubble-done"),
            ["bubble-done"] = Node("现在看看，气泡有没有遮住你想看的地方？满意的话，就可以把刚才的设置保存下来了。",
                Art("03-confirm"), new[]
                {
                    Option("就这样，保存设置", "saved", "act:save"),
                    Option("再换个气泡位置", "bubble"),
                    Option("返回调整立绘大小", "size-menu"),
                }),
            ["saved"] = Node("好啦，设置已经保存！", Art("03-confirm"), next: "settings-help"),
            ["settings-help"] = Node("以后想重新调整，按 F10 就能打开设置界面。除了刚才的选项，还可以在那里微调立绘大小和位置，不用重新走一遍引导。",
                Art("02-explain"), next: "invite-intro"),
            ["invite-intro"] = Node("设置方面就介绍到这里。店主，我还有一个小小的请求……",
                Art("04-invitation"), next: "invite"),
            ["invite"] = Node("以后采购东西的时候，我也可以来你这里逛逛吗？",
                Art("04-invitation"), new[]
                {
                    Option("当然欢迎，想买东西就来吧", "accepted", "invite:yes"),
                    Option("暂时只负责引导吧", "declined", "invite:no"),
                }),
            ["accepted"] = Node("太好了，那就说定啦！下次我会带着采购清单来，看看店主又进了什么好东西。", Art("08-delighted"), next: "visit-help"),
            ["declined"] = Node("好呀，那我就先做好引导工作，不打扰你做生意啦。",
                Art("06-shopping"), next: "visit-help"),
            ["visit-help"] = Node("以后改变主意，也可以在 F10 设置里调整是否允许我来访。想重新听一遍引导，也可以在那里开启。",
                Art("02-explain"), next: "bye"),
            // 完成标记挂在告别线上：中途关游戏或被打断都不会置为已完成。
            ["bye"] = Node("那么，今天的引导就到这里。祝你生意兴隆，店主！", Art("13-farewell"), dismiss: true, endAction: "guide:done"),
        };
        return Graph("welcome", nodes);
    }

    /// <summary>日常来访：问候 → 采购需求。赠礼答谢只插在真正收到礼物之后的那次来访。</summary>
    static Dictionary<string, object> BuyerDialogue(string needLine, bool supplies = false)
    {
        int day = Day();
        string entry = Entries[(CoreBridge.VisitorPick(Owner, "entry", Entries.Length) + day) % Entries.Length];
        bool thanks = Get(GiftThanks) == "yes";
        var nodes = new Dictionary<string, object>
        {
            ["entry"] = Node(entry, next: thanks ? "thanks-1" : "need"),
            ["need"] = Node(needLine, Art("12-thinking"), next: supplies ? "supply" : null),
        };
        if (supplies) nodes["supply"] = Node("另外带了两件用不上的物资，店主也看看收不收？", Art("06-shopping"));
        if (thanks)
        {
            nodes["thanks-1"] = Node("对了，店主，这个海豹团子送给你。", Art("08-delighted"), next: "thanks-2");
            nodes["thanks-2"] = Node("可以摆在橱窗里，我路过就更想进来逛逛了。", Art("08-delighted"), next: "need");
        }
        return Graph("entry", nodes);
    }

    /// <summary>离店台词。立绘留给心情决定，所以不开心时不会被强制换回笑脸。</summary>
    static Dictionary<string, object> FarewellDialogue() => Graph("bye", new Dictionary<string, object>
    {
        ["bye"] = Node("今天先买这些，剩下的下次再看看。店主，下次见！", dismiss: true),
    });

    // ---- 小工具 -----------------------------------------------------------------------------

    static int Day()
    {
        var reply = Parse(CoreBridge.Query("run"));
        return reply?["data"]?.Value<int?>("day") ?? 0;
    }
    static string Get(string key) => CoreBridge.VisitorState(Owner, key) ?? "";
    static void Set(string key, string value) => CoreBridge.VisitorSetState(Owner, key, value);
    static int Count(string key) => int.TryParse(Get(key), out var value) ? value : 0;

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
