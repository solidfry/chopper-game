using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Match
{
    /// <summary>Where the nuke is. The server owns it; clients read it for the HUD and markers.</summary>
    public enum NukePhase : byte
    {
        /// <summary>On its pad in the middle of the map; anyone can hook it.</summary>
        AtSpawn = 0,
        /// <summary>Hooked under a chopper (<see cref="NukeState.CarrierTeam"/>).</summary>
        Carried = 1,
        /// <summary>Lost (carrier shot down or let go). Back to the pad after <see cref="NukeTuning.DropResetSeconds"/>.</summary>
        Dropped = 2,
        /// <summary>Went off in a base. The round is over.</summary>
        Detonated = 3,
    }

    /// <summary>
    /// Capture the Nuke state (plan section 7). One nuke per round. A team hooks it, holds it in its own base zone
    /// to arm it, then flies the armed nuke into the enemy base zone to set it off. The defenders can shoot the carrier
    /// down (the nuke drops) and defuse an armed nuke by holding it in their own zone.
    /// </summary>
    public struct NukeState : IComponentData
    {
        [GhostField] public NukePhase Phase;
        /// <summary>Team of the chopper that carries it (0 = nobody).</summary>
        [GhostField] public byte CarrierTeam;
        /// <summary>Team that armed it (0 = not armed). An armed nuke stays armed when dropped.</summary>
        [GhostField] public byte ArmedBy;
        /// <summary>0..1 arming progress of <see cref="CarrierTeam"/> in its own zone.</summary>
        [GhostField(Quantization = 100)] public float ArmProgress;
        /// <summary>0..1 defuse progress of the defending team.</summary>
        [GhostField(Quantization = 100)] public float DefuseProgress;
        /// <summary>Seconds since it was dropped.</summary>
        [GhostField(Quantization = 10)] public float DroppedFor;
        /// <summary>The team that set it off in the enemy base (the round winner). 0 = none.</summary>
        [GhostField] public byte DetonatedBy;
        /// <summary>0..1 hook progress of the chopper hovering over a free nuke (HUD bar).</summary>
        [GhostField(Quantization = 100)] public float HookProgress;
        /// <summary>Team of that chopper (0 = nobody is hooking).</summary>
        [GhostField] public byte HookTeam;
    }

    /// <summary>Tuning numbers. Default values are the plan's (section 7).</summary>
    public struct NukeTuning
    {
        public float ArmSeconds;
        public float DefuseSeconds;
        public float DropResetSeconds;
        /// <summary>Arm progress lost per second while the carrier is out of its zone.</summary>
        public float ArmDecayPerSecond;

        public static NukeTuning Default => new NukeTuning
        {
            ArmSeconds = 20f,
            DefuseSeconds = 10f,
            DropResetSeconds = 30f,
            ArmDecayPerSecond = 0.05f,
        };
    }

    /// <summary>What the server saw this tick, gathered by the ECS system (zones are physics triggers).</summary>
    public struct NukeInputs
    {
        /// <summary>Team of the chopper that holds it now (0 = nobody holds it).</summary>
        public byte CarrierTeam;
        /// <summary>Team whose base zone the nuke is in (0 = no zone).</summary>
        public byte ZoneTeam;
        /// <summary>A live chopper of another team than <see cref="ZoneTeam"/> is in that zone.</summary>
        public bool ZoneContested;
    }

    public enum NukeEvent : byte
    {
        None = 0,
        PickedUp,
        Dropped,
        Armed,
        Defused,
        Reset,
        Detonated,
    }

    /// <summary>Pure Capture the Nuke rules. No ECS access, so EditMode tests cover every step.</summary>
    public static class NukeRules
    {
        /// <summary>Progress counts as full here (float sums of small steps land just under 1).</summary>
        private const float Done = 0.9999f;

        public static NukeState AtSpawn => new NukeState { Phase = NukePhase.AtSpawn };

        /// <summary>Advances the nuke one tick. Returns the most important thing that happened (one per tick).</summary>
        public static NukeEvent Tick(ref NukeState s, in NukeInputs input, in NukeTuning tuning, float deltaTime)
        {
            if (s.Phase == NukePhase.Detonated) return NukeEvent.None;

            var pickUpOrDrop = Hold(ref s, input.CarrierTeam);

            // An armed nuke flown into the other team's zone goes off.
            if (s.ArmedBy != TeamRules.None && s.Phase == NukePhase.Carried && s.CarrierTeam == s.ArmedBy &&
                input.ZoneTeam != TeamRules.None && input.ZoneTeam != s.ArmedBy)
            {
                s.Phase = NukePhase.Detonated;
                s.DetonatedBy = s.ArmedBy;
                return NukeEvent.Detonated;
            }

            if (s.ArmedBy != TeamRules.None)
            {
                // Defuse: the other team holds the armed nuke in its own zone, with no attacker there.
                var defusing = s.Phase == NukePhase.Carried && s.CarrierTeam != s.ArmedBy &&
                               input.ZoneTeam == s.CarrierTeam && !input.ZoneContested;
                if (defusing)
                {
                    s.DefuseProgress = math.saturate(s.DefuseProgress + deltaTime / math.max(tuning.DefuseSeconds, 0.01f));
                    if (s.DefuseProgress >= Done)
                    {
                        s.ArmedBy = TeamRules.None;
                        s.DefuseProgress = 0f;
                        s.ArmProgress = 0f;
                        return NukeEvent.Defused;
                    }
                }
                else if (!(s.Phase == NukePhase.Carried && input.ZoneTeam == s.CarrierTeam))
                {
                    s.DefuseProgress = 0f;  // left the zone: start again
                }
            }
            else if (s.Phase == NukePhase.Carried)
            {
                // Arm: the carrier's team holds it in its own zone. Enemies in the zone pause it.
                if (input.ZoneTeam == s.CarrierTeam)
                {
                    if (!input.ZoneContested)
                    {
                        s.ArmProgress = math.saturate(s.ArmProgress + deltaTime / math.max(tuning.ArmSeconds, 0.01f));
                        if (s.ArmProgress >= Done)
                        {
                            s.ArmedBy = s.CarrierTeam;
                            s.ArmProgress = 1f;
                            return NukeEvent.Armed;
                        }
                    }
                }
                else
                {
                    s.ArmProgress = math.max(0f, s.ArmProgress - tuning.ArmDecayPerSecond * deltaTime);
                }
            }

            if (s.Phase == NukePhase.Dropped)
            {
                s.DroppedFor += deltaTime;
                if (s.DroppedFor >= tuning.DropResetSeconds)
                {
                    s = AtSpawn;
                    return NukeEvent.Reset;
                }
            }

            return pickUpOrDrop;
        }

        /// <summary>
        /// The hook timer on a free nuke: <paramref name="candidate"/> (the chopper in reach now, or Entity.Null) must stay
        /// in reach for <paramref name="seconds"/>. A different chopper starts again from 0; nobody in reach clears it.
        /// True on the tick the hook takes (then <paramref name="hooker"/> holds it).
        /// </summary>
        public static bool TickHook(ref float progress, ref Entity hooker, Entity candidate, float seconds, float deltaTime)
        {
            if (candidate == Entity.Null)
            {
                progress = 0f;
                hooker = Entity.Null;
                return false;
            }
            if (candidate != hooker)
            {
                hooker = candidate;
                progress = 0f;
            }
            progress = math.saturate(progress + deltaTime / math.max(seconds, 0.01f));
            if (progress < Done) return false;
            progress = 0f;
            return true;
        }

        /// <summary>Pick up and drop. A new team that hooks the nuke does not keep the other team's arm progress.</summary>
        private static NukeEvent Hold(ref NukeState s, byte carrier)
        {
            if (carrier != TeamRules.None)
            {
                if (s.Phase == NukePhase.Carried && s.CarrierTeam == carrier) return NukeEvent.None;
                if (s.CarrierTeam != carrier && s.ArmedBy == TeamRules.None) s.ArmProgress = 0f;
                if (s.CarrierTeam != carrier) s.DefuseProgress = 0f;
                s.Phase = NukePhase.Carried;
                s.CarrierTeam = carrier;
                s.DroppedFor = 0f;
                return NukeEvent.PickedUp;
            }
            if (s.Phase != NukePhase.Carried) return NukeEvent.None;
            s.Phase = NukePhase.Dropped;
            s.DroppedFor = 0f;
            return NukeEvent.Dropped;
        }
    }

    /// <summary>Rounds: best of <see cref="RoundsToWin"/>*2-1. One round ends when a nuke goes off.</summary>
    public static class NukeRounds
    {
        public const int RoundsToWin = 2;

        /// <summary>The match winner, or 0 while no team has <see cref="RoundsToWin"/> round wins.</summary>
        public static byte Winner(in Unity.Collections.FixedList64Bytes<int> roundWins)
        {
            for (var i = 0; i < roundWins.Length; i++)
                if (roundWins[i] >= RoundsToWin) return (byte)(i + 1);
            return TeamRules.None;
        }
    }
}
