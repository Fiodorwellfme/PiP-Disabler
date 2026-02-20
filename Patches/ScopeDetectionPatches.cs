using System.Reflection;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace ScopeHousingMeshSurgery.Patches
{
    internal sealed class OpticSightOnEnablePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(OpticSight), "OnEnable");

        [PatchPostfix]
        private static void Postfix(OpticSight __instance)
        {
            // Always cache the enabled optic (so it's ready if mod is toggled on later)
            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[Patch] OnEnable: '{(__instance != null ? __instance.name : "null")}' " +
                $"enabled={__instance?.enabled} frame={Time.frameCount}");

            if (!ScopeHousingMeshSurgeryPlugin.ModEnabled.Value) return;
            ScopeLifecycle.OnOpticEnabled(__instance);
        }
    }

    internal sealed class OpticSightOnDisablePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(OpticSight), "OnDisable");

        [PatchPostfix]
        private static void Postfix(OpticSight __instance)
        {
            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[Patch] OnDisable: '{(__instance != null ? __instance.name : "null")}' " +
                $"frame={Time.frameCount}");

            if (!ScopeHousingMeshSurgeryPlugin.ModEnabled.Value) return;
            ScopeLifecycle.OnOpticDisabled(__instance);
        }
    }

    internal sealed class ChangeAimingModePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.FirearmController), "ChangeAimingMode");

        [PatchPostfix]
        private static void Postfix()
        {
            if (!ScopeHousingMeshSurgeryPlugin.ModEnabled.Value) return;

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[Patch] ChangeAimingMode frame={Time.frameCount}");
            ScopeLifecycle.CheckAndUpdate();
        }
    }
}
