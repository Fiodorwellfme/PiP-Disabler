using System;
using System.Reflection;
using HarmonyLib;
using EFT.Animations;

namespace PiPDisabler.Patches
{
    /// Fika ping system switches ping display system when it detects an optic scope being ADS.
    /// Prefix-patch IsZoomedOpticAiming to return false when PiP Disabler is active
    internal static class FikaCompat
    {
        private static Harmony _harmony;
        private static bool _patched;

        /// <summary>
        /// Attempt to patch Fika's WorldToScreen.IsZoomedOpticAiming.
        /// Safe to call even when Fika is not installed.
        /// </summary>
        public static void Enable()
        {
            if (_patched) return;

            try
            {
                // Resolve Fika type by reflection — no compile-time dependency
                var worldToScreenType = AccessTools.TypeByName("Fika.Core.Main.Utils.WorldToScreen");
                if (worldToScreenType == null)
                {
                    PiPDisablerPlugin.LogInfo(
                        "[FikaCompat] Fika.Core.Main.Utils.WorldToScreen not found — Fika not installed, skipping compat patch.");
                    return;
                }

                var targetMethod = AccessTools.Method(worldToScreenType, "IsZoomedOpticAiming",
                    new[] { typeof(ProceduralWeaponAnimation) });

                var prefix = new HarmonyMethod(
                    typeof(FikaCompat).GetMethod(nameof(IsZoomedOpticAimingPrefix),
                        BindingFlags.NonPublic | BindingFlags.Static));

                _harmony = new Harmony("com.fiodor.pipdisabler.fikacompat");
                _harmony.Patch(targetMethod, prefix: prefix);
                _patched = true;

                PiPDisablerPlugin.LogInfo(
                    "[FikaCompat] Patched WorldToScreen.IsZoomedOpticAiming — pings/healthbars will use main camera when PiP is disabled.");
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogError(
                    $"[FikaCompat] Failed to patch Fika compat: {ex.Message}");
            }
        }

        private static bool IsZoomedOpticAimingPrefix(ref bool __result)
        {
            if (!PiPDisablerPlugin.ModEnabled.Value)
                return true;

            if (!PiPDisablerPlugin.DisablePiP.Value)
                return true;

            if (!ScopeLifecycle.IsScoped)
                return true;

            if (ScopeLifecycle.IsModBypassedForCurrentScope)
                return true;

            __result = false;
            return false;
        }

        public static void Disable()
        {
            if (!_patched || _harmony == null) return;

            try
            {
                _harmony.UnpatchSelf();
                _patched = false;
                PiPDisablerPlugin.LogInfo("[FikaCompat] Unpatched Fika compat.");
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogError(
                    $"[FikaCompat] Failed to unpatch: {ex.Message}");
            }
        }
    }
}
