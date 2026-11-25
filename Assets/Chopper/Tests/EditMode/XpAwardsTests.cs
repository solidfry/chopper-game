using Chopper.Presentation.Hud;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Match;
using Chopper.Simulation.Progression;
using Chopper.Simulation.Session;
using NUnit.Framework;
using Unity.Collections;

namespace Chopper.Tests
{
    /// <summary>In-match XP awards (<see cref="XpAwards"/>) and the HUD popup that shows them (<see cref="MatchHudModel"/>).</summary>
    public sealed class XpAwardsTests
    {
        private static readonly XpRates Rates = XpRates.Default;

        private static MatchLogEntry Hit(int actor, int target, float amount, double time) =>
            new() { Kind = MatchLogKind.Hit, Actor = actor, Target = target, Amount = amount, Time = time };

        private static MatchLogEntry Kill(int actor, int target, double time) =>
            new() { Kind = MatchLogKind.Kill, Actor = actor, Target = target, Time = time };

        private static void Run(NativeList<DamageShare> shares, NativeList<XpAward> awards, params MatchLogEntry[] entries)
        {
            foreach (var entry in entries) XpAwards.Collect(entry, shares, Rates, awards);
        }

        [Test]
        public void Kill_GivesKillXp_WithTheKillersDamageOnThatVictim()
        {
            using var shares = new NativeList<DamageShare>(Allocator.Temp);
            using var awards = new NativeList<XpAward>(Allocator.Temp);
            Run(shares, awards, Hit(1, 2, 60f, 0), Hit(1, 2, 40f, 1), Kill(1, 2, 1));

            Assert.That(awards.Length, Is.EqualTo(1));
            Assert.That(awards[0].Player, Is.EqualTo(1));
            Assert.That(awards[0].Reason, Is.EqualTo(XpReason.Kill));
            Assert.That(awards[0].Amount, Is.EqualTo(Rates.PerKill));
            Assert.That(awards[0].Other, Is.EqualTo(2));
            Assert.That(awards[0].DamageXp, Is.EqualTo(20)); // 100 damage × 0.2
            Assert.That(shares.Length, Is.EqualTo(0), "the victim's next life starts clean");
        }

        [Test]
        public void Assist_NeedsEnoughOfTheDamage_AndARecentHit()
        {
            using var shares = new NativeList<DamageShare>(Allocator.Temp);
            using var awards = new NativeList<XpAward>(Allocator.Temp);
            // 150 damage in all. Player 3: 27 %, 2 s before the kill (assist). Player 4: 7 % (too little).
            // Player 5: 33 %, but 20 s before the kill (too old).
            Run(shares, awards,
                Hit(5, 2, 50f, 0),
                Hit(3, 2, 40f, 18), Hit(4, 2, 10f, 19), Hit(1, 2, 50f, 20),
                Kill(1, 2, 20));

            var assists = 0;
            foreach (var award in awards)
            {
                if (award.Reason != XpReason.Assist) continue;
                assists++;
                Assert.That(award.Player, Is.EqualTo(3));
                Assert.That(award.Amount, Is.EqualTo(Rates.PerAssist));
            }
            Assert.That(assists, Is.EqualTo(1));
        }

        [Test]
        public void BotVictims_GiveThePartXp()
        {
            using var shares = new NativeList<DamageShare>(Allocator.Temp);
            using var awards = new NativeList<XpAward>(Allocator.Temp);
            var bot = Chopper.Simulation.Bots.BotIds.First;
            Run(shares, awards, Hit(3, bot, 50f, 0), Hit(1, bot, 50f, 1), Kill(1, bot, 1));

            Assert.That(awards[0].Reason, Is.EqualTo(XpReason.BotKill));
            Assert.That(awards[0].Amount, Is.EqualTo(50));
            Assert.That(awards[1].Reason, Is.EqualTo(XpReason.Assist));
            Assert.That(awards[1].Amount, Is.EqualTo(25));
        }

