#!/usr/bin/env python3
"""Synthesizes the game's fallback sounds from code (no samples), CC0.

Most clips are now real recordings built by tools/AudioGen/recorded/process.py from
tools/AudioGen/recorded/specs.json; this script skips every clip that file builds (and the music),
so it only writes the synthesized fallbacks listed in tools/AudioGen/CREDITS.md.

Run: python3 tools/AudioGen/generate.py  (needs numpy and scipy). Writes 16-bit WAVs into
unity/Assets/Resources/Audio/<Folder>/; the Unity importer rules in Editor/AudioImportRules.cs pick
ADPCM or Vorbis per folder. Every output is deterministic (fixed seeds).
"""
import json
import os
import sys
import numpy as np
from scipy import signal
from scipy.io import wavfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "unity", "Assets", "Resources", "Audio"))
SR = 22050
MSR = 44100
rng = np.random.default_rng(1977)
written = []
RECORDED = {s["out"] for s in json.load(open(os.path.join(os.path.dirname(__file__), "recorded", "specs.json")))}


class Sig(np.ndarray):
    """1-D signal whose + pads the shorter operand, so layers of different lengths sum cleanly."""

    def __add__(self, o):
        if isinstance(o, np.ndarray) and o.ndim == 1 and self.ndim == 1 and len(o) != len(self):
            return mix(self, o)
        return np.ndarray.__add__(self, o)

    __radd__ = __add__


def S(x):
    return np.asarray(x, dtype=float).view(Sig)



def T(d, sr=SR):
    return S(np.arange(int(d * sr)) / sr)


def noise(n):
    return S(rng.uniform(-1, 1, n))


def pink(n):
    w = rng.standard_normal(n)
    b = [0.049922035, -0.095993537, 0.050612699, -0.004408786]
    a = [1, -2.494956002, 2.017265875, -0.522189400]
    y = signal.lfilter(b, a, w)
    return S(y / (np.max(np.abs(y)) + 1e-9))


def brown(n):
    y = np.cumsum(rng.standard_normal(n))
    y = signal.sosfilt(signal.butter(1, 20, "hp", fs=SR, output="sos"), y)
    return S(y / (np.max(np.abs(y)) + 1e-9))


def filt(x, kind, f, order=2, sr=SR):
    if isinstance(f, (list, tuple)):
        f = [min(max(v, 10), sr / 2 * 0.95) for v in f]
    else:
        f = min(max(f, 10), sr / 2 * 0.95)
    return S(signal.sosfilt(signal.butter(order, f, kind, fs=sr, output="sos"), x))


def bp(x, lo, hi, order=2, sr=SR):
    return filt(x, "bandpass", [lo, hi], order, sr)


def osc(freq, d=None, shape="sin", sr=SR, phase0=0.0):
    f = np.asarray(freq, dtype=float)
    if f.ndim == 0:
        f = np.full(int(d * sr), float(f))
    ph = phase0 + np.cumsum(f) / sr
    p = ph - np.floor(ph)
    if shape == "sin":
        return S(np.sin(2 * np.pi * ph))
    if shape == "saw":
        return S(2 * p - 1)
    if shape == "sq":
        return S(np.where(p < 0.5, 1.0, -1.0))
    if shape == "tri":
        return S(4 * np.abs(p - 0.5) - 1)
    if shape == "pulse25":
        return S(np.where(p < 0.25, 1.0, -1.0))
    raise ValueError(shape)


def env_exp(n, decay, sr=SR):
    return S(np.exp(-np.arange(n) / sr * decay))


def adsr(n, a, d, s, r, sr=SR):
    a, d, r = int(a * sr), int(d * sr), int(r * sr)
    sus = max(n - a - d - r, 0)
    e = np.concatenate([np.linspace(0, 1, max(a, 1)), np.linspace(1, s, max(d, 1)), np.full(sus, s), np.linspace(s, 0, max(r, 1))])
    return S(np.pad(e, (0, max(n - len(e), 0)))[:n])


def fade(x, fin=0.003, fout=0.01, sr=SR):
    x = x.copy()
    a, b = int(fin * sr), int(fout * sr)
    if a:
        x[:a] *= np.linspace(0, 1, a)
    if b:
        x[-b:] *= np.linspace(1, 0, b)
    return x


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[: len(p)] += p
    return S(out)


def at(x, start, n=None, sr=SR):
    i = int(start * sr)
    out = np.zeros(max(n or 0, i + len(x)))
    out[i : i + len(x)] += x
    return S(out)


def sat(x, drive=2.0):
    return np.tanh(x * drive) / np.tanh(drive)


def norm(x, peak=0.89):
    m = np.max(np.abs(x)) + 1e-9
    return x / m * peak


def loopify(x, xf=0.25, sr=SR):
    n = int(xf * sr)
    head, tail = x[:n], x[-n:]
    w = np.linspace(0, 1, n)
    body = x[n:-n] if len(x) > 2 * n else x[n:]
    blended = tail * (1 - w) + head * w
    return np.concatenate([blended, body]) if len(body) else blended


def write(folder, name, x, sr=SR, peak=0.89, stereo=False):
    if folder == "Music" or f"{folder}/{name}" in RECORDED:
        return
    path = os.path.join(ROOT, folder)
    os.makedirs(path, exist_ok=True)
    if stereo:
        m = np.max(np.abs(x)) + 1e-9
        x = x / m * peak
        data = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    else:
        data = (np.clip(norm(x, peak) if peak else x, -1, 1) * 32767).astype(np.int16)
    wavfile.write(os.path.join(path, name + ".wav"), sr, data)
    written.append((folder, name, len(data) / sr, sr, 2 if stereo else 1))


# ---------------------------------------------------------------- building blocks

def thump(f0=90, f1=40, d=0.25, decay=18):
    t = T(d)
    f = f1 + (f0 - f1) * np.exp(-t * 30)
    return osc(f) * env_exp(len(t), decay)


def click(d=0.01, hp=2000):
    return filt(noise(int(d * SR)), "hp", hp) * env_exp(int(d * SR), 400)


def metal_ring(freqs, d, decay, amp=None):
    t = T(d)
    out = np.zeros(len(t))
    for i, f in enumerate(freqs):
        a = amp[i] if amp else 1 / (i + 1)
        out += a * np.sin(2 * np.pi * f * t + rng.uniform(0, 6)) * env_exp(len(t), decay * (1 + 0.4 * i))
    return out


def crunch(d=0.5, bright=3000, density=300):
    n = int(d * SR)
    out = np.zeros(n)
    for _ in range(int(density * d)):
        i = int(rng.uniform(0, 1) ** 1.8 * n * 0.9)
        k = rng.integers(40, 400)
        burst = noise(k) * env_exp(k, rng.uniform(60, 300)) * rng.uniform(0.2, 1)
        out[i : i + k] += burst[: n - i]
    out = bp(out, 300, bright)
    return out * env_exp(n, 4 / d)


