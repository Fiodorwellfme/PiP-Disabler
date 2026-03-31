using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace PiPDisabler.Patches
{
    internal sealed class SpeedTreeCrossfadePatch : ModulePatch
    {
        private static bool _loggedMissingTarget;

        protected override MethodBase GetTargetMethod()
        {
            var type = AccessTools.TypeByName("SpeedTreeTerrainProcessor");
            if (type == null)
            {
                if (!_loggedMissingTarget)
                {
                    _loggedMissingTarget = true;
                    PiPDisablerPlugin.LogWarn("[SpeedTreeCrossfadePatch] SpeedTreeTerrainProcessor type not found.");
                }
                return null;
            }

            var target = AccessTools.Method(type, "smethod_7");
            if (target == null)
                PiPDisablerPlugin.LogWarn("[SpeedTreeCrossfadePatch] smethod_7 target not found.");
            return target;
        }

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var setFadeMode = AccessTools.PropertySetter(typeof(LODGroup), nameof(LODGroup.fadeMode));
            var setAnimate = AccessTools.PropertySetter(typeof(LODGroup), nameof(LODGroup.animateCrossFading));
            var forceFadeMode = AccessTools.Method(typeof(SpeedTreeCrossfadePatch), nameof(ForceFadeModeNone));
            var forceAnimate = AccessTools.Method(typeof(SpeedTreeCrossfadePatch), nameof(ForceAnimateCrossFadingFalse));

            foreach (var instruction in instructions)
            {
                if (instruction.Calls(setFadeMode))
                {
                    instruction.operand = forceFadeMode;
                }
                else if (instruction.Calls(setAnimate))
                {
                    instruction.operand = forceAnimate;
                }

                yield return instruction;
            }
        }

        private static void ForceFadeModeNone(LODGroup lodGroup, LODFadeMode _)
            => lodGroup.fadeMode = LODFadeMode.None;

        private static void ForceAnimateCrossFadingFalse(LODGroup lodGroup, bool _)
            => lodGroup.animateCrossFading = false;
    }
}