        [Test]
        public void RepairAndEvadedMissiles_GiveXp_AndCountIntoTheStats()
        {
            using var shares = new NativeList<DamageShare>(Allocator.Temp);
            using var awards = new NativeList<XpAward>(Allocator.Temp);
            Run(shares, awards,
                new MatchLogEntry { Kind = MatchLogKind.Repair, Actor = 1, Amount = 120f },
                new MatchLogEntry { Kind = MatchLogKind.MissileDecoyed, Actor = 2, Target = 1 },
                new MatchLogEntry { Kind = MatchLogKind.MissileEnd, Actor = 2, Target = 1, Detail = (byte)MissileOutcome.Expired },
                new MatchLogEntry { Kind = MatchLogKind.MissileEnd, Actor = 2, Target = 1, Detail = (byte)MissileOutcome.Impact });

            Assert.That(awards.Length, Is.EqualTo(3));
            var stats = new PlayerMatchStats();
            foreach (var award in awards) XpAwards.Count(award, ref stats);
            Assert.That(stats.RepairXp, Is.EqualTo(12));
            Assert.That(stats.Evades, Is.EqualTo(2));
            Assert.That(stats.EvadeXp, Is.EqualTo(2 * Rates.PerMissileEvaded));
        }

        [Test]
        public void Turrets_DamageCountsAsDamageXp_AndTheDestroyerGetsTurretXp()
        {
            using var shares = new NativeList<DamageShare>(Allocator.Temp);
            using var awards = new NativeList<XpAward>(Allocator.Temp);
            // Two pilots shoot a turret (target 0: not a player); pilot 1 destroys it.
            var hitA = new MatchLogEntry { Kind = MatchLogKind.Hit, Actor = 1, Target = 0, Amount = 40f, Damage = DamageKind.Bullet };
            var hitB = new MatchLogEntry { Kind = MatchLogKind.Hit, Actor = 2, Target = 0, Amount = 20f, Damage = DamageKind.Bullet, Detail = 1 };
            var down = new MatchLogEntry { Kind = MatchLogKind.TurretDown, Actor = 2, Target = 0 };
            Run(shares, awards, hitA, hitB, down);

            Assert.That(awards.Length, Is.EqualTo(1));
            Assert.That(awards[0].Player, Is.EqualTo(2));
            Assert.That(awards[0].Reason, Is.EqualTo(XpReason.TurretDown));
            Assert.That(awards[0].Amount, Is.EqualTo(Rates.PerTurret));
            Assert.That(shares.Length, Is.EqualTo(0), "No assist shares on a turret.");

            // Damage on the turret is damage dealt: both pilots get damage XP for it on the end screen.
            PlayerStats a = default, b = default, none = default;
            var firstBlood = false;
            MatchStatsRules.Apply(hitA, ref a, true, ref none, false, ref firstBlood);
            MatchStatsRules.Apply(hitB, ref b, true, ref none, false, ref firstBlood);
            Assert.That(a.Value.DamageDealt, Is.EqualTo(40f));
            Assert.That(b.Value.DamageDealt, Is.EqualTo(20f));

            var stats = b.Value;
            XpAwards.Count(awards[0], ref stats);
            Assert.That(stats.Turrets, Is.EqualTo(1));
            Assert.That(stats.TurretXp, Is.EqualTo(Rates.PerTurret));
        }

        [Test]
        public void Popup_StacksAComboAndKeepsTheBestTitle_ThenFades()
        {
            var model = new MatchHudModel();
            model.AddXp(XpReason.Kill, 100, 7, 24);
            model.AddXp(XpReason.Repair, 12, 0, 0);

            Assert.That(model.XpVisible, Is.True);
            Assert.That(model.XpBig, Is.True);
            Assert.That(model.XpTitle, Is.EqualTo("ENEMY DOWN"));
            Assert.That(model.XpLinesText.Split('\n'), Has.Length.EqualTo(3));

            for (var i = 0; i < 60; i++) model.Tick(1f / 60f);
            Assert.That(model.XpTotalText, Is.EqualTo("+136"));

            model.Tick(MatchHudModel.XpPopupSeconds);
            Assert.That(model.XpVisible, Is.False);
        }
    }
}