def glass(d=0.9, n_shards=60, seed=0):
    r = np.random.default_rng(seed)
    n = int(d * SR)
    out = np.zeros(n)
    hit = filt(noise(int(0.06 * SR)), "hp", 2500) * env_exp(int(0.06 * SR), 60)
    out[: len(hit)] += hit
    for _ in range(n_shards):
        start = int(r.uniform(0, 1) ** 2 * n * 0.85)
        dd = r.uniform(0.03, 0.18)
        f = r.uniform(2500, 7500)
        tt = np.arange(int(dd * SR)) / SR
        tone = np.sin(2 * np.pi * f * tt) * np.exp(-tt * r.uniform(25, 60)) * r.uniform(0.1, 0.5)
        e = min(len(tone), n - start)
        out[start : start + e] += tone[:e]
    return out * env_exp(n, 2.5)


def wood_crack(d=0.4, seed=0):
    r = np.random.default_rng(seed)
    n = int(d * SR)
    out = np.zeros(n)
    for _ in range(14):
        i = int(r.uniform(0, 0.35) * n)
        k = int(r.uniform(0.004, 0.02) * SR)
        out[i : i + k] += noise(k)[: n - i] * env_exp(k, 200) * r.uniform(0.3, 1)
    body = bp(noise(n), 400, 1800) * env_exp(n, 14)
    knock = osc(r.uniform(180, 260), d) * env_exp(n, 22)
    return bp(out, 600, 5000) + 0.5 * body + 0.6 * knock


def rustle(d=0.6, bright=6000):
    n = int(d * SR)
    am = np.abs(filt(noise(n), "lp", 25)) * 4
    return bp(noise(n), 1500, bright) * am * adsr(n, 0.05, 0.1, 0.6, d * 0.5)


def water_spray(d):
    n = int(d * SR)
    x = bp(noise(n), 700, 7000) * (0.7 + 0.3 * filt(noise(n), "lp", 6) * 5)
    gurgle = bp(noise(n), 120, 500) * 0.6
    return x + gurgle


def voice(pitch, formants, d, breath=0.15, vib=5.5, vib_amt=0.02, sr=SR):
    """Glottal pulse train through moving formant resonators: playful cartoon voice."""
    n = int(d * sr)
    pitch = np.interp(np.linspace(0, 1, n), np.linspace(0, 1, len(pitch)), pitch)
    pitch = pitch * (1 + vib_amt * np.sin(2 * np.pi * vib * np.arange(n) / sr))
    src = osc(pitch, shape="saw", sr=sr)
    src = filt(src, "lp", 3500, 1, sr) + breath * noise(n)
    out = np.zeros(n)
    nf = len(formants[0])
    for k in range(nf):
        f_track = np.interp(np.linspace(0, 1, n), np.linspace(0, 1, len(formants)), [f[k][0] for f in formants])
        bw = formants[0][k][1]
        gain = formants[0][k][2]
        seg = 256
        y = np.zeros(n)
        zi = None
        for s in range(0, n, seg):
            fc = f_track[s]
            sos = signal.butter(2, [max(fc - bw / 2, 50), min(fc + bw / 2, sr / 2 - 100)], "bandpass", fs=sr, output="sos")
            if zi is None:
                zi = np.zeros((sos.shape[0], 2))
            y[s : s + seg], zi = signal.sosfilt(sos, src[s : s + seg], zi=zi)
        out += y * gain
    return out


V_A = [(800, 160, 1.0), (1200, 180, 0.6), (2500, 250, 0.25)]
V_E = [(550, 120, 1.0), (1900, 200, 0.6), (2600, 250, 0.3)]
V_I = [(320, 100, 1.0), (2400, 220, 0.5), (3000, 300, 0.3)]
V_O = [(500, 120, 1.0), (850, 150, 0.6), (2500, 250, 0.15)]
V_U = [(350, 100, 1.0), (750, 140, 0.5), (2400, 250, 0.1)]
V_UH = [(650, 140, 1.0), (1150, 170, 0.6), (2500, 250, 0.2)]


def h_noise(d):
    n = int(d * SR)
    return bp(noise(n), 900, 4000) * adsr(n, 0.01, 0.02, 0.8, d * 0.4) * 0.4


# ---------------------------------------------------------------- vehicles

def engine_loop(fire_hz, d, harmonics, rough=0.15, noise_amt=0.2, noise_band=(80, 1200), drive=1.6, sub=0.0, seed=0):
    r = np.random.default_rng(seed)
    n = int(d * SR)
    t = np.arange(n) / SR
    jitter = 1 + 0.01 * filt(r.standard_normal(n), "lp", 8) * 6
    f = fire_hz * jitter
    out = np.zeros(n)
    for k, a in enumerate(harmonics, start=1):
        out += a * osc(f * k, phase0=r.uniform())
    pulses = osc(f, shape="pulse25")
    am = 1 + rough * filt(r.standard_normal(n), "lp", fire_hz * 0.5) * 3
    out = out * am + 0.25 * pulses * rough
    if sub:
        out += sub * osc(f * 0.5)
    out += noise_amt * bp(r.standard_normal(n), *noise_band) * (0.6 + 0.4 * np.abs(osc(f)))
    return loopify(sat(out * 0.6, drive), 0.2)


def electric_loop(base, d, partials, hum=50, hum_amt=0.3, hiss=0.05, seed=0):
    r = np.random.default_rng(seed)
    n = int(d * SR)
    out = np.zeros(n)
    wob = 1 + 0.002 * np.sin(2 * np.pi * 0.7 * np.arange(n) / SR)
    for mult, a in partials:
        out += a * osc(base * mult * wob, phase0=r.uniform())
    out += hum_amt * (osc(hum, d) + 0.5 * osc(hum * 2, d) + 0.25 * osc(hum * 3, d))
    out += hiss * filt(r.standard_normal(n), "hp", 4000)
    return loopify(out, 0.2)


