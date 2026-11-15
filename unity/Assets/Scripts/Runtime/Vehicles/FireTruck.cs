using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Operator;
using Dogpatch.Smash;
using UnityEngine;

namespace Dogpatch.Vehicles
{
    // The fire truck's kit: the light bar flashes and the siren wails whenever it is driven or moving,
    // and a tap while driving (its IVehicleAbility) opens the roof water cannon for a few seconds. It
    // auto-aims at the nearest fire ahead, else the nearest car ahead, else straight down the road. A
    // fire it soaks for a moment goes out in a burst of steam (set_fire flames); a car it hits is
    // blasted sideways, people are bowled over. Only the driver's phone sprays and shoves.
    public sealed class FireTruck : MonoBehaviour, IVehicleAbility
    {
        const float Burst = 3f;
        const float Range = 32f;
        const float Cone = 40f;
        const float SoakToPutOut = 0.7f;
        static readonly Color Water = new Color(0.55f, 0.82f, 1f);
        static readonly Color Steam = new Color(0.93f, 0.95f, 0.97f);
        static AudioClip sirenClip;

        CarController car;
        Transform cannon, lampA, lampB;
        AudioSource siren;
        FireFx fire;
        CarController victim;
        Vector3 aim;
        bool spraying;
        float nextScan, nextHit, nextDrop, sprayUntil;
        readonly Dictionary<FireFx, float> soak = new Dictionary<FireFx, float>();
        readonly Dictionary<CarController, float> shoved = new Dictionary<CarController, float>();
        readonly Collider[] near = new Collider[24];

        void Awake()
        {
            car = GetComponent<CarController>();
            siren = gameObject.AddComponent<AudioSource>();
            siren.clip = SirenClip();
            siren.loop = true;
            siren.playOnAwake = false;
            siren.spatialBlend = 1f;
            siren.minDistance = 10f;
            siren.maxDistance = 140f;
            siren.rolloffMode = AudioRolloffMode.Linear;
            siren.volume = 0.32f;
        }

        void OnDestroy()
        {
            if (siren != null) Destroy(siren);
        }

        public void Use()
        {
            if (!car.Driven || car.Autopilot) return;
            sprayUntil = Time.time + Burst;
            nextScan = 0f;
            Debug.Log("[FireTruck] water cannon on");
        }

        void Update()
        {
            if (car == null) return;
            if (cannon == null)
            {
                cannon = VehicleRoster.RigPart(car, "cannon");
                lampA = VehicleRoster.RigPart(car, "siren-a");
                lampB = VehicleRoster.RigPart(car, "siren-b");
            }
            bool on = car.Driven || Mathf.Abs(car.Speed) > 1f;
            bool flash = Mathf.Repeat(Time.time * 2.6f, 1f) < 0.5f;
            if (lampA != null) lampA.gameObject.SetActive(on && flash);
            if (lampB != null) lampB.gameObject.SetActive(on && !flash);
            if (on && !siren.isPlaying) siren.Play();
            else if (!on && siren.isPlaying) siren.Stop();

            spraying = false;
            if (cannon != null && Time.time >= sprayUntil) cannon.localRotation = Quaternion.Slerp(cannon.localRotation, Quaternion.identity, 1f - Mathf.Exp(-4f * Time.deltaTime));
            if (!car.Driven || car.Autopilot || cannon == null || Time.time >= sprayUntil) return;
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 0.2f;
                Scan();
            }
            if (fire != null) aim = fire.transform.position + Vector3.up * 0.6f;
            else if (victim != null) aim = victim.transform.position + Vector3.up * 1f;
            else aim = transform.position + transform.forward * 20f + Vector3.up * 0.5f;
            spraying = true;

