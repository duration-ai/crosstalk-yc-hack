#!/usr/bin/env python3
"""Bakes unity/Assets/Resources/Smash/physics.json: one physics entry per Polyfork model in the catalog.

Mass, material and body come from Polyfork's own physics block (polyfork.dev/api/assets/{id}); the
convex hull comes from polyfork.dev/cdn/{id}-colliders.json, mirrored on X like our GLB reader. The
gameplay columns (class, label, points, break speed) are ours, keyed by catalog role.

    python3 tools/smash/bake-physics.py
"""
import json
import os
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CATALOG = os.path.join(ROOT, "unity/Assets/Resources/Polyfork/catalog.json")
OUT = os.path.join(ROOT, "unity/Assets/Resources/Smash/physics.json")

# class: static | breakable (snaps at the base and topples) | loose (knocked over whole) | dynamic (vehicles, people)
# breakSpeed: impact speed in m/s a 1300 kg car needs to break it. points: base smash score.
# gameplayMassKg caps Polyfork's real mass where it would make the object an unbreakable wall.
ROLES = {
    "tree":         dict(cls="breakable", label="TREE",     points=150, breakSpeed=7.0, gameplayMassKg=900, snapHeight=0.22),
    "streetlight":  dict(cls="breakable", label="LAMP",     points=50,  breakSpeed=4.0, gameplayMassKg=250, snapHeight=0.03),
    "trafficlight": dict(cls="breakable", label="SIGNAL",   points=75,  breakSpeed=4.5, gameplayMassKg=300, snapHeight=0.03),
    "hydrant":      dict(cls="breakable", label="HYDRANT",  points=100, breakSpeed=3.0, gameplayMassKg=120, snapHeight=0.0),
    "meter":        dict(cls="breakable", label="METER",    points=30,  breakSpeed=2.5, gameplayMassKg=30,  snapHeight=0.0),
    "busshelter":   dict(cls="breakable", label="SHELTER",  points=200, breakSpeed=6.0, gameplayMassKg=500, snapHeight=0.0),
    "bench":        dict(cls="loose",     label="BENCH",    points=40,  breakSpeed=2.0, gameplayMassKg=70,  snapHeight=0.0),
    "bin":          dict(cls="loose",     label="TRASH",    points=25,  breakSpeed=1.5, gameplayMassKg=15,  snapHeight=0.0),
    "newsbox":      dict(cls="loose",     label="NEWS BOX", points=30,  breakSpeed=2.0, gameplayMassKg=80,  snapHeight=0.0),
    "rooftop":      dict(cls="static",    label="",         points=0,   breakSpeed=0.0, gameplayMassKg=0,   snapHeight=0.0),
    "car":          dict(cls="dynamic",   label="SMASH",    points=0,   breakSpeed=0.0, gameplayMassKg=0,   snapHeight=0.0),
    "player":       dict(cls="dynamic",   label="",         points=0,   breakSpeed=0.0, gameplayMassKg=0,   snapHeight=0.0),
    "npc":          dict(cls="dynamic",   label="",         points=0,   breakSpeed=0.0, gameplayMassKg=0,   snapHeight=0.0),
}

# Polyfork's estimate for the police cruiser is 20 t, which would make it a wall; use a sedan's mass.
MASS_FIX = {"police-cruiser-a2d25e": 1800}


def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": "dogpatch-bake/1.0"})
    with urllib.request.urlopen(request, timeout=30) as r:
        return json.load(r)


def main():
    models = json.load(open(CATALOG))["models"]
    entries = []
    for m in models:
        asset = fetch(f"https://polyfork.dev/api/assets/{m['id']}")
        physics = asset.get("physics") or {}
        colliders = fetch(f"https://polyfork.dev/cdn/{m['id']}-colliders.json")
        hull = []
        for x, y, z in colliders.get("hull", []):
            hull += [round(-x, 3), round(y, 3), round(z, 3)]
        role = ROLES[m["role"]]
        entries.append(dict(
            id=m["id"], role=m["role"], physicsClass=role["cls"], label=role["label"], points=role["points"],
            breakSpeed=role["breakSpeed"], snapHeight=role["snapHeight"],
            massKg=float(MASS_FIX.get(m["id"], physics.get("mass_kg") or asset.get("mass_kg") or 0)),
            gameplayMassKg=float(role["gameplayMassKg"]),
            material=physics.get("material", ""), body=physics.get("body", ""), hull=hull,
        ))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w") as f:
        json.dump({"source": "https://polyfork.dev", "entries": entries}, f, separators=(",", ":"))
        f.write("\n")
    print(f"wrote {len(entries)} entries to {OUT}")


if __name__ == "__main__":
    main()
