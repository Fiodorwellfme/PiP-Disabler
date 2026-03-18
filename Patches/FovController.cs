using System;
using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace PiPDisabler
{
    /// <summary>
    /// Computes the zoomed FOV for the main camera from template zoom multipliers.
    ///
    /// Template.Zooms via SightComponent:
    ///   Stepped scopes:  SightComponent.GetCurrentOpticZoom()
    ///   Variable scopes: Lerp(GetMinOpticZoom(), GetMaxOpticZoom(), ScopeZoomValue)
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
        private static PropertyInfo _templateProp; // SightComponent.Template (item json)
        private static FieldInfo _templateField;   // fallback template field
        private static PropertyInfo _templateIdProp;
        private static FieldInfo _templateIdField;
        private static PropertyInfo _templateNameProp;
        private static FieldInfo _templateNameField;
        private static bool _sightComponentSearched;

        // Dedup logging
        private static float _lastLoggedMag;
        private static string _lastLoggedSource;

        /// <summary>
        /// Computes the zoomed main-camera FOV from a fixed baseline of 50°.
        ///
        /// Priority chain:
        ///   1. Template zoom from SightComponent (ground truth — matches HUD "xN")
        ///   2. Config ScopedFov manual fallback
        /// </summary>
        public static float ComputeZoomedFov()
        {
            if (!PiPDisablerPlugin.AutoFovFromScope.Value)
                return PiPDisablerPlugin.ScopedFov.Value;

            float magnification = GetEffectiveMagnification();
            if (magnification > 0.1f)
            {
                float resultFov = MagnificationToFov(magnification, ZoomBaselineFov);

                // Log on change
                string source = _lastLoggedSource ?? "?";
                if (Mathf.Abs(magnification - _lastLoggedMag) > 0.01f)
                {
                    _lastLoggedMag = magnification;
                    PiPDisablerPlugin.LogInfo(
                        $"[FovController] mag={magnification:F2}x → mainFov={resultFov:F1}° " +
                        $"(baseline={ZoomBaselineFov:F0}°) [{source}]");
                }

                return resultFov;
            }

            return PiPDisablerPlugin.ScopedFov.Value;
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

            // 2. Config default
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
                        PiPDisablerPlugin.LogVerbose(
                            $"[FovController] Variable zoom: min={minZoom:F2}x max={maxZoom:F2}x " +
                            $"t={zoomT:F3} → {zoom:F2}x");
                        return zoom;
                    }
                }

                // Stepped or fallback: use GetCurrentOpticZoom()
                float currentZoom = (float)_getCurrentZoom.Invoke(sightComponent, null);
                if (currentZoom > 0.1f)
                {
                    PiPDisablerPlugin.LogVerbose(
                        $"[FovController] Template zoom: {currentZoom:F2}x (stepped)");
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

        private static bool TryGetTemplateObject(OpticSight os, out object template)
        {
            template = null;
            if (os == null) return false;

            object sightComponent = FindSightComponent(os);
            if (sightComponent == null) return false;

            if (!_sightComponentSearched)
            {
                _sightComponentSearched = true;
                DiscoverSightComponentMembers(sightComponent.GetType());
            }

            try
            {
                if (_templateProp != null)
                    template = _templateProp.GetValue(sightComponent, null);
                else if (_templateField != null)
                    template = _templateField.GetValue(sightComponent);
            }
            catch { }

            return template != null;
        }

        /// <summary>
        /// Returns the scope template/item _id from SightComponent.Template (item json).
        /// </summary>
        public static string GetOpticTemplateId(OpticSight os)
        {
            if (!TryGetTemplateObject(os, out var template)) return "unknown";

            try
            {
                if (_templateIdProp != null)
                {
                    object id = _templateIdProp.GetValue(template, null);
                    if (id is string s && !string.IsNullOrWhiteSpace(s))
                        return s;
                }

                if (_templateIdField != null)
                {
                    object id = _templateIdField.GetValue(template);
                    if (id is string s && !string.IsNullOrWhiteSpace(s))
                        return s;
                }
            }
            catch { }

            return "unknown";
        }

        /// <summary>
        /// Returns the scope template/item _name from SightComponent.Template (item json).
        /// </summary>
        public static string GetOpticTemplateName(OpticSight os)
        {
            if (!TryGetTemplateObject(os, out var template)) return "unknown";

            try
            {
                if (_templateNameProp != null)
                {
                    object n = _templateNameProp.GetValue(template, null);
                    if (n is string s && !string.IsNullOrWhiteSpace(s))
                        return s;
                }

                if (_templateNameField != null)
                {
                    object n = _templateNameField.GetValue(template);
                    if (n is string s && !string.IsNullOrWhiteSpace(s))
                        return s;
                }
            }
            catch { }

            return "unknown";
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
        internal static object GetSightComponentForOptic(OpticSight os)
        {
            return FindSightComponent(os);
        }

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

            // Template JSON carrier used by GetCurrentOpticZoom internals.
            const BindingFlags anyFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            _templateProp = scType.GetProperty("Template", anyFlags)
                         ?? scType.GetProperty("ItemTemplate", anyFlags);
            _templateField = scType.GetField("Template", anyFlags)
                          ?? scType.GetField("ItemTemplate", anyFlags);

            Type templateType = _templateProp != null
                ? _templateProp.PropertyType
                : _templateField != null ? _templateField.FieldType : null;

            if (templateType != null)
            {
                _templateIdProp = templateType.GetProperty("_id", anyFlags)
                               ?? templateType.GetProperty("Id", anyFlags)
                               ?? templateType.GetProperty("TemplateId", anyFlags);
                _templateIdField = templateType.GetField("_id", anyFlags)
                                ?? templateType.GetField("Id", anyFlags)
                                ?? templateType.GetField("TemplateId", anyFlags);
                _templateNameProp = templateType.GetProperty("_name", anyFlags)
                                 ?? templateType.GetProperty("Name", anyFlags);
                _templateNameField = templateType.GetField("_name", anyFlags)
                                  ?? templateType.GetField("Name", anyFlags);
            }

            PiPDisablerPlugin.LogInfo(
                $"[FovController] SightComponent members: " +
                $"GetCurrentOpticZoom={_getCurrentZoom != null}, " +
                $"GetMinOpticZoom={_getMinZoom != null}, " +
                $"GetMaxOpticZoom={_getMaxZoom != null}, " +
                $"ScopeZoomValue={_scopeZoomValue != null}, " +
                $"AdjustableOpticData={_adjOpticData != null}, " +
                $"IsAdjustableOptic={_isAdjustable != null}, " +
                $"Template={_templateProp != null || _templateField != null}, " +
                $"TemplateId={_templateIdProp != null || _templateIdField != null}, " +
                $"TemplateName={_templateNameProp != null || _templateNameField != null}");
        }

    }
}
