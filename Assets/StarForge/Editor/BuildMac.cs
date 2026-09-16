// BuildMac.cs — builds the macOS player for Apple silicon (MacBook Neo, A18 Pro).
//
// IL2CPP is used when this editor has the macOS IL2CPP variation installed and
// a C++ toolchain is present (it compiles the sim and AI to native code, which
// matters on a 2-performance-core CPU); otherwise the build falls back to Mono
// and says so.
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
            Debug.Log($"[StarForge] macOS build {s.result}: {s.outputPath}  " +
                      $"{s.totalSize / (1024f * 1024f):0.0} MB, {s.totalTime.TotalSeconds:0}s, " +
                      $"backend {(il2cpp ? "IL2CPP" : "Mono")}, {s.totalErrors} errors, {s.totalWarnings} warnings");
            return report;
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
