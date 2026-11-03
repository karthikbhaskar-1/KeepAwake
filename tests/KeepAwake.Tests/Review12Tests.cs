using System;
using KeepAwake.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeepAwake.Tests
{
    [TestClass]
    public class UntilTimeDstTests
    {
        private static TimeZoneInfo Berlin => TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

        private static DateTime Utc(int y, int mo, int d, int h, int mi, int s = 0) => new DateTime(y, mo, d, h, mi, s, DateTimeKind.Utc);

        [TestMethod]
        public void FallBack_OvernightSpanIsNineHours()
        {
            var now = Utc(2026, 10, 24, 21, 0);
            var end = UntilTime.ResolveUtc(now, new TimeSpan(7, 0, 0), Berlin);
            Assert.AreEqual(Utc(2026, 10, 25, 6, 0), end);
            Assert.AreEqual(TimeSpan.FromHours(9), end - now);
        }

        [TestMethod]
        public void SpringForward_OvernightSpanIsSevenHours()
        {
            var now = Utc(2027, 3, 27, 22, 0);
            var end = UntilTime.ResolveUtc(now, new TimeSpan(7, 0, 0), Berlin);
            Assert.AreEqual(Utc(2027, 3, 28, 5, 0), end);
            Assert.AreEqual(TimeSpan.FromHours(7), end - now);
        }

        [TestMethod]
        public void TimeInsideSpringForwardGap_ResolvesToFirstValidInstantAfterIt()
        {
            var end = UntilTime.ResolveUtc(Utc(2027, 3, 28, 0, 30), new TimeSpan(2, 30, 0), Berlin);
            Assert.AreEqual(Utc(2027, 3, 28, 1, 0), end);
        }

        [TestMethod]
        public void JustBeforeMidnight_Midnight_IsThirtySecondsAhead()
        {
            var now = Utc(2026, 6, 1, 23, 59, 30);
            Assert.AreEqual(TimeSpan.FromSeconds(30), UntilTime.ResolveUtc(now, TimeSpan.Zero, TimeZoneInfo.Utc) - now);
        }

        [TestMethod]
        public void EqualToNow_MeansTomorrow()
        {
            var now = Utc(2026, 6, 1, 17, 30);
            Assert.AreEqual(TimeSpan.FromHours(24), UntilTime.ResolveUtc(now, new TimeSpan(17, 30, 0), TimeZoneInfo.Utc) - now);
        }

        [TestMethod]
        public void UtcZone_MatchesNaiveResolve()
        {
            var now = Utc(2026, 6, 1, 18, 0);
            var tod = new TimeSpan(17, 30, 0);
            Assert.AreEqual(UntilTime.Resolve(now, tod), UntilTime.ResolveUtc(now, tod, TimeZoneInfo.Utc));
        }
    }

    [TestClass]
    public class PeriodAndScheduleTests
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
        public void PeriodId_ChangesOnActivateExtendDeactivate()
        {
            int p0 = _c.PeriodId;
            _c.Activate(TimeSpan.FromHours(1));
            int p1 = _c.PeriodId;
            Assert.AreNotEqual(p0, p1);
            _c.Extend(TimeSpan.FromHours(1));
            int p2 = _c.PeriodId;
            Assert.AreNotEqual(p1, p2);
            _c.Extend(null);
            int p3 = _c.PeriodId;
            Assert.AreNotEqual(p2, p3);
            _c.Deactivate();
            Assert.AreNotEqual(p3, _c.PeriodId);
        }

        [TestMethod]
        public void CapturedPeriodId_NoLongerMatchesAfterDeactivateActivate()
        {
            _c.Activate(TimeSpan.FromHours(1));
            int captured = _c.PeriodId;
            _c.Deactivate();
            _c.Activate(TimeSpan.FromHours(8));
            Assert.AreNotEqual(captured, _c.PeriodId);
        }

        [TestMethod]
        public void ExtendForever_AfterWarningArmed_ClearsWarningAndNeverWarns()
        {
            _c.Activate(TimeSpan.FromHours(1));
            Assert.IsNotNull(_c.WarningUtc);
            _c.Extend(null);
            Assert.IsNull(_c.WarningUtc);
            _clock.UtcNow = Now.AddMinutes(58);
            _c.Tick();
            Assert.AreEqual(0, _warnings);
        }

        [TestMethod]
        public void ShortPeriodExtendedByOneHour_ArmsWarningAtNewEndMinusFive()
        {
            _c.Activate(TimeSpan.FromMinutes(8));
            Assert.IsNull(_c.WarningUtc);
            _c.Extend(TimeSpan.FromHours(1));
            Assert.AreEqual(Now.AddMinutes(68 - 5), _c.WarningUtc);
        }

        [TestMethod]
        public void Schedule_WarningAfterEnd_EndWins()
        {
            Assert.AreEqual(10000, Schedule.NextWakeMs(true, Now, Now.AddSeconds(10), 0, Now.AddSeconds(20)));
        }

        [TestMethod]
        public void Schedule_PastWarningWakesOnce_ThenTickClearsIt()
        {
            _c.Activate(TimeSpan.FromHours(1));
            _clock.UtcNow = Now.AddMinutes(56);
            Assert.AreEqual(Schedule.MinWakeMs, Schedule.NextWakeMs(true, _c.UtcNow, _c.EndUtc, 0, Now.AddMinutes(55)));
            _c.Tick();
            Assert.AreEqual(1, _warnings);
            Assert.IsNull(_c.WarningUtc);
            Assert.AreNotEqual(Schedule.MinWakeMs, Schedule.NextWakeMs(true, _c.UtcNow, _c.EndUtc, 0, _c.WarningUtc));
        }
    }

    [TestClass]
    public class Review12HotkeyAndSettingsTests
    {
        [TestMethod]
        public void Parse_ShiftOnlyAndAltF4_AreInvalid()
        {
            foreach (var bad in new[] { "Shift+A", "Shift+1", "Shift+F5", "Alt+F4", "alt + f4" })
                Assert.IsFalse(Hotkey.TryParse(bad, out _), "input: " + bad);
            Assert.IsTrue(Hotkey.TryParse("Ctrl+Alt+F4", out _));
            Assert.IsTrue(Hotkey.TryParse("Alt+Shift+F4", out _));
        }

        [TestMethod]
        public void RoundTrip_EveryModifierCombination()
        {
            for (uint mods = 1; mods < 16; mods++)
            {
                if ((mods & ~Hotkey.ModShift) == 0) continue;
                var hk = new Hotkey(mods, (uint)'Q');
                Assert.IsTrue(Hotkey.TryParse(hk.ToString(), out var back), hk.ToString());
                Assert.AreEqual(mods, back.Modifiers);
                Assert.AreEqual(hk.ToString(), back.ToString());
            }
        }

        [TestMethod]
        public void Parse_LowercaseKey_Canonicalises()
        {
            Assert.IsTrue(Hotkey.TryParse("Ctrl+Alt+k", out var h));
            Assert.AreEqual("Ctrl+Alt+K", h.ToString());
        }

        [TestMethod]
        public void HotkeyWithQuoteAndBackslash_RoundTrips()
        {
            const string tricky = "a\"b\\c";
            var copy = Settings.Parse(new Settings { Hotkey = tricky }.ToJson());
            Assert.AreEqual(tricky, copy.Hotkey);
        }

        [TestMethod]
        public void LastUntil_WithQuote_StillProducesParseableFile()
        {
            var json = new Settings { LastUntil = "1\"7:30" }.ToJson();
            Assert.AreEqual(Durations.DefaultMinutes, Settings.Parse(json).LastDurationMinutes);
            Assert.AreEqual("Ctrl+Alt+K", Settings.Parse(json).Hotkey);
        }

        [TestMethod]
        public void KeysInReverseOrder_LoadAsUntilMode()
        {
            var s = Settings.Parse("{\"lastUntil\":\"17:30\",\"lastDurationMinutes\":-1}");
            Assert.AreEqual(-1, s.LastDurationMinutes);
            Assert.AreEqual("17:30", s.LastUntil);
            s = Settings.Parse("{\"lastDurationMinutes\":-1,\"lastUntil\":\"\"}");
            Assert.AreEqual(Durations.DefaultMinutes, s.LastDurationMinutes);
        }
    }
}
