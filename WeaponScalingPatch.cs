using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace ScopeHousingMeshSurgery.Patches
{
    /// <summary>
    /// Patches Player.CalculateScaleValueByFov to actively compensate weapon model
    /// scale across different magnification levels while ADS with optics.
    ///
    /// Problem:  When the mod lowers camera FOV for zoom, perspective projection
    ///           makes the weapon appear larger.  Higher magnification = bigger weapon.
    ///
    /// Solution: While ADS, scale proportionally to
    ///
    ///             scale = baseline × tan(scopeFov/2) / tan(hipfireFov/2)
    ///
    ///           where hipfireFov is captured right before entering ADS.
    ///           This counteracts perspective zoom so weapon screen-size remains
    ///           consistent across player FOV settings.
    /// </summary>
    internal sealed class WeaponScalingPatch : ModulePatch
    {
        // Main camera FOV at hipfire (captured right before entering ADS).
        // This is the denominator for ADS compensation.
        private static float _hipfireFov;
        // Whether compensation is active (set on scope enter, cleared on exit).
        private static bool _isActive;

        // Debug log throttling cache to avoid per-frame spam.
        private static float _lastLoggedScopeFov = -1f;
        private static float _lastLoggedMultiplier = -1f;
        private static int _lastLoggedFrame = -1000;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), nameof(Player.CalculateScaleValueByFov));
        }

        /// <summary>
        /// Cache hipfire FOV before entering ADS. Called from ScopeLifecycle.DoScopeEnter
        /// (before ApplyFov changes the camera FOV).
        /// </summary>
        public static void CaptureBaseState()
        {
            try
            {
                if (!ScopeHousingMeshSurgeryPlugin.EnableWeaponScaling.Value) return;

                var os = ScopeLifecycle.ActiveOptic;
                if (os == null) { _isActive = false; return; }

                if (CameraClass.Exist)
                    _hipfireFov = CameraClass.Instance.Fov;
                else
                    _hipfireFov = 0f;

                if (_hipfireFov <= 0.1f)
                {
                    var player = GetMainPlayer();
                    var pwa = player?.ProceduralWeaponAnimation;
                    if (pwa != null)
                        _hipfireFov = pwa.Single_2;
                }

                _isActive = true;

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[WeaponScaling] Captured base state: hipfireFov={_hipfireFov:F1}° " +
                    $"baseline={ScopeHousingMeshSurgeryPlugin.WeaponScaleBaseline.Value:F2}");
            }
            catch (Exception ex)
            {
                _isActive = false;
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[WeaponScaling] CaptureBaseState error: {ex.Message}");
            }
        }

        /// <summary>
        /// Per-frame update: push the compensated scale based on the ACTUAL current
        /// camera FOV.  This tracks animated FOV transitions (scope enter/exit) and
        /// scroll-wheel zoom changes smoothly.
        /// Called from ScopeLifecycle.Tick().
        /// </summary>
        public static void UpdateScale()
        {
            if (!_isActive) return;
            if (!ScopeHousingMeshSurgeryPlugin.EnableWeaponScaling.Value) return;

            try
            {
                var player = GetMainPlayer();
                if (player == null) return;
                if (!CameraClass.Exist) return;

                float currentFov = CameraClass.Instance.Fov;
                float multiplier;
                float compensated = ComputeCompensatedScale(currentFov, out multiplier);

                player.RibcageScaleCurrentTarget = compensated;
                LogScaleDebug("Tick", currentFov, multiplier, compensated);
            }
            catch { }
        }

        /// <summary>
        /// Restore normal EFT ribcage scaling.  Forces a recalculation at the
        /// current camera FOV so the weapon returns to EFT's default appearance.
        /// Called from ScopeLifecycle.DoScopeExit.
        /// </summary>
        public static void RestoreScale()
        {
            _isActive = false;

            try
            {
                var player = GetMainPlayer();
                if (player == null) return;

                if (!CameraClass.Exist) return;
                float currentFov = CameraClass.Instance.Fov;

                // Let EFT recalculate the ribcage scale for the restored base FOV
                player.CalculateScaleValueByFov(currentFov);
                player.SetCompensationScale(true);

                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[WeaponScaling] Restored normal scaling at FOV={currentFov:F1}");
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[WeaponScaling] RestoreScale error: {ex.Message}");
            }
        }

        /// <summary>
        /// Compute the compensated ribcage scale for a given main camera FOV.
        ///
        ///   scale = baseline × tan(scopeFov/2) / tan(hipfireFov/2)
        ///
        /// At hipfireFov: ratio = 1.0, scale = baseline.
        /// In ADS (lower scopeFov): ratio &lt; 1.0, weapon shrinks proportionally.
        /// </summary>
        private static float ComputeCompensatedScale(float scopeFov, out float multiplier)
        {
            float baseline = ScopeHousingMeshSurgeryPlugin.WeaponScaleBaseline.Value;

            if (_hipfireFov <= 0.1f)
            {
                multiplier = 1f;
                return baseline;
            }

            float halfHipfireRad = _hipfireFov * 0.5f * Mathf.Deg2Rad;
            float halfScopeRad = scopeFov * 0.5f * Mathf.Deg2Rad;

            float tanHipfire = Mathf.Tan(halfHipfireRad);
            if (Mathf.Abs(tanHipfire) < 0.0001f)
            {
                multiplier = 1f;
                return baseline;
            }

            multiplier = Mathf.Tan(halfScopeRad) / tanHipfire;

            return baseline * multiplier;
        }

        /// <summary>
        /// Harmony prefix: intercepts EFT's own CalculateScaleValueByFov calls.
        /// When active, replaces EFT's scale computation with our compensated value.
        /// The fov parameter is whatever FOV EFT is computing the scale for
        /// (may be mid-animation).
        /// </summary>
        [PatchPrefix]
        private static bool Prefix(Player __instance, float fov, ref float ____ribcageScaleCompensated)
        {
            try
            {
                if (!__instance.IsYourPlayer) return true;
                if (!ScopeHousingMeshSurgeryPlugin.EnableWeaponScaling.Value) return true;
                if (!ScopeLifecycle.IsScoped) return true;
                if (ScopeLifecycle.IsModBypassedForCurrentScope) return true;
                if (!_isActive) return true;

                float multiplier;
                float compensated = ComputeCompensatedScale(fov, out multiplier);

                ____ribcageScaleCompensated = compensated;
                __instance.RibcageScaleCurrentTarget = compensated;
                LogScaleDebug("Patch", fov, multiplier, compensated);

                return false; // Skip original method
            }
            catch
            {
                return true;
            }
        }


        private static void LogScaleDebug(string source, float scopeFov, float multiplier, float compensated)
        {
            if (!ScopeHousingMeshSurgeryPlugin.VerboseLogging.Value) return;

            bool shouldLog = Time.frameCount - _lastLoggedFrame >= 10
                || Mathf.Abs(scopeFov - _lastLoggedScopeFov) > 0.05f
                || Mathf.Abs(multiplier - _lastLoggedMultiplier) > 0.005f;

            if (!shouldLog) return;

            _lastLoggedFrame = Time.frameCount;
            _lastLoggedScopeFov = scopeFov;
            _lastLoggedMultiplier = multiplier;

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[WeaponScaling] [{source}] baseFov={_hipfireFov:F2} currentFov={scopeFov:F2} " +
                $"multiplier={multiplier:F4} finalScale={compensated:F4}");
        }

        private static Player GetMainPlayer()
        {
            try
            {
                var gw = Singleton<GameWorld>.Instance;
                return gw?.MainPlayer;
            }
            catch { return null; }
        }
    }
}
