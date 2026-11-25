using Chopper.Presentation.Audio;
using NUnit.Framework;

namespace Chopper.Tests.EditMode
{
    public class AudioDistanceTests
    {
        [Test]
        public void Air_OpenNear_DullFar()
        {
            Assert.AreEqual(AudioService.OpenCutoff, AudioDistance.AirCutoff(10f, 40f, 1200f, 2500f), 1f);
            Assert.AreEqual(2500f, AudioDistance.AirCutoff(5000f, 40f, 1200f, 2500f), 1f);
            var mid = AudioDistance.AirCutoff(300f, 40f, 1200f, 2500f);
            Assert.Less(mid, AudioService.OpenCutoff);
            Assert.Greater(mid, 2500f);
        }

        [Test]
        public void Air_GetsDullerWithDistance()
        {
            var last = float.MaxValue;
            for (var d = 50f; d <= 1200f; d += 50f)
            {
                var cutoff = AudioDistance.AirCutoff(d, 40f, 1200f, 2500f);
                Assert.Less(cutoff, last, $"d {d}");
                last = cutoff;
            }
        }

        [Test]
        public void Reverb_WetterFar()
        {
            Assert.AreEqual(0.5f, AudioDistance.ReverbMix(10f, 40f, 1200f, 0.5f, 1f), 0.001f);
            Assert.AreEqual(1f, AudioDistance.ReverbMix(2000f, 40f, 1200f, 0.5f, 1f), 0.001f);
        }

        [Test]
        public void Share_BadRangeDoesNotBreak()
        {
            Assert.AreEqual(0f, AudioDistance.Share(10f, 50f, 50f));
            Assert.AreEqual(1f, AudioDistance.Share(60f, 50f, 50f));
        }
    }
}
