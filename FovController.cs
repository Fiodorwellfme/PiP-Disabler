using System;
using System.Reflection;
using EFT.Animations;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Computes the zoomed FOV for the main camera (FOV zoom fallback mode).
    ///
    /// Zoom calculation (physically correct):
    ///   resultFov = 2 * atan(tan(baseFov/2) / magnification)
    ///
    /// FOV sources (in priority order):
    ///   1. ScopeZoomHandler.FiledOfView — runtime, variable zoom scopes
    ///   2. ScopeCameraData.FieldOfView — baked in prefab, discovered by assembly scan
    ///   3. Brute-force scan for any MonoBehaviour with FieldOfView field
    ///   4. Config ScopedFov — manual fallback
    /// </summary>
    internal static class FovController
    {
        // Cache the discovered ScopeCameraData type and field
        private static Type _scopeCamDataType;
        private static FieldInfo _scopeCamDataFovField;
        private static bool _scopeCamDataSearched;

        // Cache the last computed scope FOV for logging
        private static float _lastScopeFov;

        /// <summary>
        /// Computes the zoomed FOV given the player's base FOV.
        /// </summary>
        public static float ComputeZoomedFov(float baseFov, ProceduralWeaponAnimation pwa)
        {
            if (ScopeHousingMeshSurgeryPlugin.AutoFovFromScope.Value)
            {
                // Check scroll zoom override first — keeps FOV and reticle in sync
                float overrideFov = ZoomController.GetEffectiveScopeFov();
                float scopeFov = overrideFov > 0.1f ? overrideFov : GetScopeFov();

                if (scopeFov > 0.1f)
                {
                    float magnification = 35f / scopeFov;
                    float baseFovRad = baseFov * Mathf.Deg2Rad;
                    float resultFovRad = 2f * Mathf.Atan2(Mathf.Tan(baseFovRad * 0.5f), magnification);
                    float resultFov = resultFovRad * Mathf.Rad2Deg;

                    // Only log when value actually changes
                    if (Mathf.Abs(scopeFov - _lastScopeFov) > 0.01f)
                    {
                        _lastScopeFov = scopeFov;
                        ScopeHousingMeshSurgeryPlugin.LogInfo(
                            $"[FovController] scopeFov={scopeFov:F2} → mag={magnification:F2}x → " +
                            $"mainFov={resultFov:F1} (baseFov={baseFov:F0})" +
                            (overrideFov > 0.1f ? " [SCROLL OVERRIDE]" : ""));
                    }

                    return resultFov;
                }
            }

            return ScopeHousingMeshSurgeryPlugin.ScopedFov.Value;
        }

        /// <summary>
        /// Called on mode switch to reset the cached FOV so the next computation logs.
        /// </summary>
        public static void OnModeSwitch()
        {
            _lastScopeFov = 0f;
        }

        /// <summary>
        /// Gets the scope's optic camera FOV. Searches multiple sources.
        /// </summary>
        private static float GetScopeFov()
        {
            var os = ScopeLifecycle.ActiveOptic;
            if (os == null) return 0f;

            // === Try 1: ScopeZoomHandler.FiledOfView (runtime, variable zoom) ===
            try
            {
                var szh = os.GetComponentInParent<ScopeZoomHandler>();
                if (szh == null) szh = os.GetComponentInChildren<ScopeZoomHandler>();
                if (szh != null)
                {
                    float fov = szh.FiledOfView; // EFT typo
                    if (fov > 0.1f) return fov * 2f;
                }
            }
            catch { }

            // === Try 2: ScopeCameraData component via reflection scan ===
            float scdFov = GetFovFromScopeCameraData(os);
            if (scdFov > 0.1f) return scdFov * 2f;

            // === Try 3: Brute-force scan all MonoBehaviours for FieldOfView field ===
            float bruteFov = BruteForceFovSearch(os);
            if (bruteFov > 0.1f) return bruteFov * 2f;

            return 0f;
        }

        /// <summary>
        /// Find ScopeCameraData by cached type. Discovers type on first call.
        /// </summary>
        private static float GetFovFromScopeCameraData(OpticSight os)
        {
            if (!_scopeCamDataSearched)
            {
                _scopeCamDataSearched = true;
                DiscoverScopeCameraDataType();
            }

            if (_scopeCamDataType == null || _scopeCamDataFovField == null) return 0f;

            try
            {
                // ScopeCameraData lives as a sibling of OpticSight on the mode_XXX GameObject.
                // The OpticSight IS on mode_XXX, so search that GO and siblings.
                Component scd = os.GetComponent(_scopeCamDataType);
                if (scd == null) scd = os.GetComponentInChildren(_scopeCamDataType);
                if (scd == null) scd = os.GetComponentInParent(_scopeCamDataType);

                // Also try the scope root's children (some scopes have ScopeCameraData on a sibling node)
                if (scd == null)
                {
                    Transform root = os.transform;
                    while (root.parent != null)
                    {
                        var pn = root.parent.name ?? "";
                        if (pn.StartsWith("scope_", StringComparison.OrdinalIgnoreCase)) { root = root.parent; break; }
                        root = root.parent;
                    }
                    // Get all ScopeCameraData components under root, pick the one matching active mode
                    foreach (var comp in root.GetComponentsInChildren(_scopeCamDataType, true))
                    {
                        // Check if this component is associated with the active optic's mode
                        if (IsOnSameModeAs(comp.transform, os.transform))
                        {
                            scd = comp;
                            break;
                        }
                    }
                    // If no mode match, take any
                    if (scd == null)
                    {
                        var all = root.GetComponentsInChildren(_scopeCamDataType, true);
                        if (all.Length > 0) scd = all[0];
                    }
                }

                if (scd != null)
                {
                    float fov = (float)_scopeCamDataFovField.GetValue(scd);
                    if (fov > 0.1f)
                    {
                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[FovController] ScopeCameraData '{scd.gameObject.name}': FOV={fov:F2}");
                        return fov;
                    }
                }
            }
            catch { }

            return 0f;
        }

        /// <summary>
        /// Check if a transform shares the same mode_XXX ancestor as the optic.
        /// </summary>
        private static bool IsOnSameModeAs(Transform candidate, Transform optic)
        {
            // Walk up from each to find their mode_XXX ancestor
            Transform GetMode(Transform t)
            {
                for (var p = t; p != null; p = p.parent)
                    if (p.name != null && p.name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase))
                        return p;
                return null;
            }
            var modeC = GetMode(candidate);
            var modeO = GetMode(optic);
            if (modeC == null || modeO == null) return modeC == modeO; // both null = root level = match
            return modeC == modeO;
        }

        /// <summary>
        /// Discover the ScopeCameraData type by trying known names, then scanning assemblies.
        /// Matches any MonoBehaviour with FieldOfView + NearClipPlane + FarClipPlane fields.
        /// </summary>
        private static void DiscoverScopeCameraDataType()
        {
            // Try known type names first
            string[] typeNames = {
                "EFT.CameraControl.ScopeCameraData",
                "ScopeCameraData",
                "EFT.ScopeCameraData",
            };

            foreach (var name in typeNames)
            {
                try
                {
                    var t = AccessTools.TypeByName(name);
                    if (t != null && typeof(MonoBehaviour).IsAssignableFrom(t))
                    {
                        var f = t.GetField("FieldOfView", BindingFlags.Public | BindingFlags.Instance);
                        if (f != null && f.FieldType == typeof(float))
                        {
                            _scopeCamDataType = t;
                            _scopeCamDataFovField = f;
                            ScopeHousingMeshSurgeryPlugin.LogInfo(
                                $"[FovController] Found ScopeCameraData: {t.FullName}");
                            return;
                        }
                    }
                }
                catch { }
            }

            // Scan all assemblies for MonoBehaviour with FieldOfView+NearClipPlane+FarClipPlane
            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                "[FovController] Named lookup failed, scanning assemblies...");

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (!typeof(MonoBehaviour).IsAssignableFrom(type)) continue;

                        var fovF = type.GetField("FieldOfView", BindingFlags.Public | BindingFlags.Instance);
                        if (fovF == null || fovF.FieldType != typeof(float)) continue;

                        var ncpF = type.GetField("NearClipPlane", BindingFlags.Public | BindingFlags.Instance);
                        if (ncpF == null || ncpF.FieldType != typeof(float)) continue;

                        var fcpF = type.GetField("FarClipPlane", BindingFlags.Public | BindingFlags.Instance);
                        if (fcpF == null) continue;

                        _scopeCamDataType = type;
                        _scopeCamDataFovField = fovF;
                        ScopeHousingMeshSurgeryPlugin.LogInfo(
                            $"[FovController] Discovered ScopeCameraData via scan: {type.FullName}");
                        return;
                    }
                }
                catch { }
            }

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                "[FovController] ScopeCameraData type NOT found — will use manual FOV config");
        }

        /// <summary>
        /// Last resort: scan all MonoBehaviour components on the scope hierarchy
        /// for any that have a FieldOfView field on the active mode.
        /// </summary>
        private static float BruteForceFovSearch(OpticSight os)
        {
            try
            {
                Transform root = os.transform;
                while (root.parent != null)
                {
                    var n = root.parent.name ?? "";
                    if (n.StartsWith("scope_", StringComparison.OrdinalIgnoreCase))
                    { root = root.parent; break; }
                    root = root.parent;
                }

                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    var type = mb.GetType();

                    var fovField = type.GetField("FieldOfView", BindingFlags.Public | BindingFlags.Instance);
                    if (fovField == null || fovField.FieldType != typeof(float)) continue;

                    float fov = (float)fovField.GetValue(mb);
                    if (fov <= 0.1f || fov >= 180f) continue;

                    // Only use FOV from the same mode as our active optic
                    if (IsOnSameModeAs(mb.transform, os.transform))
                    {
                        ScopeHousingMeshSurgeryPlugin.LogInfo(
                            $"[FovController] BruteForce '{mb.gameObject.name}' type={type.Name} FOV={fov:F2}");

                        // Cache the type for future
                        if (_scopeCamDataType == null)
                        {
                            _scopeCamDataType = type;
                            _scopeCamDataFovField = fovField;
                        }
                        return fov;
                    }
                }
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose($"[FovController] BruteForce error: {ex.Message}");
            }

            return 0f;
        }
    }
}
