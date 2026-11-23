using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("ResSwitch")]
[assembly: AssemblyDescription("Switch the display resolution between native and 4:3")]
[assembly: AssemblyProduct("ResSwitch")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace ResSwitch
{
    internal static class Program
    {
        public const string QuitEventName = @"Local\ResSwitch.Quit";
        private const string MutexName = @"Local\ResSwitch.SingleInstance";

        public static string Version
        {
            get
            {
                System.Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            foreach (string a in args)
                if (string.Equals(a, "/quit", StringComparison.OrdinalIgnoreCase))
                    return RequestQuit();

            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created) return 0; // another instance is already running

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Log.Write("UI exception: " + e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log.Write("Fatal: " + e.ExceptionObject);
                };
                try
                {
                    Application.Run(new TrayController());
                }
                catch (Exception ex)
                {
                    Log.Write("Run: " + ex);
                }
                finally
                {
                    try { mutex.ReleaseMutex(); } catch { }
                }
            }
            return 0;
        }

        // "ResSwitch.exe /quit": asks the running instance to exit cleanly (used by the installer).
        private static int RequestQuit()
        {
            try
            {
                EventWaitHandle h = EventWaitHandle.OpenExisting(QuitEventName);
                h.Set();
                for (int i = 0; i < 50; i++)
                {
                    bool free;
                    using (Mutex m = new Mutex(true, MutexName, out free))
                    {
                        if (free) { m.ReleaseMutex(); break; }
                    }
                    Thread.Sleep(100);
                }
            }
            catch { }
            return 0;
        }
    }
}
