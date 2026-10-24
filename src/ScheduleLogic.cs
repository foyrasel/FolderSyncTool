using System;

namespace FolderSyncTool
{
    public static class ScheduleLogic
    {
        /// <summary>Should a backup run right now, given settings and last run?</summary>
        public static bool ShouldRunNow(AppSettings s, DateTime? lastRunUtc, DateTime nowLocal)
        {
            switch (s.Schedule)
            {
                case ScheduleType.OnLogoff:
                    return false; // triggered externally, not by timer

                case ScheduleType.Daily:
                    TimeSpan target;
                    if (!TryParseHm(s.DailyTime, out target)) return false;
                    DateTime todayTarget = nowLocal.Date + target;
                    if (nowLocal < todayTarget) return false;         // not due yet today
                    if (lastRunUtc != null)
                    {
                        DateTime lastLocal = lastRunUtc.Value.ToLocalTime();
                        if (lastLocal >= todayTarget) return false;   // already ran today's slot
                    }
                    if (!s.RunMissedOnStartup && lastRunUtc == null && nowLocal - todayTarget > TimeSpan.FromHours(12))
                        return false;                                 // stale slot, catch-up disabled
                    return true;

                case ScheduleType.Interval:
                    if (lastRunUtc == null) return true;
                    TimeSpan since = nowLocal.ToUniversalTime() - lastRunUtc.Value;
                    return since >= TimeSpan.FromMinutes(Math.Max(1, s.IntervalMinutes));

                default:
                    return false;
            }
        }

        /// <summary>Human-readable next run time (local).</summary>
        public static DateTime? NextRunLocal(AppSettings s, DateTime? lastRunUtc, DateTime nowLocal)
        {
            switch (s.Schedule)
            {
                case ScheduleType.OnLogoff:
                    return null;

                case ScheduleType.Daily:
                    {
                        TimeSpan target;
                        if (!TryParseHm(s.DailyTime, out target)) return null;
                        DateTime today = nowLocal.Date + target;
                        if (nowLocal < today) return today;
                        return today.AddDays(1);
                    }

                case ScheduleType.Interval:
                    {
                        DateTime baseUtc = lastRunUtc ?? nowLocal.ToUniversalTime();
                        DateTime next = baseUtc + TimeSpan.FromMinutes(Math.Max(1, s.IntervalMinutes));
                        DateTime nextLocal = next.ToLocalTime();
                        if (nextLocal < nowLocal) nextLocal = nowLocal;
                        return nextLocal;
                    }

                default:
                    return null;
            }
        }

        public static string Describe(AppSettings s)
        {
            switch (s.Schedule)
            {
                case ScheduleType.Daily:
                    return "Daily at " + s.DailyTime + (s.RunMissedOnStartup ? " (catch-up on)" : "");
                case ScheduleType.Interval:
                    return "Every " + s.IntervalMinutes + " min";
                case ScheduleType.OnLogoff:
                    return "On logoff";
                default:
                    return "";
            }
        }

        public static bool TryParseHm(string text, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split(':');
            int h, m;
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], out h) || !int.TryParse(parts[1], out m)) return false;
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            value = new TimeSpan(h, m, 0);
            return true;
        }
    }
}
