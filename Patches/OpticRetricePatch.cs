using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace PiPDisabler.Patches
{
    internal sealed class OpticRetriceOnPreCullPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(OpticRetrice), "OnPreCull");

        [PatchPrefix]
        private static bool Prefix()
        {
            return !PiPDisablerPlugin.ModEnabled.Value || ScopeLifecycle.IsScoped;
        }
    }
}
