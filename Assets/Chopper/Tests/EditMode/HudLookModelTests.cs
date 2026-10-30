using Chopper.Presentation.Hud;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class HudLookModelTests
    {
        private static readonly HudLookProfile Profile = new()
        {
            Glow = 1f, GlowRadius = 2f, Scanlines = 0.3f, ScanlineCount = 400f, Curvature = 0.05f,
            Chromatic = 1f, Vignette = 0.3f, Noise = 0.05f, Flicker = 0.1f,
        };

        private static readonly HudLookReactions Reactions = new()
        {
            FullGlitchDamage = 40f, GlitchDecay = 2f, GlitchChromatic = 4f, LowHullFlicker = 0.2f,
            LockPulse = 0.5f, PulseDecay = 2f, MissileNoise = 0.2f, SpawnGlitch = 0.8f,
            FeedBootHold = 0.5f, FeedBootSeconds = 1f, FeedGlitch = 0.5f,
        };

        private static HudLookModel Make(bool reduceEffects = false)
        {
            var model = new HudLookModel();
            model.Configure(Profile, Reactions, reduceEffects);
            return model;
        }

        [Test]
        public void Damage_GlitchGrowsWithDamage()
        {
            var model = Make();
            model.OnDamage(20f);
            model.Tick(0f);
            Assert.That(model.Glitch, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(model.Chromatic, Is.EqualTo(Profile.Chromatic + 0.5f * Reactions.GlitchChromatic).Within(0.001f));
        }

        [Test]
        public void Damage_GlitchIsCappedAndDecays()
        {
            var model = Make();
            model.OnDamage(400f);
            model.Tick(0f);
            Assert.That(model.Glitch, Is.EqualTo(1f));

            model.Tick(0.25f);
            Assert.That(model.Glitch, Is.EqualTo(0.5f).Within(0.001f));
            model.Tick(1f);
            Assert.That(model.Glitch, Is.EqualTo(0f));
        }

        [Test]
        public void Disrupt_HoldsGlitchForItsDuration()
        {
            var model = Make();
            model.Disrupt(0.8f, seconds: 0.5f);
            model.Tick(0.4f);
            Assert.That(model.Glitch, Is.EqualTo(0.8f).Within(0.001f));

            model.Tick(0.2f); // hold ends
            model.Tick(0.1f);
            Assert.That(model.Glitch, Is.LessThan(0.8f));
        }

        [Test]
        public void Lock_PulsesBrightnessThenFades()
        {
            var model = Make();
            model.OnLockAcquired();
            model.Tick(0f);
            Assert.That(model.Pulse, Is.EqualTo(Reactions.LockPulse).Within(0.001f));

            model.Tick(1f);
            Assert.That(model.Pulse, Is.EqualTo(0f));
        }

        [Test]
        public void Brightness_NeverAboveOneWithoutPulse()
        {
            var model = Make();
            model.LowHull = true;
            for (var i = 0; i < 100; i++)
            {
                model.Tick(1f / 60f);
                Assert.That(model.Brightness, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void ReduceEffects_NoFlickerAndWeakerGlitch()
        {
            var model = Make(reduceEffects: true);
            model.LowHull = true;
            model.OnDamage(40f);
            for (var i = 0; i < 30; i++)
            {
                model.Tick(1f / 60f);
                Assert.That(model.Brightness, Is.EqualTo(1f).Within(0.0001f));
            }

            var full = Make();
            full.OnDamage(40f);
            full.Tick(0f);
            var reduced = Make(reduceEffects: true);
            reduced.OnDamage(40f);
            reduced.Tick(0f);
            Assert.That(reduced.Glitch, Is.EqualTo(full.Glitch * HudLookModel.ReducedScale).Within(0.001f));
        }

        [Test]
        public void MissileUrgency_AddsNoise()
        {
            var calm = Make();
            calm.Tick(0f);
            var threatened = Make();
            threatened.MissileUrgency = 1f;
            threatened.Tick(0f);
            Assert.That(threatened.Noise, Is.EqualTo(calm.Noise + Reactions.MissileNoise).Within(0.001f));
        }

        [Test]
        public void Feed_BootsOnSpawnThenClears()
        {
            var model = Make();
            model.CockpitView = true;
            model.BootFeed();
            model.Tick(0.4f);
            Assert.That(model.Feed, Is.EqualTo(1f).Within(0.001f), "full static during the hold");
            model.Tick(0.6f);
            Assert.That(model.Feed, Is.InRange(0.01f, 0.99f), "clearing");
            for (var i = 0; i < 60; i++) model.Tick(1f / 60f);
            Assert.That(model.Feed, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void Feed_OnlyInCockpitView()
        {
            var model = Make();
            model.BootFeed();
            model.OnDamage(40f);
            model.Tick(0f);
            Assert.That(model.Feed, Is.EqualTo(0f));
            model.CockpitView = true;
            model.Tick(0f);
            Assert.That(model.Feed, Is.GreaterThan(0f));
        }

        [Test]
        public void Feed_HitsBringNoiseBack()
        {
            var model = Make();
            model.CockpitView = true;
            model.OnDamage(40f);
            model.Tick(0f);
            Assert.That(model.Feed, Is.EqualTo(model.Glitch * Reactions.FeedGlitch).Within(0.001f));
        }

        [Test]
        public void Feed_ReduceEffectsWeakensBoot()
        {
            var model = Make(reduceEffects: true);
            model.CockpitView = true;
            model.BootFeed();
            model.Tick(0f);
            Assert.That(model.Feed, Is.EqualTo(HudLookModel.ReducedScale).Within(0.001f));
        }
    }
}
