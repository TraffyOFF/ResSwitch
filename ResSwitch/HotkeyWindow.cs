using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ResSwitch
{
    internal static class HotkeyUtil
    {
        public const int MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8;

        public static string Format(int mods, int key)
        {
            StringBuilder sb = new StringBuilder();
            if ((mods & MOD_CONTROL) != 0) sb.Append("Ctrl+");
            if ((mods & MOD_ALT) != 0) sb.Append("Alt+");
            if ((mods & MOD_SHIFT) != 0) sb.Append("Shift+");
            if ((mods & MOD_WIN) != 0) sb.Append("Win+");
            Keys k = (Keys)key;
            string name = k.ToString();
            if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) name = name.Substring(1);
            else if (name.StartsWith("NumPad")) name = "Num" + name.Substring(6);
            sb.Append(name);
            return sb.ToString();
        }
    }

    // Hidden window that receives WM_HOTKEY.
    internal class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0xB001;
        private const int TEST_ID = 0xB002;
        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event EventHandler Pressed;
        private bool registered;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        public bool Register(uint mods, int vk)
        {
            Unregister();
            registered = RegisterHotKey(Handle, HOTKEY_ID, mods | MOD_NOREPEAT, (uint)vk);
            return registered;
        }

        public void Unregister()
        {
            if (registered) { UnregisterHotKey(Handle, HOTKEY_ID); registered = false; }
        }

        // True if the combination is free (it is released again immediately).
        public bool Test(uint mods, int vk)
        {
            bool ok = RegisterHotKey(Handle, TEST_ID, mods | MOD_NOREPEAT, (uint)vk);
            if (ok) UnregisterHotKey(Handle, TEST_ID);
            return ok;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                EventHandler h = Pressed;
                if (h != null) h(this, EventArgs.Empty);
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Unregister();
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}
