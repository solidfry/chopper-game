using Chopper.Presentation.Hud;
using NUnit.Framework;
using UnityEngine;

namespace Chopper.Tests
{
    public sealed class HudOpacityTests
    {
        private float _saved;
        private bool _hadSaved;

        // The test writes the player's real saved value: keep it and put it back.
        [SetUp]
        public void SetUp()
        {
            var key = HudOpacity.Key(HudGroup.Weapons);
            _hadSaved = PlayerPrefs.HasKey(key);
            _saved = PlayerPrefs.GetFloat(key, 1f);
            PlayerPrefs.DeleteKey(key);
        }

        [TearDown]
        public void TearDown()
        {
            var key = HudOpacity.Key(HudGroup.Weapons);
            if (_hadSaved) PlayerPrefs.SetFloat(key, _saved);
            else PlayerPrefs.DeleteKey(key);
        }

        [Test]
        public void StartsSolid_ClampsAndTellsTheView()
        {
            var opacity = new HudOpacity();
            Assert.That(opacity.Get(HudGroup.Weapons), Is.EqualTo(1f));

            var changes = 0;
            opacity.Changed += () => changes++;
            opacity.Set(HudGroup.Weapons, 0.4f);
            Assert.That(opacity.Get(HudGroup.Weapons), Is.EqualTo(0.4f));
            Assert.That(changes, Is.EqualTo(1));

            opacity.Set(HudGroup.Weapons, 0.4f);
            Assert.That(changes, Is.EqualTo(1), "same value: no event");

            opacity.Set(HudGroup.Weapons, -2f);
            Assert.That(opacity.Get(HudGroup.Weapons), Is.EqualTo(0f));
            opacity.Set(HudGroup.Weapons, 2f);
            Assert.That(opacity.Get(HudGroup.Weapons), Is.EqualTo(1f));
        }

        [Test]
        public void IsSaved()
        {
            new HudOpacity().Set(HudGroup.Weapons, 0.25f);
            Assert.That(new HudOpacity().Get(HudGroup.Weapons), Is.EqualTo(0.25f));
        }
    }
}
