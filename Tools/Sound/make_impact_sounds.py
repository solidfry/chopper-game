#!/usr/bin/env python3
"""
Placeholder impact sounds, made from noise and partials: bullet hits and chopper crashes on each surface (metal,
concrete, ground, water). Crashes also get the first ~1.3 s of a Tom Winandy medium explosion (Assets/Sound/Explosions)
mixed under them, faded out, for weight; the full 6-9 s explosions are too long for a crash. Output: Assets/Sound/Impacts/<Surface>_<Kind>_<n>.ogg, which Setup step 1
puts on the surface cues (SoundLibrary > Surfaces). Swap them for real recordings on the cue assets later.

Run: python3 Tools/Sound/make_impact_sounds.py   (needs numpy and ffmpeg with libvorbis)
"""
import os
import subprocess
import sys
import tempfile
import wave

import numpy as np

RATE = 44100
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Sound", "Impacts")
EXPLOSIONS = os.path.join(ROOT, "Assets", "Sound", "Explosions", "TomWinandySFX_Explosions Volume I_MediumExplosion_{:02d}.wav")

# Explosion under each crash file (index n-1) and its level against the synth layer. Picked for a hard attack and a
# fast fall (head/tail level); brighter ones under metal, darker ones under ground, a soft low one under water.
CRASH_LAYERS = {
    "Metal": ([14, 19, 6], 0.75),
    "Concrete": ([20, 5, 15], 0.9),
    "Ground": ([9, 11, 22], 0.9),
    "Water": ([7, 17], 0.5),
}
LAYER_SECONDS = 1.3
LAYER_FADE = 0.7


def t(seconds):
    return np.arange(int(seconds * RATE)) / RATE


def noise(rng, seconds):
    return rng.standard_normal(int(seconds * RATE))


def band(signal, low, high, slope=2.7):
    """FFT band-pass with soft edges (low/high in Hz; 0 or None = open). Higher slope = steeper edges."""
    spectrum = np.fft.rfft(signal)
    freqs = np.fft.rfftfreq(len(signal), 1 / RATE)
    mask = np.ones_like(freqs)
    if low:
        mask *= 1 / (1 + (low / np.maximum(freqs, 1e-3)) ** slope)
    if high:
        mask *= 1 / (1 + (freqs / high) ** slope)
    return np.fft.irfft(spectrum * mask, len(signal))


def decay(seconds, length, attack=0.002):
    time = t(length)
    env = np.exp(-time / max(seconds, 1e-4))
    if attack > 0:
        env *= np.minimum(1, time / attack)
    return env


def pad(signal, length):
    out = np.zeros(int(length * RATE))
    out[: min(len(signal), len(out))] = signal[: len(out)]
    return out


def at(signal, start, length):
    """Signal placed at start seconds in a buffer of length seconds."""
    out = np.zeros(int(length * RATE))
    i = int(start * RATE)
    if i >= len(out):
        return out
    n = min(len(signal), len(out) - i)
    out[i : i + n] = signal[:n]
    return out


def partials(rng, base, length, ratios, decays, levels, drift=0.0):
    time = t(length)
    out = np.zeros_like(time)
    for ratio, d, level in zip(ratios, decays, levels):
        f = base * ratio * (1 + rng.uniform(-0.02, 0.02))
        sweep = f * (1 - drift * np.minimum(time / length, 1))
        phase = 2 * np.pi * np.cumsum(sweep) / RATE
        out += level * np.sin(phase + rng.uniform(0, 6.28)) * np.exp(-time / d)
    return out


def grit(rng, length, density, decay_seconds, low, high, start=0.0):
    """Sparse clicks (debris, gravel): density per second, fading."""
    out = np.zeros(int(length * RATE))
    count = int(density * length)
    for _ in range(count):
        when = start + rng.exponential(decay_seconds)
        if when >= length:
            continue
        click = noise(rng, 0.004) * decay(0.0012, 0.004, attack=0)
        out += at(click * rng.uniform(0.3, 1.0), when, length)
    return band(out, low, high)


def metal_ping(rng, base, length, level=1.0):
    ratios = [1.0, 2.76, 5.40, 8.93, 13.3]
    decays = [length * 0.5, length * 0.35, length * 0.22, length * 0.15, length * 0.1]
    levels = [1.0, 0.7, 0.45, 0.3, 0.2]
    return level * partials(rng, base, length, ratios, decays, levels)


