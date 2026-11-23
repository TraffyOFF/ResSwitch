using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ResSwitch
{
    public class DisplayMode
    {
        public int Width;
        public int Height;
        public int Freq;
        public override string ToString() { return Width + " x " + Height + " @ " + Freq + " Hz"; }
    }

    public class DisplayInfo
    {
        public string Name;         // device name, e.g. \\.\DISPLAY2 (can change when displays are plugged / unplugged)
        public string Description;  // graphics adapter
        public string MonitorName;  // real monitor model read from its EDID, e.g. "LG ULTRAGEAR"
        public string MonitorId;    // stable identifier of the physical monitor (model + serial)
        public bool Primary;

        public string Label { get { return string.IsNullOrEmpty(MonitorName) ? Description : MonitorName; } }
    }

    internal static class DisplayManager
    {
        private const int ENUM_CURRENT_SETTINGS = -1;
        private const uint DM_POSITION = 0x00000020;
        private const uint DM_BITSPERPEL = 0x00040000;
        private const uint DM_PELSWIDTH = 0x00080000;
        private const uint DM_PELSHEIGHT = 0x00100000;
        private const uint DM_DISPLAYFREQUENCY = 0x00400000;
        private const uint CDS_TEST = 0x00000002;
        private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
        private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        private static DEVMODE NewDevMode()
        {
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
            return dm;
        }

        // monitor DeviceID -> { name, stable id }: the registry (EDID) is read once per monitor
        private static readonly Dictionary<string, string[]> monitorCache = new Dictionary<string, string[]>();

        public static List<DisplayInfo> GetDisplays()
        {
            List<DisplayInfo> list = new List<DisplayInfo>();
            for (uint i = 0; i < 32; i++)
            {
                DISPLAY_DEVICE d = new DISPLAY_DEVICE();
                d.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (!EnumDisplayDevices(null, i, ref d, 0)) break;
                if ((d.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
                DisplayInfo info = new DisplayInfo();
                info.Name = d.DeviceName;
                info.Description = d.DeviceString;
                info.Primary = (d.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;

                // the monitor plugged into this output
                info.MonitorName = null;
                info.MonitorId = d.DeviceName;
                DISPLAY_DEVICE m = new DISPLAY_DEVICE();
                m.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (EnumDisplayDevices(d.DeviceName, 0, ref m, 0) && !string.IsNullOrEmpty(m.DeviceID))
                {
                    string[] det;
                    lock (monitorCache)
                    {
                        if (!monitorCache.TryGetValue(m.DeviceID, out det))
                        {
                            det = ReadMonitorDetails(m.DeviceID, m.DeviceString);
                            monitorCache[m.DeviceID] = det;
                        }
                    }
                    info.MonitorName = det[0];
                    info.MonitorId = det[1];
                }
                list.Add(info);
            }
            return list;
        }

        // deviceId looks like MONITOR\GSM5B09\{4d36e96e-e325-11ce-bfc1-08002be10318}\0004.
        // Returns { friendly name, stable id }. Never throws.
        private static string[] ReadMonitorDetails(string deviceId, string fallbackName)
        {
            string name = fallbackName;
            string id = deviceId;
            try
            {
                string[] p = deviceId.Split('\\');
                if (p.Length >= 4)
                {
                    string model = p[1];
                    string driver = p[2] + "\\" + p[3];
                    using (RegistryKey root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + model))
                    {
                        if (root != null)
                        {
                            foreach (string inst in root.GetSubKeyNames())
                            {
                                using (RegistryKey k = root.OpenSubKey(inst))
                                {
                                    if (k == null) continue;
                                    string drv = k.GetValue("Driver") as string;
                                    if (drv == null || !string.Equals(drv, driver, StringComparison.OrdinalIgnoreCase)) continue;
                                    string edidName, edidSerial;
                                    using (RegistryKey dp = k.OpenSubKey("Device Parameters"))
                                    {
                                        byte[] edid = dp == null ? null : dp.GetValue("EDID") as byte[];
                                        ParseEdid(edid, out edidName, out edidSerial);
                                    }
                                    if (!string.IsNullOrEmpty(edidName)) name = edidName;
                                    // same model + same serial = same physical monitor; no serial: fall back to the port instance
                                    id = model + "-" + (string.IsNullOrEmpty(edidSerial) ? inst : edidSerial);
                                    return new string[] { name, id };
                                }
                            }
                        }
                    }
                    id = model + "-" + p[3];
                }
            }
            catch (Exception ex) { Log.Write("edid: " + ex.Message); }
            return new string[] { name, id };
        }

        private static void ParseEdid(byte[] e, out string name, out string serial)
        {
            name = null; serial = null;
            if (e == null || e.Length < 128) return;
            string textSerial = null;
            for (int off = 54; off <= 108; off += 18)
            {
                if (e[off] != 0 || e[off + 1] != 0 || e[off + 2] != 0) continue;
                byte tag = e[off + 3];
                if (tag != 0xFC && tag != 0xFF) continue;
                string text = System.Text.Encoding.ASCII.GetString(e, off + 5, 13);
                int nl = text.IndexOf('\n');
                if (nl >= 0) text = text.Substring(0, nl);
                text = text.Trim();
                if (text.Length == 0) continue;
                if (tag == 0xFC) name = text; else textSerial = text;
            }
            uint num = (uint)(e[12] | (e[13] << 8) | (e[14] << 16) | (e[15] << 24));
            if (!string.IsNullOrEmpty(textSerial)) serial = textSerial;
            else if (num != 0) serial = num.ToString("x");
        }

        // The real device name of the display a profile is for, or null when that monitor is not connected.
        // A profile with a MonitorId is pinned to that physical monitor; without one it follows the primary
        // display (or, for old configs, a device name).
        public static string ResolveDevice(Profile p)
        {
            if (p == null) return null;
            if (!string.IsNullOrEmpty(p.MonitorId))
            {
                foreach (DisplayInfo d in GetDisplays())
                    if (string.Equals(d.MonitorId, p.MonitorId, StringComparison.OrdinalIgnoreCase)) return d.Name;
                return null;
            }
            return FindDevice(p.Device);
        }

        // Returns the real device name for a profile device ("" = primary), or null if it is not attached.
        public static string FindDevice(string name)
        {
            List<DisplayInfo> all = GetDisplays();
            if (all.Count == 0) return null;
            if (string.IsNullOrEmpty(name))
            {
                foreach (DisplayInfo d in all) if (d.Primary) return d.Name;
                return all[0].Name;
            }
            foreach (DisplayInfo d in all)
                if (string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) return d.Name;
            return null;
        }

        public static DisplayMode GetCurrent(string device)
        {
            DEVMODE dm = NewDevMode();
            if (!EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref dm)) return null;
            DisplayMode m = new DisplayMode();
            m.Width = (int)dm.dmPelsWidth;
            m.Height = (int)dm.dmPelsHeight;
            m.Freq = (int)dm.dmDisplayFrequency;
            return m;
        }

        // Modes really offered by the driver for this display (32-bit colour, unique w/h/frequency).
        public static List<DisplayMode> GetModes(string device)
        {
            List<DisplayMode> all = new List<DisplayMode>();
            HashSet<long> seen = new HashSet<long>();
            bool any32 = false;
            List<DEVMODE> raw = new List<DEVMODE>();
            for (int i = 0; i < 5000; i++)
            {
                DEVMODE dm = NewDevMode();
                if (!EnumDisplaySettings(device, i, ref dm)) break;
                raw.Add(dm);
                if (dm.dmBitsPerPel >= 32) any32 = true;
            }
            foreach (DEVMODE dm in raw)
            {
                if (any32 && dm.dmBitsPerPel < 32) continue;
                long key = ((long)dm.dmPelsWidth << 40) | ((long)dm.dmPelsHeight << 16) | dm.dmDisplayFrequency;
                if (!seen.Add(key)) continue;
                DisplayMode m = new DisplayMode();
                m.Width = (int)dm.dmPelsWidth;
                m.Height = (int)dm.dmPelsHeight;
                m.Freq = (int)dm.dmDisplayFrequency;
                all.Add(m);
            }
            all.Sort(delegate(DisplayMode a, DisplayMode b)
            {
                if (a.Width != b.Width) return b.Width.CompareTo(a.Width);
                if (a.Height != b.Height) return b.Height.CompareTo(a.Height);
                return b.Freq.CompareTo(a.Freq);
            });
            return all;
        }

        // Validates the mode with CDS_TEST, then applies it dynamically (not written to the registry,
        // so a reboot always returns to the Windows default).
        public static bool Apply(string device, int w, int h, int freq)
        {
            if (device == null) return false;
            DisplayMode cur = GetCurrent(device);
            if (cur != null && cur.Width == w && cur.Height == h && (freq <= 0 || cur.Freq == freq)) return true;

            // Tries, in order: width/height/frequency only (the driver accepts this most reliably), then the
            // same with automatic frequency, then the complete current DEVMODE with the new values.
            int[] freqs = freq > 0 ? new int[] { freq, 0 } : new int[] { 0 };
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (int f in freqs)
                {
                    DEVMODE dm = NewDevMode();
                    if (!EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref dm)) return false;
                    uint fields;
                    if (pass == 0)
                    {
                        fields = DM_PELSWIDTH | DM_PELSHEIGHT;
                        // a secondary display must keep its place in the desktop layout
                        if (dm.dmPositionX != 0 || dm.dmPositionY != 0) fields |= DM_POSITION;
                    }
                    else fields = dm.dmFields | DM_PELSWIDTH | DM_PELSHEIGHT;
                    dm.dmPelsWidth = (uint)w;
                    dm.dmPelsHeight = (uint)h;
                    if (f > 0) { dm.dmDisplayFrequency = (uint)f; fields |= DM_DISPLAYFREQUENCY; }
                    else { dm.dmDisplayFrequency = 0; fields &= ~DM_DISPLAYFREQUENCY; }
                    dm.dmFields = fields;
                    if (ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero) != 0) continue;
                    if (ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, 0, IntPtr.Zero) == 0) return true;
                }
            }
            return false;
        }

        private static int Gcd(int a, int b) { while (b != 0) { int t = a % b; a = b; b = t; } return a; }

        public static string RatioLabel(int w, int h)
        {
            if (w <= 0 || h <= 0) return "?";
            double r = (double)w / h;
            string[] names = { "4:3", "5:4", "3:2", "16:10", "16:9", "21:9", "32:9" };
            double[] vals = { 4.0 / 3, 5.0 / 4, 3.0 / 2, 16.0 / 10, 16.0 / 9, 21.0 / 9, 32.0 / 9 };
            for (int i = 0; i < names.Length; i++)
            {
                double tol = names[i] == "21:9" ? 0.06 : 0.015;
                if (Math.Abs(r - vals[i]) < tol) return names[i];
            }
            int g = Gcd(w, h);
            return (w / g) + ":" + (h / g);
        }
    }
}
