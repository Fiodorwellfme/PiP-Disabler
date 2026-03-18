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
            // Event-driven scope detection
            SafeEnable<OpticSightOnEnablePatch>();
            SafeEnable<OpticSightOnDisablePatch>();
            SafeEnable<ChangeAimingModePatch>();
            SafeEnable<ChangeAimingModeIndexedPatch>();
            SafeEnable<SetAimPatch>();
            // No-PiP
            SafeEnable<PiPDisabler.OpticComponentUpdaterCopyComponentFromOptic_DisablePiP>();
            SafeEnable<PiPDisabler.OpticComponentUpdaterLateUpdate_DisablePiP>();
            SafeEnable<PiPDisabler.OpticSightLensFade_NoPipPatch>();
            // FOV zoom
            SafeEnable<PWAMethod23Patch>();
            // Freelook — intercept Player.Look's SetFov(35) stomp
            SafeEnable<PlayerLookPatch>();
            // Weapon scaling (freeze ribcage scale while scoped)
            SafeEnable<WeaponScalingPatch>();
            // Fika compatibility patch
            FikaCompat.Enable();
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
