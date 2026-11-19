using Chopper.Presentation.Hud;
using Chopper.Simulation.Match;
using NUnit.Framework;

namespace Chopper.Tests
{
    /// <summary><see cref="RespawnRule.Delay"/> and the HUD countdown it feeds.</summary>
    public sealed class RespawnRuleTests
    {
        [Test]
        public void Delay_GrowsPerDeath_UpToTheCap()
        {
            var rule = new RespawnRule { Base = 5f, PerDeath = 0.5f, Cap = 7f };
            Assert.That(rule.Delay(0, 30f), Is.EqualTo(5f).Within(1e-4f));
            Assert.That(rule.Delay(2, 30f), Is.EqualTo(6f).Within(1e-4f));
            Assert.That(rule.Delay(50, 30f), Is.EqualTo(7f).Within(1e-4f));
        }

        [Test]
        public void Delay_NoCap_KeepsGrowing()
        {
            var rule = new RespawnRule { Base = 2f, PerDeath = 1f, Cap = 0f };
            Assert.That(rule.Delay(10, 0f), Is.EqualTo(12f).Within(1e-4f));
        }

        [Test]
        public void Delay_WaitsForTheNextWave_NeverLessThanTheDelay()
        {
            var rule = new RespawnRule { Base = 4f, Wave = 15f };
            // Dies at 20 s: earliest 24 s, next wave 30 s.
            Assert.That(rule.Delay(0, 20f), Is.EqualTo(10f).Within(1e-3f));
            // Dies at 26 s: earliest 30 s is exactly a wave.
            Assert.That(rule.Delay(0, 26f), Is.EqualTo(4f).Within(1e-3f));
            // Dies at 27 s: earliest 31 s, next wave 45 s.
            Assert.That(rule.Delay(0, 27f), Is.EqualTo(18f).Within(1e-3f));
        }

        [Test]
        public void Delay_BadInput_NeverNegative()
        {
            var rule = new RespawnRule { Base = -3f, PerDeath = 1f };
            Assert.That(rule.Delay(-5, -10f), Is.EqualTo(0f));
        }

        [Test]
        public void MatchRules_PicksTheRuleOfTheMode()
        {
            var rules = MatchRules.Default;
            Assert.That(rules.Respawn(MatchMode.FreeForAll).Base, Is.EqualTo(RespawnRule.FreeForAll.Base));
            Assert.That(rules.Respawn(MatchMode.TeamDeathmatch).Base, Is.EqualTo(RespawnRule.Team.Base));
        }

        [Test]
        public void Hud_CountsDown_ThenRedeploying_ClearsOnRespawn()
        {
            var model = new MatchHudModel();
            model.SetLocalPlayer(1);
            model.SetMatch(MatchPhase.InProgress, 100f, 0);
            model.AddKill(killerNetworkId: 2, victimNetworkId: 1);

            model.SetRespawnCountdown(3.5f);
            Assert.That(model.RespawnText, Is.EqualTo("REDEPLOY IN 4"));
            model.Tick(1f);
            Assert.That(model.RespawnText, Is.EqualTo("REDEPLOY IN 3"));
            model.Tick(5f);
            Assert.That(model.RespawnText, Is.EqualTo("REDEPLOYING"));

            model.ClearDeathAlert();
            Assert.That(model.RespawnText, Is.Empty);
        }
    }
}