def vehicles():
    F = "Engines"
    # Four-cylinder hatchback: buzzy, two firings per rev.
    write(F, "hatchback_low", engine_loop(30, 2.0, [1, 0.6, 0.5, 0.25, 0.2, 0.1], rough=0.25, noise_amt=0.25, seed=1))
    write(F, "hatchback_high", engine_loop(100, 2.0, [1, 0.7, 0.6, 0.5, 0.3, 0.2, 0.15], rough=0.2, noise_amt=0.35, noise_band=(200, 3000), drive=2.2, seed=2))
    # Six-cylinder sedan: smoother, three firings per rev.
    write(F, "sedan_low", engine_loop(40, 2.0, [1, 0.4, 0.3, 0.12, 0.06], rough=0.1, noise_amt=0.15, seed=3))
    write(F, "sedan_high", engine_loop(130, 2.0, [1, 0.5, 0.4, 0.3, 0.15, 0.08], rough=0.08, noise_amt=0.25, noise_band=(200, 2500), drive=1.8, seed=4))
    # Cab: an old V8 burble, uneven firing.
    write(F, "cab_low", engine_loop(28, 2.0, [1, 0.8, 0.3, 0.4, 0.1], rough=0.4, noise_amt=0.2, sub=0.5, seed=5))
    write(F, "cab_high", engine_loop(95, 2.0, [1, 0.8, 0.5, 0.4, 0.2, 0.1], rough=0.3, noise_amt=0.3, noise_band=(150, 2200), drive=2.0, sub=0.3, seed=6))
    # Race car: screaming high-revving V10.
    write(F, "race_low", engine_loop(220, 2.0, [1, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2], rough=0.15, noise_amt=0.3, noise_band=(400, 5000), drive=3.0, seed=7))
    write(F, "race_high", engine_loop(620, 2.0, [1, 0.9, 0.7, 0.5, 0.35, 0.2], rough=0.1, noise_amt=0.35, noise_band=(800, 8000), drive=3.5, seed=8))
    # Muni diesel bus: low clattery rumble.
    bus_low = engine_loop(22, 2.0, [1, 0.9, 0.7, 0.5, 0.4, 0.3], rough=0.6, noise_amt=0.45, noise_band=(60, 900), drive=2.5, sub=0.6, seed=9)
    write(F, "bus_low", bus_low)
    write(F, "bus_high", engine_loop(55, 2.0, [1, 0.9, 0.7, 0.6, 0.4, 0.3, 0.2], rough=0.5, noise_amt=0.5, noise_band=(80, 1500), drive=2.5, sub=0.4, seed=10))
    # Cybertruck: dual-motor whine plus inverter tone, pitched by speed.
    write(F, "ev_whine", electric_loop(420, 2.0, [(1, 1.0), (2, 0.35), (3.02, 0.2), (7.5, 0.08)], hum=60, hum_amt=0.12, hiss=0.06, seed=11))
    write(F, "ev_hum", electric_loop(90, 2.0, [(1, 1.0), (2, 0.5), (4, 0.2)], hum=45, hum_amt=0.4, hiss=0.02, seed=12))
    # Robotaxi: a softer, purer EV tone (pedestrian warning sound).
    write(F, "robotaxi_whine", electric_loop(520, 2.0, [(1, 1.0), (1.5, 0.3), (2, 0.15)], hum=55, hum_amt=0.05, hiss=0.03, seed=13))
    # T Third light rail: traction hum + gear whine + track rumble.
    tram = electric_loop(240, 3.0, [(1, 0.7), (2, 0.25), (3, 0.15)], hum=60, hum_amt=0.6, hiss=0.04, seed=14)
    n = len(tram)
    clack = np.zeros(n)
    for s in np.arange(0.2, 3.0, 0.75):
        for off in (0.0, 0.11):
            i = int((s + off) * SR) % n
            k = int(0.05 * SR)
            seg = bp(noise(k), 200, 1500) * env_exp(k, 60)
            e = min(k, n - i)
            clack[i : i + e] += seg[:e] * 1.4
    write(F, "tram_run", loopify(tram + clack + 0.4 * filt(brown(n), "lp", 200), 0.2))
    # E-bike and scooter: small hub-motor whir plus chain/freewheel ticks.
    eb = electric_loop(310, 2.0, [(1, 1.0), (2, 0.3), (5, 0.08)], hum=0.1, hum_amt=0.0, hiss=0.05, seed=15)
    ticks = np.zeros(len(eb))
    for i in range(0, len(eb), int(SR / 18)):
        k = int(0.004 * SR)
        ticks[i : i + k] += filt(noise(k), "hp", 3000)[: len(eb) - i] * env_exp(k, 800) * 0.6
    write(F, "ebike_whir", loopify(eb + ticks, 0.15))
    write(F, "scooter_whir", electric_loop(760, 2.0, [(1, 1.0), (2, 0.4), (3, 0.2)], hum=0.1, hum_amt=0.0, hiss=0.08, seed=16))
    # Shopping cart: wire-basket rattle + caster rumble loop (squeaks are one-shots).
    n = int(2.0 * SR)
    rat = np.zeros(n)
    for _ in range(140):
        i = rng.integers(0, n - 300)
        k = rng.integers(60, 250)
        rat[i : i + k] += noise(k) * env_exp(k, 300) * rng.uniform(0.2, 1)
    rat = bp(rat, 1500, 7000) + 0.5 * bp(brown(n), 60, 400)
    write(F, "cart_rattle", loopify(rat, 0.15))
    for i in range(3):
        d = rng.uniform(0.12, 0.22)
        t = T(d)
        f = rng.uniform(1800, 2600) + np.sin(2 * np.pi * rng.uniform(25, 60) * t) * rng.uniform(250, 600)
        sq = osc(f) * np.sin(np.pi * t / d) ** 2
        write(F, f"cart_squeak_{i + 1}", sq + 0.1 * bp(noise(len(t)), 2000, 6000))
    # Road, wind and skid layers.
    n = int(3.0 * SR)
    road = filt(brown(n), "lp", 350) + 0.25 * bp(noise(n), 300, 1200)
    write(F, "tire_roll", loopify(road, 0.3))
    wind = bp(pink(n), 200, 2000) * (0.7 + 0.3 * filt(noise(n), "lp", 1.5) * 6)
    write(F, "wind_speed", loopify(wind, 0.4))
    t = T(2.0)
    sq = osc(1250 + 90 * filt(noise(len(t)), "lp", 12) * 6) * 0.6 + osc(2510 + 120 * filt(noise(len(t)), "lp", 10) * 6) * 0.25
    skid = sq * (0.7 + 0.3 * np.abs(filt(noise(len(t)), "lp", 30) * 5)) + 0.5 * bp(noise(len(t)), 800, 4000)
    write(F, "tire_skid", loopify(skid, 0.2))
    # Horns, door, bell.
    S = "Vehicles"
    for name, freqs, d, shape in [
        ("horn_hatchback", [420, 525], 0.35, "sq"),
        ("horn_sedan", [349, 440], 0.45, "sq"),
        ("horn_cab", [311, 392], 0.5, "saw"),
        ("horn_truck", [233, 277], 0.6, "saw"),
        ("horn_race", [587, 740], 0.18, "sq"),
        ("horn_scooter", [880, 1100], 0.15, "sq"),
    ]:
        n = int(d * SR)
        x = sum(osc(f * (1 + 0.004 * np.sin(2 * np.pi * 6 * np.arange(n) / SR)), shape=shape) for f in freqs)
        x = filt(x, "lp", 2600) * adsr(n, 0.01, 0.03, 0.9, 0.04)
        write(S, name, sat(x, 1.5))
    n = int(1.1 * SR)
    air = sum(osc(f, shape="saw", d=1.1) for f in [138, 174, 207]) * adsr(n, 0.04, 0.1, 0.85, 0.2)
    write(S, "horn_bus", sat(filt(air, "lp", 1800), 2.0))
    write(S, "bus_air_brake", bp(noise(int(0.8 * SR)), 2000, 9000) * adsr(int(0.8 * SR), 0.005, 0.05, 0.6, 0.5))
    bell = metal_ring([980, 2630, 4100, 5200], 1.4, 3.5)
    write(S, "tram_bell", at(bell, 0) + at(bell, 0.33, int(1.8 * SR)) * 0.9)
    write(S, "bike_bell", at(metal_ring([2637, 3950, 5300], 0.6, 7), 0) + at(metal_ring([2637, 3950, 5300], 0.6, 7), 0.16, int(0.8 * SR)))
    write(S, "robotaxi_chime", at(osc(784, 0.3) * env_exp(int(0.3 * SR), 6), 0) + at(osc(1046, 0.5) * env_exp(int(0.5 * SR), 6), 0.28, int(0.8 * SR)))
    for i, (f0, latch) in enumerate([(85, 0.06), (70, 0.08)]):
        d = thump(f0, 40, 0.35, 14) + at(click(0.02, 1500) * 0.6, latch, int(0.35 * SR)) + 0.3 * bp(noise(int(0.35 * SR)), 200, 900) * env_exp(int(0.35 * SR), 25)
        write(S, f"door_{'close' if i == 0 else 'open'}", d)
    write(S, "door_open", at(click(0.015, 2500), 0) + at(thump(140, 70, 0.2, 25) * 0.5, 0.04, int(0.25 * SR)) + 0.2 * rustle(0.25))
    # Impacts and crunches.
    for i in range(3):
        x = thump(rng.uniform(70, 110), 35, 0.6, 9) + 0.8 * crunch(0.5, 4000, 180) + 0.5 * metal_ring(list(rng.uniform(300, 2200, 5)), 0.6, 9)
        write(S, f"crunch_heavy_{i + 1}", sat(x, 1.8))
    for i in range(3):
        x = thump(rng.uniform(110, 150), 60, 0.3, 18) * 0.7 + 0.6 * crunch(0.25, 5000, 120) + 0.3 * metal_ring(list(rng.uniform(600, 3000, 4)), 0.3, 18)
        write(S, f"crunch_light_{i + 1}", x)
    for i in range(3):
        write(S, f"scrape_{i + 1}", bp(noise(int(0.6 * SR)), 900, 6000) * adsr(int(0.6 * SR), 0.02, 0.1, 0.7, 0.3) * (0.7 + 0.3 * np.sin(2 * np.pi * 37 * T(0.6))) + 0.3 * metal_ring([1400, 2900], 0.6, 6))
    w = thump(70, 28, 1.6, 4.0) * 1.2 + crunch(1.2, 5000, 400) * 1.1 + 0.6 * glass(1.4, 90, 3) + 0.4 * metal_ring([240, 610, 1330, 2700], 1.6, 3)
    debris = np.zeros(int(2.2 * SR))
    for _ in range(25):
        s = rng.uniform(0.4, 1.9)
        k = int(0.08 * SR)
        debris = mix(debris, at(metal_ring([rng.uniform(1500, 5000)], 0.08, 40) * rng.uniform(0.1, 0.4), s))
    write(S, "wreck", sat(mix(w, debris), 1.6))
    write(S, "boost_whoosh", bp(pink(int(1.0 * SR)), 300, 5000) * adsr(int(1.0 * SR), 0.15, 0.2, 0.5, 0.6) + 0.3 * osc(np.linspace(120, 480, int(1.0 * SR)), shape="saw") * adsr(int(1.0 * SR), 0.2, 0.3, 0.3, 0.4) * 0.3)


