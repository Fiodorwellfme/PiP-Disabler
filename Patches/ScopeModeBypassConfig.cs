using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace PiPDisabler
{
    [Serializable]
    internal sealed class ScopeModeBypassConfigFile
    {
        // Comma-separated template IDs (e.g. "5c0a2cec0db834001b7ce47d,5b3b713c5acfc4330140bd8d")
        public string TemplateIds = string.Empty;
        // Semicolon-separated template names (e.g. "Elcan Spectre DR;PSO-1")
        public string TemplateNames = string.Empty;
    }

    /// <summary>
    /// Loads scope-mode bypass configuration from scope_mode_bypass_ids.json
    /// (next to the plugin DLL). Reloads automatically when the file changes
    /// (checked at most every <see cref="CheckIntervalSeconds"/> seconds).
    ///
    /// A scope whose template ID or name appears here will have PiP kept
    /// disabled when the player switches to the backup/iron branch of that
    /// optic (SelectedScopeIndex==1, SelectedScopeMode==0).
    /// </summary>
    internal static class ScopeModeBypassConfig
    {
        private static string FilePath =>
            Path.Combine(PiPDisablerPlugin.GetPluginRootDirectory(), "scope_mode_bypass_ids.json");

        private static ScopeModeBypassConfigFile _file = new ScopeModeBypassConfigFile();
        private static DateTime _lastFileWriteTime = DateTime.MinValue;
        private static float _nextCheckTime = -1f;
        private const float CheckIntervalSeconds = 5f;

        private static readonly HashSet<string> _ids =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _names =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns true when the given template ID or name appears in the
        /// scope_mode_bypass_ids.json config.
        /// </summary>
        public static bool IsTemplateMatched(string templateId, string templateName)
        {
            MaybeReload();

            if (!string.IsNullOrWhiteSpace(templateId) && _ids.Contains(templateId))
                return true;

            if (!string.IsNullOrWhiteSpace(templateName) && _names.Contains(templateName))
                return true;

            return false;
        }

        private static void MaybeReload()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextCheckTime) return;
            _nextCheckTime = now + CheckIntervalSeconds;

            try
            {
                if (!File.Exists(FilePath)) return;

                DateTime lastWrite = File.GetLastWriteTime(FilePath);
                if (lastWrite <= _lastFileWriteTime) return;

                _lastFileWriteTime = lastWrite;

                string json = File.ReadAllText(FilePath);
                var parsed = JsonConvert.DeserializeObject<ScopeModeBypassConfigFile>(json);
                if (parsed == null) return;

                _file = parsed;
                RebuildSets();

                PiPDisablerPlugin.LogInfo(
                    $"[ScopeModeBypassConfig] Reloaded: {_ids.Count} ID(s), {_names.Count} name(s)");
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogWarn($"[ScopeModeBypassConfig] Load failed: {ex.Message}");
            }
        }

        private static void RebuildSets()
        {
            _ids.Clear();
            _names.Clear();

            if (!string.IsNullOrWhiteSpace(_file.TemplateIds))
            {
                foreach (var token in _file.TemplateIds.Split(
                    new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string t = token.Trim();
                    if (!string.IsNullOrEmpty(t)) _ids.Add(t);
                }
            }

            if (!string.IsNullOrWhiteSpace(_file.TemplateNames))
            {
                foreach (var token in _file.TemplateNames.Split(
                    new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string t = token.Trim();
                    if (!string.IsNullOrEmpty(t)) _names.Add(t);
                }
            }
        }
    }
}
