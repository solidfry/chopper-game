using Chopper.Presentation.Hud;
using Chopper.Simulation.Combat;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class AbilityTests
    {
        private static AbilityConfig Config(AbilityKind kind, float cooldown = 30f, float duration = 4f, float strength = 1f) =>
            new() { Kind = kind, Cooldown = cooldown, Duration = duration, Strength = strength, Radius = 150f };

        [Test]
        public void Activate_StartsOnce_ThenCoolsDown()
        {
            var config = Config(AbilityKind.Overclock);
            var state = new AbilityState();
            Assert.That(Abilities.TryActivate(ref state, config), Is.True);
            Assert.That(state.Active, Is.True);
            Assert.That(state.Uses, Is.EqualTo(1));
            Assert.That(Abilities.TryActivate(ref state, config), Is.False, "not again while on or cooling down");

            for (var i = 0; i < 41; i++) Abilities.Tick(ref state, 0.1f);
            Assert.That(state.Active, Is.False);
            Assert.That(Abilities.TryActivate(ref state, config), Is.False, "still cooling down");
            for (var i = 0; i < 270; i++) Abilities.Tick(ref state, 0.1f);
            Assert.That(Abilities.TryActivate(ref state, config), Is.True);
            Assert.That(state.Uses, Is.EqualTo(2));
        }

        [Test]
        public void NoAbility_NeverActivates()
        {
            var state = new AbilityState();
            Assert.That(Abilities.TryActivate(ref state, default), Is.False);
        }

        [Test]
        public void Aegis_TakesTheNextDamage_AndEndsWithTheAbility()
        {
            var config = Config(AbilityKind.Aegis, strength: 50f);
            var state = new AbilityState();
            Abilities.TryActivate(ref state, config);
            Assert.That(Abilities.Absorb(ref state, 30f), Is.EqualTo(0f));
            Assert.That(Abilities.Absorb(ref state, 30f), Is.EqualTo(10f).Within(1e-4f), "20 left in the shield");
            Assert.That(Abilities.Absorb(ref state, 30f), Is.EqualTo(30f));

            for (var i = 0; i < 50; i++) Abilities.Tick(ref state, 0.1f);
            Assert.That(state.Shield, Is.EqualTo(0f), "the shield ends when the ability ends");
        }

        [Test]
        public void Effects_OnlyWhileOn_AndOnlyForTheirKind()
        {
            var state = new AbilityState { ActiveLeft = 1f };
            Assert.That(Abilities.EndlessBoost(Config(AbilityKind.Overclock), state), Is.True);
            Assert.That(Abilities.EndlessBoost(Config(AbilityKind.BatteringRam), state), Is.True);
            Assert.That(Abilities.EndlessBoost(Config(AbilityKind.Aegis), state), Is.False);
            Assert.That(Abilities.CrashImmune(Config(AbilityKind.BatteringRam), state), Is.True);
            Assert.That(Abilities.ShowsAllEnemies(Config(AbilityKind.Ping), state), Is.True);
            Assert.That(Abilities.DotScale(Config(AbilityKind.VenomBelt, strength: 2f), state, DamageKind.Bullet), Is.EqualTo(2f));
            Assert.That(Abilities.DotScale(Config(AbilityKind.VenomBelt, strength: 2f), state, DamageKind.Explosion), Is.EqualTo(1f));
            Assert.That(Abilities.HealPerSecond(Config(AbilityKind.NaniteCloud, duration: 5f, strength: 0.3f), state, 100f),
                Is.EqualTo(6f).Within(1e-4f));

            var off = new AbilityState();
            Assert.That(Abilities.EndlessBoost(Config(AbilityKind.Overclock), off), Is.False);
            Assert.That(Abilities.HealPerSecond(Config(AbilityKind.NaniteCloud, duration: 5f, strength: 0.3f), off, 100f), Is.EqualTo(0f));
        }

        [Test]
        public void Hud_ShowsReady_On_AndCooldown()
        {
            var model = new ChopperHudModel();
            model.UpdateAbility("", 0f, 0f, 0f);
            Assert.That(model.Ability, Is.EqualTo(HudAbility.Hidden));

            model.UpdateAbility("AEGIS", 0f, 0f, 0f);
            Assert.That(model.AbilityText, Is.EqualTo("[F] AEGIS"));
            model.UpdateAbility("AEGIS", 2.41f, 29f, 0f);
            Assert.That(model.Ability, Is.EqualTo(HudAbility.Active));
            Assert.That(model.AbilityText, Is.EqualTo("AEGIS 2.5"));
            model.UpdateAbility("AEGIS", 0f, 11.2f, 1f);
            Assert.That(model.AbilityText, Is.EqualTo("AEGIS 12"));
            Assert.That(model.Jammed, Is.True);
        }
    }
}
