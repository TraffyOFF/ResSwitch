using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using L = ResSwitch.Localization;

namespace ResSwitch
{
    internal class TrayController : ApplicationContext
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr h);

        private AppConfig cfg;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly HotkeyWindow hotkey;
        private readonly GameWatcher watcher;
        private readonly Control marshal;
        private readonly HashSet<string> changedDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly EventWaitHandle quitEvent;
        private SettingsForm settings;
        private bool quitting;
        private bool busy;
        private bool hotkeyFailed;
        private string iconKey;
        private string pendingUpdateUrl;
        private bool firstRun;

        public AppConfig Config { get { return cfg; } }

        public TrayController()
        {
            marshal = new Control();
            IntPtr unused = marshal.Handle; // create the handle on the UI thread so BeginInvoke works

            cfg = AppConfig.Load();
            L.Init(cfg.Language);
            firstRun = !cfg.FirstRunDone;
            cfg.EnsureProfiles();
            cfg.Save();

            menu = new ContextMenuStrip();
            menu.Opening += delegate { BuildMenu(); };
            tray = new NotifyIcon();
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Toggle(); };
            tray.BalloonTipClicked += delegate
            {
                if (pendingUpdateUrl != null && UpdateCheck.IsSafeUrl(pendingUpdateUrl))
                    try { Process.Start(pendingUpdateUrl); } catch { }
            };
            tray.Text = "ResSwitch";
            tray.Icon = MakeIcon("N", true);
            tray.Visible = true;
            RefreshIcon(true);

            hotkey = new HotkeyWindow();
            hotkey.Pressed += delegate { Toggle(); };
            RegisterHotkey();

            watcher = new GameWatcher(this);
            watcher.Start();

            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            if (Startup.IsEnabled()) Startup.Set(true); // keep the Run entry pointing at the current exe

            quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.QuitEventName);
            ThreadPool.RegisterWaitForSingleObject(quitEvent, delegate
            {
                try { marshal.BeginInvoke(new Action(ExitApp)); } catch { }
            }, null, -1, true);

            marshal.BeginInvoke(new Action(AfterStart));
        }

        // ---------------------------------------------------------------- start-up

        private void AfterStart()
        {
            if (hotkeyFailed)
                Notify(L.T("notify.hotkeyFail", HotkeyUtil.Format(cfg.HotkeyMods, cfg.HotkeyKey)));

            if (firstRun)
            {
                Profile n = cfg.NativeForDevice(DisplayManager.FindDevice(""));
                string info = n == null ? "" : n.Width + " x " + n.Height + (n.Freq > 0 ? " @ " + n.Freq + " Hz" : "");
                DialogResult r = MessageBox.Show(
                    L.T("first.text", info, HotkeyUtil.Format(cfg.HotkeyMods, cfg.HotkeyKey)),
                    L.T("first.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                cfg.CheckUpdates = r == DialogResult.Yes;
                cfg.FirstRunDone = true;
                cfg.Save();
                Profile b = cfg.Find(cfg.ToggleB);
                if (b != null && !b.IsSet) Notify(L.T("notify.unset"));
            }

            if (cfg.CheckUpdates && UpdateCheck.Configured)
            {
                long last = cfg.LastUpdateCheck;
                if (last == 0 || (DateTime.UtcNow - new DateTime(last, DateTimeKind.Utc)).TotalHours > 20)
                {
                    UpdateCheck.CheckAsync(delegate(UpdateCheck.Result res)
                    {
                        try
                        {
                            marshal.BeginInvoke(new Action(delegate
                            {
                                cfg.LastUpdateCheck = DateTime.UtcNow.Ticks;
                                cfg.Save();
                                if (res.Ok && res.Newer)
                                {
                                    pendingUpdateUrl = res.Url;
                                    Notify(L.T("notify.update", res.Version));
                                }
                            }));
                        }
                        catch { }
                    });
                }
            }
        }

        private void RegisterHotkey()
        {
            hotkeyFailed = !hotkey.Register((uint)cfg.HotkeyMods, cfg.HotkeyKey);
        }

        private void OnDisplayChanged(object sender, EventArgs e)
        {
            try
            {
                marshal.BeginInvoke(new Action(delegate
                {
                    // a newly connected display gets its own native profile
                    try { if (cfg.EnsureMonitorNatives()) cfg.Save(); } catch (Exception ex) { Log.Write("monitors: " + ex.Message); }
                    RefreshIcon(false);
                }));
            }
            catch { }
        }

        // ---------------------------------------------------------------- switching

        private bool Matches(Profile p)
        {
            string dev = DisplayManager.ResolveDevice(p);
            if (dev == null || !p.IsSet) return false;
            DisplayMode cur = DisplayManager.GetCurrent(dev);
            return cur != null && cur.Width == p.Width && cur.Height == p.Height;
        }

        public void Toggle()
        {
            if (busy || quitting) return;
            busy = true;
            try
            {
                Profile a = cfg.Find(cfg.ToggleA);
                Profile b = cfg.Find(cfg.ToggleB);
                if (a == null || b == null || a == b) { OpenSettings(0); return; }
                Profile target = Matches(a) ? b : a;
                if (!target.IsSet)
                {
                    Notify(L.T("notify.unset"));
                    OpenSettings(0);
                    return;
                }
                ApplyProfile(target, true);
            }
            finally { busy = false; }
        }

        // Applies a profile. A refused mode or a missing display falls back to the native mode.
        public bool ApplyProfile(Profile p, bool announce)
        {
            if (p == null) return false;
            if (!p.IsSet) { Notify(L.T("notify.unset")); return false; }
            string dev = DisplayManager.ResolveDevice(p);
            bool ok = dev != null && DisplayManager.Apply(dev, p.Width, p.Height, p.Freq);
            if (ok)
            {
                changedDevices.Add(dev);
                RefreshIcon(false);
                if (announce && cfg.Notify) Notify(L.T("notify.switched", p.Name, p.Width, p.Height));
                return true;
            }

            // Refused mode, or the profile's monitor is not connected: back to native, never an error dialog.
            // The display that gets reset is the profile's own; if its monitor is gone, the primary display.
            string target = dev ?? DisplayManager.FindDevice("");
            if (!p.IsNative && target != null)
            {
                Profile n = cfg.NativeForDevice(target);
                if (n != null && n.IsSet) DisplayManager.Apply(target, n.Width, n.Height, n.Freq);
            }
            RefreshIcon(false);
            Notify(dev == null ? L.T("notify.monitorMissing", p.Name) : L.T("notify.fallback", p.Name));
            return false;
        }

        // Restores a mode saved earlier (used by the game watcher).
        public void ApplyMode(string device, DisplayMode m)
        {
            if (m == null || !DisplayManager.Apply(device, m.Width, m.Height, m.Freq))
            {
                Profile n = cfg.NativeForDevice(device);
                if (n != null && n.IsSet) DisplayManager.Apply(device, n.Width, n.Height, n.Freq);
            }
            RefreshIcon(false);
        }

        private void RestoreNative()
        {
            foreach (string dev in new List<string>(changedDevices))
            {
                Profile n = cfg.NativeForDevice(dev);
                if (n != null && n.IsSet) DisplayManager.Apply(dev, n.Width, n.Height, n.Freq);
            }
        }

        // ---------------------------------------------------------------- icon & menu

        private void RefreshIcon(bool force)
        {
            string prim = DisplayManager.FindDevice("");
            DisplayMode cur = prim == null ? null : DisplayManager.GetCurrent(prim);
            string label = "?";
            bool isNative = false;
            string tip = "ResSwitch";
            if (cur != null)
            {
                foreach (Profile p in cfg.Profiles)
                {
                    if (p.IsSet && p.Width == cur.Width && p.Height == cur.Height &&
                        string.Equals(DisplayManager.ResolveDevice(p), prim, StringComparison.OrdinalIgnoreCase))
                    {
                        isNative = p.IsNative;
                        break;
                    }
                }
                label = isNative ? "N" : DisplayManager.RatioLabel(cur.Width, cur.Height);
                tip = "ResSwitch - " + cur.Width + "x" + cur.Height + " @ " + cur.Freq + " Hz";
            }
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            string key = label + "|" + isNative;
            if (!force && key == iconKey) return;
            iconKey = key;
            Icon old = tray.Icon;
            tray.Icon = MakeIcon(label, isNative);
            if (old != null) old.Dispose();
        }

        private static Icon MakeIcon(string text, bool native)
        {
            const int size = 32;
            Color bg = native ? Color.FromArgb(0, 120, 212) : Color.FromArgb(214, 106, 0);
            using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (GraphicsPath path = new GraphicsPath())
                {
                    int r = 8;
                    path.AddArc(0, 0, r, r, 180, 90);
                    path.AddArc(size - r - 1, 0, r, r, 270, 90);
                    path.AddArc(size - r - 1, size - r - 1, r, r, 0, 90);
                    path.AddArc(0, size - r - 1, r, r, 90, 90);
                    path.CloseFigure();
                    using (Brush b = new SolidBrush(bg)) g.FillPath(b, path);
                }
                float fs = text.Length <= 1 ? 22f : 17f;
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                Font f = null;
                while (true)
                {
                    f = new Font("Segoe UI", fs, FontStyle.Bold, GraphicsUnit.Pixel);
                    if (g.MeasureString(text, f).Width <= size - 1 || fs <= 8f) break;
                    f.Dispose();
                    fs -= 1f;
                }
                g.DrawString(text, f, Brushes.White, new RectangleF(0, 1, size, size), sf);
                f.Dispose();
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
        }

        private void BuildMenu()
        {
            Theme.ApplyMenu(menu);
            menu.Items.Clear();

            DisplayMode cur = null;
            string prim = DisplayManager.FindDevice("");
            if (prim != null) cur = DisplayManager.GetCurrent(prim);
            ToolStripMenuItem status = new ToolStripMenuItem(cur == null ? "ResSwitch" : L.T("tray.active", cur.Width + " x " + cur.Height + " @ " + cur.Freq + " Hz"));
            status.Enabled = false;
            menu.Items.Add(status);

            ToolStripMenuItem toggle = new ToolStripMenuItem(L.T("tray.toggle"));
            toggle.ShortcutKeyDisplayString = HotkeyUtil.Format(cfg.HotkeyMods, cfg.HotkeyKey);
            toggle.Click += delegate { Toggle(); };
            menu.Items.Add(toggle);

            ToolStripMenuItem profiles = new ToolStripMenuItem(L.T("tray.profiles"));
            foreach (Profile p in cfg.Profiles)
            {
                Profile pp = p;
                ToolStripMenuItem it = new ToolStripMenuItem(p.Name + (p.IsSet ? "  (" + p.Width + " x " + p.Height + ")" : ""));
                it.Checked = Matches(p);
                it.Click += delegate { ApplyProfile(pp, true); };
                profiles.DropDownItems.Add(it);
            }
            if (menu.Renderer != null) profiles.DropDown.Renderer = menu.Renderer;
            menu.Items.Add(profiles);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem auto = new ToolStripMenuItem(L.T("tray.autoOff"));
            auto.Checked = cfg.AutoSwitchDisabled;
            auto.CheckOnClick = true;
            auto.Click += delegate
            {
                cfg.AutoSwitchDisabled = auto.Checked;
                cfg.Save();
                try { watcher.Tick(); } catch { }
            };
            menu.Items.Add(auto);

            ToolStripMenuItem set = new ToolStripMenuItem(L.T("tray.settings"));
            set.Click += delegate { OpenSettings(0); };
            menu.Items.Add(set);

            ToolStripMenuItem exit = new ToolStripMenuItem(L.T("tray.exit"));
            exit.Click += delegate { ExitApp(); };
            menu.Items.Add(exit);
        }

        public void Notify(string text)
        {
            try { tray.ShowBalloonTip(4000, "ResSwitch", text, ToolTipIcon.Info); } catch { }
        }

        // ---------------------------------------------------------------- settings

        public void OpenSettings(int tab)
        {
            if (settings != null) { settings.Activate(); return; }
            hotkey.Unregister(); // so the shortcut can be captured / tested while the window is open
            SettingsForm f = new SettingsForm(cfg.Clone(), delegate(uint m, int k) { return hotkey.Test(m, k); }, tab);
            settings = f;
            DialogResult r = DialogResult.Cancel;
            try { r = f.ShowDialog(); }
            finally { settings = null; }
            if (r == DialogResult.OK) ApplyNewConfig(f.Result);
            f.Dispose();
            RegisterHotkey();
            if (hotkeyFailed) Notify(L.T("notify.hotkeyFail", HotkeyUtil.Format(cfg.HotkeyMods, cfg.HotkeyKey)));
        }

        private void ApplyNewConfig(AppConfig c)
        {
            c.Sanitize();
            c.FirstRunDone = true;
            c.LastUpdateCheck = cfg.LastUpdateCheck;
            cfg = c;
            cfg.Save();
            L.Init(cfg.Language);
            RefreshIcon(true);
        }

        // ---------------------------------------------------------------- exit

        public void ExitApp()
        {
            if (quitting) return;
            quitting = true;
            try { watcher.Stop(); } catch { }
            try { if (cfg.RestoreOnExit) RestoreNative(); } catch (Exception ex) { Log.Write("restore: " + ex.Message); }
            try { SystemEvents.DisplaySettingsChanged -= OnDisplayChanged; } catch { }
            hotkey.Dispose();
            tray.Visible = false;
            tray.Dispose();
            ExitThread();
        }
    }
}
