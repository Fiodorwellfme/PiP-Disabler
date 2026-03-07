using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

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
    /// Transpiler tail-injection on SetCompensationScale catches EFT recalculations.
    /// A lightweight per-frame fallback in Tick() keeps scaling stable when EFT
    /// doesn't call SetCompensationScale during certain zoom transition paths.
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
            if (!ScopeHousingMeshSurgeryPlugin.EnableWeaponScaling.Value) return;
            var os = ScopeLifecycle.ActiveOptic;
            if (os == null) { _isActive = false; return; }
            _isActive = true;

            // Apply immediately on scope-enter in case EFT doesn't touch
            // SetCompensationScale this frame.
            ApplyVisualScaleIfNeeded(GetMainPlayer());
        }

        /// <summary>
        /// Fallback update while scoped.
        /// Keeps visual scale synced across camera-FOV animation frames where
        /// SetCompensationScale may not be invoked.
        /// </summary>
        public static void UpdateScale()
        {
            if (!_isActive) return;
            ApplyVisualScaleIfNeeded(GetMainPlayer());
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
        /// Harmony Transpiler on Player.SetCompensationScale.
        /// Injects a single tail-call before every return so visual ribcage scale
        /// is overridden after EFT has finished its own aim math path.
        /// </summary>
        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var helper = AccessTools.Method(typeof(WeaponScalingPatch), nameof(ApplyVisualScaleIfNeeded));

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Ret)
                {
                    code.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Call, helper));
                    i += 2;
                }
            }

            return code.AsEnumerable();
        }

        private static void ApplyVisualScaleIfNeeded(Player player)
        {
            try
            {
                if (player == null) return;
                if (!player.IsYourPlayer) return;
                if (!ScopeHousingMeshSurgeryPlugin.EnableWeaponScaling.Value) return;
                if (!ScopeLifecycle.IsScoped) return;
                if (ScopeLifecycle.IsModBypassedForCurrentScope) return;
                if (!_isActive) return;
                if (!CameraClass.Exist) return;

                float currentFov = CameraClass.Instance.Fov;
                float scale = ComputeCompensatedScale(currentFov);

                player.RibcageScaleCurrentTarget = scale;
                player.RibcageScaleCurrent = scale;
            }
            catch { }
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
