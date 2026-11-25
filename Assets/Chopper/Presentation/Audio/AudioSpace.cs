using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;

namespace Chopper.Presentation.Audio
{
    /// <summary>
    /// How a kind of place sounds (owner, 25 Nov: "in a city the sound would reverberate off buildings"). One asset per
    /// map type (open ground, city), picked by <see cref="SoundLibrary.Space"/>. Two parts:
    /// <list type="bullet">
    /// <item>Reverb: the tail and echoes of the place, on every 3D sound (a reverb zone that follows the listener).</item>
    /// <item>Distance: far sounds get duller (air takes the highs) and wetter (more echo than direct sound).</item>
    /// </list>
    /// Values are in Unity's reverb units (mB = 1/100 dB; 0 = full, -10000 = off). Made by Chopper → Setup.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Audio/Audio Space", fileName = "AudioSpace")]
    public sealed class AudioSpace : ScriptableObject, IValidatedAsset
    {
        [Header("Reverb (the place)")]
        [SerializeField, Range(-10000, 0), Tooltip("Level of the whole effect (mB). Higher = more echo.")] private int room = -1000;
        [SerializeField, Range(-10000, 0), Tooltip("Highs in the effect (mB). Lower = darker echo.")] private int roomHF = -800;
        [SerializeField, Range(0.1f, 20f), Tooltip("How long the tail lasts (s).")] private float decayTime = 1.5f;
        [SerializeField, Range(0.1f, 2f), Tooltip("How long the highs last, as a share of the tail.")] private float decayHFRatio = 0.67f;
        [SerializeField, Range(-10000, 1000), Tooltip("First clear echoes off near walls (mB): the street slap.")] private int reflections = -2273;
        [SerializeField, Range(0f, 0.3f), Tooltip("Time to the first echoes (s). Short = walls are near.")] private float reflectionsDelay = 0.007f;
        [SerializeField, Range(-10000, 2000), Tooltip("The tail level (mB).")] private int reverb = -1691;
        [SerializeField, Range(0f, 0.1f), Tooltip("Time from the first echoes to the tail (s).")] private float reverbDelay = 0.011f;
        [SerializeField, Range(0f, 100f), Tooltip("Smoothness of the tail (%). Low = grainy, separate echoes.")] private float diffusion = 50f;
        [SerializeField, Range(0f, 100f), Tooltip("Thickness of the tail (%).")] private float density = 100f;

        [Header("Street echo (walls close round the listener)")]
        [SerializeField, Range(-10000, 1000), Tooltip("First echoes when closed in (mB). Higher = sharper slap off the walls.")]
        private int enclosedReflections = -400;
        [SerializeField, Range(-10000, 2000), Tooltip("Tail level when closed in (mB).")] private int enclosedReverb = -500;
        [SerializeField, Range(0.1f, 20f), Tooltip("Tail length when closed in (s).")] private float enclosedDecayTime = 2.6f;

        [Header("Building in the way")]
        [SerializeField, Range(200f, 22000f), Tooltip("Low-pass on a sound behind a building (Hz). Lower = more muffled.")]
        private float occludedCutoff = 900f;

        [Header("Distance")]
        [SerializeField, Min(1f), Tooltip("Closer than this (m), a sound is clear and mostly direct.")] private float nearDistance = 40f;
        [SerializeField, Min(2f), Tooltip("From this distance (m), a sound is at its dullest and wettest.")] private float farDistance = 1200f;
        [SerializeField, Range(200f, 22000f), Tooltip("Low-pass on a far sound (Hz). Lower = duller far sounds.")] private float farCutoff = 2500f;
        [SerializeField, Range(0f, 1.1f), Tooltip("Echo amount on a near sound.")] private float nearReverbMix = 0.5f;
        [SerializeField, Range(0f, 1.1f), Tooltip("Echo amount on a far sound.")] private float farReverbMix = 1.05f;

        public float NearDistance => nearDistance;
        public float FarDistance => farDistance;
        public float FarCutoff => farCutoff;
        public float NearReverbMix => nearReverbMix;
        public float FarReverbMix => farReverbMix;
        public float OccludedCutoff => occludedCutoff;

