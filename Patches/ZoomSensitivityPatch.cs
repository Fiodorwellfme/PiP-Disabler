using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace PiPDisabler.Patches
{
    /// <summary>
    /// Per-scope "확대 배수" narrows the view beyond the scope's own magnification, but EFT's
    /// aiming sensitivity (FirearmController._aimingSens) only knows the scope's magnification, so
    /// the crosshair moved too fast for the extra zoom. While aiming a scope the mod draws, the
    /// sensitivity is divided by the multiplier so mouse movement per screen distance stays the same.
    /// </summary>
    internal sealed class ZoomSensitivityPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.PropertyGetter(typeof(Player.FirearmController), nameof(Player.FirearmController.AimingSensitivity));

        [PatchPostfix]
        private static void Postfix(Player.FirearmController __instance, ref float __result)
        {
            if (!Settings.ModEnabled.Value || __instance == null) return;
            if (!ScopeLifecycle.IsScoped || ScopeLifecycle.IsModBypassedForCurrentScope) return;
            if (!__instance.IsAiming) return;

            float zoom = PerScopeMeshSurgerySettings.GetZoomMultiplier();
            if (zoom > 0.01f && System.Math.Abs(zoom - 1f) > 0.001f)
                __result /= zoom;
        }
    }
}
