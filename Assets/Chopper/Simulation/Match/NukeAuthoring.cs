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
        /// <summary>Owner, 25 Nov: pick-up zone 2x larger (was 12 m).</summary>
        public const float BaseRange = 24f;
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

    /// <summary>
    /// On every chopper (baked): set by the server while it carries the nuke. A ghost field, so the owner's predicted
    /// flight and pylons agree with the server: the carrier is slower (<see cref="SpeedScale"/>, from its
    /// <see cref="NukeHookConfig.CarrySpeed"/>: the winch keeps more speed) and cannot fire pylon weapons.
    /// </summary>
    public struct NukeCarry : IComponentData
    {
        [Unity.NetCode.GhostField] public bool Carrying;
        /// <summary>Top speed scale while carrying (1 = no change).</summary>
        [Unity.NetCode.GhostField(Quantization = 1000)] public float SpeedScale;

        /// <summary>Forward drag scale for <see cref="SpeedScale"/>: top speed goes with 1 / sqrt(quadratic drag).</summary>
        public static float DragScale(in NukeCarry carry) =>
            carry.Carrying && carry.SpeedScale > 0.05f ? 1f / (carry.SpeedScale * carry.SpeedScale) : 1f;
    }

    /// <summary>A team's base zone (a vertical cylinder): arm the nuke in your own, set it off in the enemy's.</summary>
    public struct NukeBase : IComponentData
    {
        public byte Team;
        public float Radius;
        public float3 Position;
        /// <summary>Where the nuke dock stands (ground point): set down here to arm or plant.</summary>
        public float3 Dock;
    }

    /// <summary>
    /// Server only, one entity when a new Capture the Nuke round starts (made by <see cref="NukeSystem"/>):
    /// <see cref="Session.ChopperRespawnSystem"/> sends every player back to the team's base grid, to take off together.
    /// </summary>
    public struct NukeRoundStart : IComponentData
    {
    }

    /// <summary>
    /// Server to all clients: the nuke went off here (the nuke blast, the round flash). Needed because the server puts a
    /// new nuke on the pad in the same tick, so clients never see the Detonated phase in <see cref="NukeState"/>.
    /// </summary>
    public struct NukeDetonatedRpc : Unity.NetCode.IRpcCommand
    {
        public float3 Position;
        public byte Team;
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

    /// <summary>
    /// Capture the Nuke spawns (owner, 20 Nov: "located at their base and all grouped together ... all take off
    /// together"; 25 Nov: "spawn in the base, but not right on the nuke ... a facility with landing bays"): landing bays
    /// in two wings left and right of the dock (the base centre), <see cref="WingOffset"/> out, each wing
    /// <see cref="WingColumns"/> wide; everyone faces the nuke pad.
    /// </summary>
    public static class NukeBaseSpawn
    {
        public const int MaxSlots = 12;
        public const int WingColumns = 2;
        /// <summary>Metres between bays in a wing: rotors (up to about 14 m across on the Lifter) stay well apart.</summary>
        public const float Spacing = 24f;
        /// <summary>Metres from the dock to the inner bays of each wing (clear of the dock and its arms).</summary>
        public const float WingOffset = 48f;
        /// <summary>Spawn this far above the bay (on the gear, a short settle).</summary>
        public const float Height = 3f;
        /// <summary>Seconds from a new round to the take-off (everyone spawns at once).</summary>
        public const float RoundStartDelay = 3f;

        /// <summary>Bay <paramref name="index"/>: even = left wing, odd = right wing; rows go from the pad side back.</summary>
        public static void Slot(float3 basePosition, float3 towardsPad, int index, out float3 position, out quaternion rotation)
        {
            var forward = math.normalizesafe(new float3(towardsPad.x, 0f, towardsPad.z), new float3(0f, 0f, 1f));
            var right = math.cross(math.up(), forward);
            var side = index % 2 == 0 ? -1f : 1f;
            var k = index / 2;
            var column = k % WingColumns;
            var row = k / WingColumns;
            var rows = (MaxSlots / 2 + WingColumns - 1) / WingColumns;
            position = basePosition + right * (side * (WingOffset + column * Spacing)) +
                       forward * (((rows - 1) * 0.5f - row) * Spacing) + new float3(0f, Height, 0f);
            rotation = quaternion.LookRotationSafe(forward, math.up());
        }

        /// <summary>The first slot with no chopper within half a spacing (or the last slot when all are taken).</summary>
        public static int FreeSlot(float3 basePosition, float3 towardsPad, NativeArray<float3> occupied)
        {
            const float clear = 8f;
            for (var i = 0; i < MaxSlots; i++)
            {
                Slot(basePosition, towardsPad, i, out var position, out _);
                var free = true;
                for (var k = 0; k < occupied.Length && free; k++)
                    free = math.distancesq(occupied[k].xz, position.xz) >= clear * clear || math.abs(occupied[k].y - position.y) > Spacing;
                if (free) return i;
            }
            return MaxSlots - 1;
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

        /// <summary>Set down at a dock: the hang point this close sideways to the dock, metres.</summary>
        public const float DockRadius = 10f;
        /// <summary>... and at most this high over the dock ground (hover low to set it down), metres.</summary>
        public const float DockHeight = 12f;

        /// <summary>Index of the base whose dock <paramref name="hang"/> (a carried nuke) is over, low enough; or -1.</summary>
        public static int DockOf(float3 hang, NativeArray<NukeBase> bases)
        {
            for (var i = 0; i < bases.Length; i++)
            {
                var dock = bases[i].Dock;
                if (math.distancesq(hang.xz, dock.xz) <= DockRadius * DockRadius && hang.y - dock.y <= DockHeight) return i;
            }
            return -1;
        }

        /// <summary>Where a carried nuke hangs.</summary>
        public static float3 HangPoint(float3 carrier) => carrier - new float3(0f, HangBelow, 0f);
    }
}
