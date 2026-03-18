using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace PiPDisabler
{
    /// <summary>
    /// Tracks the freelook (middle-mouse look-around) state while ADS and
    /// intercepts EFT's FOV stomp when freelook ends.
    ///
    /// ── THE PROBLEM ──────────────────────────────────────────────────────
    /// Player.Look() detects the _mouseLookControl transition and calls
    /// CameraClass.SetFov(35, 1, true) for optics.  This hardcoded 35°
    /// stomps whatever zoomed FOV the mod had active (e.g. 6x on a VUDU).
    /// Because Look() runs in EFT's LateUpdate pipeline AFTER our BepInEx
    /// Update(), we cannot reliably race it with a post-hoc SetFov call.
    ///
    /// ── THE FIX ──────────────────────────────────────────────────────────
    /// A Harmony transpiler on Player.Look replaces the CameraClass.SetFov
    /// callsite with our LookSetFovInterceptor.  When the mod is active
    /// and the FOV being set is the "exit freelook" value (35° for optics),
    /// we substitute our cached scoped FOV instead.
    ///
    /// The cache (_lastAppliedScopedFov) is updated every time the mod
    /// successfully writes a zoomed FOV via ApplyFov or the PWAMethod23
    /// transpiler, so it always reflects the current magnification level.
    /// </summary>
    internal static class FreelookTracker
    {
        private static bool _isFreelooking;
        private static bool _wasFreelooking;

        /// <summary>
        /// The last FOV value the mod successfully applied while scoped.
        /// Updated by ScopeLifecycle.ApplyFov and PWAMethod23Patch via
        /// CacheAppliedFov().  Stays frozen during freelook because all
        /// mod FOV writes are gated on !IsFreelooking.
        /// </summary>
        private static float _lastAppliedScopedFov;

        // Reflection cache for Player.MouseLookControl
        private static Func<Player, bool> _getMouseLookControl;
        private static bool _reflectionSearched;

        /// <summary>True while the player is freelooking during ADS.</summary>
        public static bool IsFreelooking => _isFreelooking;

        /// <summary>
        /// Called by ApplyFov / SetFovWithOverride every time the mod writes
        /// a zoomed FOV to the camera.
        /// </summary>
        public static void CacheAppliedFov(float fov)
        {
            if (fov > 0.5f)
                _lastAppliedScopedFov = fov;
        }

        /// <summary>
        /// Returns the last scoped FOV written by the mod.
        /// </summary>
        public static bool TryGetCachedScopedFov(out float fov)
        {
            fov = _lastAppliedScopedFov;
            return fov > 0.5f;
        }

        /// <summary>
        /// Called once from plugin Awake to cache the reflection accessor.
        /// </summary>
        public static void Init()
        {
            try
            {
                var prop = AccessTools.Property(typeof(Player), "MouseLookControl");
                if (prop != null)
                {
                    try
                    {
                        _getMouseLookControl = (Func<Player, bool>)
                            Delegate.CreateDelegate(typeof(Func<Player, bool>),
                                prop.GetGetMethod(true));
                    }
                    catch
                    {
                        _getMouseLookControl = p => (bool)prop.GetValue(p);
                    }
                }

                _reflectionSearched = true;
                PiPDisablerPlugin.LogInfo(
                    $"[FreelookTracker] Init: MouseLookControl={prop != null}");
            }
            catch (Exception ex)
            {
                _reflectionSearched = true;
                PiPDisablerPlugin.LogError($"[FreelookTracker] Init failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Per-frame poll.  Called from ScopeLifecycle.Tick() (only while scoped + mod active).
        /// Returns true on the FRAME where freelook ENDS.
        /// FOV is handled by the Player.Look transpiler — caller does not need to re-apply.
        /// </summary>
        public static bool Tick()
        {
            _wasFreelooking = _isFreelooking;
            _isFreelooking = ReadMouseLookControl();

            if (_isFreelooking && !_wasFreelooking)
            {
                OnFreelookEnter();
            }
            else if (!_isFreelooking && _wasFreelooking)
            {
                OnFreelookExit();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reset state when leaving scope or toggling mod.
        /// </summary>
        public static void Reset()
        {
            _isFreelooking = false;
            _wasFreelooking = false;
            _lastAppliedScopedFov = 0f;
        }

        // ===== Transitions =====

        private static void OnFreelookEnter()
        {
            PiPDisablerPlugin.LogInfo(
                $"[FreelookTracker] Freelook START — cached FOV={_lastAppliedScopedFov:F1}°, " +
                "releasing rotation lock, hiding reticle");

            ReticleRenderer.Hide();
            ScopeEffectsRenderer.Hide();
        }

        private static void OnFreelookExit()
        {
            PiPDisablerPlugin.LogInfo(
                $"[FreelookTracker] Freelook END — cached FOV={_lastAppliedScopedFov:F1}°, " +
                "re-locking rotation, showing reticle (FOV handled by Look transpiler)");

            var os = ScopeLifecycle.ActiveOptic;
            if (os != null)
            {
                float mag = ZoomController.GetMagnification(os);
                ReticleRenderer.Show(os, mag);
                ScopeEffectsRenderer.Show();
            }
        }

        // ===== Player.Look SetFov Interceptor =====

        /// <summary>
        /// Drop-in replacement for CameraClass.SetFov called from Player.Look.
        /// When the mod is active and the player is exiting freelook while scoped
        /// with an optic, we replace EFT's target FOV with our cached value.
        ///
        /// The signature matches CameraClass.SetFov(float, float, bool) exactly
        /// so the transpiler can do a simple callvirt→call swap.
        /// </summary>
        public static void LookSetFovInterceptor(CameraClass cameraClass, float targetFov, float duration, bool force)
        {
            if (cameraClass == null)
                return;

            // Only intervene when:
            //  - Mod is on and zoom is enabled
            //  - We're scoped and not bypassed
            //  - We have a valid cached FOV
            //  - The game is trying to set a non-freelook FOV (i.e. freelook just ended
            //    and EFT wants to set 35° — we know because targetFov < settingsFov)
            if (PiPDisablerPlugin.ModEnabled.Value &&
                PiPDisablerPlugin.EnableZoom.Value &&
                ScopeLifecycle.IsScoped &&
                !ScopeLifecycle.IsModBypassedForCurrentScope &&
                _lastAppliedScopedFov > 0.5f)
            {
                // EFT sets targetFov to 35 when exiting freelook with an optic,
                // and to settingsFov when entering freelook.
                // We only intercept the "exit" case (low FOV for optic).
                // Quick heuristic: if targetFov <= 35 and we have a cached value, use ours.
                if (targetFov <= 35.5f)
                {
                    PiPDisablerPlugin.LogInfo(
                        $"[FreelookTracker] Look interceptor: replacing FOV {targetFov:F1}° → " +
                        $"{_lastAppliedScopedFov:F1}° (cached scoped FOV)");

                    targetFov = _lastAppliedScopedFov;
                }
            }

            cameraClass.SetFov(targetFov, duration, force);
        }

        // ===== Reader =====

        private static bool ReadMouseLookControl()
        {
            if (!_reflectionSearched || _getMouseLookControl == null)
                return false;

            try
            {
                var player = PiPDisablerPlugin.GetLocalPlayer();
                if (player == null) return false;
                return _getMouseLookControl(player);
            }
            catch
            {
                return false;
            }
        }
    }

    // =====================================================================
    //  Harmony patch: transpile Player.Look to intercept SetFov
    // =====================================================================

    namespace Patches
    {
        /// <summary>
        /// Transpiles Player.Look to replace CameraClass.SetFov with
        /// FreelookTracker.LookSetFovInterceptor.  This lets us substitute
        /// the FOV value at the exact callsite where EFT stomps our zoom.
        /// </summary>
        internal sealed class PlayerLookPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
                => AccessTools.Method(typeof(Player), nameof(Player.Look));

            [PatchTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var setFov = AccessTools.Method(typeof(CameraClass), nameof(CameraClass.SetFov));
                var replacement = AccessTools.Method(typeof(FreelookTracker),
                    nameof(FreelookTracker.LookSetFovInterceptor));

                int replaced = 0;
                foreach (var code in instructions)
                {
                    if (code.opcode == OpCodes.Callvirt && Equals(code.operand, setFov))
                    {
                        yield return new CodeInstruction(OpCodes.Call, replacement);
                        replaced++;
                        continue;
                    }

                    yield return code;
                }

                PiPDisablerPlugin.LogInfo(
                    $"[PlayerLookPatch] Transpiler: replaced {replaced} SetFov callsite(s)");
            }
        }
    }
}
