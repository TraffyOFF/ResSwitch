using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ResSwitch
{
    internal static class Paths
    {
        // RESSWITCH_DATA overrides the data folder (useful for tests / portable use).
        public static string DataDir
        {
            get
            {
                string o = Environment.GetEnvironmentVariable("RESSWITCH_DATA");
                if (!string.IsNullOrEmpty(o)) return o;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ResSwitch");
            }
        }
        public static string ConfigFile { get { return Path.Combine(DataDir, "config.json"); } }
        public static string UserLangDir { get { return Path.Combine(DataDir, "lang"); } }
        public static string AppLangDir { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang"); } }
        public static string ExePath { get { return Application.ExecutablePath; } }
    }

    internal static class Log
    {
        public static void Write(string msg)
        {
            try
            {
                Directory.CreateDirectory(Paths.DataDir);
                string f = Path.Combine(Paths.DataDir, "error.log");
                FileInfo fi = new FileInfo(f);
                if (fi.Exists && fi.Length > 200000) File.Delete(f);
                File.AppendAllText(f, DateTime.Now.ToString("s") + " " + msg + Environment.NewLine);
            }
            catch { }
        }
    }

    public class Profile
    {
        public string Name = "";
        public string Device = "";   // legacy: a device name such as \\.\DISPLAY2 (migrated to MonitorId when possible)
        public string MonitorId = ""; // stable id of the physical monitor this profile is pinned to; "" = primary display
        public int Width;
        public int Height;
        public int Freq;             // 0 = automatic
        public bool IsNative;

        public Profile Clone() { return (Profile)MemberwiseClone(); }

        [ScriptIgnore]
        public bool IsSet { get { return Width > 0 && Height > 0; } }

        public override string ToString() { return Name; }
    }

    public class GameRule
    {
        public string Exe = "";      // e.g. "cs2.exe"
        public string Profile = "";  // profile name

        public static string Key(string exe)
        {
            if (exe == null) return "";
            exe = Path.GetFileName(exe.Trim()).ToLowerInvariant();
            if (exe.EndsWith(".exe")) exe = exe.Substring(0, exe.Length - 4);
            return exe;
        }
    }

    public class ProfileShare
    {
        public int Format = 1;
        public List<Profile> Profiles = new List<Profile>();
    }

    public class AppConfig
    {
        public int Version = 1;
        public List<Profile> Profiles = new List<Profile>();
        public string ToggleA = "";
        public string ToggleB = "";
        // Default Ctrl+Shift+F7. Not Alt+F9/F10/F12/Z: the NVIDIA overlay (ShadowPlay) hooks those keys.
        public int HotkeyMods = 6;                 // MOD_CONTROL(2) | MOD_SHIFT(4)
        public int HotkeyKey = (int)Keys.F7;
        public string Language = "";               // "" = follow Windows
        public bool RestoreOnExit = true;
        public bool Notify = true;
        public bool CheckUpdates = false;
        public bool FirstRunDone = false;
        public long LastUpdateCheck = 0;           // UTC ticks
        public bool AutoSwitchDisabled = false;
        public int GameDelaySeconds = 3;
        public List<GameRule> Rules = new List<GameRule>();

        public static AppConfig Load()
        {
            AppConfig c = null;
            try
            {
                if (File.Exists(Paths.ConfigFile))
                {
                    string txt = File.ReadAllText(Paths.ConfigFile, Encoding.UTF8);
                    c = new JavaScriptSerializer().Deserialize<AppConfig>(txt);
                }
            }
            catch (Exception ex)
            {
                Log.Write("config load: " + ex.Message);
                try { File.Copy(Paths.ConfigFile, Paths.ConfigFile + ".bad", true); } catch { }
                c = null;
            }
            if (c == null) c = new AppConfig();
            c.Sanitize();
            return c;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Paths.DataDir);
                string json = JsonPretty.Format(new JavaScriptSerializer().Serialize(this));
                string tmp = Paths.ConfigFile + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(Paths.ConfigFile)) File.Delete(Paths.ConfigFile);
                File.Move(tmp, Paths.ConfigFile);
            }
            catch (Exception ex) { Log.Write("config save: " + ex.Message); }
        }

        public AppConfig Clone()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            AppConfig c = s.Deserialize<AppConfig>(s.Serialize(this));
            c.Sanitize();
            return c;
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        public void Sanitize()
        {
            if (Profiles == null) Profiles = new List<Profile>();
            if (Rules == null) Rules = new List<GameRule>();
            Profiles.RemoveAll(delegate(Profile p) { return p == null; });
            Rules.RemoveAll(delegate(GameRule r) { return r == null || string.IsNullOrEmpty(r.Exe); });
            foreach (Profile p in Profiles)
            {
                if (p.Name == null) p.Name = "";
                p.Name = p.Name.Trim();
                if (p.Name.Length > 64) p.Name = p.Name.Substring(0, 64);
                if (p.Device == null) p.Device = "";
                if (p.MonitorId == null) p.MonitorId = "";
                if (p.Width < 200 || p.Width > 20000 || p.Height < 200 || p.Height > 20000) { p.Width = 0; p.Height = 0; }
                p.Freq = Clamp(p.Freq, 0, 1000);
            }
            if (ToggleA == null) ToggleA = "";
            if (ToggleB == null) ToggleB = "";
            if (Language == null) Language = "";
            if (HotkeyKey <= 0 || HotkeyKey > 255 || HotkeyMods < 0 || HotkeyMods > 15)
            {
                HotkeyMods = 6;
                HotkeyKey = (int)Keys.F7;
            }
            GameDelaySeconds = Clamp(GameDelaySeconds, 0, 60);
            FixToggles();
        }

        public Profile Find(string name)
        {
            if (name == null) return null;
            foreach (Profile p in Profiles)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        // The native profile of the display with this device name (null if there is none: a native
        // resolution is never borrowed from another display).
        public Profile NativeForDevice(string dev)
        {
            if (dev == null) return null;
            foreach (Profile p in Profiles)
                if (p.IsNative && string.Equals(DisplayManager.ResolveDevice(p), dev, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        // The native profile of the display a profile is for.
        public Profile NativeForProfile(Profile p)
        {
            return NativeForDevice(DisplayManager.ResolveDevice(p));
        }

        // Old configs stored a device name (\\.\DISPLAY2): pin those profiles to the physical monitor instead,
        // because device names are reassigned when displays are plugged in or out.
        private bool MigrateDevices()
        {
            bool changed = false;
            List<DisplayInfo> displays = DisplayManager.GetDisplays();
            foreach (Profile p in Profiles)
            {
                if (p.MonitorId.Length > 0 || p.Device.Length == 0) continue;
                foreach (DisplayInfo d in displays)
                {
                    if (!string.Equals(d.Name, p.Device, StringComparison.OrdinalIgnoreCase)) continue;
                    p.MonitorId = d.Primary ? "" : d.MonitorId;
                    p.Device = "";
                    changed = true;
                }
            }
            return changed;
        }

        private string UniqueName(string baseName)
        {
            string n = baseName;
            int i = 2;
            while (Find(n) != null) n = baseName + " (" + (i++) + ")";
            return n;
        }

        // Every connected display that is not the primary one gets its own native profile (its current mode),
        // pinned to that monitor. The primary display's native profile follows the primary display.
        // Returns true if the profile list changed.
        public bool EnsureMonitorNatives()
        {
            bool changed = MigrateDevices();
            foreach (DisplayInfo d in DisplayManager.GetDisplays())
            {
                if (d.Primary) continue;
                bool has = false;
                foreach (Profile p in Profiles)
                    if (p.IsNative && string.Equals(DisplayManager.ResolveDevice(p), d.Name, StringComparison.OrdinalIgnoreCase)) has = true;
                if (has) continue;
                DisplayMode cur = DisplayManager.GetCurrent(d.Name);
                if (cur == null) continue;
                Profile n = new Profile();
                n.Name = UniqueName(Localization.T("prof.native") + " (" + d.Label + ")");
                n.IsNative = true;
                n.MonitorId = d.MonitorId;
                n.Width = cur.Width; n.Height = cur.Height; n.Freq = cur.Freq;
                Profiles.Add(n);
                changed = true;
            }
            return changed;
        }

        public void FixToggles()
        {
            if (Profiles.Count == 0) return;
            Profile n = null;
            foreach (Profile p in Profiles) if (p.IsNative) { n = p; break; }
            if (n == null) n = Profiles[0];
            if (Find(ToggleA) == null) ToggleA = n.Name;
            if (Find(ToggleB) == null)
            {
                Profile other = null;
                foreach (Profile p in Profiles) if (p != n && p.Name != ToggleA) { other = p; break; }
                ToggleB = other != null ? other.Name : n.Name;
            }
        }

        // Fills in the default "Native" and "4:3" profiles from what the display reports.
        public void EnsureProfiles()
        {
            bool fresh = Profiles.Count == 0;
            bool hasNative = false;
            foreach (Profile p in Profiles) if (p.IsNative) hasNative = true;

            if (!hasNative)
            {
                Profile n = new Profile();
                n.Name = Localization.T("prof.native");
                n.IsNative = true;
                string dev = DisplayManager.FindDevice("");
                DisplayMode cur = dev == null ? null : DisplayManager.GetCurrent(dev);
                if (cur != null) { n.Width = cur.Width; n.Height = cur.Height; n.Freq = cur.Freq; }
                else
                {
                    n.Width = Screen.PrimaryScreen.Bounds.Width;
                    n.Height = Screen.PrimaryScreen.Bounds.Height;
                }
                Profiles.Insert(0, n);
            }

            if (fresh)
            {
                Profile native = Profiles[0];
                Profile p43 = new Profile();
                p43.Name = Localization.T("prof.43");
                string dev = DisplayManager.FindDevice("");
                if (dev != null)
                {
                    DisplayMode best = null;
                    foreach (DisplayMode m in DisplayManager.GetModes(dev))
                        if (DisplayManager.RatioLabel(m.Width, m.Height) == "4:3" && (m.Width != native.Width || m.Height != native.Height)
                            && (best == null || m.Width * m.Height > best.Width * best.Height))
                            best = m;
                    if (best != null)
                    {
                        p43.Width = best.Width;
                        p43.Height = best.Height;
                        // refresh rate: highest one not above the native rate, else the lowest available
                        int pick = 0, lowest = int.MaxValue;
                        foreach (DisplayMode m in DisplayManager.GetModes(dev))
                        {
                            if (m.Width != best.Width || m.Height != best.Height) continue;
                            if (m.Freq <= native.Freq && m.Freq > pick) pick = m.Freq;
                            if (m.Freq < lowest) lowest = m.Freq;
                        }
                        if (pick == 0 && lowest != int.MaxValue) pick = lowest;
                        p43.Freq = pick;
                    }
                }
                Profiles.Add(p43); // Width = 0 when the display offers no 4:3 mode: user picks one manually
            }
            EnsureMonitorNatives();
            FixToggles();
        }

        public static void Export(string path, IList<Profile> profiles)
        {
            ProfileShare s = new ProfileShare();
            foreach (Profile p in profiles) s.Profiles.Add(p.Clone());
            File.WriteAllText(path, JsonPretty.Format(new JavaScriptSerializer().Serialize(s)), new UTF8Encoding(false));
        }

        // Throws if the file is not a valid profile file.
        public static List<Profile> Import(string path)
        {
            ProfileShare s = new JavaScriptSerializer().Deserialize<ProfileShare>(File.ReadAllText(path, Encoding.UTF8));
            List<Profile> res = new List<Profile>();
            if (s == null || s.Profiles == null) throw new InvalidDataException("no profiles");
            foreach (Profile p in s.Profiles)
            {
                if (p == null || string.IsNullOrEmpty(p.Name)) continue;
                if (p.Width < 200 || p.Width > 20000 || p.Height < 200 || p.Height > 20000) continue;
                p.Name = p.Name.Trim();
                if (p.Name.Length > 64) p.Name = p.Name.Substring(0, 64);
                p.Device = "";      // device names and monitor ids are machine specific
                p.MonitorId = "";
                p.IsNative = false; // native resolution belongs to the other machine
                if (p.Freq < 0 || p.Freq > 1000) p.Freq = 0;
                res.Add(p);
            }
            if (res.Count == 0) throw new InvalidDataException("no valid profile");
            return res;
        }
    }

    internal static class JsonPretty
    {
        public static string Format(string json)
        {
            StringBuilder sb = new StringBuilder();
            int indent = 0;
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); }
                    else if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) break;
                        indent++; NewLine(sb, indent);
                        break;
                    case '}':
                    case ']':
                        if (i > 0 && (json[i - 1] == '{' || json[i - 1] == '[')) { sb.Append(c); break; }
                        indent--; NewLine(sb, indent); sb.Append(c);
                        break;
                    case ',': sb.Append(c); NewLine(sb, indent); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static void NewLine(StringBuilder sb, int indent)
        {
            sb.Append("\r\n");
            sb.Append(' ', indent * 2);
        }
    }
}
