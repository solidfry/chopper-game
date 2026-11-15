using UnityEngine;

namespace Chopper.Editor.AudioEffects
{
    /// <summary>
    /// Applies cyberpunk audio effects to an AudioClip.
    /// Pure C# DSP - no Unity Audio Mixer dependency.
    ///
    /// Processed output is rendered STEREO at a fixed output rate so every
    /// effect (chorus/flanger/delay/reverb/pitch) can work on separate
    /// channels without corrupting interleaved data.
    /// </summary>
    public static class AudioEffectProcessor
    {
        // ── Public API ─────────────────────────────────────────────

        public static AudioClip ApplyEffects(AudioClip original, AudioEffectChain chain)
        {
            int channels = original.channels > 1 ? 2 : 1;
            int sourceRate = original.frequency;

            // Extract per-channel sample data (source may be mono or stereo)
            float[] left = new float[original.samples];
            float[] right = new float[original.samples];

            float[] raw = new float[original.samples * original.channels];
            original.GetData(raw, 0);

            int srcChannels = original.channels;
            for (int i = 0; i < original.samples; i++)
            {
                left[i] = raw[i * srcChannels];
                right[i] = srcChannels > 1 ? raw[i * srcChannels + 1] : raw[i * srcChannels];
            }

            // 1-6: EQ, distortion, saturation, bitcrush (per sample, no time dependency)
            ApplyEQ(chain, left, right, sourceRate);
            ApplyDistortionStage(left, right, chain);
            ApplySaturationStage(left, right, chain);
            ApplyBitcrushStage(left, right, chain, sourceRate);

            // 7: Compression (per channel)
            if (chain.compressionEnabled)
            {
                CompressChannel(left, chain, sourceRate);
                CompressChannel(right, chain, sourceRate);
            }

            // 8: Chorus (per channel, independent LFO phase offset for stereo width)
            if (chain.chorusEnabled)
            {
                ChorusChannel(left, chain, sourceRate, 0f);
                ChorusChannel(right, chain, sourceRate, 0.25f);
            }

            // 9: Flanger (per channel)
            if (chain.flangerEnabled)
            {
                FlangerChannel(left, chain, sourceRate, 0f);
                FlangerChannel(right, chain, sourceRate, 0.5f);
            }

            // 10: Wah-Wah (per channel)
            if (chain.wahEnabled)
            {
                WahChannel(left, chain, sourceRate);
                WahChannel(right, chain, sourceRate);
            }

            // 11: Delay (stereo aware, ping-pong swaps channels)
            if (chain.delayEnabled)
                DelayStage(left, right, chain, sourceRate);

            // 12: Reverb (true stereo convolution via shared drain buffer)
            if (chain.reverbEnabled)
                ReverbStage(left, right, chain, sourceRate);

            // 13: Pitch shift (resample + trim/pad back to original length)
            if (chain.pitchEnabled && chain.pitchSemitones != 0)
            {
                left = PitchShiftChannel(left, chain.pitchSemitones, sourceRate);
                right = PitchShiftChannel(right, chain.pitchSemitones, sourceRate);
            }

            // 14: Level normalisation then soft anti-clip limiter
            float peak = PeakOf(left, right);
            if (peak > 1e-5f)
            {
                float gain = kTargetPeak / peak;
                if (gain > 1f) gain = Mathf.Min(gain, kMaxMakeupGain);
                for (int i = 0; i < left.Length; i++)
                {
                    left[i] *= gain;
                    right[i] *= gain;
                }
            }
            Limit(left);
            Limit(right);

            // Interleave
            int frames = left.Length;
            float[] outSamples = new float[frames * channels];
            for (int i = 0; i < frames; i++)
            {
                outSamples[i * channels] = left[i];
                if (channels > 1)
                    outSamples[i * channels + 1] = right[i];
            }

            AudioClip result = AudioClip.Create(
                original.name,
                frames,
                channels,
                sourceRate,
                false);
            result.SetData(outSamples, 0);
            return result;
        }

        // ── Shared helpers ─────────────────────────────────────────

        const float kTargetPeak = 0.89f;
        const float kMaxMakeupGain = 12f;

