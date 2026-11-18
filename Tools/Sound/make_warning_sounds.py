"""Cockpit warning loops in the style of the remade lock warning (LockWarning_2070.wav, 17 Nov 2026).

That style: soft sine beeps near 1047 Hz with a 95 Hz thump under each, top cut at about 2.2 kHz, a 60-180 Hz lift, a
quiet fifth-up shimmer, a ping-pong echo that wraps inside the loop, a slow pan sway and a rising low pulse. The loop
is seamless (the echo tail wraps to the start).

Missile warning (18 Nov 2026, owner: "should sound more like the remade lock warning"): the same sound, more urgent:
two-tone beeps (1047 and 1319 Hz) twice as fast. In game the cue's pitch rises up to 1.6 as the missile closes in
(TargetingFeedback), so the base stays calm.

Run from the repo root:  python Tools/Sound/make_warning_sounds.py
Writes Assets/Chopper/Audio/SFX/Warnings/MissileWarning_Alarm.wav (same file, so Cue_MissileWarning keeps its clip).
"""
import os
import wave

import numpy as np

SR = 44100
OUT = os.path.join("Assets", "Chopper", "Audio", "SFX", "Warnings")


def one_pole_lowpass(x, cutoff):
    a = np.exp(-2.0 * np.pi * cutoff / SR)
    y = np.empty_like(x)
    state = 0.0
    # Two passes over the loop so the filter state at the start matches the end (seamless).
    for _ in range(2):
        for i in range(len(x)):
            state = (1.0 - a) * x[i] + a * state
            y[i] = state
    return y


def beep(length, start, duration, freq, gain):
    t = np.arange(length) / SR
    local = t - start
    on = (local >= 0) & (local < duration)
    env = np.zeros(length)
    lt = local[on]
    attack, release = 0.008, 0.045
    env[on] = np.minimum(1.0, lt / attack) * np.clip((duration - lt) / release, 0.0, 1.0)
    tone = np.sin(2 * np.pi * freq * t) + 0.25 * np.sin(2 * np.pi * 2 * freq * t)
    shimmer = 0.08 * np.sin(2 * np.pi * freq * 1.5 * t)          # quiet fifth up
    thump_env = np.zeros(length)
    thump_env[on] = np.exp(-lt / 0.035)
    thump = 0.55 * np.sin(2 * np.pi * 95.0 * t) * thump_env
    return gain * (env * (tone + shimmer) + thump)


def wrap_delay(x, seconds):
    return np.roll(x, int(round(seconds * SR)))


def missile_warning():
    loop = 0.6
    n = int(SR * loop)
    beeps = [(0.00, 1047.0), (0.15, 1319.0), (0.30, 1047.0), (0.45, 1319.0)]
    dry = sum(beep(n, start, 0.085, freq, 0.42) for start, freq in beeps)
    # Rising low pulse under the loop (60 Hz, swells towards the loop end, then wraps).
    t = np.arange(n) / SR
    dry += 0.10 * np.sin(2 * np.pi * 60.0 * t) * (t / loop) ** 2
    dry = one_pole_lowpass(dry, 2200.0)
    # 60-180 Hz lift: add a low-passed copy.
    dry += 0.35 * one_pole_lowpass(dry, 180.0)
    # Ping-pong echo inside the loop: left then right, wrapping round.
    left = dry + 0.28 * wrap_delay(dry, 0.075) + 0.10 * wrap_delay(dry, 0.225)
    right = dry + 0.28 * wrap_delay(dry, 0.150) + 0.10 * wrap_delay(dry, 0.300)
    # Slow pan sway: one cycle per loop, so it joins.
    sway = 0.12 * np.sin(2 * np.pi * t / loop)
    left *= 1.0 - sway
    right *= 1.0 + sway
    stereo = np.stack([left, right], axis=1)
    # Loudness like the lock warning (rms about 0.17, peak under 0.75).
    stereo *= 0.17 / np.sqrt(np.mean(stereo ** 2))
    peak = np.abs(stereo).max()
    if peak > 0.75:
        stereo *= 0.75 / peak
    return stereo


def write(name, stereo):
    path = os.path.join(OUT, name)
    data = (np.clip(stereo, -1.0, 1.0) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
    print(f"{path}: {len(stereo) / SR:.2f} s, rms {np.sqrt(np.mean(stereo ** 2)):.3f}, peak {np.abs(stereo).max():.2f}")


if __name__ == "__main__":
    write("MissileWarning_Alarm.wav", missile_warning())
