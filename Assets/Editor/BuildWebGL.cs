using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DK.EditorTools
{
    /// <summary>
    /// WebGL build entry point. Usable from the menu, or headless from CI/CLI:
    ///
    ///   Unity -quit -batchmode -nographics -projectPath . -executeMethod DK.EditorTools.BuildWebGL.Build
    /// </summary>
    public static class BuildWebGL
    {
        const string OutputPath = "Builds/WebGL";

        [MenuItem("Dungeon Keeper Prototype/Build WebGL")]
        public static void Build()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[DK] No enabled scenes in Build Settings — nothing to build.");
                if (InBatchMode) EditorApplication.Exit(1);
                return;
            }

            ApplyPlayerSettings();
            Directory.CreateDirectory(OutputPath);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[DK] WebGL build succeeded: {summary.totalSize / (1024 * 1024)} MB in {OutputPath}");
                return;
            }

            Debug.LogError($"[DK] WebGL build failed: {summary.result} ({summary.totalErrors} errors)");
            if (InBatchMode) EditorApplication.Exit(1);
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "DK Prototype";
            PlayerSettings.productName = "Dungeon Keeper Prototype";

            // Uncompressed output serves straight off any static host (and off `python -m
            // http.server`) with no Content-Encoding configuration.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.WebGL.template = "APPLICATION:Default";
            PlayerSettings.runInBackground = true;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.WebGL, Il2CppCompilerConfiguration.Release);
        }

        static bool InBatchMode => Application.isBatchMode;
    }
}
