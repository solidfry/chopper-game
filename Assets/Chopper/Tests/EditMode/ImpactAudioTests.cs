using Chopper.Presentation.Audio;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class ImpactAudioTests
    {
        [Test]
        public void CrashVolume_GrowsWithTheShareOfTheHull()
        {
            Assert.That(CrashAudioSystem.Volume(0f, 100f), Is.EqualTo(0.35f).Within(1e-4f), "a scrape still makes a sound");
            Assert.That(CrashAudioSystem.Volume(10f, 100f), Is.EqualTo(0.675f).Within(1e-4f));
            Assert.That(CrashAudioSystem.Volume(20f, 100f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(CrashAudioSystem.Volume(80f, 100f), Is.EqualTo(1f), "never louder than the cue");
            Assert.That(CrashAudioSystem.Volume(5f, 0f), Is.EqualTo(1f), "no max health: full");
        }
    }
}