# -- bullet hits -------------------------------------------------------------------------------------------------------

def metal_hit(rng, n):
    length = 0.55
    click = pad(band(noise(rng, 0.006), 2500, 14000) * decay(0.0015, 0.006, attack=0), length)
    ring = metal_ping(rng, rng.uniform(900, 1500), length, 0.6) * decay(0.12, length)
    out = click * 1.2 + ring
    if n == 3:  # ricochet whine
        time = t(length)
        sweep = 3600 * (1 - 0.55 * time / length)
        whine = np.sin(2 * np.pi * np.cumsum(sweep) / RATE) * decay(0.16, length, attack=0.02) * 0.35
        out += at(whine, 0.02, length)
    return out


def concrete_hit(rng, n):
    length = 0.5
    crack = pad(band(noise(rng, 0.012), 1500, 9000) * decay(0.003, 0.012, attack=0), length)
    thump = band(noise(rng, length), 40, 320) * decay(0.05, length) * 1.6
    chips = grit(rng, length, 60, 0.07, 1200, 7000, start=0.01) * 0.7
    return crack + thump + chips


def ground_hit(rng, n):
    length = 0.5
    thud = band(noise(rng, length), 30, 380) * decay(0.07, length) * 2.0
    spray = band(noise(rng, length), 400, 2800) * decay(0.12, length, attack=0.01) * 0.35
    clods = grit(rng, length, 25, 0.12, 300, 2500, start=0.03) * 0.5
    return thud + spray + clods


def water_hit(rng, n):
    length = 0.6
    time = t(0.06)
    sweep = 500 + 1600 * (time / 0.06)
    plip = pad(np.sin(2 * np.pi * np.cumsum(sweep) / RATE) * decay(0.03, 0.06), length) * 0.6
    splash = band(noise(rng, length), 900, 9000) * decay(0.14, length, attack=0.008) * 0.6
    return plip + splash


# -- crashes -----------------------------------------------------------------------------------------------------------

