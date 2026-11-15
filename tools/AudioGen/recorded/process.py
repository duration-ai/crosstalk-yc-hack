import sys, json, os, math
import numpy as np, soundfile as sf
from scipy.signal import resample_poly, butter, sosfilt
import pyloudnorm as pyln
from math import gcd

OUT = os.path.expanduser(sys.argv[2])
AUDIO = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "unity", "Assets", "Resources", "Audio")

def load(path, start, dur, mono):
    info = sf.info(path)
    sr = info.samplerate
    s0 = int(start * sr)
    n = int(dur * sr) if dur else -1
    x, sr = sf.read(path, start=s0, frames=n, always_2d=True, dtype="float32")
    if x.shape[1] > 2:
        x = x[:, :2]
    if mono:
        x = x.mean(axis=1, keepdims=True)
    return x, sr

def pick_event(x, sr, k, length, ev_db, gap):
    hop = max(1, int(0.01 * sr))
    n = len(x) // hop
    env = np.abs(x[: n * hop]).max(axis=1).reshape(n, hop).max(axis=1)
    th = env.max() * 10 ** (ev_db / 20)
    onsets, below = [], int(gap / 0.01)
    quiet = below
    for i, e in enumerate(env):
        if e < th: quiet += 1
        else:
            if quiet >= int(gap / 0.01): onsets.append(i)
            quiet = 0
    if not onsets: return x
    i = onsets[min(k, len(onsets) - 1)]
    a = max(0, i * hop - int(0.005 * sr))
    return x[a: a + int(length * sr)]

