using System;
using System.Threading;
using KeepAwake.Native;

namespace KeepAwake.Core
{
    /// <summary>Holds the system and display awake while <see cref="Hold"/> is in effect.</summary>
    public interface IPowerHold : IDisposable
    {
        bool IsHeld { get; }
        void Hold();
        void Release();
    }

    /// <summary>
    /// Asserts ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED from a dedicated thread,
    /// so the hold does not depend on the UI thread staying responsive.
    /// </summary>
    public sealed class ThreadPowerHold : IPowerHold
    {
        private readonly object _gate = new object();
        private ManualResetEventSlim _release;
        private Thread _thread;

        /// <summary>Execution state Windows reported for the hold thread just before the last release (diagnostics and tests).</summary>
        public uint? StateBeforeRelease { get; private set; }

        public bool IsHeld
        {
            get { lock (_gate) return _thread != null; }
        }

        public void Hold()
        {
            lock (_gate)
            {
                if (_thread != null) return;
                var release = new ManualResetEventSlim(false);
                var ready = new ManualResetEventSlim(false);
                var t = new Thread(() =>
                {
                    NativeMethods.SetThreadExecutionState(
                        NativeMethods.ExecutionState.Continuous |
                        NativeMethods.ExecutionState.SystemRequired |
                        NativeMethods.ExecutionState.DisplayRequired);
                    ready.Set();
                    release.Wait();
                    StateBeforeRelease = (uint)NativeMethods.SetThreadExecutionState(NativeMethods.ExecutionState.Continuous);
                })
                {
                    IsBackground = true,
                    Name = "KeepAwake.PowerHold",
                };
                t.Start();
                ready.Wait(2000);
                ready.Dispose();
                _release = release;
                _thread = t;
            }
        }

        public void Release()
        {
            Thread t;
            ManualResetEventSlim release;
            lock (_gate)
            {
                t = _thread;
                release = _release;
                _thread = null;
                _release = null;
            }
            if (t == null) return;
            release.Set();
            t.Join(2000);
            release.Dispose();
        }

        public void Dispose() => Release();
    }
}
