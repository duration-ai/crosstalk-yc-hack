using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Dogpatch.EditorTools
{
    // Headless build entry points, run through scripts/build-ios.sh and scripts/build-linux.sh:
    //   Unity -batchmode -nographics -quit -projectPath unity -buildTarget iOS \
    //         -executeMethod Dogpatch.EditorTools.BuildScript.BuildIOS
    // Environment:
    //   IOS_SDK        simulator (default) or device
    //   IOS_BUNDLE_ID  bundle identifier (default com.crosstalk.dogpatch)
    //   APPLE_TEAM_ID  Apple developer team for automatic signing of device builds
    //   BUILD_NUMBER   CFBundleVersion (default: UTC timestamp yyyyMMdd.HHmm, so every upload increments)
    //   BUILD_VERSION  CFBundleShortVersionString (default 0.1.0)
    //   BUILD_OUTPUT   output folder (default ../Builds/iOS-<sdk> or ../Builds/Linux)
    //   BUILD_DEFINES  extra scripting defines, comma separated (SMASH_DEMO: the smash showcase build)
    public static class BuildScript
    {
        const string ScenePath = "Assets/Scenes/Bootstrap.unity";

        static string Env(string name, string fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        public static void BuildIOS()
        {
            string sdk = Env("IOS_SDK", "simulator").ToLowerInvariant();
            if (sdk != "simulator" && sdk != "device") Fail("IOS_SDK must be simulator or device, got " + sdk);
            bool simulator = sdk == "simulator";
            string team = Env("APPLE_TEAM_ID", "");
            string output = Path.GetFullPath(Env("BUILD_OUTPUT", "../Builds/iOS-" + sdk));

            ConfigureCommon();
            var target = NamedBuildTarget.iOS;
            PlayerSettings.SetApplicationIdentifier(target, Env("IOS_BUNDLE_ID", "com.crosstalk.dogpatch"));
            PlayerSettings.iOS.buildNumber = Env("BUILD_NUMBER", DateTime.UtcNow.ToString("yyyyMMdd.HHmm"));
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] { GraphicsDeviceType.Metal });
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.sdkVersion = simulator ? iOSSdkVersion.SimulatorSDK : iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64;
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
            PlayerSettings.iOS.cameraUsageDescription = "Take a full-body photo to make your avatar, and show your face on calls with the Operator.";
            PlayerSettings.iOS.microphoneUsageDescription = "Hold Call to talk to the Operator.";
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.appleDeveloperTeamID = team;
            if (!simulator && team.Length == 0)
                Debug.LogWarning("[Build] APPLE_TEAM_ID is not set; pick a team in Xcode before running on a device");

            Build(BuildTarget.iOS, output, BuildOptions.None);
        }

        // Desktop Linux player, used to render screenshots of the city on a Linux machine.
        public static void BuildLinux()
        {
            ConfigureCommon();
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.OpenGLCore });
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            string output = Path.GetFullPath(Env("BUILD_OUTPUT", "../Builds/Linux"));
            Build(BuildTarget.StandaloneLinux64, Path.Combine(output, "Dogpatch.x86_64"), BuildOptions.None);
        }

        static void ConfigureCommon()
        {
            EnsureBootstrapScene();
            PlayerSettings.companyName = "Crosstalk";
            PlayerSettings.productName = "Dogpatch";
            PlayerSettings.bundleVersion = Env("BUILD_VERSION", "0.1.0");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.statusBarHidden = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.stripEngineCode = false;

            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (settings != null)
            {
                var so = new SerializedObject(settings);
                var handler = so.FindProperty("activeInputHandler");
                if (handler != null && handler.intValue != 0)
                {
                    handler.intValue = 0;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            AssetDatabase.SaveAssets();
        }

        // The single bootstrap scene is generated, never hand-authored. Fog is enabled on it so
        // Unity keeps the fog shader variants that GameController turns on at runtime.
        static void EnsureBootstrapScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.skybox = null;
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void Build(BuildTarget target, string location, BuildOptions options)
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = location,
                target = target,
                options = options,
                extraScriptingDefines = Env("BUILD_DEFINES", "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries),
            });
            var summary = report.summary;
            Debug.Log($"[Build] {target} {summary.result} in {summary.totalTime} -> {location} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} errors)");
            if (summary.result != BuildResult.Succeeded) Fail("Build failed: " + summary.result);
        }

        static void Fail(string message)
        {
            Debug.LogError("[Build] " + message);
            EditorApplication.Exit(1);
        }
    }
}