def resample(x, sr, rate):
    if sr == rate: return x
    g = gcd(sr, rate)
    return resample_poly(x, rate // g, sr // g, axis=0).astype("float32")

def trim_silence(x, sr, thresh_db=-50):
    env = np.abs(x).max(axis=1)
    th = 10 ** (thresh_db / 20) * env.max()
    idx = np.where(env > th)[0]
    if len(idx) == 0: return x
    a = max(0, idx[0] - int(0.003 * sr)); b = min(len(x), idx[-1] + int(0.02 * sr))
    return x[a:b]

def fades(x, sr, fin, fout):
    n = len(x)
    a = min(n, int(fin * sr)); b = min(n, int(fout * sr))
    if a > 0: x[:a] *= np.linspace(0, 1, a)[:, None] ** 0.5
    if b > 0: x[n - b:] *= np.linspace(1, 0, b)[:, None] ** 2
    return x

def make_loop(x, sr, xfade):
    n = int(xfade * sr)
    body = x[:-n].copy()
    tail = x[-n:]
    t = np.linspace(0, 1, n)[:, None]
    body[:n] = body[:n] * np.sqrt(t) + tail * np.sqrt(1 - t)
    return body

def active_rms(x):
    m = np.abs(x).max(axis=1)
    hop = 256; n = len(m) // hop
    if n == 0: return float(np.sqrt((x ** 2).mean()))
    fr = (x[: n * hop] ** 2).mean(axis=1).reshape(n, hop).mean(axis=1)
    th = fr.max() * 10 ** (-40 / 10)
    return float(np.sqrt(fr[fr > th].mean()))

def soft_limit(x, ceil):
    g = np.minimum(1.0, ceil / np.maximum(np.abs(x).max(axis=1), 1e-9))
    k = 64
    gs = np.convolve(np.pad(g, (k, k), mode="edge"), np.ones(2 * k + 1) / (2 * k + 1), mode="valid")
    g = np.minimum(g, gs)
    y = x * g[:, None]
    pk = np.abs(y).max()
    return y * (ceil / pk) if pk > ceil else y

def normalize(x, sr, spec):
    if spec.get("lufs") is not None and len(x) / sr > 0.5:
        meter = pyln.Meter(sr, block_size=min(0.4, len(x) / sr / 2))
        l = meter.integrated_loudness(x if x.shape[1] > 1 else x[:, 0])
        if np.isfinite(l): x = x * 10 ** ((spec["lufs"] - l) / 20)
    else:
        pk = np.abs(x).max()
        if pk > 0: x = x * (10 ** (spec.get("peak", -1.0) / 20) / pk)
    pk = np.abs(x).max()
    ceil = 10 ** (-1.0 / 20)
    if pk > ceil: x = x * (ceil / pk)
    return x

def run(spec):
    mono = spec.get("mono", True)
    rate = spec.get("rate", 22050)
    parts = spec["src"] if isinstance(spec["src"], list) else [spec]
    x = None
    for p in parts:
        y, sr = load(p["src"] if "src" in p and isinstance(p["src"], str) else p["path"], p.get("start", 0), p.get("dur"), mono)
        if "event" in p:
            y = pick_event(y, sr, p["event"], p.get("len", 1.0), p.get("ev_db", -25), p.get("gap", 0.15))
        y = resample(y, sr, rate)
        if p.get("pitch"):
            r = p["pitch"]
            idx = np.arange(int(len(y) / r)) * r
            y = np.stack([np.interp(idx, np.arange(len(y)), y[:, c]) for c in range(y.shape[1])], 1).astype("float32")
        g = p.get("gain", 1.0)
        y = y * g
        off = int(p.get("offset", 0) * rate)
        if x is None:
            x = np.zeros((off + len(y), y.shape[1]), "float32")
        if off + len(y) > len(x):
            x = np.vstack([x, np.zeros((off + len(y) - len(x), x.shape[1]), "float32")])
        x[off:off + len(y)] += y
    sr = rate
    x = x - x.mean(axis=0)
    hp = spec.get("hp", 30)
    if hp: x = sosfilt(butter(2, hp, "hp", fs=sr, output="sos"), x, axis=0).astype("float32")
    lp = spec.get("lp")
    if lp: x = sosfilt(butter(4, lp, "lp", fs=sr, output="sos"), x, axis=0).astype("float32")
    if spec.get("pitch"):
        r = spec["pitch"]
        n = int(len(x) / r)
        idx = np.arange(n) * r
        x = np.stack([np.interp(idx, np.arange(len(x)), x[:, c]) for c in range(x.shape[1])], 1).astype("float32")
    if spec.get("loop"):
        x = make_loop(x, sr, spec.get("xfade", 0.25))
    else:
        if spec.get("trim", True): x = trim_silence(x, sr, spec.get("trim_db", -50))
        if spec.get("maxlen"): x = x[: int(spec["maxlen"] * sr)]
        x = fades(x, sr, spec.get("fin", 0.002), spec.get("fout", 0.03))
    m = spec.get("match", True)
    old = os.path.join(AUDIO, (m if isinstance(m, str) else spec["out"]) + ".wav")
    if spec.get("match", True) and not spec.get("lufs") and os.path.exists(old):
        o, osr = sf.read(old, always_2d=True, dtype="float32")
        g = (active_rms(o) / max(active_rms(x), 1e-6)) * 10 ** (spec.get("trim_gain_db", 0) / 20)
        g = min(g, 10 ** (-1.0 / 20) / max(np.abs(x).max(), 1e-6) * 10 ** (spec.get("max_limit_db", 3) / 20))
        x = x * g
        pk = np.abs(x).max(); ceil = 10 ** (-1.0 / 20)
        if pk > ceil: x = soft_limit(x, ceil)
    else:
        x = normalize(x, sr, spec)
    dest = os.path.join(OUT, spec["out"] + ".wav")
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    sf.write(dest, x, sr, subtype="PCM_16")
    return dest, len(x) / sr

ROOTS = [os.path.expanduser(r) for r in os.environ.get("AUDIO_SOURCES", "~/.capy/work/aud/sonniss/files:~/.capy/work/aud/kenney:~/.capy/work/sf-out:~/.capy/work/music-out").split(":")]
INDEX = []
for r in ROOTS:
    for dp, dn, fn in os.walk(r):
        for f in fn:
            if f.lower().endswith((".wav", ".ogg", ".flac", ".aif", ".aiff")): INDEX.append(os.path.join(dp, f))
USED = {}

def resolve(src):
    if os.path.exists(os.path.expanduser(src)): return os.path.expanduser(src)
    hits = [f for f in INDEX if src in f]
    if not hits: raise FileNotFoundError(src)
    return sorted(hits, key=len)[0]

_load = load
def load(path, start, dur, mono):
    path = resolve(path)
    USED.setdefault(CUR, []).append(path)
    return _load(path, start, dur, mono)

CUR = None
specs = json.load(open(sys.argv[1]))
only = set(sys.argv[3:])
for s in specs:
    if only and s["out"] not in only: continue
    CUR = s["out"]
    USED[CUR] = []
    try:
        d, l = run(s)
        print(f"ok {s['out']} {l:.2f}s")
    except Exception as e:
        print(f"FAIL {s['out']} {e}")
used_path = os.path.join(OUT, "used.json")
prev = json.load(open(used_path)) if os.path.exists(used_path) else {}
prev.update(USED)
json.dump(prev, open(used_path, "w"), indent=1)
