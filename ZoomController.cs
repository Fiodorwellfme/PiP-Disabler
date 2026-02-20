using System;
using EFT.CameraControl;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Zoom state and zoom math helper.
    ///
    /// Shader-based zoom has been removed; this controller now manages
    /// scroll-zoom/FOV data only so variable scopes keep working.
    /// </summary>
    internal static class ZoomController
    {
        private static bool _shaderLoaded;
        private static bool _loadAttempted;

        // Per-scope state
        private static Renderer _activeLensRenderer;
        private static bool _isActive;

        /// <summary>
        /// The lens renderer currently managed by ZoomController (shader zoom target).
        /// LensTransparency uses this to exclude it from per-frame mesh killing.
        /// </summary>
        public static Renderer ActiveLensRenderer => _isActive ? _activeLensRenderer : null;

        // Scroll zoom override — when active, overrides the scope's native FOV.
        // Works in FOV space directly to avoid fake magnification numbers.
        private static float _scrollZoomFov;      // 0 = not active, >0 = user-set scope FOV
        private static bool  _scrollZoomActive;
        private static float _nativeFov = 35f;    // the scope's current native FOV (baseline)
        private static float _nativeMinFov = 1f;  // scope's min FOV (max zoom) from ScopeZoomHandler
        private static float _nativeMaxFov = 35f;  // scope's max FOV (min zoom) from ScopeZoomHandler
        private static bool  _fovRangeDiscovered;  // true once DiscoverFovRange has run for this scope
        private static bool  _isVariableZoom;      // true if ScopeZoomHandler was found (has zoom ring)
        private static float _lastNativeFovForSwitchDetect; // tracks native FOV changes while scroll override is active

        /// <summary>True if the shader AssetBundle was loaded successfully.</summary>
        public static bool ShaderAvailable => _shaderLoaded;

        /// <summary>True if zoom is currently applied to a lens.</summary>
        public static bool IsActive => _isActive;

        /// <summary>
        /// Shader zoom is disabled intentionally.
        /// Kept as an init hook so callers don't need conditional logic changes.
        /// </summary>
        public static void LoadShader()
        {
            if (_loadAttempted) return;
            _loadAttempted = true;

            _shaderLoaded = false;
            ScopeHousingMeshSurgeryPlugin.LogInfo(
                "[ZoomController] Shader zoom is disabled. Using FOV zoom path.");
        }

        /// <summary>
        /// Legacy shader-zoom entry point kept for API compatibility.
        /// Shader zoom was removed, so this now acts as a safe no-op.
        /// </summary>
        public static void Apply(OpticSight os, float magnification)
        {
            // No-op: shader path removed.
            _isActive = false;
            _activeLensRenderer = null;
        }

        /// <summary>
        /// Update magnification level. Called per-frame for variable zoom scopes.
        /// </summary>
        public static void SetZoom(float magnification)
        {
            // No-op: shader path removed.
        }

        /// <summary>
        /// Legacy shader-zoom cleanup hook; currently a no-op plus scroll reset.
        /// </summary>
        public static void Restore()
        {
            _activeLensRenderer = null;
            _isActive = false;

            // Reset scroll zoom on scope exit
            ResetScrollZoom();
        }

        /// <summary>
        /// Ensure the lens GO stays active while zoom is applied.
        /// LensTransparency may try to hide it — we override that.
        /// </summary>
        public static void EnsureLensVisible()
        {
            // No-op: shader path removed.
        }

        /// <summary>
        /// Compute magnification from OpticSight data.
        /// magnification = baseOpticFov / scopeZoomFov
        /// where baseOpticFov = 35° (EFT's standard optic camera FOV).
        ///
        /// Uses the SAME discovery chain as FovController:
        ///   1. Scroll zoom override (user-set via scroll wheel)
        ///   2. ScopeZoomHandler.FiledOfView (runtime, variable zoom)
        ///   3. ScopeCameraData.FieldOfView  (baked prefab, discovered by assembly scan)
        ///   4. Brute-force scan for any MonoBehaviour with FieldOfView
        ///   5. Config DefaultZoom fallback
        /// </summary>
        public static float GetMagnification(OpticSight os)
        {
            if (os == null) return ScopeHousingMeshSurgeryPlugin.DefaultZoom.Value;

            float scopeFov = GetScopeFov(os);
            if (scopeFov > 0.1f)
            {
                _nativeFov = scopeFov;

                // Discover FOV range only once per scope session
                if (!_fovRangeDiscovered)
                {
                    DiscoverFovRange(os);
                    _fovRangeDiscovered = true;
                }

                // Scroll zoom override — but detect mode switches first.
                // If the native FOV changes while scroll override is active
                // (e.g. user alt+right-clicked to switch magnification level), reset
                // scroll zoom so the native change takes effect immediately.
                if (_scrollZoomActive && _scrollZoomFov > 0f)
                {
                    // Use a small epsilon so each discrete magnification step is detected immediately.
                    // Larger thresholds caused multiple mode toggles to be required before reset.
                    if (Mathf.Abs(scopeFov - _lastNativeFovForSwitchDetect) > 0.03f)
                    {
                        ScopeHousingMeshSurgeryPlugin.LogInfo(
                            $"[ZoomController] Native FOV changed {_lastNativeFovForSwitchDetect:F2}° → {scopeFov:F2}° — resetting scroll zoom");
                        _scrollZoomActive = false;
                        _scrollZoomFov = 0f;
                    }
                    else
                    {
                        _lastNativeFovForSwitchDetect = scopeFov;
                        return 35f / _scrollZoomFov;
                    }
                }

                return 35f / scopeFov;
            }

            _nativeFov = 35f / ScopeHousingMeshSurgeryPlugin.DefaultZoom.Value;
            return ScopeHousingMeshSurgeryPlugin.DefaultZoom.Value;
        }

        /// <summary>
        /// Returns the current effective magnification as a scope FOV value.
        /// Used by FovController to keep FOV and reticle in sync.
        /// Returns 0 if no override is active.
        /// </summary>
        public static float GetEffectiveScopeFov()
        {
            if (_scrollZoomActive && _scrollZoomFov > 0f)
                return _scrollZoomFov;
            return 0f; // no override, use native
        }

        /// <summary>
        /// Process scroll wheel input for zoom adjustment.
        /// Works in FOV space: scroll up = zoom in (decrease FOV),
        /// scroll down = zoom out (increase FOV).
        /// Clamped to the scope's native FOV range from ScopeZoomHandler.
        /// Returns true if zoom changed (caller should re-apply FOV).
        /// </summary>
        public static bool HandleScrollZoom(float scrollDelta)
        {
            if (!ScopeHousingMeshSurgeryPlugin.EnableScrollZoom.Value) return false;
            if (Mathf.Abs(scrollDelta) < 0.01f) return false;

            // Don't allow scroll zoom on fixed scopes (no ScopeZoomHandler found)
            if (!_isVariableZoom) return false;

            float sensitivity = ScopeHousingMeshSurgeryPlugin.ScrollZoomSensitivity.Value;

            // FOV bounds: minFov = highest zoom, maxFov = lowest zoom
            float minFov = _nativeMinFov;
            float maxFov = _nativeMaxFov;

            // Stay slightly inside native limits so we don't stick exactly on hard endpoints.
            // This helps mode-switch detection when users scroll to the extremes.
            const float boundaryInset = 0.1f;
            float hardMinFov = _nativeMinFov + boundaryInset;
            float hardMaxFov = _nativeMaxFov - boundaryInset;

            // Safety: if scope range is too narrow, fall back to full native range.
            if (hardMaxFov <= hardMinFov + 0.01f)
            {
                hardMinFov = _nativeMinFov;
                hardMaxFov = _nativeMaxFov;
            }

            // Config overrides are in magnification units for user-friendliness
            float cfgMinMag = ScopeHousingMeshSurgeryPlugin.ScrollZoomMin.Value;
            float cfgMaxMag = ScopeHousingMeshSurgeryPlugin.ScrollZoomMax.Value;
            if (cfgMaxMag > 0f) minFov = 35f / cfgMaxMag;
            if (cfgMinMag > 0f) maxFov = 35f / cfgMinMag;

            // Keep requested range within slightly inset native boundaries.
            minFov = Mathf.Clamp(minFov, hardMinFov, hardMaxFov);
            maxFov = Mathf.Clamp(maxFov, hardMinFov, hardMaxFov);

            // Safety: if range is degenerate after config overrides and clamping, allow inset-native range.
            if (maxFov <= minFov + 0.05f)
            {
                minFov = hardMinFov;
                maxFov = hardMaxFov;
            }

            // Initialize from native FOV if this is the first scroll
            if (!_scrollZoomActive)
            {
                _scrollZoomFov = _nativeFov;
                _lastNativeFovForSwitchDetect = _nativeFov;
                _scrollZoomActive = true;
            }

            // Multiplicative scaling in FOV space:
            // scroll up (+) = zoom in = smaller FOV → divide
            // scroll down (-) = zoom out = larger FOV → multiply
            float factor = 1f + sensitivity;
            if (scrollDelta > 0f)
                _scrollZoomFov /= factor;
            else
                _scrollZoomFov *= factor;

            _scrollZoomFov = Mathf.Clamp(_scrollZoomFov, minFov, maxFov);

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[ZoomController] Scroll zoom: FOV={_scrollZoomFov:F2}° (range={minFov:F2}°-{maxFov:F2}°)");

            return true;
        }

        /// <summary>
        /// Reset scroll zoom override. Called on scope exit.
        /// </summary>
        public static void ResetScrollZoom()
        {
            _scrollZoomActive = false;
            _scrollZoomFov = 0f;
            _lastNativeFovForSwitchDetect = 0f;
            _fovRangeDiscovered = false;
            _isVariableZoom = false;
        }

        /// <summary>
        /// Discover the scope's native FOV range from ScopeZoomHandler.
        /// Uses actual EFT field names discovered via dnSpy:
        ///   Single_0 = MinMaxFov.x = max FOV (min zoom, e.g. 3.95° for Leupold 6.5x)
        ///   Single_1 = MinMaxFov.y = min FOV (max zoom, e.g. 1.29° for Leupold 20x)
        /// Stores the FOV values directly — no conversion to magnification.
        /// </summary>
        private static void DiscoverFovRange(OpticSight os)
        {
            _nativeMinFov = _nativeFov;
            _nativeMaxFov = _nativeFov;
            _isVariableZoom = false;

            try
            {
                var szh = os.GetComponentInParent<ScopeZoomHandler>();
                if (szh == null) szh = os.GetComponentInChildren<ScopeZoomHandler>();
                if (szh == null)
                {
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        "[ZoomController] No ScopeZoomHandler found — fixed scope, scroll zoom disabled");
                    return;
                }

                var szhType = szh.GetType();
                float maxFov = 0f, minFov = 0f; // maxFov = lowest zoom, minFov = highest zoom

                // Strategy 1: Read Single_0 (maxFov) and Single_1 (minFov) properties directly
                var s0Prop = szhType.GetProperty("Single_0",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var s1Prop = szhType.GetProperty("Single_1",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (s0Prop != null && s1Prop != null &&
                    s0Prop.PropertyType == typeof(float) && s1Prop.PropertyType == typeof(float))
                {
                    maxFov = (float)s0Prop.GetValue(szh);
                    minFov = (float)s1Prop.GetValue(szh);
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        $"[ZoomController] Read Single_0={maxFov:F2} Single_1={minFov:F2}");
                }

                // Strategy 2: Read private iadjustableOpticData_0 → MinMaxFov Vector3
                if (maxFov < 0.1f || minFov < 0.1f)
                {
                    foreach (var field in szhType.GetFields(
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                    {
                        if (field.FieldType.Name.Contains("IAdjustableOpticData") ||
                            field.FieldType.Name.Contains("AdjustableOptic") ||
                            field.Name.Contains("iadjustableOpticData"))
                        {
                            var opticData = field.GetValue(szh);
                            if (opticData == null) continue;

                            var mmfProp = opticData.GetType().GetProperty("MinMaxFov");
                            if (mmfProp != null && mmfProp.PropertyType == typeof(Vector3))
                            {
                                var mmf = (Vector3)mmfProp.GetValue(opticData);
                                maxFov = mmf.x;  // max FOV = min zoom
                                minFov = mmf.y;  // min FOV = max zoom
                                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                                    $"[ZoomController] Read MinMaxFov=({mmf.x:F2}, {mmf.y:F2}, {mmf.z:F2})");
                                break;
                            }
                        }
                    }
                }

                if (maxFov > 0.1f && minFov > 0.1f && maxFov > minFov)
                {
                    // Double FOV values to match the ×2 applied in GetScopeFov
                    maxFov *= 2f;
                    minFov *= 2f;
                    _nativeMaxFov = maxFov;
                    _nativeMinFov = minFov;
                    _isVariableZoom = true;
                    ScopeHousingMeshSurgeryPlugin.LogInfo(
                        $"[ZoomController] Discovered FOV range: {minFov:F2}° - {maxFov:F2}° " +
                        $"(variable zoom — scroll zoom enabled)");
                }
                else
                {
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        $"[ZoomController] Could not discover FOV range — fixed scope at FOV={_nativeFov:F2}°");
                }
            }
            catch (System.Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[ZoomController] DiscoverFovRange exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Full FOV discovery chain — mirrors FovController.GetScopeFov() logic
        /// so magnification and FOV zoom always agree.
        /// </summary>
        private static float GetScopeFov(OpticSight os)
        {
            // === Try 1: ScopeZoomHandler.FiledOfView (runtime, variable zoom) ===
            try
            {
                var szh = os.GetComponentInParent<ScopeZoomHandler>();
                if (szh == null) szh = os.GetComponentInChildren<ScopeZoomHandler>();
                if (szh != null)
                {
                    float fov = szh.FiledOfView; // Note: EFT typo "Filed" not "Field"
                    if (fov > 0.1f) return fov * 2f;
                }
            }
            catch { }

            // === Try 2: ScopeCameraData via FovController (cached type discovery) ===
            // FovController already has the full assembly-scan + brute-force logic.
            // Rather than duplicate it, we compute what the FOV would be.
            try
            {
                // Walk up to scope root, find ScopeCameraData on the active mode
                Transform scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
                if (scopeRoot != null)
                {
                    foreach (var mb in scopeRoot.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb == null) continue;
                        // Only consider components on the same mode as our optic
                        if (!IsOnSameMode(mb.transform, os.transform)) continue;

                        var type = mb.GetType();
                        var fovField = type.GetField("FieldOfView",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (fovField == null || fovField.FieldType != typeof(float)) continue;

                        float fov = (float)fovField.GetValue(mb);
                        if (fov > 0.1f && fov < 180f)
                        {
                            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                                $"[ZoomController] GetScopeFov from '{mb.gameObject.name}' type={type.Name}: {fov:F2} (×2 = {fov*2f:F2})");
                            return fov * 2f;
                        }
                    }
                }
            }
            catch { }

            return 0f;
        }

        private static bool IsOnSameMode(Transform candidate, Transform optic)
        {
            Transform GetMode(Transform t)
            {
                for (var p = t; p != null; p = p.parent)
                    if (p.name != null && (p.name.StartsWith("mode_", System.StringComparison.OrdinalIgnoreCase)
                        || p.name.Equals("mode", System.StringComparison.OrdinalIgnoreCase)))
                        return p;
                return null;
            }
            var modeC = GetMode(candidate);
            var modeO = GetMode(optic);
            if (modeC == null || modeO == null) return modeC == modeO;
            return modeC == modeO;
        }

        /// <summary>No-op: shader zoom config path removed.</summary>
        private static void ApplyConfigToMaterial()
        {
            // Intentionally empty.
        }
    }
}
