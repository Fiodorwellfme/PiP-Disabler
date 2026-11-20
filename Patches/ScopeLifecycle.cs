using EFT;
using EFT.Animations;
using EFT.CameraControl;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PiPDisabler
{
    internal static class ScopeLifecycle
    {
        private static readonly Func<ProceduralWeaponAnimation, bool> _getIsAiming
            = pwa => pwa.IsAiming;
        private static readonly Func<ProceduralWeaponAnimation, ProceduralWeaponAnimation.SightNBone> _getCurrentScope
            = pwa => pwa.CurrentScope;
        private static readonly Func<ProceduralWeaponAnimation.SightNBone, bool> _getIsOptic
            = scope => scope.IsOptic;
        private static bool _scopeAlignmentGateFailureLogged;
        private static float _postSprintAimGateExpiry;
        private static float _postStanceAimGateExpiry;
        private static bool _isScoped;
        private static OpticSight _activeOptic;
        private static OpticSight _lastEnabledOptic;
        private static bool _modBypassedForCurrentScope;
        private static bool _restoreOneXFovOnScopeExit;
        private static bool _meshSurgerySuppressedByReload;
        private static bool _reticleSuppressedByReload;
        private static MovementContext _subscribedMovementContext;

        private static float _postExitRestoreFov;
        private static float _postExitRestoreExpiry;

        public static bool HasPostExitRestore =>
            _postExitRestoreFov > 0.5f &&
            Time.realtimeSinceStartup < _postExitRestoreExpiry;
        public static float PostExitRestoreFov => _postExitRestoreFov;
        public static void ClearPostExitRestore()
        {
            _postExitRestoreFov = 0f;
            _postExitRestoreExpiry = 0f;
        }

        private static readonly HashSet<string> _scopeBlacklistNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string _scopeBlacklistRawCached;
        private static readonly HashSet<string> _scopeWhitelistNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string _scopeWhitelistRawCached;


        public static bool IsScoped => _isScoped;
        public static bool IsModBypassedForCurrentScope => _modBypassedForCurrentScope;
        public static OpticSight ActiveOptic => _activeOptic;

        public static bool ShouldRunUpdateLoop()
        {
            if (_isScoped)
                return true;

            var player = GetLocalPlayer();
            EnsureMovementStateSubscription(player);

            var pwa = player?.ProceduralWeaponAnimation;
            if (pwa == null)
                return false;

            if (!_getIsAiming(pwa))
                return false;

            try
            {
                var currentScope = _getCurrentScope(pwa);
                return currentScope != null && _getIsOptic(currentScope);
            }
            catch
            {
                return false;
            }
        }

        public static bool ShouldSuppressVanillaPiPNow()
        {
            return Settings.ModEnabled.Value &&
                   _isScoped &&
                   !_modBypassedForCurrentScope;
        }

        internal static bool IsThermalOrNightVisionOpticForBypass(OpticSight os)
        {
            return IsThermalOrNightVisionOptic(os);
        }

        internal static bool IsNameBypassed(OpticSight os)
        {
            return ScopeNameMatchesBypassPattern(os);
        }

        internal static bool IsCurrentOrPendingOpticBypassed()
        {
            if (_modBypassedForCurrentScope)
                return true;

            var os = TryGetCurrentScopeOpticFromPwa();
            if (os != null)
                return ShouldBypassForCurrentOptic(os);

            if (_isScoped && _activeOptic != null)
                return ShouldBypassForCurrentOptic(_activeOptic);

            return false;
        }

        internal static bool IsLastOpticNameBypassed()
        {
            var os = _lastEnabledOptic;
            if (os == null) return false;
            try { if (!os.enabled) return false; } catch { return false; }
            return ScopeNameMatchesBypassPattern(os);
        }

        public static void Init()
        {
            PiPDisablerPlugin.DebugLogInfo("[ScopeLifecycle] Init: IsAiming/CurrentScope/IsOptic are public — direct access.");
        }


        public static void OnOpticEnabled(OpticSight os)
        {
            // OpticSight.OnEnable fires for every OpticSight in the scene. Only react to the local
            // player's own weapon; otherwise a foreign optic toggling while we're scoped hijacks
            // _activeOptic through the "mode switch" branch below.
            if (os != null && !IsLocalPlayerOptic(os))
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[ScopeLifecycle] Ignoring OnEnable of foreign optic '{os.name}' frame={Time.frameCount}");
                return;
            }

            if (os != null)
                _lastEnabledOptic = os;

            if (os != null && ShouldBypassForCurrentOptic(os))
            {
                _activeOptic = os;
                _modBypassedForCurrentScope = true;
                ApplyBypassState(os, reason: "optic enable",
                    restoreFov: false);
            }

            if (!_isScoped && os != null)
            {
                var player = GetLocalPlayer();
                var pwa = player?.ProceduralWeaponAnimation;
                if (pwa != null && _getIsAiming(pwa))
                {
                    PiPDisablerPlugin.DebugLogInfo(
                        $"[ScopeLifecycle] Collimator→optic switch while ADS: " +
                        $"'{os.name}'[{FovController.GetOpticTemplateId(os)}] — treating as scope enter");
                }
            }

            if (_isScoped && os != null && os != _activeOptic)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[ScopeLifecycle] Mode switch while scoped: " +
                    $"'{(_activeOptic != null ? _activeOptic.name : "?")}'[{FovController.GetOpticTemplateId(_activeOptic)}] → " +
                    $"'{os.name}'[{FovController.GetOpticTemplateId(os)}]");

                _activeOptic = os;

                bool bypassForMode = ShouldBypassForCurrentOptic(os);
                if (bypassForMode)
                {
                    bool wasBypassed = _modBypassedForCurrentScope;
                    _modBypassedForCurrentScope = true;
                    ApplyBypassState(os, reason: "mode switch",
                        restoreFov: !wasBypassed);
                    return;
                }

                _modBypassedForCurrentScope = false;

                ReticleRenderer.Cleanup();
                ReticleRenderer.ExtractReticle(os);

                LensTransparency.HideAllLensSurfaces(os);

                ReticleRenderer.SetLensMaskEntries(CollectStencilEntries(os));

                FovController.OnModeSwitch();

                _meshSurgerySuppressedByReload = false;
                MeshSurgeryManager.RestoreForScope(os.transform);
                if (IsReloadActive())
                {
                    PiPDisablerPlugin.DebugLogInfo(
                        $"[ScopeLifecycle] Skipping mode-switch mesh surgery because reload is active. frame={Time.frameCount}");
                    LensTransparency.RestoreAll();
                    SuppressReticleForReload();
                    _meshSurgerySuppressedByReload = true;
                }
                else
                {
                    MeshSurgeryManager.ApplyForOptic(os);
                }
                Patches.WeaponScalingPatch.CaptureBaseState();
                if (!FreelookTracker.IsFreelooking && !_meshSurgerySuppressedByReload)
                {
                    float modeMag = FovController.GetVisualMagnification();
                    ReticleRenderer.Show(os, modeMag);
                    ScopeEffectsRenderer.Show();
                    ApplyFov(true);
                }
                CameraSettingsManager.ApplyForOptic(os);
            }

            CheckAndUpdate("OnOpticEnabled");
        }

        public static void OnOpticDisabled(OpticSight os)
        {
            // Only tear down the overlay when the optic that went away is the one we're using.
            // Previously any OpticSight.OnDisable hid the reticle/effects; if our optic stayed
            // enabled, CheckAndUpdate saw no state change and nothing re-showed them, so the
            // reticle stayed gone until the next ADS.
            if (_activeOptic == null || os == null || os == _activeOptic)
            {
                ReticleRenderer.Hide();
                ScopeEffectsRenderer.Hide();
            }
            else
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[ScopeLifecycle] OnDisable of non-active optic '{os.name}' ignored for overlay " +
                    $"(active='{_activeOptic.name}') frame={Time.frameCount}");
            }
            CheckAndUpdate("OnOpticDisabled");
        }

        private static bool IsLocalPlayerOptic(OpticSight os)
        {
            try
            {
                var player = GetLocalPlayer();
                if (player == null)
                    return true; // nothing to compare against — keep the old behaviour

                var aimTransforms = player.ProceduralWeaponAnimation?.ScopeAimTransforms;
                if (aimTransforms != null)
                {
                    for (int i = 0; i < aimTransforms.Count; i++)
                    {
                        var cache = aimTransforms[i]?.ScopePrefabCache;
                        if (cache == null) continue;
                        for (int m = 0; m < cache.ModesCount; m++)
                        {
                            if (cache.GetOpticSight(m) == os)
                                return true;
                        }
                    }
                }

                var root = player.Transform?.Original;
                return root != null && os.transform.IsChildOf(root);
            }
            catch
            {
                return true;
            }
        }

        internal static void RestoreBypassedOpticState(OpticSight os, string reason,
            bool restoreFov = false)
        {
            if (os == null) return;
            if (!ShouldBypassForCurrentOptic(os)) return;

            _activeOptic = os;
            _modBypassedForCurrentScope = true;
            ApplyBypassState(os, reason, restoreFov);
        }

        public static void CheckAndUpdate(string caller = "Update")
        {
            _lastCaller = caller;

            bool shouldBeScoped = false;
            bool exitingToNonOpticWhileAiming = false;
            string reason = "unknown";

            try
            {
                var player = GetLocalPlayer();
                EnsureMovementStateSubscription(player);

                if (player == null) { reason = "no player"; goto evaluate; }

                var pwa = player.ProceduralWeaponAnimation;
                if (pwa == null) { reason = "no PWA"; goto evaluate; }

                if (IsPlayerSprinting(player))
                {
                    ArmPostSprintAimGate();
                    reason = "sprinting";
                    goto evaluate;
                }

                if (Patches.StanceOverhaulCompat.IsStanceOverhaulTransitioning(player))
                {
                    ArmPostSprintAimGate();
                    reason = "Stance Overhaul transition";
                    goto evaluate;
                }

                bool isAiming = _getIsAiming(pwa);
                if (!isAiming) { reason = "not aiming"; goto evaluate; }

                if (Settings.BypassDuringStanceTransitions.Value && IsStanceTransitionActive(player))
                {
                    ArmPostStanceAimGate();
                    reason = "stance transition";
                    goto evaluate;
                }

                var currentScope = _getCurrentScope(pwa);
                if (currentScope == null) { reason = "no CurrentScope"; goto evaluate; }

                bool isOptic = _getIsOptic(currentScope);

                var enabledOs = FindEnabledOpticFromPWA();

                if (!isOptic)
                {
                    reason = "not optic";
                    exitingToNonOpticWhileAiming = true;
                    goto evaluate;
                }
                else if (enabledOs == null)
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

                if (ShouldApplyScopeAlignmentGate() && !IsScopeAlignedWithMainCamera(currentScope, enabledOs, out float scopeAngle, out float tolerance))
                {
                    reason = $"scope axis angle {scopeAngle:F2}° exceeds tolerance {tolerance:F2}°";
                    goto evaluate;
                }

                shouldBeScoped = true;
                _activeOptic = enabledOs;
                _lastEnabledOptic = enabledOs;
                _postSprintAimGateExpiry = 0f;
                _postStanceAimGateExpiry = 0f;
                reason = "aiming+optic+enabled OpticSight";
            }
            catch (Exception ex) { reason = $"exception: {ex.Message}"; }

        evaluate:

            if (shouldBeScoped != _isScoped)
            {
                PiPDisablerPlugin.DebugLogInfo(
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

        public static void Tick()
        {
            WatchEnterFrames();
            if (!_isScoped) return;
            if (_modBypassedForCurrentScope) return;

            CameraSettingsManager.RefreshScopedLodBias();

            bool freelookJustEnded = FreelookTracker.Tick();

            if (FreelookTracker.IsFreelooking)
            {
                return;
            }

            if (freelookJustEnded)
            {
                LensTransparency.EnsureHidden();
            }

            LensTransparency.EnsureHidden();
            if (_activeOptic != null)
            {
                bool reloadActive = IsReloadActive();
                if (reloadActive && Settings.BypassDuringReload.Value)
                {
                    SuppressReticleForReload();
                    if (!_meshSurgerySuppressedByReload)
                    {
                        MeshSurgeryManager.RestoreForScope(_activeOptic.transform);
                        LensTransparency.RestoreAll();
                        ScopeEffectsRenderer.Hide();
                        _meshSurgerySuppressedByReload = true;
                        PiPDisablerPlugin.DebugLogInfo(
                            $"[ScopeLifecycle] Mesh surgery suspended during reload. frame={Time.frameCount}");
                    }
                }
                else if (_meshSurgerySuppressedByReload)
                {
                    _meshSurgerySuppressedByReload = false;
                    MeshSurgeryManager.ApplyForOptic(_activeOptic);
                    LensTransparency.HideAllLensSurfaces(_activeOptic);
                    ResumeReticleAfterReload();
                    ScopeEffectsRenderer.Show();
                    PiPDisablerPlugin.DebugLogInfo($"[ScopeLifecycle] Mesh surgery resumed after reload. frame={Time.frameCount}");
                }
            }

            TickAutoCutZoom();

            if (_activeOptic != null)
            {
                float mag = FovController.GetVisualMagnification();
                EnsureOverlayVisible(mag);
                ReticleRenderer.UpdateTransform(mag);
                ScopeEffectsRenderer.UpdateTransform();
            }
            Patches.WeaponScalingPatch.UpdateScale();

        }

        private static int _nextOverlayCheckFrame;

        /// <summary>
        /// Safety net: while scoped (and not intentionally hidden for reload/freelook), make sure
        /// the reticle and scope effects are actually showing. Anything that hid them without a
        /// matching state change used to leave them gone until the next ADS. Checked every 15 frames.
        /// </summary>
        private static void EnsureOverlayVisible(float mag)
        {
            if (_meshSurgerySuppressedByReload || _reticleSuppressedByReload)
                return;
            if (Time.frameCount < _nextOverlayCheckFrame)
                return;
            _nextOverlayCheckFrame = Time.frameCount + 15;

            if (ReticleRenderer.HasReticle && !ReticleRenderer.IsShowing)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[ScopeLifecycle] Reticle was hidden while scoped on '{_activeOptic.name}' — re-showing. frame={Time.frameCount}");
                ReticleRenderer.Show(_activeOptic, mag);
            }

            if (!ScopeEffectsRenderer.IsVisible)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[ScopeLifecycle] Scope effects were hidden while scoped — re-showing. frame={Time.frameCount}");
                ScopeEffectsRenderer.Show();
            }
        }

        public static void ForceExit()
        {
            _meshSurgerySuppressedByReload = false;
            _reticleSuppressedByReload = false;
            FreelookTracker.Reset();
            if (_isScoped)
                DoScopeExit();
            ReticleRenderer.Cleanup();
            ScopeEffectsRenderer.Cleanup();
            Patches.WeaponScalingPatch.RestoreScale();
            CameraSettingsManager.Restore();
            UnsubscribeMovementState();
            _modBypassedForCurrentScope = false;
            _lastEnabledOptic = null;
        }

        public static void SyncState()
        {
            CheckAndUpdate("SyncState");
        }

        public static string GetActiveScopeWhitelistKey()
        {
            var os = _activeOptic != null ? _activeOptic : _lastEnabledOptic;
            return ResolveWhitelistScopeKey(os);
        }

        // ── Settings changes: heavy work runs once, after the F12 window closes ──
        // (re-entering the scope or re-cutting the scope body on every slider step allocated new
        // meshes each time and blew up memory — 1111 re-cuts in one user log.)
        private static bool _reapplyPending;
        private static bool _recutPending;

        public static void RequestReapply()
        {
            _reapplyPending = true;
            SettingsApplyGate.NoteChange();
        }

        public static void RequestRecut()
        {
            _recutPending = true;
            SettingsApplyGate.NoteChange();
        }

        /// <summary>Called every frame from the plugin Update.</summary>
        public static void TickPendingSettingWork()
        {
            if (!_reapplyPending && !_recutPending) return;
            if (!SettingsApplyGate.CanApply) return;

            bool reapply = _reapplyPending;
            _reapplyPending = false;
            _recutPending = false;

            if (reapply)
            {
                ReapplyCurrentScope("settings changed"); // re-entering also re-cuts
                return;
            }

            var os = _activeOptic;
            if (!_isScoped || _modBypassedForCurrentScope || os == null || _meshSurgerySuppressedByReload) return;

            try
            {
                MeshSurgeryManager.RestoreForScope(os.transform);
                MeshSurgeryManager.ApplyForOptic(os);
                LensTransparency.HideAllLensSurfaces(os);
                PiPDisablerPlugin.DebugLogInfo("[ScopeLifecycle] Scope body re-cut with the new settings.");
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.DebugLogInfo($"[ScopeLifecycle] Re-cut failed: {ex.Message}");
            }
        }

        // ── Automatic hole follows the camera distance (re-fit once the view has settled) ──
        // The weapon scale changes with zoom, which moves the eyepiece relative to the camera; the
        // hole is re-fitted when that distance has changed by ≥20% and stayed put for 0.6 s (not
        // during ADS motion, recoil or zoom scrolling). Same distance → same hole every time.
        private const float AutoCutSettleTime = 0.6f;
        private static int _autoCutBucketSeen = int.MinValue;
        private static float _autoCutBucketSince;

        private static void TickAutoCutZoom()
        {
            var os = _activeOptic;
            if (os == null || _meshSurgerySuppressedByReload || PixelHole.Active || !PerScopeMeshSurgerySettings.IsAutoCut())
                return;

            // Probe (debug only, heavy): once after the first re-cut of each aim, at render time.
            if (MeshSurgeryManager.ConsumeRecutFinished() && !_probeAfterRecutDone)
            {
                _probeAfterRecutDone = true;
                LensProbe.RunAtRender(os, "after re-cut");
            }

            // The view is "settled" once camera distance and weapon stretch hold still for 0.6 s.
            int bucket = MeshSurgeryManager.GetAutoApexBucket();
            int rib = MeshSurgeryManager.RibBucket(MeshSurgeryManager.RenderRibcage());
            float now = Time.realtimeSinceStartup;
            // A cut stored for this zoom (stretch) goes on screen at once — no waiting, no re-cut.
            MeshSurgeryManager.TrySwitchVariant(rib);
            if (bucket != _autoCutBucketSeen || rib != _autoCutRibSeen)
            {
                _autoCutBucketSeen = bucket;
                _autoCutRibSeen = rib;
                _autoCutBucketSince = now;
                _settleLogged = false;
                return;
            }
            if (now - _autoCutBucketSince < AutoCutSettleTime || SettingsApplyGate.IsSettingsWindowOpen)
                return;
            if (!_probeDone)
            {
                _probeDone = true;
                LensProbe.RunAtRender(os, $"view settled (camera bucket {bucket}, cut made with {MeshSurgeryManager.LastAutoApexBucketUsed})");
            }
            // Remember the settled distance so the next aim cuts right away (cache hit, no recut).
            MeshSurgeryManager.RememberSettledBucket(bucket, rib);

            if (MeshSurgeryManager.RecutRunning) return;
            bool stale = MeshSurgeryManager.AutoCutIsStale(bucket, rib, out string why);
            // "Re-cut every aim": what the Home key does, once per zoom level per aim.
            if (!stale && Settings.RecutEveryAim.Value && !_freshCutRibs.Contains(rib))
            {
                stale = true;
                why = "re-cut every aim";
            }
            if (!_settleLogged)
            {
                // Always-on, once per settled view: lets a user log show whether zooming re-fits the hole.
                _settleLogged = true;
                PiPDisablerPlugin.LogSource.LogInfo(
                    $"[AutoCut] View settled: weapon stretch {MeshSurgeryManager.RenderRibcage():F2}, camera {MeshSurgeryManager.BucketToMm(bucket):F0}mm; " +
                    $"{MeshSurgeryManager.DescribeActiveCut()} → {(stale ? "re-cut (" + why + ")" : "fits")}");
            }
            if (!stale)
            {
                // Settled with a fitting cut: now cut the other used zoom levels ahead (aimed pose).
                if (!_cutAheadQueued)
                {
                    _cutAheadQueued = true;
                    int n = MeshSurgeryManager.QueueCutAhead(os, rib);
                    if (n > 0) PiPDisablerPlugin.DebugLogInfo($"[ScopeLifecycle] Queued {n} cut(s) ahead for other zoom levels");
                }
                return;
            }
            if (now - _autoCutLastRecut < 0.3f) return;
            _autoCutLastRecut = now;
            MeshSurgeryManager.StartAsyncRecut(os, bucket, why);
            _freshCutRibs.Add(rib); // tried once for this zoom level; stale checks still retry
        }

        // Zoom levels (weapon stretch buckets) cut fresh during the current aim.
        private static readonly HashSet<int> _freshCutRibs = new HashSet<int>();

        /// <summary>Hotkey: redo the automatic hole for the current view now.</summary>
        public static void RecutHoleNow()
        {
            if (!_isScoped || _modBypassedForCurrentScope || _activeOptic == null || !PerScopeMeshSurgerySettings.IsAutoCut())
            {
                PiPDisablerPlugin.Notify(Localization.T("recut.notaiming", "PiP-Disabler: press while aiming a scope with the automatic hole"));
                return;
            }
            if (PixelHole.Active)
            {
                PiPDisablerPlugin.Notify(Localization.T("recut.pixel", "PiP-Disabler: pixel hole mode — nothing to re-cut"));
                return;
            }
            bool ok = MeshSurgeryManager.StartAsyncRecut(_activeOptic, MeshSurgeryManager.GetAutoApexBucket(), "hotkey");
            if (ok) _freshCutRibs.Add(MeshSurgeryManager.RibBucket(MeshSurgeryManager.RenderRibcage()));
            _probeAfterRecutDone = false;
            PiPDisablerPlugin.Notify(ok ? Localization.T("recut.ok", "PiP-Disabler: redoing the hole")
                                        : Localization.T("recut.fail", "PiP-Disabler: could not redo the hole (see log)"));
        }

        private static float _nextPrecutCheck;

        /// <summary>Not aiming: cut the held weapon's scope in the background (no hitch on the first aim).</summary>
        public static void TickPrecut()
        {
            MeshSurgeryManager.CheckRaidChanged();
            if (_isScoped || !Settings.ModEnabled.Value || !Settings.PrecutHeldScope.Value) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextPrecutCheck) return;
            _nextPrecutCheck = now + 1f;
            try
            {
                var os = TryGetCurrentScopeOpticFromPwa();
                if (os == null || ShouldBypassForCurrentOptic(os)) return;
                PerScopeMeshSurgerySettings.SetActiveScopeQuiet(ResolveWhitelistScopeKey(os));
                try { MeshSurgeryManager.PrecutForOptic(os); }
                finally { PerScopeMeshSurgerySettings.ClearActiveScope(); }
            }
            catch (Exception ex) { PiPDisablerPlugin.DebugLogInfo($"[ScopeLifecycle] pre-cut skipped: {ex.Message}"); }
        }

        private static int _autoCutRibSeen = int.MinValue;
        private static bool _probeAfterRecutDone;
        private static float _autoCutLastRecut;

        private static bool _probeDone;
        private static bool _settleLogged, _cutAheadQueued;

        private static void ResetAutoCutFit()
        {
            _probeDone = false;
            _autoCutBucketSeen = int.MinValue;
            _autoCutRibSeen = int.MinValue;
            MeshSurgeryManager.CancelAsyncRecut("scope exit");
            _probeAfterRecutDone = false;
            _freshCutRibs.Clear();
            _settleLogged = false;
            _cutAheadQueued = false;
        }

        /// <summary>Exit and re-enter the current scope so changed settings take effect now.</summary>
        public static void ReapplyCurrentScope(string reason)
        {
            if (!Settings.ModEnabled.Value || !_isScoped) return;
            PiPDisablerPlugin.DebugLogInfo($"[ScopeLifecycle] Re-applying current scope ({reason})");
            ForceExit();
            SyncState();
        }

        /// <summary>One-glance state for the F12 panel (Korean).</summary>
        public static string GetStatusText()
        {
            if (!Settings.ModEnabled.Value)
                return Localization.T("status.off", "Mod is off");

            var os = _activeOptic;
            if (!_isScoped || os == null)
            {
                string last = PerScopeMeshSurgerySettings.LastScopeKey;
                return string.IsNullOrEmpty(last)
                    ? Localization.T("status.notaiming", "Not aiming — aim with a scope to see it here")
                    : Localization.T("status.last", "Not aiming — last scope: {0}\nValues changed below are saved for this scope (visible when you aim again)", last);
            }

            string key = ResolveWhitelistScopeKey(os);
            string mode = _modBypassedForCurrentScope
                ? Localization.T("status.bypassed", "Original PiP mode — the settings below do not apply to this scope")
                : Localization.T("status.active", "PiP-Disabler active");
            string custom = PerScopeMeshSurgerySettings.IsUserEdited(key)
                ? Localization.T("status.custom.mine", "using my saved values")
                : PerScopeMeshSurgerySettings.GetActiveOverride() != null
                    ? Localization.T("status.custom.bundled", "using the bundled values (changing them saves my own)")
                    : Localization.T("status.custom.none", "none (using the global defaults)");
            string hole = _modBypassedForCurrentScope ? "" :
                $"\n{MeshSurgeryManager.GetAutoCutStatus()}\n{Localization.T("status.lastcheck", "Last check")}: {LensProbe.LastSummary}";
            return Localization.T("status.scope",
                "Scope: {0}\nState: {1}\nScope settings: {2}{3}\n(size, zoom and reticle show at once; the hole is redone when F12 closes, and values are saved for this scope then)",
                key, mode, custom, hole);
        }

        public static void ToggleActiveScopeWhitelistEntry()
        {
            var player = GetLocalPlayer();
            var pwa = player?.ProceduralWeaponAnimation;
            if (pwa == null || !pwa.IsAiming)
            {
                PiPDisablerPlugin.DebugLogInfo("[ScopeLifecycle] Whitelist toggle ignored: not aiming");
                return;

            }

            var os = _activeOptic;
            if (os == null)
                os = _lastEnabledOptic;

            if (os == null)
            {
                PiPDisablerPlugin.DebugLogInfo("[ScopeLifecycle] Whitelist toggle ignored: no active scope");
                return;
            }

            string scopeName = ResolveWhitelistScopeKey(os);
            if (string.IsNullOrWhiteSpace(scopeName))
            {
                PiPDisablerPlugin.DebugLogInfo(
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

            Settings.ScopeWhitelistNames.Value = string.Join(";", _scopeWhitelistNames);
            _scopeWhitelistRawCached = Settings.ScopeWhitelistNames.Value ?? string.Empty;

            PiPDisablerPlugin.DebugLogInfo(
                $"[ScopeLifecycle] Whitelist {(removed ? "removed" : "added")}: scopeKey='{scopeName}'");

            if (Settings.ModEnabled.Value && _isScoped)
            {
                ForceExit();
                SyncState();
            }
        }

        public static void ToggleActiveScopeBlacklistEntry()
        {
            var player = GetLocalPlayer();
            var pwa = player?.ProceduralWeaponAnimation;
            if (pwa == null || !pwa.IsAiming)
            {
                PiPDisablerPlugin.DebugLogInfo("[ScopeLifecycle] Blacklist toggle ignored: not aiming");
                return;
            }

            var os = _activeOptic;
            if (os == null)
                os = _lastEnabledOptic;

            if (os == null)
            {
                PiPDisablerPlugin.DebugLogInfo("[ScopeLifecycle] Blacklist toggle ignored: no active scope");
                return;
            }

            string scopeName = ResolveWhitelistScopeKey(os);
            if (string.IsNullOrWhiteSpace(scopeName))
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[ScopeLifecycle] Blacklist toggle ignored: no usable scope key for '{os.name}'");
                return;
            }

            RefreshScopeBlacklistCache();

            bool removed;
            if (_scopeBlacklistNames.Contains(scopeName))
            {
                _scopeBlacklistNames.Remove(scopeName);
                removed = true;
            }
            else
            {
                _scopeBlacklistNames.Add(scopeName);
                removed = false;
            }

            Settings.ScopeBlacklistNames.Value = string.Join(";", _scopeBlacklistNames);
            _scopeBlacklistRawCached = Settings.ScopeBlacklistNames.Value ?? string.Empty;

            PiPDisablerPlugin.DebugLogInfo(
                $"[ScopeLifecycle] Blacklist {(removed ? "removed" : "added")}: scopeKey='{scopeName}'");

            if (Settings.ModEnabled.Value && _isScoped)
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
            RefreshScopeAimTransformsForModeSwitch();
            if (!_isScoped) return;
            if (!Settings.ModEnabled.Value) return;

            PiPDisablerPlugin.DebugLogInfo(
                $"[ScopeLifecycle] SetScopeMode fired while scoped frame={Time.frameCount}");



            var os = _activeOptic;
            if (os == null) return;

            bool shouldBypass = ShouldBypassForCurrentOptic(os);
            if (shouldBypass)
            {
                bool wasBypassed = _modBypassedForCurrentScope;
                _modBypassedForCurrentScope = true;
                ApplyBypassState(os, reason: "set scope mode",
                    restoreFov: !wasBypassed);
                return;
            }

            if (_modBypassedForCurrentScope)
            {
                _modBypassedForCurrentScope = false;
                PiPDisablerPlugin.DebugLogInfo(
                    $"[ScopeLifecycle] Bypass cleared on scope mode for '{os.name}'");
            }

            FovController.OnModeSwitch();
            if (!FreelookTracker.IsFreelooking)
                ApplyFov(true);
        }

        private static void RefreshScopeAimTransformsForModeSwitch()
        {
            try
            {
                var player = GetLocalPlayer();
                var pwa = player?.ProceduralWeaponAnimation;
                if (pwa == null)
                    return;

                pwa.FindAimTransforms();
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[ScopeLifecycle] Failed to refresh scope aim transforms on mode switch: {ex.Message}");
            }
        }

        internal static bool ShouldBypassForCurrentOptic(OpticSight os)
        {
            if (os == null) return false;

            if (HasTacticalRangeFinder(os))
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[ScopeLifecycle] Rangefinder bypass: '{os.name}' contains TacticalRangeFinderController");
                return true;
            }

            if (ShouldBypassByBlacklist(os))
            {
                return true;
            }

            if (Patches.MagicOpticMountCompat.ShouldBypass(os))
            {
                return true;
            }

            if (ShouldBypassByWhitelist(os))
            {
                return true;
            }

            if (Settings.AutoDisableForVariableScopes.Value
                && IsThermalOrNightVisionOptic(os))
            {
                return true;
            }

            if (ScopeNameMatchesBypassPattern(os))
            {
                return true;
            }
            return false;
        }

        private static bool HasTacticalRangeFinder(OpticSight os)
        {
            try
            {
                return os.GetComponentInChildren<TacticalRangeFinderController>(true) != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldBypassByBlacklist(OpticSight os)
        {
            RefreshScopeBlacklistCache();
            if (_scopeBlacklistNames.Count == 0)
                return false;

            string scopeName = ResolveWhitelistScopeKey(os);
            bool blacklisted = !string.IsNullOrEmpty(scopeName)
                && !string.Equals(scopeName, "unknown", StringComparison.OrdinalIgnoreCase)
                && _scopeBlacklistNames.Contains(scopeName);

            if (blacklisted)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[ScopeLifecycle] Blacklist bypass: '{os.name}'[scopeKey={scopeName}] is in ScopeBlacklistNames");
            }

            return blacklisted;
        }

        private static bool ScopeNameMatchesBypassPattern(OpticSight os)
        {
            if (os == null) return false;
            string raw = Settings.AutoBypassNameContains?.Value;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string scopeKey = ResolveWhitelistScopeKey(os) ?? string.Empty;
            string objectName = os.name ?? string.Empty;

            foreach (string token in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = token.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                if (ContainsCI(scopeKey, t) || ContainsCI(objectName, t))
                {
                    PiPDisablerPlugin.DebugLogInfo(
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
                PiPDisablerPlugin.DebugLogInfo(
                    $"[ScopeLifecycle] Whitelist bypass: '{os.name}'[scopeKey={scopeName}] is not in ScopeWhitelistNames");
            }

            return !allowed;
        }

        private static void RefreshScopeBlacklistCache()
        {
            string raw = Settings.ScopeBlacklistNames.Value ?? string.Empty;
            if (string.Equals(raw, _scopeBlacklistRawCached, StringComparison.Ordinal))
                return;

            _scopeBlacklistRawCached = raw;
            _scopeBlacklistNames.Clear();

            var parts = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                string id = part.Trim();
                if (!string.IsNullOrEmpty(id))
                    _scopeBlacklistNames.Add(id);
            }
        }

        private static void RefreshScopeWhitelistCache()
        {
            string raw = Settings.ScopeWhitelistNames.Value ?? string.Empty;
            if (string.Equals(raw, _scopeWhitelistRawCached, StringComparison.Ordinal))
                return;

            _scopeWhitelistRawCached = raw;
            _scopeWhitelistNames.Clear();

            var parts = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
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
                // Prefer the optic's own ScopeData (vanilla assigns it in OpticSight.Awake) so a
                // sibling mode's data under the same scope root can't be picked up by mistake.
                ScopeData scopeData = os.ScopeData;
                if (scopeData == null)
                    scopeData = os.GetComponentInParent<ScopeData>();
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

                // Same test vanilla uses in OpticComponentUpdater.CopyComponentFromOptic: the data
                // component may exist with the effect switched off, which is not an NV/thermal optic.
                bool hasNightVision = scopeData.NightVisionData != null && scopeData.NightVisionData.NightVision;
                bool hasThermal = scopeData.ThermalVisionData != null && scopeData.ThermalVisionData.ThermalVision;

                if (hasNightVision || hasThermal)
                {
                    PiPDisablerPlugin.DebugLogInfo(
                        $"[ScopeLifecycle] Thermal/NV auto-bypass match: nightVision={hasNightVision} thermal={hasThermal}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[ScopeLifecycle] Thermal/NV detection failed: {ex.Message}");
            }

            return false;
        }

        // ===== State transitions =====

        private static void ApplyBypassState(OpticSight os, string reason,
            bool restoreFov)
        {
            string opticName = os != null ? os.name : "null";
            PiPDisablerPlugin.DebugLogInfo(
                $"[ScopeLifecycle] Bypassing mod for current scope ({reason}): " +
                $"'{opticName}'[{FovController.GetOpticTemplateId(os)}] " +
                $"key='{ResolveWhitelistScopeKey(os)}' adjustable={FovController.IsOpticAdjustable(os)} " +
                $"restoreFov={restoreFov}");

            // On fresh scope enter, forcing RestoreFov() can stomp EFT's own optic zoom
            // for bypassed scopes until the next zoom/mode event. Let vanilla drive FOV
            // immediately on enter, but still restore when switching from a modded mode.
            if (restoreFov)
            {
                RestoreFov();
            }

            _postExitRestoreFov = 0f;
            _postExitRestoreExpiry = 0f;

            Patches.WeaponScalingPatch.RestoreScale();
            ReticleRenderer.Cleanup();
            ScopeEffectsRenderer.Cleanup();
            LensTransparency.RestoreAll();
            CameraSettingsManager.ForceRestore();
            PiPDisabler.RestoreAllCameras();
            Patches.VanillaOpticSuppression.RestoreVanillaOpticState(os);
            Patches.MagicOpticMountCompat.RequestResync(os);

            if (os != null)
                MeshSurgeryManager.RestoreForScope(os.transform);
            else
                MeshSurgeryManager.RestoreAll();
        }

        private static float _enterRetryAfter;

        private static void DoScopeEnter()
        {
            if (Time.realtimeSinceStartup < _enterRetryAfter)
                return;

            _enterSw.Reset(); _enterSw.Start();
            _enterLaps.Length = 0;
            _enterLastLap = 0;
            try
            {
                DoScopeEnterCore();
                _enterSw.Stop();
                _enterTotalMs = _enterSw.Elapsed.TotalMilliseconds;
                _enterName = _activeOptic != null ? _activeOptic.name : "?";
                _enterWorstFrame = 0f;
                _enterWatchUntil = Time.realtimeSinceStartup + 1.5f;
            }
            catch (Exception ex)
            {
                // A half-finished enter used to leave _isScoped=true with no zoom/reticle until the
                // next ADS. Log it (always) and roll back; retry after 1s instead of every frame.
                _enterRetryAfter = Time.realtimeSinceStartup + 1f;
                PiPDisablerPlugin.LogSource.LogError(
                    $"[ScopeLifecycle] Scope enter failed for '{(_activeOptic != null ? _activeOptic.name : "null")}': {ex}");
                try { DoScopeExit(); } catch { }
            }
        }

        private static void DoScopeEnterCore()
        {
            var os = FindEnabledOpticFromPWA();
            if (os == null)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    "[ScopeLifecycle] ENTER aborted — no OpticSight found");
                return;
            }

            _isScoped = true;
            _activeOptic = os;
            PerScopeMeshSurgerySettings.SetActiveScope(ResolveWhitelistScopeKey(os));

            _modBypassedForCurrentScope = ShouldBypassForCurrentOptic(os);
            if (_modBypassedForCurrentScope)
            {
                ApplyBypassState(os, reason: "scope enter",
                    restoreFov: false);
                return;
            }

            PiPDisablerPlugin.DebugLogInfo(
                $"[ScopeLifecycle] ENTER: '{os.name}'[{FovController.GetOpticTemplateId(os)}] frame={Time.frameCount}");

            // 1. Extract reticle texture BEFORE destroying lens mesh
            ReticleRenderer.ExtractReticle(os);

            EnterLap("reticle");
            // 2. Hide ALL lens surfaces in the scope hierarchy (once)
            LensTransparency.HideAllLensSurfaces(os);

            EnterLap("lens");
            // 2b. Collect lens renderers for the reticle stencil mask.
            var lensMaskEntries = CollectStencilEntries(os);
            ReticleRenderer.SetLensMaskEntries(lensMaskEntries);
            var occluderRenderers = LensTransparency.CollectHousingRenderers(os);
            ReticleRenderer.SetOccluderMaskRenderers(occluderRenderers);

            EnterLap("masks");
            // 3. Get magnification for reticle scaling and zoom
            float mag = FovController.GetVisualMagnification();

            // 4. Show reticle overlay at the lens position, scaled for magnification
            ReticleRenderer.Show(os, mag);

            EnterLap("reticleShow");
            // 4b. Show lens vignette + scope shadow effects
            ScopeEffectsRenderer.Show();

            EnterLap("effects");
            // 5. Mesh surgery (once) — if it fails (zero entries), Tick() will retry
            _meshSurgerySuppressedByReload = false;
            if (IsReloadActive())
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[ScopeLifecycle] Skipping initial mesh surgery because reload is active. frame={Time.frameCount}");
                LensTransparency.RestoreAll();
                SuppressReticleForReload();
                _meshSurgerySuppressedByReload = true;
            }
            else
            {
                MeshSurgeryManager.ApplyForOptic(os);
            }

            EnterLap("hole");
            // 6. Swap main camera LOD/culling settings with scope camera settings
            CameraSettingsManager.ApplyForOptic(os);

            // 7. Capture weapon base scale/FOV BEFORE changing FOV (for weapon scaling compensation)
            Patches.WeaponScalingPatch.CaptureBaseState();

            EnterLap("lod");
            // 8. Apply animated FOV zoom (uses FovAnimationDuration)
            // Reset dead-band so the initial ApplyFov always fires regardless of previous state.
            // Also clear any pending post-exit restore from a previous scope session.
            _postExitRestoreFov = 0f;
            FovController.OnModeSwitch();
            ApplyFov(true);
            EnterLap("fov");
        }

        // ── Aim hitch timing (2.8.2): always on, logs only when aiming in was slow ──
        // One line per slow aim: time spent in the mod's enter steps and the slowest frame in the
        // 1.5 s after it (cuts, game's own zoom work). Answers "it stutters for 0.1 s when I aim".
        private static readonly System.Diagnostics.Stopwatch _enterSw = new System.Diagnostics.Stopwatch();
        private static readonly System.Text.StringBuilder _enterLaps = new System.Text.StringBuilder();
        private static double _enterLastLap, _enterTotalMs;
        private static float _enterWatchUntil, _enterWorstFrame;
        private static string _enterName;

        private static void EnterLap(string step)
        {
            double t = _enterSw.Elapsed.TotalMilliseconds;
            double d = t - _enterLastLap;
            _enterLastLap = t;
            if (d >= 1.0) _enterLaps.Append(step).Append(' ').Append(d.ToString("F0")).Append("ms ");
        }

        private static void WatchEnterFrames()
        {
            if (_enterWatchUntil <= 0f) return;
            float dt = Time.unscaledDeltaTime;
            if (dt > _enterWorstFrame) _enterWorstFrame = dt;
            if (Time.realtimeSinceStartup < _enterWatchUntil) return;
            _enterWatchUntil = 0f;
            float worstMs = _enterWorstFrame * 1000f;
            if (_enterTotalMs >= 15.0 || worstMs >= 60f)
                PiPDisablerPlugin.LogSource.LogInfo(
                    $"[AimTiming] '{_enterName}': aim setup {_enterTotalMs:F0}ms ({_enterLaps.ToString().TrimEnd()}), slowest frame in the next 1.5s {worstMs:F0}ms, {MeshSurgeryManager.GetAutoCutStatus()}");
        }

        private static void DoScopeExit()
        {
            if (!_isScoped) return;

            _meshSurgerySuppressedByReload = false;
            _reticleSuppressedByReload = false;

            // Reset freelook tracking so stale state doesn't persist into next scope
            FreelookTracker.Reset();

            var prevOptic = _activeOptic;
            PiPDisablerPlugin.DebugLogInfo(
                $"[ScopeLifecycle] EXIT: '{(prevOptic != null ? prevOptic.name : "null")}'" +
                $"[{FovController.GetOpticTemplateId(prevOptic)}] frame={Time.frameCount}");


            _isScoped = false;
            _activeOptic = null;
            PerScopeMeshSurgerySettings.ClearActiveScope();
            ResetAutoCutFit();

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

            // 3. Hide reticle overlay + scope effects
            bool allowShadowPersist = !_restoreOneXFovOnScopeExit;
            bool keepShadowMask = ScopeEffectsRenderer.OnScopeExit(allowShadowPersist);
            ReticleRenderer.OnScopeExit(keepShadowMask);

            // 4. Restore lens
            LensTransparency.RestoreAll();

            // 5. Restore camera LOD/culling settings
            CameraSettingsManager.Restore();

            // 6. Restore meshes
            if (prevOptic != null)
                MeshSurgeryManager.RestoreForScope(prevOptic.transform);
            else
                MeshSurgeryManager.RestoreAll();

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
                if (!CameraManager.Exist) return;

                float zoomBaseFov = FovController.MagnificationBaselineFov;
                float zoomedFov = FovController.ComputeZoomedFov();
                bool smoothScopeFov = FovController.IsSmoothScopeFovActive();

                if (zoomedFov >= 0.5f && (smoothScopeFov || zoomedFov < zoomBaseFov))
                {
                    float duration = isTransition
                        ? Settings.FovAnimationDuration.Value
                        : 0.1f; // Short duration for variable zoom updates

                    // Skip if the target hasn't changed enough (prevents coroutine restarts
                    // that stall the lerp and cause flashing). Always apply on mode transitions.
                    if (!isTransition && !FovController.HasFovChanged(zoomedFov))
                        return;

                    FovController.TrackAppliedFov(zoomedFov);
                    CameraManager.Instance.SetFov(zoomedFov, duration, false);
                    FreelookTracker.CacheAppliedFov(zoomedFov);
                    PiPDisablerPlugin.DebugLogInfo(
                        $"[ScopeLifecycle] ApplyFov: {zoomedFov:F1}° dur={duration:F2}s");
                }
                else if (isTransition && !smoothScopeFov && zoomedFov >= zoomBaseFov)
                {
                    // High-to-low mode switch where new mode has no zoom:
                    // restore to baseline with configured duration so both directions are consistent.
                    // With FOV Fix Behaviour the 1x mode goes back to the game's FOV (upstream 2.0).
                    float duration = Settings.FovAnimationDuration.Value;
                    float targetFov = zoomBaseFov;
                    if (Settings.FOVFixBehaviour.Value)
                    {
                        var pwaForFov = GetLocalPlayer()?.ProceduralWeaponAnimation;
                        if (pwaForFov != null) targetFov = pwaForFov.GetBaseFov();
                    }
                    zoomBaseFov = targetFov;
                    FovController.TrackAppliedFov(zoomBaseFov);
                    CameraManager.Instance.SetFov(zoomBaseFov, duration, false);
                    FreelookTracker.CacheAppliedFov(zoomBaseFov);
                    PiPDisablerPlugin.DebugLogInfo(
                        $"[ScopeLifecycle] ApplyFov (restore baseline): {zoomBaseFov:F1}° dur={duration:F2}s");
                }
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.DebugLogInfo(
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
                if (!CameraManager.Exist) return;
                var cc = CameraManager.Instance;
                if (cc == null) return;

                var player = GetLocalPlayer();
                if (player == null) return;
                var pwa = player.ProceduralWeaponAnimation;
                if (pwa == null) return;

                float baseFov = pwa.GetBaseFov();
                float targetFov = _restoreOneXFovOnScopeExit
                    ? Mathf.Max(1f, baseFov - 15f)
                    : baseFov;
                if (targetFov > 30f)
                {
                    float duration = Settings.FovAnimationDuration.Value;
                    // Arm the post-exit suppressor BEFORE calling SetFov so that any
                    // concurrent method_23 tick in the same frame is already blocked.
                    _postExitRestoreFov = targetFov;
                    float suppressFor = Mathf.Max(duration, 0.05f) + 0.05f;
                    _postExitRestoreExpiry = Time.realtimeSinceStartup + suppressFor;
                    FovController.TrackAppliedFov(targetFov);
                    cc.SetFov(targetFov, duration, true);
                    PiPDisablerPlugin.DebugLogInfo(
                        $"[ScopeLifecycle] RestoreFov: {targetFov:F1}° dur={duration:F2}s suppress={suppressFor:F2}s");
                }
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[ScopeLifecycle] RestoreFov error: {ex.Message}");
            }
        }

        // ===== Other Helpers =====

        private static Player GetLocalPlayer()
            => Helpers.GetLocalPlayer();

        // Resolved lazily: a missing field must only disable the reload bypass, never throw from
        // ScopeLifecycle's static initializer (which would take the whole mod down).
        private static AccessTools.FieldRef<ProceduralWeaponAnimation, Player.ValueBlenderDelay> _tacticalReloadRef;
        private static bool _tacticalReloadRefResolved;

        private static bool IsReloadActive()
        {
            try
            {
                var pwa = GetLocalPlayer()?.ProceduralWeaponAnimation;
                if (pwa == null || pwa.IsBipodUsed)
                    return false;

                if (!_tacticalReloadRefResolved)
                {
                    _tacticalReloadRefResolved = true;
                    try
                    {
                        _tacticalReloadRef = AccessTools.FieldRefAccess<ProceduralWeaponAnimation, Player.ValueBlenderDelay>("_tacticalReload");
                    }
                    catch (Exception ex)
                    {
                        PiPDisablerPlugin.LogSource.LogError($"[ScopeLifecycle] _tacticalReload not found, reload bypass disabled: {ex.Message}");
                    }
                }
                if (_tacticalReloadRef == null)
                    return false;

                var blender = _tacticalReloadRef(pwa);
                if (blender == null)
                    return false;

                return blender.Value > (Mathf.Epsilon + Settings.ReloadBypassModifier.Value);
            }
            catch
            {
                return false;
            }
        }

        private static void ArmPostSprintAimGate()
        {
            _postSprintAimGateExpiry = Time.realtimeSinceStartup + Mathf.Max(0f, Settings.PostSprintAimGateDuration.Value);
        }

        private static void EnsureMovementStateSubscription(Player player)
        {
            var movementContext = player?.MovementContext;
            if (movementContext == _subscribedMovementContext)
                return;

            UnsubscribeMovementState();

            if (movementContext == null)
                return;

            movementContext.OnStateChanged += OnMovementStateChanged;
            _subscribedMovementContext = movementContext;
        }

        private static void UnsubscribeMovementState()
        {
            if (_subscribedMovementContext == null)
                return;

            try { _subscribedMovementContext.OnStateChanged -= OnMovementStateChanged; }
            catch { }
            _subscribedMovementContext = null;
        }

        private static void OnMovementStateChanged(EPlayerState previousState, EPlayerState nextState)
        {
            if (previousState != EPlayerState.Sprint || nextState == EPlayerState.Sprint)
                return;

            ArmPostSprintAimGate();
            PiPDisablerPlugin.DebugLogInfo(
                $"[ScopeLifecycle] Post-sprint aim gate armed by MovementContext state change: {previousState} -> {nextState}");
        }

        private static void ArmPostStanceAimGate()
        {
            _postStanceAimGateExpiry = Time.realtimeSinceStartup + Mathf.Max(0f, Settings.PostStanceAimGateDuration.Value);
        }

        private static bool ShouldApplyScopeAlignmentGate()
        {
            float now = Time.realtimeSinceStartup;
            return now < _postSprintAimGateExpiry || now < _postStanceAimGateExpiry;
        }

        private static bool IsPlayerSprinting(Player player)
        {
            try
            {
                if (player == null)
                    return false;

                if (player.Physical != null && player.Physical.Sprinting)
                    return true;

                var movementContext = player.MovementContext;
                return movementContext != null &&
                       (movementContext.IsSprintEnabled ||
                        movementContext.CurrentState != null &&
                        movementContext.CurrentState.Name == EPlayerState.Sprint);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsStanceTransitionActive(Player player)
        {
            try
            {
                var movementContext = player?.MovementContext;
                if (movementContext?.CurrentState == null)
                    return false;

                EPlayerState state = movementContext.CurrentState.Name;
                return state == EPlayerState.Transit2Prone ||
                       state == EPlayerState.Prone2Stand;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsScopeAlignedWithMainCamera(ProceduralWeaponAnimation.SightNBone currentScope, OpticSight enabledOptic, out float angle, out float tolerance)
        {
            angle = 0f;
            tolerance = Mathf.Max(0f, Settings.ScopeAlignmentAngleTolerance.Value);

            try
            {
                Camera mainCamera = Helpers.GetMainCamera();
                Transform mainCameraTransform = mainCamera != null ? mainCamera.transform : null;

                if (mainCameraTransform == null || !TryGetScopeAxisDirection(currentScope, enabledOptic, out Vector3 scopeAxis))
                    throw new MissingMemberException("Missing scope axis or main camera transform");

                angle = Vector3.Angle(scopeAxis, mainCameraTransform.forward);
                return angle <= tolerance;
            }
            catch (Exception ex)
            {
                if (!_scopeAlignmentGateFailureLogged)
                {
                    _scopeAlignmentGateFailureLogged = true;
                    PiPDisablerPlugin.DebugLogInfo(
                        $"[ScopeLifecycle] Could not evaluate scope alignment gate; allowing scope activation. {ex.Message}");
                }
                angle = 0f;
                return true;
            }
        }

        private static bool TryGetScopeAxisDirection(ProceduralWeaponAnimation.SightNBone currentScope, OpticSight enabledOptic, out Vector3 direction)
        {
            direction = Vector3.zero;

            if (currentScope?.Bone != null)
            {
                Transform bone = currentScope.Bone;
                direction = bone.name == "aim_camera" ? -bone.up : -bone.forward;
                return direction.sqrMagnitude > Mathf.Epsilon;
            }

            if (PiPDisabler.OpticCameraTransform != null)
            {
                direction = PiPDisabler.OpticCameraTransform.forward;
                return direction.sqrMagnitude > Mathf.Epsilon;
            }

            if (enabledOptic != null)
            {
                direction = enabledOptic.transform.forward;
                return direction.sqrMagnitude > Mathf.Epsilon;
            }

            return false;
        }

        private static void SuppressReticleForReload()
        {
            if (_reticleSuppressedByReload) return;
            ReticleRenderer.Hide();
            _reticleSuppressedByReload = true;
        }

        private static void ResumeReticleAfterReload()
        {
            if (!_reticleSuppressedByReload) return;
            if (_activeOptic == null) return;
            float mag = FovController.GetVisualMagnification();
            ReticleRenderer.Show(_activeOptic, mag);
            _reticleSuppressedByReload = false;
        }

        private static OpticSight TryGetCurrentScopeOpticFromPwa()
        {
            try
            {
                var player = GetLocalPlayer();
                var pwa = player?.ProceduralWeaponAnimation;
                if (pwa == null) return null;

                var currentScope = _getCurrentScope(pwa);
                if (currentScope == null || !_getIsOptic(currentScope))
                    return null;

                return currentScope.ScopePrefabCache?.CurrentModOpticSight;
            }
            catch
            {
                return null;
            }
        }

        private static OpticSight FindEnabledOpticFromPWA()
        {
            if (_activeOptic != null && _activeOptic.isActiveAndEnabled)
                return _activeOptic;

            if (_lastEnabledOptic != null && _lastEnabledOptic.isActiveAndEnabled)
                return _lastEnabledOptic;

            var currentOptic = TryGetCurrentScopeOpticFromPwa();
            if (currentOptic != null && currentOptic.isActiveAndEnabled)
                return currentOptic;

            // During rapid transitions (mode switch), the incoming optic may not
            // be marked enabled yet. Trust the cache rather than doing a scene scan.
            // The OnEnable patch will fire imminently and update the cache.
            return null;
        }

    }
}
