using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FolderSyncTool
{
    public enum TaskPriority { Low, Normal, High }

    /// <summary>A work item shared over the LAN with deadline + priority.</summary>
    public class WorkItem
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "";
        public string Notes = "";
        public DateTime? DeadlineUtc = null;
        public TaskPriority Priority = TaskPriority.Normal;
        public string Status = "open";       // open | done
        public string AssignedBy = "";       // machine name of sender
        public bool Confirmed = false;       // receiver acknowledged

        // v5: reminder bookkeeping (local to this machine)
        public bool Reminded = false;
        public bool RemindAgain = false;     // re-remind once when overdue

        public override string ToString()
        {
            string d = DeadlineUtc != null
                ? DeadlineUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "no deadline";
            return Title + " [" + Priority + ", " + d + ", " + Status + "]";
        }
    }

    /// <summary>Persists work items in tasks.ini and serializes them for LAN transfer.</summary>
    public static class TaskStore
    {
        private static readonly object _lock = new object();

        public static List<WorkItem> LoadAll(string path)
        {
            var list = new List<WorkItem>();
            lock (_lock)
            {
                if (!File.Exists(path)) return list;
                foreach (string line in File.ReadAllLines(path))
                {
                    WorkItem w = ParseLine(line);
                    if (w != null) list.Add(w);
                }
            }
            return list;
        }

        public static void SaveAll(string path, List<WorkItem> items)
        {
            lock (_lock)
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                StringBuilder sb = new StringBuilder();
                foreach (WorkItem w in items) sb.AppendLine(ToLine(w));
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
        }

        public static void AddOrUpdate(string path, WorkItem item)
        {
            List<WorkItem> list = LoadAll(path);
            WorkItem found = list.Find(x => x.Id == item.Id);
            if (found != null) list.Remove(found);
            list.Add(item);
            SaveAll(path, list);
        }

        public static void Remove(string path, string id)
        {
            List<WorkItem> list = LoadAll(path);
            list.RemoveAll(x => x.Id == id);
            SaveAll(path, list);
        }

        // ---- LAN wire format (single line, UTF-8) ------------------------
        // id|title|notes|deadlineUtc|priority|status|assignedBy|confirmed

        public static string ToWire(WorkItem w)
        {
            return string.Join("|",
                w.Id ?? "",
                (w.Title ?? "").Replace("|", "/"),
                (w.Notes ?? "").Replace("|", "/").Replace("\r", " ").Replace("\n", " "),
                w.DeadlineUtc != null ? w.DeadlineUtc.Value.ToString("o") : "",
                ((int)w.Priority).ToString(),
                w.Status ?? "open",
                w.AssignedBy ?? "",
                w.Confirmed ? "1" : "0");
        }

        public static WorkItem FromWire(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            string[] p = line.Split('|');
            if (p.Length < 4) return null;
            WorkItem w = new WorkItem();
            w.Id = p[0];
            w.Title = p[1];
            w.Notes = p[2];
            if (!string.IsNullOrEmpty(p[3]))
            {
                DateTime d;
                if (DateTime.TryParseExact(p[3], "o", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out d))
                    w.DeadlineUtc = d.ToUniversalTime();
            }
            int pri;
            if (int.TryParse(p[4], out pri) && pri >= 0 && pri <= 2)
                w.Priority = (TaskPriority)pri;
            if (p.Length > 5) w.Status = p[5];
            if (p.Length > 6) w.AssignedBy = p[6];
            if (p.Length > 7) w.Confirmed = p[7] == "1";
            return w;
        }

        // ---- ini line ----------------------------------------------------

        public static string ToLine(WorkItem w)
        {
            return string.Join("|",
                w.Id,
                w.Title,
                w.Notes,
                w.DeadlineUtc != null ? w.DeadlineUtc.Value.ToString("o") : "",
                ((int)w.Priority).ToString(),
                w.Status,
                w.AssignedBy,
                w.Confirmed ? "1" : "0",
                w.Reminded ? "1" : "0",
                w.RemindAgain ? "1" : "0");
        }

        public static WorkItem ParseLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            string[] p = line.Split('|');
            if (p.Length < 8) return null;
            WorkItem w = new WorkItem();
            w.Id = p[0];
            w.Title = p[1];
            w.Notes = p[2];
            if (!string.IsNullOrEmpty(p[3]))
            {
                DateTime d;
                if (DateTime.TryParseExact(p[3], "o", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out d))
                    w.DeadlineUtc = d.ToUniversalTime();
            }
            int pri;
            if (int.TryParse(p[4], out pri) && pri >= 0 && pri <= 2)
                w.Priority = (TaskPriority)pri;
            w.Status = p[5];
            w.AssignedBy = p[6];
            w.Confirmed = p[7] == "1";
            if (p.Length > 8) w.Reminded = p[8] == "1";
            if (p.Length > 9) w.RemindAgain = p[9] == "1";
            return w;
        }

        // ---- reminder logic (pure, testable) ------------------------------

        /// <summary>
        /// Items needing a reminder right now: deadline within window and not yet
        /// reminded, or overdue once more (RemindAgain) after slipping past deadline.
        /// </summary>
        public static List<WorkItem> DueForReminder(List<WorkItem> items, DateTime nowUtc,
                                                    TimeSpan window)
        {
            var due = new List<WorkItem>();
            foreach (WorkItem w in items)
            {
                if (w.Status == "done" || w.DeadlineUtc == null) continue;
                TimeSpan left = w.DeadlineUtc.Value - nowUtc;

                if (left > TimeSpan.Zero)
                {
                    // upcoming: warn once inside the window
                    if (!w.Reminded && left <= window)
                    {
                        w.Reminded = true;
                        due.Add(w);
                    }
                }
                else if (!w.Reminded)
                {
                    // first time we ever see it, already overdue: one ping,
                    // and suppress the later "overdue" re-ping (user never
                    // got the "due soon" warning in the first place)
                    w.Reminded = true;
                    w.RemindAgain = true;
                    due.Add(w);
                }
                else if (!w.RemindAgain)
                {
                    // warned earlier while upcoming, now slipped past — one final ping
                    w.RemindAgain = true;
                    due.Add(w);
                }
            }
            return due;
        }
    }
}
