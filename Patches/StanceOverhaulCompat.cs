
using System;
using System.Reflection;
using EFT;
using UnityEngine;

namespace PiPDisabler.Patches
{
    internal static class StanceOverhaulCompat
    {
        private const string ControllerType =
            "StanceOverhaul.Controllers.StanceController";

        public static bool IsStanceOverhaulTransitioning(Player player)
        {
            return TryGetStanceProperty(player, "IsTransitioning", out var value) && value is bool transitioning && transitioning;
        }

        private static bool TryGetStanceProperty(Player player, string propertyName, out object value)
        {
            value = null;

            if (player == null)
                return false;

            try
            {
                foreach (var component in player.GetComponents<MonoBehaviour>())
                {
                    if (component == null || component.GetType().FullName != ControllerType)
                        continue;

                    var state = component.GetType().GetField("_stanceState",BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(component);

                    var property = state?.GetType().GetProperty(propertyName,BindingFlags.Instance | BindingFlags.Public);

                    if (property == null)
                        return false;

                    value = property.GetValue(state);
                    return true;
                }
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"Could not read Stance Overhaul state: {ex.Message}");
            }

            return false;
        }
    }
}