        static float PeakOf(float[] left, float[] right)
        {
            float peak = 0f;
            for (int i = 0; i < left.Length; i++)
            {
                float a = left[i] < 0f ? -left[i] : left[i];
                if (a > peak) peak = a;
                float b = right[i] < 0f ? -right[i] : right[i];
                if (b > peak) peak = b;
            }
            return peak;
        }

        /// <summary>Bounded limiter: transparent below 0.9, soft-knee above.</summary>
        static void Limit(float[] buffer)
        {
            const float knee = 0.9f;
            for (int i = 0; i < buffer.Length; i++)
            {
                float v = buffer[i];
                float a = v < 0f ? -v : v;
                if (a > knee)
                {
                    float excess = a - knee;
                    float range = 1f - knee;
                    float compressed = range * (float)System.Math.Tanh(excess / range);
                    float signed = v < 0f ? -(knee + compressed) : (knee + compressed);
                    buffer[i] = signed;
                }
            }
        }

        static float ProcessSample(float input, AudioEffectChain chain)
        {
            float x = input;

            if (chain.distortionEnabled)
            {
                float d = 1f + chain.distortionDrive * 20f;
                float wet = (float)System.Math.Tanh(x * d);
                x = x * (1f - chain.distortionMix) + wet * chain.distortionMix;
            }

            if (chain.saturationEnabled)
            {
                float d = 1f + chain.saturationDrive * 5f;
                float v = x * d;
                float wet;
                if (v > 1f) wet = 2f - 1f / v;
                else if (v < -1f) wet = -2f + 1f / (-v);
                else wet = v;
                wet /= d;
                x = x * (1f - chain.saturationMix) + wet * chain.saturationMix;
            }

            return x;
        }

        // ── 1-3. EQ ───────────────────────────────────────────────

        static void ApplyEQ(AudioEffectChain chain, float[] left, float[] right, float sr)
        {
            if (chain.eqLowEnabled)
            {
                Biquad low = Biquad.LowShelf(chain.eqLowFreq, chain.eqLowGain, sr);
                low.Process(left);
                low.Process(right);
            }
            if (chain.eqMidEnabled)
            {
                Biquad mid = Biquad.Peaking(chain.eqMidFreq, chain.eqMidGain, chain.eqMidQ, sr);
                mid.Process(left);
                mid.Process(right);
            }
            if (chain.eqHighEnabled)
            {
                Biquad high = Biquad.HighShelf(chain.eqHighFreq, chain.eqHighGain, sr);
                high.Process(left);
                high.Process(right);
            }
        }

        /// <summary>Standard RBJ-cookbook biquad, applied in place.</summary>
        struct Biquad
        {
            float _b0, _b1, _b2, _a1, _a2;

            public static Biquad LowShelf(float freq, float gainDb, float sr)
            {
                float A = Mathf.Pow(10f, gainDb / 40f);
                float w0 = 2f * Mathf.PI * Mathf.Clamp(freq, 20f, sr * 0.45f) / sr;
                float cos = Mathf.Cos(w0);
                float sin = Mathf.Sin(w0);
                float S = 1f;
                float alpha = sin / 2f * Mathf.Sqrt((A + 1f / A) * (1f / S - 1f) + 2f);
                float twoSqrtAalpha = 2f * Mathf.Sqrt(A) * alpha;

                float b0 = A * ((A + 1f) - (A - 1f) * cos + twoSqrtAalpha);
                float b1 = 2f * A * ((A - 1f) - (A + 1f) * cos);
                float b2 = A * ((A + 1f) - (A - 1f) * cos - twoSqrtAalpha);
                float a0 = (A + 1f) + (A - 1f) * cos + twoSqrtAalpha;
                float a1 = -2f * ((A - 1f) + (A + 1f) * cos);
                float a2 = (A + 1f) + (A - 1f) * cos - twoSqrtAalpha;
                return Normalize(b0, b1, b2, a0, a1, a2);
            }

