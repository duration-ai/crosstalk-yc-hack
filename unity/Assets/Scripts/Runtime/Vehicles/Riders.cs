using System.Collections.Generic;
using Dogpatch.Glb;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Vehicles
{
    // Posed riders for two-wheelers and the cart. A Polyfork character is posed once by turning its
    // bones toward target directions (seated on a bike, standing on a scooter deck, pushing a cart),
    // then baked into one static mesh, so a rider costs a single draw with no skinning per frame.
    public static class Riders
    {
        public enum Pose { Seated, Standing, Pushing }

        static readonly Dictionary<string, (Mesh mesh, Vector3 hips)> baked = new Dictionary<string, (Mesh, Vector3)>();

        // Directions in the character's own frame (x right, y up, z forward); x is mirrored per side.
        static readonly Dictionary<string, Vector3> Seated = new Dictionary<string, Vector3>
        {
            ["Spine"] = new Vector3(0f, 0.9f, 0.42f), ["Spine1"] = new Vector3(0f, 0.88f, 0.46f), ["Spine2"] = new Vector3(0f, 0.85f, 0.5f),
            ["Neck"] = new Vector3(0f, 0.95f, 0.3f),
            ["UpLeg"] = new Vector3(0.12f, -0.2f, 0.97f), ["Leg"] = new Vector3(0f, -0.94f, 0.32f), ["Foot"] = new Vector3(0f, -0.3f, 0.95f),
            ["Arm"] = new Vector3(0.28f, -0.32f, 0.9f), ["ForeArm"] = new Vector3(0.05f, -0.15f, 0.98f),
        };

        static readonly Dictionary<string, Vector3> Pushing = new Dictionary<string, Vector3>
        {
            ["Spine"] = new Vector3(0f, 0.97f, 0.22f), ["Spine1"] = new Vector3(0f, 0.96f, 0.26f), ["Spine2"] = new Vector3(0f, 0.95f, 0.3f),
            ["Neck"] = new Vector3(0f, 0.97f, 0.2f),
            ["UpLeg"] = new Vector3(0.08f, -0.98f, -0.12f), ["Leg"] = new Vector3(0f, -0.99f, -0.1f), ["Foot"] = new Vector3(0f, -0.3f, 0.95f),
            ["Arm"] = new Vector3(0.22f, -0.45f, 0.86f), ["ForeArm"] = new Vector3(0f, -0.05f, 1f),
        };

        static readonly Dictionary<string, Vector3> Standing = new Dictionary<string, Vector3>
        {
            ["Spine"] = new Vector3(0f, 0.99f, 0.08f), ["Spine1"] = new Vector3(0f, 0.99f, 0.1f), ["Spine2"] = new Vector3(0f, 0.99f, 0.12f),
            ["UpLeg"] = new Vector3(0.06f, -0.99f, 0f), ["Leg"] = new Vector3(0f, -1f, 0f), ["Foot"] = new Vector3(0f, -0.3f, 0.95f),
            ["Arm"] = new Vector3(0.25f, -0.95f, 0.05f), ["ForeArm"] = new Vector3(0.05f, -0.95f, 0.25f),
        };

        static readonly string[] Order = { "Spine", "Spine1", "Spine2", "Neck", "UpLeg", "Leg", "Foot", "Arm", "ForeArm" };

        static readonly Dictionary<string, string> Child = new Dictionary<string, string>
        {
            ["Spine"] = "Spine1", ["Spine1"] = "Spine2", ["Spine2"] = "Neck", ["Neck"] = "Head",
            ["UpLeg"] = "Leg", ["Leg"] = "Foot", ["Foot"] = "ToeBase", ["Arm"] = "ForeArm", ["ForeArm"] = "Hand",
        };

        // Mesh in the rider's frame plus where its hips ended up, for seating it on a saddle.
        public static (Mesh mesh, Vector3 hips) Bake(GlbAsset asset, string key, Pose pose, IDictionary<string, string> recolor, Material material)
        {
            string id = key + "/" + pose;
            if (baked.TryGetValue(id, out var result)) return result;

            var root = asset.Instantiate(material);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var bones = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>()) bones[t.name] = t;
            var targets = pose == Pose.Seated ? Seated : pose == Pose.Pushing ? Pushing : Standing;

            foreach (string side in new[] { "", "Left", "Right" })
                foreach (string part in Order)
                {
                    bool limb = part == "UpLeg" || part == "Leg" || part == "Foot" || part == "Arm" || part == "ForeArm";
                    if (limb == (side == "")) continue;
                    if (!targets.TryGetValue(part, out var dir)) continue;
                    if (!bones.TryGetValue(side + part, out var bone) || !bones.TryGetValue(side + Child[part], out var child)) continue;
                    var current = child.position - bone.position;
                    if (current.sqrMagnitude < 1e-6f) continue;
                    if (limb && bone.position.x < 0f) dir.x = -dir.x;
                    bone.rotation = Quaternion.FromToRotation(current.normalized, dir.normalized) * bone.rotation;
                }

            var parts = new List<CombineInstance>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var m = new Mesh();
                smr.BakeMesh(m, true);
                parts.Add(new CombineInstance { mesh = m, transform = root.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix });
            }
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                parts.Add(new CombineInstance { mesh = mf.sharedMesh, transform = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix });
            var mesh = new Mesh { name = "rider " + id, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateBounds();
            mesh = recolor != null && recolor.Count > 0 ? VehicleModels.Repaint(mesh, key, recolor, null) : mesh;
            var hips = bones.TryGetValue("Hips", out var h) ? h.position : Vector3.up;
            Object.DestroyImmediate(root);
            result = (mesh, hips);
            baked[id] = result;
            return result;
        }

        // Puts a baked rider on a vehicle visual: hips on the saddle for bikes, feet on the deck for
        // scooters, behind the handle for the cart.
        public static GameObject Mount(Transform vehicle, string type, GlbAsset asset, string key, IDictionary<string, string> recolor, Material material)
        {
            var pose = type == VehicleTypes.Scooter ? Pose.Standing : type == VehicleTypes.ShoppingCart ? Pose.Pushing : Pose.Seated;
            var (mesh, hips) = Bake(asset, key, pose, recolor, material);
            var go = new GameObject("rider");
            go.transform.SetParent(vehicle, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            Vector3 anchor = pose == Pose.Seated ? new Vector3(0f, 0.93f, -0.24f) : pose == Pose.Standing ? new Vector3(0f, 0.14f, 0.02f) : new Vector3(0f, 0f, -0.86f);
            go.transform.localPosition = pose == Pose.Seated ? anchor - hips : new Vector3(anchor.x, anchor.y - mesh.bounds.min.y, anchor.z);
            return go;
        }
    }
}
