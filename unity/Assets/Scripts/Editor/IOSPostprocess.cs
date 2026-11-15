using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace Dogpatch.EditorTools
{
    // App Store requirements the Unity iOS export does not cover on its own: the export-compliance
    // flag and a 1024px single-size app icon (unity/Branding/AppIcon-1024.png, opaque RGB).
    public class IOSPostprocess : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
#if UNITY_IOS
            if (report.summary.platform != BuildTarget.iOS) return;
            string project = report.summary.outputPath;

            string plistPath = Path.Combine(project, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            plist.WriteToFile(plistPath);

            string iconSource = Path.GetFullPath("Branding/AppIcon-1024.png");
            if (!File.Exists(iconSource)) throw new BuildFailedException("Missing app icon " + iconSource);
            string iconSet = Path.Combine(project, "Unity-iPhone/Images.xcassets/AppIcon.appiconset");
            if (Directory.Exists(iconSet)) Directory.Delete(iconSet, true);
            Directory.CreateDirectory(iconSet);
            File.Copy(iconSource, Path.Combine(iconSet, "AppIcon-1024.png"));
            File.WriteAllText(Path.Combine(iconSet, "Contents.json"),
                "{\"images\":[{\"filename\":\"AppIcon-1024.png\",\"idiom\":\"universal\",\"platform\":\"ios\",\"size\":\"1024x1024\"}],\"info\":{\"author\":\"xcode\",\"version\":1}}\n");
#endif
        }
    }
}
