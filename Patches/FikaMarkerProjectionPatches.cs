using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using PiPDisabler.Compatibility;
using SPT.Reflection.Patching;

namespace PiPDisabler.Patches
{
    internal abstract class FikaUseOpticZoomBoolPatchBase : ModulePatch
    {
        private readonly string _typeName;
        private readonly string _methodName;

        protected FikaUseOpticZoomBoolPatchBase(string typeName, string methodName)
        {
            _typeName = typeName;
            _methodName = methodName;
        }

        protected override MethodBase GetTargetMethod()
        {
            var type = AccessTools.TypeByName(_typeName);
            if (type == null) return null;

            return AccessTools.Method(type, _methodName);
        }

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var adjust = AccessTools.Method(typeof(FikaCompat), nameof(FikaCompat.AdjustUseOpticCamera));
            var settingGetterName = GetSettingGetterName(__originalMethod);
            bool shouldWrapNextConfigValue = false;

            foreach (var code in instructions)
            {
                if (CallsSettingGetter(code, settingGetterName))
                {
                    shouldWrapNextConfigValue = true;
                    yield return code;
                    continue;
                }

                yield return code;

                if (shouldWrapNextConfigValue && IsBooleanConfigValueGetter(code))
                {
                    yield return new CodeInstruction(OpCodes.Call, adjust);
                    shouldWrapNextConfigValue = false;
                }
            }
        }

        private static string GetSettingGetterName(MethodBase method)
        {
            if (method?.DeclaringType == null)
                return null;

            var methodId = method.DeclaringType.FullName + "." + method.Name;
            if (string.Equals(methodId, "Fika.Core.Main.Factories.PingFactory+AbstractPing.Update", StringComparison.Ordinal))
                return "get_PingUseOpticZoom";

            if (string.Equals(methodId, "Fika.Core.Main.Components.FikaHealthBar.UpdateScreenSpacePosition", StringComparison.Ordinal))
                return "get_NamePlateUseOpticZoom";

            return null;
        }

        private static bool CallsSettingGetter(CodeInstruction code, string getterName)
        {
            if (code == null || string.IsNullOrEmpty(getterName)) return false;
            if (code.opcode != OpCodes.Call && code.opcode != OpCodes.Callvirt) return false;

            var method = code.operand as MethodInfo;
            if (method == null) return false;

            return string.Equals(method.Name, getterName, StringComparison.Ordinal);
        }

        private static bool IsBooleanConfigValueGetter(CodeInstruction code)
        {
            if (code == null) return false;
            if (code.opcode != OpCodes.Call && code.opcode != OpCodes.Callvirt) return false;

            var method = code.operand as MethodInfo;
            if (method == null) return false;
            if (!string.Equals(method.Name, "get_Value", StringComparison.Ordinal)) return false;

            return method.ReturnType == typeof(bool);
        }
    }

    internal sealed class FikaAbstractPingUpdatePatch : FikaUseOpticZoomBoolPatchBase
    {
        public FikaAbstractPingUpdatePatch()
            : base(
                "Fika.Core.Main.Factories.PingFactory+AbstractPing",
                "Update")
        {
        }
    }

    internal sealed class FikaHealthBarUpdateScreenSpacePositionPatch : FikaUseOpticZoomBoolPatchBase
    {
        public FikaHealthBarUpdateScreenSpacePositionPatch()
            : base(
                "Fika.Core.Main.Components.FikaHealthBar",
                "UpdateScreenSpacePosition")
        {
        }
    }
}
