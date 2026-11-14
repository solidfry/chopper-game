using Chopper.Simulation.Visuals;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class MatchConditionsTests
    {
        [Test]
        public void Mask_SetsOneBitPerValue()
        {
            Assert.That(Conditions.Mask(new[] { TimeOfDay.Morning, TimeOfDay.Night }), Is.EqualTo(0b1001));
            Assert.That(Conditions.Mask(new[] { Weather.Rain, Weather.Fog }), Is.EqualTo(0b1010));
            Assert.That(Conditions.Mask((TimeOfDay[])null), Is.EqualTo(0));
        }

        [Test]
        public void Pick_ReturnsOnlyAllowedValues_SpreadByRoll_AndFallsBackOnEmpty()
        {
            var mask = Conditions.Mask(new[] { TimeOfDay.Day, TimeOfDay.Night });
            Assert.That(Conditions.PickTime(mask, 0f), Is.EqualTo(TimeOfDay.Day));
            Assert.That(Conditions.PickTime(mask, 0.49f), Is.EqualTo(TimeOfDay.Day));
            Assert.That(Conditions.PickTime(mask, 0.5f), Is.EqualTo(TimeOfDay.Night));
            Assert.That(Conditions.PickTime(mask, 1f), Is.EqualTo(TimeOfDay.Night), "roll 1 stays in range");
            Assert.That(Conditions.PickTime(0, 0.7f), Is.EqualTo(Conditions.DefaultTime));
            Assert.That(Conditions.PickWeather(0, 0.3f), Is.EqualTo(Conditions.DefaultWeather));
        }
    }
}
