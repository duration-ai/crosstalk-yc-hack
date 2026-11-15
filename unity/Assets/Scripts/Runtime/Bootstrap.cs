using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch
{
    // The only scene is an empty bootstrap; everything else is created from code and data here.
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindAnyObjectByType<GameController>() != null) return;
            if (Smash.Demo.SmashDemo.Requested(out var scene))
            {
                new GameObject("SmashDemo").AddComponent<Smash.Demo.SmashDemo>().Scene = scene;
                return;
            }
            new GameObject("Game").AddComponent<GameController>();
        }
    }
}
