using System;
using System.Management;

namespace FolderSyncTool
{
    /// <summary>
    /// v5: Volume Shadow Copy via WMI (Win32_ShadowCopy.Create). Lets us read
    /// files that are exclusively locked (Outlook PST, SQL db files, ...).
    /// Requires elevation; gracefully reports failure otherwise.
    /// </summary>
    public static class VssHelper
    {
        public static string LastError = "";

        /// <summary>
        /// Create a shadow copy of the volume holding <paramref name="anyPathOnVolume"/>
        /// and return its device root (e.g. \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy3).
        /// Returns null when unsupported/unavailable.
        /// </summary>
        public static string CreateShadow(string anyPathOnVolume)
        {
            LastError = "";
            if (!BackupEngine.IsAdmin())
            {
                LastError = "Shadow copy needs an elevated process.";
                return null;
            }
            try
            {
                using (ManagementClass cls = new ManagementClass("Win32_ShadowCopy"))
                {
                    using (ManagementBaseObject inParams =
                        cls.GetMethodParameters("Create"))
                    {
                        inParams["Context"] = "ClientAccessible";
                        inParams["Volume"] = VolumeFromPath(anyPathOnVolume);
                        using (ManagementBaseObject outParams = cls.InvokeMethod("Create", inParams, null))
                        {
                            if (outParams == null) { LastError = "WMI returned nothing"; return null; }
                            uint rc = Convert.ToUInt32(outParams["ReturnValue"]);
                            if (rc != 0) { LastError = "Win32_ShadowCopy.Create rc=" + rc; return null; }
                            string id = Convert.ToString(outParams["ShadowID"]);
                            return QueryDeviceById(id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }

        private static string VolumeFromPath(string path)
        {
            string root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path));
            // WMI wants "C:\"
            return root.EndsWith("\\") ? root : root + "\\";
        }

        private static string QueryDeviceById(string shadowId)
        {
            using (var searcher = new ManagementObjectSearcher(
                "SELECT DeviceObject FROM Win32_ShadowCopy WHERE ID='" + shadowId + "'"))
            {
                foreach (ManagementObject o in searcher.Get())
                {
                    using (o)
                    {
                        object dev = o["DeviceObject"];
                        return dev is string ? (string)dev : null;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Fetch a readable local copy of <paramref name="sourcePath"/> through the
        /// newest shadow copy of its volume. Returns a temp file path, or null.
        /// </summary>
        public static string CopyViaShadow(string sourcePath)
        {
            string device = CreateShadow(sourcePath);
            if (device == null) return null;

            string mapped = BackupEngine.MapToShadowPath(sourcePath, device);
            if (mapped == null) return null;

            string tmp = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "fst_vss_" + Guid.NewGuid().ToString("N") +
                System.IO.Path.GetExtension(sourcePath));
            System.IO.File.Copy(mapped, tmp, true);
            return tmp;
        }

        public static void TryCleanup(string tempPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(tempPath) &&
                    tempPath.IndexOf("fst_vss_", StringComparison.Ordinal) >= 0)
                    System.IO.File.Delete(tempPath);
            }
            catch { }
        }
    }
}
