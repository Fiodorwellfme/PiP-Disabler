using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace ScopeHousingMeshSurgery.Patches
{
    internal sealed class AdjustShotVectorsPatch : ModulePatch
    {
        private const float ScaleEpsilon = 0.0005f;
        private static readonly FieldInfo PlayerField = AccessTools.Field(typeof(Player.FirearmController), "_player");

        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.AdjustShotVectors));

        [PatchPostfix]
        private static void Postfix(Player.FirearmController __instance, ref Vector3 position, ref Vector3 direction)
        {
            var player = GetPlayer(__instance);
            if (player == null) return;

            var pwa = player.ProceduralWeaponAnimation;
            if (pwa == null || !pwa.ShotNeedsFovAdjustments) return;

            float scale = player.RibcageScaleCurrent;

            // Vanilla already adjusts when scale < 1f; we add the missing scale > 1f case.
            if (scale <= 1f + ScaleEpsilon) return;

            var self = __instance.HandsHierarchy?.Self;
            if (self == null) return;

            Vector3 localPosition = self.InverseTransformPoint(position);
            Vector3 localDirection = self.InverseTransformDirection(direction);
            localPosition.z *= scale;
            localDirection.z *= scale;
            position = self.TransformPoint(localPosition);
            direction = self.TransformDirection(localDirection).normalized;
        }

        private static Player GetPlayer(Player.FirearmController firearmController)
        {
            if (firearmController == null || PlayerField == null)
                return null;

            return PlayerField.GetValue(firearmController) as Player;
        }
    }
}
