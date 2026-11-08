#if UNITY_IOS && UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Editor.Build
{
    public static class IosBuildPostProcessor
    {
        [PostProcessBuild(999)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            ConfigureInfoPlist(pathToBuiltProject);
            ConfigureXcodeProject(pathToBuiltProject);
        }

        private static void ConfigureInfoPlist(string pathToBuiltProject)
        {
            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            if (!File.Exists(plistPath))
            {
                return;
            }

            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            PlistElementDict root = plist.root;

            // Enable iOS Files app integration ("On My iPhone > YARG > Songs")
            root.SetBoolean("UIFileSharingEnabled", true);
            root.SetBoolean("LSSupportsOpeningDocumentsInPlace", true);
            root.SetBoolean("UISupportsDocumentBrowser", false);

            // Force full-screen edge-to-edge rendering
            root.SetBoolean("UIRequiresFullScreen", true);
            root.SetBoolean("UIStatusBarHidden", true);
            root.SetBoolean("UIViewControllerBasedStatusBarAppearance", false);

            // Enable 120Hz ProMotion frame rates on iOS
            root.SetBoolean("CADisableMinimumFrameDurationOnPhone", true);

            // Ensure modern launch screen dictionary exists so iOS never falls back to 16:9 letterboxed compatibility mode
            if (!root.values.ContainsKey("UILaunchScreen"))
            {
                root.CreateDict("UILaunchScreen");
            }

            // Permissions for Vocals microphone and Bluetooth controllers/instruments
            root.SetString("NSMicrophoneUsageDescription",
                "YARG uses the microphone for Vocals and Harmonies gameplay.");
            root.SetString("NSBluetoothAlwaysUsageDescription",
                "YARG uses Bluetooth to connect wireless rhythm game instruments and controllers.");
            root.SetString("NSBluetoothPeripheralUsageDescription",
                "YARG uses Bluetooth to connect wireless rhythm game instruments and controllers.");

            // MFi / Bluetooth / USB-C Game Controller capabilities
            root.SetBoolean("GCSupportsControllerUserInteraction", true);
            PlistElementArray controllers = root.CreateArray("GCSupportedGameControllers");
            controllers.AddDict().SetString("ProfileName", "ExtendedGamepad");
            controllers.AddDict().SetString("ProfileName", "MicroGamepad");
            controllers.AddDict().SetString("ProfileName", "DirectionalGamepad");

            // Allow all orientations for full-screen display versatility
            ConfigureOrientations(root, "UISupportedInterfaceOrientations");
            ConfigureOrientations(root, "UISupportedInterfaceOrientations~ipad");

            plist.WriteToFile(plistPath);
        }

        private static void ConfigureOrientations(PlistElementDict root, string key)
        {
            PlistElementArray orientations = root.CreateArray(key);
            orientations.AddString("UIInterfaceOrientationLandscapeLeft");
            orientations.AddString("UIInterfaceOrientationLandscapeRight");
            orientations.AddString("UIInterfaceOrientationPortrait");
            orientations.AddString("UIInterfaceOrientationPortraitUpsideDown");
        }

        private static void ConfigureXcodeProject(string pathToBuiltProject)
        {
            string pbxPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            if (!File.Exists(pbxPath))
            {
                return;
            }

            var proj = new PBXProject();
            proj.ReadFromFile(pbxPath);

            string mainTarget = proj.GetUnityMainTargetGuid();
            string frameworkTarget = proj.GetUnityFrameworkTargetGuid();

            foreach (string targetGuid in new[] { mainTarget, frameworkTarget })
            {
                if (string.IsNullOrEmpty(targetGuid))
                {
                    continue;
                }

                proj.SetBuildProperty(targetGuid, "ENABLE_BITCODE", "NO");
                proj.AddBuildProperty(targetGuid, "LD_RUNPATH_SEARCH_PATHS", "@executable_path/Frameworks");
                proj.AddBuildProperty(targetGuid, "LD_RUNPATH_SEARCH_PATHS", "@loader_path/Frameworks");
            }

            if (!string.IsNullOrEmpty(frameworkTarget))
            {
                proj.AddFrameworkToProject(frameworkTarget, "GameController.framework", false);
                proj.AddFrameworkToProject(frameworkTarget, "CoreHaptics.framework", true);
                proj.AddFrameworkToProject(frameworkTarget, "AudioToolbox.framework", false);
                proj.AddFrameworkToProject(frameworkTarget, "AVFoundation.framework", false);
                proj.AddFrameworkToProject(frameworkTarget, "ImageIO.framework", false);
                proj.AddFrameworkToProject(frameworkTarget, "CoreGraphics.framework", false);
                proj.AddFrameworkToProject(frameworkTarget, "CFNetwork.framework", false);

                proj.AddBuildProperty(frameworkTarget, "OTHER_LDFLAGS", "-lsqlite3");
                proj.AddBuildProperty(frameworkTarget, "OTHER_LDFLAGS", "-lz");
            }

            proj.WriteToFile(pbxPath);
        }
    }
}
#endif
