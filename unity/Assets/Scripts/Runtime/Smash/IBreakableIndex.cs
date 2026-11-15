using UnityEngine;

namespace Dogpatch.Smash
{
    public readonly struct BreakableInfo
    {
        public readonly int Id;
        public readonly string AssetId;
        public readonly Matrix4x4 Transform;
        public readonly Bounds WorldBounds;
        public readonly PhysicsEntry Physics;

        public BreakableInfo(int id, string assetId, Matrix4x4 transform, Bounds worldBounds, PhysicsEntry physics)
        {
            Id = id;
            AssetId = assetId;
            Transform = transform;
            WorldBounds = worldBounds;
            Physics = physics;
        }
    }

    // The seam between the smash systems and whoever merges the world's static props into chunks:
    // each placed instance has its own collider, and can be pulled out of its chunk and put back.
    public interface IBreakableIndex
    {
        bool TryFromCollider(Collider collider, out int id);
        BreakableInfo Info(int id);

        // Hides the instance in its merged chunk and disables its collider. Returns its own mesh in
        // model space (origin at its base) to spawn as a dynamic copy at Info(id).Transform.
        Mesh Detach(int id);
        void Restore(int id);
        bool IsDetached(int id);
    }
}
