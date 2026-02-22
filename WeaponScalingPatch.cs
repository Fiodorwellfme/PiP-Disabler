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
    /// Solution: Scale the weapon proportionally to FOV so it always occupies the
    ///           same screen space.  The configurable baseline scale applies at the
    ///           scope's LOWEST magnification (highest FOV).  As magnification
    ///           increases (FOV drops), the weapon shrinks by:
    ///
    ///             scale = baseline × tan(currentFov/2) / tan(referenceFov/2)
    ///
    ///           where referenceFov is the main camera FOV at the scope's minimum
    ///           magnification.  This exactly counteracts perspective magnification.
    ///
    ///           For a 2x-6x scope with baseline=0.6:
    ///             • at 2x  → scale = 0.600  (weapon at configured size)
    ///             • at 4x  → scale = 0.312  (weapon shrinks to match)
    ///             • at 6x  → scale = 0.210  (same screen coverage as 2x)
    /// </summary>
    internal sealed class WeaponScalingPatch : ModulePatch
    {
        // Reference main camera FOV at the scope's lowest magnification.
        // This is the FOV where weapon scale = configurable baseline.
        private static float _referenceFov;
        // Whether compensation is active (set on scope enter, cleared on exit).
        private static bool _isActive;

        // Zoom formula baseline (must match FovController.ZoomBaselineFov)
        private const float ZoomBaseline = 50f;
        private const float BaseOpticFov = 35f;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), nameof(Player.CalculateScaleValueByFov));
        }

        /// <summary>
        /// Compute and cache the reference FOV for the current scope's lowest magnification.
        /// Called from ScopeLifecycle.DoScopeEnter (before ApplyFov changes the camera FOV).
        /// </summary>
        public static void CaptureBaseState()
        {
            try
            {
                if (!ScopeHousingMeshSurgeryPlugin.EnableWeaponScaling.Value) return;

                var os = ScopeLifecycle.ActiveOptic;
                if (os == null) { _isActive = false; return; }

                // Get the scope's minimum magnification (highest scope FOV = widest view)
                float minMag = ZoomController.GetMinMagnification(os);
                if (minMag < 0.5f) minMag = 1f;

                // Compute what the main camera FOV would be at this minimum magnification,
                // using the same formula as FovController.ComputeZoomedFov:
                //   resultFov = 2 * atan(tan(baseFov/2) / magnification)
                float halfBaseRad = ZoomBaseline * 0.5f * Mathf.Deg2Rad;
                _referenceFov = 2f * Mathf.Atan(Mathf.Tan(halfBaseRad) / minMag) * Mathf.Rad2Deg;

                _isActive = true;

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[WeaponScaling] Captured base state: minMag={minMag:F2}x → " +
                    $"referenceFov={_referenceFov:F1}° baseline={ScopeHousingMeshSurgeryPlugin.WeaponScaleBaseline.Value:F2}");
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
                // Drive EFT's own compensation pipeline so PWA receives the same
                // ribcage/FOV scale value used for viewmodel math.
                player.CalculateScaleValueByFov(currentFov);
                player.SetCompensationScale(false);
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
        ///   scale = baseline × tan(currentFov/2) / tan(referenceFov/2)
        ///
        /// At referenceFov (scope's lowest magnification): ratio = 1.0, scale = baseline.
        /// As FOV decreases (higher zoom): ratio &lt; 1.0, weapon shrinks proportionally.
        /// </summary>
        private static float ComputeCompensatedScale(float currentFov)
        {
            float baseline = ScopeHousingMeshSurgeryPlugin.WeaponScaleBaseline.Value;

            if (_referenceFov <= 0.1f) return baseline;

            float halfRefRad = _referenceFov * 0.5f * Mathf.Deg2Rad;
            float halfCurRad = currentFov * 0.5f * Mathf.Deg2Rad;

            float tanRef = Mathf.Tan(halfRefRad);
            if (Mathf.Abs(tanRef) < 0.0001f) return baseline;

            float ratio = Mathf.Tan(halfCurRad) / tanRef;

            return baseline * ratio;
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

                float compensated = ComputeCompensatedScale(fov);

                ____ribcageScaleCompensated = compensated;
                __instance.RibcageScaleCurrent = compensated;
                __instance.RibcageScaleCurrentTarget = compensated;

                return false; // Skip original method
            }
            catch
            {
                return true;
            }
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
