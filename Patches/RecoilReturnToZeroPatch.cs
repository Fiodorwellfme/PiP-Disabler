using System.Reflection;
using EFT.Animations;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace PiPDisabler.Patches
{
    internal sealed class RecoilReturnToZeroPatch : ModulePatch
    {
        private static readonly FieldInfo _afterRecoilDefaultPositionField =
            AccessTools.Field(typeof(NewRotationRecoilProcess), "_afterRecoilDefaultPosition");

        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(NewRotationRecoilProcess), nameof(NewRotationRecoilProcess.CalculateAfterRecoilWeaponOffset));

        [PatchPostfix]
        private static void Postfix(NewRotationRecoilProcess __instance)
        {
            if (__instance == null ||
                !Settings.ModEnabled.Value ||
                !Settings.ForceRecoilReturnToZero.Value)
            {
                return;
            }

            _afterRecoilDefaultPositionField?.SetValue(__instance, Vector2.zero);
        }
    }
}
