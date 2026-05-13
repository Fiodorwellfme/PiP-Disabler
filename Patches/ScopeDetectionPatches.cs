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

    internal sealed class TacticalRangeFinderOnEnablePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(TacticalRangeFinderController), "OnEnable");

        [PatchPostfix]
        private static void Postfix(TacticalRangeFinderController __instance)
        {
            if (!Settings.ModEnabled.Value) return;
            if (__instance == null) return;

            var opticSight = ResolveRangeFinderOptic(__instance.transform);

            PiPDisablerPlugin.DebugLogInfo(
                $"[Patch] TacticalRangeFinder OnEnable: optic='{opticSight?.name ?? "null"}' " +
                $"path='{GetPath(opticSight != null ? opticSight.transform : null)}' frame={Time.frameCount}");

            ScopeLifecycle.RestoreBypassedOpticState(opticSight,
                reason: "tactical rangefinder enable");
        }

        private static OpticSight ResolveRangeFinderOptic(Transform rangeFinderTransform)
        {
            if (rangeFinderTransform == null) return null;

            Transform itemRoot = null;
            for (var t = rangeFinderTransform; t != null; t = t.parent)
            {
                if (t.name == "item")
                {
                    itemRoot = t;
                    break;
                }
            }

            var searchRoot = itemRoot != null ? itemRoot : rangeFinderTransform.root;
            var optics = searchRoot.GetComponentsInChildren<OpticSight>(true);
            if (optics == null || optics.Length == 0)
                return null;

            OpticSight best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < optics.Length; i++)
            {
                var optic = optics[i];
                if (optic == null) continue;

                string path = GetPath(optic.transform);
                int score = 0;
                if (optic.isActiveAndEnabled) score += 100;
                if (path.IndexOf("optic_camera", System.StringComparison.OrdinalIgnoreCase) >= 0) score += 50;
                if (optic.CameraData != null) score += 10;
                if (optic.ScopeData != null) score += 10;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = optic;
                }
            }

            return best;
        }

        private static string GetPath(Transform transform)
        {
            if (transform == null) return "null";

            string path = transform.name;
            for (var t = transform.parent; t != null; t = t.parent)
                path = t.name + "/" + path;
            return path;
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
