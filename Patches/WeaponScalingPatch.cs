using Bsg.GameSettings;
using Comfort.Common;
using System.Reflection;
using EFT;
using EFT.Settings;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
using EFT.Animations;

namespace PiPDisabler.Patches
{
    internal sealed class WeaponScalingPatch : ModulePatch
    {
        private static bool _isActive;
        private static bool _suppressCompensationOverride;
        private static float _lastLoggedScale = -1f;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), nameof(Player.SetCompensationScale));
        }
        public static void CaptureBaseState()
        {
            CameraManager.Instance.OnFovChanged -= OnFovChanged;
            var os = ScopeLifecycle.ActiveOptic;
            if (os == null) { _isActive = false; return; }
            _isActive = true;
            CameraManager.Instance.OnFovChanged += OnFovChanged;
        }

        private static void OnFovChanged(float currentFov)
        {
            if (_isActive) UpdateScale();
        }
        public static void UpdateScale()
        {
            if (!_isActive) return;
            float scale = GetManualScale();
            if (Settings.FOVFixBehaviour.Value && CameraManager.Instance.Fov > 35)
            {
                // float minMagnificationScale = PerScopeMeshSurgerySettings.ActiveScopeOverride.WeaponScaleMinMagnification;
                float t = Mathf.InverseLerp(35f, 75f, CameraManager.Instance.Fov);
                scale = Mathf.Lerp(1, 0.65f, t);
            }
            PiPDisablerPlugin.DebugLogInfo($"The scale target is {scale}");
            if (Settings.FOVFixBehaviour.Value && CameraManager.Instance.Fov == Singleton<SettingsManager>.Instance.Game.Settings.FieldOfView.Value) 
            {
                PiPDisablerPlugin.DebugLogInfo($"Scale wasn't updated because at 1x and FOVFixBehaviour is on. Current FOV is {CameraManager.Instance.Fov}");
                return;
            }
            var player = GetMainPlayer();
            if (player == null) return;

            
            player.RibcageScaleCurrentTarget = scale;
            player.RibcageScaleCurrent = scale;
            LogScale(scale);
            PiPDisablerPlugin.DebugLogInfo($"Scale was updated, it is now {scale}");
        }

        public static void RestoreScale()
        {
            CameraManager.Instance.OnFovChanged -= OnFovChanged;
            _isActive = false;
            RestoreVanillaScale();
        }

        public static void RestoreScaleForFreelook()
        {
            if (!_isActive) return;
            RestoreVanillaScale();
        }

        private static void RestoreVanillaScale()
        {
            var player = GetMainPlayer();
            if (player == null) return;
            int settingsFov = GetVanillaSettingsFov();
            _suppressCompensationOverride = true;
            try
            {
                player.OnFovUpdatedEvent(settingsFov);
                player.RibcageScaleCurrent = player.RibcageScaleCurrentTarget;
            }
            finally
            {
                _suppressCompensationOverride = false;
            }
        }

    private static float GetManualScale()
    {
        if (!PerScopeMeshSurgerySettings.TryGetWeaponScale(out float minScale, out float maxScale))
            return Settings.ManualWeaponScale.Value * Settings.GlobalScopeScalingMultiplier.Value;

        minScale *= Settings.GlobalScopeScalingMultiplier.Value;
        maxScale *= Settings.GlobalScopeScalingMultiplier.Value;

        if (TryGetSingleModeScale(minScale, out float singleModeScale))
            return singleModeScale;

            float t = GetCurrentMagnificationT();
            return Mathf.Lerp(minScale, maxScale, t);
        }

        private static bool TryGetSingleModeScale(float targetScale, out float scale)
        {
            scale = 0f;

            var range = FovController.GetTemplateZoomRange();
            float minZoom = Mathf.Min(range.min, range.max);
            float maxZoom = Mathf.Max(range.min, range.max);
            if (minZoom <= 0.1f || maxZoom <= 0.1f || !Mathf.Approximately(minZoom, maxZoom))
                return false;

            if (maxZoom <= 1.01f)
            {
                scale = targetScale;
                return true;
            }

            float t = FovController.GetVisualZoomPosition();
            scale = Mathf.Lerp(1f, targetScale, t);
            return true;
        }

        private static float GetCurrentMagnificationT()
        {
            var range = FovController.GetTemplateZoomRange();
            float minZoom = Mathf.Min(range.min, range.max);
            float maxZoom = Mathf.Max(range.min, range.max);
            if (minZoom <= 0.1f || maxZoom <= 0.1f)
                return FovController.GetVisualZoomPosition();

            if (Mathf.Approximately(minZoom, maxZoom))
                return FovController.GetVisualZoomPosition();

            float currentMagnification = GetCurrentFovMagnification();
            return Mathf.Clamp01(Mathf.InverseLerp(minZoom, maxZoom, currentMagnification));
        }

        private static float GetCurrentFovMagnification()
        {
            if (!EFT.CameraControl.CameraManager.Exist)
                return FovController.GetVisualMagnification();

            float currentFov = Mathf.Max(0.1f, EFT.CameraControl.CameraManager.Instance.Fov);
            float baseFovRad = FovController.MagnificationBaselineFov * Mathf.Deg2Rad;
            float currentFovRad = currentFov * Mathf.Deg2Rad;
            return Mathf.Max(1f, Mathf.Tan(baseFovRad * 0.5f) / Mathf.Tan(currentFovRad * 0.5f));
        }

        private static void LogScale(float scale)
        {
            if (!Settings.DebugLogging.Value) return;
            if (System.Math.Abs(scale - _lastLoggedScale) < 0.01f) return;

            _lastLoggedScale = scale;
            PiPDisablerPlugin.DebugLogInfo($"[WeaponScaling] manual scale={scale:F3}");
        }

        private static int GetVanillaSettingsFov()
        {
            return (int)Singleton<EFT.Settings.SettingsManager>.Instance.Game.Settings.FieldOfView;
        }

        [PatchPostfix]
        private static void Postfix(Player __instance)
        {
            if (!__instance.IsYourPlayer) return;
            if (_suppressCompensationOverride) return;
            if (!ScopeLifecycle.IsScoped) return;
            if (ScopeLifecycle.IsModBypassedForCurrentScope) return;
            if (!_isActive) return;
            if (Settings.FOVFixBehaviour.Value && CameraManager.Instance.Fov == Singleton<SettingsManager>.Instance.Game.Settings.FieldOfView.Value) 
            {
                PiPDisablerPlugin.DebugLogInfo("The scale postfix didn't change the scale because at 1x and FOVFixBehaviour is on");
                return;
            }
            float scale = GetManualScale();
            __instance.RibcageScaleCurrentTarget = scale;
            __instance.RibcageScaleCurrent = scale;
            LogScale(scale);
        }

        private static Player GetMainPlayer()
            => Helpers.GetLocalPlayer();
    }
}
