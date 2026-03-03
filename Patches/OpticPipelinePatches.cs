using System;
using System.Diagnostics;
using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace ScopeHousingMeshSurgery.Patches
{
    /// <summary>
    /// Diagnostics + mitigation hooks for expensive vanilla optic setup paths.
    /// </summary>
    internal sealed class CameraClassMethod10Patch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(CameraClass), "method_10");

        [PatchPrefix]
        private static bool Prefix(ref long __state)
        {
            if (ScopeHousingMeshSurgeryPlugin.OpticPipelineTimingLogs.Value)
                __state = Stopwatch.GetTimestamp();

            if (ScopeHousingMeshSurgeryPlugin.ModEnabled.Value
                && ScopeHousingMeshSurgeryPlugin.DisablePiP.Value
                && ScopeHousingMeshSurgeryPlugin.SkipVanillaOpticPipelineInNoPiP.Value
                && !ScopeLifecycle.IsModBypassedForCurrentScope)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[OpticPipeline] Skipping CameraClass.method_10 (NoPiP mitigation) frame={Time.frameCount}");
                return false;
            }

            return true;
        }

        [PatchPostfix]
        private static void Postfix(long __state)
        {
            if (!ScopeHousingMeshSurgeryPlugin.OpticPipelineTimingLogs.Value) return;
            if (__state == 0) return;

            double ms = (Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency;
            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[Timing] CameraClass.method_10 took {ms:F3}ms frame={Time.frameCount}");
        }
    }

    internal sealed class OpticSightOnEnableTimingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(OpticSight), "OnEnable");

        [PatchPrefix]
        private static void Prefix(ref long __state)
        {
            if (!ScopeHousingMeshSurgeryPlugin.OpticPipelineTimingLogs.Value) return;
            __state = Stopwatch.GetTimestamp();
        }

        [PatchPostfix]
        private static void Postfix(OpticSight __instance, long __state)
        {
            if (!ScopeHousingMeshSurgeryPlugin.OpticPipelineTimingLogs.Value) return;
            if (__state == 0) return;

            double ms = (Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency;
            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[Timing] OpticSight.OnEnable '{(__instance != null ? __instance.name : "null")}' took {ms:F3}ms frame={Time.frameCount}");
        }
    }

    internal sealed class SetFovParamsTimingPatch : ModulePatch
    {
        private static int _windowStartFrame = -1;
        private static int _windowCount;

        protected override MethodBase GetTargetMethod()
        {
            var pwaType = AccessTools.TypeByName("EFT.Animations.ProceduralWeaponAnimation");
            return pwaType != null ? AccessTools.Method(pwaType, "SetFovParams") : null;
        }

        [PatchPrefix]
        private static void Prefix()
        {
            if (!ScopeHousingMeshSurgeryPlugin.OpticPipelineTimingLogs.Value) return;

            if (_windowStartFrame < 0)
                _windowStartFrame = Time.frameCount;

            _windowCount++;

            int elapsedFrames = Time.frameCount - _windowStartFrame;
            if (elapsedFrames < 60) return;

            float cps = elapsedFrames > 0 ? _windowCount / (elapsedFrames / 60f) : 0f;
            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[Timing] SetFovParams calls: {_windowCount} over {elapsedFrames} frames (~{cps:F1}/sec)");

            _windowStartFrame = Time.frameCount;
            _windowCount = 0;
        }
    }
}
