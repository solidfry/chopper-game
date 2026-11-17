using Chopper.Simulation.Combat;
using NUnit.Framework;
using Unity.Mathematics;

namespace Chopper.Tests
{
    public sealed class FlareFlightTests
    {
        [Test]
        public void Position_StartsAtOriginAndMatchesStepByStepFlight()
        {
            var origin = new float3(10f, 50f, -4f);
            var velocity = new float3(20f, 3f, -8f);
            Assert.That(math.distance(FlareFlight.Position(origin, velocity, 0f), origin), Is.LessThan(1e-4f));

            // The same flight the flare visuals step frame by frame.
            var position = origin;
            var v = velocity;
            const float dt = 1f / 600f;
            for (var t = 0f; t < 3f; t += dt)
            {
                v += new float3(0f, -FlareFlight.Gravity, 0f) * dt;
                v *= math.exp(-FlareFlight.Drag * dt);
                position += v * dt;
            }
            Assert.That(math.distance(FlareFlight.Position(origin, velocity, 3f), position), Is.LessThan(0.2f));
        }

        [Test]
        public void Drop_BurnsForTheDecoyTimeThenStops()
        {
            var state = default(CountermeasureState);
            FlareFlight.Drop(ref state, new float3(0f, 100f, 0f), quaternion.identity, new float3(30f, 0f, 0f), 3.5f);
            Assert.That(state.FlaresInAir, Is.True);
            for (var i = 0; i < 60 * 3; i++) FlareFlight.Tick(ref state, 1f / 60f);
            Assert.That(state.FlaresInAir, Is.True, "still burning at 3 s");
            for (var i = 0; i < 60; i++) FlareFlight.Tick(ref state, 1f / 60f);
            Assert.That(state.FlaresInAir, Is.False, "burnt out after 3.5 s");
            // The cloud drifts behind the chopper and sinks slowly: it hangs in the air.
            var at = FlareFlight.Position(state);
            Assert.That(at.y, Is.GreaterThan(90f));
        }
    }
}
