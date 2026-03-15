using System.Reflection;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace PiPDisabler.Patches
{
    internal sealed class OpticSightOnEnablePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(OpticSight), "OnEnable");

        [PatchPostfix]
        private static void Postfix(OpticSight __instance)
        {
            // Always cache the enabled optic (so it's ready if mod is toggled on later)
            PiPDisablerPlugin.LogInfo(
                $"[Patch] OnEnable: '{(__instance != null ? __instance.name : "null")}' " +
                $"enabled={__instance?.enabled} frame={Time.frameCount}");
            ScopeLifecycle.RecordExternalEvent("Patch.OpticSight.OnEnable", __instance);

            if (!PiPDisablerPlugin.ModEnabled.Value) return;
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
            PiPDisablerPlugin.LogInfo(
                $"[Patch] OnDisable: '{(__instance != null ? __instance.name : "null")}' " +
                $"frame={Time.frameCount}");
            ScopeLifecycle.RecordExternalEvent("Patch.OpticSight.OnDisable", __instance);

            if (!PiPDisablerPlugin.ModEnabled.Value) return;
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
            if (!PiPDisablerPlugin.ModEnabled.Value) return;

            PiPDisablerPlugin.LogInfo(
                $"[Patch] ChangeAimingMode frame={Time.frameCount}");
            ScopeLifecycle.RecordExternalEvent("Patch.ChangeAimingMode");
            ScopeLifecycle.CheckAndUpdate("ChangeAimingMode");
        }
    }

    internal sealed class FirearmControllerSetAimIndexPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.FirearmController), "SetAimIndex");

        [PatchPostfix]
        private static void Postfix(Player.FirearmController __instance, int index)
        {
            ScopeLifecycle.RecordExternalEvent("Patch.SetAimIndex", __instance, $"index={index}");
        }
    }
}
