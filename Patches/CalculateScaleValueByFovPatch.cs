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

        private static int _lastDebugFrame = -1;
        private static float _lastDebugScale = -999f;
        private static float _lastDebugFov = -999f;

        public static void UpdateRibcageScale(float newScale)
            => UpdateRibcageScale(newScale, "Generic", null);

        public static void UpdateRibcageScale(float newScale, string source, float? requestedFov)
        {
            try
            {
                var gw = Singleton<GameWorld>.Instance;
                var player = gw?.MainPlayer;
                if (player == null) return;

                float currentFov = CameraClass.Exist ? CameraClass.Instance.Fov : 60f;
                float referenceFov;
                float computedScale = ComputeScaleForCurrentFov(player, currentFov, out referenceFov);
                float scale = ScopeHousingMeshSurgeryPlugin.LockWeaponSizeAcrossScopeZoom.Value
                    ? computedScale
                    : newScale;

                player.RibcageScaleCurrentTarget = scale;

                bool shouldLog = ScopeHousingMeshSurgeryPlugin.VerboseLogging != null && ScopeHousingMeshSurgeryPlugin.VerboseLogging.Value;
                if (shouldLog)
                {
                    int frame = Time.frameCount;
                    bool changedEnough = Mathf.Abs(_lastDebugScale - scale) > 0.0005f || Mathf.Abs(_lastDebugFov - currentFov) > 0.01f;
                    bool frameGate = frame != _lastDebugFrame;
                    if (changedEnough && frameGate)
                    {
                        _lastDebugFrame = frame;
                        _lastDebugScale = scale;
                        _lastDebugFov = currentFov;
                        ScopeHousingMeshSurgeryPlugin.LogInfo(
                            $"[WeaponScale][{source}] reqFov={(requestedFov.HasValue ? requestedFov.Value.ToString("F2") : "n/a")} " +
                            $"camFov={currentFov:F2} refFov={referenceFov:F2} baseScale={ScopeHousingMeshSurgeryPlugin.WeaponFovScale.Value:F4} " +
                            $"lock={ScopeHousingMeshSurgeryPlugin.LockWeaponSizeAcrossScopeZoom.Value} computed={computedScale:F4} applied={scale:F4} " +
                            $"ribTarget={player.RibcageScaleCurrentTarget:F4}");
                    }
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


        private static float ComputeScaleForCurrentFov(Player player, float currentFov, out float referenceFov)
        {
            float baseScale = ScopeHousingMeshSurgeryPlugin.WeaponFovScale.Value;
            referenceFov = currentFov;
            if (!ScopeHousingMeshSurgeryPlugin.LockWeaponSizeAcrossScopeZoom.Value)
                return baseScale;

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
            float referenceFov;
            float scale = ComputeScaleForCurrentFov(__instance, currentFov, out referenceFov);
            ____ribcageScaleCompensated = scale;
            UpdateRibcageScale(scale, "CalculateScaleValueByFov.Prefix", __0);
            return false;
        }
    }
}
