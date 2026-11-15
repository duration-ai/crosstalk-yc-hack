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
            new GameObject("Game").AddComponent<GameController>();
        }
    }
}
