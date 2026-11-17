using LaunchGiftPortraits.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PortraitRuntime.Core;

var tests = new List<object>();
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Event(string name, object payload) => PortraitPolicy.OnEvent(name, JsonConvert.SerializeObject(payload));
object Visit(string id = "injured", string visit = "v1") => new { identity = id, visitId = visit, spriteKey = "lun", factory = "Story.CreateInjured", factionReputationTier = "high" };
void Line(string key, string id = "injured", string visit = "v1") => Event("dialogue.display", new { identity = id, visitId = visit, spriteKey = "lun", key });
void Reset() { Event("client.before-arrival", new { }); CoreBridge.Applies.Clear(); CustomerBeautyPolicy.Enabled = true; PlayerStore.Instance.isClientBeingArrested = false; }
void Test(string name, Action test) {
    try { Reset(); test(); tests.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { tests.Add(new { name, passed = false }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
var pack = new Pack { Id = "core", Characters = new() { ["lun"] = new() {
    Identifiers = new[] { "injured", "healthy" }, Base = "healthy.png",
    Variants = new() { ["lun"] = "healthy.png", ["workbench|lun|injured|lun|entry"] = "injured-entry.png",
        ["workbench|lun|injured|lun|line:scoped"] = "injured-scoped.png",
        ["workbench|lun|injured|lun|exit"] = "injured-exit.png" },
    Story = new() { ["hit"] = "injured.png", ["recover"] = "recovered.png" }
} } };
CoreBridge.Catalog.Add(pack);
CoreBridge.Catalog.Add(new Pack { Id = "visitors-v2", Characters = new() { ["ordinary"] = new() {
    Identifiers = new[] { "ordinary" }, Base = "ordinary.png", Variants = new() { ["mood:欢喜"] = "ordinary-joy.png", ["mood:不开心"] = "ordinary-upset.png" }
} } });
Test("dialogue end retains last explicit story image", () => { Event("client.arrival",Visit());Line("hit");Event("dialogue.end",new {source="store"});Check(PortraitPolicy.LastPath=="injured.png","ended reset portrait"); });
Test("unmapped continuation retains injured state", () => { Event("client.arrival",Visit());Line("hit");Line("unmapped");Check(PortraitPolicy.LastPath=="injured.png","unmapped line reset portrait"); });
Test("unmapped line retains scene entry before first mapped line", () => {Event("client.arrival",Visit());Line("unmapped");Check(PortraitPolicy.LastPath=="injured-entry.png","entry state lost");});
Test("repeated arrival catch-up does not reset a story line", () => {Event("client.arrival",Visit());Line("hit");Event("client.arrival",Visit());Check(PortraitPolicy.LastPath=="injured.png","same visit reset");});
Test("new visit clears retained portrait even for same character", () => {Event("client.arrival",Visit());Line("hit");Event("client.arrival",Visit("healthy","v2"));Check(PortraitPolicy.LastPath=="healthy.png","previous visit leaked");});
Test("new visit detected from dialogue context", () => {Event("client.arrival",Visit());Line("hit");Line("unmapped","injured","v2");Check(PortraitPolicy.LastPath=="injured-entry.png","dialogue visit leaked");});
Test("next explicit story line still wins", () => {Event("client.arrival",Visit());Line("hit");Line("recover");Check(PortraitPolicy.LastPath=="recovered.png","new mapped line ignored");});
Test("scoped author mapping still wins", () => {Event("client.arrival",Visit());Line("hit");Line("scoped");Check(PortraitPolicy.LastPath=="injured-scoped.png","scoped mapping ignored");});
Test("explicit exit mapping still wins", () => {Event("client.arrival",Visit());Line("hit");Event("client.leaving",new {});Check(PortraitPolicy.LastPath=="injured-exit.png","exit mapping ignored");});
Test("unmapped story exit retains last line", () => {Event("client.arrival",Visit("healthy"));Line("hit","healthy");Event("client.leaving",new {});Check(PortraitPolicy.LastPath=="injured.png","unmapped exit reset");});
Test("ordinary customer joy still changes on budget sale", () => {Event("client.arrival",Visit("ordinary"));Event("trade.sale",new {context=new {visitId="v1"},details=new {committed=true,paid=90,nearBudget=true}});Check(PortraitPolicy.LastPath=="ordinary-joy.png","ordinary mood lost");Event("dialogue.end",new {source="store"});Check(PortraitPolicy.LastPath=="ordinary-joy.png","ordinary joy reset");});
Test("content-authored portrait persists through dialogue gaps", () => {Event("client.arrival",Visit());Event("dialogue.display",new {identity="injured",visitId="v1",pack="core",portrait="authored.png"});Event("dialogue.end",new {source="store"});Check(PortraitPolicy.LastPath=="authored.png","authored portrait reset");});
Test("disabling replacement can still restore vanilla", () => {Event("client.arrival",Visit());Line("hit");CustomerBeautyPolicy.Enabled=false;PortraitPolicy.Reassert();Check(PortraitPolicy.LastPath==null,"disabled portrait stuck");});
// Actual package fixture ensures manual artwork mappings remain the authority.
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
var actual = JsonConvert.DeserializeObject<Pack>(File.ReadAllText(Path.Combine(root,"packs/core/pack.json")));
Test("actual Lun injured scene retains mapped portrait through gap", () => {
    CoreBridge.Catalog = new Catalog();CoreBridge.Catalog.Add(actual);
    Event("client.arrival",new {identity="officerLunShot",visitId="real",spriteKey="officer_lun",factory="StoreClientUniqueList.CreateOfficerLunShot"});
    Event("dialogue.display",new {identity="officerLunShot",visitId="real",spriteKey="officer_lun",key="dialog_lun_shot_accept"});
    var expected=PortraitPolicy.LastPath;Check(expected?.Contains("受伤") == true,"fixture is not injured artwork");
    Event("dialogue.end",new {source="store"});
    Event("dialogue.display",new {identity="officerLunShot",visitId="real",spriteKey="officer_lun",key="new-unmapped-game-line"});
    Check(PortraitPolicy.LastPath==expected,"actual mapping reverted");
});

Test("locked craving entry survives sale dialogue and departure", () => {
 CoreBridge.Catalog = new Catalog();
 CoreBridge.Catalog.Add(new Pack {Id="visitors-v2",Characters=new(){["customer"]=new(){Identifiers=new[]{"locked"},Base="base.png",Variants=new(){["mood:渴求"]="craving.png"}}}});
 Event("client.arrival",new{identity="locked",visitId="lock",spriteKey="customer",visitExpressionLock="渴求",entryDialogueKey="repeat"});
 Check(PortraitPolicy.LastPath=="craving.png","entry lock not applied");
 Event("trade.sale",new{context=new{visitId="lock"},details=new{committed=true,paid=100,nearBudget=true}});
 Event("dialogue.display",new{identity="locked",visitId="lock",key="anything"});
 Event("client.leaving",new{});
 Check(PortraitPolicy.LastPath=="craving.png","locked state overwritten");
});
Test("security arrival uses dedicated entry", () => {
 CoreBridge.Catalog=new Catalog();
 CoreBridge.Catalog.Add(new Pack {Id="visitors-v2",Characters=new(){["officer"]=new(){Identifiers=new[]{"officer"},Base="base.png",Variants=new(){["security:entry"]="arrival.png",["mood:不开心"]="upset.png"}}}});
 Event("client.arrival",new{identity="officer",visitId="security",spriteKey="officer",factory="StoreClientListSecurity.CreateInspector"});
 Check(PortraitPolicy.LastPath=="arrival.png","security arrived as ordinary mood");
});
Console.WriteLine(JsonConvert.SerializeObject(tests));
return tests.Any(t => JObject.FromObject(t).Value<bool>("passed") == false) ? 1 : 0;

public sealed class PlayerStore { public static PlayerStore Instance {get;} = new(); public bool isClientBeingArrested {get;set;} }

namespace LaunchGiftPortraits.Content {
    static class CustomerBeautyPolicy { public static bool Enabled=true; public static bool ShouldReplace(string pack,string character)=>Enabled; }
    static class XiaoxiaoRuntime { public const string Pack="xiaoxiao"; }
    static class SpritePresenter {public static void Bind(){} public static void Unbind(){} public static void Clear(){} }
    static class SpecialTargets {public static void Refresh(){} public static void Restore(){} }
    static class CoreBridge {
        public static Catalog Catalog=new(); public static readonly List<string> Applies=new();
        public static bool Apply(string request) {var q=JObject.Parse(request);Applies.Add(q.Value<string>("path"));return true;}
        public static void Restore(){} public static void Sync(){}
        public static string Lookup(string request) {var q=JObject.Parse(request);return JsonConvert.SerializeObject(q["sprite"]!=null ? SpriteLookup.ResolveSpriteName(Catalog,q.Value<string>("sprite")) : SpriteLookup.Resolve(Catalog,q.Value<string>("identity"),q.Value<string>("factory"),q.Value<string>("spriteKey"),q.Value<string>("dialogueKey")));}
    }
}
