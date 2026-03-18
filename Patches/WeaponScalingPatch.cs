using System;
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
        private static float _restoreDeadline = -1f;

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
            _restoreDeadline = -1f;
        }

        /// <summary>
        /// Per-frame update: override visual ribcage scale with our computed value.
        /// Snaps both fields so there's no lerp delay.
        /// Called from ScopeLifecycle.Tick().
        /// </summary>
        public static void UpdateScale()
        {
            if (!PiPDisablerPlugin.EnableWeaponScaling.Value) return;

            try
            {
                if (!_isActive) return;

                var player = GetMainPlayer();
                if (player == null) return;
                if (!CameraClass.Exist) return;

                float currentFov = CameraClass.Instance.Fov;
                float scale = ComputeCompensatedScale(currentFov);

                // Override ONLY visual fields — aim math (_compensatoryScale etc.) untouched
                player.RibcageScaleCurrentTarget = scale;
                player.RibcageScaleCurrent = scale; // instant snap

                if (_restoreDeadline >= 0f && Time.unscaledTime >= _restoreDeadline)
                    RestoreScale();
            }
            catch { }
        }

        /// <summary>
        /// Keep matching the animated camera FOV until the unscope transition ends.
        /// Called from ScopeLifecycle.DoScopeExit after RestoreFov starts.
        /// </summary>
        public static void BeginRestore(float duration)
        {
            if (!_isActive)
            {
                RestoreScale();
                return;
            }

            _restoreDeadline = Time.unscaledTime + Mathf.Max(0f, duration);
        }

        /// <summary>
        /// Restore normal EFT ribcage scaling.
        /// </summary>
        public static void RestoreScale()
        {
            _isActive = false;
            _restoreDeadline = -1f;

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

                PiPDisablerPlugin.LogVerbose(
                    $"[WeaponScaling] Restored normal scaling (settingsFov={settingsFov:F1})");
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
        private static float ComputeCompensatedScale(float currentFov)
        {
            float halfRefRad = ZoomBaseline * 0.5f * Mathf.Deg2Rad;
            float halfCurRad = currentFov * 0.5f * Mathf.Deg2Rad;
            float baseline = PiPDisablerPlugin.BaselineWeaponScale.Value;
            float strength = PiPDisablerPlugin.WeaponScaleStrength.Value;
            float tanRef = Mathf.Tan(halfRefRad);
            float tanCur = Mathf.Tan(halfCurRad);
            float invRatio = tanRef / tanCur;

            return baseline * ((1f - strength) + strength * invRatio);
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
                float scale = ComputeCompensatedScale(currentFov);

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
