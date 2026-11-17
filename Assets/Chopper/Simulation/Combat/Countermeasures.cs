using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>Flares, from the utility module's countermeasure definition.</summary>
    public struct CountermeasureConfig : IComponentData
    {
        public float Cooldown;
        /// <summary>Seconds the flares burn in the air. While they burn, heat seekers see the flares, not the chopper.</summary>
        public float DecoyDuration;
        /// <summary>Drops before a reload. 0 = no limit. See <see cref="Magazine"/>.</summary>
        public byte MagazineSize;
        /// <summary>Seconds after the last drop until the magazine is full again.</summary>
        public float ReloadSeconds;
        /// <summary>When a bot drops them (Auto = shortly before a missile hits).</summary>
        public Bots.BotUse Bot;
    }

    /// <summary>Predicted on the owner, sent to everyone (<see cref="FlareCount"/> drives the flare visuals).</summary>
    public struct CountermeasureState : IComponentData
    {
        [GhostField] public float CooldownRemaining;
        [GhostField] public float DecoyRemaining;
        /// <summary>Times flares were dropped. Clients show a flare burst each time it goes up.</summary>
        [GhostField] public uint FlareCount;
        /// <summary>Drops used from the magazine, so the default is full.</summary>
        [GhostField] public byte RoundsUsed;
        [GhostField] public float ReloadRemaining;

        /// <summary>Seconds the last flares still burn (0 = none in the air). See <see cref="FlareFlight"/>.</summary>
        [GhostField] public float FlareRemaining;
        /// <summary>Seconds since the last drop.</summary>
        [GhostField] public float FlareAge;
        /// <summary>Where and how fast the centre of the last flare cloud left the chopper.</summary>
        [GhostField] public float3 FlareOrigin;
        [GhostField] public float3 FlareVelocity;

        public bool FlaresInAir => FlareRemaining > 0f;
    }

    /// <summary>
    /// One path for burning flares: where the centre of the flare cloud is, for the server (locks and heat missiles go
    /// after it) and the client (lock box on the flares; the flare visuals use the same gravity and drag).
    /// </summary>
    public static class FlareFlight
    {
        public const float Gravity = 3f;
        public const float Drag = 1.6f;
        /// <summary>Flares burn far hotter than any engine: a seeker that sees them goes for them.</summary>
        public const float Heat = 1000f;

        /// <summary>Starts a flare cloud under the chopper: it keeps some of the chopper's speed and drifts back.</summary>
        public static void Drop(ref CountermeasureState state, float3 position, quaternion rotation, float3 chopperVelocity, float burnSeconds)
        {
            var up = math.mul(rotation, math.up());
            var back = -math.mul(rotation, math.forward());
            state.FlareOrigin = position - up * 0.8f;
            state.FlareVelocity = chopperVelocity * 0.6f + back * 6f;
            state.FlareAge = 0f;
            state.FlareRemaining = math.max(state.FlareRemaining, burnSeconds);
        }

        /// <summary>Counts the burn down.</summary>
        public static void Tick(ref CountermeasureState state, float deltaTime)
        {
            if (state.FlareRemaining <= 0f) return;
            state.FlareRemaining = math.max(0f, state.FlareRemaining - deltaTime);
            state.FlareAge += deltaTime;
        }

        /// <summary>Centre of the flare cloud now (closed form of gravity with linear drag).</summary>
        public static float3 Position(in CountermeasureState state) => Position(state.FlareOrigin, state.FlareVelocity, state.FlareAge);

        public static float3 Velocity(in CountermeasureState state)
        {
            var terminal = new float3(0f, -Gravity / Drag, 0f);
            return terminal + (state.FlareVelocity - terminal) * math.exp(-Drag * state.FlareAge);
        }

        public static float3 Position(float3 origin, float3 velocity, float age)
        {
            var terminal = new float3(0f, -Gravity / Drag, 0f);
            return origin + terminal * age + (velocity - terminal) * (1f - math.exp(-Drag * age)) / Drag;
        }
    }
}
