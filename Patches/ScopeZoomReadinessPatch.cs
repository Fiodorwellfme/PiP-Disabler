using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace PiPDisabler.Patches
{
    /// <summary>
    /// EFT can retain an optic updater before ScopeZoomHandler.Init has supplied
    /// its adjustable-optic data. UpdateScope otherwise dereferences that data
    /// every frame (method_2). Resume the native update as soon as Init completes.
    /// This validity check also applies when the user toggles PiP replacement off.
    /// </summary>
    internal sealed class ScopeZoomReadinessPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ScopeZoomHandler), nameof(ScopeZoomHandler.UpdateScope));

        [PatchPrefix]
        private static bool Prefix(IAdjustableOpticData ____adjustableOpticData)
            => ____adjustableOpticData != null;
    }
}
