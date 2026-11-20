using Chopper.Simulation.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Simulation.Match
{
    /// <summary>Server only: the chopper that holds the nuke (Entity.Null = nobody).</summary>
    public struct NukeCarrier : IComponentData
    {
        public Entity Chopper;
        /// <summary>The chopper whose hook timer runs (<see cref="NukeRules.TickHook"/>).</summary>
        public Entity Hooking;
    }

    /// <summary>
    /// A chopper's nuke hook, from its loadout (<see cref="Loadout.LoadoutBuilder"/>): seconds to hook a free nuke while
    /// in reach, the reach, and its top speed while carrying. The Heavy Winch (Lifter) makes all three better.
    /// </summary>
    public struct NukeHookConfig : IComponentData
    {
        /// <summary>Without a winch (owner, 20 Nov: 5 to 10 s; the winch gives the 5).</summary>
        public const float BaseSeconds = 10f;
        public const float BaseRange = 12f;
        /// <summary>Top speed while carrying without a winch (step 3 applies it).</summary>
        public const float BaseCarrySpeed = 0.75f;

        public float Seconds;
        public float Range;
        public float CarrySpeed;

        public static NukeHookConfig Base => From(1f, 1f, 1f);

        public static NukeHookConfig From(float timeMultiplier, float rangeMultiplier, float carryMultiplier) => new()
        {
            Seconds = BaseSeconds * (timeMultiplier > 0f ? timeMultiplier : 1f),
            Range = BaseRange * (rangeMultiplier > 0f ? rangeMultiplier : 1f),
            CarrySpeed = math.min(1f, BaseCarrySpeed * (carryMultiplier > 0f ? carryMultiplier : 1f)),
        };
    }

    /// <summary>A team's base zone (a vertical cylinder): arm the nuke in your own, set it off in the enemy's.</summary>
    public struct NukeBase : IComponentData
    {
        public byte Team;
        public float Radius;
        public float3 Position;
    }

    /// <summary>Server only, one entity per round win. Made by <see cref="NukeSystem"/>, used by <see cref="MatchScoreSystem"/>.</summary>
    public struct NukeRoundWon : IComponentData
    {
        public byte Team;
    }

    /// <summary>
    /// The Capture the Nuke nuke: a pre-spawned ghost in the gameplay subscene (Chopper → Setup step 4 puts it on the
    /// pad). The server moves it (pad, under its carrier, on the ground where it falls); clients interpolate it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NukeAuthoring : MonoBehaviour
    {
        private sealed class Baker : Baker<NukeAuthoring>
        {
            public override void Bake(NukeAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, NukeRules.AtSpawn);
                AddComponent<NukeCarrier>(entity);
                // Seekers can show it; not damageable for now (shooting the carrier drops it).
                AddComponent(entity, new Targetable { Class = TargetClass.Ground, BaseHeat = 0.9f });
            }
        }
    }

    /// <summary>A team's base zone. Chopper → Setup step 4 adds one per team.</summary>
    [DisallowMultipleComponent]
    public sealed class NukeBaseAuthoring : MonoBehaviour
    {
        [SerializeField, Tooltip("1.. (team A = 1).")] private byte team = 1;
        [SerializeField] private float radius = 45f;

        public byte Team { get => team; set => team = value; }

        private sealed class Baker : Baker<NukeBaseAuthoring>
        {
            public override void Bake(NukeBaseAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new NukeBase { Team = authoring.team, Radius = authoring.radius, Position = authoring.transform.position });
            }
        }
    }

    /// <summary>Pure zone and hook maths for <see cref="NukeSystem"/> (EditMode tests).</summary>
    public static class NukeZones
    {
        /// <summary>The nuke hangs this far under its carrier, metres.</summary>
        public const float HangBelow = 6f;

        /// <summary>The team whose zone holds <paramref name="position"/> (horizontal distance; any height), or 0.</summary>
        public static byte ZoneOf(float3 position, NativeArray<NukeBase> bases)
        {
            for (var i = 0; i < bases.Length; i++)
                if (Inside(position, bases[i])) return bases[i].Team;
            return TeamRules.None;
        }

        public static bool Inside(float3 position, in NukeBase zone) =>
            math.distancesq(position.xz, zone.Position.xz) <= zone.Radius * zone.Radius;

        /// <summary>The model's origin is the hook eye: this far above the ground the body rests on it, metres.</summary>
        public const float RestHeight = 1.4f;

        /// <summary>Where a nuke resting on <paramref name="ground"/> has its origin.</summary>
        public static float3 Resting(float3 ground) => ground + new float3(0f, RestHeight, 0f);

        /// <summary>Where a carried nuke hangs.</summary>
        public static float3 HangPoint(float3 carrier) => carrier - new float3(0f, HangBelow, 0f);
    }
}
