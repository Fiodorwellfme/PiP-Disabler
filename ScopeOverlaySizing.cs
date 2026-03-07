using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Shared screen-space sizing math for overlays that should track the
    /// physical scope aperture regardless of magnification/FOV.
    /// </summary>
    internal static class ScopeOverlaySizing
    {
        private const float ReferenceLensDistance = 0.075f;

        /// <summary>
        /// Converts a physical aperture/reticle size (meters at lens plane) into
        /// a centered clip-space diameter (NDC units).
        /// </summary>
        public static float ComputeNdcDiameter(Camera cam, float baseSize, float magnification)
        {
            float mag = Mathf.Max(1f, magnification);
            float fovDeg = cam != null ? cam.fieldOfView : 35f;
            float tanHalfFov = Mathf.Max(0.01f, Mathf.Tan(fovDeg * Mathf.Deg2Rad * 0.5f));

            float angularSize = (Mathf.Max(0.001f, baseSize) / mag) / ReferenceLensDistance;
            float ndcDiameter = angularSize / tanHalfFov;
            return Mathf.Clamp(ndcDiameter, 0.01f, 2f);
        }
    }
}
