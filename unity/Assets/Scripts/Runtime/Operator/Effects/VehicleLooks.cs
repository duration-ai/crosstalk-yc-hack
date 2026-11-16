using System;
using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.World;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Dogpatch.Operator
{
    // A visible part for each car mechanic effect (boosters for speed, an anchor for slow, oil
    // for slidey tyres, springs for bouncy, hover rings for low gravity, chrome and an anvil for
    // heavy, a wind-up key for a shrunk car, monster-truck dress for a grown one, jump jets for hop), attached to every racer's
    // car the row reaches while it runs and popped off when it ends. Every device runs the same
    // activeEffects rows, so remote racers' cars wear the look too.
    public static class VehicleLooks
    {
        delegate GameObject Build(CarController car, (Vector3 center, Vector3 size) box, float k, Material material);

        static readonly Dictionary<string, Build> Looks = new Dictionary<string, Build>
        {
            ["vehicle_faster"] = (car, box, k, m) => Boosters(box, k, m, false),
            ["nitro"] = (car, box, k, m) => Boosters(box, k, m, true),
            ["vehicle_slower"] = (car, box, k, m) => Anchor(box, k, m),
            ["grip_down"] = (car, box, k, m) => Oil(box, k, m),
            ["bouncy"] = Springs,
            ["gravity_lower"] = HoverRings,
            ["gravity_higher"] = (car, box, k, m) => Anvil(box, k, m),
            ["vehicle_hop"] = (car, box, k, m) => HopJets(box, k, m),
            ["shrink"] = (car, box, k, m) => WindUpKey(box, k, m),
            ["grow"] = (car, box, k, m) => Stacks(box, k, m),
        };

        public static Action Attach(EffectContext context, ActiveEffect row, EffectTarget target)
        {
            if (row.instant || !Looks.TryGetValue(row.effect, out var build)) return null;
            float k = OperatorEffect.Scale(row.strength, 0.85f, 1f, 1.2f);
            var looks = new Dictionary<CarController, GameObject>();
            var undo = new List<Action>();
            var key = new object();
            void Sweep()
            {
                foreach (var car in Cars(context, row, target))
                {
                    if (looks.ContainsKey(car)) continue;
                    var shape = Racers.CarShape(car);
                    if (shape == null) continue;
                    if (row.effect == "bouncy") undo.Add(Bounce(shape, key, k));
                    if (row.effect == "gravity_higher") undo.Add(Heavy(car, shape, key));
                    var look = build(car, Racers.Box(car), k, context.Material);
                    look.transform.SetParent(shape, false);
                    Pop.In(look);
                    looks[car] = look;
                }
            }
            Sweep();
            float next = 0f;
            var ticker = EffectTicker.On(context, () =>
            {
                if (Time.time < next) return;
                next = Time.time + 0.5f;
                Sweep();
            });
            return () =>
            {
                if (ticker != null) Object.Destroy(ticker);
                foreach (var look in looks.Values) Pop.Out(look);
                foreach (var u in undo) u();
                looks.Clear();
                undo.Clear();
            };
        }

        // bouncy: the body rides up on its springs and bobs.
        static Action Bounce(Transform shape, object key, float k)
        {
            var layers = Racers.Layers(shape);
            float phase = Random.value * 6f;
            layers.Lift(key, () => (0.42f + 0.14f * Mathf.Sin(Time.time * 7f + phase)) * k);
            return () =>
            {
                if (layers != null) layers.Remove(key);
            };
        }

        // gravity_higher: the car turns to polished chrome and squats low on its wheels.
        static Action Heavy(CarController car, Transform shape, object key)
        {
            var layers = Racers.Layers(shape);
            layers.Pivot = 0f;
            layers.Scale(key, new Vector3(1.05f, 0.84f, 1.02f));
            var morph = Morph.On(car.gameObject);
            var chrome = new Morph.Mod { Mix = new Color(0.8f, 0.84f, 0.9f), Amount = 0.7f, Rim = 0.4f };
            morph.Add(chrome);
            return () =>
            {
                if (layers != null) layers.Remove(key);
                if (morph != null) morph.Remove(chrome);
            };
        }

        // An object or class row dresses the racers' cars it names; players and world rows dress
        // every racer's car.
        static List<CarController> Cars(EffectContext context, ActiveEffect row, EffectTarget target)
        {
            var cars = new List<CarController>();
            if (row.scope == "object" || row.scope == "class")
            {
                foreach (var tag in target.Entities)
                    if (tag != null && tag.TryGetComponent<CarController>(out var car) && Racers.IsRacer(car)) cars.Add(car);
                return cars;
            }
            foreach (var car in Racers.Cars(context, target)) if (car != null) cars.Add(car);
            if (!string.IsNullOrEmpty(target.Owner)) return cars;
            foreach (var tag in EntityTag.All)
                if (tag != null && (tag.ControlledBy == "me" || tag.ControlledBy == "player") && tag.TryGetComponent<CarController>(out var racer) && !cars.Contains(racer)) cars.Add(racer);
            return cars;
        }

        static Color C(string hex) => PropMeshes.C(hex);

        static GameObject Root(string name) => new GameObject($"{name} (operator look)");

        static GameObject Part(GameObject root, string name, MeshBuilder mb, Material material, Vector3 at, LookMotion.Kind? motion = null, bool shadows = true)
        {
            var go = PropMeshes.Attach(root.transform, name, mb.ToMesh(name), material, at, shadows);
            if (motion.HasValue) go.AddComponent<LookMotion>().Mode = motion.Value;
            return go;
        }

        // Wheel hubs in the car's own space, or four corners of its box when it has no wheels.
        static List<Vector3> Wheels(CarController car, (Vector3 center, Vector3 size) box)
        {
            var hubs = new List<Vector3>();
            foreach (var t in car.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("wheel-") && !t.name.EndsWith("-mesh")) hubs.Add(car.transform.InverseTransformPoint(t.position));
            if (hubs.Count > 0) return hubs;
            float hx = box.size.x * 0.42f, hz = box.size.z * 0.32f, y = box.center.y - box.size.y * 0.5f + 0.3f;
            foreach (var (x, z) in new[] { (-1f, 1f), (1f, 1f), (-1f, -1f), (1f, -1f) }) hubs.Add(new Vector3(box.center.x + x * hx, y, box.center.z + z * hz));
            return hubs;
        }

        // A faceted tube (or cone, when rb is 0) from a to b.
        static void Prism(MeshBuilder mb, Vector3 a, Vector3 b, float ra, float rb, int sides, Color color, bool caps = true)
        {
            var axis = (b - a).normalized;
            var u = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var v = Vector3.Cross(axis, u);
            Vector3 Around(Vector3 c, float r, int i)
            {
                float t = i * Mathf.PI * 2f / sides;
                return c + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * r;
            }
            for (int i = 0; i < sides; i++)
            {
                Vector3 a0 = Around(a, ra, i), a1 = Around(a, ra, i + 1), b0 = Around(b, rb, i), b1 = Around(b, rb, i + 1);
                var outward = ((a0 + a1 + b0 + b1) * 0.25f - (a + b) * 0.5f).normalized;
                if (rb > 0.001f) mb.Quad(a0, a1, b1, b0, color, outward);
                else mb.Triangle(a0, a1, b, color, outward);
                if (!caps) continue;
                if (ra > 0.001f) mb.Triangle(a, a1, a0, color, -axis);
                if (rb > 0.001f) mb.Triangle(b, b0, b1, color, axis);
            }
        }

        static void Strut(MeshBuilder mb, Vector3 a, Vector3 b, float thick, Color color) =>
            mb.Box((a + b) * 0.5f, new Vector3(thick, thick, Vector3.Distance(a, b)), Quaternion.LookRotation(b - a), color);

        // vehicle_faster: twin rockets on the rear deck with flickering orange jets. nitro: fatter
        // blue tanks, a bonnet scoop and long cyan jets.
        static GameObject Boosters((Vector3 center, Vector3 size) box, float k, Material material, bool nitro)
        {
            var root = Root(nitro ? "Nitro" : "Boosters");
            float hx = box.size.x * 0.5f, hz = box.size.z * 0.5f;
            float r = (nitro ? 0.3f : 0.24f) * k, len = (nitro ? 1.5f : 1.7f) * k;
            float y = box.center.y + box.size.y * (nitro ? 0.38f : 0.3f);
            var body = C(nitro ? "#2D8CFF" : "#F4F4F0");
            var band = C(nitro ? "#F4F4F0" : "#E8333A");
            var dark = C("#2B2F3A");
            var mb = new MeshBuilder();
            foreach (float side in new[] { -1f, 1f })
            {
                float x = box.center.x + side * hx * (nitro ? 0.42f : 0.6f);
                float back = box.center.z - hz - 0.2f * k;
                var tail = new Vector3(x, y, back);
                var nose = tail + Vector3.forward * len;
                Prism(mb, tail, nose, r, r, 8, body);
                Prism(mb, tail + Vector3.forward * len * 0.55f, tail + Vector3.forward * len * 0.7f, r * 1.05f, r * 1.05f, 8, band, false);
                Prism(mb, nose, nose + Vector3.forward * r * (nitro ? 1.2f : 2.4f), r, 0f, 8, band);
                Prism(mb, tail, tail + Vector3.back * 0.22f * k, r * 0.75f, r * 0.95f, 8, dark);
                mb.Box(tail + new Vector3(0f, r * 1.2f, 0.25f * k), new Vector3(0.05f, r * 1.4f, 0.45f * k), Quaternion.identity, band);
                mb.Box(tail + new Vector3(side * r * 1.2f, 0f, 0.25f * k), new Vector3(r * 1.4f, 0.05f, 0.45f * k), Quaternion.identity, band);
                mb.Box(new Vector3(x, (y + box.center.y) * 0.5f, back + len * 0.5f), new Vector3(0.12f, y - box.center.y, len * 0.5f), Quaternion.identity, dark);

                var jet = new MeshBuilder();
                float jetLen = (nitro ? 1.5f : 1.1f) * k;
                Prism(jet, Vector3.zero, Vector3.back * jetLen, r * 0.8f, 0f, 7, C(nitro ? "#38E1FF" : "#FF7A1A"));
                Prism(jet, Vector3.back * 0.05f, Vector3.back * jetLen * 0.6f, r * 0.5f, 0f, 7, C(nitro ? "#F4FBFF" : "#FFE14D"));
                Part(root, "Jet", jet, material, tail + Vector3.back * 0.22f * k, LookMotion.Kind.Flicker, false);
            }
            if (nitro)
                mb.Box(new Vector3(box.center.x, box.center.y + box.size.y * 0.5f + 0.08f, box.center.z + hz * 0.45f), new Vector3(hx * 0.6f, 0.18f, hz * 0.35f), Quaternion.identity, dark);
            Part(root, "Rockets", mb, material, Vector3.zero);
            return root;
        }

        // vehicle_slower: a ship's anchor on a chain, dragging along the road behind the car.
        static GameObject Anchor((Vector3 center, Vector3 size) box, float k, Material material)
        {
            var root = Root("Anchor");
            float hz = box.size.z * 0.5f, bottom = box.center.y - box.size.y * 0.5f;
            var iron = C("#2B2F3A");
            var rust = C("#8A4B2A");
            var steel = C("#8C93A6");
            var hitch = new Vector3(box.center.x, bottom + 0.35f, box.center.z - hz);
            var mb = new MeshBuilder();
            var drop = new Vector3(0f, bottom + 0.75f * k - hitch.y, -1.0f * k);
            const int links = 7;
            for (int i = 0; i < links; i++)
            {
                float t = (i + 0.5f) / links;
                var at = Vector3.Lerp(Vector3.zero, drop, t) + Vector3.down * Mathf.Sin(t * Mathf.PI) * 0.2f;
                var size = i % 2 == 0 ? new Vector3(0.05f, 0.16f, 0.24f) : new Vector3(0.16f, 0.05f, 0.24f);
                mb.Box(at, size * k, Quaternion.LookRotation(drop), steel);
            }
            var ring = drop + Vector3.down * 0.05f * k;
            var crown = drop + new Vector3(0f, -0.75f * k, -0.35f * k);
            Prism(mb, ring, crown, 0.07f * k, 0.07f * k, 6, iron);
            mb.Box(Vector3.Lerp(ring, crown, 0.15f), new Vector3(0.5f, 0.08f, 0.08f) * k, Quaternion.LookRotation(crown - ring), rust);
            foreach (float side in new[] { -1f, 1f })
            {
                var tip = crown + new Vector3(side * 0.42f * k, 0.3f * k, 0.05f * k);
                Prism(mb, crown, tip, 0.06f * k, 0.06f * k, 6, iron);
                mb.Triangle(tip + new Vector3(side * 0.12f, 0.12f, 0f) * k, tip + new Vector3(-side * 0.08f, -0.06f, 0f) * k, tip + new Vector3(side * 0.02f, -0.2f, 0f) * k, rust, Vector3.back);
                mb.Triangle(tip + new Vector3(side * 0.12f, 0.12f, 0f) * k, tip + new Vector3(-side * 0.08f, -0.06f, 0f) * k, tip + new Vector3(side * 0.02f, -0.2f, 0f) * k, rust, Vector3.forward);
            }
            Part(root, "Anchor", mb, material, hitch, LookMotion.Kind.Billow).transform.localScale = Vector3.one * 1.5f;
            var hook = new MeshBuilder();
            hook.Box(Vector3.back * 0.12f, new Vector3(0.14f, 0.14f, 0.24f), Quaternion.identity, iron);
            Part(root, "Hitch", hook, material, hitch);
            return root;
        }

        // grip_down / slippery: an oily slick under the car, a rainbow sheen across the roof and a
        // trail of drips left on the road.
        static GameObject Oil((Vector3 center, Vector3 size) box, float k, Material material)
        {
            var root = Root("Oil");
            float hx = box.size.x * 0.5f, hz = box.size.z * 0.5f;
            float bottom = box.center.y - box.size.y * 0.5f, top = box.center.y + box.size.y * 0.5f;
            var ink = C("#231833");
            var sheen = new[] { C("#7B3FE4"), C("#1FC8B4"), C("#FFD23F"), C("#E040A0") };
            var mb = new MeshBuilder();
            for (int i = -1; i <= 1; i++)
                mb.Disc(new Vector2(box.center.x, box.center.z + i * hz * 0.55f), hx * 1.05f * k, bottom + 0.025f, ink, 10);
            for (int i = 0; i < 4; i++)
            {
                mb.Disc(new Vector2(box.center.x + (i % 2 == 0 ? -0.75f : 0.8f) * hx, box.center.z + (i - 1.5f) * hz * 0.45f), 0.3f * k, bottom + 0.035f, sheen[i], 7);
                mb.Box(new Vector3(box.center.x, top + 0.015f, box.center.z + (i - 1.5f) * 0.32f), new Vector3(hx * 1.1f, 0.02f, 0.1f), Quaternion.Euler(0f, 18f, 0f), sheen[i]);
            }
            for (int i = -1; i <= 1; i++)
                mb.Box(new Vector3(box.center.x + i * hx * 0.5f, bottom + 0.12f, box.center.z - hz + 0.05f), new Vector3(0.07f, 0.16f, 0.07f), Quaternion.identity, ink);
            Part(root, "Slick", mb, material, Vector3.zero, null, false);

            var drop = new MeshBuilder();
            drop.Disc(Vector2.zero, 0.32f, 0f, ink, 7);
            drop.Disc(new Vector2(0.06f, 0.04f), 0.14f, 0.004f, sheen[1], 6);
            var trail = root.AddComponent<OilTrail>();
            trail.Drop = drop.ToMesh("oil drop");
            trail.Material = material;
            trail.From = new Vector3(box.center.x, bottom, box.center.z - hz);
            return root;
        }

        // bouncy: the car rides up on a coil spring under each wheel and bounces on them.
        static GameObject Springs(CarController car, (Vector3 center, Vector3 size) box, float k, Material material)
        {
            var root = Root("Springs");
            float bottom = box.center.y - box.size.y * 0.5f;
            var coil = C("#FF4FA3");
            var plate = C("#FFCB1F");
            foreach (var hub in Wheels(car, box))
            {
                var mb = new MeshBuilder();
                float r = 0.2f * k, h = 0.48f * k;
                const int turns = 4, steps = 8;
                for (int i = 0; i < turns * steps; i++)
                {
                    float t0 = i * Mathf.PI * 2f / steps, t1 = (i + 1) * Mathf.PI * 2f / steps;
                    var a = new Vector3(Mathf.Cos(t0) * r, -h * i / (turns * steps), Mathf.Sin(t0) * r);
                    var b = new Vector3(Mathf.Cos(t1) * r, -h * (i + 1) / (turns * steps), Mathf.Sin(t1) * r);
                    Strut(mb, a, b, 0.06f * k, coil);
                }
                mb.Box(new Vector3(0f, 0.02f, 0f), new Vector3(r * 2.4f, 0.06f, r * 2.4f), Quaternion.identity, plate);
                mb.Box(new Vector3(0f, -h, 0f), new Vector3(r * 2.6f, 0.08f, r * 2.6f), Quaternion.identity, plate);
                Part(root, "Spring", mb, material, new Vector3(hub.x, bottom, hub.z));
            }
            return root;
        }

        // gravity_lower: glowing cyan rings spinning under each wheel and a halo round the car.
        static GameObject HoverRings(CarController car, (Vector3 center, Vector3 size) box, float k, Material material)
        {
            var root = Root("Hover rings");
            float bottom = box.center.y - box.size.y * 0.5f;
            var halo = new MeshBuilder();
            float reach = Mathf.Max(box.size.x, box.size.z) * 0.62f;
            Ring(halo, reach, 0.16f * k, 0f, C("#38E1FF"));
            Ring(halo, reach + 0.3f * k, 0.06f * k, -0.05f * k, C("#E8FDFF"));
            Part(root, "Halo", halo, material, new Vector3(box.center.x, bottom + 0.25f, box.center.z), LookMotion.Kind.Spin, false);
            foreach (var hub in Wheels(car, box))
            {
                var mb = new MeshBuilder();
                Ring(mb, 0.55f * k, 0.12f * k, 0f, C("#38E1FF"));
                Ring(mb, 0.36f * k, 0.07f * k, 0.18f * k, C("#E8FDFF"));
                Ring(mb, 0.72f * k, 0.05f * k, -0.06f * k, C("#2D8CFF"));
                Part(root, "Ring", mb, material, new Vector3(hub.x + Mathf.Sign(hub.x - box.center.x) * 0.15f, bottom + 0.06f, hub.z), LookMotion.Kind.Spin, false);
            }
            return root;
        }

        // gravity_higher: a black iron anvil stamped with a yellow band on the roof.
        static GameObject Anvil((Vector3 center, Vector3 size) box, float k, Material material)
        {
            var root = Root("Anvil");
            float top = box.center.y + box.size.y * 0.5f;
            var iron = C("#2B2F3A");
            var edge = C("#4B5368");
            var mb = new MeshBuilder();
            var at = new Vector3(box.center.x, top, box.center.z - box.size.z * 0.08f);
            mb.Box(at + new Vector3(0f, 0.1f * k, 0f), new Vector3(0.7f, 0.2f, 0.5f) * k, Quaternion.identity, iron);
            mb.Box(at + new Vector3(0f, 0.3f * k, 0f), new Vector3(0.36f, 0.22f, 0.28f) * k, Quaternion.identity, edge);
            mb.Box(at + new Vector3(0f, 0.5f * k, -0.05f * k), new Vector3(0.5f, 0.2f, 0.95f) * k, Quaternion.identity, iron);
            Prism(mb, at + new Vector3(0f, 0.53f * k, 0.42f * k), at + new Vector3(0f, 0.5f * k, 0.85f * k), 0.1f * k, 0f, 6, iron);
            mb.Box(at + new Vector3(0f, 0.5f * k, -0.05f * k), new Vector3(0.52f, 0.06f, 0.4f) * k, Quaternion.identity, C("#FFCB1F"));
            Part(root, "Anvil", mb, material, Vector3.zero);
            return root;
        }

        // smaller: a brass wind-up key turning on the roof, like a toy car.
        static GameObject WindUpKey((Vector3 center, Vector3 size) box, float k, Material material)
        {
            var root = Root("Wind-up key");
            float top = box.center.y + box.size.y * 0.5f;
            var brass = C("#E0B33C");
            var shine = C("#FFE08A");
            float s = Mathf.Max(0.6f, box.size.x * 0.32f) * k;
            var mb = new MeshBuilder();
            Prism(mb, Vector3.zero, Vector3.up * 0.5f * s, 0.08f * s, 0.08f * s, 6, brass);
            foreach (float side in new[] { -1f, 1f })
            {
                var lobe = new Vector3(side * 0.42f * s, 0.75f * s, 0f);
                mb.Box(new Vector3(side * 0.16f * s, 0.62f * s, 0f), new Vector3(0.36f * s, 0.12f * s, 0.08f * s), Quaternion.Euler(0f, 0f, side * 25f), brass);
                Prism(mb, lobe + Vector3.back * 0.05f * s, lobe + Vector3.forward * 0.05f * s, 0.26f * s, 0.26f * s, 8, brass);
                Prism(mb, lobe + Vector3.back * 0.06f * s, lobe + Vector3.forward * 0.06f * s, 0.12f * s, 0.12f * s, 8, shine, true);
            }
            Part(root, "Key", mb, material, new Vector3(box.center.x, top - 0.02f, box.center.z - box.size.z * 0.12f), LookMotion.Kind.Wind);
            return root;
        }

        // bigger: monster-truck dress, twin chrome exhaust stacks, a roof light bar and a bull bar.
        static GameObject Stacks((Vector3 center, Vector3 size) box, float k, Material material)
        {
            var root = Root("Monster");
            float hx = box.size.x * 0.5f, hz = box.size.z * 0.5f;
            float top = box.center.y + box.size.y * 0.5f, bottom = box.center.y - box.size.y * 0.5f;
            var chrome = C("#D7DCE2");
            var dark = C("#2B2F3A");
            var amber = C("#FFB21F");
            var mb = new MeshBuilder();
            foreach (float side in new[] { -1f, 1f })
            {
                var foot = new Vector3(box.center.x + side * (hx + 0.08f), box.center.y, box.center.z - hz * 0.1f);
                Prism(mb, foot, foot + Vector3.up * (top - box.center.y + 0.9f * k), 0.1f * k, 0.12f * k, 8, chrome);
                Prism(mb, foot + Vector3.up * (top - box.center.y + 0.9f * k), foot + Vector3.up * (top - box.center.y + 1.0f * k) + Vector3.back * 0.12f, 0.12f * k, 0.13f * k, 8, dark);
            }
            mb.Box(new Vector3(box.center.x, top + 0.08f, box.center.z + hz * 0.15f), new Vector3(box.size.x * 0.75f, 0.1f, 0.18f), Quaternion.identity, dark);
            for (int i = -2; i <= 2; i++)
                mb.Box(new Vector3(box.center.x + i * box.size.x * 0.14f, top + 0.16f, box.center.z + hz * 0.15f), new Vector3(0.18f, 0.1f, 0.12f), Quaternion.identity, amber);
            float front = box.center.z + hz + 0.15f;
            mb.Box(new Vector3(box.center.x, bottom + 0.35f, front), new Vector3(box.size.x * 0.9f, 0.1f, 0.1f), Quaternion.identity, chrome);
            mb.Box(new Vector3(box.center.x, bottom + 0.6f, front), new Vector3(box.size.x * 0.7f, 0.1f, 0.1f), Quaternion.identity, chrome);
            foreach (float side in new[] { -1f, 1f })
                mb.Box(new Vector3(box.center.x + side * box.size.x * 0.35f, bottom + 0.48f, front), new Vector3(0.1f, 0.4f, 0.1f), Quaternion.identity, chrome);
            Part(root, "Stacks", mb, material, Vector3.zero);
            return root;
        }

        // vehicle_hop: four jump jets under the sills, firing down.
        static GameObject HopJets((Vector3 center, Vector3 size) box, float k, Material material)
        {
            var root = Root("Hop jets");
            float hx = box.size.x * 0.5f, hz = box.size.z * 0.5f;
            float bottom = box.center.y - box.size.y * 0.5f;
            var dark = C("#2B2F3A");
            var mb = new MeshBuilder();
            foreach (float side in new[] { -1f, 1f })
            foreach (float end in new[] { -1f, 1f })
            {
                var at = new Vector3(box.center.x + side * (hx + 0.18f), bottom + 0.4f, box.center.z + end * hz * (end < 0f ? 0.8f : 0.55f));
                Prism(mb, at + Vector3.up * 0.35f * k, at, 0.14f * k, 0.2f * k, 8, C("#E8333A"));
                mb.Box(at + new Vector3(-side * 0.12f, 0.2f * k, 0f), new Vector3(0.24f, 0.08f, 0.12f) * k, Quaternion.identity, dark);
                Prism(mb, at, at + Vector3.down * 0.12f * k, 0.15f * k, 0.2f * k, 8, dark);
                var jet = new MeshBuilder();
                Prism(jet, Vector3.zero, Vector3.back * 0.5f * k, 0.17f * k, 0f, 7, C("#FF7A1A"));
                Prism(jet, Vector3.back * 0.02f, Vector3.back * 0.3f * k, 0.1f * k, 0f, 7, C("#FFE14D"));
                var go = Part(root, "Jet", jet, material, at + Vector3.down * 0.12f * k, LookMotion.Kind.Flicker, false);
                go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            }
            Part(root, "Nozzles", mb, material, Vector3.zero);
            return root;
        }

        static void Ring(MeshBuilder mb, float radius, float width, float y, Color color)
        {
            const int n = 12;
            var lift = Vector3.up * width * 0.35f;
            Vector3 D(float t, float r) => new Vector3(Mathf.Cos(t) * r, y, Mathf.Sin(t) * r);
            for (int i = 0; i < n; i++)
            {
                float t0 = i * Mathf.PI * 2f / n, t1 = (i + 1) * Mathf.PI * 2f / n;
                Vector3 o0 = D(t0, radius + width * 0.5f), o1 = D(t1, radius + width * 0.5f), i0 = D(t0, radius - width * 0.5f), i1 = D(t1, radius - width * 0.5f);
                var flat = new Vector3((o0 + o1).x, 0f, (o0 + o1).z).normalized;
                mb.Quad(i0 + lift, i1 + lift, o1 + lift, o0 + lift, color, Vector3.up);
                mb.Quad(i0, o0, o1, i1, color, Vector3.down);
                mb.Quad(o0, o0 + lift, o1 + lift, o1, color, flat);
                mb.Quad(i0, i1, i1 + lift, i0 + lift, color, -flat);
            }
        }
    }

    // Keeps a look's parts alive: jets flicker, chutes billow, rings spin.
    public sealed class LookMotion : MonoBehaviour
    {
        public enum Kind { Flicker, Billow, Spin, Wind }

        public Kind Mode;
        Vector3 rest;
        Quaternion restRotation;
        float seed;

        void Start()
        {
            rest = transform.localScale;
            restRotation = transform.localRotation;
            seed = Random.value * 10f;
        }

        void Update()
        {
            float t = Time.time + seed;
            switch (Mode)
            {
                case Kind.Flicker:
                    float w = 0.85f + 0.3f * Mathf.PerlinNoise(t * 9f, 0f);
                    transform.localScale = new Vector3(rest.x * w, rest.y * w, rest.z * (0.6f + 0.8f * Mathf.PerlinNoise(0f, t * 14f)));
                    break;
                case Kind.Billow:
                    float b = Mathf.Sin(t * 4.5f) * 0.06f;
                    transform.localScale = new Vector3(rest.x * (1f + b), rest.y * (1f + b), rest.z * (1f - b));
                    transform.localRotation = restRotation * Quaternion.Euler(Mathf.Sin(t * 1.3f) * 3f, Mathf.Sin(t * 1.7f) * 5f, Mathf.Sin(t * 2.1f) * 4f);
                    break;
                case Kind.Spin:
                    transform.localRotation = restRotation * Quaternion.Euler(0f, t * 140f, 0f);
                    transform.localScale = rest * (1f + 0.08f * Mathf.Sin(t * 6f));
                    break;
                case Kind.Wind:
                    transform.localRotation = restRotation * Quaternion.Euler(0f, t * 90f, 0f);
                    break;
            }
        }
    }

    // Leaves oil drops on the road behind a slidey car; each shrinks away after a few seconds.
    public sealed class OilTrail : MonoBehaviour
    {
        const float Spacing = 0.9f;
        const float Life = 3.5f;
        const int Max = 28;

        public Mesh Drop;
        public Material Material;
        public Vector3 From;
        readonly Queue<(Transform drop, float born, float size)> drops = new Queue<(Transform, float, float)>();
        Vector3 last;

        void Update()
        {
            var at = transform.TransformPoint(From);
            if ((at - last).sqrMagnitude > Spacing * Spacing)
            {
                last = at;
                if (drops.Count >= Max)
                {
                    var (oldest, _, _) = drops.Dequeue();
                    if (oldest != null) Destroy(oldest.gameObject);
                }
                var go = new GameObject("Oil drop (operator)");
                go.AddComponent<MeshFilter>().sharedMesh = Drop;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = Material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                float size = Random.Range(0.7f, 1.4f);
                go.transform.SetPositionAndRotation(new Vector3(at.x, at.y + 0.03f, at.z), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                go.transform.localScale = Vector3.one * size;
                drops.Enqueue((go.transform, Time.time, size));
            }
            foreach (var (drop, born, size) in drops)
                if (drop != null) drop.localScale = Vector3.one * size * Mathf.Clamp01((born + Life - Time.time) / 0.8f);
            while (drops.Count > 0 && (drops.Peek().drop == null || Time.time - drops.Peek().born > Life))
            {
                var (drop, _, _) = drops.Dequeue();
                if (drop != null) Destroy(drop.gameObject);
            }
        }

        void OnDestroy()
        {
            foreach (var (drop, _, _) in drops) if (drop != null) Destroy(drop.gameObject);
            drops.Clear();
        }
    }
}
