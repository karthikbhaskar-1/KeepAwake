using System;
using KeepAwake.Native;

namespace KeepAwake.Core
{
    public static class Uptime
    {
        public static TimeSpan Current => TimeSpan.FromMilliseconds(NativeMethods.GetTickCount64());

        public static DateTime BootTimeLocal(IClock clock) => clock.UtcNow.ToLocalTime() - Current;
    }
}
