using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Anyone on foot who can be shoved with a swipe: pedestrians, racers waiting on the start ring.
    public interface IShoveable
    {
        Vector3 Position { get; }

        bool CanBeShoved { get; }

        // impulse is in m/s on the ground plane; by is whoever shoved.
        void Shove(Vector3 impulse, GameObject by);
    }
}
