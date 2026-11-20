using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PiPDisabler
{
    /// <summary>
    /// Translations for the F12 (Configuration Manager) panel and the on-screen messages.
    /// English is built in (the cfg names/descriptions and the English text in the code).
    /// Other languages are plain JSON files in BepInEx/plugins/PiP-Disabler/locales/&lt;code&gt;.json,
    /// named after the game's language code (en, ru, kr, ch, ge, fr, ...), so players can add one.
    /// Only the displayed text changes (ConfigurationManagerAttributes.DispName/Category/Description);
    /// the cfg section/key names stay English so existing user settings are kept.
    ///
    /// File format:
    /// {
    ///   "settings": { "Section|Key": { "category": "...", "name": "...", "description": "..." } },
    ///   "messages": { "message.key": "text with {0} placeholders" }
    /// }
    /// Missing entries fall back to English.
    /// </summary>
    internal static class Localization
    {
        internal const string GameOption = "Game";
        internal const string EnglishOption = "English";

        private struct Text
        {
            public string Category, Name, Desc;
        }

        private static string _loadedCode;
        private static Dictionary<string, Text> _settings;
        private static Dictionary<string, string> _messages;

        // Advanced (hidden) in the original, shown because they are the ones people tune.
        private static readonly HashSet<string> Visible = new HashSet<string>
        {
            K("Per scope settings", "DeleteCustomMeshSurgerySettingsKey"),
            K("Per scope settings", "Zoom Multiplier"),
            K("Per scope settings", "Weapon Scale Min Magnification"),
            K("Per scope settings", "Weapon Scale Max Magnification"),
            K("Per scope settings", "Weapon Scale Multiplier"),
            K("Per scope settings", "CutLength"),
            K("Per scope settings", "Cut Width Multiplier"),
            K("Per scope settings", "Use Manual Cut Shape"),
            K("General", "Auto Disable For NV/Thermals"),
            K("General", "Baseline FOV"),
        };

        // Hidden so per-scope values have one place to edit: the global cut/scale fallbacks
        // (still used by scopes without saved settings) and the save key (saving is automatic).
        private static readonly HashSet<string> Hidden = new HashSet<string>
        {
            K("Per scope settings", "Save custom settings key"),
            K("Hacks", "Manual Weapon Scale"),
            K("Hacks", "Visual Recoil Compensation"),
            K("Debug", "Draw reticle after everything"),
        };

        private static string K(string section, string key) => section + "|" + key;

        internal static string LocalesDir
        {
            get
            {
                string dll = PiPDisablerPlugin.Instance?.Info?.Location;
                string dir = string.IsNullOrEmpty(dll) ? Path.Combine(BepInEx.Paths.PluginPath, "PiP-Disabler") : Path.GetDirectoryName(dll);
                return Path.Combine(dir, "locales");
            }
        }

        /// <summary>Language files found next to the DLL (file names without .json).</summary>
        internal static string[] AvailableCodes()
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(LocalesDir))
                    foreach (var f in Directory.GetFiles(LocalesDir, "*.json"))
                        list.Add(Path.GetFileNameWithoutExtension(f));
            }
            catch { }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list.ToArray();
        }

        /// <summary>The language code the "Settings Language" option resolves to; null = English.</summary>
        internal static string ResolveCode()
        {
            string v = Settings.Language?.Value;
            if (string.IsNullOrEmpty(v) || v.Equals(EnglishOption, StringComparison.OrdinalIgnoreCase)) return null;
            if (v.Equals(GameOption, StringComparison.OrdinalIgnoreCase)) v = GameLanguageCode();
            if (string.IsNullOrEmpty(v) || v.Equals("en", StringComparison.OrdinalIgnoreCase)) return null;
            return v;
        }

        private static string GameLanguageCode()
        {
            try
            {
                if (Comfort.Common.Singleton<EFT.Settings.SettingsManager>.Instantiated)
                    return Comfort.Common.Singleton<EFT.Settings.SettingsManager>.Instance.Game.Settings.Language.Value;
            }
            catch { }
            return null;
        }

        private static void EnsureLoaded(string code)
        {
            if (code == _loadedCode && (code == null || _settings != null)) return;
            _loadedCode = code;
            _settings = null;
            _messages = null;
            if (code == null) return;

            string path = Path.Combine(LocalesDir, code + ".json");
            if (!File.Exists(path)) return;
            try
            {
                var root = JObject.Parse(File.ReadAllText(path));
                _settings = new Dictionary<string, Text>();
                _messages = new Dictionary<string, string>();
                if (root["settings"] is JObject s)
                    foreach (var p in s.Properties())
                        if (p.Value is JObject o)
                            _settings[p.Name] = new Text
                            {
                                Category = (string)o["category"],
                                Name = (string)o["name"],
                                Desc = (string)o["description"],
                            };
                if (root["messages"] is JObject m)
                    foreach (var p in m.Properties())
                        _messages[p.Name] = (string)p.Value;
                PiPDisablerPlugin.LogSource?.LogInfo($"Language file loaded: {path} ({_settings.Count} settings, {_messages.Count} messages)");
            }
            catch (Exception e)
            {
                _settings = null;
                _messages = null;
                PiPDisablerPlugin.LogSource?.LogWarning($"Language file {path} could not be read, using English: {e.Message}");
            }
        }

        /// <summary>Message text: the translation for <paramref name="key"/>, or the English text. {0}.. are filled with args.</summary>
        internal static string T(string key, string english, params object[] args)
        {
            EnsureLoaded(ResolveCode());
            string text = english;
            if (_messages != null && _messages.TryGetValue(key, out var tr) && !string.IsNullOrEmpty(tr))
                text = tr;
            if (args == null || args.Length == 0) return text;
            try { return string.Format(text, args); }
            catch (FormatException)
            {
                try { return string.Format(english, args); } catch (FormatException) { return english; }
            }
        }

        public static void Apply(IEnumerable<ConfigEntryBase> entries)
        {
            EnsureLoaded(ResolveCode());
            foreach (var entry in entries)
            {
                if (entry?.Definition == null || entry.Description == null) continue;
                string key = K(entry.Definition.Section, entry.Definition.Key);

                var tags = entry.Description.Tags;
                ConfigurationManagerAttributes attributes = null;
                if (tags != null)
                {
                    foreach (var tag in tags)
                    {
                        attributes = tag as ConfigurationManagerAttributes;
                        if (attributes != null) break;
                    }
                }
                if (attributes == null) continue;

                if (Visible.Contains(key))
                    attributes.IsAdvanced = false;
                if (Hidden.Contains(key) || entry.Definition.Section == "Global Mesh Surgery settings")
                    attributes.Browsable = false;

                Text text = default;
                if (_settings == null || !_settings.TryGetValue(key, out text))
                {
                    // English: the original names, sections and descriptions.
                    attributes.DispName = null;
                    attributes.Category = null;
                    attributes.Description = null;
                    continue;
                }
                attributes.DispName = string.IsNullOrEmpty(text.Name) ? null : text.Name;
                attributes.Category = string.IsNullOrEmpty(text.Category) ? null : text.Category;
                attributes.Description = string.IsNullOrEmpty(text.Desc) ? null : text.Desc;
            }
        }

        // The game's settings are not loaded yet when the plugin starts, and the player can change the
        // language in game: re-apply the F12 texts when the language the "Game" option resolves to changes.
        private static string _appliedCode = "\u0000";
        private static float _nextCheck;
        internal static void Tick()
        {
            if (Settings.Language == null || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 2f;
            string code = ResolveCode();
            if (code == _appliedCode) return;
            _appliedCode = code;
            Apply(Settings.ConfigEntries);
        }
    }
}
