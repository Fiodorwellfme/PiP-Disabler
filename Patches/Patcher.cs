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
            SafeEnable<OpticSightOnEnablePatch>();
            SafeEnable<OpticSightOnDisablePatch>();
            SafeEnable<ChangeAimingModePatch>();
            SafeEnable<SetScopeModePatch>();
            SafeEnable<PlayerOnSetInHandsPatch>();
            SafeEnable<PlayerSetInventoryOpenedPatch>();
            SafeEnable<PiPDisabler.OpticComponentUpdaterCopyComponentFromOptic_DisablePiP>();
            SafeEnable<PiPDisabler.OpticComponentUpdaterLateUpdate_DisablePiP>();
            SafeEnable<PiPDisabler.OpticSightLensFade_NoPipPatch>();
            SafeEnable<PWAMethod23Patch>();
            SafeEnable<PlayerLookPatch>();
            SafeEnable<WeaponScalingPatch>();
            FikaCompat.Enable();
            FOVFixCompat.Enable();
            DERPCompat.Enable();

        }

        private static void SafeEnable<T>() where T : ModulePatch, new()
        {
            try
            {
                new T().Enable();
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogSource.LogError($"[Patcher] Failed to enable {typeof(T).Name}: {ex.Message}");
            }
        }
    }
}
