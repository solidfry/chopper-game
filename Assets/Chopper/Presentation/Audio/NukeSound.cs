using Chopper.Presentation.Hud;
using Chopper.Simulation.Match;
using UnityEngine;

namespace Chopper.Presentation.Audio
{
    /// <summary>Which short call a nuke change plays (<see cref="NukeSound.Stinger"/>).</summary>
    public enum NukeStinger : byte
    {
        None = 0,
        Hooked,
        Dropped,
        Armed,
        Defused,
        Reset,
    }

    /// <summary>
    /// Capture the Nuke sound rules (pure, tested). The blast is heard when its shock front reaches the listener (the
    /// same 340 m/s as the shake), as a close roar near the base and a dull, long rumble far away. A siren runs while a
    /// nuke is armed (louder for the team it threatens). Arm, defuse and hook progress beep faster as the bar fills.
    /// </summary>
    public static class NukeSound
    {
        /// <summary>Under this distance (m) the blast is all close roar.</summary>
        public const float NearDistance = 150f;
        /// <summary>From this distance (m) the blast is all far rumble.</summary>
        public const float FarDistance = 900f;
        /// <summary>The far rumble fades to <see cref="FarFloor"/> at this distance (m) and stays there: the whole map hears it.</summary>
        public const float MapDistance = 3000f;
        public const float FarFloor = 0.4f;

        /// <summary>Seconds between beeps at 0 and at full progress.</summary>
        public const float SlowBeep = 0.9f;
        public const float FastBeep = 0.12f;

        /// <summary>Siren volume for the team that armed the nuke, and for a player with no team.</summary>
        public const float OwnSiren = 0.4f;
        public const float NeutralSiren = 0.7f;

        /// <summary>Volumes (0..1) of the close and far blast cues at this distance from the blast.</summary>
        public static void BlastMix(float distance, out float near, out float far)
        {
            var k = Mathf.InverseLerp(NearDistance, FarDistance, distance);
            k = k * k * (3f - 2f * k);
            near = 1f - k;
            var fade = Mathf.InverseLerp(FarDistance, MapDistance, distance);
            far = Mathf.Lerp(0.3f, 1f, k) * Mathf.Lerp(1f, FarFloor, fade);
        }

        /// <summary>Seconds between two progress beeps; faster as <paramref name="progress"/> (0..1) fills.</summary>
        public static float BeepInterval(float progress)
        {
            var p = Mathf.Clamp01(progress);
            return Mathf.Lerp(SlowBeep, FastBeep, p * Mathf.Sqrt(p));
        }

        /// <summary>Siren volume (0..1) for a player of team <paramref name="self"/>; 0 when the nuke is not armed.</summary>
        public static float SirenVolume(in NukeState nuke, byte self)
        {
            if (nuke.ArmedBy == TeamRules.None || nuke.Phase == NukePhase.Detonated) return 0f;
            if (self == TeamRules.None) return NeutralSiren;
            return self == nuke.ArmedBy ? OwnSiren : 1f;
        }

        /// <summary>
        /// The progress bar that should beep now (arm, defuse or hook, 0..1), or a negative number for none. Only a bar
        /// that has grown since the last frame beeps: a bar that decays or holds stays quiet.
        /// </summary>
        public static float BeepProgress(in NukeState now, in NukeState before)
        {
            if (now.Phase == NukePhase.Detonated) return -1f;
            if (now.DefuseProgress > before.DefuseProgress) return now.DefuseProgress;
            if (now.ArmProgress > before.ArmProgress && now.ArmedBy == TeamRules.None) return now.ArmProgress;
            if (now.HookTeam != TeamRules.None && now.HookProgress > before.HookProgress) return now.HookProgress;
            return -1f;
        }

        /// <summary>The short call for a nuke change; the blast has its own sound.</summary>
        public static NukeStinger Stinger(NukeChange change) => change switch
        {
            NukeChange.PickedUp => NukeStinger.Hooked,
            NukeChange.Dropped => NukeStinger.Dropped,
            NukeChange.Armed => NukeStinger.Armed,
            NukeChange.Defused => NukeStinger.Defused,
            NukeChange.Reset => NukeStinger.Reset,
            _ => NukeStinger.None,
        };
    }
}
