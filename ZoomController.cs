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
        private static float _scrollStartNativeFov; // native FOV when scroll zoom first activated

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
@@ -544,43 +375,32 @@ namespace ScopeHousingMeshSurgery
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