            public static Biquad HighShelf(float freq, float gainDb, float sr)
            {
                float A = Mathf.Pow(10f, gainDb / 40f);
                float w0 = 2f * Mathf.PI * Mathf.Clamp(freq, 20f, sr * 0.45f) / sr;
                float cos = Mathf.Cos(w0);
                float sin = Mathf.Sin(w0);
                float S = 1f;
                float alpha = sin / 2f * Mathf.Sqrt((A + 1f / A) * (1f / S - 1f) + 2f);
                float twoSqrtAalpha = 2f * Mathf.Sqrt(A) * alpha;

                float b0 = A * ((A + 1f) + (A - 1f) * cos + twoSqrtAalpha);
                float b1 = -2f * A * ((A - 1f) + (A + 1f) * cos);
                float b2 = A * ((A + 1f) + (A - 1f) * cos - twoSqrtAalpha);
                float a0 = (A + 1f) - (A - 1f) * cos + twoSqrtAalpha;
                float a1 = 2f * ((A - 1f) - (A + 1f) * cos);
                float a2 = (A + 1f) - (A - 1f) * cos - twoSqrtAalpha;
                return Normalize(b0, b1, b2, a0, a1, a2);
            }

            public static Biquad Peaking(float freq, float gainDb, float q, float sr)
            {
                float A = Mathf.Pow(10f, gainDb / 40f);
                float w0 = 2f * Mathf.PI * Mathf.Clamp(freq, 20f, sr * 0.45f) / sr;
                float cos = Mathf.Cos(w0);
                float sin = Mathf.Sin(w0);
                float alpha = sin / (2f * Mathf.Max(q, 0.1f));

                float b0 = 1f + alpha * A;
                float b1 = -2f * cos;
                float b2 = 1f - alpha * A;
                float a0 = 1f + alpha / A;
                float a1 = -2f * cos;
                float a2 = 1f - alpha / A;
                return Normalize(b0, b1, b2, a0, a1, a2);
            }

            static Biquad Normalize(float b0, float b1, float b2, float a0, float a1, float a2)
            {
                return new Biquad
                {
                    _b0 = b0 / a0,
                    _b1 = b1 / a0,
                    _b2 = b2 / a0,
                    _a1 = a1 / a0,
                    _a2 = a2 / a0
                };
            }

            public void Process(float[] buffer)
            {
                float x1 = 0f, x2 = 0f, y1 = 0f, y2 = 0f;
                for (int i = 0; i < buffer.Length; i++)
                {
                    float x = buffer[i];
                    float y = _b0 * x + _b1 * x1 + _b2 * x2 - _a1 * y1 - _a2 * y2;
                    x2 = x1; x1 = x; y2 = y1; y1 = y;
                    buffer[i] = y;
                }
            }
        }

        // ── 4-5. Distortion / Saturation ──────────────────────────

        static void ApplyDistortionStage(float[] left, float[] right, AudioEffectChain chain)
        {
            if (!chain.distortionEnabled && !chain.saturationEnabled) return;
            for (int i = 0; i < left.Length; i++)
            {
                left[i] = ProcessSample(left[i], chain);
                right[i] = ProcessSample(right[i], chain);
            }
        }

        static void ApplySaturationStage(float[] left, float[] right, AudioEffectChain chain)
        {
            // Saturation is folded into ProcessSample; nothing extra to do.
        }

        // ── 6. Bitcrush ───────────────────────────────────────────

        static void ApplyBitcrushStage(float[] left, float[] right, AudioEffectChain chain, float sr)
        {
            if (!chain.bitcrushEnabled) return;

            float steps = Mathf.Pow(2f, Mathf.Clamp(chain.bitcrushBits, 1, 16) - 1);
            int hold = Mathf.Max(1, Mathf.RoundToInt(sr / Mathf.Max(chain.bitcrushSampleRate, 1000f)));

            BitcrushChannel(left, steps, hold);
            BitcrushChannel(right, steps, hold);
        }

        static void BitcrushChannel(float[] buffer, float steps, int hold)
        {
            float held = 0f;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (i % hold == 0)
                    held = Mathf.Round(buffer[i] * steps) / steps;
                buffer[i] = held;
            }
        }

        // ── 7. Compression ────────────────────────────────────────

