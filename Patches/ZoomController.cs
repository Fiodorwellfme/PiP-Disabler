using System;
using EFT.CameraControl;

namespace PiPDisabler
{
    /// <summary>
    /// Handles scope zoom math.
    /// Magnification is driven from Template.Zooms via FovController.
    /// </summary>
    internal static class ZoomController
    {
        // Range info (discovered from template or FOV fallback)
        private static float _nativeMinMag;        // minimum magnification (widest view)
        private static float _nativeMaxMag;        // maximum magnification (tightest view)
        private static bool  _rangeDiscovered;

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
                    float minFov = szh.Single_1;
                    if (minFov > 0.1f)
                        return minFov;
                }
            }
            catch { }

            // Fixed scope: current magnification → FOV
            float currentMag = FovController.GetEffectiveMagnification();
            return FovController.MagnificationToFov(currentMag, FovController.ZoomBaselineFov);
        }

        /// <summary>Resets zoom state on scope-out.</summary>
        public static void Restore()
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

                float maxFov = szh.Single_0;
                float minFov = szh.Single_1;

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
    }
}
