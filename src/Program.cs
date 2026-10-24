using System;
using System.Threading;
using System.Windows.Forms;

namespace FolderSyncTool
{
    public static class Program
    {
        public static AppSettings Settings;
        public static string TasksPathOverride = null;   // test hook
        public static Mutex InstanceMutex;

        [STAThread]
        static void Main(string[] args)
        {
            bool startMinimized = false;
            foreach (string a in args)
                if (string.Equals(a, "/minimized", StringComparison.OrdinalIgnoreCase))
                    startMinimized = true;

            bool created;
            InstanceMutex = new Mutex(true, "FolderSyncTool_SingleInstance", out created);
            if (!created) return; // second launch — exit silently

            Settings = new AppSettings();
            Settings.Load();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(startMinimized));
        }
    }
}
