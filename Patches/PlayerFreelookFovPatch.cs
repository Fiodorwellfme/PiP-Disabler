using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace PiPDisabler.Patches
{
    /// <summary>
    /// Preserves ADS FOV across freelook release.
    /// Vanilla EFT can force a hardcoded ADS FOV when freelook exits; this patch
    /// restores the exact camera FOV that was active before freelook started.
    /// </summary>
    internal sealed class PlayerFreelookFovPatch : ModulePatch
    {
        private const float Epsilon = 1E-45f;

        private static readonly Dictionary<Player, float> _cachedAdsFovByPlayer =
            new Dictionary<Player, float>(2);

        private static readonly FieldInfo _mouseLookControlField =
            AccessTools.Field(typeof(Player), "_mouseLookControl");

        private struct LookState
        {
            public bool WasMouseLookControl;
            public bool WasAiming;
        }

        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player), "Look", new[] { typeof(float), typeof(float), typeof(bool) });

        [PatchPrefix]
        private static void Prefix(Player __instance, float deltaLookY, float deltaLookX, out LookState __state)
        {
            __state = default;
            if (!PiPDisablerPlugin.ModEnabled.Value || __instance == null || _mouseLookControlField == null)
                return;

            bool wasAiming = IsPlayerAiming(__instance);
            bool wasMouseLookControl = GetMouseLookControl(__instance);

            __state = new LookState
            {
                WasAiming = wasAiming,
                WasMouseLookControl = wasMouseLookControl
            };

            // Freelook activation is consumed in this Look call when both axes have input.
            if (!wasAiming || wasMouseLookControl)
                return;

            if (Math.Abs(deltaLookY) < Epsilon || Math.Abs(deltaLookX) < Epsilon)
                return;

            var camera = CameraClass.Instance;
            if (camera == null)
                return;

            _cachedAdsFovByPlayer[__instance] = camera.Fov;
        }

        [PatchPostfix]
        private static void Postfix(Player __instance, LookState __state)
        {
            if (!PiPDisablerPlugin.ModEnabled.Value || __instance == null || _mouseLookControlField == null)
                return;

            // Clean stale cache after exiting ADS.
            if (!IsPlayerAiming(__instance))
            {
                _cachedAdsFovByPlayer.Remove(__instance);
                return;
            }

            bool isMouseLookControl = GetMouseLookControl(__instance);
            bool freelookJustStopped = __state.WasAiming && __state.WasMouseLookControl && !isMouseLookControl;
            if (!freelookJustStopped)
                return;

            if (!_cachedAdsFovByPlayer.TryGetValue(__instance, out float restoreFov))
                return;

            var camera = CameraClass.Instance;
            if (camera == null)
                return;

            camera.SetFov(restoreFov, 0f, true);
            _cachedAdsFovByPlayer.Remove(__instance);

            PiPDisablerPlugin.LogInfo($"[FreelookFov] Restored ADS FOV after freelook release: {restoreFov:F2}");
        }

        private static bool GetMouseLookControl(Player player)
        {
            try { return (bool)_mouseLookControlField.GetValue(player); }
            catch { return false; }
        }

        private static bool IsPlayerAiming(Player player)
        {
            try
            {
                return player.HandsController != null && player.HandsController.IsAiming && !player.IsAI;
            }
            catch
            {
                return false;
            }
        }
    }
}
