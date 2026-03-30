using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace PiPDisabler
{
    internal static class ZeroingController
    {
        private static float _lastZeroingTime;
        private const float ZeroingCooldownSeconds = 0.2f;

        public static int CurrentZeroingMeters { get; private set; }

        public static void Tick()
        {
            if (!PiPDisablerPlugin.EnableZeroing.Value || !ScopeLifecycle.IsScoped)
                return;

            if (Time.unscaledTime - _lastZeroingTime < ZeroingCooldownSeconds)
                return;

            bool wantsUp = Input.GetKeyDown(PiPDisablerPlugin.ZeroingUpKey.Value);
            bool wantsDown = Input.GetKeyDown(PiPDisablerPlugin.ZeroingDownKey.Value);
            if (!wantsUp && !wantsDown)
                return;

            Player player = PiPDisablerPlugin.GetLocalPlayer();
            if (player == null)
                return;

            var pwa = player.ProceduralWeaponAnimation;
            if (pwa == null)
                return;

            if (wantsUp)
                pwa.OpticCalibrationSwitchUp();
            else
                pwa.OpticCalibrationSwitchDown();

            _lastZeroingTime = Time.unscaledTime;
            ReadCurrentZeroing(player);
        }

        public static void ReadCurrentZeroing(Player player = null)
        {
            if (player == null)
                player = PiPDisablerPlugin.GetLocalPlayer();
            if (player == null)
                return;

            var pwa = player.ProceduralWeaponAnimation;
            if (pwa == null)
                return;

            SightComponent sight = pwa.CurrentAimingMod;
            if (sight == null)
                return;

            int meters = sight.GetCurrentOpticCalibrationDistance();
            if (meters == CurrentZeroingMeters)
                return;

            CurrentZeroingMeters = meters;
            PiPDisablerPlugin.LogInfo($"[Zeroing] Current distance: {meters}m");
        }

        public static void Reset()
        {
            CurrentZeroingMeters = 0;
        }
    }
}
