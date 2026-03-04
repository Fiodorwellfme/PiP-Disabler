using System;
using System.Reflection;
using EFT.Animations;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Computes the ADS FOV for the MAIN player camera from optic template zoom data.
    ///
    /// Zoom calculation (physically correct):
    ///   resultFov = 2 * atan(tan(baseFov/2) / magnification)
    ///
    /// Magnification sources (priority):
    ///   1. SightComponent/template zoom state (ground truth used by HUD xN)
    ///   2. Scope camera FOV-derived magnification fallback (only when template zoom is unavailable)
    ///   3. Config ScopedFov fallback
    /// </summary>
    internal static class FovController
    {
        // Keep zoom math anchored to EFT's standard baseline, regardless of player settings.
        public const float ZoomBaselineFov = 50f;

        // Cache the discovered ScopeCameraData type and field
        private static Type _scopeCamDataType;
        private static FieldInfo _scopeCamDataFovField;
        private static bool _scopeCamDataSearched;

        // Cache last values so logs are readable (only when values/source change)
        private static float _lastLoggedMagnification;
        private static string _lastLoggedSource;

        /// <summary>
        /// Computes the zoomed FOV from a fixed baseline FOV (50°).
        /// </summary>
        public static float ComputeZoomedFov(float baseFov, ProceduralWeaponAnimation pwa)
        {
            _ = baseFov;
            float effectiveBaseFov = ZoomBaselineFov;

            if (ScopeHousingMeshSurgeryPlugin.AutoFovFromScope.Value)
            {
                float magnification = GetTemplateMagnification(pwa, out string magSource);

                // Fallback allowed only if template-based magnification isn't available.
                if (magnification <= 0.01f)
                {
                    float overrideFov = ZoomController.GetEffectiveScopeFov();
                    float scopeFov = overrideFov > 0.1f ? overrideFov : GetScopeFov();
                    if (scopeFov > 0.1f)
                    {
                        magnification = 35f / scopeFov;
                        magSource = overrideFov > 0.1f
                            ? "Fallback ScopeFOV (scroll override)"
                            : "Fallback ScopeFOV (scope camera)";
                    }
                }

                if (magnification > 0.01f)
                {
                    float baseHalfRad = (effectiveBaseFov * Mathf.Deg2Rad) * 0.5f;
                    float resultFovRad = 2f * Mathf.Atan(Mathf.Tan(baseHalfRad) / magnification);
                    float resultFov = resultFovRad * Mathf.Rad2Deg;

                    if (Mathf.Abs(magnification - _lastLoggedMagnification) > 0.005f ||
                        !string.Equals(_lastLoggedSource, magSource, StringComparison.Ordinal))
                    {
                        _lastLoggedMagnification = magnification;
                        _lastLoggedSource = magSource;
                        ScopeHousingMeshSurgeryPlugin.LogInfo(
                            $"[FovController] mag={magnification:F3}x source={magSource} -> mainFov={resultFov:F3} (baseline={effectiveBaseFov:F1})");
                    }

                    return resultFov;
                }

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    "[FovController] Failed to resolve template magnification and scope FOV fallback; using config ScopedFov");
            }

            return ScopeHousingMeshSurgeryPlugin.ScopedFov.Value;
        }

        /// <summary>
        /// Called on mode switch to reset cached logging so next computation logs.
        /// </summary>
        public static void OnModeSwitch()
        {
            _lastLoggedMagnification = 0f;
            _lastLoggedSource = null;
        }

        private static float GetTemplateMagnification(ProceduralWeaponAnimation pwa, out string source)
        {
            source = "none";

            if (pwa == null)
            {
                source = "PWA null";
                return 0f;
            }

            object sightComponent = null;
            try
            {
                sightComponent = pwa.CurrentAimingMod;
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[FovController] Failed to access pwa.CurrentAimingMod: {ex.Message}");
            }

            if (sightComponent == null)
            {
                source = "CurrentAimingMod null";
                return 0f;
            }

            try
            {
                var sightType = sightComponent.GetType();

                float directZoom = InvokeFloatMethod(sightComponent, "GetCurrentOpticZoom");
                if (directZoom > 0.01f)
                {
                    float smoothZoom = ReadFloatMember(sightComponent, "ScopeZoomValue");
                    float minZoom = InvokeFloatMethod(sightComponent, "GetMinOpticZoom");
                    float maxZoom = InvokeFloatMethod(sightComponent, "GetMaxOpticZoom");

                    if (smoothZoom > 0.01f)
                    {
                        if (minZoom > 0.01f && maxZoom > 0.01f)
                        {
                            float low = Mathf.Min(minZoom, maxZoom);
                            float high = Mathf.Max(minZoom, maxZoom);

                            // If ScopeZoomValue itself is already in zoom units, use it directly.
                            if (smoothZoom >= low - 0.001f && smoothZoom <= high + 0.001f)
                            {
                                source = $"SightComponent.ScopeZoomValue direct [{sightType.Name}]";
                                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                                    $"[FovController] smooth direct zoom={smoothZoom:F3} range=[{low:F3},{high:F3}] currentStep={directZoom:F3}");
                                return smoothZoom;
                            }

                            // Common alt representation: normalized [0..1].
                            if (smoothZoom >= -0.001f && smoothZoom <= 1.001f)
                            {
                                float lerped = Mathf.Lerp(low, high, Mathf.Clamp01(smoothZoom));
                                source = $"SightComponent.ScopeZoomValue normalized [{sightType.Name}]";
                                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                                    $"[FovController] smooth normalized={smoothZoom:F3} -> zoom={lerped:F3} range=[{low:F3},{high:F3}] currentStep={directZoom:F3}");
                                return lerped;
                            }
                        }

                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[FovController] ScopeZoomValue present but not mappable: {smoothZoom:F3} (min={minZoom:F3}, max={maxZoom:F3})");
                    }

                    source = $"SightComponent.GetCurrentOpticZoom [{sightType.Name}]";
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        $"[FovController] using GetCurrentOpticZoom={directZoom:F3}");
                    return directZoom;
                }

                float templateZoom = TryGetTemplateZoomFromArrays(sightComponent, out string templateDetails);
                if (templateZoom > 0.01f)
                {
                    source = $"Template.Zooms {templateDetails}";
                    return templateZoom;
                }

                source = $"SightComponent no zoom data [{sightType.Name}]";
                return 0f;
            }
            catch (Exception ex)
            {
                source = $"Exception resolving template magnification: {ex.Message}";
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[FovController] GetTemplateMagnification exception: {ex}");
                return 0f;
            }
        }

        private static float InvokeFloatMethod(object instance, string methodName)
        {
            if (instance == null) return 0f;
            try
            {
                var m = instance.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                if (m == null || m.ReturnType != typeof(float)) return 0f;
                return (float)m.Invoke(instance, null);
            }
            catch
            {
                return 0f;
            }
        }

        private static float ReadFloatMember(object instance, string memberName)
        {
            if (instance == null) return 0f;
            try
            {
                var t = instance.GetType();
                var p = t.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && p.PropertyType == typeof(float))
                    return (float)p.GetValue(instance);

                var f = t.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(float))
                    return (float)f.GetValue(instance);
            }
            catch { }
            return 0f;
        }

        private static int ReadIntMember(object instance, string memberName)
        {
            if (instance == null) return -1;
            try
            {
                var t = instance.GetType();
                var p = t.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && p.PropertyType == typeof(int))
                    return (int)p.GetValue(instance);

                var f = t.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(int))
                    return (int)f.GetValue(instance);
            }
            catch { }
            return -1;
        }

        private static float TryGetTemplateZoomFromArrays(object sightComponent, out string details)
        {
            details = "";
            if (sightComponent == null) return 0f;

            try
            {
                var t = sightComponent.GetType();
                object template = null;

                var templateProp = t.GetProperty("Template", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (templateProp != null)
                    template = templateProp.GetValue(sightComponent);

                if (template == null)
                {
                    var templateField = t.GetField("Template", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (templateField != null)
                        template = templateField.GetValue(sightComponent);
                }

                if (template == null)
                {
                    details = "(template null)";
                    return 0f;
                }

                var templateType = template.GetType();
                var zoomsProp = templateType.GetProperty("Zooms", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (zoomsProp == null)
                {
                    details = "(template missing Zooms)";
                    return 0f;
                }

                var zooms = zoomsProp.GetValue(template) as Array;
                if (zooms == null || zooms.Length == 0)
                {
                    details = "(template Zooms empty)";
                    return 0f;
                }

                int scopeIndex = ReadIntMember(sightComponent, "SelectedScope");
                if (scopeIndex < 0)
                    scopeIndex = ReadIntMember(sightComponent, "SelectedScopeIndex");
                if (scopeIndex < 0)
                    scopeIndex = 0;

                scopeIndex = Mathf.Clamp(scopeIndex, 0, zooms.Length - 1);

                object scopeModesObj = zooms.GetValue(scopeIndex);
                var scopeModes = scopeModesObj as Array;
                if (scopeModes == null || scopeModes.Length == 0)
                {
                    details = $"(Zooms[{scopeIndex}] empty)";
                    return 0f;
                }

                int modeIndex = ReadIntMember(sightComponent, "SelectedScopeMode");
                if (modeIndex < 0)
                {
                    // fallback: ScopesSelectedModes[selectedScope]
                    var modesProp = t.GetProperty("ScopesSelectedModes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    int[] modes = null;
                    if (modesProp != null)
                        modes = modesProp.GetValue(sightComponent) as int[];
                    if (modes == null)
                    {
                        var modesField = t.GetField("ScopesSelectedModes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (modesField != null)
                            modes = modesField.GetValue(sightComponent) as int[];
                    }

                    if (modes != null && scopeIndex >= 0 && scopeIndex < modes.Length)
                        modeIndex = modes[scopeIndex];
                }

                if (modeIndex < 0)
                    modeIndex = 0;

                modeIndex = Mathf.Clamp(modeIndex, 0, scopeModes.Length - 1);
                object zoomObj = scopeModes.GetValue(modeIndex);

                if (zoomObj is float zoom && zoom > 0.01f)
                {
                    details = $"[{templateType.Name}] scope={scopeIndex} mode={modeIndex}";
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        $"[FovController] Template.Zooms scope={scopeIndex} mode={modeIndex} -> {zoom:F3}");
                    return zoom;
                }

                details = $"(Zooms[{scopeIndex}][{modeIndex}] invalid)";
            }
            catch (Exception ex)
            {
                details = $"(template reflection error: {ex.Message})";
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[FovController] TryGetTemplateZoomFromArrays failed: {ex.Message}");
            }

            return 0f;
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
