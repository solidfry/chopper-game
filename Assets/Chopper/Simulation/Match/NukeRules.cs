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
        /// <summary>Lost (carrier shot down). Back to the pad after <see cref="NukeTuning.DropResetSeconds"/>.</summary>
        Dropped = 2,
        /// <summary>Went off in a base. The round is over.</summary>
        Detonated = 3,
        /// <summary>Set down in the dock of <see cref="NukeState.DockTeam"/>'s own base: it arms there
        /// (<see cref="NukeState.ArmProgress"/>). The enemy can hook it out, which stops the arm.</summary>
        Docked = 4,
        /// <summary>Armed and set down in the enemy base (<see cref="NukeState.DockTeam"/> = the defenders): the final
        /// countdown runs (<see cref="NukeState.Countdown"/>); the defenders can disarm it by hovering over it.</summary>
        Planted = 5,
    }

    /// <summary>
    /// Capture the Nuke state (plan section 7; dock rules from the owner, 25 Nov). One nuke per round. A team hooks
    /// it and sets it down in its own base dock, where it arms. The enemy can steal it from the dock (the arm stops;
    /// they arm it in their own dock). The armed nuke is flown to the enemy base and set down: a countdown starts and
    /// the defenders can disarm it before it goes off.
    /// </summary>
    public struct NukeState : IComponentData
    {
        [GhostField] public NukePhase Phase;
        /// <summary>Team of the chopper that carries it (0 = nobody).</summary>
        [GhostField] public byte CarrierTeam;
        /// <summary>Team that armed it (0 = not armed). Stealing it clears this.</summary>
        [GhostField] public byte ArmedBy;
        /// <summary>0..1 arming progress in <see cref="DockTeam"/>'s dock.</summary>
        [GhostField(Quantization = 100)] public float ArmProgress;
        /// <summary>0..1 disarm progress of the defenders over a planted nuke.</summary>
        [GhostField(Quantization = 100)] public float DefuseProgress;
        /// <summary>Seconds since it was dropped.</summary>
        [GhostField(Quantization = 10)] public float DroppedFor;
        /// <summary>The team that set it off in the enemy base (the round winner). 0 = none.</summary>
        [GhostField] public byte DetonatedBy;
        /// <summary>0..1 hook progress of the chopper hovering over a free nuke (HUD bar).</summary>
        [GhostField(Quantization = 100)] public float HookProgress;
        /// <summary>Team of that chopper (0 = nobody is hooking).</summary>
        [GhostField] public byte HookTeam;
        /// <summary>Team whose base dock holds it (<see cref="NukePhase.Docked"/>, <see cref="NukePhase.Planted"/>).</summary>
        [GhostField] public byte DockTeam;
        /// <summary>Seconds left before a planted nuke goes off.</summary>
        [GhostField(Quantization = 10)] public float Countdown;
    }

    /// <summary>Tuning numbers. Default values are the plan's (section 7) and the owner's (25 Nov).</summary>
    public struct NukeTuning
    {
        public float ArmSeconds;
        /// <summary>Hover time over a planted nuke to disarm it.</summary>
        public float DefuseSeconds;
        public float DropResetSeconds;
        /// <summary>The final countdown once the armed nuke is set down in the enemy base.</summary>
        public float PlantSeconds;

        public static NukeTuning Default => new NukeTuning
        {
            ArmSeconds = 20f,
            DefuseSeconds = 10f,
            DropResetSeconds = 30f,
            PlantSeconds = 45f,
        };
    }

    /// <summary>What the server saw this tick, gathered by the ECS system.</summary>
    public struct NukeInputs
    {
        /// <summary>Team of the chopper that holds it now (0 = nobody holds it).</summary>
        public byte CarrierTeam;
        /// <summary>Team whose base dock the carried nuke is over, low enough to set down (0 = none).</summary>
        public byte DockTeam;
        /// <summary>A live enemy chopper is in the dock team's zone (pauses the arm).</summary>
        public bool ZoneContested;
        /// <summary>The defenders' hover over a planted nuke finished this tick.</summary>
        public bool Disarmed;
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
        /// <summary>Set down in its own base dock: arming starts.</summary>
        Docked,
        /// <summary>Set down armed in the enemy base: the countdown starts.</summary>
        Planted,
        /// <summary>The enemy hooked it out of a dock: the arm (or the armed state) is lost.</summary>
        Stolen,
    }

    /// <summary>Pure Capture the Nuke rules. No ECS access, so EditMode tests cover every step.</summary>
    public static class NukeRules
    {
        /// <summary>Progress counts as full here (float sums of small steps land just under 1).</summary>
        private const float Done = 0.9999f;

        public static NukeState AtSpawn => new NukeState { Phase = NukePhase.AtSpawn };

        /// <summary>Who may hook the nuke now: anyone from the pad or a drop; only the enemy from a dock that is still
        /// arming (the owners wait for the arm); anyone from a dock once armed (owners carry it, the enemy steals it);
        /// only the defenders over a planted nuke (and that hook disarms, it does not lift).</summary>
        public static bool CanHook(in NukeState s, byte team)
        {
            if (team == TeamRules.None) return false;
            switch (s.Phase)
            {
                case NukePhase.AtSpawn:
                case NukePhase.Dropped:
                    return true;
                case NukePhase.Docked:
                    return s.ArmedBy != TeamRules.None || team != s.DockTeam;
                case NukePhase.Planted:
                    return team == s.DockTeam;
                default:
                    return false;
            }
        }

        /// <summary>Hook time for <paramref name="team"/>: the disarm time over a planted nuke, else the chopper's own.</summary>
        public static float HookSeconds(in NukeState s, float chopperSeconds, in NukeTuning tuning) =>
            s.Phase == NukePhase.Planted ? tuning.DefuseSeconds : chopperSeconds;

        /// <summary>Advances the nuke one tick. Returns the most important thing that happened (one per tick).</summary>
        public static NukeEvent Tick(ref NukeState s, in NukeInputs input, in NukeTuning tuning, float deltaTime)
        {
            if (s.Phase == NukePhase.Detonated) return NukeEvent.None;

            if (s.Phase == NukePhase.Planted)
            {
                if (input.Disarmed)
                {
                    // Disarmed in the defenders' base: a plain dropped nuke (back to the pad unless someone hooks it).
                    s.ArmedBy = TeamRules.None;
                    s.ArmProgress = 0f;
                    s.DefuseProgress = 0f;
                    s.Countdown = 0f;
                    s.DockTeam = TeamRules.None;
                    s.Phase = NukePhase.Dropped;
                    s.DroppedFor = 0f;
                    return NukeEvent.Defused;
                }
                s.Countdown = math.max(0f, s.Countdown - deltaTime);
                if (s.Countdown > 0f) return NukeEvent.None;
                s.Phase = NukePhase.Detonated;
                s.DetonatedBy = s.ArmedBy;
                return NukeEvent.Detonated;
            }

            var held = Hold(ref s, input.CarrierTeam);
            if (held != NukeEvent.None) return held;

            if (s.Phase == NukePhase.Carried && input.DockTeam != TeamRules.None)
            {
                if (input.DockTeam == s.CarrierTeam && s.ArmedBy == TeamRules.None)
                {
                    s.Phase = NukePhase.Docked;
                    s.DockTeam = s.CarrierTeam;
                    s.CarrierTeam = TeamRules.None;
                    return NukeEvent.Docked;
                }
                if (input.DockTeam != s.CarrierTeam && s.ArmedBy == s.CarrierTeam)
                {
                    s.Phase = NukePhase.Planted;
                    s.DockTeam = input.DockTeam;
                    s.CarrierTeam = TeamRules.None;
                    s.Countdown = tuning.PlantSeconds;
                    s.DefuseProgress = 0f;
                    return NukeEvent.Planted;
                }
            }

            if (s.Phase == NukePhase.Docked && s.ArmedBy == TeamRules.None && !input.ZoneContested)
            {
                s.ArmProgress = math.saturate(s.ArmProgress + deltaTime / math.max(tuning.ArmSeconds, 0.01f));
                if (s.ArmProgress >= Done)
                {
                    s.ArmedBy = s.DockTeam;
                    s.ArmProgress = 1f;
                    return NukeEvent.Armed;
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

            return NukeEvent.None;
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

        /// <summary>Pick up and drop. Hooking it out of the other team's dock steals it: arm progress and armed state go.</summary>
        private static NukeEvent Hold(ref NukeState s, byte carrier)
        {
            if (carrier != TeamRules.None)
            {
                if (s.Phase == NukePhase.Carried && s.CarrierTeam == carrier) return NukeEvent.None;
                var stolen = s.Phase == NukePhase.Docked && s.DockTeam != carrier;
                if (stolen || (s.ArmedBy != TeamRules.None && s.ArmedBy != carrier))
                {
                    s.ArmedBy = TeamRules.None;
                    s.ArmProgress = 0f;
                }
                // Not yet armed: lifting it out of the dock (or from anywhere) loses the arm progress.
                if (s.ArmedBy == TeamRules.None) s.ArmProgress = 0f;
                s.DefuseProgress = 0f;
                s.Phase = NukePhase.Carried;
                s.CarrierTeam = carrier;
                s.DockTeam = TeamRules.None;
                s.DroppedFor = 0f;
                return stolen ? NukeEvent.Stolen : NukeEvent.PickedUp;
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
