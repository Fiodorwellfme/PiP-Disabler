using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace PiPDisabler
{
    /// <summary>
    /// Heavy work caused by a settings change (re-cutting the scope body, re-entering the scope,
    /// writing the settings file) waits until the F12 Configuration Manager window is closed, so
    /// dragging a slider does it once instead of dozens of times. Without a detectable window it
    /// waits until the value has stopped changing for a moment.
    /// </summary>
    internal static class SettingsApplyGate
    {
        private const string ConfigManagerGuid = "com.bepis.bepinex.configurationmanager";
        private const float SettleDelay = 1.0f;

        private static bool _searched;
        private static object _manager;
        private static PropertyInfo _displayingWindow;
        private static float _lastChangeAt = -100f;

        public static void NoteChange() => _lastChangeAt = Time.realtimeSinceStartup;

        private static bool _wasOpen;
        private static float _nextKoreanApplyAt = 10f;

        /// <summary>
        /// A translation plugin can overwrite our Korean names/categories after startup (a user's
        /// F12 showed machine-translated names for older keys and ours only for new ones). Put ours
        /// back 10 s after start and every time the window closes, so the next opening shows them.
        /// </summary>
        public static void Tick()
        {
            bool open = IsSettingsWindowOpen;
            if ((_wasOpen && !open) || (_nextKoreanApplyAt > 0f && Time.realtimeSinceStartup >= _nextKoreanApplyAt))
            {
                _nextKoreanApplyAt = -1f;
                Localization.Apply(Settings.ConfigEntries);
            }
            _wasOpen = open;
        }

        /// <summary>True while the F12 settings window is open.</summary>
        public static bool IsSettingsWindowOpen
        {
            get
            {
                if (!_searched)
                {
                    _searched = true;
                    try
                    {
                        if (Chainloader.PluginInfos.TryGetValue(ConfigManagerGuid, out var info) && info.Instance != null)
                        {
                            _manager = info.Instance;
                            _displayingWindow = _manager.GetType().GetProperty("DisplayingWindow",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        }
                    }
                    catch { _displayingWindow = null; }
                }

                if (_displayingWindow == null) return false;
                try { return (bool)_displayingWindow.GetValue(_manager, null); }
                catch { return false; }
            }
        }

        /// <summary>Pending setting work may run now.</summary>
        public static bool CanApply
        {
            get
            {
                if (IsSettingsWindowOpen) return false;
                // Window found: apply right after it closes. Not found: wait for the value to settle.
                return _displayingWindow != null || Time.realtimeSinceStartup - _lastChangeAt >= SettleDelay;
            }
        }
    }
}
