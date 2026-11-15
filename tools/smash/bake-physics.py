#!/usr/bin/env python3
"""Bakes unity/Assets/Resources/Smash/physics.json.

vehicles: each catalog car's mass from Polyfork's own physics block (polyfork.dev/api/assets/{id}).
roles: how each breakable role in the world (variants.json roles, plus generated "billboard") smashes.
Which instances are breakable, their real mass, body and convex hulls come from CROS-19's
StaticInstanceIndex at runtime; this file only adds the gameplay side: label, points, break speed,
a gameplay mass cap (so a 70 t shade tree doesn't act as a wall) and how it goes over.

    python3 tools/smash/bake-physics.py
"""
import json
import os
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CATALOG = os.path.join(ROOT, "unity/Assets/Resources/Polyfork/catalog.json")
OUT = os.path.join(ROOT, "unity/Assets/Resources/Smash/physics.json")

# style: topple (pivots over its base, away from the car), fly (knocked loose, tumbling with spin),
# snap (a tree: the stump stays, the crown topples from the cut at snapHeight of its height).
# breakSpeed: impact speed in m/s a 1300 kg car needs. maxMassKg caps the instance's Polyfork mass.
ROLES = [
    ("tree:street",           "TREE",      150, 6.5, 900,  "snap",   0.22, False),
    ("tree:ornamental",       "TREE",      150, 6.0, 700,  "snap",   0.25, False),
    ("tree:shade",            "BIG TREE",  300, 9.0, 1600, "snap",   0.18, False),
    ("tree:palm",             "PALM",      200, 8.0, 1200, "snap",   0.20, False),
    ("tree:conifer",          "PINE",      250, 9.0, 1500, "snap",   0.15, False),
    ("tree",                  "TREE",      150, 7.0, 900,  "snap",   0.22, False),
    ("prop:street_lamp",      "LAMP",      50,  3.5, 250,  "topple", 0.0,  False),
    ("prop:traffic_signal",   "SIGNAL",    75,  4.0, 300,  "topple", 0.0,  False),
    ("prop:stop",             "STOP SIGN", 40,  2.0, 40,   "topple", 0.0,  False),
    ("prop:give_way",         "SIGN",      40,  2.0, 40,   "topple", 0.0,  False),
    ("prop:bus_stop",         "BUS STOP",  40,  2.0, 70,   "topple", 0.0,  False),
    ("prop:power_pole",       "POLE",      120, 6.0, 700,  "topple", 0.0,  False),
    ("prop:flagpole",         "FLAGPOLE",  60,  3.0, 100,  "topple", 0.0,  False),
    ("prop:parking_meter",    "METER",     30,  2.0, 30,   "topple", 0.0,  False),
    ("prop:charging_station", "CHARGER",   80,  3.0, 100,  "topple", 0.0,  False),
    ("prop:bus_shelter",      "SHELTER",   200, 5.5, 500,  "topple", 0.0,  False),
    ("prop:fire_hydrant",     "HYDRANT",   100, 2.5, 120,  "fly",    0.0,  True),
    ("prop:drinking_water",   "FOUNTAIN",  60,  4.0, 300,  "fly",    0.0,  True),
    ("prop:bench",            "BENCH",     40,  1.5, 70,   "fly",    0.0,  False),
    ("prop:trash_can",        "TRASH",     25,  1.0, 15,   "fly",    0.0,  False),
    ("prop:vending",          "NEWS BOX",  30,  1.5, 80,   "fly",    0.0,  False),
    ("prop:picnic_table",     "PICNIC",    40,  1.5, 130,  "fly",    0.0,  False),
    ("prop:post_box",         "MAILBOX",   60,  2.5, 120,  "fly",    0.0,  False),
    ("prop:bike_rack",        "BIKE RACK", 30,  2.0, 70,   "fly",    0.0,  False),
    ("prop",                  "SMASH",     25,  2.0, 100,  "fly",    0.0,  False),
    ("billboard",             "BILLBOARD", 300, 6.5, 900,  "topple", 0.0,  False),
    ("landmark",              "SMASH",     100, 6.0, 500,  "topple", 0.0,  False),
]

# Polyfork's estimate for the police cruiser is 20 t, which would make it a wall; use a sedan's mass.
MASS_FIX = {"police-cruiser-a2d25e": 1800}


def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": "dogpatch-bake/1.0"})
    with urllib.request.urlopen(request, timeout=30) as r:
        return json.load(r)


def main():
    vehicles = []
    for m in json.load(open(CATALOG))["models"]:
        if m["role"] != "car":
            continue
        asset = fetch(f"https://polyfork.dev/api/assets/{m['id']}")
        mass = MASS_FIX.get(m["id"], (asset.get("physics") or {}).get("mass_kg") or asset.get("mass_kg") or 1300)
        vehicles.append(dict(id=m["id"], massKg=float(mass)))
    roles = [dict(role=r, label=l, points=p, breakSpeed=b, maxMassKg=float(mm), style=s, snapHeight=h, spout=sp)
             for r, l, p, b, mm, s, h, sp in ROLES]
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w") as f:
        json.dump({"source": "https://polyfork.dev", "vehicles": vehicles, "roles": roles}, f, indent=1)
        f.write("\n")
    print(f"wrote {len(vehicles)} vehicles and {len(roles)} roles to {OUT}")


if __name__ == "__main__":
    main()
