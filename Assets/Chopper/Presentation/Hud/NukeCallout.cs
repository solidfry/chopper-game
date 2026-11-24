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

    /// <summary>A change of the nuke that every player hears about (HUD flash, later a sound).</summary>
    public enum NukeChange : byte
    {
        None = 0,
        PickedUp,
        Dropped,
        Armed,
        Defused,
        Reset,
        Detonated,
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

    /// <summary>
    /// Capture the Nuke centre text (owner's CTN step 4b): what the local player should do now, from the replicated
    /// <see cref="NukeState"/>, and short flashes when the nuke changes hands. Pure: EditMode tests cover it.
    /// </summary>
    public static class NukeCallout
    {
        /// <summary>
        /// The standing line. <paramref name="zoneTeam"/>: whose base zone the nuke is in (0 = none). <paramref name="carrying"/>:
        /// the local chopper holds the nuke. <paramref name="inRange"/>: the local chopper is within its own hook range.
        /// </summary>
        public static Callout Status(in NukeState s, byte self, byte zoneTeam, bool carrying, bool inRange, float dropResetSeconds)
        {
            if (self == TeamRules.None || s.Phase == NukePhase.Detonated) return Callout.Empty;

            if (s.Phase is NukePhase.AtSpawn or NukePhase.Dropped)
            {
                if (s.HookTeam != TeamRules.None && s.HookProgress > 0f)
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

            // Carried.
            var ours = s.CarrierTeam == self;
            if (s.ArmedBy == TeamRules.None)
            {
                var arming = zoneTeam != TeamRules.None && zoneTeam == s.CarrierTeam;
                if (!ours)
                    return arming
                        ? new Callout($"ENEMY ARMING  {Percent(s.ArmProgress)} · STOP THEM", CalloutTone.Alert, s.ArmProgress)
                        : new Callout("ENEMY HAS THE NUKE · SHOOT THE CARRIER", CalloutTone.Alert);
                if (arming) return new Callout($"ARMING  {Percent(s.ArmProgress)} · HOLD IN BASE", CalloutTone.Good, s.ArmProgress);
                return carrying ? new Callout("FLY THE NUKE TO YOUR BASE", CalloutTone.Good)
                    : new Callout("ESCORT YOUR CARRIER HOME", CalloutTone.Good);
            }

            if (s.ArmedBy == self)
                return ours
                    ? new Callout(carrying ? "NUKE ARMED · FLY TO THE ENEMY BASE" : "NUKE ARMED · ESCORT YOUR CARRIER", CalloutTone.Good)
                    : s.DefuseProgress > 0f
                        ? new Callout($"ENEMY DEFUSING  {Percent(s.DefuseProgress)} · STOP THEM", CalloutTone.Alert, s.DefuseProgress)
                        : new Callout("ENEMY HAS YOUR ARMED NUKE · GET IT BACK", CalloutTone.Alert);

            // Armed by the enemy.
            if (!ours) return new Callout("ARMED NUKE INBOUND · DEFEND YOUR BASE", CalloutTone.Alert);
            if (s.DefuseProgress > 0f) return new Callout($"DEFUSING  {Percent(s.DefuseProgress)}", CalloutTone.Good, s.DefuseProgress);
            return new Callout(carrying ? "TAKE IT TO YOUR BASE TO DEFUSE" : "ESCORT IT HOME TO DEFUSE", CalloutTone.Good);
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
            if (before.ArmedBy == TeamRules.None && after.ArmedBy != TeamRules.None)
            {
                team = after.ArmedBy;
                return NukeChange.Armed;
            }
            if (before.ArmedBy != TeamRules.None && after.ArmedBy == TeamRules.None && after.Phase == NukePhase.Carried)
            {
                team = after.CarrierTeam;
                return NukeChange.Defused;
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
                NukeChange.Armed => ours ? new Callout("NUKE ARMED", CalloutTone.Good) : new Callout("ENEMY ARMED THE NUKE", CalloutTone.Alert),
                NukeChange.Defused => ours ? new Callout("NUKE DEFUSED", CalloutTone.Good) : new Callout("ENEMY DEFUSED YOUR NUKE", CalloutTone.Alert),
                NukeChange.Reset => new Callout("NUKE BACK ON THE PAD", CalloutTone.Neutral),
                _ => Callout.Empty,  // a detonation has its own banner and blast
            };
        }

        private static string Percent(float progress) => $"{(int)math.round(math.saturate(progress) * 100f)}%";
    }
}
