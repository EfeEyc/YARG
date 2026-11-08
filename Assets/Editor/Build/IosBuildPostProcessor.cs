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

            // Force full screen edge-to-edge rendering without windowed letterboxing
            root.SetBoolean("UIRequiresFullScreen", true);
            root.SetBoolean("UIStatusBarHidden", true);
            root.SetBoolean("UIViewControllerBasedStatusBarAppearance", false);

            // Enable 120Hz ProMotion frame rates on iOS
            root.SetBoolean("CADisableMinimumFrameDurationOnPhone", true);

            // Use the unrestricted launch screen for all device classes so UIKit never letterboxes to 16:9
            root.SetString("UILaunchStoryboardName", "LaunchScreen-iPad");
            root.SetString("UILaunchStoryboardName~ipad", "LaunchScreen-iPad");
            root.SetString("UILaunchStoryboardName~iphone", "LaunchScreen-iPad");
            if (!root.values.ContainsKey("UILaunchScreen"))
            {
                root.CreateDict("UILaunchScreen");
            }

            foreach (string file in Directory.GetFiles(pathToBuiltProject, "LaunchScreen-iPad.storyboard", SearchOption.AllDirectories))
            {
                string dir = Path.GetDirectoryName(file);
                if (dir != null)
                {
                    string iphoneFile = Path.Combine(dir, "LaunchScreen-iPhone.storyboard");
                    File.Copy(file, iphoneFile, true);
                }
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
            ConfigureOrientations(root, "UISupportedInterfaceOrientations~iphone");

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
                proj.AddBuildProperty(targetGuid, "LD_RUNPATH_SEARCH_PATHS", "@executable_path/Frameworks/bass.framework");
                proj.AddBuildProperty(targetGuid, "LD_RUNPATH_SEARCH_PATHS", "@executable_path/Frameworks/bass_fx.framework");
                proj.AddBuildProperty(targetGuid, "LD_RUNPATH_SEARCH_PATHS", "@executable_path/Frameworks/bassmix.framework");
                proj.AddBuildProperty(targetGuid, "LD_RUNPATH_SEARCH_PATHS", "@executable_path/Frameworks/bassopus.framework");
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

                // Ensure desktop dynamic libraries are not linked into the iOS build
                string stbDylibGuid = proj.FindFileGuidByProjectPath("Libraries/Plugins/STB2CSharp/MacOS/STB2CSharp.dylib");
                if (!string.IsNullOrEmpty(stbDylibGuid))
                {
                    proj.RemoveFileFromBuild(frameworkTarget, stbDylibGuid);
                    proj.RemoveFile(stbDylibGuid);
                }
            }

            proj.WriteToFile(pbxPath);
        }
    }
}
#endif
