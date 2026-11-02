using System;
using KeepAwake.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeepAwake.Tests
{
    [TestClass]
    public class PowerHoldTests
    {
        [TestMethod]
        public void Hold_SetsSystemAndDisplayRequired_OnItsThread()
        {
            using (var hold = new ThreadPowerHold())
            {
                hold.Hold();
                Assert.IsTrue(hold.IsHeld);
                hold.Release();
                Assert.IsFalse(hold.IsHeld);
                Assert.IsTrue(hold.StateBeforeRelease.HasValue, "release did not run on the hold thread");
                uint s = hold.StateBeforeRelease.Value;
                const uint systemRequired = 0x1, displayRequired = 0x2;
                Assert.AreEqual(systemRequired, s & systemRequired, "ES_SYSTEM_REQUIRED was not in effect");
                Assert.AreEqual(displayRequired, s & displayRequired, "ES_DISPLAY_REQUIRED was not in effect");
            }
        }

        [TestMethod]
        public void Hold_IsIdempotent_AndReleaseWithoutHoldIsSafe()
        {
            using (var hold = new ThreadPowerHold())
            {
                hold.Release();
                hold.Hold();
                hold.Hold();
                Assert.IsTrue(hold.IsHeld);
                hold.Release();
                hold.Release();
                Assert.IsFalse(hold.IsHeld);
            }
        }
    }
}
