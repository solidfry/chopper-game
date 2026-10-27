using Chopper.Simulation.Flight;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Transforms;

namespace Chopper.Tests
{
    public sealed class PredictionSmoothingTests
    {
        private static readonly PredictionSmoothing Settings = new() { MaxDistance = 10f, PositionFraction = 0.25f, RotationFraction = 0.5f };

        [Test]
        public void Apply_RemovesPartOfASmallError()
        {
            var previous = LocalTransform.FromPosition(0f, 0f, 0f);
            var corrected = LocalTransform.FromPosition(4f, 0f, 0f);
            var result = PredictionSmoothing.Apply(previous, corrected, Settings);
            Assert.That(result.Position.x, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Apply_ErrorFadesOverRepeatedCorrections()
        {
            var truth = LocalTransform.FromPosition(4f, 0f, 0f);
            var shown = LocalTransform.FromPosition(0f, 0f, 0f);
            for (var i = 0; i < 20; i++) shown = PredictionSmoothing.Apply(shown, truth, Settings);
            Assert.That(math.distance(shown.Position, truth.Position), Is.LessThan(0.02f));
        }

        [Test]
        public void Apply_BigErrorSnaps()
        {
            var previous = LocalTransform.FromPosition(0f, 0f, 0f);
            var corrected = LocalTransform.FromPosition(50f, 0f, 0f);
            Assert.That(PredictionSmoothing.Apply(previous, corrected, Settings).Position.x, Is.EqualTo(50f));
        }

        [Test]
        public void Apply_NoErrorChangesNothing()
        {
            var pose = LocalTransform.FromPositionRotation(new float3(3f, 2f, 1f), quaternion.RotateY(0.7f));
            var result = PredictionSmoothing.Apply(pose, pose, Settings);
            Assert.That(math.distance(result.Position, pose.Position), Is.LessThan(1e-5f));
            Assert.That(math.abs(math.dot(result.Rotation.value, pose.Rotation.value)), Is.EqualTo(1f).Within(1e-4f));
        }
    }
}
