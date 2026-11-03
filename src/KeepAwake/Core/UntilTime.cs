using System;
using System.Globalization;

namespace KeepAwake.Core
{
    /// <summary>The "until a time" choice: parsing HH:mm text and resolving it to the next matching local instant.</summary>
    public static class UntilTime
    {
        /// <summary>Value stored in lastDurationMinutes while an until-time is the remembered choice.</summary>
        public const int UntilMinutes = -1;

        /// <summary>Accepts H:mm or HH:mm on a 24 h clock (00:00 to 23:59). Anything else is rejected.</summary>
        public static bool TryParse(string text, out TimeSpan timeOfDay)
        {
            timeOfDay = TimeSpan.Zero;
            if (text == null) return false;
            var t = text.Trim();
            int colon = t.IndexOf(':');
            if (colon < 1 || colon > 2 || t.Length - colon - 1 != 2) return false;
            var hh = t.Substring(0, colon);
            var mm = t.Substring(colon + 1);
            if (!AllDigits(hh) || !AllDigits(mm)) return false;
            int h = int.Parse(hh, CultureInfo.InvariantCulture), m = int.Parse(mm, CultureInfo.InvariantCulture);
            if (h > 23 || m > 59) return false;
            timeOfDay = new TimeSpan(h, m, 0);
            return true;
        }

        public static string Format(TimeSpan timeOfDay) =>
            new DateTime(2000, 1, 1).Add(timeOfDay).ToString("HH:mm", CultureInfo.InvariantCulture);

        /// <summary>Today at the given time, or tomorrow when that time is not after <paramref name="localNow"/>.</summary>
        public static DateTime Resolve(DateTime localNow, TimeSpan timeOfDay)
        {
            var target = localNow.Date + timeOfDay;
            return target <= localNow ? localNow.Date.AddDays(1) + timeOfDay : target;
        }

        /// <summary>
        /// The next UTC instant at which the clock in <paramref name="tz"/> reads <paramref name="timeOfDay"/>, strictly after
        /// <paramref name="nowUtc"/>. A time inside a spring-forward gap resolves to the first valid instant after the gap;
        /// an ambiguous fall-back time resolves to standard time.
        /// </summary>
        public static DateTime ResolveUtc(DateTime nowUtc, TimeSpan timeOfDay, TimeZoneInfo tz)
        {
            nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
            for (int day = 0; day < 3; day++)
            {
                var local = DateTime.SpecifyKind(localNow.Date.AddDays(day) + timeOfDay, DateTimeKind.Unspecified);
                while (tz.IsInvalidTime(local)) local = local.AddMinutes(15);
                var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
                if (utc > nowUtc) return utc;
            }
            throw new InvalidOperationException("no matching instant within three days");
        }

        /// <summary>Now plus one hour, rounded up to the next quarter hour.</summary>
        public static TimeSpan Suggest(DateTime localNow)
        {
            var t = localNow.AddHours(1);
            long quarter = TimeSpan.FromMinutes(15).Ticks;
            long rounded = (t.TimeOfDay.Ticks + quarter - 1) / quarter * quarter;
            return TimeSpan.FromTicks(rounded % TimeSpan.TicksPerDay);
        }

        private static bool AllDigits(string s)
        {
            if (s.Length == 0) return false;
            foreach (var c in s) if (c < '0' || c > '9') return false;
            return true;
        }
    }
}
