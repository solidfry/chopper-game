using Chopper.Simulation.Match;
using Unity.Mathematics;

namespace Chopper.Presentation.Hud
{
    /// <summary>How a callout line reads: plain (what to do), good for us (green), or a threat (red).</summary>
    public enum CalloutTone : byte
    {
        None = 0,
        Neutral = 1,
        Good = 2,
        Alert = 3,
    }

    /// <summary>A change of the nuke that every player hears about (HUD flash and a call).</summary>
    public enum NukeChange : byte
    {
        None = 0,
        PickedUp,
        Dropped,
        Armed,
        Defused,
        Reset,
        Detonated,
        /// <summary>Set down in a team's own dock: arming starts.</summary>
        Docked,
        /// <summary>Set down armed in the enemy base: the final countdown starts.</summary>
        Planted,
        /// <summary>Hooked out of the other team's dock: their arm is lost.</summary>
        Stolen,
    }

    /// <summary>One line of HUD text, with an optional 0..1 progress bar (negative = no bar).</summary>
    public readonly struct Callout
    {
        public readonly string Text;
        public readonly float Progress;
        public readonly CalloutTone Tone;

        public Callout(string text, CalloutTone tone, float progress = -1f)
        {
            Text = text;
            Tone = tone;
            Progress = progress;
        }

        public static Callout Empty => new("", CalloutTone.None);
    }

    /// <summary>The nuke countdown every player sees (owner, 25 Nov): a state name and the seconds left.</summary>
    public readonly struct NukeTimer
    {
        public readonly string Label;
        public readonly float Seconds;
        public readonly CalloutTone Tone;
        /// <summary>True for the final countdown (the HUD makes it stand out more).</summary>
        public readonly bool Final;

        public NukeTimer(string label, float seconds, CalloutTone tone, bool final = false)
        {
            Label = label;
            Seconds = seconds;
            Tone = tone;
            Final = final;
        }

        public bool Shown => !string.IsNullOrEmpty(Label);
        public static NukeTimer None => default;

        /// <summary>m:ss, rounded up (a timer never shows 0:00 while time is left).</summary>
        public string Clock
        {
            get
            {
                var whole = (int)math.ceil(math.max(0f, Seconds));
                return $"{whole / 60}:{whole % 60:00}";
            }
        }
    }

