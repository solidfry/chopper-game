using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// The pilot's combat rules on a chopper (plan section 5b), from <see cref="Loadout.PilotModifiers"/>. Same on client
    /// and server (both apply the loadout); only the server's damage systems read it. Scales of 1 = no change.
    /// </summary>
    public struct PilotTraits : IComponentData
    {
        public float DamageTakenScale;
        /// <summary>Crash and landing damage taken (on top of <see cref="DamageTakenScale"/>).</summary>
        public float CrashTakenScale;
        /// <summary>Damage this chopper's rams do to the other chopper.</summary>
        public float RamDealtScale;
        /// <summary>A kill gives back this share of the hull.</summary>
        public float KillHeal;
        public float BulletDotShare;
        public float BulletDotSeconds;
        public float ExplosiveDotShare;
        public float ExplosiveDotSeconds;
        public bool JamOnHit;

        public static PilotTraits None => new()
        {
            DamageTakenScale = 1f,
            CrashTakenScale = 1f,
            RamDealtScale = 1f,
            BulletDotSeconds = 3f,
            ExplosiveDotSeconds = 4f,
        };

        /// <summary>The damage this chopper takes from one hit of this kind.</summary>
        public float Taken(float amount, DamageKind kind)
        {
            var scale = math.max(DamageTakenScale, 0f);
            if (kind is DamageKind.Impact or DamageKind.Landing) scale *= math.max(CrashTakenScale, 0f);
            return amount * scale;
        }

        /// <summary>
        /// The damage over time this attacker adds for one of its hits: gun hits corrode, blasts burn. False when the
        /// pilot has none for that kind (damage over time never starts more damage over time).
        /// </summary>
        public bool DotFor(DamageKind kind, float hitAmount, out DamageOverTime dot)
        {
            dot = default;
            var share = kind switch
            {
                DamageKind.Bullet => BulletDotShare,
                DamageKind.Explosion => ExplosiveDotShare,
                _ => 0f,
            };
            var seconds = kind == DamageKind.Bullet ? BulletDotSeconds : ExplosiveDotSeconds;
            if (share <= 0f || hitAmount <= 0f || seconds <= 0f) return false;
            dot = new DamageOverTime
            {
                Remaining = hitAmount * share,
                SecondsLeft = seconds,
                Kind = kind == DamageKind.Bullet ? DamageKind.Corrosion : DamageKind.Burn,
            };
            return true;
        }
    }

    /// <summary>
    /// Server only: damage still to come on a chopper (corroding rounds, burning blasts). <see cref="DamageOverTimeSystem"/>
    /// turns it into ordinary <see cref="DamageEvent"/>s in pulses, so kill credit, hit markers and the repair interrupt
    /// all work as for any hit.
    /// </summary>
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    [InternalBufferCapacity(4)]
    public struct DamageOverTime : IBufferElementData
    {
        public float Remaining;
        public float SecondsLeft;
        public Entity Source;
        /// <summary><see cref="DamageKind.Corrosion"/> or <see cref="DamageKind.Burn"/>.</summary>
        public DamageKind Kind;
    }

    /// <summary>
    /// What everyone sees of a chopper's damage over time (green corrosion sparks, flames). Written by the server
    /// (<see cref="DamageOverTimeSystem"/>), sent to every client.
    /// </summary>
    public struct StatusLook : IComponentData
    {
        public const byte Corroding = 1;
        public const byte Burning = 2;

        [GhostField] public byte Flags;
        /// <summary>Server only (not sent): time since the last damage pulse.</summary>
        public float PulseTimer;

        public bool Has(byte flag) => (Flags & flag) != 0;
    }

    /// <summary>The damage over time rules, pure (edit-mode tests: PilotTests).</summary>
    public static class DamageOverTimeRules
    {
        /// <summary>Damage is dealt in pulses this far apart (fewer RPCs than one per tick).</summary>
        public const float PulseSeconds = 0.5f;
        /// <summary>At most this many effects per chopper; a new one replaces the weakest.</summary>
        public const int MaxEffects = 6;

        /// <summary>
        /// Adds a new effect. The same source and kind again merges: the damage adds up and the time restarts (a gun
        /// burst is one corrosion, not thirty).
        /// </summary>
        public static void Add(DynamicBuffer<DamageOverTime> effects, in DamageOverTime dot)
        {
            for (var i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                if (effect.Source != dot.Source || effect.Kind != dot.Kind) continue;
                effect.Remaining += dot.Remaining;
                effect.SecondsLeft = math.max(effect.SecondsLeft, dot.SecondsLeft);
                effects[i] = effect;
                return;
            }
            if (effects.Length < MaxEffects)
            {
                effects.Add(dot);
                return;
            }
            var weakest = 0;
            for (var i = 1; i < effects.Length; i++)
                if (effects[i].Remaining < effects[weakest].Remaining) weakest = i;
            effects[weakest] = dot;
        }

        /// <summary>
        /// One pulse of <paramref name="seconds"/>: the share of what is left that belongs to this time, so the total is
        /// dealt by the end. Returns the damage and moves the effect on.
        /// </summary>
        public static float Pulse(ref DamageOverTime effect, float seconds)
        {
            if (effect.SecondsLeft <= 0f || effect.Remaining <= 0f) return 0f;
            var share = math.saturate(seconds / effect.SecondsLeft);
            var amount = effect.Remaining * share;
            effect.Remaining -= amount;
            effect.SecondsLeft = math.max(0f, effect.SecondsLeft - seconds);
            return amount;
        }
    }
}
