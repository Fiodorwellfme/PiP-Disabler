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

        private static bool DebugLogsEnabled =>
            ScopeHousingMeshSurgeryPlugin.WeaponScalingDebugLogging != null &&
            ScopeHousingMeshSurgeryPlugin.WeaponScalingDebugLogging.Value;

        private static void LogDebug(string message)
        {
            if (!DebugLogsEnabled) return;
            ScopeHousingMeshSurgeryPlugin.LogInfo("[WeaponScaling:Debug] " + message);
        }

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
                LogDebug($"CaptureBaseState invoked. enabled={ScopeHousingMeshSurgeryPlugin.EnableWeaponScaling.Value} " +
                         $"scoped={ScopeLifecycle.IsScoped} bypassed={ScopeLifecycle.IsModBypassedForCurrentScope} active={_isActive}");

                if (!ScopeHousingMeshSurgeryPlugin.EnableWeaponScaling.Value)
                {
                    LogDebug("CaptureBaseState aborted: EnableWeaponScaling=false.");
                    return;
                }

                var os = ScopeLifecycle.ActiveOptic;
                if (os == null)
                {
                    _isActive = false;
                    LogDebug("CaptureBaseState aborted: ActiveOptic=null. Marking _isActive=false.");
                    return;
                }

                // Get the scope's minimum magnification (highest scope FOV = widest view)
                float minMag = ZoomController.GetMinMagnification(os);
                LogDebug($"CaptureBaseState raw min magnification from optic '{os.name}': {minMag:F4}x");
                if (minMag < 0.5f) minMag = 1f;
                LogDebug($"CaptureBaseState clamped min magnification: {minMag:F4}x");

                // Compute what the main camera FOV would be at this minimum magnification,
                // using the same formula as FovController.ComputeZoomedFov:
                //   resultFov = 2 * atan(tan(baseFov/2) / magnification)
                float halfBaseRad = ZoomBaseline * 0.5f * Mathf.Deg2Rad;
                float tanHalfBase = Mathf.Tan(halfBaseRad);
                _referenceFov = 2f * Mathf.Atan(Mathf.Tan(halfBaseRad) / minMag) * Mathf.Rad2Deg;

                _isActive = true;

                LogDebug("CaptureBaseState math details: " +
                         $"zoomBaseline={ZoomBaseline:F2}° halfBaseRad={halfBaseRad:F6} tanHalfBase={tanHalfBase:F6} " +
                         $"referenceFov={_referenceFov:F6}° baselineScale={ScopeHousingMeshSurgeryPlugin.WeaponScaleBaseline.Value:F6}");

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[WeaponScaling] Captured base state: minMag={minMag:F2}x → " +
                    $"referenceFov={_referenceFov:F1}° baseline={ScopeHousingMeshSurgeryPlugin.WeaponScaleBaseline.Value:F2}");
            }
            catch (Exception ex)
            {
                _isActive = false;
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[WeaponScaling] CaptureBaseState error: {ex.Message}");
                LogDebug($"CaptureBaseState exception: {ex}");
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
            if (!_isActive)
            {
                LogDebug("UpdateScale early exit: _isActive=false.");
                return;
            }

            if (!ScopeHousingMeshSurgeryPlugin.EnableWeaponScaling.Value)
            {
                LogDebug("UpdateScale early exit: EnableWeaponScaling=false.");
                return;
            }

            try
            {
                var player = GetMainPlayer();
                if (player == null)
                {
                    LogDebug("UpdateScale early exit: main player is null.");
                    return;
                }

                if (!CameraClass.Exist)
                {
                    LogDebug("UpdateScale early exit: CameraClass does not exist.");
                    return;
                }

                float currentFov = CameraClass.Instance.Fov;
                float compensated = ComputeCompensatedScale(currentFov);
                float prevTarget = player.RibcageScaleCurrentTarget;

                player.RibcageScaleCurrentTarget = compensated;
                LogDebug($"UpdateScale applied. currentFov={currentFov:F6} referenceFov={_referenceFov:F6} " +
                         $"prevTarget={prevTarget:F6} newTarget={compensated:F6} baseline={ScopeHousingMeshSurgeryPlugin.WeaponScaleBaseline.Value:F6}");
            }
            catch (Exception ex)
            {
                LogDebug($"UpdateScale exception: {ex}");
            }
        }

        /// <summary>
        /// Restore normal EFT ribcage scaling.  Forces a recalculation at the
        /// current camera FOV so the weapon returns to EFT's default appearance.
        /// Called from ScopeLifecycle.DoScopeExit.
        /// </summary>
        public static void RestoreScale()
        {
            LogDebug($"RestoreScale invoked. Previous _isActive={_isActive}.");
            _isActive = false;

            try
            {
                var player = GetMainPlayer();
                if (player == null)
                {
                    LogDebug("RestoreScale early exit: main player is null.");
                    return;
                }

                if (!CameraClass.Exist)
                {
                    LogDebug("RestoreScale early exit: CameraClass does not exist.");
                    return;
                }
                float currentFov = CameraClass.Instance.Fov;

                // Let EFT recalculate the ribcage scale for the restored base FOV
                player.CalculateScaleValueByFov(currentFov);
                player.SetCompensationScale(true);

                LogDebug($"RestoreScale applied. currentFov={currentFov:F6} ribcageTarget={player.RibcageScaleCurrentTarget:F6}");

                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[WeaponScaling] Restored normal scaling at FOV={currentFov:F1}");
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[WeaponScaling] RestoreScale error: {ex.Message}");
                LogDebug($"RestoreScale exception: {ex}");
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

            if (_referenceFov <= 0.1f)
            {
                LogDebug($"ComputeCompensatedScale fallback: invalid referenceFov={_referenceFov:F6}. Returning baseline={baseline:F6}.");
                return baseline;
            }

            float halfRefRad = _referenceFov * 0.5f * Mathf.Deg2Rad;
            float halfCurRad = currentFov * 0.5f * Mathf.Deg2Rad;

            float tanRef = Mathf.Tan(halfRefRad);
            float tanCur = Mathf.Tan(halfCurRad);

            if (Mathf.Abs(tanRef) < 0.0001f)
            {
                LogDebug($"ComputeCompensatedScale fallback: tanRef too small ({tanRef:F8}). Returning baseline={baseline:F6}.");
                return baseline;
            }

            float ratio = tanCur / tanRef;
            float compensated = baseline * ratio;

            LogDebug("ComputeCompensatedScale math: " +
                     $"currentFov={currentFov:F6} referenceFov={_referenceFov:F6} baseline={baseline:F6} " +
                     $"halfCurRad={halfCurRad:F6} halfRefRad={halfRefRad:F6} tanCur={tanCur:F6} tanRef={tanRef:F6} " +
                     $"ratio={ratio:F6} compensated={compensated:F6}");

            return compensated;
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
                if (!__instance.IsYourPlayer)
                {
                    LogDebug("Prefix passthrough: __instance is not local player.");
                    return true;
                }
                if (!ScopeHousingMeshSurgeryPlugin.EnableWeaponScaling.Value)
                {
                    LogDebug("Prefix passthrough: EnableWeaponScaling=false.");
                    return true;
                }
                if (!ScopeLifecycle.IsScoped)
                {
                    LogDebug($"Prefix passthrough: not scoped. fov={fov:F6} active={_isActive}");
                    return true;
                }
                if (ScopeLifecycle.IsModBypassedForCurrentScope)
                {
                    LogDebug("Prefix passthrough: current scope bypassed by mod policy.");
                    return true;
                }
                if (!_isActive)
                {
                    LogDebug($"Prefix passthrough: compensation inactive. fov={fov:F6}");
                    return true;
                }

                float compensated = ComputeCompensatedScale(fov);
                float prevComp = ____ribcageScaleCompensated;
                float prevTarget = __instance.RibcageScaleCurrentTarget;

                ____ribcageScaleCompensated = compensated;
                __instance.RibcageScaleCurrentTarget = compensated;

                LogDebug($"Prefix override applied. fov={fov:F6} prevComp={prevComp:F6} newComp={compensated:F6} " +
                         $"prevTarget={prevTarget:F6} newTarget={__instance.RibcageScaleCurrentTarget:F6} " +
                         $"referenceFov={_referenceFov:F6}");

                return false; // Skip original method
            }
            catch (Exception ex)
            {
                LogDebug($"Prefix exception. Falling back to original method. ex={ex}");
                return true;
            }
        }

        private static Player GetMainPlayer()
        {
            try
            {
                var gw = Singleton<GameWorld>.Instance;
                var player = gw?.MainPlayer;
                if (DebugLogsEnabled)
                {
                    LogDebug($"GetMainPlayer resolved. gameWorldNull={gw == null} playerNull={player == null}");
                }
                return player;
            }
            catch (Exception ex)
            {
                LogDebug($"GetMainPlayer exception: {ex}");
                return null;
            }
        }
    }
}