# ---------------------------------------------------------------- impacts and breakables

def impacts():
    F = "Impacts"
    for i in range(3):
        write(F, f"metal_{i + 1}", thump(rng.uniform(120, 180), 70, 0.4, 16) * 0.6 + metal_ring(list(rng.uniform(350, 3500, 6)), 0.7, 7) + 0.3 * crunch(0.15, 6000, 80))
        write(F, f"concrete_{i + 1}", thump(rng.uniform(80, 110), 40, 0.4, 14) + 0.6 * bp(noise(int(0.3 * SR)), 300, 2500) * env_exp(int(0.3 * SR), 25) + 0.4 * crunch(0.3, 3000, 60))
        write(F, f"wood_{i + 1}", wood_crack(0.45, i))
        write(F, f"plastic_{i + 1}", thump(rng.uniform(200, 320), 150, 0.25, 22) + 0.4 * bp(noise(int(0.2 * SR)), 800, 4000) * env_exp(int(0.2 * SR), 35))
        write(F, f"foliage_{i + 1}", rustle(0.7) + 0.3 * wood_crack(0.2, 10 + i) * 0.5)
        write(F, f"glass_{i + 1}", glass(0.9, 50 + 10 * i, 20 + i))
        b = thump(rng.uniform(150, 200), 90, 0.25, 20) + 0.3 * osc(np.linspace(300, 180, int(0.25 * SR))) * env_exp(int(0.25 * SR), 15)
        write(F, f"body_{i + 1}", b)
    n = int(1.2 * SR)
    splash = bp(noise(n), 300, 6000) * adsr(n, 0.005, 0.15, 0.4, 0.8) + thump(70, 35, 0.6, 7)
    for _ in range(20):
        splash = mix(splash, at(osc(rng.uniform(600, 1800), 0.04) * env_exp(int(0.04 * SR), 60) * rng.uniform(0.1, 0.4), rng.uniform(0.15, 1.0)))
    write(F, "splash", splash)
    F = "Breakables"
    gush = water_spray(4.0)
    write(F, "hydrant_gush", loopify(gush, 0.4))
    write(F, "hydrant_break", thump(110, 50, 0.5, 10) + 0.8 * metal_ring([310, 820, 1650, 2900], 0.9, 5) + at(water_spray(0.8) * adsr(int(0.8 * SR), 0.05, 0.2, 0.8, 0.4), 0.05, int(0.9 * SR)))
    zap = bp(noise(int(0.6 * SR)), 2000, 8000) * (np.sin(2 * np.pi * 60 * T(0.6)) > 0.3) * env_exp(int(0.6 * SR), 5)
    write(F, "street_light", metal_ring([180, 470, 1020, 2210], 1.4, 3.5) + 0.7 * at(glass(0.6, 30, 30), 0.12, int(1.4 * SR)) + 0.4 * at(zap, 0.1, int(1.4 * SR)))
    beep = osc(1200, 0.12) * adsr(int(0.12 * SR), 0.005, 0.02, 0.6, 0.05)
    write(F, "traffic_light", metal_ring([260, 690, 1500], 1.0, 4) + 0.4 * at(zap, 0.05, int(1.1 * SR)) + 0.3 * at(beep, 0.3, int(1.1 * SR)) + 0.2 * at(beep * 0.7, 0.5, int(1.1 * SR)))
    rolls = np.zeros(int(1.2 * SR))
    for k in range(7):
        rolls = mix(rolls, at(thump(rng.uniform(250, 380), 180, 0.08, 40) * rng.uniform(0.3, 0.8), 0.25 + k * 0.12))
    write(F, "trash_can", thump(240, 140, 0.4, 14) + 0.5 * metal_ring([520, 1310], 0.5, 9) + rolls)
    paper = np.zeros(int(1.0 * SR))
    for _ in range(18):
        paper = mix(paper, at(bp(noise(int(0.05 * SR)), 1500, 7000) * env_exp(int(0.05 * SR), 50) * rng.uniform(0.1, 0.4), rng.uniform(0.1, 0.9)))
    write(F, "newspaper_box", thump(150, 80, 0.4, 14) + 0.6 * metal_ring([420, 980, 2100], 0.6, 8) + paper)
    coins = np.zeros(int(1.0 * SR))
    for _ in range(16):
        coins = mix(coins, at(metal_ring([rng.uniform(3500, 6500), rng.uniform(7000, 9500)], 0.12, 35) * rng.uniform(0.2, 0.6), rng.uniform(0.08, 0.8)))
    write(F, "parking_meter", metal_ring([340, 900, 1900], 0.7, 7) + coins)
    write(F, "bench", wood_crack(0.6, 40) + 0.4 * wood_crack(0.4, 41) * 0.8 + 0.4 * thump(90, 45, 0.5, 9))
    write(F, "tree", wood_crack(0.4, 50) + at(rustle(1.3, 7000), 0.05, int(1.4 * SR)) + 0.3 * thump(70, 40, 0.6, 7))
    write(F, "bus_shelter", glass(1.6, 140, 60) + 0.4 * metal_ring([210, 540, 1300], 1.2, 4) + 0.3 * thump(90, 45, 0.4, 12))
    write(F, "fence", wood_crack(0.5, 70) + 0.6 * at(wood_crack(0.4, 71), 0.07, int(0.6 * SR)))
    cone = thump(330, 200, 0.18, 30) + 0.25 * osc(np.linspace(520, 380, int(0.18 * SR))) * env_exp(int(0.18 * SR), 20)
    write(F, "cone", mix(cone, at(cone * 0.4, 0.2), at(cone * 0.2, 0.33)))
    write(F, "barrier", thump(200, 110, 0.4, 14) + 0.5 * bp(noise(int(0.3 * SR)), 600, 3000) * env_exp(int(0.3 * SR), 20) + 0.3 * wood_crack(0.3, 80))
    write(F, "mailbox", thump(120, 60, 0.5, 10) + 0.6 * metal_ring([180, 445, 980], 0.8, 5) + 0.3 * paper[: int(0.8 * SR)])
    write(F, "bike_dock", metal_ring([520, 1240, 2710, 3900], 1.0, 5) + 0.6 * crunch(0.4, 5000, 150) + at(metal_ring([1800, 3500], 0.3, 12) * 0.4, 0.25, int(1.0 * SR)))
    ding = metal_ring([1180, 2950], 0.6, 6)
    write(F, "payphone", metal_ring([300, 760, 1500], 0.8, 6) + 0.6 * at(ding, 0.15, int(0.9 * SR)) + 0.3 * at(ding, 0.35, int(0.95 * SR)))
    n = int(1.2 * SR)
    wob = osc(70 + 30 * np.sin(2 * np.pi * 9 * T(1.2)) * env_exp(n, 2), shape="tri") * env_exp(n, 3)
    write(F, "sign", wob * 0.8 + 0.5 * metal_ring([400, 1100], 0.5, 8))
    write(F, "billboard", wood_crack(0.8, 90) + wob * 0.6 + 0.6 * thump(60, 30, 1.0, 4))
    write(F, "planter", thump(90, 45, 0.4, 12) + 0.8 * crunch(0.6, 3000, 200) + 0.3 * rustle(0.5))
    box = thump(140, 80, 0.3, 18) + 0.4 * bp(noise(int(0.3 * SR)), 300, 1500) * env_exp(int(0.3 * SR), 20)
    write(F, "crates", mix(box, at(box * 0.6, 0.15), at(box * 0.4, 0.31), at(wood_crack(0.3, 95) * 0.5, 0.02)))
    write(F, "storefront_glass", glass(2.0, 200, 120) + 0.3 * thump(80, 40, 0.5, 8))
    write(F, "fruit_stand", mix(box, *[at(thump(rng.uniform(180, 300), 120, 0.1, 30) * 0.5, rng.uniform(0.1, 1.0)) for _ in range(14)]))
    write(F, "generic", thump(120, 60, 0.3, 14) + 0.5 * crunch(0.3, 4000, 100))


