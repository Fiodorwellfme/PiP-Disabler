using System;
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

            if (!PiPDisablerPlugin.ModEnabled.Value) return;
            ScopeLifecycle.OnOpticDisabled(__instance);
        }
    }

    internal sealed class ChangeAimingModePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.FirearmController), "ChangeAimingMode", Type.EmptyTypes);

        [PatchPrefix]
        private static void Prefix(ref int __state)
        {
            if (!PiPDisablerPlugin.ModEnabled.Value) return;
            __state = ScopeAimStateResolver.GetCurrentWeaponAimIndex();
        }

        [PatchPostfix]
        private static void Postfix(int __state)
        {
            if (!PiPDisablerPlugin.ModEnabled.Value) return;

            PiPDisablerPlugin.LogInfo(
                $"[Patch] ChangeAimingMode frame={Time.frameCount}");
            ScopeLifecycle.OnAimModeChanged("ChangeAimingMode()", __state);
        }
    }

    internal sealed class ChangeAimingModeIndexedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.FirearmController), "ChangeAimingMode", new[] { typeof(int) });

        [PatchPrefix]
        private static void Prefix(ref int __state)
        {
            if (!PiPDisablerPlugin.ModEnabled.Value) return;
            __state = ScopeAimStateResolver.GetCurrentWeaponAimIndex();
        }

        [PatchPostfix]
        private static void Postfix(int modeIndex, int __state)
        {
            if (!PiPDisablerPlugin.ModEnabled.Value) return;

            PiPDisablerPlugin.LogInfo(
                $"[Patch] ChangeAimingMode({modeIndex}) frame={Time.frameCount}");
            ScopeLifecycle.OnAimModeChanged($"ChangeAimingMode({modeIndex})", __state);
        }
    }

    internal sealed class SetAimPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.FirearmController), "SetAim", new[] { typeof(int) });

        [PatchPrefix]
        private static void Prefix(ref int __state)
        {
            if (!PiPDisablerPlugin.ModEnabled.Value) return;
            __state = ScopeAimStateResolver.GetCurrentWeaponAimIndex();
        }

        [PatchPostfix]
        private static void Postfix(int scopeIndex, int __state)
        {
            if (!PiPDisablerPlugin.ModEnabled.Value) return;

            PiPDisablerPlugin.LogInfo(
                $"[Patch] SetAim({scopeIndex}) frame={Time.frameCount}");
            ScopeLifecycle.OnAimModeChanged($"SetAim({scopeIndex})", __state);
        }
    }
}
