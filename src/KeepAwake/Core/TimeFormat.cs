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

        /// <summary>
        /// Time left for display: rounds up to the next whole minute so a fresh 8 h session reads "8 h 0 min",
        /// and shows "under a minute" for the last 60 s.
        /// </summary>
        public static string Remaining(TimeSpan remaining)
        {
            if (remaining < TimeSpan.FromMinutes(1)) return "under a minute";
            return Compact(TimeSpan.FromMinutes(Math.Ceiling(remaining.TotalMinutes - 1e-9)));
        }

        /// <summary>Whole-unit duration without zero trailing units: "8 h", "1 h 30 min", "2 d", "45 min".</summary>
        public static string Duration(TimeSpan span)
        {
            var total = (long)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero);
            if (total <= 0) return "under a minute";
            long days = total / 1440, hours = total % 1440 / 60, mins = total % 60;
            var parts = new List<string>(3);
            if (days > 0) parts.Add(days + " d");
            if (hours > 0) parts.Add(hours + " h");
            if (mins > 0) parts.Add(mins + " min");
            return string.Join(" ", parts);
        }

        /// <summary>"Thu 12:20" up to 6 days ago, else "30 Oct 12:20".</summary>
        public static string Since(DateTime localTime, DateTime localNow) => Since(localTime, localNow, CultureInfo.CurrentCulture);

        public static string Since(DateTime localTime, DateTime localNow, CultureInfo culture)
        {
            var age = localNow - localTime;
            return age.TotalDays <= 6
                ? localTime.ToString("ddd HH:mm", culture)
                : localTime.ToString("d MMM HH:mm", culture);
        }

        public static string Clock(DateTime localTime) =>
            localTime.ToString("HH:mm", CultureInfo.CurrentCulture);
    }
}
