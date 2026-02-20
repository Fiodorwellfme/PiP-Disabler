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
    /// Two zoom modes:
    ///
    /// 1. SHADER ZOOM (preferred): This patch does NOTHING. EFT sets FOV=35 normally.
    ///    The GrabPass shader on the lens handles all magnification. Weapon stays at
    ///    EFT's standard ADS size. No FOV fighting.
    ///
    /// 2. FOV ZOOM (fallback, no AssetBundle): After EFT calls SetFov(35), we
    ///    immediately call SetFov with our computed zoom value and the configured
    ///    FovAnimationDuration for smooth animated transitions.
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

                // Shader zoom mode: don't touch FOV at all. Let EFT's FOV=35 stand.
                if (ZoomController.ShaderAvailable && ScopeHousingMeshSurgeryPlugin.EnableShaderZoom.Value)
                    return;

                // FOV zoom fallback: only if enabled and no shader
                if (!ScopeHousingMeshSurgeryPlugin.EnableZoom.Value) return;

                // Only apply when actually scoped (prevents FOV changes outside ADS)
                if (!ScopeLifecycle.IsScoped) return;

                // Only in first person, aiming, not sprinting.
                if (!__instance.IsAiming) return;
                if (__instance.Sprint) return;

                // Check if it's an optic (not iron sights / red dot).
                bool isOptic;
                try { isOptic = __instance.CurrentScope.IsOptic; }
                catch { return; }
                if (!isOptic) return;

                if (!CameraClass.Exist) return;

                float baseFov = __instance.Single_2; // Player's FOV setting (50-75)
                float zoomedFov = FovController.ComputeZoomedFov(baseFov, __instance);

                if (zoomedFov < 0.5f || zoomedFov >= baseFov) return;

                // Use FovAnimationDuration for smooth animated transition into zoom.
                // EFT's own SetFov starts a coroutine that smoothly animates to the target.
                float duration = ScopeHousingMeshSurgeryPlugin.FovAnimationDuration.Value;
                CameraClass.Instance.SetFov(zoomedFov, duration, false);

                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[PWAPatch] FOV zoom: base={baseFov:F0} → zoom={zoomedFov:F1} dur={duration:F2}s");
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[PWAPatch] Error: {ex.Message}");
            }
        }
    }
}