            var local = transform.InverseTransformPoint(aim);
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -70f, 70f);
            cannon.localRotation = Quaternion.Slerp(cannon.localRotation, Quaternion.Euler(0f, yaw, 0f), 1f - Mathf.Exp(-10f * Time.deltaTime));
            Spray();
            if (Time.time >= nextHit)
            {
                nextHit = Time.time + 0.1f;
                Hit(0.1f);
            }
        }

        // Fires first (the nearest in a wide cone), else the nearest car in the cone.
        void Scan()
        {
            var p = transform.position;
            var f = transform.forward;
            fire = null;
            float best = Range * Range;
            foreach (var fx in FindObjectsByType<FireFx>(FindObjectsSortMode.None))
            {
                var d = fx.transform.position - p;
                d.y = 0f;
                if (d.sqrMagnitude < best && Vector3.Angle(f, d) < Cone + 15f)
                {
                    best = d.sqrMagnitude;
                    fire = fx;
                }
            }
            victim = null;
            if (fire != null) return;
            best = Range * Range;
            var all = VehicleDamage.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i].Car;
                if (c == null || c == car) continue;
                var d = c.transform.position - p;
                d.y = 0f;
                if (d.sqrMagnitude < best && d.sqrMagnitude > 4f && Vector3.Angle(f, d) < Cone)
                {
                    best = d.sqrMagnitude;
                    victim = c;
                }
            }
        }

        // The stream: pooled particles along a short arc from the nozzle to the target, with a spray
        // where it lands.
        void Spray()
        {
            var fx = SmashWorld.Instance?.Fx;
            if (fx == null || Time.time < nextDrop) return;
            nextDrop = Time.time + 0.035f;
            var from = cannon.position + cannon.forward * 0.9f + Vector3.up * 0.4f;
            float span = Vector3.Distance(from, aim);
            float t0 = Mathf.Repeat(Time.time * 7f, 1f) / 8f;
            for (int k = 0; k < 8; k++)
            {
                float t = t0 + k / 8f;
                var at = Vector3.Lerp(from, aim, t) + Vector3.up * (span * 0.12f * 4f * t * (1f - t));
                fx.Smoke(at, Water, Mathf.Lerp(0.45f, 0.9f, t), 1, 0.1f, Random.insideUnitSphere * 0.6f, 0.3f);
            }
            fx.Smoke(aim, Water, 1.2f, 3, 1.4f, Random.insideUnitSphere * 2.5f, 0.5f);
        }

        void Hit(float dt)
        {
            if (fire != null)
            {
                soak.TryGetValue(fire, out float s);
                s += dt;
                soak[fire] = s;
                if (s < SoakToPutOut) return;
                var at = fire.transform.position;
                soak.Remove(fire);
                Destroy(fire.gameObject);
                fire = null;
                var fx = SmashWorld.Instance?.Fx;
                fx?.Smoke(at + Vector3.up, Steam, 2.5f, 12, 2.5f);
                fx?.Poof(at, 2.5f, Water, 10);
                Debug.Log($"[FireTruck] put out a fire at {at}");
                return;
            }
            int n = Physics.OverlapSphereNonAlloc(aim, 2.5f, near, ~0, QueryTriggerInteraction.Ignore);
            var side = transform.right * Mathf.Sign(Vector3.Dot(aim - transform.position, transform.right) + 0.001f);
            for (int i = 0; i < n; i++)
            {
                var shove = near[i].GetComponentInParent<IShoveable>();
                if (shove != null && shove.CanBeShoved) shove.Shove((side * 0.8f + transform.forward * 0.4f) * 9f, gameObject);
                var c = near[i].GetComponentInParent<CarController>();
                if (c == null || c == car || c.Driven) continue;
                if (shoved.TryGetValue(c, out float last) && Time.time - last < 0.6f) continue;
                shoved[c] = Time.time;
                var body = c.GetComponent<Rigidbody>();
                c.Stun(1.6f);
                body.linearVelocity += (side + transform.forward * 0.35f).normalized * 9f + Vector3.up * 1.8f;
                body.angularVelocity += Vector3.up * Random.Range(-1.5f, 1.5f);
                Debug.Log($"[FireTruck] blasted {c.name} sideways");
            }
        }

        // A two-tone wail, built once: a rising and falling sweep between 650 and 1450 Hz with a little
        // odd harmonic so it buzzes like a horn speaker, not a pure tone.
        static AudioClip SirenClip()
        {
            if (sirenClip != null) return sirenClip;
            const int Rate = 22050;
            const float Period = 2.8f;
            var data = new float[(int)(Rate * Period)];
            double phase = 0;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)Rate;
                float sweep = 0.5f - 0.5f * Mathf.Cos(t / Period * Mathf.PI * 2f);
                float freq = Mathf.Lerp(650f, 1450f, sweep);
                phase += 2.0 * System.Math.PI * freq / Rate;
                float s = (float)(System.Math.Sin(phase) + 0.3 * System.Math.Sin(phase * 3.0));
                data[i] = s * 0.6f;
            }
            sirenClip = AudioClip.Create("fire-truck-siren", data.Length, 1, Rate, false);
            sirenClip.SetData(data, 0);
            return sirenClip;
        }
    }
}
