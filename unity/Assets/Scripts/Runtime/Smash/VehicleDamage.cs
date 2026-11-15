using System;
using System.Collections.Generic;
using Dogpatch.Ambient;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Smash
{
    // Crash physics and damage for one vehicle: smashing through breakables, mass-true barging
    // (a heavy car shoves and can launch a light one), dents pushed into the flat-shaded mesh at the
    // hit, bumpers and doors that pop off, smoke as health drops, and a cartoon wreck (blackened,
    // smoking, a wheel rolling away) after which the car no longer drives.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(CarController))]
    public sealed class VehicleDamage : MonoBehaviour
    {
        public static readonly List<VehicleDamage> All = new List<VehicleDamage>();
        public static event Action<CarController> Wrecked;

        const float MinImpact = 2f;
        const float AttackMemory = 4f;
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly Color WreckTint = new Color(0.3f, 0.29f, 0.3f);
        static readonly ContactPoint[] contacts = new ContactPoint[8];

        public CarController Car { get; private set; }
        public Rigidbody Body { get; private set; }
        public VehicleSpec Spec { get; private set; }
        public float Damage { get; private set; }
        public float Durability => Spec.Durability * (Car.Driven ? DamageTuning.Current.playerDurabilityScale : DamageTuning.Current.otherDurabilityScale);
        public float Health01 => Mathf.Clamp01(1f - Damage / Mathf.Max(Durability, 1f));
        public bool IsWrecked { get; private set; }
        public int Invulnerable;
        public Vector3 LastVelocity => lastVelocity;
        public float LastHitTime { get; private set; } = -99f;
        public CarController LastAttacker => Time.time - lastAttackTime < AttackMemory ? lastAttacker : null;

        Vector3 lastVelocity;
        Vector3 lastAngular;
        CarController lastAttacker;
        float lastAttackTime = -99f;
        float nextSmoke;
        string specModel;
        bool tumbling;
        bool frontPopped, rearPopped;
        readonly Dictionary<int, float> pairCooldown = new Dictionary<int, float>();
        readonly HashSet<Collider> pedestriansTouching = new HashSet<Collider>();
        MaterialPropertyBlock block;
        List<Panel> panels;
        readonly List<(Transform part, bool wasActive)> hiddenParts = new List<(Transform, bool)>();

        sealed class Panel
        {
            public MeshFilter Filter;
            public Mesh Original;
            public Mesh Mesh;
            public Vector3[] Base;
            public Vector3[] Verts;
            public int[][] Triangles;
        }

        void Awake()
        {
            Car = GetComponent<CarController>();
            Body = GetComponent<Rigidbody>();
        }

        void Start() => ApplySpec();

        // Mass and crash budget from the vehicle's profile, read again after an in-place swap, and a
        // centre of mass low in the body so a car that does tip lands on its wheels.
        void ApplySpec()
        {
            specModel = Car.ModelId;
            Spec = VehicleSpecs.For(Car);
            Body.mass = Spec.Mass;
            Body.maxDepenetrationVelocity = DamageTuning.Current.maxDepenetration;
            var box = GetComponent<BoxCollider>();
            if (box != null) Body.centerOfMass = new Vector3(box.center.x, box.center.y - box.size.y * 0.25f, box.center.z);
        }

        void OnEnable() => All.Add(this);

        void OnDisable() => All.Remove(this);

        void FixedUpdate()
        {
            lastVelocity = Body.isKinematic ? Vector3.zero : Body.linearVelocity;
            lastAngular = Body.isKinematic ? Vector3.zero : Body.angularVelocity;
            if (Car.ModelId != specModel) ApplySpec();
            if (!Body.isKinematic) Settle();

            if (!Car.Driven && !Body.isKinematic && Car.Stunned)
                Body.linearDamping = 0.9f;
            else
                Body.linearDamping = 0f;

            if (tumbling) Recover();
            else if (Car.Driven && !Car.Stunned && !Body.isKinematic)
                Body.angularVelocity *= 0.7f;
        }

        // Nothing flies: upward speed and roll/pitch spin are capped whatever pushed the car (a hit,
        // a launch, several contacts in one step, depenetration), and an undriven or stunned car
        // off the ground falls faster so it lands within a second.
        void Settle()
        {
            var tuning = DamageTuning.Current;
            var v = Body.linearVelocity;
            if (v.y > tuning.maxRiseSpeed) Body.linearVelocity = new Vector3(v.x, tuning.maxRiseSpeed, v.z);
            var w = Body.angularVelocity;
            var tip = new Vector2(w.x, w.z);
            if (tip.sqrMagnitude > tuning.maxTumbleSpin * tuning.maxTumbleSpin)
            {
                tip = tip.normalized * tuning.maxTumbleSpin;
                Body.angularVelocity = new Vector3(tip.x, w.y, tip.y);
            }
            if (Car.Driven && !Car.Stunned) return;
            var p = Body.position;
            if (p.y - World.RoadSurface.GroundY(new Vector2(p.x, p.z)) > 0.3f)
                Body.AddForce(Physics.gravity * (tuning.airborneGravity - 1f), ForceMode.Acceleration);
        }

        // A parked car near a moving one stops being kinematic for a moment.
        public void Loosen(float seconds)
        {
            if (IsWrecked) return;
            Car.Stun(seconds);
        }

        void Recover()
        {
            float tilt = Vector3.Angle(transform.up, Vector3.up);
            bool slow = Body.linearVelocity.sqrMagnitude < 2.5f && Body.angularVelocity.sqrMagnitude < 4f;
            if (tilt < 4f && slow)
            {
                tumbling = false;
                Body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                var e = transform.eulerAngles;
                Body.MoveRotation(Quaternion.Euler(0f, e.y, 0f));
                return;
            }
            if (!IsWrecked) Car.Stun(0.2f);
            if (!slow || Car.Stunned && Time.time - LastHitTime < 1.6f) return;
            var upright = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            Body.angularVelocity = Vector3.zero;
            if (tilt > 60f && Body.position.y < 1.5f) Body.linearVelocity = new Vector3(0f, 2.5f, 0f);
            Body.MoveRotation(Quaternion.RotateTowards(Body.rotation, upright, 220f * Time.fixedDeltaTime));
        }

        void OnCollisionEnter(Collision collision) => Handle(collision);

        void Handle(Collision collision)
        {
            var other = collision.collider;
            int n = collision.GetContacts(contacts);
            if (n == 0) return;
            var point = Vector3.zero;
            var normal = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                point += contacts[i].point;
                normal += contacts[i].normal;
            }
            point /= n;
            normal = normal.normalized;
            if (normal.y > 0.65f) return;

            var otherBody = other.attachedRigidbody;
            var otherCar = otherBody != null ? otherBody.GetComponent<CarController>() : null;
            if (otherCar != null)
            {
                var od = otherCar.GetComponent<VehicleDamage>();
                if (od != null && GetInstanceID() < od.GetInstanceID()) Barge(od, other, point, normal);
                return;
            }

            var tuning = DamageTuning.Current;
            bool breakable = false;
            var world = SmashWorld.Instance;
            if (world != null && world.Index != null && world.Index.TryFromCollider(other, out var instance))
            {
                var flat = new Vector3(lastVelocity.x, 0f, lastVelocity.z);
                float impact = Mathf.Max(Mathf.Abs(Vector3.Dot(flat, normal)), flat.magnitude * 0.75f);
                var scorer = Scorer();
                if (world.TryBreak(instance, Car, scorer, point, lastVelocity, impact, Spec.Mass, out float share))
                {
                    var v = lastVelocity * (1f - Mathf.Clamp(share, 0f, 0.85f) * (1f - Spec.BargeStrength * 0.7f));
                    Body.linearVelocity = new Vector3(v.x, Body.linearVelocity.y, v.z);
                    Body.angularVelocity = lastAngular;
                    float smash = impact * share * 0.6f;
                    TakeHit(other, null, impact, point, normal, smash * tuning.breakableDamageScale, smash, null);
                    return;
                }
                breakable = instance.Class == World.PhysicsClass.Breakable;
            }

            if (otherBody != null && !otherBody.isKinematic) return;
            float speed = Mathf.Abs(Vector3.Dot(lastVelocity, normal));
            if (speed < MinImpact) return;
            float dent = speed * 1.4f * (1f - Spec.BargeStrength);
            float damage = speed < tuning.minDamageImpact ? 0f : speed * tuning.wallDamagePerImpact * (1f - Spec.BargeStrength);
            TakeHit(other, null, speed, point, normal, breakable ? damage * tuning.breakableDamageScale : damage, dent, null);
        }

        // Resolves a car-on-car hit once per pair: a 1D collision along the line between them with
        // real masses, then each car keeps BargeStrength of its own momentum. A much heavier car
        // that hits hard enough launches the lighter one.
        void Barge(VehicleDamage other, Collider otherCollider, Vector3 point, Vector3 normal)
        {
            int key = other.GetInstanceID();
            if (pairCooldown.TryGetValue(key, out float until) && Time.time < until) return;
            pairCooldown[key] = Time.time + 0.35f;

            var va = lastVelocity;
            var vb = other.lastVelocity;
            var d = other.transform.position - transform.position;
            d.y = 0f;
            d = d.sqrMagnitude > 1e-4f ? d.normalized : transform.forward;
            float closing = Vector3.Dot(va - vb, d);
            if (closing < MinImpact) return;

            bool aLeads = Vector3.Dot(va, d) >= Vector3.Dot(vb, -d);
            var instigator = aLeads ? this : other;
            var victim = aLeads ? other : this;
            var dir = aLeads ? d : -d;
            float m1 = instigator.Spec.Mass, m2 = victim.Spec.Mass;
            var v1 = instigator.lastVelocity;
            var v2 = victim.lastVelocity;
            float u1 = Vector3.Dot(v1, dir), u2 = Vector3.Dot(v2, dir);
            const float e = 0.3f;
            float w1 = (m1 * u1 + m2 * u2 - m2 * e * (u1 - u2)) / (m1 + m2);
            float w2 = (m1 * u1 + m2 * u2 + m1 * e * (u1 - u2)) / (m1 + m2);
            w1 = Mathf.Lerp(w1, u1, instigator.Spec.BargeStrength);
            w2 = Mathf.Lerp(w2, u2, victim.Spec.BargeStrength);

            if (!victim.Car.Driven) victim.Car.Stun(1.4f);
            if (!instigator.Car.Driven) instigator.Car.Stun(0.6f);
            if (instigator.Car.Driven) instigator.Car.Stun(0.15f);
            instigator.Body.linearVelocity = v1 + (w1 - u1) * dir;
            victim.Body.linearVelocity = v2 + (w2 - u2) * dir;
            if (instigator.Car.Driven) instigator.Body.angularVelocity = instigator.lastAngular;

            bool launch = m1 / m2 >= 1.3f && closing > 9f && victim.Spec.BargeStrength < 0.9f && !victim.IsWrecked;
            if (launch) victim.Launch(dir, closing, m1 / m2);

            victim.lastAttacker = instigator.Car;
            victim.lastAttackTime = Time.time;
            float ratioToVictim = Mathf.Clamp(m1 / m2, 0.25f, 4f);
            float ratioToInstigator = Mathf.Clamp(m2 / m1, 0.25f, 4f);
            float toInstigator = closing * ratioToInstigator * (1f - instigator.Spec.BargeStrength);
            float toVictim = closing * ratioToVictim * (1f - victim.Spec.BargeStrength);
            float scale = closing < DamageTuning.Current.minDamageImpact ? 0f : DamageTuning.Current.carDamageScale;
            instigator.TakeHit(otherCollider, victim.Car, closing, point, -dir, toInstigator * scale, toInstigator, null);
            victim.TakeHit(otherCollider, instigator.Car, closing, point, dir, toVictim * scale, toVictim, instigator.Car);

            if (closing > 4f)
            {
                int points = Mathf.RoundToInt((60f + closing * 8f) * Mathf.Clamp(Mathf.Sqrt(ratioToVictim), 0.7f, 1.6f) / 10f) * 10 + (launch ? 150 : 0);
                SmashScore.Add(instigator.Scorer(), SmashKind.Barge, launch ? "LAUNCH" : "SMASH", points, point + Vector3.up * 1.5f);
            }
            SmashWorld.Instance?.Fx.Poof(point, Mathf.Clamp(closing * 0.08f, 0.6f, 1.6f), SmashFx.SmokeLight, 6);
        }

        void Launch(Vector3 dir, float closing, float ratio)
        {
            tumbling = true;
            Car.Stun(2.4f);
            Body.constraints = RigidbodyConstraints.None;
            var tuning = DamageTuning.Current;
            float lift = Mathf.Min(1.2f + closing * 0.08f * Mathf.Min(ratio, 3f), tuning.maxRiseSpeed);
            Body.linearVelocity = new Vector3(Body.linearVelocity.x, Mathf.Max(Body.linearVelocity.y, 0f) * 0.5f + lift, Body.linearVelocity.z);
            var axis = Vector3.Cross(Vector3.up, dir).normalized;
            Body.angularVelocity = axis * Mathf.Min(1.2f + closing * 0.08f, tuning.maxTumbleSpin) + Vector3.up * UnityEngine.Random.Range(-1.5f, 1.5f);
        }

        // Who gets the points for what this car does: the car that launched it, if that was recent.
        CarController Scorer() => LastAttacker != null && !Car.Driven ? LastAttacker : Car;

        // damage is what the hit costs in health; dent is how hard it looks (dents, debris, panels
        // popping off), which stays as dramatic as before the damage tuning.
        void TakeHit(Collider other, CarController otherCar, float impact, Vector3 point, Vector3 normal, float damage, float dent, CarController attacker)
        {
            LastHitTime = Time.time;
            VehicleImpacts.Raise(new VehicleHit(Car, other, otherCar, impact, point, normal, damage));
            if (IsWrecked || dent <= 0f || Invulnerable > 0) return;
            Damage += damage;
            Dent(point, dent);
            var fx = SmashWorld.Instance?.Fx;
            if (fx != null && dent > 3f) fx.Debris(point, lastVelocity * 0.3f + normal * 2f, Palette(), Mathf.Clamp(Mathf.RoundToInt(dent * 0.5f), 2, 10), 0.18f);
            if (dent > 9f) PopPanel(point, normal);
            if (Damage >= Durability) Wreck(attacker ?? LastAttacker);
        }

        IReadOnlyList<Color> Palette()
        {
            EnsurePanels();
            return panels.Count > 0 ? SmashFx.PaletteOf(panels[0].Original) : Array.Empty<Color>();
        }

        // Body meshes are shared between cars, so each car copies its own the first time it is hit.
        void EnsurePanels()
        {
            if (panels != null) return;
            panels = new List<Panel>();
            foreach (var mf in GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || IsWheel(mf.transform)) continue;
                var copy = Instantiate(mf.sharedMesh);
                copy.name = mf.sharedMesh.name + " (dented)";
                var p = new Panel { Filter = mf, Original = mf.sharedMesh, Mesh = copy, Base = copy.vertices, Triangles = new int[copy.subMeshCount][] };
                p.Verts = (Vector3[])p.Base.Clone();
                for (int s = 0; s < copy.subMeshCount; s++) p.Triangles[s] = copy.GetTriangles(s);
                mf.sharedMesh = copy;
                panels.Add(p);
            }
        }

        static bool IsWheel(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.name.StartsWith("wheel-") || t.name.StartsWith("steer-")) return true;
            return false;
        }

        void Dent(Vector3 point, float damage)
        {
            EnsurePanels();
            float depth = Mathf.Min(0.05f + damage * 0.012f, 0.3f);
            float radius = Mathf.Min(0.55f + damage * 0.03f, 1.3f);
            var center = transform.TransformPoint(Body.centerOfMass) + Vector3.up * 0.2f;
            foreach (var p in panels)
            {
                var t = p.Filter.transform;
                var local = t.InverseTransformPoint(point);
                var push = t.InverseTransformDirection((center - point).normalized);
                bool changed = false;
                for (int i = 0; i < p.Verts.Length; i++)
                {
                    float dist = Vector3.Distance(p.Verts[i], local);
                    if (dist >= radius) continue;
                    float f = 1f - dist / radius;
                    f = f * f * (3f - 2f * f);
                    var moved = p.Verts[i] + push * depth * f;
                    var offset = moved - p.Base[i];
                    p.Verts[i] = p.Base[i] + Vector3.ClampMagnitude(offset, 0.4f);
                    changed = true;
                }
                if (!changed) continue;
                p.Mesh.SetVertices(p.Verts);
                p.Mesh.RecalculateBounds();
            }
        }

        // Pops the bumper slab at the struck end off as its own piece, or a door when the model
        // has door pivots (Polyfork rig parts) and the hit is on the side.
        void PopPanel(Vector3 point, Vector3 normal)
        {
            var world = SmashWorld.Instance;
            if (world == null) return;
            var local = transform.InverseTransformPoint(point);
            var ext = Car.HalfExtents;
            if (Mathf.Abs(local.z) < ext.z * 0.55f)
            {
                var door = transform.Find(local.x < 0f ? "door-l" : "door-r") ?? FindDeep(transform, local.x < 0f ? "door-l" : "door-r");
                if (door == null || !door.gameObject.activeSelf) return;
                foreach (var mf in door.GetComponentsInChildren<MeshFilter>())
                    world.SpawnPiece(mf.sharedMesh, mf.transform.localToWorldMatrix, 30f,
                        lastVelocity * 0.6f + transform.right * Mathf.Sign(local.x) * 4f + Vector3.up * 3f, UnityEngine.Random.insideUnitSphere * 6f, 8f);
                hiddenParts.Add((door, true));
                door.gameObject.SetActive(false);
                return;
            }
            bool front = local.z > 0f;
            if (front ? frontPopped : rearPopped) return;
            if (front) frontPopped = true; else rearPopped = true;

            EnsurePanels();
            var bounds = new Bounds(transform.InverseTransformPoint(transform.position), Vector3.zero);
            float zEdge = front ? ext.z * 2f : 0f;
            foreach (var p in panels)
            {
                var t = p.Filter.transform;
                var toCar = transform.worldToLocalMatrix * t.localToWorldMatrix;
                var verts = new List<Vector3>();
                var colors = new List<Color32>();
                var tris = new List<int>();
                var srcColors = p.Mesh.colors32;
                float zMin = float.MaxValue, zMax = float.MinValue;
                foreach (var v in p.Base)
                {
                    float z = toCar.MultiplyPoint3x4(v).z;
                    zMin = Mathf.Min(zMin, z);
                    zMax = Mathf.Max(zMax, z);
                }
                float slab = Mathf.Min(0.42f, (zMax - zMin) * 0.11f);
                for (int s = 0; s < p.Triangles.Length; s++)
                {
                    var tri = p.Triangles[s];
                    bool cut = false;
                    for (int i = 0; i + 2 < tri.Length; i += 3)
                    {
                        var c = toCar.MultiplyPoint3x4((p.Base[tri[i]] + p.Base[tri[i + 1]] + p.Base[tri[i + 2]]) / 3f);
                        bool inSlab = front ? c.z > zMax - slab : c.z < zMin + slab;
                        if (!inSlab || c.y > ext.y * 0.9f) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            verts.Add(p.Verts[tri[i + k]]);
                            if (srcColors.Length > 0) colors.Add(srcColors[tri[i + k]]);
                            tris.Add(tris.Count);
                        }
                        tri[i + 1] = tri[i];
                        tri[i + 2] = tri[i];
                        cut = true;
                    }
                    if (cut) p.Mesh.SetTriangles(tri, s, false);
                }
                if (tris.Count == 0) continue;
                var piece = new Mesh { name = "bumper" };
                piece.SetVertices(verts);
                if (colors.Count == verts.Count) piece.SetColors(colors);
                piece.SetTriangles(tris, 0);
                piece.RecalculateBounds();
                var away = (front ? transform.forward : -transform.forward);
                world.SpawnPiece(piece, t.localToWorldMatrix, 25f, lastVelocity * 0.5f + away * 3f + Vector3.up * 3.5f,
                    transform.right * UnityEngine.Random.Range(-6f, 6f), 7f);
            }
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        void Wreck(CarController attacker)
        {
            if (IsWrecked) return;
            IsWrecked = true;
            Damage = Durability;
            Car.enabled = false;
            Body.isKinematic = false;
            block ??= new MaterialPropertyBlock();
            block.SetColor(TintId, WreckTint);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(block);

            var world = SmashWorld.Instance;
            if (world != null)
            {
                world.Fx.Poof(transform.position + Vector3.up, 2.2f, SmashFx.SmokeDark, 10);
                var wheel = FindDeep(transform, "wheel-fl") ?? FindDeep(transform, "wheel-fr");
                if (wheel != null)
                {
                    foreach (var mf in wheel.GetComponentsInChildren<MeshFilter>())
                    {
                        var side = Mathf.Sign(transform.InverseTransformPoint(mf.transform.position).x);
                        world.SpawnPiece(mf.sharedMesh, mf.transform.localToWorldMatrix, 18f,
                            lastVelocity * 0.6f + transform.forward * 5f + transform.right * side * 2f + Vector3.up * 1.5f, transform.right * 16f, 9f, true);
                    }
                    hiddenParts.Add((wheel, true));
                    wheel.gameObject.SetActive(false);
                }
            }

            if (attacker != null && attacker != Car) SmashScore.Add(attacker, SmashKind.Wreck, "WRECK", 500, transform.position + Vector3.up * 2f);
            Debug.Log($"[Smash] wrecked {Car.VehicleId} ({Car.ModelId}) by {(attacker != null ? attacker.VehicleId : "itself")}");
            Wrecked?.Invoke(Car);
        }

        public void Repair()
        {
            if (panels != null)
                foreach (var p in panels)
                {
                    p.Filter.sharedMesh = p.Original;
                    Destroy(p.Mesh);
                }
            panels = null;
            foreach (var (part, wasActive) in hiddenParts) part.gameObject.SetActive(wasActive);
            hiddenParts.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(null);
            frontPopped = rearPopped = false;
            Damage = 0f;
            IsWrecked = false;
            tumbling = false;
            Body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            Car.enabled = true;
            var e = transform.eulerAngles;
            Car.Place(new Vector3(transform.position.x, Mathf.Max(transform.position.y, 0.05f), transform.position.z), e.y);
        }

        void Update()
        {
            var fx = SmashWorld.Instance?.Fx;
            if (fx == null || Time.time < nextSmoke) return;
            float health = Health01;
            if (health > DamageTuning.Current.lightSmokeBelowHealth) return;
            var view = SmashWorld.Instance.View;
            if (view != null && (view.transform.position - transform.position).sqrMagnitude > 140f * 140f) return;
            var ext = Car.HalfExtents;
            var engine = transform.TransformPoint(new Vector3(0f, ext.y * 1.6f, ext.z * 0.55f));
            if (IsWrecked)
            {
                nextSmoke = Time.time + 0.09f;
                fx.Smoke(engine, SmashFx.SmokeDark, 1.1f, 1, 2.2f);
            }
            else if (health < DamageTuning.Current.greySmokeBelowHealth)
            {
                nextSmoke = Time.time + 0.14f;
                fx.Smoke(engine, SmashFx.SmokeGrey, 0.75f, 1, 1.8f, -lastVelocity * 0.3f);
            }
            else
            {
                nextSmoke = Time.time + 0.3f;
                fx.Smoke(engine, SmashFx.SmokeLight, 0.55f, 1, 1.4f, -lastVelocity * 0.3f);
            }
        }

        // Pedestrians are kinematic triggers (CROS-20), so a car passes through and they react.
        void OnTriggerEnter(Collider other)
        {
            var reactive = other.GetComponentInParent<IVehicleReactive>();
            if (reactive == null || !pedestriansTouching.Add(other)) return;
            var otherBody = other.attachedRigidbody;
            var relative = lastVelocity - (otherBody != null && !otherBody.isKinematic ? otherBody.linearVelocity : Vector3.zero);
            if (relative.sqrMagnitude < 1f) return;
            ThreatMonitor.NotifyImpact(this, reactive, other.ClosestPoint(transform.position), relative);
        }

        void OnTriggerExit(Collider other) => pedestriansTouching.Remove(other);
    }
}
