using System;
using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace PiPDisabler
{
    /// <summary>
    /// Swaps main camera LOD bias and culling settings with scope-appropriate values during ADS.
    ///
    /// When zoomed in, distant objects fill more screen pixels and should render at higher detail.
    /// This manager uses template-derived magnification for LOD/culling and only reads
    /// ScopeCameraData for the far clip override.
    ///
    /// From Elcan ScopeCameraData:
    ///   FieldOfView = 5.75 (4x mode) / 23 (1x mode)
    ///   NearClipPlane = 0.05
    ///   FarClipPlane = 1000
    ///   OpticCullingMask = 1
    ///   OpticCullingMaskScale = 1
    ///
    /// Settings modified:
    ///   QualitySettings.lodBias          — multiplied by magnification factor
    ///   Camera.main.farClipPlane         — set to scope's FarClipPlane if greater
    ///   Camera.layerCullDistances        — adjusted for scope's culling scale
    /// </summary>
    internal static class CameraSettingsManager
    {
        // Saved original values for restore
        private static float _savedLodBias;
        private static float _savedFarClip;
        private static float[] _savedCullDistances;
        private static int _savedMaxLodLevel;
        private static bool _applied;

        // Reflection cache for ScopeCameraData fields
        private static Type _scdType;
        private static FieldInfo _scdFarClipField;
        private static bool _scdSearched;

        /// <summary>
        /// Apply scope-optimized camera settings for the active optic.
        /// Call on scope enter and mode switch.
        /// </summary>
        public static void ApplyForOptic(OpticSight os)
        {
            if (os == null) return;

            var cam = PiPDisablerPlugin.GetMainCamera();
            if (cam == null) return;

            // Save originals (only on first apply, not re-apply from mode switch)
            if (!_applied)
            {
                _savedLodBias = QualitySettings.lodBias;
                _savedFarClip = cam.farClipPlane;
                _savedCullDistances = cam.layerCullDistances != null
                    ? (float[])cam.layerCullDistances.Clone()
                    : null;
                _savedMaxLodLevel = QualitySettings.maximumLODLevel;
                _applied = true;

                PiPDisablerPlugin.LogVerbose(
                    $"[CameraSettings] Saved: lodBias={_savedLodBias:F2} farClip={_savedFarClip:F0} " +
                    $"maxLOD={_savedMaxLodLevel}");
            }

            // Read scope's ScopeCameraData for its settings
            float scopeFarClip = 0f;

            if (TryGetScopeCameraData(os, out scopeFarClip))
            {
                PiPDisablerPlugin.LogVerbose(
                    $"[CameraSettings] ScopeCameraData: FarClip={scopeFarClip:F0}");
            }

            // Calculate magnification from template zoom (matches HUD).
            float magnification = FovController.GetEffectiveMagnification();
            if (magnification < 0.1f)
                magnification = 1f;

            // === Apply LOD bias ===
            // Increase LOD bias proportionally to magnification.
            // At 4x zoom, objects at distance appear 4x larger → use 4x finer LODs.
            float manualLodBias = PiPDisablerPlugin.GetManualLodBiasForCurrentMap();
            float newLodBias = manualLodBias > 0f
                ? manualLodBias
                : _savedLodBias * Mathf.Max(magnification, 1f);
            QualitySettings.lodBias = newLodBias;

            // Force highest LOD by default unless overridden by manual max LOD level.
            int manualMaxLod = PiPDisablerPlugin.ManualMaximumLodLevel != null
                ? PiPDisablerPlugin.ManualMaximumLodLevel.Value
                : -1;
            int appliedMaxLod = manualMaxLod >= 0 ? manualMaxLod : 0;
            QualitySettings.maximumLODLevel = appliedMaxLod;

            // === Apply far clip plane ===
            if (scopeFarClip > cam.farClipPlane)
                cam.farClipPlane = scopeFarClip;

            // === Adjust layer cull distances for scope magnification ===
            // Increase cull distances proportionally so objects stay visible when zoomed
            if (_savedCullDistances != null)
            {
                float manualCullMultiplier = PiPDisablerPlugin.ManualCullingMultiplier != null
                    ? PiPDisablerPlugin.ManualCullingMultiplier.Value
                    : 0f;
                float cullingMultiplier = manualCullMultiplier > 0f
                    ? manualCullMultiplier
                    : Mathf.Max(magnification, 1f);

                float[] newCull = (float[])_savedCullDistances.Clone();
                for (int i = 0; i < newCull.Length; i++)
                {
                    if (newCull[i] > 0f)
                        newCull[i] *= cullingMultiplier;
                }
                cam.layerCullDistances = newCull;
            }

            PiPDisablerPlugin.LogInfo(
                $"[CameraSettings] Applied: lodBias {_savedLodBias:F2}→{newLodBias:F2} " +
                $"(mag={magnification:F1}x) farClip={cam.farClipPlane:F0} maxLOD={appliedMaxLod}");
        }

        /// <summary>
        /// Restore original camera settings. Call on scope exit.
        /// </summary>
        public static void Restore()
        {
            if (!_applied) return;

            QualitySettings.lodBias = _savedLodBias;
            QualitySettings.maximumLODLevel = _savedMaxLodLevel;

            var cam = PiPDisablerPlugin.GetMainCamera();
            if (cam != null)
            {
                cam.farClipPlane = _savedFarClip;
                if (_savedCullDistances != null)
                    cam.layerCullDistances = _savedCullDistances;
            }

            PiPDisablerPlugin.LogInfo(
                $"[CameraSettings] Restored: lodBias={_savedLodBias:F2} " +
                $"farClip={_savedFarClip:F0} maxLOD={_savedMaxLodLevel}");

            _applied = false;
        }

        /// <summary>
        /// Try to read ScopeCameraData from the scope hierarchy via reflection.
        /// </summary>
        private static bool TryGetScopeCameraData(OpticSight os, out float farClip)
        {
            farClip = 0f;

            DiscoverType();

            if (_scdType == null) return false;

            try
            {
                // Find the ScopeCameraData component on the same mode as the active optic
                Component scd = os.GetComponent(_scdType);
                if (scd == null) scd = os.GetComponentInChildren(_scdType);
                if (scd == null) scd = os.GetComponentInParent(_scdType);

                // Search scope root as fallback
                if (scd == null)
                {
                    Transform root = os.transform;
                    while (root.parent != null)
                    {
                        var pn = root.parent.name ?? "";
                        if (pn.StartsWith("scope_", StringComparison.OrdinalIgnoreCase))
                        { root = root.parent; break; }
                        root = root.parent;
                    }

                    // Find ScopeCameraData on same mode
                    foreach (var comp in root.GetComponentsInChildren(_scdType, true))
                    {
                        if (IsOnSameMode(comp.transform, os.transform))
                        {
                            scd = comp;
                            break;
                        }
                    }
                }

                if (scd == null) return false;

                if (_scdFarClipField != null)
                    farClip = (float)_scdFarClipField.GetValue(scd);

                return farClip > 0.1f;
            }
            catch { return false; }
        }

        private static bool IsOnSameMode(Transform a, Transform b)
            => PiPDisablerPlugin.IsOnSameMode(a, b);

        private static void DiscoverType()
        {
            if (_scdSearched) return;
            _scdSearched = true;

            // Try known names
            string[] names = { "EFT.CameraControl.ScopeCameraData", "ScopeCameraData" };
            foreach (var name in names)
            {
                try
                {
                    var t = AccessTools.TypeByName(name);
                    if (t != null && typeof(MonoBehaviour).IsAssignableFrom(t))
                    {
                        CacheFields(t);
                        if (_scdFarClipField != null) return;
                    }
                }
                catch { }
            }

            // Assembly scan: MonoBehaviour with FarClipPlane and the rest of the ScopeCameraData shape.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (!typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
                        var f1 = type.GetField("FieldOfView", BindingFlags.Public | BindingFlags.Instance);
                        var f2 = type.GetField("NearClipPlane", BindingFlags.Public | BindingFlags.Instance);
                        var f3 = type.GetField("FarClipPlane", BindingFlags.Public | BindingFlags.Instance);
                        if (f1 == null || f2 == null || f3 == null) continue;
                        if (f1.FieldType != typeof(float) || f3.FieldType != typeof(float)) continue;

                        CacheFields(type);
                        PiPDisablerPlugin.LogInfo(
                            $"[CameraSettings] Found ScopeCameraData: {type.FullName}");
                        return;
                    }
                }
                catch { }
            }
        }

        private static void CacheFields(Type t)
        {
            _scdType = t;
            _scdFarClipField = t.GetField("FarClipPlane", BindingFlags.Public | BindingFlags.Instance);
        }
    }
}