# ---------------------------------------------------------------- world ambience

def ambience():
    F = "Ambience"
    d = 24.0
    n = int(d * SR)
    t = np.arange(n) / SR
    city = filt(brown(n), "lp", 300) * 0.7 + 0.3 * bp(pink(n), 200, 1500)
    write(F, "city_base", loopify(city, 1.0))
    # Third Street traffic: a stream of pass-bys plus a bus.
    traffic = 0.4 * filt(brown(n), "lp", 250)
    for _ in range(14):
        c = rng.uniform(0, d)
        w = rng.uniform(1.2, 3.0)
        g = np.exp(-((t - c) / (w / 2)) ** 2)
        f = rng.uniform(35, 70)
        car = (bp(noise(n), 150, 1800) * 0.6 + osc(f * (1 + 0.05 * np.tanh((c - t) * 2))) * 0.3) * g
        traffic += car * rng.uniform(0.4, 1)
    write(F, "third_st_traffic", loopify(traffic, 1.0))
    gust = 0.6 + 0.4 * filt(noise(n), "lp", 0.3) * 30
    wind = bp(pink(n), 150, 1200) * np.clip(gust, 0.1, 1.5) + 0.3 * bp(noise(n), 1500, 4000) * np.clip(gust - 0.6, 0, 1)
    write(F, "pier_wind", loopify(wind, 1.5))
    lap = np.zeros(n)
    for c in np.arange(0.5, d, 1.7):
        lap += bp(noise(n), 200, 1500) * np.exp(-((t - c - rng.uniform(-0.3, 0.3)) / 0.35) ** 2) * rng.uniform(0.3, 1)
    write(F, "bay_water", loopify(lap + 0.2 * filt(brown(n), "lp", 200), 1.0))
    hum = 0.8 * filt(brown(n), "lp", 180) + 0.4 * bp(pink(n), 300, 900) + 0.1 * osc(55, d) + 0.05 * osc(110, d)
    write(F, "freeway_hum", loopify(hum, 1.0))
    rain = bp(noise(n), 800, 9000) * 0.6 + 0.4 * filt(pink(n), "lp", 1200)
    drops = np.zeros(n)
    for _ in range(900):
        i = rng.integers(0, n - 200)
        k = rng.integers(30, 120)
        drops[i : i + k] += filt(noise(k), "hp", 2000) * env_exp(k, 300) * rng.uniform(0.2, 1)
    write(F, "rain", loopify(rain + drops, 1.0))
    snow = filt(pink(n), "lp", 600) * (0.5 + 0.5 * np.clip(filt(noise(n), "lp", 0.2) * 40, 0, 1)) + 0.15 * bp(noise(n), 3000, 8000)
    write(F, "snow", loopify(snow, 1.5))
    crickets = np.zeros(n)
    for voice_i in range(7):
        f = rng.uniform(3800, 5200)
        rate = rng.uniform(1.5, 3.2)
        phase = rng.uniform(0, 1)
        pan_gain = rng.uniform(0.2, 1)
        gate = ((t * rate + phase) % 1.0) < 0.22
        chirp = (np.sin(2 * np.pi * 30 * t) > 0) * gate
        crickets += osc(f, d) * filt(chirp.astype(float), "lp", 400) * pan_gain
    write(F, "night_crickets", loopify(crickets + 0.1 * filt(brown(n), "lp", 200), 1.0))
    birds = np.zeros(n)
    for _ in range(40):
        c = rng.uniform(0, d - 1)
        dd = rng.uniform(0.08, 0.3)
        tt = T(dd)
        f = rng.uniform(2500, 4500) + rng.uniform(-1500, 1500) * tt / dd + 300 * np.sin(2 * np.pi * rng.uniform(20, 40) * tt)
        birds = mix(birds, at(osc(f) * np.sin(np.pi * tt / dd) * rng.uniform(0.2, 0.6), c))
    write(F, "day_birds", loopify(birds[:n] + 0.05 * filt(brown(n), "lp", 200), 1.0))
    # One-shots.
    for i in range(3):
        dd = rng.uniform(0.5, 0.9)
        n2 = int(dd * SR)
        f = np.concatenate([np.linspace(1400, 2200, n2 // 3), np.linspace(2200, 1100, n2 - n2 // 3)]) * rng.uniform(0.85, 1.15)
        g = voice(f / 2.3, [V_A, V_E], dd, breath=0.25, vib=14, vib_amt=0.05) * adsr(n2, 0.02, 0.1, 0.7, dd * 0.4)
        call = mix(g, at(g * 0.7, dd * 0.9), at(g * 0.5, dd * 1.8))
        write(F, f"gull_{i + 1}", call)
    n4 = int(4.5 * SR)
    def horn(f, dd):
        nn = int(dd * SR)
        x = sum(osc(f * k, dd, "saw") / k for k in range(1, 6))
        return filt(x, "lp", 700) * adsr(nn, 0.25, 0.2, 0.9, 0.5)
    foghorn = mix(horn(165, 2.0), at(horn(131, 2.2), 2.1))
    write(F, "foghorn", foghorn + 0.4 * filt(np.pad(foghorn, (int(0.35 * SR), 0))[: len(foghorn)], "lp", 400))
    chord = sum(osc(f, 1.8, "saw") for f in [311, 370, 466, 554, 622])
    ch = filt(chord, "lp", 2500) * adsr(int(1.8 * SR), 0.05, 0.2, 0.85, 0.4)
    write(F, "caltrain_horn", mix(ch, at(ch[: int(0.7 * SR)] * 0.9, 2.1)))
    n5 = int(8.0 * SR)
    tt = T(8.0)
    rum = filt(brown(n5), "lp", 220) * np.exp(-((tt - 4) / 2.2) ** 2) + 0.5 * bp(noise(n5), 300, 2500) * np.exp(-((tt - 4) / 1.6) ** 2)
    write(F, "caltrain_pass", rum)
    sir_f = 900 + 350 * np.sin(2 * np.pi * 0.6 * T(6.0))
    write(F, "distant_siren", filt(osc(sir_f, shape="tri"), "lp", 1800) * adsr(int(6 * SR), 1.5, 0.5, 0.8, 2.5))


# ---------------------------------------------------------------- people

def people():
    F = "People"
    surfaces = {
        "asphalt": (80, 2500, 0.6, 0.06),
        "concrete": (100, 3500, 0.8, 0.05),
        "grass": (300, 6000, 0.3, 0.12),
        "wood": (120, 1800, 1.0, 0.06),
        "metal": (200, 4500, 0.7, 0.05),
        "water": (300, 5000, 0.4, 0.15),
    }
    for s, (lo, hi, knock, dd) in surfaces.items():
        for i in range(3):
            n = int(0.18 * SR)
            heel = thump(rng.uniform(90, 140), 50, 0.18, 35) * knock
            scuff = bp(noise(n), lo, hi) * env_exp(n, 1 / dd) * 0.7
            x = mix(heel, scuff)
            if s == "grass":
                x = mix(x * 0.4, rustle(0.18, 7000))
            if s == "metal":
                x = mix(x, metal_ring([rng.uniform(600, 1200), rng.uniform(1800, 3000)], 0.18, 25) * 0.4)
            if s == "water":
                x = mix(x * 0.3, bp(noise(n), 400, 3000) * env_exp(n, 20) * (1 + np.sin(2 * np.pi * 40 * T(0.18))))
            write(F, f"step_{s}_{i + 1}", x)
    # Playful pedestrian voices: no pain, no screams. Pitch tracks are fractions of a base.
    bases = [220, 260, 175, 300, 140, 240]
    def line(name, idx, track, vowels, d, h=0.0):
        b = bases[idx % len(bases)]
        x = voice(np.array(track) * b, vowels, d, breath=0.12, vib=6, vib_amt=0.015)
        n = len(x)
        x *= adsr(n, 0.02, 0.05, 0.85, d * 0.35)
        if h:
            x = mix(h_noise(h), at(x, h * 0.7))
        write(F, name, x)
    line("hey_1", 0, [1.0, 1.25, 1.1], [V_E, V_E, V_I], 0.42, h=0.07)
    line("hey_2", 1, [1.1, 1.35, 1.2], [V_E, V_E, V_I], 0.38, h=0.06)
    line("hey_3", 4, [1.0, 1.2, 1.05], [V_E, V_I], 0.45, h=0.08)
    line("whoa_1", 2, [1.0, 1.5, 1.2], [V_U, V_O, V_A], 0.55)
    line("whoa_2", 3, [1.1, 1.6, 1.3], [V_U, V_O, V_A], 0.5)
    line("woo_1", 5, [1.0, 1.8, 1.6], [V_U, V_U], 0.5)
    line("yikes_1", 3, [1.4, 1.7, 1.3], [V_I, V_A, V_I], 0.4)
    line("oops_1", 1, [1.3, 1.0], [V_O, V_U], 0.35)
    line("jump_hup_1", 4, [1.0, 1.15], [V_UH, V_UH], 0.16, h=0.04)
    line("jump_hup_2", 4, [1.1, 1.25], [V_UH, V_U], 0.14, h=0.03)
    write(F, "land", thump(110, 50, 0.2, 25) + 0.4 * bp(noise(int(0.15 * SR)), 200, 2000) * env_exp(int(0.15 * SR), 30))
    write(F, "shove", thump(160, 80, 0.15, 30) + 0.4 * bp(pink(int(0.3 * SR)), 300, 3000) * adsr(int(0.3 * SR), 0.02, 0.05, 0.5, 0.2))


# ---------------------------------------------------------------- game and UI

def tone(f, d, shape="sin", a=0.005, r=0.1, sr=SR):
    n = int(d * sr)
    return osc(f, d, shape, sr) * adsr(n, a, 0.03, 0.8, r, sr)


def ui():
    F = "Ui"
    write(F, "countdown_beep", tone(880, 0.22, "sq", r=0.05) * 0.5 + tone(1760, 0.22) * 0.3)
    slam = thump(70, 30, 0.9, 5) * 1.3 + 0.8 * tone(1760, 0.6, "sq") * env_exp(int(0.6 * SR), 4) * 0.5 + crunch(0.4, 4000, 120) * 0.5
    write(F, "countdown_go", mix(slam, at(bp(pink(int(0.6 * SR)), 500, 6000) * adsr(int(0.6 * SR), 0.01, 0.1, 0.4, 0.4) * 0.7, 0.02)))
    up = mix(*[at(tone(f, 0.12, "tri"), i * 0.06) for i, f in enumerate([784, 988, 1175, 1568])])
    write(F, "place_up", mix(up, bp(pink(int(0.4 * SR)), 1500, 7000) * adsr(int(0.4 * SR), 0.15, 0.05, 0.3, 0.2) * 0.4))
    down = mix(*[at(tone(f, 0.14, "tri"), i * 0.07) for i, f in enumerate([784, 659, 523])])
    write(F, "place_down", down)
    whoosh = bp(pink(int(0.5 * SR)), 600, 6000) * adsr(int(0.5 * SR), 0.12, 0.05, 0.6, 0.3)
    write(F, "news_whoosh", mix(whoosh, at(thump(220, 120, 0.1, 40) * 0.4, 0.2)))
    ring_n = int(2.0 * SR)
    tt = T(2.0)
    bell = (osc(1000 + 0 * tt) + osc(1250 + 0 * tt) * 0.7) * (np.sin(2 * np.pi * 20 * tt) > 0) * ((tt % 2.0) < 1.0)
    write(F, "call_ring", filt(bell, "lp", 4000) * 0.8)
    write(F, "payphone_pickup", mix(thump(90, 50, 0.25, 18), at(click(0.02, 1200), 0.05), at(metal_ring([1800, 3600], 0.15, 25) * 0.3, 0.07), at(tone(425, 0.25, "sin") * 0.25, 0.25)))
    write(F, "call_hangup", mix(thump(100, 50, 0.2, 20), at(click(0.015, 1500), 0.03)))
    n = int(6.0 * SR)
    line = bp(noise(n), 300, 3400) * 0.15 + 0.08 * osc(60, 6.0) + 0.05 * osc(120, 6.0)
    crack = np.zeros(n)
    for _ in range(40):
        i = rng.integers(0, n - 100)
        crack[i : i + 30] += noise(30) * rng.uniform(0.1, 0.5)
    write(F, "operator_line_bed", loopify(line + bp(crack, 1000, 5000), 0.5))
    write(F, "operator_on", mix(tone(1568, 0.08, "sq") * 0.4, at(tone(2093, 0.12, "sq") * 0.4, 0.08)))
    write(F, "pause_open", mix(tone(659, 0.1, "tri"), at(tone(988, 0.15, "tri"), 0.06)))
    write(F, "pause_close", mix(tone(988, 0.1, "tri"), at(tone(659, 0.15, "tri"), 0.06)))
    write(F, "ui_tap", tone(1400, 0.05, "tri", r=0.03) * env_exp(int(0.05 * SR), 40))
    write(F, "ui_ready", mix(tone(880, 0.1, "sq") * 0.5, at(tone(1320, 0.18, "sq") * 0.5, 0.08)))
    write(F, "toast", tone(1200, 0.08, "tri") * env_exp(int(0.08 * SR), 30))


# ---------------------------------------------------------------- music

BPM = 140
BEAT = 60 / BPM
BARS = 16
MLEN = BARS * 4 * BEAT


def mtone(freq, d, shape, sr=MSR):
    return osc(freq, d, shape, sr)


def note_hz(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def place(buf, x, t0, pan=0.0):
    i = int(t0 * MSR)
    e = min(len(x), buf.shape[1] - i)
    if e <= 0:
        return
    l, r = np.cos((pan + 1) * np.pi / 4), np.sin((pan + 1) * np.pi / 4)
    buf[0, i : i + e] += x[:e] * l
    buf[1, i : i + e] += x[:e] * r


def mf(x, kind, f, order=2):
    return filt(x, kind, f, order, MSR)


def kick():
    d = 0.35
    t = np.arange(int(d * MSR)) / MSR
    f = 45 + 120 * np.exp(-t * 35)
    return np.sin(2 * np.pi * np.cumsum(f) / MSR) * np.exp(-t * 9) + 0.3 * mf(noise(len(t)), "hp", 3000) * np.exp(-t * 200)


def snare():
    d = 0.25
    t = np.arange(int(d * MSR)) / MSR
    return 0.5 * np.sin(2 * np.pi * 190 * t) * np.exp(-t * 25) + 0.8 * mf(noise(len(t)), "bandpass", [1200, 9000]) * np.exp(-t * 16)


def hat(open_=False):
    d = 0.25 if open_ else 0.05
    t = np.arange(int(d * MSR)) / MSR
    return mf(noise(len(t)), "hp", 7000) * np.exp(-t * (12 if open_ else 90))


def clap():
    d = 0.2
    t = np.arange(int(d * MSR)) / MSR
    e = np.exp(-t * 30) + 0.6 * np.exp(-np.maximum(t - 0.012, 0) * 30) * (t > 0.012)
    return mf(noise(len(t)), "bandpass", [900, 5000]) * e


def synth(m, d, shape="saw", cutoff=3000, detune=0.0, a=0.005, rel=0.08, voices=1):
    n = int(d * MSR)
    f = note_hz(m)
    x = np.zeros(n)
    for v in range(voices):
        dt = (v - (voices - 1) / 2) * detune
        x += osc(f * 2 ** (dt / 1200), n / MSR, shape, MSR, phase0=rng.uniform())
    x /= voices
    x = mf(x, "lp", cutoff)
    return x * adsr(n, a, 0.05, 0.75, min(rel, d * 0.5), MSR)


# E major arcade progression, two bars each: E - C#m - A - B, with a lift to F#m - B in the chorus.
CHORDS = [
    [52, 56, 59, 63], [49, 52, 56, 59], [45, 49, 52, 56], [47, 51, 54, 57],
    [52, 56, 59, 63], [49, 52, 56, 59], [45, 49, 52, 56], [47, 51, 54, 57],
    [45, 49, 52, 56], [47, 51, 54, 57], [44, 47, 52, 56], [49, 52, 56, 59],
    [42, 45, 49, 52], [47, 51, 54, 57], [45, 49, 52, 56], [47, 51, 54, 59],
]
LEAD_A = [(0, 71, 0.5), (0.5, 73, 0.5), (1, 75, 1), (2, 76, 0.5), (2.5, 75, 0.5), (3, 73, 1),
          (4, 71, 0.5), (4.5, 68, 0.5), (5, 71, 1.5), (6.5, 73, 0.5), (7, 75, 1)]
LEAD_B = [(0, 76, 0.75), (0.75, 78, 0.75), (1.5, 80, 1), (2.5, 78, 0.5), (3, 76, 1), (4, 75, 0.5),
          (4.5, 76, 0.5), (5, 78, 1), (6, 80, 0.5), (6.5, 83, 0.5), (7, 80, 1)]


def music():
    F = "Music"
    n = int(MLEN * MSR)
    roam = np.zeros((2, n))
    drive = np.zeros((2, n))
    lead = np.zeros((2, n))
    for bar in range(BARS):
        chord = CHORDS[bar]
        t0 = bar * 4 * BEAT
        # Roam layer: soft pads, round bass on the beat, light shaker.
        for k, m in enumerate(chord):
            pad = synth(m + 12, 4 * BEAT, "saw", 1400, detune=12, a=0.3, rel=0.4, voices=3) * 0.12
            place(roam, pad, t0, pan=(k - 1.5) * 0.4)
        for b in range(4):
            place(roam, synth(chord[0] - 12, BEAT * 0.9, "tri", 600) * 0.45, t0 + b * BEAT)
            place(roam, hat() * 0.12, t0 + b * BEAT + BEAT / 2, pan=0.3)
        for b in (0, 2.5):
            place(roam, kick() * 0.35, t0 + b * BEAT)
        for s in range(8):
            m = chord[[0, 1, 2, 3, 2, 1, 2, 3][s]] + 12
            place(roam, synth(m, BEAT * 0.45, "tri", 2500, rel=0.2) * 0.12, t0 + s * BEAT / 2, pan=-0.3 if s % 2 else 0.3)
        # Drive layer: full drums, pumping octave bass, 16th arps.
        for b in range(4):
            place(drive, kick() * 0.9, t0 + b * BEAT)
            if b in (1, 3):
                place(drive, snare() * 0.6, t0 + b * BEAT)
                place(drive, clap() * 0.35, t0 + b * BEAT, pan=0.1)
            for s in range(4):
                place(drive, hat(s == 2) * (0.28 if s % 2 else 0.18), t0 + b * BEAT + s * BEAT / 4, pan=0.25)
        if bar % 4 == 3:
            for s in range(4):
                place(drive, snare() * (0.3 + 0.1 * s), t0 + 3 * BEAT + s * BEAT / 4)
        for e in range(8):
            m = chord[0] - 12 + (12 if e % 2 else 0)
            place(drive, synth(m, BEAT / 2 * 0.85, "saw", 900 + 300 * (e % 2), detune=8, voices=2) * 0.42, t0 + e * BEAT / 2)
        for s in range(16):
            m = chord[[0, 1, 2, 3][s % 4]] + 24
            place(drive, synth(m, BEAT / 4 * 0.8, "sq", 3500, rel=0.03) * 0.09, t0 + s * BEAT / 4, pan=0.5 if s % 2 else -0.5)
        # Lead layer: hook melody (verse A, chorus B) plus crash and stabs.
        phrase = LEAD_A if bar < 8 else LEAD_B
        if bar % 2 == 0:
            for (bt, m, dur) in phrase:
                x = synth(m, dur * BEAT * 0.95, "saw", 4200, detune=10, a=0.01, rel=0.1, voices=3)
                nn = len(x)
                x *= 1 + 0.15 * np.sin(2 * np.pi * 5.5 * np.arange(nn) / MSR) * np.clip(np.arange(nn) / MSR / 0.2, 0, 1)
                place(lead, x * 0.28, t0 + bt * BEAT, pan=0.05)
                place(lead, x * 0.1, t0 + bt * BEAT + BEAT * 0.75, pan=-0.5)
        for b in (0, 1.5, 3):
            for m in chord[1:]:
                place(lead, synth(m + 12, BEAT * 0.35, "sq", 2800) * 0.07, t0 + b * BEAT, pan=0.2)
        if bar % 4 == 0:
            cr = mf(noise(int(1.5 * MSR)), "hp", 5000) * env_exp(int(1.5 * MSR), 3, MSR)
            place(lead, cr * 0.25, t0, pan=-0.2)
    for name, stem in [("music_roam", roam), ("music_drive", drive), ("music_lead", lead)]:
        for ch in range(2):
            stem[ch] = sat(stem[ch], 1.2)
        write(F, name, stem.T, MSR, peak=0.8, stereo=True)
    # Results sting: a short major fanfare with a slam.
    d = 3.5
    st = np.zeros((2, int(d * MSR)))
    for i, m in enumerate([64, 68, 71, 76]):
        place(st, synth(m, 0.18, "saw", 4000, detune=10, voices=3) * 0.4, i * 0.12)
    for m in [52, 64, 68, 71, 76, 80]:
        place(st, synth(m, 2.4, "saw", 3500, detune=12, a=0.01, rel=1.2, voices=3) * 0.18, 0.5)
    place(st, kick() * 1.0, 0.5)
    place(st, mf(noise(int(2.5 * MSR)), "hp", 5000) * env_exp(int(2.5 * MSR), 2, MSR) * 0.35, 0.5)
    for ch in range(2):
        st[ch] = sat(st[ch], 1.3)
    write(F, "results_sting", st.T, MSR, peak=0.85, stereo=True)


if __name__ == "__main__":
    only = sys.argv[1:]
    for fn in [vehicles, impacts, ambience, people, ui, music]:
        if not only or fn.__name__ in only:
            fn()
            print(f"{fn.__name__}: done", flush=True)
    total = sum(d for _, _, d, _, _ in written)
    print(f"{len(written)} clips, {total:.1f} s total")
