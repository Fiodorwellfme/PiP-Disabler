using System;
using EFT.CameraControl;
using UnityEngine;

namespace PiPDisabler
{
    /// <summary>
    /// Handles scope zoom math.
    /// Magnification is driven from Template.Zooms via FovController.
    /// </summary>
    internal static class ZoomController
    {
        public static Renderer ActiveLensRenderer => null;

        // Range info (discovered from template or FOV fallback)
        private static float _nativeMinMag;        // minimum magnification (widest view)
        private static float _nativeMaxMag;        // maximum magnification (tightest view)
        private static bool  _rangeDiscovered;

        public static bool ShaderAvailable => false;

        /// <summary>No shader state is active.</summary>
        public static bool IsActive => false;

        /// <summary>Kept for API compatibility with existing startup flow.</summary>
        public static void LoadShader()
        {
            PiPDisablerPlugin.LogInfo(
                "[ZoomController] Using template-based FOV zoom.");
        }

        /// <summary>Kept for API compatibility. No-op.</summary>
        public static void Apply(OpticSight os, float magnification) { }

        /// <summary>Kept for API compatibility. No-op.</summary>
        public static void SetZoom(float magnification) { }

        /// <summary>Resets zoom state on scope-out.</summary>
        public static void Restore()
        {
            ResetScrollZoom();
        }

        /// <summary>Kept for API compatibility. No-op.</summary>
        public static void EnsureLensVisible() { }

        /// <summary>
        /// Returns the current effective magnification.
        /// Delegates to FovController.GetEffectiveMagnification().
        /// </summary>
        public static float GetMagnification(OpticSight os)
        {
            if (os == null) return PiPDisablerPlugin.DefaultZoom.Value;

            // Ensure range is discovered
            if (!_rangeDiscovered)
            {
                DiscoverZoomRange(os);
                _rangeDiscovered = true;
            }

            return FovController.GetEffectiveMagnification();
        }

        public static float GetScrollZoomMagnification() => 0f;

        /// <summary>
        /// Returns the optic's minimum FOV (maximum magnification) in degrees.
        /// Converts from template zoom range when available.
        /// </summary>
        public static float GetMinFov(OpticSight os)
        {
            if (os == null) return 35f / PiPDisablerPlugin.DefaultZoom.Value;

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
            if (os == null) return PiPDisablerPlugin.DefaultZoom.Value;

            var (minZoom, maxZoom) = FovController.GetTemplateZoomRange();
            if (minZoom > 0.1f)
                return minZoom;

            // Fallback: if range discovered from FOV
            if (_rangeDiscovered && _nativeMinMag > 0.1f)
                return _nativeMinMag;

            return FovController.GetEffectiveMagnification();
        }

        /// <summary>
        /// No runtime input zoom override.
        /// </summary>
        public static bool HandleScrollZoom(float scrollDelta)
        {
            return false;
        }

        /// <summary>
        /// Reset cached zoom range. Called on scope exit.
        /// </summary>
        public static void ResetScrollZoom()
        {
            _rangeDiscovered = false;
        }

        /// <summary>
        /// Discover the scope's zoom range.
        /// Primary: template min/max zoom from FovController.
        /// Fallback: ScopeZoomHandler FOV range → converted to magnification.
        /// </summary>
        private static void DiscoverZoomRange(OpticSight os)
        {

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
                PiPDisablerPlugin.LogInfo(
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
                    PiPDisablerPlugin.LogVerbose(
                        "[ZoomController] No ScopeZoomHandler — fixed scope");
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
                        PiPDisablerPlugin.LogInfo(
                        $"[ZoomController] Discovered FOV-based zoom range: " +
                        $"{_nativeMinMag:F2}x - {_nativeMaxMag:F2}x (from FOV {minFov:F2}°-{maxFov:F2}°)");
                }
                else
                {
                    PiPDisablerPlugin.LogVerbose(
                        $"[ZoomController] Could not discover zoom range — fixed scope at {currentMag:F2}x");
                }
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogVerbose(
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
