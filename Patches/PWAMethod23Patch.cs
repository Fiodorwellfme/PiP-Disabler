using System;
using System.Reflection;
using EFT.Animations;
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

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var setFov = AccessTools.Method(typeof(CameraClass), nameof(CameraClass.SetFov));
            var intercept = AccessTools.Method(typeof(PWAMethod23Patch), nameof(SetFovIntercept));

            for (int i = 0; i < code.Count; i++)
            {
                var ci = code[i];
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt)
                    && ci.operand is MethodInfo mi
                    && mi == setFov)
                {
                    code.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    i++;
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = intercept;
                }
            }

            return code.AsEnumerable();
        }

        private static void SetFovIntercept(
            CameraClass cameraClass,
            float vanillaFov,
            float vanillaDuration,
            bool vanillaForce,
            ProceduralWeaponAnimation pwa)
        {
            try
            {
                if (cameraClass == null)
                    return;

                // Keep vanilla path unless we are fully in scoped-zoom mode.
                if (!ScopeHousingMeshSurgeryPlugin.ModEnabled.Value
                    || !ScopeHousingMeshSurgeryPlugin.EnableZoom.Value
                    || !ScopeLifecycle.IsScoped
                    || ScopeLifecycle.IsModBypassedForCurrentScope
                    || pwa == null
                    || !pwa.IsAiming
                    || pwa.Sprint)
                {
                    cameraClass.SetFov(vanillaFov, vanillaDuration, vanillaForce);
                    return;
                }

                bool isOptic;
                try { isOptic = pwa.CurrentScope.IsOptic; }
                catch
                {
                    cameraClass.SetFov(vanillaFov, vanillaDuration, vanillaForce);
                    return;
                }

                if (!isOptic)
                {
                    cameraClass.SetFov(vanillaFov, vanillaDuration, vanillaForce);
                    return;
                }

                float playerBaseFov = pwa.Single_2;
                float zoomBaseFov = FovController.ZoomBaselineFov;
                float zoomedFov = FovController.ComputeZoomedFov(playerBaseFov, pwa);
                if (zoomedFov < 0.5f || zoomedFov >= zoomBaseFov)
                {
                    cameraClass.SetFov(vanillaFov, vanillaDuration, vanillaForce);
                    return;
                }

                float duration = ScopeHousingMeshSurgeryPlugin.FovAnimationDuration.Value;
                cameraClass.SetFov(zoomedFov, duration, false);

                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[PWATranspiler] FOV override: vanilla={vanillaFov:F1} zoom={zoomedFov:F1} dur={duration:F2}s");
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose($"[PWATranspiler] Error: {ex.Message}");
                cameraClass?.SetFov(vanillaFov, vanillaDuration, vanillaForce);
            }
        }
    }
}