        static void CompressChannel(float[] buffer, AudioEffectChain chain, float sr)
        {
            float threshold = Mathf.Pow(10f, chain.compressionThreshold / 20f);
            float ratio = Mathf.Max(chain.compressionRatio, 1f);
            float attack = Mathf.Exp(-1f / Mathf.Max(sr * chain.compressionAttack, 1f));
            float release = Mathf.Exp(-1f / Mathf.Max(sr * chain.compressionRelease, 1f));
            float mix = chain.compressionMix;

            float env = 0f;
            for (int i = 0; i < buffer.Length; i++)
            {
                float abs = Mathf.Abs(buffer[i]);
                env = abs > env
                    ? attack * env + (1f - attack) * abs
                    : release * env + (1f - release) * abs;

                float targetGain = 1f;
                if (env > threshold && env > 1e-6f)
                {
                    // Gain reduction in linear domain
                    float overDb = 20f * Mathf.Log10(env / threshold);
                    float reducedDb = overDb * (1f - 1f / ratio);
                    targetGain = Mathf.Pow(10f, -reducedDb / 20f);
                }

                float wet = buffer[i] * targetGain;
                buffer[i] = buffer[i] * (1f - mix) + wet * mix;
            }
        }

        // ── 8. Chorus ─────────────────────────────────────────────

        static void ChorusChannel(float[] buffer, AudioEffectChain chain, float sr, float phaseOffset)
        {
            float rate = Mathf.Max(chain.chorusRate, 0.01f);
            float depthMs = Mathf.Clamp(chain.chorusDepth, 0f, 30f);
            float mix = Mathf.Clamp01(chain.chorusMix);
            int maxDelay = Mathf.Max(4, Mathf.RoundToInt((2f + depthMs * 1.5f) * sr / 1000f) + 2);

            float[] delayLine = new float[maxDelay];
            int writePos = 0;

            for (int i = 0; i < buffer.Length; i++)
            {
                float lfo = Mathf.Sin(2f * Mathf.PI * rate * i / sr + phaseOffset * 2f * Mathf.PI);
                float delayMs = 12f + lfo * depthMs;
                float delaySamples = Mathf.Clamp(delayMs * sr / 1000f, 1f, maxDelay - 2f);

                // Linear interpolation read
                float readPosF = writePos - delaySamples;
                while (readPosF < 0f) readPosF += maxDelay;
                int r0 = (int)readPosF;
                float frac = readPosF - r0;
                int r1 = (r0 + 1) % maxDelay;
                float wet = delayLine[r0] * (1f - frac) + delayLine[r1] * frac;

                delayLine[writePos] = buffer[i];
                writePos = (writePos + 1) % maxDelay;

                buffer[i] = buffer[i] * (1f - mix) + wet * mix;
            }
        }

        // ── 9. Flanger ────────────────────────────────────────────

        static void FlangerChannel(float[] buffer, AudioEffectChain chain, float sr, float phaseOffset)
        {
            float rate = Mathf.Max(chain.flangerRate, 0.01f);
            float depthMs = Mathf.Clamp(chain.flangerDepth, 0f, 8f);
            float feedback = Mathf.Clamp(chain.flangerFeedback, 0f, 0.9f);
            float mix = Mathf.Clamp01(chain.flangerMix);
            int maxDelay = Mathf.Max(4, Mathf.RoundToInt((1f + depthMs) * sr / 1000f) + 2);

            float[] delayLine = new float[maxDelay];
            int writePos = 0;
            double phase = phaseOffset;

            for (int i = 0; i < buffer.Length; i++)
            {
                phase += rate / sr;
                if (phase > 1.0) phase -= 1.0;

                float lfo = Mathf.Sin((float)(2.0 * System.Math.PI * phase));
                float delayMs = 1.5f + (0.5f + 0.5f * lfo) * depthMs;
                float delaySamples = Mathf.Clamp(delayMs * sr / 1000f, 1f, maxDelay - 2f);

                float readPosF = writePos - delaySamples;
                while (readPosF < 0f) readPosF += maxDelay;
                int r0 = (int)readPosF;
                float frac = readPosF - r0;
                int r1 = (r0 + 1) % maxDelay;
                float wet = delayLine[r0] * (1f - frac) + delayLine[r1] * frac;

                delayLine[writePos] = buffer[i] + wet * feedback;
                writePos = (writePos + 1) % maxDelay;

                buffer[i] = buffer[i] * (1f - mix) + wet * mix;
            }
        }

