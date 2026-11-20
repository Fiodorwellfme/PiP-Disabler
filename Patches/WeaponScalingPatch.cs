using Bsg.GameSettings;
using Comfort.Common;
using System.Reflection;
using EFT;
using EFT.CameraControl;
using EFT.Settings;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

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
        // Upstream 2.0 (2026-10-04): follow every FOV step (the SetFov coroutine runs after
        // Update), so the weapon scale no longer lags the zoom animation by a frame.
        private static CameraManager _fovEventSource;

        public static void CaptureBaseState()
        {
            UnsubscribeFovChanged();
            var os = ScopeLifecycle.ActiveOptic;
            if (os == null) { _isActive = false; return; }
            _isActive = true;
            SubscribeFovChanged();
        }

        private static void SubscribeFovChanged()
        {
            try
            {
                if (!CameraManager.Exist || CameraManager.Instance == null) return;
                _fovEventSource = CameraManager.Instance;
                _fovEventSource.OnFovChanged += OnFovChanged;
            }
            catch { _fovEventSource = null; }
        }

        private static void UnsubscribeFovChanged()
        {
            if (_fovEventSource == null) return;
            try { _fovEventSource.OnFovChanged -= OnFovChanged; }
            catch { }
            _fovEventSource = null;
        }

        private static void OnFovChanged(float currentFov)
        {
            if (_isActive) UpdateScale();
        }

        public static void UpdateScale()
        {
            if (!_isActive) return;
            var player = GetMainPlayer();
            if (player == null) return;
            if (!TryGetScale(out float scale)) return;

            player.RibcageScaleCurrentTarget = scale;
            player.RibcageScaleCurrent = scale;
            LogScale(scale);
        }

        /// <summary>
        /// Upstream 2.0 FOV Fix Behaviour handling: at the game's own FOV (the 1x view) the vanilla
        /// scale is left alone; between 35° and that FOV the weapon blends from 1.0 down to 0.65.
        /// </summary>
        private static bool TryGetScale(out float scale)
        {
            // 3.6: same scope looks the same on every weapon (eyepiece distance normalised).
            scale = EyeDistanceNormalizer.Apply(GetManualScale());
            if (!Settings.FOVFixBehaviour.Value || !CameraManager.Exist || CameraManager.Instance == null)
                return true;

            float fov = CameraManager.Instance.Fov;
            if (Mathf.Abs(fov - GetVanillaSettingsFov()) < 0.01f)
                return false;

            if (fov > 35f)
                scale = Mathf.Lerp(1f, 0.65f, Mathf.InverseLerp(35f, 75f, fov));
            return true;
        }

        public static void RestoreScale()
        {
            UnsubscribeFovChanged();
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

        // This scope has its own weapon scale: the global multiplier does not stack on top.

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
            if (!CameraManager.Exist)
                return FovController.GetVisualMagnification();

            float currentFov = Mathf.Max(0.1f, CameraManager.Instance.Fov);
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
            // Must be the game's settings FOV, not CameraManager.Fov: this runs on scope exit while
            // the camera is still zoomed, and feeding the zoomed FOV left the weapon mis-scaled.
            try
            {
                if (Singleton<SettingsManager>.Instantiated)
                    return Singleton<SettingsManager>.Instance.Game.Settings.FieldOfView;
            }
            catch { }
            return 75;
        }

        [PatchPostfix]
        private static void Postfix(Player __instance)
        {
            if (!__instance.IsYourPlayer) return;
            if (_suppressCompensationOverride) return;
            if (!ScopeLifecycle.IsScoped) return;
            if (ScopeLifecycle.IsModBypassedForCurrentScope) return;
            if (!_isActive) return;
            if (!TryGetScale(out float scale)) return;

            __instance.RibcageScaleCurrentTarget = scale;
            __instance.RibcageScaleCurrent = scale;
            LogScale(scale);
        }

        private static Player GetMainPlayer()
            => Helpers.GetLocalPlayer();
    }
}
