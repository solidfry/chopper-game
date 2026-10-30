using System.Collections.Generic;
using Chopper.Presentation.Hud;
using NUnit.Framework;
using UnityEngine;

namespace Chopper.Tests
{
    public sealed class ChopperHudModelTests
    {
        [Test]
        public void Update_ConvertsSpeedToWholeKilometresPerHour()
        {
            var model = new ChopperHudModel();
            model.Update(speedMetresPerSecond: 50f, altitudeMetres: 0f, boostEnergy: 1f, boosting: false, boostStartEnergy: 0.2f);
            Assert.That(model.SpeedText, Is.EqualTo("180"));
        }

        [Test]
        public void Update_ClampsAltitudeBelowGroundToZero()
        {
            var model = new ChopperHudModel();
            model.Update(0f, altitudeMetres: -3.4f, 1f, false, 0.2f);
            Assert.That(model.AltitudeText, Is.EqualTo("0"));
        }

        [Test]
        public void Update_BoostTankAndReadyState()
        {
            var model = new ChopperHudModel();

            model.Update(0f, 0f, boostEnergy: 0.1f, boosting: false, boostStartEnergy: 0.2f);
            Assert.That(model.BoostEnergy, Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(model.BoostReady, Is.False);

            // Still boosting on a low tank: ready (the boost keeps going until empty).
            model.Update(0f, 0f, boostEnergy: 0.1f, boosting: true, boostStartEnergy: 0.2f);
            Assert.That(model.Boosting, Is.True);
            Assert.That(model.BoostReady, Is.True);

            model.Update(0f, 0f, boostEnergy: 1f, boosting: false, boostStartEnergy: 0.2f);
            Assert.That(model.BoostEnergy, Is.EqualTo(1f));
            Assert.That(model.BoostReady, Is.True);
        }

        [Test]
        public void Update_RaisesChangesOnlyForValuesThatChanged()
        {
            var model = new ChopperHudModel();
            model.Update(10f, 20f, 1f, false, 0.2f);

            var changed = new List<string>();
            model.propertyChanged += (_, args) => changed.Add(args.propertyName.ToString());

            model.Update(10f, 20f, 1f, false, 0.2f);
            Assert.That(changed, Is.Empty);

            model.Update(20f, 20f, 1f, false, 0.2f);
            Assert.That(changed, Is.EqualTo(new[] { nameof(ChopperHudModel.SpeedText) }));
        }

        [Test]
        public void ClearChopper_HidesHudUntilNextUpdate()
        {
            var model = new ChopperHudModel();
            model.Update(0f, 0f, 1f, false, 0.2f);
            model.ClearChopper();
            Assert.That(model.HasChopper, Is.False);

            model.Update(0f, 0f, 1f, false, 0.2f);
            Assert.That(model.HasChopper, Is.True);
        }

        [Test]
        public void UpdateHealth_RoundsUpAndFlagsLowHealth()
        {
            var model = new ChopperHudModel();
            model.UpdateHealth(current: 24.2f, max: 100f);
            Assert.That(model.HealthText, Is.EqualTo("25"));
            Assert.That(model.HealthFraction, Is.EqualTo(0.24f).Within(0.001f));
            Assert.That(model.HealthLow, Is.True);

            model.UpdateHealth(80f, 100f);
            Assert.That(model.HealthLow, Is.False);
        }

        [Test]
        public void UpdateAim_HidesReticleBehindCamera()
        {
            var model = new ChopperHudModel();
            model.UpdateAim(new Vector3(0.25f, 0.75f, 10f));
            Assert.That(model.AimVisible, Is.True);
            Assert.That(model.AimViewport, Is.EqualTo(new Vector2(0.25f, 0.75f)));

            model.UpdateAim(new Vector3(0.5f, 0.5f, -1f));
            Assert.That(model.AimVisible, Is.False);
        }

        [Test]
        public void ShowHit_MarkersExpireAfterTheirDuration()
        {
            var model = new ChopperHudModel();
            model.ShowHit(killed: true);
            Assert.That(model.HitMarkerVisible, Is.True);
            Assert.That(model.KillMarkerVisible, Is.True);

            model.Tick(ChopperHudModel.HitMarkerDuration + 0.01f);
            Assert.That(model.HitMarkerVisible, Is.False);
            Assert.That(model.KillMarkerVisible, Is.True);

            model.Tick(ChopperHudModel.KillMarkerDuration);
            Assert.That(model.KillMarkerVisible, Is.False);
        }

        [Test]
        public void ShowDamageFrom_ShowsDirectionThenFades()
        {
            var model = new ChopperHudModel();
            model.ShowDamageFrom(-91.6f);
            Assert.That(model.DamageIndicatorVisible, Is.True);
            Assert.That(model.DamageDirectionDegrees, Is.EqualTo(-92f));

            model.Tick(ChopperHudModel.DamageIndicatorDuration + 0.01f);
            Assert.That(model.DamageIndicatorVisible, Is.False);
        }

        [Test]
        public void UpdateAttitude_RoundsRollToHalfDegrees()
        {
            var model = new ChopperHudModel();
            model.UpdateAttitude(rollDegrees: 12.3f, yawRate: 0f, pitchRate: 0f);
            Assert.That(model.RollDegrees, Is.EqualTo(12.5f));
        }

        [Test]
        public void UpdateAttitude_PitchRoundsAndHeadingWraps()
        {
            var model = new ChopperHudModel();
            model.UpdateAttitude(0f, 0f, 0f, pitchDegrees: -7.8f, headingDegrees: -90.4f);
            Assert.That(model.PitchDegrees, Is.EqualTo(-8f));
            Assert.That(model.HeadingDegrees, Is.EqualTo(270f));
            model.UpdateAttitude(0f, 0f, 0f, headingDegrees: 359.7f);
            Assert.That(model.HeadingDegrees, Is.EqualTo(0f));
        }

        [Test]
        public void Sway_LagsBehindTurnsAndStaysWithinLimit()
        {
            var model = new ChopperHudModel();
            model.UpdateAttitude(0f, yawRate: 1000f, pitchRate: -1000f);

            model.Tick(0.016f);
            Assert.That(model.Sway.x, Is.LessThan(0f), "Turning right swings the frame left.");
            Assert.That(model.Sway.x, Is.GreaterThan(-ChopperHudModel.MaxSway), "One frame does not reach the target.");

            for (var i = 0; i < 300; i++) model.Tick(0.016f);
            Assert.That(model.Sway.x, Is.EqualTo(-ChopperHudModel.MaxSway).Within(0.5f));
            Assert.That(model.Sway.y, Is.EqualTo(ChopperHudModel.MaxSway).Within(0.5f));

            model.ClearChopper();
            for (var i = 0; i < 300; i++) model.Tick(0.016f);
            Assert.That(model.Sway.magnitude, Is.LessThan(0.5f), "With no chopper the frame returns to the centre.");
        }
    }
}
