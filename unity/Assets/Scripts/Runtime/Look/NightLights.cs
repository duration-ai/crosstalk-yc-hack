using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Smash;
using UnityEngine;

namespace Dogpatch.Look
{
    // Night lighting from real sources, pooled near the camera: a soft light over you so you never
    // vanish in the dark, headlight cones on the nearest other cars, and warm pools under the nearest
    // street lamps. Pixel lights only, no shadows, all off by day. EnvironmentRig drives the dark
    // and the driven car's own headlights; windows, tail lamps and lamp heads glow through the
    // shared shader's night term.
    public sealed class NightLights : MonoBehaviour
    {
        const int CarLights = 3;
        const int LampLights = 4;
        public const int MaxLights = 1 + CarLights + LampLights;

        static readonly Color LampWarm = SpeedDial.Hex("#FFD49A");
        static readonly Color Beam = SpeedDial.Hex("#FFF3DA");
        static readonly Color Hero = SpeedDial.Hex("#B9C8FF");

        GameController game;
        Light hero;
        readonly Light[] cars = new Light[CarLights];
        readonly Light[] lamps = new Light[LampLights];
        readonly List<Vector3> lampHeads = new List<Vector3>();
        CarController[] allCars = new CarController[0];
        readonly CarController[] nearCars = new CarController[CarLights];
        readonly Vector3[] nearLamps = new Vector3[LampLights];
        int nearLampCount;
        float nextPick, nextCars, nextLamps;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<NightLights>() != null) return;
            var go = new GameObject("Night lights");
            DontDestroyOnLoad(go);
            go.AddComponent<NightLights>();
        }

        static Light Make(Transform parent, string name, LightType type, Color color, float range, float spot)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.type = type;
            light.color = color;
            light.range = range;
            if (type == LightType.Spot)
            {
                light.spotAngle = spot;
                light.innerSpotAngle = spot * 0.45f;
            }
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.enabled = false;
            return light;
        }

        void Awake()
        {
            hero = Make(transform, "Hero light", LightType.Point, Hero, 11f, 0f);
            for (int i = 0; i < CarLights; i++) cars[i] = Make(transform, "Car headlights " + i, LightType.Spot, Beam, 42f, 64f);
            for (int i = 0; i < LampLights; i++) lamps[i] = Make(transform, "Lamp pool " + i, LightType.Point, LampWarm, 15f, 0f);
        }

        void LateUpdate()
        {
            float night = WorldEnvironment.NightFactor;
            var cam = Camera.main;
            bool on = night > 0.05f && cam != null;
            if (!on)
            {
                hero.enabled = false;
                foreach (var l in cars) l.enabled = false;
                foreach (var l in lamps) l.enabled = false;
                return;
            }
            if (game == null) game = FindAnyObjectByType<GameController>();
            var eye = cam.transform.position;
            float now = Time.unscaledTime;
            if (now >= nextCars)
            {
                nextCars = now + 1f;
                allCars = FindObjectsByType<CarController>(FindObjectsSortMode.None);
            }
            if (lampHeads.Count == 0 && now >= nextLamps)
            {
                nextLamps = now + 2f;
                var index = SmashWorld.Instance != null ? SmashWorld.Instance.Index : null;
                if (index != null)
                    foreach (var s in index.All)
                        if (s.Variant == "p-lamp") lampHeads.Add(new Vector3(s.WorldBounds.center.x, s.WorldBounds.max.y - 0.4f, s.WorldBounds.center.z));
                if (lampHeads.Count > 0) Debug.Log($"[NightLights] {lampHeads.Count} street lamps");
            }
            if (now >= nextPick)
            {
                nextPick = now + 0.25f;
                Pick(eye);
            }

            var focus = game != null ? game.Focus : null;
            hero.enabled = focus != null;
            if (focus != null)
            {
                hero.transform.position = focus.position + Vector3.up * 3.2f;
                hero.intensity = 0.55f * night;
            }

            var mine = game != null ? game.CurrentCar : null;
            for (int i = 0; i < CarLights; i++)
            {
                var car = nearCars[i];
                bool lit = car != null && car != mine && car.isActiveAndEnabled;
                cars[i].enabled = lit;
                if (!lit) continue;
                var t = car.transform;
                cars[i].transform.SetPositionAndRotation(t.TransformPoint(new Vector3(0f, 0.85f, car.HalfExtents.z + 0.2f)), t.rotation * Quaternion.Euler(10f, 0f, 0f));
                cars[i].intensity = 2.6f * night;
            }
            for (int i = 0; i < LampLights; i++)
            {
                bool lit = i < nearLampCount;
                lamps[i].enabled = lit;
                if (!lit) continue;
                lamps[i].transform.position = nearLamps[i];
                lamps[i].intensity = 3.2f * night;
            }
        }

        // The nearest few cars and lamp heads to the camera, by a partial insertion sort.
        void Pick(Vector3 eye)
        {
            var mine = game != null ? game.CurrentCar : null;
            var carDist = new float[CarLights];
            for (int i = 0; i < CarLights; i++)
            {
                nearCars[i] = null;
                carDist[i] = 90f * 90f;
            }
            foreach (var car in allCars)
            {
                if (car == null || car == mine || Mathf.Abs(car.Speed) < 1.5f) continue;
                float d = (car.transform.position - eye).sqrMagnitude;
                for (int i = 0; i < CarLights; i++)
                {
                    if (d >= carDist[i]) continue;
                    for (int j = CarLights - 1; j > i; j--)
                    {
                        carDist[j] = carDist[j - 1];
                        nearCars[j] = nearCars[j - 1];
                    }
                    carDist[i] = d;
                    nearCars[i] = car;
                    break;
                }
            }

            var lampDist = new float[LampLights];
            for (int i = 0; i < LampLights; i++) lampDist[i] = 70f * 70f;
            nearLampCount = 0;
            foreach (var p in lampHeads)
            {
                float d = (p - eye).sqrMagnitude;
                for (int i = 0; i < LampLights; i++)
                {
                    if (d >= lampDist[i]) continue;
                    for (int j = LampLights - 1; j > i; j--)
                    {
                        lampDist[j] = lampDist[j - 1];
                        nearLamps[j] = nearLamps[j - 1];
                    }
                    lampDist[i] = d;
                    nearLamps[i] = p;
                    nearLampCount = Mathf.Min(nearLampCount + 1, LampLights);
                    break;
                }
            }
        }
    }
}
