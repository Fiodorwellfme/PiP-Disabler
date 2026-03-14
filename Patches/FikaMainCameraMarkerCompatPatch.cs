using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace PiPDisabler.Patches
{
    internal sealed class FikaPingMainCameraCompatPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            var type = AccessTools.TypeByName("Fika.Core.Main.Factories.PingFactory+AbstractPing");
            return type != null ? AccessTools.Method(type, "Update") : null;
        }

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var fikaConfigType = AccessTools.TypeByName("Fika.Core.FikaConfig");
            var pingUseOpticZoomGetter = fikaConfigType != null
                ? AccessTools.PropertyGetter(fikaConfigType, "PingUseOpticZoom")
                : null;
            var configValueGetter = AccessTools.PropertyGetter(typeof(ConfigEntry<bool>), "Value");

            bool replaced = false;
            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i < list.Count; i++)
            {
                if (!replaced && pingUseOpticZoomGetter != null && configValueGetter != null &&
                    i + 1 < list.Count &&
                    list[i].Calls(pingUseOpticZoomGetter) &&
                    list[i + 1].Calls(configValueGetter))
                {
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Ldc_I4_0);
                    i++;
                    replaced = true;
                    continue;
                }

                yield return list[i];
            }

            if (!replaced)
                PiPDisablerPlugin.LogWarn("[FikaCompat] Ping patch did not find PingUseOpticZoom IL pattern.");
        }
    }

    internal sealed class FikaHealthBarMainCameraCompatPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            var type = AccessTools.TypeByName("Fika.Core.Main.Components.FikaHealthBar");
            return type != null ? AccessTools.Method(type, "UpdateScreenSpacePosition") : null;
        }

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var fikaConfigType = AccessTools.TypeByName("Fika.Core.FikaConfig");
            var namePlateUseOpticZoomGetter = fikaConfigType != null
                ? AccessTools.PropertyGetter(fikaConfigType, "NamePlateUseOpticZoom")
                : null;
            var configValueGetter = AccessTools.PropertyGetter(typeof(ConfigEntry<bool>), "Value");

            bool replaced = false;
            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i < list.Count; i++)
            {
                if (!replaced && namePlateUseOpticZoomGetter != null && configValueGetter != null &&
                    i + 1 < list.Count &&
                    list[i].Calls(namePlateUseOpticZoomGetter) &&
                    list[i + 1].Calls(configValueGetter))
                {
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Ldc_I4_0);
                    i++;
                    replaced = true;
                    continue;
                }

                yield return list[i];
            }

            if (!replaced)
                PiPDisablerPlugin.LogWarn("[FikaCompat] Health bar patch did not find NamePlateUseOpticZoom IL pattern.");
        }
    }
}
