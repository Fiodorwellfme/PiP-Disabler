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
    [BepInPlugin("com.fiodor.pipdisabler", "PiP-Disabler", PluginVersion)]
    [BepInDependency("com.fontaine.fovfix", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.Shibatsu.DynamicExternalResolution", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("7Bpencil.MagicOpticMount", BepInDependency.DependencyFlags.SoftDependency)]

    public sealed class PiPDisablerPlugin : BaseUnityPlugin
    {
        public const string PluginVersion = "2.1.0";

        public static ManualLogSource LogSource;
        internal static PiPDisablerPlugin Instance;

        public static void DebugLogInfo(object data)
        {
            if (Settings.DebugLogging.Value)
            {
                LogSource.LogInfo(data);
            }
        }

        public static void DebugLogError(object data)
        {
            if (Settings.DebugLogging.Value)
            {
                LogSource.LogError(data);
            }
        }

        private void Awake()
        {
            Instance = this;
            LogSource = Logger;
            LogSource.LogInfo($"PiP-Disabler {PluginVersion} loaded.");
            Settings.Init(Config);
            UserData.MoveOldFiles();
            Patches.Patcher.Enable();
            ScopeLifecycle.Init();
            FreelookTracker.Init();
            Settings.ModEnabled.SettingChanged += OnModEnabledChanged;
            Settings.ScopeBlacklistNames.SettingChanged += OnScopeListSettingsChanged;
            Settings.ScopeWhitelistNames.SettingChanged += OnWhitelistSettingsChanged;
        }

        private void OnApplicationQuit()
        {
            PerScopeMeshSurgerySettings.FlushPendingWrite();
        }

        private void OnDestroy()
        {
            PerScopeMeshSurgerySettings.FlushPendingWrite();
            // Plugin unload or game exit — restore everything
            ScopeLifecycle.ForceExit();
            CameraSettingsManager.ForceRestore();
            LensTransparency.FullRestoreAll();
            MeshSurgeryManager.CleanupForShutdown();
            Patches.VisualRecoilCompensationPatch.Disable();
            PiPDisabler.RestoreAllCameras();

            Settings.ModEnabled.SettingChanged -= OnModEnabledChanged;
            Settings.ScopeBlacklistNames.SettingChanged -= OnScopeListSettingsChanged;
            Settings.ScopeWhitelistNames.SettingChanged -= OnWhitelistSettingsChanged;
        }

        private static void OnModEnabledChanged(object sender, EventArgs e)
        {
            if (!Settings.ModEnabled.Value)
            {
                ScopeLifecycle.ForceExit();
                CameraSettingsManager.ForceRestore();
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
            OnScopeListSettingsChanged(sender, e);
        }

        private static void OnScopeListSettingsChanged(object sender, EventArgs e)
        {
            if (!Settings.ModEnabled.Value) return;
            if (ScopeLifecycle.IsScoped)
            {
                ScopeLifecycle.ForceExit();
                ScopeLifecycle.SyncState();
            }
        }


        internal static void Notify(string message)
        {
            try
            {
                EFT.Communications.NotificationManager.DisplayMessageNotification(message);
            }
            catch (Exception ex)
            {
                DebugLogInfo($"[Notify] {message} (notification failed: {ex.Message})");
            }
        }

        private void Update()
        {
            Localization.Tick();
            if (Settings.ModToggleKey.Value != KeyCode.None && InputProxy.GetKeyDown(Settings.ModToggleKey.Value))
            {
                Settings.ModEnabled.Value = !Settings.ModEnabled.Value;
                DebugLogInfo($"[Global] Mod {(Settings.ModEnabled.Value ? "ENABLED" : "DISABLED")}");
            }
            if (!Settings.ModEnabled.Value) return;

            if (Settings.RecutHoleKey.Value != KeyCode.None && InputProxy.GetKeyDown(Settings.RecutHoleKey.Value))
                ScopeLifecycle.RecutHoleNow();
            ScopeLifecycle.TickPrecut();

            if (Settings.ScopeWhitelistToggleEntryKey.Value != KeyCode.None && InputProxy.GetKeyDown(Settings.ScopeWhitelistToggleEntryKey.Value))
            {
                ScopeLifecycle.ToggleActiveScopeWhitelistEntry();
            }

            if (Settings.ScopeBlacklistToggleEntryKey.Value != KeyCode.None && InputProxy.GetKeyDown(Settings.ScopeBlacklistToggleEntryKey.Value))
            {
                ScopeLifecycle.ToggleActiveScopeBlacklistEntry();
            }

            if (Settings.SaveCustomMeshSurgerySettingsKey.Value != KeyCode.None && InputProxy.GetKeyDown(Settings.SaveCustomMeshSurgerySettingsKey.Value))
            {
                string scopeKey = ScopeLifecycle.GetActiveScopeWhitelistKey();
                if (string.IsNullOrWhiteSpace(scopeKey))
                {
                    DebugLogInfo("[CustomMeshSettings] Save ignored: no active scope key");
                    Notify(Localization.T("key.notaiming", "PiP-Disabler: press this while aiming a scope"));
                }
                else
                {
                    bool saved = PerScopeMeshSurgerySettings.SaveCustomSettingsForScope(scopeKey);
                    DebugLogInfo(saved
                        ? $"[CustomMeshSettings] Saved custom settings for scope key '{scopeKey}'"
                        : "[CustomMeshSettings] Save failed");
                    Notify(saved ? Localization.T("key.saved", "PiP-Disabler: saved settings for '{0}' — applied", scopeKey)
                                 : Localization.T("key.savefail", "PiP-Disabler: save failed"));
                    if (saved) ScopeLifecycle.ReapplyCurrentScope("per-scope settings saved");
                }
            }

            if (Settings.DeleteCustomMeshSurgerySettingsKey.Value != KeyCode.None && InputProxy.GetKeyDown(Settings.DeleteCustomMeshSurgerySettingsKey.Value))
            {
                string scopeKey = ScopeLifecycle.GetActiveScopeWhitelistKey();
                if (string.IsNullOrWhiteSpace(scopeKey))
                {
                    DebugLogInfo("[CustomMeshSettings] Delete ignored: no active scope key");
                    Notify(Localization.T("key.notaiming", "PiP-Disabler: press this while aiming a scope"));
                }
                else
                {
                    bool removed = PerScopeMeshSurgerySettings.DeleteCustomSettingsForScope(scopeKey);
                    DebugLogInfo(removed
                        ? $"[CustomMeshSettings] Deleted custom settings for scope key '{scopeKey}'"
                        : $"[CustomMeshSettings] No custom settings existed for scope key '{scopeKey}'");
                    Notify(removed ? Localization.T("key.removed", "PiP-Disabler: removed settings for '{0}' — back to defaults", scopeKey)
                                   : Localization.T("key.none", "PiP-Disabler: '{0}' has no saved settings", scopeKey));
                    if (removed) ScopeLifecycle.ReapplyCurrentScope("per-scope settings deleted");
                }
            }

            SettingsApplyGate.Tick();
            ScopeLifecycle.TickPendingSettingWork();
            PerScopeMeshSurgerySettings.TickPendingWrite();

            if (!ScopeLifecycle.ShouldRunUpdateLoop())
                return;

            PiPDisabler.TickBaseOpticCamera();
            ScopeLifecycle.CheckAndUpdate("Update");
            ScopeLifecycle.Tick();
            Patches.MagicOpticMountCompat.Tick();
        }
    }
}
