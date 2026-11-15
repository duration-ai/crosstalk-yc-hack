using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dogpatch.EditorTools
{
    // Import settings for Resources/Audio by folder, for every platform: music streams as Vorbis,
    // ambience beds stay Vorbis-compressed in memory, and every short SFX and engine loop is
    // ADPCM compressed in memory (cheap to decode, no loop clicks).
    public sealed class AudioImportRules : AssetPostprocessor
    {
        public override uint GetVersion() => 2;

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/Resources/Audio/")) return;
            var importer = (AudioImporter)assetImporter;
            string folder = Path.GetFileName(Path.GetDirectoryName(assetPath));
            var s = importer.defaultSampleSettings;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            switch (folder)
            {
                case "Music":
                    s.compressionFormat = AudioCompressionFormat.Vorbis;
                    s.quality = 0.1f;
                    s.loadType = Path.GetFileNameWithoutExtension(assetPath).StartsWith("music_") ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
                    break;
                case "Ambience":
                    s.compressionFormat = AudioCompressionFormat.Vorbis;
                    s.quality = 0.45f;
                    s.loadType = AudioClipLoadType.CompressedInMemory;
                    break;
                default:
                    s.compressionFormat = AudioCompressionFormat.ADPCM;
                    s.loadType = AudioClipLoadType.CompressedInMemory;
                    break;
            }
            importer.defaultSampleSettings = s;
            importer.forceToMono = folder != "Music";
            importer.loadInBackground = folder == "Music";
            importer.ClearSampleSettingOverride("iOS");
        }
    }
}
