using EFT.CameraControl;

namespace PiPDisabler
{
    /// <summary>
    /// Resolves the minimum scoped FOV used by scope-enter and bypass checks.
    /// </summary>
    internal static class ZoomController
    {
        /// <summary>
        /// Returns the optic's minimum FOV (maximum magnification) in degrees.
        /// Converts from template zoom range when available.
        /// </summary>
        public static float GetMinFov(OpticSight os)
        {
            if (os == null) return 35f / PiPDisablerPlugin.DefaultZoom.Value;

            var (minZoom, maxZoom) = FovController.GetTemplateZoomRange();
            if (maxZoom > 0.1f)
                return FovController.MagnificationToFov(maxZoom, FovController.ZoomBaselineFov);

            float currentMag = FovController.GetEffectiveMagnification();
            return FovController.MagnificationToFov(currentMag, FovController.ZoomBaselineFov);
        }
    }
}
