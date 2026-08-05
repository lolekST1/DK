using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DK.EditorTools
{
    /// <summary>
    /// One-time project wiring that would otherwise be manual Editor clicking: text asset
    /// serialization, the URP pipeline asset, and the bootstrap scene being present and in
    /// the build list. Runs on load, is idempotent, and never touches settings twice.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectAutoSetup
    {
        const string ScenePath = "Assets/Scenes/Bootstrap.unity";
        const string SettingsFolder = "Assets/Settings";
        const string PipelineAssetPath = SettingsFolder + "/DK_URP.asset";
        const string RendererAssetPath = SettingsFolder + "/DK_URP_Renderer.asset";
        const string SceneCheckedFlag = "DK.BootstrapSceneChecked";

        static ProjectAutoSetup()
        {
            // Asset importing is still in flight during a static constructor.
            EditorApplication.delayCall += Run;
        }

        static void Run()
        {
            EnsureSerializationSettings();
            EnsureBootstrapScene();
            EnsureRenderPipeline();
        }

        [MenuItem("Dungeon Keeper Prototype/Re-run Project Setup")]
        static void RunFromMenu() => Run();

        // ---------------------------------------------------------------- serialization

        static void EnsureSerializationSettings()
        {
            if (EditorSettings.serializationMode != SerializationMode.ForceText)
                EditorSettings.serializationMode = SerializationMode.ForceText;

            // EditorSettings.externalVersionControl is obsolete; VersionControlSettings owns this now.
            if (VersionControlSettings.mode != "Visible Meta Files")
                VersionControlSettings.mode = "Visible Meta Files";
        }

        // ---------------------------------------------------------------- scene

        static void EnsureBootstrapScene()
        {
            if (!File.Exists(ScenePath))
            {
                CreateBootstrapScene();
            }
            else if (!SessionState.GetBool(SceneCheckedFlag, false) &&
                     AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                // Opening the scene is only worth doing once per Editor session, not on
                // every domain reload.
                SessionState.SetBool(SceneCheckedFlag, true);
                RepairBootstrapSceneIfNeeded();
            }

            AddSceneToBuildSettings();
        }

        static void CreateBootstrapScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var go = new GameObject("GameBootstrap");
            go.AddComponent<GameBootstrap>();
            SceneManager.MoveGameObjectToScene(go, scene);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh();

            Debug.Log($"[DK] Created bootstrap scene at {ScenePath}.");
        }

        /// <summary>
        /// The committed scene holds a single GameBootstrap component. If that reference ever
        /// breaks (a script GUID changing, say), put it back rather than leaving a dead scene.
        /// </summary>
        static void RepairBootstrapSceneIfNeeded()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var opened = SceneManager.GetSceneByPath(ScenePath);
            bool wasAlreadyOpen = opened.IsValid() && opened.isLoaded;
            var scene = wasAlreadyOpen ? opened : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            bool found = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<GameBootstrap>() == null) continue;
                found = true;
                break;
            }

            if (!found)
            {
                var target = scene.rootCount > 0 ? scene.GetRootGameObjects()[0] : null;
                if (target == null)
                {
                    target = new GameObject("GameBootstrap");
                    SceneManager.MoveGameObjectToScene(target, scene);
                }

                target.AddComponent<GameBootstrap>();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[DK] Restored the missing GameBootstrap component on the bootstrap scene.");
            }

            if (!wasAlreadyOpen) EditorSceneManager.CloseScene(scene, true);
        }

        static void AddSceneToBuildSettings()
        {
            var existing = EditorBuildSettings.scenes;
            foreach (var entry in existing)
            {
                if (entry.path != ScenePath) continue;

                if (!entry.enabled)
                {
                    entry.enabled = true;
                    EditorBuildSettings.scenes = existing; // The array only persists on assignment.
                }
                return;
            }

            var scenes = new EditorBuildSettingsScene[existing.Length + 1];
            scenes[0] = new EditorBuildSettingsScene(ScenePath, true);
            Array.Copy(existing, 0, scenes, 1, existing.Length);
            EditorBuildSettings.scenes = scenes;

            Debug.Log("[DK] Added the bootstrap scene to Build Settings.");
        }

        // ---------------------------------------------------------------- render pipeline

        /// <summary>
        /// Creates and assigns a URP asset if the package is installed and nothing is assigned.
        /// Done by reflection so the project still compiles if URP is absent — the game itself
        /// picks its shaders from whichever pipeline ends up active.
        /// </summary>
        static void EnsureRenderPipeline()
        {
            if (GraphicsSettings.defaultRenderPipeline != null) return;

            var existing = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PipelineAssetPath);
            if (existing != null)
            {
                AssignPipeline(existing);
                return;
            }

            var pipelineType = FindType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset");
            var rendererDataType = FindType("UnityEngine.Rendering.Universal.UniversalRendererData");
            if (pipelineType == null || rendererDataType == null)
            {
                Debug.Log("[DK] URP package not found — running on the Built-in pipeline. " +
                          "The prototype renders either way.");
                return;
            }

            var createMethod = pipelineType.GetMethod(
                "Create",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { FindType("UnityEngine.Rendering.Universal.ScriptableRendererData") ?? rendererDataType },
                null);

            if (createMethod == null)
            {
                Debug.LogWarning("[DK] Could not create a URP asset automatically (UniversalRenderPipelineAsset.Create " +
                                 "is not available in this URP version). Create one via Assets > Create > Rendering > " +
                                 "URP Asset and assign it in Project Settings > Graphics.");
                return;
            }

            try
            {
                if (!AssetDatabase.IsValidFolder(SettingsFolder))
                    AssetDatabase.CreateFolder("Assets", "Settings");

                var rendererData = ScriptableObject.CreateInstance(rendererDataType);
                rendererData.name = "DK_URP_Renderer";
                AssetDatabase.CreateAsset(rendererData, RendererAssetPath);

                var pipeline = (RenderPipelineAsset)createMethod.Invoke(null, new object[] { rendererData });
                pipeline.name = "DK_URP";
                AssetDatabase.CreateAsset(pipeline, PipelineAssetPath);
                AssetDatabase.SaveAssets();

                AssignPipeline(pipeline);
                Debug.Log($"[DK] Created and assigned a URP asset at {PipelineAssetPath}.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DK] Automatic URP setup failed ({e.Message}). The prototype still runs on the " +
                                 "Built-in pipeline; assign a URP asset manually if you want URP.");
            }
        }

        static void AssignPipeline(RenderPipelineAsset pipeline)
        {
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            TuneRendering(pipeline);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// A URP asset is created with multisampling off and 50 metres of shadow distance,
        /// which is a fraction of this map. The Editor's Game view hides both — it is usually
        /// looking at a small part of the dungeon from close up — and a build does not: block
        /// edges crawl, and everything past the shadow distance lights differently from
        /// everything inside it, which reads as only part of the map being lit.
        ///
        /// Set through reflection like the rest of the URP handling here, so the project still
        /// compiles and runs with the package absent.
        /// </summary>
        public static void TuneRendering(RenderPipelineAsset pipeline)
        {
            // Built-in fallback path, and harmless under URP.
            QualitySettings.antiAliasing = 4;
            QualitySettings.shadowDistance = ShadowDistance;
            QualitySettings.pixelLightCount = 4;

            if (pipeline == null) return;

            TrySet(pipeline, "msaaSampleCount", 4);
            TrySet(pipeline, "shadowDistance", ShadowDistance);
            TrySet(pipeline, "shadowCascadeCount", 4);
        }

        /// <summary>Long enough to cover a 32x32 grid seen from the far end of the zoom.</summary>
        const float ShadowDistance = 160f;

        static void TrySet(object target, string property, object value)
        {
            var info = target.GetType().GetProperty(property,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.SetProperty);

            if (info == null || !info.CanWrite) return;

            try
            {
                info.SetValue(target, value);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DK] Could not set {property} on the render pipeline asset ({e.Message}).");
            }
        }

        static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }
    }
}
