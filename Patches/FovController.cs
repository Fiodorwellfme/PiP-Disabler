using System;
using System.Linq;
using System.Reflection;
using EFT.CameraControl;
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
        public const float ZoomBaselineFov = 50f;
        public const float OneXFovOffset = 15f;

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
        // SightComponent.Item access
        private static PropertyInfo _itemProp;     // SightComponent.Item
        private static FieldInfo _itemField;
        // Item.Template path: lazily discovered on first access (concrete runtime type)
        private static bool _itemTemplateSearched;
        private static PropertyInfo _itemTemplateProp;
        private static FieldInfo _itemTemplateField;
        private static PropertyInfo _itemTemplateIdProp;
        private static FieldInfo _itemTemplateIdField;
        private static PropertyInfo _itemTemplateNameProp;
        private static FieldInfo _itemTemplateNameField;
        private static object _cachedItemTemplate;
        // Scope state: SelectedScopeIndex and SelectedScopeMode
        private static PropertyInfo _selectedScopeIndexProp;
        private static PropertyInfo _selectedScopeModeProp;
        private static bool _sightComponentSearched;

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
                        ? $"(1x override={oneXTargetFov:F1}° base-15)"
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
            var player = PiPDisablerPlugin.GetLocalPlayer();
            var pwa = player?.ProceduralWeaponAnimation;
            if (pwa == null)
                return ZoomBaselineFov - OneXFovOffset;

            return Mathf.Max(1f, pwa.Single_2 - OneXFovOffset);
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
            // Clear item template cache; the item reference may change between scopes
            _cachedItemTemplate = null;
            _itemTemplateSearched = false;
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
                        LogTemplateZoomVerbose(zoom,
                            $"[FovController] Variable zoom: min={minZoom:F2}x max={maxZoom:F2}x " +
                            $"t={zoomT:F3} → {zoom:F2}x",
                            "variable");
                        return zoom;
                    }
                }

                // Stepped or fallback: use GetCurrentOpticZoom()
                float currentZoom = (float)_getCurrentZoom.Invoke(sightComponent, null);
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

        /// <summary>
        /// Checks whether <paramref name="templateValue"/> is an item JSON template by
        /// looking for a <c>_id</c> member on its concrete type. If found, outputs all
        /// cached reflection members and logs the result.
        /// </summary>
        private static bool TryBindItemTemplateMembers(
            object templateValue,
            PropertyInfo sourceProp,
            FieldInfo sourceField,
            BindingFlags anyFlags,
            out PropertyInfo idProp, out FieldInfo idField,
            out PropertyInfo nameProp, out FieldInfo nameField)
        {
            idProp = null; idField = null;
            nameProp = null; nameField = null;
            if (templateValue == null) return false;

            var tType = templateValue.GetType();
            idProp = tType.GetProperty("_id", anyFlags)
                  ?? tType.GetProperty("Id", anyFlags)
                  ?? tType.GetProperty("TemplateId", anyFlags);
            idField = idProp == null
                ? (tType.GetField("_id", anyFlags)
                   ?? tType.GetField("Id", anyFlags)
                   ?? tType.GetField("TemplateId", anyFlags))
                : null;

            if (idProp == null && idField == null) return false;

            nameProp = tType.GetProperty("_name", anyFlags)
                    ?? tType.GetProperty("Name", anyFlags);
            nameField = nameProp == null
                ? (tType.GetField("_name", anyFlags)
                   ?? tType.GetField("Name", anyFlags))
                : null;

            string accessor = sourceProp != null ? $"prop:{sourceProp.Name}" : $"field:{sourceField?.Name}";
            PiPDisablerPlugin.LogVerbose(
                $"[FovController] ItemTemplate members: " +
                $"_id={idProp != null || idField != null}, " +
                $"_name={nameProp != null || nameField != null} " +
                $"(tType={tType.Name} via {accessor})");
            return true;
        }

        /// <summary>
        /// Returns the scope item template (_id / _name carrier) by walking
        /// SightComponent.Item → item.Template.  Lazily caches all reflection
        /// members on the concrete runtime type because EFT obfuscates class names.
        /// Uses GetProperties (plural) to avoid AmbiguousMatchException when the
        /// item class declares multiple "Template" properties.
        /// </summary>
        private static bool TryGetItemTemplateObject(OpticSight os, out object itemTemplate)
        {
            itemTemplate = null;
            if (os == null) return false;

            if (_cachedItemTemplate != null)
            {
                itemTemplate = _cachedItemTemplate;
                return true;
            }

            object sc = FindSightComponent(os);
            if (sc == null)
            {
                PiPDisablerPlugin.LogVerbose("[FovController] TryGetItemTemplate: sc=null");
                return false;
            }

            if (!_sightComponentSearched)
            {
                _sightComponentSearched = true;
                DiscoverSightComponentMembers(sc.GetType());
            }

            const BindingFlags anyFlags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // Step 1: SightComponent.Item
            object item = null;
            try
            {
                if (_itemProp != null)
                    item = _itemProp.GetValue(sc, null);
                else if (_itemField != null)
                    item = _itemField.GetValue(sc);
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogVerbose(
                    $"[FovController] TryGetItemTemplate: Item getter threw {ex.GetType().Name}: {ex.Message}");
            }

            if (item == null)
            {
                PiPDisablerPlugin.LogVerbose(
                    $"[FovController] TryGetItemTemplate: item=null " +
                    $"(itemProp={_itemProp?.Name ?? "none"}, scType={sc.GetType().Name})");
                return false;
            }

            // Steps 2+3: Find the Template property whose runtime value has _id.
            // Use GetProperties (plural) to avoid AmbiguousMatchException when
            // AssaultScopeItemClass has multiple "Template" properties (zoom data
            // vs item JSON template).  Evaluate each at runtime and keep the one
            // that carries _id/_name.
            if (!_itemTemplateSearched)
            {
                _itemTemplateSearched = true;
                var itemType = item.GetType();

                // Check all properties named "Template" or "ItemTemplate"
                foreach (var prop in itemType.GetProperties(anyFlags)
                    .Where(p => p.Name == "Template" || p.Name == "ItemTemplate"))
                {
                    try
                    {
                        var val = prop.GetValue(item, null);
                        if (val == null) continue;
                        if (TryBindItemTemplateMembers(val, prop, null, anyFlags,
                                out var tId, out var tIdF, out var tNm, out var tNmF))
                        {
                            _itemTemplateProp    = prop;
                            _itemTemplateIdProp  = tId;
                            _itemTemplateIdField = tIdF;
                            _itemTemplateNameProp  = tNm;
                            _itemTemplateNameField = tNmF;
                            _cachedItemTemplate = val;
                            itemTemplate = val;
                            return true;
                        }
                    }
                    catch { }
                }

                // Check fields as well
                foreach (var fld in itemType.GetFields(anyFlags)
                    .Where(f => f.Name == "Template" || f.Name == "ItemTemplate"))
                {
                    try
                    {
                        var val = fld.GetValue(item);
                        if (val == null) continue;
                        if (TryBindItemTemplateMembers(val, null, fld, anyFlags,
                                out var tId, out var tIdF, out var tNm, out var tNmF))
                        {
                            _itemTemplateField   = fld;
                            _itemTemplateIdProp  = tId;
                            _itemTemplateIdField = tIdF;
                            _itemTemplateNameProp  = tNm;
                            _itemTemplateNameField = tNmF;
                            _cachedItemTemplate = val;
                            itemTemplate = val;
                            return true;
                        }
                    }
                    catch { }
                }

                PiPDisablerPlugin.LogVerbose(
                    $"[FovController] TryGetItemTemplate: no Template+_id found on {itemType.Name}");
            }

            return false;
        }

        /// <summary>
        /// Returns the scope item _id from SightComponent.Item.Template.
        /// </summary>
        public static string GetOpticTemplateId(OpticSight os)
        {
            if (!TryGetItemTemplateObject(os, out var template)) return "unknown";

            try
            {
                if (_itemTemplateIdProp != null)
                {
                    object id = _itemTemplateIdProp.GetValue(template, null);
                    if (id != null) { string s = id.ToString(); if (!string.IsNullOrWhiteSpace(s)) return s; }
                }

                if (_itemTemplateIdField != null)
                {
                    object id = _itemTemplateIdField.GetValue(template);
                    if (id != null) { string s = id.ToString(); if (!string.IsNullOrWhiteSpace(s)) return s; }
                }
            }
            catch { }

            return "unknown";
        }

        /// <summary>
        /// Returns the scope item _name from SightComponent.Item.Template.
        /// </summary>
        public static string GetOpticTemplateName(OpticSight os)
        {
            if (!TryGetItemTemplateObject(os, out var template)) return "unknown";

            try
            {
                if (_itemTemplateNameProp != null)
                {
                    object n = _itemTemplateNameProp.GetValue(template, null);
                    if (n != null) { string s = n.ToString(); if (!string.IsNullOrWhiteSpace(s)) return s; }
                }

                if (_itemTemplateNameField != null)
                {
                    object n = _itemTemplateNameField.GetValue(template);
                    if (n != null) { string s = n.ToString(); if (!string.IsNullOrWhiteSpace(s)) return s; }
                }
            }
            catch { }

            return "unknown";
        }

        /// <summary>
        /// Returns (SelectedScopeIndex, SelectedScopeMode) from the SightComponent
        /// for the given optic. Returns (-1, -1) if the values cannot be read.
        /// </summary>
        public static (int index, int mode) GetCurrentScopeState(OpticSight os)
        {
            object sc = FindSightComponent(os);
            if (sc == null) return (-1, -1);

            if (!_sightComponentSearched)
            {
                _sightComponentSearched = true;
                DiscoverSightComponentMembers(sc.GetType());
            }

            try
            {
                int idx = _selectedScopeIndexProp != null
                    ? (int)_selectedScopeIndexProp.GetValue(sc, null)
                    : -1;
                int mode = _selectedScopeModeProp != null
                    ? (int)_selectedScopeModeProp.GetValue(sc, null)
                    : -1;
                return (idx, mode);
            }
            catch { }

            return (-1, -1);
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

            // Item.Template fallback: SightComponent.Item → item.Template
            const BindingFlags anyFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            _itemProp = scType.GetProperty("Item", anyFlags);
            _itemField = scType.GetField("Item", anyFlags);

            // Scope state properties for scope-mode bypass evaluation
            _selectedScopeIndexProp = scType.GetProperty("SelectedScopeIndex", anyFlags);
            _selectedScopeModeProp  = scType.GetProperty("SelectedScopeMode",  anyFlags);

            PiPDisablerPlugin.LogInfo(
                $"[FovController] SightComponent members: " +
                $"GetCurrentOpticZoom={_getCurrentZoom != null}, " +
                $"GetMinOpticZoom={_getMinZoom != null}, " +
                $"GetMaxOpticZoom={_getMaxZoom != null}, " +
                $"ScopeZoomValue={_scopeZoomValue != null}, " +
                $"AdjustableOpticData={_adjOpticData != null}, " +
                $"IsAdjustableOptic={_isAdjustable != null}, " +
                $"Item={_itemProp != null || _itemField != null}, " +
                $"SelectedScopeIndex={_selectedScopeIndexProp != null}, " +
                $"SelectedScopeMode={_selectedScopeModeProp != null}");
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