    /// <summary>
    /// Capture the Nuke centre text (owner's CTN step 4b): what the local player should do now, from the replicated
    /// <see cref="NukeState"/>, and short flashes when the nuke changes hands. Pure: EditMode tests cover it.
    /// </summary>
    public static class NukeCallout
    {
        /// <summary>
        /// The standing line. <paramref name="carrying"/>: the local chopper holds the nuke. <paramref name="inRange"/>:
        /// the local chopper is within its own hook range.
        /// </summary>
        public static Callout Status(in NukeState s, byte self, bool carrying, bool inRange, float dropResetSeconds)
        {
            if (self == TeamRules.None || s.Phase == NukePhase.Detonated) return Callout.Empty;
            var hooking = s.HookTeam != TeamRules.None && s.HookProgress > 0f;

            switch (s.Phase)
            {
                case NukePhase.AtSpawn:
                case NukePhase.Dropped:
                {
                    if (hooking)
                        return s.HookTeam == self
                            ? new Callout(inRange ? $"HOOKING  {Percent(s.HookProgress)}" : "ALLY HOOKING THE NUKE", CalloutTone.Good, s.HookProgress)
                            : new Callout("ENEMY HOOKING THE NUKE", CalloutTone.Alert, s.HookProgress);
                    if (inRange) return new Callout("HOLD POSITION TO HOOK", CalloutTone.Good);
                    if (s.Phase == NukePhase.AtSpawn) return new Callout("HOOK THE NUKE", CalloutTone.Neutral);
                    var reset = (int)math.ceil(math.max(0f, dropResetSeconds - s.DroppedFor));
                    return s.ArmedBy == TeamRules.None ? new Callout($"NUKE DROPPED · RESET IN {reset}", CalloutTone.Neutral)
                        : s.ArmedBy == self ? new Callout($"ARMED NUKE DROPPED · RECOVER IT · {reset}", CalloutTone.Alert)
                        : new Callout($"ENEMY ARMED NUKE DROPPED · HOOK IT · {reset}", CalloutTone.Good);
                }

                case NukePhase.Docked:
                {
                    var ourDock = s.DockTeam == self;
                    if (hooking)
                    {
                        if (s.HookTeam == self)
                            return new Callout(ourDock ? $"LIFTING  {Percent(s.HookProgress)}" : $"STEALING  {Percent(s.HookProgress)}", CalloutTone.Good, s.HookProgress);
                        return ourDock
                            ? new Callout("ENEMY STEALING YOUR NUKE · STOP THEM", CalloutTone.Alert, s.HookProgress)
                            : new Callout("ENEMY LIFTING THEIR NUKE", CalloutTone.Alert, s.HookProgress);
                    }
                    if (s.ArmedBy == TeamRules.None)
                        return ourDock
                            ? new Callout($"ARMING  {Percent(s.ArmProgress)} · DEFEND THE DOCK", CalloutTone.Good, s.ArmProgress)
                            : new Callout(inRange ? "HOLD POSITION TO STEAL" : $"ENEMY ARMING  {Percent(s.ArmProgress)} · STEAL THE NUKE", CalloutTone.Alert, s.ArmProgress);
                    if (inRange) return new Callout(ourDock ? "HOLD POSITION TO HOOK" : "HOLD POSITION TO STEAL", CalloutTone.Good);
                    return ourDock
                        ? new Callout("NUKE ARMED · HOOK IT FROM THE DOCK", CalloutTone.Good)
                        : new Callout("ENEMY NUKE ARMED · STEAL IT", CalloutTone.Alert);
                }

                case NukePhase.Planted:
                {
                    var defending = s.DockTeam == self;
                    var disarming = s.DefuseProgress > 0f && s.HookTeam != TeamRules.None;
                    if (defending)
                    {
                        if (disarming) return new Callout($"DISARMING  {Percent(s.DefuseProgress)}", CalloutTone.Good, s.DefuseProgress);
                        return new Callout(inRange ? "HOLD POSITION TO DISARM" : "NUKE PLANTED IN YOUR BASE · DISARM IT", CalloutTone.Alert);
                    }
                    return disarming
                        ? new Callout($"ENEMY DISARMING  {Percent(s.DefuseProgress)} · STOP THEM", CalloutTone.Alert, s.DefuseProgress)
                        : new Callout("NUKE PLANTED · DEFEND IT", CalloutTone.Good);
                }
            }

            // Carried.
            var ours = s.CarrierTeam == self;
            if (s.ArmedBy == TeamRules.None)
            {
                if (!ours) return new Callout("ENEMY HAS THE NUKE · SHOOT THE CARRIER", CalloutTone.Alert);
                return carrying ? new Callout("SET THE NUKE DOWN IN YOUR BASE DOCK", CalloutTone.Good)
                    : new Callout("ESCORT YOUR CARRIER HOME", CalloutTone.Good);
            }
            if (ours)
                return new Callout(carrying ? "NUKE ARMED · PLANT IT IN THE ENEMY DOCK" : "NUKE ARMED · ESCORT YOUR CARRIER", CalloutTone.Good);
            return new Callout("ARMED NUKE INBOUND · SHOOT THE CARRIER", CalloutTone.Alert);
        }

        /// <summary>
        /// The countdown for every player (owner, 25 Nov): arming in a dock, the final countdown of a planted nuke, a
        /// dropped nuke's reset. None in other states.
        /// </summary>
        public static NukeTimer Timer(in NukeState s, byte self, in NukeTuning tuning)
        {
            switch (s.Phase)
            {
                case NukePhase.Docked when s.ArmedBy == TeamRules.None:
                    var left = (1f - math.saturate(s.ArmProgress)) * tuning.ArmSeconds;
                    // No team yet (spectator, or before Team replicates): neutral, not "enemy".
                    if (self == TeamRules.None) return new NukeTimer("ARMING", left, CalloutTone.Neutral);
                    return s.DockTeam == self
                        ? new NukeTimer("ARMING", left, CalloutTone.Good)
                        : new NukeTimer("ENEMY ARMING", left, CalloutTone.Alert);
                case NukePhase.Planted:
                    var tone = self == TeamRules.None ? CalloutTone.Neutral : s.DockTeam == self ? CalloutTone.Alert : CalloutTone.Good;
                    return new NukeTimer("DETONATION", s.Countdown, tone, final: true);
                case NukePhase.Dropped:
                    return new NukeTimer("NUKE RESET", math.max(0f, tuning.DropResetSeconds - s.DroppedFor), CalloutTone.Neutral);
                default:
                    return NukeTimer.None;
            }
        }

