using Chopper.Presentation.Camera;
using Chopper.Simulation.Combat;
using NUnit.Framework;
using Unity.Collections;

namespace Chopper.Tests
{
    /// <summary>The optics module: zoom steps limited by the gun, the key cycle, field of view and aim speed.</summary>
    public sealed class ZoomTests
    {
        private static FixedList32Bytes<float> Levels(params float[] values)
        {
            var list = new FixedList32Bytes<float>();
            foreach (var value in values) list.Add(value);
            return list;
        }

        [Test]
        public void Steps_AGunWithEnoughZoom_KeepsEveryLevel()
        {
            var steps = ZoomMath.Steps(Levels(3f, 6f, 10f), gunMaxZoom: 10f);
            Assert.That(steps.Length, Is.EqualTo(3));
            Assert.That(steps[2], Is.EqualTo(10f));
        }

        [Test]
        public void Steps_AShortRangeGun_CapsAndDropsLevels()
        {
            // 3x and 6x on a 4x gun: 3x, then 4x. 10x would also be 4x: dropped.
            var steps = ZoomMath.Steps(Levels(3f, 6f, 10f), gunMaxZoom: 4f);
            Assert.That(steps.Length, Is.EqualTo(2));
            Assert.That(steps[0], Is.EqualTo(3f));
            Assert.That(steps[1], Is.EqualTo(4f));

            // A 2x gun gets one step from any scope.
            Assert.That(ZoomMath.Steps(Levels(3f, 6f, 10f), gunMaxZoom: 2f).Length, Is.EqualTo(1));
        }

        [Test]
        public void Steps_AGunThatCannotZoom_GivesNoSteps()
        {
            Assert.That(ZoomMath.Steps(Levels(2f, 4f), gunMaxZoom: 1f).Length, Is.EqualTo(0));
            Assert.That(ZoomMath.Steps(default, gunMaxZoom: 10f).Length, Is.EqualTo(0));
        }

        [Test]
        public void Next_CyclesThroughTheStepsAndBackToNone()
        {
            Assert.That(ZoomMath.Next(0, 3), Is.EqualTo(1));
            Assert.That(ZoomMath.Next(2, 3), Is.EqualTo(3));
            Assert.That(ZoomMath.Next(3, 3), Is.EqualTo(0));
            Assert.That(ZoomMath.Next(0, 0), Is.EqualTo(0));
        }

        [Test]
        public void FieldOfView_NarrowsWithMagnification()
        {
            Assert.That(ZoomMath.FieldOfView(60f, 1f), Is.EqualTo(60f));
            Assert.That(ZoomMath.FieldOfView(60f, 0f), Is.EqualTo(60f));
            // 2x: tan(half) halves. 60 degrees gives about 32.2.
            Assert.That(ZoomMath.FieldOfView(60f, 2f), Is.EqualTo(32.204f).Within(0.01f));
            Assert.That(ZoomMath.FieldOfView(60f, 10f), Is.LessThan(ZoomMath.FieldOfView(60f, 4f)));
        }

        [Test]
        public void AimScale_SlowsTheMouseMoreThanTheAxes()
        {
            Assert.That(ZoomMath.MouseScale(1f), Is.EqualTo(1f));
            Assert.That(ZoomMath.MouseScale(4f), Is.EqualTo(0.25f));
            Assert.That(ZoomMath.AxisScale(4f), Is.EqualTo(0.5f));
            Assert.That(ZoomMath.AxisScale(0f), Is.EqualTo(1f));
        }

        [Test]
        public void ZoomState_TheKeyStepsThroughAndTheTextFollows()
        {
            var zoom = new ZoomState();
            zoom.Cycle();
            Assert.That(zoom.Zoomed, Is.False, "no optics: the key does nothing");

            zoom.SetSteps(Levels(2f, 4f));
            zoom.Cycle();
            Assert.That(zoom.Target, Is.EqualTo(2f));
            Assert.That(zoom.Text, Is.EqualTo("ZOOM 2x"));
            zoom.Cycle();
            Assert.That(zoom.Target, Is.EqualTo(4f));
            zoom.Cycle();
            Assert.That(zoom.Zoomed, Is.False);
            Assert.That(zoom.Text, Is.EqualTo(""));
        }

        [Test]
        public void ZoomState_GlidesToTheStep()
        {
            var zoom = new ZoomState();
            zoom.SetSteps(Levels(4f));
            zoom.Cycle();
            zoom.Tick(1f / 60f);
            Assert.That(zoom.Current, Is.GreaterThan(1f).And.LessThan(4f));
            for (var i = 0; i < 120; i++) zoom.Tick(1f / 60f);
            Assert.That(zoom.Current, Is.EqualTo(4f).Within(0.01f));
        }

        [Test]
        public void ZoomState_ANewLoadoutOrNoChopper_SwitchesTheZoomOff()
        {
            var zoom = new ZoomState();
            zoom.SetSteps(Levels(2f, 4f));
            zoom.Cycle();
            // The same steps again (every frame): the zoom stays.
            zoom.SetSteps(Levels(2f, 4f));
            Assert.That(zoom.Step, Is.EqualTo(1));

            zoom.SetSteps(Levels(3f));
            Assert.That(zoom.Zoomed, Is.False);

            zoom.Cycle();
            zoom.Clear();
            Assert.That(zoom.Zoomed, Is.False);
            Assert.That(zoom.Current, Is.EqualTo(1f));
            Assert.That(zoom.StepCount, Is.EqualTo(0));
        }
    }
}
