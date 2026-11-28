"""
Builds the minigun spin sounds from one recording (owner, 28 Nov 2026: "mini-gun-154177").

The recording has three parts: spin-up (0 - 0.55 s), steady firing (0.55 - 2.6 s) and spin-down (2.6 - 4.9 s).
All three are cut with ONE gain, so the spin-up, the fire loop and the spin-down sit at one level and join without a
jump. The fire loop is made seamless with an equal-power crossfade. Output: 48 kHz, 16-bit mono WAV.

  Minigun:   Assets/Chopper/Audio/SFX/Guns/MinigunSpin/{SpinUp,FireLoop,SpinDown}.wav     (loop at -14 dB RMS)
  Minigun Mk2: Assets/Chopper/Audio/SFX/Guns/MinigunMk2Spin/{SpinUp,FireLoop,SpinDown}.wav (slower, lower, more bass
             and grit; loop at -10 dB RMS)

The burst tails (LoopTail1/2) are not touched.

Input: 32-bit float mono samples (raw), as Unity decoded the mp3 (AudioClip.GetData), and its sample rate:
  python Tools/Sound/make_minigun_sounds.py <samples.raw> 24000
"""
import sys
import wave
from pathlib import Path

import numpy as np
from scipy import signal

OUT_RATE = 48000
ROOT = Path(__file__).resolve().parents[2] / "Assets/Chopper/Audio/SFX/Guns"

# Part times in the recording, seconds.
SPIN_UP = (0.0, 0.62)
LOOP = (0.65, 2.55)
SPIN_DOWN = (2.55, 4.95)
CROSSFADE = 0.15


def db(x):
    return 20.0 * np.log10(max(x, 1e-12))


def rms(x):
    return float(np.sqrt(np.mean(x * x)))


def cut(x, sr, start, end):
    return x[int(start * sr):int(end * sr)].copy()


def fades(x, sr, fade_in, fade_out):
    n_in, n_out = int(fade_in * sr), int(fade_out * sr)
    if n_in:
        x[:n_in] *= np.sin(np.linspace(0, np.pi / 2, n_in)) ** 2
    if n_out:
        x[-n_out:] *= np.cos(np.linspace(0, np.pi / 2, n_out)) ** 2
    return x


def seamless_loop(x, sr, crossfade):
    """The loop's end crossfades into its start (equal power): played round, there is no click or gap."""
    n = int(crossfade * sr)
    body, tail = x[:-n], x[-n:]
    t = np.linspace(0, np.pi / 2, n)
    head = body[:n] * np.sin(t) + tail * np.cos(t)
    return np.concatenate([head, body[n:]])


MK2_SPEED = 0.84


def mk2(x, sr):
    """Chunkier: 16 % slower and lower, a bass shelf, a little drive (grit)."""
    slow = signal.resample(x, int(len(x) / MK2_SPEED))
    b, a = signal.butter(2, 220 / (sr / 2), "low")
    bass = signal.lfilter(b, a, slow)
    shaped = slow + 0.9 * bass
    return np.tanh(shaped * 1.8) / np.tanh(1.8)


def write(path, x):
    peak = np.max(np.abs(x))
    if peak > 0.98:
        x = x * (0.98 / peak)
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(OUT_RATE)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype("<i2").tobytes())
    print(f"{path.relative_to(ROOT)}: {len(x) / OUT_RATE:.2f} s, rms {db(rms(x)):.1f} dB, peak {db(np.max(np.abs(x))):.1f} dB")


def build(x, sr, folder, loop_db, stretch=1.0):
    """<stretch>: the recording was slowed by this factor (Mk2), so every part time grows with it."""
    part = lambda t: (t[0] * stretch, t[1] * stretch)
    loop = part(LOOP)
    # One gain for all three parts, set by the fire loop's level.
    gain = 10 ** (loop_db / 20) / rms(cut(x, sr, *loop))
    x = x * gain
    write(ROOT / folder / "SpinUp.wav", fades(cut(x, sr, *part(SPIN_UP)), sr, 0.005, 0.07))
    write(ROOT / folder / "FireLoop.wav", seamless_loop(cut(x, sr, loop[0], loop[1] + CROSSFADE), sr, CROSSFADE))
    write(ROOT / folder / "SpinDown.wav", fades(cut(x, sr, *part(SPIN_DOWN)), sr, 0.03, 0.15))


def main():
    raw, rate = sys.argv[1], int(sys.argv[2])
    x = np.fromfile(raw, dtype=np.float32).astype(np.float64)
    x = signal.resample_poly(x, OUT_RATE, rate)
    x -= np.mean(x)
    build(x, OUT_RATE, "MinigunSpin", -14.1)
    build(mk2(x, OUT_RATE), OUT_RATE, "MinigunMk2Spin", -10.0, stretch=1 / MK2_SPEED)


if __name__ == "__main__":
    main()
