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
            model.Update(speedMetresPerSecond: 50f, altitudeMetres: 0f, dashCooldownRemaining: 0f, dashCooldown: 2f);
            Assert.That(model.SpeedText, Is.EqualTo("180"));
        }

        [Test]
        public void Update_ClampsAltitudeBelowGroundToZero()
        {
            var model = new ChopperHudModel();
            model.Update(0f, altitudeMetres: -3.4f, 0f, 2f);
            Assert.That(model.AltitudeText, Is.EqualTo("0"));
        }

        [Test]
        public void Update_DashChargeFillsAsCooldownRunsOut()
        {
            var model = new ChopperHudModel();

            model.Update(0f, 0f, dashCooldownRemaining: 1.5f, dashCooldown: 2f);
            Assert.That(model.DashCharge, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(model.DashReady, Is.False);

            model.Update(0f, 0f, dashCooldownRemaining: 0f, dashCooldown: 2f);
            Assert.That(model.DashCharge, Is.EqualTo(1f));
            Assert.That(model.DashReady, Is.True);
        }

        [Test]
        public void Update_RaisesChangesOnlyForValuesThatChanged()
        {
            var model = new ChopperHudModel();
            model.Update(10f, 20f, 0f, 2f);

            var changed = new List<string>();
            model.propertyChanged += (_, args) => changed.Add(args.propertyName.ToString());

            model.Update(10f, 20f, 0f, 2f);
            Assert.That(changed, Is.Empty);

            model.Update(20f, 20f, 0f, 2f);
            Assert.That(changed, Is.EqualTo(new[] { nameof(ChopperHudModel.SpeedText) }));
        }

        [Test]
        public void ClearChopper_HidesHudUntilNextUpdate()
        {
            var model = new ChopperHudModel();
            model.Update(0f, 0f, 0f, 2f);
            model.ClearChopper();
            Assert.That(model.HasChopper, Is.False);

            model.Update(0f, 0f, 0f, 2f);
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
    }
}
