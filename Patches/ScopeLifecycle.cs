using System;
using System.Reflection;
using System.Collections.Generic;
using EFT;
using EFT.Animations;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace PiPDisabler
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

        // Fast delegates (avoid PropertyInfo.GetValue overhead per frame)
        private static Func<ProceduralWeaponAnimation, bool> _getIsAiming;
        private static Func<ProceduralWeaponAnimation, object> _getCurrentScope;
        private static Func<object, bool> _getIsOptic;

        // State
        private static bool _isScoped;
        private static OpticSight _activeOptic;
        private static OpticSight _lastEnabledOptic; // cache from OnEnable
        private static bool _modBypassedForCurrentScope;
        private static bool _restoreOneXFovOnScopeExit;

        // Mesh surgery retry: when the initial cut on scope enter produces zero
        // entries (GPU buffers not ready, transforms not settled), retry on
        // subsequent frames until it succeeds or we hit the attempt limit.
        private static int _meshRetryAttemptsLeft;
        private static int _meshRetryNextFrame;
        private const int MeshRetryMaxAttempts = 10;
        private const int MeshRetryFrameInterval = 3;

        // Post-exit FOV restore: suppresses EFT's method_23 SetFov calls while our
        // restore coroutine is animating. Without this, EFT's SetFov(35°) kills the
        // coroutine immediately, causing the FOV to flash on ADS exit.
        private static float _postExitRestoreFov;
        private static float _postExitRestoreExpiry; // Time.realtimeSinceStartup

        public static bool HasPostExitRestore =>
            _postExitRestoreFov > 0.5f &&
            Time.realtimeSinceStartup < _postExitRestoreExpiry;
        public static float PostExitRestoreFov => _postExitRestoreFov;

        private static readonly HashSet<string> _scopeWhitelistNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string _scopeWhitelistRawCached;


        public static bool IsScoped => _isScoped;
        public static bool IsModBypassedForCurrentScope => _modBypassedForCurrentScope;
        public static OpticSight ActiveOptic => _activeOptic;

        /// <summary>
        /// Shared optic classification helper for other systems that must make
        /// early decisions before scope state is fully transitioned.
        /// </summary>
        internal static bool IsThermalOrNightVisionOpticForBypass(OpticSight os)
        {
            return IsThermalOrNightVisionOptic(os);
        }

        /// <summary>
        /// Returns true if the given optic matches the AutoBypassNameContains pattern.
        /// Callable from PiPDisabler patches which have a concrete OpticSight reference.
        /// </summary>
        internal static bool IsNameBypassed(OpticSight os)
        {
            return ScopeNameMatchesBypassPattern(os);
        }

        /// <summary>
        /// Returns true if the most recently enabled OpticSight (as seen by the
        /// OnEnable patch — set before CheckAndUpdate / PWA check) matches
        /// AutoBypassNameContains and is still enabled in the scene.
        /// Used by PiPDisabler.ShouldAllowVanillaPiP() which has no concrete
        /// OpticSight but must decide per-frame whether to restore vanilla PiP.
        /// </summary>
        internal static bool IsLastOpticNameBypassed()
        {
            var os = _lastEnabledOptic;
            if (os == null) return false;
            try { if (!os.enabled) return false; } catch { return false; }
            return ScopeNameMatchesBypassPattern(os);
        }

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

                // Build fast getter delegates to avoid PropertyInfo.GetValue overhead per frame
                if (_reflectionReady)
                {
                    try
                    {
                        _getIsAiming = (Func<ProceduralWeaponAnimation, bool>)
                            Delegate.CreateDelegate(typeof(Func<ProceduralWeaponAnimation, bool>),
                                _isAimingProp.GetGetMethod(true));
                    }
                    catch
                    {
                        _getIsAiming = pwa => (bool)_isAimingProp.GetValue(pwa);
                    }

                    try
                    {
                        var getter = _currentScopeProp.GetGetMethod(true);
                        // CurrentScope returns SightNBone (value may be null), use generic Func
                        _getCurrentScope = pwa => getter.Invoke(pwa, null);
                    }
                    catch
                    {
                        _getCurrentScope = pwa => _currentScopeProp.GetValue(pwa);
                    }

                    try
                    {
                        var scopeType = _currentScopeProp.PropertyType;
                        var isOpticGetter = _isOpticProp.GetGetMethod(true);
                        _getIsOptic = scope => (bool)isOpticGetter.Invoke(scope, null);
                    }
                    catch
                    {
                        _getIsOptic = scope => (bool)_isOpticProp.GetValue(scope);
                    }
                }

                PiPDisablerPlugin.LogInfo(
                    $"[ScopeLifecycle] Reflection: IsAiming={_isAimingProp != null}, " +
                    $"CurrentScope={_currentScopeProp != null}, IsOptic={_isOpticProp != null}");
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogError($"[ScopeLifecycle] Init failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Called from OpticSight.OnEnable patch. Caches the OpticSight and checks state.
        /// </summary>
        public static void OnOpticEnabled(OpticSight os)
        {
            if (os != null)
                _lastEnabledOptic = os;

            // If NOT scoped but aiming and an OpticSight just enabled, this is a
            // collimator→magnified switch while already ADS (e.g. LCO starting in
            // red-dot mode). Log it distinctly; CheckAndUpdate below will call DoScopeEnter.
            if (!_isScoped && os != null)
            {
                try
                {
                    var player = GetLocalPlayer();
                    var pwa = player?.ProceduralWeaponAnimation;
                    if (pwa != null && _reflectionReady && _getIsAiming(pwa))
                    {
                        PiPDisablerPlugin.LogInfo(
                            $"[ScopeLifecycle] Collimator→optic switch while ADS: " +
                            $"'{os.name}'[{FovController.GetOpticTemplateId(os)}] — treating as scope enter");
                    }
                }
                catch { }
            }

            // If already scoped and a DIFFERENT optic enables → genuine mode switch.
            // Guard against sibling mode_000/mode_001 co-activating on scope enter, which
            // would falsely trigger a restore+recut cycle and cause a 1-2 frame mesh flash.
            if (_isScoped && os != null && os != _activeOptic)
            {
                PiPDisablerPlugin.LogInfo(
                    $"[ScopeLifecycle] Mode switch while scoped: " +
                    $"'{(_activeOptic != null ? _activeOptic.name : "?")}'[{FovController.GetOpticTemplateId(_activeOptic)}] → " +
                    $"'{os.name}'[{FovController.GetOpticTemplateId(os)}]");

                // Update the active optic to the new mode
                _activeOptic = os;

                float minFov = ZoomController.GetMinFov(os);
                bool bypassForMode = ShouldBypassForCurrentOptic(os, minFov);
                if (bypassForMode)
                {
                    _modBypassedForCurrentScope = true;
                    ApplyBypassState(os, minFov, reason: "mode switch",
                        restoreFov: true);
                    return;
                }

                _modBypassedForCurrentScope = false;

                // Re-extract reticle from the NEW mode's linza
                ReticleRenderer.Cleanup();
                ReticleRenderer.ExtractReticle(os);

                // Re-hide lenses (the new mode's lens might not be hidden yet)
                LensTransparency.HideAllLensSurfaces(os);

                // Recollect lens renderers for the new mode's geometry.
                ReticleRenderer.SetLensMaskEntries(CollectStencilEntries(os));

                // Notify FOV controller the mode changed so it re-reads ScopeCameraData
                FovController.OnModeSwitch();

                // RESTORE all meshes first, then re-cut with new mode's plane position.
                _meshRetryAttemptsLeft = 0;
                if (PiPDisablerPlugin.EnableMeshSurgery.Value)
                {
                    MeshSurgeryManager.RestoreForScope(os.transform);
                    MeshSurgeryManager.ApplyForOptic(os);

                    if (!MeshSurgeryManager.HasSuccessfulCut())
                    {
                        PiPDisablerPlugin.LogInfo(
                            $"[ScopeLifecycle][DEBUG] Mode-switch mesh surgery produced zero cuts — " +
                            $"scheduling retry. frame={Time.frameCount}");
                        _meshRetryAttemptsLeft = MeshRetryMaxAttempts;
                        _meshRetryNextFrame = Time.frameCount + MeshRetryFrameInterval;
                    }
                }

                // Re-apply camera settings for the new mode's FOV
                CameraSettingsManager.ApplyForOptic(os);

                // Capture weapon base scale/FOV before FOV changes
                Patches.WeaponScalingPatch.CaptureBaseState();

                // If freelooking, defer reticle/effects/FOV — they'll be restored
                // when freelook ends via FreelookTracker.OnFreelookExit().
                if (!FreelookTracker.IsFreelooking)
                {
                    // Show reticle for the new mode (with magnification scaling)
                    float modeMag = ZoomController.GetMagnification(os);
                    ReticleRenderer.Show(os, modeMag);

                    // Re-show scope effects so their CommandBuffer is re-ordered
                    // AFTER ReticleRenderer's CB. Without this, the shadow's stencil
                    // test reads before the stencil is written and the mask breaks.
                    ScopeEffectsRenderer.Show();

                    // Animated FOV change for mode switch (uses configured duration)
                    ApplyFov(true);
                }
            }

            CheckAndUpdate("OnOpticEnabled");
        }

        /// <summary>
        /// Called from OpticSight.OnDisable patch. Checks if we should exit.
        /// </summary>
        public static void OnOpticDisabled(OpticSight os)
        {
            ReticleRenderer.Hide();
            ScopeEffectsRenderer.Hide();
            CheckAndUpdate("OnOpticDisabled");
        }

        /// <summary>
        /// Core state check. Reads PWA state via reflection.
        /// Called from ChangeAimingMode patch and Update safety net.
        /// </summary>
        public static void CheckAndUpdate(string caller = "Update")
        {
            _lastCaller = caller;
            if (!_reflectionReady) return;

            bool shouldBeScoped = false;
            bool exitingToNonOpticWhileAiming = false;
            string reason = "unknown";

            try
            {
                var player = GetLocalPlayer();
                if (player == null) { reason = "no player"; goto evaluate; }

                var pwa = player.ProceduralWeaponAnimation;
                if (pwa == null) { reason = "no PWA"; goto evaluate; }

                bool isAiming = _getIsAiming(pwa);
                if (!isAiming) { reason = "not aiming"; goto evaluate; }

                object currentScope = _getCurrentScope(pwa);
                if (currentScope == null) { reason = "no CurrentScope"; goto evaluate; }

                bool isOptic = _getIsOptic(currentScope);

                var enabledOs = FindEnabledOpticFromPWA();

                // On hybrid transitions, CurrentScope.IsOptic can lag one frame behind
                // while OpticSight.OnEnable already fired. Prefer enabled OpticSight.
                if (enabledOs != null)
                {
                    shouldBeScoped = true;
                    _activeOptic = enabledOs;
                    _lastEnabledOptic = enabledOs;
                    reason = isOptic
                        ? "aiming+optic+enabled OpticSight"
                        : "aiming+enabled OpticSight (CurrentScope lag)";
                }
                else if (!isOptic)
                {
                    reason = "not optic";
                    exitingToNonOpticWhileAiming = true;
                    goto evaluate;
                }
                else
                {
                    // Hybrid toggle case: CurrentScope may still report optic while the
                    // enabled OpticSight switched off (e.g., now in collimator mode).
                    // Force scope exit immediately so RestoreAll runs without waiting for
                    // a full ADS exit.
                    shouldBeScoped = false;
                    reason = "optic flag true but no enabled OpticSight";
                    exitingToNonOpticWhileAiming = true;
                    goto evaluate;
                }
            }
            catch (Exception ex) { reason = $"exception: {ex.Message}"; }

            evaluate:

            // Log every state CHANGE (not every frame)
            if (shouldBeScoped != _isScoped)
            {
                PiPDisablerPlugin.LogInfo(
                    $"[ScopeLifecycle] State change: {(_isScoped ? "SCOPED" : "NOT_SCOPED")} → " +
                    $"{(shouldBeScoped ? "SCOPED" : "NOT_SCOPED")} reason='{reason}' " +
                    $"caller={_lastCaller} frame={Time.frameCount}");
            }

            if (shouldBeScoped && !_isScoped)
            {
                _restoreOneXFovOnScopeExit = false;
                DoScopeEnter();
            }
            else if (!shouldBeScoped && _isScoped)
            {
                _restoreOneXFovOnScopeExit = exitingToNonOpticWhileAiming;
                DoScopeExit();
            }
        }

        // Caller tag for lightweight state-change logging (replaces expensive StackTrace)
        private static string _lastCaller = "?";

        /// <summary>
        /// Per-frame maintenance. Zero allocations when scoped (just bool checks).
        /// Called from plugin Update.
        /// </summary>
        public static void Tick()
        {
            if (!_isScoped) return;
            if (_modBypassedForCurrentScope) return;

            // ── Freelook tracking ────────────────────────────────────────
            // Poll each frame.  Returns true on the exact frame freelook ends.
            bool freelookJustEnded = FreelookTracker.Tick();

            if (FreelookTracker.IsFreelooking)
            {
                // While freelooking: skip all mod per-frame updates.
                // Camera rotation is unlocked (checked in ReticleRenderer.OnPreCull).
                // FOV override is skipped (checked in PWAMethod23Patch).
                // Reticle/effects are hidden (done by FreelookTracker.OnFreelookEnter).
                return;
            }

            if (freelookJustEnded)
            {
                // FOV is restored by two complementary paths:
                //   1. FreelookTracker.OnFreelookExit() called SetFov directly (Update).
                //   2. PlayerLookPatch transpiler intercepts Player.Look's SetFov(35)
                //      in LateUpdate and substitutes the pre-freelook snapshot.
                // Re-hide lenses in case EFT restored them during freelook.
                LensTransparency.EnsureHidden();
            }

            // Keep lens transparent if EFT restores the original materials
            LensTransparency.EnsureHidden();

            // ── Mesh surgery retry ─────────────────────────────────────────
            // If the initial cut on scope enter produced zero entries (GPU buffers
            // weren't uploaded yet, or transforms hadn't settled), retry here.
            if (_meshRetryAttemptsLeft > 0
                && PiPDisablerPlugin.EnableMeshSurgery.Value
                && _activeOptic != null
                && Time.frameCount >= _meshRetryNextFrame)
            {
                _meshRetryAttemptsLeft--;
                PiPDisablerPlugin.LogInfo(
                    $"[ScopeLifecycle][DEBUG] Mesh surgery retry attempt " +
                    $"({MeshRetryMaxAttempts - _meshRetryAttemptsLeft}/{MeshRetryMaxAttempts}) " +
                    $"frame={Time.frameCount}");

                bool success = MeshSurgeryManager.RetryPendingCut(_activeOptic);
                if (success)
                {
                    PiPDisablerPlugin.LogInfo(
                        $"[ScopeLifecycle] Mesh surgery retry SUCCEEDED on attempt " +
                        $"{MeshRetryMaxAttempts - _meshRetryAttemptsLeft}. frame={Time.frameCount}");
                    _meshRetryAttemptsLeft = 0;
                }
                else
                {
                    _meshRetryNextFrame = Time.frameCount + MeshRetryFrameInterval;
                    if (_meshRetryAttemptsLeft == 0)
                    {
                        PiPDisablerPlugin.LogInfo(
                            $"[ScopeLifecycle] Mesh surgery retry EXHAUSTED all {MeshRetryMaxAttempts} attempts. " +
                            $"frame={Time.frameCount}");
                    }
                }
            }

            // Update reticle position/rotation/scale and effects
            if (_activeOptic != null)
            {
                float mag = ZoomController.GetMagnification(_activeOptic);
                ReticleRenderer.UpdateTransform(mag);
                ScopeEffectsRenderer.UpdateTransform();
            }

            // PiP stays disabled via Harmony patches — no per-frame action needed.

            // Per-frame weapon scale compensation (tracks animated FOV transitions)
            Patches.WeaponScalingPatch.UpdateScale();

            // Zeroing input polling
            ZeroingController.Tick();
        }

        /// <summary>
        /// Force exit all mod effects (called on global toggle off or scene change).
        /// Clears cached optic so stale references don't ghost into the next session.
        /// </summary>
        public static void ForceExit()
        {
            _meshRetryAttemptsLeft = 0;
            FreelookTracker.Reset();
            if (_isScoped)
                DoScopeExit();
            ReticleRenderer.Cleanup();
            ScopeEffectsRenderer.Cleanup();
            ZoomController.Restore();
            Patches.WeaponScalingPatch.RestoreScale();
            CameraSettingsManager.Restore();
            PlaneVisualizer.Hide();
            ZeroingController.Reset();
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
            // _lastEnabledOptic was cleared by ForceExit; CheckAndUpdate will rely on
            // the latest enabled optic cache when DoScopeEnter runs.
            CheckAndUpdate("SyncState");
        }

        /// <summary>
        /// Adds/removes the current scope whitelist key in ScopeWhitelistNames.
        /// </summary>
        public static string GetActiveScopeWhitelistKey()
        {
            var os = _activeOptic != null ? _activeOptic : _lastEnabledOptic;
            return ResolveWhitelistScopeKey(os);
        }

        public static void ToggleActiveScopeWhitelistEntry()
        {
            var os = _activeOptic;
            if (os == null)
                os = _lastEnabledOptic;

            if (os == null)
            {
                PiPDisablerPlugin.LogInfo("[ScopeLifecycle] Whitelist toggle ignored: no active scope");
                return;
            }

            string scopeName = ResolveWhitelistScopeKey(os);
            if (string.IsNullOrWhiteSpace(scopeName))
            {
                PiPDisablerPlugin.LogWarn(
                    $"[ScopeLifecycle] Whitelist toggle ignored: no usable scope key for '{os.name}'");
                return;
            }

            RefreshScopeWhitelistCache();

            bool removed;
            if (_scopeWhitelistNames.Contains(scopeName))
            {
                _scopeWhitelistNames.Remove(scopeName);
                removed = true;
            }
            else
            {
                _scopeWhitelistNames.Add(scopeName);
                removed = false;
            }

            PiPDisablerPlugin.ScopeWhitelistNames.Value = string.Join(",", _scopeWhitelistNames);
            _scopeWhitelistRawCached = PiPDisablerPlugin.ScopeWhitelistNames.Value ?? string.Empty;

            PiPDisablerPlugin.LogInfo(
                $"[ScopeLifecycle] Whitelist {(removed ? "removed" : "added")}: scopeKey='{scopeName}'");

            if (PiPDisablerPlugin.ModEnabled.Value && _isScoped)
            {
                ForceExit();
                SyncState();
            }
        }
        private static string ResolveWhitelistScopeKey(OpticSight os)
        {
            if (os == null) return null;

            // Primary: derive from the object under mod_scope that is not a mount.
            string modScopeName = ResolveNameUnderModScope(os.transform);
            if (!string.IsNullOrWhiteSpace(modScopeName))
                return modScopeName;

            // Prefer the runtime object name over template metadata.
            // Template reflection can momentarily report stale values on mode switches.
            string objectName = NormalizeScopeKey(os.name);
            if (!string.IsNullOrWhiteSpace(objectName))
                return objectName;

            // Fallback for atypical hierarchies.
            string templateName = FovController.GetOpticTemplateName(os);
            if (!string.IsNullOrWhiteSpace(templateName)
                && !string.Equals(templateName, "unknown", StringComparison.OrdinalIgnoreCase))
                return templateName;

            string templateId = FovController.GetOpticTemplateId(os);
            if (!string.IsNullOrWhiteSpace(templateId)
                && !string.Equals(templateId, "unknown", StringComparison.OrdinalIgnoreCase))
                return templateId;

            return null;
        }

        private static string ResolveNameUnderModScope(Transform opticTransform)
        {
            if (opticTransform == null) return null;

            Transform modScope = null;
            for (var t = opticTransform; t != null; t = t.parent)
            {
                if (ContainsCI(t.name, "mod_scope"))
                {
                    modScope = t;
                    break;
                }
            }

            if (modScope == null) return null;

            // Walk up the active optic path: pick first node under mod_scope that
            // is not a mount/mode container.
            for (var t = opticTransform; t != null && t != modScope; t = t.parent)
            {
                if (!IsUsableScopeNameNode(t.name)) continue;
                return NormalizeScopeKey(t.name);
            }

            return null;
        }

        private static bool IsUsableScopeNameNode(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (ContainsCI(name, "mount")) return false;
            if (ContainsCI(name, "mod_scope")) return false;
            if (name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Equals("mode", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static string NormalizeScopeKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string key = raw.Trim();
            if (key.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase))
                key = key.Substring(0, key.Length - "(Clone)".Length).Trim();
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }

        private static bool ContainsCI(string s, string token)
        {
            if (s == null || token == null) return false;
            return s.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Called from SetScopeMode patches after EFT applies scope state changes.
        /// Re-applies zoom/FOV immediately while scoped.
        /// </summary>
        public static void OnSetScopeMode()
        {
            if (!_isScoped) return;
            if (!PiPDisablerPlugin.ModEnabled.Value) return;

            PiPDisablerPlugin.LogInfo(
                $"[ScopeLifecycle] SetScopeMode fired while scoped frame={Time.frameCount}");

            var os = _activeOptic;
            if (os == null) return;

            if (_modBypassedForCurrentScope) return;

            FovController.OnModeSwitch();
            if (!FreelookTracker.IsFreelooking)
                ApplyFov(true);
        }

        private static bool ShouldBypassForCurrentOptic(OpticSight os, float minFov)
        {
            _ = minFov;
            if (os == null) return false;

            if (ShouldBypassByWhitelist(os))
                return true;

            if (PiPDisablerPlugin.AutoDisableForVariableScopes.Value
                && (FovController.IsOpticAdjustable(os) || IsThermalOrNightVisionOptic(os)))
                return true;

            if (ScopeNameMatchesBypassPattern(os))
                return true;

            return false;
        }

        private static bool ScopeNameMatchesBypassPattern(OpticSight os)
        {
            if (os == null) return false;
            string raw = PiPDisablerPlugin.AutoBypassNameContains?.Value;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string scopeKey   = ResolveWhitelistScopeKey(os) ?? string.Empty;
            string objectName = os.name ?? string.Empty;

            foreach (string token in raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = token.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                if (ContainsCI(scopeKey, t) || ContainsCI(objectName, t))
                {
                    PiPDisablerPlugin.LogInfo(
                        $"[ScopeLifecycle] AutoBypassNameContains match: token='{t}'" +
                        $" objectName='{objectName}' scopeKey='{scopeKey}'");
                    return true;
                }
            }
            return false;
        }

        private static bool ShouldBypassByWhitelist(OpticSight os)
        {
            RefreshScopeWhitelistCache();
            if (_scopeWhitelistNames.Count == 0)
                return false;

            string scopeName = ResolveWhitelistScopeKey(os);
            bool allowed = !string.IsNullOrEmpty(scopeName)
                && !string.Equals(scopeName, "unknown", StringComparison.OrdinalIgnoreCase)
                && _scopeWhitelistNames.Contains(scopeName);

            if (!allowed)
            {
                PiPDisablerPlugin.LogInfo(
                    $"[ScopeLifecycle] Whitelist bypass: '{os.name}'[scopeKey={scopeName}] is not in ScopeWhitelistNames");
            }

            return !allowed;
        }

        private static void RefreshScopeWhitelistCache()
        {
            string raw = PiPDisablerPlugin.ScopeWhitelistNames.Value ?? string.Empty;
            if (string.Equals(raw, _scopeWhitelistRawCached, StringComparison.Ordinal))
                return;

            _scopeWhitelistRawCached = raw;
            _scopeWhitelistNames.Clear();

            var parts = raw.Split(new[] { ',', ';', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                string id = part.Trim();
                if (!string.IsNullOrEmpty(id))
                    _scopeWhitelistNames.Add(id);
            }
        }

        private static bool IsThermalOrNightVisionOptic(OpticSight os)
        {
            if (os == null) return false;

            try
            {
                ScopeData scopeData = os.GetComponentInParent<ScopeData>();
                if (scopeData == null)
                    scopeData = os.GetComponentInChildren<ScopeData>(true);

                // ScopeData may live as a sibling under scope root (not direct parent/child of OpticSight).
                if (scopeData == null)
                {
                    Transform scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
                    if (scopeRoot != null)
                    {
                        scopeData = scopeRoot.GetComponentInChildren<ScopeData>(true);
                        if (scopeData == null && scopeRoot.parent != null)
                            scopeData = scopeRoot.parent.GetComponentInChildren<ScopeData>(true);
                    }
                }

                if (scopeData == null)
                    return false;

                bool hasNightVision = scopeData.NightVisionData != null;
                bool hasThermal = scopeData.ThermalVisionData != null;

                if (hasNightVision || hasThermal)
                {
                    PiPDisablerPlugin.LogInfo(
                        $"[ScopeLifecycle] Thermal/NV auto-bypass match: nightVision={hasNightVision} thermal={hasThermal}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogVerbose(
                    $"[ScopeLifecycle] Thermal/NV detection failed: {ex.Message}");
            }

            return false;
        }

        // ===== State transitions =====

        private static void ApplyBypassState(OpticSight os, float minFov, string reason,
            bool restoreFov)
        {
            string opticName = os != null ? os.name : "null";
            PiPDisablerPlugin.LogInfo(
                $"[ScopeLifecycle] Bypassing mod for current scope ({reason}): " +
                $"'{opticName}'[{FovController.GetOpticTemplateId(os)}] " +
                $"key='{ResolveWhitelistScopeKey(os)}' minFov={minFov:F2}° adjustable={FovController.IsOpticAdjustable(os)} " +
                $"restoreFov={restoreFov}");

            // On fresh scope enter, forcing RestoreFov() can stomp EFT's own optic zoom
            // for bypassed scopes until the next zoom/mode event. Let vanilla drive FOV
            // immediately on enter, but still restore when switching from a modded mode.
            if (restoreFov)
            {
                RestoreFov();
            }
            Patches.WeaponScalingPatch.RestoreScale();
            ZoomController.Restore();
            ReticleRenderer.Cleanup();
            ScopeEffectsRenderer.Cleanup();
            LensTransparency.RestoreAll();
            CameraSettingsManager.Restore();
            PiPDisabler.RestoreAllCameras();

            if (PiPDisablerPlugin.GetRestoreOnUnscope())
            {
                if (os != null)
                    MeshSurgeryManager.RestoreForScope(os.transform);
                else
                    MeshSurgeryManager.RestoreAll();
            }

            PlaneVisualizer.Hide();
            ZeroingController.Reset();
        }

        private static void DoScopeEnter()
        {
            var os = FindEnabledOpticFromPWA();
            if (os == null)
            {
                PiPDisablerPlugin.LogVerbose(
                    "[ScopeLifecycle] ENTER aborted — no OpticSight found");
                return;
            }

            _isScoped = true;
            _activeOptic = os;
            PerScopeMeshSurgerySettings.SetActiveScope(ResolveWhitelistScopeKey(os));

            float minFov = ZoomController.GetMinFov(os);
            _modBypassedForCurrentScope = ShouldBypassForCurrentOptic(os, minFov);
            if (_modBypassedForCurrentScope)
            {
                ApplyBypassState(os, minFov, reason: "scope enter",
                    restoreFov: false);
                return;
            }

            PiPDisablerPlugin.LogInfo(
                $"[ScopeLifecycle] ENTER: '{os.name}'[{FovController.GetOpticTemplateId(os)}] frame={Time.frameCount}");

            // 1. Extract reticle texture BEFORE destroying lens mesh
            ReticleRenderer.ExtractReticle(os);

            // 2. Hide ALL lens surfaces in the scope hierarchy (once)
            LensTransparency.HideAllLensSurfaces(os);

            // 2b. Collect lens renderers for the reticle stencil mask.
            var lensMaskEntries = CollectStencilEntries(os);
            ReticleRenderer.SetLensMaskEntries(lensMaskEntries);
            var occluderRenderers = LensTransparency.CollectHousingRenderers(os);
            if (PiPDisablerPlugin.StencilIncludeWeaponMeshes.Value)
                occluderRenderers.AddRange(
                    LensTransparency.CollectWeaponRenderers(os, occluderRenderers));
            ReticleRenderer.SetOccluderMaskRenderers(occluderRenderers);

            // 3. Get magnification for reticle scaling and zoom
            float mag = ZoomController.GetMagnification(os);

            // 4. Show reticle overlay at the lens position, scaled for magnification
            ReticleRenderer.Show(os, mag);

            // 4b. Show lens vignette + scope shadow effects
            ScopeEffectsRenderer.Show();

            // 5. Mesh surgery (once) — if it fails (zero entries), Tick() will retry
            _meshRetryAttemptsLeft = 0;
            if (PiPDisablerPlugin.EnableMeshSurgery.Value)
            {
                MeshSurgeryManager.ApplyForOptic(os);

                if (!MeshSurgeryManager.HasSuccessfulCut())
                {
                    PiPDisablerPlugin.LogInfo(
                        $"[ScopeLifecycle][DEBUG] Initial mesh surgery produced zero cuts — " +
                        $"scheduling retry (up to {MeshRetryMaxAttempts} attempts). frame={Time.frameCount}");
                    _meshRetryAttemptsLeft = MeshRetryMaxAttempts;
                    _meshRetryNextFrame = Time.frameCount + MeshRetryFrameInterval;
                }
            }

            // 6. Show cut plane visualizer (even without mesh surgery, for debugging)
            if (PiPDisablerPlugin.GetShowCutPlane()
                && !PiPDisablerPlugin.EnableMeshSurgery.Value)
            {
                ShowPlaneOnly(os);
            }

            // 7. Swap main camera LOD/culling settings with scope camera settings
            CameraSettingsManager.ApplyForOptic(os);

            // 8. Capture weapon base scale/FOV BEFORE changing FOV (for weapon scaling compensation)
            Patches.WeaponScalingPatch.CaptureBaseState();

            // 9. Apply animated FOV zoom (uses FovAnimationDuration)
            // Reset dead-band so the initial ApplyFov always fires regardless of previous state.
            // Also clear any pending post-exit restore from a previous scope session.
            _postExitRestoreFov = 0f;
            FovController.OnModeSwitch();
            ApplyFov(true);

            // 10. Read initial zeroing distance
            ZeroingController.ReadCurrentZeroing();
        }

        private static void DoScopeExit()
        {
            if (!_isScoped) return;

            _meshRetryAttemptsLeft = 0;

            // Reset freelook tracking so stale state doesn't persist into next scope
            FreelookTracker.Reset();

            var prevOptic = _activeOptic;
            PiPDisablerPlugin.LogInfo(
                $"[ScopeLifecycle] EXIT: '{(prevOptic != null ? prevOptic.name : "null")}'" +
                $"[{FovController.GetOpticTemplateId(prevOptic)}] frame={Time.frameCount}");


            _isScoped = false;
            _activeOptic = null;
            PerScopeMeshSurgerySettings.ClearActiveScope();

            // If this scope was bypassed, skip mod cleanup paths.
            if (_modBypassedForCurrentScope)
            {
                _modBypassedForCurrentScope = false;
                _restoreOneXFovOnScopeExit = false;
                return;
            }
            
            PiPDisabler.CleanupVanillaOpticState(prevOptic);
            
            // 1. Restore FOV with ADS-matched animation timing
            RestoreFov();

            // 1b. Restore normal weapon model scaling (after FOV is back to normal)
            Patches.WeaponScalingPatch.RestoreScale();

            // 2. Restore zoom controller
            ZoomController.Restore();

            // 3. Hide reticle overlay + scope effects
            bool allowShadowPersist = !_restoreOneXFovOnScopeExit;
            bool keepShadowMask = ScopeEffectsRenderer.OnScopeExit(allowShadowPersist);
            ReticleRenderer.OnScopeExit(keepShadowMask);

            // 4. Restore lens
            LensTransparency.RestoreAll();

            // 5. Restore camera LOD/culling settings
            CameraSettingsManager.Restore();

            // 6. Restore meshes
            if (PiPDisablerPlugin.GetRestoreOnUnscope())
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
            _restoreOneXFovOnScopeExit = false;
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
            if (FreelookTracker.IsFreelooking) return;
            ApplyFov(false); // false = short duration for scroll feel
        }

        /// <summary>
        /// Collects stencil-mask renderers from the scope lens only.
        /// The housing and weapon body are intentionally excluded.
        /// </summary>
        private static List<LensTransparency.LensMaskEntry> CollectStencilEntries(OpticSight os)
        {
            return LensTransparency.CollectLensMaskEntries(os);
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
                if (!PiPDisablerPlugin.EnableZoom.Value) return;
                if (!CameraClass.Exist) return;

                float zoomBaseFov = FovController.ZoomBaselineFov;
                float zoomedFov = FovController.ComputeZoomedFov();

                if (zoomedFov >= 0.5f && zoomedFov < zoomBaseFov)
                {
                    float duration = isTransition
                        ? PiPDisablerPlugin.FovAnimationDuration.Value
                        : 0.1f; // Short duration for variable zoom updates

                    // Skip if the target hasn't changed enough (prevents coroutine restarts
                    // that stall the lerp and cause flashing). Always apply on mode transitions.
                    if (!isTransition && !FovController.HasFovChanged(zoomedFov))
                        return;

                    FovController.TrackAppliedFov(zoomedFov);
                    CameraClass.Instance.SetFov(zoomedFov, duration, false);
                    FreelookTracker.CacheAppliedFov(zoomedFov);
                    PiPDisablerPlugin.LogInfo(
                        $"[ScopeLifecycle] ApplyFov: {zoomedFov:F1}° dur={duration:F2}s");
                }
                else if (isTransition && zoomedFov >= zoomBaseFov)
                {
                    // High-to-low mode switch where new mode has no zoom:
                    // restore to baseline with configured duration so both directions are consistent
                    float duration = PiPDisablerPlugin.FovAnimationDuration.Value;
                    FovController.TrackAppliedFov(zoomBaseFov);
                    CameraClass.Instance.SetFov(zoomBaseFov, duration, false);
                    FreelookTracker.CacheAppliedFov(zoomBaseFov);
                    PiPDisablerPlugin.LogInfo(
                        $"[ScopeLifecycle] ApplyFov (restore baseline): {zoomBaseFov:F1}° dur={duration:F2}s");
                }
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogVerbose(
                    $"[ScopeLifecycle] ApplyFov error: {ex.Message}");
            }
        }

        /// <summary>
        /// Restore FOV to baseline using the same ADS animation timing.
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
                float targetFov = _restoreOneXFovOnScopeExit
                    ? FovController.GetOneXTargetFov()
                    : baseFov;
                if (targetFov > 30f)
                {
                    float duration = PiPDisablerPlugin.FovAnimationDuration.Value;
                    // Arm the post-exit suppressor BEFORE calling SetFov so that any
                    // concurrent method_23 tick in the same frame is already blocked.
                    _postExitRestoreFov = targetFov;
                    float suppressFor = Mathf.Max(duration, 0.05f) + 0.05f;
                    _postExitRestoreExpiry = Time.realtimeSinceStartup + suppressFor;
                    FovController.TrackAppliedFov(targetFov);
                    cc.SetFov(targetFov, duration, true);
                    PiPDisablerPlugin.LogVerbose(
                        $"[ScopeLifecycle] RestoreFov: {targetFov:F1}° dur={duration:F2}s suppress={suppressFor:F2}s");
                }
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogVerbose(
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

                planePoint += planeNormal * PiPDisablerPlugin.GetPlaneOffsetMeters();
                PlaneVisualizer.Show(planePoint, planeNormal);
            }
            catch { }
        }

        private static Player GetLocalPlayer()
            => PiPDisablerPlugin.GetLocalPlayer();

        /// <summary>
        /// Finds an enabled OpticSight from cached references only.
        /// No scene-wide FindObjectsOfType — those cause multi-ms hitches.
        /// The OnEnable/OnDisable patches keep _lastEnabledOptic warm.
        /// </summary>
        private static OpticSight FindEnabledOpticFromPWA()
        {
            if (_activeOptic != null && _activeOptic.isActiveAndEnabled)
                return _activeOptic;

            if (_lastEnabledOptic != null && _lastEnabledOptic.isActiveAndEnabled)
                return _lastEnabledOptic;

            // During rapid transitions (mode switch), the incoming optic may not
            // be marked enabled yet. Trust the cache rather than doing a scene scan.
            // The OnEnable patch will fire imminently and update the cache.
            return null;
        }

    }
}
