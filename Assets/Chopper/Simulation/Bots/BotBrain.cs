using Chopper.Simulation.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Chopper.Simulation.Bots
{
    public enum BotMode : byte
    {
        /// <summary>No enemy known: wander (mostly the city).</summary>
        Patrol,
        /// <summary>Go after the target: approach on a path, then circle it and shoot.</summary>
        Attack,
        /// <summary>A missile is coming or a seeker has a lock: break turn, flares, abilities.</summary>
        Evade,
        /// <summary>Low health: run away from enemies, on a path.</summary>
        Retreat,
        /// <summary>Safe and low on health: hover and hold repair.</summary>
        Repair,
    }

    /// <summary>An enemy chopper the bot knows about this tick.</summary>
    public struct BotContact
    {
        public Entity Entity;
        public float3 Position;
        public float Health01;
        public float Distance;
        /// <summary>A clear line from the bot (within vision range).</summary>
        public bool InSight;
    }

    /// <summary>What the bot knows about itself and the threats on it this tick (gathered by <see cref="BotPilotSystem"/>).</summary>
    public struct BotSituation
    {
        public Entity Self;
        public float3 Position;
        public float3 Velocity;
        public float3 Forward;
        public float Health01;

        public bool HasGun;
        public float GunRange;
        public float GunHeat;
        public bool GunOverheated;

        public int SelectedPylon;
        /// <summary>Index of a straight rocket pylon with rounds, or -1.</summary>
        public int RocketPylon;
        public float RocketSpeed;
        /// <summary>Index of a seeker pylon with rounds that can lock air targets, or -1.</summary>
        public int SeekerPylon;
        public float SeekerLockRange;
        public bool Locked;
        public Entity LockTarget;

        public bool MissileIncoming;
        public float MissileTimeToImpact;
        /// <summary>Direction the nearest incoming missile travels (towards the bot).</summary>
        public float3 MissileDirection;
        public byte Locking;
        public byte LockedBy;

        public bool FlaresReady;
        public BotUse FlareUse;
        public bool AbilityReady;
        public bool AbilityActive;
        public AbilityKind AbilityKind;
        public BotUse AbilityUse;
        public float AbilityRadius;
        public bool CanRepair;
    }

    /// <summary>Button presses the brain wants this frame, on top of the pilot's sticks.</summary>
    public struct BotActions
    {
        public bool Ability;
        public bool Countermeasure;
        public bool CyclePylon;
        public bool Repair;
        public bool Dive;
    }

    /// <summary>The brain's wish for the navigator: a goal, on a path or straight.</summary>
    public struct BotIntent
    {
        /// <summary>Let <see cref="BotWander"/> pick the goal (patrol).</summary>
        public bool Wander;
        public float3 Goal;
        /// <summary>Plan a path on the grid to <see cref="Goal"/>; else fly straight (the eyes still avoid walls).</summary>
        public bool UsePath;
        public BotActions Actions;
    }

    /// <summary>
    /// The bot's decisions (plan section 7a, first version). Every <see cref="BotHandling.ReactionSeconds"/> it scores its
    /// needs and picks a mode and a target: evade (missile, lock), retreat and repair (low health), attack (an enemy in
    /// sight or heard), else patrol. Every frame it acts on the mode: an order for the pilot, a goal for the navigator and
    /// button presses (weapons, pylon cycling, flares, the pilot ability, repair). Abilities and flares follow the
    /// <see cref="BotUse"/> rule on their definition. Human limits: it reacts late, sees the target through a lag, aims
    /// with an error that shrinks while it holds the aim, and sometimes forgets a gadget.
    /// </summary>
    public struct BotBrain : IComponentData
    {
        public Random Random;
        public BotMode Mode;
        public Entity Target;
        /// <summary>Where the bot believes the target is (lagged), and its velocity from those beliefs.</summary>
        public float3 TargetPosition;
        public float3 TargetVelocity;
        public bool TargetKnown;
        public double NextThink;
        public double LastDamage;
        public float LastHealth;
        public bool Aiming;
        public double AimSince;
        public float3 AimOffset;
        public double NextAimOffset;
        public float StrafeSign;
        public double NextStrafeFlip;
        public double ExtendUntil;
        public bool GunCooling;
        public double NextCycle;
        public double NextFlareTry;
        public double NextAbilityTry;

        /// <summary>Seconds of no damage before the bot stops running and repairs.</summary>
        private const double SafeSeconds = 2.5;
        private const double CycleRepeatSeconds = 0.35;

        public static BotBrain Create(uint seed) => new()
        {
            Random = Random.CreateFromIndex(seed),
            LastHealth = 1f,
            LastDamage = -100d,
            StrafeSign = 1f,
        };

        /// <summary>A new chopper (spawn): forget the old fight.</summary>
        public void Reset()
        {
            Mode = BotMode.Patrol;
            Target = Entity.Null;
            TargetKnown = false;
            LastHealth = 1f;
            LastDamage = -100d;
            Aiming = false;
            ExtendUntil = 0d;
            GunCooling = false;
        }

        public void Think(in BotSituation s, in NativeArray<BotContact> contacts, in BotHandling h, double now, float deltaTime,
            ref PilotOrder order, out BotIntent intent)
        {
            intent = default;
            if (s.Health01 < LastHealth - 1e-3f) LastDamage = now;
            LastHealth = s.Health01;

            var targetIndex = Find(contacts, Target);
            Perceive(contacts, targetIndex, h, deltaTime);

            if (now >= NextThink)
            {
                NextThink = now + h.ReactionSeconds * Random.NextFloat(0.8f, 1.2f);
                Decide(s, contacts, h, now);
                targetIndex = Find(contacts, Target);
            }

            order.Speed = h.CruiseSpeed;
            order.ArriveRadius = h.ArriveRadius;
            order.RemainingDistance = 0f;
            order.Aim = false;
            order.Face = false;
            order.Fire = false;
            order.FireSecondary = false;
            order.AllowBoost = false;

            switch (Mode)
            {
                case BotMode.Patrol:
                    intent.Wander = true;
                    break;
                case BotMode.Attack:
                    Attack(s, contacts, targetIndex, h, now, deltaTime, ref order, ref intent);
                    break;
                case BotMode.Evade:
                    Evade(s, contacts, h, ref order, ref intent);
                    break;
                case BotMode.Retreat:
                    intent.Goal = s.Position + Away(s, contacts) * h.RetreatDistance;
                    intent.UsePath = true;
                    order.AllowBoost = true;
                    break;
                case BotMode.Repair:
                    intent.Goal = s.Position;
                    order.Speed = 0f;
                    intent.Actions.Repair = s.CanRepair;
                    break;
            }

            Gadgets(s, contacts, targetIndex, h, now, ref intent.Actions);
        }

        /// <summary>The target as the bot sees it: a lagged position, and a velocity from that.</summary>
        private void Perceive(in NativeArray<BotContact> contacts, int targetIndex, in BotHandling h, float deltaTime)
        {
            if (targetIndex < 0)
            {
                Target = Entity.Null;
                TargetKnown = false;
                return;
            }
            var seen = contacts[targetIndex].Position;
            if (!TargetKnown)
            {
                TargetPosition = seen;
                TargetVelocity = float3.zero;
                TargetKnown = true;
                return;
            }
            var blend = 1f - math.exp(-deltaTime / math.max(h.ReactionSeconds * 0.5f, 0.01f));
            var previous = TargetPosition;
            TargetPosition = math.lerp(TargetPosition, seen, blend);
            if (deltaTime > 1e-4f)
                TargetVelocity = math.lerp(TargetVelocity, (TargetPosition - previous) / deltaTime, blend);
        }

        private void Decide(in BotSituation s, in NativeArray<BotContact> contacts, in BotHandling h, double now)
        {
            // Target: close, hurt, in front, and the current one is kept unless another is much better.
            var best = Entity.Null;
            var bestScore = float.MinValue;
            for (var i = 0; i < contacts.Length; i++)
            {
                var c = contacts[i];
                if (!c.InSight && c.Distance > h.HearingRange) continue;
                var ahead = math.dot(s.Forward, math.normalizesafe(c.Position - s.Position));
                var score = -c.Distance + (1f - c.Health01) * 150f + ahead * 120f + (c.Entity == Target ? 200f : 0f) + (c.InSight ? 100f : 0f);
                if (score <= bestScore) continue;
                bestScore = score;
                best = c.Entity;
            }
            if (best != Target)
            {
                Target = best;
                TargetKnown = false;
            }

            // Needs.
            var evade = s.MissileIncoming && s.MissileTimeToImpact < 4f ? 1f : s.LockedBy > 0 ? 0.75f : s.Locking > 0 ? 0.4f : 0f;
            var hurt = Mode is BotMode.Retreat or BotMode.Repair ? s.Health01 < h.RepairDoneHealth : s.Health01 < h.RetreatHealth;
            var retreat = hurt ? 0.8f : 0f;
            var attack = Target != Entity.Null ? 0.6f : 0f;
            // A bot with endless boost and no crash damage (Battering Ram) goes in instead of running.
            if (s.AbilityActive && s.AbilityKind == AbilityKind.BatteringRam && Target != Entity.Null) attack = 1.2f;

            var mode = BotMode.Patrol;
            var top = 0.2f;
            if (attack > top) { mode = BotMode.Attack; top = attack; }
            if (retreat > top) { mode = BotMode.Retreat; top = retreat; }
            if (evade > top) mode = BotMode.Evade;

            if (mode == BotMode.Retreat && now - LastDamage > SafeSeconds && !EnemyClose(s, contacts, h))
                mode = BotMode.Repair;

            if (mode != Mode)
            {
                if (mode == BotMode.Evade || mode == BotMode.Attack) StrafeSign = Random.NextBool() ? 1f : -1f;
                Aiming = false;
                Mode = mode;
            }
        }

        private void Attack(in BotSituation s, in NativeArray<BotContact> contacts, int targetIndex, in BotHandling h,
            double now, float deltaTime, ref PilotOrder order, ref BotIntent intent)
        {
            if (targetIndex < 0 || !TargetKnown)
            {
                intent.Wander = true;
                return;
            }

            var contact = contacts[targetIndex];
            var target = TargetPosition;
            var toTarget = target - s.Position;
            var distance = math.length(toTarget);
            var ramming = s.AbilityActive && s.AbilityKind == AbilityKind.BatteringRam;
            var engageRange = math.max(h.HasGunRange(s), s.SeekerPylon >= 0 ? s.SeekerLockRange : 0f) * 1.1f;

            if (ramming)
            {
                intent.Goal = target;
                order.Speed = h.CruiseSpeed * 1.5f;
                order.ArriveRadius = 1f;
                order.AllowBoost = true;
                order.Face = true;
                order.FacePoint = target;
                return;
            }

            if (now < ExtendUntil)
            {
                // Extend: away and a little up, then turn back for a new pass.
                var away = math.normalizesafe(Flat(s.Position - target), s.Forward);
                intent.Goal = s.Position + away * 250f + new float3(0f, 15f, 0f);
                order.AllowBoost = true;
                return;
            }

            if (!contact.InSight || distance > engageRange)
            {
                // Approach on a path; look at it when close.
                intent.Goal = target + new float3(0f, 15f, 0f);
                intent.UsePath = true;
                order.AllowBoost = distance > 400f;
                if (distance < engageRange * 2f)
                {
                    order.Face = true;
                    order.FacePoint = target;
                }
                Aiming = false;
                return;
            }

            if (distance < h.MinRange)
            {
                ExtendUntil = now + Random.NextFloat(1.8f, 3f);
                Aiming = false;
                return;
            }

            // Circle the target at the preferred range (a strafe), and change direction now and then.
            if (now >= NextStrafeFlip)
            {
                NextStrafeFlip = now + Random.NextFloat(4f, 9f);
                if (Random.NextFloat() < 0.4f) StrafeSign = -StrafeSign;
            }
            var radial = math.normalizesafe(Flat(s.Position - target), -s.Forward);
            var tangent = math.cross(math.up(), radial) * StrafeSign;
            var spot = target + radial * h.PreferredRange + tangent * (h.PreferredRange * h.StrafeRate * 2f);
            spot.y = math.max(target.y + 12f, s.Position.y - 20f);
            intent.Goal = spot;
            order.Speed = h.EngageSpeed;
            order.ArriveRadius = 10f;

            // Weapon: seeker when it can lock, rockets for slow targets or close in, else the gun.
            var targetSpeed = math.length(TargetVelocity);
            var wanted = -1;
            if (s.SeekerPylon >= 0 && distance < s.SeekerLockRange && distance > h.MinRange * 1.5f) wanted = s.SeekerPylon;
            else if (s.RocketPylon >= 0 && (targetSpeed < 10f || distance < 250f)) wanted = s.RocketPylon;
            if (wanted >= 0 && wanted != s.SelectedPylon && now >= NextCycle)
            {
                intent.Actions.CyclePylon = true;
                NextCycle = now + CycleRepeatSeconds;
            }
            var rockets = wanted >= 0 && wanted == s.SelectedPylon && wanted == s.RocketPylon;
            var seeker = wanted >= 0 && wanted == s.SelectedPylon && wanted == s.SeekerPylon;

            // Aim point: the gun is hitscan (no lead); rockets lead the target.
            var aim = target;
            if (rockets && s.RocketSpeed > 1f) aim += TargetVelocity * (distance / s.RocketSpeed);
            if (now >= NextAimOffset)
            {
                NextAimOffset = now + Random.NextFloat(0.4f, 0.9f);
                AimOffset = Random.NextFloat3Direction();
            }
            var settle = Aiming ? math.saturate((float)(now - AimSince) / h.AimSettleSeconds) : 0f;
            var error = h.AimError * (1f - 0.67f * settle);
            aim += AimOffset * (distance * math.tan(error));

            var toAim = math.normalizesafe(aim - s.Position, s.Forward);
            var alignment = math.dot(s.Forward, toAim);
            if (alignment > h.AimConeCos)
            {
                if (!Aiming) AimSince = now;
                Aiming = true;
                order.Aim = true;
                order.AimPoint = aim;
            }
            else
            {
                Aiming = false;
                order.Face = true;
                order.FacePoint = aim;
            }

            // Fire when lined up (the true target, not the aim point: the error makes it miss sometimes).
            var onTarget = math.dot(s.Forward, math.normalizesafe(target - s.Position)) > math.cos(math.max(math.radians(1.5f), math.atan(5f / distance)));
            if (s.GunOverheated || s.GunHeat >= h.GunHeatRelease) GunCooling = true;
            else if (s.GunHeat < h.GunHeatRelease * 0.4f) GunCooling = false;
            order.Fire = Aiming && onTarget && s.HasGun && distance < s.GunRange && !GunCooling;
            order.FireSecondary = (rockets && Aiming && onTarget) || (seeker && s.Locked && s.LockTarget == Target);
        }

        private void Evade(in BotSituation s, in NativeArray<BotContact> contacts, in BotHandling h, ref PilotOrder order, ref BotIntent intent)
        {
            // Break across the missile's path and down towards cover; without a missile, across the line to the enemy.
            var threat = s.MissileIncoming ? s.MissileDirection : -Away(s, contacts);
            var side = math.normalizesafe(math.cross(math.up(), threat), math.right()) * StrafeSign;
            intent.Goal = s.Position + side * 250f + threat * 60f + new float3(0f, -35f, 0f);
            order.Speed = h.CruiseSpeed * 1.3f;
            order.AllowBoost = true;
            intent.Actions.Dive = s.MissileIncoming && s.MissileTimeToImpact < 1.5f;
        }

        private void Gadgets(in BotSituation s, in NativeArray<BotContact> contacts, int targetIndex, in BotHandling h, double now,
            ref BotActions actions)
        {
            if (s.FlaresReady && now >= NextFlareTry)
            {
                var use = BotUseRules.Flares(s.FlareUse);
                if (use.Trigger == BotTrigger.IncomingMissile) use.Threshold *= h.FlareTimingScale;
                if (Wants(use, s, contacts, targetIndex, h, now, 0f))
                {
                    NextFlareTry = now + 0.6;
                    actions.Countermeasure = Random.NextFloat() >= h.MistakeChance;
                }
            }

            if (s.AbilityReady && now >= NextAbilityTry)
            {
                var use = BotUseRules.Resolve(s.AbilityUse, s.AbilityKind);
                if (Wants(use, s, contacts, targetIndex, h, now, s.AbilityRadius))
                {
                    NextAbilityTry = now + 1.0;
                    actions.Ability = Random.NextFloat() >= h.MistakeChance;
                }
            }
        }

        public bool Wants(in BotUse use, in BotSituation s, in NativeArray<BotContact> contacts, int targetIndex, in BotHandling h,
            double now, float radius)
        {
            var hasTarget = targetIndex >= 0 && TargetKnown;
            var distance = hasTarget ? math.distance(TargetPosition, s.Position) : float.MaxValue;
            switch (use.Trigger)
            {
                case BotTrigger.IncomingMissile:
                    return s.MissileIncoming && s.MissileTimeToImpact < use.Threshold;
                case BotTrigger.BeingLocked:
                    return s.Locking > 0 || s.LockedBy > 0;
                case BotTrigger.LowHealth:
                    return s.Health01 < use.Threshold;
                case BotTrigger.UnderFire:
                    return now - LastDamage < use.Threshold;
                case BotTrigger.AttackRun:
                    return Mode == BotMode.Attack && Aiming && distance < use.Threshold;
                case BotTrigger.Chase:
                    return Mode == BotMode.Attack && hasTarget && distance > use.Threshold &&
                           math.dot(s.Velocity, TargetPosition - s.Position) > 0f;
                case BotTrigger.Escape:
                    return Mode is BotMode.Evade or BotMode.Retreat;
                case BotTrigger.EnemiesNear:
                {
                    var reach = use.Threshold > 0f ? use.Threshold : radius;
                    for (var i = 0; i < contacts.Length; i++)
                        if (contacts[i].Distance < reach) return true;
                    return false;
                }
                case BotTrigger.NoTarget:
                    return Mode == BotMode.Patrol && !hasTarget;
                case BotTrigger.Ram:
                    return Mode == BotMode.Attack && distance < use.Threshold &&
                           math.dot(s.Velocity - TargetVelocity, TargetPosition - s.Position) > 5f * distance;
                default:
                    return false;
            }
        }

        /// <summary>Away from the nearest known enemy (flat), or back the way it faces when none.</summary>
        private static float3 Away(in BotSituation s, in NativeArray<BotContact> contacts)
        {
            var nearest = float.MaxValue;
            var away = -s.Forward;
            for (var i = 0; i < contacts.Length; i++)
            {
                if (contacts[i].Distance >= nearest) continue;
                nearest = contacts[i].Distance;
                away = s.Position - contacts[i].Position;
            }
            return math.normalizesafe(Flat(away), math.forward());
        }

        private static bool EnemyClose(in BotSituation s, in NativeArray<BotContact> contacts, in BotHandling h)
        {
            for (var i = 0; i < contacts.Length; i++)
            {
                var c = contacts[i];
                if (c.Distance < h.HearingRange || (c.InSight && c.Distance < h.RetreatDistance)) return true;
            }
            return false;
        }

        private static int Find(in NativeArray<BotContact> contacts, Entity entity)
        {
            if (entity == Entity.Null) return -1;
            for (var i = 0; i < contacts.Length; i++)
                if (contacts[i].Entity == entity) return i;
            return -1;
        }

        private static float3 Flat(float3 v) => new(v.x, 0f, v.z);
    }

    internal static class BotHandlingExtensions
    {
        /// <summary>Gun range, or a default when the chopper has no gun.</summary>
        public static float HasGunRange(this in BotHandling handling, in BotSituation s) => s.HasGun ? s.GunRange : handling.PreferredRange;
    }
}
