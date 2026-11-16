using System;
using System.IO;
using UnityEngine;

namespace Dogpatch.Race
{
    // Headless check: "-setpiece-stills <dir>" saves one still of every set piece as soon as a race
    // builds them, looking along the racing line from behind and above.
    public sealed class SetPieceStills : MonoBehaviour
    {
        string output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-setpiece-stills");
            if (at < 0 || at + 1 >= args.Length) return;
            var stills = new GameObject("SetPieceStills").AddComponent<SetPieceStills>();
            stills.output = args[at + 1];
            SetPieces.Built += stills.Capture;
        }

        void Capture(Transform root)
        {
            Directory.CreateDirectory(output);
            var rt = new RenderTexture(1280, 720, 24);
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var cam = new GameObject("still camera").AddComponent<Camera>();
            var main = Camera.main;
            if (main != null) cam.CopyFrom(main);
            cam.targetTexture = rt;
            cam.fieldOfView = 55f;
            string route = root.name.Replace("SetPieces ", "").Replace(" ", "-").Replace("&", "and").ToLowerInvariant();
            foreach (Transform piece in root)
            {
                var bounds = new Bounds(piece.position, Vector3.one);
                foreach (var r in piece.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                float size = Mathf.Clamp(bounds.extents.magnitude, 3f, 45f);
                cam.transform.position = bounds.center - piece.forward * (size * 1.3f + 5f) + piece.right * size * 0.35f + Vector3.up * (size * 0.55f + 3f);
                cam.transform.LookAt(bounds.center);
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                var path = Path.Combine(output, $"{route}-{piece.name.Replace(" ", "-")}.png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log("[SetPieces] still " + path);
            }
            Destroy(cam.gameObject);
            Destroy(tex);
            rt.Release();
        }
    }
}
