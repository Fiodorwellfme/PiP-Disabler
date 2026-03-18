using UnityEngine;

namespace PiPDisabler
{
    /// <summary>
    /// Raycasts from the aiming camera each frame while scoped and remaps the
    /// hit distance to a LOD bias value using a piecewise-linear curve defined
    /// by five runtime-configurable breakpoints (50 m, 100 m, 200 m, 300 m, 400 m).
    ///
    /// Close targets get a higher LOD bias (more detail), distant targets get less.
    /// When the raycast misses (sky / beyond max range), the 400 m value is used.
    /// </summary>
    internal static class DistanceLodBiasController
    {
        // Cached result updated every Tick().
        private static float _currentLodBias;
        private static float _currentDistance;
        private static bool _hasHit;

        // Max raycast range — anything beyond this uses the 400 m bias.
        private const float MaxRaycastDistance = 600f;

        /// <summary>
        /// The LOD bias computed from the last raycast. Only meaningful when
        /// <see cref="PiPDisablerPlugin.EnableDistanceLodBias"/> is true and
        /// the scope is active.
        /// </summary>
        public static float CurrentLodBias => _currentLodBias;

        /// <summary>Distance to the aimed-at surface (0 if no hit).</summary>
        public static float CurrentDistance => _currentDistance;

        /// <summary>Whether the last raycast hit anything.</summary>
        public static bool HasHit => _hasHit;

        /// <summary>
        /// Call once per frame while scoped. Performs the raycast and updates
        /// <see cref="CurrentLodBias"/>.
        /// </summary>
        public static void Tick()
        {
            if (PiPDisablerPlugin.EnableDistanceLodBias == null ||
                !PiPDisablerPlugin.EnableDistanceLodBias.Value)
                return;

            var cam = PiPDisablerPlugin.GetMainCamera();
            if (cam == null) return;

            var ray = new Ray(cam.transform.position, cam.transform.forward);

            int mask = PiPDisablerPlugin.DistanceLodBiasRaycastMask != null
                ? PiPDisablerPlugin.DistanceLodBiasRaycastMask.Value
                : 0;
            // Negative value convention: treat as ~abs(value) to exclude layers.
            if (mask < 0) mask = ~(-mask);
            if (mask == 0) mask = ~0; // all layers

            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, MaxRaycastDistance, mask, QueryTriggerInteraction.Ignore))
            {
                _hasHit = true;
                _currentDistance = hit.distance;
            }
            else
            {
                _hasHit = false;
                _currentDistance = MaxRaycastDistance;
            }

            _currentLodBias = EvaluateLodBias(_currentDistance);
        }

        /// <summary>
        /// Reset state when leaving scope.
        /// </summary>
        public static void Reset()
        {
            _currentLodBias = 0f;
            _currentDistance = 0f;
            _hasHit = false;
        }

        /// <summary>
        /// Piecewise-linear interpolation across the five configurable breakpoints.
        /// Distances &lt;= 50 m clamp to the 50 m value; distances &gt;= 400 m clamp
        /// to the 400 m value.
        /// </summary>
        private static float EvaluateLodBias(float distance)
        {
            float v50  = PiPDisablerPlugin.DistanceLodBias_50m  != null ? PiPDisablerPlugin.DistanceLodBias_50m.Value  : 6f;
            float v100 = PiPDisablerPlugin.DistanceLodBias_100m != null ? PiPDisablerPlugin.DistanceLodBias_100m.Value : 5f;
            float v200 = PiPDisablerPlugin.DistanceLodBias_200m != null ? PiPDisablerPlugin.DistanceLodBias_200m.Value : 4f;
            float v300 = PiPDisablerPlugin.DistanceLodBias_300m != null ? PiPDisablerPlugin.DistanceLodBias_300m.Value : 3f;
            float v400 = PiPDisablerPlugin.DistanceLodBias_400m != null ? PiPDisablerPlugin.DistanceLodBias_400m.Value : 2f;

            if (distance <= 50f)  return v50;
            if (distance <= 100f) return Mathf.Lerp(v50,  v100, Mathf.InverseLerp(50f,  100f, distance));
            if (distance <= 200f) return Mathf.Lerp(v100, v200, Mathf.InverseLerp(100f, 200f, distance));
            if (distance <= 300f) return Mathf.Lerp(v200, v300, Mathf.InverseLerp(200f, 300f, distance));
            if (distance <= 400f) return Mathf.Lerp(v300, v400, Mathf.InverseLerp(300f, 400f, distance));
            return v400;
        }
    }
}
