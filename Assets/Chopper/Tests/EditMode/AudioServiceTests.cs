using Chopper.Presentation.Audio;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class AudioServiceTests
    {
        [Test]
        public void ToDecibels_MapsLinearVolumeToMixerRange()
        {
            Assert.That(AudioService.ToDecibels(1f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(AudioService.ToDecibels(0.5f), Is.EqualTo(-6.02f).Within(0.01f));
            Assert.That(AudioService.ToDecibels(0f), Is.EqualTo(-80f));
        }
    }
}