def hull_crunch(rng, length, level=1.0):
    """The chopper's own airframe in any crash: low boom, bent-metal crunch, rattling parts."""
    boom = band(noise(rng, length), 25, 160) * decay(0.18, length) * 2.5
    boom += np.sin(2 * np.pi * rng.uniform(45, 60) * t(length)) * decay(0.22, length) * 0.8
    crunch_noise = band(noise(rng, length), 250, 4500)
    grains = np.repeat(rng.uniform(0.2, 1.0, int(length * 90) + 1), RATE // 90 + 1)[: len(crunch_noise)]
    crunch = crunch_noise * grains * decay(0.25, length, attack=0.004) * 0.8
    groan = metal_ping(rng, rng.uniform(160, 280), length, 0.5) * decay(0.6, length)
    rattle = np.zeros(int(length * RATE))
    for _ in range(14):
        when = 0.05 + rng.exponential(0.3)
        if when < length - 0.2:
            rattle += at(metal_ping(rng, rng.uniform(1200, 3200), 0.2, rng.uniform(0.08, 0.25)) * decay(0.04, 0.2), when, length)
    return level * (boom + crunch + groan + rattle)


def metal_crash(rng, n):
    length = 1.8
    clang = metal_ping(rng, rng.uniform(320, 520), length, 0.9) * decay(0.5, length)
    return hull_crunch(rng, length) + clang


def concrete_crash(rng, n):
    length = 1.8
    crumble = grit(rng, length, 900, 0.35, 300, 5000, start=0.02) * 1.1
    dust = band(noise(rng, length), 150, 1800) * decay(0.4, length, attack=0.03) * 0.4
    return hull_crunch(rng, length, 0.9) + crumble + dust


def ground_crash(rng, n):
    length = 1.8
    thud = band(noise(rng, length), 20, 240) * decay(0.25, length) * 2.2
    dirt = band(noise(rng, length), 200, 2200) * decay(0.45, length, attack=0.02) * 0.6
    clods = grit(rng, length, 220, 0.4, 200, 2000, start=0.05) * 0.8
    return hull_crunch(rng, length, 0.7) + thud + dirt + clods


def water_crash(rng, n):
    length = 2.0
    slap = band(noise(rng, length), 60, 1200) * decay(0.12, length) * 1.6
    splash = band(noise(rng, length), 700, 10000) * decay(0.6, length, attack=0.02) * 0.9
    bubbles = np.zeros(int(length * RATE))
    for _ in range(40):
        when = 0.2 + rng.exponential(0.5)
        if when < length - 0.05:
            f = rng.uniform(300, 1400)
            time = t(0.04)
            blip = np.sin(2 * np.pi * np.cumsum(f * (1 + 2.5 * time / 0.04)) / RATE) * decay(0.012, 0.04)
            bubbles += at(blip * rng.uniform(0.05, 0.2), when, length)
    return hull_crunch(rng, length, 0.5) + slap + splash + bubbles


SOUNDS = [
    ("Metal", "Hit", metal_hit, 3),
    ("Concrete", "Hit", concrete_hit, 3),
    ("Ground", "Hit", ground_hit, 3),
    ("Water", "Hit", water_hit, 2),
    ("Metal", "Crash", metal_crash, 3),
    ("Concrete", "Crash", concrete_crash, 3),
    ("Ground", "Crash", ground_crash, 3),
    ("Water", "Crash", water_crash, 2),
]


def explosion_layer(number):
    """The start of a medium explosion: mono, from its onset, faded out over the last LAYER_FADE seconds, peak 1."""
    raw = subprocess.run(["ffmpeg", "-nostdin", "-loglevel", "error", "-i", EXPLOSIONS.format(number), "-ac", "1", "-ar", str(RATE),
                          "-f", "f32le", "-"], check=True, capture_output=True, stdin=subprocess.DEVNULL).stdout
    signal = np.frombuffer(raw, np.float32).astype(np.float64)
    onset = int(np.argmax(np.abs(signal) > 0.1 * np.max(np.abs(signal))))
    signal = signal[onset:onset + int(LAYER_SECONDS * RATE)].copy()
    fade = min(len(signal), int(LAYER_FADE * RATE))
    signal[-fade:] *= np.linspace(1, 0, fade) ** 2
    return signal / (np.max(np.abs(signal)) or 1)


def with_layer(signal, surface, n):
    numbers, level = CRASH_LAYERS[surface]
    if not os.path.exists(EXPLOSIONS.format(numbers[n - 1])):
        print(f"  no explosion {numbers[n - 1]}: synth only")
        return signal
    layer = explosion_layer(numbers[n - 1])
    out = np.zeros(max(len(signal), len(layer)))
    out[:len(signal)] += signal / (np.max(np.abs(signal)) or 1)
    out[:len(layer)] += layer * level
    return out


def finish(signal):
    # Short fade at the end, then peak at -1 dBFS.
    fade = min(len(signal), int(0.03 * RATE))
    signal[-fade:] *= np.linspace(1, 0, fade)
    peak = np.max(np.abs(signal)) or 1
    return signal / peak * 10 ** (-1 / 20)


def write_ogg(signal, path):
    pcm = (np.clip(signal, -1, 1) * 32767).astype(np.int16)
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
        wav_path = tmp.name
    try:
        with wave.open(wav_path, "wb") as wav:
            wav.setnchannels(1)
            wav.setsampwidth(2)
            wav.setframerate(RATE)
            wav.writeframes(pcm.tobytes())
        subprocess.run(["ffmpeg", "-nostdin", "-y", "-loglevel", "error", "-i", wav_path, "-fflags", "+bitexact", "-c:a", "libvorbis", "-q:a", "6", path],
                       check=True, stdin=subprocess.DEVNULL)
    finally:
        os.remove(wav_path)


def main():
    os.makedirs(OUT, exist_ok=True)
    for surface, kind, make, count in SOUNDS:
        for n in range(1, count + 1):
            # Fixed seed per file, so a re-run makes the same sounds.
            rng = np.random.default_rng(sum(map(ord, surface + kind)) * 10 + n)
            path = os.path.join(OUT, f"{surface}_{kind}_{n}.ogg")
            signal = make(rng, n)
            if kind == "Crash":
                signal = with_layer(signal, surface, n)
            write_ogg(finish(signal), path)
            print(os.path.relpath(path, ROOT))


if __name__ == "__main__":
    sys.exit(main())
