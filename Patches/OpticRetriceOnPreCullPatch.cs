using System.Reflection;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace PiPDisabler.Patches
{
    internal sealed class OpticRetriceOnPreCullPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            var type = AccessTools.TypeByName("EFT.CameraControl.OpticRetrice");
            return AccessTools.Method(type, "OnPreCull");
        }

        [PatchPrefix]
        private static bool Prefix()
        {
            if (!PiPDisablerPlugin.ModEnabled.Value)
                return true;

            if (ScopeLifecycle.IsModBypassedForCurrentScope)
                return true;

            return ScopeLifecycle.IsScoped && ScopeLifecycle.IsOpticSubScopeActive;
        }
    }
}
