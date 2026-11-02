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
        public void Nudge()
        {
            var inputs = new NativeMethods.INPUT[2];
            inputs[0].type = NativeMethods.INPUT_MOUSE;
            inputs[0].u.mi.dx = 1;
            inputs[0].u.mi.dwFlags = NativeMethods.MOUSEEVENTF_MOVE;
            inputs[1].type = NativeMethods.INPUT_MOUSE;
            inputs[1].u.mi.dx = -1;
            inputs[1].u.mi.dwFlags = NativeMethods.MOUSEEVENTF_MOVE;
            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }
    }

    /// <summary>Runs the nudger on an interval while started.</summary>
    public sealed class ActivitySimulator : IDisposable
    {
        private readonly IInputNudger _nudger;
        private readonly TimeSpan _interval;
        private Timer _timer;

        public ActivitySimulator(IInputNudger nudger, TimeSpan interval)
        {
            _nudger = nudger ?? throw new ArgumentNullException(nameof(nudger));
            _interval = interval;
        }

        public bool IsRunning => _timer != null;

        public void Start()
        {
            if (_timer != null) return;
            _timer = new Timer(_ => Safe(_nudger.Nudge), null, _interval, _interval);
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
