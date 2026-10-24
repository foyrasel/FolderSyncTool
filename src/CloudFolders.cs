using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace FolderSyncTool
{
    public class CloudFolder
    {
        public string Name;
        public string Path;
        public bool Exists;
    }

    /// <summary>
    /// Detects local cloud-sync folders. v5 improves OneDrive/Google Drive
    /// resolution (registry + env + known fallbacks) so destination picks
    /// always land inside a folder the cloud client actually syncs.
    /// </summary>
    public static class CloudFolders
    {
        /// <summary>All known cloud folders for this machine (existing first).</summary>
        public static List<CloudFolder> GetKnownCloudFolders()
        {
            List<CloudFolder> list = new List<CloudFolder>();
            foreach (string p in OneDriveCandidates())
                AddUnique(list, "OneDrive", p);
            foreach (string p in GoogleDriveCandidates())
                AddUnique(list, "Google Drive", p);
            foreach (string p in DropboxCandidates())
                AddUnique(list, "Dropbox", p);
            return list;
        }

        /// <summary>Suggested destination: <cloud>\FolderSyncTool (first existing cloud).</summary>
        public static string SuggestDestination()
        {
            List<CloudFolder> clouds = GetKnownCloudFolders();
            foreach (CloudFolder c in clouds)
            {
                if (c.Exists)
                    return Path.Combine(c.Path, "FolderSyncTool");
            }
            return null;
        }

        /// <summary>Pure helper: first candidate that exists on disk (testable).</summary>
        public static string PickFirstExisting(IEnumerable<string> candidates)
        {
            foreach (string c in candidates)
            {
                if (string.IsNullOrEmpty(c)) continue;
                try { if (Directory.Exists(c)) return c; } catch { }
            }
            return null;
        }

        // ------------------------------------------------------------------

        private static void AddUnique(List<CloudFolder> list, string name, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            foreach (CloudFolder c in list)
                if (string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)) return;
            bool exists = false;
            try { exists = Directory.Exists(path); } catch { }
            list.Add(new CloudFolder { Name = name, Path = path, Exists = exists });
        }

        public static List<string> OneDriveCandidates()
        {
            List<string> list = new List<string>();

            // registry: per-user known folder (most reliable)
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\OneDrive\Accounts\Personal"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("UserFolder");
                        if (v is string) list.Add((string)v);
                    }
                }
            }
            catch { }

            // env vars (OneDrive, OneDriveConsumer, OneDriveCommercial)
            foreach (string varName in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                string env = SafeEnv(varName);
                if (!string.IsNullOrEmpty(env)) list.Add(env);
            }

            // classic fallbacks
            string profile = SafeEnv("USERPROFILE");
            if (!string.IsNullOrEmpty(profile))
            {
                list.Add(Path.Combine(profile, "OneDrive"));
                list.Add(Path.Combine(profile, "OneDrive - Personal"));
            }
            return list;
        }

        public static List<string> GoogleDriveCandidates()
        {
            List<string> list = new List<string>();
            string profile = SafeEnv("USERPROFILE");
            if (!string.IsNullOrEmpty(profile))
            {
                list.Add(Path.Combine(profile, "Google Drive"));
                list.Add(Path.Combine(profile, "GoogleDrive"));
                list.Add(Path.Combine(profile, "My Drive"));
                list.Add(Path.Combine(profile, "Google Drive Files"));
            }

            // registry for the newer "Google Drive for desktop" mirrored folder
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Google\Drive"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("MountedVolumePath");
                        if (v is string && !string.IsNullOrEmpty((string)v))
                            list.Add((string)v);
                    }
                }
            }
            catch { }

            return list;
        }

        public static List<string> DropboxCandidates()
        {
            List<string> list = new List<string>();
            string profile = SafeEnv("USERPROFILE");
            if (!string.IsNullOrEmpty(profile))
            {
                list.Add(Path.Combine(profile, "Dropbox"));
                list.Add(Path.Combine(profile, "Dropbox (Personal)"));
            }
            string info = Path.Combine(SafeEnv("APPDATA") ?? "", "Dropbox", "info.json");
            list.Add(DropboxFolderFromInfo(info));
            return list;
        }

        /// <summary>Pure helper: parse "path" from Dropbox info.json (testable).</summary>
        public static string DropboxFolderFromInfo(string infoJsonPath)
        {
            try
            {
                if (string.IsNullOrEmpty(infoJsonPath) || !File.Exists(infoJsonPath)) return null;
                string json = File.ReadAllText(infoJsonPath);
                string key = "\"path\":";
                int i = json.IndexOf(key, StringComparison.Ordinal);
                if (i < 0) return null;
                int q1 = json.IndexOf('"', i + key.Length);
                if (q1 < 0) return null;
                int q2 = json.IndexOf('"', q1 + 1);
                if (q2 < 0) return null;
                string raw = json.Substring(q1 + 1, q2 - q1 - 1);
                // info.json stores Windows paths with escaped backslashes
                return raw.Replace("\\\\", "\\");
            }
            catch { return null; }
        }

        private static string SafeEnv(string name)
        {
            try { return Environment.GetEnvironmentVariable(name); }
            catch { return null; }
        }
    }
}
