// BuildMac.cs — builds the macOS player for Apple silicon (MacBook Neo, A18 Pro).
//
// IL2CPP is used when this editor has the macOS IL2CPP variation installed and
// a C++ toolchain is present (it compiles the sim and AI to native code, which
// matters on a 2-performance-core CPU); otherwise the build falls back to Mono
// and says so.
//
// The icon is Art/AppIcon.png (made by Tools/make_app_icon.py): Apple's icon
// grid with transparent corners. After a build the bundle is touched and
// re-registered with Launch Services, because Unity rebuilds into the same
// bundle without changing its date and macOS otherwise keeps showing whatever
// icon it cached the first time it saw the app -- here, none.
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace StarForge.EditorTools
{
    public static class BuildMac
    {
        public const string OutputPath = "Builds/StarForge.app";
        public const string IconPath = "Assets/StarForge/Art/AppIcon.png";

        [MenuItem("StarForge/Build macOS Player (Apple silicon)", priority = 60)]
        public static void BuildMenu() => Build(true);

        public static BuildReport Build(bool preferIl2Cpp)
        {
            var target = NamedBuildTarget.Standalone;
            bool il2cpp = preferIl2Cpp && Il2CppAvailable();
            PlayerSettings.SetScriptingBackend(target, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
            if (il2cpp) PlayerSettings.SetIl2CppCodeGeneration(target, Il2CppCodeGeneration.OptimizeSpeed);
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = OSArchitecture.ARM64;
            PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.Low);
            PlayerSettings.macOS.buildNumber = "1";
            PlayerSettings.bundleVersion = "1.0";
            PlayerSettings.SetApplicationIdentifier(target, "com.starforge.game");
            ConfigureIcon();

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MapBuilder.ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            if (s.result == BuildResult.Succeeded) RefreshIcon(OutputPath);
            UnityEngine.Debug.Log($"[StarForge] macOS build {s.result}: {s.outputPath}  " +
                      $"{s.totalSize / (1024f * 1024f):0.0} MB, {s.totalTime.TotalSeconds:0}s, " +
                      $"backend {(il2cpp ? "IL2CPP" : "Mono")}, {s.totalErrors} errors, {s.totalWarnings} warnings");
            return report;
        }

        static void ConfigureIcon()
        {
            var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer == null) { UnityEngine.Debug.LogWarning("[StarForge] no app icon at " + IconPath); return; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed || importer.mipmapEnabled ||
                !importer.alphaIsTransparency || importer.npotScale != TextureImporterNPOTScale.None || importer.maxTextureSize < 1024)
            {
                importer.textureType = TextureImporterType.Default;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        }

        static void RefreshIcon(string app)
        {
            try
            {
                string full = Path.GetFullPath(app);
                Directory.SetLastWriteTimeUtc(full, DateTime.UtcNow);
                const string lsregister = "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister";
                if (!File.Exists(lsregister)) return;
                using var p = Process.Start(new ProcessStartInfo(lsregister, $"-f \"{full}\"") { UseShellExecute = false, CreateNoWindow = true });
                p?.WaitForExit(10000);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[StarForge] could not refresh the app icon: " + e.Message);
            }
        }

        static bool Il2CppAvailable()
        {
            string engines = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "MacStandaloneSupport", "Variations");
            bool variation = Directory.Exists(engines) &&
                             Directory.GetDirectories(engines, "*il2cpp*").Length > 0;
            bool toolchain = File.Exists("/usr/bin/clang") || Directory.Exists("/Library/Developer/CommandLineTools");
            return variation && toolchain;
        }
    }
}
