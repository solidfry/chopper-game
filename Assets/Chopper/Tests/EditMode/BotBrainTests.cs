using Chopper.Simulation.Bots;
using Chopper.Simulation.Combat;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Chopper.Tests
{
    /// <summary><see cref="BotBrain"/> decisions from hand-made situations (no world).</summary>
    public sealed class BotBrainTests
    {
        private static readonly Entity Enemy = new() { Index = 5, Version = 1 };

        private static BotHandling Handling()
        {
            var handling = new BotPilotTuning().ToHandling();
            var skill = new BotSkillTuning { MistakeChance = 0f, AimErrorDegrees = 0f };
            skill.Apply(ref handling);
            return handling;
        }

        private static BotSituation Self() => new()
        {
            Position = new float3(0f, 60f, 0f),
            Forward = math.forward(),
            Health01 = 1f,
            HasGun = true,
            GunRange = 400f,
            RocketPylon = -1,
            SeekerPylon = -1,
            MissileTimeToImpact = float.MaxValue,
        };

        private static NativeArray<BotContact> Contacts(params BotContact[] contacts) => new(contacts, Allocator.Temp);

        private static BotContact Ahead(float distance) => new()
        {
            Entity = Enemy, Position = new float3(0f, 60f, distance), Health01 = 1f, Distance = distance, InSight = true,
        };

        private static BotIntent Think(ref BotBrain brain, in BotSituation s, NativeArray<BotContact> contacts, out PilotOrder order,
            int frames = 1)
        {
            order = default;
            var intent = default(BotIntent);
            for (var i = 0; i < frames; i++)
                brain.Think(s, contacts, Handling(), i * 0.1, 0.1f, ref order, out intent);
            return intent;
        }

        [Test]
        public void IncomingMissile_Evades_AndDropsFlares()
        {
            var brain = BotBrain.Create(1);
            var s = Self();
            s.MissileIncoming = true;
            s.MissileTimeToImpact = 0.8f;
            s.MissileDirection = new float3(1f, 0f, 0f);
            s.FlaresReady = true;
            var contacts = Contacts(Ahead(500f));

            var intent = Think(ref brain, s, contacts, out _);

            Assert.AreEqual(BotMode.Evade, brain.Mode);
            Assert.IsTrue(intent.Actions.Countermeasure, "no flares");
            contacts.Dispose();
        }

        [Test]
        public void EnemyAhead_InRange_AimsAndFires()
        {
            var brain = BotBrain.Create(2);
            var s = Self();
            var contacts = Contacts(Ahead(200f));

            // A few frames: the perceived position settles on the real one.
            Think(ref brain, s, contacts, out var order, frames: 10);

            Assert.AreEqual(BotMode.Attack, brain.Mode);
            Assert.IsTrue(order.Aim, "not aiming");
            Assert.IsTrue(order.Fire, "not firing");
            contacts.Dispose();
        }

        [Test]
        public void EnemyBehindBuildings_Approaches_OnAPath_WithoutFiring()
        {
            var brain = BotBrain.Create(3);
            var s = Self();
            var hidden = Ahead(200f);
            hidden.InSight = false;
            hidden.Distance = 200f; // inside hearing range
            var contacts = Contacts(hidden);

            var intent = Think(ref brain, s, contacts, out var order, frames: 3);

            Assert.AreEqual(BotMode.Attack, brain.Mode);
            Assert.IsTrue(intent.UsePath);
            Assert.IsFalse(order.Fire);
            contacts.Dispose();
        }

        [Test]
        public void LowHealth_NoEnemy_Repairs()
        {
            var brain = BotBrain.Create(4);
            brain.LastHealth = 0.2f;
            var s = Self();
            s.Health01 = 0.2f;
            s.CanRepair = true;
            var contacts = Contacts();

            var intent = Think(ref brain, s, contacts, out var order);

            Assert.AreEqual(BotMode.Repair, brain.Mode);
            Assert.IsTrue(intent.Actions.Repair);
            Assert.AreEqual(0f, order.Speed);
            contacts.Dispose();
        }

        [Test]
        public void LowHealth_EnemyNear_Retreats()
        {
            var brain = BotBrain.Create(5);
            brain.LastHealth = 0.2f;
            var s = Self();
            s.Health01 = 0.2f;
            var contacts = Contacts(Ahead(150f));

            var intent = Think(ref brain, s, contacts, out _);

            Assert.AreEqual(BotMode.Retreat, brain.Mode);
            Assert.Less(intent.Goal.z, 0f, "not running away from the enemy");
            contacts.Dispose();
        }

        [Test]
        public void NaniteCloud_IsUsed_AtLowHealth_ByTheDefaultRule()
        {
            var brain = BotBrain.Create(6);
            brain.LastHealth = 0.4f;
            var s = Self();
            s.Health01 = 0.4f;
            s.AbilityReady = true;
            s.AbilityKind = AbilityKind.NaniteCloud;
            var contacts = Contacts();

            var intent = Think(ref brain, s, contacts, out _);

            Assert.IsTrue(intent.Actions.Ability);
            contacts.Dispose();
        }

        [Test]
        public void DataRule_OverridesTheDefault()
        {
            var brain = BotBrain.Create(7);
            var s = Self();
            s.AbilityReady = true;
            s.AbilityKind = AbilityKind.NaniteCloud;
            s.AbilityUse = new BotUse(BotTrigger.Never, 0f);
            brain.LastHealth = 0.1f;
            s.Health01 = 0.1f;
            var contacts = Contacts();

            var intent = Think(ref brain, s, contacts, out _);

            Assert.IsFalse(intent.Actions.Ability);
            contacts.Dispose();
        }

        [Test]
        public void SeekerInLockRange_CyclesToIt_AndFiresOnLock()
        {
            var brain = BotBrain.Create(8);
            var s = Self();
            s.SelectedPylon = 0;
            s.RocketPylon = 0;
            s.RocketSpeed = 120f;
            s.SeekerPylon = 1;
            s.SeekerLockRange = 600f;
            var contacts = Contacts(Ahead(350f));

            var intent = Think(ref brain, s, contacts, out _, frames: 3);
            Assert.IsTrue(intent.Actions.CyclePylon || brain.NextCycle > 0d, "did not cycle to the seeker");

            s.SelectedPylon = 1;
            s.Locked = true;
            s.LockTarget = Enemy;
            Think(ref brain, s, contacts, out var order, frames: 3);
            Assert.IsTrue(order.FireSecondary, "did not fire on lock");
            contacts.Dispose();
        }
    }
}
