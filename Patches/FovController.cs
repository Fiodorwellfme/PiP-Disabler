using System;
using System.Reflection;
using EFT.CameraControl;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace PiPDisabler
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
        public static float ZoomBaselineFov => PiPDisablerPlugin.BaselineFOV.Value;

        // --- Per-scope caches (cleared on mode switch / scope exit) ---
        private static SightComponent _cachedSightComponent;
        private static OpticSight _cachedSightComponentForOptic;

        // --- Per-frame magnification cache (avoids redundant reflection per frame) ---
        private static float _cachedMagnification;
        private static int _cachedMagnificationFrame = -1;

        // --- Reflection cache: SightModVisualControllers discovery only ---
        // (SightComponent members are accessed directly — all public)
        private static Type _smvcType;           // SightModVisualControllers
        private static PropertyInfo _sightModProp; // .SightMod → SightComponent
        private static bool _smvcSearched;

        // --- Fallback: ScopeCameraData type/field cache ---
        private static Type _scopeCamDataType;
        private static FieldInfo _scopeCamDataFovField;
        private static bool _scopeCamDataSearched;

        // Dead-band: suppress SetFov calls when the target FOV hasn't changed enough.
        // Prevents CameraClass.SetFov from restarting the lerp coroutine every frame
        // when method_23 is ticked, which would stall the animation and cause flashing.
        private static float _lastAppliedFov;
        public const float FovChangeThreshold = 0.05f; // degrees

        /// <summary>
        /// Returns true when <paramref name="newFov"/> differs from the last
        /// applied FOV by at least <see cref="FovChangeThreshold"/> degrees.
        /// </summary>
        public static bool HasFovChanged(float newFov)
            => Mathf.Abs(newFov - _lastAppliedFov) >= FovChangeThreshold;

        /// <summary>Records the most-recently applied FOV target.</summary>
        public static void TrackAppliedFov(float fov) => _lastAppliedFov = fov;

        // Dedup logging
        private static float _lastLoggedMag;
        private static string _lastLoggedSource;
        private static float _lastTemplateZoomLog = -1f;
        private static string _lastTemplateZoomLogMode;

        /// <summary>
        /// Computes the zoomed main-camera FOV from a fixed baseline of 50°.
        ///
        /// Priority chain:
        ///   1. Template zoom from SightComponent (ground truth — matches HUD "xN")
        ///   2. ScopeCameraData.FieldOfView fallback (mag = 35 / fov)
        ///   3. Config ScopedFov manual fallback
        /// </summary>
        public static float ComputeZoomedFov()
        {
            if (!PiPDisablerPlugin.AutoFovFromScope.Value)
                return PiPDisablerPlugin.ScopedFov.Value;

            float magnification = GetEffectiveMagnification();
            if (magnification > 0.1f)
            {
                float oneXTargetFov = GetOneXTargetFov();
                float resultFov = magnification <= 1.01f
                    ? oneXTargetFov
                    : MagnificationToFov(magnification, ZoomBaselineFov);

                // Log on change
                string source = _lastLoggedSource ?? "?";
                if (Mathf.Abs(magnification - _lastLoggedMag) > 0.01f)
                {
                    _lastLoggedMag = magnification;
                    string mapping = magnification <= 1.01f
                        ? $"(1x override={oneXTargetFov:F1}°)"
                        : $"(baseline={ZoomBaselineFov:F0}°)";
                    PiPDisablerPlugin.LogInfo(
                        $"[FovController] mag={magnification:F2}x → mainFov={resultFov:F1}° " +
                        $"{mapping} [{source}]");
                }

                return resultFov;
            }

            return PiPDisablerPlugin.ScopedFov.Value;
        }

        /// <summary>
        /// Returns the current effective magnification from the best available source.
        /// Used by FovController.
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
            return PiPDisablerPlugin.DefaultZoom.Value;
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

        public static float GetOneXTargetFov()
        {
            var os = ScopeLifecycle.ActiveOptic;
            var player = PiPDisablerPlugin.GetLocalPlayer();
            var pwa = player?.ProceduralWeaponAnimation;

            if (os != null && TryGetCurrentTemplateZoomEntry(os, out float currentZoom, out int modeCount))
            {
                if (Mathf.Abs(currentZoom - 1f) <= 0.01f)
                {
                    // Single-entry 1x mode uses vanilla ADS offset behavior.
                    if (modeCount == 1 && pwa != null)
                        return Mathf.Max(1f, pwa.Single_2 - 15f);

                    // 1x inside a multi-mode stack stays fixed at optic FOV.
                    return 35f;
                }
            }

            if (pwa == null)
                return ZoomBaselineFov;

            return Mathf.Max(1f, pwa.Single_2);
        }

        private static bool TryGetCurrentTemplateZoomEntry(OpticSight os, out float zoom, out int modeCount)
        {
            zoom = 0f;
            modeCount = 0;

            if (os == null) return false;
            var sc = FindSightComponent(os);
            if (sc == null) return false;

            var state = GetCurrentScopeState(os);
            if (state.index < 0 || state.mode < 0) return false;

            var zooms = sc.Template?.Zooms;
            if (zooms == null || state.index >= zooms.Length) return false;

            var modeZooms = zooms[state.index];
            if (modeZooms == null) return false;

            modeCount = modeZooms.Length;
            if (state.mode >= modeCount) return false;

            zoom = modeZooms[state.mode];
            return true;
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
            // Reset dead-band so the next ApplyFov always fires
            _lastAppliedFov = 0f;
            _lastTemplateZoomLog = -1f;
            _lastTemplateZoomLogMode = null;
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

            var sc = FindSightComponent(os);
            if (sc == null) return 0f;

            try
            {
                // Variable zoom: interpolate between template min/max using ScopeZoomValue
                if (sc.AdjustableOpticData.IsAdjustableOptic)
                {
                    float minZoom = sc.GetMinOpticZoom();
                    float maxZoom = sc.GetMaxOpticZoom();
                    float zoomT   = Mathf.Clamp01(sc.ScopeZoomValue);

                    if (minZoom > 0.1f && maxZoom > 0.1f && maxZoom > minZoom)
                    {
                        float zoom = Mathf.Lerp(minZoom, maxZoom, zoomT);
                        LogTemplateZoomVerbose(zoom,
                            $"[FovController] Variable zoom: min={minZoom:F2}x max={maxZoom:F2}x " +
                            $"t={zoomT:F3} → {zoom:F2}x",
                            "variable");
                        return zoom;
                    }
                }

                // Stepped or fallback: use GetCurrentOpticZoom()
                float currentZoom = sc.GetCurrentOpticZoom();
                if (currentZoom > 0.1f)
                {
                    LogTemplateZoomVerbose(
                        currentZoom,
                        $"[FovController] Template zoom: {currentZoom:F2}x (stepped)",
                        "stepped");
                    return currentZoom;
                }
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogVerbose(
                    $"[FovController] GetTemplateZoom exception: {ex.Message}");
            }

            return 0f;
        }

        private static void LogTemplateZoomVerbose(float zoom, string message, string mode)
        {
            if (Mathf.Abs(zoom - _lastTemplateZoomLog) <= 0.01f &&
                string.Equals(mode, _lastTemplateZoomLogMode, StringComparison.Ordinal))
                return;

            _lastTemplateZoomLog = zoom;
            _lastTemplateZoomLogMode = mode;
            PiPDisablerPlugin.LogVerbose(message);
        }

        /// <summary>
        /// Returns the template min/max zoom for the current scope.
        /// for magnification bounds.
        /// Returns (minZoom, maxZoom). Returns (0,0) if not available.
        /// </summary>
        public static (float min, float max) GetTemplateZoomRange()
        {
            var os = ScopeLifecycle.ActiveOptic;
            if (os == null) return (0f, 0f);

            var sc = FindSightComponent(os);
            if (sc == null) return (0f, 0f);

            try
            {
                float minZ = sc.GetMinOpticZoom();
                float maxZ = sc.GetMaxOpticZoom();
                if (minZ > 0.1f && maxZ > 0.1f)
                    return (minZ, maxZ);
            }
            catch { }

            return (0f, 0f);
        }

        public static bool IsOpticAdjustable(OpticSight os)
        {
            if (os == null) return false;
            var sc = FindSightComponent(os);
            if (sc == null) return false;
            try { return sc.AdjustableOpticData.IsAdjustableOptic; }
            catch { return false; }
        }


        /// <summary>
        /// Returns the scope item _id from SightComponent.Item.Template.
        /// </summary>
        public static string GetOpticTemplateId(OpticSight os)
        {
            try
            {
                var t = FindSightComponent(os)?.Item?.Template;
                if (t == null) return "unknown";
                var id = t._id.ToString();
                return string.IsNullOrWhiteSpace(id) ? "unknown" : id;
            }
            catch { return "unknown"; }
        }

        /// <summary>
        /// Returns the scope item _name from SightComponent.Item.Template.
        /// </summary>
        public static string GetOpticTemplateName(OpticSight os)
        {
            try
            {
                var t = FindSightComponent(os)?.Item?.Template;
                if (t == null) return "unknown";
                return string.IsNullOrWhiteSpace(t._name) ? "unknown" : t._name;
            }
            catch { return "unknown"; }
        }

        /// <summary>
        /// Returns (SelectedScopeIndex, SelectedScopeMode) from the SightComponent
        /// for the given optic. Returns (-1, -1) if the values cannot be read.
        /// </summary>
        public static (int index, int mode) GetCurrentScopeState(OpticSight os)
        {
            var sc = FindSightComponent(os);
            if (sc == null) return (-1, -1);
            try { return (sc.SelectedScopeIndex, sc.SelectedScopeMode); }
            catch { return (-1, -1); }
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
        private static SightComponent FindSightComponent(OpticSight os)
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

                var result = _sightModProp.GetValue(smvc) as SightComponent;
                if (result != null)
                {
                    _cachedSightComponent = result;
                    _cachedSightComponentForOptic = os;
                }
                return result;
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogVerbose(
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
                            PiPDisablerPlugin.LogInfo(
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
            PiPDisablerPlugin.LogVerbose(
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
                            PiPDisablerPlugin.LogInfo(
                                $"[FovController] Discovered SightModVisualControllers via scan: " +
                                $"{type.FullName}, SightMod={prop.Name} ({prop.PropertyType.Name})");
                            return;
                        }
                    }
                }
                catch { }
            }

            PiPDisablerPlugin.LogWarn(
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
                        PiPDisablerPlugin.LogVerbose(
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
                        PiPDisablerPlugin.LogVerbose(
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
                PiPDisablerPlugin.LogVerbose(
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
                            PiPDisablerPlugin.LogInfo(
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
                        PiPDisablerPlugin.LogInfo(
                            $"[FovController] Discovered ScopeCameraData via scan: {type.FullName}");
                        return;
                    }
                }
                catch { }
            }

            PiPDisablerPlugin.LogInfo(
                "[FovController] ScopeCameraData type NOT found — fallback unavailable");
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private static bool IsOnSameModeAs(Transform candidate, Transform optic)
            => PiPDisablerPlugin.IsOnSameMode(candidate, optic);
    }
}
