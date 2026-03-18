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
        /// <summary>
        /// Returns the current effective magnification.
        /// Delegates to FovController.GetEffectiveMagnification().
        /// </summary>
        public static float GetMagnification(OpticSight os)
        {
            if (os == null) return PiPDisablerPlugin.DefaultZoom.Value;
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

            // Read from ScopeZoomHandler directly when the template range is unavailable.
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

    }
}
