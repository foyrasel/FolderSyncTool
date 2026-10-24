using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;

namespace FolderSyncTool
{
    /// <summary>Result of one pair run.</summary>
    public class PairRunResult
    {
        public int Copied;
        public int Deleted;     // mirror deletes
        public int Versioned;   // old versions saved
        public int Failed;
        public int Skipped;
        public bool Cancelled;
        public List<string> Messages = new List<string>();
    }

    /// <summary>
    /// Incremental backup engine (v5): cancellation support, per-pair file type
    /// filters, versioning of overwritten files, optional VSS shadow copy.
    /// </summary>
    public static class BackupEngine
    {
        public static bool IsChanged(FileInfo src, FileInfo dst)
        {
            if (!dst.Exists) return true;
            if (src.Length != dst.Length) return true;
            double delta = Math.Abs((src.LastWriteTimeUtc - dst.LastWriteTimeUtc).TotalSeconds);
            return delta > 1.0;
        }

        public static bool MatchesFilter(string fileName, HashSet<string> exts)
        {
            if (exts == null || exts.Count == 0) return true; // no filter = all files
            string ext = Path.GetExtension(fileName);
            if (ext.Length == 0) return false;
            return exts.Contains(ext.ToLowerInvariant());
        }

        /// <summary>Parse "docx, xlsx|.pdf" or ".docx,.xlsx" into a lowercase ".ext" set.</summary>
        public static HashSet<string> ParseTypes(string types)
        {
            HashSet<string> set = new HashSet<string>();
            if (string.IsNullOrWhiteSpace(types)) return set;
            foreach (string raw in types.Split(new[] { ',', ';' }))
            {
                string t = raw.Trim().ToLowerInvariant();
                if (t.Length == 0) continue;
                if (!t.StartsWith(".")) t = "." + t;
                set.Add(t);
            }
            return set;
        }

        /// <summary>Effective filter for a pair: per-pair types, else global.</summary>
        public static HashSet<string> FilterForPair(AppSettings s, PairInfo p)
        {
            string types = string.IsNullOrWhiteSpace(p.Types) ? s.GlobalTypes : p.Types;
            return ParseTypes(types);
        }

        // ------------------------------------------------------------------

        public static PairRunResult RunPair(AppSettings s, PairInfo p, Action<string> log,
                                             CancellationToken ct)
        {
            return RunPair(s, p, log, ct, null);
        }

