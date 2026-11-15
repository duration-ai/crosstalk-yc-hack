using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Dogpatch.EditorTools
{
    // Reports the imported (compressed) size of every clip for the active build target, as the
    // Inspector's "Imported Size" shows it. Run headless with -buildTarget iOS:
    //   Unity -batchmode -quit -buildTarget iOS -executeMethod Dogpatch.EditorTools.AudioBudget.Report
    public static class AudioBudget
    {
        public static void Report()
        {
            var compSize = typeof(AudioImporter).GetProperty("compSize", BindingFlags.NonPublic | BindingFlags.Instance);
            var origSize = typeof(AudioImporter).GetProperty("origSize", BindingFlags.NonPublic | BindingFlags.Instance);
            var csv = new StringBuilder("folder,clip,seconds,channels,rate,format,load,original_bytes,imported_bytes\n");
            long total = 0, resident = 0, streamed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources/Audio" }).OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                var s = importer.defaultSampleSettings;
                long comp = compSize != null ? System.Convert.ToInt64(compSize.GetValue(importer)) : 0;
                long orig = origSize != null ? System.Convert.ToInt64(origSize.GetValue(importer)) : 0;
                total += comp;
                if (s.loadType == AudioClipLoadType.Streaming) streamed += comp;
                else resident += comp;
                csv.AppendLine($"{Path.GetFileName(Path.GetDirectoryName(path))},{clip.name},{clip.length:F2},{clip.channels},{clip.frequency},{s.compressionFormat},{s.loadType},{orig},{comp}");
            }
            string output = System.Environment.GetEnvironmentVariable("AUDIO_BUDGET_OUT") ?? "../Builds/audio-budget.csv";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, csv.ToString());
            Debug.Log($"[AudioBudget] target={EditorUserBuildSettings.activeBuildTarget} total={total / 1024} KB resident={resident / 1024} KB streamed={streamed / 1024} KB -> {output}");
        }
    }
}
