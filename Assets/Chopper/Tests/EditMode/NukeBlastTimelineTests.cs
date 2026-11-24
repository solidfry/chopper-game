using Chopper.Presentation.Effects;
using NUnit.Framework;

namespace Chopper.Tests.EditMode
{
    public class NukeBlastTimelineTests
    {
        [Test]
        public void Cap_RisesFromBurstToTop()
        {
            Assert.AreEqual(30f, NukeBlastTimeline.CapHeight(0f, 30f, 230f, 9f), 0.01f);
            Assert.Greater(NukeBlastTimeline.CapHeight(9f, 30f, 230f, 9f), 215f);
            Assert.LessOrEqual(NukeBlastTimeline.CapHeight(60f, 30f, 230f, 9f), 230f);
        }

        [Test]
        public void Cap_IsWideWithinHalfASecond()
        {
            Assert.Greater(NukeBlastTimeline.CapRadius(0.5f), NukeBlastTimeline.BaseRadius * 0.85f);
            Assert.Greater(NukeBlastTimeline.CapSpread(3f), 1.3f);
        }

        [Test]
        public void Stem_AlwaysReachesTheCap()
        {
            for (var t = 0f; t < 20f; t += 0.5f)
            {
                var y = NukeBlastTimeline.CapHeight(t, 30f, 230f, 9f);
                var r = NukeBlastTimeline.CapRadius(t);
                Assert.AreEqual(y - r * 0.4f, NukeBlastTimeline.StemHeight(y, r), 0.01f, $"t {t}");
            }
        }

        [Test]
        public void Rings_ShowThenEnd()
        {
            Assert.IsFalse(NukeBlastTimeline.Ring(0, 0f, out _, out _));
            Assert.IsTrue(NukeBlastTimeline.Ring(0, 1f, out var radius, out _));
            Assert.Greater(radius, NukeBlastTimeline.BaseRadius);
            Assert.IsFalse(NukeBlastTimeline.Ring(0, 10f, out _, out _));
        }

        [Test]
        public void Cloud_CoolsAndFades()
        {
            Assert.AreEqual(1f, NukeBlastTimeline.CapHeat(0.2f));
            Assert.Less(NukeBlastTimeline.CapHeat(15f), 0.3f);
            Assert.AreEqual(0f, NukeBlastTimeline.Fade(10f, 22f, 5f));
            Assert.AreEqual(1f, NukeBlastTimeline.Fade(30f, 22f, 5f));
        }

        [Test]
        public void Shock_ArrivesAtTheSpeedOfSound()
        {
            Assert.AreEqual(1f, NukeBlastTimeline.ShockArrival(340f, 340f), 0.001f);
        }
    }
}
