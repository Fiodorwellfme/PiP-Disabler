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
    /// Magnification sources (in priority order):
    ///   1. SightComponent.Template.Zooms (+ smooth interpolation when possible)
    ///   2. SightComponent.GetCurrentOpticZoom()
    ///   3. ScopeCameraData.FieldOfView / ScopeZoomHandler as last resort
    ///   4. Keep previous applied value
    /// </summary>
    internal static class FovController
    {
        // Keep zoom math anchored to EFT's standard baseline, regardless of player settings.
        public const float ZoomBaselineFov = 50f;

        // Cache the discovered ScopeCameraData type and field
        private static Type _scopeCamDataType;
        private static FieldInfo _scopeCamDataFovField;
        private static bool _scopeCamDataSearched;

        // Cache last applied values; used for "keep previous" behavior on bad data.
        private static float _lastAppliedMagnification;
        private static float _lastAppliedAdsFov;
        private static string _lastSource;

        /// <summary>
        /// Computes the zoomed FOV from a fixed baseline FOV (50°).
        /// </summary>
        public static float ComputeZoomedFov(float baseFov, ProceduralWeaponAnimation pwa)
        {
            _ = baseFov;
            float effectiveBaseFov = ZoomBaselineFov;

            if (ScopeHousingMeshSurgeryPlugin.AutoFovFromScope.Value)
            {
                if (TryGetMagnification(pwa, out float magnification, out string source))
                {
                    float baseFovRad = effectiveBaseFov * Mathf.Deg2Rad;
                    float resultFovRad = 2f * Mathf.Atan2(Mathf.Tan(baseFovRad * 0.5f), magnification);
                    float resultFov = resultFovRad * Mathf.Rad2Deg;

                    bool changed = Mathf.Abs(resultFov - _lastAppliedAdsFov) > 0.01f
                                || Mathf.Abs(magnification - _lastAppliedMagnification) > 0.01f
                                || !string.Equals(source, _lastSource, StringComparison.Ordinal);
                    if (changed)
                    {
                        _lastAppliedMagnification = magnification;
                        _lastAppliedAdsFov = resultFov;
                        _lastSource = source;
                        ScopeHousingMeshSurgeryPlugin.LogInfo(
                            $"[FovController] src={source} mag={magnification:F3}x → mainFov={resultFov:F2}° (base={effectiveBaseFov:F0}°)");
                    }

                    return resultFov;
                }

                // Keep previous applied ADS FOV if we temporarily fail to resolve zoom.
                if (_lastAppliedAdsFov > 0.5f)
                    return _lastAppliedAdsFov;
            }

            return ScopeHousingMeshSurgeryPlugin.ScopedFov.Value;
        }

        /// <summary>
        /// Called on mode switch to reset the cached FOV so the next computation logs.
        /// </summary>
        public static void OnModeSwitch()
        {
            _lastSource = null;
        }

        public static void OnScopeExit()
        {
            _lastAppliedMagnification = 0f;
            _lastAppliedAdsFov = 0f;
            _lastSource = null;
        }

        private static bool TryGetMagnification(ProceduralWeaponAnimation pwa, out float magnification, out string source)
        {
            magnification = 0f;
            source = null;

            var sightComp = GetCurrentSightComponent(pwa);

            // Priority 1: Template.Zooms (+ smooth interpolation)
            if (TryGetTemplateZoomMagnification(pwa, sightComp, out magnification, out source))
                return true;

            // Priority 2: SightComponent.GetCurrentOpticZoom()
            if (TryGetSightComponentCurrentZoom(sightComp, out magnification))
            {
                source = "SightComponent.GetCurrentOpticZoom";
                return true;
            }

            // Priority 3: Scope camera FOV-derived magnification fallback
            float overrideFov = ZoomController.GetEffectiveScopeFov();
            float scopeFov = overrideFov > 0.1f ? overrideFov : GetScopeFov();
            if (scopeFov > 0.1f)
            {
                magnification = 35f / scopeFov;
                source = overrideFov > 0.1f ? "ScrollOverrideScopeFov" : "ScopeCameraData.FieldOfView";
                return true;
            }

            return false;
        }

        private static object GetCurrentSightComponent(ProceduralWeaponAnimation pwa)
        {
            try
            {
                if (pwa == null) return null;
                var prop = pwa.GetType().GetProperty("CurrentAimingMod", BindingFlags.Public | BindingFlags.Instance);
                return prop?.GetValue(pwa, null);
            }
            catch { return null; }
        }

        private static bool TryGetSightComponentCurrentZoom(object sightComp, out float magnification)
        {
            magnification = 0f;
            if (sightComp == null) return false;
            try
            {
                var method = sightComp.GetType().GetMethod("GetCurrentOpticZoom", BindingFlags.Public | BindingFlags.Instance);
                if (method == null) return false;
                float value = (float)method.Invoke(sightComp, null);
                if (value > 0.01f)
                {
                    magnification = value;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool TryGetTemplateZoomMagnification(ProceduralWeaponAnimation pwa, object sightComp, out float magnification, out string source)
        {
            magnification = 0f;
            source = null;
            if (sightComp == null) return false;

            try
            {
                var templateObj = GetMemberValue<object>(sightComp, "Template", null);
                if (templateObj == null) return false;

                var zoomsObj = GetMemberValue<object>(templateObj, "Zooms", null);
                var zooms = zoomsObj as Array;
                if (zooms == null || zooms.Length == 0) return false;

                int selectedScope = GetCurrentScopeIndexFromPwa(pwa);
                if (selectedScope < 0)
                    selectedScope = GetIntPropertyValue(sightComp, "SelectedScope", GetIntPropertyValue(sightComp, "SelectedScopeIndex", 0));
                if (selectedScope < 0 || selectedScope >= zooms.Length) return false;

                var scopeModesArray = zooms.GetValue(selectedScope) as Array;
                if (scopeModesArray == null || scopeModesArray.Length == 0) return false;

                int pwaScopeMode = GetCurrentScopeModeFromPwa(pwa);
                int selectedMode = pwaScopeMode;
                if (selectedMode < 0)
                    selectedMode = GetSelectedScopeMode(sightComp, selectedScope);
                if (selectedMode < 0 || selectedMode >= scopeModesArray.Length)
                {
                    // Stepped edge-case handling: keep previous applied value if available.
                    if (_lastAppliedMagnification > 0.01f)
                    {
                        magnification = _lastAppliedMagnification;
                        source = "Template.Zooms(previous-mode-keep)";
                        return true;
                    }
                    return false;
                }

                float modeZoom = Convert.ToSingle(scopeModesArray.GetValue(selectedMode));
                if (modeZoom <= 0.01f) return false;

                if (TryGetSmoothInterpolatedZoom(sightComp, templateObj, scopeModesArray, out float smoothMag))
                {
                    magnification = smoothMag;
                    source = "Template.Zooms(interpolated)";
                    return true;
                }

                magnification = modeZoom;
                source = selectedMode == pwaScopeMode
                    ? "Template.Zooms(current-scope)"
                    : "Template.Zooms(selected-mode)";
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetSmoothInterpolatedZoom(object sightComp, object templateObj, Array scopeModesArray, out float smoothMag)
        {
            smoothMag = 0f;
            if (scopeModesArray == null || scopeModesArray.Length < 2) return false;

            try
            {
                bool isAdjustable = GetBoolPropertyValue(templateObj, "IsAdjustableOptic", false);
                if (!isAdjustable) return false;

                float minZoom = float.MaxValue;
                float maxZoom = float.MinValue;
                for (int i = 0; i < scopeModesArray.Length; i++)
                {
                    float z = Convert.ToSingle(scopeModesArray.GetValue(i));
                    if (z <= 0.01f) continue;
                    if (z < minZoom) minZoom = z;
                    if (z > maxZoom) maxZoom = z;
                }
                if (minZoom == float.MaxValue || maxZoom == float.MinValue || maxZoom <= minZoom)
                    return false;

                Vector3 minMaxFov = GetMemberValue(templateObj, "MinMaxFov", Vector3.zero);
                if (minMaxFov.x <= 0.01f || minMaxFov.y <= 0.01f || Mathf.Abs(minMaxFov.y - minMaxFov.x) < 0.0001f)
                    return false;

                float scopeZoomValue = GetFloatPropertyValue(sightComp, "ScopeZoomValue", float.NaN);
                if (float.IsNaN(scopeZoomValue) || scopeZoomValue <= 0.01f)
                    return false;

                // FOV gets smaller as magnification increases.
                float t = Mathf.InverseLerp(minMaxFov.x, minMaxFov.y, scopeZoomValue);
                smoothMag = Mathf.Lerp(minZoom, maxZoom, t);
                return smoothMag > 0.01f;
            }
            catch
            {
                return false;
            }
        }

        private static int GetSelectedScopeMode(object sightComp, int selectedScope)
        {
            try
            {
                var arrObj = GetMemberValue<object>(sightComp, "ScopesSelectedModes", null);
                if (arrObj is int[] arr)
                {
                    if (selectedScope >= 0 && selectedScope < arr.Length)
                        return arr[selectedScope];
                }

                return GetIntPropertyValue(sightComp, "SelectedScopeMode", 0);
            }
            catch { return 0; }
        }

        private static int GetIntPropertyValue(object obj, string propertyName, int fallback)
        {
            try
            {
                return Convert.ToInt32(GetMemberValue<object>(obj, propertyName, fallback));
            }
            catch { }
            return fallback;
        }

        private static float GetFloatPropertyValue(object obj, string propertyName, float fallback)
        {
            try
            {
                return Convert.ToSingle(GetMemberValue<object>(obj, propertyName, fallback));
            }
            catch { }
            return fallback;
        }

        private static bool GetBoolPropertyValue(object obj, string propertyName, bool fallback)
        {
            try
            {
                return Convert.ToBoolean(GetMemberValue<object>(obj, propertyName, fallback));
            }
            catch { }
            return fallback;
        }

        private static int GetCurrentScopeIndexFromPwa(ProceduralWeaponAnimation pwa)
        {
            try
            {
                var currentScope = GetMemberValue<object>(pwa, "CurrentScope", null);
                if (currentScope == null) return -1;

                // SPT 4.x decomp refs: FirearmScopeStateStruct.ScopeIndexInsideSight
                // and PWA.CurrentScope carries this runtime context.
                int idx = GetIntPropertyValue(currentScope, "ScopeIndexInsideSight", -1);
                if (idx >= 0) return idx;

                return GetIntPropertyValue(currentScope, "ScopeIndex", -1);
            }
            catch { return -1; }
        }

        private static int GetCurrentScopeModeFromPwa(ProceduralWeaponAnimation pwa)
        {
            try
            {
                var currentScope = GetMemberValue<object>(pwa, "CurrentScope", null);
                if (currentScope == null) return -1;

                int mode = GetIntPropertyValue(currentScope, "ScopeMode", -1);
                if (mode >= 0) return mode;

                return GetIntPropertyValue(currentScope, "ScopeModeIndex", -1);
            }
            catch { return -1; }
        }

        private static T GetMemberValue<T>(object obj, string memberName, T fallback)
        {
            if (obj == null || string.IsNullOrEmpty(memberName)) return fallback;
            try
            {
                var type = obj.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                var prop = type.GetProperty(memberName, flags);
                if (prop != null)
                {
                    object value = prop.GetValue(obj, null);
                    if (value is T v) return v;
                    if (value != null) return (T)Convert.ChangeType(value, typeof(T));
                }

                var field = type.GetField(memberName, flags);
                if (field != null)
                {
                    object value = field.GetValue(obj);
                    if (value is T v) return v;
                    if (value != null) return (T)Convert.ChangeType(value, typeof(T));
                }
            }
            catch { }

            return fallback;
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
                    if (fov > 0.1f) return fov;
                }
            }
            catch { }

            // === Try 2: ScopeCameraData component via reflection scan ===
            float scdFov = GetFovFromScopeCameraData(os);
            if (scdFov > 0.1f) return scdFov;

            // === Try 3: Brute-force scan all MonoBehaviours for FieldOfView field ===
            float bruteFov = BruteForceFovSearch(os);
            if (bruteFov > 0.1f) return bruteFov;

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
