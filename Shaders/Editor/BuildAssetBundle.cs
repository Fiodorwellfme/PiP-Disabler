// BuildAssetBundle.cs — Unity Editor script
// Place this in Assets/Editor/ in a Unity project (version matching EFT, ~2021.x)
//
// Steps:
//   1. Create a new Unity project (or open existing)
//   2. Copy ScopeZoom.shader into Assets/Shaders/
//   3. Copy this file into Assets/Editor/
//   4. In Project window, select ScopeZoom.shader
//   5. In Inspector, set AssetBundle to "scopezoom" (bottom-right dropdown)
//   6. Menu: Assets → Build Scope Zoom Bundle
//   7. Copy the generated scopezoom.bundle to your mod folder:
//      BepInEx/plugins/ScopeHousingMeshSurgery/assets/scopezoom.bundle

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public class BuildScopeZoomBundle
{
    [MenuItem("Assets/Build Scope Zoom Bundle")]
    static void BuildBundle()
    {
        string outputDir = Path.Combine(Application.dataPath, "..", "AssetBundles");
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // Method 1: Build all marked bundles
        BuildPipeline.BuildAssetBundles(
            outputDir,
            BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows64);

        string bundlePath = Path.Combine(outputDir, "scopezoom");
        if (File.Exists(bundlePath))
        {
            Debug.Log($"[ScopeZoom] Bundle built successfully: {bundlePath}");
            Debug.Log("[ScopeZoom] Copy this file to: BepInEx/plugins/ScopeHousingMeshSurgery/assets/scopezoom.bundle");

            // Also copy with .bundle extension for clarity
            string destPath = Path.Combine(outputDir, "scopezoom.bundle");
            if (bundlePath != destPath)
                File.Copy(bundlePath, destPath, overwrite: true);
        }
        else
        {
            Debug.LogError("[ScopeZoom] Bundle not found! Make sure ScopeZoom.shader has AssetBundle set to 'scopezoom'.");
        }
    }

    // Alternative: Build a single bundle from explicit asset list (no need to tag assets)
    [MenuItem("Assets/Build Scope Zoom Bundle (Auto)")]
    static void BuildBundleAuto()
    {
        string outputDir = Path.Combine(Application.dataPath, "..", "AssetBundles");
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // Find the shader
        string[] guids = AssetDatabase.FindAssets("ScopeZoom t:Shader");
        if (guids.Length == 0)
        {
            Debug.LogError("[ScopeZoom] ScopeZoom.shader not found in project. Place it in Assets/Shaders/.");
            return;
        }

        string shaderPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        Debug.Log($"[ScopeZoom] Found shader at: {shaderPath}");

        AssetBundleBuild[] builds = new AssetBundleBuild[1];
        builds[0].assetBundleName = "scopezoom";
        builds[0].assetNames = new string[] { shaderPath };

        BuildPipeline.BuildAssetBundles(
            outputDir,
            builds,
            BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows64);

        string bundlePath = Path.Combine(outputDir, "scopezoom");
        if (File.Exists(bundlePath))
        {
            string destPath = Path.Combine(outputDir, "scopezoom.bundle");
            File.Copy(bundlePath, destPath, overwrite: true);
            Debug.Log($"[ScopeZoom] Bundle built: {destPath}");
            Debug.Log("[ScopeZoom] Copy to: BepInEx/plugins/ScopeHousingMeshSurgery/assets/scopezoom.bundle");
        }
    }
}
#endif
