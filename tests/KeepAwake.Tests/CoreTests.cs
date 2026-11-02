using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using KeepAwake.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeepAwake.Tests
{
    /// <summary>IClock fake with a settable UtcNow.</summary>
    public sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; }
    }

    /// <summary>IPowerHold fake that counts Hold/Release calls.</summary>
    public sealed class FakePowerHold : IPowerHold
    {
        public int HoldCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public bool IsDisposed { get; private set; }

        public bool IsHeld
        {
            get { return HoldCalls > ReleaseCalls; }
        }

        public void Hold()
        {
            HoldCalls++;
        }

        public void Release()
        {
            ReleaseCalls++;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    /// <summary>IInputNudger fake with a thread-safe Nudge counter.</summary>
    public sealed class FakeNudger : IInputNudger
    {
        private int _count;

        public int Count
        {
            get { return Volatile.Read(ref _count); }
        }

        public void Nudge()
        {
            Interlocked.Increment(ref _count);
        }
    }

    [TestClass]
    public class AwakeControllerTests
    {
        private static readonly DateTime BaseUtc = new DateTime(2024, 10, 30, 12, 0, 0, DateTimeKind.Utc);

        private FakeClock _clock;
        private FakePowerHold _hold;
        private AwakeController _controller;

        [TestInitialize]
        public void SetUp()
        {
            _clock = new FakeClock { UtcNow = BaseUtc };
            _hold = new FakePowerHold();
            _controller = new AwakeController(_clock, _hold);
        }

        [TestCleanup]
        public void TearDown()
        {
            _controller.Dispose();
        }

        // ---- Activate -------------------------------------------------------

        [TestMethod]
        public void Activate_Timed_SetsStateStartEndDurationElapsedRemainingProgress()
        {
            _controller.Activate(TimeSpan.FromMinutes(30));

            Assert.AreEqual(AwakeState.Active, _controller.State);
            Assert.IsTrue(_controller.IsActive);
            Assert.IsFalse(_controller.IsForever);
            Assert.AreEqual(BaseUtc, _controller.StartedUtc);
            Assert.AreEqual((DateTime?)BaseUtc.AddMinutes(30), _controller.EndUtc);
            Assert.AreEqual((TimeSpan?)TimeSpan.FromMinutes(30), _controller.Duration);
            Assert.AreEqual(TimeSpan.Zero, _controller.Elapsed);
            Assert.AreEqual((TimeSpan?)TimeSpan.FromMinutes(30), _controller.Remaining);
            Assert.AreEqual(0.0, _controller.Progress, 0.0000001);
            Assert.AreEqual(1, _hold.HoldCalls);
            Assert.AreEqual(0, _hold.ReleaseCalls);
            Assert.IsTrue(_hold.IsHeld);
        }

        [TestMethod]
        public void Activate_ThenAdvanceClock_ElapsedRemainingProgressTrack()
        {
            _controller.Activate(TimeSpan.FromMinutes(30));
            _clock.UtcNow = BaseUtc.AddMinutes(15);

            Assert.AreEqual(TimeSpan.FromMinutes(15), _controller.Elapsed);
            Assert.AreEqual((TimeSpan?)TimeSpan.FromMinutes(15), _controller.Remaining);
            Assert.AreEqual(0.5, _controller.Progress, 0.0000001);
        }

        [TestMethod]
        public void Remaining_ClampsAtZero_AndProgressClampsToOne()
        {
            _controller.Activate(TimeSpan.FromMinutes(10));
            _clock.UtcNow = BaseUtc.AddMinutes(11);

            Assert.AreEqual((TimeSpan?)TimeSpan.Zero, _controller.Remaining);
            Assert.AreEqual(1.0, _controller.Progress, 0.0000001);
        }

        [TestMethod]
        public void Activate_Forever_SetsActiveWithNoEnd()
        {
            _controller.Activate(null);

            Assert.IsTrue(_controller.IsActive);
            Assert.IsTrue(_controller.IsForever);
            Assert.IsNull(_controller.EndUtc);
            Assert.IsNull(_controller.Duration);
            Assert.IsNull(_controller.Remaining);
            Assert.AreEqual(TimeSpan.Zero, _controller.Elapsed);
            Assert.AreEqual(1.0, _controller.Progress, 0.0000001);
            Assert.AreEqual(1, _hold.HoldCalls);
            Assert.AreEqual(0, _hold.ReleaseCalls);
        }

        [TestMethod]
        public void Activate_ZeroOrNegativeDuration_Throws()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _controller.Activate(TimeSpan.Zero));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _controller.Activate(TimeSpan.FromSeconds(-1)));

            Assert.IsFalse(_controller.IsActive);
            Assert.AreEqual(0, _hold.HoldCalls);
        }

        // ---- Extend ---------------------------------------------------------

        [TestMethod]
        public void Extend_WhileActive_AddsTime()
        {
            _controller.Activate(TimeSpan.FromMinutes(30));
            _clock.UtcNow = BaseUtc.AddMinutes(10);

            _controller.Extend(TimeSpan.FromMinutes(30));

            Assert.IsTrue(_controller.IsActive);
            Assert.AreEqual((DateTime?)BaseUtc.AddMinutes(60), _controller.EndUtc);
            Assert.AreEqual((TimeSpan?)TimeSpan.FromMinutes(60), _controller.Duration);
            Assert.AreEqual((TimeSpan?)TimeSpan.FromMinutes(50), _controller.Remaining);
        }

        [TestMethod]
        public void Extend_WithNull_BecomesForever()
        {
            _controller.Activate(TimeSpan.FromMinutes(30));

            _controller.Extend(null);

            Assert.IsTrue(_controller.IsForever);
            Assert.IsNull(_controller.EndUtc);
            Assert.IsNull(_controller.Duration);
            Assert.IsNull(_controller.Remaining);
        }

        [TestMethod]
        public void Extend_WhileForever_StaysForever()
        {
            _controller.Activate(null);

            _controller.Extend(TimeSpan.FromHours(1));

            Assert.IsTrue(_controller.IsForever);
            Assert.IsNull(_controller.EndUtc);
        }

        [TestMethod]
        public void Extend_WhileInactive_Activates()
        {
            Assert.IsFalse(_controller.IsActive);

            _controller.Extend(TimeSpan.FromMinutes(15));

            Assert.IsTrue(_controller.IsActive);
            Assert.AreEqual((DateTime?)BaseUtc.AddMinutes(15), _controller.EndUtc);
            Assert.AreEqual(1, _hold.HoldCalls);
        }

        [TestMethod]
        public void Extend_WhileInactive_WithNull_ActivatesForever()
        {
            _controller.Extend(null);

            Assert.IsTrue(_controller.IsForever);
            Assert.AreEqual(1, _hold.HoldCalls);
        }

        [TestMethod]
        public void Extend_ZeroOrNegative_Throws()
        {
            _controller.Activate(TimeSpan.FromMinutes(30));

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _controller.Extend(TimeSpan.Zero));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _controller.Extend(TimeSpan.FromSeconds(-1)));

            Assert.AreEqual((DateTime?)BaseUtc.AddMinutes(30), _controller.EndUtc);
        }

        // ---- Deactivate -----------------------------------------------------

        [TestMethod]
        public void Deactivate_ReleasesHoldAndResetsState()
        {
            _controller.Activate(TimeSpan.FromMinutes(30));

            _controller.Deactivate();

            Assert.AreEqual(AwakeState.Inactive, _controller.State);
            Assert.IsFalse(_controller.IsActive);
            Assert.IsNull(_controller.EndUtc);
            Assert.IsNull(_controller.Duration);
            Assert.IsNull(_controller.Remaining);
            Assert.AreEqual(TimeSpan.Zero, _controller.Elapsed);
            Assert.AreEqual(0.0, _controller.Progress, 0.0000001);
            Assert.AreEqual(1, _hold.ReleaseCalls);
            Assert.IsFalse(_hold.IsHeld);
        }

        [TestMethod]
        public void Deactivate_WhileInactive_DoesNothing()
        {
            _controller.Deactivate();

            Assert.IsFalse(_controller.IsActive);
            Assert.AreEqual(0, _hold.ReleaseCalls);
        }

        // ---- Tick -----------------------------------------------------------

        [TestMethod]
        public void Tick_BeforeEnd_DoesNothing()
        {
            _controller.Activate(TimeSpan.FromMinutes(30));
            int fired = 0;
            _controller.Expired += (s, e) => fired++;
            _clock.UtcNow = BaseUtc.AddMinutes(29).AddSeconds(59);

            _controller.Tick();

            Assert.AreEqual(0, fired);
            Assert.IsTrue(_controller.IsActive);
        }

        [TestMethod]
        public void Tick_AtEnd_FiresExpiredExactlyOnceWithKeptDurationAndDeactivates()
        {
            _controller.Activate(TimeSpan.FromMinutes(30));
            int fired = 0;
            TimeSpan kept = TimeSpan.MinValue;
            _controller.Expired += (s, e) => { fired++; kept = e.KeptAwake; };

            _clock.UtcNow = BaseUtc.AddMinutes(30);
            _controller.Tick();

            Assert.AreEqual(1, fired);
            Assert.AreEqual(TimeSpan.FromMinutes(30), kept);
            Assert.IsFalse(_controller.IsActive);
            Assert.AreEqual(1, _hold.ReleaseCalls);

            _clock.UtcNow = BaseUtc.AddHours(2);
            _controller.Tick();
            _controller.Tick();

            Assert.AreEqual(1, fired);
        }

        [TestMethod]
        public void Tick_AfterExtend_ReportsTotalKeptDuration()
        {
            _controller.Activate(TimeSpan.FromMinutes(30));
            _controller.Extend(TimeSpan.FromMinutes(30));
            TimeSpan kept = TimeSpan.MinValue;
            _controller.Expired += (s, e) => kept = e.KeptAwake;

            _clock.UtcNow = BaseUtc.AddMinutes(60);
            _controller.Tick();

            Assert.AreEqual(TimeSpan.FromMinutes(60), kept);
            Assert.IsFalse(_controller.IsActive);
        }

        [TestMethod]
        public void Tick_WhileForever_NeverExpires()
        {
            _controller.Activate(null);
            int fired = 0;
            _controller.Expired += (s, e) => fired++;

            _clock.UtcNow = BaseUtc.AddDays(365);
            _controller.Tick();
            _clock.UtcNow = BaseUtc.AddDays(366);
            _controller.Tick();

            Assert.AreEqual(0, fired);
            Assert.IsTrue(_controller.IsActive);
        }

        // ---- Events ---------------------------------------------------------

        [TestMethod]
        public void StateChanged_FiresOnActivateExtendAndDeactivate()
        {
            int changes = 0;
            _controller.StateChanged += (s, e) => changes++;

            _controller.Activate(TimeSpan.FromMinutes(30));
            Assert.AreEqual(1, changes);

            _controller.Extend(TimeSpan.FromMinutes(30));
            Assert.AreEqual(2, changes);

            _controller.Deactivate();
            Assert.AreEqual(3, changes);

            _controller.Deactivate();
            Assert.AreEqual(3, changes);
        }

        // ---- Tooltip --------------------------------------------------------

        [TestMethod]
        public void TooltipText_Inactive()
        {
            Assert.AreEqual("KeepAwake - Inactive", _controller.TooltipText());
        }

        [TestMethod]
        public void TooltipText_Forever()
        {
            _controller.Activate(null);

            Assert.AreEqual("KeepAwake - Active - forever", _controller.TooltipText());
        }

        [TestMethod]
        public void TooltipText_ShowsCompactRemaining()
        {
            _controller.Activate(TimeSpan.FromMinutes(45));
            _clock.UtcNow = BaseUtc.AddMinutes(15);

            Assert.AreEqual("KeepAwake - Active - 30 min left", _controller.TooltipText());
        }

        [TestMethod]
        public void TooltipText_LessThanAMinuteLeft()
        {
            _controller.Activate(TimeSpan.FromMinutes(1));
            _clock.UtcNow = BaseUtc.AddSeconds(30);

            Assert.AreEqual("KeepAwake - Active - under a minute left", _controller.TooltipText());
        }

        [TestMethod]
        public void TooltipText_ForAllDurations_StaysUnder64Chars()
        {
            var spans = new List<TimeSpan?>();
            foreach (DurationOption option in Durations.All)
            {
                spans.Add(option.Span);
            }
            spans.Add(TimeSpan.FromSeconds(45));
            spans.Add(TimeSpan.FromMinutes(59));
            spans.Add(new TimeSpan(2, 37, 0));
            spans.Add(new TimeSpan(3, 4, 12, 0));
            spans.Add(TimeSpan.FromDays(30));
            spans.Add(TimeSpan.FromDays(400));

            foreach (TimeSpan? span in spans)
            {
                _controller.Deactivate();
                _controller.Activate(span);

                string tooltip = _controller.TooltipText();
                Assert.IsTrue(tooltip.Length < 64,
                    "span={0} tooltip='{1}' length={2}", span, tooltip, tooltip.Length);
                Assert.IsTrue(tooltip.StartsWith("KeepAwake - Active - ", StringComparison.Ordinal),
                    "unexpected prefix for span {0}: '{1}'", span, tooltip);
            }
        }
    }

    [TestClass]
    public class TimeFormatTests
    {
        [TestMethod]
        public void Compact_ZeroOrSubMinute_IsUnderAMinute()
        {
            Assert.AreEqual("under a minute", TimeFormat.Compact(TimeSpan.Zero));
            Assert.AreEqual("under a minute", TimeFormat.Compact(TimeSpan.FromSeconds(1)));
            Assert.AreEqual("under a minute", TimeFormat.Compact(TimeSpan.FromSeconds(59)));
        }

        [TestMethod]
        public void Compact_MinutesOnly()
        {
            Assert.AreEqual("45 min", TimeFormat.Compact(TimeSpan.FromMinutes(45)));
        }

        [TestMethod]
        public void Compact_HoursAndMinutes()
        {
            Assert.AreEqual("2 h 37 min", TimeFormat.Compact(new TimeSpan(2, 37, 0)));
        }

        [TestMethod]
        public void Compact_DaysHoursAndMinutes()
        {
            Assert.AreEqual("3 d 4 h 12 min", TimeFormat.Compact(new TimeSpan(3, 4, 12, 0)));
        }

        [TestMethod]
        public void Compact_Negative_IsUnderAMinute()
        {
            Assert.AreEqual("under a minute", TimeFormat.Compact(TimeSpan.FromMinutes(-5)));
        }
    }

    [TestClass]
    public class DurationsTests
    {
        [TestMethod]
        public void FromMinutes_KnownValues_MapToMatchingOptions()
        {
            Assert.AreEqual(60, Durations.FromMinutes(60).Minutes);
            Assert.AreEqual(120, Durations.FromMinutes(120).Minutes);
            Assert.AreEqual(240, Durations.FromMinutes(240).Minutes);
            Assert.AreEqual(480, Durations.FromMinutes(480).Minutes);
            Assert.AreEqual(1440, Durations.FromMinutes(1440).Minutes);
            Assert.IsTrue(Durations.FromMinutes(Durations.ForeverMinutes).IsForever);
        }

        [TestMethod]
        public void FromMinutes_UnknownValue_FallsBackToEightHours()
        {
            DurationOption fallback = Durations.FromMinutes(90);

            Assert.AreEqual("8 hours", fallback.Label);
            Assert.AreEqual(480, fallback.Minutes);
            Assert.IsFalse(fallback.IsForever);

            Assert.AreEqual(480, Durations.FromMinutes(-7).Minutes);
        }
    }

    [TestClass]
    public class SettingsTests
    {
        private static string TempFile()
        {
            return Path.Combine(Path.GetTempPath(), "keepawake-tests-" + Guid.NewGuid().ToString("N") + ".json");
        }

        private static string TempDir()
        {
            return Path.Combine(Path.GetTempPath(), "keepawake-tests-" + Guid.NewGuid().ToString("N"));
        }

        [TestMethod]
        public void Save_ThenLoad_RoundTripsValues()
        {
            string path = TempFile();
            try
            {
                var settings = new Settings();
                settings.LastDurationMinutes = 120;
                settings.SimulateActivity = true;
                settings.ThemeMode = ThemeMode.Dark;

                settings.Save(path);
                Assert.IsTrue(File.Exists(path));

                Settings loaded = Settings.Load(path);

                Assert.AreEqual(120, loaded.LastDurationMinutes);
                Assert.IsTrue(loaded.SimulateActivity);
                Assert.AreEqual("Dark", loaded.Theme);
                Assert.AreEqual(ThemeMode.Dark, loaded.ThemeMode);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void Save_ThenLoad_RoundTripsForever()
        {
            string path = TempFile();
            try
            {
                var settings = new Settings();
                settings.LastDurationMinutes = Durations.ForeverMinutes;
                settings.Save(path);

                Settings loaded = Settings.Load(path);

                Assert.AreEqual(Durations.ForeverMinutes, loaded.LastDurationMinutes);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void Load_MissingFile_ReturnsDefaults()
        {
            Settings loaded = Settings.Load(TempFile());

            Assert.AreEqual(Durations.DefaultMinutes, loaded.LastDurationMinutes);
            Assert.IsFalse(loaded.SimulateActivity);
            Assert.AreEqual(ThemeMode.System, loaded.ThemeMode);
        }

        [TestMethod]
        public void Load_CorruptFile_ReturnsDefaults()
        {
            string path = TempFile();
            try
            {
                File.WriteAllText(path, "{ this is definitely not json !!! ");

                Settings loaded = Settings.Load(path);

                Assert.AreEqual(Durations.DefaultMinutes, loaded.LastDurationMinutes);
                Assert.IsFalse(loaded.SimulateActivity);
                Assert.AreEqual(ThemeMode.System, loaded.ThemeMode);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void Load_UnknownDuration_FallsBackToDefaultButKeepsOtherValues()
        {
            string path = TempFile();
            try
            {
                File.WriteAllText(path, "{\"lastDurationMinutes\":90,\"simulateActivity\":true,\"theme\":\"Light\"}");

                Settings loaded = Settings.Load(path);

                Assert.AreEqual(Durations.DefaultMinutes, loaded.LastDurationMinutes);
                Assert.IsTrue(loaded.SimulateActivity);
                Assert.AreEqual(ThemeMode.Light, loaded.ThemeMode);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void ThemeMode_UnknownThemeString_FallsBackToSystem()
        {
            var settings = new Settings();
            settings.Theme = "bogus";

            Assert.AreEqual(ThemeMode.System, settings.ThemeMode);
        }

        [TestMethod]
        public void ResolvePath_WritableDirectory_ReturnsBesidePath()
        {
            string dir = TempDir();
            Directory.CreateDirectory(dir);
            try
            {
                string resolved = SettingsStore.ResolvePath(dir);

                Assert.AreEqual(Path.Combine(dir, SettingsStore.FileName), resolved);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir);
            }
        }
    }

    [TestClass]
    public class ActivitySimulatorTests
    {
        [TestMethod]
        public void Start_NudgesPeriodically()
        {
            var nudger = new FakeNudger();
            using (var simulator = new ActivitySimulator(nudger, TimeSpan.FromMilliseconds(15)))
            {
                Assert.IsFalse(simulator.IsRunning);

                simulator.Start();
                Assert.IsTrue(simulator.IsRunning);

                Thread.Sleep(300);
                int nudged = nudger.Count;
                Assert.IsTrue(nudged >= 2, "expected at least 2 nudges, got " + nudged);
            }
        }

        [TestMethod]
        public void Stop_HaltsNudging()
        {
            var nudger = new FakeNudger();
            using (var simulator = new ActivitySimulator(nudger, TimeSpan.FromMilliseconds(15)))
            {
                simulator.Start();
                Thread.Sleep(100);

                simulator.Stop();
                Assert.IsFalse(simulator.IsRunning);

                Thread.Sleep(100); // let any in-flight callback drain
                int afterStop = nudger.Count;

                Thread.Sleep(300);
                Assert.AreEqual(afterStop, nudger.Count);
            }
        }

        [TestMethod]
        public void Start_WhileAlreadyRunning_KeepsRunning()
        {
            var nudger = new FakeNudger();
            using (var simulator = new ActivitySimulator(nudger, TimeSpan.FromMilliseconds(15)))
            {
                simulator.Start();
                simulator.Start();

                Assert.IsTrue(simulator.IsRunning);

                Thread.Sleep(200);
                Assert.IsTrue(nudger.Count >= 2);
            }
        }

        [TestMethod]
        public void Dispose_StopsTheSimulator()
        {
            var nudger = new FakeNudger();
            var simulator = new ActivitySimulator(nudger, TimeSpan.FromMilliseconds(15));

            simulator.Start();
            Assert.IsTrue(simulator.IsRunning);

            simulator.Dispose();
            Assert.IsFalse(simulator.IsRunning);
        }
    }
}
