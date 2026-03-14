using Comfort.Common;
using EFT;
using EFT.Animations;

namespace PiPDisabler.Compatibility
{
    internal static class FikaCompat
    {
        internal static bool AdjustUseOpticCamera(bool originalValue)
        {
            if (!originalValue)
                return false;

            var gameWorld = Singleton<GameWorld>.Instance;
            var player = gameWorld != null ? gameWorld.MainPlayer : null;
            if (player == null)
                return originalValue;

            var pwa = player.ProceduralWeaponAnimation;
            if (pwa == null || !pwa.IsAiming)
                return originalValue;

            var currentScope = pwa.CurrentScope;
            if (currentScope == null || !currentScope.IsOptic)
                return originalValue;

            // Single source of truth: use ScopeLifecycle's real bypass state.
            if (ScopeLifecycle.IsCurrentScopeBypassedForCompat(player, pwa, currentScope))
                return originalValue;

            return false;
        }
    }
}
