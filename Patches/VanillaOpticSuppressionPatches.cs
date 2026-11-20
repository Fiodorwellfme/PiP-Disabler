using System;
using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace PiPDisabler.Patches
{
    internal static class VanillaOpticSuppression
    {
        private static bool _allowSetResolution;

        public static bool ShouldSuppress(OpticSight opticSight)
        {
            if (!Settings.ModEnabled.Value)
                return false;

            return !ScopeLifecycle.ShouldBypassForCurrentOptic(opticSight);
        }

        public static void EnsureRenderTextureForVanilla(EFT.CameraControl.OpticCameraManager manager)
        {
            if (manager == null || manager.Camera == null)
                return;

            if (manager._renderTexture != null && manager.Camera.targetTexture != null)
                return;

            try
            {
                _allowSetResolution = true;
                manager.SetResolution(manager.OpticFinalResolution);
            }
            finally
            {
                _allowSetResolution = false;
            }
        }

        public static void RestoreVanillaOpticState(OpticSight opticSight)
        {
            if (opticSight == null || !CameraManager.Exist || CameraManager.Instance == null)
                return;

            var manager = CameraManager.Instance.OpticCameraManager;
            if (manager == null)
                return;

            try
            {
                manager.CurrentOpticSight = opticSight;

                if (opticSight.CameraData != null)
                {
                    manager.OpticRetrice?.SetOpticSight(opticSight);
                    if (manager.Updater != null)
                        manager.Updater.CopyComponentFromOptic(opticSight);
                }

                global::PiPDisabler.PiPDisabler.ForceLensFade(opticSight, false);

                if (manager.Camera != null)
                {
                    manager.Camera.enabled = true;
                    manager.Camera.gameObject.SetActive(true);
                }

                EnsureRenderTextureForVanilla(manager);
                CameraManager.Instance.method_10();
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[VanillaOpticSuppression] Restore vanilla optic state failed: {ex.Message}");
            }
        }

        public static void ReleaseRenderTexture(EFT.CameraControl.OpticCameraManager manager)
        {
            if (manager == null)
                return;

            try
            {
                if (manager.Camera != null)
                    manager.Camera.targetTexture = null;

                var rt = manager._renderTexture;
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.Destroy(rt);
                    manager._renderTexture = null;
                }

                Shader.SetGlobalTexture(EFT.CameraControl.OpticCameraManager._camTexId, null);
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[VanillaOpticSuppression] ReleaseRenderTexture failed: {ex.Message}");
            }
        }

        public static bool ShouldKeepSetResolution()
            => _allowSetResolution || !Settings.ModEnabled.Value || ScopeLifecycle.IsCurrentOrPendingOpticBypassed();
    }

    internal sealed class OpticCameraManagerEnableOptic_NoPipPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(EFT.CameraControl.OpticCameraManager), "OnOpticSightEnabled");

        [PatchPrefix]
        private static bool Prefix(EFT.CameraControl.OpticCameraManager __instance, OpticSight opticSight)
        {
            if (__instance == null)
                return true;

            if (!VanillaOpticSuppression.ShouldSuppress(opticSight))
            {
                VanillaOpticSuppression.EnsureRenderTextureForVanilla(__instance);
                return true;
            }

            try
            {
                __instance.CurrentOpticSight = null;
                __instance.OpticRetrice?.SetOpticSight(null);

                if (opticSight?.CameraData != null && __instance.Updater != null)
                    __instance.Updater.CopyComponentFromOptic(opticSight);

                if (__instance.Camera != null)
                    __instance.Camera.gameObject.SetActive(true);

                VanillaOpticSuppression.ReleaseRenderTexture(__instance);

                PiPDisablerPlugin.DebugLogInfo(
                    $"[VanillaOpticSuppression] Skipped vanilla optic manager enable for '{opticSight?.name ?? "null"}' but kept updater sync active");
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[VanillaOpticSuppression] Manager enable suppression failed: {ex.Message}");
            }

            return false;
        }
    }

    internal sealed class OpticCameraManagerSetResolution_NoPipPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(EFT.CameraControl.OpticCameraManager), nameof(EFT.CameraControl.OpticCameraManager.SetResolution));

        [PatchPostfix]
        private static void Postfix(EFT.CameraControl.OpticCameraManager __instance)
        {
            if (VanillaOpticSuppression.ShouldKeepSetResolution())
                return;

            VanillaOpticSuppression.ReleaseRenderTexture(__instance);
        }
    }

    internal sealed class CameraClassOnOpticEnabled_NoPipPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            // 4.1: method_10 lives on CameraManager (subscribed to OpticCameraManager.OnOpticEnabled),
            // not on OpticCameraManager. Targeting the wrong type made this patch silently fail.
            => AccessTools.Method(typeof(CameraManager), "method_10");

        [PatchPrefix]
        private static bool Prefix()
        {
            if (!Settings.ModEnabled.Value || ScopeLifecycle.IsCurrentOrPendingOpticBypassed())
                return true;

            var currentOptic = CameraManager.Instance?.OpticCameraManager?.CurrentOpticSight;
            if (currentOptic != null && ScopeLifecycle.ShouldBypassForCurrentOptic(currentOptic))
                return true;

            PiPDisablerPlugin.DebugLogInfo(
                "[VanillaOpticSuppression] Skipped CameraClass optic SSAA/lens enable path");
            return false;
        }
    }
}
