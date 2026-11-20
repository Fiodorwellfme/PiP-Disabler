using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace PiPDisabler.Patches
{
    /// <summary>
    /// Stance Overhaul compatibility (upstream 2.1.0 "Stance overhaul compat fix"): while its
    /// StanceController reports a stance transition, the scope is bypassed like sprinting.
    ///
    /// Upstream walks player.GetComponents&lt;MonoBehaviour&gt;() and reflects every call; this
    /// runs every frame, so the type, members and the local player's controller are cached.
    /// </summary>
    internal static class StanceOverhaulCompat
    {
        private const string ControllerTypeName = "StanceOverhaul.Controllers.StanceController";

        private static bool _resolved;
        private static Type _controllerType;
        private static FieldInfo _stateField;
        private static Type _stateType;
        private static PropertyInfo _isTransitioning;

        private static Player _cachedPlayer;
        private static Component _cachedController;
        private static bool _failed;

        public static bool IsStanceOverhaulTransitioning(Player player)
        {
            if (_failed || player == null)
                return false;

            if (!_resolved)
            {
                _resolved = true;
                _controllerType = AccessTools.TypeByName(ControllerTypeName);
                if (_controllerType == null)
                {
                    _failed = true;
                    return false;
                }

                _stateField = AccessTools.Field(_controllerType, "_stanceState");
                PiPDisablerPlugin.LogSource.LogInfo("[StanceOverhaulCompat] Stance Overhaul detected — scope is bypassed during its stance transitions.");
            }

            try
            {
                if (!ReferenceEquals(player, _cachedPlayer) || _cachedController == null)
                {
                    _cachedPlayer = player;
                    _cachedController = player.GetComponent(_controllerType);
                }

                if (_cachedController == null || _stateField == null)
                    return false;

                object state = _stateField.GetValue(_cachedController);
                if (state == null)
                    return false;

                Type stateType = state.GetType();
                if (stateType != _stateType)
                {
                    _stateType = stateType;
                    _isTransitioning = stateType.GetProperty("IsTransitioning", BindingFlags.Instance | BindingFlags.Public);
                }

                return _isTransitioning != null && _isTransitioning.GetValue(state) is bool transitioning && transitioning;
            }
            catch (Exception ex)
            {
                _failed = true;
                PiPDisablerPlugin.LogSource.LogWarning($"[StanceOverhaulCompat] Could not read Stance Overhaul state ({ex.Message}) — compat disabled.");
                return false;
            }
        }
    }
}
