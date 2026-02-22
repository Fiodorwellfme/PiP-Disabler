using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using SPT.Reflection.Patching;
using UnityEngine;

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
                {
                    float currentFov = CameraClass.Exist ? CameraClass.Instance.Fov : 60f;
                    float scale = ScopeHousingMeshSurgeryPlugin.LockWeaponSizeAcrossScopeZoom.Value
                        ? ComputeScaleForCurrentFov(player, currentFov)
                        : newScale;
                    player.RibcageScaleCurrentTarget = scale;
                }
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


        private static float ComputeScaleForCurrentFov(Player player, float currentFov)
        {
            float baseScale = ScopeHousingMeshSurgeryPlugin.WeaponFovScale.Value;
            if (!ScopeHousingMeshSurgeryPlugin.LockWeaponSizeAcrossScopeZoom.Value)
                return baseScale;

            float referenceFov = currentFov;
            try
            {
                var pwa = player?.ProceduralWeaponAnimation;
                if (pwa != null && pwa.Single_2 > 1f)
                    referenceFov = pwa.Single_2;
            }
            catch { }

            float cur = Mathf.Clamp(currentFov, 1f, 179f);
            float reference = Mathf.Clamp(referenceFov, 1f, 179f);

            float curTan = Mathf.Max(0.001f, Mathf.Tan(cur * Mathf.Deg2Rad * 0.5f));
            float refTan = Mathf.Max(0.001f, Mathf.Tan(reference * Mathf.Deg2Rad * 0.5f));

            return baseScale * (curTan / refTan);
        }

        [PatchPrefix]
        private static bool Prefix(Player __instance, float __0, ref float ____ribcageScaleCompensated)
        {
            if (__instance == null || !__instance.IsYourPlayer)
                return true;

            if (!ScopeHousingMeshSurgeryPlugin.ModEnabled.Value)
                return true;

            if (!ScopeHousingMeshSurgeryPlugin.EnableWeaponFovScale.Value)
                return true;

            float currentFov = (__0 > 0.1f) ? __0 : (CameraClass.Exist ? CameraClass.Instance.Fov : 60f);
            float scale = ComputeScaleForCurrentFov(__instance, currentFov);
            ____ribcageScaleCompensated = scale;
            UpdateRibcageScale(scale);
            return false;
        }
    }
}
