using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Web.Script.Serialization;

namespace ResSwitch
{
    // Opt-in. Reads the latest GitHub release; sends no user data, downloads and installs nothing.
    internal static class UpdateCheck
    {
        // "owner/repository" of the GitHub project whose latest release is checked.
        public const string Repo = "TraffyOFF/ResSwitch";

        public static bool Configured { get { return !Repo.StartsWith("CHANGE-ME"); } }

        public class Result
        {
            public bool Ok;
            public bool Newer;
            public string Version = "";
            public string Url = "";
        }

        public static void CheckAsync(Action<Result> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Result r = new Result();
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
                    req.UserAgent = "ResSwitch-UpdateCheck";
                    req.Accept = "application/vnd.github+json";
                    req.Timeout = 8000;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                    {
                        Dictionary<string, object> d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(sr.ReadToEnd());
                        string tag = d.ContainsKey("tag_name") ? Convert.ToString(d["tag_name"]) : "";
                        string url = d.ContainsKey("html_url") ? Convert.ToString(d["html_url"]) : "";
                        Version latest, current;
                        if (TryParse(tag, out latest) && TryParse(Program.Version, out current))
                        {
                            r.Ok = true;
                            r.Newer = latest > current;
                            r.Version = tag.TrimStart('v', 'V');
                            r.Url = url;
                        }
                    }
                }
                catch (Exception ex) { Log.Write("update check: " + ex.Message); }
                try { done(r); } catch { }
            });
        }

        private static bool TryParse(string s, out Version v)
        {
            v = null;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim().TrimStart('v', 'V');
            int i = 0;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            s = s.Substring(0, i).Trim('.');
            if (s.IndexOf('.') < 0) s += ".0";
            return Version.TryParse(s, out v);
        }

        // Only GitHub pages are ever opened.
        public static bool IsSafeUrl(string url)
        {
            return url != null && url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
