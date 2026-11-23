using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using L = ResSwitch.Localization;

namespace ResSwitch
{
    // Text box that captures a key combination instead of typing text.
    internal class HotkeyBox : TextBox
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public int Mods;
        public int Key;
        public Func<int, int, bool> Accept; // called with a new combination; false = refused

        public HotkeyBox()
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            Cursor = Cursors.Hand;
        }

        public void SetValue(int mods, int key)
        {
            Mods = mods; Key = key;
            Text = HotkeyUtil.Format(mods, key);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            Keys mods = keyData & Keys.Modifiers;
            bool win = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
            if (mods == Keys.None && !win && (code == Keys.Tab || code == Keys.Escape || code == Keys.Return))
                return base.ProcessCmdKey(ref msg, keyData);
            if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu || code == Keys.LWin || code == Keys.RWin || code == Keys.None)
                return true; // modifier alone: wait for the real key

            int m = 0;
            if ((mods & Keys.Control) != 0) m |= HotkeyUtil.MOD_CONTROL;
            if ((mods & Keys.Alt) != 0) m |= HotkeyUtil.MOD_ALT;
            if ((mods & Keys.Shift) != 0) m |= HotkeyUtil.MOD_SHIFT;
            if (win) m |= HotkeyUtil.MOD_WIN;
            if (Accept == null || Accept(m, (int)code)) SetValue(m, (int)code);
            return true;
        }
    }

    internal class SettingsForm : Form
    {
        private class DisplayItem
        {
            public string Value; public string Text;
            public override string ToString() { return Text; }
        }
        private class ResItem
        {
            public int W, H; public bool Missing;
            public override string ToString()
            {
                return W + " x " + H + "   (" + DisplayManager.RatioLabel(W, H) + ")" + (Missing ? "  *" : "");
            }
        }
        private class FreqItem
        {
            public int Hz;
            public override string ToString() { return Hz == 0 ? L.T("prof.freqAuto") : Hz + " Hz"; }
        }
        private class LangItem
        {
            public string Code, Name;
            public override string ToString() { return Name; }
        }

        private readonly AppConfig cfg;
        private readonly Func<uint, int, bool> testHotkey;
        private int loading;

        // chrome
        private const int PageCount = 5;
        private readonly Panel[] pages = new Panel[PageCount];
        private readonly RadioButton[] tabs = new RadioButton[PageCount];

        // main ("Switch") page
        private Label lblEmptyRules;

        // profiles page
        private ListBox lstProfiles;
        private TextBox txtName;
        private ComboBox cboDisplay, cboRatio, cboRes, cboFreq, cboToggleA, cboToggleB;
        private Button btnAdd, btnDel, btnTest, btnUseCurrent, btnExport, btnImport;
        private TableLayoutPanel editor;

        // general page
        private HotkeyBox hkBox;
        private Label hkMsg, updStatus;
        private CheckBox chkStartup, chkRestore, chkNotify, chkUpdates;
        private ComboBox cboLang;

        // games page
        private CheckBox chkAuto;
        private NumericUpDown numDelay;
        private ListView lstRules;
        private ComboBox cboRuleProfile;

        // gpu page
        private Label gpuLabel;
        private TextBox gpuText;

        private readonly Dictionary<string, List<DisplayMode>> modeCache = new Dictionary<string, List<DisplayMode>>();

        public AppConfig Result { get { return cfg; } }

        public SettingsForm(AppConfig config, Func<uint, int, bool> testHotkey, int startTab)
        {
            cfg = config;
            this.testHotkey = testHotkey;

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = L.T("set.title");
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(760, 540);
            MinimumSize = new Size(700, 520);

            BuildChrome();
            BuildMainPage();
            BuildProfilesPage();
            BuildGeneralPage();
            BuildGamesPage();
            BuildGpuPage();

            LoadProfilesList();
            RefreshRules();
            ShowPage(startTab >= 0 && startTab < PageCount ? startTab : 0);
            Theme.Apply(this);
            lblEmptyRules.ForeColor = Theme.IsDark() ? Color.FromArgb(170, 170, 170) : SystemColors.GrayText;
        }

        // ============================================================ chrome

        private void BuildChrome()
        {
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.Height = 44;
            bar.Padding = new Padding(10, 8, 10, 4);
            string[] keys = { "tab.main", "tab.profiles", "tab.general", "tab.games", "tab.gpu" };
            for (int i = 0; i < PageCount; i++)
            {
                RadioButton rb = new RadioButton();
                rb.Appearance = Appearance.Button;
                rb.Text = L.T(keys[i]);
                rb.TextAlign = ContentAlignment.MiddleCenter;
                rb.Size = new Size(138, 32);
                rb.Margin = new Padding(0, 0, 6, 0);
                int idx = i;
                rb.CheckedChanged += delegate { if (rb.Checked) ShowPage(idx); };
                tabs[i] = rb;
                bar.Controls.Add(rb);
            }

            FlowLayoutPanel bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 48;
            bottom.FlowDirection = FlowDirection.RightToLeft;
            bottom.Padding = new Padding(10, 8, 10, 8);
            Button cancel = new Button();
            cancel.Text = L.T("common.cancel");
            cancel.Size = new Size(110, 30);
            cancel.DialogResult = DialogResult.Cancel;
            Button ok = new Button();
            ok.Text = L.T("common.ok");
            ok.Size = new Size(110, 30);
            ok.Click += delegate { OnOk(); };
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(ok);
            CancelButton = cancel;

            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            for (int i = 0; i < PageCount; i++)
            {
                pages[i] = new Panel();
                pages[i].Dock = DockStyle.Fill;
                pages[i].Padding = new Padding(10);
                pages[i].Visible = false;
                host.Controls.Add(pages[i]);
            }
            Controls.Add(host);
            Controls.Add(bottom);
            Controls.Add(bar);
        }

        private void ShowPage(int idx)
        {
            if (loading == 0)
            {
                // both pages edit the same profiles: refresh the one being shown
                if (idx == 0) LoadMain();
                else if (idx == 1 && lstProfiles != null) LoadSelected();
            }
            for (int i = 0; i < PageCount; i++)
            {
                pages[i].Visible = i == idx;
                if (tabs[i].Checked != (i == idx)) tabs[i].Checked = i == idx;
            }
        }

        // ============================================================ main page ("Switch")

        private class ModeCard
        {
            public Label Name, Sub;   // Sub: which monitor (only shown with several displays)
            public ComboBox Ratio, Res, Freq;
            public Button Test, UseCurrent;
        }

        private ModeCard card1, card2;

        // One editable "mode" card, bound to the profile returned by get().
        private GroupBox BuildCard(string title, Func<Profile> get, out ModeCard card)
        {
            ModeCard c = new ModeCard();
            GroupBox g = new GroupBox();
            g.Text = title;
            g.Dock = DockStyle.Fill;
            g.Padding = new Padding(12, 8, 12, 12);
            g.Height = 245;

            c.Name = new Label();
            c.Name.AutoSize = true;
            c.Name.Font = new Font(Font.FontFamily, 14f, FontStyle.Bold);
            c.Name.Location = new Point(14, 28);
            c.Sub = new Label();
            c.Sub.AutoSize = true;
            c.Sub.Font = new Font(Font.FontFamily, 8.5f, FontStyle.Regular);
            c.Sub.Location = new Point(16, 50);

            TableLayoutPanel f = new TableLayoutPanel();
            f.ColumnCount = 2;
            f.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            f.RowCount = 3;
            for (int ri = 0; ri < 3; ri++) f.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            f.Location = new Point(10, 68);
            f.Size = new Size(330, 108);
            c.Ratio = Combo(); c.Res = Combo(); c.Freq = Combo();
            c.Ratio.Items.AddRange(new object[] { L.T("ratio.all"), "4:3", "16:10", "16:9" });
            c.Ratio.SelectedIndex = 0;
            f.Controls.Add(Lbl(L.T("prof.ratio")), 0, 0); f.Controls.Add(c.Ratio, 1, 0);
            f.Controls.Add(Lbl(L.T("prof.res")), 0, 1); f.Controls.Add(c.Res, 1, 1);
            f.Controls.Add(Lbl(L.T("prof.freq")), 0, 2); f.Controls.Add(c.Freq, 1, 2);

            c.Test = new Button();
            c.Test.Text = L.T("prof.test");
            c.Test.Size = new Size(110, 32);
            c.Test.Location = new Point(14, 192);
            c.UseCurrent = new Button();
            c.UseCurrent.Text = L.T("prof.useCurrent");
            c.UseCurrent.Size = new Size(205, 32);
            c.UseCurrent.Location = new Point(134, 192);

            ModeCard mc = c;
            c.Test.Click += delegate { TestProfile(get()); };
            c.UseCurrent.Click += delegate
            {
                Profile p = get();
                if (p == null) return;
                string dev = DisplayManager.ResolveDevice(p);
                DisplayMode cur = dev == null ? null : DisplayManager.GetCurrent(dev);
                if (cur == null) return;
                p.Width = cur.Width; p.Height = cur.Height; p.Freq = cur.Freq;
                LoadMain();
            };
            c.Ratio.SelectedIndexChanged += delegate
            {
                if (loading > 0) return;
                Profile p = get();
                loading++;
                try { PopulateRes(p, mc.Res, mc.Ratio); PopulateFreq(p, mc.Freq); }
                finally { loading--; }
            };
            c.Res.SelectedIndexChanged += delegate
            {
                if (loading > 0) return;
                Profile p = get();
                ResItem it = mc.Res.SelectedItem as ResItem;
                if (p == null || it == null) return;
                SetResolution(p, it);
                loading++;
                try { PopulateFreq(p, mc.Freq); }
                finally { loading--; }
            };
            c.Freq.SelectedIndexChanged += delegate
            {
                if (loading > 0) return;
                Profile p = get();
                FreqItem it = mc.Freq.SelectedItem as FreqItem;
                if (p != null && it != null) p.Freq = it.Hz;
            };

            g.Controls.Add(c.Name);
            g.Controls.Add(c.Sub);
            g.Controls.Add(f);
            g.Controls.Add(c.Test);
            g.Controls.Add(c.UseCurrent);
            card = c;
            return g;
        }

        private void BuildMainPage()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            for (int i = 0; i < 4; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Label intro = new Label();
            intro.Text = L.T("main.intro");
            intro.AutoSize = true;
            intro.Margin = new Padding(0, 2, 0, 10);
            intro.Font = new Font(Font.FontFamily, 10f, FontStyle.Regular);
            root.Controls.Add(intro, 0, 0);

            TableLayoutPanel cards = new TableLayoutPanel();
            cards.Dock = DockStyle.Fill;
            cards.AutoSize = true;
            cards.ColumnCount = 2;
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            GroupBox g1 = BuildCard("1 - " + L.T("main.mode1"), delegate { return cfg.Find(cfg.ToggleA); }, out card1);
            g1.Margin = new Padding(0, 0, 8, 0);
            GroupBox g2 = BuildCard("2 - " + L.T("main.mode2"), delegate { return cfg.Find(cfg.ToggleB); }, out card2);
            g2.Margin = new Padding(8, 0, 0, 0);
            cards.Controls.Add(g1, 0, 0);
            cards.Controls.Add(g2, 1, 0);
            root.Controls.Add(cards, 0, 1);

            Label note = new Label();
            note.Text = L.T("main.mode1note");
            note.AutoSize = true;
            note.MaximumSize = new Size(700, 0);
            note.Margin = new Padding(0, 8, 0, 0);
            root.Controls.Add(note, 0, 2);

            // shortcut
            hkBox = new HotkeyBox();
            hkBox.Width = 220;
            hkBox.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
            hkBox.SetValue(cfg.HotkeyMods, cfg.HotkeyKey);
            hkMsg = new Label();
            hkMsg.AutoSize = true;
            hkMsg.ForeColor = Color.FromArgb(220, 60, 60);
            hkBox.Accept = delegate(int m, int k)
            {
                if (m == 0) { hkMsg.Text = L.T("gen.hotkeyNeedMod"); return false; }
                if (m == hkBox.Mods && k == hkBox.Key) { hkMsg.Text = ""; return true; }
                if (testHotkey != null && !testHotkey((uint)m, k)) { hkMsg.Text = L.T("gen.hotkeyInUse"); return false; }
                hkMsg.Text = "";
                return true;
            };
            FlowLayoutPanel hk = Flow();
            hk.Margin = new Padding(0, 10, 0, 0);
            hk.Controls.Add(Lbl(L.T("gen.hotkey")));
            hk.Controls.Add(hkBox);
            hk.Controls.Add(hkMsg);
            root.Controls.Add(hk, 0, 3);
            Label hint = new Label();
            hint.Text = L.T("gen.hotkeyHint") + "  " + L.T("main.clickIcon");
            hint.AutoSize = true;
            hint.MaximumSize = new Size(700, 0);
            root.Controls.Add(hint, 0, 4);

            pages[0].Controls.Add(root);
        }

        private void LoadCard(ModeCard c, Profile p, bool enabled)
        {
            c.Name.Text = p == null ? "-" : p.Name;
            c.Sub.Text = "";
            List<DisplayInfo> displays = DisplayManager.GetDisplays();
            if (p != null && displays.Count > 1)
            {
                string dev = DisplayManager.ResolveDevice(p);
                string label = null;
                foreach (DisplayInfo d in displays) if (d.Name == dev) label = d.Label;
                c.Sub.Text = label ?? L.T("prof.disconnected");
            }
            foreach (Control x in new Control[] { c.Ratio, c.Res, c.Freq, c.Test, c.UseCurrent }) x.Enabled = enabled;
            PopulateRes(p, c.Res, c.Ratio);
            PopulateFreq(p, c.Freq);
        }

        private void LoadMain()
        {
            loading++;
            try
            {
                Profile a = cfg.Find(cfg.ToggleA);
                Profile b = cfg.Find(cfg.ToggleB);
                LoadCard(card1, a, a != null);
                LoadCard(card2, b, b != null && b != a);
            }
            finally { loading--; }
        }

        private static Label Lbl(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(0, 6, 8, 6);
            return l;
        }

        private static ComboBox Combo()
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Dock = DockStyle.Fill;
            return c;
        }

        private static Button Btn(string text, int width)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = false;
            b.Size = new Size(width, 30);
            b.Margin = new Padding(0, 4, 8, 4);
            return b;
        }

        private static FlowLayoutPanel Flow()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.AutoSize = true;
            f.Dock = DockStyle.Fill;
            f.WrapContents = true;
            return f;
        }

        // ============================================================ profiles page

        private void BuildProfilesPage()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 2;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            Label profIntro = new Label();
            profIntro.Text = L.T("prof.intro");
            profIntro.AutoSize = true;
            profIntro.MaximumSize = new Size(700, 0);
            profIntro.Margin = new Padding(0, 0, 0, 10);
            root.Controls.Add(profIntro, 0, 0);
            root.SetColumnSpan(profIntro, 2);

            // left: list + add / delete
            TableLayoutPanel left = new TableLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.ColumnCount = 1;
            left.RowCount = 3;
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Controls.Add(Lbl(L.T("prof.list")), 0, 0);
            lstProfiles = new ListBox();
            lstProfiles.Dock = DockStyle.Fill;
            lstProfiles.IntegralHeight = false;
            lstProfiles.SelectedIndexChanged += delegate { if (loading == 0) LoadSelected(); };
            left.Controls.Add(lstProfiles, 0, 1);
            FlowLayoutPanel lb = Flow();
            btnAdd = Btn(L.T("prof.add"), 90);
            btnDel = Btn(L.T("prof.delete"), 90);
            btnAdd.Click += delegate { AddProfile(); };
            btnDel.Click += delegate { DeleteProfile(); };
            lb.Controls.Add(btnAdd);
            lb.Controls.Add(btnDel);
            left.Controls.Add(lb, 0, 2);
            root.Controls.Add(left, 0, 1);

            // right: editor
            editor = new TableLayoutPanel();
            editor.Dock = DockStyle.Fill;
            editor.ColumnCount = 2;
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            editor.Padding = new Padding(14, 0, 0, 0);

            txtName = new TextBox();
            txtName.Dock = DockStyle.Fill;
            txtName.MaxLength = 64;
            txtName.TextChanged += delegate { OnNameChanged(); };
            cboDisplay = Combo();
            cboRatio = Combo();
            cboRes = Combo();
            cboFreq = Combo();
            cboRatio.Items.AddRange(new object[] { L.T("ratio.all"), "4:3", "16:10", "16:9" });
            cboRatio.SelectedIndex = 0;
            cboDisplay.SelectedIndexChanged += delegate { OnDisplayChosen(); };
            cboRatio.SelectedIndexChanged += delegate { if (loading == 0) { loading++; try { PopulateRes(Sel()); PopulateFreq(Sel()); } finally { loading--; } } };
            cboRes.SelectedIndexChanged += delegate { OnResChosen(); };
            cboFreq.SelectedIndexChanged += delegate { OnFreqChosen(); };

            AddRow(L.T("prof.name"), txtName);
            AddRow(L.T("prof.display"), cboDisplay);
            AddRow(L.T("prof.ratio"), cboRatio);
            AddRow(L.T("prof.res"), cboRes);
            AddRow(L.T("prof.freq"), cboFreq);

            FlowLayoutPanel tb = Flow();
            btnTest = Btn(L.T("prof.test"), 110);
            btnUseCurrent = Btn(L.T("prof.useCurrent"), 190);
            btnTest.Click += delegate { TestSelected(); };
            btnUseCurrent.Click += delegate { UseCurrent(); };
            tb.Controls.Add(btnTest);
            tb.Controls.Add(btnUseCurrent);
            AddRow("", tb);

            // toggle pair
            cboToggleA = Combo();
            cboToggleB = Combo();
            cboToggleA.SelectedIndexChanged += delegate { if (loading == 0 && cboToggleA.SelectedItem != null) cfg.ToggleA = ((Profile)cboToggleA.SelectedItem).Name; };
            cboToggleB.SelectedIndexChanged += delegate { if (loading == 0 && cboToggleB.SelectedItem != null) cfg.ToggleB = ((Profile)cboToggleB.SelectedItem).Name; };
            TableLayoutPanel tg = new TableLayoutPanel();
            tg.Dock = DockStyle.Fill;
            tg.AutoSize = true;
            tg.ColumnCount = 3;
            tg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            tg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            tg.Controls.Add(cboToggleA, 0, 0);
            tg.Controls.Add(Lbl(L.T("prof.and")), 1, 0);
            tg.Controls.Add(cboToggleB, 2, 0);
            AddRow(L.T("prof.toggleBetween"), tg);

            FlowLayoutPanel ie = Flow();
            btnExport = Btn(L.T("prof.export"), 130);
            btnImport = Btn(L.T("prof.import"), 130);
            btnExport.Click += delegate { ExportProfiles(); };
            btnImport.Click += delegate { ImportProfiles(); };
            ie.Controls.Add(btnExport);
            ie.Controls.Add(btnImport);
            AddRow("", ie);

            // the display chooser only matters with several displays
            if (DisplayManager.GetDisplays().Count <= 1)
            {
                editor.GetControlFromPosition(0, 1).Visible = false;
                editor.GetControlFromPosition(1, 1).Visible = false;
            }

            root.Controls.Add(editor, 1, 1);
            pages[1].Controls.Add(root);
        }

        private void AddRow(string label, Control c)
        {
            int r = editor.RowCount;
            editor.RowCount = r + 1;
            editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label l = Lbl(label);
            editor.Controls.Add(l, 0, r);
            editor.Controls.Add(c, 1, r);
        }

        private Profile Sel() { return lstProfiles.SelectedItem as Profile; }

        private void LoadProfilesList()
        {
            loading++;
            try
            {
                lstProfiles.Items.Clear();
                foreach (Profile p in cfg.Profiles) lstProfiles.Items.Add(p);
                if (lstProfiles.Items.Count > 0) lstProfiles.SelectedIndex = 0;
                RefreshToggleCombos();
            }
            finally { loading--; }
            LoadSelected();
        }

        private void RefreshToggleCombos()
        {
            loading++;
            try
            {
                cboToggleA.Items.Clear(); cboToggleB.Items.Clear();
                foreach (Profile p in cfg.Profiles) { cboToggleA.Items.Add(p); cboToggleB.Items.Add(p); }
                cboToggleA.SelectedItem = cfg.Find(cfg.ToggleA);
                cboToggleB.SelectedItem = cfg.Find(cfg.ToggleB);

                object keep = cboRuleProfile == null ? null : cboRuleProfile.SelectedItem;
                if (cboRuleProfile != null)
                {
                    cboRuleProfile.Items.Clear();
                    foreach (Profile p in cfg.Profiles) cboRuleProfile.Items.Add(p);
                    if (keep != null && cboRuleProfile.Items.Contains(keep)) cboRuleProfile.SelectedItem = keep;
                    else
                    {
                        Profile b = cfg.Find(cfg.ToggleB);
                        if (b != null) cboRuleProfile.SelectedItem = b;
                        else if (cboRuleProfile.Items.Count > 0) cboRuleProfile.SelectedIndex = 0;
                    }
                }
            }
            finally { loading--; }
        }

        private void LoadSelected()
        {
            loading++;
            try
            {
                Profile p = Sel();
                bool has = p != null;
                foreach (Control c in new Control[] { txtName, cboDisplay, cboRatio, cboRes, cboFreq, btnTest, btnUseCurrent })
                    c.Enabled = has;
                btnDel.Enabled = has && !p.IsNative;
                if (!has) return;
                txtName.Text = p.Name;
                PopulateDisplays(p);
                PopulateRes(p);
                PopulateFreq(p);
            }
            finally { loading--; }
        }

        private void PopulateDisplays(Profile p)
        {
            cboDisplay.Items.Clear();
            DisplayItem sel = null;
            foreach (DisplayInfo d in DisplayManager.GetDisplays())
            {
                DisplayItem it = new DisplayItem();
                // the primary display is stored as "" (follows whichever display is primary); the others by monitor id
                it.Value = d.Primary ? "" : d.MonitorId;
                it.Text = d.Label + (d.Primary ? "  (" + L.T("prof.primary") + ")" : "");
                cboDisplay.Items.Add(it);
                if (p.MonitorId.Length > 0 ? string.Equals(it.Value, p.MonitorId, StringComparison.OrdinalIgnoreCase)
                    : (p.Device.Length > 0 ? string.Equals(d.Name, p.Device, StringComparison.OrdinalIgnoreCase) : d.Primary))
                    sel = it;
            }
            if (sel == null && (p.MonitorId.Length > 0 || p.Device.Length > 0))
            {
                sel = new DisplayItem();
                sel.Value = p.MonitorId;
                sel.Text = (p.MonitorId.Length > 0 ? p.MonitorId : p.Device) + "  (" + L.T("prof.disconnected") + ")";
                cboDisplay.Items.Add(sel);
            }
            if (sel != null) cboDisplay.SelectedItem = sel;
            else if (cboDisplay.Items.Count > 0) cboDisplay.SelectedIndex = 0;
        }

        private List<DisplayMode> ModesFor(Profile p)
        {
            string dev = DisplayManager.ResolveDevice(p);
            if (dev == null) return new List<DisplayMode>();
            List<DisplayMode> m;
            if (!modeCache.TryGetValue(dev, out m)) { m = DisplayManager.GetModes(dev); modeCache[dev] = m; }
            return m;
        }

        private void PopulateRes(Profile p) { PopulateRes(p, cboRes, cboRatio); }
        private void PopulateFreq(Profile p) { PopulateFreq(p, cboFreq); }

        // Fills a resolution list with the modes the driver offers for the profile's display,
        // filtered by the aspect-ratio combo (first item = all).
        private void PopulateRes(Profile p, ComboBox res, ComboBox ratio)
        {
            res.Items.Clear();
            if (p == null) return;
            string filter = ratio.SelectedIndex <= 0 ? "" : (string)ratio.SelectedItem;
            HashSet<string> seen = new HashSet<string>();
            ResItem sel = null;
            foreach (DisplayMode m in ModesFor(p))
            {
                if (filter.Length > 0 && DisplayManager.RatioLabel(m.Width, m.Height) != filter) continue;
                if (!seen.Add(m.Width + "x" + m.Height)) continue;
                ResItem it = new ResItem();
                it.W = m.Width; it.H = m.Height;
                res.Items.Add(it);
                if (m.Width == p.Width && m.Height == p.Height) sel = it;
            }
            if (sel == null && p.IsSet)
            {
                sel = new ResItem();
                sel.W = p.Width; sel.H = p.Height; sel.Missing = true;
                res.Items.Insert(0, sel);
            }
            if (sel != null) res.SelectedItem = sel;
        }

        private void PopulateFreq(Profile p, ComboBox freq)
        {
            freq.Items.Clear();
            if (p == null) return;
            FreqItem auto = new FreqItem();
            freq.Items.Add(auto);
            FreqItem sel = auto;
            if (p.IsSet)
            {
                foreach (DisplayMode m in ModesFor(p))
                {
                    if (m.Width != p.Width || m.Height != p.Height) continue;
                    FreqItem f = new FreqItem();
                    f.Hz = m.Freq;
                    freq.Items.Add(f);
                    if (m.Freq == p.Freq) sel = f;
                }
                if (p.Freq > 0 && sel == auto)
                {
                    FreqItem f = new FreqItem();
                    f.Hz = p.Freq;
                    freq.Items.Add(f);
                    sel = f;
                }
            }
            freq.SelectedItem = sel;
        }

        // Sets the resolution; keeps the refresh rate only if the new resolution offers it.
        private void SetResolution(Profile p, ResItem it)
        {
            p.Width = it.W; p.Height = it.H;
            bool ok = false;
            foreach (DisplayMode m in ModesFor(p)) if (m.Width == p.Width && m.Height == p.Height && m.Freq == p.Freq) ok = true;
            if (!ok) p.Freq = 0;
        }

        private void OnNameChanged()
        {
            if (loading > 0) return;
            Profile p = Sel();
            if (p == null) return;
            string old = p.Name;
            string nw = txtName.Text;
            p.Name = nw;
            if (cfg.ToggleA == old) cfg.ToggleA = nw;
            if (cfg.ToggleB == old) cfg.ToggleB = nw;
            foreach (GameRule r in cfg.Rules) if (r.Profile == old) r.Profile = nw;
            loading++;
            try
            {
                int i = lstProfiles.SelectedIndex;
                lstProfiles.Items[i] = p;
                lstProfiles.SelectedIndex = i;
                RefreshToggleCombos();
                RefreshRules();
            }
            finally { loading--; }
        }

        private void OnDisplayChosen()
        {
            if (loading > 0) return;
            Profile p = Sel();
            DisplayItem it = cboDisplay.SelectedItem as DisplayItem;
            if (p == null || it == null) return;
            p.MonitorId = it.Value;
            p.Device = "";
            loading++;
            try { PopulateRes(p); PopulateFreq(p); }
            finally { loading--; }
        }

        private void OnResChosen()
        {
            if (loading > 0) return;
            Profile p = Sel();
            ResItem it = cboRes.SelectedItem as ResItem;
            if (p == null || it == null) return;
            SetResolution(p, it);
            loading++;
            try { PopulateFreq(p); }
            finally { loading--; }
        }

        private void OnFreqChosen()
        {
            if (loading > 0) return;
            Profile p = Sel();
            FreqItem it = cboFreq.SelectedItem as FreqItem;
            if (p == null || it == null) return;
            p.Freq = it.Hz;
        }

        private string UniqueName(string baseName)
        {
            string n = baseName;
            int i = 2;
            while (cfg.Find(n) != null) n = baseName + " (" + (i++) + ")";
            return n;
        }

        private void AddProfile()
        {
            Profile p = new Profile();
            p.Name = UniqueName(L.T("prof.new"));
            string dev = DisplayManager.FindDevice("");
            DisplayMode cur = dev == null ? null : DisplayManager.GetCurrent(dev);
            if (cur != null) { p.Width = cur.Width; p.Height = cur.Height; p.Freq = cur.Freq; }
            cfg.Profiles.Add(p);
            LoadProfilesList();
            lstProfiles.SelectedItem = p;
            txtName.Focus();
            txtName.SelectAll();
        }

        private void DeleteProfile()
        {
            Profile p = Sel();
            if (p == null) return;
            if (p.IsNative) { MessageBox.Show(this, L.T("prof.cantDeleteNative"), "ResSwitch", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            cfg.Profiles.Remove(p);
            cfg.Rules.RemoveAll(delegate(GameRule r) { return string.Equals(r.Profile, p.Name, StringComparison.OrdinalIgnoreCase); });
            cfg.FixToggles();
            LoadProfilesList();
            RefreshRules();
        }

        private void UseCurrent()
        {
            Profile p = Sel();
            if (p == null) return;
            string dev = DisplayManager.ResolveDevice(p);
            DisplayMode cur = dev == null ? null : DisplayManager.GetCurrent(dev);
            if (cur == null) return;
            p.Width = cur.Width; p.Height = cur.Height; p.Freq = cur.Freq;
            loading++;
            try { PopulateRes(p); PopulateFreq(p); }
            finally { loading--; }
        }

        private void TestSelected() { TestProfile(Sel()); }

        private void TestProfile(Profile p)
        {
            if (p == null || !p.IsSet) return;
            string dev = DisplayManager.ResolveDevice(p);
            DisplayMode prev = dev == null ? null : DisplayManager.GetCurrent(dev);
            if (dev == null || prev == null || !DisplayManager.Apply(dev, p.Width, p.Height, p.Freq))
            {
                MessageBox.Show(this, L.T("test.fail"), "ResSwitch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult r;
            using (ConfirmRevertForm f = new ConfirmRevertForm(10)) r = f.ShowDialog();
            if (r != DialogResult.Yes) DisplayManager.Apply(dev, prev.Width, prev.Height, prev.Freq);
        }

        private void ExportProfiles()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "ResSwitch profiles (*.json)|*.json";
                d.FileName = "resswitch-profiles.json";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    AppConfig.Export(d.FileName, cfg.Profiles);
                    MessageBox.Show(this, L.T("prof.exported"), "ResSwitch", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "ResSwitch", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void ImportProfiles()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "ResSwitch profiles (*.json)|*.json";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    List<Profile> list = AppConfig.Import(d.FileName);
                    foreach (Profile p in list)
                    {
                        p.Name = UniqueName(p.Name);
                        cfg.Profiles.Add(p);
                    }
                    LoadProfilesList();
                    MessageBox.Show(this, L.T("prof.imported", list.Count), "ResSwitch", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception)
                {
                    MessageBox.Show(this, L.T("prof.importFail"), "ResSwitch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // ============================================================ general page

        private void BuildGeneralPage()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 3;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320)); // fixed width for the language list
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // filler

            chkStartup = Chk(L.T("gen.startup"), Startup.IsEnabled());
            chkRestore = Chk(L.T("gen.restoreExit"), cfg.RestoreOnExit);
            chkNotify = Chk(L.T("gen.notify"), cfg.Notify);
            chkUpdates = Chk(L.T("gen.updates"), cfg.CheckUpdates);
            int row = 0;
            foreach (CheckBox c in new CheckBox[] { chkStartup, chkRestore, chkNotify, chkUpdates })
            {
                t.Controls.Add(c, 0, row);
                t.SetColumnSpan(c, 3);
                row++;
            }

            Button check = Btn(L.T("gen.checkNow"), 150);
            check.Margin = new Padding(24, 2, 8, 2);
            updStatus = new Label();
            updStatus.AutoSize = true;
            updStatus.Margin = new Padding(0, 10, 0, 0);
            check.Click += delegate { CheckUpdatesNow(); };
            FlowLayoutPanel uf = Flow();
            uf.Controls.Add(check);
            uf.Controls.Add(updStatus);
            t.Controls.Add(uf, 0, row);
            t.SetColumnSpan(uf, 3);
            row++;

            cboLang = Combo();
            LangItem auto = new LangItem();
            auto.Code = ""; auto.Name = L.T("gen.langAuto");
            cboLang.Items.Add(auto);
            LangItem sel = auto;
            foreach (KeyValuePair<string, string> kv in L.Available())
            {
                LangItem li = new LangItem();
                li.Code = kv.Key; li.Name = kv.Value;
                cboLang.Items.Add(li);
                if (kv.Key == cfg.Language) sel = li;
            }
            cboLang.SelectedItem = sel;
            t.Controls.Add(Lbl(L.T("gen.language")), 0, row);
            t.Controls.Add(cboLang, 1, row);
            row++;

            Label ver = new Label();
            ver.Text = "ResSwitch " + Program.Version;
            ver.AutoSize = true;
            ver.Margin = new Padding(0, 24, 0, 0);
            ver.ForeColor = SystemColors.GrayText;
            t.Controls.Add(ver, 0, row);
            t.SetColumnSpan(ver, 3);

            pages[2].Controls.Add(t);
        }

        private static CheckBox Chk(string text, bool value)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Checked = value;
            c.AutoSize = true;
            c.Margin = new Padding(0, 6, 0, 6);
            return c;
        }

        private void CheckUpdatesNow()
        {
            if (!UpdateCheck.Configured) { updStatus.Text = L.T("notify.updateNotConfigured"); return; }
            updStatus.Text = "...";
            UpdateCheck.CheckAsync(delegate(UpdateCheck.Result r)
            {
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (!r.Ok) updStatus.Text = L.T("notify.updateFail");
                        else if (!r.Newer) updStatus.Text = L.T("notify.noUpdate");
                        else
                        {
                            updStatus.Text = L.T("notify.update", r.Version);
                            if (UpdateCheck.IsSafeUrl(r.Url)) try { Process.Start(r.Url); } catch { }
                        }
                    }));
                }
                catch { }
            });
        }

        // ============================================================ games page

        private void BuildGamesPage()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 1;
            t.RowCount = 7;
            for (int i = 0; i < 7; i++) t.RowStyles.Add(new RowStyle(i == 4 ? SizeType.Percent : SizeType.AutoSize, i == 4 ? 100 : 0));

            chkAuto = Chk(L.T("games.enable"), !cfg.AutoSwitchDisabled);
            t.Controls.Add(chkAuto, 0, 0);

            FlowLayoutPanel d = Flow();
            d.Controls.Add(Lbl(L.T("games.delay")));
            numDelay = new NumericUpDown();
            numDelay.Minimum = 0; numDelay.Maximum = 60;
            numDelay.Value = Math.Max(0, Math.Min(60, cfg.GameDelaySeconds));
            numDelay.Width = 70;
            d.Controls.Add(numDelay);
            t.Controls.Add(d, 0, 1);

            Label hint = new Label();
            hint.Text = L.T("games.hint");
            hint.AutoSize = true;
            hint.MaximumSize = new Size(700, 0);
            hint.Margin = new Padding(0, 4, 0, 6);
            t.Controls.Add(hint, 0, 2);

            lstRules = new ListView();
            lstRules.Dock = DockStyle.Fill;
            lstRules.View = View.Details;
            lstRules.FullRowSelect = true;
            lstRules.HideSelection = false;
            lstRules.MultiSelect = false;
            lstRules.Columns.Add(L.T("games.colProgram"), 300);
            lstRules.Columns.Add(L.T("games.colProfile"), 200);
            lstRules.SelectedIndexChanged += delegate
            {
                if (lstRules.SelectedItems.Count == 0) return;
                GameRule r = (GameRule)lstRules.SelectedItems[0].Tag;
                Profile p = cfg.Find(r.Profile);
                loading++;
                try { if (p != null) cboRuleProfile.SelectedItem = p; }
                finally { loading--; }
            };
            lblEmptyRules = new Label();
            lblEmptyRules.Text = L.T("games.empty");
            lblEmptyRules.AutoSize = true;
            lblEmptyRules.MaximumSize = new Size(700, 0);
            lblEmptyRules.Margin = new Padding(0, 2, 0, 6);
            t.Controls.Add(lblEmptyRules, 0, 3);
            t.Controls.Add(lstRules, 0, 4);

            TableLayoutPanel pf = new TableLayoutPanel();
            pf.Dock = DockStyle.Fill;
            pf.AutoSize = true;
            pf.ColumnCount = 3;
            pf.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pf.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            pf.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pf.Controls.Add(Lbl(L.T("games.pickProfile")), 0, 0);
            cboRuleProfile = Combo();
            cboRuleProfile.SelectedIndexChanged += delegate
            {
                if (loading > 0 || lstRules.SelectedItems.Count == 0 || cboRuleProfile.SelectedItem == null) return;
                GameRule r = (GameRule)lstRules.SelectedItems[0].Tag;
                r.Profile = ((Profile)cboRuleProfile.SelectedItem).Name;
                lstRules.SelectedItems[0].SubItems[1].Text = r.Profile;
            };
            pf.Controls.Add(cboRuleProfile, 1, 0);
            t.Controls.Add(pf, 0, 5);

            FlowLayoutPanel bf = Flow();
            Button bRun = Btn(L.T("games.addRunning"), 210);
            Button bSteam = Btn(L.T("games.addSteam"), 210);
            Button bBrowse = Btn(L.T("games.addBrowse"), 130);
            Button bRemove = Btn(L.T("games.remove"), 110);
            bRun.Click += delegate { AddFromRunning(); };
            bSteam.Click += delegate { AddFromSteam(); };
            bBrowse.Click += delegate { AddFromBrowse(); };
            bRemove.Click += delegate
            {
                if (lstRules.SelectedItems.Count == 0) return;
                cfg.Rules.Remove((GameRule)lstRules.SelectedItems[0].Tag);
                RefreshRules();
            };
            bf.Controls.Add(bRun); bf.Controls.Add(bSteam); bf.Controls.Add(bBrowse); bf.Controls.Add(bRemove);
            t.Controls.Add(bf, 0, 6);

            pages[3].Controls.Add(t);
            RefreshToggleCombos();
        }

        private void RefreshRules()
        {
            if (lstRules == null) return;
            lstRules.Items.Clear();
            foreach (GameRule r in cfg.Rules)
            {
                ListViewItem it = new ListViewItem(r.Exe);
                it.SubItems.Add(r.Profile);
                it.Tag = r;
                lstRules.Items.Add(it);
            }
            if (lblEmptyRules != null) lblEmptyRules.Visible = lstRules.Items.Count == 0;
        }

        private void AddRule(string exe)
        {
            Profile p = cboRuleProfile.SelectedItem as Profile;
            if (p == null) return;
            string key = GameRule.Key(exe);
            foreach (GameRule r in cfg.Rules)
                if (GameRule.Key(r.Exe) == key) { r.Profile = p.Name; RefreshRules(); return; }
            GameRule nr = new GameRule();
            nr.Exe = Path.GetFileName(exe);
            if (!nr.Exe.ToLowerInvariant().EndsWith(".exe")) nr.Exe += ".exe";
            nr.Profile = p.Name;
            cfg.Rules.Add(nr);
            RefreshRules();
        }

        private void AddFromRunning()
        {
            List<string> names = new List<string>();
            List<string> labels = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            string self = Path.GetFileNameWithoutExtension(Paths.ExePath).ToLowerInvariant();
            using (new WaitCursor(this))
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        string title = p.MainWindowTitle;
                        if (p.MainWindowHandle != IntPtr.Zero && title.Length > 0 && p.ProcessName.ToLowerInvariant() != self && seen.Add(p.ProcessName.ToLowerInvariant()))
                        {
                            names.Add(p.ProcessName + ".exe");
                            labels.Add(p.ProcessName + ".exe   -   " + title);
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            if (names.Count == 0) return;
            using (PickListForm f = new PickListForm(L.T("games.pickProcess"), L.T("games.pickProcess"), labels, 0))
            {
                if (f.ShowDialog(this) == DialogResult.OK && f.SelectedIndex >= 0) AddRule(names[f.SelectedIndex]);
            }
        }

        private void AddFromBrowse()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "Programs (*.exe)|*.exe";
                if (d.ShowDialog(this) == DialogResult.OK) AddRule(d.FileName);
            }
        }

        private void AddFromSteam()
        {
            List<SteamHelper.Game> games;
            using (new WaitCursor(this)) games = SteamHelper.FindGames();
            if (games.Count == 0) { MessageBox.Show(this, L.T("games.steamNone"), "ResSwitch", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            List<string> labels = new List<string>();
            foreach (SteamHelper.Game g in games) labels.Add(g.Name);
            SteamHelper.Game chosen;
            using (PickListForm f = new PickListForm(L.T("games.steamPick"), L.T("games.steamPick"), labels, 0))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.SelectedIndex < 0) return;
                chosen = games[f.SelectedIndex];
            }
            List<SteamHelper.ExeCandidate> exes;
            using (new WaitCursor(this)) exes = SteamHelper.FindExecutables(chosen.Dir);
            if (exes.Count == 0) { MessageBox.Show(this, L.T("games.noExe"), "ResSwitch", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (exes.Count == 1) { AddRule(exes[0].Path); return; }
            List<string> el = new List<string>();
            foreach (SteamHelper.ExeCandidate c in exes) el.Add(c.ToString());
            using (PickListForm f = new PickListForm(L.T("games.pickExe"), chosen.Name + " - " + L.T("games.pickExe"), el, 0))
            {
                if (f.ShowDialog(this) == DialogResult.OK && f.SelectedIndex >= 0) AddRule(exes[f.SelectedIndex].Path);
            }
        }

        private class WaitCursor : IDisposable
        {
            private readonly Control c;
            public WaitCursor(Control c) { this.c = c; c.Cursor = Cursors.WaitCursor; }
            public void Dispose() { c.Cursor = Cursors.Default; }
        }

        // ============================================================ GPU page

        private void BuildGpuPage()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 1;
            t.RowCount = 2;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            gpuLabel = new Label();
            gpuLabel.AutoSize = true;
            gpuLabel.Margin = new Padding(0, 4, 0, 8);
            gpuLabel.Text = "...";
            gpuText = new TextBox();
            gpuText.Multiline = true;
            gpuText.ReadOnly = true;
            gpuText.ScrollBars = ScrollBars.Vertical;
            gpuText.Dock = DockStyle.Fill;
            t.Controls.Add(gpuLabel, 0, 0);
            t.Controls.Add(gpuText, 0, 1);
            pages[4].Controls.Add(t);

            ThreadPool.QueueUserWorkItem(delegate
            {
                GpuHelp.Info info = GpuHelp.Detect();
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        gpuLabel.Text = L.T("gpu.detected", info.Names.Length > 0 ? info.Names : "?");
                        gpuText.Text = GpuHelp.HelpText(info);
                        gpuText.SelectionStart = 0;
                        gpuText.SelectionLength = 0;
                    }));
                }
                catch { }
            });
        }

        // ============================================================ commit

        private void OnOk()
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Profile p in cfg.Profiles)
            {
                p.Name = p.Name.Trim();
                if (p.Name.Length == 0 || !names.Add(p.Name))
                {
                    ShowPage(1);
                    MessageBox.Show(this, L.T("prof.nameInvalid"), "ResSwitch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            cfg.HotkeyMods = hkBox.Mods;
            cfg.HotkeyKey = hkBox.Key;
            cfg.RestoreOnExit = chkRestore.Checked;
            cfg.Notify = chkNotify.Checked;
            cfg.CheckUpdates = chkUpdates.Checked;
            LangItem li = cboLang.SelectedItem as LangItem;
            cfg.Language = li == null ? "" : li.Code;
            cfg.AutoSwitchDisabled = !chkAuto.Checked;
            cfg.GameDelaySeconds = (int)numDelay.Value;
            Startup.Set(chkStartup.Checked);
            DialogResult = DialogResult.OK;
        }
    }
}
