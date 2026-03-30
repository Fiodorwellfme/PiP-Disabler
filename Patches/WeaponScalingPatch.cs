using System;
using Bsg.GameSettings;
using Comfort.Common;
using System.Reflection;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace PiPDisabler.Patches
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
    /// Per-frame UpdateScale() catches coroutine lerp and dynamic FOV changes.
    /// </summary>
    internal sealed class WeaponScalingPatch : ModulePatch
    {
        private static bool _isActive;

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
            if (!PiPDisablerPlugin.EnableWeaponScaling.Value) return;
            var os = ScopeLifecycle.ActiveOptic;
            if (os == null) { _isActive = false; return; }
            _isActive = true;
        }

        /// <summary>
        /// Per-frame update: override visual ribcage scale with our computed value.
        /// Snaps both fields so there's no lerp delay.
        /// Called from ScopeLifecycle.Tick().
        /// </summary>
        public static void UpdateScale()
        {
            if (!_isActive) return;
            if (!PiPDisablerPlugin.EnableWeaponScaling.Value) return;

            try
            {
                var player = GetMainPlayer();
                if (player == null) return;
                if (!CameraClass.Exist) return;

                float currentFov = CameraClass.Instance.Fov;
                float settingsFov = GetSettingsFov(player);
                float scale = ComputeCompensatedScale(currentFov, settingsFov);

                // Override ONLY visual fields — aim math (_compensatoryScale etc.) untouched
                player.RibcageScaleCurrentTarget = scale;
                player.RibcageScaleCurrent = scale; // instant snap
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

            try
            {
                var player = GetMainPlayer();
                if (player == null) return;

                // Match EFT's vanilla restore path (OnFovUpdatedEvent).
                int settingsFov = GetVanillaSettingsFov();
                player.OnFovUpdatedEvent(settingsFov);

                PiPDisablerPlugin.LogVerbose(
                    $"[WeaponScaling] Restored normal scaling (settingsFov={settingsFov})");
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogVerbose(
                    $"[WeaponScaling] RestoreScale error: {ex.Message}");
            }
        }

        /// <summary>
        /// Compute the compensated ribcage scale for a given main camera FOV.
        /// Formula:
        /// invRatio = tan(referenceFov/2) / tan(currentFov/2)
        /// scale = baseline * ((1f - strength) + strength * invRatio)
        /// </summary>
        private static float ComputeCompensatedScale(float currentFov, float settingsFov)
        {
            float halfRefRad = ZoomBaseline * 0.5f * Mathf.Deg2Rad;
            float halfCurRad = currentFov * 0.5f * Mathf.Deg2Rad;
            GetAutoScaleTuning(settingsFov, out float baseline, out float strength);
            float tanRef = Mathf.Tan(halfRefRad);
            float tanCur = Mathf.Tan(halfCurRad);
            float invRatio = tanRef / tanCur;

            return baseline * ((1f - strength) + strength * invRatio);
        }

        private static float GetSettingsFov(Player player)
        {
            var pwa = player != null ? player.ProceduralWeaponAnimation : null;
            return pwa != null ? pwa.Single_2 : 50f;
        }

        private static int GetVanillaSettingsFov()
        {
            try
            {
                return (int)Singleton<SharedGameSettingsClass>.Instance.Game.Settings.FieldOfView;
            }
            catch
            {
                return 50;
            }
        }

        private static void GetAutoScaleTuning(float settingsFov, out float baseline, out float strength)
        {
            const float fovMin = 50f;
            const float fovMid = 62f;
            const float fovMax = 75f;

            const float baselineAt50 = 0.8873239f;
            const float baselineAt62 = 0.6619719f;
            const float baselineAt75 = 0.53990f;

            const float strengthAt50 = 0.2723005f;
            const float strengthAt62 = 0.2723005f;
            const float strengthAt75 = 0.422535f;

            float clampedFov = Mathf.Clamp(settingsFov, fovMin, fovMax);
            if (clampedFov <= fovMid)
            {
                float tLow = Mathf.InverseLerp(fovMin, fovMid, clampedFov);
                baseline = Mathf.Lerp(baselineAt50, baselineAt62, tLow);
                strength = Mathf.Lerp(strengthAt50, strengthAt62, tLow);
                return;
            }

            float tHigh = Mathf.InverseLerp(fovMid, fovMax, clampedFov);
            baseline = Mathf.Lerp(baselineAt62, baselineAt75, tHigh);
            strength = Mathf.Lerp(strengthAt62, strengthAt75, tHigh);
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
                if (!PiPDisablerPlugin.EnableWeaponScaling.Value) return;
                if (!ScopeLifecycle.IsScoped) return;
                if (ScopeLifecycle.IsModBypassedForCurrentScope) return;
                if (!_isActive) return;
                if (!CameraClass.Exist) return;

                float currentFov = CameraClass.Instance.Fov;
                float settingsFov = GetSettingsFov(__instance);
                float scale = ComputeCompensatedScale(currentFov, settingsFov);

                // Override visual scale AFTER EFT has finished all aim math
                __instance.RibcageScaleCurrentTarget = scale;
                __instance.RibcageScaleCurrent = scale; // instant snap
            }
            catch { }
        }

        private static Player GetMainPlayer()
            => PiPDisablerPlugin.GetLocalPlayer();
    }
}
