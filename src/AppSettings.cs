using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FolderSyncTool
{
    public enum ScheduleType { Daily, Interval, OnLogoff }

    /// <summary>One backup pair (source/dest) plus per-pair v5 options.</summary>
    public class PairInfo
    {
        public string Source = "";
        public string Dest = "";
        public bool Enabled = true;

        // v5: per-pair file type filter. Empty => fall back to global filter.
        public string Types = "";

        // v5: keep timestamped copies of overwritten files in <dest>\_versions
        public bool KeepVersions = false;

        // v5: try VSS shadow copy so locked files (PST etc.) can be read
        public bool UseShadowCopy = false;
    }

    public class AppSettings
    {
        // ---- schedule ----
        public ScheduleType Schedule = ScheduleType.Daily;
        public string DailyTime = "14:00";      // local HH:mm
        public int IntervalMinutes = 60;
        public bool RunMissedOnStartup = false;  // catch-up

        // ---- global file filter (used when pair.Types empty) ----
        public string GlobalTypes = ".docx,.xlsx,.pptx,.pdf";

        // ---- log ----
        public bool LogEnabled = true;
        public bool MirrorDeletes = false;       // delete dest extras
        public bool LogOnlyCopied = false;       // log copied files only

        // ---- behavior ----
        public bool StartWithWindows = false;
        public bool MinimizeToTray = true;

        // ---- v5: versioning ----
        public int VersionCount = 5;             // kept copies per file (>=1)
        public bool KeepReceivedVersions = false; // also version received LAN files

        // ---- v5: LAN encryption ----
        public bool EncryptTransfers = false;
        public string AesKeyB64 = "";            // 32-byte key, base64

        // ---- v5: appearance ----
        public bool DarkMode = false;

        // ---- v5: manual peers (added by IP) ----
        public string ManualPeers = "";          // "ip;ip;ip"

        // ---- pairs ----
        public List<PairInfo> Pairs = new List<PairInfo>();

        public static string SettingsPathOverride = null;

        public static string SettingsPath
        {
            get
            {
                if (SettingsPathOverride != null) return SettingsPathOverride;
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FolderSyncTool");
                return Path.Combine(dir, "settings.ini");
            }
        }

        public static string TasksPath
        {
            get
            {
                if (SettingsPathOverride != null)
                    return Path.Combine(Path.GetDirectoryName(SettingsPathOverride), "tasks.ini");
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FolderSyncTool");
                return Path.Combine(dir, "tasks.ini");
            }
        }

        // ------------------------------------------------------------------

        public void Load()
        {
            string path = SettingsPath;
            if (!File.Exists(path)) { MigrateV1(path); return; }

            Dictionary<string, string> map = ReadIni(path);
            Schedule = ParseEnum(Get(map, "Schedule", Schedule.ToString()), ScheduleType.Daily);
            DailyTime = Get(map, "DailyTime", DailyTime);
            int.TryParse(Get(map, "IntervalMinutes", IntervalMinutes.ToString()), out IntervalMinutes);
            if (IntervalMinutes < 1) IntervalMinutes = 1;
            RunMissedOnStartup = ParseBool(Get(map, "RunMissedOnStartup", "0"), false);

            GlobalTypes = Get(map, "GlobalTypes", GlobalTypes);
            LogEnabled = ParseBool(Get(map, "LogEnabled", "1"), true);
            MirrorDeletes = ParseBool(Get(map, "MirrorDeletes", "0"), false);
            LogOnlyCopied = ParseBool(Get(map, "LogOnlyCopied", "0"), false);
            StartWithWindows = ParseBool(Get(map, "StartWithWindows", "0"), false);
            MinimizeToTray = ParseBool(Get(map, "MinimizeToTray", "1"), true);

            int vcount = 5;
            int.TryParse(Get(map, "VersionCount", "5"), out vcount);
            VersionCount = Math.Max(1, vcount);
            KeepReceivedVersions = ParseBool(Get(map, "KeepReceivedVersions", "0"), false);

            EncryptTransfers = ParseBool(Get(map, "EncryptTransfers", "0"), false);
            AesKeyB64 = Get(map, "AesKeyB64", "");
            DarkMode = ParseBool(Get(map, "DarkMode", "0"), false);
            ManualPeers = Get(map, "ManualPeers", "");

            Pairs.Clear();
            int count = 0;
            int.TryParse(Get(map, "PairCount", "0"), out count);
            for (int i = 1; i <= count; i++)
            {
                string src = Get(map, "Pair" + i + ".Source", null);
                string dst = Get(map, "Pair" + i + ".Dest", null);
                if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst)) continue;
                PairInfo p = new PairInfo();
                p.Source = src;
                p.Dest = dst;
                p.Enabled = ParseBool(Get(map, "Pair" + i + ".Enabled", "1"), true);
                p.Types = Get(map, "Pair" + i + ".Types", "");
                p.KeepVersions = ParseBool(Get(map, "Pair" + i + ".KeepVersions", "0"), false);
                p.UseShadowCopy = ParseBool(Get(map, "Pair" + i + ".UseShadowCopy", "0"), false);
                Pairs.Add(p);
            }
        }

        public void Save()
        {
            string path = SettingsPath;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Schedule=" + Schedule);
            sb.AppendLine("DailyTime=" + DailyTime);
            sb.AppendLine("IntervalMinutes=" + IntervalMinutes);
            sb.AppendLine("RunMissedOnStartup=" + BoolStr(RunMissedOnStartup));
            sb.AppendLine("GlobalTypes=" + GlobalTypes ?? "");
            sb.AppendLine("LogEnabled=" + BoolStr(LogEnabled));
            sb.AppendLine("MirrorDeletes=" + BoolStr(MirrorDeletes));
            sb.AppendLine("LogOnlyCopied=" + BoolStr(LogOnlyCopied));
            sb.AppendLine("StartWithWindows=" + BoolStr(StartWithWindows));
            sb.AppendLine("MinimizeToTray=" + BoolStr(MinimizeToTray));
            sb.AppendLine("VersionCount=" + VersionCount);
            sb.AppendLine("KeepReceivedVersions=" + BoolStr(KeepReceivedVersions));
            sb.AppendLine("EncryptTransfers=" + BoolStr(EncryptTransfers));
            sb.AppendLine("AesKeyB64=" + (AesKeyB64 ?? ""));
            sb.AppendLine("DarkMode=" + BoolStr(DarkMode));
            sb.AppendLine("ManualPeers=" + (ManualPeers ?? ""));
            sb.AppendLine("PairCount=" + Pairs.Count);
            for (int i = 0; i < Pairs.Count; i++)
            {
                PairInfo p = Pairs[i];
                int n = i + 1;
                sb.AppendLine("Pair" + n + ".Source=" + p.Source);
                sb.AppendLine("Pair" + n + ".Dest=" + p.Dest);
                sb.AppendLine("Pair" + n + ".Enabled=" + BoolStr(p.Enabled));
                sb.AppendLine("Pair" + n + ".Types=" + (p.Types ?? ""));
                sb.AppendLine("Pair" + n + ".KeepVersions=" + BoolStr(p.KeepVersions));
                sb.AppendLine("Pair" + n + ".UseShadowCopy=" + BoolStr(p.UseShadowCopy));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>v1 settings lived in old flat keys — bring them over once.</summary>
        private void MigrateV1(string path)
        {
            string old = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FolderSyncTool", "backup.ini");
            if (!File.Exists(old)) return;

            Dictionary<string, string> map = ReadIni(old);
            string src = Get(map, "Source", null);
            string dst = Get(map, "Dest", null);
            if (!string.IsNullOrEmpty(src) && !string.IsNullOrEmpty(dst))
            {
                PairInfo p = new PairInfo();
                p.Source = src; p.Dest = dst;
                Pairs.Add(p);
            }
            GlobalTypes = Get(map, "Types", GlobalTypes);
            LogEnabled = ParseBool(Get(map, "LogEnabled", "1"), true);
            MirrorDeletes = ParseBool(Get(map, "MirrorDeletes", "0"), false);
            Schedule = ParseEnum(Get(map, "Schedule", Schedule.ToString()), ScheduleType.Daily);
            DailyTime = Get(map, "DailyTime", DailyTime);
            int.TryParse(Get(map, "IntervalMinutes", "60"), out IntervalMinutes);
            if (IntervalMinutes < 1) IntervalMinutes = 1;
        }

        // helpers ---------------------------------------------------------

        private static Dictionary<string, string> ReadIni(string path)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(path))
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#") || t.StartsWith(";")) continue;
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                string k = t.Substring(0, eq).Trim();
                string v = t.Substring(eq + 1).Trim();
                map[k] = v;
            }
            return map;
        }

        private static string Get(Dictionary<string, string> map, string key, string def)
        {
            string v;
            if (map.TryGetValue(key, out v)) return v;
            return def;
        }

        private static string BoolStr(bool b) { return b ? "1" : "0"; }

        /// <summary>bool.TryParse only accepts True/False — our INI writes 1/0.</summary>
        private static bool ParseBool(string s, bool def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            s = s.Trim();
            if (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return def;
        }

        private static ScheduleType ParseEnum(string s, ScheduleType def)
        {
            try { return (ScheduleType)Enum.Parse(typeof(ScheduleType), s, true); }
            catch { return def; }
        }
    }
}
