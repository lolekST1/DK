// Editor-side stubs, same purpose as UnityStubs.cs: type-checking without Unity present.
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEditor
{
    public enum SerializationMode { Mixed, ForceBinary, ForceText }

    public static class EditorSettings
    {
        public static SerializationMode serializationMode;
        public static string externalVersionControl;
    }

    public static class EditorApplication
    {
        public delegate void CallbackFunction();
        public static CallbackFunction delayCall;
        public static bool isPlayingOrWillChangePlaymode;
        public static void Exit(int code) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class InitializeOnLoadAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class MenuItemAttribute : Attribute { public MenuItemAttribute(string path) { } }

    public class SceneAsset : UnityEngine.Object { }

    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object => null;
        public static bool IsValidFolder(string path) => false;
        public static string CreateFolder(string parent, string name) => "";
        public static void CreateAsset(UnityEngine.Object asset, string path) { }
        public static void SaveAssets() { }
        public static void Refresh() { }
    }

    public class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene(string path, bool enabled) { this.path = path; this.enabled = enabled; }
        public string path;
        public bool enabled;
    }

    public static class EditorBuildSettings
    {
        public static EditorBuildSettingsScene[] scenes = new EditorBuildSettingsScene[0];
    }

    public static class SessionState
    {
        public static bool GetBool(string key, bool def) => false;
        public static void SetBool(string key, bool value) { }
    }

    public enum BuildTarget { WebGL }
    public enum BuildTargetGroup { WebGL }
    public enum BuildOptions { None }
    public enum ScriptingImplementation { Mono2x, IL2CPP }
    public enum Il2CppCompilerConfiguration { Debug, Release, Master }
    public enum WebGLCompressionFormat { Brotli, Gzip, Disabled }
    public enum WebGLExceptionSupport { None, ExplicitlyThrownExceptionsOnly, FullWithoutStacktrace, FullWithStacktrace }

    public struct BuildPlayerOptions
    {
        public string[] scenes;
        public string locationPathName;
        public BuildTarget target;
        public BuildTargetGroup targetGroup;
        public BuildOptions options;
    }

    public static class BuildPipeline
    {
        public static Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions options) => null;
    }

    public static class PlayerSettings
    {
        public static string companyName;
        public static string productName;
        public static bool runInBackground;

        public static class WebGL
        {
            public static WebGLCompressionFormat compressionFormat;
            public static bool decompressionFallback;
            public static bool dataCaching;
            public static WebGLExceptionSupport exceptionSupport;
            public static string template;
        }

        public static void SetScriptingBackend(Build.NamedBuildTarget target, ScriptingImplementation impl) { }
        public static void SetIl2CppCompilerConfiguration(Build.NamedBuildTarget target, Il2CppCompilerConfiguration cfg) { }
    }
}

namespace UnityEditor.Build
{
    public struct NamedBuildTarget
    {
        public static NamedBuildTarget WebGL => default;
    }
}

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown, Succeeded, Failed, Cancelled }

    public struct BuildSummary
    {
        public BuildResult result;
        public ulong totalSize;
        public int totalErrors;
    }

    public class BuildReport { public BuildSummary summary; }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene, DefaultGameObjects }
    public enum NewSceneMode { Single, Additive }
    public enum OpenSceneMode { Single, Additive, AdditiveWithoutLoading }

    public static class EditorSceneManager
    {
        public static Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => default;
        public static Scene OpenScene(string path, OpenSceneMode mode) => default;
        public static bool SaveScene(Scene scene, string path) => true;
        public static bool SaveScene(Scene scene) => true;
        public static bool CloseScene(Scene scene, bool removeScene) => true;
        public static void MarkSceneDirty(Scene scene) { }
    }
}
