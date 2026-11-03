using System;

namespace KeepAwake.Core
{
    public enum AwakeState { Inactive, Active }

    public sealed class ExpiredEventArgs : EventArgs
    {
        public ExpiredEventArgs(TimeSpan keptAwake) { KeptAwake = keptAwake; }
        public TimeSpan KeptAwake { get; }
    }

    /// <summary>
    /// The keep-awake state machine. Inactive, or Active until <see cref="EndUtc"/> (null = forever).
    /// Thread-affine: call from one thread (the UI thread) and drive <see cref="Tick"/> from a timer.
    /// </summary>
    public sealed class AwakeController : IDisposable
    {
        private readonly IClock _clock;
        private readonly IPowerHold _hold;
        private bool _warned;

        public AwakeController(IClock clock, IPowerHold hold)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _hold = hold ?? throw new ArgumentNullException(nameof(hold));
        }

        public AwakeState State { get; private set; } = AwakeState.Inactive;
        public DateTime StartedUtc { get; private set; }
        public DateTime? EndUtc { get; private set; }
        public TimeSpan? Duration { get; private set; }

        /// <summary>Changes on every Activate, Extend and Deactivate, so a captured value identifies one period.</summary>
        public int PeriodId { get; private set; }

        /// <summary>The controller's notion of now (monotonic in the real app).</summary>
        public DateTime UtcNow => _clock.UtcNow;

        public bool IsActive => State == AwakeState.Active;
        public bool IsForever => IsActive && EndUtc == null;

        /// <summary>True while active and Windows refused the execution-state request.</summary>
        public bool HoldFailed => IsActive && !_hold.LastHoldSucceeded;

        public TimeSpan Elapsed => IsActive ? _clock.UtcNow - StartedUtc : TimeSpan.Zero;

        public TimeSpan? Remaining =>
            IsActive && EndUtc.HasValue ? (TimeSpan?)Max(EndUtc.Value - _clock.UtcNow, TimeSpan.Zero) : null;

        /// <summary>0..1 of the period used; 1 when forever (ring fully lit).</summary>
        public double Progress
        {
            get
            {
                if (!IsActive) return 0;
                if (!Duration.HasValue || Duration.Value <= TimeSpan.Zero) return 1;
                var p = Elapsed.TotalSeconds / Duration.Value.TotalSeconds;
                return p < 0 ? 0 : p > 1 ? 1 : p;
            }
        }

        /// <summary>
        /// When the "ends in 5 minutes" warning is due, or null when none is pending (inactive, forever, the period
        /// is shorter than 10 minutes, or the warning already fired for this period).
        /// </summary>
        public DateTime? WarningUtc =>
            IsActive && EndUtc.HasValue && !_warned && Duration.HasValue && Duration.Value >= Schedule.MinPeriodForWarning
                ? EndUtc.Value - Schedule.WarnBefore
                : (DateTime?)null;

        public event EventHandler StateChanged;
        public event EventHandler ExpiryWarning;
        public event EventHandler<ExpiredEventArgs> Expired;

        public void Activate(TimeSpan? duration)
        {
            if (duration.HasValue && duration.Value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(duration));
            var now = _clock.UtcNow;
            StartedUtc = now;
            Duration = duration;
            EndUtc = duration.HasValue ? now + duration.Value : (DateTime?)null;
            State = AwakeState.Active;
            _warned = false;
            PeriodId++;
            _hold.Hold();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Adds time to the remaining period. Null makes it forever. Forever stays forever.</summary>
        public void Extend(TimeSpan? span)
        {
            if (!IsActive) { Activate(span); return; }
            if (EndUtc == null) return;
            PeriodId++;
            if (span == null)
            {
                EndUtc = null;
                Duration = null;
            }
            else
            {
                if (span.Value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(span));
                EndUtc = EndUtc.Value + span.Value;
                Duration = EndUtc.Value - StartedUtc;
                _warned = false;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Deactivate()
        {
            if (!IsActive) return;
            _hold.Release();
            State = AwakeState.Inactive;
            PeriodId++;
            EndUtc = null;
            Duration = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Call about once a second. Fires <see cref="Expired"/> and deactivates when the period is over.</summary>
        public void Tick()
        {
            if (!IsActive || EndUtc == null) return;
            var now = _clock.UtcNow;
            if (now < EndUtc.Value)
            {
                var warn = WarningUtc;
                if (warn.HasValue && now >= warn.Value)
                {
                    _warned = true;
                    ExpiryWarning?.Invoke(this, EventArgs.Empty);
                }
                return;
            }
            var kept = EndUtc.Value - StartedUtc;
            Deactivate();
            Expired?.Invoke(this, new ExpiredEventArgs(kept));
        }

        /// <summary>Tray tooltip, always under 64 characters.</summary>
        public string TooltipText()
        {
            if (!IsActive) return "KeepAwake - Inactive";
            if (EndUtc == null) return "KeepAwake - Active - forever";
            return "KeepAwake - Active - " + TimeFormat.Remaining(Remaining.Value) + " left";
        }

        private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

        public void Dispose()
        {
            Deactivate();
            _hold.Dispose();
        }
    }
}
