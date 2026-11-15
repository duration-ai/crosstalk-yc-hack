using System;
using UnityEngine;

namespace Dogpatch.Operator
{
    // Operator settings from Resources/Operator/operator.json, overridable on the command line
    // (-operator-convex <url>, -operator-local, -operator-transport auto|dotnet|http,
    // -operator-room <code>, -operator-commit manual|vad, -operator-pad-ms <ms>) and by the
    // CONVEX_URL environment variable. An empty convexUrl, -operator-local or the scripted
    // -speeddial-demo keep the on-device LocalOperator.
    [Serializable]
    public class OperatorConfig
    {
        public string convexUrl = "";
        public string transport = "auto";
        public string roomCode = "";
        public float connectTimeoutS = 6f;
        public string sttModel = "scribe_v2_realtime";
        public string language = "en";
        public string commitStrategy = "manual";
        public string[] keyterms = { "Waymo", "Zoox", "Lyft", "YC", "nitro" };
        public int chunkMs = 150;
        public int preRollMs = 500;
        public int silencePadMs = 0;
        public int finalWaitMs = 1500;
        public bool debugInput = true;

        public bool UseConvex => !string.IsNullOrWhiteSpace(convexUrl);

        public static OperatorConfig Load()
        {
            var asset = Resources.Load<TextAsset>("Operator/operator");
            var config = asset != null ? JsonUtility.FromJson<OperatorConfig>(asset.text) : new OperatorConfig();
            var env = Environment.GetEnvironmentVariable("CONVEX_URL");
            if (!string.IsNullOrWhiteSpace(env)) config.convexUrl = env.Trim();
            var args = Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                int at = Array.IndexOf(args, name);
                return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
            }
            bool demo = Array.IndexOf(args, "-speeddial-demo") >= 0;
            config.convexUrl = Arg("-operator-convex") ?? (demo || Array.IndexOf(args, "-operator-local") >= 0 ? "" : config.convexUrl);
            config.transport = Arg("-operator-transport") ?? config.transport;
            config.roomCode = Arg("-operator-room") ?? config.roomCode;
            config.commitStrategy = Arg("-operator-commit") ?? config.commitStrategy;
            if (int.TryParse(Arg("-operator-pad-ms"), out int pad)) config.silencePadMs = pad;
            return config;
        }
    }
}