        /// <param name="shadowMap">Optional test hook: maps source path -> readable copy path.</param>
        public static PairRunResult RunPair(AppSettings s, PairInfo p, Action<string> log,
                                             CancellationToken ct,
                                             Func<string, string> shadowMap)
        {
            PairRunResult r = new PairRunResult();
            if (log == null) log = delegate { };

            if (string.IsNullOrEmpty(p.Source) || string.IsNullOrEmpty(p.Dest))
            {
                r.Messages.Add("Pair skipped: missing source or destination.");
                return r;
            }
            if (!Directory.Exists(p.Source))
            {
                r.Messages.Add("Source missing: " + p.Source);
                r.Failed++;
                return r;
            }
            try { Directory.CreateDirectory(p.Dest); }
            catch (Exception ex)
            {
                r.Messages.Add("Cannot create destination: " + ex.Message);
                r.Failed++;
                return r;
            }

            bool useShadow = p.UseShadowCopy && IsAdmin();
            if (p.UseShadowCopy && !IsAdmin())
                log("Shadow copy requested but process is not elevated; falling back to direct read.");

            HashSet<string> exts = FilterForPair(s, p);
            bool mirror = s.MirrorDeletes;

            // index destination for mirror-delete + versioning
            Dictionary<string, string> destFiles = IndexDest(p.Dest);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var files = SafeEnumerate(p.Source);
            foreach (string srcPath in files)
            {
                if (ct.IsCancellationRequested) { r.Cancelled = true; break; }

                string name = Path.GetFileName(srcPath);
                if (IsJunk(name)) { r.Skipped++; continue; }
                if (!MatchesFilter(name, exts)) { r.Skipped++; continue; }

                string rel = srcPath.Substring(p.Source.Length).TrimStart('\\', '/');
                string dstPath = Path.Combine(p.Dest, rel);
                seen.Add(rel.ToLowerInvariant());

                string readFrom = srcPath;
                bool viaShadow = false;
                if (useShadow && shadowMap != null)
                {
                    try
                    {
                        string mapped = shadowMap(srcPath);
                        if (!string.IsNullOrEmpty(mapped)) { readFrom = mapped; viaShadow = true; }
                    }
                    catch { /* fall back to direct read */ }
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dstPath));

                    FileInfo src = new FileInfo(readFrom);
                    FileInfo dst = new FileInfo(dstPath);

                    if (dst.Exists && !IsChanged(src, dst))
                    {
                        if (viaShadow) VssHelper.TryCleanup(readFrom);
                        continue; // unchanged
                    }

                    // v5: versioning — keep a stamped copy before overwrite
                    if (dst.Exists && p.KeepVersions)
                    {
                        SaveVersion(dstPath, s.VersionCount);
                        r.Versioned++;
                    }

                    string tmp = dstPath + ".fst_tmp";
                    if (File.Exists(tmp)) File.Delete(tmp);

                    int attempt = 0;
                    while (true)
                    {
                        try
                        {
                            if (viaShadow)
                            {
                                // shadow files may not be directly openable as streams by
                                // some readers — copy through a local temp first
                                string localTmp = tmp + ".vss";
                                File.Copy(readFrom, localTmp, true);
                                if (File.Exists(tmp)) File.Delete(tmp);
                                File.Move(localTmp, tmp);
                                VssHelper.TryCleanup(readFrom);
                            }
                            else
                            {
                                File.Copy(srcPath, tmp, true);
                            }
                            break;
                        }
                        catch (IOException) when (attempt < 2)
                        {
                            attempt++;
                            Thread.Sleep(300);
                        }
                    }

                    if (ct.IsCancellationRequested)
                    {
                        if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { } }
                        r.Cancelled = true;
                        break;
                    }
                    if (File.Exists(dstPath)) File.Delete(dstPath);
                    File.Move(tmp, dstPath);
                    r.Copied++;
                    log("Copied: " + rel);
                }
                catch (Exception ex)
                {
                    r.Failed++;
                    log("FAILED: " + rel + " — " + ex.Message);
                }
            }

            // mirror deletes
            if (mirror && !r.Cancelled)
            {
                foreach (var kv in destFiles)
                {
                    if (ct.IsCancellationRequested) { r.Cancelled = true; break; }
                    if (seen.Contains(kv.Key)) continue;
                    if (kv.Key.StartsWith("_versions", StringComparison.OrdinalIgnoreCase)) continue;
                    // v5: only mirror-delete files the current filter actually manages
                    if (!MatchesFilter(Path.GetFileName(kv.Value), exts)) continue;
                    try
                    {
                        File.Delete(kv.Value);
                        r.Deleted++;
                        log("Deleted (mirror): " + kv.Key);
                    }
                    catch (Exception ex)
                    {
                        log("FAILED delete: " + kv.Key + " — " + ex.Message);
                    }
                }
            }

            return r;
        }

        // ------------------------------------------------------------------

        /// <summary>Save a timestamped copy of dstPath under <dest>\_versions, prune old ones.</summary>
        public static string SaveVersion(string filePath, int keep)
        {
            string dir = Path.GetDirectoryName(filePath);
            string name = Path.GetFileNameWithoutExtension(filePath);
            string ext = Path.GetExtension(filePath);
            string vdir = Path.Combine(dir, "_versions");
            Directory.CreateDirectory(vdir);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string target = Path.Combine(vdir, name + "__" + stamp + ext);
            int n = 1;
            while (File.Exists(target))
            {
                target = Path.Combine(vdir, name + "__" + stamp + "_" + n + ext);
                n++;
            }
            File.Copy(filePath, target);

            PruneVersions(vdir, name, ext, Math.Max(1, keep));
            return target;
        }

        public static void PruneVersions(string vdir, string baseName, string ext, int keep)
        {
            try
            {
                string prefix = baseName + "__";
                var old = Directory.GetFiles(vdir, prefix + "*" + ext)
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .Skip(Math.Max(1, keep))
                    .ToList();
                foreach (string f in old)
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Map a normal path to its shadow-copy counterpart, or null. Pure string
        /// logic with manual drive-letter parsing (Path.GetPathRoot is a no-op on
        /// Unix, which would make this untestable and brittle).
        /// </summary>
        public static string MapToShadowPath(string sourcePath, string shadowDeviceRoot)
        {
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(shadowDeviceRoot))
                return null;
            string p = sourcePath.Replace('/', '\\');
            if (p.Length < 3 || p[1] != ':') return null;   // need "X:\..."
            string rest = p.Substring(2).TrimStart('\\');  // "Users\x\a.txt"
            if (rest.Length == 0) return null;
            if (!shadowDeviceRoot.EndsWith("\\", StringComparison.Ordinal)) shadowDeviceRoot += "\\";
            return shadowDeviceRoot + rest;
        }

        public static bool IsAdmin()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal pr = new WindowsPrincipal(id);
                    return pr.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        // internals --------------------------------------------------------

        private static bool IsJunk(string name)
        {
            // hidden files are already excluded in SafeFiles(); office lock files start with ~$
            return name.StartsWith("~$") || (name.StartsWith(".") && name != "..");
        }

        private static IEnumerable<string> SafeEnumerate(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                foreach (string f in SafeFiles(dir)) yield return f;
                foreach (string d in SafeDirs(dir)) stack.Push(d);
            }
        }

        private static IEnumerable<string> SafeFiles(string dir)
        {
            try
            {
                return Directory.GetFiles(dir).Where(f =>
                {
                    var attr = new FileInfo(f).Attributes;
                    return (attr & FileAttributes.Hidden) == 0
                        && (attr & FileAttributes.ReparsePoint) == 0;
                });
            }
            catch { return new string[0]; }
        }

        private static IEnumerable<string> SafeDirs(string dir)
        {
            try
            {
                return Directory.GetDirectories(dir).Where(d =>
                {
                    var attr = new DirectoryInfo(d).Attributes;
                    return (attr & FileAttributes.ReparsePoint) == 0
                        && (attr & FileAttributes.Hidden) == 0;
                });
            }
            catch { return new string[0]; }
        }

        private static Dictionary<string, string> IndexDest(string dest)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(dest)) return map;
            var stack = new Stack<string>();
            stack.Push(dest);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string[] files = new string[0];
                string[] dirs = new string[0];
                try { files = Directory.GetFiles(dir); } catch { }
                try { dirs = Directory.GetDirectories(dir); } catch { }
                foreach (string f in files)
                {
                    string rel = f.Substring(dest.Length).TrimStart('\\', '/');
                    map[rel] = f;
                }
                foreach (string d in dirs)
                {
                    var attr = new DirectoryInfo(d).Attributes;
                    if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                    stack.Push(d);
                }
            }
            return map;
        }
    }
}
