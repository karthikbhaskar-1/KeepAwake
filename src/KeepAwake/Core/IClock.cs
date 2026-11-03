using System;
using KeepAwake.Native;

namespace KeepAwake.Core
{
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    /// <summary>
    /// Wall-clock time at start-up plus the monotonic tick count since then, so a manual clock change or an
    /// NTP correction cannot shorten or stretch a running session. The tick count keeps running through sleep.
    /// </summary>
    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new SystemClock();

        private readonly DateTime _origin = DateTime.UtcNow;
        private readonly ulong _originTicks = NativeMethods.GetTickCount64();

        public DateTime UtcNow => _origin.AddMilliseconds(NativeMethods.GetTickCount64() - _originTicks);
    }
}
