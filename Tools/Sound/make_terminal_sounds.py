#!/usr/bin/env python3
"""
Terminal UI sounds for the end-of-match screen: key taps and relay clacks made from noise snaps and short damped
thumps, with no tones (taps, not pings). Output: Assets/Sound/UI/Terminal/<Name>.wav, which Setup step 1 puts on the results cues
(SoundLibrary > Results).

  Term_Tick_1..3   count-up tick (numbers rolling), ~20 ms
  Term_Line_1..2   one XP line / one award card prints, ~60 ms
  Term_Window      a panel pops open: a relay double clack and a short hiss, ~160 ms
  Term_Award       an award stamps down: one heavy low tap and a bounce, ~100 ms
  Term_Level       level up: a burst of fast typing ending on a heavy tap, ~380 ms
  Term_XpPing      XP: a soft, round ping; the screen raises its pitch as the XP fills, ~320 ms

Run: python Tools/Sound/make_terminal_sounds.py   (needs numpy)
"""
import os
import wave

import numpy as np

RATE = 44100
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Sound", "UI", "Terminal")


def t(seconds):
    return np.arange(int(seconds * RATE)) / RATE


def soften(signal, taps=6):
    """A moving average: a simple low-pass for noise."""
    return np.convolve(signal, np.ones(taps) / taps, mode="same")


def env(seconds, attack=0.001, decay=0.02):
    x = t(seconds)
    rise = np.clip(x / max(attack, 1e-6), 0.0, 1.0)
    return rise * np.exp(-x / decay)


def place(total, part, at):
    start = int(at * RATE)
    end = min(len(total), start + len(part))
    total[start:end] += part[: end - start]


def finish(signal, peak_db=-3.0):
    fade = int(0.003 * RATE)
    signal[-fade:] *= np.linspace(1.0, 0.0, fade)
    peak = np.max(np.abs(signal))
    if peak > 0:
        signal = signal / peak * 10 ** (peak_db / 20)
    return signal


def write(name, signal):
    os.makedirs(OUT, exist_ok=True)
    data = (np.clip(signal, -1, 1) * 32767).astype("<i2")
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(RATE)
        f.writeframes(data.tobytes())
    print("wrote", name)


def band(rng, seconds, low_taps, high_taps):
    """Noise between two moving averages: a band, brighter with fewer taps."""
    n = rng.standard_normal(int(seconds * RATE) + 64)
    return (soften(n, low_taps) - soften(n, high_taps))[: int(seconds * RATE)]


def tap(rng, body=260.0, bright=3, weight=1.0, length=0.03):
    """
    One key tap, no pitch to speak of: a bright noise snap (the key cap), a short damped thump (the plastic case) and a
    faint rattle. All of it gone in a few tens of milliseconds.
    """
    s = np.zeros(int(length * RATE))
    snap = band(rng, 0.006, bright, bright * 6) * np.exp(-t(0.006) / 0.0012)
    place(s, snap * 1.0, 0.0)
    x = t(length)
    thump = np.sin(2 * np.pi * body * x) * np.exp(-x / 0.005) * 0.55 * weight
    place(s, thump, 0.0005)
    rattle = band(rng, 0.012, 2, 10) * np.exp(-t(0.012) / 0.003) * 0.25
    place(s, rattle, 0.003)
    return s


def tick(rng, body):
    s = tap(rng, body=body, bright=2, weight=0.6, length=0.022)
    return finish(s, -6.0)


def line(rng, body):
    # Key down, then the key coming back up: two taps close together, the second lighter.
    s = np.zeros(int(0.06 * RATE))
    place(s, tap(rng, body=body, bright=3, weight=1.0), 0.0)
    place(s, tap(rng, body=body * 1.3, bright=2, weight=0.4) * 0.45, 0.028)
    return finish(s, -5.0)


def window(rng):
    # A relay clack: a hard double tap, a low case thump and a short hiss as the panel lights.
    s = np.zeros(int(0.16 * RATE))
    place(s, tap(rng, body=140, bright=2, weight=1.4, length=0.05), 0.0)
    place(s, tap(rng, body=210, bright=2, weight=0.7, length=0.04) * 0.7, 0.018)
    hiss = band(rng, 0.09, 2, 6) * env(0.09, 0.004, 0.025) * 0.18
    place(s, hiss, 0.02)
    return finish(s, -4.0)


def award(rng):
    # A stamp: one heavy, low tap with a quick second bounce.
    s = np.zeros(int(0.1 * RATE))
    place(s, tap(rng, body=110, bright=3, weight=1.8, length=0.06), 0.0)
    place(s, tap(rng, body=150, bright=3, weight=0.6, length=0.03) * 0.35, 0.034)
    return finish(s, -4.0)


def level(rng):
    # A fast typed burst that speeds up and ends on a heavy tap: a printer line running.
    times = [0.0, 0.055, 0.1, 0.138, 0.17, 0.197, 0.22, 0.24, 0.29]
    s = np.zeros(int(0.38 * RATE))
    for i, at in enumerate(times):
        last = i == len(times) - 1
        part = tap(rng, body=120 if last else 230 + 25 * (i % 3), bright=2 if last else 3,
                   weight=1.8 if last else 0.8, length=0.06 if last else 0.03)
        place(s, part * (1.0 if last else 0.7), at)
    return finish(s, -3.0)


def xp_ping(rng):
    """
    The XP ping (the game raises its pitch as the XP fills): a soft sine at A5 with a quiet octave and twelfth for
    sparkle, two voices a few cents apart for width, a tiny pitch scoop into the note, a soft tap at the start and a
    short bloom. Gentle, round, no hard edge.
    """
    length = 0.32
    x = t(length)
    base = 880.0
    scoop = 1.0 - 0.03 * np.exp(-x / 0.012)  # starts a hair flat and slides up: the juice
    s = np.zeros(len(x))
    for detune, level in ((1.0, 0.6), (1.0035, 0.4)):
        phase = 2 * np.pi * np.cumsum(base * detune * scoop) / RATE
        s += np.sin(phase) * level
        s += np.sin(2 * phase) * level * 0.22 * np.exp(-x / 0.05)
        s += np.sin(3 * phase) * level * 0.08 * np.exp(-x / 0.025)
    s *= env(length, 0.003, 0.085)
    place(s, band(rng, 0.004, 2, 8) * np.exp(-t(0.004) / 0.001) * 0.15, 0.0)
    return finish(s, -6.0)


def main():
    rng = np.random.default_rng(7)
    for i, body in enumerate([300, 340, 380], start=1):
        write(f"Term_Tick_{i}", tick(rng, body))
    for i, body in enumerate([220, 250], start=1):
        write(f"Term_Line_{i}", line(rng, body))
    write("Term_Window", window(rng))
    write("Term_Award", award(rng))
    write("Term_Level", level(rng))
    write("Term_XpPing", xp_ping(rng))


if __name__ == "__main__":
    main()
