using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace ResSwitch
{
    // Languages are flat JSON files {"key": "text"}: embedded in the exe, and overridable / extendable
    // with <exe folder>\lang\xx.json or %AppData%\ResSwitch\lang\xx.json (no recompilation needed).
    internal static class Localization
    {
        private const string ResPrefix = "ResSwitch.lang.";
        private static Dictionary<string, string> cur = new Dictionary<string, string>();
        private static Dictionary<string, string> en = new Dictionary<string, string>();
        public static string Code = "en";

        public static void Init(string preferred)
        {
            en = Load("en") ?? new Dictionary<string, string>();
            string code = preferred;
            if (string.IsNullOrEmpty(code))
                code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            Dictionary<string, string> d = Load(code);
            if (d == null) { code = "en"; d = en; }
            Code = code;
            cur = d;
        }

        public static string T(string key)
        {
            string v;
            if (cur.TryGetValue(key, out v)) return v;
            if (en.TryGetValue(key, out v)) return v;
            return key;
        }

        public static string T(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch (FormatException) { return T(key); }
        }

        // code -> native language name
        public static List<KeyValuePair<string, string>> Available()
        {
            SortedDictionary<string, string> codes = new SortedDictionary<string, string>();
            foreach (string n in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                if (n.StartsWith(ResPrefix) && n.EndsWith(".json"))
                    codes[n.Substring(ResPrefix.Length, n.Length - ResPrefix.Length - 5).ToLowerInvariant()] = null;
            foreach (string dir in new string[] { Paths.AppLangDir, Paths.UserLangDir })
            {
                try
                {
                    if (Directory.Exists(dir))
                        foreach (string f in Directory.GetFiles(dir, "*.json"))
                            codes[Path.GetFileNameWithoutExtension(f).ToLowerInvariant()] = null;
                }
                catch { }
            }
            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
            foreach (string code in new List<string>(codes.Keys))
            {
                Dictionary<string, string> d = Load(code);
                string name = code;
                if (d != null && d.ContainsKey("lang.name")) name = d["lang.name"];
                list.Add(new KeyValuePair<string, string>(code, name));
            }
            return list;
        }

        private static Dictionary<string, string> Load(string code)
        {
            code = code.ToLowerInvariant();
            Dictionary<string, string> result = null;
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResPrefix + code + ".json"))
                {
                    if (s != null)
                        using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                            result = Parse(r.ReadToEnd());
                }
            }
            catch (Exception ex) { Log.Write("lang embedded " + code + ": " + ex.Message); }

            foreach (string dir in new string[] { Paths.AppLangDir, Paths.UserLangDir })
            {
                try
                {
                    string f = Path.Combine(dir, code + ".json");
                    if (!File.Exists(f)) continue;
                    Dictionary<string, string> over = Parse(File.ReadAllText(f, Encoding.UTF8));
                    if (over == null) continue;
                    if (result == null) result = new Dictionary<string, string>();
                    foreach (KeyValuePair<string, string> kv in over) result[kv.Key] = kv.Value;
                }
                catch (Exception ex) { Log.Write("lang file " + code + ": " + ex.Message); }
            }
            return result;
        }

        private static Dictionary<string, string> Parse(string json)
        {
            return new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(json);
        }
    }
}
