using System.Reflection;
using EFT;
using EFT.CameraControl;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
using System.Linq;

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
            PiPDisablerPlugin.DebugLogInfo(
                $"[Patch] OnEnable: '{(__instance != null ? __instance.name : "null")}' " +
                $"enabled={__instance?.enabled} frame={Time.frameCount}");

            if (!Settings.ModEnabled.Value) return;
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
            PiPDisablerPlugin.DebugLogInfo(
                $"[Patch] OnDisable: '{(__instance != null ? __instance.name : "null")}' " +
                $"frame={Time.frameCount}");

            if (!Settings.ModEnabled.Value) return;
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
            if (!Settings.ModEnabled.Value) return;

            PiPDisablerPlugin.DebugLogInfo(
                $"[Patch] ChangeAimingMode frame={Time.frameCount}");
            ScopeLifecycle.CheckAndUpdate("ChangeAimingMode");
            ScopeLifecycle.OnSetScopeMode();
        }
    }

    /// <summary>
    /// Postfix on Player.FirearmController.SetScopeMode(FirearmScopeStateStruct[]).
    /// Fires after EFT applies the new scope/mode state to SightComponent, so
    /// ScopeLifecycle re-applies FOV change immediately.
    /// </summary>
    internal sealed class SetScopeModePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // FirearmController is an inner class of Player; find SetScopeMode by name and
            // parameter type (FirearmScopeStateStruct[]) to avoid ambiguity.
            var fcType = typeof(Player.FirearmController);
            var method = fcType.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m =>
                    m.Name == "SetScopeMode"
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType.IsArray);

            if (method == null)
                PiPDisablerPlugin.DebugLogInfo("[Patch] SetScopeMode: target method not found");

            return method;
        }

        [PatchPostfix]
        private static void Postfix()
        {
            if (!Settings.ModEnabled.Value) return;

            PiPDisablerPlugin.DebugLogInfo(
                $"[Patch] SetScopeMode frame={Time.frameCount}");
            ScopeLifecycle.OnSetScopeMode();
        }
    }

    /// <summary>
    /// Postfix on Player.OnSetInHands(GEventArgs9).
    /// Slot/weapon switches flow through this path; re-sync scope state so ADS
    /// enter logic does not depend on manual slot toggling.
    /// </summary>
    internal sealed class PlayerOnSetInHandsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player), "OnSetInHands");

        [PatchPostfix]
        private static void Postfix(Player __instance, GEventArgs9 eventArgs)
        {
            if (!Settings.ModEnabled.Value) return;
            if (__instance == null || eventArgs == null || eventArgs.Status != CommandStatus.Succeed) return;

            var localPlayer = Helpers.GetLocalPlayer();
            if (!ReferenceEquals(__instance, localPlayer)) return;

            PiPDisablerPlugin.DebugLogInfo(
                $"[Patch] OnSetInHands frame={Time.frameCount} item='{eventArgs.Item?.TemplateId ?? "null"}'");

            if (ScopeLifecycle.IsScoped)
                ScopeLifecycle.ForceExit();

            ScopeLifecycle.SyncState();
        }
    }
}
