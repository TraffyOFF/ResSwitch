using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ResSwitch
{
    internal static class Theme
    {
        public static readonly Color DarkBack = Color.FromArgb(32, 32, 32);
        public static readonly Color DarkControl = Color.FromArgb(51, 51, 51);
        public static readonly Color DarkBorder = Color.FromArgb(96, 96, 96);
        public static readonly Color DarkFore = Color.FromArgb(240, 240, 240);
        public static readonly Color DarkHover = Color.FromArgb(70, 70, 70);
        public static readonly Color Accent = Color.FromArgb(0, 120, 212);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        public static bool IsDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k == null) return false;
                    object v = k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        public static void ApplyTitleBar(Form f)
        {
            if (!IsDark()) return;
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
            }
            catch { }
        }

        private static Icon appIcon;

        // The exe's own icon (WinForms would otherwise show its generic default icon in title bars).
        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try { appIcon = Icon.ExtractAssociatedIcon(Paths.ExePath); } catch { }
                }
                return appIcon;
            }
        }

        // Icon for every window + dark mode (light mode keeps the standard Windows look).
        public static void Apply(Form f)
        {
            Icon ic = AppIcon;
            if (ic != null) f.Icon = ic;
            if (!IsDark()) return;
            f.BackColor = DarkBack;
            f.ForeColor = DarkFore;
            f.HandleCreated += delegate { ApplyTitleBar(f); };
            if (f.IsHandleCreated) ApplyTitleBar(f);
            Recurse(f);
        }

        private static void Recurse(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                Style(c);
                Recurse(c);
            }
        }

        private static void DarkExplorer(Control c)
        {
            if (c.IsHandleCreated) SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
            else c.HandleCreated += delegate { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); };
        }

        private static void Style(Control c)
        {
            c.ForeColor = DarkFore;
            if (c is RadioButton && ((RadioButton)c).Appearance == Appearance.Button)
            {
                RadioButton rb = (RadioButton)c;
                rb.FlatStyle = FlatStyle.Flat;
                rb.BackColor = DarkControl;
                rb.FlatAppearance.BorderColor = DarkBorder;
                rb.FlatAppearance.CheckedBackColor = Accent;
                rb.FlatAppearance.MouseOverBackColor = DarkHover;
            }
            else if (c is Button)
            {
                Button b = (Button)c;
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = DarkControl;
                b.FlatAppearance.BorderColor = DarkBorder;
                b.FlatAppearance.MouseOverBackColor = DarkHover;
            }
            else if (c is ComboBox)
            {
                ComboBox cb = (ComboBox)c;
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = DarkControl;
                DarkExplorer(cb);
                // flat combos can keep stale pixels when resized or shown: repaint them
                cb.Resize += delegate { cb.Invalidate(); };
                cb.VisibleChanged += delegate { cb.Invalidate(); };
            }
            else if (c is TextBox || c is ListBox || c is ListView || c is NumericUpDown)
            {
                c.BackColor = DarkControl;
                if (c is TextBox) ((TextBox)c).BorderStyle = BorderStyle.FixedSingle;
                if (c is ListBox) ((ListBox)c).BorderStyle = BorderStyle.FixedSingle;
                if (c is ListView) ((ListView)c).BorderStyle = BorderStyle.FixedSingle;
                if (!(c is NumericUpDown)) DarkExplorer(c);
            }
            else
            {
                c.BackColor = DarkBack;
            }
        }

        // ---- dark tray menu ----
        public static void ApplyMenu(ContextMenuStrip m)
        {
            if (IsDark())
            {
                m.Renderer = new DarkRenderer();
                m.BackColor = DarkControl;
                m.ForeColor = DarkFore;
            }
            else
            {
                m.Renderer = new ToolStripProfessionalRenderer();
                m.BackColor = SystemColors.Menu;
                m.ForeColor = SystemColors.MenuText;
            }
        }

        private class DarkRenderer : ToolStripProfessionalRenderer
        {
            public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? DarkFore : Color.FromArgb(140, 140, 140);
                base.OnRenderItemText(e);
            }
            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = DarkFore;
                base.OnRenderArrow(e);
            }
        }

        private class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return DarkControl; } }
            public override Color ImageMarginGradientBegin { get { return DarkControl; } }
            public override Color ImageMarginGradientMiddle { get { return DarkControl; } }
            public override Color ImageMarginGradientEnd { get { return DarkControl; } }
            public override Color MenuBorder { get { return DarkBorder; } }
            public override Color MenuItemBorder { get { return DarkHover; } }
            public override Color MenuItemSelected { get { return DarkHover; } }
            public override Color MenuItemSelectedGradientBegin { get { return DarkHover; } }
            public override Color MenuItemSelectedGradientEnd { get { return DarkHover; } }
            public override Color MenuItemPressedGradientBegin { get { return DarkHover; } }
            public override Color MenuItemPressedGradientMiddle { get { return DarkHover; } }
            public override Color MenuItemPressedGradientEnd { get { return DarkHover; } }
            public override Color SeparatorDark { get { return DarkBorder; } }
            public override Color SeparatorLight { get { return DarkBorder; } }
            public override Color CheckBackground { get { return Accent; } }
            public override Color CheckSelectedBackground { get { return Accent; } }
            public override Color CheckPressedBackground { get { return Accent; } }
        }
    }
}
