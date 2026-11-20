using Unity.Entities;
using Unity.Mathematics;

namespace Chopper.Simulation.Bots
{
    /// <summary>
    /// The match objective as the bots see it (Capture the Nuke). Filled by the server bot system; empty in other modes.
    /// </summary>
    public struct BotObjectiveInfo
    {
        public bool Active;
        public float3 NukePosition;
        public Match.NukePhase Phase;
        /// <summary>The chopper that holds the nuke (Entity.Null when none).</summary>
        public Entity Carrier;
        public byte CarrierTeam;
        /// <summary>The team that armed the nuke (0 = not armed).</summary>
        public byte ArmedBy;
        public float3 Base1;
        public float3 Base2;
    }

    /// <summary>A live gun turret, as the bots see it (a ground target).</summary>
    public struct BotTurretInfo
    {
        public Entity Entity;
        /// <summary>Aim point (the gun head).</summary>
        public float3 Position;
        public float Health01;
        /// <summary><see cref="Combat.TurretConfig.Team"/> (0 = shoots everyone).</summary>
        public byte Team;
    }

    /// <summary>
    /// Pure objective choice for a bot (Capture the Nuke, plan section 7 step 5): where to fly when no fight needs it.
    /// Carrier: home base until armed, then the enemy base. Nuke free: hover over it to hook it. Team mate carries:
    /// escort. Enemy carries: chase the carrier.
    /// </summary>
    public static class BotObjective
    {
        /// <summary>Hover this high over a free nuke: inside the base hook range (12 m), clear of the ground.</summary>
        public const float HookHoverHeight = 8f;
        /// <summary>Arrive radius over the nuke: close enough to stay in hook range.</summary>
        public const float HookArriveRadius = 3f;
        /// <summary>Fly this high over a base zone centre.</summary>
        public const float BaseHeight = 25f;
        /// <summary>An enemy further than this does not pull a bot off the objective, metres.</summary>
        public const float FocusRange = 250f;
        /// <summary>This close to a free nuke, a bot keeps hooking even in a fight, metres.</summary>
        public const float HookCommitRange = 60f;
        /// <summary>
        /// Target score cost of a turret against a chopper, metres: an enemy chopper this much further away is still
        /// picked first (owner, 20 Nov: "prioritise the turrets or nearby enemies").
        /// </summary>
        public const float TurretScorePenalty = 150f;
        /// <summary>Escorts fly this far above the carrier.</summary>
        public const float EscortHeight = 15f;

        public enum Kind : byte
        {
            None,
            Hook,
            ArmAtHome,
            Deliver,
            Escort,
            Chase,
        }

        public static Kind Choose(in BotObjectiveInfo info, Entity self, byte team, out float3 goal, out float arriveRadius)
        {
            goal = default;
            arriveRadius = 0f;
            if (!info.Active || team == 0 || info.Phase == Match.NukePhase.Detonated) return Kind.None;

            var home = team == 1 ? info.Base1 : info.Base2;
            var enemy = team == 1 ? info.Base2 : info.Base1;
            if (info.Carrier == self && self != Entity.Null)
            {
                var armed = info.ArmedBy == team;
                goal = (armed ? enemy : home) + new float3(0f, BaseHeight, 0f);
                arriveRadius = 8f;
                return armed ? Kind.Deliver : Kind.ArmAtHome;
            }
            if (info.Carrier != Entity.Null)
            {
                var ally = info.CarrierTeam == team;
                goal = info.NukePosition + new float3(0f, ally ? EscortHeight + Match.NukeZones.HangBelow : Match.NukeZones.HangBelow, 0f);
                arriveRadius = ally ? 20f : 10f;
                return ally ? Kind.Escort : Kind.Chase;
            }
            goal = info.NukePosition + new float3(0f, HookHoverHeight, 0f);
            arriveRadius = HookArriveRadius;
            return Kind.Hook;
        }
    }
}