        /// <summary>What changed between two replicated states, and the team that did it (0 = nobody).</summary>
        public static NukeChange Diff(in NukeState before, in NukeState after, out byte team)
        {
            team = TeamRules.None;
            if (before.Phase == NukePhase.Detonated) return NukeChange.None;  // next round: a fresh nuke on the pad
            if (after.Phase == NukePhase.Detonated)
            {
                team = after.DetonatedBy;
                return NukeChange.Detonated;
            }
            if (after.Phase == NukePhase.Planted && before.Phase != NukePhase.Planted)
            {
                team = after.ArmedBy;
                return NukeChange.Planted;
            }
            if (before.Phase == NukePhase.Planted && after.Phase != NukePhase.Planted)
            {
                // Only a disarm drops the nuke. A blast resets it to the pad in the same server tick, so clients see
                // Planted -> AtSpawn: that is not a defuse (the blast comes as NukeDetonatedRpc).
                if (after.Phase != NukePhase.Dropped) return NukeChange.None;
                team = before.DockTeam;
                return NukeChange.Defused;
            }
            if (before.ArmedBy == TeamRules.None && after.ArmedBy != TeamRules.None)
            {
                team = after.ArmedBy;
                return NukeChange.Armed;
            }
            if (after.Phase == NukePhase.Docked && before.Phase != NukePhase.Docked)
            {
                team = after.DockTeam;
                return NukeChange.Docked;
            }
            if (before.Phase == NukePhase.Docked && after.Phase == NukePhase.Carried && after.CarrierTeam != before.DockTeam)
            {
                team = after.CarrierTeam;
                return NukeChange.Stolen;
            }
            if (after.Phase == NukePhase.Carried && (before.Phase != NukePhase.Carried || before.CarrierTeam != after.CarrierTeam))
            {
                team = after.CarrierTeam;
                return NukeChange.PickedUp;
            }
            if (before.Phase == NukePhase.Carried && after.Phase == NukePhase.Dropped)
            {
                team = before.CarrierTeam;
                return NukeChange.Dropped;
            }
            if (before.Phase == NukePhase.Dropped && after.Phase == NukePhase.AtSpawn) return NukeChange.Reset;
            return NukeChange.None;
        }

        /// <summary>The short flash for a change, from the local team's point of view.</summary>
        public static Callout Flash(NukeChange change, byte team, byte self)
        {
            var ours = team != TeamRules.None && team == self;
            return change switch
            {
                NukeChange.PickedUp => ours ? new Callout("NUKE HOOKED", CalloutTone.Good) : new Callout("ENEMY HOOKED THE NUKE", CalloutTone.Alert),
                NukeChange.Dropped => ours ? new Callout("NUKE LOST", CalloutTone.Alert) : new Callout("ENEMY DROPPED THE NUKE", CalloutTone.Good),
                NukeChange.Docked => ours ? new Callout("NUKE DOCKED · ARMING", CalloutTone.Good) : new Callout("ENEMY DOCKED THE NUKE · ARMING", CalloutTone.Alert),
                NukeChange.Stolen => ours ? new Callout("NUKE STOLEN", CalloutTone.Good) : new Callout("ENEMY STOLE YOUR NUKE", CalloutTone.Alert),
                NukeChange.Armed => ours ? new Callout("NUKE ARMED", CalloutTone.Good) : new Callout("ENEMY ARMED THE NUKE", CalloutTone.Alert),
                NukeChange.Planted => ours ? new Callout("NUKE PLANTED · DEFEND IT", CalloutTone.Good) : new Callout("NUKE PLANTED IN YOUR BASE · DISARM IT", CalloutTone.Alert),
                NukeChange.Defused => ours ? new Callout("NUKE DISARMED", CalloutTone.Good) : new Callout("ENEMY DISARMED YOUR NUKE", CalloutTone.Alert),
                NukeChange.Reset => new Callout("NUKE BACK ON THE PAD", CalloutTone.Neutral),
                NukeChange.Detonated => ours ? new Callout("ENEMY BASE DESTROYED · ROUND WON", CalloutTone.Good)
                    : new Callout("BASE DESTROYED · ROUND LOST", CalloutTone.Alert),
                _ => Callout.Empty,
            };
        }

        private static string Percent(float progress) => $"{(int)math.round(math.saturate(progress) * 100f)}%";
    }
}
