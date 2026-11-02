using System;
using System.Collections.Generic;
using System.Globalization;

namespace KeepAwake.Core
{
    public static class TimeFormat
    {
        /// <summary>"3 d 4 h 12 min", "2 h 37 min", "45 min", "under a minute".</summary>
        public static string Compact(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            int days = span.Days, hours = span.Hours, mins = span.Minutes;
            if (days == 0 && hours == 0 && mins == 0) return "under a minute";
            var parts = new List<string>(3);
            if (days > 0) parts.Add(days + " d");
            if (days > 0 || hours > 0) parts.Add(hours + " h");
            parts.Add(mins + " min");
            return string.Join(" ", parts);
        }

        /// <summary>Hours and minutes for the ring centre: ("1", "23") or ("0", "05").</summary>
        public static void RingParts(TimeSpan span, out string hours, out string minutes)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            hours = ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture);
            minutes = span.Minutes.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>"Thu 12:20" within the last 6 days, else "30 Oct 12:20".</summary>
        public static string Since(DateTime localTime, DateTime localNow)
        {
            var age = localNow - localTime;
            return age.TotalDays < 6
                ? localTime.ToString("ddd HH:mm", CultureInfo.CurrentCulture)
                : localTime.ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
        }

        public static string Clock(DateTime localTime) =>
            localTime.ToString("HH:mm", CultureInfo.CurrentCulture);
    }
}
