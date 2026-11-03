using System;
using System.Runtime.InteropServices;
using System.Threading;
using KeepAwake.Native;

namespace KeepAwake.Core
{
    public interface IInputNudger
    {
        void Nudge();
    }

    /// <summary>Moves the mouse 1 px right then 1 px left so idle detection (Teams, Slack, screensaver) resets.</summary>
    public sealed class MouseNudger : IInputNudger
    {
        /// <summary>Result of the last SendInput call (number of events injected), for diagnostics.</summary>
        public uint LastResult { get; private set; }

        /// <summary>The user touched the keyboard or mouse within this window, so a nudge would only be visible jitter.</summary>
        public const uint UserActiveWindowMs = 50000;

        public static bool ShouldNudge(uint tickCount, uint lastInputTick) =>
            unchecked(tickCount - lastInputTick) >= UserActiveWindowMs;

        public void Nudge()
        {
            var lii = new NativeMethods.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.LASTINPUTINFO)) };
            if (NativeMethods.GetLastInputInfo(ref lii) && !ShouldNudge(unchecked((uint)Environment.TickCount), lii.dwTime)) return;
            var inputs = new NativeMethods.INPUT[2];
            inputs[0].type = NativeMethods.INPUT_MOUSE;
            inputs[0].u.mi.dx = 1;
            inputs[0].u.mi.dwFlags = NativeMethods.MOUSEEVENTF_MOVE;
            inputs[1].type = NativeMethods.INPUT_MOUSE;
            inputs[1].u.mi.dx = -1;
            inputs[1].u.mi.dwFlags = NativeMethods.MOUSEEVENTF_MOVE;
            LastResult = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }
    }

    /// <summary>Runs the nudger on an interval while started.</summary>
    public sealed class ActivitySimulator : IDisposable
    {
        private readonly IInputNudger _nudger;
        private readonly TimeSpan _interval;
        private IDisposable _timer;
        private readonly Func<TimerCallback, TimeSpan, IDisposable> _timerFactory;

        public ActivitySimulator(IInputNudger nudger, TimeSpan interval)
            : this(nudger, interval, null)
        {
        }

        /// <summary>Lets tests drive the callbacks by hand instead of waiting on a real timer.</summary>
        internal ActivitySimulator(IInputNudger nudger, TimeSpan interval, Func<TimerCallback, TimeSpan, IDisposable> timerFactory)
        {
            _timerFactory = timerFactory ?? ((cb, every) => new Timer(cb, null, every, every));
            _nudger = nudger ?? throw new ArgumentNullException(nameof(nudger));
            _interval = interval;
        }

        public bool IsRunning => _timer != null;

        public void Start()
        {
            if (_timer != null) return;
            _timer = _timerFactory(_ => Safe(_nudger.Nudge), _interval);
        }

        public void Stop()
        {
            var t = _timer;
            _timer = null;
            t?.Dispose();
        }

        private static void Safe(Action a)
        {
            try { a(); }
            catch { /* input injection can be refused by policy; never crash the app */ }
        }

        public void Dispose() => Stop();
    }
}
