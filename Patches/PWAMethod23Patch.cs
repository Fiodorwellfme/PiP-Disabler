using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EFT.Animations;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ScopeHousingMeshSurgery.Patches
{
    /// <summary>
    /// Rewrites ProceduralWeaponAnimation.method_23's SetFov call in-place so
    /// EFT only executes one FOV write per frame path.
    /// </summary>
    internal sealed class PWAMethod23Patch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ProceduralWeaponAnimation),
                nameof(ProceduralWeaponAnimation.method_23));

        [PatchPrefix]
        private static void Prefix(ProceduralWeaponAnimation __instance)
        {
            if (__instance == null) return;
            FovOverrideContext.CurrentPwa = __instance;
        }

        [PatchPostfix]
        private static void Postfix()
        {
            FovOverrideContext.CurrentPwa = null;
        }

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var setFov = AccessTools.Method(typeof(CameraClass), nameof(CameraClass.SetFov));
            var replacement = AccessTools.Method(typeof(PWAMethod23Patch), nameof(SetFovWithOverride));

            foreach (var code in instructions)
            {
                if (code.opcode == OpCodes.Callvirt && Equals(code.operand, setFov))
                {
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                    continue;
                }

                yield return code;
            }
        }

        private static void SetFovWithOverride(CameraClass cameraClass, float targetFov, float duration, bool force)
        {
            var pwa = FovOverrideContext.CurrentPwa;
            if (cameraClass == null)
                return;

            if (pwa != null &&
                ScopeHousingMeshSurgeryPlugin.ModEnabled.Value &&
                ScopeHousingMeshSurgeryPlugin.EnableZoom.Value &&
                ScopeLifecycle.IsScoped &&
                !ScopeLifecycle.IsModBypassedForCurrentScope &&
                pwa.IsAiming &&
                !pwa.Sprint)
            {
                bool isOptic;
                try { isOptic = pwa.CurrentScope.IsOptic; }
                catch { isOptic = false; }

                if (isOptic)
                {
                    float playerBaseFov = pwa.Single_2;
                    float zoomBaseFov = FovController.ZoomBaselineFov;
                    float zoomedFov = FovController.ComputeZoomedFov(playerBaseFov, pwa);

                    if (zoomedFov >= 0.5f && zoomedFov < zoomBaseFov)
                    {
                        targetFov = zoomedFov;
                        duration = ScopeHousingMeshSurgeryPlugin.FovAnimationDuration.Value;
                        force = false;
                    }
                }
            }

            cameraClass.SetFov(targetFov, duration, force);
        }

        private static class FovOverrideContext
        {
            [System.ThreadStatic]
            internal static ProceduralWeaponAnimation CurrentPwa;
        }
    }
}
