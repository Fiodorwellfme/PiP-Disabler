using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using PiPDisabler.Compatibility;
using SPT.Reflection.Patching;

namespace PiPDisabler.Patches
{
    internal abstract class FikaMarkerProjectionPatchBase : ModulePatch
    {
        private readonly string[] _candidateTypeNames;
        private readonly string[] _candidateMethodNames;

        protected FikaMarkerProjectionPatchBase(string[] candidateTypeNames, string[] candidateMethodNames)
        {
            _candidateTypeNames = candidateTypeNames;
            _candidateMethodNames = candidateMethodNames;
        }

        protected override MethodBase GetTargetMethod()
        {
            for (int t = 0; t < _candidateTypeNames.Length; t++)
            {
                var type = AccessTools.TypeByName(_candidateTypeNames[t]);
                if (type == null) continue;

                for (int m = 0; m < _candidateMethodNames.Length; m++)
                {
                    var method = AccessTools.Method(type, _candidateMethodNames[m]);
                    if (method != null && MethodCallsProjectToCanvas(method))
                        return method;
                }

                var methods = AccessTools.GetDeclaredMethods(type);
                for (int i = 0; i < methods.Count; i++)
                {
                    if (MethodCallsProjectToCanvas(methods[i]))
                        return methods[i];
                }
            }

            return null;
        }

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var adjustMethod = AccessTools.Method(typeof(FikaCompat), nameof(FikaCompat.AdjustUseOpticCamera));
            foreach (var code in instructions)
            {
                if (IsProjectToCanvasCall(code))
                    yield return new CodeInstruction(OpCodes.Call, adjustMethod);

                yield return code;
            }
        }

        private static bool MethodCallsProjectToCanvas(MethodBase method)
        {
            if (method == null) return false;

            var body = method.GetMethodBody();
            return body != null;
        }

        private static bool IsProjectToCanvasCall(CodeInstruction instruction)
        {
            if (instruction == null) return false;
            if (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt) return false;

            var method = instruction.operand as MethodInfo;
            if (method == null) return false;
            if (!string.Equals(method.Name, "ProjectToCanvas", StringComparison.Ordinal)) return false;
            if (method.DeclaringType == null) return false;
            if (!method.DeclaringType.FullName.Contains("WorldToScreen")) return false;

            var parameters = method.GetParameters();
            if (parameters.Length == 0) return false;

            return parameters[parameters.Length - 1].ParameterType == typeof(bool);
        }
    }

    internal sealed class FikaCoopPingsMarkerProjectionPatch : FikaMarkerProjectionPatchBase
    {
        public FikaCoopPingsMarkerProjectionPatch()
            : base(
                new[]
                {
                    "Fika.Core.Coop.Pings.CoopPingManager",
                    "Fika.Core.Coop.Pings.CoopPingsManager",
                    "Fika.Core.Coop.Components.CoopPingComponent"
                },
                new[]
                {
                    "Update",
                    "LateUpdate",
                    "UpdateMarkers",
                    "UpdatePingMarkers"
                })
        {
        }
    }

    internal sealed class FikaNamePlatesMarkerProjectionPatch : FikaMarkerProjectionPatchBase
    {
        public FikaNamePlatesMarkerProjectionPatch()
            : base(
                new[]
                {
                    "Fika.Core.Coop.Players.NamePlates.CoopNamePlatesManager",
                    "Fika.Core.Coop.Players.NamePlates.NamePlateManager",
                    "Fika.Core.Coop.Components.NamePlateComponent"
                },
                new[]
                {
                    "Update",
                    "LateUpdate",
                    "UpdateNamePlates",
                    "UpdateMarker"
                })
        {
        }
    }

    internal sealed class FikaHealthBarsMarkerProjectionPatch : FikaMarkerProjectionPatchBase
    {
        public FikaHealthBarsMarkerProjectionPatch()
            : base(
                new[]
                {
                    "Fika.Core.Coop.Players.HealthBars.CoopHealthBarManager",
                    "Fika.Core.Coop.Players.HealthBars.HealthBarManager",
                    "Fika.Core.Coop.Components.HealthBarComponent"
                },
                new[]
                {
                    "Update",
                    "LateUpdate",
                    "UpdateHealthBars",
                    "UpdateMarker"
                })
        {
        }
    }
}
