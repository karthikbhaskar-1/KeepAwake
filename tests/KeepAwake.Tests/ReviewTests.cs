using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using KeepAwake.Core;
using KeepAwake.UI;
using KeepAwake.UI.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeepAwake.Tests
{
    internal static class TempPaths
    {
        public static string NewDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "keepawake-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void Delete(string dir)
        {
            try
            {
                foreach (var f in Directory.GetFiles(dir)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dir, true);
            }
            catch { }
        }
    }

    [TestClass]
    public class SettingsParsingTests
    {
        [TestMethod]
        public void Parse_OnlyTheme_KeepsDefaultDuration()
        {
            var s = Settings.Parse("{\"theme\":\"Dark\"}");

            Assert.AreEqual(Durations.DefaultMinutes, s.LastDurationMinutes);
            Assert.AreEqual(ThemeMode.Dark, s.ThemeMode);
            Assert.IsFalse(s.SimulateActivity);
        }

        [TestMethod]
        public void Parse_OnlyDuration_KeepsThemeSystemAndSimulateOff()
        {
            var s = Settings.Parse("{\"lastDurationMinutes\":60}");

            Assert.AreEqual(60, s.LastDurationMinutes);
            Assert.AreEqual("System", s.Theme);
            Assert.AreEqual(ThemeMode.System, s.ThemeMode);
            Assert.IsFalse(s.SimulateActivity);
        }

        [TestMethod]
        public void Parse_ExplicitZero_IsForever()
        {
            Assert.AreEqual(Durations.ForeverMinutes, Settings.Parse("{\"lastDurationMinutes\":0}").LastDurationMinutes);
        }

        [TestMethod]
        public void Parse_NegativeOrUnknownDuration_FallsBackToDefault()
        {
            Assert.AreEqual(Durations.DefaultMinutes, Settings.Parse("{\"lastDurationMinutes\":-5}").LastDurationMinutes);
            Assert.AreEqual(Durations.DefaultMinutes, Settings.Parse("{\"lastDurationMinutes\":77}").LastDurationMinutes);
            Assert.AreEqual(Durations.DefaultMinutes, Settings.Parse("{\"lastDurationMinutes\":\"abc\"}").LastDurationMinutes);
        }

        [TestMethod]
        public void ThemeMode_NumericAndOddValues_FallBackToSystem()
        {
            foreach (var bad in new[] { "5", "-1", "0", "", "bogus" })
                Assert.AreEqual(ThemeMode.System, new Settings { Theme = bad }.ThemeMode, "value '" + bad + "'");
            Assert.AreEqual(ThemeMode.System, new Settings { Theme = null }.ThemeMode);
        }

        [TestMethod]
        public void ThemeMode_IsCaseInsensitiveAndTrimmed()
        {
            Assert.AreEqual(ThemeMode.Dark, new Settings { Theme = " dark " }.ThemeMode);
            Assert.AreEqual(ThemeMode.Dark, new Settings { Theme = "DARK" }.ThemeMode);
            Assert.AreEqual(ThemeMode.Light, new Settings { Theme = "light" }.ThemeMode);
        }

        [TestMethod]
        public void Parse_NullLiteralEmptyAndGarbage_GiveDefaults()
        {
            foreach (var text in new[] { "null", "", "   ", "[]", "42", "{", "{\"theme\"", null })
            {
                var s = Settings.Parse(text);
                Assert.AreEqual(Durations.DefaultMinutes, s.LastDurationMinutes, "input: " + text);
                Assert.AreEqual(ThemeMode.System, s.ThemeMode, "input: " + text);
                Assert.IsFalse(s.SimulateActivity, "input: " + text);
            }
        }

        [TestMethod]
        public void Parse_ToleratesBomWhitespaceUnknownAndNestedKeys()
        {
            var json = "﻿  {\n \"extra\": {\"a\": [1, 2, {\"b\": \"}\"}]},\n \"lastDurationMinutes\" : 240 ,\"future\":\"x\",\n\"simulateActivity\": true, \"theme\": \"Light\" }";

            var s = Settings.Parse(json);

            Assert.AreEqual(240, s.LastDurationMinutes);
            Assert.IsTrue(s.SimulateActivity);
            Assert.AreEqual(ThemeMode.Light, s.ThemeMode);
        }

        [TestMethod]
        public void Load_FileWithUtf8Bom_IsRead()
        {
            var dir = TempPaths.NewDir();
            try
            {
                var path = Path.Combine(dir, "keepawake.json");
                File.WriteAllText(path, "{\"lastDurationMinutes\":120,\"theme\":\"Dark\"}", new System.Text.UTF8Encoding(true));

                var s = Settings.Load(path);

                Assert.AreEqual(120, s.LastDurationMinutes);
                Assert.AreEqual(ThemeMode.Dark, s.ThemeMode);
            }
            finally { TempPaths.Delete(dir); }
        }

        [TestMethod]
        public void ToJson_ThenParse_RoundTrips()
        {
            var original = new Settings { LastDurationMinutes = 1440, SimulateActivity = true, ThemeMode = ThemeMode.Light };

            var json = original.ToJson();
            var copy = Settings.Parse(json);

            Assert.AreEqual("{\"lastDurationMinutes\":1440,\"simulateActivity\":true,\"theme\":\"Light\"}", json);
            Assert.AreEqual(1440, copy.LastDurationMinutes);
            Assert.IsTrue(copy.SimulateActivity);
            Assert.AreEqual(ThemeMode.Light, copy.ThemeMode);
        }
    }

    [TestClass]
    public class SettingsStoreTests
    {
        [TestMethod]
        public void ResolvePath_ReadOnlyFileBesideExe_ReturnsFallback_AndLeavesNoResidue()
        {
            var dir = TempPaths.NewDir();
            var fallbackDir = TempPaths.NewDir();
            try
            {
                var beside = Path.Combine(dir, SettingsStore.FileName);
                File.WriteAllText(beside, "{}");
                File.SetAttributes(beside, FileAttributes.ReadOnly);
                var fallback = Path.Combine(fallbackDir, "KeepAwake", SettingsStore.FileName);

                var resolved = SettingsStore.ResolvePath(dir, fallback);

                Assert.AreEqual(fallback, resolved);
                CollectionAssert.AreEqual(new[] { beside }, Directory.GetFileSystemEntries(dir));
                Assert.AreEqual(0, Directory.GetFileSystemEntries(fallbackDir).Length);
            }
            finally { TempPaths.Delete(dir); TempPaths.Delete(fallbackDir); }
        }

        [TestMethod]
        public void ResolvePath_WritableFileBesideExe_ReturnsBeside()
        {
            var dir = TempPaths.NewDir();
            try
            {
                var beside = Path.Combine(dir, SettingsStore.FileName);
                File.WriteAllText(beside, "{}");

                Assert.AreEqual(beside, SettingsStore.ResolvePath(dir, Path.Combine(dir, "nope", "x.json")));
            }
            finally { TempPaths.Delete(dir); }
        }

        [TestMethod]
        public void ResolvePath_NoFile_ReturnsBesideWithoutCreatingAnything()
        {
            var dir = TempPaths.NewDir();
            try
            {
                var resolved = SettingsStore.ResolvePath(dir, Path.Combine(dir, "fb", "k.json"));

                Assert.AreEqual(Path.Combine(dir, SettingsStore.FileName), resolved);
                Assert.AreEqual(0, Directory.GetFileSystemEntries(dir).Length);
            }
            finally { TempPaths.Delete(dir); }
        }

        [TestMethod]
        public void ResolvePath_ExistingFallbackWins()
        {
            var dir = TempPaths.NewDir();
            try
            {
                var beside = Path.Combine(dir, SettingsStore.FileName);
                var fallback = Path.Combine(dir, "fb.json");
                File.WriteAllText(beside, "{}");
                File.WriteAllText(fallback, "{}");

                Assert.AreEqual(fallback, SettingsStore.ResolvePath(dir, fallback));
            }
            finally { TempPaths.Delete(dir); }
        }

        [TestMethod]
        public void Save_LeavesNoTempFile_AndOverwritesAtomically()
        {
            var dir = TempPaths.NewDir();
            try
            {
                var path = Path.Combine(dir, "keepawake.json");
                new Settings { LastDurationMinutes = 60 }.Save(path);
                new Settings { LastDurationMinutes = 240 }.Save(path);

                CollectionAssert.AreEqual(new[] { path }, Directory.GetFileSystemEntries(dir));
                Assert.AreEqual(240, Settings.Load(path).LastDurationMinutes);
            }
            finally { TempPaths.Delete(dir); }
        }

        [TestMethod]
        public void Save_OverReadOnlyFile_Throws_AndLeavesNoTempFile()
        {
            var dir = TempPaths.NewDir();
            try
            {
                var path = Path.Combine(dir, "keepawake.json");
                File.WriteAllText(path, "{}");
                File.SetAttributes(path, FileAttributes.ReadOnly);

                bool threw = false;
                try { new Settings().Save(path); }
                catch (UnauthorizedAccessException) { threw = true; }
                catch (IOException) { threw = true; }

                Assert.IsTrue(threw, "saving over a read-only file must fail so the caller can fall back");
                CollectionAssert.AreEqual(new[] { path }, Directory.GetFileSystemEntries(dir));
            }
            finally { TempPaths.Delete(dir); }
        }

        [TestMethod]
        public void Save_CreatesMissingDirectory()
        {
            var dir = TempPaths.NewDir();
            try
            {
                var path = Path.Combine(dir, "sub", "keepawake.json");
                new Settings().Save(path);
                Assert.IsTrue(File.Exists(path));
            }
            finally { TempPaths.Delete(dir); }
        }
    }

    [TestClass]
    public class TimeFormatReviewTests
    {
        [TestMethod]
        public void RingParts_CoversEdges()
        {
            string h, m;
            TimeFormat.RingParts(TimeSpan.Zero, out h, out m);
            Assert.AreEqual("0", h); Assert.AreEqual("00", m);

            TimeFormat.RingParts(new TimeSpan(0, 59, 59), out h, out m);
            Assert.AreEqual("0", h); Assert.AreEqual("59", m);

            TimeFormat.RingParts(new TimeSpan(25, 5, 0), out h, out m);
            Assert.AreEqual("25", h); Assert.AreEqual("05", m);

            TimeFormat.RingParts(TimeSpan.FromMinutes(-3), out h, out m);
            Assert.AreEqual("0", h); Assert.AreEqual("00", m);
        }

        [TestMethod]
        public void Compact_ZeroMiddleUnits()
        {
            Assert.AreEqual("1 d 0 h 5 min", TimeFormat.Compact(new TimeSpan(1, 0, 5, 0)));
            Assert.AreEqual("8 h 0 min", TimeFormat.Compact(TimeSpan.FromHours(8)));
        }

        [TestMethod]
        public void Duration_OmitsZeroTrailingUnits()
        {
            Assert.AreEqual("8 h", TimeFormat.Duration(TimeSpan.FromHours(8)));
            Assert.AreEqual("1 h 30 min", TimeFormat.Duration(TimeSpan.FromMinutes(90)));
            Assert.AreEqual("45 min", TimeFormat.Duration(TimeSpan.FromMinutes(45)));
            Assert.AreEqual("1 d", TimeFormat.Duration(TimeSpan.FromHours(24)));
            Assert.AreEqual("1 d 4 h", TimeFormat.Duration(TimeSpan.FromHours(28)));
            Assert.AreEqual("under a minute", TimeFormat.Duration(TimeSpan.FromSeconds(10)));
            Assert.AreEqual("9 h", TimeFormat.Duration(TimeSpan.FromHours(9).Add(TimeSpan.FromSeconds(10))));
        }

        [TestMethod]
        public void Remaining_RoundsUpToTheNextMinute()
        {
            Assert.AreEqual("8 h 0 min", TimeFormat.Remaining(TimeSpan.FromHours(8)));
            Assert.AreEqual("8 h 0 min", TimeFormat.Remaining(TimeSpan.FromHours(8) - TimeSpan.FromSeconds(1)));
            Assert.AreEqual("7 h 59 min", TimeFormat.Remaining(TimeSpan.FromHours(8) - TimeSpan.FromSeconds(61)));
            Assert.AreEqual("1 min", TimeFormat.Remaining(TimeSpan.FromSeconds(60)));
            Assert.AreEqual("under a minute", TimeFormat.Remaining(TimeSpan.FromSeconds(59.9)));
            Assert.AreEqual("under a minute", TimeFormat.Remaining(TimeSpan.Zero));
            Assert.AreEqual("under a minute", TimeFormat.Remaining(TimeSpan.FromMinutes(-2)));
        }

        [TestMethod]
        public void Since_WeekdayUpToSixDays_DateBeyond()
        {
            var inv = CultureInfo.InvariantCulture;
            var now = new DateTime(2026, 11, 2, 12, 0, 0);

            var recent = TimeFormat.Since(now.AddDays(-5.99), now, inv);
            var sixDays = TimeFormat.Since(now.AddDays(-6), now, inv);
            var sevenDays = TimeFormat.Since(now.AddDays(-7), now, inv);

            Assert.AreEqual(now.AddDays(-5.99).ToString("ddd HH:mm", inv), recent);
            Assert.AreEqual(now.AddDays(-6).ToString("ddd HH:mm", inv), sixDays);
            Assert.AreEqual(now.AddDays(-7).ToString("d MMM HH:mm", inv), sevenDays);
        }
    }

    [TestClass]
    public class DurationsReviewTests
    {
        [TestMethod]
        public void Extend_IsOneTwoFourHoursAndForever()
        {
            var spans = Durations.Extend.Select(d => d.Span).ToArray();

            CollectionAssert.AreEqual(
                new TimeSpan?[] { TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(4), null }, spans);
        }

        [TestMethod]
        public void ActivateLabel_ForeverAndEightHours()
        {
            Assert.AreEqual("Keep awake forever", Durations.ActivateLabel(Durations.FromMinutes(Durations.ForeverMinutes)));
            Assert.AreEqual("Keep awake for 8 hours", Durations.ActivateLabel(Durations.FromMinutes(480)));
        }

        [TestMethod]
        public void FromMinutes_Zero_IsTheForeverOption()
        {
            Assert.IsTrue(Durations.FromMinutes(0).IsForever);
        }
    }

    [TestClass]
    public class ControllerReviewTests
    {
        private static readonly DateTime BaseUtc = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

        [TestMethod]
        public void Tick_ExactlyAtEnd_Expires()
        {
            var clock = new FakeClock { UtcNow = BaseUtc };
            using (var c = new AwakeController(clock, new FakePowerHold()))
            {
                c.Activate(TimeSpan.FromMinutes(10));
                clock.UtcNow = BaseUtc.AddMinutes(10).AddTicks(-1);
                c.Tick();
                Assert.IsTrue(c.IsActive);

                clock.UtcNow = BaseUtc.AddMinutes(10);
                c.Tick();
                Assert.IsFalse(c.IsActive);
            }
        }

        [TestMethod]
        public void Extend_AfterEndPassedButBeforeTick_AddsToTheOldEnd()
        {
            var clock = new FakeClock { UtcNow = BaseUtc };
            using (var c = new AwakeController(clock, new FakePowerHold()))
            {
                c.Activate(TimeSpan.FromMinutes(10));
                clock.UtcNow = BaseUtc.AddMinutes(20);   // resumed from sleep: end passed, Tick has not run yet

                c.Extend(TimeSpan.FromHours(1));
                c.Tick();

                Assert.IsTrue(c.IsActive);
                Assert.AreEqual((DateTime?)BaseUtc.AddMinutes(70), c.EndUtc);
            }
        }

        [TestMethod]
        public void Activate_WhileActive_ResetsStart_AndRealHoldStaysSingle()
        {
            var clock = new FakeClock { UtcNow = BaseUtc };
            using (var hold = new ThreadPowerHold())
            using (var c = new AwakeController(clock, hold))
            {
                c.Activate(TimeSpan.FromHours(1));
                clock.UtcNow = BaseUtc.AddMinutes(5);
                c.Activate(TimeSpan.FromHours(2));

                Assert.AreEqual(BaseUtc.AddMinutes(5), c.StartedUtc);
                Assert.AreEqual((DateTime?)BaseUtc.AddMinutes(125), c.EndUtc);
                Assert.IsTrue(hold.IsHeld);
                c.Deactivate();
                Assert.IsFalse(hold.IsHeld, "one release must end the hold");
            }
        }

        [TestMethod]
        public void Expired_IsRaisedAfterStateChanged_WithInactiveState()
        {
            var clock = new FakeClock { UtcNow = BaseUtc };
            var order = new List<string>();
            using (var c = new AwakeController(clock, new FakePowerHold()))
            {
                c.StateChanged += (s, e) => order.Add("state:" + c.State);
                c.Expired += (s, e) => order.Add("expired:" + c.State + ":" + e.KeptAwake.TotalMinutes);
                c.Activate(TimeSpan.FromMinutes(5));
                order.Clear();

                clock.UtcNow = BaseUtc.AddMinutes(5);
                c.Tick();
            }
            CollectionAssert.AreEqual(new[] { "state:Inactive", "expired:Inactive:5" }, order);
        }

        [TestMethod]
        public void ClockMovingBackwards_ProgressStaysZero_AndRingShowsZero()
        {
            var clock = new FakeClock { UtcNow = BaseUtc };
            using (var c = new AwakeController(clock, new FakePowerHold()))
            {
                c.Activate(TimeSpan.FromHours(1));
                clock.UtcNow = BaseUtc.AddMinutes(-30);

                Assert.IsTrue(c.Elapsed < TimeSpan.Zero);
                Assert.AreEqual(0.0, c.Progress, 1e-9);
                string h, m;
                TimeFormat.RingParts(c.Elapsed, out h, out m);
                Assert.AreEqual("0", h);
                Assert.AreEqual("00", m);
            }
        }

        [TestMethod]
        public void Dispose_WhileActive_RaisesStateChanged_ReleasesThenDisposesHold()
        {
            var clock = new FakeClock { UtcNow = BaseUtc };
            var hold = new FakePowerHold();
            var c = new AwakeController(clock, hold);
            int changes = 0;
            c.Activate(TimeSpan.FromHours(1));
            c.StateChanged += (s, e) => changes++;

            c.Dispose();

            Assert.AreEqual(1, changes);
            Assert.AreEqual(1, hold.ReleaseCalls);
            Assert.IsTrue(hold.IsDisposed);
        }

        [TestMethod]
        public void Tooltip_MoreThanADayRemaining_ShowsDays_AndStaysShort()
        {
            var clock = new FakeClock { UtcNow = BaseUtc };
            using (var c = new AwakeController(clock, new FakePowerHold()))
            {
                c.Activate(TimeSpan.FromHours(24));
                c.Extend(TimeSpan.FromHours(4));

                var tip = c.TooltipText();

                Assert.AreEqual("KeepAwake - Active - 1 d 4 h 0 min left", tip);
                Assert.IsTrue(tip.Length < 64);
            }
        }

        [TestMethod]
        public void Tooltip_FreshEightHourSession_ReadsEightHours()
        {
            var clock = new FakeClock { UtcNow = BaseUtc };
            using (var c = new AwakeController(clock, new FakePowerHold()))
            {
                c.Activate(TimeSpan.FromHours(8));
                clock.UtcNow = BaseUtc.AddSeconds(2);

                Assert.AreEqual("KeepAwake - Active - 8 h 0 min left", c.TooltipText());
            }
        }

        [TestMethod]
        public void HoldFailed_ReflectsThePowerHoldResult()
        {
            var clock = new FakeClock { UtcNow = BaseUtc };
            var hold = new FakePowerHold { LastHoldSucceeded = false };
            using (var c = new AwakeController(clock, hold))
            {
                Assert.IsFalse(c.HoldFailed, "inactive: nothing to report");
                c.Activate(TimeSpan.FromHours(1));
                Assert.IsTrue(c.HoldFailed);
            }
        }

        [TestMethod]
        public void SystemClock_IsMonotonic()
        {
            var a = SystemClock.Instance.UtcNow;
            Thread.Sleep(20);
            var b = SystemClock.Instance.UtcNow;

            Assert.IsTrue(b > a);
            Assert.IsTrue(Math.Abs((b - DateTime.UtcNow).TotalSeconds) < 5, "stays close to wall time");
        }
    }

    [TestClass]
    public class ScheduleTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

        [TestMethod]
        public void Inactive_NeedsNoWake()
        {
            Assert.AreEqual(-1, Schedule.NextWakeMs(false, Now, null, 0));
            Assert.AreEqual(-1, Schedule.NextWakeMs(false, Now, Now.AddHours(1), 0));
        }

        [TestMethod]
        public void Forever_NeedsNoWake()
        {
            Assert.AreEqual(-1, Schedule.NextWakeMs(true, Now, null, 30000));
        }

        [TestMethod]
        public void TenSecondsToEnd_WakesAtTheEnd()
        {
            Assert.AreEqual(10000, Schedule.NextWakeMs(true, Now, Now.AddSeconds(10), 0));
        }

        [TestMethod]
        public void ThreeMinutesToEnd_WakesForTheTrayRefresh()
        {
            Assert.AreEqual(40000, Schedule.NextWakeMs(true, Now, Now.AddMinutes(3), 20000));
        }

        [TestMethod]
        public void LongSession_NeverWaitsMoreThanAMinute()
        {
            Assert.AreEqual(60000, Schedule.NextWakeMs(true, Now, Now.AddHours(8), 0));
        }

        [TestMethod]
        public void PastTheEnd_UsesTheMinimum()
        {
            Assert.AreEqual(Schedule.MinWakeMs, Schedule.NextWakeMs(true, Now, Now.AddSeconds(-5), 0));
            Assert.AreEqual(Schedule.MinWakeMs, Schedule.NextWakeMs(true, Now, Now.AddHours(1), 59990));
        }
    }

    [TestClass]
    public class ThemeTests
    {
        [TestMethod]
        public void FromAbgr_ReordersChannels()
        {
            var c = Theme.FromAbgr(unchecked((int)0xFFD77800));

            Assert.AreEqual(0x00, c.R);
            Assert.AreEqual(0x78, c.G);
            Assert.AreEqual(0xD7, c.B);
        }

        private static void AssertColor(int rgb, Color actual, string name)
        {
            Assert.AreEqual(Color.FromArgb(0xFF, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF).ToArgb(), actual.ToArgb(), name);
        }

        [TestMethod]
        public void Build_Dark_MatchesTheSpecColours()
        {
            var p = Theme.Build(true, Theme.DefaultAccent);

            Assert.IsTrue(p.IsDark);
            AssertColor(0x0F1115, p.Bg, "bg");
            AssertColor(0x182238, p.Glow, "glow");
            AssertColor(0x262B37, p.CardBorder, "border");
            AssertColor(0xF2F4F8, p.Text, "text");
            AssertColor(0x8B93A7, p.Muted, "muted");
            AssertColor(0x5F6780, p.Faint, "faint");
            AssertColor(0x222837, p.Track, "track");
            AssertColor(0x171A21, p.Menu, "menu");
            AssertColor(0x2E3442, p.MenuBorder, "menu border");
            AssertColor(0x34D399, p.Success, "success");
            AssertColor(0xF87171, p.Danger, "danger");
        }

        [TestMethod]
        public void Build_Light_MatchesTheSpecColours()
        {
            var p = Theme.Build(false, Theme.DefaultAccent);

            Assert.IsFalse(p.IsDark);
            AssertColor(0xF7F8FC, p.Bg, "bg");
            AssertColor(0xE8EEFB, p.Glow, "glow");
            AssertColor(0xFFFFFF, p.Card, "card");
            AssertColor(0xE2E5EE, p.CardBorder, "border");
            AssertColor(0x111827, p.Text, "text");
            AssertColor(0x6B7280, p.Muted, "muted");
            AssertColor(0x9CA3AF, p.Faint, "faint");
            AssertColor(0xE3E7F0, p.Track, "track");
            AssertColor(0xFFFFFF, p.Menu, "menu");
            AssertColor(0xD5D9E4, p.MenuBorder, "menu border");
            AssertColor(0x047857, p.Success, "success");
            AssertColor(0xB91C1C, p.Danger, "danger");
        }
    }

    [TestClass]
    public class PainterTests
    {
        [TestMethod]
        public void FromHsl_PrimaryColoursRoundTrip()
        {
            Assert.AreEqual(Color.FromArgb(255, 0, 0).ToArgb(), Painter.FromHsl(0, 1, 0.5f).ToArgb());
            Assert.AreEqual(Color.FromArgb(0, 255, 0).ToArgb(), Painter.FromHsl(120, 1, 0.5f).ToArgb());
            Assert.AreEqual(Color.FromArgb(0, 0, 255).ToArgb(), Painter.FromHsl(240, 1, 0.5f).ToArgb());
            Assert.AreEqual(Color.FromArgb(0, 255, 255).ToArgb(), Painter.FromHsl(180, 1, 0.5f).ToArgb());
            Assert.AreEqual(Color.White.ToArgb(), Painter.FromHsl(10, 1, 1f).ToArgb());
            Assert.AreEqual(Color.Black.ToArgb(), Painter.FromHsl(10, 1, 0f).ToArgb());
            Assert.AreEqual(Painter.FromHsl(30, 1, 0.5f).ToArgb(), Painter.FromHsl(390, 1, 0.5f).ToArgb(), "hue wraps");
        }

        [TestMethod]
        public void Mix_ClampsOutsideZeroToOne()
        {
            var a = Color.FromArgb(10, 20, 30);
            var b = Color.FromArgb(200, 210, 220);

            Assert.AreEqual(a.ToArgb(), Painter.Mix(a, b, -1f).ToArgb());
            Assert.AreEqual(b.ToArgb(), Painter.Mix(a, b, 2f).ToArgb());
            Assert.AreEqual(Color.FromArgb(105, 115, 125).ToArgb(), Painter.Mix(a, b, 0.5f).ToArgb());
        }
    }

    [TestClass]
    public class SplitButtonTests
    {
        [TestMethod]
        public void IsChevronHit_ScalesWithDpi()
        {
            foreach (var dpi in new[] { 96, 120, 144, 192 })
            {
                int scale = (int)Math.Round(46 * dpi / 96f);
                int width = (int)Math.Round(324 * dpi / 96f);
                Assert.IsTrue(PillButton.IsChevronHit(width, dpi, 46, new Point(width - 1, 5)), "right edge at " + dpi);
                Assert.IsTrue(PillButton.IsChevronHit(width, dpi, 46, new Point(width - scale, 5)), "left edge of chevron at " + dpi);
                Assert.IsFalse(PillButton.IsChevronHit(width, dpi, 46, new Point(width - scale - 1, 5)), "just left of chevron at " + dpi);
                Assert.IsFalse(PillButton.IsChevronHit(width, dpi, 46, new Point(width / 2, 5)), "body at " + dpi);
            }
        }

        [TestMethod]
        public void IsChevronHit_ExtendButtonUsesItsNarrowerChevron()
        {
            Assert.IsTrue(PillButton.IsChevronHit(194, 96, 40, new Point(160, 20)));
            Assert.IsFalse(PillButton.IsChevronHit(194, 96, 40, new Point(150, 20)));
        }
    }

    [TestClass]
    public class ActivitySimulatorTimerTests
    {
        private sealed class ManualTimer : IDisposable
        {
            public TimerCallback Callback;
            public bool Disposed;
            public void Dispose() { Disposed = true; }
        }

        [TestMethod]
        public void ManualTimer_DrivesNudgesWithoutSleeping()
        {
            var nudger = new FakeNudger();
            ManualTimer timer = null;
            TimeSpan seen = TimeSpan.Zero;
            using (var sim = new ActivitySimulator(nudger, TimeSpan.FromSeconds(60), (cb, every) =>
            {
                seen = every;
                timer = new ManualTimer { Callback = cb };
                return timer;
            }))
            {
                sim.Start();
                Assert.AreEqual(TimeSpan.FromSeconds(60), seen);
                Assert.AreEqual(0, nudger.Count);

                timer.Callback(null);
                timer.Callback(null);
                Assert.AreEqual(2, nudger.Count);

                sim.Stop();
                Assert.IsTrue(timer.Disposed);
                Assert.IsFalse(sim.IsRunning);
            }
        }

        [TestMethod]
        public void Nudger_ThatThrows_DoesNotEscapeTheCallback()
        {
            ManualTimer timer = null;
            using (var sim = new ActivitySimulator(new ThrowingNudger(), TimeSpan.FromSeconds(1), (cb, every) =>
            {
                timer = new ManualTimer { Callback = cb };
                return timer;
            }))
            {
                sim.Start();
                timer.Callback(null);
            }
        }

        private sealed class ThrowingNudger : IInputNudger
        {
            public void Nudge() { throw new InvalidOperationException("blocked"); }
        }

        [TestMethod]
        public void ShouldNudge_OnlyWhenTheUserHasBeenIdle()
        {
            Assert.IsFalse(MouseNudger.ShouldNudge(100000, 99000), "input a second ago");
            Assert.IsFalse(MouseNudger.ShouldNudge(100000, 50001));
            Assert.IsTrue(MouseNudger.ShouldNudge(100000, 50000));
            Assert.IsTrue(MouseNudger.ShouldNudge(100000, 1000));
            Assert.IsFalse(MouseNudger.ShouldNudge(5, uint.MaxValue - 10), "tick counter wrap-around");
        }
    }

    [TestClass]
    public class ThreadPowerHoldReviewTests
    {
        [TestMethod]
        public void Hold_Release_Hold_Again_Works()
        {
            using (var hold = new ThreadPowerHold())
            {
                hold.Hold();
                Assert.IsTrue(hold.IsHeld);
                hold.Release();
                Assert.IsFalse(hold.IsHeld);
                hold.Hold();
                Assert.IsTrue(hold.IsHeld);
                hold.Release();
                Assert.IsFalse(hold.IsHeld);
            }
        }

        [TestMethod]
        public void Dispose_WithoutHold_IsSafe()
        {
            var hold = new ThreadPowerHold();
            hold.Dispose();
            hold.Dispose();
            Assert.IsFalse(hold.IsHeld);
        }

        [TestMethod]
        public void StateBeforeRelease_IncludesContinuous()
        {
            const uint continuous = 0x80000000;
            using (var hold = new ThreadPowerHold())
            {
                hold.Hold();
                hold.Release();
                Assert.IsTrue(hold.StateBeforeRelease.HasValue);
                Assert.AreEqual(continuous, hold.StateBeforeRelease.Value & continuous);
            }
        }

        [TestMethod]
        public void Hold_ReportsSuccess()
        {
            using (var hold = new ThreadPowerHold())
            {
                hold.Hold();
                Assert.IsTrue(hold.LastHoldSucceeded);
                hold.Release();
            }
        }
    }
}
