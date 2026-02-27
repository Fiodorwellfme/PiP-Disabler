using System;
using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Swaps main camera LOD bias and culling settings with scope-appropriate values during ADS.
    ///
    /// When zoomed in, distant objects fill more screen pixels and should render at higher detail.
    /// This manager reads the scope's ScopeCameraData (FieldOfView, FarClipPlane, etc.) and
    /// increases the LOD bias proportionally to the magnification.
    ///
    /// From Elcan ScopeCameraData:
    ///   FieldOfView = 5.75 (4x mode) / 23 (1x mode)
    ///   NearClipPlane = 0.05
    ///   FarClipPlane = 1000
    ///   OpticCullingMask = 1
    ///   OpticCullingMaskScale = 1
    ///
    /// Settings modified:
    ///   QualitySettings.lodBias          — multiplied by magnification factor or manually set in config
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
        private static FieldInfo _scdFovField;
        private static FieldInfo _scdFarClipField;
        private static FieldInfo _scdNearClipField;
        private static FieldInfo _scdCullingMaskField;
        private static FieldInfo _scdCullingScaleField;
        private static bool _scdSearched;

        /// <summary>
        /// Apply scope-optimized camera settings for the active optic.
        /// Call on scope enter and mode switch.
        /// </summary>
        public static void ApplyForOptic(OpticSight os)
        {
            if (os == null) return;

            var cam = ScopeHousingMeshSurgeryPlugin.GetMainCamera();
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

                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[CameraSettings] Saved: lodBias={_savedLodBias:F2} farClip={_savedFarClip:F0} " +
                    $"maxLOD={_savedMaxLodLevel}");
            }

            // Read scope's ScopeCameraData for its settings
            float scopeFov = 0f;
            float scopeFarClip = 0f;

            if (TryGetScopeCameraData(os, out scopeFov, out scopeFarClip))
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[CameraSettings] ScopeCameraData: FOV={scopeFov:F2} FarClip={scopeFarClip:F0}");
            }

            // Calculate magnification, NEEDS TO BE REMOVED
            float magnification = 1f;
            if (scopeFov > 0.1f)
                magnification = 35f / scopeFov;

            // === Apply LOD bias ===
            // THIS NEEDS TO BE SET MANUALLY IN THE CONFIG FILE AS WELL, IF SET TO 0 THEN WE NEED TO FIND ANOTHER OPTION
            // Increase LOD bias proportionally to magnification.
            // At 4x zoom, objects at distance appear 4x larger → use 4x finer LODs.
            //if ScopeHousingMeshSurgeryPlugin.LODbias.Value == 0
            //    float newLodBias = _savedLodBias * Mathf.Max(magnification, 1f);
            //if ScopeHousingMeshSurgeryPlugin.LODbias.Value != 0 
            //    float newLodBias = ScopeHousingMeshSurgeryPlugin.LODbias.Value;
            float newLodBias = _savedLodBias * Mathf.Max(magnification, 1f);
            QualitySettings.lodBias = newLodBias;

            // Set different LOD level during scope view from main view.
            // THIS NEEDS TO BE SET MANUALLY IN THE CONFIG FILE AS WELL
            // QualitySettings.maximumLODLevel = ScopeHousingMeshSurgeryPlugin.maximumLODLevel.Value;
            QualitySettings.maximumLODLevel = 0;

            // === Apply far clip plane ===
            if (scopeFarClip > cam.farClipPlane)
                cam.farClipPlane = scopeFarClip;

            // === Adjust layer cull distances for scope magnification ===
            // Increase cull distances proportionally so objects stay visible when zoomed
            if (_savedCullDistances != null)
            {
                float[] newCull = (float[])_savedCullDistances.Clone();
                for (int i = 0; i < newCull.Length; i++)
                {
                    if (newCull[i] > 0f)
                        newCull[i] *= Mathf.Max(magnification, 1f);
                }
                cam.layerCullDistances = newCull;
            }

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[CameraSettings] Applied: lodBias {_savedLodBias:F2}→{newLodBias:F2} " +
                $"(mag={magnification:F1}x) farClip={cam.farClipPlane:F0} maxLOD=0");
        }

        /// <summary>
        /// Restore original camera settings. Call on scope exit.
        /// </summary>
        public static void Restore()
        {
            if (!_applied) return;

            QualitySettings.lodBias = _savedLodBias;
            QualitySettings.maximumLODLevel = _savedMaxLodLevel;

            var cam = ScopeHousingMeshSurgeryPlugin.GetMainCamera();            if (cam != null)
            {
                cam.farClipPlane = _savedFarClip;
                if (_savedCullDistances != null)
                    cam.layerCullDistances = _savedCullDistances;
            }

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[CameraSettings] Restored: lodBias={_savedLodBias:F2} " +
                $"farClip={_savedFarClip:F0} maxLOD={_savedMaxLodLevel}");

            _applied = false;
        }

        /// <summary>
        /// Try to read ScopeCameraData from the scope hierarchy via reflection.
        /// </summary>
        private static bool TryGetScopeCameraData(OpticSight os, out float fov, out float farClip)
        {
            fov = 0f;
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

                if (_scdFovField != null)
                    fov = (float)_scdFovField.GetValue(scd);
                if (_scdFarClipField != null)
                    farClip = (float)_scdFarClipField.GetValue(scd);

                return fov > 0.1f;
            }
            catch { return false; }
        }

        private static bool IsOnSameMode(Transform a, Transform b)
        {
            Transform GetMode(Transform t)
            {
                for (var p = t; p != null; p = p.parent)
                    if (p.name != null && p.name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase))
                        return p;
                return null;
            }
            var mA = GetMode(a);
            var mB = GetMode(b);
            return mA == mB;
        }

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
                        if (_scdFovField != null) return;
                    }
                }
                catch { }
            }

            // Assembly scan: MonoBehaviour with FieldOfView + NearClipPlane + FarClipPlane
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
                        if (f1.FieldType != typeof(float)) continue;

                        CacheFields(type);
                        ScopeHousingMeshSurgeryPlugin.LogInfo(
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
            _scdFovField = t.GetField("FieldOfView", BindingFlags.Public | BindingFlags.Instance);
            _scdFarClipField = t.GetField("FarClipPlane", BindingFlags.Public | BindingFlags.Instance);
            _scdNearClipField = t.GetField("NearClipPlane", BindingFlags.Public | BindingFlags.Instance);
            _scdCullingMaskField = t.GetField("OpticCullingMask", BindingFlags.Public | BindingFlags.Instance);
            _scdCullingScaleField = t.GetField("OpticCullingMaskScale", BindingFlags.Public | BindingFlags.Instance);
        }
    }
}
