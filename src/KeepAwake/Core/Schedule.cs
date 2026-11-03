using System;

namespace KeepAwake.Core
{
    /// <summary>Pure scheduling rules for the UI core timer, so they can be tested without a window.</summary>
    public static class Schedule
    {
        public const int TrayRefreshMs = 60000;
        public const int MinWakeMs = 50;
        public static readonly TimeSpan WarnBefore = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan MinPeriodForWarning = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Milliseconds until the core timer next needs to run, or -1 when nothing needs a wake
        /// (inactive or forever: nothing can expire and the tooltip never changes).
        /// <paramref name="warnUtc"/> is the pending expiry-warning instant, if any: one more candidate wake.
        /// </summary>
        public static int NextWakeMs(bool active, DateTime nowUtc, DateTime? endUtc, double msSinceTrayUpdate, DateTime? warnUtc = null)
        {
            if (!active || endUtc == null) return -1;
            double toEnd = (endUtc.Value - nowUtc).TotalMilliseconds;
            double toTray = TrayRefreshMs - msSinceTrayUpdate;
            double next = Math.Min(toEnd, toTray);
            if (warnUtc.HasValue) next = Math.Min(next, (warnUtc.Value - nowUtc).TotalMilliseconds);
            if (next < MinWakeMs) return MinWakeMs;
            if (next > TrayRefreshMs) return TrayRefreshMs;
            return (int)Math.Ceiling(next);
        }
    }
}
