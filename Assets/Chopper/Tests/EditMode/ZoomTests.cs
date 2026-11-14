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
        public void Signal_Thermal_FollowsHeat()
        {
            // A chopper at rest (heat 1) is half bright; a hot one (boosting, a missile) is full.
            Assert.That(VisionMath.Signal(VisionMode.Thermal, heat: 1f, speed: 0f, distance: 100f, range: 0f), Is.EqualTo(0.5f));
            Assert.That(VisionMath.Signal(VisionMode.Thermal, VisionMath.MissileHeat, 0f, 100f, 0f), Is.EqualTo(1f));
            // A cool engine (pilot trait) is dimmer.
            Assert.That(VisionMath.Signal(VisionMode.Thermal, 0.4f, 0f, 100f, 0f), Is.EqualTo(0.2f).Within(1e-4f));
        }

        [Test]
        public void Signal_Motion_DoesNotSeeAHover()
        {
            Assert.That(VisionMath.Signal(VisionMode.Motion, heat: 3f, speed: 0f, distance: 100f, range: 0f), Is.EqualTo(0f));
            Assert.That(VisionMath.Signal(VisionMode.Motion, 3f, VisionMath.MotionMinSpeed, 100f, 0f), Is.EqualTo(0f));
            Assert.That(VisionMath.Signal(VisionMode.Motion, 0f, 23f, 100f, 0f), Is.EqualTo(0.5f));
            Assert.That(VisionMath.Signal(VisionMode.Motion, 0f, 80f, 100f, 0f), Is.EqualTo(1f));
        }

        [Test]
        public void Signal_Range_CutsAndFadesAtTheEdge()
        {
            Assert.That(VisionMath.Signal(VisionMode.DeepScan, 0f, 0f, distance: 100f, range: 250f), Is.EqualTo(1f));
            Assert.That(VisionMath.Signal(VisionMode.DeepScan, 0f, 0f, 250f, 250f), Is.EqualTo(0f));
            Assert.That(VisionMath.Signal(VisionMode.DeepScan, 0f, 0f, 400f, 250f), Is.EqualTo(0f));
            // In the last 15% of the range it fades.
            var edge = VisionMath.Signal(VisionMode.DeepScan, 0f, 0f, 240f, 250f);
            Assert.That(edge, Is.GreaterThan(0f).And.LessThan(1f));
        }

        [Test]
        public void Signal_NightAndNone_MarkNothing()
        {
            Assert.That(VisionMath.Signal(VisionMode.Night, 3f, 80f, 10f, 0f), Is.EqualTo(0f));
            Assert.That(VisionMath.Signal(VisionMode.None, 3f, 80f, 10f, 0f), Is.EqualTo(0f));
            Assert.That(VisionMath.MarksTargets(VisionMode.Night), Is.False);
            Assert.That(VisionMath.MarksTargets(VisionMode.Thermal), Is.True);
        }

        [Test]
        public void ZoomState_TheVisionKeyTogglesTheViewAndTheTextShowsBoth()
        {
            var zoom = new ZoomState();
            zoom.ToggleVision();
            Assert.That(zoom.VisionOn, Is.False, "no special view: the key does nothing");

            zoom.SetOptics(new OpticsConfig { Steps = Levels(2f, 4f), Vision = VisionMode.Thermal, VisionRange = 1200f });
            Assert.That(zoom.VisionOn, Is.False);
            zoom.ToggleVision();
            Assert.That(zoom.VisionOn, Is.True);
            Assert.That(zoom.Text, Is.EqualTo("THERMAL"));
            Assert.That(zoom.VisionRange, Is.EqualTo(1200f));
            zoom.Cycle();
            Assert.That(zoom.Text, Is.EqualTo("THERMAL  ZOOM 2x"));

            zoom.ToggleVision();
            Assert.That(zoom.Text, Is.EqualTo("ZOOM 2x"));
        }

        [Test]
        public void ZoomState_TheViewFadesInAndOut()
        {
            var zoom = new ZoomState();
            zoom.SetOptics(new OpticsConfig { Vision = VisionMode.Night });
            Assert.That(zoom.ShownVision, Is.EqualTo(VisionMode.None));

            zoom.ToggleVision();
            zoom.Tick(0.1f);
            Assert.That(zoom.ShownVision, Is.EqualTo(VisionMode.Night));
            Assert.That(zoom.VisionBlend, Is.GreaterThan(0f).And.LessThan(1f));
            zoom.Tick(1f);
            Assert.That(zoom.VisionBlend, Is.EqualTo(1f));

            // Off: the mode stays on screen until the fade ends.
            zoom.ToggleVision();
            zoom.Tick(0.1f);
            Assert.That(zoom.ShownVision, Is.EqualTo(VisionMode.Night));
            zoom.Tick(1f);
            Assert.That(zoom.ShownVision, Is.EqualTo(VisionMode.None));
        }

        [Test]
        public void ZoomState_OtherOptics_SwitchTheViewOff()
        {
            var zoom = new ZoomState();
            zoom.SetOptics(new OpticsConfig { Vision = VisionMode.Thermal });
            zoom.ToggleVision();
            // The same optics again (every frame): the view stays.
            zoom.SetOptics(new OpticsConfig { Vision = VisionMode.Thermal });
            Assert.That(zoom.VisionOn, Is.True);

            zoom.SetOptics(new OpticsConfig { Vision = VisionMode.DeepScan, VisionRange = 250f });
            Assert.That(zoom.VisionOn, Is.False);
            Assert.That(zoom.Text, Is.EqualTo(""));
        }

        [Test]
        public void VisionLook_EveryModeHasABuiltInLook_AndOnlyMarkingModesGlow()
        {
            foreach (VisionMode mode in System.Enum.GetValues(typeof(VisionMode)))
            {
                if (mode == VisionMode.None) continue;
                var look = VisionLook.Default(mode);
                Assert.That(look.Mode, Is.EqualTo(mode));
                Assert.That(look.GlowColor.maxColorComponent > 0f, Is.EqualTo(VisionMath.MarksTargets(mode)), mode.ToString());
            }
            Assert.That(VisionLook.Default(VisionMode.DeepScan).ThroughWalls, Is.True);
            Assert.That(VisionLook.Default(VisionMode.Thermal).ThroughWalls, Is.False);
            // A far target stays a visible dot.
            Assert.That(VisionLook.Default(VisionMode.Thermal).GlowSizeAt(1000f), Is.EqualTo(12f).Within(1e-3f));
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
