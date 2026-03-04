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
    /// Patches Player.SetCompensationScale to override the VISUAL ribcage scale
    /// without touching aim math.
    ///
    /// === EFT's ribcage pipeline (from decompiled source) ===
    ///
    /// 1. CalculateScaleValueByFov(settingsFov)
    ///      → _ribcageScaleCompensated = Lerp(1.0, 0.65, InverseLerp(50, 75, fov))
    ///
    /// 2. SetCompensationScale(force)
    ///      → RibcageScaleCurrentTarget = _ribcageScaleCompensated
    ///      → if (force) RibcageScaleCurrent = target; ResetFovAdjustments()
    ///      → PWA.SetFovParams(_ribcageScaleCompensated)   ← AIM MATH lives here
    ///            → _compensatoryScale = scale
    ///            → Vector3_0 = (1, scale, 1)  → aim point TRS
    ///            → _fovCompensatoryDistance    → camera Z offset
    ///
    /// 3. Per-frame coroutine lerps RibcageScaleCurrent → RibcageScaleCurrentTarget
    ///
    /// === Our approach ===
    ///
    /// We let EFT run the ENTIRE pipeline normally so that _compensatoryScale,
    /// aim point TRS, and camera offset stay correct.  THEN we override only
    /// the visual fields:
    ///
    ///   RibcageScaleCurrentTarget = ourScale
    ///   RibcageScaleCurrent       = ourScale   (instant snap, no lerp)
    ///
    /// This makes the weapon model shrink without moving the reticle/aim point.
    ///
    /// Postfix on SetCompensationScale catches all EFT-initiated recalculations.
    /// Per-frame UpdateScale() catches the coroutine lerp and scroll zoom changes.
    /// </summary>
    internal sealed class WeaponScalingPatch : ModulePatch
    {
        private static bool _isActive;
        private static float _lastAppliedScale = 1f;
        private const float ScaleEpsilon = 0.0005f;

        // Zoom formula baseline (must match FovController.ZoomBaselineFov)
        private const float ZoomBaseline = 50f;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), nameof(Player.SetCompensationScale));
        }

        /// <summary>
        /// Called from ScopeLifecycle.DoScopeEnter.
        /// </summary>
        public static void CaptureBaseState()
        {
            if (!ScopeHousingMeshSurgeryPlugin.EnableWeaponScaling.Value) return;
            var os = ScopeLifecycle.ActiveOptic;
            if (os == null) { _isActive = false; return; }
            _isActive = true;
            _lastAppliedScale = 1f;
        }

        /// <summary>
        /// Per-frame update: override visual ribcage scale with our computed value.
        /// Snaps both fields so there's no lerp delay.
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
                float scale = ComputeCompensatedScale(currentFov);

                ApplyScale(player, scale);
            }
            catch { }
        }

        /// <summary>
        /// Restore normal EFT ribcage scaling.
        /// Called from ScopeLifecycle.DoScopeExit.
        /// </summary>
        public static void RestoreScale()
        {
            _isActive = false;
            _lastAppliedScale = 1f;

            try
            {
                var player = GetMainPlayer();
                if (player == null) return;

                // Use the player's SETTINGS FOV (50-75) — that's what EFT's
                // CalculateScaleValueByFov expects.  Not the camera FOV which
                // may still be zoomed when called mid-scope or from config toggle.
                var pwa = player.ProceduralWeaponAnimation;
                float settingsFov = pwa != null ? pwa.Single_2 : 50f;

                player.CalculateScaleValueByFov(settingsFov);
                player.SetCompensationScale(true);

                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[WeaponScaling] Restored normal scaling (settingsFov={settingsFov:F1})");
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[WeaponScaling] RestoreScale error: {ex.Message}");
            }
        }

        /// <summary>
        /// Compute the compensated ribcage scale for a given main camera FOV.
        /// User's formula: (1 / (ratio + offset)) * multiplier
        /// where ratio = tan(currentFov/2) / tan(50°/2)
        /// </summary>
        private static float ComputeCompensatedScale(float currentFov)
        {
            float halfRefRad = ZoomBaseline * 0.5f * Mathf.Deg2Rad;
            float halfCurRad = currentFov * 0.5f * Mathf.Deg2Rad;
            float multiplier = ScopeHousingMeshSurgeryPlugin.WeaponScaleMultiplier.Value;
            float offset = ScopeHousingMeshSurgeryPlugin.WeaponScaleOffset.Value;
            float tanRef = Mathf.Tan(halfRefRad);
            float ratio = Mathf.Tan(halfCurRad) / tanRef;

            return (1 / (ratio + offset)) * multiplier;
        }

        /// <summary>
        /// Harmony Postfix on Player.SetCompensationScale.
        ///
        /// Runs AFTER EFT has:
        ///   1. Copied _ribcageScaleCompensated → RibcageScaleCurrentTarget
        ///   2. Called PWA.SetFovParams(_ribcageScaleCompensated) → aim math set correctly
        ///   3. Optionally snapped RibcageScaleCurrent (if force=true)
        ///
        /// We then override only the visual scale fields, leaving aim math untouched.
        /// </summary>
        [PatchPostfix]
        private static void Postfix(Player __instance)
        {
            try
            {
                if (!__instance.IsYourPlayer) return;
                if (!ScopeHousingMeshSurgeryPlugin.EnableWeaponScaling.Value) return;
                if (!ScopeLifecycle.IsScoped) return;
                if (ScopeLifecycle.IsModBypassedForCurrentScope) return;
                if (!_isActive) return;
                if (!CameraClass.Exist) return;

                float currentFov = CameraClass.Instance.Fov;
                float scale = ComputeCompensatedScale(currentFov);

                ApplyScale(__instance, scale);
            }
            catch { }
        }

        private static void ApplyScale(Player player, float scale)
        {
            // Keep visual rig and aim/camera math on the same compensation value.
            player.RibcageScaleCurrentTarget = scale;
            player.RibcageScaleCurrent = scale;

            var pwa = player.ProceduralWeaponAnimation;
            if (pwa == null) return;

            pwa.SetFovParams(scale);

            if (!Mathf.Approximately(_lastAppliedScale, scale) && Mathf.Abs(_lastAppliedScale - scale) > ScaleEpsilon)
            {
                // Force calibration refresh even when scale > 1f.
                pwa.method_2();
                _lastAppliedScale = scale;
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