        // ── 10. Wah-Wah ───────────────────────────────────────────

        static void WahChannel(float[] buffer, AudioEffectChain chain, float sr)
        {
            float rate = Mathf.Max(chain.wahRate, 0.01f);
            float depth = Mathf.Clamp(chain.wahDepth, 100f, 2000f);
            float mix = Mathf.Clamp01(chain.wahMix);

            // State-variable bandpass state
            float low = 0f, band = 0f;
            int blockSize = 32;
            float b0 = 0f, b1 = 0f, b2 = 0f, a1 = 0f, a2 = 0f;
            int blockStart = -1;

            float[] wetBuf = new float[buffer.Length];

            for (int i = 0; i < buffer.Length; i++)
            {
                if (i - blockStart >= blockSize)
                {
                    blockStart = i;
                    float lfo = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * rate * i / sr);
                    float cutoff = Mathf.Clamp(250f + lfo * depth, 100f, sr * 0.45f);
                    Biquad bp = Biquad.Peaking(cutoff, 12f, chain.wahResonance, sr);
                    // Reflection is impossible on a struct copy -> use the stateless form
                    float w0 = 2f * Mathf.PI * cutoff / sr;
                    float alpha = Mathf.Sin(w0) / (2f * Mathf.Max(chain.wahResonance, 0.7f));
                    float cos = Mathf.Cos(w0);
                    float n0 = alpha;
                    float n1 = 0f;
                    float n2 = -alpha;
                    float d0 = 1f + alpha;
                    float d1 = -2f * cos;
                    float d2 = 1f - alpha;
                    b0 = n0 / d0; b1 = n1 / d0; b2 = n2 / d0;
                    a1 = d1 / d0; a2 = d2 / d0;
                }

                float x = buffer[i];
                float y = b0 * x + b1 * low + b2 * band - a1 * low - a2 * band;
                band = low;
                low = y;
                wetBuf[i] = y;
            }

            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = buffer[i] * (1f - mix) + wetBuf[i] * mix;
        }

        // ── 11. Delay ─────────────────────────────────────────────

        static void DelayStage(float[] left, float[] right, AudioEffectChain chain, float sr)
        {
            int delaySamples = Mathf.Max(1, Mathf.RoundToInt(chain.delayTime * sr));
            int bufSize = delaySamples + 1;
            float feedback = Mathf.Clamp(chain.delayFeedback, 0f, 0.9f);
            float mix = Mathf.Clamp01(chain.delayMix);
            bool pingPong = chain.delayPingPong;

            float[] bufL = new float[bufSize];
            float[] bufR = new float[bufSize];
            int writePos = 0;

            for (int i = 0; i < left.Length; i++)
            {
                int readPos = writePos;
                float dl = bufL[readPos];
                float dr = pingPong ? bufR[readPos] : bufL[readPos];

                bufL[writePos] = left[i] + dr * feedback;
                bufR[writePos] = pingPong
                    ? right[i] + dl * feedback
                    : right[i] + dr * feedback;

                left[i] = left[i] * (1f - mix) + dl * mix;
                right[i] = right[i] * (1f - mix) + dr * mix;

                writePos = (writePos + 1) % bufSize;
            }
        }

        // ── 12. Reverb (Schroeder: 4 combs + 2 allpass, per channel) ──

        static void ReverbStage(float[] left, float[] right, AudioEffectChain chain, float sr)
        {
            float mix = Mathf.Clamp01(chain.reverbMix);
            float dry = Mathf.Clamp01(chain.reverbDryLevel);
            float decay = Mathf.Clamp(chain.reverbDecay, 0.05f, 10f);
            float damping = Mathf.Clamp01(chain.reverbDamping);

            float[] wetL = new float[left.Length];
            float[] wetR = new float[right.Length];

            SchroederReverb.Render(left, wetL, sr, decay, damping, 0);
            SchroederReverb.Render(right, wetR, sr, decay, damping, 7);

            for (int i = 0; i < left.Length; i++)
            {
                left[i] = left[i] * dry + wetL[i] * mix;
                right[i] = right[i] * dry + wetR[i] * mix;
            }
        }

        static class SchroederReverb
        {
            // Prime-ish comb delays (ms) and allpass delays (ms)
            static readonly float[] CombMs = { 29.7f, 37.1f, 41.1f, 43.7f };
            static readonly float[] AllpassMs = { 5.0f, 1.7f };

            public static void Render(float[] input, float[] output, float sr, float decay, float damping, int offset)
            {
                int n = input.Length;

                int[] combLen = new int[CombMs.Length];
                float[] combFb = new float[CombMs.Length];
                float[][] combs = new float[CombMs.Length][];
                int[] combPos = new int[CombMs.Length];

                for (int c = 0; c < CombMs.Length; c++)
                {
                    float ms = CombMs[c] + offset * 0.37f;
                    combLen[c] = Mathf.Max(8, Mathf.RoundToInt(ms * sr / 1000f));
                    combs[c] = new float[combLen[c]];
                    combPos[c] = 0;
                    // Feedback that yields roughly `decay` seconds of tail
                    combFb[c] = Mathf.Pow(10f, -3f * combLen[c] / (sr * Mathf.Max(decay, 0.05f)));
                }

                float damp = Mathf.Clamp(damping, 0f, 0.95f);
                float[] combLow = new float[CombMs.Length];

                int[] apLen = new int[AllpassMs.Length];
                float[][] allpass = new float[AllpassMs.Length][];
                int[] apPos = new int[AllpassMs.Length];
                for (int a = 0; a < AllpassMs.Length; a++)
                {
                    apLen[a] = Mathf.Max(2, Mathf.RoundToInt((AllpassMs[a] + offset * 0.11f) * sr / 1000f));
                    allpass[a] = new float[apLen[a]];
                    apPos[a] = 0;
                }

                const float inputGain = 0.18f;

                for (int i = 0; i < n; i++)
                {
                    float x = input[i] * inputGain;
                    float merged = 0f;

                    for (int c = 0; c < CombMs.Length; c++)
                    {
                        float y = combs[c][combPos[c]];
                        // One-pole lowpass inside the loop = damping
                        combLow[c] = y * (1f - damp) + combLow[c] * damp;
                        combs[c][combPos[c]] = x + combLow[c] * combFb[c] * 0.98f;
                        combPos[c] = (combPos[c] + 1) % combLen[c];
                        merged += y;
                    }
                    merged *= 0.25f;

                    for (int a = 0; a < AllpassMs.Length; a++)
                    {
                        float buffered = allpass[a][apPos[a]];
                        float outSample = -merged + buffered;
                        allpass[a][apPos[a]] = merged + buffered * 0.5f;
                        apPos[a] = (apPos[a] + 1) % apLen[a];
                        merged = outSample;
                    }

                    output[i] = merged;
                }
            }
        }

        // ── 13. Pitch shift (linear resample, length restored) ────

        static float[] PitchShiftChannel(float[] input, int semitones, float sr)
        {
            float ratio = Mathf.Pow(2f, semitones / 12f);
            int n = input.Length;
            int targetLen = Mathf.CeilToInt(n / ratio);

            // Read at `ratio` speed through the source
            float[] temp = new float[targetLen];
            for (int i = 0; i < targetLen; i++)
            {
                float srcPos = i * ratio;
                int idx = (int)srcPos;
                float frac = srcPos - idx;
                if (idx + 1 < n)
                    temp[i] = input[idx] * (1f - frac) + input[idx + 1] * frac;
                else if (idx < n)
                    temp[i] = input[idx];
            }

            // Restore original duration (pad with silence or trim tail)
            float[] output = new float[n];
            int copy = Mathf.Min(n, temp.Length);
            System.Array.Copy(temp, output, copy);
            return output;
        }
    }
}
