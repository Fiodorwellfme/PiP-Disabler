using System;
using Bsg.GameSettings;
using Comfort.Common;
using System.Reflection;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace PiPDisabler.Patches
{
    internal sealed class WeaponScalingPatch : ModulePatch
    {
        private static bool _isActive;
        private const float ZoomBaseline = 50f;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), nameof(Player.SetCompensationScale));
        }
        public static void CaptureBaseState()
        {
            if (!Settings.EnableWeaponScaling.Value) return;
            var os = ScopeLifecycle.ActiveOptic;
            if (os == null) { _isActive = false; return; }
            _isActive = true;
        }
        public static void UpdateScale()
        {
            if (!_isActive) return;
            if (!Settings.EnableWeaponScaling.Value) return;
            var player = GetMainPlayer();
            if (player == null) return;
            if (!CameraClass.Exist) return;

            float currentFov = CameraClass.Instance.Fov;
            float settingsFov = GetSettingsFov(player);
            float scale = ComputeCompensatedScale(currentFov, settingsFov);
            player.RibcageScaleCurrentTarget = scale;
            player.RibcageScaleCurrent = scale;
        }

        public static void RestoreScale()
        {
            _isActive = false;
            var player = GetMainPlayer();
            if (player == null) return;
            int settingsFov = GetVanillaSettingsFov();
            player.OnFovUpdatedEvent(settingsFov);
        }

        private static float ComputeCompensatedScale(float currentFov, float settingsFov)
        {
            float halfRefRad = ZoomBaseline * 0.5f * Mathf.Deg2Rad;
            float halfCurRad = currentFov * 0.5f * Mathf.Deg2Rad;
            GetAutoScaleTuning(settingsFov, out float baseline, out float strength);
            float tanRef = Mathf.Tan(halfRefRad);
            float tanCur = Mathf.Tan(halfCurRad);
            float invRatio = tanRef / tanCur;

            return baseline * ((1f - strength) + strength * invRatio);
        }

        private static float GetSettingsFov(Player player)
        {
            var pwa = player != null ? player.ProceduralWeaponAnimation : null;
            return pwa != null ? pwa.Single_2 : 50f;
        }

        private static int GetVanillaSettingsFov()
        {
            return (int)Singleton<SharedGameSettingsClass>.Instance.Game.Settings.FieldOfView;
        }

        private static void GetAutoScaleTuning(float settingsFov, out float baseline, out float strength)
        {
            const float fovMin = 50f;
            const float fovMid = 62f;
            const float fovMax = 75f;

            const float baselineAt50 = 0.8873239f;
            const float baselineAt62 = 0.6619719f;
            const float baselineAt75 = 0.53990f;

            const float strengthAt50 = 0.2723005f;
            const float strengthAt62 = 0.2723005f;
            const float strengthAt75 = 0.422535f;

            float clampedFov = Mathf.Clamp(settingsFov, fovMin, fovMax);
            if (clampedFov <= fovMid)
            {
                float tLow = Mathf.InverseLerp(fovMin, fovMid, clampedFov);
                baseline = Mathf.Lerp(baselineAt50, baselineAt62, tLow);
                strength = Mathf.Lerp(strengthAt50, strengthAt62, tLow);
                return;
            }

            float tHigh = Mathf.InverseLerp(fovMid, fovMax, clampedFov);
            baseline = Mathf.Lerp(baselineAt62, baselineAt75, tHigh);
            strength = Mathf.Lerp(strengthAt62, strengthAt75, tHigh);
        }

        [PatchPostfix]
        private static void Postfix(Player __instance)
        {
            if (!__instance.IsYourPlayer) return;
            if (!Settings.EnableWeaponScaling.Value) return;
            if (!ScopeLifecycle.IsScoped) return;
            if (ScopeLifecycle.IsModBypassedForCurrentScope) return;
            if (!_isActive) return;
            if (!CameraClass.Exist) return;

            float currentFov = CameraClass.Instance.Fov;
            float settingsFov = GetSettingsFov(__instance);
            float scale = ComputeCompensatedScale(currentFov, settingsFov);
            __instance.RibcageScaleCurrentTarget = scale;
            __instance.RibcageScaleCurrent = scale;
        }

        private static Player GetMainPlayer()
            => Helpers.GetLocalPlayer();
    }
}
