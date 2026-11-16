using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Audio
{
    // Engine, road, wind and skid loops for one vehicle, plus its doors, horn and collisions.
    // Attached by SoundSystem to every CarController; reads only the car's public state.
    public sealed class VehicleSounds : MonoBehaviour
    {
        CarController car;
        EngineProfile profile;
        SoundLoop low, high, road, wind, skid;
        float rpm;
        int gear;
        float lastSpeed;
        float accel;
        float lastYaw;
        float yawRate;
        bool wasDriven;
        float engineOffAt;
        float nextImpact;
        float nextScrape;
        float nextSqueak;

        public EngineProfile Profile => profile;

        public static void Honk(Component vehicle)
        {
            var sounds = vehicle != null ? vehicle.GetComponentInParent<VehicleSounds>() : null;
            if (sounds != null) sounds.Horn();
        }

        void Start()
        {
            car = GetComponent<CarController>();
            profile = EngineProfile.For(car.ModelId + " " + car.VehicleId + " " + name);
            rpm = profile.IdleRpm;
            lastYaw = transform.eulerAngles.y;
            wasDriven = car.Driven;
        }

        public void Horn()
        {
            if (profile?.Horn == null) return;
            SoundSystem.Play(profile.Horn, transform.position + Vector3.up, Bus.Vehicles, 0.9f, Random.Range(0.97f, 1.03f), 200, 120f);
        }

        void Update()
        {
            if (car == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float speed = Mathf.Abs(car.Speed);
            accel = Mathf.Lerp(accel, (speed - lastSpeed) / dt, 1f - Mathf.Exp(-8f * dt));
            lastSpeed = speed;
            float yaw = transform.eulerAngles.y;
            yawRate = Mathf.Lerp(yawRate, Mathf.DeltaAngle(lastYaw, yaw) / dt, 1f - Mathf.Exp(-10f * dt));
            lastYaw = yaw;

            if (car.Driven != wasDriven)
            {
                wasDriven = car.Driven;
                var door = transform.position + transform.right * -1f + Vector3.up;
                SoundSystem.Play("door_open", door, Bus.Vehicles, 0.8f, Random.Range(0.95f, 1.05f), 180, 30f);
                Invoke(nameof(DoorClose), 0.55f);
                if (car.Driven) rpm = profile.IdleRpm * 1.6f;
            }

            bool running = car.Driven || speed > 0.4f;
            if (running) engineOffAt = Time.time + (car.Driven ? 0f : 2f);
            if (running && low == null && high == null) StartLoops();
            else if (!running && (low != null || high != null) && Time.time > engineOffAt) StopLoops();
            if (high == null && low == null) return;

            int priority = car.Driven ? 250 : 120;
            float load = Mathf.Clamp01(accel / 4f);
            if (profile.Electric) UpdateElectric(speed, load, priority);
            else UpdateCombustion(speed, load, dt, priority);
            float squeak = Mathf.Clamp(1f / Mathf.Sqrt(Mathf.Max(transform.lossyScale.y, 0.05f)), 0.7f, 1.8f);
            if (low != null) low.Pitch *= squeak;
            if (high != null) high.Pitch *= squeak;

            if (road != null)
            {
                road.Volume = Mathf.Clamp01(speed / 20f) * 0.45f;
                road.Pitch = 0.7f + speed / 40f;
            }
            if (wind != null)
            {
                wind.Volume = Mathf.SmoothStep(0f, 0.55f, Mathf.InverseLerp(10f, 30f, speed));
                wind.Pitch = 0.8f + speed / 60f;
            }
            UpdateSkid(speed);

            if (profile.Class == EngineClass.Cart && speed > 0.3f && Time.time > nextSqueak)
            {
                nextSqueak = Time.time + Mathf.Lerp(0.9f, 0.28f, speed / 6f) * Random.Range(0.8f, 1.2f);
                SoundSystem.Play("cart_squeak", transform.position, Bus.Vehicles, 0.5f, Random.Range(0.85f, 1.25f), 90, 30f);
            }
        }

        void DoorClose() => SoundSystem.Play("door_close", transform.position + Vector3.up, Bus.Vehicles, 0.9f, Random.Range(0.95f, 1.05f), 180, 30f);

        void UpdateCombustion(float speed, float load, float dt, int priority)
        {
            float target = car.Driven && speed < 0.3f ? profile.IdleRpm * (1f + 0.5f * load) : profile.TargetRpm(speed, ref gear);
            rpm = Mathf.MoveTowards(rpm, target, (target > rpm ? 5000f : 9000f) * dt);
            float x = Mathf.InverseLerp(profile.IdleRpm, profile.MaxRpm, rpm);
            float onLoad = 0.6f + 0.4f * Mathf.Max(load, car.Driven ? 0.35f : 0.2f);
            low.Volume = profile.Volume * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.7f, x))) * (0.75f + 0.25f * onLoad);
            high.Volume = profile.Volume * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.65f, x)) * onLoad;
            low.Pitch = Mathf.Clamp(rpm / profile.LowRpm, 0.5f, 2.6f);
            high.Pitch = Mathf.Clamp(rpm / profile.HighRpm, 0.4f, 2.2f);
            low.Priority = high.Priority = priority;
        }

        void UpdateElectric(float speed, float load, int priority)
        {
            float ratio = Mathf.Clamp01(speed / profile.TopSpeed);
            high.Pitch = 0.55f + 1.25f * ratio;
            high.Volume = profile.Volume * (profile.Class == EngineClass.Cart ? Mathf.Clamp01(speed / 2.5f) : 0.12f + 0.88f * Mathf.Pow(ratio, 0.7f) * (0.65f + 0.35f * load));
            high.Priority = priority;
            if (low != null)
            {
                low.Pitch = 0.9f + 0.35f * ratio;
                low.Volume = profile.Volume * 0.45f * (0.5f + 0.5f * ratio);
                low.Priority = priority;
            }
        }

        void UpdateSkid(float speed)
        {
            float lateral = speed * Mathf.Abs(yawRate) * Mathf.Deg2Rad;
            float braking = speed > 4f ? -accel : 0f;
            float amount = profile.Class == EngineClass.Cart || profile.Class == EngineClass.Tram ? 0f
                : Mathf.Max(Mathf.InverseLerp(9f, 16f, lateral), Mathf.InverseLerp(9f, 15f, braking));
            if (amount > 0.05f && skid == null) skid = SoundSystem.Loop("tire_skid", Bus.Vehicles, transform, 60f, car.Driven ? 230 : 110, 0f);
            if (skid == null) return;
            skid.Volume = Mathf.MoveTowards(skid.Volume, amount * 0.85f, Time.deltaTime * 6f);
            skid.Pitch = 0.9f + 0.2f * amount;
            if (skid.Volume <= 0.001f && amount <= 0.05f)
            {
                skid.Stop(0.05f);
                skid = null;
            }
        }

        void StartLoops()
        {
            int priority = car.Driven ? 250 : 120;
            high = SoundSystem.Loop(profile.High, Bus.Vehicles, transform, profile.Range, priority, 0f);
            if (profile.Low != null) low = SoundSystem.Loop(profile.Low, Bus.Vehicles, transform, profile.Range, priority, 0f);
            if (low == null && !profile.Electric) low = high;
            if (car.Driven && profile.Road)
            {
                road = SoundSystem.Loop("tire_roll", Bus.Vehicles, transform, 40f, 200, 0f);
                wind = SoundSystem.Loop("wind_speed", Bus.Vehicles, null, 0f, 200, 0f);
            }
        }

        void StopLoops()
        {
            foreach (var l in new[] { low, high, road, wind, skid }) l?.Stop(0.4f);
            low = high = road = wind = skid = null;
        }

        void OnDisable() => StopLoops();

        void OnCollisionEnter(Collision c)
        {
            if (c.contactCount == 0) return;
            var contact = c.GetContact(0);
            float force = Mathf.Abs(Vector3.Dot(c.relativeVelocity, contact.normal));
            if (force < 1.2f || Time.time < nextImpact) return;
            var otherCar = c.collider.GetComponentInParent<CarController>();
            if (otherCar != null && otherCar.GetInstanceID() < car.GetInstanceID() && otherCar.GetComponent<VehicleSounds>() != null) return;
            nextImpact = Time.time + 0.15f;
            GameEvents.RaiseImpact(new ImpactInfo
            {
                Position = contact.point,
                Force = force,
                Surface = otherCar != null ? Surface.Vehicle : SurfaceOf(c.collider),
                InvolvesPlayer = IsPlayer(car) || IsPlayer(otherCar),
            });
        }

        void OnCollisionStay(Collision c)
        {
            if (!car.Driven || c.contactCount == 0 || Time.time < nextScrape) return;
            var n = c.GetContact(0).normal;
            if (Mathf.Abs(n.y) > 0.6f) return;
            float slide = (c.relativeVelocity - Vector3.Dot(c.relativeVelocity, n) * n).magnitude;
            if (slide < 3f) return;
            nextScrape = Time.time + 0.35f;
            SoundSystem.Play("scrape", c.GetContact(0).point, Bus.Vehicles, Mathf.Clamp01(slide / 12f), Random.Range(0.9f, 1.1f), 160, 40f);
        }

        static bool IsPlayer(CarController c) => c != null && SoundSystem.Game != null && SoundSystem.Game.CurrentCar == c;

        // Owners can tag colliders with SoundSurface; otherwise the world's object names decide.
        public static Surface SurfaceOf(Collider collider)
        {
            var tag = collider.GetComponentInParent<SoundSurface>();
            if (tag != null) return tag.Surface;
            if (collider is CharacterController || collider.GetComponentInParent<PlayerController>() != null) return Surface.Body;
            for (var t = collider.transform; t != null; t = t.parent)
            {
                string n = t.name;
                if (n.StartsWith("NPC") || n.Contains("Pedestrian")) return Surface.Body;
                if (n.StartsWith("Building") || n == "MapEdge") return Surface.Concrete;
                if (n == "Seabed" || n == "Water") return Surface.Water;
                if (n == "PropColliders") return Surface.Metal;
                if (n.Contains("Tree") || n.Contains("tree")) return Surface.Foliage;
            }
            var m = collider.sharedMaterial != null ? collider.sharedMaterial.name.ToLowerInvariant() : "";
            if (m.Contains("metal")) return Surface.Metal;
            if (m.Contains("wood")) return Surface.Wood;
            if (m.Contains("glass")) return Surface.Glass;
            if (m.Contains("plastic")) return Surface.Plastic;
            return Surface.Concrete;
        }
    }
}
