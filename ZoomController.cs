using System;
using System.IO;
using System.Reflection;
using EFT.CameraControl;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Manages GrabPass shader zoom on the scope lens.
    ///
    /// Flow:
    ///   1. Awake: LoadShader() loads ScopeZoom shader from AssetBundle
    ///   2. Scope-in: Apply() swaps lens material to zoom material
    ///   3. Per-frame: UpdateZoom() adjusts magnification for variable scopes
    ///   4. Scope-out: Restore() puts original material back
    ///
    /// The shader handles everything GPU-side:
    ///   - GrabPass captures screen before lens renders (no feedback loop)
    ///   - Fragment shader magnifies UVs toward scope center
    ///   - Circular vignette masks to scope shape
    ///   - Procedural crosshair reticle overlay
    ///
    /// Zero CPU overhead beyond material property updates. No extra cameras.
    /// </summary>
    internal static class ZoomController
    {
        private static Shader _zoomShader;
        private static Material _zoomMaterial;
        private static bool _shaderLoaded;
        private static bool _loadAttempted;

        // Per-scope state
        private static Renderer _activeLensRenderer;
        private static Material _originalLensMaterial;
        private static bool _isActive;
        private static float _lastLoggedZoom = -1f;

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
        /// Attempts to load the ScopeZoom shader from an AssetBundle.
        /// Called once during plugin Awake. Safe to call multiple times.
        ///
        /// Expected bundle location:
        ///   BepInEx/plugins/ScopeHousingMeshSurgery/assets/scopezoom.bundle
        /// </summary>
        public static void LoadShader()
        {
            if (_loadAttempted) return;
            _loadAttempted = true;

            try
            {
                var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(assemblyDir)) return;

                // Try multiple paths for flexibility
                string[] candidates = {
                    Path.Combine(assemblyDir, "assets", "scopezoom.bundle"),
                    Path.Combine(assemblyDir, "assets", "scopezoom"),
                    Path.Combine(assemblyDir, "scopezoom.bundle"),
                    Path.Combine(assemblyDir, "scopezoom"),
                };

                string bundlePath = null;
                foreach (var path in candidates)
                {
                    if (File.Exists(path))
                    {
                        bundlePath = path;
                        break;
                    }
                }

                if (bundlePath == null)
                {
                    ScopeHousingMeshSurgeryPlugin.LogInfo(
                        "[ZoomController] No scopezoom.bundle found. Shader zoom disabled — using FOV zoom fallback.");
                    ScopeHousingMeshSurgeryPlugin.LogInfo(
                        $"[ZoomController] Searched: {candidates[0]}");
                    return;
                }

                var bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    ScopeHousingMeshSurgeryPlugin.LogError(
                        $"[ZoomController] AssetBundle.LoadFromFile returned null for: {bundlePath}");
                    return;
                }

                // Try multiple asset paths (depends on how the bundle was built)
                string[] shaderNames = {
                    "Assets/Shaders/ScopeZoom.shader",
                    "ScopeZoom",
                    "ScopeHousingMeshSurgery/ScopeZoom",
                };

                foreach (var name in shaderNames)
                {
                    _zoomShader = bundle.LoadAsset<Shader>(name);
                    if (_zoomShader != null) break;
                }

                // Fallback: load first shader in the bundle
                if (_zoomShader == null)
                {
                    var allShaders = bundle.LoadAllAssets<Shader>();
                    if (allShaders != null && allShaders.Length > 0)
                        _zoomShader = allShaders[0];
                }

                bundle.Unload(false); // Unload bundle metadata, keep loaded assets

                if (_zoomShader == null)
                {
                    ScopeHousingMeshSurgeryPlugin.LogError(
                        "[ZoomController] ScopeZoom shader not found in bundle. " +
                        "Ensure the shader is named 'ScopeZoom' and tagged in the AssetBundle.");
                    return;
                }

                if (!_zoomShader.isSupported)
                {
                    ScopeHousingMeshSurgeryPlugin.LogError(
                        "[ZoomController] ScopeZoom shader is not supported on this GPU/platform.");
                    return;
                }

                _zoomMaterial = new Material(_zoomShader);
                _zoomMaterial.name = "ScopeZoom_Runtime";
                _shaderLoaded = true;

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    "[ZoomController] Shader zoom loaded successfully from: " + bundlePath);
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogError(
                    $"[ZoomController] Failed to load shader: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply the zoom shader to the scope's lens renderer.
        /// Called on scope-in. Replaces the lens material (which normally shows PiP or is hidden).
        /// </summary>
        public static void Apply(OpticSight os, float magnification)
        {
            if (!_shaderLoaded || _zoomMaterial == null) return;
            if (os == null) return;

            Renderer lensRenderer = null;
            try { lensRenderer = os.LensRenderer; } catch { }
            if (lensRenderer == null) return;

            // If already active on this renderer, just update zoom
            if (_isActive && _activeLensRenderer == lensRenderer)
            {
                SetZoom(magnification);
                return;
            }

            // If active on a different renderer, restore first
            if (_isActive) Restore();

            // Save original state
            _activeLensRenderer = lensRenderer;
            _originalLensMaterial = lensRenderer.material; // Creates instance (Material, not SharedMaterial)
            _isActive = true;

            // Apply zoom material with initial settings
            SetZoom(magnification);
            ApplyConfigToMaterial();
            lensRenderer.material = _zoomMaterial;

            // CRITICAL: Make sure the lens GameObject is active.
            // LensTransparency normally hides it — we need it visible for the shader to render.
            if (!lensRenderer.gameObject.activeSelf)
                lensRenderer.gameObject.SetActive(true);

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[ZoomController] Zoom applied: {magnification:F1}x on '{os.name}'");
        }

        /// <summary>
        /// Update magnification level. Called per-frame for variable zoom scopes.
        /// </summary>
        public static void SetZoom(float magnification)
        {
            if (!_isActive || _zoomMaterial == null) return;

            magnification = Mathf.Max(1f, magnification);
            _zoomMaterial.SetFloat("_Zoom", magnification);

            // Log only on significant change
            if (Mathf.Abs(magnification - _lastLoggedZoom) > 0.1f)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[ZoomController] Zoom = {magnification:F1}x");
                _lastLoggedZoom = magnification;
            }
        }

        /// <summary>
        /// Restore the original lens material. Called on scope-out.
        /// </summary>
        public static void Restore()
        {
            if (!_isActive) return;

            try
            {
                if (_activeLensRenderer != null && _originalLensMaterial != null)
                    _activeLensRenderer.material = _originalLensMaterial;
            }
            catch { /* Renderer may have been destroyed */ }

            _activeLensRenderer = null;
            _originalLensMaterial = null;
            _isActive = false;
            _lastLoggedZoom = -1f;

            // Reset scroll zoom on scope exit
            ResetScrollZoom();

            ScopeHousingMeshSurgeryPlugin.LogVerbose("[ZoomController] Zoom removed, original lens material restored.");
        }

        /// <summary>
        /// Ensure the lens GO stays active while zoom is applied.
        /// LensTransparency may try to hide it — we override that.
        /// </summary>
        public static void EnsureLensVisible()
        {
            if (!_isActive || _activeLensRenderer == null) return;

            try
            {
                if (!_activeLensRenderer.gameObject.activeSelf)
                    _activeLensRenderer.gameObject.SetActive(true);
            }
            catch { }
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

        /// <summary>Copy config values to shader material properties.</summary>
        private static void ApplyConfigToMaterial()
        {
            if (_zoomMaterial == null) return;

            // All configurable shader properties are set through Unity material inspector
            // defaults in the shader itself. Users can customize by editing the shader
            // or we can add BepInEx config entries for them later.
            //
            // For now, the shader defaults are:
            //   _VignetteRadius = 0.92
            //   _VignetteSoftness = 0.08
            //   _ReticleThickness = 0.0015
            //   _ReticleGap = 0.03
            //   _ReticleDot = 1
        }
    }
}
