using UnityEngine;

namespace Chopper.Editor.AudioEffects
{
    /// <summary>
    /// Serialized container for all audio effect settings.
    /// Save as Asset to reuse chains across sessions.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Audio/Audio Effect Chain", fileName = "NewAudioEffectChain")]
    public class AudioEffectChain : ScriptableObject
    {
        [Header("Distortion")]
        public bool distortionEnabled = true;
        public float distortionDrive = 0.7f;
        public float distortionMix = 1.0f;

        [Header("Bitcrush")]
        public bool bitcrushEnabled = true;
        [Range(1, 16)] public int bitcrushBits = 8;
        [Range(1000, 48000)] public int bitcrushSampleRate = 8000;

        [Header("Chorus")]
        public bool chorusEnabled = true;
        [Range(0.1f, 5.0f)] public float chorusRate = 1.5f;
        [Range(0.0f, 100.0f)] public float chorusDepth = 8.0f;
        [Range(0.0f, 0.9f)] public float chorusMix = 0.5f;

        [Header("Flanger")]
        public bool flangerEnabled = true;
        [Range(0.1f, 10.0f)] public float flangerRate = 2.0f;
        [Range(0.0f, 100.0f)] public float flangerDepth = 30.0f;
        [Range(0.0f, 0.95f)] public float flangerFeedback = 0.4f;
        [Range(0.0f, 1.0f)] public float flangerMix = 0.5f;

        [Header("Reverb")]
        public bool reverbEnabled = true;
        [Range(0.01f, 10.0f)] public float reverbDecay = 3.0f;
        [Range(0.0f, 1.0f)] public float reverbDamping = 0.3f;
        [Range(0.0f, 1.0f)] public float reverbMix = 0.4f;
        [Range(0.0f, 1.0f)] public float reverbDryLevel = 0.8f;

        [Header("Delay")]
        public bool delayEnabled = true;
        [Range(0.001f, 2.0f)] public float delayTime = 0.15f;
        [Range(0.0f, 0.95f)] public float delayFeedback = 0.3f;
        [Range(0.0f, 1.0f)] public float delayMix = 0.3f;
        public bool delayPingPong = false;

        [Header("EQ - Low Shelf")]
        public bool eqLowEnabled = true;
        [Range(20f, 500f)] public float eqLowFreq = 200f;
        public float eqLowGain = 6f;

        [Header("EQ - Peaking")]
        public bool eqMidEnabled = true;
        [Range(200f, 5000f)] public float eqMidFreq = 1000f;
        public float eqMidGain = -3f;
        [Range(0.1f, 10f)] public float eqMidQ = 1.5f;

        [Header("EQ - High Shelf")]
        public bool eqHighEnabled = true;
        [Range(3000f, 20000f)] public float eqHighFreq = 8000f;
        public float eqHighGain = 4f;

        [Header("Compression")]
        public bool compressionEnabled = true;
        [Range(0.0f, 1.0f)] public float compressionThreshold = -20f;
        [Range(1f, 20f)] public float compressionRatio = 4f;
        [Range(0.001f, 1f)] public float compressionAttack = 0.01f;
        [Range(0.001f, 1f)] public float compressionRelease = 0.1f;
        [Range(0.0f, 1.0f)] public float compressionMix = 1.0f;

        [Header("Pitch Shift")]
        public bool pitchEnabled = true;
        [Range(-24, 24)] public int pitchSemitones = -2;

        [Header("Wah-Wah")]
        public bool wahEnabled = true;
        [Range(0.1f, 20f)] public float wahRate = 2.0f;
        [Range(100f, 2000f)] public float wahDepth = 800f;
        [Range(1f, 20f)] public float wahResonance = 5f;
        [Range(0.0f, 1.0f)] public float wahMix = 0.5f;

        [Header("Saturation")]
        public bool saturationEnabled = true;
        [Range(0.0f, 10.0f)] public float saturationDrive = 1.5f;
        [Range(0.0f, 1.0f)] public float saturationMix = 0.8f;
    }
}
