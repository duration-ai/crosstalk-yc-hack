using System.Collections.Generic;
using Dogpatch.Vehicles;
using Dogpatch.World;
using UnityEngine;

namespace Dogpatch.Ambient
{
    // Six-wheeled sidewalk delivery robots trundling along the pedestrian walk graph near the camera,
    // with their safety flags up. Decoration only: no colliders, one draw call each.
    public sealed class SidewalkRobots : MonoBehaviour
    {
        const int Count = 6;
        const float Speed = 1.5f;
        const float KeepRadius = 140f;
        const float SpawnMin = 25f;
        const float SpawnMax = 90f;

        sealed class Robot
        {
            public Transform Body;
            public int From = -1, To = -1;
            public float T;
            public float Length;
        }

        readonly List<Robot> robots = new List<Robot>();
        readonly List<int> nearby = new List<int>();
        PedestrianGraph graph;
        System.Random random = new System.Random(415);

        public static SidewalkRobots Create(Material material)
        {
            var go = new GameObject("SidewalkRobots");
            var system = go.AddComponent<SidewalkRobots>();
            var mesh = RobotMesh();
            for (int i = 0; i < Count; i++)
            {
                var part = new GameObject("delivery-robot");
                part.transform.SetParent(go.transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                part.SetActive(false);
                system.robots.Add(new Robot { Body = part.transform });
            }
            return system;
        }

        void Update()
        {
            if (graph == null)
            {
                if (PedestrianSystem.Instance == null || PedestrianSystem.Instance.Graph == null) return;
                graph = PedestrianSystem.Instance.Graph;
            }
            var view = Camera.main;
            if (view == null) return;
            var eye = new Vector2(view.transform.position.x, view.transform.position.z);

            foreach (var r in robots)
            {
                if (r.From < 0 || Vector2.Distance(graph.Nodes[r.From], eye) > KeepRadius)
                {
                    if (!Respawn(r, eye)) continue;
                }
                r.T += Speed * Time.deltaTime / Mathf.Max(r.Length, 0.01f);
                if (r.T >= 1f && !Advance(r)) { r.From = -1; r.Body.gameObject.SetActive(false); continue; }
                var a = graph.Nodes[r.From];
                var b = graph.Nodes[r.To];
                var p = Vector2.Lerp(a, b, r.T);
                var dir = b - a;
                float y = graph.Kinds[r.To] == WalkKind.Sidewalk || graph.Kinds[r.From] == WalkKind.Sidewalk ? RoadSurface.SidewalkY : 0.02f;
                var target = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y));
                r.Body.SetPositionAndRotation(new Vector3(p.x, y, p.y), Quaternion.RotateTowards(r.Body.rotation, target, 240f * Time.deltaTime));
            }
        }

        bool Respawn(Robot r, Vector2 eye)
        {
            r.Body.gameObject.SetActive(false);
            r.From = -1;
            nearby.Clear();
            graph.Query(eye, SpawnMax, nearby);
            for (int attempt = 0; attempt < 12 && nearby.Count > 0; attempt++)
            {
                int node = nearby[random.Next(nearby.Count)];
                if (graph.Kinds[node] != WalkKind.Sidewalk || Vector2.Distance(graph.Nodes[node], eye) < SpawnMin) continue;
                r.From = node;
                r.To = -1;
                if (!Advance(r)) { r.From = -1; continue; }
                r.T = 0f;
                r.Body.gameObject.SetActive(true);
                return true;
            }
            return false;
        }

        // Moves on to a next sidewalk edge, never turning straight back unless it is a dead end, and
        // never onto a road crossing.
        bool Advance(Robot r)
        {
            int at = r.To < 0 ? r.From : r.To;
            int back = r.To < 0 ? -1 : r.From;
            var edges = graph.Edges[at];
            int pick = -1, options = 0;
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (e.Kind == WalkKind.Crossing || e.To == back || e.Length < 0.05f) continue;
                if (random.Next(++options) == 0) pick = i;
            }
            if (pick < 0)
            {
                if (back < 0) return false;
                r.From = at;
                r.To = back;
                r.Length = Vector2.Distance(graph.Nodes[at], graph.Nodes[back]);
                r.T = 0f;
                return r.Length > 0.05f;
            }
            r.From = at;
            r.To = edges[pick].To;
            r.Length = edges[pick].Length;
            r.T = 0f;
            return true;
        }

        // A white cooler on six wheels with a dark lid and an orange safety flag; nose along +z.
        static Mesh RobotMesh()
        {
            var lp = new LowPoly();
            var white = LowPoly.Hex("#F4F5F7");
            var dark = LowPoly.Hex("#2A2C30");
            var orange = LowPoly.Hex("#FF6A1F", 0.4f);
            var lamp = LowPoly.Hex("#BFE9FF", 0.8f);
            lp.Box(new Vector3(0f, 0.36f, 0f), new Vector3(0.62f, 0.46f, 0.78f), white);
            lp.Box(new Vector3(0f, 0.62f, -0.02f), new Vector3(0.58f, 0.08f, 0.7f), dark);
            lp.Box(new Vector3(0f, 0.4f, 0.395f), new Vector3(0.4f, 0.08f, 0.02f), lamp);
            for (int i = -1; i <= 1; i++)
                foreach (float side in new[] { 1f, -1f })
                    lp.CylinderX(new Vector3(side * 0.33f, 0.11f, i * 0.27f), 0.11f, 0.08f, 6, dark, dark);
            lp.Bar(new Vector3(-0.22f, 0.66f, -0.3f), new Vector3(-0.22f, 1.55f, -0.3f), 0.02f, dark);
            lp.Box(new Vector3(-0.22f, 1.47f, -0.42f), new Vector3(0.02f, 0.16f, 0.24f), orange);
            return lp.ToMesh("delivery robot");
        }
    }
}
