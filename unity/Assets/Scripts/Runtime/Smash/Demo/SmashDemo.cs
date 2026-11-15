using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Dogpatch.Gameplay;
using Dogpatch.Glb;
using Dogpatch.World;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Dogpatch.Smash.Demo
{
    // Scripted smash sequences on a small street set, for recordings, perf numbers and the iOS
    // showcase build. Not part of the game: it only runs when asked for.
    //   Dogpatch.x86_64 -smashDemo lamps|parked|barge|wreck|dive|crash20|durability|all [-capture <dir>] [-perf <file>]
    // Several scenes run in order when comma separated (crash20,crash20 measures a warm second run).
    // Built with the SMASH_DEMO define (BUILD_DEFINES=SMASH_DEMO), the player loops all of them.
    public sealed class SmashDemo : MonoBehaviour
    {
        static readonly Color Asphalt = Hex("A9ADB4");
        static readonly Color Sidewalk = Hex("EEE7DA");
        static readonly Color LaneYellow = Hex("FFCB1F");
        static readonly Color LaneWhite = Hex("F7F8F9");
        static readonly Color Grass = Hex("48C26A");
        static readonly Color Concrete = Hex("C9C4BA");
        static readonly Color[] Houses = { Hex("FFE27A"), Hex("8CE3BF"), Hex("FF9C86"), Hex("BBA6F7"), Hex("9ED7FF"), Hex("FFF3DA"), Hex("E2603F") };

        public string Scene = "all";
        string captureDir;
        string perfFile;
        bool loop;
        int frame;

        ModelLibrary models;
        Material material;
        Camera view;
        Transform root;
        StaticModels statics;
        CarController hero;
        readonly Dictionary<CarController, (Vector3 dir, float strength)> drivers = new Dictionary<CarController, (Vector3, float)>();
        Transform camTarget;
        Vector3? camFixed;
        float camDistance = 10f;
        float camHeight = 4.2f;
        float camSide;

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        public static bool Requested(out string scene)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-smashDemo")
                {
                    scene = args[i + 1];
                    return true;
                }
#if SMASH_DEMO
            scene = "showcase";
            return true;
#else
            scene = null;
            return false;
#endif
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        IEnumerator Start()
        {
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            captureDir = Arg("-capture");
            perfFile = Arg("-perf");
            if (captureDir != null)
            {
                Directory.CreateDirectory(captureDir);
                Time.captureFramerate = 30;
            }
            material = new Material(Resources.Load<Shader>("Shaders/FlatVertexColor")) { name = "FlatVertexColor" };
            models = new ModelLibrary();
            SetupRendering();

            loop = Scene == "showcase";
            string[] all = { "lamps", "parked", "barge", "wreck", "dive", "crash20" };
            do
            {
                foreach (var name in Scene == "all" || loop ? all : Scene.Split(','))
                {
                    Debug.Log("[SmashDemo] scene " + name);
                    yield return Run(name);
                }
            } while (loop);
            Debug.Log($"[SmashDemo] done, {frame} frames captured");
            raw?.Dispose();
            raw = null;
            Application.Quit();
        }

        IEnumerator Run(string name)
        {
            switch (name)
            {
                case "lamps": return Lamps();
                case "barge": return Barge();
                case "wreck": return Wreck();
                case "dive": return Dive();
                case "crash20": return Crash20();
                case "parked": return Parked();
                case "durability": return Durability();
                default: throw new ArgumentException("unknown smash demo " + name);
            }
        }

        void SetupRendering()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            view = go.AddComponent<Camera>();
            var sky = new Color(0.55f, 0.78f, 0.95f);
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = sky;
            view.nearClipPlane = 0.3f;
            view.farClipPlane = 600f;
            view.fieldOfView = 55f;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.05f;
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 0.55f;
            sun.shadowBias = 0.08f;
            sun.shadowNormalBias = 0.6f;
            QualitySettings.shadowDistance = 90f;
            QualitySettings.shadowCascades = 1;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = sky;
            RenderSettings.fogStartDistance = 200f;
            RenderSettings.fogEndDistance = 600f;
            Shader.SetGlobalColor("_SkyAmbient", new Color(0.36f, 0.4f, 0.47f));
            Shader.SetGlobalColor("_GroundAmbient", new Color(0.27f, 0.25f, 0.22f));
        }

        // A straight street along +Z: road 14 m wide, sidewalks, lines, a row of houses each side.
        void BuildStreet()
        {
            if (root != null) Destroy(root.gameObject);
            drivers.Clear();
            SmashScore.ResetFor(hero);
            root = new GameObject("SmashDemoSet").transform;
            var mb = new MeshBuilder();
            mb.Quad(new Vector3(-300f, 0f, -300f), new Vector3(-300f, 0f, 300f), new Vector3(300f, 0f, 300f), new Vector3(300f, 0f, -300f), Grass, Vector3.up);
            mb.Quad(new Vector3(-12f, 0.02f, -300f), new Vector3(-12f, 0.02f, 300f), new Vector3(12f, 0.02f, 300f), new Vector3(12f, 0.02f, -300f), Sidewalk, Vector3.up);
            mb.Quad(new Vector3(-7f, 0.04f, -300f), new Vector3(-7f, 0.04f, 300f), new Vector3(7f, 0.04f, 300f), new Vector3(7f, 0.04f, -300f), Asphalt, Vector3.up);
            foreach (float x in new[] { -0.2f, 0.2f })
                mb.Quad(new Vector3(x - 0.08f, 0.05f, -300f), new Vector3(x - 0.08f, 0.05f, 300f), new Vector3(x + 0.08f, 0.05f, 300f), new Vector3(x + 0.08f, 0.05f, -300f), LaneYellow, Vector3.up);
            for (float z = -300f; z < 300f; z += 9f)
                foreach (float x in new[] { -3.5f, 3.5f })
                    mb.Quad(new Vector3(x - 0.08f, 0.05f, z), new Vector3(x - 0.08f, 0.05f, z + 3.5f), new Vector3(x + 0.08f, 0.05f, z + 3.5f), new Vector3(x + 0.08f, 0.05f, z), LaneWhite, Vector3.up);
            int k = 0;
            for (float z = -120f; z < 200f; z += 11f, k++)
                foreach (float side in new[] { -1f, 1f })
                {
                    float h = 8f + (k * 7 % 5) * 2.2f;
                    var c = new Vector3(side * 19f, h * 0.5f, z);
                    mb.Box(c, new Vector3(13f, h, 10.6f), Quaternion.identity, Houses[(k + (side > 0 ? 3 : 0)) % Houses.Length]);
                    var wall = new GameObject("House").AddComponent<BoxCollider>();
                    wall.transform.SetParent(root, false);
                    wall.center = c;
                    wall.size = new Vector3(13f, h, 10.6f);
                }
            WorldBuilder.Emit(root, "Street", mb.ToMesh("street"), material, true);
            var ground = new GameObject("Ground").AddComponent<BoxCollider>();
            ground.transform.SetParent(root, false);
            ground.center = new Vector3(0f, -0.5f, 0f);
            ground.size = new Vector3(600f, 1f, 600f);
            statics = new StaticModels(models);
        }

        // Places a world variant (variants.json) exactly as StreetFurniture does in the city.
        void Prop(string variant, float x, float z, float yaw = 0f, float capsuleRadius = 0f, float capsuleHeight = 2.5f)
        {
            var layer = variant.StartsWith("t-") ? StaticModels.Layer.Trees : StaticModels.Layer.Props;
            statics.PlaceAtOrigin(layer, variant, new Vector2(x, z), Matrix4x4.TRS(new Vector3(x, 0.03f, z), Quaternion.Euler(0f, yaw, 0f), Vector3.one), null, capsuleRadius, capsuleHeight);
        }

        void Finish(CarController focus)
        {
            statics.Emit(root, material, root.gameObject.AddComponent<DistanceLod>());
            SmashWorld.Install(root.gameObject, statics.Index, models, material, view);
            hero = focus;
        }

        CarController Car(string assetId, string id, float x, float z, float heading)
        {
            var car = CarController.Create(id, models.Get(assetId), material, new Vector3(x, 0.05f, z), heading);
            car.transform.SetParent(root, true);
            return car;
        }

        void Drive(CarController car, Vector3 dir, float strength)
        {
            if (!car.Driven) car.SetDriven(true);
            drivers[car] = (dir.normalized, strength);
        }

        void Chase(Transform target, float distance = 10f, float height = 4.2f, float side = 0f)
        {
            camTarget = target;
            camFixed = null;
            camDistance = distance;
            camHeight = height;
            camSide = side;
            SnapCamera();
        }

        void Watch(Transform target, Vector3 from)
        {
            camTarget = target;
            camFixed = from;
            SnapCamera();
        }

        void SnapCamera()
        {
            var (pos, look) = CameraGoal();
            view.transform.position = pos;
            view.transform.rotation = Quaternion.LookRotation(look - pos);
        }

        (Vector3 pos, Vector3 look) CameraGoal()
        {
            var t = camTarget.position;
            if (camFixed.HasValue) return (camFixed.Value, t + Vector3.up * 0.8f);
            var fwd = camTarget.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            return (t - fwd * camDistance + Vector3.up * camHeight + right * camSide, t + Vector3.up * 1.2f + fwd * 4f);
        }

        void FixedUpdate()
        {
            foreach (var pair in drivers)
                if (pair.Key != null && pair.Key.enabled) pair.Key.SetInput(pair.Value.dir, pair.Value.strength);
        }

        void LateUpdate()
        {
            if (camTarget != null)
            {
                var (pos, look) = CameraGoal();
                float k = 1f - Mathf.Exp(-Time.deltaTime * (camFixed.HasValue ? 6f : 4f));
                view.transform.position = Vector3.Lerp(view.transform.position, pos, k);
                view.transform.rotation = Quaternion.Slerp(view.transform.rotation, Quaternion.LookRotation(look - view.transform.position), k * 1.5f);
            }
        }

        // Appends the finished frame as raw RGB24 to <capture>/frames.rgb (bottom-up rows), turned
        // into video afterwards with ffmpeg -f rawvideo -pix_fmt rgb24 -vf vflip.
        void Grab()
        {
            if (captureDir == null) return;
            if (grab == null || grab.width != Screen.width || grab.height != Screen.height)
            {
                grab = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                File.WriteAllText(Path.Combine(captureDir, "size.txt"), Screen.width + "x" + Screen.height);
            }
            grab.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
            grab.Apply(false);
            raw ??= new FileStream(Path.Combine(captureDir, "frames.rgb"), FileMode.Create);
            var data = grab.GetRawTextureData<byte>();
            raw.Write(data.AsReadOnlySpan());
            frame++;
        }

        Texture2D grab;
        FileStream raw;

        void OnDestroy() => raw?.Dispose();

        IEnumerator Capture(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                yield return new WaitForEndOfFrame();
                Grab();
            }
        }

        // A row of five lamps and a tree on the curb, driven straight through at full drag.
        IEnumerator Lamps()
        {
            BuildStreet();
            for (int i = 0; i < 5; i++) Prop("p-lamp", 5.6f, i * 9f, -90f, 0.2f, 3f);
            Prop("t-street-round-tree", 5.6f, 52f, 30f, 0.3f, 3f);
            Prop("p-hydrant", 5.2f, 62f);
            Prop("p-bin", 6.0f, 68f);
            Prop("p-stop", 5.4f, 73f, -90f);
            Prop("p-bench", 6.2f, 79f, -90f);
            Prop("p-pole", 5.6f, 86f, 0f, 0.15f, 3f);
            var car = Car("compact-hatchback-386417", "hero", 5.6f, -45f, 0f);
            Finish(car);
            Chase(car.transform, 11f, 4.6f, -2.5f);
            yield return Capture(0.5f);
            Drive(car, Vector3.forward, 1f);
            yield return Capture(7.5f);
            Drive(car, Vector3.forward, 0f);
            yield return Capture(2f);
        }

        // A hatchback swerves into a row of cars parked along the curb and shoves them aside.
        IEnumerator Parked()
        {
            BuildStreet();
            string[] parked = { "town-car-56eeb6", "yellow-cab-8df4a0", "family-minivan-924de0", "compact-hatchback-386417", "police-cruiser-a2d25e" };
            for (int i = 0; i < parked.Length; i++) Car(parked[i], "parked-" + i, 5.4f, 10f + i * 7f, i % 2 == 0 ? 0f : 180f);
            var car = Car("family-crossover-suv-42027f", "hero", 2f, -40f, 0f);
            Finish(car);
            Chase(car.transform, 12f, 5f, -4f);
            yield return Capture(0.4f);
            Drive(car, Vector3.forward, 1f);
            yield return Capture(2.2f);
            Drive(car, new Vector3(0.12f, 0f, 1f), 1f);
            yield return Capture(4.5f);
            Drive(car, Vector3.forward, 0f);
            yield return Capture(2f);
        }

        // A 3.1 t Cybertruck-weight pickup barges a parked sedan side-on, then a second one.
        IEnumerator Barge()
        {
            BuildStreet();
            var truck = Car("pickup-truck-a34bf2", "cybertruck", -2f, -40f, 0f);
            MakeWedge(truck);
            var sedan = Car("town-car-56eeb6", "sedan-1", -2f, 10f, 90f);
            var cab = Car("yellow-cab-8df4a0", "sedan-2", -1f, 45f, 75f);
            VehicleSpecs.Provider = c => c == truck ? new VehicleSpec { Mass = 3100f, Durability = 160f, BargeStrength = 0.85f } : (VehicleSpec?)null;
            Finish(truck);
            Watch(sedan.transform, new Vector3(-17f, 4.5f, -2f));
            yield return Capture(0.5f);
            Drive(truck, Vector3.forward, 1f);
            yield return Capture(3f);
            Chase(truck.transform, 12f, 5f, -4f);
            yield return Capture(4.5f);
            Drive(truck, Vector3.forward, 0f);
            yield return Capture(2f);
            VehicleSpecs.Provider = null;
        }

        // A delivery truck launches then wrecks a parked hatchback; then our own hatchback rams a
        // wall until it is wrecked, and the driver hops out.
        IEnumerator Wreck()
        {
            BuildStreet();
            var truck = Car("delivery-truck-a9537e", "truck", -2f, -30f, 0f);
            var victim = Car("compact-hatchback-386417", "victim", -2f, 8f, 90f);
            var mine = Car("family-crossover-suv-42027f", "mine", 4f, 70f, 0f);
            var block = new GameObject("Barrier").AddComponent<BoxCollider>();
            block.transform.SetParent(root, false);
            block.center = new Vector3(4f, 1.2f, 104f);
            block.size = new Vector3(8f, 2.4f, 1.2f);
            var mb = new MeshBuilder();
            mb.Box(block.center, block.size, Quaternion.identity, Concrete);
            WorldBuilder.Emit(root, "Barrier", mb.ToMesh("barrier"), material, true);
            Finish(truck);
            Chase(truck.transform, 13f, 5.5f, -5f);
            Drive(truck, Vector3.forward, 1f);
            yield return Capture(3.2f);
            Drive(truck, Vector3.back, 0.6f);
            yield return Capture(1.6f);
            Drive(truck, (victim.transform.position - truck.transform.position).normalized, 1f);
            yield return Capture(2.6f);
            Drive(truck, Vector3.forward, 0f);
            yield return Capture(1.2f);

            hero = mine;
            Chase(mine.transform, 11f, 4.6f, 4f);
            for (int ram = 0; ram < 4 && !mine.GetComponent<VehicleDamage>().IsWrecked; ram++)
            {
                Drive(mine, Vector3.forward, 1f);
                yield return Capture(2.3f);
                if (mine.GetComponent<VehicleDamage>().IsWrecked) break;
                Drive(mine, Vector3.back, 0.7f);
                yield return Capture(1.6f);
            }
            drivers.Remove(mine);
            var hop = Pedestrian("commuter-381e7f", mine.transform.position - mine.transform.right * 1.6f, mine.transform.eulerAngles.y - 90f);
            hop.HopOut(-mine.transform.right);
            Watch(mine.transform, mine.transform.position + new Vector3(-9f, 4f, -7f));
            yield return Capture(3.5f);
        }

        // The durability check: the roster hatchback, driven as the player's car, runs through ten lamps
        // and a tree on the curb, then rams a concrete wall five times. Health is logged after every hit.
        IEnumerator Durability()
        {
            BuildStreet();
            for (int i = 0; i < 10; i++) Prop("p-lamp", 5.6f, i * 8f, -90f, 0.2f, 3f);
            Prop("t-street-round-tree", 5.6f, 88f, 30f, 0.3f, 3f);
            var wall = new GameObject("Wall").AddComponent<BoxCollider>();
            wall.transform.SetParent(root, false);
            wall.center = new Vector3(0f, 2f, 170f);
            wall.size = new Vector3(25f, 4f, 2f);
            var mb = new MeshBuilder();
            mb.Box(wall.center, wall.size, Quaternion.identity, Concrete);
            WorldBuilder.Emit(root, "Wall", mb.ToMesh("wall"), material, true);
            var car = Vehicles.VehicleRoster.Spawn(Vehicles.VehicleTypes.Hatchback, new Vector3(5.6f, 0.05f, -40f), 0f, root);
            Finish(car);
            car.SetDriven(true);
            var damage = car.GetComponent<VehicleDamage>();
            int hits = 0;
            void OnHit(VehicleHit hit)
            {
                if (hit.Car != car) return;
                hits++;
                string what = hit.Other == wall ? "wall" : hit.Other != null ? hit.Other.name : "?";
                float left = Mathf.Max(damage.Durability - damage.Damage - hit.Damage, 0f);
                Debug.Log($"[Durability] hit {hits}: {what} at {hit.ImpactSpeed:F1} m/s, damage {hit.Damage:F1}, health {left / damage.Durability * 100f:F0}% ({left:F0}/{damage.Durability:F0})");
            }
            VehicleImpacts.Hit += OnHit;
            yield return null;
            Debug.Log($"[Durability] hatchback durability {damage.Durability:F0} (base {damage.Spec.Durability:F0})");
            Chase(car.transform, 11f, 4.6f, -2.5f);
            Drive(car, Vector3.forward, 1f);
            while (car.transform.position.z < 100f) yield return Capture(0.1f);
            Debug.Log($"[Durability] after 10 lamps and the tree: smashed {SmashWorld.Instance.BrokenCount}, health {damage.Health01 * 100f:F0}%");
            for (int ram = 1; ram <= 5 && !damage.IsWrecked; ram++)
            {
                Drive(car, new Vector3(-car.transform.position.x * 0.02f, 0f, 1f), 1f);
                float speed = 0f;
                for (float t = 0f; t < 8f && car.transform.position.z < wall.center.z - 3.2f; t += 0.1f)
                {
                    speed = Mathf.Max(speed, car.GetComponent<Rigidbody>().linearVelocity.magnitude);
                    yield return Capture(0.1f);
                }
                yield return Capture(0.6f);
                Debug.Log($"[Durability] wall ram {ram} at up to {speed:F1} m/s: health {damage.Health01 * 100f:F0}%{(damage.IsWrecked ? " WRECKED" : "")}");
                if (damage.IsWrecked) break;
                Drive(car, Vector3.back, 1f);
                while (car.transform.position.z > wall.center.z - 60f) yield return Capture(0.1f);
                Drive(car, Vector3.forward, 0f);
                yield return Capture(0.5f);
            }
            VehicleImpacts.Hit -= OnHit;
            Debug.Log($"[Durability] done: health {damage.Health01 * 100f:F0}%, wrecked={damage.IsWrecked}");
            drivers.Remove(car);
            yield return Capture(1.5f);
        }

        // A car drives at two people standing in the road; they dive out of the way.
        IEnumerator Dive()
        {
            BuildStreet();
            var car = Car("yellow-cab-8df4a0", "cab", 2f, -40f, 0f);
            Pedestrian("delivery-courier-327e30", new Vector3(2.2f, 0.03f, 12f), 180f);
            Pedestrian("construction-worker-c1ad8c", new Vector3(1.4f, 0.03f, 30f), 160f);
            Pedestrian("commuter-381e7f", new Vector3(2.8f, 0.03f, 48f), 200f);
            Finish(car);
            Chase(car.transform, 10f, 3.8f, 2.5f);
            yield return Capture(0.4f);
            Drive(car, Vector3.forward, 0.85f);
            yield return Capture(5.5f);
            Drive(car, Vector3.forward, 0f);
            yield return Capture(2f);
        }

        // Twenty breakables and two parked cars in one cluster, hit at full speed by a heavy
        // truck, measured frame by frame.
        IEnumerator Crash20()
        {
            BuildStreet();
            string[] kinds = { "p-lamp", "p-bin", "p-bench", "p-hydrant", "p-newsbox", "p-meter" };
            int n = 0;
            for (int row = 0; row < 5; row++)
                for (int col = 0; col < 4 && n < 18; col++, n++)
                    Prop(kinds[n % kinds.Length], -1.6f + col * 1.1f + (row % 2) * 0.5f, 20f + row * 3.5f, n * 37f, kinds[n % kinds.Length] == "p-lamp" ? 0.2f : 0f, 3f);
            Prop("t-street-egg-tree", -0.6f, 40f, 0f, 0.3f, 3f);
            Prop("t-ornamental", 0.8f, 43f, 0f, 0.3f, 3f);
            Car("town-car-56eeb6", "parked-1", -1.5f, 52f, 90f);
            Car("family-minivan-924de0", "parked-2", 2.5f, 57f, 80f);
            var truck = Car("delivery-truck-a9537e", "truck", 0f, -35f, 0f);
            Finish(truck);
            Chase(truck.transform, 14f, 6f, -5f);
            var frames = new List<double>();
            var stepper = root.gameObject.AddComponent<PhysicsStepper>();
            Physics.simulationMode = SimulationMode.Script;
            Drive(truck, Vector3.forward, 1f);
            float end = Time.time + 9f;
            var last = Stopwatch.GetTimestamp();
            int peakBodies = 0, peakParticles = 0;
            while (Time.time < end)
            {
                yield return new WaitForEndOfFrame();
                Grab();
                var now = Stopwatch.GetTimestamp();
                frames.Add((now - last) * 1000.0 / Stopwatch.Frequency);
                last = now;
                peakBodies = Mathf.Max(peakBodies, SmashWorld.Instance.ActivePieces);
                peakParticles = Mathf.Max(peakParticles, SmashWorld.Instance.Fx.LiveParticles);
            }
            Physics.simulationMode = SimulationMode.FixedUpdate;
            Drive(truck, Vector3.forward, 0f);
            Report(stepper.Samples, frames, peakBodies, peakParticles);
            Destroy(stepper);
            yield return Capture(1f);
        }

        void Report(List<double> sim, List<double> frames, int peakBodies, int peakParticles)
        {
            double first = sim.Count > 0 ? sim[0] : 0;
            sim = sim.GetRange(Mathf.Min(5, sim.Count), Mathf.Max(0, sim.Count - 5));
            sim.Sort();
            frames.Sort();
            double P(List<double> l, double q) => l.Count == 0 ? 0 : l[Mathf.Clamp((int)(q * (l.Count - 1)), 0, l.Count - 1)];
            double Avg(List<double> l) { double s = 0; foreach (var v in l) s += v; return l.Count == 0 ? 0 : s / l.Count; }
            var line = $"crash20 broken={SmashWorld.Instance.BrokenCount} peakPieces={peakBodies} peakParticles={peakParticles} " +
                       $"physicsMs avg={Avg(sim):F2} p95={P(sim, 0.95):F2} p99={P(sim, 0.99):F2} max={P(sim, 1):F2} firstStep={first:F2} " +
                       $"frameMs avg={Avg(frames):F2} p95={P(frames, 0.95):F2} max={P(frames, 1):F2} frames={frames.Count} " +
                       $"score={SmashScore.TotalFor(hero)} cpu={SystemInfo.processorType} gfx={SystemInfo.graphicsDeviceName}";
            Debug.Log("[SmashDemo] " + line);
            if (perfFile != null) File.AppendAllText(perfFile, line + "\n");
        }

        DemoPedestrian Pedestrian(string assetId, Vector3 position, float heading)
        {
            var go = models.Get(assetId).Instantiate(material, root);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, heading, 0f));
            go.GetComponent<GlbAnimator>()?.Play("Idle");
            return go.AddComponent<DemoPedestrian>();
        }

        // Steps physics by hand after every other FixedUpdate and times each step, contact
        // callbacks (all the smash handling) included.
        [DefaultExecutionOrder(32000)]
        sealed class PhysicsStepper : MonoBehaviour
        {
            public readonly List<double> Samples = new List<double>();
            readonly Stopwatch watch = new Stopwatch();

            void FixedUpdate()
            {
                if (Physics.simulationMode != SimulationMode.Script) return;
                watch.Restart();
                Physics.Simulate(Time.fixedDeltaTime);
                watch.Stop();
                Samples.Add(watch.Elapsed.TotalMilliseconds);
            }
        }

        // Reshapes the pickup into a wedge-bodied electric truck: hides its body, keeps its wheels.
        void MakeWedge(CarController car)
        {
            foreach (var mf in car.GetComponentsInChildren<MeshFilter>())
            {
                bool wheel = false;
                for (var t = mf.transform; t != null; t = t.parent) wheel |= t.name.StartsWith("wheel-") || t.name.StartsWith("steer-");
                if (!wheel) mf.GetComponent<MeshRenderer>().enabled = false;
            }
            var steel = Hex("C9CED6");
            var glass = Hex("2C4FA3");
            var dark = Hex("3A3F4B");
            var ext = car.HalfExtents;
            float w = ext.x * 0.98f, l = ext.z, b = 0.42f, peak = 1.95f;
            var mb = new MeshBuilder();
            Vector3 P(float x, float y, float z) => new Vector3(x, y, z);
            var fl = P(-w, b, l); var fr = P(w, b, l); var rl = P(-w, b, -l); var rr = P(w, b, -l);
            var nl = P(-w, 1.05f, l); var nr = P(w, 1.05f, l);
            var tl = P(-w * 0.9f, peak, l * 0.05f); var tr = P(w * 0.9f, peak, l * 0.05f);
            var bl = P(-w, 1.3f, -l); var br = P(w, 1.3f, -l);
            mb.Quad(fl, nl, nr, fr, steel, Vector3.forward);
            mb.Quad(nl, tl, tr, nr, glass, (Vector3.forward + Vector3.up).normalized);
            mb.Quad(tl, bl, br, tr, steel, (Vector3.up - Vector3.forward * 0.3f).normalized);
            mb.Quad(rr, br, bl, rl, steel, Vector3.back);
            mb.Quad(rl, bl, tl, nl, steel, Vector3.left);
            mb.Triangle(rl, nl, fl, steel, Vector3.left);
            mb.Quad(fr, nr, tr, br, steel, Vector3.right);
            mb.Triangle(fr, br, rr, steel, Vector3.right);
            mb.Quad(fl, fr, rr, rl, dark, Vector3.down);
            mb.Box(P(0f, 1.0f, l + 0.01f), P(w * 1.8f, 0.06f, 0.02f), Quaternion.identity, Hex("F7F8F9"));
            var go = new GameObject("wedge-body");
            go.transform.SetParent(car.transform.GetChild(0), false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("wedge");
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
