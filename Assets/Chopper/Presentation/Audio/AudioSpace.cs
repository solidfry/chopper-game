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

        /// <summary>Puts this place's reverb on the zone.</summary>
        public void Apply(AudioReverbZone zone)
        {
            zone.reverbPreset = AudioReverbPreset.User;
            zone.room = room;
            zone.roomHF = roomHF;
            zone.roomLF = 0;
            zone.decayTime = decayTime;
            zone.decayHFRatio = decayHFRatio;
            zone.reflections = reflections;
            zone.reflectionsDelay = reflectionsDelay;
            zone.reverb = reverb;
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
            float smooth, float far, float cutoff, float nearMix, float farMix)
        {
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

        public static float ReverbMix(float distance, float near, float far, float nearMix, float farMix) =>
            Mathf.Lerp(nearMix, farMix, Share(distance, near, far));
    }
}
