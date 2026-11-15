using Dogpatch.Gameplay;
using Dogpatch.World;
using UnityEngine;

namespace Dogpatch.WorldChanges
{
    // A sunshine-yellow launch ramp dropped on the route by the Ramp change. A car crossing the lip
    // gets airtime scaled by its speed.
    public sealed class Ramp : MonoBehaviour
    {
        const float Width = 4.2f;
        const float Length = 6f;
        const float Height = 1.2f;

        public static Ramp Create(Transform parent, Vector3 position, float heading, Material material)
        {
            var go = new GameObject("Ramp");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, heading, 0f));

            var yellow = PaintJob.Linear("#FFCB1F");
            var ink = PaintJob.Linear("#1C2347");
            float w = Width * 0.5f;
            var a = new Vector3(-w, 0f, 0f);
            var b = new Vector3(w, 0f, 0f);
            var c = new Vector3(w, Height, Length);
            var d = new Vector3(-w, Height, Length);
            var e = new Vector3(-w, 0f, Length);
            var f = new Vector3(w, 0f, Length);
            var slope = Vector3.Cross(d - a, b - a).normalized;
            var mb = new MeshBuilder();
            mb.Quad(a, d, c, b, yellow, slope);
            for (int i = 0; i < 3; i++)
            {
                float t0 = 0.2f + i * 0.25f;
                float t1 = t0 + 0.09f;
                var l0 = Vector3.Lerp(a, d, t0) + slope * 0.02f;
                var r0 = Vector3.Lerp(b, c, t0) + slope * 0.02f;
                var mid = Vector3.Lerp(Vector3.Lerp(a, d, t1 + 0.06f), Vector3.Lerp(b, c, t1 + 0.06f), 0.5f) + slope * 0.02f;
                var l1 = Vector3.Lerp(a, d, t1) + slope * 0.02f;
                var r1 = Vector3.Lerp(b, c, t1) + slope * 0.02f;
                var mid0 = Vector3.Lerp(Vector3.Lerp(a, d, t0 + 0.06f), Vector3.Lerp(b, c, t0 + 0.06f), 0.5f) + slope * 0.02f;
                mb.Triangle(l0, mid0, l1, ink, slope);
                mb.Triangle(l1, mid0, mid, ink, slope);
                mb.Triangle(mid0, r0, mid, ink, slope);
                mb.Triangle(mid, r0, r1, ink, slope);
            }
            mb.Quad(e, d, c, f, ink, Vector3.forward);
            mb.Triangle(a, e, d, ink, Vector3.left);
            mb.Triangle(b, c, f, ink, Vector3.right);
            var mesh = mb.ToMesh("ramp");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;

            var lip = new GameObject("Lip");
            lip.transform.SetParent(go.transform, false);
            var trigger = lip.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, Height, Length - 0.6f);
            trigger.size = new Vector3(Width, 2.4f, 1.6f);
            return lip.AddComponent<Ramp>();
        }

        void OnTriggerEnter(Collider other)
        {
            var car = other.GetComponentInParent<CarController>();
            if (car == null || !car.Driven) return;
            CarFx.On(car).Launch(Mathf.Clamp(Mathf.Abs(car.Speed) * 0.42f, 4f, 9.5f));
        }
    }
}
