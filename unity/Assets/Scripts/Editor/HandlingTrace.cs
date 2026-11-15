using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Dogpatch.Gameplay;
using Dogpatch.Glb;
using UnityEditor;
using UnityEngine;

namespace Dogpatch.EditorTools
{
    // Scripted drive of the default hatchback on a flat plane, stepping CarController.FixedUpdate and
    // the physics scene by hand, so two versions of the controller can be compared trace for trace:
    //   Unity -batchmode -nographics -quit -projectPath unity -executeMethod Dogpatch.EditorTools.HandlingTrace.Run
    // TRACE_OUTPUT names the CSV (default ../Builds/handling-trace.csv).
    public static class HandlingTrace
    {
        public static void Run()
        {
            string output = Path.GetFullPath(Environment.GetEnvironmentVariable("TRACE_OUTPUT") ?? "../Builds/handling-trace.csv");
            Physics.simulationMode = SimulationMode.Script;
            var ground = new GameObject("TraceGround").AddComponent<BoxCollider>();
            ground.size = new Vector3(4000f, 1f, 4000f);
            ground.center = new Vector3(0f, -0.5f, 0f);

            var models = new ModelLibrary();
            var material = new Material(Shader.Find("Unlit/Color"));
            var car = CarController.Create("trace", models.Get("compact-hatchback-386417"), material, new Vector3(0f, 0.05f, 0f), 0f);
            var fixedUpdate = typeof(CarController).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            var awake = typeof(CarController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            awake?.Invoke(car, null);
            car.SetDriven(true);

            float dt = Time.fixedDeltaTime;
            var csv = new StringBuilder("step,input_deg,strength,x,z,yaw,speed\n");
            for (int step = 0; step < 900; step++)
            {
                float t = step * dt;
                float yaw = car.transform.eulerAngles.y;
                float inputDeg;
                float strength;
                if (t < 3f) { inputDeg = 0f; strength = 1f; }
                else if (t < 5f) { inputDeg = 60f; strength = 0.8f; }
                else if (t < 7f) { inputDeg = -40f; strength = 0.6f; }
                else if (t < 9f) { inputDeg = yaw + 180f; strength = 1f; }
                else if (t < 12f) { inputDeg = yaw + 160f; strength = 0.7f; }
                else if (t < 15f) { inputDeg = yaw; strength = 0f; }
                else { inputDeg = 0f; strength = 0f; }
                var dir = strength > 0f ? Quaternion.Euler(0f, inputDeg, 0f) * Vector3.forward : Vector3.zero;
                car.SetInput(dir, strength);
                fixedUpdate.Invoke(car, null);
                Physics.Simulate(dt);
                var p = car.transform.position;
                csv.Append(string.Format(CultureInfo.InvariantCulture, "{0},{1:F2},{2:F2},{3:F4},{4:F4},{5:F4},{6:F4}\n", step, inputDeg, strength, p.x, p.z, car.transform.eulerAngles.y, car.Speed));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, csv.ToString());
            Physics.simulationMode = SimulationMode.FixedUpdate;
            Debug.Log("[Trace] wrote " + output);
        }
    }
}
