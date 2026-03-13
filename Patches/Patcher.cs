using System;
using SPT.Reflection.Patching;

namespace PiPDisabler.Patches
{
    internal static class Patcher
    {
        private static bool _enabled;

        public static void Enable()
        {
            if (_enabled) return;
            _enabled = true;

            // Event-driven scope detection (like SPT-Dynamic-External-Resolution)
            SafeEnable<OpticSightOnEnablePatch>();
            SafeEnable<OpticSightOnDisablePatch>();
            SafeEnable<ChangeAimingModePatch>();

            // No-PiP
            SafeEnable<PiPDisabler.OpticComponentUpdaterCopyComponentFromOptic_DisablePiP>();
            SafeEnable<PiPDisabler.OpticComponentUpdaterLateUpdate_DisablePiP>();
            SafeEnable<PiPDisabler.OpticSightLensFade_NoPipPatch>();

            // FOV zoom
            SafeEnable<PWAMethod23Patch>();

            // Weapon scaling (freeze ribcage scale while scoped)
            SafeEnable<WeaponScalingPatch>();

            // Upscaler quality fast-switch (avoid heavy same-family runtime reconfigure)
            SafeEnable<GClass1074DlssModePatch>();
            SafeEnable<GClass1074Fsr2AntiAliasingPatch>();
            SafeEnable<GClass1074Fsr2SetPatch>();
            SafeEnable<GClass1074Fsr3AntiAliasingPatch>();
            SafeEnable<GClass1074Fsr3SetPatch>();
        }

        private static void SafeEnable<T>() where T : ModulePatch, new()
        {
            try
            {
                new T().Enable();
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogError($"[Patcher] Failed to enable {typeof(T).Name}: {ex.Message}");
            }
        }
    }
}
