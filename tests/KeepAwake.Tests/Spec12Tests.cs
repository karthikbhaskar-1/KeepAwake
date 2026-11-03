using System;
using KeepAwake.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeepAwake.Tests
{
    [TestClass]
    public class UntilTimeTests
    {
        private static readonly DateTime Day = new DateTime(2026, 11, 2);

        [TestMethod]
        public void Resolve_LaterToday_IsToday()
        {
            var r = UntilTime.Resolve(Day.AddHours(16), new TimeSpan(17, 30, 0));
            Assert.AreEqual(Day.AddHours(17.5), r);
        }

        [TestMethod]
        public void Resolve_EarlierThanNow_IsTomorrow()
        {
            var r = UntilTime.Resolve(Day.AddHours(18), new TimeSpan(17, 30, 0));
            Assert.AreEqual(Day.AddDays(1).AddHours(17.5), r);
        }

        [TestMethod]
        public void Resolve_ExactlyNow_IsTomorrow()
        {
            var r = UntilTime.Resolve(Day.AddHours(17.5), new TimeSpan(17, 30, 0));
            Assert.AreEqual(Day.AddDays(1).AddHours(17.5), r);
        }

        [TestMethod]
        public void TryParse_AcceptsValidTimes()
        {
            Assert.IsTrue(UntilTime.TryParse("17:30", out var t));
            Assert.AreEqual(new TimeSpan(17, 30, 0), t);
            Assert.IsTrue(UntilTime.TryParse(" 9:05 ", out t));
            Assert.AreEqual(new TimeSpan(9, 5, 0), t);
            Assert.IsTrue(UntilTime.TryParse("00:00", out t));
            Assert.IsTrue(UntilTime.TryParse("23:59", out t));
            Assert.AreEqual("09:05", UntilTime.Format(new TimeSpan(9, 5, 0)));
        }

        [TestMethod]
        public void TryParse_RejectsInvalidStrings()
        {
            foreach (var bad in new[] { null, "", " ", "1730", "24:00", "12:60", "7:5", "ab:cd", "12:3x", "-1:30", "12:30:00", ":30", "1:2:3", "123:00" })
                Assert.IsFalse(UntilTime.TryParse(bad, out _), "input: " + bad);
        }

        [TestMethod]
        public void Suggest_IsNowPlusOneHourRoundedUpToQuarter()
        {
            Assert.AreEqual(new TimeSpan(17, 15, 0), UntilTime.Suggest(Day.AddHours(16).AddMinutes(2)));
            Assert.AreEqual(new TimeSpan(17, 0, 0), UntilTime.Suggest(Day.AddHours(16)));
            Assert.AreEqual(new TimeSpan(17, 45, 0), UntilTime.Suggest(Day.AddHours(16).AddMinutes(31)));
            Assert.AreEqual(TimeSpan.Zero, UntilTime.Suggest(Day.AddHours(22).AddMinutes(50)));
        }
    }

    [TestClass]
    public class ExpiryWarningTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

        private FakeClock _clock;
        private AwakeController _c;
        private int _warnings;

        [TestInitialize]
        public void Setup()
        {
            _clock = new FakeClock { UtcNow = Now };
            _c = new AwakeController(_clock, new FakePowerHold());
            _warnings = 0;
            _c.ExpiryWarning += (s, e) => _warnings++;
        }

        [TestMethod]
        public void Schedule_IncludesWarningInstant()
        {
            Assert.AreEqual(30000, Schedule.NextWakeMs(true, Now, Now.AddMinutes(10), 0, Now.AddSeconds(30)));
            Assert.AreEqual(60000, Schedule.NextWakeMs(true, Now, Now.AddHours(8), 0, Now.AddHours(8).AddMinutes(-5)));
            Assert.AreEqual(Schedule.MinWakeMs, Schedule.NextWakeMs(true, Now, Now.AddHours(1), 0, Now.AddSeconds(-1)));
        }

        [TestMethod]
        public void Schedule_NoWarning_BehavesAsBefore()
        {
            Assert.AreEqual(-1, Schedule.NextWakeMs(true, Now, null, 0, null));
            Assert.AreEqual(10000, Schedule.NextWakeMs(true, Now, Now.AddSeconds(10), 0, null));
        }

        [TestMethod]
        public void WarningUtc_IsFiveMinutesBeforeEnd()
        {
            _c.Activate(TimeSpan.FromHours(1));
            Assert.AreEqual(Now.AddMinutes(55), _c.WarningUtc);
        }

        [TestMethod]
        public void WarningUtc_NullForForeverInactiveAndShortPeriods()
        {
            Assert.IsNull(_c.WarningUtc);
            _c.Activate(null);
            Assert.IsNull(_c.WarningUtc);
            _c.Deactivate();
            _c.Activate(TimeSpan.FromMinutes(9));
            Assert.IsNull(_c.WarningUtc);
            _c.Deactivate();
            _c.Activate(TimeSpan.FromMinutes(10));
            Assert.AreEqual(Now.AddMinutes(5), _c.WarningUtc);
        }

        [TestMethod]
        public void ShortPeriod_NeverWarns()
        {
            _c.Activate(TimeSpan.FromMinutes(8));
            _clock.UtcNow = Now.AddMinutes(4);
            _c.Tick();
            _clock.UtcNow = Now.AddMinutes(8);
            _c.Tick();
            Assert.AreEqual(0, _warnings);
        }

        [TestMethod]
        public void Warning_FiresOncePerActivation()
        {
            _c.Activate(TimeSpan.FromHours(1));
            _clock.UtcNow = Now.AddMinutes(54);
            _c.Tick();
            Assert.AreEqual(0, _warnings);
            _clock.UtcNow = Now.AddMinutes(55);
            _c.Tick();
            _clock.UtcNow = Now.AddMinutes(57);
            _c.Tick();
            Assert.AreEqual(1, _warnings);
            Assert.IsNull(_c.WarningUtc);
        }

        [TestMethod]
        public void Extend_ReArmsTheWarning()
        {
            _c.Activate(TimeSpan.FromHours(1));
            _clock.UtcNow = Now.AddMinutes(56);
            _c.Tick();
            Assert.AreEqual(1, _warnings);

            _c.Extend(TimeSpan.FromHours(1));
            Assert.AreEqual(Now.AddMinutes(115), _c.WarningUtc);
            _clock.UtcNow = Now.AddMinutes(115);
            _c.Tick();
            Assert.AreEqual(2, _warnings);
        }

        [TestMethod]
        public void Expiry_WithoutWarningTick_DoesNotWarn()
        {
            _c.Activate(TimeSpan.FromHours(1));
            _clock.UtcNow = Now.AddHours(2);
            _c.Tick();
            Assert.AreEqual(0, _warnings);
            Assert.IsFalse(_c.IsActive);
        }

        [TestMethod]
        public void ReActivation_ReArmsTheWarning()
        {
            _c.Activate(TimeSpan.FromHours(1));
            _clock.UtcNow = Now.AddMinutes(56);
            _c.Tick();
            _c.Deactivate();
            _c.Activate(TimeSpan.FromHours(1));
            _clock.UtcNow = Now.AddMinutes(56 + 56);
            _c.Tick();
            Assert.AreEqual(2, _warnings);
        }
    }

    [TestClass]
    public class HotkeyTests
    {
        [TestMethod]
        public void Parse_CtrlAltK()
        {
            Assert.IsTrue(Hotkey.TryParse("Ctrl+Alt+K", out var h));
            Assert.AreEqual(Hotkey.ModControl | Hotkey.ModAlt, h.Modifiers);
            Assert.AreEqual((uint)'K', h.VirtualKey);
            Assert.AreEqual("Ctrl+Alt+K", h.ToString());
        }

        [TestMethod]
        public void Parse_IsCaseAndSpaceTolerant()
        {
            Assert.IsTrue(Hotkey.TryParse("ctrl + shift + f5", out var h));
            Assert.AreEqual(Hotkey.ModControl | Hotkey.ModShift, h.Modifiers);
            Assert.AreEqual(0x74u, h.VirtualKey);
            Assert.AreEqual("Ctrl+Shift+F5", h.ToString());
        }

        [TestMethod]
        public void Parse_WinAndDigit()
        {
            Assert.IsTrue(Hotkey.TryParse("Win+1", out var h));
            Assert.AreEqual(Hotkey.ModWin, h.Modifiers);
            Assert.AreEqual((uint)'1', h.VirtualKey);
        }

        [TestMethod]
        public void Parse_FunctionKeyRange()
        {
            Assert.IsTrue(Hotkey.TryParse("Alt+F24", out var h));
            Assert.AreEqual(0x87u, h.VirtualKey);
            Assert.AreEqual("Alt+F24", h.ToString());
            Assert.IsFalse(Hotkey.TryParse("Alt+F25", out _));
            Assert.IsFalse(Hotkey.TryParse("Alt+F0", out _));
        }

        [TestMethod]
        public void Parse_Empty_MeansDisabled()
        {
            foreach (var s in new[] { "", "   ", null })
            {
                Assert.IsTrue(Hotkey.TryParse(s, out var h));
                Assert.IsTrue(h.IsNone);
            }
        }

        [TestMethod]
        public void Parse_NoKeyOrNoModifier_IsInvalid()
        {
            foreach (var bad in new[] { "Ctrl", "Ctrl+Alt", "K", "F5", "Ctrl+K+J", "Ctrl++K", "Ctrl+Enter", "Ctrl+" })
                Assert.IsFalse(Hotkey.TryParse(bad, out _), "input: " + bad);
        }
    }

    [TestClass]
    public class PowerRulesTests
    {
        [TestMethod]
        public void ShouldStopOnBattery_TruthTable()
        {
            for (int i = 0; i < 8; i++)
            {
                bool active = (i & 1) != 0, on = (i & 2) != 0, battery = (i & 4) != 0;
                Assert.AreEqual(active && on && battery, PowerRules.ShouldStopOnBattery(active, on, battery), "i=" + i);
            }
        }
    }

    [TestClass]
    public class Settings12Tests
    {
        [TestMethod]
        public void Defaults_MatchSpec()
        {
            var s = new Settings();
            Assert.AreEqual("", s.LastUntil);
            Assert.IsFalse(s.StopOnBattery);
            Assert.IsFalse(s.StopWhenLocked);
            Assert.AreEqual("Ctrl+Alt+K", s.Hotkey);
        }

        [TestMethod]
        public void ToJson_WritesAllKeysInSpecOrder()
        {
            Assert.AreEqual(
                "{\"lastDurationMinutes\":480,\"lastUntil\":\"\",\"simulateActivity\":false,\"theme\":\"System\",\"stopOnBattery\":false,\"stopWhenLocked\":false,\"hotkey\":\"Ctrl+Alt+K\"}",
                new Settings().ToJson());
        }

        [TestMethod]
        public void RoundTrip_AllNewKeys()
        {
            var original = new Settings
            {
                LastDurationMinutes = UntilTime.UntilMinutes,
                LastUntil = "17:30",
                SimulateActivity = true,
                ThemeMode = ThemeMode.Dark,
                StopOnBattery = true,
                StopWhenLocked = true,
                Hotkey = "Win+Shift+F9",
            };
            var copy = Settings.Parse(original.ToJson());
            Assert.AreEqual(-1, copy.LastDurationMinutes);
            Assert.AreEqual("17:30", copy.LastUntil);
            Assert.IsTrue(copy.SimulateActivity);
            Assert.AreEqual(ThemeMode.Dark, copy.ThemeMode);
            Assert.IsTrue(copy.StopOnBattery);
            Assert.IsTrue(copy.StopWhenLocked);
            Assert.AreEqual("Win+Shift+F9", copy.Hotkey);
        }

        [TestMethod]
        public void RoundTrip_EmptyHotkeyStaysDisabled()
        {
            var copy = Settings.Parse(new Settings { Hotkey = "" }.ToJson());
            Assert.AreEqual("", copy.Hotkey);
        }

        [TestMethod]
        public void OldV11File_StillLoads_WithNewDefaults()
        {
            var s = Settings.Parse("{\"lastDurationMinutes\":120,\"simulateActivity\":true,\"theme\":\"Light\"}");
            Assert.AreEqual(120, s.LastDurationMinutes);
            Assert.IsTrue(s.SimulateActivity);
            Assert.AreEqual(ThemeMode.Light, s.ThemeMode);
            Assert.AreEqual("", s.LastUntil);
            Assert.IsFalse(s.StopOnBattery);
            Assert.IsFalse(s.StopWhenLocked);
            Assert.AreEqual("Ctrl+Alt+K", s.Hotkey);
        }

        [TestMethod]
        public void UntilMode_WithoutValidLastUntil_FallsBackToDefault()
        {
            Assert.AreEqual(Durations.DefaultMinutes, Settings.Parse("{\"lastDurationMinutes\":-1}").LastDurationMinutes);
            Assert.AreEqual(Durations.DefaultMinutes, Settings.Parse("{\"lastDurationMinutes\":-1,\"lastUntil\":\"25:00\"}").LastDurationMinutes);
            Assert.AreEqual("", Settings.Parse("{\"lastUntil\":\"nonsense\"}").LastUntil);
        }

        [TestMethod]
        public void LastUntil_IsNormalised()
        {
            Assert.AreEqual("09:05", Settings.Parse("{\"lastUntil\":\"9:05\"}").LastUntil);
        }
    }
}
