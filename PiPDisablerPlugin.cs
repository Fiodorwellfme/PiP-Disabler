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
    [BepInPlugin("com.fiodor.pipdisabler", "PiP-Disabler", "0.8.0")]
    [BepInDependency("com.fontaine.fovfix", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.Shibatsu.DynamicExternalResolution", BepInDependency.DependencyFlags.SoftDependency)]

    public sealed class PiPDisablerPlugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        internal static PiPDisablerPlugin Instance;
        private void Awake()
        {
            Instance = this;
            LogSource = Logger;
            LogSource.LogInfo("PiP-Disabler 0.8.0 loaded.");
            Settings.Init(Config);
            Patches.Patcher.Enable();
            ScopeLifecycle.Init();
            FreelookTracker.Init();
            Settings.ModEnabled.SettingChanged += OnModEnabledChanged;
            Settings.ScopeWhitelistNames.SettingChanged += OnWhitelistSettingsChanged;
        }

              
        private void OnDestroy()
        {
            // Plugin unload or game exit — restore everything
            ScopeLifecycle.ForceExit();
            LensTransparency.FullRestoreAll();
            MeshSurgeryManager.CleanupForShutdown();
            PiPDisabler.RestoreAllCameras();

            Settings.ModEnabled.SettingChanged -= OnModEnabledChanged;
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
    }
}
