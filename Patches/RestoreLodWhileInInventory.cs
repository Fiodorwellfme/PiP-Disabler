using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace PiPDisabler.Patches
{
    internal sealed class PlayerSetInventoryOpenedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player), "SetInventoryOpened");

        [PatchPostfix]
        private static void Postfix(bool opened)
        {
            if (!opened) return;

            CameraSettingsManager.RestoreIfPending();
        }
    }
}