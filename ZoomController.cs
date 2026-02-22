using System;
using EFT.CameraControl;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Handles scope zoom math and scroll-wheel zoom override.
    ///
    /// Shader-based lens zoom has been retired; this controller now keeps only
    /// FOV/magnification state so variable scopes continue to work.
    /// </summary>
    internal static class ZoomController
    {
        /// <summary>
        /// Shader zoom is removed, so no lens renderer is excluded from transparency kills.
        /// </summary>
        public static Renderer ActiveLensRenderer => null;

        // Scroll zoom override — when active, overrides the scope's native FOV.
        // Works in FOV space directly to avoid fake magnification numbers.
        private static float _scrollZoomFov;      // 0 = not active, >0 = user-set scope FOV
        private static bool  _scrollZoomActive;
        private static float _nativeFov = 35f;    // the scope's current native FOV (baseline)
        private static float _nativeMinFov = 1f;  // scope's min FOV (max zoom) from ScopeZoomHandler
        private static float _nativeMaxFov = 35f;  // scope's max FOV (min zoom) from ScopeZoomHandler
        private static bool  _fovRangeDiscovered;  // true once DiscoverFovRange has run for this scope
        private static bool  _isVariableZoom;      // true if ScopeZoomHandler was found (has zoom ring)
        private static float _scrollStartNativeFov; // native FOV when scroll zoom first activated

        /// <summary>Shader zoom is disabled permanently.</summary>
        public static bool ShaderAvailable => false;

        /// <summary>No shader state is active.</summary>
        public static bool IsActive => false;

        /// <summary>
        /// Kept for API compatibility with existing startup flow.
        /// </summary>
        public static void LoadShader()
        {
            ScopeHousingMeshSurgeryPlugin.LogInfo(
                "[ZoomController] Shader zoom removed. Using FOV zoom path only.");
        }

        /// <summary>
        /// Kept for API compatibility. No-op because shader zoom is removed.
        /// </summary>
        public static void Apply(OpticSight os, float magnification)
        {
            _ = os;
            _ = magnification;
        }

        /// <summary>
        /// Kept for API compatibility. No-op because shader zoom is removed.
        /// </summary>
        public static void SetZoom(float magnification)
        {
            _ = magnification;
        }

        /// <summary>
        /// Clears scroll-zoom override on scope-out.
        /// </summary>
        public static void Restore()
        {
            ResetScrollZoom();
        }

        /// <summary>
        /// Kept for API compatibility. No-op because shader zoom is removed.
        /// </summary>
        public static void EnsureLensVisible()
        {
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
                // If the native FOV changed significantly from when scroll zoom started
                // (e.g. user alt+right-clicked to switch magnification level), reset
                // scroll zoom so the native change takes effect.
                if (_scrollZoomActive && _scrollZoomFov > 0f)
                {
                    if (Mathf.Abs(scopeFov - _scrollStartNativeFov) > 0.3f)
                    {
                        ScopeHousingMeshSurgeryPlugin.LogInfo(
                            $"[ZoomController] Native FOV changed {_scrollStartNativeFov:F2}° → {scopeFov:F2}° — resetting scroll zoom");
                        _scrollZoomActive = false;
                        _scrollZoomFov = 0f;
                    }
                    else
                    {
                        return 35f / _scrollZoomFov;
                    }
                }

                return 35f / scopeFov;
            }

            _nativeFov = 35f / ScopeHousingMeshSurgeryPlugin.DefaultZoom.Value;
            return ScopeHousingMeshSurgeryPlugin.DefaultZoom.Value;
        }

        /// <summary>
        /// Returns the optic's maximum magnification.
        /// For variable zoom scopes this is read from ScopeZoomHandler range.
        /// For fixed scopes this falls back to current magnification.
        /// </summary>
        public static float GetMaxMagnification(OpticSight os)
        {
            if (os == null) return ScopeHousingMeshSurgeryPlugin.DefaultZoom.Value;

            try
            {
                var szh = os.GetComponentInParent<ScopeZoomHandler>();
                if (szh == null) szh = os.GetComponentInChildren<ScopeZoomHandler>();
                if (szh != null)
                {
                    var szhType = szh.GetType();
                    float minFov = 0f; // min FOV = highest zoom

                    var s1Prop = szhType.GetProperty("Single_1",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (s1Prop != null && s1Prop.PropertyType == typeof(float))
                        minFov = (float)s1Prop.GetValue(szh);

                    if (minFov < 0.1f)
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
                                    minFov = mmf.y;
                                    break;
                                }
                            }
                        }
                    }

                    if (minFov > 0.1f)
                        return 35f / minFov;
                }
            }
            catch { }

            return GetMagnification(os);
        }

        /// <summary>
        /// Returns the optic's minimum magnification (highest scope FOV = widest view).
        /// For variable zoom scopes this is read from ScopeZoomHandler range.
        /// For fixed scopes this equals the current (only) magnification.
        /// </summary>
        public static float GetMinMagnification(OpticSight os)
        {
            if (os == null) return ScopeHousingMeshSurgeryPlugin.DefaultZoom.Value;

            // If FOV range has been discovered and we have a valid max FOV (min zoom)
            if (_fovRangeDiscovered && _nativeMaxFov > 0.1f)
                return 35f / _nativeMaxFov;

            // Range not yet discovered — try to discover it now
            try
            {
                var szh = os.GetComponentInParent<ScopeZoomHandler>();
                if (szh == null) szh = os.GetComponentInChildren<ScopeZoomHandler>();
                if (szh != null)
                {
                    var szhType = szh.GetType();
                    float maxFov = 0f;

                    var s0Prop = szhType.GetProperty("Single_0",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (s0Prop != null && s0Prop.PropertyType == typeof(float))
                        maxFov = (float)s0Prop.GetValue(szh);

                    if (maxFov < 0.1f)
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
                                    maxFov = mmf.x; // max FOV = min zoom
                                    break;
                                }
                            }
                        }
                    }

                    if (maxFov > 0.1f)
                        return 35f / maxFov;
                }
            }
            catch { }

            // Fixed scope: min mag = current mag
            return GetMagnification(os);
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

            // Config overrides are in magnification units for user-friendliness
            float cfgMinMag = ScopeHousingMeshSurgeryPlugin.ScrollZoomMin.Value;
            float cfgMaxMag = ScopeHousingMeshSurgeryPlugin.ScrollZoomMax.Value;
            if (cfgMaxMag > 0f) minFov = 35f / cfgMaxMag;
            if (cfgMinMag > 0f) maxFov = 35f / cfgMinMag;

            // Safety: if range is degenerate after config overrides, allow ±50%
            if (maxFov <= minFov + 0.05f)
            {
                minFov = _nativeFov * 0.5f;
                maxFov = _nativeFov * 2f;
            }

            // Initialize from native FOV if this is the first scroll
            if (!_scrollZoomActive)
            {
                _scrollZoomFov = _nativeFov;
                _scrollStartNativeFov = _nativeFov;
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
            _scrollStartNativeFov = 0f;
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
                    if (fov > 0.1f) return fov;
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
                                $"[ZoomController] GetScopeFov from '{mb.gameObject.name}' type={type.Name}: {fov:F2}");
                            return fov;
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
    }
}
