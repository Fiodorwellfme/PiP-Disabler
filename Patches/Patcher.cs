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
            SafeEnable<TacticalRangeFinderOnEnablePatch>();
            SafeEnable<TacticalRangeFinderIgnoreLocalBodyPatch>();
            SafeEnable<ChangeAimingModePatch>();
            SafeEnable<ChangeAimingModeIndexPatch>();
            SafeEnable<SetScopeModePatch>();
            SafeEnable<PlayerOnSetInHandsPatch>();
            SafeEnable<PlayerSetInventoryOpenedPatch>();
            SafeEnable<OpticCameraManagerEnableOptic_NoPipPatch>();
            SafeEnable<OpticCameraManagerSetResolution_NoPipPatch>();
            SafeEnable<CameraClassOnOpticEnabled_NoPipPatch>();
            SafeEnable<MainCameraLodBiasSetByFovPatch>();
            SafeEnable<PiPDisabler.OpticComponentUpdaterCopyComponentFromOptic_DisablePiP>();
            SafeEnable<ScopeZoomReadinessPatch>();
            SafeEnable<ZoomSensitivityPatch>();
            SafeEnable<PiPDisabler.OpticComponentUpdaterLateUpdate_DisablePiP>();
            SafeEnable<PiPDisabler.OpticSightLensFade_NoPipPatch>();
            SafeEnable<PWAMethod23Patch>();
            SafeEnable<PlayerLookPatch>();
            SafeEnable<WeaponScalingPatch>();
            VisualRecoilCompensationPatch.Enable();
            SafeEnable<GrassMotionVectorSuppressionPatch>();
            SafeEnable<PWAWeaponRootZOffsetPatch>();
            SafeEnable<FireModeSwitchMovementPatch>();
            SafeEnable<MagnificationSwitchMovementContextPatch>();
            SafeEnable<ModToggleTriggerMovementPatch>();
            SafeEnable<SwayVectorVelocityFovScalingPatch>();
            SafeEnable<SwayComponentVelocityFovScalingPatch>();
            SafeEnable<SpringVectorAccelerationFovScalingPatch>();
            SafeEnable<SpringComponentAccelerationFovScalingPatch>();
            SafeEnable<RecoilReturnToZeroPatch>();
            SafeEnable<AimWalkSwayWalkPatch>();
            SafeEnable<AimWalkSwayMotionPatch>();
            SafeEnable<AimHeadBobCameraPatch>();
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
                // Always logged (not gated by Debug Logging): a patch that fails to apply
                // silently disables part of the mod and is otherwise invisible in LogOutput.log.
                PiPDisablerPlugin.LogSource.LogError($"[Patcher] Failed to enable {typeof(T).Name}: {ex.Message}");
            }
        }
    }
}
