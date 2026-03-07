using System;
using System.Reflection;
using EFT.Animations;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace ScopeHousingMeshSurgery.Patches
{
    /// <summary>
    /// Patches ProceduralWeaponAnimation.method_23 — THE source of main camera FOV decisions.
    ///
    /// EFT's method_23 logic:
    ///   - Not aiming:        SetFov(BaseFov,       1, true)
    ///   - Aiming non-optic:  SetFov(BaseFov - 15f, 1, false)
    ///   - Aiming optic:      SetFov(35f,           1, false)
    ///
    /// This patch applies FOV zoom after EFT's method_23 camera update.
    /// It computes magnification-driven FOV and applies the configured
    /// FovAnimationDuration for smooth transitions.
    /// </summary>
    internal sealed class PWAMethod23Patch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ProceduralWeaponAnimation),
                nameof(ProceduralWeaponAnimation.method_23));

        [PatchPostfix]
        private static void Postfix(ProceduralWeaponAnimation __instance)
        {
            try
            {
                // Global mod toggle
                if (!ScopeHousingMeshSurgeryPlugin.ModEnabled.Value) return;

                // FOV zoom: only if enabled
                if (!ScopeHousingMeshSurgeryPlugin.EnableZoom.Value) return;

                // Only apply when actually scoped (prevents FOV changes outside ADS)
                if (!ScopeLifecycle.IsScoped) return;

                // Auto-bypass mode for scoped optics: keep vanilla EFT FOV behavior.
                if (ScopeLifecycle.IsModBypassedForCurrentScope) return;

                // Only in first person, aiming, not sprinting.
                if (!__instance.IsAiming) return;
                if (__instance.Sprint) return;

                // Check if it's an optic (not iron sights / red dot).
                bool isOptic;
                try { isOptic = __instance.CurrentScope.IsOptic; }
                catch { return; }
                if (!isOptic) return;

                if (!CameraClass.Exist) return;

                float playerBaseFov = __instance.Single_2; // Player's own FOV setting
                float zoomBaseFov = FovController.ZoomBaselineFov; // Fixed baseline for zoom strength
                float zoomedFov = FovController.ComputeZoomedFov(playerBaseFov, __instance);

                if (zoomedFov < 0.5f || zoomedFov >= zoomBaseFov) return;

                // Use FovAnimationDuration for smooth animated transition into zoom.
                // EFT's own SetFov starts a coroutine that smoothly animates to the target.
                float duration = ScopeHousingMeshSurgeryPlugin.FovAnimationDuration.Value;
                CameraClass.Instance.SetFov(zoomedFov, duration, false);

                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[PWAPatch] FOV zoom: playerBase={playerBaseFov:F0} fixedBase={zoomBaseFov:F0} → zoom={zoomedFov:F1} dur={duration:F2}s");
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[PWAPatch] Error: {ex.Message}");
            }
        }
    }
}
