using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ResSwitch
{
    // Watches the process list (like Task Manager: no injection, no admin rights) every 2 seconds.
    // A rule "program -> profile" applies the profile when the program starts and restores the previous
    // mode when the last matching program closes. If several games run, the most recently started wins.
    internal class GameWatcher
    {
        private readonly TrayController host;
        private readonly Timer timer = new Timer();
        private readonly List<string> order = new List<string>();                 // active rule keys, oldest first
        private readonly Dictionary<string, DateTime> firstSeen = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, DisplayMode> previous = new Dictionary<string, DisplayMode>(StringComparer.OrdinalIgnoreCase);
        private string appliedKey;

        public GameWatcher(TrayController host)
        {
            this.host = host;
            timer.Interval = 2000;
            timer.Tick += delegate { try { Tick(); } catch (Exception ex) { Log.Write("watcher: " + ex.Message); } };
        }

        public void Start() { timer.Start(); }
        public void Stop() { timer.Stop(); }

        public void Tick()
        {
            AppConfig cfg = host.Config;
            if (cfg.AutoSwitchDisabled || cfg.Rules.Count == 0)
            {
                order.Clear();
                if (previous.Count > 0 || appliedKey != null) RestoreAll();
                return;
            }

            // which rules currently have a running process
            Dictionary<string, GameRule> running = new Dictionary<string, GameRule>();
            Dictionary<string, DateTime> starts = new Dictionary<string, DateTime>();
            foreach (GameRule r in cfg.Rules)
            {
                string key = GameRule.Key(r.Exe);
                if (key.Length == 0 || running.ContainsKey(key)) continue;
                Process[] ps = null;
                try { ps = Process.GetProcessesByName(key); } catch { }
                if (ps == null) continue;
                if (ps.Length > 0)
                {
                    running[key] = r;
                    DateTime latest = DateTime.MinValue;
                    foreach (Process p in ps)
                    {
                        try { if (p.StartTime > latest) latest = p.StartTime; } catch { }
                    }
                    starts[key] = latest == DateTime.MinValue ? DateTime.Now : latest;
                }
                foreach (Process p in ps) p.Dispose();
            }

            order.RemoveAll(delegate(string k) { return !running.ContainsKey(k); });
            List<string> fresh = new List<string>();
            foreach (string k in running.Keys) if (!order.Contains(k)) fresh.Add(k);
            fresh.Sort(delegate(string a, string b) { return starts[a].CompareTo(starts[b]); });
            foreach (string k in fresh) { order.Add(k); firstSeen[k] = DateTime.Now; }

            if (order.Count == 0)
            {
                if (previous.Count > 0 || appliedKey != null) RestoreAll();
                return;
            }

            string winner = order[order.Count - 1];
            if (winner == appliedKey) return;
            if ((DateTime.Now - firstSeen[winner]).TotalSeconds < cfg.GameDelaySeconds) return; // let the game start first

            appliedKey = winner;
            Profile p2 = cfg.Find(running[winner].Profile);
            if (p2 == null || !p2.IsSet) return;
            string dev = DisplayManager.ResolveDevice(p2);
            if (dev != null && !previous.ContainsKey(dev))
            {
                DisplayMode cur = DisplayManager.GetCurrent(dev);
                if (cur != null) previous[dev] = cur;
            }
            host.ApplyProfile(p2, false);
        }

        public void RestoreAll()
        {
            foreach (KeyValuePair<string, DisplayMode> kv in previous)
                host.ApplyMode(kv.Key, kv.Value);
            previous.Clear();
            appliedKey = null;
        }
    }

    internal static class SteamHelper
    {
        public class Game
        {
            public string Name;
            public string Dir;
            public override string ToString() { return Name; }
        }

        public class ExeCandidate
        {
            public string Path;
            public string Relative;
            public long Size;
            public override string ToString()
            {
                string size = Size >= 1024 * 1024 ? (Size / (1024 * 1024)) + " MB" : Math.Max(1, Size / 1024) + " KB";
                return Relative + "  (" + size + ")";
            }
        }

        private static string SteamRoot()
        {
            string[][] places = new string[][] {
                new string[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath" },
                new string[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath" },
                new string[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath" }
            };
            foreach (string[] p in places)
            {
                try
                {
                    string v = Registry.GetValue(p[0], p[1], null) as string;
                    if (!string.IsNullOrEmpty(v))
                    {
                        v = v.Replace('/', '\\');
                        if (Directory.Exists(v)) return v;
                    }
                }
                catch { }
            }
            return null;
        }

        private static string VdfValue(string text, string key)
        {
            Match m = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        public static List<Game> FindGames()
        {
            List<Game> games = new List<Game>();
            string root = SteamRoot();
            if (root == null) return games;

            List<string> libs = new List<string>();
            libs.Add(root);
            try
            {
                string vdf = Path.Combine(root, @"steamapps\libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    {
                        string lib = m.Groups[1].Value.Replace("\\\\", "\\");
                        if (!libs.Exists(delegate(string x) { return string.Equals(x, lib, StringComparison.OrdinalIgnoreCase); })) libs.Add(lib);
                    }
            }
            catch { }

            foreach (string lib in libs)
            {
                string steamapps = Path.Combine(lib, "steamapps");
                string[] manifests;
                try { manifests = Directory.GetFiles(steamapps, "appmanifest_*.acf"); }
                catch { continue; }
                foreach (string f in manifests)
                {
                    try
                    {
                        string txt = File.ReadAllText(f);
                        string name = VdfValue(txt, "name");
                        string dir = VdfValue(txt, "installdir");
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dir)) continue;
                        if (name.StartsWith("Steamworks") || name.StartsWith("Steam Linux Runtime") || name.StartsWith("Proton")) continue;
                        string full = Path.Combine(Path.Combine(steamapps, "common"), dir);
                        if (!Directory.Exists(full)) continue;
                        Game g = new Game();
                        g.Name = name;
                        g.Dir = full;
                        games.Add(g);
                    }
                    catch { }
                }
            }
            games.Sort(delegate(Game a, Game b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            return games;
        }

        private static readonly Regex Junk = new Regex(
            "unins|uninstall|setup|install|redist|vcredist|vc_redist|dxsetup|dotnet|crash|report|helper|easyanticheat|beservice|battleye|start_protected_game|prereq|updater|webhelper|diagnostic",
            RegexOptions.IgnoreCase);

        // Executables inside the game folder (depth <= 4), biggest first; obvious helpers are left out.
        public static List<ExeCandidate> FindExecutables(string gameDir)
        {
            List<ExeCandidate> list = new List<ExeCandidate>();
            Scan(gameDir, gameDir, 0, list);
            list.Sort(delegate(ExeCandidate a, ExeCandidate b) { return b.Size.CompareTo(a.Size); });
            if (list.Count > 30) list.RemoveRange(30, list.Count - 30);
            return list;
        }

        private static void Scan(string root, string dir, int depth, List<ExeCandidate> list)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*.exe"))
                {
                    string fn = Path.GetFileName(f);
                    if (Junk.IsMatch(fn)) continue;
                    ExeCandidate c = new ExeCandidate();
                    c.Path = f;
                    c.Relative = f.Substring(root.Length).TrimStart('\\');
                    c.Size = new FileInfo(f).Length;
                    list.Add(c);
                }
                if (depth >= 4) return;
                foreach (string d in Directory.GetDirectories(dir)) Scan(root, d, depth + 1, list);
            }
            catch { }
        }
    }
}
