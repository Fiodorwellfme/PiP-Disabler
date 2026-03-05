using System;
using EFT.CameraControl;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Handles scope zoom math and scroll-wheel zoom override.
    ///
    /// Magnification is now driven from Template.Zooms via FovController.
    /// Scroll zoom operates in magnification space (not FOV space).
    /// </summary>
    internal static class ZoomController
    {
        /// <summary>
        /// Shader zoom is removed, so no lens renderer is excluded from transparency kills.
        /// </summary>
        public static Renderer ActiveLensRenderer => null;

        // Scroll zoom override — when active, overrides the scope's magnification.
        private static float _scrollZoomMag;       // 0 = not active, >0 = user-set magnification
        private static bool  _scrollZoomActive;
        private static float _scrollStartTemplateMag; // template mag when scroll zoom first activated

        // Range info (discovered from template or FOV fallback)
        private static float _nativeMinMag;        // minimum magnification (widest view)
        private static float _nativeMaxMag;        // maximum magnification (tightest view)
        private static bool  _rangeDiscovered;
        private static bool  _isVariableZoom;

        /// <summary>Shader zoom is disabled permanently.</summary>
        public static bool ShaderAvailable => false;

        /// <summary>No shader state is active.</summary>
        public static bool IsActive => false;

        /// <summary>Kept for API compatibility with existing startup flow.</summary>
        public static void LoadShader()
        {
            ScopeHousingMeshSurgeryPlugin.LogInfo(
                "[ZoomController] Shader zoom removed. Using template-based FOV zoom.");
        }

        /// <summary>Kept for API compatibility. No-op.</summary>
        public static void Apply(OpticSight os, float magnification) { }

        /// <summary>Kept for API compatibility. No-op.</summary>
        public static void SetZoom(float magnification) { }

        /// <summary>Clears scroll-zoom override on scope-out.</summary>
        public static void Restore()
        {
            ResetScrollZoom();
        }

        /// <summary>Kept for API compatibility. No-op.</summary>
        public static void EnsureLensVisible() { }

        /// <summary>
        /// Returns the current effective magnification.
        /// Delegates to FovController.GetEffectiveMagnification() which uses:
        ///   1. Scroll zoom override
        ///   2. Template.Zooms (primary)
        ///   3. ScopeCameraData FOV (fallback)
        ///   4. Config DefaultZoom
        /// </summary>
        public static float GetMagnification(OpticSight os)
        {
            if (os == null) return ScopeHousingMeshSurgeryPlugin.DefaultZoom.Value;

            // Ensure range is discovered for scroll zoom bounds
            if (!_rangeDiscovered)
            {
                DiscoverZoomRange(os);
                _rangeDiscovered = true;
            }

            // Detect mode switch: if template mag changed significantly from when
            // scroll zoom started, reset scroll zoom so the native change takes effect.
            if (_scrollZoomActive && _scrollZoomMag > 0f)
            {
                float currentTemplateMag = FovController.GetEffectiveMagnification();
                // Use the non-scroll path to detect template changes
                // (GetEffectiveMagnification already skips scroll if we ask it directly)
                // Instead, compare against what we stored
                // NOTE: we rely on the TEMPLATE source changing significantly
            }

            return FovController.GetEffectiveMagnification();
        }

        /// <summary>
        /// Returns the scroll zoom magnification override, or 0 if not active.
        /// Called by FovController as priority #1 in the magnification chain.
        /// </summary>
        public static float GetScrollZoomMagnification()
        {
            if (_scrollZoomActive && _scrollZoomMag > 0.1f)
                return _scrollZoomMag;
            return 0f;
        }

        /// <summary>
        /// Returns the optic's minimum FOV (maximum magnification) in degrees.
        /// Used by ScopeLifecycle for high-magnification bypass detection.
        /// Converts from template zoom range when available.
        /// </summary>
        public static float GetMinFov(OpticSight os)
        {
            if (os == null) return 35f / ScopeHousingMeshSurgeryPlugin.DefaultZoom.Value;

            // Try template zoom range first
            var (minZoom, maxZoom) = FovController.GetTemplateZoomRange();
            if (maxZoom > 0.1f)
            {
                // Min FOV corresponds to max magnification
                return FovController.MagnificationToFov(maxZoom, FovController.ZoomBaselineFov);
            }

            // Fallback: read from ScopeZoomHandler directly
            try
            {
                var szh = os.GetComponentInParent<ScopeZoomHandler>();
                if (szh == null) szh = os.GetComponentInChildren<ScopeZoomHandler>();
                if (szh != null)
                {
                    var szhType = szh.GetType();
                    float minFov = 0f;

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
                        return minFov;
                }
            }
            catch { }

            // Fixed scope: current magnification → FOV
            float currentMag = FovController.GetEffectiveMagnification();
            return FovController.MagnificationToFov(currentMag, FovController.ZoomBaselineFov);
        }

        /// <summary>
        /// Returns the optic's minimum magnification (widest view).
        /// For variable zoom scopes this is from template min zoom.
        /// For fixed scopes this equals the current magnification.
        /// </summary>
        public static float GetMinMagnification(OpticSight os)
        {
            if (os == null) return ScopeHousingMeshSurgeryPlugin.DefaultZoom.Value;

            var (minZoom, maxZoom) = FovController.GetTemplateZoomRange();
            if (minZoom > 0.1f)
                return minZoom;

            // Fallback: if range discovered from FOV
            if (_rangeDiscovered && _nativeMinMag > 0.1f)
                return _nativeMinMag;

            return FovController.GetEffectiveMagnification();
        }

        /// <summary>
        /// Process scroll wheel input for zoom adjustment.
        /// Now works in magnification space: scroll up = zoom in (increase mag),
        /// scroll down = zoom out (decrease mag).
        /// Clamped to the scope's magnification range from template.
        /// Returns true if zoom changed (caller should re-apply FOV).
        /// </summary>
        public static bool HandleScrollZoom(float scrollDelta)
        {
            if (!ScopeHousingMeshSurgeryPlugin.EnableScrollZoom.Value) return false;
            if (Mathf.Abs(scrollDelta) < 0.01f) return false;

            // Don't allow scroll zoom on fixed scopes
            if (!_isVariableZoom) return false;

            float sensitivity = ScopeHousingMeshSurgeryPlugin.ScrollZoomSensitivity.Value;

            // Magnification bounds
            float minMag = _nativeMinMag;
            float maxMag = _nativeMaxMag;

            // Config overrides (already in magnification units)
            float cfgMinMag = ScopeHousingMeshSurgeryPlugin.ScrollZoomMin.Value;
            float cfgMaxMag = ScopeHousingMeshSurgeryPlugin.ScrollZoomMax.Value;
            if (cfgMinMag > 0f) minMag = cfgMinMag;
            if (cfgMaxMag > 0f) maxMag = cfgMaxMag;

            // Safety: if range is degenerate, allow ±50%
            if (maxMag <= minMag + 0.05f)
            {
                float currentMag = FovController.GetEffectiveMagnification();
                minMag = currentMag * 0.5f;
                maxMag = currentMag * 2f;
            }

            // Initialize from current template magnification if this is the first scroll
            if (!_scrollZoomActive)
            {
                _scrollZoomMag = FovController.GetEffectiveMagnification();
                _scrollStartTemplateMag = _scrollZoomMag;
                _scrollZoomActive = true;
            }

            // Multiplicative scaling in magnification space:
            // scroll up (+) = zoom in = higher magnification → multiply
            // scroll down (-) = zoom out = lower magnification → divide
            float factor = 1f + sensitivity;
            if (scrollDelta > 0f)
                _scrollZoomMag *= factor;
            else
                _scrollZoomMag /= factor;

            _scrollZoomMag = Mathf.Clamp(_scrollZoomMag, minMag, maxMag);

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[ZoomController] Scroll zoom: mag={_scrollZoomMag:F2}x (range={minMag:F2}x-{maxMag:F2}x)");

            return true;
        }

        /// <summary>
        /// Reset scroll zoom override. Called on scope exit.
        /// </summary>
        public static void ResetScrollZoom()
        {
            _scrollZoomActive = false;
            _scrollZoomMag = 0f;
            _scrollStartTemplateMag = 0f;
            _rangeDiscovered = false;
            _isVariableZoom = false;
        }

        /// <summary>
        /// Discover the scope's zoom range.
        /// Primary: template min/max zoom from FovController.
        /// Fallback: ScopeZoomHandler FOV range → converted to magnification.
        /// </summary>
        private static void DiscoverZoomRange(OpticSight os)
        {
            _isVariableZoom = false;

            // Set defaults from current magnification
            float currentMag = FovController.GetEffectiveMagnification();
            _nativeMinMag = currentMag;
            _nativeMaxMag = currentMag;

            // Strategy 1: Template zoom range
            var (minZoom, maxZoom) = FovController.GetTemplateZoomRange();
            if (minZoom > 0.1f && maxZoom > 0.1f && maxZoom > minZoom)
            {
                _nativeMinMag = minZoom;
                _nativeMaxMag = maxZoom;
                _isVariableZoom = true;
                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[ZoomController] Discovered template zoom range: {minZoom:F2}x - {maxZoom:F2}x (variable)");
                return;
            }

            // Strategy 2: ScopeZoomHandler FOV range → magnification
            try
            {
                var szh = os.GetComponentInParent<ScopeZoomHandler>();
                if (szh == null) szh = os.GetComponentInChildren<ScopeZoomHandler>();
                if (szh == null)
                {
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        "[ZoomController] No ScopeZoomHandler — fixed scope, scroll zoom disabled");
                    return;
                }

                var szhType = szh.GetType();
                float maxFov = 0f, minFov = 0f;

                var s0Prop = szhType.GetProperty("Single_0",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var s1Prop = szhType.GetProperty("Single_1",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (s0Prop != null && s1Prop != null &&
                    s0Prop.PropertyType == typeof(float) && s1Prop.PropertyType == typeof(float))
                {
                    maxFov = (float)s0Prop.GetValue(szh);
                    minFov = (float)s1Prop.GetValue(szh);
                }

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
                                maxFov = mmf.x;
                                minFov = mmf.y;
                                break;
                            }
                        }
                    }
                }

                if (maxFov > 0.1f && minFov > 0.1f && maxFov > minFov)
                {
                    // Convert FOV range to magnification range (35° optic camera baseline)
                    _nativeMinMag = 35f / maxFov; // max FOV → min magnification
                    _nativeMaxMag = 35f / minFov; // min FOV → max magnification
                    _isVariableZoom = true;
                    ScopeHousingMeshSurgeryPlugin.LogInfo(
                        $"[ZoomController] Discovered FOV-based zoom range: " +
                        $"{_nativeMinMag:F2}x - {_nativeMaxMag:F2}x (from FOV {minFov:F2}°-{maxFov:F2}°)");
                }
                else
                {
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        $"[ZoomController] Could not discover zoom range — fixed scope at {currentMag:F2}x");
                }
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[ZoomController] DiscoverZoomRange exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a candidate and optic share the same mode_XXX ancestor.
        /// </summary>
        private static bool IsOnSameMode(Transform candidate, Transform optic)
        {
            Transform GetMode(Transform t)
            {
                for (var p = t; p != null; p = p.parent)
                    if (p.name != null && (p.name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase)
                        || p.name.Equals("mode", StringComparison.OrdinalIgnoreCase)))
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
