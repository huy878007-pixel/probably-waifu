using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace System.Runtime.CompilerServices { internal static class IsExternalInit {} }

namespace NativePorts
{
    internal static class NativeClock
    {
        private static readonly System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();
        public static long Milliseconds => Watch.ElapsedMilliseconds;
    }
    internal static class NativeAccess
    {
        public static void Set(object instance,string name,object value) => AccessTools.Field(instance.GetType(),name).SetValue(instance,value);
        public static T Field<T>(object instance,string name)
        {
            if(instance==null)return default(T);
            var field=AccessTools.Field(instance.GetType(),name);
            if(field==null)throw new MissingFieldException(instance.GetType().Name,name);
            return (T)field.GetValue(instance);
        }
        public static T TryCast<T>(this object instance) where T:class => instance as T;
        public static long? ToInt64(this IntPtr? pointer) => pointer?.ToInt64();
        public static T Cast<T>(this object instance) where T:class => instance as T ?? throw new InvalidCastException(typeof(T).FullName);
        public static void UnpatchSelf(this Harmony harmony) => harmony.UnpatchAll(harmony.Id);
    }
    internal static class NativeHash
    {
        public static byte[] HashData(byte[] input) { using(var sha=System.Security.Cryptography.SHA256.Create()) return sha.ComputeHash(input); }
        public static string ToHexString(byte[] input) => BitConverter.ToString(input).Replace("-", "");
    }
    internal sealed class NativeReferenceComparer : IEqualityComparer<object>
    {
        public static readonly NativeReferenceComparer Instance = new NativeReferenceComparer();
        public new bool Equals(object x,object y) => ReferenceEquals(x,y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
    internal static class NativeBitOps
    {
        public static int PopCount(ulong n) { int count=0;while(n!=0){n&=n-1;count++;}return count; }
        public static int PopCount(uint n) => PopCount((ulong)n);
        public static int TrailingZeroCount(ulong n) { if(n==0)return 64;int c=0;while((n&1)==0){n>>=1;c++;}return c; }
    }
    internal static class DelegateSupport
    {
        public static T ConvertDelegate<T>(Delegate action) where T:Delegate => (T)Delegate.CreateDelegate(typeof(T),action.Target,action.Method);
    }
    internal static class NativeIdentity
    {
        private sealed class Identity { public long Value; }
        private static readonly ConditionalWeakTable<object, Identity> Ids = new ConditionalWeakTable<object, Identity>();
        private static long next;
        public static IntPtr Id(object value)
        {
            if (value == null || (value is UnityEngine.Object unity && unity == null)) return IntPtr.Zero;
            return new IntPtr(Ids.GetValue(value, _ => new Identity { Value = System.Threading.Interlocked.Increment(ref next) }).Value);
        }
    }
}
namespace MelonLoader.Preferences { public class ValueValidator {} }
namespace MelonLoader
{
    public class MelonBase { public MelonLogger.Instance LoggerInstance { get; } = new MelonLogger.Instance(); }
    public abstract class MelonMod : MelonBase, IMod
    {
        private NativePump pump;
        private Harmony owned;
        public Harmony HarmonyInstance => owned;
        public void Init(ModManifest manifest)
        {
            MelonPreferences.Configure(manifest.ID);
            owned = new Harmony("mod." + manifest.ID);
            OnInitializeMelon();
        }
        public void OnEnable()
        {
            var go = new GameObject(GetType().Assembly.GetName().Name + ".Lifecycle");
            UnityEngine.Object.DontDestroyOnLoad(go);
            pump = go.AddComponent<NativePump>();
            pump.Owner = this;
            SceneManager.sceneLoaded += Loaded;
            SceneManager.sceneUnloaded += Unloaded;
            OnLateInitializeMelon();
            var scene = SceneManager.GetActiveScene();
            OnSceneWasLoaded(scene.buildIndex, scene.name);
        }
        public void OnDisable()
        {
            SceneManager.sceneLoaded -= Loaded;
            SceneManager.sceneUnloaded -= Unloaded;
            OnDeinitializeMelon();
            if (pump != null) UnityEngine.Object.Destroy(pump.gameObject);
            owned?.UnpatchAll(owned.Id);
            MelonPreferences.Save();
        }
        private void Loaded(Scene scene, LoadSceneMode mode) => OnSceneWasLoaded(scene.buildIndex,scene.name);
        private void Unloaded(Scene scene) => OnSceneWasUnloaded(scene.buildIndex,scene.name);
        public virtual void OnInitializeMelon() {}
        public virtual void OnLateInitializeMelon() {}
        public virtual void OnUpdate() {}
        public virtual void OnLateUpdate() {}
        public virtual void OnGUI() {}
        public virtual void OnSceneWasLoaded(int index,string name) {}
        public virtual void OnSceneWasUnloaded(int index,string name) {}
        public virtual void OnDeinitializeMelon() {}
        internal Coroutine StartCoroutine(IEnumerator enumerator) => pump.StartCoroutine(enumerator);
    }
    public sealed class NativePump : MonoBehaviour
    {
        public MelonMod Owner;
        private void Update() => Owner?.OnUpdate();
        private void LateUpdate() => Owner?.OnLateUpdate();
        private void OnGUI() => Owner?.OnGUI();
    }
    public static class MelonCoroutines
    {
        public static object Start(IEnumerator routine)
        {
            var pump = UnityEngine.Object.FindObjectOfType<NativePump>();
            if (pump == null) throw new InvalidOperationException("Native lifecycle not initialized");
            return pump.StartCoroutine(routine);
        }
    }
    public static class MelonLogger
    {
        public class Instance
        {
            public void Msg(object value) => MelonLogger.Msg(value);
            public void Warning(object value) => MelonLogger.Warning(value);
            public void Error(object value) => MelonLogger.Error(value);
        }
        public static void Msg(object value) => Debug.Log("[NativePort] " + value);
        public static void Warning(object value) => Debug.LogWarning("[NativePort] " + value);
        public static void Error(object value) => Debug.LogError("[NativePort] " + value);
    }
    public sealed class MelonPreferences_Entry<T>
    {
        internal string Key;
        public T Value { get; set; }
    }
    public sealed class MelonPreferences_Category
    {
        private readonly string id;
        internal MelonPreferences_Category(string id) { this.id=id; }
        public void SaveToFile(bool printmsg = false) => MelonPreferences.Save();
        public MelonPreferences_Entry<T> CreateEntry<T>(string key,T value,string displayName=null,string description=null,bool hidden=false,bool dontSaveDefault=false,Preferences.ValueValidator validator=null,string oldIdentifier=null)
            => MelonPreferences.Create(id+"/"+key,value);
    }
    public static class MelonPreferences
    {
        private static string file;
        private static readonly Dictionary<string,string> Values=new Dictionary<string,string>();
        private static readonly Dictionary<string,Func<string>> Getters=new Dictionary<string,Func<string>>();
        public static void Configure(string id)
        {
            file=Path.Combine(Application.persistentDataPath,"NativePortConfigs",id+".xml");
            if(!File.Exists(file))return;
            foreach(var element in XDocument.Load(file).Root.Elements("Entry")) Values[(string)element.Attribute("key")]=element.Value;
        }
        public static MelonPreferences_Category CreateCategory(string id,params object[] unused) => new MelonPreferences_Category(id);
        internal static MelonPreferences_Entry<T> Create<T>(string key,T fallback)
        {
            T value=fallback;
            if(Values.TryGetValue(key,out var text))
            {
                try { value=(T)(typeof(T).IsEnum?Enum.Parse(typeof(T),text):Convert.ChangeType(text,typeof(T),CultureInfo.InvariantCulture)); }
                catch(Exception ex) { Debug.LogWarning("[NativePort] Invalid preference " + key + ": " + ex.Message); }
            }
            var entry=new MelonPreferences_Entry<T>{Key=key,Value=value};
            Getters[key]=()=>Convert.ToString(entry.Value,CultureInfo.InvariantCulture);
            return entry;
        }
        public static bool HasEntry(string category,string key) => Values.ContainsKey(category+"/"+key)||Getters.ContainsKey(category+"/"+key);
        public static T GetEntryValue<T>(string category,string key)
        {
            string id=category+"/"+key;
            var text=Getters.TryGetValue(id,out var getter)?getter():Values[id];
            return (T)(typeof(T).IsEnum?Enum.Parse(typeof(T),text):Convert.ChangeType(text,typeof(T),CultureInfo.InvariantCulture));
        }
        public static void Save()
        {
            if(file==null)return;
            foreach(var pair in Getters)Values[pair.Key]=pair.Value();
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file));
            var root=new XElement("Preferences");
            foreach(var pair in Values)root.Add(new XElement("Entry",new XAttribute("key",pair.Key),pair.Value));
            var temp=file+".tmp";
            new XDocument(root).Save(temp);
            if(File.Exists(file)) File.Copy(file,file+".bak",true);
            File.Copy(temp,file,true);File.Delete(temp);
        }
    }
}
