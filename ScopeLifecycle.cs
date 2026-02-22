using System;
using System.Reflection;
using EFT;
using EFT.Animations;
using EFT.CameraControl;
using Comfort.Common;
using HarmonyLib;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Event-driven scope lifecycle. State machine with two states: scoped / not scoped.
    ///
    /// Entry points (event-driven, fire ONCE per transition):
    ///   OnOpticEnabled(os)  — from OpticSight.OnEnable patch
    ///   OnOpticDisabled(os) — from OpticSight.OnDisable patch
    ///   CheckAndUpdate()    — from ChangeAimingMode patch + Update safety net
    ///
    /// Per-frame (zero-alloc maintenance):
    ///   Tick() — ensure lens stays hidden, update variable zoom
    ///
    /// Detection (same as SPT-Dynamic-External-Resolution):
    ///   player.ProceduralWeaponAnimation.IsAiming
    ///   player.ProceduralWeaponAnimation.CurrentScope.IsOptic
    /// </summary>
    internal static class ScopeLifecycle
    {
        // Reflection cache
        private static PropertyInfo _isAimingProp;
        private static PropertyInfo _currentScopeProp;
        private static PropertyInfo _isOpticProp;
        private static bool _reflectionReady;

        // State
        private static bool _isScoped;
        private static OpticSight _activeOptic;
        private static OpticSight _lastEnabledOptic; // cache from OnEnable
        private static bool _modBypassedForCurrentScope;

        public static bool IsScoped => _isScoped;
        public static bool IsModBypassedForCurrentScope => _modBypassedForCurrentScope;
        public static OpticSight ActiveOptic => _activeOptic;

        /// <summary>
        /// One-time reflection setup. Call from plugin Awake.
        /// </summary>
        public static void Init()
        {
            try
            {
                var pwaType = typeof(ProceduralWeaponAnimation);
                _isAimingProp = AccessTools.Property(pwaType, "IsAiming");
                _currentScopeProp = AccessTools.Property(pwaType, "CurrentScope");

                if (_currentScopeProp != null)
                {
                    var scopeType = _currentScopeProp.PropertyType; // SightNBone
                    _isOpticProp = AccessTools.Property(scopeType, "IsOptic");
                }

                _reflectionReady = _isAimingProp != null
                                && _currentScopeProp != null
                                && _isOpticProp != null;

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[ScopeLifecycle] Reflection: IsAiming={_isAimingProp != null}, " +
                    $"CurrentScope={_currentScopeProp != null}, IsOptic={_isOpticProp != null}");
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogError($"[ScopeLifecycle] Init failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Called from OpticSight.OnEnable patch. Caches the OpticSight and checks state.
        /// </summary>
        public static void OnOpticEnabled(OpticSight os)
        {
            if (os != null)
                _lastEnabledOptic = os;

            // If already scoped and a DIFFERENT optic enables → genuine mode switch.
            // Guard against sibling mode_000/mode_001 co-activating on scope enter, which
            // would falsely trigger a restore+recut cycle and cause a 1-2 frame mesh flash.
            if (_isScoped && os != null && os != _activeOptic)
            {
                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[ScopeLifecycle] Mode switch while scoped: '{(_activeOptic != null ? _activeOptic.name : "?")}' → '{os.name}'");

                // Update the active optic to the new mode
                _activeOptic = os;

                float maxMag = ZoomController.GetMaxMagnification(os);
                bool bypassForMode = ScopeHousingMeshSurgeryPlugin.AutoDisableForHighMagnificationScopes.Value
                    && maxMag > 10f;
                if (bypassForMode)
                {
                    _modBypassedForCurrentScope = true;
                    ReticleRenderer.Cleanup();
                    ScopeEffectsRenderer.Cleanup();
                    LensTransparency.RestoreAll();
                    CameraSettingsManager.Restore();
                    PiPDisabler.RestoreAllCameras();
                    if (ScopeHousingMeshSurgeryPlugin.RestoreOnUnscope.Value)
                        MeshSurgeryManager.RestoreForScope(os.transform);
                    PlaneVisualizer.Hide();
                    ZeroingController.Reset();
                    return;
                }

                _modBypassedForCurrentScope = false;

                // Re-extract reticle from the NEW mode's linza
                ReticleRenderer.Cleanup();
                ReticleRenderer.ExtractReticle(os);

                // Re-hide lenses (the new mode's lens might not be hidden yet)
                LensTransparency.HideAllLensSurfaces(os);

                // Show reticle for the new mode (with magnification scaling)
                float modeMag = ZoomController.GetMagnification(os);
                ReticleRenderer.Show(os, modeMag);

                // Notify FOV controller the mode changed so it re-reads ScopeCameraData
                FovController.OnModeSwitch();

                // RESTORE all meshes first, then re-cut with new mode's plane position.
                if (ScopeHousingMeshSurgeryPlugin.EnableMeshSurgery.Value)
                {
                    MeshSurgeryManager.RestoreForScope(os.transform);
                    MeshSurgeryManager.ApplyForOptic(os);
                }

                // Re-apply camera settings for the new mode's FOV
                CameraSettingsManager.ApplyForOptic(os);

                // Animated FOV change for mode switch (uses configured duration)
                ApplyFov(true);
            }

            CheckAndUpdate();
        }

        /// <summary>
        /// Called from OpticSight.OnDisable patch. Checks if we should exit.
        /// </summary>
        public static void OnOpticDisabled(OpticSight os)
        {
            CheckAndUpdate();
        }

        /// <summary>
        /// Core state check. Reads PWA state via reflection.
        /// Called from ChangeAimingMode patch and Update safety net.
        /// </summary>
        public static void CheckAndUpdate()
        {
            if (!_reflectionReady) return;

            bool shouldBeScoped = false;
            string reason = "unknown";

            try
            {
                var player = GetLocalPlayer();
                if (player == null) { reason = "no player"; goto evaluate; }

                var pwa = player.ProceduralWeaponAnimation;
                if (pwa == null) { reason = "no PWA"; goto evaluate; }

                bool isAiming = (bool)_isAimingProp.GetValue(pwa);
                if (!isAiming) { reason = "not aiming"; goto evaluate; }

                object currentScope = _currentScopeProp.GetValue(pwa);
                if (currentScope == null) { reason = "no CurrentScope"; goto evaluate; }

                bool isOptic = (bool)_isOpticProp.GetValue(currentScope);
                if (!isOptic) { reason = "not optic"; goto evaluate; }

                var enabledOs = FindEnabledOpticFromPWA();
                if (enabledOs == null)
                {
                    // Hybrid toggle case: CurrentScope may still report optic while the
                    // enabled OpticSight switched off (e.g., now in collimator mode).
                    // Force scope exit immediately so RestoreAll runs without waiting for
                    // a full ADS exit.
                    shouldBeScoped = false;
                    reason = "optic flag true but no enabled OpticSight";
                    goto evaluate;
                }

                shouldBeScoped = true;
                _activeOptic = enabledOs;
                _lastEnabledOptic = enabledOs;
                reason = "aiming+optic+enabled OpticSight";
            }
            catch (Exception ex) { reason = $"exception: {ex.Message}"; }

            evaluate:

            // Log every state CHANGE (not every frame)
            if (shouldBeScoped != _isScoped)
            {
                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[ScopeLifecycle] State change: {(_isScoped ? "SCOPED" : "NOT_SCOPED")} → " +
                    $"{(shouldBeScoped ? "SCOPED" : "NOT_SCOPED")} reason='{reason}' " +
                    $"caller={GetCaller()} frame={Time.frameCount}");
            }

            if (shouldBeScoped && !_isScoped)
            {
                DoScopeEnter();
            }
            else if (!shouldBeScoped && _isScoped)
            {
                DoScopeExit();
            }
        }

        /// <summary>Identifies what called CheckAndUpdate for debugging.</summary>
        private static string GetCaller()
        {
            try
            {
                var st = new System.Diagnostics.StackTrace(2, false);
                if (st.FrameCount > 0)
                {
                    var method = st.GetFrame(0)?.GetMethod();
                    if (method != null)
                        return $"{method.DeclaringType?.Name}.{method.Name}";
                }
            }
            catch { }
            return "?";
        }

        /// <summary>
        /// Per-frame maintenance. Zero allocations when scoped (just bool checks).
        /// Called from plugin Update.
        /// </summary>
        public static void Tick()
        {
            if (!_isScoped) return;
            if (_modBypassedForCurrentScope) return;

            // Ensure lens stays hidden + update variable zoom
            if (ZoomController.IsActive)
            {
                if (_activeOptic != null)
                {
                    float mag = ZoomController.GetMagnification(_activeOptic);
                    ZoomController.SetZoom(mag);
                    // Update reticle + effects position (smoothed), rotation, and scale each frame
                    ReticleRenderer.UpdateTransform(mag);
                    ScopeEffectsRenderer.UpdateTransform(baseSize: 0f, magnification: mag);
                }
                ZoomController.EnsureLensVisible();

                // ALWAYS re-kill OTHER lens surfaces even when shader zoom is active.
                // Pass the ZoomController's managed renderer as exclusion so it stays alive
                // for the zoom shader, while any other glass/linza surfaces EFT restores
                // get killed again immediately.
                LensTransparency.EnsureHidden(ZoomController.ActiveLensRenderer);
            }
            else
            {
                LensTransparency.EnsureHidden();

                // Even without shader zoom, update reticle position/rotation/scale
                if (_activeOptic != null)
                {
                    float mag = ZoomController.GetMagnification(_activeOptic);
                    ReticleRenderer.UpdateTransform(mag);
                    ScopeEffectsRenderer.UpdateTransform(baseSize: 0f, magnification: mag);
                }
            }

            // PiP stays disabled via Harmony patches — no per-frame action needed.

            // Zeroing input polling
            ZeroingController.Tick();
        }

        /// <summary>
        /// Force exit all mod effects (called on global toggle off or scene change).
        /// Clears cached optic so stale references don't ghost into the next session.
        /// </summary>
        public static void ForceExit()
        {
            if (_isScoped)
                DoScopeExit();
            _modBypassedForCurrentScope = false;
            // Always clear the last-enabled cache so a stale OpticSight reference
            // from before the disable doesn't get used on the next scope enter.
            _lastEnabledOptic = null;
        }

        /// <summary>
        /// Called when the mod is re-enabled at runtime.
        /// Immediately reads current PWA state so the mod catches up if the player
        /// is already scoped — without this, effects stay absent until the next
        /// scope enter/exit event fires.
        /// </summary>
        public static void SyncState()
        {
            // _lastEnabledOptic was cleared by ForceExit; if we're already scoped
            // FindOpticFromPWA() will locate the active one when DoScopeEnter fires.
            CheckAndUpdate();
        }

        // ===== State transitions =====

        private static void DoScopeEnter()
        {
            // Get the OpticSight — cached from OnEnable, or find from pwa
            var os = _lastEnabledOptic;
            if (os == null)
                os = FindOpticFromPWA();
            if (os == null)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    "[ScopeLifecycle] ENTER aborted — no OpticSight found");
                return;
            }

            _isScoped = true;
            _activeOptic = os;

            float maxMag = ZoomController.GetMaxMagnification(os);
            _modBypassedForCurrentScope = ScopeHousingMeshSurgeryPlugin.AutoDisableForHighMagnificationScopes.Value
                && maxMag > 10f;
            if (_modBypassedForCurrentScope)
            {
                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[ScopeLifecycle] Bypassing mod for high-magnification scope: max={maxMag:F1}x (>10x)");

                LensTransparency.RestoreAll();
                CameraSettingsManager.Restore();
                PiPDisabler.RestoreAllCameras();
                PlaneVisualizer.Hide();
                ZeroingController.Reset();
                return;
            }

            // Check blacklist before doing anything — blacklisted scopes skip surgery + reticle
            var scopeRootForBlacklist = ScopeHierarchy.FindScopeRoot(os.transform);
            string rootNameForBlacklist = scopeRootForBlacklist != null ? scopeRootForBlacklist.name : "";
            bool isBlacklisted = ScopeDiagnostics.IsBlacklisted(rootNameForBlacklist);

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[ScopeLifecycle] ENTER: '{os.name}' root='{rootNameForBlacklist}' " +
                $"blacklisted={isBlacklisted} frame={Time.frameCount}");

            // 1. Extract reticle texture BEFORE destroying lens mesh
            if (!isBlacklisted)
                ReticleRenderer.ExtractReticle(os);

            // 2. Hide ALL lens surfaces in the scope hierarchy (once)
            LensTransparency.HideAllLensSurfaces(os);

            // 3. Get magnification for reticle scaling and zoom
            float mag = ZoomController.GetMagnification(os);

            // 4. Show reticle overlay at the lens position, scaled for magnification
            if (!isBlacklisted)
                ReticleRenderer.Show(os, mag);

            // 4b. Show lens vignette + scope shadow effects (always shown, even for blacklisted scopes)
            Transform lensT = os.LensRenderer != null ? os.LensRenderer.transform : os.transform;
            float baseSize = ScopeHousingMeshSurgeryPlugin.ReticleBaseSize.Value;
            if (baseSize < 0.001f) baseSize = ScopeHousingMeshSurgeryPlugin.CylinderRadius.Value * 2f;
            if (baseSize < 0.001f) baseSize = 0.030f;
            ScopeEffectsRenderer.Show(lensT, baseSize, mag);

            // 5. Shader zoom (if available)
            if (ZoomController.ShaderAvailable && ScopeHousingMeshSurgeryPlugin.EnableShaderZoom.Value)
            {
                ZoomController.Apply(os, mag);
            }

            // 5. Mesh surgery (once — skipped for blacklisted scopes)
            if (!isBlacklisted && ScopeHousingMeshSurgeryPlugin.EnableMeshSurgery.Value)
                MeshSurgeryManager.ApplyForOptic(os);

            // 6. Show cut plane visualizer (even without mesh surgery, for debugging)
            if (ScopeHousingMeshSurgeryPlugin.ShowCutPlane.Value
                && !ScopeHousingMeshSurgeryPlugin.EnableMeshSurgery.Value)
            {
                ShowPlaneOnly(os);
            }

            // 7. Swap main camera LOD/culling settings with scope camera settings
            CameraSettingsManager.ApplyForOptic(os);

            // 8. Apply animated FOV zoom (uses FovAnimationDuration)
            ApplyFov(true);

            // 9. Read initial zeroing distance
            ZeroingController.ReadCurrentZeroing();
        }

        private static void DoScopeExit()
        {
            if (!_isScoped) return;

            var prevOptic = _activeOptic;
            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[ScopeLifecycle] EXIT: '{(prevOptic != null ? prevOptic.name : "null")}' frame={Time.frameCount}");

            _isScoped = false;
            _activeOptic = null;

            // If this scope was bypassed (high magnification), skip mod cleanup paths.
            if (_modBypassedForCurrentScope)
            {
                _modBypassedForCurrentScope = false;
                return;
            }

            // 1. Restore FOV INSTANTLY (duration=0, no sluggish exit feel)
            RestoreFov();

            // 2. Restore zoom controller
            ZoomController.Restore();

            // 2b. Always reset scroll zoom (Restore only runs for shader zoom)
            ZoomController.ResetScrollZoom();

            // 3. Hide reticle overlay + scope effects
            ReticleRenderer.Cleanup();
            ScopeEffectsRenderer.Cleanup();

            // 4. Restore lens
            LensTransparency.RestoreAll();

            // 5. Restore camera LOD/culling settings
            CameraSettingsManager.Restore();

            // 6. Restore meshes
            if (ScopeHousingMeshSurgeryPlugin.RestoreOnUnscope.Value)
            {
                if (prevOptic != null)
                    MeshSurgeryManager.RestoreForScope(prevOptic.transform);
                else
                    MeshSurgeryManager.RestoreAll();
            }

            // 7. Hide plane visualizer
            PlaneVisualizer.Hide();

            // 8. Reset zeroing state
            ZeroingController.Reset();
        }

        // ===== FOV Helpers =====

        /// <summary>
        /// Public entry point to re-apply FOV zoom.
        /// Called when scroll wheel changes magnification so the FOV updates
        /// to match the new zoom level without waiting for the next method_23 call.
        /// Uses a short duration for responsive feel.
        /// </summary>
        public static void ReapplyFov()
        {
            if (!_isScoped) return;
            if (_modBypassedForCurrentScope) return;
            ApplyFov(false); // false = short duration for scroll feel
        }

        /// <summary>
        /// Apply the FOV zoom for the current scope, with configurable animation duration.
        /// isTransition=true uses FovAnimationDuration config (scope enter / mode switch).
        /// </summary>
        private static void ApplyFov(bool isTransition)
        {
            try
            {
                if (_modBypassedForCurrentScope) return;
                if (!ScopeHousingMeshSurgeryPlugin.EnableZoom.Value) return;
                if (ZoomController.ShaderAvailable && ScopeHousingMeshSurgeryPlugin.EnableShaderZoom.Value)
                    return; // Shader zoom mode doesn't need FOV changes

                if (!CameraClass.Exist) return;

                var player = GetLocalPlayer();
                if (player == null) return;
                var pwa = player.ProceduralWeaponAnimation;
                if (pwa == null) return;

                float playerBaseFov = pwa.Single_2;
                float zoomBaseFov = FovController.ZoomBaselineFov;
                float zoomedFov = FovController.ComputeZoomedFov(playerBaseFov, pwa);

                if (zoomedFov >= 0.5f && zoomedFov < zoomBaseFov)
                {
                    float duration = isTransition
                        ? ScopeHousingMeshSurgeryPlugin.FovAnimationDuration.Value
                        : 0.1f; // Short duration for variable zoom updates

                    CameraClass.Instance.SetFov(zoomedFov, duration, false);
                    if (ScopeHousingMeshSurgeryPlugin.EnableWeaponFovScale.Value)
                        Patches.CalculateScaleValueByFovPatch.UpdateRibcageScale(ScopeHousingMeshSurgeryPlugin.WeaponFovScale.Value);
                    ScopeHousingMeshSurgeryPlugin.LogInfo(
                        $"[ScopeLifecycle] ApplyFov: {zoomedFov:F1}° dur={duration:F2}s");
                }
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[ScopeLifecycle] ApplyFov error: {ex.Message}");
            }
        }

        /// <summary>
        /// Restore FOV to baseline INSTANTLY (duration=0).
        /// Scope exit should feel snappy, never sluggish.
        /// </summary>
        private static void RestoreFov()
        {
            try
            {
                if (!CameraClass.Exist) return;
                var cc = CameraClass.Instance;
                if (cc == null) return;

                var player = GetLocalPlayer();
                if (player == null) return;
                var pwa = player.ProceduralWeaponAnimation;
                if (pwa == null) return;

                float baseFov = pwa.Single_2;
                if (baseFov > 30f)
                {
                    cc.SetFov(baseFov, 0f, true); // duration=0 = instant
                    if (ScopeHousingMeshSurgeryPlugin.EnableWeaponFovScale.Value)
                        Patches.CalculateScaleValueByFovPatch.UpdateRibcageScale(ScopeHousingMeshSurgeryPlugin.WeaponFovScale.Value);
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        $"[ScopeLifecycle] RestoreFov: {baseFov:F1}° (instant)");
                }
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[ScopeLifecycle] RestoreFov error: {ex.Message}");
            }
        }

        // ===== Other Helpers =====

        /// <summary>
        /// Computes the cut plane and shows the visualizer without actually cutting.
        /// </summary>
        private static void ShowPlaneOnly(OpticSight os)
        {
            try
            {
                var scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
                if (scopeRoot == null) return;

                Transform activeMode = os.transform;
                if (!ScopeHierarchy.TryGetPlane(os, scopeRoot, activeMode,
                    out var planePoint, out var planeNormal, out var camPos))
                    return;

                planePoint += planeNormal * ScopeHousingMeshSurgeryPlugin.PlaneOffsetMeters.Value;
                PlaneVisualizer.Show(planePoint, planeNormal);
            }
            catch { }
        }

        private static Player GetLocalPlayer()
        {
            try
            {
                var gw = Singleton<GameWorld>.Instance;
                return gw != null ? gw.MainPlayer : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Fallback: find OpticSight if _lastEnabledOptic wasn't cached.
        /// Uses FindObjectsOfType as last resort.
        /// </summary>
        private static OpticSight FindOpticFromPWA()
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<OpticSight>();
                foreach (var os in all)
                {
                    if (os != null && os.isActiveAndEnabled)
                        return os;
                }
                foreach (var os in all)
                {
                    if (os != null) return os;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Finds an enabled OpticSight, preferring active and cached instances before
        /// a global search.
        /// </summary>
        private static OpticSight FindEnabledOpticFromPWA()
        {
            if (_activeOptic != null && _activeOptic.isActiveAndEnabled)
                return _activeOptic;

            if (_lastEnabledOptic != null && _lastEnabledOptic.isActiveAndEnabled)
                return _lastEnabledOptic;

            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<OpticSight>();
                foreach (var os in all)
                {
                    if (os != null && os.isActiveAndEnabled)
                        return os;
                }
            }
            catch { }

            return null;
        }
    }
}
