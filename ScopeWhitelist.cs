using System;
using System.Collections.Generic;
using EFT.CameraControl;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    internal static class ScopeWhitelist
    {
        private static readonly HashSet<string> _entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            ReloadFromConfig();
        }

        public static void ReloadFromConfig()
        {
            _entries.Clear();

            var raw = ScopeHousingMeshSurgeryPlugin.ScopeWhitelistCsv != null
                ? ScopeHousingMeshSurgeryPlugin.ScopeWhitelistCsv.Value
                : string.Empty;

            if (string.IsNullOrWhiteSpace(raw)) return;

            var parts = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                var name = Normalize(parts[i]);
                if (!string.IsNullOrEmpty(name))
                    _entries.Add(name);
            }
        }

        public static bool IsAllowedForOptic(OpticSight os, out string scopeName)
        {
            scopeName = GetScopeNameUnderModScope(os);
            if (string.IsNullOrEmpty(scopeName))
                return false;

            return _entries.Contains(scopeName);
        }

        public static void ToggleCurrentScopeEntry()
        {
            var os = ScopeLifecycle.ActiveOptic;
            if (os == null)
            {
                ScopeHousingMeshSurgeryPlugin.LogWarn("[ScopeWhitelist] Toggle ignored: no active scoped optic.");
                return;
            }

            string scopeName = GetScopeNameUnderModScope(os);
            if (string.IsNullOrEmpty(scopeName))
            {
                ScopeHousingMeshSurgeryPlugin.LogWarn("[ScopeWhitelist] Toggle ignored: could not resolve scope_* under mod_scope.");
                return;
            }

            if (_entries.Contains(scopeName))
            {
                _entries.Remove(scopeName);
                SaveToConfig();
                ScopeHousingMeshSurgeryPlugin.LogInfo($"[ScopeWhitelist] Removed '{scopeName}' from whitelist.");
            }
            else
            {
                _entries.Add(scopeName);
                SaveToConfig();
                ScopeHousingMeshSurgeryPlugin.LogInfo($"[ScopeWhitelist] Added '{scopeName}' to whitelist.");
            }
        }

        private static void SaveToConfig()
        {
            if (ScopeHousingMeshSurgeryPlugin.ScopeWhitelistCsv == null) return;

            var arr = new string[_entries.Count];
            _entries.CopyTo(arr);
            Array.Sort(arr, StringComparer.OrdinalIgnoreCase);
            ScopeHousingMeshSurgeryPlugin.ScopeWhitelistCsv.Value = string.Join(",", arr);
        }

        private static string GetScopeNameUnderModScope(OpticSight os)
        {
            if (os == null) return null;

            var scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
            if (scopeRoot == null) return null;

            string rootName = Normalize(scopeRoot.name);
            if (rootName.StartsWith("scope_", StringComparison.OrdinalIgnoreCase) && HasModScopeAncestor(scopeRoot))
                return rootName;

            for (var t = os.transform; t != null; t = t.parent)
            {
                var n = Normalize(t.name);
                if (n.StartsWith("scope_", StringComparison.OrdinalIgnoreCase) && HasModScopeAncestor(t))
                    return n;
            }

            return rootName.StartsWith("scope_", StringComparison.OrdinalIgnoreCase) ? rootName : null;
        }

        private static bool HasModScopeAncestor(Transform t)
        {
            for (var p = t != null ? t.parent : null; p != null; p = p.parent)
            {
                var n = p.name;
                if (!string.IsNullOrEmpty(n) && n.IndexOf("mod_scope", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            return name.Replace("(Clone)", string.Empty).Trim();
        }
    }
}
