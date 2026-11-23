using System;
using System.Management;

namespace ResSwitch
{
    internal static class GpuHelp
    {
        public enum Brand { Unknown, Nvidia, Amd, Intel }

        public class Info
        {
            public string Names = "";
            public Brand Brand = Brand.Unknown;
        }

        private static Brand Classify(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("nvidia") || n.Contains("geforce") || n.Contains("quadro") || n.Contains("rtx") || n.Contains("gtx")) return Brand.Nvidia;
            if (n.Contains("amd") || n.Contains("radeon") || n.Contains("ati ")) return Brand.Amd;
            if (n.Contains("intel")) return Brand.Intel;
            return Brand.Unknown;
        }

        // WMI query, can take a second: call from a background thread.
        public static Info Detect()
        {
            Info info = new Info();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                using (ManagementObjectCollection coll = s.Get())
                {
                    foreach (ManagementBaseObject o in coll)
                    {
                        object v = o["Name"];
                        if (v == null) continue;
                        string name = v.ToString();
                        if (info.Names.Length > 0) info.Names += ", ";
                        info.Names += name;
                        Brand b = Classify(name);
                        // a discrete GPU wins over an integrated Intel one
                        if (b != Brand.Unknown && (info.Brand == Brand.Unknown || info.Brand == Brand.Intel)) info.Brand = b;
                    }
                }
            }
            catch (Exception ex) { Log.Write("gpu: " + ex.Message); }
            return info;
        }

        public static string HelpText(Info info)
        {
            string key;
            switch (info.Brand)
            {
                case Brand.Nvidia: key = "gpu.nvidia"; break;
                case Brand.Amd: key = "gpu.amd"; break;
                case Brand.Intel: key = "gpu.intel"; break;
                default: key = "gpu.generic"; break;
            }
            return Localization.T(key).Replace("\n", "\r\n");
        }
    }
}
