using System;
using System.Reflection;
using EFT.Animations;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Computes the zoomed FOV for the main camera from template zoom multipliers.
    ///
    /// PRIMARY SOURCE — Template.Zooms via SightComponent:
    ///   Stepped scopes:  SightComponent.GetCurrentOpticZoom()
    ///   Variable scopes: Lerp(GetMinOpticZoom(), GetMaxOpticZoom(), ScopeZoomValue)
    ///
    /// FALLBACK — ScopeCameraData.FieldOfView (only if template zoom unavailable):
    ///   magnification = 35 / scopeCameraFov
    ///
    /// FOV formula (physically correct):
    ///   resultFov = 2 * atan(tan(baseFov/2) / magnification)
    ///   baseFov   = 50° fixed baseline
    /// </summary>
    internal static class FovController
    {
        // Fixed baseline for zoom-to-FOV conversion — independent of player FOV settings.
        public const float ZoomBaselineFov = 50f;

        // --- Per-scope caches (cleared on mode switch / scope exit) ---
        private static object _cachedSightComponent;
        private static OpticSight _cachedSightComponentForOptic;

        // --- Per-frame magnification cache (avoids redundant reflection per frame) ---
        private static float _cachedMagnification;
        private static int _cachedMagnificationFrame = -1;

        // --- Reflection cache for SightComponent access ---
        private static Type _smvcType;           // SightModVisualControllers
        private static PropertyInfo _sightModProp; // .SightMod → SightComponent
        private static bool _smvcSearched;

        private static MethodInfo _getCurrentZoom; // SightComponent.GetCurrentOpticZoom()
        private static MethodInfo _getMinZoom;     // SightComponent.GetMinOpticZoom()
        private static MethodInfo _getMaxZoom;     // SightComponent.GetMaxOpticZoom()
        private static FieldInfo  _scopeZoomValue; // SightComponent.ScopeZoomValue (float)
        private static PropertyInfo _adjOpticData; // SightComponent.AdjustableOpticData → IAdjustableOpticData
        private static PropertyInfo _isAdjustable; // IAdjustableOpticData.IsAdjustableOptic (bool)
        private static bool _sightComponentSearched;

        // --- Fallback: ScopeCameraData type/field cache ---
        private static Type _scopeCamDataType;
        private static FieldInfo _scopeCamDataFovField;
        private static bool _scopeCamDataSearched;

        // Dedup logging
        private static float _lastLoggedMag;
        private static string _lastLoggedSource;

        /// <summary>
        /// Computes the zoomed main-camera FOV from a fixed baseline of 50°.
        ///
        /// Priority chain:
        ///   1. Template zoom from SightComponent (ground truth — matches HUD "xN")
        ///   2. ScopeCameraData.FieldOfView fallback (mag = 35 / fov)
        ///   3. Config ScopedFov manual fallback
        /// </summary>
        public static float ComputeZoomedFov(float baseFov, ProceduralWeaponAnimation pwa)
        {
            _ = baseFov;
            _ = pwa;

            if (!ScopeHousingMeshSurgeryPlugin.AutoFovFromScope.Value)
                return ScopeHousingMeshSurgeryPlugin.ScopedFov.Value;

            float magnification = GetEffectiveMagnification();
            if (magnification > 0.1f)
            {
                float resultFov = MagnificationToFov(magnification, ZoomBaselineFov);

                // Log on change
                string source = _lastLoggedSource ?? "?";
                if (Mathf.Abs(magnification - _lastLoggedMag) > 0.01f)
                {
                    _lastLoggedMag = magnification;
                    ScopeHousingMeshSurgeryPlugin.LogInfo(
                        $"[FovController] mag={magnification:F2}x → mainFov={resultFov:F1}° " +
                        $"(baseline={ZoomBaselineFov:F0}°) [{source}]");
                }

                return resultFov;
            }

            return ScopeHousingMeshSurgeryPlugin.ScopedFov.Value;
        }

        /// <summary>
        /// Returns the current effective magnification from the best available source.
        /// Used by both FovController and ZoomController.
        /// Cached per-frame to avoid redundant reflection calls when multiple
        /// systems query magnification in the same frame.
        /// </summary>
        public static float GetEffectiveMagnification()
        {
            // Per-frame cache: multiple callers (Tick, WeaponScaling, etc.) hit this per frame
            int frame = Time.frameCount;
            if (frame == _cachedMagnificationFrame && _cachedMagnification > 0.1f)
                return _cachedMagnification;

            float result = GetEffectiveMagnificationUncached();
            _cachedMagnification = result;
            _cachedMagnificationFrame = frame;
            return result;
        }

        private static float GetEffectiveMagnificationUncached()
        {
            // 1. Template zoom (primary — matches HUD)
            float templateMag = GetTemplateZoom();
            if (templateMag > 0.1f)
            {
                _lastLoggedSource = "TEMPLATE";
                return templateMag;
            }

            // 2. ScopeCameraData FOV fallback
            float fovMag = GetFovBasedMagnification();
            if (fovMag > 0.1f)
            {
                _lastLoggedSource = "CAMERA_FOV";
                return fovMag;
            }

            // 3. Config default
            _lastLoggedSource = "DEFAULT";
            return ScopeHousingMeshSurgeryPlugin.DefaultZoom.Value;
        }

        /// <summary>
        /// Converts magnification to main-camera vertical FOV.
        ///   resultFov = 2 * atan(tan(baseFov/2) / magnification)
        /// </summary>
        public static float MagnificationToFov(float magnification, float baseFov)
        {
            if (magnification < 0.1f) magnification = 1f;
            float baseFovRad = baseFov * Mathf.Deg2Rad;
            float resultRad = 2f * Mathf.Atan2(Mathf.Tan(baseFovRad * 0.5f), magnification);
            return resultRad * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Called on mode switch to reset log dedup so the next computation logs.
        /// </summary>
        public static void OnModeSwitch()
        {
            _lastLoggedMag = 0f;
            _lastLoggedSource = null;
            // Clear per-scope caches so FindSightComponent re-discovers for new mode
            _cachedSightComponent = null;
            _cachedSightComponentForOptic = null;
            // Invalidate per-frame cache
            _cachedMagnificationFrame = -1;
        }

        // =====================================================================
        //  PRIMARY: Template zoom from SightComponent
        // =====================================================================

        /// <summary>
        /// Reads the magnification from Template.Zooms via SightComponent.
        ///
        /// Stepped (non-adjustable) scopes:
        ///   SightComponent.GetCurrentOpticZoom()
        ///     → Template.Zooms[SelectedScope][SelectedScopeMode]
        ///
        /// Variable (adjustable) scopes:
        ///   Lerp(GetMinOpticZoom(), GetMaxOpticZoom(), ScopeZoomValue)
        ///   This matches the HUD endpoint labels and smoothly interpolates
        ///   between them using the same parameter the game tracks.
        /// </summary>
        private static float GetTemplateZoom()
        {
            var os = ScopeLifecycle.ActiveOptic;
            if (os == null) return 0f;

            // Find SightComponent via SightModVisualControllers
            object sightComponent = FindSightComponent(os);
            if (sightComponent == null) return 0f;

            // Discover SightComponent methods if not yet cached
            if (!_sightComponentSearched)
            {
                _sightComponentSearched = true;
                DiscoverSightComponentMembers(sightComponent.GetType());
            }

            if (_getCurrentZoom == null) return 0f;

            try
            {
                // Check if this is an adjustable (variable zoom) optic
                bool isAdjustable = false;
                if (_adjOpticData != null && _isAdjustable != null)
                {
                    try
                    {
                        object adjData = _adjOpticData.GetValue(sightComponent);
                        if (adjData != null)
                            isAdjustable = (bool)_isAdjustable.GetValue(adjData);
                    }
                    catch { }
                }

                if (isAdjustable && _getMinZoom != null && _getMaxZoom != null && _scopeZoomValue != null)
                {
                    // Variable zoom: interpolate between template min/max
                    float minZoom = (float)_getMinZoom.Invoke(sightComponent, null);
                    float maxZoom = (float)_getMaxZoom.Invoke(sightComponent, null);
                    float zoomT = (float)_scopeZoomValue.GetValue(sightComponent);

                    // ScopeZoomValue: 0 = min zoom, 1 = max zoom. Clamp for safety.
                    zoomT = Mathf.Clamp01(zoomT);

                    if (minZoom > 0.1f && maxZoom > 0.1f && maxZoom > minZoom)
                    {
                        float zoom = Mathf.Lerp(minZoom, maxZoom, zoomT);
                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[FovController] Variable zoom: min={minZoom:F2}x max={maxZoom:F2}x " +
                            $"t={zoomT:F3} → {zoom:F2}x");
                        return zoom;
                    }
                }

                // Stepped or fallback: use GetCurrentOpticZoom()
                float currentZoom = (float)_getCurrentZoom.Invoke(sightComponent, null);
                if (currentZoom > 0.1f)
                {
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        $"[FovController] Template zoom: {currentZoom:F2}x (stepped)");
                    return currentZoom;
                }
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[FovController] GetTemplateZoom exception: {ex.Message}");
            }

            return 0f;
        }

        /// <summary>
        /// Returns the template min/max zoom for the current scope. Used by ZoomController
        /// for magnification bounds.
        /// Returns (minZoom, maxZoom). Returns (0,0) if not available.
        /// </summary>
        public static (float min, float max) GetTemplateZoomRange()
        {
            var os = ScopeLifecycle.ActiveOptic;
            if (os == null) return (0f, 0f);

            object sc = FindSightComponent(os);
            if (sc == null) return (0f, 0f);

            if (!_sightComponentSearched)
            {
                _sightComponentSearched = true;
                DiscoverSightComponentMembers(sc.GetType());
            }

            if (_getMinZoom == null || _getMaxZoom == null) return (0f, 0f);

            try
            {
                float minZ = (float)_getMinZoom.Invoke(sc, null);
                float maxZ = (float)_getMaxZoom.Invoke(sc, null);
                if (minZ > 0.1f && maxZ > 0.1f)
                    return (minZ, maxZ);
            }
            catch { }

            return (0f, 0f);
        }

        /// <summary>
        /// Returns true when the current optic exposes AdjustableOpticData.IsAdjustableOptic.
        /// Used for optional auto-bypass of variable scopes.
        /// </summary>
        public static bool IsCurrentOpticAdjustable()
        {
            return IsOpticAdjustable(ScopeLifecycle.ActiveOptic);
        }

        public static bool IsOpticAdjustable(OpticSight os)
        {
            if (os == null) return false;

            object sightComponent = FindSightComponent(os);
            if (sightComponent == null) return false;

            if (!_sightComponentSearched)
            {
                _sightComponentSearched = true;
                DiscoverSightComponentMembers(sightComponent.GetType());
            }

            if (_adjOpticData == null || _isAdjustable == null) return false;

            try
            {
                object adjData = _adjOpticData.GetValue(sightComponent);
                if (adjData == null) return false;
                return (bool)_isAdjustable.GetValue(adjData);
            }
            catch
            {
                return false;
            }
        }

        // =====================================================================
        //  SightComponent discovery via SightModVisualControllers
        // =====================================================================

        /// <summary>
        /// Finds the SightComponent for an OpticSight by walking up the hierarchy
        /// to find SightModVisualControllers, then reading its SightMod property.
        ///
        /// Hierarchy (from SPT4_Modding_Reference):
        ///   Sight Mod GameObject
        ///     ├── SightModVisualControllers  ← has SightMod (SightComponent)
        ///     └── mode_0 / mode_1
        ///          └── OpticSight  ← we start here
        /// </summary>
        private static object FindSightComponent(OpticSight os)
        {
            // Per-scope cache: SightComponent doesn't change during a scope session
            if (_cachedSightComponentForOptic == os && _cachedSightComponent != null)
                return _cachedSightComponent;

            if (!_smvcSearched)
            {
                _smvcSearched = true;
                DiscoverSightModVisualControllers();
            }

            if (_smvcType == null || _sightModProp == null) return null;

            try
            {
                // Walk up from OpticSight to find SightModVisualControllers
                Component smvc = os.GetComponentInParent(_smvcType);
                if (smvc == null)
                    smvc = os.GetComponentInChildren(_smvcType);

                // Also try walking to scope root explicitly
                if (smvc == null)
                {
                    Transform root = ScopeHierarchy.FindScopeRoot(os.transform);
                    if (root != null)
                    {
                        smvc = root.GetComponentInChildren(_smvcType, true);
                        // SightModVisualControllers sits on the sight mod object,
                        // which may be above scope_xxx
                        if (smvc == null && root.parent != null)
                            smvc = root.parent.GetComponentInChildren(_smvcType, true);
                    }
                }

                if (smvc == null) return null;

                var result = _sightModProp.GetValue(smvc);
                if (result != null)
                {
                    _cachedSightComponent = result;
                    _cachedSightComponentForOptic = os;
                }
                return result;
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[FovController] FindSightComponent exception: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Discovers the SightModVisualControllers type by:
        ///   1. Direct name lookup
        ///   2. Assembly scan for MonoBehaviour with "SightMod" property
        ///      whose return type has GetCurrentOpticZoom()
        /// </summary>
        private static void DiscoverSightModVisualControllers()
        {
            // Strategy 1: Direct type name lookup
            string[] typeNames = {
                "SightModVisualControllers",
                "EFT.Visual.SightModVisualControllers",
                "EFT.SightModVisualControllers",
            };

            foreach (var name in typeNames)
            {
                try
                {
                    var t = AccessTools.TypeByName(name);
                    if (t != null && typeof(MonoBehaviour).IsAssignableFrom(t))
                    {
                        var prop = FindSightModProperty(t);
                        if (prop != null)
                        {
                            _smvcType = t;
                            _sightModProp = prop;
                            ScopeHousingMeshSurgeryPlugin.LogInfo(
                                $"[FovController] Found SightModVisualControllers: {t.FullName}, " +
                                $"SightMod={prop.Name} ({prop.PropertyType.Name})");
                            return;
                        }
                    }
                }
                catch { }
            }

            // Strategy 2: Scan assemblies for MonoBehaviour with a property whose
            // return type has GetCurrentOpticZoom method
            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                "[FovController] SightModVisualControllers name lookup failed, scanning assemblies...");

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (!typeof(MonoBehaviour).IsAssignableFrom(type)) continue;

                        var prop = FindSightModProperty(type);
                        if (prop != null)
                        {
                            _smvcType = type;
                            _sightModProp = prop;
                            ScopeHousingMeshSurgeryPlugin.LogInfo(
                                $"[FovController] Discovered SightModVisualControllers via scan: " +
                                $"{type.FullName}, SightMod={prop.Name} ({prop.PropertyType.Name})");
                            return;
                        }
                    }
                }
                catch { }
            }

            ScopeHousingMeshSurgeryPlugin.LogWarn(
                "[FovController] SightModVisualControllers NOT found — template zoom unavailable");
        }

        /// <summary>
        /// Looks for a property on the given type that returns a SightComponent-like object.
        /// Identifies SightComponent by checking for GetCurrentOpticZoom method.
        /// Prefers property named "SightMod" but accepts any match.
        /// </summary>
        private static PropertyInfo FindSightModProperty(Type type)
        {
            PropertyInfo fallback = null;

            foreach (var prop in type.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (prop.PropertyType == typeof(object) || prop.PropertyType.IsPrimitive) continue;

                // Check if the return type looks like SightComponent
                // (has GetCurrentOpticZoom method)
                var getZoom = prop.PropertyType.GetMethod("GetCurrentOpticZoom",
                    BindingFlags.Public | BindingFlags.Instance);
                if (getZoom == null) continue;
                if (getZoom.ReturnType != typeof(float)) continue;

                // Prefer the property named "SightMod"
                if (prop.Name == "SightMod")
                    return prop;

                if (fallback == null)
                    fallback = prop;
            }

            return fallback;
        }

        /// <summary>
        /// Discovers methods/fields on SightComponent type for zoom access.
        /// </summary>
        private static void DiscoverSightComponentMembers(Type scType)
        {
            var flags = BindingFlags.Public | BindingFlags.Instance;

            _getCurrentZoom = scType.GetMethod("GetCurrentOpticZoom", flags);
            _getMinZoom = scType.GetMethod("GetMinOpticZoom", flags);
            _getMaxZoom = scType.GetMethod("GetMaxOpticZoom", flags);

            // ScopeZoomValue: public float field
            _scopeZoomValue = scType.GetField("ScopeZoomValue",
                BindingFlags.Public | BindingFlags.Instance);

            // AdjustableOpticData property → IAdjustableOpticData → IsAdjustableOptic
            _adjOpticData = scType.GetProperty("AdjustableOpticData", flags);
            if (_adjOpticData != null)
            {
                var adjType = _adjOpticData.PropertyType;
                _isAdjustable = adjType.GetProperty("IsAdjustableOptic", flags);
            }

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[FovController] SightComponent members: " +
                $"GetCurrentOpticZoom={_getCurrentZoom != null}, " +
                $"GetMinOpticZoom={_getMinZoom != null}, " +
                $"GetMaxOpticZoom={_getMaxZoom != null}, " +
                $"ScopeZoomValue={_scopeZoomValue != null}, " +
                $"AdjustableOpticData={_adjOpticData != null}, " +
                $"IsAdjustableOptic={_isAdjustable != null}");
        }

        // =====================================================================
        //  FALLBACK: ScopeCameraData.FieldOfView → magnification
        // =====================================================================

        /// <summary>
        /// Fallback magnification from scope camera FOV.
        /// Only used when template zoom is unavailable.
        /// magnification = 35 / scopeCameraFov  (EFT's standard optic camera baseline is 35°)
        /// </summary>
        private static float GetFovBasedMagnification()
        {
            var os = ScopeLifecycle.ActiveOptic;
            if (os == null) return 0f;

            // Try ScopeZoomHandler.FiledOfView first (runtime, variable zoom)
            float fov = GetScopeZoomHandlerFov(os);

            // Then ScopeCameraData.FieldOfView
            if (fov < 0.1f)
                fov = GetFovFromScopeCameraData(os);

            // Then brute force
            if (fov < 0.1f)
                fov = BruteForceFovSearch(os);

            if (fov > 0.1f)
                return 35f / fov;

            return 0f;
        }

        private static float GetScopeZoomHandlerFov(OpticSight os)
        {
            try
            {
                var szh = os.GetComponentInParent<ScopeZoomHandler>();
                if (szh == null) szh = os.GetComponentInChildren<ScopeZoomHandler>();
                if (szh != null)
                {
                    float fov = szh.FiledOfView; // EFT typo
                    if (fov > 0.1f) return fov;
                }
            }
            catch { }
            return 0f;
        }

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
                Component scd = os.GetComponent(_scopeCamDataType);
                if (scd == null) scd = os.GetComponentInChildren(_scopeCamDataType);
                if (scd == null) scd = os.GetComponentInParent(_scopeCamDataType);

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
                    foreach (var comp in root.GetComponentsInChildren(_scopeCamDataType, true))
                    {
                        if (IsOnSameModeAs(comp.transform, os.transform))
                        { scd = comp; break; }
                    }
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
                            $"[FovController] ScopeCameraData fallback: FOV={fov:F2}");
                        return fov;
                    }
                }
            }
            catch { }
            return 0f;
        }

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
                    var fovField = type.GetField("FieldOfView",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (fovField == null || fovField.FieldType != typeof(float)) continue;

                    float fov = (float)fovField.GetValue(mb);
                    if (fov <= 0.1f || fov >= 180f) continue;

                    if (IsOnSameModeAs(mb.transform, os.transform))
                    {
                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[FovController] BruteForce fallback: {mb.gameObject.name} FOV={fov:F2}");
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
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[FovController] BruteForce error: {ex.Message}");
            }
            return 0f;
        }

        // =====================================================================
        //  ScopeCameraData type discovery
        // =====================================================================

        private static void DiscoverScopeCameraDataType()
        {
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
                "[FovController] ScopeCameraData type NOT found — fallback unavailable");
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private static bool IsOnSameModeAs(Transform candidate, Transform optic)
        {
            Transform GetMode(Transform t)
            {
                for (var p = t; p != null; p = p.parent)
                    if (p.name != null && p.name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase))
                        return p;
                return null;
            }
            var modeC = GetMode(candidate);
            var modeO = GetMode(optic);
            if (modeC == null || modeO == null) return modeC == modeO;
            return modeC == modeO;
        }
    }
}
