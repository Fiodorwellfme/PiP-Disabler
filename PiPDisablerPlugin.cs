using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PiPDisabler
{
    [BepInPlugin("com.fiodor.pipdisabler", "PiP-Disabler", "0.7.0")]
    public sealed class PiPDisablerPlugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        internal static PiPDisablerPlugin Instance;
        private void Awake()
        {
            Instance = this;
            LogSource = Logger;
            LogSource.LogInfo("plugin loaded!");
            Settings.Init(Config);
            Patches.Patcher.Enable();
            ScopeLifecycle.Init();
            FreelookTracker.Init();
            Settings.ModEnabled.SettingChanged += OnModEnabledChanged;
            Settings.EnableWeaponScaling.SettingChanged += OnWeaponScalingToggled;
            Settings.ScopeWhitelistNames.SettingChanged += OnWhitelistSettingsChanged;

            LogSource.LogInfo("PiP-Disabler 0.6.0 loaded.");
        }
              
        private void OnDestroy()
        {
            // Plugin unload or game exit — restore everything
            ScopeLifecycle.ForceExit();
            LensTransparency.FullRestoreAll();
            MeshSurgeryManager.CleanupForShutdown();
            PiPDisabler.RestoreAllCameras();

            Settings.ModEnabled.SettingChanged -= OnModEnabledChanged;
            Settings.EnableWeaponScaling.SettingChanged -= OnWeaponScalingToggled;
            Settings.ScopeWhitelistNames.SettingChanged -= OnWhitelistSettingsChanged;
        }

        private static void OnModEnabledChanged(object sender, EventArgs e)
        {
            if (!Settings.ModEnabled.Value)
            {
                ScopeLifecycle.ForceExit();
                LensTransparency.FullRestoreAll();
                PiPDisabler.RestoreAllCameras();
            }
            else
            {
                ScopeLifecycle.SyncState();
            }
        }

        private static void OnWeaponScalingToggled(object sender, EventArgs e)
        {
            if (!Settings.EnableWeaponScaling.Value)
            {
                Patches.WeaponScalingPatch.RestoreScale();
            }
            else if (ScopeLifecycle.IsScoped)
            {
                Patches.WeaponScalingPatch.CaptureBaseState();
            }
        }

        private static void OnWhitelistSettingsChanged(object sender, EventArgs e)
        {
            if (!Settings.ModEnabled.Value) return;
            if (ScopeLifecycle.IsScoped)
            {
                ScopeLifecycle.ForceExit();
                ScopeLifecycle.SyncState();
            }
        }


        private void Update()
        {
            if (Settings.ModToggleKey.Value != KeyCode.None && InputProxy.GetKeyDown(Settings.ModToggleKey.Value))
            {
                Settings.ModEnabled.Value = !Settings.ModEnabled.Value;
                LogSource.LogInfo($"[Global] Mod {(Settings.ModEnabled.Value ? "ENABLED" : "DISABLED")}");
            }
            if (!Settings.ModEnabled.Value) return;

            if (Settings.ScopeWhitelistToggleEntryKey.Value != KeyCode.None && InputProxy.GetKeyDown(Settings.ScopeWhitelistToggleEntryKey.Value))
            {
                ScopeLifecycle.ToggleActiveScopeWhitelistEntry();
            }

            if (Settings.SaveCustomMeshSurgerySettingsKey.Value != KeyCode.None && InputProxy.GetKeyDown(Settings.SaveCustomMeshSurgerySettingsKey.Value))
            {
                string scopeKey = ScopeLifecycle.GetActiveScopeWhitelistKey();
                if (string.IsNullOrWhiteSpace(scopeKey))
                {
                    LogSource.LogInfo("[CustomMeshSettings] Save ignored: no active scope key");
                }
                else
                {
                    bool saved = PerScopeMeshSurgerySettings.SaveCustomSettingsForScope(scopeKey);
                    LogSource.LogInfo(saved
                        ? $"[CustomMeshSettings] Saved custom settings for scope key '{scopeKey}'"
                        : "[CustomMeshSettings] Save failed");
                }
            }

            if (Settings.DeleteCustomMeshSurgerySettingsKey.Value != KeyCode.None && InputProxy.GetKeyDown(Settings.DeleteCustomMeshSurgerySettingsKey.Value))
            {
                string scopeKey = ScopeLifecycle.GetActiveScopeWhitelistKey();
                if (string.IsNullOrWhiteSpace(scopeKey))
                {
                    LogSource.LogInfo("[CustomMeshSettings] Delete ignored: no active scope key");
                }
                else
                {
                    bool removed = PerScopeMeshSurgerySettings.DeleteCustomSettingsForScope(scopeKey);
                    LogSource.LogInfo(removed
                        ? $"[CustomMeshSettings] Deleted custom settings for scope key '{scopeKey}'"
                        : $"[CustomMeshSettings] No custom settings existed for scope key '{scopeKey}'");
                }
            }

            PiPDisabler.TickBaseOpticCamera();
            ScopeLifecycle.CheckAndUpdate("Update");
            ScopeLifecycle.Tick();
        }
        internal static string GetCurrentLocationId()
        {
            try
            {
                var gw = Singleton<GameWorld>.Instance;
                return gw != null ? gw.LocationId : null;
            }
            catch
            {
                return null;
            }
        }

        internal static float GetManualLodBiasForCurrentMap()
        {
            float fallback = Settings.ManualLodBias != null ? Settings.ManualLodBias.Value : 0f;
            string locationId = GetCurrentLocationId();
            if (string.IsNullOrEmpty(locationId) || Settings.MapManualLodBias == null)
                return fallback;

            ConfigEntry<float> entry;
            if (Settings.MapManualLodBias.TryGetValue(locationId, out entry) && entry != null)
                return entry.Value;

            return fallback;
        }

        private void BindPerMapLodBias(string configKeySuffix, string mapDisplayName, params string[] locationIds)
        {
            if (locationIds == null || locationIds.Length == 0 || string.IsNullOrWhiteSpace(configKeySuffix))
                return;

            var entry = Config.Bind("General Per-Map", $"ManualLodBias_{configKeySuffix}", Settings.ManualLodBias.Value,
                new ConfigDescription(
                    $"Manual LOD bias override while scoped on map '{mapDisplayName}'.\n" +
                    "0 = auto (baseLodBias * magnification).\n" +
                    ">0 = force this exact value (e.g. 4.0).",
                    new AcceptableValueRange<float>(0f, 20f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            for (int i = 0; i < locationIds.Length; i++)
            {
                string locationId = locationIds[i];
                if (string.IsNullOrWhiteSpace(locationId))
                    continue;
                Settings.MapManualLodBias[locationId] = entry;
            }
        }
    }
}
