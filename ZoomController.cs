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
                    // Double FOV values to match the ×2 applied in GetScopeFov
                    maxFov *= 2f;
                    minFov *= 2f;
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
                    if (fov > 0.1f) return fov * 2f;
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
                                $"[ZoomController] GetScopeFov from '{mb.gameObject.name}' type={type.Name}: {fov:F2} (×2 = {fov*2f:F2})");
                            return fov * 2f;
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