        /// <summary>
        /// Puts this place's reverb on the zone. <paramref name="enclosure"/> (0..1) blends toward the street echo, and
        /// the first echoes come later the further the walls are (<paramref name="wallDistance"/>, there and back).
        /// </summary>
        public void Apply(AudioReverbZone zone, float enclosure = 0f, float wallDistance = 50f)
        {
            var e = Mathf.Clamp01(enclosure);
            zone.reverbPreset = AudioReverbPreset.User;
            zone.room = room;
            zone.roomHF = roomHF;
            zone.roomLF = 0;
            zone.decayTime = Mathf.Lerp(decayTime, enclosedDecayTime, e);
            zone.decayHFRatio = decayHFRatio;
            zone.reflections = Mathf.RoundToInt(Mathf.Lerp(reflections, enclosedReflections, e));
            zone.reflectionsDelay = Mathf.Lerp(reflectionsDelay, AudioDistance.EchoDelay(wallDistance), e);
            zone.reverb = Mathf.RoundToInt(Mathf.Lerp(reverb, enclosedReverb, e));
            zone.reverbDelay = reverbDelay;
            zone.diffusion = diffusion;
            zone.density = density;
            zone.HFReference = 5000f;
            zone.LFReference = 250f;
        }

        public void CollectProblems(List<string> problems)
        {
            if (farDistance <= nearDistance) problems.Add($"{name}: far distance must be more than near distance.");
        }

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void Initialise(int level, int highs, float decay, float highsDecay, int echoes, float echoDelay, int tail, float tailDelay,
            float smooth, float far, float cutoff, float nearMix, float farMix, int streetEchoes, int streetTail, float streetDecay,
            float hiddenCutoff)
        {
            enclosedReflections = streetEchoes;
            enclosedReverb = streetTail;
            enclosedDecayTime = streetDecay;
            occludedCutoff = hiddenCutoff;
            room = level;
            roomHF = highs;
            decayTime = decay;
            decayHFRatio = highsDecay;
            reflections = echoes;
            reflectionsDelay = echoDelay;
            reverb = tail;
            reverbDelay = tailDelay;
            diffusion = smooth;
            farDistance = far;
            farCutoff = cutoff;
            nearReverbMix = nearMix;
            farReverbMix = farMix;
        }
    }

    /// <summary>Distance rules for <see cref="AudioSpace"/> (pure, tested).</summary>
    public static class AudioDistance
    {
        /// <summary>0 at or under near, 1 at or past far; eased, as hearing is roughly logarithmic in distance.</summary>
        public static float Share(float distance, float near, float far)
        {
            if (far <= near) return distance > near ? 1f : 0f;
            var k = Mathf.Clamp01(Mathf.Log(Mathf.Max(distance, near) / near) / Mathf.Log(far / near));
            return k;
        }

        /// <summary>Low-pass (Hz) for air at this distance: open near, <paramref name="farCutoff"/> far, in log steps.</summary>
        public static float AirCutoff(float distance, float near, float far, float farCutoff)
        {
            var k = Share(distance, near, far);
            return Mathf.Exp(Mathf.Lerp(Mathf.Log(AudioService.OpenCutoff), Mathf.Log(Mathf.Max(farCutoff, 10f)), k));
        }

        /// <summary>Speed of sound (m/s), for echo delays.</summary>
        public const float SoundSpeed = 343f;

        /// <summary>Time for an echo off a wall this far away (there and back), within the reverb's limit (0.3 s).</summary>
        public static float EchoDelay(float wallDistance) => Mathf.Clamp(2f * wallDistance / SoundSpeed, 0f, 0.3f);

        /// <summary>
        /// How closed in the listener is, from rays cast round it: each ray's hit fraction (0..1 of its length), or a
        /// negative number for a miss. A near wall counts more than a far one. 0 = open, 1 = close walls all round.
        /// </summary>
        public static float Enclosure(IReadOnlyList<float> hitFractions)
        {
            if (hitFractions.Count == 0) return 0f;
            var sum = 0f;
            foreach (var f in hitFractions)
                if (f >= 0f) sum += 1f - 0.6f * Mathf.Clamp01(f);
            return Mathf.Clamp01(sum / hitFractions.Count);
        }

        /// <summary>Volume share of a sound behind a building: half (-6 dB) when fully hidden.</summary>
        public static float OcclusionGain(float occlusion) => Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(occlusion));

        /// <summary>Low-pass (Hz) for a sound this hidden: open when clear, <paramref name="occludedCutoff"/> when hidden.</summary>
        public static float OccludedCutoff(float occlusion, float occludedCutoff) =>
            Mathf.Exp(Mathf.Lerp(Mathf.Log(AudioService.OpenCutoff), Mathf.Log(Mathf.Max(occludedCutoff, 10f)), Mathf.Clamp01(occlusion)));

        public static float ReverbMix(float distance, float near, float far, float nearMix, float farMix) =>
            Mathf.Lerp(nearMix, farMix, Share(distance, near, far));
    }
}
