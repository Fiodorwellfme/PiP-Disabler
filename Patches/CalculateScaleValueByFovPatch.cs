using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using SPT.Reflection.Patching;

namespace ScopeHousingMeshSurgery.Patches
{
    /// <summary>
    /// Optional weapon-scale override for players who want first-person weapon size
    /// decoupled from EFT's FOV compensation logic.
    /// </summary>
    internal sealed class CalculateScaleValueByFovPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => typeof(Player).GetMethod("CalculateScaleValueByFov");

        public static void UpdateRibcageScale(float newScale)
        {
            try
            {
                var gw = Singleton<GameWorld>.Instance;
                var player = gw?.MainPlayer;
                if (player != null)
                    player.RibcageScaleCurrentTarget = newScale;
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose($"[WeaponScale] UpdateRibcageScale failed: {ex.Message}");
            }
        }

        public static void RestoreScale()
        {
            try
            {
                var gw = Singleton<GameWorld>.Instance;
                var player = gw?.MainPlayer;
                if (player != null && CameraClass.Exist)
                {
                    player.CalculateScaleValueByFov(CameraClass.Instance.Fov);
                    player.SetCompensationScale(true);
                }
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose($"[WeaponScale] RestoreScale failed: {ex.Message}");
            }
        }

        [PatchPrefix]
        private static bool Prefix(Player __instance, ref float ____ribcageScaleCompensated)
        {
            if (__instance == null || !__instance.IsYourPlayer)
                return true;

            if (!ScopeHousingMeshSurgeryPlugin.ModEnabled.Value)
                return true;

            if (!ScopeHousingMeshSurgeryPlugin.EnableWeaponFovScale.Value)
                return true;

            float scale = ScopeHousingMeshSurgeryPlugin.WeaponFovScale.Value;
            ____ribcageScaleCompensated = scale;
            UpdateRibcageScale(scale);
            return false;
        }
    }
}
